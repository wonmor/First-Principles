using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>EditMode tests for stars, daily challenges, streaks, hint quotas and achievements.</summary>
public class ProgressRulesTests
{
    [Test]
    public void Stars_FollowPublishedRules()
    {
        Assert.AreEqual(3, ProgressRules.StarsFor(100, 0));
        Assert.AreEqual(3, ProgressRules.StarsFor(85, 0));
        Assert.AreEqual(2, ProgressRules.StarsFor(80, 0), "no falls but too many grazes");
        Assert.AreEqual(2, ProgressRules.StarsFor(100, 1));
        Assert.AreEqual(1, ProgressRules.StarsFor(100, 2));
        Assert.AreEqual(1, ProgressRules.StarsFor(0, 9));
    }

    [Test]
    public void Daily_IsDeterministicAndInPool()
    {
        var pool = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 };
        var a = ProgressRules.DailyFor("20261025", pool);
        var b = ProgressRules.DailyFor("20261025", pool);
        Assert.AreEqual(a.LevelIndex, b.LevelIndex);
        Assert.AreEqual(a.Goal, b.Goal);
        Assert.Contains(a.LevelIndex, pool);
        var seen = new HashSet<int>();
        var start = new DateTime(2026, 1, 1);
        for (int d = 0; d < 60; d++)
            seen.Add(ProgressRules.DailyFor(ProgressRules.DateKey(start.AddDays(d)), pool).LevelIndex);
        Assert.Greater(seen.Count, 6, "daily level should vary across days");
        Assert.IsFalse(ProgressRules.DailyFor("20261025", new List<int>()).IsValid);
    }

    [Test]
    public void Daily_Goals()
    {
        Assert.IsTrue(ProgressRules.MeetsDailyGoal(DailyGoal.NoFalls, new RunResult(0, 50, 0, 10f)));
        Assert.IsFalse(ProgressRules.MeetsDailyGoal(DailyGoal.NoFalls, new RunResult(0, 100, 1, 10f)));
        Assert.IsTrue(ProgressRules.MeetsDailyGoal(DailyGoal.HighScore, new RunResult(0, 95, 3, 10f)));
        Assert.IsFalse(ProgressRules.MeetsDailyGoal(DailyGoal.HighScore, new RunResult(0, 90, 0, 10f)));
        Assert.IsTrue(ProgressRules.MeetsDailyGoal(DailyGoal.ThreeStars, new RunResult(0, 90, 0, 10f)));
    }

    [Test]
    public void Streak_ContinuesBreaksAndShields()
    {
        Assert.AreEqual(1, ProgressRules.NextStreak("", 0, "20261025", false, out _));
        Assert.AreEqual(5, ProgressRules.NextStreak("20261024", 4, "20261025", false, out _));
        Assert.AreEqual(4, ProgressRules.NextStreak("20261025", 4, "20261025", false, out _), "same day");
        Assert.AreEqual(1, ProgressRules.NextStreak("20261023", 4, "20261025", false, out _), "missed a day");
        Assert.AreEqual(5, ProgressRules.NextStreak("20261023", 4, "20261025", true, out bool used));
        Assert.IsTrue(used);
        Assert.AreEqual(1, ProgressRules.NextStreak("20261020", 4, "20261025", true, out _), "shield covers one day only");
        Assert.AreEqual(1, ProgressRules.NextStreak("20261231", 9, "20270101", false, out _) - 9, "year boundary continues");
    }

    [Test]
    public void DisplayStreak_Lapses()
    {
        Assert.AreEqual(4, ProgressRules.DisplayStreak("20261024", 4, "20261025", false));
        Assert.AreEqual(0, ProgressRules.DisplayStreak("20261023", 4, "20261025", false));
        Assert.AreEqual(4, ProgressRules.DisplayStreak("20261023", 4, "20261025", true));
        Assert.IsTrue(ProgressRules.ShieldReady("", "20261025"));
        Assert.IsFalse(ProgressRules.ShieldReady("20261022", "20261025"));
        Assert.IsTrue(ProgressRules.ShieldReady("20261018", "20261025"));
    }

    [Test]
    public void Hints_FreeQuotaResetsDaily_PlusUnlimited()
    {
        Assert.AreEqual(3, ProgressRules.HintsLeft(false, "", 0, "20261025"));
        Assert.AreEqual(1, ProgressRules.HintsLeft(false, "20261025", 2, "20261025"));
        Assert.AreEqual(0, ProgressRules.HintsLeft(false, "20261025", 5, "20261025"));
        Assert.AreEqual(3, ProgressRules.HintsLeft(false, "20261024", 3, "20261025"), "refills the next day");
        Assert.AreEqual(int.MaxValue, ProgressRules.HintsLeft(true, "20261025", 99, "20261025"));
    }

    [Test]
    public void Achievements_Unlock()
    {
        var s = new ProgressSnapshot { Stars = new int[60] };
        Assert.IsEmpty(AchievementCatalog.Earned(s, 59));
        for (int i = 0; i <= 8; i++) s.Stars[i] = 3;
        s.LevelsCleared = 9;
        s.ThreeStarLevels = 9;
        s.TotalStars = 27;
        var e = AchievementCatalog.Earned(s, 59);
        CollectionAssert.Contains(e, "FP_FIRST_CLEAR");
        CollectionAssert.Contains(e, "FP_THREE_STARS");
        CollectionAssert.Contains(e, "FP_CORE_DONE");
        CollectionAssert.Contains(e, "FP_STARS_25");
        CollectionAssert.DoesNotContain(e, "FP_PERFECT_10");
        CollectionAssert.DoesNotContain(e, "FP_ALL");
    }

    [Test]
    public void Achievement_Ids_AreUniqueSteamApiNames()
    {
        var ids = new HashSet<string>();
        foreach (var a in AchievementCatalog.All)
        {
            Assert.IsTrue(ids.Add(a.Id), a.Id);
            StringAssert.IsMatch("^[A-Z0-9_]+$", a.Id);
        }
    }

    [Test]
    public void PlusProducts_AreDistinctFromFullGame()
    {
        Assert.IsTrue(LevelAccessPolicy.IsPlusProduct(LevelAccessPolicy.PlusMonthlyProductId));
        Assert.IsTrue(LevelAccessPolicy.IsPlusProduct(LevelAccessPolicy.PlusYearlyProductId));
        Assert.IsFalse(LevelAccessPolicy.IsPlusProduct(LevelAccessPolicy.FullGameProductId));
    }

    [Test]
    public void FullGameOwner_KeepsEverything_EvenWithoutPlus()
    {
        // Full Game purchase (or grandfathered install) alone unlocks every level on store builds.
        Assert.IsTrue(LevelAccessPolicy.ResolveHasFullGame(EntitlementPlatformMode.StoreGated, null, true, false));
        Assert.IsTrue(LevelAccessPolicy.ResolveHasFullGame(EntitlementPlatformMode.StoreGated, null, false, true));
        Assert.IsFalse(LevelAccessPolicy.ResolveHasFullGame(EntitlementPlatformMode.StoreGated, null, false, false));
    }
}
