using NUnit.Framework;

/// <summary>EditMode tests for the free-tier / full-game gating rules (Window ▸ General ▸ Test Runner ▸ EditMode).</summary>
public class LevelAccessPolicyTests
{
    [Test]
    public void FreeTier_IsFirstTwoCategories()
    {
        var core = GameLevelCatalog.SelectCategories[0];
        var integration = GameLevelCatalog.SelectCategories[1];
        Assert.AreEqual(0, core.FirstLevelIndex);
        Assert.AreEqual(core.LastLevelIndexInclusive + 1, integration.FirstLevelIndex);
        Assert.AreEqual(LevelAccessPolicy.FreeLevelLastIndexInclusive, integration.LastLevelIndexInclusive,
            "Free tier should end exactly at the end of the 'Multivar & integration' category.");
        Assert.AreEqual(14, LevelAccessPolicy.FreeLevelCount);
    }

    [Test]
    public void LevelsUpToFreeEnd_AreUnlockedWithoutPurchase()
    {
        for (int i = 0; i <= LevelAccessPolicy.FreeLevelLastIndexInclusive; i++)
            Assert.IsTrue(LevelAccessPolicy.IsLevelUnlocked(i, hasFullGame: false), $"level {i}");
    }

    [Test]
    public void PremiumLevels_LockedWithoutPurchase_UnlockedWithPurchase()
    {
        for (int i = LevelAccessPolicy.FreeLevelCount; i < GameLevelCatalog.LevelCount; i++)
        {
            Assert.IsFalse(LevelAccessPolicy.IsLevelUnlocked(i, hasFullGame: false), $"level {i} should be locked");
            Assert.IsTrue(LevelAccessPolicy.IsLevelUnlocked(i, hasFullGame: true), $"level {i} should be unlocked");
        }
    }

    [Test]
    public void NegativeIndex_NeverUnlocked()
    {
        Assert.IsFalse(LevelAccessPolicy.IsLevelUnlocked(-1, false));
        Assert.IsFalse(LevelAccessPolicy.IsLevelUnlocked(-1, true));
    }

    [Test]
    public void CategoryStates_MatchCatalog()
    {
        foreach (var cat in GameLevelCatalog.SelectCategories)
        {
            bool free = cat.LastLevelIndexInclusive <= LevelAccessPolicy.FreeLevelLastIndexInclusive;
            var locked = LevelAccessPolicy.GetCategoryState(cat.FirstLevelIndex, cat.LastLevelIndexInclusive, false);
            var owned = LevelAccessPolicy.GetCategoryState(cat.FirstLevelIndex, cat.LastLevelIndexInclusive, true);
            Assert.AreEqual(free ? CategoryLockState.Free : CategoryLockState.Locked, locked, cat.DefaultTitle);
            Assert.AreEqual(free ? CategoryLockState.Free : CategoryLockState.Unlocked, owned, cat.DefaultTitle);
        }
    }

    [Test]
    public void NoCategory_StraddlesTheFreeBoundary()
    {
        foreach (var cat in GameLevelCatalog.SelectCategories)
        {
            bool firstFree = LevelAccessPolicy.IsLevelFree(cat.FirstLevelIndex);
            bool lastFree = LevelAccessPolicy.IsLevelFree(cat.LastLevelIndexInclusive);
            Assert.AreEqual(firstFree, lastFree, $"'{cat.DefaultTitle}' is half free / half locked");
        }
    }

    [Test]
    public void NextPlayableLevel_StopsAtPaywall()
    {
        const int count = 59;
        Assert.AreEqual(1, LevelAccessPolicy.NextPlayableLevel(0, count, false));
        Assert.AreEqual(13, LevelAccessPolicy.NextPlayableLevel(12, count, false));
        Assert.AreEqual(-1, LevelAccessPolicy.NextPlayableLevel(13, count, false), "after the last free level → paywall");
        Assert.AreEqual(14, LevelAccessPolicy.NextPlayableLevel(13, count, true));
        Assert.AreEqual(0, LevelAccessPolicy.NextPlayableLevel(58, count, true), "wraps after the last level");
        Assert.AreEqual(-1, LevelAccessPolicy.NextPlayableLevel(0, 0, true));
    }

