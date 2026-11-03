using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NV3 = System.Numerics.Vector3;

// -----------------------------------------------------------------------------
// Space3DCalculatorView: the 3D graphing calculator.
// -----------------------------------------------------------------------------
// Left panel: up to six functions (each with its colour, kind and errors), sliders a, b, c when a
// function uses them, the viewing range, colour mode, and examples. Right: the 3D view (orbit, pan,
// zoom; tap a plot to inspect the point: coordinates, partial derivatives or r(t) and its speed).
// Functions are saved per device (PlayerPrefs) so the last session comes back.
// -----------------------------------------------------------------------------

public class Space3DCalculatorView
{
    const int MaxRows = 6;
    const string PrefsKey = "fp.space3d.rows";
    static readonly float[] Halves = { 2f, 3f, 5f, 8f, 10f, 20f };

    static readonly string[] Examples =
    {
        "sin(x)*cos(y)",
        "(2cos(t), 2sin(t), t/3), t = -3pi..3pi",
        "x^2 - y^2",
        "x^2 + y^2 + z^2 = 9",
        "a*x + b*y + c",
        "(u*cos(v), u*sin(v), v/3), u = 0..3, v = 0..4pi",
        "3*exp(-(x^2 + y^2)/4)",
        "x^2 + y^2 - z^2 = 1",
        "(3cos(u)cos(v), 3cos(u)sin(v), 3sin(u)), u = -pi/2..pi/2, v = 0..2pi",
        "sin(sqrt(x^2 + y^2))*2",
        "(cos(t)(3 + cos(5t)), sin(t)(3 + cos(5t)), sin(5t)), t = 0..2pi",
        "y = x^2/3 - 2",
    };

    sealed class Row
    {
        public string Text;
        public Plot3DSpec Spec;
        public string Error;
        public bool Visible = true;
        public int ColorIndex;
        public MeshFilter Mf;
        public GameObject Go;
        public Plot3DMesh Sampled;
        public RectTransform Ui;
        public TMP_InputField Input;
        public TextMeshProUGUI Status;
        public Button HideBtn;
        public Image Bar;
    }

    readonly Space3DScene host;
    readonly Transform world;
    readonly List<Row> rows = new List<Row>();
    readonly float[] prm = { 1f, 1f, 1f };
    float half = 5f;
    bool colorByHeight = true;
    int exampleCursor;

    RectTransform root, panel, content, rowsRoot, slidersRoot, readout, collapsedBtn;
    readonly RectTransform[] sliderRows = new RectTransform[3];
    readonly Slider[] sliders = new Slider[3];
    readonly TextMeshProUGUI[] sliderValues = new TextMeshProUGUI[3];
    TextMeshProUGUI rangeLabel, readoutText;
    Button colorBtn, addBtn;
    GameObject marker;
    MeshFilter markerMf;
    Transform plotsRoot;
    float panelW;
    bool collapsed;

    public Space3DCalculatorView(Space3DScene host, Transform world)
    {
        this.host = host;
        this.world = world;
        plotsRoot = new GameObject("CalcPlots").transform;
        plotsRoot.SetParent(world, false);
        BuildUi();
        Load();
        host.Rig.Tapped += OnTapped;
    }

    Box3 Box => new Box3 { XMin = -half, XMax = half, YMin = -half, YMax = half, ZMin = -half, ZMax = half };

    public void SetActive(bool on)
    {
        root.gameObject.SetActive(on);
        plotsRoot.gameObject.SetActive(on);
        if (!on)
        {
            HideReadout();
            return;
        }

        host.SetTitle(LocalizationManager.Get("space3d.calc_title", "3D graphing"));
        host.Axes.gameObject.SetActive(true);
        host.Rig.FollowPoint = null;
        host.Rig.TouchOrbitRegion = null;
        host.Rig.AllowPan = true;
        host.Rig.MinPitch = -80f;
        host.Axes.ShowTicks = true;
        host.Axes.Rebuild(Box);
        host.Axes.SetLabelsVisible(true);
        host.Rig.Frame(Box);
        ApplyInset();
        ReplotAll();
    }

    // ------------------------------------------------------------------ UI

