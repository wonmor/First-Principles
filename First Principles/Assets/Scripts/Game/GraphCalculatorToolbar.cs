using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Faxas-style graphing: <b>Trans</b> edits transformation parameters; <b>Scale</b> zooms the window in/out.
/// Trans: short tap cycles A → k → C → D; double-tap nudges +; hold (~½s) nudges −.
/// Scale: short tap zoom in; hold zoom out. Pair with <see cref="GraphPinchZoom"/> for pinch.
/// </summary>
public class GraphCalculatorToolbar : MonoBehaviour
{
    [SerializeField] private float longPressSeconds = 0.48f;
    [SerializeField] private float doubleTapWindow = 0.38f;
    [SerializeField] private float paramStep = 0.1f;
    [SerializeField] private float kStep = 0.06f;
    [SerializeField] private float zoomTapFactor = 0.86f;

    private FunctionPlotter plot;
    private TextMeshProUGUI hint;
    private int paramIndex;
    private float transLastShortTime = -1f;

    public void Configure(FunctionPlotter functionPlotter, Button transBtn, Button scaleBtn, TextMeshProUGUI parameterHint)
    {
        plot = functionPlotter;
        hint = parameterHint;

        if (transBtn != null)
        {
            transBtn.onClick.RemoveAllListeners();
            AttachPressSplit(transBtn.gameObject, OnTransShort, OnTransLong);
        }

        if (scaleBtn != null)
        {
            scaleBtn.onClick.RemoveAllListeners();
            AttachPressSplit(scaleBtn.gameObject, OnScaleShort, OnScaleLong);
        }

        RefreshHint();
    }

    private void AttachPressSplit(GameObject go, System.Action shortRel, System.Action longRel)
    {
        var h = go.GetComponent<UiShortLongPress>();
        if (h == null)
            h = go.AddComponent<UiShortLongPress>();
        h.longPressThreshold = longPressSeconds;
        h.onShortRelease = shortRel;
        h.onLongRelease = longRel;
    }

    private void OnTransShort()
    {
        if (plot == null)
            return;

        if (transLastShortTime > 0f && Time.time - transLastShortTime < doubleTapWindow)
        {
            NudgeParam(+1);
            transLastShortTime = -1f;
            return;
        }

        transLastShortTime = Time.time;
        paramIndex = (paramIndex + 1) % 4;
        RefreshHint();
    }

    private void OnTransLong()
    {
        if (plot == null)
            return;
        NudgeParam(-1);
    }

    private void NudgeParam(int sign)
    {
        float s = sign > 0 ? 1f : -1f;
        switch (paramIndex)
        {
            case 0:
                plot.transA = ClampGeneric(plot.transA + s * paramStep);
                break;
            case 1:
                plot.transK = Mathf.Clamp(plot.transK + s * kStep, 0.02f, 6f);
                break;
            case 2:
                plot.transC = ClampGeneric(plot.transC + s * paramStep);
                break;
            case 3:
                plot.transD = ClampGeneric(plot.transD + s * paramStep);
                break;
        }

        RefreshHint();
    }

    private static float ClampGeneric(float v)
    {
        return Mathf.Clamp(v, -12f, 12f);
    }

    private void OnScaleShort() => ApplyZoomWindow(zoomTapFactor);

    private void OnScaleLong() => ApplyZoomWindow(1f / zoomTapFactor);

    private void ApplyZoomWindow(float halfWidthMultiplier)
    {
        if (plot == null)
            return;
        float mid = (plot.xStart + plot.xEnd) * 0.5f;
        float half = (plot.xEnd - plot.xStart) * 0.5f * halfWidthMultiplier;
        half = Mathf.Clamp(half, 0.35f, 160f);
        plot.xStart = mid - half;
        plot.xEnd = mid + half;
        plot.step = Mathf.Clamp((plot.xEnd - plot.xStart) / 480f, 0.004f, 0.42f);
    }

    private void RefreshHint()
    {
        if (hint == null || plot == null)
            return;

        string[] names =
        {
            "A (vertical scale)",
            "k (horizontal scale)",
            "C (vertical shift)",
            "D (horizontal shift)"
        };
        hint.richText = true;
        hint.alignment = TextAlignmentOptions.Center;
        hint.text =
            $"<b>Faxas Instruments-style graphing</b>\n" +
            $"<size=92%><b>Trans</b> → {names[paramIndex]} · double-tap <b>+</b> · hold <b>−</b> · " +
            $"<b>Scale</b> zoom in / hold zoom out · two-finger <b>pinch</b></size>\n" +
            $"<size=88%><color=#a8b2d1>A={plot.transA:0.##} &nbsp;k={plot.transK:0.##} &nbsp;C={plot.transC:0.##} &nbsp;D={plot.transD:0.##} &nbsp;&nbsp;" +
            $"x∈[{plot.xStart:0.##},{plot.xEnd:0.##}]</color></size>";
    }
}

/// <summary>Short release vs long release on the same <see cref="Button"/> without using onClick.</summary>
public class UiShortLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public float longPressThreshold = 0.48f;
    public System.Action onShortRelease;
    public System.Action onLongRelease;

    private float downTime;
    private bool down;

    public void OnPointerDown(PointerEventData eventData)
    {
        downTime = Time.time;
        down = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!down)
            return;
        down = false;
        float dt = Time.time - downTime;
        if (dt >= longPressThreshold)
            onLongRelease?.Invoke();
        else
            onShortRelease?.Invoke();
    }
}