using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Release (non-development) macOS build with QaShots enabled through the FP_QA_SHOTS define, for
/// App Store screenshots without the "Development Build" watermark. Gated like the Mac App Store
/// build (FP_IAP_GATE_STANDALONE), no Steam. Restores the Standalone defines afterwards.
///   Unity -batchmode -quit -projectPath "First Principles" -executeMethod StoreShotsBuild.BuildMac
/// Output: Builds/StoreShots/First Principles.app (ignored by git).
/// </summary>
public static class StoreShotsBuild
{
    public static void BuildMac()
    {
        PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone, out string[] before);
        try
        {
            var defines = before.Where(d => d != "FP_STEAM" && d != "FP_IAP_GATE_STANDALONE" && d != "FP_QA_SHOTS").ToList();
            defines.Add("FP_IAP_GATE_STANDALONE");
            defines.Add("FP_QA_SHOTS");
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, defines.ToArray());
#if UNITY_EDITOR_OSX
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
            UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject = false;
#endif
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Menu.unity", "Assets/Scenes/LevelSelect.unity", "Assets/Scenes/Game.unity" },
                locationPathName = "../Builds/StoreShots/First Principles.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var result = BuildPipeline.BuildPlayer(options);
            if (result.summary.result == BuildResult.Succeeded)
                Debug.Log("BUILD SUCCEEDED: store shots");
            else
            {
                Debug.LogError("BUILD FAILED: " + result.summary.result);
                EditorApplication.Exit(1);
            }
        }
        finally
        {
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, before);
        }
    }
}
