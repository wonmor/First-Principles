using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural star glyphs (white RGBA, tint via <see cref="Image.color"/>): a filled star for earned
/// stars and a line-outline star for unearned ones — same no-asset approach as <see cref="LockGlyph"/>.
/// Also provides a line-style lightbulb (hint), flame (streak), trophy (achievements) and
/// chevron (jump marker) so overlays never need emoji or geometric-shape icons.
/// </summary>
public static class StarGlyph
{
    const int Size = 96;
    static Sprite _filled, _outline, _bulb, _flame, _trophy, _chevron, _calendar;

    public static Sprite Filled => _filled != null ? _filled : (_filled = BuildStar(true));
    public static Sprite Outline => _outline != null ? _outline : (_outline = BuildStar(false));
    public static Sprite Bulb => _bulb != null ? _bulb : (_bulb = BuildSdf(BulbSdf));
    public static Sprite Flame => _flame != null ? _flame : (_flame = BuildSdf(FlameSdf));
    public static Sprite Trophy => _trophy != null ? _trophy : (_trophy = BuildSdf(TrophySdf));
    public static Sprite Chevron => _chevron != null ? _chevron : (_chevron = BuildSdf(ChevronSdf));
    public static Sprite Calendar => _calendar != null ? _calendar : (_calendar = BuildSdf(CalendarSdf));

    public static Sprite Medal => _medal != null ? _medal : (_medal = BuildSdf(MedalSdf));
    public static Sprite Pin => _pin != null ? _pin : (_pin = BuildSdf(PinSdf));
    public static Sprite Stopwatch => _stopwatch != null ? _stopwatch : (_stopwatch = BuildSdf(StopwatchSdf));
    public static Sprite Spring => _spring != null ? _spring : (_spring = BuildSdf(SpringSdf));
    public static Sprite Flag => _flag != null ? _flag : (_flag = BuildSdf(FlagSdf));
    public static Sprite Restart => _restart != null ? _restart : (_restart = BuildSdf(RestartSdf));
    static Sprite _medal, _pin, _stopwatch, _spring, _flag, _restart;

    // Navigation / menu line glyphs (Lucide-style strokes) for the Bauhaus UI.
    public static Sprite ArrowLeft => _arrowL != null ? _arrowL : (_arrowL = BuildSdf(p => ArrowSdf(new Vector2(-p.x, p.y))));
    public static Sprite ArrowRight => _arrowR != null ? _arrowR : (_arrowR = BuildSdf(p => ArrowSdf(p)));
    public static Sprite Calculator => _calc != null ? _calc : (_calc = BuildSdf(CalculatorSdf));
    public static Sprite Globe => _globe != null ? _globe : (_globe = BuildSdf(GlobeSdf));
    public static Sprite Book => _book != null ? _book : (_book = BuildSdf(BookSdf));
    public static Sprite Help => _help != null ? _help : (_help = BuildSdf(HelpSdf));
    public static Sprite Close => _close != null ? _close : (_close = BuildSdf(CloseSdf));
    public static Sprite Sliders => _sliders != null ? _sliders : (_sliders = BuildSdf(SlidersSdf));
    public static Sprite User => _user != null ? _user : (_user = BuildSdf(UserSdf));
    static Sprite _arrowL, _arrowR, _calc, _globe, _book, _help, _close, _sliders, _user;

    public static Image Add(RectTransform parent, string name, Sprite sprite, Color tint, Vector2 size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.color = tint;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>
    /// Bauhaus star: earned = yellow fill with an ink rim (reads on paper), unearned = grey line star.
    /// Returns the fill image; toggle it with <see cref="SetStar"/>.
    /// </summary>
    public static Image AddStar(RectTransform parent, string name, bool on, Vector2 size)
    {
        var img = Add(parent, name, Filled, UiKit.Gold, size);
        var rim = Add(img.rectTransform, "Rim", Outline, Bauhaus.Ink, Vector2.zero);
        rim.rectTransform.anchorMin = Vector2.zero;
        rim.rectTransform.anchorMax = Vector2.one;
        rim.rectTransform.offsetMin = Vector2.zero;
        rim.rectTransform.offsetMax = Vector2.zero;
        SetStar(img, on);
        return img;
    }

    public static void SetStar(Image star, bool on)
    {
        if (star == null)
            return;
        star.sprite = on ? Filled : Outline;
        star.color = on ? UiKit.Gold : UiKit.GoldDim;
        var rim = star.transform.Find("Rim");
        if (rim != null)
            rim.gameObject.SetActive(on);
    }

    // ------------------------------------------------------------------ star

    static float StarSdf(Vector2 p, float rOuter, float rInner)
    {
        // Signed distance to a 5-point star polygon (negative inside).
        const int n = 10;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float r = i % 2 == 0 ? rOuter : rInner;
            pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        return PolySdf(p, pts);
    }

    static Sprite BuildStar(bool filled)
    {
        var star = BuildSdf(p =>
        {
            float d = StarSdf(p, 44f, 19f);
            if (filled)
                return d;
            return Mathf.Abs(d + 3f) - 3.2f; // line outline ~6.4px
        });
        star.name = "Star"; // keeps its own tint inside buttons (see Bauhaus.Button)
        return star;
    }

    // ------------------------------------------------------------------ line glyphs

    static float Ring(Vector2 p, Vector2 c, float r, float w) => Mathf.Abs((p - c).magnitude - r) - w * 0.5f;

    static float Segment(Vector2 p, Vector2 a, Vector2 b, float w)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - w * 0.5f;
    }

