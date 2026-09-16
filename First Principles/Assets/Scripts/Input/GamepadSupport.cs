#if ENABLE_INPUT_SYSTEM
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Full controller support driver. Keeps a uGUI selection alive so the gamepad
/// (d-pad / left stick via <see cref="UnityEngine.InputSystem.UI.InputSystemUIInputModule"/>)
/// can navigate menus, maps the cancel button (B / circle, plus Esc on keyboard) to a
/// per-scene "back" action, hides the OS cursor while a controller is the active device,
/// and pauses a running match when the controller unplugs or the app loses focus.
/// </summary>
/// <remarks>
/// The Game scene never auto-selects UI during a platformer match: the south button is the
/// jump input there, and a focused button would be clicked by the same press. Backing out
/// of a match is driven directly by the cancel button clicking the scene's BackButton.
/// </remarks>
public class GamepadSupport : MonoBehaviour
{
    private const string MenuSceneName = "Menu";
    private const string LevelSelectSceneName = "LevelSelect";
    private const string GameSceneName = "Game";

    private static GamepadSupport instance;

    /// <summary>True while the most recent input came from a gamepad (drives cursor hiding and auto-focus).</summary>
    public static bool ControllerActive { get; private set; }

    private static bool matchPaused;
    private static GameObject pauseOverlayRoot;

