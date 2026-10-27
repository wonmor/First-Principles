// -----------------------------------------------------------------------------
// Menu footer copy — title + optional credits (localized separately).
// -----------------------------------------------------------------------------
// <c>menu.version_line</c> + <c>menu.credits_line</c> in each <c>Localization/{code}.txt</c>.
// -----------------------------------------------------------------------------

/// <summary>Composes menu footer rich text for TextMesh Pro.</summary>
public static class SceneCreditsFooter
{
    /// <summary>Title + version line only (fallback if credits key empty).</summary>
    public const string HomeFooterDefault =
        "<b>First Principles</b> <color=#57534A>(version 1.0)</color>";

    public const string CreditsLineDefaultEn =
        "<size=34>John Seong (Orch Aerospace) × GameGenesis (Rayan Kaissi)</size>";

    public const string SupportContactDefault =
        "<size=32><color=#57534A>Support:</color> <color=#57534A>contact@orchestrsim.com</color></size>";

    /// <summary>Centered block: version, credits, support email (<c>menu.support_contact</c>).</summary>
    public static string BuildMenuFooterRichText()
    {
        string ver = LocalizationManager.Get("menu.version_line", HomeFooterDefault)
            .Replace("1.0)", UnityEngine.Application.version + ")");
        string cred = LocalizationManager.Get("menu.credits_line", CreditsLineDefaultEn);
        string sup = LocalizationManager.Get("menu.support_contact", SupportContactDefault);

        var sb = new System.Text.StringBuilder();
        sb.Append("<align=center>");
        sb.Append(ver);
        if (!string.IsNullOrWhiteSpace(cred))
        {
            sb.Append("\n\n");
            sb.Append(cred);
        }

        if (!string.IsNullOrWhiteSpace(sup))
        {
            sb.Append("\n\n");
            sb.Append(sup);
        }

        sb.Append("</align>");
        return sb.ToString();
    }

    /// <summary>
    /// Bauhaus menu foot (1.4): the same three lines, left-aligned and compact. Absolute
    /// <c>&lt;size=NN&gt;</c> tags from the localized lines are dropped so the block autosizes in its column.
    /// </summary>
    public static string BuildMenuFooterCompact()
    {
        string full = BuildMenuFooterRichText();
        full = System.Text.RegularExpressions.Regex.Replace(full, @"</?size(=[0-9.]+)?>", "");
        full = full.Replace("<align=center>", "").Replace("</align>", "").Replace("\n\n", "\n");
        return full;
    }
}
