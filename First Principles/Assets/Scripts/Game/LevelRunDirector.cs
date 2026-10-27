using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// LevelRunDirector — in-level extras layered on top of LevelManager:
//   * live star meter (3 stars that dim as you fall / graze f′),
//   * Hint button (3 free a day, unlimited with Plus): explains the level's rule and
//     drops "jump here" chevrons over every gap / step for a few seconds,
//   * first-run coach marks on the primer level (move → jump → f′ double jump → exit),
//   * goal chip when the run is today's daily challenge or a map challenge (Flawless,
//     Time trial with a live clock, region / Grand trials; hints are off in no-hint modes),
//   * first-level coach tour (spotlight walkthrough of controls, stars and hints).
// -----------------------------------------------------------------------------

public class LevelRunDirector : MonoBehaviour
{
    LevelManager _lm;
    Canvas _canvas;
    RectTransform _safe;

    RectTransform _meterRt;
    Image[] _meterStars;
    int _meterShown = -1;

    Button _hintBtn;
    TextMeshProUGUI _hintLabel;
    readonly List<GameObject> _hintMarkers = new List<GameObject>();
    GameObject _hintCard;
    Coroutine _hintRoutine;

    TextMeshProUGUI _dailyChip;

    // Coach marks
    GameObject _coachRoot;
    TextMeshProUGUI _coachText;
    int _coachStep = -1;
    Vector2 _coachStartPos;
    bool _jumpedSinceStep;
    bool _airJumpedSinceStep;
    float _coachStepTime;

    public int HintsUsedThisLevel { get; private set; }

    public void Bind(LevelManager lm)
    {
        _lm = lm;
        _canvas = FindAnyObjectByType<Canvas>();
        if (_canvas == null)
            return;
        _safe = MobileUiRoots.GetSafeContentParent(_canvas.transform);
        if (_safe == null)
            _safe = _canvas.transform as RectTransform;
        BuildStarMeter();
        BuildHintButton();
        lm.LevelStarted += OnLevelStarted;
    }

    void OnEnable()
    {
        GameFeel.Jumped += OnJumped;
        Entitlements.Changed += RefreshHintLabel;
        ProgressStore.Changed += RefreshHintLabel;
    }

    void OnDisable()
    {
        GameFeel.Jumped -= OnJumped;
        Entitlements.Changed -= RefreshHintLabel;
        ProgressStore.Changed -= RefreshHintLabel;
        if (_lm != null)
            _lm.LevelStarted -= OnLevelStarted;
    }

    void OnLevelStarted(int levelIndex, bool restart)
    {
        if (!restart)
            HintsUsedThisLevel = 0;
        ClearHintMarkers();
        _meterShown = -1;
        RefreshHintLabel();
        RefreshDailyChip(levelIndex);
        if (!restart && !ProgressStore.LevelTourDone && !QaShots.Enabled && !CoachTour.IsOpen)
            StartCoroutine(StartLevelTourSoon(levelIndex));
        else if (!restart && levelIndex == 0 && !ProgressStore.OnboardingDone && _coachStep < 0)
            StartCoach();
    }

    void Update()
    {
        if (_lm == null || _lm.IsGraphCalculatorMode)
            return;
        int potential = ProgressRules.StarsFor(_lm.CurrentScore, _lm.FallsThisLevel);
        if (potential != _meterShown)
        {
            bool drop = _meterShown > potential && _meterShown > 0;
            _meterShown = potential;
            for (int i = 0; i < _meterStars.Length; i++)
            {
                StarGlyph.SetStar(_meterStars[i], i < potential);
            }
            if (drop && potential < _meterStars.Length)
                StartCoroutine(FeelDirector.PopScale(_meterStars[potential].transform, 0.35f, 1.25f));
        }
        TickCoach();
        TickChallengeClock();
    }

    // ------------------------------------------------------------------ star meter

