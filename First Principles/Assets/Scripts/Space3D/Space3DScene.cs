using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// Space3DScene: root of the "Space3D" scene (1.5): the 3D graphing calculator and the 3D stages.
// -----------------------------------------------------------------------------
// The scene file only holds this component; camera, canvas, event system and both views are built
// here so the scene stays mergeable. Top bar: Back (to Menu, or to stage select from a stage),
// title, and two tabs: Graph | Stages. The left panel of each view shrinks the 3D viewport through
// an off-centre projection (see LateUpdate), so the plot is centred in the visible area.
// -----------------------------------------------------------------------------

public static class Space3DSession
{
    public const string SceneName = "Space3D";

    /// <summary>0 = graphing calculator, 1 = stages. Set by the Menu before loading the scene.</summary>
    public static int RequestedTab;

    /// <summary>QA / deep link: open this stage directly (-1 = none).</summary>
    public static int RequestedStage = -1;
}

public class Space3DScene : MonoBehaviour
{
    public Camera Cam { get; private set; }
    public Space3DCameraRig Rig { get; private set; }
    public Canvas Canvas { get; private set; }
    public RectTransform Safe { get; private set; }
    public RectTransform Body { get; private set; }  // below the top bar
    public RectTransform Labels { get; private set; }
    public Space3DAxes Axes { get; private set; }
    public float CanvasWidth { get; private set; }
    public const float TopBarHeight = 104f;

    /// <summary>Canvas units covered by the left panel (the 3D view is centred in the rest).</summary>
    public float LeftInset;

    Space3DCalculatorView calc;
    Space3DStageView stages;

    public Space3DCalculatorView Calculator => calc;
    public Space3DStageView Stages => stages;
    Button tabGraph, tabStages, backBtn;
    TextMeshProUGUI title;
    int tab = -1;

    public static bool Mobile => Application.isMobilePlatform || DeviceLayout.PreferOnScreenGameControls;

    void Awake()
    {
        Application.targetFrameRate = Mathf.Max(Application.targetFrameRate, 60);
        BuildCameraAndCanvas();
        BuildTopBar();
        var world = new GameObject("World3D").transform;
        Axes = new GameObject("Axes").AddComponent<Space3DAxes>();
        Axes.transform.SetParent(world, false);
        Axes.Init(Cam, Canvas, Labels);
        calc = new Space3DCalculatorView(this, world);
        stages = new Space3DStageView(this, world);
        int want = Mathf.Clamp(Space3DSession.RequestedTab, 0, 1);
        if (Space3DSession.RequestedStage >= 0)
            want = 1;
        ShowTab(want);
        if (Space3DSession.RequestedStage >= 0)
        {
            stages.OpenStage(Space3DSession.RequestedStage);
            Space3DSession.RequestedStage = -1;
        }
    }

