using TMPro;
using UnityEngine;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// Bauhaus — the single visual theme for every UI surface (1.4 redesign)
// -----------------------------------------------------------------------------
// Flat primaries (red, yellow, blue) + black ink on an off-white paper ground, square
// corners, thick black rules, one geometric sans (Outfit, OFL). No gradients, glows,
// drop shadows or bevels. Buttons are flat colour blocks with a black rule and crisp
// (zero-fade) hover / press / selected states.
//
// Everything that draws UI goes through here, directly or via RuntimeUiPolish / UiKit,
// so the palette and button behaviour live in one place.
// -----------------------------------------------------------------------------

public static class Bauhaus
{
    // --- Palette ---------------------------------------------------------------------
    /// <summary>Page ground.</summary>
    public static readonly Color Paper = Hex("F2EEE4");
    /// <summary>Raised card / sheet on top of the page (slightly lighter than paper).</summary>
    public static readonly Color Card = Hex("FBF9F4");
    public static readonly Color Ink = Hex("141414");
    /// <summary>Secondary text on paper (captions, hints). Contrast 6.4:1 on Paper.</summary>
    public static readonly Color InkSoft = Hex("57534A");
    public static readonly Color Red = Hex("D7261E");
    public static readonly Color Yellow = Hex("F5BE0B");
    public static readonly Color Blue = Hex("1F4FA0");
    /// <summary>Hairlines: minor grid, dividers, unearned stars.</summary>
    public static readonly Color Rule = Hex("D6CFBF");
    public static readonly Color RuleStrong = Hex("A79E8B");
    /// <summary>Flat modal scrim (no blur, no gradient).</summary>
    public static readonly Color Scrim = new Color(0.08f, 0.078f, 0.07f, 0.62f);

    // Pressed shades (same hue, darker) so labels keep their contrast while held.
    static readonly Color RedPressed = Hex("A81B14");
    static readonly Color BluePressed = Hex("163B78");
    static readonly Color YellowPressed = Hex("D9A600");

    /// <summary>Thick rule around sheets / panels (canvas units).</summary>
    public const float PanelRule = 4f;
    /// <summary>Rule around buttons and chips.</summary>
    public const float ButtonRule = 3f;

    public enum Tone
    {
        /// <summary>Card fill, ink label, black rule: secondary actions.</summary>
        Paper,
        /// <summary>Primary action.</summary>
        Red,
        Blue,
        Yellow,
        Ink,
    }

    public static Color Fill(Tone t)
    {
        switch (t)
        {
            case Tone.Red: return Red;
            case Tone.Blue: return Blue;
            case Tone.Yellow: return Yellow;
            case Tone.Ink: return Ink;
            default: return Card;
        }
    }

    /// <summary>Label / glyph colour on top of a tone.</summary>
    public static Color OnFill(Tone t) => t == Tone.Paper || t == Tone.Yellow ? Ink : Card;

    public static Color Hover(Tone t)
    {
        switch (t)
        {
            case Tone.Red:
            case Tone.Blue: return Ink;
            case Tone.Ink: return Red;
            case Tone.Yellow: return Card;
            default: return Yellow;
        }
    }

    public static Color Pressed(Tone t)
    {
        switch (t)
        {
            case Tone.Red: return RedPressed;
            case Tone.Blue: return BluePressed;
            case Tone.Ink: return RedPressed;
            case Tone.Yellow: return YellowPressed;
            default: return YellowPressed;
        }
    }

    // --- Sprites -----------------------------------------------------------------------
    static Sprite _square;

