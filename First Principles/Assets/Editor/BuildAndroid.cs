using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// -----------------------------------------------------------------------------
// BuildHelper — batch-mode build targets (Build menu + -executeMethod).
// -----------------------------------------------------------------------------
// Outputs (repo root, ignored by git):
//   Builds/Steam/Windows/First Principles.exe   — premium Steam build, FP_STEAM define
//   Builds/Steam/macOS/First Principles.app     — premium Steam build (universal), FP_STEAM
//   Builds/iOS/                                  — Xcode project for the App Store
//   Builds/FirstPrinciples.aab                   — Google Play (needs keystore env vars)
//   Builds/Dev/macOS/First Principles.app        — development build used for QA screenshots
// Example:
//   Unity -batchmode -quit -projectPath "First Principles" -executeMethod BuildHelper.BuildWindows
// -----------------------------------------------------------------------------

public static class BuildHelper
{
    public const string Version = "1.4";
    public const int IosBuildNumber = 7;
    public const int AndroidVersionCode = 6;
    public const string IosBundleId = "com.orchestrsim.FirstPrinciples";
    public const string AndroidPackage = "com.OrchAerospaceandGameGenesis.FirstPrinciples";
    public const string AppleTeamId = "Z64KRUX3W3";
    const string SteamDefine = "FP_STEAM";

    static readonly string[] Scenes =
    {
        "Assets/Scenes/Menu.unity",
        "Assets/Scenes/LevelSelect.unity",
        "Assets/Scenes/Game.unity",
    };

    static void ApplyCommonPlayerSettings()
    {
        PlayerSettings.bundleVersion = Version;
        PlayerSettings.iOS.buildNumber = IosBuildNumber.ToString();
        PlayerSettings.macOS.buildNumber = IosBuildNumber.ToString();
        PlayerSettings.Android.bundleVersionCode = AndroidVersionCode;
    }

    [MenuItem("Build/Android AAB (Release)")]
    public static void BuildRelease()
    {
        // Keystore configuration. Passwords come from env vars so the repo never holds them.
        // export ANDROID_KEYSTORE_PASS=...; export ANDROID_KEYALIAS_PASS=... before launching Unity.
        string keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
        string aliasPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");
        if (string.IsNullOrEmpty(keystorePass) || string.IsNullOrEmpty(aliasPass))
        {
            Fail("ANDROID_KEYSTORE_PASS / ANDROID_KEYALIAS_PASS env vars are required to build a signed AAB.");
            return;
        }

        ApplyCommonPlayerSettings();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidPackage);
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH") ?? "user.keystore";
        PlayerSettings.Android.keystorePass = keystorePass;
        PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_NAME") ?? "release";
        PlayerSettings.Android.keyaliasPass = aliasPass;
        SetSteamDefine(NamedBuildTarget.Android, false);

        EditorUserBuildSettings.buildAppBundle = true;
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        Run(BuildTarget.Android, "../Builds/FirstPrinciples.aab", BuildOptions.None);
    }

    [MenuItem("Build/Windows x64 (Steam)")]
    public static void BuildWindows()
    {
        ApplyCommonPlayerSettings();
        SetSteamDefine(NamedBuildTarget.Standalone, true);
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        Run(BuildTarget.StandaloneWindows64, "../Builds/Steam/Windows/First Principles.exe", BuildOptions.None);
    }

    [MenuItem("Build/macOS (Steam)")]
    public static void BuildMacSteam()
    {
        ApplyCommonPlayerSettings();
        SetSteamDefine(NamedBuildTarget.Standalone, true);
#if UNITY_EDITOR_OSX
        UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
        UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject = false;
#endif
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
        Run(BuildTarget.StandaloneOSX, "../Builds/Steam/macOS/First Principles.app", BuildOptions.None);
    }

    /// <summary>
    /// Mac App Store build: same bundle id as iOS (universal app record), store-gated with the
    /// same IAP products (FP_IAP_GATE_STANDALONE), receipt validation on. Signing, sandbox
    /// entitlements and the .pkg are done by tools/mas_package.sh.
    /// </summary>
    [MenuItem("Build/macOS (Mac App Store)")]
    public static void BuildMacAppStore()
    {
        ApplyCommonPlayerSettings();
        SetSteamDefine(NamedBuildTarget.Standalone, false);
        PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] before);
        var defines = before.Where(d => d != MasDefine).ToList();
        defines.Add(MasDefine);
        string oldId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
        bool oldValidation = PlayerSettings.useMacAppStoreValidation;
        try
        {
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, defines.ToArray());
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, IosBundleId);
            PlayerSettings.useMacAppStoreValidation = true;
            PlayerSettings.macOS.applicationCategoryType = "public.app-category.educational-games";
