using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Game scene: invisible full-screen touch layer (first under safe-area HUD). Hold <b>left half</b> → move left,
/// hold <b>right half</b> → move right; release with a dominant <b>upward swipe</b> → jump. Replaces virtual ◀ ▶ Jump buttons.
/// </summary>
public class GameplayScreenTouchZones : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private static GameplayScreenTouchZones _instance;

    private readonly Dictionary<int, Vector2> _pointerDownScreen = new Dictionary<int, Vector2>();
    private readonly Dictionary<int, int> _pointerSide = new Dictionary<int, int>();

    public static void EnsureForGameCanvas(Transform canvasTransform)
    {
        if (canvasTransform == null || !DeviceLayout.PreferOnScreenGameControls)
            return;

        DestroyLegacyButtonBar(canvasTransform);

        if (_instance != null)
            return;

        Transform existing = canvasTransform.Find("GameplayScreenTouchZonesRoot");
        if (existing == null)
        {
            var safe = canvasTransform.Find(MobileUiRoots.SafeContentName);
            if (safe != null)
                existing = safe.Find("GameplayScreenTouchZonesRoot");
        }

        if (existing != null)
        {
            _instance = existing.GetComponent<GameplayScreenTouchZones>();
            return;
        }

        var parentRt = MobileUiRoots.GetSafeContentParent(canvasTransform) ?? canvasTransform as RectTransform;
        if (parentRt == null)
            return;

        var go = new GameObject("GameplayScreenTouchZonesRoot", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parentRt, false);
        rt.SetAsFirstSibling();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;

        _instance = go.AddComponent<GameplayScreenTouchZones>();
    }

    private static void DestroyLegacyButtonBar(Transform canvasTransform)
    {
        Transform t = canvasTransform.Find("MobileTouchControlsRoot");
        if (t == null)
        {
            var safe = canvasTransform.Find(MobileUiRoots.SafeContentName);
            if (safe != null)
                t = safe.Find("MobileTouchControlsRoot");
        }

        if (t != null)
            UnityEngine.Object.Destroy(t.gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
        MobileInputBridge.ClearTouchRouting();
    }

    private static float MinSwipeUpPixels()
    {
        float dpi = Screen.dpi > 1f ? Screen.dpi : 163f;
        return Mathf.Clamp(48f * (dpi / 160f), 40f, 100f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        int id = eventData.pointerId;
        Vector2 pos = eventData.position;
        _pointerDownScreen[id] = pos;
        float mid = Screen.width * 0.5f;
        int side = pos.x < mid ? -1 : 1;
        _pointerSide[id] = side;
        if (side < 0)
            MobileHoldAxis.PressLeft();
        else
            MobileHoldAxis.PressRight();
    }

    public void OnPointerUp(PointerEventData eventData) =>
        EndPointer(eventData.pointerId, evaluateSwipeJump: true, eventData.position);

    public void OnPointerExit(PointerEventData eventData) =>
        EndPointer(eventData.pointerId, evaluateSwipeJump: false, eventData.position);

    private void EndPointer(int pointerId, bool evaluateSwipeJump, Vector2 releasePosition)
    {
        if (!_pointerSide.TryGetValue(pointerId, out int side))
            return;

        if (evaluateSwipeJump && _pointerDownScreen.TryGetValue(pointerId, out Vector2 down))
        {
            Vector2 delta = releasePosition - down;
            float minUp = MinSwipeUpPixels();
            if (delta.y > minUp && delta.y > Mathf.Abs(delta.x) * 0.55f)
                MobileInputBridge.QueueJump();
        }

        _pointerDownScreen.Remove(pointerId);
        if (side < 0)
            MobileHoldAxis.ReleaseLeft();
        else
            MobileHoldAxis.ReleaseRight();
        _pointerSide.Remove(pointerId);
    }
}
