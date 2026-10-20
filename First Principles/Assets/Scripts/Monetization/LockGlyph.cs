using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural padlock sprite (white RGBA, tint via <see cref="Image.color"/>), in the same
/// no-asset style as <see cref="RuntimeUiPolish"/>. Works with every font/locale (no emoji glyph needed).
/// </summary>
public static class LockGlyph
{
    const int Size = 64;
    static Sprite _locked;
    static Sprite _unlocked;

    public static Sprite Locked => _locked != null ? _locked : (_locked = Build(open: false));
    public static Sprite Unlocked => _unlocked != null ? _unlocked : (_unlocked = Build(open: true));

    /// <summary>Adds a padlock <see cref="Image"/> anchored to the right edge of <paramref name="parent"/>.</summary>
    public static Image AddToRow(RectTransform parent, float size, Color tint, bool open = false)
    {
        var go = new GameObject("LockGlyph");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = new Vector2(-18f, 0f);
        var img = go.AddComponent<Image>();
        img.sprite = open ? Unlocked : Locked;
        img.preserveAspect = true;
        img.color = tint;
        img.raycastTarget = false;
        return img;
    }

    static Sprite Build(bool open)
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        // Body: rounded rect in the lower part. Shackle: ring segment (upper half circle + legs).
        const float bodyX0 = 10f, bodyX1 = 54f, bodyY0 = 4f, bodyY1 = 34f, bodyR = 6f;
        const float cx = 32f, shackleR = 14f, thick = 5.5f;
        float shackleBaseY = bodyY1 - 2f;
        float shackleCy = 44f;
        float openShift = open ? 9f : 0f; // open lock: shackle lifted and right leg cut

        var keyhole = new Vector2(32f, 21f);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float a = 0f;

                // body
                float dx = Mathf.Max(bodyX0 + bodyR - px, 0f, px - (bodyX1 - bodyR));
                float dy = Mathf.Max(bodyY0 + bodyR - py, 0f, py - (bodyY1 - bodyR));
                float dBody = Mathf.Sqrt(dx * dx + dy * dy) - bodyR;
                a = Mathf.Max(a, Mathf.Clamp01(0.5f - dBody));

                // shackle arc (upper half)
                float sy = py - openShift;
                float ringD = Mathf.Abs(Vector2.Distance(new Vector2(px, sy), new Vector2(cx, shackleCy)) - shackleR) - thick * 0.5f;
                if (sy >= shackleCy)
                    a = Mathf.Max(a, Mathf.Clamp01(0.5f - ringD));

                // shackle legs
                float legHalf = thick * 0.5f;
                bool inLegY = sy < shackleCy && py > shackleBaseY - 1f;
                if (inLegY)
                {
                    float dl = Mathf.Abs(px - (cx - shackleR)) - legHalf;
                    a = Mathf.Max(a, Mathf.Clamp01(0.5f - dl));
                    if (!open)
                    {
                        float dr = Mathf.Abs(px - (cx + shackleR)) - legHalf;
                        a = Mathf.Max(a, Mathf.Clamp01(0.5f - dr));
                    }
                }

                // keyhole cut-out
                float dk = Vector2.Distance(new Vector2(px, py), keyhole) - 4f;
                bool slot = Mathf.Abs(px - keyhole.x) < 1.6f && py < keyhole.y && py > keyhole.y - 9f;
                if (dk < 0f || slot)
                    a *= 0.15f;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
    }
}
