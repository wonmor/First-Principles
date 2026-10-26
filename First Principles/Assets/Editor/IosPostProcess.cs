using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;

/// <summary>
/// Info.plist tweaks for App Store uploads (export compliance: no non-exempt encryption).
/// Plain-text edit on purpose: batch builds switch to iOS inside the same editor session, so
/// code behind <c>#if UNITY_IOS</c> (and the iOS Xcode API) would not be compiled yet.
/// </summary>
public static class IosPostProcess
{
    const string Key = "ITSAppUsesNonExemptEncryption";

    [PostProcessBuild(100)]
    public static void OnPostprocessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS)
            return;
        string plistPath = Path.Combine(path, "Info.plist");
        if (!File.Exists(plistPath))
            return;
        string text = File.ReadAllText(plistPath);
        if (text.Contains("<key>" + Key + "</key>"))
            return;
        int end = text.LastIndexOf("</dict>", System.StringComparison.Ordinal);
        if (end < 0)
            return;
        text = text.Substring(0, end) + "\t<key>" + Key + "</key>\n\t<false/>\n" + text.Substring(end);
        File.WriteAllText(plistPath, text);
    }
}