    void BuildStarMeter()
    {
        bool tablet = DeviceLayout.IsTabletLike();
        float topPad = DeviceLayout.PreferOnScreenGameControls ? 12f : 20f;
        _meterRt = UiKit.Rect("StarMeter", _safe, new Vector2(0f, 1f), new Vector2(0f, 1f));
        _meterRt.pivot = new Vector2(0f, 1f);
        _meterRt.sizeDelta = new Vector2(tablet ? 180f : 160f, tablet ? 52f : 46f);
        _meterRt.anchoredPosition = new Vector2(24f, -(topPad + (tablet ? 100f : 96f)));
        var h = _meterRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = false;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;
        _meterStars = new Image[ProgressRules.MaxStars];
        float s = tablet ? 46f : 40f;
        for (int i = 0; i < _meterStars.Length; i++)
            _meterStars[i] = StarGlyph.AddStar(_meterRt, "MeterStar" + i, true, new Vector2(s, s));
    }

    // ------------------------------------------------------------------ hints

    void BuildHintButton()
    {
        bool tablet = DeviceLayout.IsTabletLike();
        float topPad = DeviceLayout.PreferOnScreenGameControls ? 12f : 20f;
        float mathW = tablet ? 330f : 300f; // matches LevelManager's Math concepts chip
        var rt = UiKit.Rect("HintButton", _safe, new Vector2(1f, 1f), new Vector2(1f, 1f));
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(tablet ? 196f : 178f, tablet ? 60f : 56f);
        rt.anchoredPosition = new Vector2(-(18f + mathW + 12f), -topPad);
        // Hint: yellow Bauhaus block, ink bulb glyph + label.
        var img = rt.gameObject.AddComponent<Image>();
        _hintBtn = rt.gameObject.AddComponent<Button>();
        _hintBtn.targetGraphic = img;
        var icon = StarGlyph.Add(rt, "Bulb", StarGlyph.Bulb, Bauhaus.Ink, new Vector2(40f, 40f));
        var irt = icon.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
        irt.pivot = new Vector2(0f, 0.5f);
        irt.anchoredPosition = new Vector2(10f, 0f);
        _hintLabel = UiKit.Text(rt, "Text", new Vector2(0.28f, 0.08f), new Vector2(0.96f, 0.92f), "",
            UiTypography.Scale(tablet ? 26 : 24), Bauhaus.Ink, FontStyles.Bold);
        Bauhaus.Button(_hintBtn, Bauhaus.Tone.Yellow);
        _hintBtn.onClick.AddListener(OnHintPressed);
        RefreshHintLabel();

        // Quick restart (also R on keyboard, Y / triangle on a controller).
        var rr = UiKit.Rect("RestartButton", _safe, new Vector2(1f, 1f), new Vector2(1f, 1f));
        rr.pivot = new Vector2(1f, 1f);
        float rs = tablet ? 60f : 56f;
        rr.sizeDelta = new Vector2(rs, rs);
        rr.anchoredPosition = new Vector2(rt.anchoredPosition.x - rt.sizeDelta.x - 12f, -topPad);
        var rimg = rr.gameObject.AddComponent<Image>();
        var rbtn = rr.gameObject.AddComponent<Button>();
        rbtn.targetGraphic = rimg;
        var rglyph = StarGlyph.Add(rr, "Glyph", StarGlyph.Restart, Bauhaus.Ink, new Vector2(rs * 0.62f, rs * 0.62f));
        rglyph.rectTransform.anchoredPosition = Vector2.zero;
        Bauhaus.Button(rbtn, Bauhaus.Tone.Paper);
        rbtn.onClick.AddListener(() => { if (_lm != null) _lm.QuickRestart(); });
    }

    void RefreshHintLabel()
    {
        if (_hintLabel == null)
            return;
        int left = ProgressStore.HintsLeftToday;
        string word = LocalizationManager.Get("hint.button", "Hint");
        if (_lm != null && ChallengeSession.BlocksHints(_lm.CurrentLevelIndex))
        {
            _hintLabel.text = LocalizationManager.Get("hint.off", "Hints off");
            return;
        }
        _hintLabel.text = Entitlements.HasPlus || left == int.MaxValue ? word : $"{word} ({left})";
    }

