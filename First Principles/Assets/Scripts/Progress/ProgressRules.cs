using System;
using System.Collections.Generic;

// -----------------------------------------------------------------------------
// ProgressRules — pure, engine-free rules for stars, daily challenges, streaks,
// hint quotas and achievements. Unit-tested in Assets/Editor/Tests/ProgressRulesTests.cs.
// Runtime state lives in ProgressStore; this file only decides.
// -----------------------------------------------------------------------------

/// <summary>Outcome of one cleared run of a level.</summary>
public readonly struct RunResult
{
    public readonly int LevelIndex;
    /// <summary>Score at the exit (starts at 100, minus 5 per f′ graze).</summary>
    public readonly int Score;
    /// <summary>Falls / hazard deaths since the level was entered (restarts included).</summary>
    public readonly int Falls;
    public readonly float Seconds;
    public readonly int HintsUsed;

    public RunResult(int levelIndex, int score, int falls, float seconds, int hintsUsed = 0)
    {
        LevelIndex = levelIndex;
        Score = score;
        Falls = falls;
        Seconds = seconds;
        HintsUsed = hintsUsed;
    }
}

public enum DailyGoal
{
    /// <summary>Clear it with three stars.</summary>
    ThreeStars = 0,
    /// <summary>Clear it without falling once.</summary>
    NoFalls = 1,
    /// <summary>Clear it with at least <see cref="ProgressRules.DailyHighScore"/> points.</summary>
    HighScore = 2,
}

public readonly struct DailyChallengeSpec
{
    public readonly string DateKey;
    public readonly int LevelIndex;
    public readonly DailyGoal Goal;

    public DailyChallengeSpec(string dateKey, int levelIndex, DailyGoal goal)
    {
        DateKey = dateKey;
        LevelIndex = levelIndex;
        Goal = goal;
    }

    public bool IsValid => LevelIndex >= 0;
}

public static class ProgressRules
{
    public const int MaxStars = 3;
    /// <summary>Two stars: reach the exit with at most this many falls.</summary>
    public const int TwoStarMaxFalls = 1;
    /// <summary>Three stars: no falls and at least this score (100 − 5 per graze → up to 3 grazes).</summary>
    public const int ThreeStarMinScore = 85;
    public const int DailyHighScore = 95;
    /// <summary>Hints per day without First Principles Plus.</summary>
    public const int FreeHintsPerDay = 3;
    /// <summary>Plus: one missed day per rolling week is forgiven.</summary>
    public const int StreakShieldCooldownDays = 7;

    // ------------------------------------------------------------------ stars

    public static int StarsFor(int score, int falls)
    {
        int stars = 1;
        if (falls <= TwoStarMaxFalls)
            stars = 2;
        if (falls == 0 && score >= ThreeStarMinScore)
            stars = 3;
        return stars;
    }

    public static int StarsFor(RunResult r) => StarsFor(r.Score, r.Falls);

    // ------------------------------------------------------------------ daily challenge

    /// <summary>yyyyMMdd in the player's local calendar.</summary>
    public static string DateKey(DateTime localDate) => localDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

