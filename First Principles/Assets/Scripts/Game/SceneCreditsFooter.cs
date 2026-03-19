using TMPro;
using UnityEngine;

// -----------------------------------------------------------------------------
// SceneCreditsFooter — same legal/credits lines as Menu.unity (keep in sync)
// -----------------------------------------------------------------------------
// Level select + Game HUD build UI at runtime; this centralizes copy so all scenes agree.
// -----------------------------------------------------------------------------

/// <summary>Footer rich text shown on level select and in-game (matches <c>Menu</c> scene).</summary>
public static class SceneCreditsFooter
{
    public const string ProprietaryLine = "© 2022-2026 · Proprietary · All rights reserved · First Principles";

    public const string AttributionLine =
        "<b>GAME GENESIS</b> (<link=\"https://github.com/rkaissi/\"><color=#8b9dc9>Rayan Kaissi</color></link>) × " +
        "<b>ORCH AEROSPACE</b> (<link=\"https://github.com/wonmor/\"><color=#8b9dc9>John Wonmo Seong</color></link>)";

    public const string SchoolPrideLine =
        "Proud graduates of <b>Garth Webb Secondary School</b>, Oakville.";

    /// <summary>Encourages support — keep tone grateful, not pushy (store / tip jars / wishlists).</summary>
    public const string SupportLine =
        "Four years of development — if you value this work, please support the project. Thank you.";

    public const string UnityLine = "Made with <b>Unity</b>. Unity is a trademark of Unity Technologies.";

    /// <summary>Compact block for small bottom strips (rich text).</summary>
    public static string BuildCompactRichText()
    {
        return
            "<align=center>" +
            $"<size=90%><color=#aaaaaa>{ProprietaryLine}</color></size>\n" +
            $"<size=82%><color=#888899>{AttributionLine}</color></size>\n" +
            $"<size=74%><color=#7a8498><i>{SchoolPrideLine}</i></color></size>\n" +
            $"<size=76%><color=#6f7d90><i>{SupportLine}</i></color></size>\n" +
            $"<size=78%><color=#6a6f80>{UnityLine}</color></size>" +
            "</align>";
    }

    public static void CopyFontIfPossible(TextMeshProUGUI target)
    {
        if (target == null)
            return;

        var any = Object.FindAnyObjectByType<TextMeshProUGUI>();
        if (any != null && any != target && any.font != null)
            target.font = any.font;
        else if (TMP_Settings.defaultFontAsset != null)
            target.font = TMP_Settings.defaultFontAsset;
    }
}