    void OnHintPressed()
    {
        if (_lm == null || _lm.Player == null || _lm.Player.World == null)
            return;
        if (ChallengeSession.BlocksHints(_lm.CurrentLevelIndex))
        {
            FeelDirector.Play(SfxId.Locked, 0.7f);
            StartCoroutine(FeelDirector.PopScale(_hintBtn.transform, 0.3f, 1.15f));
            return;
        }
        if (!ProgressStore.TryUseHint())
        {
            FeelDirector.Play(SfxId.Locked, 0.7f);
            PaywallOverlay.Open(_canvas.transform, null, PaywallOverlay.Reason.Hints);
            return;
        }
        HintsUsedThisLevel++;
        FeelDirector.Play(SfxId.Hint, 0.8f);
        if (_hintRoutine != null)
            StopCoroutine(_hintRoutine);
        _hintRoutine = StartCoroutine(ShowHint());
    }

    IEnumerator ShowHint()
    {
        ClearHintMarkers();
        BuildHintMarkers(_lm.Player.World);
        ShowHintCard(_lm.CurrentDefinition);
        float t = 0f;
        const float life = 7f;
        while (t < life)
        {
            t += Time.unscaledDeltaTime;
            float pulse = 0.6f + 0.4f * Mathf.Sin(t * 6f);
            float fade = t > life - 1f ? life - t : 1f;
            foreach (var m in _hintMarkers)
            {
                if (m == null) continue;
                var g = m.GetComponentInChildren<Image>();
                if (g != null) g.color = Bauhaus.WithAlpha(Bauhaus.Red, pulse * fade);
            }
            yield return null;
        }
        ClearHintMarkers();
        _hintRoutine = null;
    }

    void BuildHintMarkers(GraphWorld world)
    {
        var root = _lm.ObstaclesRoot;
        var plane = _lm.CartesianPlane;
        if (root == null || plane == null || world == null)
            return;
        var gs = world.gridSize;
        if (gs.x <= 0 || gs.y <= 0)
            return;
        float uw = plane.rect.width / gs.x;
        float uh = plane.rect.height / gs.y;

        var plats = new List<GridRect>(world.platforms);
        plats.Sort((a, b) => a.xMin.CompareTo(b.xMin));
        int added = 0;
        for (int i = 0; i + 1 < plats.Count && added < 24; i++)
        {
            var a = plats[i];
            var b = plats[i + 1];
            bool gap = b.xMin - a.xMax > 0.2f;
            bool stepUp = b.yMax - a.yMax > 1.1f && b.xMin - a.xMax > -0.05f;
            if (!gap && !stepUp)
                continue;
            // Chevron centred above the take-off edge.
            var rect = new GridRect(a.xMax - 0.9f, a.xMax + 0.3f, a.yMax + 0.3f, a.yMax + 1.5f);
            var go = new GameObject("HintMarker_" + i);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            var layout = go.AddComponent<GraphObstacleUILayout>();
            layout.gridRect = rect;
            layout.ApplyPixelLayout(uw, uh);
            var img = StarGlyph.Add(rt, "Chevron", StarGlyph.Chevron, Bauhaus.Red, Vector2.zero);
            var irt = img.rectTransform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            _hintMarkers.Add(go);
            added++;
        }
    }

    void ClearHintMarkers()
    {
        foreach (var m in _hintMarkers)
            if (m != null) Destroy(m);
        _hintMarkers.Clear();
        if (_hintCard != null)
            Destroy(_hintCard);
        _hintCard = null;
    }