    void BuildCameraAndCanvas()
    {
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        Cam = camGo.AddComponent<Camera>();
        Cam.clearFlags = CameraClearFlags.SolidColor;
        Cam.backgroundColor = Bauhaus.Paper;
        Cam.fieldOfView = 40f;
        Cam.nearClipPlane = 0.05f;
        Cam.farClipPlane = 500f;
        Cam.allowMSAA = true;
        QualitySettings.antiAliasing = Mobile ? 2 : 4;
        camGo.AddComponent<AudioListener>();
        Rig = camGo.AddComponent<Space3DCameraRig>();

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        var cgo = new GameObject("Canvas3D");
        Canvas = cgo.AddComponent<Canvas>();
        Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        // Landscape everywhere; portrait desktop windows fall back to width so panels still fit.
        scaler.matchWidthOrHeight = Screen.width >= Screen.height ? 1f : 0f;
        cgo.AddComponent<GraphicRaycaster>();
        CanvasWidth = Screen.width >= Screen.height ? 1080f * Screen.width / Mathf.Max(1, Screen.height) : 1920f;

        Labels = UiKit.Rect("AxisLabels", Canvas.transform, Vector2.zero, Vector2.one);
        Safe = UiKit.Rect("Safe", Canvas.transform, Vector2.zero, Vector2.one);
        DeviceLayout.ApplySafeAreaAnchors(Safe);
        Body = Space3DUi.Rect("Body", Safe, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, -TopBarHeight));
    }

    void BuildTopBar()
    {
        var bar = Space3DUi.Rect("TopBar", Safe, new Vector2(0f, 1f), new Vector2(1f, 1f));
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(0f, TopBarHeight);
        var barImg = bar.gameObject.AddComponent<Image>();
        barImg.color = Bauhaus.Paper;
        var rule = Space3DUi.Rect("Rule", bar, new Vector2(0f, 0f), new Vector2(1f, 0f));
        rule.sizeDelta = new Vector2(0f, 6f);
        var ruleImg = rule.gameObject.AddComponent<Image>();
        ruleImg.color = Bauhaus.Ink;
        ruleImg.raycastTarget = false;

        var back = Space3DUi.Pinned("BackButton", bar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 3f), new Vector2(190f, 72f));
        backBtn = Space3DUi.Button(back, LocalizationManager.Get("ui.back", "Back"), Bauhaus.Tone.Paper, StarGlyph.ArrowLeft, OnBack, 24f, 30f);

        var titleRt = Space3DUi.Rect("Title", bar, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(232f, 8f), new Vector2(-560f, -4f));
        title = Space3DUi.Label(titleRt, "Text", "", 40f, Bauhaus.Ink, FontStyles.Bold);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;

        var g = Space3DUi.Pinned("TabGraph", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-276f, 3f), new Vector2(250f, 72f));
        tabGraph = Space3DUi.Button(g, LocalizationManager.Get("space3d.tab_graph", "3D graph"), Bauhaus.Tone.Paper, StarGlyph.Calculator, () => ShowTab(0), 24f, 30f);
        var s = Space3DUi.Pinned("TabStages", bar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 3f), new Vector2(250f, 72f));
        tabStages = Space3DUi.Button(s, LocalizationManager.Get("space3d.tab_stages", "3D stages"), Bauhaus.Tone.Paper, StarGlyph.Flag, () => ShowTab(1), 24f, 30f);
    }

    public void SetTitle(string t)
    {
        if (title != null)
            title.text = t;
    }

    public void ShowTab(int t)
    {
        if (t == tab && t == 1)
        {
            stages.ShowSelect();
            return;
        }

        tab = t;
        Space3DSession.RequestedTab = t;
        Space3DUi.SetTone(tabGraph, t == 0 ? Bauhaus.Tone.Blue : Bauhaus.Tone.Paper);
        Space3DUi.SetTone(tabStages, t == 1 ? Bauhaus.Tone.Red : Bauhaus.Tone.Paper);
        calc.SetActive(t == 0);
        stages.SetActive(t == 1);
        EventSystem.current?.SetSelectedGameObject(null);
    }

    public void OnBack()
    {
        if (tab == 1 && stages.HandleBack())
            return;
        if (tab == 1 && stages.InStage)
        {
            stages.ShowSelect();
            return;
        }

        Space3DSession.RequestedStage = -1;
        SceneTransitionHost.LoadSingleScene("Menu");
    }

    void LateUpdate()
    {
        // Centre the 3D view in the area right of the side panel (off-centre projection).
        if (Cam == null)
            return;
        Cam.ResetProjectionMatrix();
        float canvasW = Mathf.Max(1f, Canvas.GetComponent<RectTransform>().rect.width);
        float frac = Mathf.Clamp01(LeftInset / canvasW);
        if (frac > 0.001f)
        {
            var p = Cam.projectionMatrix;
            p[0, 2] = -frac;
            Cam.projectionMatrix = p;
        }
    }
}
