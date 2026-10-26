using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

// -----------------------------------------------------------------------------
// LevelSelectController — all UI for the LevelSelect scene is code-generated
// -----------------------------------------------------------------------------
// Parents to MobileUiRoots safe rect. The middle of the screen is the WorldMapView (themed
// regions, path, trials); around it: language chip, title, unlock chip, progress chip (top),
// Back, daily challenge card, Math tips and How to play (bottom). First visit runs the map
// coach tour (replayable from Progress & achievements > Show tutorial).
// -----------------------------------------------------------------------------

/// <summary>
/// Builds Limbo-style level list UI at runtime and loads <c>Game</c> with <see cref="LevelSelection"/>.
/// </summary>
public class LevelSelectController : MonoBehaviour
{
    [Tooltip("Full-screen panel tint (defaults match RuntimeUiPolish theme).")]
    [SerializeField] private Color backgroundColor = new Color(0.09f, 0.10f, 0.14f, 0.97f);
    [SerializeField] private Color buttonColor = new Color(0.20f, 0.23f, 0.30f, 0.95f);

    private TextMeshProUGUI titleTmp;
    private TextMeshProUGUI howToPlayTmp;
    private TextMeshProUGUI mathTipsTmp;
    private TextMeshProUGUI backTmp;
    private TextMeshProUGUI levelSelectLangTmp;
    private WorldMapView _map;
    private RectTransform _dailyRt;
    private RectTransform _progressChipRt;
    private RectTransform _panelRt;
    private Transform _canvasTransform;
    private TextMeshProUGUI _dailyTmp;

    public static LevelSelectController Instance { get; private set; }

    private TextMeshProUGUI unlockChipTmp;

    private void OnEnable()
    {
        LocalizationManager.LanguageChanged += RefreshLocalizedStrings;
        Entitlements.Changed += OnEntitlementsChanged;
    }

    private void OnDisable()
    {
        LocalizationManager.LanguageChanged -= RefreshLocalizedStrings;
        Entitlements.Changed -= OnEntitlementsChanged;
    }

    /// <summary>Ownership / price changed (purchase, restore, store answered): redraw locks and the unlock chip.</summary>
    private void OnEntitlementsChanged()
    {
        if (_map != null)
            _map.Rebuild();
        RefreshUnlockChip();
    }

    /// <summary>Text for the "Unlock all levels" chip (includes the localized store price once known).</summary>
    private static string UnlockChipLabel()
    {
        string price = Entitlements.LocalizedPrice;
        if (string.IsNullOrEmpty(price))
            return LocalizationManager.Get("paywall.buy", "Unlock all levels");
        string fmt = LocalizationManager.Get("paywall.buy_price", "Unlock all levels | {0}");
        try { return string.Format(fmt, price); }
        catch (System.FormatException) { return fmt; }
    }

    private static bool ShowUnlockChip =>
        !Entitlements.HasFullGame && Entitlements.Mode != EntitlementPlatformMode.AlwaysUnlocked;

