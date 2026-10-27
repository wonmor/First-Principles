using UnityEngine;
#if FP_STEAM && !DISABLESTEAMWORKS && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX)
using System.Text;
using Steamworks;
#endif

// -----------------------------------------------------------------------------
// SteamBridge — Steamworks.NET integration for the premium Steam build.
// -----------------------------------------------------------------------------
// Compiled only when the FP_STEAM scripting define is set (BuildHelper's Steam targets set
// it); every other build gets the no-op stub below. Responsibilities:
//   * SteamAPI.Init / RunCallbacks / Shutdown,
//   * mirror ProgressStore achievements to Steam achievements (ids = API names, see
//     STEAM_RELEASE.md) and push the "stars" / "levels_cleared" stats,
//   * Steam Cloud: progress.json is written with ISteamRemoteStorage on every save and
//     merged (best-of-both) from the cloud copy at startup.
// The Steam build is premium: Entitlements treats standalone builds as fully unlocked.
// SteamAppId 4570070 = "First Principles: A Calculus Platformer" (0 would skip RestartAppIfNecessary).
// -----------------------------------------------------------------------------

public static class SteamBridge
{
    /// <summary>Steam App ID from Steamworks (owner fills in). 0 = development: no RestartAppIfNecessary.</summary>
    public const uint SteamAppId = 4570070;

    public static bool IsActive { get; private set; }

#if FP_STEAM && !DISABLESTEAMWORKS && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX)
    const string CloudFile = ProgressStore.FileName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (IsActive)
            return;
        try
        {
            if (SteamAppId != 0 && SteamAPI.RestartAppIfNecessary(new AppId_t(SteamAppId)))
            {
                Application.Quit();
                return;
            }
            if (!Packsize.Test() || !DllCheck.Test())
            {
                Debug.LogWarning("SteamBridge: Steamworks.NET binary mismatch; Steam features disabled.");
                return;
            }
            IsActive = SteamAPI.Init();
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogWarning($"SteamBridge: steam_api not found ({e.Message}); Steam features disabled.");
            IsActive = false;
        }
        if (!IsActive)
        {
            Debug.Log("SteamBridge: Steam not running / not owned; playing offline (local saves only).");
            return;
        }

        var go = new GameObject("SteamBridgeRunner");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();

        PullCloud();
        ProgressStore.AchievementUnlocked += a => Unlock(a.Id);
        ProgressStore.Saved += PushCloud;
        ProgressStore.Changed += PushStats;
        // Re-sync achievements earned offline / before Steam was attached.
        foreach (var id in ProgressStore.Data.achievements)
            SteamUserStats.SetAchievement(id);
        PushStats();
    }

    static void Unlock(string apiName)
    {
        if (!IsActive) return;
        SteamUserStats.SetAchievement(apiName);
        SteamUserStats.StoreStats();
    }

    static void PushStats()
    {
        if (!IsActive) return;
        SteamUserStats.SetStat("stars", ProgressStore.TotalStars);
        SteamUserStats.SetStat("levels_cleared", ProgressStore.Snapshot().LevelsCleared);
        SteamUserStats.SetStat("daily_best_streak", ProgressStore.Data.dailyBestStreak);
        SteamUserStats.StoreStats();
    }

    static void PullCloud()
    {
        if (!SteamRemoteStorage.IsCloudEnabledForAccount() || !SteamRemoteStorage.IsCloudEnabledForApp())
            return;
        if (!SteamRemoteStorage.FileExists(CloudFile))
            return;
        int size = SteamRemoteStorage.GetFileSize(CloudFile);
        if (size <= 0 || size > 1 << 20)
            return;
        var buf = new byte[size];
        int read = SteamRemoteStorage.FileRead(CloudFile, buf, size);
        if (read > 0)
            ProgressStore.MergeFromJson(Encoding.UTF8.GetString(buf, 0, read));
    }

    static void PushCloud(string json)
    {
        if (!IsActive || string.IsNullOrEmpty(json))
            return;
        if (!SteamRemoteStorage.IsCloudEnabledForAccount() || !SteamRemoteStorage.IsCloudEnabledForApp())
            return;
        var bytes = Encoding.UTF8.GetBytes(json);
        SteamRemoteStorage.FileWrite(CloudFile, bytes, bytes.Length);
    }

    sealed class Runner : MonoBehaviour
    {
        void Update()
        {
            if (IsActive)
                SteamAPI.RunCallbacks();
        }

        void OnApplicationQuit()
        {
            ProgressStore.FlushIfDirty();
            if (IsActive)
            {
                SteamAPI.Shutdown();
                IsActive = false;
            }
        }
    }
#endif
}
