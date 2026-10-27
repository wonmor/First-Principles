using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// -----------------------------------------------------------------------------
// CoachTour — short spotlight walkthrough (3 to 5 steps) for first-time players.
// -----------------------------------------------------------------------------
// Dims everything except the element being explained (four dim panels around a rounded
// focus ring), and shows a card with Skip / Back / Next (Done on the last step).
// Works with touch and mouse (buttons), keyboard (Enter / Space / → next, ← back, Esc skip)
// and controllers (A next via focused button, B skip via OverlayCancelHandler, d-pad moves
// between the card buttons). Used by the world map and by the first level the player opens.
// -----------------------------------------------------------------------------

public sealed class CoachStep
{
    /// <summary>Element to spotlight (null = no spotlight, card centred).</summary>
    public Func<RectTransform> Target;
    public string Title;
    public string Body;
    /// <summary>Runs before the spotlight is placed (e.g. scroll the map to the target).</summary>
    public Action OnEnter;
}

public class CoachTour : MonoBehaviour
{
    const string RootName = "CoachTourRoot";
    static readonly Color Dim = Bauhaus.Scrim;

    List<CoachStep> _steps;
    Action<bool> _onFinish;
    int _index = -1;
    RectTransform _root;
    readonly RectTransform[] _dims = new RectTransform[4];
    RectTransform _ring;
    Image _ringImg;
    RectTransform _card;
    TextMeshProUGUI _title, _body, _counter;
    Button _skip, _back, _next;
    TextMeshProUGUI _nextLabel;
    Coroutine _placing;
    float _lastAction;
    RectTransform _target;

    public static bool IsOpen { get; private set; }

    /// <param name="onFinish">true when the player reached Done, false when skipped.</param>
    public static CoachTour Show(Transform canvas, List<CoachStep> steps, Action<bool> onFinish)
    {
        if (canvas == null || steps == null || steps.Count == 0)
            return null;
        var old = canvas.Find(RootName);
        if (old != null)
            Destroy(old.gameObject);

        var rt = UiKit.Rect(RootName, canvas, Vector2.zero, Vector2.one);
        rt.SetAsLastSibling();
        var tour = rt.gameObject.AddComponent<CoachTour>();
        tour._root = rt;
        tour._steps = steps;
        tour._onFinish = onFinish;
        tour.Build();
        IsOpen = true;
        tour.Go(0);
        return tour;
    }

