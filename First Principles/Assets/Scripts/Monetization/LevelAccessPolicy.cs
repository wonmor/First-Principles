// -----------------------------------------------------------------------------
// LevelAccessPolicy — pure gating rules for the free tier vs the "Full Game" unlock
// -----------------------------------------------------------------------------
// No UnityEngine dependency so the rules are trivially unit-testable (see
// Assets/Editor/Tests/LevelAccessPolicyTests.cs). Runtime state (purchase cache,
// store, debug override) lives in Entitlements; this file only decides.
//
// Model: the first two level-select categories ("Core & series" + "Multivar &
// integration", level indices 0..13 = 14 levels, incl. the Riemann-sum stages)
// are free. Everything else (45 playable levels + future additions) is unlocked
// by ONE non-consumable product, FullGameProductId.
// -----------------------------------------------------------------------------

/// <summary>How a store-backed build should treat the paywall on a given runtime platform.</summary>
public enum EntitlementPlatformMode
{
    /// <summary>Build ships through a store with IAP (iOS / Android): levels past the free tier need the purchase.</summary>
    StoreGated,
    /// <summary>Editor: gated (so the paywall can be tested) but backed by Unity IAP's fake store.</summary>
    EditorGated,
    /// <summary>Desktop / other builds that are distributed as a full (paid or free) download: everything unlocked.</summary>
    AlwaysUnlocked,
}

/// <summary>Lock state for a level-select category row.</summary>
public enum CategoryLockState
{
    /// <summary>Every level in the category is part of the free tier.</summary>
    Free,
    /// <summary>At least one level is locked for this player.</summary>
    Locked,
    /// <summary>Category is premium but the player owns the full game.</summary>
    Unlocked,
}

public static class LevelAccessPolicy
{
    /// <summary>Store product id (identical on App Store Connect and Google Play Console).</summary>
    public const string FullGameProductId = "firstprinciples_full_unlock";

    /// <summary>Inclusive last free level index (0..13 = Core &amp; series + Multivar &amp; integration).</summary>
    public const int FreeLevelLastIndexInclusive = 13;

    /// <summary>Number of free levels (indices 0..<see cref="FreeLevelLastIndexInclusive"/>).</summary>
    public const int FreeLevelCount = FreeLevelLastIndexInclusive + 1;

    /// <summary>When true, installs that already had the free 1.x build (all levels open) keep everything.</summary>
    public const bool GrandfatherExistingInstalls = true;

    public static bool IsLevelFree(int levelIndex) =>
        levelIndex >= 0 && levelIndex <= FreeLevelLastIndexInclusive;

    /// <summary>True when the level can be played. Negative indices are never playable.</summary>
    public static bool IsLevelUnlocked(int levelIndex, bool hasFullGame)
    {
        if (levelIndex < 0)
            return false;
        return hasFullGame || IsLevelFree(levelIndex);
    }

    /// <summary>Category is free when every index in [first, last] is free.</summary>
    public static CategoryLockState GetCategoryState(int firstLevelIndex, int lastLevelIndexInclusive, bool hasFullGame)
    {
        bool allFree = IsLevelFree(firstLevelIndex) && IsLevelFree(lastLevelIndexInclusive);
        if (allFree)
            return CategoryLockState.Free;
        return hasFullGame ? CategoryLockState.Unlocked : CategoryLockState.Locked;
    }

    /// <summary>
    /// Which level the game should load after <paramref name="currentLevelIndex"/> is cleared.
    /// Returns -1 when the next level is locked (caller shows the paywall instead).
    /// Wraps to 0 after the last level (existing behaviour in LevelManager.AdvanceLevel).
    /// </summary>
    public static int NextPlayableLevel(int currentLevelIndex, int levelCount, bool hasFullGame)
    {
        if (levelCount <= 0)
            return -1;
        int next = currentLevelIndex + 1;
        if (next >= levelCount || next < 0)
            next = 0;
        return IsLevelUnlocked(next, hasFullGame) ? next : -1;
    }

    /// <summary>Count of levels in [0, levelCount) the player cannot open yet.</summary>
    public static int LockedLevelCount(int levelCount, bool hasFullGame)
    {
        if (hasFullGame || levelCount <= FreeLevelCount)
            return 0;
        return levelCount - FreeLevelCount;
    }

    /// <summary>
    /// Final entitlement decision. Any single source is enough: a platform that is never gated,
    /// the developer override, a purchase seen from the store (or cached from an earlier session),
    /// or a grandfathered pre-paywall install.
    /// </summary>
    public static bool ResolveHasFullGame(
        EntitlementPlatformMode platform,
        bool? debugOverride,
        bool purchasedOrCached,
        bool grandfathered)
    {
        if (debugOverride.HasValue)
            return debugOverride.Value;
        if (platform == EntitlementPlatformMode.AlwaysUnlocked)
            return true;
        return purchasedOrCached || grandfathered;
    }

    /// <summary>
    /// Pre-paywall builds (1.0–1.2) always wrote the <c>fp_language</c> PlayerPref on first launch.
    /// If it already exists the very first time the paywall build starts (and we have never stamped
    /// our own marker), the player installed the old free-everything build — keep their levels.
    /// </summary>
    public static bool ShouldGrandfather(bool isEditor, bool alreadyEvaluated, bool hadLegacyPrefs)
    {
        if (!GrandfatherExistingInstalls || isEditor || alreadyEvaluated)
            return false;
        return hadLegacyPrefs;
    }

    /// <summary>
    /// Platform policy. Only the mobile stores are wired to IAP; macOS/Windows/Linux builds are
    /// sold or distributed as complete downloads (not through a store with this IAP product),
    /// so they are never gated. Define FP_IAP_GATE_STANDALONE if a store-backed desktop SKU
    /// (e.g. Mac App Store) is added later.
    /// </summary>
    public static EntitlementPlatformMode ModeFor(bool isEditor, bool isIosLike, bool isAndroid, bool gateStandalone)
    {
        if (isEditor)
            return EntitlementPlatformMode.EditorGated;
        if (isIosLike || isAndroid)
            return EntitlementPlatformMode.StoreGated;
        return gateStandalone ? EntitlementPlatformMode.StoreGated : EntitlementPlatformMode.AlwaysUnlocked;
    }
}

/// <summary>
/// One-shot handoff Game → LevelSelect: "the player just cleared the free tier, show the paywall".
/// Same pattern as <see cref="LevelSelection"/>.
/// </summary>
public static class PaywallRequest
{
    static bool _pending;

    public static void Request() => _pending = true;

    public static bool Consume()
    {
        bool p = _pending;
        _pending = false;
        return p;
    }
}
