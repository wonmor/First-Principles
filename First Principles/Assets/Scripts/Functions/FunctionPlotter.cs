/*
 * FunctionPlotter.cs — John Seong / First Principles
 *
 * Maintenance overview:
 *   - Update() only runs a full replot when parameters change (or the Lorenz phase is animated, which level
 *     play turns off); during the left-to-right reveal it only refreshes vertex alpha.
 *   - f and f′ come from GraphFunctionMath (analytic f′ where the family allows) and are mapped to grid space by
 *     GraphViewMapping; GraphWorldBuilder uses the same two, so the drawn curves and the platforms always agree.
 *   - Level mode draws f′ on its own axis (derivativeOwnAxis): f′ = 0 on the centre line, fitted to the plane.
 *   - Both polylines are clipped to the plane with NaN breaks (PolylineClip); the renderers skip those segments.
 *   - To add a new curve: extend FunctionType, GraphFunctionMath.Evaluate / Derivative and UpdateEquationText.
 *   - SampleCurvePlotterY / SetEquationExtraSuffix support Riemann overlay and TMP sub-lines.
 */

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class FunctionPlotter : MonoBehaviour
{
    // The starting & ending x coordinates
    public float xStart = 0f;
    public float xEnd = 10f;

    // Increase in xValue for each for loop iteration
    public float step = 0.5f;

    public float transA = 0, transK = 0, transC = 0, transD = 0;

    // Power variable only applicable to 'x^n' functions — represented as 'n' here
    // Base N variable only applicable to 'n^x' functions — represented as 'n' here

    public int power = 2;

    /// <summary>Central-difference step used where f′ has no closed form (see <see cref="GraphFunctionMath.Derivative"/>).</summary>
    public float NumericalDerivativeStep => TableCurveDerivativeStep;

    // The type of function to be plotted
    public FunctionType functionType;

    public int baseN = 2;

    /// <summary>When <see cref="functionType"/> is <see cref="FunctionType.CustomExpression"/>, evaluates this as <c>f(u)</c> with <c>u = transK×(x−transD)</c>; plotted <c>y = transA×f(u)+transC</c>.</summary>
    [TextArea(1, 3)]
    public string customExpression = "x^2";

    public bool differentiate = false;

    [Tooltip("Level mode: wind-tunnel GPU backdrop (set from LevelDefinition for aerospace stages).")]
    public bool showWindTunnelBackdrop = false;

    [Tooltip("Level mode: vertically scale f and f′ so the band fits the grid (flat curves read clearly).")]
    public bool autoScaleVertical = true;

    [Tooltip("Fraction of half the grid height (from center) used by the fitted band.")]
    [Range(0.38f, 0.92f)]
    public float verticalFillFraction = 0.74f;

    [Tooltip("Level mode: horizontally scale the domain window [xStart,xEnd] so it fills most of the grid.")]
    public bool autoScaleHorizontal = true;

    [Tooltip("Graphing calculator: match vertical scale to horizontal zoom so one grid column and one row span the same plotter x/y units (pinch/Scale zoom x and y together). Ignored for polar plot styles.")]
    public bool lockVerticalPlotScaleToHorizontalWindow = false;

    [Tooltip("Fraction of half the grid width (from center) used by the fitted x-window.")]
    [Range(0.38f, 0.92f)]
    public float horizontalFillFraction = 0.74f;

    public const float DefaultVerticalScaleClampMin = 0.38f;
    public const float DefaultVerticalScaleClampMax = 7.5f;
    public const float DefaultHorizontalScaleClampMin = 0.38f;
    public const float DefaultHorizontalScaleClampMax = 7.5f;

    public float verticalScaleClampMin = DefaultVerticalScaleClampMin;
    public float verticalScaleClampMax = DefaultVerticalScaleClampMax;

    public float horizontalScaleClampMin = DefaultHorizontalScaleClampMin;
    public float horizontalScaleClampMax = DefaultHorizontalScaleClampMax;

    [Tooltip("Level mode: draw f′ on its own vertical axis pivoted at 0 (f′ = 0 on the centre line, fitted to the plane). Off = f′ shares f's axis (graphing calculator).")]
    public bool derivativeOwnAxis = false;

    [Tooltip("Scroll the Lorenz time slice every frame. Off in level play: platforms are built once from the curve, so a moving curve would no longer match the ground.")]
    public bool animateLorenzPhase = false;

    // Local points list
    private List<Vector2> points = new List<Vector2>();
    private List<Vector2> dPoints = new List<Vector2>();

    /// <summary>Vertical map: raw plotter y → offset used in grid space before adding grid origin y.</summary>
    private float _autoMid;
    private float _autoScale = 1f;
    /// <summary>Horizontal map: plotter x → offset from center in grid columns before adding grid origin x.</summary>
    private float _autoMidX;
    private float _autoScaleX = 1f;
    /// <summary>f′ vertical map (own axis in level mode; equals f's map otherwise).</summary>
    private float _autoDMid;
    private float _autoDScale = 1f;
    private int _cachedGridOriginY;

    [SerializeField] TextMeshProUGUI equationText;

    /// <summary>Optional second line under the main equation (e.g. Riemann / integral note).</summary>
    private string equationExtraSuffix = "";
    /// <summary>Hex color (no leading <c>#</c>) wrapping <see cref="equationExtraSuffix"/>; inline <c>&lt;color&gt;</c> tags inside the suffix override per-span.</summary>
    private string equationExtraSuffixColorHex = "57534A";

    private LineRendererUI lineRenderer;
    private DerivRendererUI derivRenderer;

    /// <summary>Last <see cref="ComputePlotInvalidationHash"/> after a full <see cref="PlotFunction"/> — skips redundant work in <see cref="Update"/>.</summary>
    private int _cachedPlotParamHash = int.MinValue;

    [Tooltip("Seconds for the main f and f′ curves to fade in left→right after the graph parameters change.")]
    public float graphRevealDurationSeconds = 2.1f;

    int _graphRevealParamsHash = int.MinValue;
    float _graphRevealT01 = 1f;
    float _lorenzPhaseScroll;

    // AeroDragPolarTriple: extra polylines (cloned once from main LineRendererUI).
    private LineRendererUI overlayParasitic;
    private LineRendererUI overlayInduced;
    private Color overlayDragPolarParasiticColor = Bauhaus.Ink;
    private Color overlayDragPolarInducedColor = Bauhaus.YellowLine;

    private void Reset()
    {
        InitPlotFunction();
        RefreshGrid();
    }

    private void OnValidate()
    {
        InitPlotFunction();
        RefreshGrid();
    }

    private void Update()
    {
        UpdateGraphRevealAnimation();

        // Lorenz stage scrolls phase every frame (when animated) — full resample required.
        if (functionType == FunctionType.ChaosLorenzButterflyX && animateLorenzPhase)
        {
            InitPlotFunction();
            return;
        }

        int paramHash = ComputePlotInvalidationHash();
        if (paramHash != _cachedPlotParamHash)
        {
            InitPlotFunction();
            return;
        }

        // Graph reveal: only push fade uniforms + remesh existing points (no ComputeGraph).
        if (_graphRevealT01 < 0.999f)
        {
            if (lineRenderer == null)
                lineRenderer = LineRendererUI.FindPrimaryCurve();
            if (derivRenderer == null)
                derivRenderer = FindAnyObjectByType<DerivRendererUI>();

            PushGraphRevealToRenderers();
            if (lineRenderer != null)
                lineRenderer.SetVerticesDirty();
            if (derivRenderer != null && differentiate)
                derivRenderer.SetVerticesDirty();
        }
    }

    /// <summary>When this changes, <see cref="PlotFunction"/> must run (same inputs that affect <see cref="ComputeGraph"/>).</summary>
    int ComputePlotInvalidationHash() =>
        HashCode.Combine(
            ComputeGraphRevealParamsHash(),
            differentiate,
            showWindTunnelBackdrop,
            equationExtraSuffix ?? "");

    void UpdateGraphRevealAnimation()
    {
        int h = ComputeGraphRevealParamsHash();
        if (h != _graphRevealParamsHash)
        {
            _graphRevealParamsHash = h;
            _graphRevealT01 = 0f;
            if (functionType != FunctionType.ChaosLorenzButterflyX || !animateLorenzPhase)
                _lorenzPhaseScroll = 0f;
        }

        float dur = Mathf.Max(0.05f, graphRevealDurationSeconds);
        // Lorenz / chaos-theory stage already animates via _lorenzPhaseScroll; horizontal alpha reveal uses grid-x and
        // can zero the whole polyline when revealX sits on gx0 (progress 0 → all vertices faded).
        if (functionType == FunctionType.ChaosLorenzButterflyX)
            _graphRevealT01 = 1f;
        else
            _graphRevealT01 = Mathf.MoveTowards(_graphRevealT01, 1f, Time.deltaTime / dur);

        if (functionType == FunctionType.ChaosLorenzButterflyX && animateLorenzPhase)
        {
            float tMax = LorenzAttractorSamples.TimeMax;
            if (tMax > 1e-4f)
                _lorenzPhaseScroll = Mathf.Repeat(_lorenzPhaseScroll + Time.deltaTime * 3.15f, tMax);
        }
    }

    int ComputeGraphRevealParamsHash()
    {
        int a = HashCode.Combine(functionType, xStart, xEnd, step, transA, transK);
        int b = HashCode.Combine(transC, transD, power, baseN, customExpression ?? "");
        int c = HashCode.Combine(autoScaleVertical, autoScaleHorizontal, lockVerticalPlotScaleToHorizontalWindow,
            verticalFillFraction, horizontalFillFraction, derivativeOwnAxis);
        return HashCode.Combine(a, b, c);
    }

    void PushGraphRevealToRenderers()
    {
        if (lineRenderer == null)
            return;

        Vector2Int go = lineRenderer.gridSize / 2;
        float gx0 = MapDisplayX(xStart) + go.x;
        float gx1 = MapDisplayX(xEnd) + go.x;

        float revealT = functionType == FunctionType.ChaosLorenzButterflyX ? 1f : _graphRevealT01;
        lineRenderer.SetGraphRevealFade(revealT, gx0, gx1);
        if (derivRenderer != null)
            derivRenderer.SetGraphRevealFade(revealT, gx0, gx1);

        if (functionType == FunctionType.AeroDragPolarTriple)
        {
            if (overlayParasitic != null)
                overlayParasitic.SetGraphRevealFade(revealT, gx0, gx1);
            if (overlayInduced != null)
                overlayInduced.SetGraphRevealFade(revealT, gx0, gx1);
        }
    }

    /// <summary>Graphing-calculator derivative overlays share the same left→right reveal as the primary plot.</summary>
    public void ApplyGraphRevealToLineRenderer(LineRendererUI lr)
    {
        if (lr == null || lineRenderer == null)
            return;
        Vector2Int go = lineRenderer.gridSize / 2;
        float revealT = functionType == FunctionType.ChaosLorenzButterflyX ? 1f : _graphRevealT01;
        lr.SetGraphRevealFade(revealT, MapDisplayX(xStart) + go.x, MapDisplayX(xEnd) + go.x);
    }

    public void InitPlotFunction()
    {
        PlotFunction(this.functionType);
    }

    // Refresh ONLY the original function graph, not the derivative one
    public void RefreshLine()
    {
        lineRenderer.enabled = false;
        lineRenderer.enabled = true;
    }

    public void RefreshDeriv()
    {
        if (derivRenderer == null)
            return;
        derivRenderer.enabled = false;
        derivRenderer.enabled = true;
    }

    public void RefreshGrid()
    {
        var grid = FindAnyObjectByType<GridRendererUI>();
        if (grid == null)
            return;
        grid.enabled = false;
        grid.enabled = true;
    }

    /// <summary>Current curve parameters (single source of truth for f, f′, platforms and audits).</summary>
    public GraphFunctionParams CurrentParams =>
        new GraphFunctionParams(functionType, transA, transK, transC, transD, power, baseN, customExpression, _lorenzPhaseScroll);

    /// <summary>Plotter → grid map from the last <see cref="ComputeGraph"/>.</summary>
    public GraphViewMapping CurrentMapping
    {
        get
        {
            Vector2Int gs = lineRenderer != null ? lineRenderer.gridSize : new Vector2Int(_cachedGridOriginY * 2, _cachedGridOriginY * 2);
            return new GraphViewMapping
            {
                MidX = _autoMidX, ScaleX = _autoScaleX, MidY = _autoMid, ScaleY = _autoScale,
                DMidY = _autoDMid, DScaleY = _autoDScale, GridSize = gs
            };
        }
    }

    /// <summary>Fit settings currently set on this plotter (inspector / LevelManager).</summary>
    public GraphViewMapping.FitSettings CurrentFitSettings => new GraphViewMapping.FitSettings
    {
        AutoVertical = autoScaleVertical,
        VerticalFill = verticalFillFraction,
        AutoHorizontal = autoScaleHorizontal,
        HorizontalFill = horizontalFillFraction,
        VerticalScaleMin = verticalScaleClampMin,
        VerticalScaleMax = verticalScaleClampMax,
        HorizontalScaleMin = horizontalScaleClampMin,
        HorizontalScaleMax = horizontalScaleClampMax,
        LockVerticalToHorizontal = lockVerticalPlotScaleToHorizontalWindow,
        DerivativeOwnAxis = derivativeOwnAxis,
    };

    /// <summary>
    /// f(x) in plotter coordinates (before adding grid origin), using current transforms.
    /// </summary>
    public float SampleCurvePlotterY(float xPlotter)
    {
        return GraphFunctionMath.Evaluate(CurrentParams, xPlotter);
    }

    /// <summary>Grid-space y of the curve at plotter x (includes auto vertical fit). Used by platforms &amp; Riemann UI.</summary>
    public float SampleCurveGridY(float xPlotter)
    {
        if (lineRenderer == null)
            lineRenderer = LineRendererUI.FindPrimaryCurve();
        if (lineRenderer != null)
            _cachedGridOriginY = lineRenderer.gridSize.y / 2;
        float raw = SampleCurvePlotterY(xPlotter);
        if (!IsFinite(raw))
            return float.NaN;
        return MapDisplayY(raw) + _cachedGridOriginY;
    }

    /// <summary>
    /// Graphing calculator: <c>dⁿy/dxⁿ</c> at plotter <paramref name="xPlotter"/> (same vertical map as <see cref="ComputeGraph"/>).
    /// Order 1 is the shared analytic / scaled-step derivative; higher orders use central differences.
    /// </summary>
    public float SampleNthDerivativeGridY(float xPlotter, int order)
    {
        if (lineRenderer == null)
            lineRenderer = LineRendererUI.FindPrimaryCurve();
        if (lineRenderer == null)
            return float.NaN;

        Vector2Int gridOrigin = lineRenderer.gridSize / 2;
        order = Mathf.Clamp(order, 1, 4);
        var prm = CurrentParams;
        float raw = order == 1
            ? GraphFunctionMath.Derivative(prm, xPlotter, TableCurveDerivativeStep)
            : MathNthDerivative.Evaluate(xx => GraphFunctionMath.Evaluate(prm, xx), xPlotter, order,
                GraphFunctionMath.NumericStep(xPlotter, 1e-2f));
        if (!IsFinite(raw))
            return float.NaN;
        return order == 1 ? CurrentMapping.DerivGridY(raw) : MapDisplayY(raw) + gridOrigin.y;
    }

    /// <summary>f′(x) in <b>raw</b> plotter units (not affected by vertical exaggeration). For gameplay thresholds.</summary>
    public float EvaluateNumericalDerivativeY(float xPlotter)
    {
        return GraphFunctionMath.Derivative(CurrentParams, xPlotter, TableCurveDerivativeStep);
    }

    /// <summary>
    /// Fallback step for curves without a closed-form f′ (Lorenz table, Mandelbrot): half the plot step, so the drawn
    /// f′ reads the slope of the drawn f instead of per-sample noise.
    /// </summary>
    float TableCurveDerivativeStep => TableCurveDerivativeStepFor(step);

    public static float TableCurveDerivativeStepFor(float plotStep) => Mathf.Clamp(plotStep * 0.5f, 1e-3f, 0.25f);

    float MapDisplayY(float rawY) => (rawY - _autoMid) * _autoScale;

    float MapDisplayX(float rawX) => (rawX - _autoMidX) * _autoScaleX;

    /// <summary>Grid column offset from center → plotter <c>x</c> sampled for f / f′ (inverse of <see cref="MapDisplayX"/>).</summary>
    public float DisplayOffsetFromCenterToPlotterX(float displayOffsetFromCenter)
    {
        float s = Mathf.Max(_autoScaleX, 1e-6f);
        return displayOffsetFromCenter / s + _autoMidX;
    }

    /// <summary>Plotter <c>x</c> → absolute grid X (same convention as <see cref="ComputeGraph"/> polyline points).</summary>
    public float MapPlotterXToGridX(float xPlotter, int gridOriginX) => MapDisplayX(xPlotter) + gridOriginX;

    /// <summary>
    /// Inverse of vertical display fit: grid tick offset from center (one unit = one plotter‑y step)
    /// maps to the <b>mathematical</b> y (or polar r) read on that horizontal line.
    /// When auto-fit is off, this is identity (mid 0, scale 1).
    /// </summary>
    public float AxisTickOffsetToMathY(float tickOffsetFromCenter)
    {
        float s = Mathf.Max(_autoScale, 1e-6f);
        return _autoMid + tickOffsetFromCenter / s;
    }

    /// <summary>
    /// Horizontal axis readout at a tick <paramref name="tickOffsetFromCenter"/> grid columns right of center
    /// (one column = one plotter unit in <c>x</c> or polar <c>θ</c>).
    /// - Level / preset graphs: tick shows plotter <c>x</c> (or <c>θ</c>).
    /// - Graphing calculator (<see cref="FunctionType.CustomExpression"/>): tick shows inner <c>u = transK×(x−transD)</c>
    ///   so numbers match <b>Trans</b> the same way your <c>f(u)</c> does.
    /// </summary>
    public float AxisTickOffsetToMathX(float tickOffsetFromCenter)
    {
        float xPlot = DisplayOffsetFromCenterToPlotterX(tickOffsetFromCenter);
        if (functionType == FunctionType.CustomExpression)
            return transK * (xPlot - transD);
        return xPlot;
    }

    /// <summary>Current vertical auto-fit pivot (math axis). For axis label refresh.</summary>
    public float VerticalAxisLabelPivot => _autoMid;

    /// <summary>Current vertical display stretch factor. For axis label refresh.</summary>
    public float VerticalAxisLabelScale => _autoScale;

    /// <summary>Horizontal auto-fit pivot in plotter <c>x</c>. For axis label refresh.</summary>
    public float HorizontalAxisLabelPivot => _autoMidX;

    /// <summary>Horizontal display stretch (grid columns per plotter‑<c>x</c> unit). For axis label refresh.</summary>
    public float HorizontalAxisLabelScale => _autoScaleX;

    public void SetEquationExtraSuffix(string suffix)
    {
        equationExtraSuffix = suffix ?? "";
        equationExtraSuffixColorHex = "57534A";
    }

    /// <param name="colorHex">With or without leading <c>#</c>; <c>null</c>/empty restores the default grey-blue.</param>
    public void SetEquationExtraSuffix(string suffix, string colorHex)
    {
        equationExtraSuffix = suffix ?? "";
        equationExtraSuffixColorHex = string.IsNullOrEmpty(colorHex) ? "57534A" : colorHex.TrimStart('#');
    }

    /// <summary>True when the curve is the polar graph <c>r(θ)</c> (horizontal axis = θ, vertical = r), not Cartesian <c>y(x)</c>.</summary>
    public static bool IsPolarPlotStyle(FunctionType type) =>
        type == FunctionType.PolarCardioid || type == FunctionType.PolarRose || type == FunctionType.PolarGoldenLogSpiral;

    /// <summary>Switches to typed expression mode (graphing calculator).</summary>
    public void SetCustomExpression(string expression)
    {
        customExpression = string.IsNullOrWhiteSpace(expression) ? "0" : expression.Trim();
        functionType = FunctionType.CustomExpression;
    }

    private void PlotFunction(FunctionType type)
    {
        lineRenderer = LineRendererUI.FindPrimaryCurve();
        derivRenderer = FindAnyObjectByType<DerivRendererUI>();

        // Lorenz: disable per-vertex horizontal fade (see UpdateGraphRevealAnimation); derivative uses same flag.
        bool lorenzButterfly = type == FunctionType.ChaosLorenzButterflyX;
        if (lineRenderer != null)
            lineRenderer.enableHorizontalGraphReveal = !lorenzButterfly;
        if (derivRenderer != null)
            derivRenderer.enableHorizontalGraphReveal = !lorenzButterfly;

        if (lineRenderer != null)
        {
            points.Clear();
            RefreshLine();

            ComputeGraph(type, transA, transK, transC, transD, power, baseN);
            lineRenderer.points = points;

            if (type == FunctionType.AeroDragPolarTriple)
            {
                EnsureDragPolarOverlayLines(lineRenderer);
                PopulateDragPolarOverlayPoints(transA, transK, transC, transD, power);
                overlayParasitic.gameObject.SetActive(true);
                overlayInduced.gameObject.SetActive(true);
            }
            else
                SetDragPolarOverlaysActive(false);
        }

        if (differentiate == true && lineRenderer != null)
        {
            // Refresh ONLY the derivative graph & show on the UI
            RefreshDeriv();

            // ComputeGraph above already filled dPoints from the same samples as f (one pass, one map).
            if (derivRenderer != null)
                derivRenderer.points = dPoints;
        }
        else if (differentiate == false)
        {
            dPoints.Clear();
            // Do not call RefreshDeriv() here — it re-enabled the Graphic every frame while stale
            // points could remain on DerivRendererUI (list is only aliased to dPoints when differentiate is true).
            if (derivRenderer != null)
            {
                derivRenderer.points.Clear();
                derivRenderer.enabled = false;
            }
        }

        // Stage backdrops: Mandelbrot; black hole; golden φ-spiral; wind tunnel (aerospace).
        if (lineRenderer != null)
        {
            var gridRt = lineRenderer.transform.parent as RectTransform;
            MandelbrotFractalBackdrop.Sync(gridRt, this);
            BlackHoleGargantuaBackdrop.Sync(gridRt, this);
            GoldenSpiralBackdrop.Sync(gridRt, this);
            WindTunnelBackdrop.Sync(gridRt, this);
            PushGraphRevealToRenderers();
        }

        _cachedPlotParamHash = ComputePlotInvalidationHash();
    }

    public void ComputeGraph(FunctionType functionType, float transA, float transK, float transC, float transD, int power, int baseN)
    {
        if (equationText != null)
            UpdateEquationText(functionType, transA, transK, transC, transD, power, baseN);

        Vector2Int gridSize = lineRenderer.gridSize;
        Vector2Int gridOrigin = gridSize / 2;
        _cachedGridOriginY = gridOrigin.y;

        var prm = new GraphFunctionParams(functionType, transA, transK, transC, transD, power, baseN, customExpression, _lorenzPhaseScroll);

        // Fit primarily to f(x) (and drag-polar overlays) so flat teaching curves fill the grid; f′ uses the same map.
        var map = GraphViewMapping.Fit(prm, xStart, xEnd, step, gridSize, CurrentFitSettings, TableCurveDerivativeStep);
        _autoDMid = map.DMidY;
        _autoDScale = map.DScaleY;
        _autoMid = map.MidY;
        _autoScale = map.ScaleY;
        _autoMidX = map.MidX;
        _autoScaleX = map.ScaleX;

        // Polylines: f and f′ from the shared math, clipped to the grid band (breaks instead of off-plane spikes).
        GraphPolylines.Sample(prm, map, xStart, xEnd, step, TableCurveDerivativeStep, points, dPoints);
    }

    private float EvaluateFunctionY(FunctionType type, float transA, float transK, float transC, float transD, int power, int baseN, float xValue) =>
        GraphFunctionMath.Evaluate(new GraphFunctionParams(type, transA, transK, transC, transD, power, baseN, customExpression, _lorenzPhaseScroll), xValue);

    private static bool IsFinite(float f) => GraphFunctionMath.IsFinite(f);

    private static string EscapeTmpRichText(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return raw.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private void UpdateEquationText(FunctionType type, float transA, float transK, float transC, float transD, int power, int baseN)
    {
        // Keep equation text simple and consistent; domain errors (e.g. log/sqrt of non-positive) are handled by skipping non-finite points.
        string a = transA.ToString();
        string k = transK.ToString();
        string c = transC.ToString();
        string d = transD.ToString();

        switch (type)
        {
            case FunctionType.Power:
                equationText.text = $@"\(f(x) = {a}\cdot\left({k}(x-{d})\right)^{{{power}}} + {c}\)";
                break;
            case FunctionType.Absolute:
                equationText.text = $@"\(f(x) = {a}\left(\left|{k}(x-{d})\right| + {c}\right)\)";
                break;
            case FunctionType.Exponential:
                // transA* (baseN^(k(x-d)) + c)
                equationText.text = $@"\(f(x) = {a}\left({baseN}^{{{k}(x-{d})}} + {c}\right)\)";
                break;
            case FunctionType.NaturalExp:
                equationText.text = $@"\(f(x) = {a}\left(e^{{{k}(x-{d})}} + {c}\right)\)";
                break;
            case FunctionType.Log:
                equationText.text = $@"\(f(x) = {a}\left(\log_{10}\!\left({k}(x-{d})\right) + {c}\right)\)";
                break;
            case FunctionType.NaturalLog:
                equationText.text = $@"\(f(x) = {a}\left(\ln\left({k}(x-{d})\right) + {c}\right)\)";
                break;
            case FunctionType.SquareRoot:
                equationText.text = $@"\(f(x) = {a}\left(\sqrt{{{k}(x-{d})}} + {c}\right)\)";
                break;
            case FunctionType.Sine:
                equationText.text = $@"\(f(x) = {a}\left(\sin\left({k}(x-{d})\right) + {c}\right)\)";
                break;
            case FunctionType.Cosine:
                equationText.text = $@"\(f(x) = {a}\left(\cos\left({k}(x-{d})\right) + {c}\right)\)";
                break;
            case FunctionType.Tangent:
                equationText.text = $@"\(f(x) = {a}\left(\tan\left({k}(x-{d})\right) + {c}\right)\)";
                break;
            case FunctionType.MaclaurinExpSeries:
                equationText.text = $@"\(P_{{{power}}}[e^{{u}}],\; u={k}(x-{d})\)"; 
                break;
            case FunctionType.MaclaurinSinSeries:
                equationText.text = $@"\(P_{{{power}}}[\sin u],\; u={k}(x-{d})\)";
                break;
            case FunctionType.MaclaurinCosSeries:
                equationText.text = $@"\(P_{{{power}}}[\cos u],\; u={k}(x-{d})\)";
                break;
            case FunctionType.GeometricSeriesPartial:
                equationText.text = $@"\(\sum_{{j=0}}^{{{power}}} u^{{j}},\; u={k}(x-{d})\)";
                break;
            case FunctionType.MultivarParaboloidSlice:
                equationText.text = $@"\(z={a}\left(u^{2}+y_{{0}}^{2}\right),\; u={k}(x-{d}),\; y_{{0}}={c}\)";
                break;
            case FunctionType.MultivarSaddleSlice:
                equationText.text = $@"\(z={a}\left(u^{2}-y_{{0}}^{2}\right),\; u={k}(x-{d}),\; y_{{0}}={c}\)";
                break;
            case FunctionType.SpringMassUndamped:
            {
                float omegaDisp = GraphFunctionMath.SpringMassUndampedOmega(power);
                equationText.text =
                    $@"<b>\(\text{{Spring–mass (undamped)}}\)</b> \(x \approx {a}\cos({omegaDisp:0.###}\,u)+{c},\; u={k}(x-{d})\)\n" +
                    $@"<size=88%><color=#57534A>Hooke \(F=-kx\Rightarrow \ddot{{x}}=-\omega^2 x\), \(\omega=\sqrt{{k/m}}\).</color></size>";
                break;
            }
            case FunctionType.DampedOscillator:
                equationText.text = $@"\(f={a}\, e^{{-\alpha|u|}}\sin(\omega u)+{c},\; u={k}(x-{d})\)";
                break;
            case FunctionType.HyperbolicCosine:
                equationText.text = $@"\(f={a}\left(\cosh(u)+{c}\right),\; u={k}(x-{d})\)";
                break;
            case FunctionType.FullWaveRectifiedSine:
                equationText.text = $@"\(f={a}\left(\left|\sin u\right|+{c}\right),\; u={k}(x-{d})\)";
                break;
            case FunctionType.Arctangent:
                equationText.text = $@"\(f={a}\arctan(u)+{c},\; u={k}(x-{d})\)";
                break;
            case FunctionType.Logistic:
                equationText.text = $@"\(\text{{Logistic}},\; L\approx{a},\; u={k}(x-{d})\)";
                break;
            case FunctionType.HyperbolicSine:
                equationText.text = $@"\(f={a}\sinh(u)+{c},\; u={k}(x-{d})\)";
                break;
            case FunctionType.ExponentialDecay:
                equationText.text = $@"\(f={a}\, e^{{-{k}|u|}}+{c},\; u={k}(x-{d})\)";
                break;
            case FunctionType.PolarCardioid:
            {
                float thLo = Mathf.Min(transK * (xStart - transD), transK * (xEnd - transD));
                float thHi = Mathf.Max(transK * (xStart - transD), transK * (xEnd - transD));
                equationText.text =
                    $@"\(r(\theta)={a}(1+\cos\theta)+{c}\)\n<size=88%><color=#57534A>\(\theta\in[{thLo:0.##},{thHi:0.##}]\)</color></size>";
                break;
            }
            case FunctionType.PolarRose:
            {
                int nRose = Mathf.Max(1, power);
                float thLo = Mathf.Min(transK * (xStart-transD), transK * (xEnd-transD));
                float thHi = Mathf.Max(transK * (xStart-transD), transK * (xEnd-transD));
                equationText.text =
                    $@"\(r(\theta)={a}\cos({nRose}\theta)+{c}\)\n<size=88%><color=#57534A>\(\theta\in[{thLo:0.##},{thHi:0.##}]\)</color></size>";
                break;
            }
            case FunctionType.PolarGoldenLogSpiral:
            {
                float thLo = Mathf.Min(transK * (xStart-transD), transK * (xEnd-transD));
                float thHi = Mathf.Max(transK * (xStart-transD), transK * (xEnd-transD));
                float kk = Mathf.Clamp(Mathf.Max(1, baseN) / 750f, 0.055f, 0.19f);
                string phiStr = GraphFunctionMath.GoldenRatioPhi.ToString("0.###");
                equationText.text =
                    $@"<b>\(\text{{Golden spiral}}\)</b> \(r(\theta)\approx {a}\varphi^{{{kk:0.###}\theta}}+{c}\)\n" +
                    $@"<size=88%><color=#57534A>\(\varphi=\frac{{1+\sqrt{{5}}}}{{2}}\approx {phiStr}\), \(\theta\in[{thLo:0.##},{thHi:0.##}]\), \(u={k}(x-{d})\).</color></size>";
                break;
            }
            case FunctionType.CircleUpper:
                equationText.text = $@"\(u^{2}+(y-{c})^{2}={a}^{2}\ \text{{(upper)}},\; u={k}(x-{d})\)";
                break;
            case FunctionType.AeroLiftVsAlpha:
                equationText.text = $@"\(C_L(\alpha)\ \text{{stall model}},\; \text{{slope}}\approx {a},\; u={k}(x-{d})\)";
                break;
            case FunctionType.AeroIsothermalDensity:
                equationText.text = $@"\(\rho/\rho_0 \propto e^{{-h/H}},\; u={k}(x-{d}),\; H^{{-1}}\propto {baseN}\)";
                break;
            case FunctionType.AeroNewtonianSinSquared:
                equationText.text = $@"\(C_p \propto \sin^{{2}}\alpha,\; u={k}(x-{d})\)";
                break;
            case FunctionType.AeroDragPolarTriple:
                equationText.text =
                    $@"<b>\(\text{{Drag polar — three traces}}\)</b>\n" +
                    $@"<size=92%><color=#57534A><b>Parasitic</b> \(C_{{D,\text{{par}}}} \approx {a}\cdot({c})\) (flat) | " +
                    $@"<b>Induced</b> \(C_{{D,\text{{ind}}}} = {a}\,u^{{{power}}}\) | " +
                    $@"<b>Total</b> \(C_{{D,\text{{tot}}}} = {a}\,(u^{{{power}}}+{c})\), \(u={k}(x-{d})\).</color></size>";
                break;
            case FunctionType.ThermoAdiabaticPV:
            {
                float g = Mathf.Clamp(baseN / 100f, 1.02f, 2.2f);
                equationText.text =
                    $@"<b>Adiabatic ideal gas</b> \(PV^{{\gamma}}=\text{{const}}\)\n" +
                    $@"<size=92%><color=#57534A>Sketch: \(P \propto u^{{-{g:0.##}}}\), \(u={k}(x-{d})\) (&gt;0). " +
                    $@"\(\gamma\) from <b>N</b>={baseN} → \(\gamma\approx {g:0.##}\).</color></size>";
                break;
            }
            case FunctionType.MandelbrotEscapeImSlice:
                equationText.text =
                    $@"<b>\(\text{{Mandelbrot slice}}\)</b> \(h\propto\text{{escape-time}},\; c=({a})+\mathrm{{i}}u,\; u={k}(x-{d}),\; N={power}\)";
                break;
            case FunctionType.EconomyDotcomBubbleStylized:
                equationText.text =
                    "<b>Stylized equity index path</b> (dot-com era <i>mood</i>)\n" +
                    $"<size=92%><color=#57534A>Not S&amp;P 500 or Nasdaq data — a smooth teaching curve through a <color=#1F4FA0>late-90s run-up</color>, <color=#D7261E>2000–02 drawdown</color>, and slow recovery. " +
                    $"Height = <b>{c}</b> + <b>{a}</b>×(piecewise path), <i>u</i> = <b>{k}</b>(x−<b>{d}</b>).</color></size>";
                break;
            case FunctionType.EconomySubprime2008Stylized:
                equationText.text =
                    "<b>Stylized index path</b> (2008 crisis <i>mood</i>)\n" +
                    $"<size=92%><color=#57534A>Not GSPC / real estate indices — qualitative <color=#D7261E>pre-crisis climb</color>, <color=#D7261E>sharp stress pocket</color>, then crawl. " +
                    $"Height = <b>{c}</b> + <b>{a}</b>×(piecewise path), <i>u</i> = <b>{k}</b>(x−<b>{d}</b>).</color></size>";
                break;
            case FunctionType.TransformFourierSinc:
                equationText.text =
                    $@"<b>Fourier mood — \(\mathrm{{sinc}}\)</b> \(\mathrm{{sinc}}(u)=\frac{{\sin u}}{{u}},\; u={k}(x-{d})\)\n" +
                    $@"<size=88%><color=#57534A>Rect pulse in time ↔ tall \(\mathrm{{sinc}}\) in frequency; side lobes are interference homework.</color></size>";
                break;
            case FunctionType.TransformLaplaceCausalDecay:
            {
                float sL = Mathf.Clamp(baseN / 100f, 0.1f, 4.5f);
                equationText.text =
                    $@"<b>Laplace mood — causal decay</b> \(f(t)=H(t)\, e^{{-{sL:0.##}t}},\; t\propto u={k}(x-{d})\)\n" +
                    $@"<size=88%><color=#57534A>Table entry \(\mathcal{{L}}\{{e^{{-at}}\}}=\frac{{1}}{{s+a}}\); walk the exponential after \(t=0\).</color></size>";
                break;
            }
            case FunctionType.ChaosLorenzButterflyX:
                equationText.text =
                    $@"<b>\(\text{{Chaos theory}}\)</b> — Lorenz \(x(t)\), \(\sigma=10,\rho=28,\beta=8/3\), \(u={k}(x-{d})\approx t\)\n" +
                    $@"<size=88%><color=#57534A>Tiny initial changes diverge on the attractor — this curve is a <b>time slice</b> of that chaos, normalized to read on the grid.</color></size>";
                break;
            case FunctionType.PhysicsGravityWellInverseSqrt:
                equationText.text =
                    $@"<b>\(\text{{Gravity well}}\)</b> slice \(f \propto -1/\sqrt{{u^{2}+\varepsilon^{2}}}+C,\; u={k}(x-{d}),\; \varepsilon\sim |{d}|\)\n" +
                    $@"<size=88%><color=#57534A>Softened \(1/r\) potential — steep near the center, flattens away; a readable stand-in for “falling into” a deep well.</color></size>";
                break;
            case FunctionType.BigONLogN:
                equationText.text =
                    $@"<b>\(O(n\log n)\)</b> growth mood: \(f \propto u\ln u + C,\; u=\max({k}(x-{d}),\text{{small}})\)\n" +
                    $@"<size=88%><color=#57534A>Classic merge-sort / balanced-tree flavor: work grows a bit faster than linear, slower than quadratic.</color></size>";
                break;
            case FunctionType.CustomExpression:
            {
                string esc = EscapeTmpRichText(customExpression);
                equationText.text =
                    $"<b>Typed f(u)</b>\n" +
                    $"<size=94%><color=#141414>{esc}</color></size>\n" +
                    $"<size=78%><color=#57534A>Plotted: <b>y = A×f(u)+C</b>, u = k×(x−D). Edit below. Use sin, cos, tan, sqrt, ln, log, exp, ^, pi, e …</color></size>";
                if (!string.IsNullOrEmpty(equationExtraSuffix))
                    equationText.text += $"\n<size=85%><color=#{equationExtraSuffixColorHex}>{equationExtraSuffix}</color></size>";
                return;
            }
            default:
                equationText.text = @"\(f(x)\)";
                break;
        }

        if (!string.IsNullOrEmpty(equationExtraSuffix))
            equationText.text += $"\n<size=85%><color=#{equationExtraSuffixColorHex}>{equationExtraSuffix}</color></size>";

        if (equationText != null)
            equationText.text = TmpLatex.Process(equationText.text);
    }

    /// <summary>LevelManager sets tint for parasitic / induced overlay lines (total stays <c>curveRenderer.color</c>).</summary>
    public void ConfigureDragPolarOverlayColors(Color parasitic, Color induced)
    {
        overlayDragPolarParasiticColor = parasitic;
        overlayDragPolarInducedColor = induced;
        if (overlayParasitic != null)
            overlayParasitic.color = parasitic;
        if (overlayInduced != null)
            overlayInduced.color = induced;
    }

    void SetDragPolarOverlaysActive(bool on)
    {
        if (overlayParasitic != null)
            overlayParasitic.gameObject.SetActive(on);
        if (overlayInduced != null)
            overlayInduced.gameObject.SetActive(on);
    }

    void EnsureDragPolarOverlayLines(LineRendererUI template)
    {
        if (template == null || overlayParasitic != null)
            return;

        overlayParasitic = CreateDragPolarOverlay(template, "Parasitic");
        overlayInduced = CreateDragPolarOverlay(template, "Induced");
        overlayParasitic.color = overlayDragPolarParasiticColor;
        overlayInduced.color = overlayDragPolarInducedColor;
        overlayParasitic.raycastTarget = false;
        overlayInduced.raycastTarget = false;
        float t = template.thickness;
        overlayParasitic.thickness = t * 0.78f;
        overlayInduced.thickness = t * 0.78f;
    }

    static LineRendererUI CreateDragPolarOverlay(LineRendererUI template, string objectName)
    {
        var parent = template.transform.parent;
        int idx = template.transform.GetSiblingIndex();
        var clone = Instantiate(template.gameObject, parent);
        clone.name = LineRendererUI.DragPolarOverlayNamePrefix + objectName;
        clone.transform.SetSiblingIndex(idx);
        return clone.GetComponent<LineRendererUI>();
    }

    void PopulateDragPolarOverlayPoints(float transA, float transK, float transC, float transD, int pow)
    {
        if (overlayParasitic == null || overlayInduced == null || lineRenderer == null)
            return;

        Vector2Int gridOrigin = lineRenderer.gridSize / 2;
        overlayParasitic.points.Clear();
        overlayInduced.points.Clear();

        for (float i = xStart; i <= xEnd; i += step)
        {
            float xValue = i;
            float u = transK * (xValue - transD);
            float yPar = transA * transC;
            float yInd = transA * Mathf.Pow(u, pow);

            if (IsFinite(yPar))
                overlayParasitic.points.Add(new Vector2(MapDisplayX(xValue) + gridOrigin.x, MapDisplayY(yPar) + gridOrigin.y));
            if (IsFinite(yInd))
                overlayInduced.points.Add(new Vector2(MapDisplayX(xValue) + gridOrigin.x, MapDisplayY(yInd) + gridOrigin.y));
        }

        overlayParasitic.enabled = false;
        overlayParasitic.enabled = true;
        overlayInduced.enabled = false;
        overlayInduced.enabled = true;
    }
}

public enum FunctionType
{
    Power,
    Absolute,
    Exponential,
    NaturalExp,
    Log,
    NaturalLog,
    SquareRoot,
    Sine,
    Cosine,
    Tangent,

    // Infinite series / Taylor–Maclaurin (partial sums)
    MaclaurinExpSeries,
    MaclaurinSinSeries,
    MaclaurinCosSeries,
    GeometricSeriesPartial,

    // Multivariable surfaces as 1D slices (fixed y₀ = transC, δ = transD shift)
    MultivarParaboloidSlice,
    MultivarSaddleSlice,

    // Engineering / applied classical shapes
    /// <summary>Undamped linear spring SHM: \(x=A\cos(\omega u)+x_0\); \texttt{power} sets \(\omega\), \(u=k(x-D)\).</summary>
    SpringMassUndamped,
    DampedOscillator,
    HyperbolicCosine,
    FullWaveRectifiedSine,

    // AP Calculus BC extras, polar (r vs θ plotted with θ on the horizontal axis), Physics C hooks
    Arctangent,
    Logistic,
    HyperbolicSine,
    ExponentialDecay,
    PolarCardioid,
    PolarRose,

    // Upper semicircle: y = k + √(R²−u²), u = transK×(x−transD), R = |transA|, center (transD, transC) when transK = 1.
    CircleUpper,

    // Aerospace / aerodynamics (toy models for instruction — not a CFD solver)
    AeroLiftVsAlpha,
    AeroIsothermalDensity,
    AeroNewtonianSinSquared,

    /// <summary>Escape iteration count vs Im(c) with fixed Re(c) = transA (boss slice); uses |Im| in iteration for conjugate symmetry.</summary>
    MandelbrotEscapeImSlice,

    /// <summary>User-typed <c>f(u)</c> via <see cref="FunctionPlotter.customExpression"/> (graphing calculator).</summary>
    CustomExpression,

    /// <summary>
    /// Parabolic drag polar with <b>three</b> rendered curves: \(C_{D,\mathrm{par}}\) (flat),
    /// \(C_{D,\mathrm{ind}} \propto u^{\texttt{power}}\), and total \(C_{D,\mathrm{tot}}\) (same as <see cref="Power"/> with \(u=k(x-D)\)).
    /// </summary>
    AeroDragPolarTriple,

    /// <summary>Smooth stylized “index chart” evoking late-90s run-up and drawdown (not real market data).</summary>
    EconomyDotcomBubbleStylized,

    /// <summary>Smooth stylized path evoking 2007–09 equity stress / recovery (not real market data).</summary>
    EconomySubprime2008Stylized,

    /// <summary>Physics C thermo: adiabatic \(P\propto V^{-\gamma}\); \(\gamma=\texttt{baseN}/100\) (e.g. 140 → 1.40), \(u=k(x-D)&gt;0\) as volume-like axis.</summary>
    ThermoAdiabaticPV,

    /// <summary>Boss: polar graph \(r(\theta)=A\varphi^{k\theta}+C\) with golden ratio \(\varphi\); horizontal axis = θ.</summary>
    PolarGoldenLogSpiral,

    /// <summary>\(\mathrm{sinc}(u)=\sin u/u\) — Fourier transform flavor of a rectangular pulse.</summary>
    TransformFourierSinc,

    /// <summary>Causal \(H(t)\,e^{-st}\) — Laplace-table exponential decay for \(t\ge 0\); \(s\sim \texttt{baseN}/100\).</summary>
    TransformLaplaceCausalDecay,

    /// <summary>Final boss: Lorenz attractor \(x(t)\) (normalized); chaos theory / sensitive dependence visualization.</summary>
    ChaosLorenzButterflyX,

    /// <summary>Boss: softened \(1/r\) gravitational potential slice (deep well at \(u=0\)).</summary>
    PhysicsGravityWellInverseSqrt,

    /// <summary>CS: \(O(n\log n)\) growth shape via \(u\ln u\) (positive branch).</summary>
    BigONLogN
}

/* 

:: Numerical Differentiation Explanation ::

You can't calculate the exact derivative of a function using a computer program (unless you're doing symbolic math... but that's another, way more complicated, topic).

There are several approaches to computing a numerical derivative of a function. The simplest is the centered three-point method:

Take a small number h
Evaluate  [f(x+h) - f(x-h)] / 2h 
Voilà, an approximation of f'(x), with only two function evaluations
Another approach is the centered five-point method:

Take a small number h
Evaluate [f(x-2h) - 8f(x-h) + 8f(x+h) - f(x+2h)] / 12h
Voilà, a better approximation of f'(x), but it requires more function evaluations

In this program, we use the relatively simpler approach 'the three-point method.'

*/