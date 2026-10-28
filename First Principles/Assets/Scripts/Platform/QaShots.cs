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
        Debug.Log($"QaShots: writing to {_dir} at {Screen.width}x{Screen.height} x{_scale}");

        SeedProgress();
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

    IEnumerator Shot(string name)
    {
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