    [Test]
    public void LockedLevelCount()
    {
        Assert.AreEqual(45, LevelAccessPolicy.LockedLevelCount(59, false));
        Assert.AreEqual(0, LevelAccessPolicy.LockedLevelCount(59, true));
        Assert.AreEqual(0, LevelAccessPolicy.LockedLevelCount(10, false));
    }

    [Test]
    public void Resolve_DesktopAlwaysUnlocked()
    {
        Assert.IsTrue(LevelAccessPolicy.ResolveHasFullGame(EntitlementPlatformMode.AlwaysUnlocked, null, false, false));
    }

    [Test]
    public void Resolve_StoreGated_NeedsPurchaseOrGrandfather()
    {
        var m = EntitlementPlatformMode.StoreGated;
        Assert.IsFalse(LevelAccessPolicy.ResolveHasFullGame(m, null, false, false));
        Assert.IsTrue(LevelAccessPolicy.ResolveHasFullGame(m, null, true, false));
        Assert.IsTrue(LevelAccessPolicy.ResolveHasFullGame(m, null, false, true));
    }

    [Test]
    public void Resolve_DebugOverrideWins()
    {
        Assert.IsTrue(LevelAccessPolicy.ResolveHasFullGame(EntitlementPlatformMode.StoreGated, true, false, false));
        Assert.IsFalse(LevelAccessPolicy.ResolveHasFullGame(EntitlementPlatformMode.StoreGated, false, true, true));
        Assert.IsFalse(LevelAccessPolicy.ResolveHasFullGame(EntitlementPlatformMode.AlwaysUnlocked, false, false, false));
    }

    [Test]
    public void PlatformModes()
    {
        Assert.AreEqual(EntitlementPlatformMode.EditorGated, LevelAccessPolicy.ModeFor(true, false, false, false));
        Assert.AreEqual(EntitlementPlatformMode.StoreGated, LevelAccessPolicy.ModeFor(false, true, false, false));
        Assert.AreEqual(EntitlementPlatformMode.StoreGated, LevelAccessPolicy.ModeFor(false, false, true, false));
        Assert.AreEqual(EntitlementPlatformMode.AlwaysUnlocked, LevelAccessPolicy.ModeFor(false, false, false, false));
        Assert.AreEqual(EntitlementPlatformMode.StoreGated, LevelAccessPolicy.ModeFor(false, false, false, true));
    }

    [Test]
    public void Grandfathering()
    {
        Assert.IsTrue(LevelAccessPolicy.ShouldGrandfather(isEditor: false, alreadyEvaluated: false, hadLegacyPrefs: true));
        Assert.IsFalse(LevelAccessPolicy.ShouldGrandfather(false, false, false), "fresh install");
        Assert.IsFalse(LevelAccessPolicy.ShouldGrandfather(false, true, true), "only on the first paywall-build launch");
        Assert.IsFalse(LevelAccessPolicy.ShouldGrandfather(true, false, true), "never in the Editor");
    }

    [Test]
    public void CacheToken_IsDeviceBound_AndStable()
    {
        string a1 = Entitlements.CacheTokenFor("device-A");
        string a2 = Entitlements.CacheTokenFor("device-A");
        string b = Entitlements.CacheTokenFor("device-B");
        Assert.AreEqual(a1, a2);
        Assert.AreNotEqual(a1, b);
        Assert.AreEqual(32, a1.Length);
    }

    [Test]
    public void PaywallRequest_IsOneShot()
    {
        PaywallRequest.Consume();
        Assert.IsFalse(PaywallRequest.Consume());
        PaywallRequest.Request();
        Assert.IsTrue(PaywallRequest.Consume());
        Assert.IsFalse(PaywallRequest.Consume());
    }

    [Test]
    public void ProductId_IsStable()
    {
        Assert.AreEqual("firstprinciples_full_unlock", LevelAccessPolicy.FullGameProductId);
    }
}