    public static bool TryParseDateKey(string key, out DateTime date) =>
        DateTime.TryParseExact(key ?? "", "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);

    /// <summary>Stable 32-bit FNV-1a so every platform picks the same daily level for a given pool.</summary>
    public static uint StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s ?? "")
            {
                h ^= c;
                h *= 16777619;
            }
            return h;
        }
    }

    /// <summary>
    /// Today's challenge. <paramref name="pool"/> is the list of level indices the player can open
    /// (free tier: the free levels; owners / Plus: every playable level). Deterministic per (date, pool).
    /// </summary>
    public static DailyChallengeSpec DailyFor(string dateKey, IList<int> pool)
    {
        if (pool == null || pool.Count == 0)
            return new DailyChallengeSpec(dateKey, -1, DailyGoal.ThreeStars);
        uint h = StableHash("fp-daily|" + dateKey);
        int level = pool[(int)(h % (uint)pool.Count)];
        var goal = (DailyGoal)((h >> 8) % 3u);
        return new DailyChallengeSpec(dateKey, level, goal);
    }

    public static bool MeetsDailyGoal(DailyGoal goal, RunResult r)
    {
        switch (goal)
        {
            case DailyGoal.ThreeStars: return StarsFor(r) >= 3;
            case DailyGoal.NoFalls: return r.Falls == 0;
            case DailyGoal.HighScore: return r.Score >= DailyHighScore;
            default: return false;
        }
    }

    /// <summary>
    /// New streak after completing the daily challenge on <paramref name="today"/>.
    /// <paramref name="shieldAvailable"/> (Plus) forgives exactly one missed day; <paramref name="usedShield"/> reports it.
    /// </summary>
    public static int NextStreak(string lastCompletedKey, int currentStreak, string todayKey, bool shieldAvailable, out bool usedShield)
    {
        usedShield = false;
        if (!TryParseDateKey(todayKey, out var today))
            return Math.Max(1, currentStreak);
        if (!TryParseDateKey(lastCompletedKey, out var last))
            return 1;
        int gap = (int)Math.Round((today.Date - last.Date).TotalDays);
        if (gap <= 0)
            return Math.Max(1, currentStreak); // already done today (or clock moved back)
        if (gap == 1)
            return currentStreak + 1;
        if (gap == 2 && shieldAvailable)
        {
            usedShield = true;
            return currentStreak + 1;
        }
        return 1;
    }

    /// <summary>Streak to display: it lapses once a full day passes without a completed daily (unless shield-saveable).</summary>
    public static int DisplayStreak(string lastCompletedKey, int streak, string todayKey, bool shieldAvailable)
    {
        if (!TryParseDateKey(todayKey, out var today) || !TryParseDateKey(lastCompletedKey, out var last))
            return 0;
        int gap = (int)Math.Round((today.Date - last.Date).TotalDays);
        if (gap <= 1)
            return streak;
        if (gap == 2 && shieldAvailable)
            return streak;
        return 0;
    }

    public static bool ShieldReady(string lastShieldKey, string todayKey)
    {
        if (!TryParseDateKey(lastShieldKey, out var last) || !TryParseDateKey(todayKey, out var today))
            return true;
        return (today.Date - last.Date).TotalDays >= StreakShieldCooldownDays;
    }

    // ------------------------------------------------------------------ hints

    /// <summary>Hints left today. Plus = unlimited (int.MaxValue).</summary>
    public static int HintsLeft(bool hasPlus, string hintDayKey, int hintsUsedThatDay, string todayKey)
    {
        if (hasPlus)
            return int.MaxValue;
        int used = hintDayKey == todayKey ? hintsUsedThatDay : 0;
        return Math.Max(0, FreeHintsPerDay - used);
    }
}

// -----------------------------------------------------------------------------
// Achievements — ids double as Steamworks achievement API names (see STEAM_RELEASE.md).
// -----------------------------------------------------------------------------

public sealed class AchievementDef
{
    public readonly string Id;
    public readonly string Title;
    public readonly string Description;

    public AchievementDef(string id, string title, string description)
    {
        Id = id;
        Title = title;
        Description = description;
    }
}

/// <summary>Snapshot of lifetime stats an achievement check needs (built from ProgressStore).</summary>
public sealed class ProgressSnapshot
{
    public int[] Stars = new int[0];
    public int TotalStars;
    public int LevelsCleared;
    public int ThreeStarLevels;
    public int AirJumps;
    public int Falls;
    public int DailiesCompleted;
    public int BestDailyStreak;
    public int HintsUsed;

    public bool Cleared(int levelIndex) => levelIndex >= 0 && levelIndex < Stars.Length && Stars[levelIndex] > 0;

    public bool ClearedRange(int first, int lastInclusive)
    {
        for (int i = first; i <= lastInclusive; i++)
            if (!Cleared(i))
                return false;
        return true;
    }
}

