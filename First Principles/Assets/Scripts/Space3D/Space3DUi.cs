using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// -----------------------------------------------------------------------------
// Space3DUi: Bauhaus controls built at runtime for the 3D screens (buttons with line glyphs,
// sliders, expression fields). Everything goes through Bauhaus / UiKit so it matches 1.4.
// -----------------------------------------------------------------------------

public static class Space3DUi
{
    public static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 offMin = default, Vector2 offMax = default)
    {
        var rt = UiKit.Rect(name, parent, aMin, aMax);
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
        return rt;
    }

    /// <summary>Fixed-size rect pinned to an anchor point.</summary>
    public static RectTransform Pinned(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rt = UiKit.Rect(name, parent, anchor, anchor);
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static Image Card(RectTransform rt, Color? fill = null, float rule = Bauhaus.PanelRule)
    {
        var img = rt.gameObject.AddComponent<Image>();
        Bauhaus.Panel(img, fill ?? Bauhaus.Card, rule);
        img.raycastTarget = true;
        return img;
    }

    public static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var t = UiKit.Text(parent, name, Vector2.zero, Vector2.one, text, UiTypography.Scale(size), color, style, align);
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    /// <summary>Flat Bauhaus button with an optional line glyph on the left and a label.</summary>
    public static Button Button(RectTransform rt, string label, Bauhaus.Tone tone, Sprite glyph, Action onClick,
        float fontSize = 24f, float glyphSize = 34f)
    {
        var img = rt.gameObject.AddComponent<Image>();
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var text = UiKit.Text(rt, "Text", Vector2.zero, Vector2.one, label ?? "", UiTypography.Scale(fontSize),
            Bauhaus.OnFill(tone), FontStyles.Bold, glyph != null && !string.IsNullOrEmpty(label)
                ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.offsetMin = new Vector2(10f, 4f);
        text.rectTransform.offsetMax = new Vector2(-10f, -4f);
        if (glyph != null)
        {
            if (string.IsNullOrEmpty(label))
            {
                var g = StarGlyph.Add(rt, "Glyph", glyph, Bauhaus.OnFill(tone), new Vector2(glyphSize, glyphSize));
                g.rectTransform.anchorMin = g.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            }
            else
                Bauhaus.LeadGlyph(rt, text, glyph, glyphSize, 12f);
        }

        Bauhaus.Button(btn, tone);
        btn.onClick.AddListener(() =>
        {
            FeelDirector.Play(SfxId.Click, 0.6f);
            onClick?.Invoke();
        });
        return btn;
    }

    public static void SetLabel(Button b, string text)
    {
        var t = b != null ? b.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (t != null)
            t.text = text;
    }

    public static void SetTone(Button b, Bauhaus.Tone tone)
    {
        if (b == null)
            return;
        Bauhaus.Button(b, tone);
        foreach (var img in b.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject != b.gameObject && img.name == "Glyph")
                img.color = Bauhaus.OnFill(tone);
        }
    }

    /// <summary>Horizontal slider: ink track, yellow fill, ink block handle (all flat, Bauhaus).</summary>
    public static Slider Slider(RectTransform rt, float min, float max, float value, Action<float> onChange)
    {
        var bg = UiKit.Rect("Track", rt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        bg.sizeDelta = new Vector2(0f, 10f);
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.sprite = Bauhaus.Square;
        bgImg.color = Bauhaus.Ink;

        var fillArea = UiKit.Rect("Fill Area", rt, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
        fillArea.sizeDelta = new Vector2(0f, 10f);
        var fill = UiKit.Rect("Fill", fillArea, Vector2.zero, Vector2.one);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.sprite = Bauhaus.Square;
        fillImg.color = Bauhaus.Yellow;

        var handleArea = UiKit.Rect("Handle Slide Area", rt, Vector2.zero, Vector2.one);
        handleArea.offsetMin = new Vector2(14f, 0f);
        handleArea.offsetMax = new Vector2(-14f, 0f);
        var handle = UiKit.Rect("Handle", handleArea, new Vector2(0f, 0f), new Vector2(0f, 1f));
        handle.sizeDelta = new Vector2(28f, -6f);
        var hImg = handle.gameObject.AddComponent<Image>();
        hImg.sprite = Bauhaus.Square;
        hImg.color = Bauhaus.Ink;

        // Generous invisible hit area so fingers can grab it.
        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(0, 0, 0, 0);

        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill;
        s.handleRect = handle;
        s.targetGraphic = hImg;
        s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        s.minValue = min;
        s.maxValue = max;
        s.value = value;
        var c = s.colors;
        c.normalColor = Bauhaus.Ink;
        c.highlightedColor = Bauhaus.Red;
        c.selectedColor = Bauhaus.Red;
        c.pressedColor = Bauhaus.Red;
        c.colorMultiplier = 1f;
        c.fadeDuration = 0f;
        s.colors = c;
        hImg.color = Color.white;
        s.onValueChanged.AddListener(v => onChange?.Invoke(v));
        return s;
    }

    public static TMP_InputField Input(RectTransform rt, string text, string placeholder, float fontSize,
        Action<string> onChanged, Action<string> onSubmit)
    {
        var bg = rt.gameObject.AddComponent<Image>();
        Bauhaus.Panel(bg, Color.white, 2f);

        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(rt, false);
        var art = area.GetComponent<RectTransform>();
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(12f, 6f);
        art.offsetMax = new Vector2(-12f, -6f);

        TextMeshProUGUI Make(string name, string s, Color col, FontStyles style)
        {
            var go = new GameObject(name);
            var trt = go.AddComponent<RectTransform>();
            trt.SetParent(area.transform, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            UiTypography.ApplyDefaultFontAsset(t);
            t.text = s;
            t.fontSize = UiTypography.Scale(fontSize);
            t.color = col;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.richText = false;
            return t;
        }

        var textMesh = Make("Text", text, Bauhaus.Ink, FontStyles.Bold);
        var ph = Make("Placeholder", placeholder, Bauhaus.WithAlpha(Bauhaus.InkSoft, 0.7f), FontStyles.Normal);

        var input = rt.gameObject.AddComponent<TMP_InputField>();
        input.textComponent = textMesh;
        input.placeholder = ph;
        input.textViewport = art;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterValidation = TMP_InputField.CharacterValidation.None;
        input.richText = false;
        input.text = text;
        input.targetGraphic = bg;
        if (onChanged != null)
            input.onValueChanged.AddListener(s => onChanged(s));
        if (onSubmit != null)
        {
            input.onSubmit.AddListener(s => onSubmit(s));
            input.onEndEdit.AddListener(s => onSubmit(s));
        }

        return input;
    }

    /// <summary>"0.75" / "−1.5": short number with a real minus sign for labels.</summary>
    public static string Num(float v)
    {
        if (Mathf.Abs(v) < 5e-4f)
            v = 0f;
        string s = Mathf.Abs(v) >= 100f ? v.ToString("0") : Mathf.Abs(v - Mathf.Round(v)) < 5e-4f ? v.ToString("0")
            : v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return s.Replace('-', '−');
    }
}
