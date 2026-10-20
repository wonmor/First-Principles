using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// -----------------------------------------------------------------------------
// Entitlements — single source of truth for "does this player own the full game?"
// -----------------------------------------------------------------------------
// Sources (any one unlocks; see LevelAccessPolicy.ResolveHasFullGame):
//   1. Platform: desktop / non-store builds are always unlocked.
//   2. Store: Unity IAP (StoreKit 2 / Play Billing) reports the non-consumable as owned
//      at startup (FetchPurchases), after a purchase, or after Restore Purchases.
//   3. Local cache: a device-bound token in PlayerPrefs so the game opens unlocked
//      offline / before the store answers.
//   4. Grandfathering: installs that ran the pre-paywall free build keep everything.
//   5. Debug override (Editor + Development builds only): Tools ▸ First Principles ▸ IAP,
//      or the hidden 7-tap on the paywall title. FP_UNLOCK_ALL scripting define forces unlock.
// -----------------------------------------------------------------------------

public enum StoreAvailability
{
    /// <summary>Platform never gates (desktop) — no store is contacted.</summary>
    NotRequired,
    Connecting,
    Ready,
    /// <summary>Store could not be reached / product missing. Purchases are disabled; cached ownership still applies.</summary>
    Unavailable,
}

public enum PurchaseOutcome
{
    Succeeded,
    Cancelled,
    /// <summary>Ask to Buy / pending payment — will complete later via the store callback.</summary>
    Deferred,
    Failed,
}

/// <summary>Store-facing operations Entitlements needs; implemented by <see cref="UnityIapEntitlementStore"/>.</summary>
public interface IEntitlementStore
{
    void Connect();
    string LocalizedPrice { get; }
    void PurchaseFullGame();
    void Restore(Action<bool, string> onDone);
}

public static class Entitlements
{
    const string CacheKey = "fp_ent_full";
    const string DebugOverrideKey = "fp_ent_debug";
    const string GrandfatherKey = "fp_ent_legacy";
    const string EvaluatedMarkerKey = "fp_ent_seen";
    const string CacheSalt = "FirstPrinciples.Entitlements.v1";

    static bool _initialized;
    static bool _hadLegacyPrefsAtBoot;
    static bool _purchasedOrCached;
    static bool _grandfathered;
    static IEntitlementStore _store;

    public static EntitlementPlatformMode Mode { get; private set; } = EntitlementPlatformMode.EditorGated;
    public static StoreAvailability Availability { get; private set; } = StoreAvailability.NotRequired;

    /// <summary>Raised on the main thread whenever ownership, price or store availability changes.</summary>
    public static event Action Changed;

    /// <summary>Raised after a purchase attempt from the paywall finishes.</summary>
    public static event Action<PurchaseOutcome, string> PurchaseFinished;

    public static bool HasFullGame
    {
        get
        {
            EnsureInitialized();
#if FP_UNLOCK_ALL
            return true;
#else
            return LevelAccessPolicy.ResolveHasFullGame(Mode, DebugOverride, _purchasedOrCached, _grandfathered);
#endif
        }
    }

    /// <summary>Localized store price (e.g. "$4.99", "₩6,600") or null until the store answers.</summary>
    public static string LocalizedPrice => _store?.LocalizedPrice;

    public static bool CanPurchase => Availability == StoreAvailability.Ready && !HasFullGame;

    public static bool IsLevelUnlocked(int levelIndex) =>
        LevelAccessPolicy.IsLevelUnlocked(levelIndex, HasFullGame);

    public static CategoryLockState GetCategoryState(LevelSelectCategory cat) =>
        LevelAccessPolicy.GetCategoryState(cat.FirstLevelIndex, cat.LastLevelIndexInclusive, HasFullGame);