    /// <summary>Flat white square, 9-sliced so it can stand in anywhere a rounded sprite was used.</summary>
    public static Sprite Square
    {
        get
        {
            if (_square != null)
                return _square;
            const int n = 8;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            tex.name = "BauhausSquare";
            _square = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(2, 2, 2, 2));
            _square.name = "BauhausSquare";
            return _square;
        }
    }

    static Sprite _frame;

    /// <summary>9-sliced hollow square (6 px rule, clear centre): focus rings and outline-only frames.</summary>
    public static Sprite Frame
    {
        get
        {
            if (_frame != null)
                return _frame;
            const int n = 16, b = 6;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool edge = x < b || y < b || x >= n - b || y >= n - b;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(edge ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _frame = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(b, b, b, b));
            _frame.name = "BauhausFrame";
            return _frame;
        }
    }

    // --- Styling helpers ---------------------------------------------------------------

    /// <summary>Solid black rule around a graphic (uGUI <see cref="Outline"/> on a flat quad = crisp border).</summary>
    public static Outline AddRule(Graphic g, float px = ButtonRule, Color? color = null)
    {
        if (g == null)
            return null;
        var o = g.GetComponent<Outline>();
        if (px <= 0f)
        {
            if (o != null)
                Object.Destroy(o);
            return null;
        }
        if (o == null)
            o = g.gameObject.AddComponent<Outline>();
        o.effectColor = color ?? Ink;
        o.effectDistance = new Vector2(px, px);
        o.useGraphicAlpha = true;
        return o;
    }

    /// <summary>Removes legacy soft drop shadows (keeps <see cref="Outline"/> rules).</summary>
    public static void StripShadow(GameObject go)
    {
        if (go == null)
            return;
        foreach (var s in go.GetComponents<Shadow>())
        {
            if (s != null && s.GetType() == typeof(Shadow))
                Object.Destroy(s);
        }
    }

    /// <summary>Flat panel / sheet: square, tone fill, optional black rule, no shadow.</summary>
    public static void Panel(Image img, Color? fill = null, float rule = PanelRule)
    {
        if (img == null)
            return;
        img.sprite = Square;
        img.type = Image.Type.Sliced;
        img.color = fill ?? Card;
        AddRule(img, rule);
        StripShadow(img.gameObject);
    }

    /// <summary>
    /// Flat Bauhaus button: tone fill, black rule, crisp colour swap on hover / press / gamepad select,
    /// labels recoloured for contrast. Behaviour (onClick) is untouched.
    /// </summary>
    /// <param name="labelRoot">Only recolour labels / glyphs under this child (e.g. a map tile's face, not its caption).</param>
    /// <param name="recolorLabels">False keeps the caller's label colours (e.g. a dimmed "locked" caption).</param>
    public static void Button(Button b, Tone tone, float rule = ButtonRule, Transform labelRoot = null, bool recolorLabels = true)
    {
        if (b == null)
            return;
        var g = b.targetGraphic as Image;
        if (g == null)
            g = b.GetComponent<Image>();
        if (g != null)
        {
            g.sprite = Square;
            g.type = Image.Type.Sliced;
            g.color = Color.white; // ColorTint multiplies; the block carries the real colour.
            b.targetGraphic = g;
            AddRule(g, rule);
            StripShadow(g.gameObject);
        }

        b.transition = Selectable.Transition.ColorTint;
        var c = b.colors;
        c.colorMultiplier = 1f;
        c.fadeDuration = 0f;
        c.normalColor = Fill(tone);
        c.highlightedColor = Hover(tone);
        c.selectedColor = Hover(tone);
        c.pressedColor = Pressed(tone);
        c.disabledColor = Rule; // flat light block; BauhausButtonSkin greys the label
        b.colors = c;

        // Swap the old hand-drawn sprite states (Play / Back art) for colour-only transitions.
        var ss = b.spriteState;
        ss.highlightedSprite = null;
        ss.pressedSprite = null;
        ss.selectedSprite = null;
        ss.disabledSprite = null;
        b.spriteState = ss;

        var skin = b.GetComponent<BauhausButtonSkin>();
        if (skin == null)
            skin = b.gameObject.AddComponent<BauhausButtonSkin>();
        skin.Tone = tone;
        skin.LabelRoot = labelRoot;
        skin.RecolorLabels = recolorLabels;
        skin.Apply();
    }

    /// <summary>Line glyph pinned to the left of a button / chip, label shifted right to make room.</summary>
    public static Image LeadGlyph(RectTransform button, TMP_Text label, Sprite glyph, float size = 30f, float inset = 14f)
    {
        var icon = StarGlyph.Add(button, "Glyph", glyph, label != null ? label.color : Ink, new Vector2(size, size));
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        icon.rectTransform.pivot = new Vector2(0f, 0.5f);
        icon.rectTransform.anchoredPosition = new Vector2(inset, 0f);
        if (label != null)
        {
            var o = label.rectTransform.offsetMin;
            label.rectTransform.offsetMin = new Vector2(inset + size + 10f, o.y);
        }
        return icon;
    }

    /// <summary>Text on the Bauhaus ground: project font, flat colour, no outline / glow / underlay.</summary>
    public static void Label(TMP_Text t, Color color)
    {
        if (t == null)
            return;
        t.color = color;
        Flatten(t);
    }

    /// <summary>Strips TMP outline / underlay / glow so text renders as flat ink.</summary>
    public static void Flatten(TMP_Text t)
    {
        if (t == null)
            return;
        if (t.outlineWidth > 0f)
            t.outlineWidth = 0f;
        var dropShadows = t.GetComponents<Shadow>();
        foreach (var s in dropShadows)
            if (s != null)
                Object.Destroy(s);
    }

    /// <summary>
    /// Maps a colour from the pre-1.4 dark theme to a Bauhaus tone (used where old call sites still
    /// pass their accent colour: coral -> red, teal / indigo / violet / green -> blue, gold -> yellow,
    /// dark neutrals -> paper button).
    /// </summary>
    public static Tone ToneFor(Color c)
    {
        if (Approximately(c, Red) || Approximately(c, RedPressed)) return Tone.Red;
        if (Approximately(c, Blue)) return Tone.Blue;
        if (Approximately(c, Yellow)) return Tone.Yellow;
        if (Approximately(c, Ink)) return Tone.Ink;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (s < 0.22f || v < 0.3f)
            return Tone.Paper;
        float deg = h * 360f;
        if (deg < 20f || deg >= 330f) return Tone.Red;
        if (deg < 70f) return Tone.Yellow;
        return Tone.Blue;
    }

    static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

    /// <summary>Plot ink for curves: maps a level's neon curve colour onto the nearest primary.</summary>
    public static Color PlotColor(Color legacy, Color fallback)
    {
        Color.RGBToHSV(legacy, out float h, out float s, out float v);
        if (s < 0.18f)
            return fallback;
        float deg = h * 360f;
        if (deg < 22f || deg >= 300f) return Red;
        if (deg < 75f) return YellowLine;
        return Blue;
    }

    /// <summary>Yellow dark enough to read as a thin line on paper (fills use <see cref="Yellow"/>).</summary>
    public static readonly Color YellowLine = Hex("E0A800");

    public static string HexOf(Color c) => ColorUtility.ToHtmlStringRGB(c);

    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }

    public static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
}