    void Build()
    {
        for (int i = 0; i < 4; i++)
        {
            var d = UiKit.Rect("Dim" + i, _root, Vector2.zero, Vector2.zero);
            d.pivot = Vector2.zero;
            var img = d.gameObject.AddComponent<Image>();
            img.color = Dim;
            img.raycastTarget = true; // block taps on the rest of the screen
            _dims[i] = d;
        }

        _ring = UiKit.Rect("FocusRing", _root, Vector2.zero, Vector2.zero);
        _ring.pivot = new Vector2(0.5f, 0.5f);
        _ringImg = _ring.gameObject.AddComponent<Image>();
        _ringImg.sprite = Bauhaus.Frame;
        _ringImg.type = Image.Type.Sliced;
        _ringImg.color = Bauhaus.Yellow;
        _ringImg.raycastTarget = false;

        bool tablet = DeviceLayout.IsTabletLike();
        _card = UiKit.Panel(_root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Bauhaus.Card);
        _card.pivot = new Vector2(0.5f, 0.5f);
        _card.sizeDelta = new Vector2(tablet ? 900f : 860f, tablet ? 330f : 310f);
        UiKit.Band(_card, Bauhaus.Yellow, 14f);

        var bulb = StarGlyph.Add(_card, "Glyph", StarGlyph.Bulb, Bauhaus.Ink, new Vector2(44f, 44f));
        bulb.rectTransform.anchorMin = bulb.rectTransform.anchorMax = new Vector2(0f, 1f);
        bulb.rectTransform.pivot = new Vector2(0f, 1f);
        bulb.rectTransform.anchoredPosition = new Vector2(28f, -36f);

        _title = UiKit.Text(_card, "Title", new Vector2(0.1f, 0.72f), new Vector2(0.8f, 0.9f), "",
            UiTypography.Scale(tablet ? 34 : 32), Bauhaus.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        _counter = UiKit.Text(_card, "Counter", new Vector2(0.8f, 0.72f), new Vector2(0.96f, 0.9f), "",
            UiTypography.Scale(20), UiKit.Muted, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        _body = UiKit.Text(_card, "Body", new Vector2(0.05f, 0.3f), new Vector2(0.95f, 0.72f), "",
            UiTypography.Scale(tablet ? 25 : 24), Bauhaus.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        _skip = UiKit.Button(_card, "Skip", new Vector2(0.04f, 0.06f), new Vector2(0.28f, 0.25f),
            LocalizationManager.Get("tour.skip", "Skip"), Bauhaus.Tone.Paper, () => Finish(false), 24);
        _back = UiKit.Button(_card, "Back", new Vector2(0.44f, 0.06f), new Vector2(0.66f, 0.25f),
            LocalizationManager.Get("tour.back", "Back"), Bauhaus.Tone.Paper, () => Go(_index - 1), 24);
        _next = UiKit.Button(_card, "Next", new Vector2(0.68f, 0.06f), new Vector2(0.96f, 0.25f),
            LocalizationManager.Get("tour.next", "Next"), Bauhaus.Tone.Red, () => Advance(), 26);
        _nextLabel = _next.GetComponentInChildren<TextMeshProUGUI>();

        // Controller: B / Esc → Skip (GamepadSupport), focus starts on Next so A advances.
        OverlayCancelHandler.Attach(_root.gameObject, _skip, _next);
    }

    /// <summary>Same as pressing Next / Done (used by the QA screenshot tour).</summary>
    public void Next() => Advance();

    void Advance()
    {
        if (_index >= _steps.Count - 1)
            Finish(true);
        else
            Go(_index + 1);
    }

    void Go(int i)
    {
        if (i < 0 || i >= _steps.Count)
            return;
        _index = i;
        _lastAction = Time.unscaledTime;
        var step = _steps[i];
        try { step.OnEnter?.Invoke(); }
        catch (Exception e) { Debug.LogWarning("CoachTour step: " + e.Message); }

        _title.text = step.Title;
        _body.text = step.Body;
        _counter.text = string.Format(LocalizationManager.Get("tour.counter", "{0} of {1}"), i + 1, _steps.Count);
        _nextLabel.text = i == _steps.Count - 1 ? LocalizationManager.Get("tour.done", "Done") : LocalizationManager.Get("tour.next", "Next");
        _back.gameObject.SetActive(i > 0);
        foreach (var t in new[] { _title, _body, _counter })
            LocalizationManager.ApplyTextDirection(t);

        if (_placing != null)
            StopCoroutine(_placing);
        _placing = StartCoroutine(PlaceAfterLayout(step));
        FeelDirector.Play(SfxId.Click, 0.5f, 1.1f);
        StartCoroutine(FeelDirector.PopScale(_card, 0.3f, 1.05f));
    }

    IEnumerator PlaceAfterLayout(CoachStep step)
    {
        // Let scrolls / layout settle (the map scrolls to the node first).
        yield return null;
        yield return null;
        _target = null;
        try { _target = step.Target?.Invoke(); }
        catch (Exception) { _target = null; }
        Place();
        _placing = null;
    }

    void Place()
    {
        Rect area = _root.rect;
        Rect hole;
        bool has = _target != null && _target.gameObject.activeInHierarchy;
        if (has)
        {
            var corners = new Vector3[4];
            _target.GetWorldCorners(corners);
            Vector2 a = _root.InverseTransformPoint(corners[0]);
            Vector2 b = _root.InverseTransformPoint(corners[2]);
            const float pad = 14f;
            float xMin = Mathf.Max(area.xMin, Mathf.Min(a.x, b.x) - pad);
            float yMin = Mathf.Max(area.yMin, Mathf.Min(a.y, b.y) - pad);
            float xMax = Mathf.Min(area.xMax, Mathf.Max(a.x, b.x) + pad);
            float yMax = Mathf.Min(area.yMax, Mathf.Max(a.y, b.y) + pad);
            hole = Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin + 1f, xMax), Mathf.Max(yMin + 1f, yMax));
        }
        else
        {
            hole = new Rect(area.center, Vector2.zero);
        }

        // Dim panels: bottom, top, left, right of the hole (positions are relative to the root's lower-left).
        Vector2 o = area.min;
        SetDim(0, new Rect(area.xMin, area.yMin, area.width, hole.yMin - area.yMin), o);
        SetDim(1, new Rect(area.xMin, hole.yMax, area.width, area.yMax - hole.yMax), o);
        SetDim(2, new Rect(area.xMin, hole.yMin, hole.xMin - area.xMin, hole.height), o);
        SetDim(3, new Rect(hole.xMax, hole.yMin, area.xMax - hole.xMax, hole.height), o);

        _ring.gameObject.SetActive(has);
        if (has)
        {
            _ring.anchorMin = _ring.anchorMax = Vector2.zero;
            _ring.anchoredPosition = hole.center - o;
            _ring.sizeDelta = hole.size + new Vector2(8f, 8f);
        }

        // Card: on the side of the screen with more room, clear of the hole.
        float cardH = _card.sizeDelta.y;
        float cardW = Mathf.Min(_card.sizeDelta.x, area.width - 40f);
        _card.sizeDelta = new Vector2(cardW, cardH);
        Vector2 pos;
        if (!has)
            pos = area.center;
        else
        {
            float above = area.yMax - hole.yMax;
            float below = hole.yMin - area.yMin;
            float y = above >= below
                ? Mathf.Min(area.yMax - cardH * 0.5f - 16f, hole.yMax + 24f + cardH * 0.5f)
                : Mathf.Max(area.yMin + cardH * 0.5f + 16f, hole.yMin - 24f - cardH * 0.5f);
            if (Mathf.Max(above, below) < cardH + 30f)
            {
                // Not enough room above or below: beside the hole, or over its lower part for big targets.
                float rightRoom = area.xMax - hole.xMax, leftRoom = hole.xMin - area.xMin;
                if (Mathf.Max(rightRoom, leftRoom) >= cardW + 30f)
                {
                    bool right = rightRoom > leftRoom;
                    float x = right ? hole.xMax + 16f + cardW * 0.5f : hole.xMin - 16f - cardW * 0.5f;
                    pos = new Vector2(x, Mathf.Clamp(hole.center.y, area.yMin + cardH * 0.5f + 12f, area.yMax - cardH * 0.5f - 12f));
                }
                else
                {
                    pos = new Vector2(hole.center.x, Mathf.Max(area.yMin + cardH * 0.5f + 16f, hole.yMin + cardH * 0.5f + 24f));
                }
                pos.x = Mathf.Clamp(pos.x, area.xMin + cardW * 0.5f + 12f, area.xMax - cardW * 0.5f - 12f);
            }
            else
            {
                float x = Mathf.Clamp(hole.center.x, area.xMin + cardW * 0.5f + 16f, area.xMax - cardW * 0.5f - 16f);
                pos = new Vector2(x, y);
            }
        }
        _card.anchorMin = _card.anchorMax = Vector2.zero;
        _card.anchoredPosition = pos - o;
        _card.SetAsLastSibling();
    }

    void SetDim(int i, Rect r, Vector2 origin)
    {
        var d = _dims[i];
        d.anchorMin = d.anchorMax = Vector2.zero;
        d.anchoredPosition = r.position - origin;
        d.sizeDelta = new Vector2(Mathf.Max(0f, r.width), Mathf.Max(0f, r.height));
    }

    void Update()
    {
        if (_ringImg != null && _ringImg.gameObject.activeSelf)
        {
            float k = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f);
            _ringImg.color = Bauhaus.WithAlpha(Bauhaus.Yellow, k);
        }
        // Keep the spotlight glued to targets that move (e.g. a bobbing marker or a scrolling map).
        if (_target != null && _placing == null && Time.frameCount % 10 == 0)
            Place();

#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null || Time.unscaledTime - _lastAction < 0.2f)
            return;
        // A button the keyboard can "submit" is already handled by the UI module.
        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        bool uiOwnsSubmit = sel != null && sel.transform.IsChildOf(_root);
        if (!uiOwnsSubmit && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            Advance();
        else if (kb.rightArrowKey.wasPressedThisFrame && !uiOwnsSubmit)
            Advance();
        else if (kb.leftArrowKey.wasPressedThisFrame && !uiOwnsSubmit)
            Go(_index - 1);
        // Esc is routed by GamepadSupport to the overlay's cancel button (Skip).
#endif
    }

    void Finish(bool completed)
    {
        IsOpen = false;
        var cb = _onFinish;
        _onFinish = null;
        Destroy(gameObject);
        cb?.Invoke(completed);
    }

    void OnDestroy()
    {
        if (_onFinish != null)
        {
            IsOpen = false;
            var cb = _onFinish;
            _onFinish = null;
            cb(false);
        }
    }
}
