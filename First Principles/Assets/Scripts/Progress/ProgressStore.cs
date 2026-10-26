using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// -----------------------------------------------------------------------------
// ProgressStore — persistent player progress (stars, bests, stats, daily streak,
// hints, achievements, onboarding flags).
// -----------------------------------------------------------------------------
// Saved as JSON in Application.persistentDataPath/progress.json (atomic write).
// A file (not PlayerPrefs) so Steam Cloud can sync it (SteamBridge mirrors it with
// ISteamRemoteStorage; Auto-Cloud can also point at the same path). Merging a cloud
// copy keeps the best of both (max stars / stats), so progress never goes backwards.
// -----------------------------------------------------------------------------

[Serializable]
public class ProgressData
{
    public int version = 1;
    public int[] stars = new int[0];
    public int[] bestScore = new int[0];
    public int[] clears = new int[0];
    public float[] bestSeconds = new float[0];

    public int jumps;
    public int airJumps;
    public int falls;
    public int grazes;
    public int hintsUsedTotal;

    public string dailyLastKey = "";
    public int dailyStreak;
    public int dailyBestStreak;
    public int dailiesCompleted;
    public string dailyShieldKey = "";

    public string hintDayKey = "";
    public int hintsUsedThatDay;

    public List<string> achievements = new List<string>();

    public bool onboardingDone;
    /// <summary>Medals from optional map challenges (see <see cref="ChallengeRules.MedalId"/>).</summary>
    public List<string> medals = new List<string>();
    public bool mapTourDone;
    public bool levelTourDone;
    public bool sfxMuted;
    public long savedUnix;
}

public static class ProgressStore
{
    public const string FileName = "progress.json";

    static ProgressData _data;
    static bool _dirty;

    /// <summary>Raised after any change (stars, streak, achievements…).</summary>
    public static event Action Changed;

    /// <summary>Raised once per newly unlocked achievement (UI toast + Steam).</summary>
    public static event Action<AchievementDef> AchievementUnlocked;

    /// <summary>QA screenshot runs use their own file so they never touch real progress.</summary>
    public static string FilePath => Path.Combine(Application.persistentDataPath, QaShots.Enabled ? "progress-qa.json" : FileName);

