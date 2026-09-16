using UnityEditor;
using UnityEngine;

public static class BuildHelper
{
    [MenuItem("Build/Android AAB (Release)")]
    public static void BuildRelease()
    {
        // Keystore configuration. Passwords come from env vars so the repo never holds them.
        // export ANDROID_KEYSTORE_PASS=...; export ANDROID_KEYALIAS_PASS=... before launching Unity.
        string keystorePass = System.Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
        string aliasPass = System.Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");
        if (string.IsNullOrEmpty(keystorePass) || string.IsNullOrEmpty(aliasPass))
        {
            Debug.LogError("ANDROID_KEYSTORE_PASS / ANDROID_KEYALIAS_PASS env vars are required to build a signed AAB.");
            return;
        }

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = "user.keystore";
        PlayerSettings.Android.keystorePass = keystorePass;
        PlayerSettings.Android.keyaliasName = "release";
        PlayerSettings.Android.keyaliasPass = aliasPass;

        // Build AAB.
        EditorUserBuildSettings.buildAppBundle = true;
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        string outputPath = "../Builds/FirstPrinciples.aab";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(
            System.IO.Path.GetFullPath(outputPath)));

        var options = new BuildPlayerOptions
        {
            scenes = new[]
            {
                "Assets/Scenes/Menu.unity",
                "Assets/Scenes/LevelSelect.unity",
                "Assets/Scenes/Game.unity"
            },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        var result = BuildPipeline.BuildPlayer(options);
        if (result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"BUILD SUCCEEDED: {outputPath} ({result.summary.totalSize} bytes)");
        else
            Debug.LogError($"BUILD FAILED: {result.summary.result}");
    }

    [MenuItem("Build/Windows x64 (Steam)")]
    public static void BuildWindows()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

        string outputDir = "../Builds/Windows";
        string outputPath = outputDir + "/First Principles.exe";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath(outputDir));

        var options = new BuildPlayerOptions
        {
            scenes = new[]
            {
                "Assets/Scenes/Menu.unity",
                "Assets/Scenes/LevelSelect.unity",
                "Assets/Scenes/Game.unity"
            },
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var result = BuildPipeline.BuildPlayer(options);
        if (result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"BUILD SUCCEEDED: {outputPath} ({result.summary.totalSize} bytes)");
        else
        {
            Debug.LogError($"BUILD FAILED: {result.summary.result}");
            if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                EditorApplication.Exit(1);
        }
    }

    [MenuItem("Build/iOS Xcode Project")]
    public static void BuildIOS()
    {
        PlayerSettings.bundleVersion = "1.2";
        PlayerSettings.iOS.buildNumber = "3";

        string outputPath = "../Builds/iOS";
        System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath(outputPath));

        var options = new BuildPlayerOptions
        {
            scenes = new[]
            {
                "Assets/Scenes/Menu.unity",
                "Assets/Scenes/LevelSelect.unity",
                "Assets/Scenes/Game.unity"
            },
            locationPathName = outputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        };

        var result = BuildPipeline.BuildPlayer(options);
        if (result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            Debug.Log($"iOS BUILD SUCCEEDED: {outputPath}");
        else
            Debug.LogError($"iOS BUILD FAILED: {result.summary.result}");
    }
}