    // ---------------------------------------------------------------- boot

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void SnapshotLegacyInstall()
    {
        // Runs before LocalizationManager.Boot (BeforeSceneLoad) writes fp_language on a fresh
        // install, so seeing it here means an older build already ran on this device.
        _initialized = false;
        _store = null;
        _hadLegacyPrefsAtBoot = PlayerPrefs.HasKey(LocalizationManager.PlayerPrefsKey);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() => EnsureInitialized();

    static void EnsureInitialized()
    {
        if (_initialized)
            return;
        _initialized = true;

        bool gateStandalone =
#if FP_IAP_GATE_STANDALONE
            true;
#else
            false;
#endif
        bool iosLike = Application.platform == RuntimePlatform.IPhonePlayer
                       || Application.platform == RuntimePlatform.tvOS
                       || Application.platform == RuntimePlatform.VisionOS;
        Mode = LevelAccessPolicy.ModeFor(
            Application.isEditor,
            iosLike,
            Application.platform == RuntimePlatform.Android,
            gateStandalone);

        _purchasedOrCached = PlayerPrefs.GetString(CacheKey, "") == ExpectedCacheToken();

        bool evaluated = PlayerPrefs.GetInt(EvaluatedMarkerKey, 0) == 1;
        if (LevelAccessPolicy.ShouldGrandfather(Application.isEditor, evaluated, _hadLegacyPrefsAtBoot))
            PlayerPrefs.SetInt(GrandfatherKey, 1);
        if (!evaluated)
        {
            PlayerPrefs.SetInt(EvaluatedMarkerKey, 1);
            PlayerPrefs.Save();
        }
        _grandfathered = !Application.isEditor && PlayerPrefs.GetInt(GrandfatherKey, 0) == 1;

        if (Mode == EntitlementPlatformMode.AlwaysUnlocked)
        {
            Availability = StoreAvailability.NotRequired;
            return;
        }

        Availability = StoreAvailability.Connecting;
        try
        {
            _store = new UnityIapEntitlementStore();
            _store.Connect();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Entitlements: store init failed — purchases disabled. {e.Message}");
            Availability = StoreAvailability.Unavailable;
        }
    }

    // ---------------------------------------------------------------- store callbacks (main thread)

    internal static void OnStoreReady()
    {
        Availability = StoreAvailability.Ready;
        RaiseChanged();
    }

    internal static void OnStoreUnavailable(string reason)
    {
        Debug.LogWarning($"Entitlements: store unavailable ({reason}). Cached ownership still applies.");
        Availability = StoreAvailability.Unavailable;
        RaiseChanged();
    }

    /// <summary>The store reports the full-game product as owned (purchase, restore or startup fetch).</summary>
    internal static void GrantFullGame()
    {
        bool before = HasFullGame;
        _purchasedOrCached = true;
        PlayerPrefs.SetString(CacheKey, ExpectedCacheToken());
        PlayerPrefs.Save();
        if (!before)
            Debug.Log("Entitlements: full game unlocked.");
        RaiseChanged();
    }

    internal static void ReportPurchase(PurchaseOutcome outcome, string message)
    {
        PurchaseFinished?.Invoke(outcome, message);
    }

    static void RaiseChanged() => Changed?.Invoke();

    // ---------------------------------------------------------------- UI entry points

    public static void BuyFullGame()
    {
        EnsureInitialized();
        if (HasFullGame)
        {
            ReportPurchase(PurchaseOutcome.Succeeded, null);
            return;
        }
        if (_store == null || Availability != StoreAvailability.Ready)
        {
            ReportPurchase(PurchaseOutcome.Failed, "store_unavailable");
            return;
        }
        _store.PurchaseFullGame();
    }

    /// <summary>Restore Purchases (required by App Review for non-consumables).</summary>
    public static void RestorePurchases(Action<bool, string> onDone)
    {
        EnsureInitialized();
        if (_store == null)
        {
            onDone?.Invoke(HasFullGame, HasFullGame ? null : "store_unavailable");
            return;
        }
        _store.Restore((ok, err) => onDone?.Invoke(ok, err));
    }

    /// <summary>Re-attempt the store connection (e.g. paywall opened after an offline launch).</summary>
    public static void RetryConnectIfUnavailable()
    {
        EnsureInitialized();
        if (_store != null && Availability == StoreAvailability.Unavailable)
        {
            Availability = StoreAvailability.Connecting;
            RaiseChanged();
            _store.Connect();
        }
    }

    // ---------------------------------------------------------------- debug

    /// <summary>
    /// Developer override: true = unlocked, false = locked (ignores purchase/grandfather), null = normal.
    /// Only honoured in the Editor and Development builds; release builds always return null.
    /// </summary>
    public static bool? DebugOverride
    {
        get
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
                return null;
            int v = PlayerPrefs.GetInt(DebugOverrideKey, -1);
            return v < 0 ? (bool?)null : v == 1;
        }
        set
        {
            if (value.HasValue)
                PlayerPrefs.SetInt(DebugOverrideKey, value.Value ? 1 : 0);
            else
                PlayerPrefs.DeleteKey(DebugOverrideKey);
            PlayerPrefs.Save();
            RaiseChanged();
        }
    }

    /// <summary>Forget the cached purchase + grandfather flag (debug / QA). The store re-grants on next fetch if owned.</summary>
    public static void ClearLocalCacheForTesting()
    {
        PlayerPrefs.DeleteKey(CacheKey);
        PlayerPrefs.DeleteKey(GrandfatherKey);
        PlayerPrefs.DeleteKey(EvaluatedMarkerKey);
        PlayerPrefs.Save();
        _purchasedOrCached = false;
        _grandfathered = false;
        RaiseChanged();
    }

    /// <summary>Device-bound token so copying a prefs file between devices does not carry the unlock.</summary>
    internal static string ExpectedCacheToken() => CacheTokenFor(SystemInfo.deviceUniqueIdentifier);

    public static string CacheTokenFor(string deviceId)
    {
        using (var sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(
                CacheSalt + "|" + LevelAccessPolicy.FullGameProductId + "|" + (deviceId ?? "")));
            var sb = new StringBuilder(32);
            for (int i = 0; i < 16; i++)
                sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