    private GameObject lastSelected;
    private bool cursorHidden;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;
        var go = new GameObject("GamepadSupport");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GamepadSupport>();
    }

    private void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
        Application.focusChanged += OnFocusChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        Application.focusChanged -= OnFocusChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        var gp = Gamepad.current;
        UpdateActiveDevice(gp);

        if (gp != null && ControllerActive && NavigationPressed(gp))
            EnsureSelection();

        bool cancelPressed =
            (gp != null && gp.buttonEast.wasPressedThisFrame) ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
        if (cancelPressed && !TypingInInputField())
            HandleCancel();

        if (ControllerActive)
            ScrollSelectionIntoView();
    }

    #region Device tracking / cursor

    private void UpdateActiveDevice(Gamepad gp)
    {
        if (gp != null && GamepadUsedThisFrame(gp))
            ControllerActive = true;

        var mouse = Mouse.current;
        if (mouse != null &&
            (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame))
            ControllerActive = false;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            ControllerActive = false;

        if (ControllerActive != cursorHidden)
        {
            cursorHidden = ControllerActive;
            Cursor.visible = !cursorHidden;
        }
    }

    private static bool GamepadUsedThisFrame(Gamepad gp)
    {
        return gp.buttonSouth.wasPressedThisFrame || gp.buttonEast.wasPressedThisFrame ||
               gp.buttonWest.wasPressedThisFrame || gp.buttonNorth.wasPressedThisFrame ||
               gp.startButton.wasPressedThisFrame || gp.selectButton.wasPressedThisFrame ||
               gp.dpad.ReadValue() != Vector2.zero ||
               gp.leftStick.ReadValue().sqrMagnitude > 0.25f;
    }

    private static bool NavigationPressed(Gamepad gp)
    {
        return gp.dpad.ReadValue() != Vector2.zero ||
               gp.leftStick.ReadValue().sqrMagnitude > 0.25f ||
               gp.buttonSouth.wasPressedThisFrame;
    }

    private static bool TypingInInputField()
    {
        var es = EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null)
            return false;
        var field = es.currentSelectedGameObject.GetComponent<TMP_InputField>();
        return field != null && field.isFocused;
    }

    #endregion

    #region Focus management

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (matchPaused)
            ResumeMatch();
        if (ControllerActive)
            StartCoroutine(SelectDefaultAfterUiSpawn());
    }

    private System.Collections.IEnumerator SelectDefaultAfterUiSpawn()
    {
        // Menu spawns its extra buttons one frame after OnEnable; LevelSelect finishes its
        // scroll layout at end of frame. Two frames covers both.
        yield return null;
        yield return null;
        EnsureSelection();
    }

    private void EnsureSelection()
    {
        var es = EventSystem.current;
        if (es == null)
            return;
        var current = es.currentSelectedGameObject;
        if (current != null && current.activeInHierarchy)
            return;

        GameObject target = null;
        var overlay = OverlayCancelHandler.Topmost;
        if (overlay != null)
        {
            target = overlay.InitialFocusObject;
        }
        else
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == MenuSceneName)
                target = GameObject.FindGameObjectWithTag("PlayButton");
            else if (scene == LevelSelectSceneName)
                target = FindFirstLevelListButton();
            // Game scene: no auto-focus — south button doubles as jump there.
        }

        if (target != null && target.activeInHierarchy)
            es.SetSelectedGameObject(target);
    }

    private static GameObject FindFirstLevelListButton()
    {
        var scroll = FindAnyObjectByType<ScrollRect>();
        if (scroll != null && scroll.content != null)
        {
            var btn = scroll.content.GetComponentInChildren<Button>(false);
            if (btn != null && btn.interactable)
                return btn.gameObject;
        }
        var fallback = GameObject.Find("BackButton");
        return fallback != null ? fallback : null;
    }

    private void ScrollSelectionIntoView()
    {
        var es = EventSystem.current;
        var selected = es != null ? es.currentSelectedGameObject : null;
        if (selected == null || selected == lastSelected)
        {
            lastSelected = selected;
            return;
        }
        lastSelected = selected;

        var scroll = selected.GetComponentInParent<ScrollRect>();
        if (scroll == null || !scroll.vertical || scroll.content == null || scroll.viewport == null)
            return;
        var itemRt = selected.transform as RectTransform;
        if (itemRt == null || !itemRt.IsChildOf(scroll.content))
            return;

        Canvas.ForceUpdateCanvases();
        float viewportH = scroll.viewport.rect.height;
        float contentH = scroll.content.rect.height;
        if (contentH <= viewportH)
            return;

        // Item center measured downward from the top edge of the content.
        Vector2 local = (Vector2)scroll.content.InverseTransformPoint(itemRt.TransformPoint(itemRt.rect.center));
        float itemFromTop = -(local.y - scroll.content.rect.yMax);
        float scrollable = contentH - viewportH;
        float viewTop = (1f - scroll.verticalNormalizedPosition) * scrollable;
        float viewBottom = viewTop + viewportH;

        float itemH = itemRt.rect.height;
        float pad = itemH * 0.5f + 12f;
        float newTop = viewTop;
        if (itemFromTop - pad < viewTop)
            newTop = itemFromTop - pad;
        else if (itemFromTop + pad > viewBottom)
            newTop = itemFromTop + pad - viewportH;
        else
            return;

        scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - newTop / scrollable);
    }

    #endregion

    #region Cancel / back

    private void HandleCancel()
    {
        if (matchPaused)
        {
            ResumeMatch();
            return;
        }

        var overlay = OverlayCancelHandler.Topmost;
        if (overlay != null)
        {
            overlay.Cancel();
            return;
        }

        string scene = SceneManager.GetActiveScene().name;
        if (scene == GameSceneName || scene == LevelSelectSceneName)
        {
            var backGo = GameObject.Find("BackButton");
            var backBtn = backGo != null ? backGo.GetComponent<Button>() : null;
            if (backBtn != null && backBtn.isActiveAndEnabled)
                backBtn.onClick.Invoke();
        }
        else if (scene == MenuSceneName)
        {
            ShowQuitConfirmOverlay();
        }
    }

    private static void ShowQuitConfirmOverlay()
    {
        var canvas = FindActiveSceneCanvas();
        if (canvas == null)
            return;
        if (canvas.transform.Find("QuitConfirmOverlay") != null)
            return;

        var root = BuildOverlayRoot(canvas.transform, "QuitConfirmOverlay",
            LocalizationManager.Get("ui.quit_confirm", "Quit the game?"),
            out RectTransform panelRt);

        var noBtn = BuildOverlayButton(panelRt, "CancelButton",
            LocalizationManager.Get("ui.quit_no", "Cancel"),
            new Vector2(0.08f, 0.10f), new Vector2(0.48f, 0.38f),
            () => Destroy(root));
        BuildOverlayButton(panelRt, "QuitButton",
            LocalizationManager.Get("ui.quit_yes", "Quit"),
            new Vector2(0.52f, 0.10f), new Vector2(0.92f, 0.38f),
            () =>
            {
                var fader = FindAnyObjectByType<SceneFader>();
                if (fader != null)
                    fader.QuitGame();
                else
                    Application.Quit();
            });

        OverlayCancelHandler.Attach(root, noBtn, noBtn);
    }

    #endregion

    #region Match pause

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Gamepad &&
            (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected))
            PauseMatch(LocalizationManager.Get("ui.controller_disconnected", "Controller disconnected"));
    }

    private void OnFocusChanged(bool focused)
    {
        if (!focused)
            PauseMatch(LocalizationManager.Get("ui.paused", "Paused"));
    }

    private static void PauseMatch(string title)
    {
        if (matchPaused)
            return;
        if (SceneManager.GetActiveScene().name != GameSceneName)
            return;
        // Only a platformer match pauses; the graphing calculator has no time-critical state.
        if (FindAnyObjectByType<PlayerControllerUI2D>() == null)
            return;
        var canvas = FindActiveSceneCanvas();
        if (canvas == null)
            return;

        matchPaused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        pauseOverlayRoot = BuildOverlayRoot(canvas.transform, "MatchPauseOverlay", title, out RectTransform panelRt);
        var resumeBtn = BuildOverlayButton(panelRt, "ResumeButton",
            LocalizationManager.Get("ui.resume", "Resume"),
            new Vector2(0.25f, 0.10f), new Vector2(0.75f, 0.38f),
            ResumeMatch);
        OverlayCancelHandler.Attach(pauseOverlayRoot, resumeBtn, resumeBtn);
    }

    private static void ResumeMatch()
    {
        if (!matchPaused)
            return;
        matchPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (pauseOverlayRoot != null)
        {
            Destroy(pauseOverlayRoot);
            pauseOverlayRoot = null;
        }
    }

    #endregion

    #region Runtime overlay building

    private static Canvas FindActiveSceneCanvas()
    {
        var active = SceneManager.GetActiveScene();
        Canvas best = null;
        foreach (var canvas in FindObjectsByType<Canvas>())
        {
            if (!canvas.isRootCanvas || canvas.gameObject.scene != active)
                continue;
            if (best == null || canvas.sortingOrder > best.sortingOrder)
                best = canvas;
        }
        return best;
    }

    private static GameObject BuildOverlayRoot(Transform canvasTransform, string name, string title, out RectTransform panelRt)
    {
        var root = new GameObject(name);
        var rootRt = root.AddComponent<RectTransform>();
        rootRt.SetParent(canvasTransform, false);
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        rootRt.SetAsLastSibling();

        var dim = root.AddComponent<Image>();
        dim.color = new Color(0.04f, 0.05f, 0.11f, 0.9f);
        dim.raycastTarget = true;

        bool tablet = DeviceLayout.IsTabletLike();
        var panelGo = new GameObject("Panel");
        panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.SetParent(root.transform, false);
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(tablet ? 560f : 480f, tablet ? 240f : 210f);

        var panelImg = panelGo.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(panelImg);
        panelImg.color = RuntimeUiPolish.PanelMid;
        RuntimeUiPolish.ApplyDropShadow(panelRt, new Vector2(3f, -5f), 0.34f);

        var titleGo = new GameObject("Title");
        var titleRt = titleGo.AddComponent<RectTransform>();
        titleRt.SetParent(panelGo.transform, false);
        titleRt.anchorMin = new Vector2(0.06f, 0.48f);
        titleRt.anchorMax = new Vector2(0.94f, 0.92f);
        titleRt.offsetMin = Vector2.zero;
        titleRt.offsetMax = Vector2.zero;

        var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        titleTmp.text = title;
        titleTmp.fontSize = UiTypography.Scale(tablet ? 36 : 30);
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = RuntimeUiPolish.TitleIvory;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            titleTmp.font = TMP_Settings.defaultFontAsset;
        LocalizationManager.ApplyTextDirection(titleTmp);

        return root;
    }

    private static Button BuildOverlayButton(RectTransform panelRt, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(panelRt, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = RuntimeUiPolish.ButtonNeutral;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        RuntimeUiPolish.ApplyButtonTransitions(btn, RuntimeUiPolish.ButtonNeutral,
            RuntimeUiPolish.ButtonNeutralHover, RuntimeUiPolish.PanelDeep);
        btn.onClick.AddListener(onClick);

        var txtGo = new GameObject("Text");
        var txtRt = txtGo.AddComponent<RectTransform>();
        txtRt.SetParent(go.transform, false);
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = new Vector2(8f, 6f);
        txtRt.offsetMax = new Vector2(-8f, -6f);

        var tmp = txtGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = UiTypography.Scale(DeviceLayout.IsTabletLike() ? 28 : 24);
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = RuntimeUiPolish.TitleIvory;
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
        LocalizationManager.ApplyTextDirection(tmp);

        return btn;
    }

    #endregion
}
#endif
