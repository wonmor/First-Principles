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
            if (!Debug.isDebugBuild && !Application.isEditor)
                return false;
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

        yield return new WaitForSecondsRealtime(4f);
        yield return Shot("menu");

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
            MapLevelSheet.Open(canvas.transform, 2, ChallengeKind.None, "core", new Color(0.22f, 0.66f, 0.62f), null, null, null, null);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("level-sheet");
            MapLevelSheet.Close();

            map.ScrollToLevel(8, true);
            bool open = map.IsTrialOpenForRegionEnding(8, out string key, out string req);
            MapLevelSheet.Open(canvas.transform, 8, ChallengeKind.RegionTrial, key, new Color(0.22f, 0.66f, 0.62f), () => open, req, null, null);
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

        AchievementsOverlay.Open(canvas.transform);
        yield return new WaitForSecondsRealtime(1f);
        yield return Shot("achievements");
        AchievementsOverlay.Close();

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

        Debug.Log("QaShots: done");
        Application.Quit();
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