    void BuildUi()
    {
        root = Space3DUi.Rect("CalcRoot", host.Body, Vector2.zero, Vector2.one);
        panelW = Mathf.Min(680f, host.CanvasWidth * 0.42f);
        panel = Space3DUi.Rect("Panel", root, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(16f, 16f), new Vector2(16f + panelW, -14f));
        Space3DUi.Card(panel);

        // Header: "Functions" + collapse.
        var header = Space3DUi.Rect("Header", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -74f), new Vector2(-18f, -10f));
        Space3DUi.Label(header, "Text", LocalizationManager.Get("space3d.functions", "Functions"), 30f, Bauhaus.Ink, FontStyles.Bold);
        var hide = Space3DUi.Pinned("HidePanel", header, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(64f, 58f));
        Space3DUi.Button(hide, null, Bauhaus.Tone.Paper, StarGlyph.ArrowLeft, () => SetCollapsed(true), 22f, 30f);

        // Scrolling body.
        var viewport = Space3DUi.Rect("Viewport", panel, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -82f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0);
        content = Space3DUi.Rect("Content", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f));
        content.pivot = new Vector2(0.5f, 1f);
        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 12f;
        vlg.padding = new RectOffset(8, 8, 6, 16);
        vlg.childControlHeight = false;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        rowsRoot = LayoutBlock("Rows", 10f);
        var addRow = Fixed("AddRow", 72f);
        var addRt = Space3DUi.Rect("Add", addRow, new Vector2(0f, 0f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-6f, 0f));
        addBtn = Space3DUi.Button(addRt, LocalizationManager.Get("space3d.add", "Add function"), Bauhaus.Tone.Blue, null, () => AddRow("", true), 22f);
        var exRt = Space3DUi.Rect("Examples", addRow, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(6f, 0f), Vector2.zero);
        Space3DUi.Button(exRt, LocalizationManager.Get("space3d.examples", "Example"), Bauhaus.Tone.Yellow, StarGlyph.Bulb, NextExample, 22f, 30f);

        slidersRoot = LayoutBlock("Sliders", 6f);
        for (int k = 0; k < 3; k++)
        {
            int slot = k;
            var r = Space3DUi.Rect("Slider_" + Plot3DVars.ParamNames[k], slidersRoot, Vector2.zero, Vector2.one);
            r.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            sliderRows[k] = r;
            var name = Space3DUi.Rect("Name", r, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(56f, 0f));
            Space3DUi.Label(name, "Text", Plot3DVars.ParamNames[k], 30f, Bauhaus.Ink, FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Center);
            var sr = Space3DUi.Rect("Slider", r, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(64f, 8f), new Vector2(-116f, -8f));
            sliders[k] = Space3DUi.Slider(sr, -5f, 5f, prm[k], v => OnSlider(slot, v));
            var val = Space3DUi.Rect("Value", r, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-108f, 0f), Vector2.zero);
            sliderValues[k] = Space3DUi.Label(val, "Text", Space3DUi.Num(prm[k]), 26f, Bauhaus.Ink, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        }

        var rangeRow = Fixed("Range", 70f);
        var minus = Space3DUi.Rect("Smaller", rangeRow, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(70f, 0f));
        Space3DUi.Button(minus, "−", Bauhaus.Tone.Paper, null, () => StepRange(-1), 34f);
        var rl = Space3DUi.Rect("RangeLabel", rangeRow, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(78f, 0f), new Vector2(-6f, 0f));
        rangeLabel = Space3DUi.Label(rl, "Text", "", 22f, Bauhaus.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        var plus = Space3DUi.Rect("Bigger", rangeRow, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(-76f, 0f), new Vector2(-6f, 0f));
        Space3DUi.Button(plus, "+", Bauhaus.Tone.Paper, null, () => StepRange(1), 34f);
        var col = Space3DUi.Rect("Colour", rangeRow, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(6f, 0f), Vector2.zero);
        colorBtn = Space3DUi.Button(col, "", Bauhaus.Tone.Paper, null, ToggleColor, 20f);

        var resetRow = Fixed("ResetRow", 64f);
        var rv = Space3DUi.Rect("ResetView", resetRow, Vector2.zero, Vector2.one);
        Space3DUi.Button(rv, LocalizationManager.Get("space3d.reset_view", "Reset view"), Bauhaus.Tone.Paper, StarGlyph.Restart, () => host.Rig.Frame(Box), 22f, 28f);

        var help = Fixed("Help", 150f);
        var ht = Space3DUi.Label(help, "Text", LocalizationManager.Get("space3d.calc_help",
            "Drag to turn, pinch or scroll to zoom, two fingers or right-drag to pan. Tap a graph to read a point. " +
            "Curves: (x(t), y(t), z(t)). Surfaces: (x(u,v), y(u,v), z(u,v)). Add a range like t = 0..4pi."),
            18f, Bauhaus.InkSoft, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        ht.enableAutoSizing = true;
        ht.fontSizeMin = 13f;

        // Collapsed: one small button to bring the panel back.
        collapsedBtn = Space3DUi.Pinned("ShowPanel", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -14f), new Vector2(240f, 72f));
        Space3DUi.Button(collapsedBtn, LocalizationManager.Get("space3d.functions", "Functions"), Bauhaus.Tone.Blue, StarGlyph.Sliders, () => SetCollapsed(false), 22f, 30f);
        collapsedBtn.gameObject.SetActive(false);

        // Inspect readout (top-right).
        readout = Space3DUi.Pinned("Readout", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(520f, 200f));
        Space3DUi.Card(readout, Bauhaus.Card, Bauhaus.ButtonRule);
        var rtxt = Space3DUi.Rect("Text", readout, Vector2.zero, Vector2.one, new Vector2(18f, 12f), new Vector2(-70f, -12f));
        readoutText = Space3DUi.Label(rtxt, "Text", "", 22f, Bauhaus.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        readoutText.enableAutoSizing = true;
        readoutText.fontSizeMin = 14f;
        var close = Space3DUi.Pinned("Close", readout, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -8f), new Vector2(54f, 54f));
        Space3DUi.Button(close, null, Bauhaus.Tone.Paper, StarGlyph.Close, HideReadout, 20f, 24f);
        readout.gameObject.SetActive(false);

        Space3DRender.NewMeshObject("InspectMarker", world, Space3DRender.Lit, out markerMf);
        marker = markerMf.gameObject;
        marker.SetActive(false);
        RefreshRangeLabel();
        RefreshColorLabel();
    }

    RectTransform LayoutBlock(string name, float spacing)
    {
        var rt = Space3DUi.Rect(name, content, Vector2.zero, Vector2.one);
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
        rt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    RectTransform Fixed(string name, float height)
    {
        var rt = Space3DUi.Rect(name, content, Vector2.zero, Vector2.one);
        rt.sizeDelta = new Vector2(0f, height);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return rt;
    }

    void SetCollapsed(bool c)
    {
        collapsed = c;
        panel.gameObject.SetActive(!c);
        collapsedBtn.gameObject.SetActive(c);
        ApplyInset();
    }

    void ApplyInset() => host.LeftInset = collapsed || !root.gameObject.activeInHierarchy ? 0f : panelW + 16f;

    void RefreshRangeLabel()
    {
        if (rangeLabel != null)
            rangeLabel.text = $"−{Space3DUi.Num(half)} to {Space3DUi.Num(half)}";
    }

    void RefreshColorLabel()
    {
        Space3DUi.SetLabel(colorBtn, colorByHeight
            ? LocalizationManager.Get("space3d.color_height", "Colour: height")
            : LocalizationManager.Get("space3d.color_function", "Colour: function"));
    }

    // ------------------------------------------------------------------ rows

    void AddRow(string text, bool focus)
    {
        if (rows.Count >= MaxRows)
            return;
        var row = new Row { Text = text, ColorIndex = NextColorIndex() };
        var rt = Space3DUi.Rect("Fn" + rows.Count, rowsRoot, Vector2.zero, Vector2.one);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = 118f;
        row.Ui = rt;

        var bar = Space3DUi.Rect("ColourBar", rt, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(12f, 0f));
        row.Bar = bar.gameObject.AddComponent<Image>();
        row.Bar.sprite = Bauhaus.Square;
        row.Bar.raycastTarget = false;

        var input = Space3DUi.Rect("Input", rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(22f, -66f), new Vector2(-152f, 0f));
        row.Input = Space3DUi.Input(input, text, LocalizationManager.Get("space3d.placeholder", "sin(x)*cos(y)"), 24f,
            s => OnRowEdited(row, s, false), s => OnRowEdited(row, s, true));

        var st = Space3DUi.Rect("Status", rt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(24f, 2f), new Vector2(-152f, 46f));
        row.Status = Space3DUi.Label(st, "Text", "", 19f, Bauhaus.InkSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        row.Status.enableAutoSizing = true;
        row.Status.fontSizeMin = 12f;
        row.Status.textWrappingMode = TextWrappingModes.NoWrap;
        row.Status.overflowMode = TextOverflowModes.Ellipsis;

        var hb = Space3DUi.Rect("Hide", rt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-142f, -66f), new Vector2(-70f, 0f));
        row.HideBtn = Space3DUi.Button(hb, LocalizationManager.Get("space3d.hide", "Hide"), Bauhaus.Tone.Paper, null, () => ToggleVisible(row), 17f);
        var del = Space3DUi.Rect("Delete", rt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-62f, -66f), new Vector2(0f, 0f));
        Space3DUi.Button(del, null, Bauhaus.Tone.Paper, StarGlyph.Close, () => RemoveRow(row), 20f, 24f);

        row.Go = Space3DRender.NewMeshObject("Plot", plotsRoot, Space3DRender.LitWithContours(1f, 0.22f), out row.Mf);
        rows.Add(row);
        RefreshRowChrome(row);
        Parse(row);
        Replot(row);
        RefreshSliders();
        RefreshAddButton();
        if (focus)
        {
            row.Input.Select();
            row.Input.ActivateInputField();
        }
    }

    int NextColorIndex()
    {
        for (int c = 0; c < Space3DRender.FunctionColors.Length; c++)
        {
            bool used = false;
            foreach (var r in rows)
                used |= r.ColorIndex == c;
            if (!used)
                return c;
        }

        return rows.Count % Space3DRender.FunctionColors.Length;
    }

    void RemoveRow(Row row)
    {
        rows.Remove(row);
        UnityEngine.Object.Destroy(row.Ui.gameObject);
        UnityEngine.Object.Destroy(row.Go);
        HideReadout();
        RefreshSliders();
        RefreshAddButton();
        Save();
    }

    void ToggleVisible(Row row)
    {
        row.Visible = !row.Visible;
        row.Go.SetActive(row.Visible);
        RefreshRowChrome(row);
        RefreshSliders();
        Save();
    }

    void RefreshAddButton()
    {
        if (addBtn != null)
            addBtn.interactable = rows.Count < MaxRows;
    }

    void RefreshRowChrome(Row row)
    {
        var c = Space3DRender.FunctionColors[row.ColorIndex % Space3DRender.FunctionColors.Length];
        row.Bar.color = row.Visible ? c : Bauhaus.Rule;
        Space3DUi.SetLabel(row.HideBtn, row.Visible ? LocalizationManager.Get("space3d.hide", "Hide") : LocalizationManager.Get("space3d.show", "Show"));
    }

    void OnRowEdited(Row row, string text, bool commit)
    {
        if (text == row.Text && !commit)
            return;
        row.Text = text;
        Parse(row);
        Replot(row);
        RefreshSliders();
        if (commit)
            Save();
    }

    void Parse(Row row)
    {
        row.Spec = null;
        row.Error = null;
        if (string.IsNullOrWhiteSpace(row.Text))
        {
            row.Status.text = LocalizationManager.Get("space3d.empty", "Type z = f(x, y), a curve (x(t), y(t), z(t)) or F(x, y, z) = 0");
            row.Status.color = Bauhaus.InkSoft;
            return;
        }

        if (Plot3DParser.TryParse(row.Text, out var spec, out string err))
        {
            row.Spec = spec;
            row.Status.text = spec.KindLabel;
            row.Status.color = Bauhaus.InkSoft;
        }
        else
        {
            row.Error = err;
            row.Status.text = err;
            row.Status.color = Bauhaus.Red;
        }
    }

    void ReplotAll()
    {
        foreach (var r in rows)
            Replot(r);
    }

    int Resolution(Plot3DKind kind)
    {
        int visible = 0;
        foreach (var r in rows)
            if (r.Visible && r.Spec != null) visible++;
        return kind == Plot3DKind.Implicit
            ? Plot3DSampler.ImplicitResolution(Space3DScene.Mobile)
            : Plot3DSampler.SurfaceResolution(Space3DScene.Mobile, Mathf.Max(1, visible));
    }

    void Replot(Row row)
    {
        var mesh = row.Mf.sharedMesh;
        if (row.Spec == null)
        {
            mesh.Clear();
            row.Sampled = null;
            return;
        }

        var box = Box;
        var sampled = Plot3DSampler.Sample(row.Spec, box, prm, Resolution(row.Spec.Kind));
        row.Sampled = sampled;
        var color = Space3DRender.FunctionColors[row.ColorIndex % Space3DRender.FunctionColors.Length];
        if (row.Spec.Kind == Plot3DKind.Curve)
        {
            Space3DRender.FillTubes(mesh, sampled.Polylines, Mathf.Clamp(box.Size * 0.006f, 0.02f, 0.2f), color, Space3DScene.Mobile ? 6 : 8);
            row.Go.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_ContourAlpha", 0f);
        }
        else
        {
            Space3DRender.FillSurface(mesh, sampled, colorByHeight, color, box.ZMin, box.ZMax);
            var mat = row.Go.GetComponent<MeshRenderer>().sharedMaterial;
            mat.SetFloat("_Contour", Space3DAxes.TickStep(half) * 0.5f);
            mat.SetFloat("_ContourAlpha", 0.2f);
        }

        if (row.Spec.Kind == Plot3DKind.Surface || row.Spec.Kind == Plot3DKind.ParamSurface || row.Spec.Kind == Plot3DKind.Implicit)
        {
            if (sampled.TriangleCount == 0 && row.Error == null)
                row.Status.text = row.Spec.KindLabel + "  " + LocalizationManager.Get("space3d.outside", "(nothing inside the box: try a bigger range)");
            else
                row.Status.text = row.Spec.KindLabel;
        }
    }

    void RefreshSliders()
    {
        bool any = false;
        for (int k = 0; k < 3; k++)
        {
            bool used = false;
            foreach (var r in rows)
                used |= r.Visible && r.Spec != null && r.Spec.UsesParam(k);
            sliderRows[k].gameObject.SetActive(used);
            any |= used;
        }

        slidersRoot.gameObject.SetActive(any);
    }

    void OnSlider(int k, float v)
    {
        v = Mathf.Round(v * 10f) / 10f;
        prm[k] = v;
        sliderValues[k].text = Space3DUi.Num(v);
        foreach (var r in rows)
        {
            if (r.Spec != null && r.Spec.UsesParam(k))
                Replot(r);
        }

        HideReadout();
    }

    void StepRange(int dir)
    {
        int i = Array.IndexOf(Halves, half);
        if (i < 0) i = 2;
        i = Mathf.Clamp(i + dir, 0, Halves.Length - 1);
        if (Halves[i] == half)
            return;
        half = Halves[i];
        RefreshRangeLabel();
        host.Axes.Rebuild(Box);
        host.Rig.Frame(Box, host.Rig.Yaw, host.Rig.Pitch);
        HideReadout();
        ReplotAll();
        Save();
    }

    void ToggleColor()
    {
        colorByHeight = !colorByHeight;
        RefreshColorLabel();
        ReplotAll();
        Save();
    }

    void NextExample()
    {
        string ex = Examples[exampleCursor % Examples.Length];
        exampleCursor++;
        if (rows.Count >= MaxRows)
            RemoveRow(rows[0]);
        AddRow(ex, false);
        Save();
    }

    // ------------------------------------------------------------------ inspect

    void OnTapped(Vector2 screen)
    {
        if (!root.gameObject.activeInHierarchy)
            return;
        var ray = host.Cam.ScreenPointToRay(screen);
        float bestT = float.PositiveInfinity;
        Row bestRow = null;
        NV3 bestP = default;
        float bestParam0 = 0f, bestParam1 = 0f;
        var box = Box;
        foreach (var r in rows)
        {
            if (!r.Visible || r.Spec == null || r.Sampled == null)
                continue;
            if (r.Spec.Kind == Plot3DKind.Surface || r.Spec.Kind == Plot3DKind.Implicit)
            {
                if (RayMarch(r.Spec, ray, box, out float t, out NV3 p) && t < bestT)
                {
                    bestT = t;
                    bestRow = r;
                    bestP = p;
                }
            }
            else
            {
                // Nearest sampled point on screen (front-most within a finger's width).
                float radius = 34f * Mathf.Max(1f, Screen.height / 1080f);
                if (r.Spec.Kind == Plot3DKind.Curve)
                {
                    for (int li = 0; li < r.Sampled.Polylines.Count; li++)
                    {
                        var line = r.Sampled.Polylines[li];
                        for (int k = 0; k < line.Count; k++)
                            ConsiderPoint(r, line[k], r.Sampled.PolylineParams[li][k], 0f);
                    }
                }
                else
                {
                    int n1 = r.Sampled.GridN + 1;
                    var used = new bool[r.Sampled.Vertices.Count];
                    foreach (int idx in r.Sampled.Triangles)
                        used[idx] = true;
                    for (int k = 0; k < used.Length; k++)
                    {
                        if (!used[k]) continue;
                        float u = r.Spec.Range0[0] + (r.Spec.Range0[1] - r.Spec.Range0[0]) * (k % n1) / r.Sampled.GridN;
                        float v = r.Spec.Range1[0] + (r.Spec.Range1[1] - r.Spec.Range1[0]) * (k / n1) / r.Sampled.GridN;
                        ConsiderPoint(r, r.Sampled.Vertices[k], u, v);
                    }
                }

                void ConsiderPoint(Row row, NV3 p, float a, float b)
                {
                    var w = Space3DRender.ToWorld(p);
                    var sp = host.Cam.WorldToScreenPoint(w);
                    if (sp.z <= 0f || ((Vector2)sp - screen).magnitude > radius)
                        return;
                    if (sp.z < bestT)
                    {
                        bestT = sp.z;
                        bestRow = row;
                        bestP = p;
                        bestParam0 = a;
                        bestParam1 = b;
                    }
                }
            }
        }

        if (bestRow == null)
        {
            HideReadout();
            return;
        }

        ShowReadout(bestRow, bestP, bestParam0, bestParam1);
    }

    /// <summary>First crossing of the surface along the view ray, refined by bisection.</summary>
    bool RayMarch(Plot3DSpec spec, Ray ray, Box3 box, out float hitT, out NV3 hit)
    {
        hitT = 0f;
        hit = default;
        float G(NV3 p)
        {
            if (spec.Kind == Plot3DKind.Implicit)
                return spec.EvalImplicit(p, prm);
            int ax = spec.DependentAxis;
            float a = ax == 0 ? p.Y : p.X, b = ax == 2 ? p.Y : p.Z;
            float w = ax == 0 ? p.X : ax == 1 ? p.Y : p.Z;
            return w - spec.EvalSurface(a, b, prm);
        }

        NV3 At(float t) => Space3DRender.ToMath(ray.origin + ray.direction * t);
        float far = (host.Cam.transform.position.magnitude + box.Size * 2f);
        int steps = 500;
        float dt = far / steps;
        float prevG = float.NaN, prevT = 0f;
        for (int i = 0; i <= steps; i++)
        {
            float t = i * dt;
            var p = At(t);
            if (!box.Contains(p))
            {
                prevG = float.NaN;
                continue;
            }

            float g = G(p);
            if (!float.IsNaN(prevG) && !float.IsNaN(g) && Mathf.Sign(g) != Mathf.Sign(prevG) && Mathf.Abs(g - prevG) < box.Size)
            {
                float lo = prevT, hi = t, glo = prevG;
                for (int k = 0; k < 30; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    float gm = G(At(mid));
                    if (float.IsNaN(gm) || Mathf.Sign(gm) == Mathf.Sign(glo))
                    {
                        lo = mid;
                        glo = gm;
                    }
                    else
                        hi = mid;
                }

                hitT = (lo + hi) * 0.5f;
                hit = At(hitT);
                return true;
            }

            prevG = g;
            prevT = t;
        }

        return false;
    }

    void ShowReadout(Row row, NV3 p, float a, float b)
    {
        var spec = row.Spec;
        string P(NV3 q) => $"({Space3DUi.Num(q.X)}, {Space3DUi.Num(q.Y)}, {Space3DUi.Num(q.Z)})";
        string head = $"<b>{Esc(row.Text)}</b>";
        string body;
        switch (spec.Kind)
        {
            case Plot3DKind.Surface:
            {
                int ax = spec.DependentAxis;
                float u = ax == 0 ? p.Y : p.X, v = ax == 2 ? p.Y : p.Z;
                var g = spec.SurfaceGradient(u, v, prm);
                string[] names = { "x", "y", "z" };
                string dep = names[ax], fa = names[ax == 0 ? 1 : 0], fb = names[ax == 2 ? 1 : 2];
                body = $"(x, y, z) = {P(p)}\n∂{dep}/∂{fa} = {Space3DUi.Num(g.X)}     ∂{dep}/∂{fb} = {Space3DUi.Num(g.Y)}";
                break;
            }
            case Plot3DKind.Curve:
            {
                var vel = spec.CurveVelocity(a, prm);
                body = $"t = {Space3DUi.Num(a)}\nr(t) = {P(p)}\n|r′(t)| = {Space3DUi.Num(vel.Length())}";
                break;
            }
            case Plot3DKind.ParamSurface:
                body = $"u = {Space3DUi.Num(a)}, v = {Space3DUi.Num(b)}\nr(u, v) = {P(p)}";
                break;
            default:
                body = $"(x, y, z) = {P(p)}\nF(x, y, z) = 0";
                break;
        }

        readoutText.text = head + "\n" + body;
        readout.gameObject.SetActive(true);

        // Marker: a small 3D cross at the point and a drop line to the floor.
        float s = Box.Size * 0.025f;
        var lines = new List<List<NV3>>
        {
            new List<NV3> { p - new NV3(s, 0, 0), p + new NV3(s, 0, 0) },
            new List<NV3> { p - new NV3(0, s, 0), p + new NV3(0, s, 0) },
            new List<NV3> { p - new NV3(0, 0, s), p + new NV3(0, 0, s) },
        };
        float floor = Mathf.Clamp(0f, Box.ZMin, Box.ZMax);
        if (Mathf.Abs(p.Z - floor) > s)
            lines.Add(new List<NV3> { p, new NV3(p.X, p.Y, floor) });
        Space3DRender.FillTubes(markerMf.sharedMesh, lines, Box.Size * 0.0045f, Bauhaus.Ink, 6);
        marker.SetActive(true);
    }

    static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

    void HideReadout()
    {
        if (readout != null)
            readout.gameObject.SetActive(false);
        if (marker != null)
            marker.SetActive(false);
    }

    // ------------------------------------------------------------------ persistence

    [Serializable]
    class Saved
    {
        public List<string> texts = new List<string>();
        public List<bool> visible = new List<bool>();
        public float[] prm = { 1f, 1f, 1f };
        public float half = 5f;
        public bool byHeight = true;
        public int example;
    }

    void Save()
    {
        var s = new Saved { half = half, byHeight = colorByHeight, prm = (float[])prm.Clone(), example = exampleCursor };
        foreach (var r in rows)
        {
            s.texts.Add(r.Text ?? "");
            s.visible.Add(r.Visible);
        }

        try
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(s));
            PlayerPrefs.Save();
        }
        catch (Exception)
        {
            // Prefs are a convenience only.
        }
    }

    void Load()
    {
        Saved s = null;
        try
        {
            if (PlayerPrefs.HasKey(PrefsKey) && !QaShots.Enabled)
                s = JsonUtility.FromJson<Saved>(PlayerPrefs.GetString(PrefsKey));
        }
        catch (Exception)
        {
            s = null;
        }

        if (s == null || s.texts == null || s.texts.Count == 0)
        {
            s = new Saved();
            s.texts.Add(Examples[0]);
            s.texts.Add(Examples[1]);
            s.visible.Add(true);
            s.visible.Add(true);
            s.example = 2;
        }

        half = Array.IndexOf(Halves, s.half) >= 0 ? s.half : 5f;
        colorByHeight = s.byHeight;
        exampleCursor = s.example;
        for (int k = 0; k < 3 && s.prm != null && k < s.prm.Length; k++)
        {
            prm[k] = s.prm[k];
            sliders[k].SetValueWithoutNotify(prm[k]);
            sliderValues[k].text = Space3DUi.Num(prm[k]);
        }

        for (int i = 0; i < s.texts.Count && i < MaxRows; i++)
        {
            AddRow(s.texts[i], false);
            if (i < s.visible.Count && !s.visible[i])
                ToggleVisible(rows[rows.Count - 1]);
        }

        RefreshRangeLabel();
        RefreshColorLabel();
    }

    /// <summary>QA / store screenshots: inspect the point under a screen position.</summary>
    public void QaInspect(Vector2 screen) => OnTapped(screen);

    /// <summary>QA / store screenshots: replace the functions.</summary>
    public void SetFunctions(params string[] texts)
    {
        while (rows.Count > 0)
            RemoveRow(rows[0]);
        foreach (var t in texts)
            AddRow(t, false);
    }
}