#if UNITY_EDITOR_OSX
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
            // Xcode project so xcodebuild can archive with cloud-managed signing (tools/mas_upload.sh).
            UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject = true;
#endif
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            try
            {
                Run(BuildTarget.StandaloneOSX, "../Builds/MAS/FirstPrinciples", BuildOptions.None);
            }
            finally
            {
#if UNITY_EDITOR_OSX
                UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject = false;
#endif
            }
        }
        finally
        {
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, before);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, oldId);
            PlayerSettings.useMacAppStoreValidation = oldValidation;
        }
    }

    const string MasDefine = "FP_IAP_GATE_STANDALONE";

    /// <summary>Development macOS build (no Steam) for automated QA screenshots (-fpShots).</summary>
    [MenuItem("Build/macOS (Dev, QA screenshots)")]
    public static void BuildMacDev()
    {
        ApplyCommonPlayerSettings();
        SetSteamDefine(NamedBuildTarget.Standalone, false);
#if UNITY_EDITOR_OSX
        UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
        UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject = false;
#endif
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
        Run(BuildTarget.StandaloneOSX, "../Builds/Dev/macOS/First Principles.app", BuildOptions.Development);
    }

    [MenuItem("Build/iOS Xcode Project")]
    public static void BuildIOS()
    {
        ApplyCommonPlayerSettings();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, IosBundleId);
        PlayerSettings.iOS.appleDeveloperTeamID = AppleTeamId;
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        SetSteamDefine(NamedBuildTarget.iOS, false);
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
        Run(BuildTarget.iOS, "../Builds/iOS", BuildOptions.None);
    }

    /// <summary>Development iOS Simulator build for automated QA screenshots (QaShots).</summary>
    [MenuItem("Build/iOS Simulator (Dev, QA screenshots)")]
    public static void BuildIOSSimulatorDev()
    {
        ApplyCommonPlayerSettings();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, IosBundleId);
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
        SetSteamDefine(NamedBuildTarget.iOS, false);
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
        try
        {
            Run(BuildTarget.iOS, "../Builds/iOS-Sim", BuildOptions.Development);
        }
        finally
        {
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        }
    }

    static void SetSteamDefine(NamedBuildTarget target, bool on)
    {
        PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
        var list = defines.Where(d => d != SteamDefine).ToList();
        if (on)
            list.Add(SteamDefine);
        PlayerSettings.SetScriptingDefineSymbols(target, list.ToArray());
    }

    static void Run(BuildTarget target, string outputPath, BuildOptions opts)
    {
        string full = Path.GetFullPath(outputPath);
        string dir = target == BuildTarget.iOS ? full : Path.GetDirectoryName(full);
        Directory.CreateDirectory(dir);

        var options = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = outputPath,
            target = target,
            options = opts,
        };

        var result = BuildPipeline.BuildPlayer(options);
        if (result.summary.result == BuildResult.Succeeded)
            Debug.Log($"BUILD SUCCEEDED: {outputPath} ({result.summary.totalSize} bytes)");
        else
            Fail($"BUILD FAILED: {result.summary.result} ({result.summary.totalErrors} errors)");
    }

    static void Fail(string message)
    {
        Debug.LogError(message);
        if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
            EditorApplication.Exit(1);
    }
}
