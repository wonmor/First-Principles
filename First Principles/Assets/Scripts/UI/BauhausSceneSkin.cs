using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// BauhausSceneSkin — applies the Bauhaus theme to the few scene-authored objects
// -----------------------------------------------------------------------------
// Almost all UI is built in code through Bauhaus / UiKit / RuntimeUiPolish. The scenes still own
// a handful of designer objects from 1.0 (camera clear colour, fade images, the hand-drawn
// "BACK" / "Trans" / "Scale" sprite buttons, the bevelled logo frame, the equation display bar
// and the dark grid background). This re-skins them in one place on every scene load, so the
// scenes stay untouched and the theme is still defined by the shared Bauhaus palette.
// -----------------------------------------------------------------------------

public static class BauhausSceneSkin
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Bauhaus.Paper;
        }

        // Scene fades go to paper, not to white or black.
        foreach (var name in new[] { "FadeImage", "FadeImage2" })
            foreach (var img in FindAll<Image>(name))
                img.color = Bauhaus.WithAlpha(Bauhaus.Paper, img.color.a);

        if (scene.name == "Game")
            SkinGame();
    }

    static void SkinGame()
    {
        var gridBg = Find<Image>("Grid Background");
        if (gridBg != null)
            gridBg.color = Bauhaus.Paper;

        // Footer: the equation sits in a white box with an even black rule; no grey bevel. Ink type so the soft-ink / red / blue notes read.
        var outside2 = GameObject.Find("OutsideSquare2");
        if (outside2 != null)
            outside2.SetActive(false);
        SetBlock("Display Panel", new Color(0f, 0f, 0f, 0f)); // container only
        SetBlock("Black Square", Color.white);
        var box = Find<Image>("Black Square");
        if (box != null)
            Bauhaus.AddRule(box, Bauhaus.PanelRule);
        SetBlock("OutsideSquare1", new Color(0f, 0f, 0f, 0f)); // old drop block behind the readout
        var eq = Find<TextMeshProUGUI>("Equation");
        if (eq != null)
            Bauhaus.Label(eq, Bauhaus.Ink);

        var logo = GameObject.Find("Logo");
        if (logo != null)
            SkinLogoFrame(logo.transform);

        SkinSpriteButton("BackButton", LocalizationManager.Get("ui.back", "Back"), StarGlyph.ArrowLeft, Bauhaus.Tone.Paper);
        SkinSpriteButton("TransButton", "Trans", null, Bauhaus.Tone.Paper);
        SkinSpriteButton("ScaleButton", "Scale", null, Bauhaus.Tone.Paper);
    }

    static void SetBlock(string name, Color c)
    {
        var img = Find<Image>(name);
        if (img == null)
            return;
        img.sprite = Bauhaus.Square;
        img.type = Image.Type.Sliced;
        img.color = c;
    }

    /// <summary>Logo (lim h→0 video): drop the grey bevel and offset shadow; solid ink frame.</summary>
    public static void SkinLogoFrame(Transform logo)
    {
        if (logo == null)
            return;
        var bevel = logo.Find("Square1");
        if (bevel != null)
            bevel.gameObject.SetActive(false);
        var shadow = logo.Find("Square2");
        if (shadow != null)
            shadow.gameObject.SetActive(false);
        var frame = logo.Find("Square3");
        if (frame != null && frame.TryGetComponent<Image>(out var img))
        {
            img.sprite = Bauhaus.Square;
            img.color = Bauhaus.Ink;
        }
    }

    /// <summary>Replaces a hand-drawn sprite button with a flat Bauhaus block + label (+ optional line glyph).</summary>
    public static void SkinSpriteButton(string name, string label, Sprite glyph, Bauhaus.Tone tone)
    {
        var go = FindIncludingInactive(name);
        if (go == null || go.GetComponent<Button>() == null)
            return;
        var rt = go.GetComponent<RectTransform>();
        if (rt.Find("BauhausLabel") == null)
        {
            var t = UiKit.Text(rt, "BauhausLabel", Vector2.zero, Vector2.one, label, UiTypography.Scale(30),
                Bauhaus.OnFill(tone), FontStyles.Bold,
                glyph != null ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
            t.rectTransform.offsetMin = new Vector2(glyph != null ? 70f : 12f, 8f);
            t.rectTransform.offsetMax = new Vector2(-12f, -8f);
            if (glyph != null)
            {
                var g = StarGlyph.Add(rt, "Glyph", glyph, Bauhaus.OnFill(tone), new Vector2(40f, 40f));
                g.rectTransform.anchorMin = g.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                g.rectTransform.pivot = new Vector2(0f, 0.5f);
                g.rectTransform.anchoredPosition = new Vector2(20f, 0f);
            }
        }
        Bauhaus.Button(go.GetComponent<Button>(), tone);
    }

    static T Find<T>(string name) where T : Component
    {
        var go = FindIncludingInactive(name);
        return go != null ? go.GetComponent<T>() : null;
    }

    static System.Collections.Generic.IEnumerable<T> FindAll<T>(string name) where T : Component
    {
        foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c != null && c.gameObject.name == name)
                yield return c;
    }

    static GameObject FindIncludingInactive(string name)
    {
        foreach (var t in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.gameObject.name == name && t.gameObject.scene.IsValid())
                return t.gameObject;
        return null;
    }
}