    public static ProgressData Data
    {
        get
        {
            if (_data == null)
                Load();
            return _data;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _data = null;
        _dirty = false;
    }

    static void Load()
    {
        _data = null;
        try
        {
            if (File.Exists(FilePath))
                _data = JsonUtility.FromJson<ProgressData>(File.ReadAllText(FilePath));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ProgressStore: could not read progress ({e.Message}); starting fresh.");
        }
        _data ??= new ProgressData();
        EnsureArrays(_data, GameLevelCatalog.LevelCount);
    }

    static void EnsureArrays(ProgressData d, int n)
    {
        if (d.stars == null || d.stars.Length < n) Array.Resize(ref d.stars, n);
        if (d.bestScore == null || d.bestScore.Length < n) Array.Resize(ref d.bestScore, n);
        if (d.clears == null || d.clears.Length < n) Array.Resize(ref d.clears, n);
        if (d.bestSeconds == null || d.bestSeconds.Length < n) Array.Resize(ref d.bestSeconds, n);
        d.achievements ??= new List<string>();
        d.medals ??= new List<string>();
    }

    public static void Save()
    {
        var d = Data;
        d.savedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            string json = JsonUtility.ToJson(d);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(tmp, FilePath);
            _dirty = false;
            Saved?.Invoke(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ProgressStore: save failed ({e.Message}).");
        }
    }

    /// <summary>Raised with the JSON after each save (SteamBridge pushes it to Steam Cloud).</summary>
    public static event Action<string> Saved;

    static void Touch()
    {
        _dirty = true;
        Save();
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ queries

    public static string TodayKey => ProgressRules.DateKey(DateTime.Now);

    public static int StarsFor(int levelIndex)
    {
        var d = Data;
        return levelIndex >= 0 && levelIndex < d.stars.Length ? d.stars[levelIndex] : 0;
    }

    public static int BestScore(int levelIndex)
    {
        var d = Data;
        return levelIndex >= 0 && levelIndex < d.bestScore.Length ? d.bestScore[levelIndex] : 0;
    }

    public static int TotalStars
    {
        get
        {
            int t = 0;
            foreach (var s in Data.stars) t += s;
            return t;
        }
    }

    public static int StarsInRange(int first, int lastInclusive)
    {
        int t = 0;
        for (int i = first; i <= lastInclusive; i++)
            t += StarsFor(i);
        return t;
    }

    public static int ClearedInRange(int first, int lastInclusive)
    {
        int t = 0;
        for (int i = first; i <= lastInclusive; i++)
            if (StarsFor(i) > 0) t++;
        return t;
    }

    public static int PlayableLevelCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < GameLevelCatalog.LevelCount; i++)
                if (!GameLevelCatalog.IsComingSoonLevel(i)) n++;
            return n;
        }
    }

    public static bool HasAchievement(string id) => Data.achievements.Contains(id);

    public static ProgressSnapshot Snapshot()
    {
        var d = Data;
        var s = new ProgressSnapshot
        {
            Stars = (int[])d.stars.Clone(),
            AirJumps = d.airJumps,
            Falls = d.falls,
            DailiesCompleted = d.dailiesCompleted,
            BestDailyStreak = d.dailyBestStreak,
            HintsUsed = d.hintsUsedTotal,
        };
        foreach (var st in d.stars)
        {
            s.TotalStars += st;
            if (st > 0) s.LevelsCleared++;
            if (st >= 3) s.ThreeStarLevels++;
        }
        return s;
    }

    // ------------------------------------------------------------------ mutations

    public sealed class RecordOutcome
    {
        public int Stars;
        public int PreviousStars;
        public bool NewBestScore;
        public bool FirstClear;
        public bool DailyCompletedNow;
        public int DailyStreak;
    }

    /// <summary>Stores a cleared run; returns what changed (for the level-complete card).</summary>
    public static RecordOutcome RecordClear(RunResult r, DailyChallengeSpec? daily)
    {
        var d = Data;
        EnsureArrays(d, Mathf.Max(GameLevelCatalog.LevelCount, r.LevelIndex + 1));
        int i = r.LevelIndex;
        var o = new RecordOutcome
        {
            Stars = ProgressRules.StarsFor(r),
            PreviousStars = d.stars[i],
            FirstClear = d.clears[i] == 0,
        };
        o.NewBestScore = d.clears[i] > 0 && r.Score > d.bestScore[i];
        d.stars[i] = Mathf.Max(d.stars[i], o.Stars);
        d.bestScore[i] = Mathf.Max(d.bestScore[i], r.Score);
        d.clears[i]++;
        if (d.bestSeconds[i] <= 0f || r.Seconds < d.bestSeconds[i])
            d.bestSeconds[i] = r.Seconds;

        if (daily.HasValue && daily.Value.IsValid && daily.Value.LevelIndex == i
            && daily.Value.DateKey == TodayKey && d.dailyLastKey != TodayKey
            && ProgressRules.MeetsDailyGoal(daily.Value.Goal, r))
        {
            bool shield = Entitlements.HasPlus && ProgressRules.ShieldReady(d.dailyShieldKey, TodayKey);
            d.dailyStreak = ProgressRules.NextStreak(d.dailyLastKey, d.dailyStreak, TodayKey, shield, out bool usedShield);
            if (usedShield)
                d.dailyShieldKey = TodayKey;
            d.dailyLastKey = TodayKey;
            d.dailyBestStreak = Mathf.Max(d.dailyBestStreak, d.dailyStreak);
            d.dailiesCompleted++;
            o.DailyCompletedNow = true;
        }
        o.DailyStreak = CurrentDailyStreak;

        Touch();
        CheckAchievements();
        return o;
    }

    public static void AddJump(bool air)
    {
        var d = Data;
        d.jumps++;
        if (air)
        {
            d.airJumps++;
            if (d.airJumps == 50)
                CheckAchievements();
        }
        _dirty = true;
    }

    public static void AddFall()
    {
        Data.falls++;
        _dirty = true;
        if (Data.falls % 10 == 0)
            FlushIfDirty();
        if (Data.falls == 100)
            CheckAchievements();
    }

    public static void AddGraze()
    {
        Data.grazes++;
        _dirty = true;
    }

    public static void FlushIfDirty()
    {
        if (_dirty)
            Save();
    }

    public static int HintsLeftToday => ProgressRules.HintsLeft(Entitlements.HasPlus, Data.hintDayKey, Data.hintsUsedThatDay, TodayKey);

    /// <summary>Consumes one hint if the quota allows. Returns false when out of free hints.</summary>
    public static bool TryUseHint()
    {
        if (HintsLeftToday <= 0)
            return false;
        var d = Data;
        if (d.hintDayKey != TodayKey)
        {
            d.hintDayKey = TodayKey;
            d.hintsUsedThatDay = 0;
        }
        d.hintsUsedThatDay++;
        d.hintsUsedTotal++;
        Touch();
        CheckAchievements();
        return true;
    }

    public static int CurrentDailyStreak =>
        ProgressRules.DisplayStreak(Data.dailyLastKey, Data.dailyStreak, TodayKey,
            Entitlements.HasPlus && ProgressRules.ShieldReady(Data.dailyShieldKey, TodayKey));

    public static bool DailyDoneToday => Data.dailyLastKey == TodayKey;

    public static bool OnboardingDone
    {
        get => Data.onboardingDone;
        set
        {
            if (Data.onboardingDone == value) return;
            Data.onboardingDone = value;
            Touch();
        }
    }

    public static bool MapTourDone
    {
        get => Data.mapTourDone;
        set
        {
            if (Data.mapTourDone == value) return;
            Data.mapTourDone = value;
            Touch();
        }
    }

    public static bool LevelTourDone
    {
        get => Data.levelTourDone;
        set
        {
            if (Data.levelTourDone == value) return;
            Data.levelTourDone = value;
            Touch();
        }
    }

    // ------------------------------------------------------------------ medals

    public static bool HasMedal(string id) => !string.IsNullOrEmpty(id) && Data.medals.Contains(id);

    public static int MedalCount => Data.medals.Count;

    public static float BestSeconds(int levelIndex)
    {
        var b = Data.bestSeconds;
        return levelIndex >= 0 && levelIndex < b.Length ? b[levelIndex] : 0f;
    }

    /// <summary>Evaluates the active map challenge for a cleared run. Returns the medal id when newly earned.</summary>
    public static ChallengeOutcome RecordChallenge(RunResult r, ChallengeSpec? spec)
    {
        var o = new ChallengeOutcome();
        if (!spec.HasValue || spec.Value.Kind == ChallengeKind.None || spec.Value.LevelIndex != r.LevelIndex)
            return o;
        var c = spec.Value;
        o.Kind = c.Kind;
        o.Met = ChallengeRules.Meets(c.Kind, r, c.ParSeconds);
        o.ParSeconds = c.ParSeconds;
        string id = ChallengeRules.MedalId(c.Kind, c.LevelIndex, c.RegionKey);
        if (o.Met && id != null && !Data.medals.Contains(id))
        {
            Data.medals.Add(id);
            o.NewMedal = id;
            Touch();
        }
        return o;
    }

    public static bool SfxMuted
    {
        get => Data.sfxMuted;
        set
        {
            Data.sfxMuted = value;
            Touch();
        }
    }

    public static void CheckAchievements()
    {
        var earned = AchievementCatalog.Earned(Snapshot(), PlayableLevelCount);
        bool any = false;
        foreach (var id in earned)
        {
            if (Data.achievements.Contains(id))
                continue;
            Data.achievements.Add(id);
            any = true;
            var def = AchievementCatalog.Find(id);
            if (def != null)
                AchievementUnlocked?.Invoke(def);
        }
        if (any)
            Touch();
    }

    /// <summary>Merge a cloud copy (Steam) into local progress: never lose stars, stats or achievements.</summary>
    public static void MergeFromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return;
        ProgressData other;
        try { other = JsonUtility.FromJson<ProgressData>(json); }
        catch { return; }
        if (other == null)
            return;
        var d = Data;
        int n = Mathf.Max(GameLevelCatalog.LevelCount, other.stars?.Length ?? 0);
        EnsureArrays(d, n);
        EnsureArrays(other, n);
        for (int i = 0; i < n; i++)
        {
            d.stars[i] = Mathf.Max(d.stars[i], other.stars[i]);
            d.bestScore[i] = Mathf.Max(d.bestScore[i], other.bestScore[i]);
            d.clears[i] = Mathf.Max(d.clears[i], other.clears[i]);
            if (other.bestSeconds[i] > 0f && (d.bestSeconds[i] <= 0f || other.bestSeconds[i] < d.bestSeconds[i]))
                d.bestSeconds[i] = other.bestSeconds[i];
        }
        d.jumps = Mathf.Max(d.jumps, other.jumps);
        d.airJumps = Mathf.Max(d.airJumps, other.airJumps);
        d.falls = Mathf.Max(d.falls, other.falls);
        d.grazes = Mathf.Max(d.grazes, other.grazes);
        d.hintsUsedTotal = Mathf.Max(d.hintsUsedTotal, other.hintsUsedTotal);
        d.dailiesCompleted = Mathf.Max(d.dailiesCompleted, other.dailiesCompleted);
        d.dailyBestStreak = Mathf.Max(d.dailyBestStreak, other.dailyBestStreak);
        if (string.CompareOrdinal(other.dailyLastKey ?? "", d.dailyLastKey ?? "") > 0)
        {
            d.dailyLastKey = other.dailyLastKey;
            d.dailyStreak = other.dailyStreak;
        }
        foreach (var a in other.achievements)
            if (!d.achievements.Contains(a))
                d.achievements.Add(a);
        d.onboardingDone |= other.onboardingDone;
        if (other.medals != null)
            foreach (var m in other.medals)
                if (!d.medals.Contains(m))
                    d.medals.Add(m);
        d.mapTourDone |= other.mapTourDone;
        d.levelTourDone |= other.levelTourDone;
        Touch();
    }

    /// <summary>QA only: wipe local progress.</summary>
    public static void ResetForTesting()
    {
        _data = new ProgressData();
        EnsureArrays(_data, GameLevelCatalog.LevelCount);
        Touch();
    }

    // ------------------------------------------------------------------ daily

    /// <summary>Levels the player can open right now (pool for the daily challenge).</summary>
    public static List<int> UnlockedPlayableLevels()
    {
        var pool = new List<int>();
        for (int i = 0; i < GameLevelCatalog.LevelCount; i++)
            if (!GameLevelCatalog.IsComingSoonLevel(i) && Entitlements.IsLevelUnlocked(i))
                pool.Add(i);
        return pool;
    }

    public static DailyChallengeSpec TodaysChallenge() =>
        ProgressRules.DailyFor(TodayKey, UnlockedPlayableLevels());
}