    /// <summary>The level's actual generation rule, stated plainly (matches GraphObstacleGenerator).</summary>
    public static string RuleText(LevelDefinition def)
    {
        if (def == null)
            return "";
        if (def.useRiemannStairPlatforms && def.riemannRule != RiemannRule.None)
        {
            string rule = def.riemannRule == RiemannRule.Left ? LocalizationManager.Get("hint.riemann.left", "left endpoint")
                : def.riemannRule == RiemannRule.Right ? LocalizationManager.Get("hint.riemann.right", "right endpoint")
                : LocalizationManager.Get("hint.riemann.mid", "midpoint");
            return string.Format(LocalizationManager.Get("hint.rule.riemann",
                "Each step is one Riemann rectangle: its height is f(x*) at the {0} of the subinterval (n = {1}). Jump from tread to tread across the gaps."),
                rule, def.riemannRectCount);
        }
        return string.Format(LocalizationManager.Get("hint.rule.derivative",
            "Ground exists only where f′(x) > {0}. Where the slope drops to {0} or below, the column becomes a spike pit, so jump across it. Platforms sit at height f(x)."),
            def.derivativeSafeThreshold.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    void ShowHintCard(LevelDefinition def)
    {
        bool tablet = DeviceLayout.IsTabletLike();
        var rt = UiKit.Rect("HintCard", _safe, new Vector2(0.18f, 0f), new Vector2(0.82f, 0f));
        rt.pivot = new Vector2(0.5f, 0f);
        float bottom = DeviceLayout.PreferOnScreenGameControls ? DeviceLayout.TouchControlBarHeight + 16f : 24f;
        rt.sizeDelta = new Vector2(0f, tablet ? 190f : 176f);
        rt.anchoredPosition = new Vector2(0f, bottom);
        // Hint card: white sheet, black rule, yellow strip (the hint colour) on the left.
        var img = rt.gameObject.AddComponent<Image>();
        Bauhaus.Panel(img, Color.white, Bauhaus.ButtonRule);
        img.raycastTarget = false;
        var strip = UiKit.Rect("Strip", rt, new Vector2(0f, 0f), new Vector2(0f, 1f));
        strip.pivot = new Vector2(0f, 0.5f);
        strip.sizeDelta = new Vector2(14f, 0f);
        var simg = strip.gameObject.AddComponent<Image>();
        simg.sprite = Bauhaus.Square;
        simg.color = Bauhaus.Yellow;
        simg.raycastTarget = false;
        string body = RuleText(def) + "\n<color=#1F4FA0>" +
                      LocalizationManager.Get("hint.airjump",
                          "Touch the f′ curve in mid-air for a double jump (costs 5 points). Chevrons mark the take-off edges.") +
                      "</color>";
        UiKit.Text(rt, "Body", new Vector2(0.05f, 0.08f), new Vector2(0.96f, 0.92f), body,
            UiTypography.Scale(tablet ? 22 : 20), Bauhaus.Ink);
        _hintCard = rt.gameObject;
    }

    // ------------------------------------------------------------------ daily chip

    void RefreshDailyChip(int levelIndex)
    {
        var d = DailyChallengeSession.Active;
        var ch = ChallengeSession.Active;
        bool challenge = ch.HasValue && ch.Value.LevelIndex == levelIndex;
        bool show = challenge || (d.HasValue && d.Value.LevelIndex == levelIndex && !ProgressStore.DailyDoneToday);
        if (!show)
        {
            if (_dailyChip != null)
                Destroy(_dailyChip.transform.parent.gameObject);
            _dailyChip = null;
            return;
        }
        if (_dailyChip == null)
        {
            bool tablet = DeviceLayout.IsTabletLike();
            float topPad = DeviceLayout.PreferOnScreenGameControls ? 12f : 20f;
            var rt = UiKit.Rect("DailyChip", _safe, new Vector2(0f, 1f), new Vector2(0f, 1f));
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(tablet ? 520f : 470f, 46f);
            rt.anchoredPosition = new Vector2(200f, -(topPad + (tablet ? 102f : 98f)));
            var img = rt.gameObject.AddComponent<Image>();
            Bauhaus.Panel(img, Bauhaus.Yellow, 2f);
            img.raycastTarget = false;
            _dailyChip = UiKit.Text(rt, "Text", new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f), "",
                UiTypography.Scale(20), Bauhaus.Ink, FontStyles.Bold);
        }
        if (challenge)
        {
            _challengeLevel = levelIndex;
            _dailyChip.text = ChallengeChipText(ch.Value);
            _dailyChip.transform.parent.GetComponent<Image>().color = Bauhaus.Card; // challenge runs: paper chip
        }
        else
        {
            _challengeLevel = -1;
            _dailyChip.text = LocalizationManager.Get("daily.chip", "Daily challenge: ") + DailyChallengeSession.GoalText(d.Value.Goal);
        }
    }

    int _challengeLevel = -1;
    float _nextClockTick;

    string ChallengeChipText(ChallengeSpec c)
    {
        string head = ChallengeSession.Title(c.Kind) + ": ";
        if (c.Kind == ChallengeKind.TimeTrial && _lm != null)
        {
            float t = _lm.LevelElapsed;
            string clock = LevelCompleteOverlay.FormatTime(t);
            string color = t <= c.ParSeconds ? "#1F4FA0" : "#D7261E";
            return head + $"<color={color}>{clock}</color> / {c.ParSeconds}s";
        }
        return head + ChallengeSession.GoalText(c);
    }

    void TickChallengeClock()
    {
        if (_dailyChip == null || _challengeLevel < 0 || Time.unscaledTime < _nextClockTick)
            return;
        _nextClockTick = Time.unscaledTime + 0.1f;
        var ch = ChallengeSession.Active;
        if (ch.HasValue && ch.Value.Kind == ChallengeKind.TimeTrial && ch.Value.LevelIndex == _challengeLevel)
            _dailyChip.text = ChallengeChipText(ch.Value);
    }

    // ------------------------------------------------------------------ first-level coach tour

    IEnumerator StartLevelTourSoon(int levelIndex)
    {
        // Let the HUD lay out, then freeze the run while the walkthrough is up.
        yield return new WaitForSecondsRealtime(0.8f);
        // Let the level's story card finish first (the tour then explains the playfield).
        float waited = 0f;
        while (_lm != null && _lm.StageIntroShowing && waited < 30f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.3f);
        if (ProgressStore.LevelTourDone || CoachTour.IsOpen || _canvas == null)
            yield break;
        StartLevelTour(levelIndex);
    }

    void StartLevelTour(int levelIndex)
    {
        bool touch = DeviceLayout.PreferOnScreenGameControls;
        bool pad = UnityEngine.InputSystem.Gamepad.current != null;
        string controls = touch
            ? LocalizationManager.Get("tour.level.controls.touch", "Hold the left or right side of the screen to move. Tap with a second finger to jump. The circular arrow restarts instantly.")
            : pad ? LocalizationManager.Get("tour.level.controls.pad", "Move with the left stick or d-pad and press A to jump; hold A for a higher jump. Y restarts instantly, B leaves the level.")
            : LocalizationManager.Get("tour.level.controls.keys", "Move with ← → or A / D, jump with Space, W or ↑ (hold for a higher jump). R restarts instantly, Esc leaves the level.");
        var steps = new List<CoachStep>
        {
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.level.controls.title", "Controls"),
                Body = controls,
                Target = () => null,
            },
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.level.graph.title", "The level is the graph"),
                Body = LocalizationManager.Get("tour.level.graph.body",
                    "You walk on f(x). Ground only exists where the slope f′(x) is high enough, so jump the pits where it dips. Touch the red f′ curve in mid-air for a double jump. Grab ∂ tokens for points, bounce off springs for big leaps, and reach the yellow exit on the right. Longer levels have a checkpoint flag."),
                Target = () => _lm != null ? _lm.CartesianPlane : null,
            },
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.level.stars.title", "Stars"),
                Body = string.Format(LocalizationManager.Get("tour.level.stars.body",
                    "These three stars show what this run is worth right now. Falling or grazing f′ costs you: 3 stars need no falls and {0}+ points."),
                    ProgressRules.ThreeStarMinScore),
                Target = () => _meterRt,
            },
            new CoachStep
            {
                Title = LocalizationManager.Get("tour.level.hint.title", "Hints"),
                Body = string.Format(LocalizationManager.Get("tour.level.hint.body",
                    "Stuck? Hint explains this level's rule and marks every take-off edge for a few seconds. You get {0} free hints a day."),
                    ProgressRules.FreeHintsPerDay),
                Target = () => _hintBtn != null ? (RectTransform)_hintBtn.transform : null,
            },
        };
        float savedScale = Time.timeScale;
        Time.timeScale = 0f;
        if (_lm != null && _lm.Player != null)
            _lm.Player.SetInputLocked(true);
        CoachTour.Show(_canvas.transform, steps, done =>
        {
            Time.timeScale = savedScale <= 0f ? 1f : savedScale;
            // Unlock a frame later so the press that closed the tour is not also a jump.
            if (isActiveAndEnabled)
                StartCoroutine(UnlockInputNextFrame());
            ProgressStore.LevelTourDone = true;
            // The walkthrough covers what the old on-the-fly coach marks taught.
            ProgressStore.OnboardingDone = true;
        });
    }


    IEnumerator UnlockInputNextFrame()
    {
        yield return null;
        yield return null;
        if (_lm != null && _lm.Player != null)
            _lm.Player.SetInputLocked(false);
    }

    // ------------------------------------------------------------------ onboarding coach marks

    void StartCoach()
    {
        bool tablet = DeviceLayout.IsTabletLike();
        var rt = UiKit.Rect("CoachMark", _safe, new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.5f));
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, tablet ? 120f : 108f);
        rt.anchoredPosition = new Vector2(0f, tablet ? 150f : 130f);
        // First-run coach mark: ink block, paper type.
        var img = rt.gameObject.AddComponent<Image>();
        Bauhaus.Panel(img, Bauhaus.Ink, 0f);
        img.raycastTarget = false;
        _coachText = UiKit.Text(rt, "Text", new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), "",
            UiTypography.Scale(tablet ? 30 : 27), Bauhaus.Card, FontStyles.Bold);
        _coachRoot = rt.gameObject;
        SetCoachStep(0);
    }

    void SetCoachStep(int step)
    {
        _coachStep = step;
        _coachStepTime = Time.unscaledTime;
        _jumpedSinceStep = false;
        _airJumpedSinceStep = false;
        if (_lm != null && _lm.Player != null)
            _coachStartPos = _lm.Player.PlayerCenterGrid;
        bool touch = DeviceLayout.PreferOnScreenGameControls;
        bool pad = UnityEngine.InputSystem.Gamepad.current != null;
        string text;
        switch (step)
        {
            case 0:
                text = touch ? LocalizationManager.Get("coach.move.touch", "Hold the left or right side of the screen to move")
                    : pad ? LocalizationManager.Get("coach.move.pad", "Move with the left stick")
                    : LocalizationManager.Get("coach.move.keys", "Move with ← → or A / D");
                break;
            case 1:
                text = touch ? LocalizationManager.Get("coach.jump.touch", "Tap the jump button to hop over the gaps")
                    : pad ? LocalizationManager.Get("coach.jump.pad", "Press A to jump over the gaps")
                    : LocalizationManager.Get("coach.jump.keys", "Press Space (or W / ↑) to jump over the gaps");
                break;
            case 2:
                text = LocalizationManager.Get("coach.air",
                    "Gaps open where the slope f′(x) dips. Touch the red f′ curve in mid-air and jump again for a double jump");
                break;
            case 3:
                text = LocalizationManager.Get("coach.exit", "Reach the yellow exit on the right. Fewer falls = more stars");
                break;
            default:
                FinishCoach();
                return;
        }
        if (_coachText != null)
        {
            _coachText.text = text;
            LocalizationManager.ApplyTextDirection(_coachText);
            StartCoroutine(FeelDirector.PopScale(_coachRoot.transform, 0.35f, 1.12f));
        }
    }

    void TickCoach()
    {
        if (_coachStep < 0 || _lm == null || _lm.Player == null)
            return;
        float elapsed = Time.unscaledTime - _coachStepTime;
        switch (_coachStep)
        {
            case 0:
                if (Mathf.Abs(_lm.Player.PlayerCenterGrid.x - _coachStartPos.x) > 1.2f)
                    SetCoachStep(1);
                break;
            case 1:
                if (_jumpedSinceStep && elapsed > 0.6f)
                    SetCoachStep(2);
                break;
            case 2:
                if (_airJumpedSinceStep || elapsed > 9f)
                    SetCoachStep(3);
                break;
            case 3:
                if (elapsed > 5f)
                    SetCoachStep(4);
                break;
        }
    }

    void OnJumped(bool air)
    {
        _jumpedSinceStep = true;
        if (air)
            _airJumpedSinceStep = true;
    }

    void FinishCoach()
    {
        _coachStep = -2;
        if (_coachRoot != null)
            Destroy(_coachRoot);
        _coachRoot = null;
        ProgressStore.OnboardingDone = true;
    }
}
