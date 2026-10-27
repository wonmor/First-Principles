using UnityEngine;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// RuntimeUiPolish — shared styling entry points (now backed by the Bauhaus theme)
// -----------------------------------------------------------------------------
// No Asset Store download required. For higher-end art, see docs/optional-unity-assets.md
// -----------------------------------------------------------------------------

/// <summary>Legacy styling API used across the runtime UI; forwards to <see cref="Bauhaus"/> (flat, square, no shadows).</summary>
public static class RuntimeUiPolish
{
    private static Sprite _softCircle;

    /// <summary>
    /// Historical name: since 1.4 (Bauhaus) this is the flat square <see cref="Bauhaus.Square"/>, so every
    /// "rounded" panel and chip in the game renders with crisp square corners.
    /// </summary>
    public static Sprite Rounded9Slice => Bauhaus.Square;

    /// <summary>Soft-edged disk for the graph “character” (tint with <see cref="Image.color"/>).</summary>
    public static Sprite SoftCharacterBlob
    {
        get
        {
            if (_softCircle == null)
                _softCircle = BuildRadialFalloffSprite(48);
            return _softCircle;
        }
    }

    // --- Palette (Bauhaus: paper, ink, red / yellow / blue) ----------------------
    // Names are kept from the old graphite theme so every call site picks up the new look.
    public static readonly Color PanelDeep = Bauhaus.Paper;
    public static readonly Color PanelMid = Bauhaus.Card;
    public static readonly Color ButtonNeutral = Bauhaus.Card;
    public static readonly Color ButtonNeutralHover = Bauhaus.Yellow;
    public static readonly Color AccentTeal = Bauhaus.Blue;
    public static readonly Color AccentCoral = Bauhaus.Red;
    public static readonly Color AccentJump = Bauhaus.Blue;
    /// <summary>Primary text colour (ink on paper).</summary>
    public static readonly Color TitleIvory = Bauhaus.Ink;
    /// <summary>Graph player glyph: solid ink so it reads on the paper plot and every curve colour.</summary>
    public static readonly Color PlayerBody = Bauhaus.Ink;

    /// <summary>Lightens an accent (menu chip, etc.) so the math glyph pops on top.</summary>
    public static Color BrightenPlayerAccent(Color accent, float towardWhite = 0.42f) =>
        Color.Lerp(accent, Color.white, Mathf.Clamp01(towardWhite));

    /// <summary>Bauhaus has no drop shadows: removes any legacy soft shadow instead of adding one.</summary>
    public static void ApplyDropShadow(RectTransform target, Vector2? distance = null, float alpha = 0.28f)
    {
        if (target != null)
            Bauhaus.StripShadow(target.gameObject);
    }

    /// <summary>
    /// Flat Bauhaus button states. <paramref name="baseColor"/> picks the tone (coral = red, teal / violet = blue,
    /// gold = yellow, dark neutrals = paper); the highlight / pressed arguments from old call sites are ignored
    /// in favour of the shared crisp hover / press colours.
    /// </summary>
    public static void ApplyButtonTransitions(Button button, Color baseColor, Color highlighted, Color pressed)
    {
        if (button == null)
            return;
        Bauhaus.Button(button, Bauhaus.ToneFor(baseColor));
    }

    public static void UseRoundedSliced(Image img)
    {
        if (img == null)
            return;

        img.sprite = Bauhaus.Square;
        img.type = Image.Type.Sliced;
        img.preserveAspect = false;
    }

    public static bool ShouldUseSlicedForSize(float pxWidth, float pxHeight)
    {
        return pxWidth >= 28f && pxHeight >= 20f;
    }

    private static Sprite BuildRadialFalloffSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float cx = (size - 1) * 0.5f;
        float cy = (size - 1) * 0.5f;
        float rad = size * 0.42f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                float a = Mathf.Clamp01(1f - (d - rad + 2f) / 4f);
                a = Mathf.Pow(a, 1.4f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