/// <summary>Handoff Level Select → Game: "this run is today's daily challenge".</summary>
public static class DailyChallengeSession
{
    public static DailyChallengeSpec? Active { get; private set; }

    public static void Begin(DailyChallengeSpec spec) => Active = spec;

    public static void End() => Active = null;

    public static string GoalText(DailyGoal g)
    {
        switch (g)
        {
            case DailyGoal.ThreeStars: return LocalizationManager.Get("daily.goal.three_stars", "Clear it with 3 stars");
            case DailyGoal.NoFalls: return LocalizationManager.Get("daily.goal.no_falls", "Clear it without a single fall");
            default:
                return string.Format(LocalizationManager.Get("daily.goal.high_score", "Clear it with {0}+ points"), ProgressRules.DailyHighScore);
        }
    }
}

/// <summary>What a map challenge run asks for (set by the world map, read by the Game scene).</summary>
public readonly struct ChallengeSpec
{
    public readonly ChallengeKind Kind;
    public readonly int LevelIndex;
    public readonly int ParSeconds;
    public readonly string RegionKey;

    public ChallengeSpec(ChallengeKind kind, int levelIndex, int parSeconds = 0, string regionKey = null)
    {
        Kind = kind;
        LevelIndex = levelIndex;
        ParSeconds = parSeconds;
        RegionKey = regionKey;
    }
}

