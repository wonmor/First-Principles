using System.Collections.Generic;
using TMPro;
using UnityEngine;
using NV3 = System.Numerics.Vector3;

// -----------------------------------------------------------------------------
// Space3DAxes: x / y / z axes (red / blue / ink tubes), the floor grid and screen-space labels
// (axis names and tick numbers) that track the 3D positions every frame.
// -----------------------------------------------------------------------------

public class Space3DAxes : MonoBehaviour
{
    public static readonly Color XColor = Bauhaus.Red;
    public static readonly Color YColor = Bauhaus.Blue;
    public static readonly Color ZColor = Bauhaus.Ink;

    Camera cam;
    RectTransform labelRoot;
    Canvas canvas;
    Box3 box;
    MeshFilter axesMf, gridMf;
    readonly List<(Vector3 world, TextMeshProUGUI tmp)> labels = new List<(Vector3, TextMeshProUGUI)>();
    readonly Stack<TextMeshProUGUI> pool = new Stack<TextMeshProUGUI>();

    public bool ShowTicks = true;

    public void Init(Camera camera, Canvas c, RectTransform labelsParent)
    {
        cam = camera;
        canvas = c;
        labelRoot = labelsParent;
        Space3DRender.NewMeshObject("Axes", transform, Space3DRender.Lit, out axesMf);
        Space3DRender.NewMeshObject("Grid", transform, Space3DRender.Unlit, out gridMf);
    }

    public static float TickStep(float half) => half <= 3f ? 1f : half <= 7f ? 1f : half <= 12f ? 2f : half <= 30f ? 5f : 10f;

