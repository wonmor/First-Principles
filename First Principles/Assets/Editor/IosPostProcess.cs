#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

/// <summary>Info.plist tweaks for App Store uploads (export compliance: no non-exempt encryption).</summary>
public static class IosPostProcess
{
    [PostProcessBuild(100)]
    public static void OnPostprocessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS)
            return;
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        plist.WriteToFile(plistPath);
    }
}
#endif