public sealed class ChallengeOutcome
{
    public ChallengeKind Kind;
    public bool Met;
    public int ParSeconds;
    public string NewMedal;
}

public static class ChallengeSession
{
    public static ChallengeSpec? Active { get; private set; }

    public static void Begin(ChallengeSpec spec) => Active = spec;

    public static void End() => Active = null;

    public static bool BlocksHints(int levelIndex) =>
        Active.HasValue && Active.Value.LevelIndex == levelIndex && ChallengeRules.NoHints(Active.Value.Kind);

    public static string Title(ChallengeKind k)
    {
        switch (k)
        {
            case ChallengeKind.Flawless: return LocalizationManager.Get("challenge.flawless", "Flawless run");
            case ChallengeKind.TimeTrial: return LocalizationManager.Get("challenge.time", "Time trial");
            case ChallengeKind.RegionTrial: return LocalizationManager.Get("challenge.trial", "Region trial");
            case ChallengeKind.GrandTrial: return LocalizationManager.Get("challenge.grand", "Grand trial");
            default: return "";
        }
    }

    public static string GoalText(ChallengeSpec c)
    {
        switch (c.Kind)
        {
            case ChallengeKind.TimeTrial:
                return string.Format(LocalizationManager.Get("challenge.time.goal", "Reach the exit in {0}s or less"), c.ParSeconds);
            case ChallengeKind.RegionTrial:
                return string.Format(LocalizationManager.Get("challenge.trial.goal", "No falls, no hints, {0}+ points"), ChallengeRules.TrialMinScore);
            default:
                return string.Format(LocalizationManager.Get("challenge.flawless.goal", "No falls, no hints, {0}+ points"), ChallengeRules.FlawlessMinScore);
        }
    }
}

/// <summary>Game → world map handoff: what just changed, so the map can celebrate it.</summary>
public static class MapReturn
{
    public static bool Pending { get; private set; }
    public static int LevelIndex { get; private set; }
    public static int PreviousStars { get; private set; }
    public static int Stars { get; private set; }
    public static bool FirstClear { get; private set; }
    public static string NewMedal { get; private set; }
    /// <summary>Medal count before this run (to spot a Grand trial gate opening).</summary>
    public static int PreviousMedalCount { get; private set; }

    public static void Set(int level, int prevStars, int stars, bool firstClear, string newMedal, int prevMedals)
    {
        // Keep the earliest "previous" state if several levels are cleared before returning to the map.
        if (!Pending)
        {
            PreviousMedalCount = prevMedals;
        }
        Pending = true;
        LevelIndex = level;
        PreviousStars = prevStars;
        Stars = stars;
        FirstClear = firstClear;
        NewMedal = newMedal;
    }

    public static void Clear() => Pending = false;
}