    public void Rebuild(Box3 b, bool showGrid = true)
    {
        box = b;
        float size = b.Size;
        float r = Mathf.Clamp(size * 0.0035f, 0.012f, 0.12f);
        var v = new List<Vector3>();
        var n = new List<Vector3>();
        var c = new List<Color>();
        var t = new List<int>();
        float z0 = Mathf.Clamp(0f, b.ZMin, b.ZMax);
        float x0 = Mathf.Clamp(0f, b.XMin, b.XMax), y0 = Mathf.Clamp(0f, b.YMin, b.YMax);
        Space3DRender.AppendTube(new List<NV3> { new NV3(b.XMin, y0, z0), new NV3(b.XMax, y0, z0) }, r, XColor, 6, v, n, c, t);
        Space3DRender.AppendTube(new List<NV3> { new NV3(x0, b.YMin, z0), new NV3(x0, b.YMax, z0) }, r, YColor, 6, v, n, c, t);
        Space3DRender.AppendTube(new List<NV3> { new NV3(x0, y0, b.ZMin), new NV3(x0, y0, b.ZMax) }, r, ZColor, 6, v, n, c, t);
        float step = TickStep(Mathf.Max(b.XMax, -b.XMin));
        float tick = r * 4f;
        for (float x = Mathf.Ceil(b.XMin / step) * step; x <= b.XMax + 1e-4f; x += step)
        {
            if (Mathf.Abs(x) < 1e-4f) continue;
            Space3DRender.AppendTube(new List<NV3> { new NV3(x, y0, z0 - tick), new NV3(x, y0, z0 + tick) }, r * 0.8f, XColor, 4, v, n, c, t);
        }

        for (float y = Mathf.Ceil(b.YMin / step) * step; y <= b.YMax + 1e-4f; y += step)
        {
            if (Mathf.Abs(y) < 1e-4f) continue;
            Space3DRender.AppendTube(new List<NV3> { new NV3(x0, y, z0 - tick), new NV3(x0, y, z0 + tick) }, r * 0.8f, YColor, 4, v, n, c, t);
        }

        float zstep = TickStep(Mathf.Max(b.ZMax, -b.ZMin));
        for (float z = Mathf.Ceil(b.ZMin / zstep) * zstep; z <= b.ZMax + 1e-4f; z += zstep)
        {
            if (Mathf.Abs(z) < 1e-4f) continue;
            Space3DRender.AppendTube(new List<NV3> { new NV3(x0 - tick, y0, z), new NV3(x0 + tick, y0, z) }, r * 0.8f, ZColor, 4, v, n, c, t);
        }

        Space3DRender.FillLists(axesMf.sharedMesh, v, n, c, t);
        if (showGrid)
            Space3DRender.FillGrid(gridMf.sharedMesh, b, step, z0, Bauhaus.WithAlpha(Bauhaus.RuleStrong, 0.55f), Bauhaus.WithAlpha(Bauhaus.InkSoft, 0.6f));
        else
            gridMf.sharedMesh.Clear();

        // Labels.
        foreach (var l in labels)
        {
            l.tmp.gameObject.SetActive(false);
            pool.Push(l.tmp);
        }

        labels.Clear();
        float pad = size * 0.045f;
        AddLabel(new NV3(b.XMax + pad, y0, z0), "x", XColor, 34, true);
        AddLabel(new NV3(x0, b.YMax + pad, z0), "y", YColor, 34, true);
        AddLabel(new NV3(x0, y0, b.ZMax + pad), "z", ZColor, 34, true);
        if (ShowTicks)
        {
            for (float x = Mathf.Ceil(b.XMin / step) * step; x <= b.XMax + 1e-4f; x += step)
                if (Mathf.Abs(x) > 1e-4f && Mathf.Abs(x - b.XMax) > 1e-4f)
                    AddLabel(new NV3(x, y0, z0 - tick * 4f), Space3DUi.Num(x), XColor, 20, false);
            for (float y = Mathf.Ceil(b.YMin / step) * step; y <= b.YMax + 1e-4f; y += step)
                if (Mathf.Abs(y) > 1e-4f && Mathf.Abs(y - b.YMax) > 1e-4f)
                    AddLabel(new NV3(x0, y, z0 - tick * 4f), Space3DUi.Num(y), YColor, 20, false);
            for (float z = Mathf.Ceil(b.ZMin / zstep) * zstep; z <= b.ZMax + 1e-4f; z += zstep)
                if (Mathf.Abs(z) > 1e-4f && Mathf.Abs(z - b.ZMax) > 1e-4f)
                    AddLabel(new NV3(x0 - tick * 5f, y0, z), Space3DUi.Num(z), ZColor, 20, false);
        }
    }

    void AddLabel(NV3 p, string text, Color color, float size, bool bold)
    {
        TextMeshProUGUI tmp;
        if (pool.Count > 0)
        {
            tmp = pool.Pop();
            tmp.gameObject.SetActive(true);
        }
        else
        {
            tmp = UiKit.Text(labelRoot, "AxisLabel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "", 20, color);
            tmp.rectTransform.sizeDelta = new Vector2(90f, 44f);
            tmp.enableAutoSizing = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
        }

        tmp.text = text;
        tmp.color = color;
        tmp.fontSize = UiTypography.Scale(size);
        tmp.fontStyle = bold ? FontStyles.Bold | FontStyles.Italic : FontStyles.Normal;
        labels.Add((Space3DRender.ToWorld(p), tmp));
    }

    public void SetLabelsVisible(bool on)
    {
        if (labelRoot != null)
            labelRoot.gameObject.SetActive(on);
    }

    void LateUpdate()
    {
        if (cam == null || labelRoot == null || !labelRoot.gameObject.activeInHierarchy)
            return;
        var parent = labelRoot;
        foreach (var (world, tmp) in labels)
        {
            var sp = cam.WorldToScreenPoint(world);
            bool on = sp.z > 0.1f && cam.pixelRect.Contains(sp);
            if (tmp.gameObject.activeSelf != on)
                tmp.gameObject.SetActive(on);
            if (!on)
                continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, sp, canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null, out var local);
            tmp.rectTransform.anchoredPosition = local;
        }
    }
}