    static float BulbSdf(Vector2 p)
    {
        const float w = 6f;
        // Upper bulb arc (open at the bottom) + neck lines + two base bars.
        Vector2 c = new Vector2(0f, 10f);
        float ring = Ring(p, c, 24f, w);
        if (p.y < c.y - 14f) ring = 999f;
        float d = ring;
        d = Mathf.Min(d, Segment(p, new Vector2(-19f, -4f), new Vector2(-11f, -20f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(19f, -4f), new Vector2(11f, -20f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-11f, -26f), new Vector2(11f, -26f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-8f, -35f), new Vector2(8f, -35f), w));
        return d;
    }

    static float FlameSdf(Vector2 p)
    {
        const float w = 6f;
        float d = 999f;
        Vector2 prev = Vector2.zero;
        // Teardrop outline: parametric flame.
        for (int i = 0; i <= 48; i++)
        {
            float t = i / 48f * Mathf.PI * 2f;
            float x = Mathf.Sin(t) * (18f + 6f * Mathf.Cos(t));
            float y = -Mathf.Cos(t) * 26f - 6f + (Mathf.Cos(t) < 0f ? -Mathf.Cos(t) * 14f : 0f);
            var q = new Vector2(x, y);
            if (i > 0) d = Mathf.Min(d, Segment(p, prev, q, w));
            prev = q;
        }
        return d;
    }

    static float TrophySdf(Vector2 p)
    {
        const float w = 6f;
        float d = 999f;
        // Cup: two sides + bottom curve, handles, stem, base.
        d = Mathf.Min(d, Segment(p, new Vector2(-22f, 34f), new Vector2(22f, 34f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-22f, 34f), new Vector2(-18f, 6f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(22f, 34f), new Vector2(18f, 6f), w));
        float bowl = Ring(p, new Vector2(0f, 8f), 18f, w);
        if (p.y > 8f) bowl = 999f;
        d = Mathf.Min(d, bowl);
        float hl = Ring(p, new Vector2(-24f, 24f), 9f, w);
        if (p.x > -24f) hl = 999f;
        float hr = Ring(p, new Vector2(24f, 24f), 9f, w);
        if (p.x < 24f) hr = 999f;
        d = Mathf.Min(d, Mathf.Min(hl, hr));
        d = Mathf.Min(d, Segment(p, new Vector2(0f, -10f), new Vector2(0f, -26f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-16f, -30f), new Vector2(16f, -30f), w));
        return d;
    }

    static float ChevronSdf(Vector2 p)
    {
        const float w = 9f;
        float d = Segment(p, new Vector2(-26f, -6f), new Vector2(0f, 20f), w);
        d = Mathf.Min(d, Segment(p, new Vector2(26f, -6f), new Vector2(0f, 20f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-26f, -26f), new Vector2(0f, 0f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(26f, -26f), new Vector2(0f, 0f), w));
        return d;
    }

    static float CalendarSdf(Vector2 p)
    {
        const float w = 6f;
        float d = 999f;
        d = Mathf.Min(d, Segment(p, new Vector2(-30f, 24f), new Vector2(30f, 24f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-30f, -30f), new Vector2(30f, -30f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-30f, 24f), new Vector2(-30f, -30f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(30f, 24f), new Vector2(30f, -30f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-30f, 10f), new Vector2(30f, 10f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-14f, 34f), new Vector2(-14f, 16f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(14f, 34f), new Vector2(14f, 16f), w));
        // Check mark inside.
        d = Mathf.Min(d, Segment(p, new Vector2(-14f, -10f), new Vector2(-4f, -20f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-4f, -20f), new Vector2(16f, 2f), w));
        return d;
    }

    static float MedalSdf(Vector2 p)
    {
        const float w = 6f;
        // Ribbon (two straps meeting the ring) + ring + a small check-style chevron inside.
        float d = Segment(p, new Vector2(-18f, 40f), new Vector2(-6f, 10f), w);
        d = Mathf.Min(d, Segment(p, new Vector2(18f, 40f), new Vector2(6f, 10f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-18f, 40f), new Vector2(18f, 40f), w));
        d = Mathf.Min(d, Ring(p, new Vector2(0f, -12f), 22f, w));
        d = Mathf.Min(d, Segment(p, new Vector2(-9f, -12f), new Vector2(-2f, -19f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-2f, -19f), new Vector2(10f, -5f), w));
        return d;
    }

    static float PinSdf(Vector2 p)
    {
        const float w = 6f;
        // Map pin: round head outline + inner ring + point.
        float head = Ring(p, new Vector2(0f, 12f), 24f, w);
        if (p.y < -4f && Mathf.Abs(p.x) < 20f) head = 999f;
        float d = head;
        d = Mathf.Min(d, Ring(p, new Vector2(0f, 12f), 9f, w));
        d = Mathf.Min(d, Segment(p, new Vector2(-18f, -4f), new Vector2(0f, -38f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(18f, -4f), new Vector2(0f, -38f), w));
        return d;
    }

    static float StopwatchSdf(Vector2 p)
    {
        const float w = 6f;
        float d = Ring(p, new Vector2(0f, -6f), 28f, w);
        d = Mathf.Min(d, Segment(p, new Vector2(-8f, 34f), new Vector2(8f, 34f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(0f, 34f), new Vector2(0f, 24f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(0f, -6f), new Vector2(0f, 12f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(0f, -6f), new Vector2(12f, -14f), w));
        return d;
    }

    static float SpringSdf(Vector2 p)
    {
        const float w = 6f;
        // Coil spring: top and bottom plates + zigzag.
        float d = Segment(p, new Vector2(-30f, 34f), new Vector2(30f, 34f), w);
        d = Mathf.Min(d, Segment(p, new Vector2(-30f, -34f), new Vector2(30f, -34f), w));
        Vector2 prev = new Vector2(0f, -34f);
        for (int i = 1; i <= 7; i++)
        {
            float y = -34f + i * (68f / 8f);
            var q = new Vector2(i % 2 == 1 ? -22f : 22f, y);
            d = Mathf.Min(d, Segment(p, prev, q, w));
            prev = q;
        }
        d = Mathf.Min(d, Segment(p, prev, new Vector2(0f, 34f), w));
        return d;
    }

    static float FlagSdf(Vector2 p)
    {
        const float w = 6f;
        float d = Segment(p, new Vector2(-22f, -40f), new Vector2(-22f, 40f), w);
        d = Mathf.Min(d, Segment(p, new Vector2(-22f, 38f), new Vector2(28f, 26f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(28f, 26f), new Vector2(-22f, 12f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(-34f, -40f), new Vector2(-10f, -40f), w));
        return d;
    }

    static float RestartSdf(Vector2 p)
    {
        const float w = 7f;
        // Circular arrow (open arc) with an arrow head at the top.
        Vector2 c = new Vector2(0f, -4f);
        float ang = Mathf.Atan2(p.y - c.y, p.x - c.x) * Mathf.Rad2Deg;
        float arc = Ring(p, c, 28f, w);
        if (ang > 55f && ang < 115f) arc = 999f;
        float d = arc;
        Vector2 tip = c + new Vector2(Mathf.Cos(55f * Mathf.Deg2Rad), Mathf.Sin(55f * Mathf.Deg2Rad)) * 28f;
        d = Mathf.Min(d, Segment(p, tip, tip + new Vector2(-16f, 6f), w));
        d = Mathf.Min(d, Segment(p, tip, tip + new Vector2(2f, -17f), w));
        return d;
    }

    static float ArrowSdf(Vector2 p)
    {
        const float w = 7f;
        float d = Segment(p, new Vector2(-30f, 0f), new Vector2(30f, 0f), w);
        d = Mathf.Min(d, Segment(p, new Vector2(30f, 0f), new Vector2(8f, 22f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(30f, 0f), new Vector2(8f, -22f), w));
        return d;
    }

    static float RectLines(Vector2 p, float x0, float y0, float x1, float y1, float w)
    {
        float d = Segment(p, new Vector2(x0, y0), new Vector2(x1, y0), w);
        d = Mathf.Min(d, Segment(p, new Vector2(x1, y0), new Vector2(x1, y1), w));
        d = Mathf.Min(d, Segment(p, new Vector2(x1, y1), new Vector2(x0, y1), w));
        d = Mathf.Min(d, Segment(p, new Vector2(x0, y1), new Vector2(x0, y0), w));
        return d;
    }

    static float CalculatorSdf(Vector2 p)
    {
        const float w = 6f;
        float d = RectLines(p, -26f, -36f, 26f, 36f, w);
        d = Mathf.Min(d, RectLines(p, -14f, 12f, 14f, 24f, w));
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 3; c++)
            {
                var k = new Vector2(-13f + c * 13f, -6f - r * 16f);
                d = Mathf.Min(d, Segment(p, k, k + new Vector2(0.01f, 0f), 8f));
            }
        return d;
    }

    static float GlobeSdf(Vector2 p)
    {
        const float w = 6f;
        float d = Ring(p, Vector2.zero, 34f, w);
        d = Mathf.Min(d, Segment(p, new Vector2(-34f, 0f), new Vector2(34f, 0f), w));
        // Meridian: ellipse approximated by a horizontally squashed ring.
        float m = Ring(new Vector2(p.x * 2.1f, p.y), Vector2.zero, 34f, w * 2.1f) / 2.1f;
        d = Mathf.Min(d, m);
        return d;
    }

    static float BookSdf(Vector2 p)
    {
        const float w = 6f;
        Vector2 a = new Vector2(0f, -26f), b = new Vector2(-34f, -20f), c = new Vector2(-34f, 30f), e = new Vector2(0f, 24f);
        float d = Segment(p, a, b, w);
        d = Mathf.Min(d, Segment(p, b, c, w));
        d = Mathf.Min(d, Segment(p, c, e, w));
        d = Mathf.Min(d, Segment(p, e, a, w));
        Vector2 q = new Vector2(-p.x, p.y);
        d = Mathf.Min(d, Segment(q, a, b, w));
        d = Mathf.Min(d, Segment(q, b, c, w));
        d = Mathf.Min(d, Segment(q, c, e, w));
        return d;
    }

    static float HelpSdf(Vector2 p)
    {
        const float w = 6f;
        float d = Ring(p, Vector2.zero, 36f, w);
        Vector2 c = new Vector2(0f, 9f);
        float ang = Mathf.Atan2(p.y - c.y, p.x - c.x) * Mathf.Rad2Deg;
        float hook = Ring(p, c, 11f, w);
        if (ang < -60f && ang > -180f + 1f) hook = 999f;
        d = Mathf.Min(d, hook);
        Vector2 end = c + new Vector2(Mathf.Cos(-60f * Mathf.Deg2Rad), Mathf.Sin(-60f * Mathf.Deg2Rad)) * 11f;
        d = Mathf.Min(d, Segment(p, end, new Vector2(0f, -8f), w));
        d = Mathf.Min(d, Segment(p, new Vector2(0f, -19f), new Vector2(0f, -19.01f), 8f));
        return d;
    }

    static float CloseSdf(Vector2 p)
    {
        const float w = 7f;
        float d = Segment(p, new Vector2(-24f, -24f), new Vector2(24f, 24f), w);
        return Mathf.Min(d, Segment(p, new Vector2(-24f, 24f), new Vector2(24f, -24f), w));
    }

    static float SlidersSdf(Vector2 p)
    {
        const float w = 6f;
        float d = 999f;
        float[] ys = { 24f, 0f, -24f };
        float[] knob = { -12f, 14f, -4f };
        for (int i = 0; i < 3; i++)
        {
            d = Mathf.Min(d, Segment(p, new Vector2(-34f, ys[i]), new Vector2(knob[i] - 9f, ys[i]), w));
            d = Mathf.Min(d, Segment(p, new Vector2(knob[i] + 9f, ys[i]), new Vector2(34f, ys[i]), w));
            d = Mathf.Min(d, Ring(p, new Vector2(knob[i], ys[i]), 7f, w));
        }
        return d;
    }

    static float UserSdf(Vector2 p)
    {
        const float w = 6f;
        float d = Ring(p, new Vector2(0f, 14f), 15f, w);
        float shoulders = Ring(p, new Vector2(0f, -36f), 28f, w);
        if (p.y < -36f) shoulders = 999f;
        return Mathf.Min(d, shoulders);
    }

    // ------------------------------------------------------------------ helpers

    static float PolySdf(Vector2 p, Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]);
        float s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i];
            Vector2 w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
                s *= -1f;
        }
        return s * Mathf.Sqrt(d);
    }

    delegate float Sdf(Vector2 p);

    static Sprite BuildSdf(Sdf sdf)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var px = new Color32[Size * Size];
        float half = Size * 0.5f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                float a = Mathf.Clamp01(0.5f - sdf(p));
                px[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "Glyph"; // Bauhaus.Button recolours "Glyph*" children with the label colour.
        return sprite;
    }
}
