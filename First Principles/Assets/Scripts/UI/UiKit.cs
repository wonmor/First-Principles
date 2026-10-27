using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small builders shared by the runtime overlays (level complete, achievements, paywall, daily card)
/// so they share the <see cref="Bauhaus"/> look (paper sheets, black rules, flat primary buttons)
/// and play the UI click sound.
/// </summary>
public static class UiKit
{
    /// <summary>Earned-star fill (always drawn with an ink rim, see <see cref="StarGlyph.SetStar"/>).</summary>
    public static readonly Color Gold = Bauhaus.Yellow;
    /// <summary>Unearned star / locked glyph.</summary>
    public static readonly Color GoldDim = Bauhaus.RuleStrong;
    /// <summary>Positive / informational accent (was mint): Bauhaus blue.</summary>
    public static readonly Color Mint = Bauhaus.Blue;
    /// <summary>Secondary text on paper.</summary>
    public static readonly Color Muted = Bauhaus.InkSoft;
    /// <summary>Plus subscription accent (was violet): Bauhaus blue.</summary>
    public static readonly Color PlusViolet = Bauhaus.Blue;
    /// <summary>Highlight text / badges (was gold text, unreadable on paper): Bauhaus red.</summary>
    public static readonly Color Accent = Bauhaus.Red;

    public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    public static RectTransform FullScreenDim(string name, Transform canvas, float alpha, Action onTapOutside)
    {
        var rt = Rect(name, canvas, Vector2.zero, Vector2.one);
        rt.SetAsLastSibling();
        var dim = rt.gameObject.AddComponent<Image>();
        // Flat ink scrim (alpha kept proportional to the caller's old dim strength).
        dim.color = Bauhaus.WithAlpha(Bauhaus.Scrim, Mathf.Clamp01(alpha * 0.72f));
        dim.raycastTarget = true;
        if (onTapOutside != null)
        {
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = dim;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => onTapOutside());
        }
        return rt;
    }

    public static RectTransform Panel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Color? color = null)
    {
        var rt = Rect("Panel", parent, anchorMin, anchorMax);
        var img = rt.gameObject.AddComponent<Image>();
        Bauhaus.Panel(img, color ?? Bauhaus.Card);
        img.raycastTarget = true;
        // Swallow taps so they don't reach the dim layer.
        var swallow = rt.gameObject.AddComponent<Button>();
        swallow.targetGraphic = img;
        swallow.transition = Selectable.Transition.None;
        return rt;
    }

    /// <summary>Solid colour bar (Bauhaus header band / rule) across the top of a sheet.</summary>
    public static RectTransform Band(RectTransform panel, Color color, float height, float inset = 0f)
    {
        var band = Rect("Band", panel, new Vector2(0f, 1f), new Vector2(1f, 1f));
        band.pivot = new Vector2(0.5f, 1f);
        band.sizeDelta = new Vector2(-inset * 2f, height);
        band.anchoredPosition = new Vector2(0f, -inset);
        var img = band.gameObject.AddComponent<Image>();
        img.sprite = Bauhaus.Square;
        img.color = color;
        img.raycastTarget = false;
        return band;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        string text, float size, Color color, FontStyles style = FontStyles.Normal,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var rt = Rect(name, parent, anchorMin, anchorMax);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        UiTypography.ApplyDefaultFontAsset(tmp);
        tmp.text = text;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = size;
        tmp.fontSizeMin = Mathf.Max(11f, size * 0.5f);
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = color;
        tmp.richText = true;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        LocalizationManager.ApplyTextDirection(tmp);
        return tmp;
    }

    /// <param name="bg">Button colour; mapped to a Bauhaus tone (red / blue / yellow / ink / paper).</param>
    /// <param name="textColor">Ignored since 1.4: labels always take the tone's contrast colour.</param>
    public static Button Button(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        string label, Color bg, Action onClick, float fontSize = 26f, Color? textColor = null)
        => Button(parent, name, anchorMin, anchorMax, label, Bauhaus.ToneFor(bg), onClick, fontSize);

    public static Button Button(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        string label, Bauhaus.Tone tone, Action onClick, float fontSize = 26f)
    {
        var rt = Rect(name, parent, anchorMin, anchorMax);
        var img = rt.gameObject.AddComponent<Image>();
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        Text(rt, "Text", new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), label,
            UiTypography.Scale(fontSize), Bauhaus.OnFill(tone), FontStyles.Bold);
        Bauhaus.Button(btn, tone);
        btn.onClick.AddListener(() =>
        {
            FeelDirector.Play(SfxId.Click, 0.6f);
            onClick?.Invoke();
        });
        return btn;
    }

    /// <summary>Row of three stars (earned: yellow with an ink rim; unearned: grey outline).</summary>
    public static Image[] Stars(Transform parent, Vector2 anchorMin, Vector2 anchorMax, int earned, float size)
    {
        var row = Rect("Stars", parent, anchorMin, anchorMax);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleCenter;
        h.spacing = size * 0.12f;
        h.childControlWidth = false;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        var imgs = new Image[ProgressRules.MaxStars];
        for (int i = 0; i < imgs.Length; i++)
        {
            bool on = i < earned;
            imgs[i] = StarGlyph.AddStar(row, "Star" + i, on, new Vector2(size, size));
        }
        return imgs;
    }

    public static void OpenUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
            Application.OpenURL(url);
    }
}