/// <summary>
/// Keeps a Bauhaus button's labels and line glyphs in the tone's label colour. Re-applies on
/// <c>Start</c> because most runtime builders add the label after styling the button.
/// </summary>
[DisallowMultipleComponent]
public class BauhausButtonSkin : MonoBehaviour
{
    public Bauhaus.Tone Tone;
    public Transform LabelRoot;
    public bool RecolorLabels = true;

    void Start() => Apply();

    bool _lastInteractable = true;

    void Update()
    {
        // Disabled buttons: soft-ink label on the light "Rule" block (readable, clearly inactive).
        var b = GetComponent<Button>();
        bool on = b == null || b.IsInteractable();
        if (on == _lastInteractable)
            return;
        _lastInteractable = on;
        if (on)
        {
            Apply();
            return;
        }
        if (!RecolorLabels)
            return;
        var root = LabelRoot != null ? LabelRoot : transform;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            t.color = Bauhaus.InkSoft;
    }

    public void Apply()
    {
        if (!RecolorLabels)
            return;
        var b = GetComponent<Button>();
        Graphic block = b != null ? b.targetGraphic : null;
        Color on = Bauhaus.OnFill(Tone);
        var root = LabelRoot != null ? LabelRoot : transform;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!SitsOnOwnBlock(t.transform, block))
                Bauhaus.Label(t, on);
        }
        foreach (var img in root.GetComponentsInChildren<Image>(true))
        {
            if (img == block || img.sprite == null || img.sprite.name != "Glyph")
                continue;
            if (!SitsOnOwnBlock(img.transform.parent, block))
                img.color = on;
        }
    }

    /// <summary>True when a child sits on its own coloured block (e.g. a "Best value" tag) rather than the button.</summary>
    bool SitsOnOwnBlock(Transform t, Graphic block)
    {
        for (var p = t; p != null && p != transform; p = p.parent)
        {
            var img = p.GetComponent<Image>();
            if (img != null && img != block && img.sprite == Bauhaus.Square && img.color.a > 0.05f)
                return true;
        }
        return false;
    }
}
