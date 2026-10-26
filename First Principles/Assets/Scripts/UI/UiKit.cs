using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small builders shared by the runtime overlays (level complete, achievements, paywall, daily card)
/// so they match <see cref="RuntimeUiPolish"/> styling and play the UI click sound.
/// </summary>
public static class UiKit
{
    public static readonly Color Gold = new Color(1f, 0.82f, 0.36f, 1f);
    public static readonly Color GoldDim = new Color(0.55f, 0.58f, 0.68f, 0.55f);
    public static readonly Color Mint = new Color(0.55f, 0.88f, 0.8f, 1f);
    public static readonly Color Muted = new Color(0.72f, 0.77f, 0.88f, 0.95f);
    public static readonly Color PlusViolet = new Color(0.46f, 0.38f, 0.92f, 0.98f);

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
        dim.color = new Color(0.04f, 0.05f, 0.1f, alpha);
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
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = color ?? RuntimeUiPolish.PanelMid;
        img.raycastTarget = true;
        // Swallow taps so they don't reach the dim layer.
        var swallow = rt.gameObject.AddComponent<Button>();
        swallow.targetGraphic = img;
        swallow.transition = Selectable.Transition.None;
        RuntimeUiPolish.ApplyDropShadow(rt, new Vector2(3f, -5f), 0.34f);
        return rt;
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

    public static Button Button(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        string label, Color bg, Action onClick, float fontSize = 26f, Color? textColor = null)
    {
        var rt = Rect(name, parent, anchorMin, anchorMax);
        var img = rt.gameObject.AddComponent<Image>();
        RuntimeUiPolish.UseRoundedSliced(img);
        img.color = bg;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        RuntimeUiPolish.ApplyButtonTransitions(btn, bg, Color.Lerp(bg, Color.white, 0.18f), Color.Lerp(bg, Color.black, 0.22f));
        RuntimeUiPolish.ApplyDropShadow(rt, new Vector2(2f, -3f), 0.26f);
        Text(rt, "Text", new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), label,
            UiTypography.Scale(fontSize), textColor ?? RuntimeUiPolish.TitleIvory, FontStyles.Bold);
        btn.onClick.AddListener(() =>
        {
            FeelDirector.Play(SfxId.Click, 0.6f);
            onClick?.Invoke();
        });
        return btn;
    }

    /// <summary>Row of three stars (earned filled gold, unearned outline).</summary>
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
            imgs[i] = StarGlyph.Add(row, "Star" + i, on ? StarGlyph.Filled : StarGlyph.Outline, on ? Gold : GoldDim, new Vector2(size, size));
        }
        return imgs;
    }

    public static void OpenUrl(string url)
    {
        if (!string.IsNullOrEmpty(url))
            Application.OpenURL(url);
    }
}