public static class AchievementCatalog
{
    // Level index ranges (must match GameLevelCatalog.SelectCategories).
    const int CoreFirst = 0, CoreLast = 8;
    const int RiemannFirst = 10, RiemannLast = 13;
    const int AeroFirst = 35, AeroLast = 41;
    const int BossFirst = 46, BossLast = 49;
    const int BigOFirst = 51, BigOLast = 58;

    public static readonly AchievementDef[] All =
    {
        new AchievementDef("FP_FIRST_CLEAR", "First principles", "Clear your first level."),
        new AchievementDef("FP_THREE_STARS", "Flawless derivative", "Earn three stars on any level."),
        new AchievementDef("FP_CORE_DONE", "Series complete", "Clear every Core & series level."),
        new AchievementDef("FP_RIEMANN", "Riemann rider", "Clear all four area and Riemann-sum levels."),
        new AchievementDef("FP_STARS_25", "Constellation", "Collect 25 stars."),
        new AchievementDef("FP_STARS_75", "Star chart", "Collect 75 stars."),
        new AchievementDef("FP_PERFECT_10", "Perfectionist", "Three-star 10 different levels."),
        new AchievementDef("FP_AIR_50", "Ride the slope", "Double-jump off the f′ line 50 times."),
        new AchievementDef("FP_FALLS_100", "Limits exist", "Fall 100 times and keep going."),
        new AchievementDef("FP_DAILY_1", "Daily practice", "Complete a daily challenge."),
        new AchievementDef("FP_DAILY_7", "Seven-day streak", "Complete daily challenges 7 days in a row."),
        new AchievementDef("FP_HINT_1", "Ask the tutor", "Use your first hint."),
        new AchievementDef("FP_AERO", "Cleared for takeoff", "Clear every Aerospace level."),
        new AchievementDef("FP_BIG_O", "Asymptotically optimal", "Clear every Big O level."),
        new AchievementDef("FP_BOSSES", "Final boss", "Clear all four final boss levels."),
        new AchievementDef("FP_ALL", "Q.E.D.", "Clear every level in the game."),
    };

    public static AchievementDef Find(string id)
    {
        foreach (var a in All)
            if (a.Id == id)
                return a;
        return null;
    }

    /// <summary>Ids whose conditions hold for <paramref name="s"/> (caller filters already-unlocked ones).</summary>
    public static List<string> Earned(ProgressSnapshot s, int playableLevelCount)
    {
        var o = new List<string>();
        if (s == null)
            return o;
        if (s.LevelsCleared >= 1) o.Add("FP_FIRST_CLEAR");
        if (s.ThreeStarLevels >= 1) o.Add("FP_THREE_STARS");
        if (s.ClearedRange(CoreFirst, CoreLast)) o.Add("FP_CORE_DONE");
        if (s.ClearedRange(RiemannFirst, RiemannLast)) o.Add("FP_RIEMANN");
        if (s.TotalStars >= 25) o.Add("FP_STARS_25");
        if (s.TotalStars >= 75) o.Add("FP_STARS_75");
        if (s.ThreeStarLevels >= 10) o.Add("FP_PERFECT_10");
        if (s.AirJumps >= 50) o.Add("FP_AIR_50");
        if (s.Falls >= 100) o.Add("FP_FALLS_100");
        if (s.DailiesCompleted >= 1) o.Add("FP_DAILY_1");
        if (s.BestDailyStreak >= 7) o.Add("FP_DAILY_7");
        if (s.HintsUsed >= 1) o.Add("FP_HINT_1");
        if (s.ClearedRange(AeroFirst, AeroLast)) o.Add("FP_AERO");
        if (s.ClearedRange(BigOFirst, BigOLast)) o.Add("FP_BIG_O");
        if (s.ClearedRange(BossFirst, BossLast)) o.Add("FP_BOSSES");
        if (playableLevelCount > 0 && s.LevelsCleared >= playableLevelCount) o.Add("FP_ALL");
        return o;
    }
}
