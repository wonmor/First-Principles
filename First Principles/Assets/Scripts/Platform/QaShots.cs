using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// QaShots — automated smoke tour + screenshots for QA / store review images.
// -----------------------------------------------------------------------------
// Development builds only. Launch with:
//   "First Principles.app/Contents/MacOS/First Principles" -fpShots <outDir> [-fpShotScale 3]
//     -screen-width 932 -screen-height 430 -screen-fullscreen 0
// Uses a separate progress file (progress-qa.json) and a simulated store with the
// configured USD prices, so it never touches the player's real progress or the real store.
// Walks Menu → Level Select (daily card, chapters with stars) → paywall → achievements →
// gameplay with autopilot → hint → level-complete card → first-run coach mark, then quits.
// -----------------------------------------------------------------------------

public class QaShots : MonoBehaviour
{
    public static bool Enabled
    {
        get
        {
#if !FP_QA_SHOTS
            if (!Debug.isDebugBuild && !Application.isEditor)
                return false;
#endif
            // Desktop: -fpShots <dir>. iOS Simulator: SIMCTL_CHILD_FP_SHOTS=1 xcrun simctl launch …
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-fpShots") >= 0
                   || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FP_SHOTS"));
        }
    }

    string _dir;
    int _scale = 2;
    int _n;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Enabled || Application.isEditor)
            return;
        var go = new GameObject("QaShots");
        DontDestroyOnLoad(go);
        go.AddComponent<QaShots>();
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        _dir = Arg("-fpShots") ?? Path.Combine(Application.persistentDataPath, "qa-shots");
        int.TryParse(Arg("-fpShotScale") ?? Environment.GetEnvironmentVariable("FP_SHOT_SCALE") ?? "1", out _scale);
        Directory.CreateDirectory(_dir);
        if (Application.isBatchMode)
        {
            // Offscreen (screen locked / headless): render into a texture at the requested size.
            int.TryParse(Arg("-screen-width") ?? "1920", out int ow);
            int.TryParse(Arg("-screen-height") ?? "1080", out int oh);
            Space3DSession.ScreenOverride = new Vector2Int(ow, oh);
            Screen.SetResolution(ow, oh, FullScreenMode.Windowed);
            _rt = new RenderTexture(ow, oh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _rt.Create();
            SceneManager.sceneLoaded += (s, m) => Retarget();
            Retarget();
        }

        Debug.Log($"QaShots: writing to {_dir} at {Screen.width}x{Screen.height} x{_scale} (offscreen {(_rt != null ? _rt.width + "x" + _rt.height : "off")})");

        SeedProgress();
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fp3d") >= 0)
        {
            yield return Tour3D();
            Debug.Log("QaShots: done");
            Application.Quit();
            yield break;
        }

        string rec = Arg("-fpRecord");
        if (!string.IsNullOrEmpty(rec))
        {
            yield return Record(rec, int.TryParse(Arg("-fpRecordLevel"), out int rl) ? rl : 2);
            Application.Quit();
            yield break;
        }
        Entitlements.DebugOverride = false; // show the free tier (locks, paywall)
        Entitlements.InstallQaStore(new QaPriceStore());
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fpStore") >= 0)
        {
            yield return StoreTour();
            Debug.Log("QaShots: done");
            Application.Quit();
            yield break;
        }

        yield return new WaitForSecondsRealtime(4f);
        yield return Shot("menu");

        var menuCanvas = FindAnyObjectByType<Canvas>();
        if (menuCanvas != null)
        {
            MenuPlayerGlyphPickerOverlay.Open(menuCanvas.transform);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("menu-player-icon");
            GameObject.Find("MenuPlayerGlyphPickerRoot")?.SetActive(false);
            MenuTutorialOverlay.Open(menuCanvas.transform);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("menu-how-to-play");
            GameObject.Find("MenuTutorialOverlayRoot")?.SetActive(false);
        }

        // World map, arriving back from a first clear of level 7 (2 stars): stars pop, the path
        // fills, the Core trial opens (19/18 stars) and the marker hops to level 8.
        var d = ProgressStore.Data;
        d.stars[7] = 2;
        d.clears[7] = 1;
        d.bestScore[7] = 80;
        d.bestSeconds[2] = 22.4f;
        ProgressStore.Save();
        MapReturn.Set(7, 0, 2, true, null, 0);
        SceneManager.LoadScene("LevelSelect");
        yield return new WaitForSecondsRealtime(1.5f);
        yield return Shot("map-celebrate-stars");
        yield return new WaitForSecondsRealtime(1.3f);
        yield return Shot("map-trial-unlocked");
        yield return new WaitForSecondsRealtime(3f);
        yield return Shot("map");

        var lsc = FindAnyObjectByType<LevelSelectController>();
        var map = FindAnyObjectByType<WorldMapView>();
        var canvas = FindAnyObjectByType<Canvas>();
        if (map != null)
        {
            MapLevelSheet.Open(canvas.transform, 2, ChallengeKind.None, "core", Bauhaus.Blue, null, null, null, null);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("level-sheet");
            MapLevelSheet.Close();

            map.ScrollToLevel(8, true);
            bool open = map.IsTrialOpenForRegionEnding(8, out string key, out string req);
            MapLevelSheet.Open(canvas.transform, 8, ChallengeKind.RegionTrial, key, Bauhaus.Blue, () => open, req, null, null);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("trial-sheet");
            MapLevelSheet.Close();

            map.ScrollToLevel(13, true);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("map-integration-trial-gated");
            map.ScrollToLevel(20);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("map-locked-region");
            map.ScrollToLevel(49);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("map-bosses-grand-trial");
        }

        if (lsc != null)
        {
            lsc.StartMapTour();
            for (int step = 1; step <= 5; step++)
            {
                yield return new WaitForSecondsRealtime(1.1f);
                var tour = FindAnyObjectByType<CoachTour>();
                if (tour == null)
                    break;
                yield return Shot("map-tour-" + step);
                tour.Next();
            }
        }

        canvas = FindAnyObjectByType<Canvas>();
        PaywallOverlay.Open(canvas.transform);
        yield return new WaitForSecondsRealtime(1.2f);
        yield return Shot("paywall");
        PaywallOverlay.Close();

        PaywallOverlay.Open(canvas.transform, null, PaywallOverlay.Reason.Hints);
        yield return new WaitForSecondsRealtime(1.2f);
        yield return Shot("paywall-plus");
        PaywallOverlay.Close();

        AchievementsOverlay.Open(canvas.transform);
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot("achievements");
        AchievementsOverlay.Close();

        MathArticlesOverlay.Open(canvas.transform);
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot("math-tips");
        GameObject.Find("MathArticlesOverlayRoot")?.SetActive(false);

        // Gameplay: today's daily challenge on a free level, with autopilot.
        var spec = ProgressStore.TodaysChallenge();
        DailyChallengeSession.Begin(new DailyChallengeSpec(spec.DateKey, 2, spec.Goal));
        LevelSelection.SetSelectedLevel(2);
        SceneManager.LoadScene("Game");
        yield return new WaitForSecondsRealtime(1.6f);
        yield return Shot("stage-intro");
        yield return new WaitForSecondsRealtime(7f);

        MobileHoldAxis.PressRight();
        for (int i = 0; i < 6; i++)
        {
            MobileInputBridge.QueueJump();
            yield return new WaitForSecondsRealtime(0.35f);
        }
        yield return Shot("gameplay");
        MobileHoldAxis.Clear();

        // Pause sheet (normally opened when focus is lost or a controller unplugs).
        var pause = typeof(GamepadSupport).GetMethod("PauseMatch", BindingFlags.Static | BindingFlags.NonPublic);
        var resume = typeof(GamepadSupport).GetMethod("ResumeMatch", BindingFlags.Static | BindingFlags.NonPublic);
        if (pause != null && resume != null)
        {
            pause.Invoke(null, new object[] { LocalizationManager.Get("ui.paused", "Paused") });
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("pause");
            resume.Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.3f);
        }

        var hint = GameObject.Find("HintButton");
        hint?.GetComponent<Button>()?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.8f);
        yield return Shot("hint");

        // Level-complete card (simulated three-star clear of this level).
        canvas = FindAnyObjectByType<Canvas>();
        var r = new RunResult(2, 90, 0, 41.3f);
        var o = new ProgressStore.RecordOutcome { Stars = 3, PreviousStars = 1, FirstClear = false, DailyCompletedNow = true, DailyStreak = 5 };
        LevelCompleteOverlay.Open(canvas.transform, r, o, DailyChallengeSession.Active, null, null, null);
        yield return new WaitForSecondsRealtime(2.2f);
        yield return Shot("level-complete");
        LevelCompleteOverlay.Close();

        // Time trial on a cleared level: live clock in the goal chip, hints off.
        DailyChallengeSession.End();
        ChallengeSession.Begin(new ChallengeSpec(ChallengeKind.TimeTrial, 2, ChallengeRules.ParSeconds(22.4f)));
        LevelSelection.SetSelectedLevel(2);
        SceneManager.LoadScene("Game");
        yield return new WaitForSecondsRealtime(8f);
        yield return Shot("time-trial");
        canvas = FindAnyObjectByType<Canvas>();
        var cr = new RunResult(2, 95, 0, 18.2f);
        var co = new ProgressStore.RecordOutcome { Stars = 3, PreviousStars = 3 };
        var cc = new ChallengeOutcome { Kind = ChallengeKind.TimeTrial, Met = true, ParSeconds = 20, NewMedal = "t2" };
        LevelCompleteOverlay.Open(canvas.transform, cr, co, null, null, null, null, cc);
        yield return new WaitForSecondsRealtime(2.2f);
        yield return Shot("medal-earned");
        LevelCompleteOverlay.Close();
        ChallengeSession.End();

        // First level visit: the coach tour (controls, graph, stars, hints).
        ProgressStore.Data.levelTourDone = false;
        LevelSelection.SetSelectedLevel(0);
        SceneManager.LoadScene("Game");
        yield return new WaitForSecondsRealtime(3f);
        var lrd = FindAnyObjectByType<LevelRunDirector>();
        // Same path a first-time player gets: waits for the story card, then starts the tour.
        var soon = typeof(LevelRunDirector).GetMethod("StartLevelTourSoon", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(lrd, new object[] { 0 }) as IEnumerator;
        if (lrd != null && soon != null)
            lrd.StartCoroutine(soon);
        var introBtn = GameObject.Find("StageIntroOverlay")?.GetComponentInChildren<Button>();
        introBtn?.onClick.Invoke();
        for (int i = 0; i < 40 && FindAnyObjectByType<CoachTour>() == null; i++)
            yield return new WaitForSecondsRealtime(0.25f);
        for (int step = 1; step <= 4; step++)
        {
            yield return new WaitForSecondsRealtime(1.1f);
            var tour = FindAnyObjectByType<CoachTour>();
            if (tour == null)
                break;
            yield return Shot("level-tour-" + step);
            tour.Next();
        }

        // Graphing calculator mode: type an expression, then add f′ and the ∫ area strips.
        GraphCalculatorSession.RequestEnterFromMenu();
        SceneManager.LoadScene("Game");
        yield return new WaitForSecondsRealtime(3f);
        yield return Shot("calculator");
        var input = GameObject.Find(GraphCalculatorEquationPanel.EquationRootObjectName)?.GetComponentInChildren<TMPro.TMP_InputField>();
        if (input != null)
        {
            input.text = "sin(x)*x/2";
            input.onSubmit.Invoke(input.text);
        }
        yield return new WaitForSecondsRealtime(0.6f);
        GameObject.Find("GraphCalcDerivButton")?.GetComponent<Button>()?.onClick.Invoke();
        GameObject.Find("GraphCalcIntegralButton")?.GetComponent<Button>()?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(1.5f);
        yield return Shot("calculator-analysis");

        Debug.Log("QaShots: done");
        Application.Quit();
    }

    // ------------------------------------------------------------------ store screenshots
    // -fpStore: the App Store set, rendered at the window size (no upscaling): menu, world map,
    // achievements, gameplay after the intro and story banner have gone, hint, level complete,
    // graphing calculator. Achievements are pre-seeded so no unlock toast covers a shot.
    //   -fpStoreLevels "2,11"   gameplay levels (first one also runs the daily / hint / complete shots)
    //   -fpStoreCalc "expr;expr" calculator expressions (each shot plain and with f' + area)

    IEnumerator StoreTour()
    {
        var d = ProgressStore.Data;
        foreach (var id in AchievementCatalog.Earned(ProgressStore.Snapshot(), ProgressStore.PlayableLevelCount))
            if (!d.achievements.Contains(id))
                d.achievements.Add(id);
        foreach (var id in new[] { "FP_HINT_1", "FP_DAILY_1" })
            if (!d.achievements.Contains(id))
                d.achievements.Add(id);
        for (int i = 0; i < 14; i++)
            if (d.stars[i] > 0)
                d.bestSeconds[i] = 18.4f + i * 1.7f;
        foreach (var m in new[] { "f0", "t1", "f3" }) // a few challenge medals (flawless / time trial)
            if (!d.medals.Contains(m))
                d.medals.Add(m);
        ProgressStore.MapTourDone = true;
        ProgressStore.LevelTourDone = true;
        ProgressStore.Save();

        yield return new WaitForSecondsRealtime(4f);
        yield return Shot("menu");

        SceneManager.LoadScene("LevelSelect");
        yield return new WaitForSecondsRealtime(3.5f);
        yield return Shot("map");
        var canvas = FindAnyObjectByType<Canvas>();
        AchievementsOverlay.Open(canvas.transform);
        yield return new WaitForSecondsRealtime(1.2f);
        yield return Shot("achievements");
        AchievementsOverlay.Close();
        yield return new WaitForSecondsRealtime(0.6f);
        MapLevelSheet.Open(canvas.transform, 6, ChallengeKind.None, "core", Bauhaus.Blue, null, null, null, null);
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot("level-sheet");
        MapLevelSheet.Close();

        string[] levels = (Arg("-fpStoreLevels") ?? "2").Split(',');
        for (int li = 0; li < levels.Length; li++)
        {
            if (!int.TryParse(levels[li], out int level))
                continue;
            bool first = li == 0;
            if (first)
            {
                var spec = ProgressStore.TodaysChallenge();
                DailyChallengeSession.Begin(new DailyChallengeSpec(spec.DateKey, level, spec.Goal));
            }
            else
                DailyChallengeSession.End();
            yield return PlayToMidLevel(level);
            if (first)
            {
                // Deterministic mid-level poses: stand on a platform part-way along, then a jump off it.
                var pc = FindAnyObjectByType<LevelManager>()?.Player;
                string[] fr = (Arg("-fpStorePose") ?? "0.3,0.5").Split(',');
                for (int pi = 0; pi < fr.Length && pc != null && pc.World != null; pi++)
                {
                    float f = float.Parse(fr[pi], System.Globalization.CultureInfo.InvariantCulture);
                    float targetX = pc.World.gridSize.x * f;
                    GridRect best = default;
                    float bestD = float.MaxValue;
                    foreach (var pl in pc.World.platforms)
                    {
                        float dx = Mathf.Abs((pl.xMin + pl.xMax) * 0.5f - targetX);
                        if (pl.xMax - pl.xMin >= 0.6f && dx < bestD)
                        {
                            bestD = dx;
                            best = pl;
                        }
                    }
                    if (bestD == float.MaxValue)
                        break;
                    pc.TeleportTo((best.xMin + best.xMax) * 0.5f, best.yMax);
                    yield return new WaitForSecondsRealtime(0.7f);
                    yield return Shot($"pose-L{level}-{pi}-stand");
                    MobileInputBridge.QueueJump();
                    yield return new WaitForSecondsRealtime(0.22f);
                    yield return Shot($"pose-L{level}-{pi}-jump");
                    yield return new WaitForSecondsRealtime(1.2f);
                }
            }
            float[] at = first ? new float[0] : new[] { 0.9f, 1.6f, 2.3f, 3.0f };
            float t = 0f, jumpAt = 0.3f;
            int k = 0;
            while (k < at.Length)
            {
                if (MobileHoldAxis.Axis == 0f)
                    MobileHoldAxis.PressRight();
                if (t >= jumpAt)
                {
                    MobileInputBridge.QueueJump();
                    jumpAt = t + 0.55f;
                }
                if (t >= at[k])
                {
                    yield return Shot($"gameplay-L{level}-{(char)('a' + k)}");
                    k++;
                    t += 0.4f;
                    continue;
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            MobileHoldAxis.Clear();
            if (!first)
                continue;

            yield return new WaitForSecondsRealtime(0.5f);
            GameObject.Find("HintButton")?.GetComponent<Button>()?.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1.0f);
            yield return Shot($"hint-L{level}");
            yield return new WaitForSecondsRealtime(3f);

            canvas = FindAnyObjectByType<Canvas>();
            var r = new RunResult(level, 95, 0, 38.6f);
            var o = new ProgressStore.RecordOutcome { Stars = 3, PreviousStars = 2, FirstClear = false, DailyCompletedNow = true, DailyStreak = 5 };
            LevelCompleteOverlay.Open(canvas.transform, r, o, DailyChallengeSession.Active, null, null, null);
            yield return new WaitForSecondsRealtime(2.4f);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null); // no keyboard-focus tint
            yield return Shot($"level-complete-L{level}");
            LevelCompleteOverlay.Close();
            DailyChallengeSession.End();
        }

        string[] exprs = (Arg("-fpStoreCalc") ?? "sin(x)*x/2").Split(';');
        for (int ei = 0; ei < exprs.Length; ei++)
        {
            GraphCalculatorSession.RequestEnterFromMenu();
            SceneManager.LoadScene("Game");
            yield return new WaitForSecondsRealtime(3f);
            var input = GameObject.Find(GraphCalculatorEquationPanel.EquationRootObjectName)?.GetComponentInChildren<TMPro.TMP_InputField>();
            if (input != null)
            {
                input.text = exprs[ei];
                input.onSubmit.Invoke(input.text);
                input.DeactivateInputField();
            }
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            yield return new WaitForSecondsRealtime(2.6f); // graph reveal is 2.1 s
            yield return Shot($"calc{ei}");
            GameObject.Find("GraphCalcDerivButton")?.GetComponent<Button>()?.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot($"calc{ei}-deriv");
            GameObject.Find("GraphCalcIntegralButton")?.GetComponent<Button>()?.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot($"calc{ei}-analysis");
        }

        // -fpCalcTools: check Trans / Scale (short tap cycles the parameter, double tap nudges +, Scale tap zooms in).
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fpCalcTools") >= 0)
        {
            var trans = GameObject.Find("TransButton")?.GetComponent<UiShortLongPress>();
            var scale = GameObject.Find("ScaleButton")?.GetComponent<UiShortLongPress>();
            var fp = FindAnyObjectByType<FunctionPlotter>();
            Debug.Log($"QaShots: calc tools trans={(trans != null)} scale={(scale != null)}");
            if (trans != null && scale != null && fp != null)
            {
                Debug.Log($"QaShots: before A={fp.transA} k={fp.transK} x=[{fp.xStart},{fp.xEnd}]");
                trans.onShortRelease?.Invoke(); // A -> k
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("calc-tools-trans-k");
                trans.onShortRelease?.Invoke(); // k -> C
                trans.onShortRelease?.Invoke(); // quick second tap: C + step
                trans.onLongRelease?.Invoke();  // hold: C - step
                trans.onLongRelease?.Invoke();  // hold: C - step
                scale.onShortRelease?.Invoke(); // zoom in
                yield return new WaitForSecondsRealtime(2.6f); // graph reveal is 2.1 s
                Debug.Log($"QaShots: after A={fp.transA} k={fp.transK} C={fp.transC} x=[{fp.xStart},{fp.xEnd}]");
                yield return Shot("calc-tools");
                scale.onLongRelease?.Invoke(); // zoom back out
                yield return new WaitForSecondsRealtime(2.6f);
                Debug.Log($"QaShots: zoom out x=[{fp.xStart},{fp.xEnd}]");
                yield return Shot("calc-tools-zoom-out");
            }
        }
    }

    /// <summary>
    /// 1.5 3D tour: menu entry, 3D calculator (surface + helix, quadrics, inspect), stage select, a stage
    /// before / after solving, mid-run and cleared, a rail stage mid-ride and the helicoid.
    ///   -fp3d [-fp3dOnly calc|stages]
    /// </summary>
    IEnumerator Tour3D()
    {
        Entitlements.DebugOverride = true;
        var d = ProgressStore.Data;
        d.stars3d = new int[Stage3DCatalog.Count];
        int[] seeded = { 3, 3, 2, 3 };
        for (int i = 0; i < seeded.Length; i++)
            d.stars3d[i] = seeded[i];
        ProgressStore.Save();
        string only = Arg("-fp3dOnly") ?? "";

        if (only == "")
        {
            yield return new WaitForSecondsRealtime(4f);
            yield return Shot("menu-3d");
        }

        Space3DSession.RequestedTab = 0;
        SceneManager.LoadScene(Space3DSession.SceneName);
        yield return new WaitForSecondsRealtime(2.5f);
        var root = FindAnyObjectByType<Space3DScene>();
        if (root == null)
        {
            Debug.LogError("QaShots: Space3D scene missing");
            yield break;
        }

        if (only == "" || only == "calc")
        {
            root.Calculator.SetFunctions("sin(x)*cos(y)", "(2cos(t), 2sin(t), t/3), t = -3pi..3pi");
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("3d-calc");
            root.Rig.Yaw -= 50f;
            root.Rig.Pitch = 34f;
            yield return new WaitForSecondsRealtime(0.5f);
            root.Calculator.QaInspect(new Vector2(Space3DSession.ScreenW * 0.62f, Space3DSession.ScreenH * 0.5f));
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("3d-calc-inspect");
            root.Calculator.SetFunctions("x^2 + y^2 + z^2 = 9", "x^2 + y^2 - z^2 = 1", "(u*cos(v), u*sin(v), v/3), u = 0..3, v = 0..4pi");
            root.Rig.Frame(Box3.Cube(5f));
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("3d-calc-implicit");
            root.Calculator.SetFunctions("a*x^2 - b*y^2 + c", "3*exp(-(x^2 + y^2)/4)");
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("3d-calc-sliders");
        }

        if (only == "" || only == "stages")
        {
            root.ShowTab(1);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("3d-stage-select");

            var sv = root.Stages;
            sv.OpenStage(3); // the bowl
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("3d-stage-edit");
            sv.QaApplySolution();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("3d-stage-solved");
            sv.QaAutopilot = true;
            sv.QaGo();
            yield return new WaitForSecondsRealtime(1.4f);
            yield return Shot("3d-stage-run");
            for (float w = 0f; w < 20f && sv.QaRunning; w += 0.2f)
                yield return new WaitForSecondsRealtime(0.2f);
            yield return new WaitForSecondsRealtime(0.8f);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            yield return Shot("3d-stage-clear");

            foreach (int st in new[] { 6, 11, 12, 8 })
            {
                sv.OpenStage(st);
                sv.QaApplySolution();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot($"3d-stage{st + 1}-edit");
                sv.QaGo();
                yield return new WaitForSecondsRealtime(st == 11 ? 3.2f : 1.8f);
                yield return Shot($"3d-stage{st + 1}-run");
            }

            sv.QaAutopilot = false;
        }
    }

    /// <summary>Loads a level, skips the story card and waits until the story banner has faded.</summary>
    IEnumerator PlayToMidLevel(int level)
    {
        // A hit-stop cut short by the previous scene change can leave timeScale low; reset it.
        Time.timeScale = 1f;
        LevelSelection.SetSelectedLevel(level);
        SceneManager.LoadScene("Game");
        yield return new WaitForSecondsRealtime(1.5f);
        Time.timeScale = 1f;
        var lm = FindAnyObjectByType<LevelManager>();
        var flow = typeof(LevelManager).GetField("levelFlowRoutine", BindingFlags.Instance | BindingFlags.NonPublic);
        for (float w = 0f; w < 20f; w += 0.2f)
        {
            var intro = GameObject.Find("StageIntroOverlay");
            if (intro != null)
                intro.GetComponentInChildren<Button>()?.onClick.Invoke();
            if (w > 1f && (lm == null || flow == null || flow.GetValue(lm) == null))
            {
                Debug.Log($"QaShots: level {level} ready after {w:0.0}s (lm {(lm != null)}, timeScale {Time.timeScale})");
                break;
            }
            yield return new WaitForSecondsRealtime(0.2f);
        }
        yield return new WaitForSecondsRealtime(0.5f);
    }

    /// <summary>Gameplay clip for the report: autopilot run, frames written as PNGs (stitched into a GIF outside).</summary>
    IEnumerator Record(string dir, int level)
    {
        Directory.CreateDirectory(dir);
        ProgressStore.Data.levelTourDone = true;
        Entitlements.DebugOverride = true;
        LevelSelection.SetSelectedLevel(level);
        SceneManager.LoadScene("Game");
        yield return new WaitForSecondsRealtime(2.5f);
        // Skip the story card.
        var intro = GameObject.Find("StageIntroOverlay");
        var tap = intro != null ? intro.GetComponentInChildren<Button>() : null;
        tap?.onClick.Invoke();
        yield return new WaitForSecondsRealtime(1.2f);
        MobileHoldAxis.PressRight();
        int frame = 0;
        float next = 0f, jumpAt = 0.4f;
        for (float t = 0f; t < 10f; t += Time.unscaledDeltaTime)
        {
            if (MobileHoldAxis.Axis == 0f)
                MobileHoldAxis.PressRight(); // level start clears touch routing once input unlocks
            if (t >= jumpAt)
            {
                MobileInputBridge.QueueJump();
                jumpAt = t + 0.55f;
            }
            if (t >= next)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"f{frame++:000}.png"), 1);
                next = t + 1f / 15f;
            }
            yield return null;
        }
        MobileHoldAxis.Clear();
        yield return new WaitForSecondsRealtime(0.5f);
    }

    static RenderTexture _rt;

    static Camera _uiCam;
    static RenderTexture _rtUi;
    static readonly Vector3 UiPlace = new Vector3(0f, -5000f, 0f);

    /// <summary>
    /// Offscreen mode: scene cameras draw into the capture texture; overlay canvases become a world-space sheet
    /// (1080 units tall, like the real canvas) far below the scene, drawn on top by an orthographic UI camera.
    /// </summary>
    static Texture2D ReadRt(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        t.Apply();
        RenderTexture.active = prev;
        return t;
    }

    static void Retarget()
    {
        if (_rt == null)
            return;
        float h = 1080f, w = 1080f * _rt.width / _rt.height;
        if (_uiCam == null)
        {
            var go = new GameObject("QaUiCamera");
            _uiCam = go.AddComponent<Camera>();
            _uiCam.orthographic = true;
            _uiCam.orthographicSize = h * 0.5f;
            _uiCam.clearFlags = CameraClearFlags.SolidColor;
            _uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _rtUi = new RenderTexture(_rt.width, _rt.height, 24, RenderTextureFormat.ARGB32);
            _rtUi.Create();
            _uiCam.targetTexture = _rtUi;
            _uiCam.nearClipPlane = 0.1f;
            _uiCam.farClipPlane = 50f;
            _uiCam.depth = 100f;
            go.transform.position = UiPlace + new Vector3(0f, 0f, -10f);
        }

        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (cam != _uiCam)
                cam.targetTexture = _rt;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace)
                continue;
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = _uiCam;
            var rt = c.GetComponent<RectTransform>();
            rt.SetPositionAndRotation(UiPlace, Quaternion.identity);
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(w, h);
        }
    }

    IEnumerator Shot(string name)
    {
        if (_rt != null)
        {
            Retarget();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            yield return null;
            yield return new WaitForEndOfFrame();
            // Batchmode players do not render on their own: draw every camera into the texture now.
            Canvas.ForceUpdateCanvases();
            var cams = new System.Collections.Generic.List<Camera>(FindObjectsByType<Camera>(FindObjectsSortMode.None));
            cams.Sort((x, y) => x.depth.CompareTo(y.depth));
            foreach (var cam in cams)
            {
                if (!cam.isActiveAndEnabled || cam == _uiCam) continue;
                cam.targetTexture = _rt;
                cam.Render();
            }

            _uiCam.targetTexture = _rtUi;
            _uiCam.Render();
            var scene = ReadRt(_rt);
            var ui = ReadRt(_rtUi);
            var a = scene.GetPixels32();
            var b = ui.GetPixels32();
            for (int i = 0; i < a.Length; i++)
            {
                int k = b[i].a;
                if (k == 0) continue;
                // UI was alpha-blended over transparent black, so its colour is already premultiplied.
                a[i].r = (byte)Mathf.Min(255, b[i].r + a[i].r * (255 - k) / 255);
                a[i].g = (byte)Mathf.Min(255, b[i].g + a[i].g * (255 - k) / 255);
                a[i].b = (byte)Mathf.Min(255, b[i].b + a[i].b * (255 - k) / 255);
            }

            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(a);
            tex.Apply();
            Destroy(scene);
            Destroy(ui);
            string f = Path.Combine(_dir, $"{++_n:00}-{name}.png");
            File.WriteAllBytes(f, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log($"QaShots: {f}");
            yield break;
        }

        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fpStore") >= 0)
        {
            // Store set: no keyboard / controller focus tint and no hover from wherever the desktop cursor
            // happens to sit over the window (disabling the input module sends pointer-exit to everything).
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            foreach (var es in FindObjectsByType<UnityEngine.EventSystems.EventSystem>())
                es.enabled = false;
            yield return null;
            yield return null;
        }
        yield return new WaitForEndOfFrame();
        string file = $"{++_n:00}-{name}.png";
        string path = Path.Combine(_dir, file);
        // Mobile players treat the path as relative to persistentDataPath.
        ScreenCapture.CaptureScreenshot(Application.isMobilePlatform ? "qa-shots/" + file : path, Mathf.Max(1, _scale));
        Debug.Log($"QaShots: {path}");
        yield return new WaitForSecondsRealtime(0.4f);
    }

    static void SeedProgress()
    {
        ProgressStore.ResetForTesting();
        var d = ProgressStore.Data;
        int[] stars = { 3, 3, 2, 3, 1, 2, 3, 0, 0, 2, 3, 0, 0, 0 };
        for (int i = 0; i < stars.Length; i++)
        {
            d.stars[i] = stars[i];
            d.clears[i] = stars[i] > 0 ? 1 : 0;
            d.bestScore[i] = stars[i] == 3 ? 95 : stars[i] > 0 ? 80 : 0;
        }
        d.airJumps = 37;
        d.falls = 64;
        d.dailyStreak = 4;
        d.dailyBestStreak = 6;
        d.dailiesCompleted = 9;
        d.dailyLastKey = ProgressRules.DateKey(DateTime.Now.AddDays(-1));
        d.onboardingDone = true;
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fpStore") >= 0)
        {
            // Store set: already-earned achievements, so no unlock toast covers the menu.
            foreach (var id in AchievementCatalog.Earned(ProgressStore.Snapshot(), ProgressStore.PlayableLevelCount))
                if (!d.achievements.Contains(id))
                    d.achievements.Add(id);
        }
        ProgressStore.Save();
        ProgressStore.CheckAchievements();
    }

    /// <summary>Simulated store returning the configured USD prices (screenshots only).</summary>
    sealed class QaPriceStore : IEntitlementStore
    {
        public void Connect() { }

        public string PriceFor(string productId)
        {
            switch (productId)
            {
                case LevelAccessPolicy.PlusYearlyProductId: return "$9.99";
                case LevelAccessPolicy.PlusMonthlyProductId: return "$1.99";
                case LevelAccessPolicy.FullGameProductId: return "$4.99";
                default: return null;
            }
        }

        public bool IsAvailable(string productId) => PriceFor(productId) != null;

        public void Purchase(string productId) => Entitlements.ReportPurchase(PurchaseOutcome.Cancelled, null);

        public void Restore(Action<bool, string> onDone) => onDone?.Invoke(true, null);
    }
}
