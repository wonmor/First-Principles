using UnityEditor;
using UnityEngine;

public static class BuildAndroid
{
    [MenuItem("Build/Android AAB (Release)")]
    public static void BuildRelease()
    {
        // Keystore configuration.
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = "user.keystore";
        PlayerSettings.Android.keystorePass = "Horizon1207!";
        PlayerSettings.Android.keyaliasName = "release";
        PlayerSettings.Android.keyaliasPass = "Horizon1207!";

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
}
