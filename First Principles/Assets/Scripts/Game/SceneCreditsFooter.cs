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
        "<b>First Principles</b> <color=#555555>(version 1.0)</color>";

    public const string CreditsLineDefaultEn =
        "<size=34>John Seong (Orch Aerospace) × GameGenesis (Rayan Kaissi)</size>";

    /// <summary>Centered block: version line, then credits when <c>menu.credits_line</c> is non-empty.</summary>
    public static string BuildMenuFooterRichText()
    {
        string ver = LocalizationManager.Get("menu.version_line", HomeFooterDefault);
        string cred = LocalizationManager.Get("menu.credits_line", CreditsLineDefaultEn);
        if (string.IsNullOrWhiteSpace(cred))
            return "<align=center>" + ver + "</align>";
        return "<align=center>" + ver + "\n\n" + cred + "</align>";
    }
}