    private void OpenPaywall()
    {
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas != null)
            PaywallOverlay.Open(canvas.transform);
    }

    private void RefreshLocalizedStrings()
    {
        if (titleTmp != null)
        {
            titleTmp.text = LocalizationManager.Get("map.title", "World map");
            LocalizationManager.ApplyTextDirection(titleTmp);
        }

        if (howToPlayTmp != null)
        {
            howToPlayTmp.text = LocalizationManager.Get("menu.tutorial_button", "How to play");
            LocalizationManager.ApplyTextDirection(howToPlayTmp);
        }

        if (mathTipsTmp != null)
        {
            mathTipsTmp.text = LocalizationManager.Get("ui.math_tips", "Math tips & snippets");
            LocalizationManager.ApplyTextDirection(mathTipsTmp);
        }
        if (backTmp != null)
        {
            backTmp.text = LocalizationManager.Get("ui.back_menu", "Back to Menu");
            LocalizationManager.ApplyTextDirection(backTmp);
        }
        if (unlockChipTmp != null)
        {
            unlockChipTmp.text = UnlockChipLabel();
            LocalizationManager.ApplyTextDirection(unlockChipTmp);
        }
        RefreshLevelSelectLanguageLabel();
    }

    private void RefreshLevelSelectLanguageLabel()
    {
        if (levelSelectLangTmp == null)
            return;
        levelSelectLangTmp.text = LocalizationManager.GetLanguageChipDisplayText();
        LocalizationManager.ApplyLanguagePickerTextDirection(levelSelectLangTmp);
    }

    private void Start()
    {
        Instance = this;
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("LevelSelectController: No Canvas in scene.");
            return;
        }
        _canvasTransform = canvas.transform;

        var safeParent = MobileUiRoots.GetSafeContentParent(canvas.transform);
        var panel = new GameObject("LevelSelectPanel");
        var prt = panel.AddComponent<RectTransform>();
        prt.SetParent(safeParent != null ? safeParent : canvas.transform, false);
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = Vector2.zero;
        _panelRt = prt;

        var pimg = panel.AddComponent<Image>();
        pimg.color = backgroundColor;
        pimg.raycastTarget = true;

        var backdropGo = new GameObject("FallingSymbolsBackdrop");
        var backdropRt = backdropGo.AddComponent<RectTransform>();
        backdropRt.SetParent(prt, false);
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        backdropGo.AddComponent<LevelSelectFallingSymbolsBackdrop>();
        backdropGo.transform.SetAsFirstSibling();

        bool tablet = DeviceLayout.IsTabletLike();
        var titleGo = new GameObject("Title");
        var titleRt = titleGo.AddComponent<RectTransform>();
        titleRt.SetParent(panel.transform, false);
        titleRt.anchorMin = new Vector2(0.5f, 1f);
        titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(tablet ? 560f : 520f, tablet ? 72f : 66f);
        titleRt.anchoredPosition = new Vector2(tablet ? -120f : -110f, tablet ? -6f : -4f);

        titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        titleTmp.text = LocalizationManager.Get("map.title", "World map");
        titleTmp.enableAutoSizing = true;
        titleTmp.fontSizeMin = UiTypography.Scale(24);
        titleTmp.fontSizeMax = UiTypography.Scale(tablet ? 44 : 40);
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = RuntimeUiPolish.TitleIvory;
        CopyFontFromAny(titleTmp);
        LocalizationManager.ApplyTextDirection(titleTmp);

        CreateLevelSelectLanguagePicker(panel.transform);
        CreateProgressChip(panel.transform, canvas.transform);
        RefreshUnlockChip();

        // World map fills the middle band.
        var mapHost = UiKit.Rect("MapHost", panel.transform, new Vector2(0f, tablet ? 0.13f : 0.14f), new Vector2(1f, tablet ? 0.9f : 0.885f));
        _map = WorldMapView.Create(mapHost, canvas.transform, StartGameAt, StartChallenge, OpenPaywall, ShowComingSoonOverlay);
        _map.Built += OnMapBuilt;
        _map.Rebuild();

        BuildDailyCard(panel.transform);
        CreateMathArticlesButton(panel.transform, canvas.transform);
        CreateHowToPlayButton(panel.transform, canvas.transform);
        CreateBackButton(panel.transform);

        // Arrived here because the player cleared the last free level (see LevelManager.AdvanceLevel).
        if (PaywallRequest.Consume())
            PaywallOverlay.Open(canvas.transform);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private bool _tourChecked;

    private void OnMapBuilt()
    {
        if (_tourChecked)
            return;
        _tourChecked = true;
        if (!ProgressStore.MapTourDone && !PaywallOverlay.IsOpen && !QaShots.Enabled)
            StartCoroutine(StartTourSoon());
    }

    private IEnumerator StartTourSoon()
    {
        yield return new WaitForSecondsRealtime(0.6f);
        StartMapTour();
    }

    /// <summary>Settings "Show tutorial": forget both tours and replay the map one now.</summary>
    public static void ReplayTutorials()
    {
        ProgressStore.MapTourDone = false;
        ProgressStore.LevelTourDone = false;
        if (Instance != null)
            Instance.StartMapTour();
    }

    public void StartMapTour()
    {
        if (_canvasTransform == null || _map == null)
            return;
        MapLevelSheet.Close();
        bool touch = DeviceLayout.PreferOnScreenGameControls;
        bool pad = GamepadSupport.ControllerActive;
        string pick = touch ? LocalizationManager.Get("tour.map.pick.touch", "Tap a tile to see its stars and play it. Swipe sideways to explore the map.")
            : pad ? LocalizationManager.Get("tour.map.pick.pad", "Move between tiles with the d-pad or left stick and press A to open one.")
            : LocalizationManager.Get("tour.map.pick.keys", "Click a tile to see its stars and play it. Scroll or drag sideways to explore the map.");
        var steps = new List<CoachStep>
        {
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.map.here.title", "You are here"),
                Body = LocalizationManager.Get("tour.map.here.body", "Your glyph marks the next level on the path. Each region of the map is one topic, from derivatives to Big O.") + " " + pick,
                OnEnter = () => _map.ScrollTo(_map.CurrentNodeRect, true),
                Target = () => _map.CurrentNodeRect,
            },
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.map.stars.title", "Stars and medals"),
                Body = string.Format(LocalizationManager.Get("tour.map.stars.body",
                    "Reach the exit for 1 star, fall at most once for 2, no falls and {0}+ points for 3. Cleared levels open a Flawless run and a Time trial that each earn a medal."),
                    ProgressRules.ThreeStarMinScore),
                OnEnter = () => _map.ScrollTo(_map.StarsExampleRect, true),
                Target = () => _map.StarsExampleRect,
            },
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.map.trial.title", "Trials and gated paths"),
                Body = string.Format(LocalizationManager.Get("tour.map.trial.body",
                    "Dashed paths lead to optional boss trials. Each one opens once you hold {0} stars per level in its region, and the Grand trial needs {1} medals. The main path never needs them."),
                    ChallengeRules.TrialStarsPerLevel, ChallengeRules.GrandTrialMedalsNeeded),
                OnEnter = () => _map.ScrollTo(_map.FirstTrialRect, true),
                Target = () => _map.FirstTrialRect,
            },
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.map.daily.title", "Daily challenge"),
                Body = LocalizationManager.Get("tour.map.daily.body", "A new goal every day. Complete it to grow your streak."),
                Target = () => _dailyRt,
            },
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.map.progress.title", "Progress and settings"),
                Body = LocalizationManager.Get("tour.map.progress.body", "Your stars, medals and achievements live here, along with sound and Show tutorial to replay this walkthrough."),
                Target = () => _progressChipRt,
            },
        };
        if (_dailyRt == null)
            steps.RemoveAt(3);
        CoachTour.Show(_canvasTransform, steps, done =>
        {
            ProgressStore.MapTourDone = true;
            if (_map != null)
                _map.ScrollTo(_map.CurrentNodeRect, false);
        });
    }

    private void StartChallenge(ChallengeSpec spec)
    {
        if (!Entitlements.IsLevelUnlocked(spec.LevelIndex))
        {
            OpenPaywall();
            return;
        }
        DailyChallengeSession.End();
        ChallengeSession.Begin(spec);
        LevelSelection.SetSelectedLevel(spec.LevelIndex);
        SceneTransitionHost.LoadSingleScene("Game");
    }

    private void RefreshUnlockChip()
    {
        var existing = _panelRt != null ? _panelRt.Find("UnlockChip") : null;
        if (!ShowUnlockChip)
        {
            if (existing != null)
                Destroy(existing.gameObject);
            unlockChipTmp = null;
            return;
        }
        if (existing != null || _panelRt == null)
        {
            if (unlockChipTmp != null)
                unlockChipTmp.text = UnlockChipLabel();
            return;
        }
        bool tablet = DeviceLayout.IsTabletLike();
        var btn = UiKit.Button(_panelRt, "UnlockChip", new Vector2(1f, 1f), new Vector2(1f, 1f), UnlockChipLabel(),
            RuntimeUiPolish.AccentCoral, OpenPaywall, tablet ? 22 : 20, Color.white);
        var rt = (RectTransform)btn.transform;
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(tablet ? 340f : 320f, tablet ? 64f : 56f);
        rt.anchoredPosition = new Vector2(-((tablet ? 14f : 10f) + (tablet ? 400f : 370f) + 12f), tablet ? -10f : -8f);
        unlockChipTmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        var lk = StarGlyph.Add(rt, "Lock", LockGlyph.Unlocked, Color.white, new Vector2(30f, 30f));
        lk.rectTransform.anchorMin = lk.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        lk.rectTransform.pivot = new Vector2(0f, 0.5f);
        lk.rectTransform.anchoredPosition = new Vector2(12f, 0f);
        unlockChipTmp.rectTransform.offsetMin = new Vector2(30f, unlockChipTmp.rectTransform.offsetMin.y);
    }

    private void CreateHowToPlayButton(Transform panelRoot, Transform canvasTransform)
    {
        if (GameObject.Find("LevelSelectHowToPlayButton") != null)
            return;

        var go = new GameObject("LevelSelectHowToPlayButton");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(panelRoot, false);
        bool tablet = DeviceLayout.IsTabletLike();
        // Bottom-right corner (Math tips sits to its left; see CreateMathArticlesButton).
        float margin = tablet ? 20f : 16f;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(tablet ? 260f : 240f, tablet ? 64f : 58f);
        rt.anchoredPosition = new Vector2(-margin, margin + 4f);

        var img = go.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = new Color(0.24f, 0.30f, 0.55f, 0.96f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        RuntimeUiPolish.ApplyButtonTransitions(btn, img.color,
            Color.Lerp(img.color, Color.white, 0.2f),
            Color.Lerp(img.color, Color.black, 0.18f));
        RuntimeUiPolish.ApplyDropShadow(rt, new Vector2(2f, -3f), 0.26f);

        var textGo = new GameObject("Text");
        var trt = textGo.AddComponent<RectTransform>();
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16f, 8f);
        trt.offsetMax = new Vector2(-16f, -8f);

        howToPlayTmp = textGo.AddComponent<TextMeshProUGUI>();
        howToPlayTmp.text = LocalizationManager.Get("menu.tutorial_button", "How to play");
        howToPlayTmp.fontSize = UiTypography.Scale(tablet ? 28 : 25);
        howToPlayTmp.fontStyle = FontStyles.Bold;
        howToPlayTmp.alignment = TextAlignmentOptions.Center;
        howToPlayTmp.color = new Color(0.92f, 0.98f, 1f, 1f);
        howToPlayTmp.textWrappingMode = TextWrappingModes.NoWrap;
        howToPlayTmp.enableAutoSizing = true;
        howToPlayTmp.fontSizeMin = UiTypography.Scale(14);
        howToPlayTmp.fontSizeMax = howToPlayTmp.fontSize;
        howToPlayTmp.overflowMode = TextOverflowModes.Overflow;
        howToPlayTmp.richText = true;
        CopyFontFromAny(howToPlayTmp);
        howToPlayTmp.fontStyle = FontStyles.Bold;
        LocalizationManager.ApplyTextDirection(howToPlayTmp);

        btn.onClick.AddListener(() =>
        {
            if (canvasTransform == null)
                return;
            MenuTutorialOverlay.Open(canvasTransform);
        });
    }

    private void CreateMathArticlesButton(Transform panelRoot, Transform canvasTransform)
    {
        var go = new GameObject("MathArticlesButton");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(panelRoot, false);
        bool tablet = DeviceLayout.IsTabletLike();
        float margin = tablet ? 20f : 16f;
        // Bottom-right, left of "How to play".
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(tablet ? 290f : 268f, tablet ? 64f : 58f);
        rt.anchoredPosition = new Vector2(-(margin + (tablet ? 260f : 240f) + 12f), margin + 4f);

        var img = go.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = RuntimeUiPolish.AccentTeal;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        RuntimeUiPolish.ApplyButtonTransitions(btn, RuntimeUiPolish.AccentTeal,
            Color.Lerp(RuntimeUiPolish.AccentTeal, Color.white, 0.2f),
            Color.Lerp(RuntimeUiPolish.AccentTeal, Color.black, 0.22f));
        RuntimeUiPolish.ApplyDropShadow(rt, new Vector2(2f, -3f), 0.28f);

        var textGo = new GameObject("Text");
        var trt = textGo.AddComponent<RectTransform>();
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16f, 8f);
        trt.offsetMax = new Vector2(-16f, -8f);

        mathTipsTmp = textGo.AddComponent<TextMeshProUGUI>();
        mathTipsTmp.text = LocalizationManager.Get("ui.math_tips", "Math tips & snippets");
        mathTipsTmp.fontSize = UiTypography.Scale(tablet ? 28 : 25);
        mathTipsTmp.fontStyle = FontStyles.Bold;
        mathTipsTmp.alignment = TextAlignmentOptions.Center;
        mathTipsTmp.color = new Color(0.9f, 0.96f, 1f, 1f);
        mathTipsTmp.textWrappingMode = TextWrappingModes.NoWrap;
        mathTipsTmp.enableAutoSizing = true;
        mathTipsTmp.fontSizeMin = UiTypography.Scale(14);
        mathTipsTmp.fontSizeMax = mathTipsTmp.fontSize;
        mathTipsTmp.overflowMode = TextOverflowModes.Overflow;
        CopyFontFromAny(mathTipsTmp);
        mathTipsTmp.fontStyle = FontStyles.Bold;
        LocalizationManager.ApplyTextDirection(mathTipsTmp);

        btn.onClick.AddListener(() => MathArticlesOverlay.Open(canvasTransform));
    }

    /// <summary>Top-right chip: total stars + achievements count; opens the achievements / settings sheet.</summary>
    private void CreateProgressChip(Transform panelRoot, Transform canvasTransform)
    {
        bool tablet = DeviceLayout.IsTabletLike();
        var rt = UiKit.Rect("ProgressChip", panelRoot, new Vector2(1f, 1f), new Vector2(1f, 1f));
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(tablet ? -14f : -10f, tablet ? -10f : -8f);
        rt.sizeDelta = new Vector2(tablet ? 400f : 370f, tablet ? 64f : 56f);
        _progressChipRt = rt;
        var img = rt.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = new Color(0.16f, 0.2f, 0.28f, 0.92f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        RuntimeUiPolish.ApplyButtonTransitions(btn, img.color, Color.Lerp(img.color, Color.white, 0.15f), Color.Lerp(img.color, Color.black, 0.2f));
        btn.onClick.AddListener(() =>
        {
            FeelDirector.Play(SfxId.Click, 0.6f);
            AchievementsOverlay.Open(canvasTransform);
        });
        int maxStars = ProgressStore.PlayableLevelCount * ProgressRules.MaxStars;
        float fs = UiTypography.Scale(tablet ? 22 : 20);
        AddChipStat(rt, "Stars", StarGlyph.Filled, UiKit.Gold, 0f, 0.42f, $"{ProgressStore.TotalStars}/{maxStars}", fs);
        AddChipStat(rt, "Medals", StarGlyph.Medal, UiKit.Gold, 0.42f, 0.66f, ProgressStore.MedalCount.ToString(), fs);
        AddChipStat(rt, "Ach", StarGlyph.Trophy, UiKit.Mint, 0.66f, 1f,
            $"{ProgressStore.Data.achievements.Count}/{AchievementCatalog.All.Length}", fs);
    }

    private static void AddChipStat(RectTransform chip, string name, Sprite glyph, Color c, float x0, float x1, string text, float size)
    {
        var cell = UiKit.Rect(name, chip, new Vector2(x0, 0f), new Vector2(x1, 1f));
        var icon = StarGlyph.Add(cell, "Glyph", glyph, c, new Vector2(32f, 32f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.pivot = new Vector2(0f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(10f, 0f);
        var t = UiKit.Text(cell, "Text", new Vector2(0f, 0.1f), new Vector2(1f, 0.9f), text, size, c, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft);
        t.rectTransform.offsetMin = new Vector2(46f, 0f);
    }

    private void CreateLevelSelectLanguagePicker(Transform panelRoot)
    {
        if (GameObject.Find("LevelSelectLanguagePicker") != null)
            return;

        bool tablet = DeviceLayout.IsTabletLike();
        var go = new GameObject("LevelSelectLanguagePicker");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(panelRoot, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(tablet ? 14f : 10f, tablet ? -10f : -8f);
        rt.sizeDelta = new Vector2(tablet ? 380f : 340f, tablet ? 64f : 56f);

        var img = go.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = new Color(0.16f, 0.2f, 0.28f, 0.92f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        RuntimeUiPolish.ApplyButtonTransitions(btn, img.color,
            Color.Lerp(img.color, Color.white, 0.15f),
            Color.Lerp(img.color, Color.black, 0.2f));
        btn.onClick.AddListener(LocalizationManager.CycleNext);

        var textGo = new GameObject("Text");
        var trt = textGo.AddComponent<RectTransform>();
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(12f, 6f);
        trt.offsetMax = new Vector2(-12f, -6f);

        levelSelectLangTmp = textGo.AddComponent<TextMeshProUGUI>();
        levelSelectLangTmp.enableAutoSizing = true;
        levelSelectLangTmp.fontSizeMin = Mathf.Max(12, UiTypography.Scale(14));
        levelSelectLangTmp.fontSizeMax = UiTypography.Scale(tablet ? 26 : 22);
        levelSelectLangTmp.fontSize = levelSelectLangTmp.fontSizeMax;
        levelSelectLangTmp.alignment = TextAlignmentOptions.Center;
        levelSelectLangTmp.color = new Color(0.93f, 0.96f, 1f, 1f);
        levelSelectLangTmp.textWrappingMode = TextWrappingModes.Normal;
        levelSelectLangTmp.overflowMode = TextOverflowModes.Overflow;
        CopyFontFromAny(levelSelectLangTmp);
        RefreshLevelSelectLanguageLabel();
    }

    private static void CopyFontFromAny(TextMeshProUGUI target)
    {
        UiTypography.ApplyDefaultFontAsset(target);
        if (target.font == null)
        {
            var any = FindAnyObjectByType<TextMeshProUGUI>();
            if (any != null && any != target && any.font != null)
            {
                target.font = any.font;
                if (any.fontSharedMaterial != null)
                    target.fontSharedMaterial = any.fontSharedMaterial;
            }
        }

        if (target != null)
            target.fontStyle = FontStyles.Bold;
    }

    private void CreateBackButton(Transform parent)
    {
        bool tablet = DeviceLayout.IsTabletLike();
        var go = new GameObject("BackButton");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        float margin = tablet ? 20f : 16f;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(tablet ? 260f : 240f, tablet ? 64f : 58f);
        rt.anchoredPosition = new Vector2(margin, margin + 4f);

        var img = go.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = buttonColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        RuntimeUiPolish.ApplyButtonTransitions(btn, buttonColor,
            RuntimeUiPolish.ButtonNeutralHover,
            new Color(0.12f, 0.13f, 0.16f, 1f));
        RuntimeUiPolish.ApplyDropShadow(rt, new Vector2(1.5f, -2.5f), 0.22f);

        var textGo = new GameObject("Text");
        var trt = textGo.AddComponent<RectTransform>();
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 6f);
        trt.offsetMax = new Vector2(-10f, -6f);

        backTmp = textGo.AddComponent<TextMeshProUGUI>();
        backTmp.text = LocalizationManager.Get("ui.back_menu", "Back to Menu");
        backTmp.fontSize = UiTypography.Scale(tablet ? 28 : 25);
        backTmp.fontStyle = FontStyles.Bold;
        backTmp.alignment = TextAlignmentOptions.Center;
        backTmp.color = RuntimeUiPolish.TitleIvory;
        CopyFontFromAny(backTmp);
        backTmp.fontStyle = FontStyles.Bold;
        LocalizationManager.ApplyTextDirection(backTmp);

        btn.onClick.AddListener(() => SceneTransitionHost.LoadSingleScene("Menu"));
    }

    private void StartGameAt(int index)
    {
        if (GameLevelCatalog.IsComingSoonLevel(index))
        {
            ShowComingSoonOverlay();
            return;
        }

        if (!Entitlements.IsLevelUnlocked(index))
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
                PaywallOverlay.Open(canvas.transform, () => StartGameAt(index));
            return;
        }

        DailyChallengeSession.End();
        ChallengeSession.End();
        LevelSelection.SetSelectedLevel(index);
        SceneTransitionHost.LoadSingleScene("Game");
    }

    // ------------------------------------------------------------------ daily challenge card

    /// <summary>Bottom-centre card for today's challenge (free players draw from free levels).</summary>
    private void BuildDailyCard(Transform panelRoot)
    {
        var spec = ProgressStore.TodaysChallenge();
        if (!spec.IsValid)
            return;
        bool tablet = DeviceLayout.IsTabletLike();
        bool done = ProgressStore.DailyDoneToday;
        int streak = ProgressStore.CurrentDailyStreak;
        string title = done
            ? LocalizationManager.Get("daily.done_title", "Daily challenge done. Back tomorrow!")
            : string.Format(LocalizationManager.Get("daily.title", "Daily challenge: {0}"),
                GameLevelCatalog.GetLocalizedDisplayName(spec.LevelIndex));
        string sub = done
            ? string.Format(LocalizationManager.Get("daily.streak", "Streak: {0} day(s)"), streak)
            : DailyChallengeSession.GoalText(spec.Goal) +
              (streak > 0 ? "   |   " + string.Format(LocalizationManager.Get("daily.streak", "Streak: {0} day(s)"), streak) : "");
        var bg = new Color(0.27f, 0.22f, 0.5f, 0.97f);
        var btn = UiKit.Button(panelRoot, "DailyCard", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), "", bg, () =>
        {
            if (done)
            {
                FeelDirector.Play(SfxId.Locked, 0.5f);
                return;
            }
            ChallengeSession.End();
            DailyChallengeSession.Begin(spec);
            LevelSelection.SetSelectedLevel(spec.LevelIndex);
            SceneTransitionHost.LoadSingleScene("Game");
        });
        var rt = (RectTransform)btn.transform;
        rt.pivot = new Vector2(0.5f, 0f);
        float margin = tablet ? 20f : 16f;
        // Fits between Back (left) and Math tips + How to play (right) on a landscape phone.
        rt.sizeDelta = new Vector2(tablet ? 680f : 700f, tablet ? 88f : 80f);
        rt.anchoredPosition = new Vector2(tablet ? -130f : -140f, margin - 2f);
        _dailyRt = rt;
        var icon = StarGlyph.Add(rt, "DailyIcon", done ? StarGlyph.Calendar : StarGlyph.Flame,
            done ? UiKit.Mint : UiKit.Gold, new Vector2(46f, 46f));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.pivot = new Vector2(0f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(16f, 0f);
        _dailyTmp = UiKit.Text(rt, "Label", new Vector2(0f, 0.06f), new Vector2(1f, 0.94f),
            $"<b>{title}</b>\n<size=76%><color=#d9d2ff>{sub}</color></size>",
            UiTypography.Scale(tablet ? 24 : 22), RuntimeUiPolish.TitleIvory, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        _dailyTmp.rectTransform.offsetMin = new Vector2(76f, 0f);
        _dailyTmp.rectTransform.offsetMax = new Vector2(-14f, 0f);
    }

    private const string ComingSoonOverlayName = "ComingSoonOverlayRoot";

    /// <summary>Full-screen message for catalog levels not yet playable (e.g. Hohmann transfer).</summary>
    private void ShowComingSoonOverlay()
    {
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return;

        var existing = canvas.transform.Find(ComingSoonOverlayName);
        if (existing != null)
            Destroy(existing.gameObject);

        var root = new GameObject(ComingSoonOverlayName);
        var rootRt = root.AddComponent<RectTransform>();
        rootRt.SetParent(canvas.transform, false);
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;
        rootRt.SetAsLastSibling();

        var dim = root.AddComponent<Image>();
        dim.color = new Color(0.04f, 0.05f, 0.1f, 0.88f);
        dim.raycastTarget = true;

        var dimBtn = root.AddComponent<Button>();
        dimBtn.targetGraphic = dim;
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(() => Destroy(root));

        bool tablet = DeviceLayout.IsTabletLike();
        var panelGo = new GameObject("Panel");
        var panelRt = panelGo.AddComponent<RectTransform>();
        panelRt.SetParent(root.transform, false);
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(tablet ? 560f : 480f, tablet ? 200f : 176f);

        var panelImg = panelGo.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(panelImg);
        panelImg.color = new Color(0.12f, 0.14f, 0.2f, 0.98f);
        panelImg.raycastTarget = true;

        var panelBtn = panelGo.AddComponent<Button>();
        panelBtn.targetGraphic = panelImg;
        panelBtn.transition = Selectable.Transition.None;
        panelBtn.onClick.AddListener(() => Destroy(root));
        OverlayCancelHandler.Attach(root, dimBtn, panelBtn);

        var msgGo = new GameObject("Message");
        var msgRt = msgGo.AddComponent<RectTransform>();
        msgRt.SetParent(panelGo.transform, false);
        msgRt.anchorMin = new Vector2(0.06f, 0.28f);
        msgRt.anchorMax = new Vector2(0.94f, 0.92f);
        msgRt.offsetMin = Vector2.zero;
        msgRt.offsetMax = Vector2.zero;

        var msgTmp = msgGo.AddComponent<TextMeshProUGUI>();
        msgTmp.text = LocalizationManager.Get("ui.coming_soon", "Coming soon!");
        msgTmp.fontSize = UiTypography.Scale(tablet ? 36 : 30);
        msgTmp.alignment = TextAlignmentOptions.Center;
        msgTmp.color = RuntimeUiPolish.TitleIvory;
        msgTmp.fontStyle = FontStyles.Bold;
        msgTmp.raycastTarget = false;
        CopyFontFromAny(msgTmp);
        LocalizationManager.ApplyTextDirection(msgTmp);

        var okGo = new GameObject("DismissHint");
        var okRt = okGo.AddComponent<RectTransform>();
        okRt.SetParent(panelGo.transform, false);
        okRt.anchorMin = new Vector2(0.06f, 0.08f);
        okRt.anchorMax = new Vector2(0.94f, 0.26f);
        okRt.offsetMin = Vector2.zero;
        okRt.offsetMax = Vector2.zero;

        var okTmp = okGo.AddComponent<TextMeshProUGUI>();
        okTmp.text = LocalizationManager.Get("ui.coming_soon_tap", "Tap anywhere to close");
        okTmp.fontSize = UiTypography.Scale(tablet ? 22 : 19);
        okTmp.alignment = TextAlignmentOptions.Center;
        okTmp.color = new Color(0.75f, 0.8f, 0.92f, 0.85f);
        okTmp.raycastTarget = false;
        CopyFontFromAny(okTmp);
        okTmp.fontStyle = FontStyles.Italic;
        LocalizationManager.ApplyTextDirection(okTmp);
    }
}
