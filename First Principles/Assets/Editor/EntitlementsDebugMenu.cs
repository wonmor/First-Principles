using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor shortcuts for testing the paywall. Values are stored in the Editor's PlayerPrefs,
/// so they never ship in a build. (Development builds: tap the paywall title 7× instead.)
/// </summary>
public static class EntitlementsDebugMenu
{
    const string Root = "Tools/First Principles/IAP/";

    [MenuItem(Root + "Debug override: Unlock everything")]
    static void ForceUnlock() => Set(true);

    [MenuItem(Root + "Debug override: Force locked (free tier only)")]
    static void ForceLock() => Set(false);

    [MenuItem(Root + "Debug override: Off (use store state)")]
    static void ClearOverride() => Set(null);

    [MenuItem(Root + "Clear cached purchase")]
    static void ClearCache()
    {
        Entitlements.ClearLocalCacheForTesting();
        Debug.Log("Entitlements: cleared cached purchase / grandfather flag.");
    }

    [MenuItem(Root + "Log entitlement state")]
    static void LogState()
    {
        Debug.Log($"Entitlements: HasFullGame={Entitlements.HasFullGame} Mode={Entitlements.Mode} " +
                  $"Store={Entitlements.Availability} Price={Entitlements.LocalizedPrice ?? "(unknown)"} " +
                  $"DebugOverride={(Entitlements.DebugOverride.HasValue ? Entitlements.DebugOverride.Value.ToString() : "off")}");
    }

    static void Set(bool? value)
    {
        Entitlements.DebugOverride = value;
        Debug.Log($"Entitlements: debug override = {(value.HasValue ? value.Value.ToString() : "off")}");
    }
}
