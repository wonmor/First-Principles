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
        return BuildSdf(p =>
        {
            float d = StarSdf(p, 44f, 19f);
            if (filled)
                return d;
            return Mathf.Abs(d + 3f) - 3.2f; // line outline ~6.4px
        });
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
        return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
    }
}
