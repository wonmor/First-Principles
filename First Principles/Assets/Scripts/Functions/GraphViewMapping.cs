using UnityEngine;

/// <summary>
/// Plotter units to grid cells: the auto-fit used by <see cref="FunctionPlotter"/> for the drawn f / f′ and by
/// <see cref="GraphWorldBuilder"/> for platforms. Engine-free so the EditMode audits use the exact same map.
/// Grid space: gridX = (x − MidX) × ScaleX + gridSize.x / 2, gridY = (y − MidY) × ScaleY + gridSize.y / 2
/// (integer halves, matching the polyline convention).
/// f′ has its own vertical map (<see cref="DMidY"/>, <see cref="DScaleY"/>): in level play it is pivoted at 0, so f′ = 0 sits
/// on the plane's centre line whatever f's offset is (it used to share f's pivot and was drawn off the plane on most levels).
/// </summary>
public struct GraphViewMapping
{
    public float MidX, ScaleX, MidY, ScaleY;
    /// <summary>Vertical map for f′ (equal to MidY / ScaleY when the derivative shares f's axis, e.g. the graphing calculator).</summary>
    public float DMidY, DScaleY;
    public Vector2Int GridSize;

    public Vector2Int GridOrigin => GridSize / 2;

    public static GraphViewMapping Identity(Vector2Int gridSize) =>
        new GraphViewMapping { MidX = 0f, ScaleX = 1f, MidY = 0f, ScaleY = 1f, DMidY = 0f, DScaleY = 1f, GridSize = gridSize };

    public float DisplayX(float xPlot) => (xPlot - MidX) * ScaleX;
    public float DisplayY(float yRaw) => (yRaw - MidY) * ScaleY;
    public float GridX(float xPlot) => DisplayX(xPlot) + GridOrigin.x;
    public float GridY(float yRaw) => DisplayY(yRaw) + GridOrigin.y;
    /// <summary>f′ value → grid y on the derivative's own axis.</summary>
    public float DerivGridY(float dyRaw) => (dyRaw - DMidY) * DScaleY + GridOrigin.y;
    /// <summary>Grid y of f′ = <paramref name="threshold"/> (the floor / gap line in level play).</summary>
    public float DerivThresholdGridY(float threshold) => DerivGridY(threshold);

    /// <summary>Grid x → plotter x (inverse of <see cref="GridX"/>).</summary>
    public float PlotterXFromGridX(float gridX) => (gridX - GridOrigin.x) / Mathf.Max(ScaleX, 1e-6f) + MidX;

    /// <summary>Grid y → raw plotter y (inverse of <see cref="GridY"/>).</summary>
    public float RawYFromGridY(float gridY) => (gridY - GridOrigin.y) / Mathf.Max(ScaleY, 1e-6f) + MidY;

    /// <summary>Fit settings copied from the plotter / level definition.</summary>
    public struct FitSettings
    {
        public bool AutoVertical;
        public float VerticalFill;
        public bool AutoHorizontal;
        public float HorizontalFill;
        public float VerticalScaleMin, VerticalScaleMax;
        public float HorizontalScaleMin, HorizontalScaleMax;
        public bool LockVerticalToHorizontal;
        /// <summary>Level play: fit f′ on its own axis pivoted at 0 (see <see cref="GraphViewMapping"/>).</summary>
        public bool DerivativeOwnAxis;

        public static FitSettings ForLevel(LevelDefinition def) => new FitSettings
        {
            AutoVertical = def.autoFitGraphVertical,
            VerticalFill = def.graphVerticalFillFraction,
            AutoHorizontal = def.autoFitGraphHorizontal,
            HorizontalFill = def.graphHorizontalFillFraction,
            VerticalScaleMin = FunctionPlotter.DefaultVerticalScaleClampMin,
            VerticalScaleMax = FunctionPlotter.DefaultVerticalScaleClampMax,
            HorizontalScaleMin = FunctionPlotter.DefaultHorizontalScaleClampMin,
            HorizontalScaleMax = FunctionPlotter.DefaultHorizontalScaleClampMax,
            LockVerticalToHorizontal = false,
            DerivativeOwnAxis = true,
        };
    }

    /// <summary>Samples f over [xStart, xEnd] at <paramref name="step"/> (same loop as the drawn polyline) and fits it.</summary>
    public static GraphViewMapping Fit(in GraphFunctionParams p, float xStart, float xEnd, float step, Vector2Int gridSize, in FitSettings s,
        float derivStep = 0f)
    {
        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        step = Mathf.Max(step, 1e-4f);
        var dAbs = s.DerivativeOwnAxis ? new System.Collections.Generic.List<float>() : null;
        for (float x = xStart; x <= xEnd; x += step)
        {
            AddExtent(GraphFunctionMath.Evaluate(p, x), ref lo, ref hi);
            if (dAbs != null)
            {
                float d = GraphFunctionMath.Derivative(p, x, derivStep);
                if (GraphFunctionMath.IsFinite(d))
                    dAbs.Add(Mathf.Abs(d));
            }
            if (p.Type == FunctionType.AeroDragPolarTriple)
            {
                float u = p.K * (x - p.D);
                AddExtent(p.A * p.C, ref lo, ref hi);
                AddExtent(p.A * Mathf.Pow(u, p.Power), ref lo, ref hi);
            }
        }

        var m = Identity(gridSize);
        // Vertical: the min clamp may not push the band off the plane (it did for wide-range curves such as cosh, x², e^x).
        FitAxis(s.AutoVertical, gridSize.y, lo, hi, s.VerticalFill, s.VerticalScaleMin, s.VerticalScaleMax, out m.MidY, out m.ScaleY,
            neverOverflow: true);
        bool hOk = gridSize.x >= 2 && Mathf.Abs(xEnd - xStart) >= 1e-6f;
        FitAxis(s.AutoHorizontal && hOk, gridSize.x, Mathf.Min(xStart, xEnd), Mathf.Max(xStart, xEnd), s.HorizontalFill,
            s.HorizontalScaleMin, s.HorizontalScaleMax, out m.MidX, out m.ScaleX);

        if (s.LockVerticalToHorizontal && !FunctionPlotter.IsPolarPlotStyle(p.Type))
        {
            m.MidY = 0f;
            m.ScaleY = m.ScaleX;
        }

        m.DMidY = m.MidY;
        m.DScaleY = m.ScaleY;
        if (dAbs != null && dAbs.Count > 0)
        {
            // Robust extent (90th percentile of |f′|): asymptote spikes (tan, √x, ln x near 0, circle edges) clip off
            // the plane instead of flattening the rest of f′ to a line.
            dAbs.Sort();
            float ext = dAbs[Mathf.Clamp(Mathf.CeilToInt(0.9f * dAbs.Count) - 1, 0, dAbs.Count - 1)];
            FitAxis(true, gridSize.y, -ext, ext, s.VerticalFill, 1e-6f, s.VerticalScaleMax, out m.DMidY, out m.DScaleY, neverOverflow: true);
            m.DMidY = 0f;
        }
        return m;
    }

    static void AddExtent(float v, ref float lo, ref float hi)
    {
        if (!GraphFunctionMath.IsFinite(v))
            return;
        if (v < lo) lo = v;
        if (v > hi) hi = v;
    }

    /// <summary>Pivot on 0 when the band straddles it, else on the band centre; stretch into the fill fraction.</summary>
    static void FitAxis(bool enabled, int cells, float lo, float hi, float fill, float scaleMin, float scaleMax, out float mid, out float scale,
        bool neverOverflow = false)
    {
        if (!enabled || float.IsInfinity(lo) || float.IsInfinity(hi) || lo > hi)
        {
            mid = 0f;
            scale = 1f;
            return;
        }

        mid = lo <= 0f && hi >= 0f ? 0f : (lo + hi) * 0.5f;
        float halfSpan = Mathf.Max(Mathf.Max(Mathf.Abs(lo - mid), Mathf.Abs(hi - mid)), 1e-3f) * 1.085f;
        const float marginCells = 0.85f;
        float targetHalf = Mathf.Max(fill * (cells * 0.5f - marginCells), 0.72f);
        float raw = targetHalf / halfSpan;
        scale = Mathf.Clamp(raw, neverOverflow ? Mathf.Min(scaleMin, raw) : scaleMin, scaleMax);
    }
}

/// <summary>Builds the drawn f and f′ polylines (grid space, clipped with breaks). Used by the plotter and the audits.</summary>
public static class GraphPolylines
{
    [System.ThreadStatic] static System.Collections.Generic.List<Vector2> _rawCurve, _rawDeriv;

    public static void Sample(in GraphFunctionParams p, in GraphViewMapping map, float xStart, float xEnd, float step, float derivStep,
        System.Collections.Generic.List<Vector2> curveOut, System.Collections.Generic.List<Vector2> derivOut)
    {
        _rawCurve ??= new System.Collections.Generic.List<Vector2>();
        _rawDeriv ??= new System.Collections.Generic.List<Vector2>();
        _rawCurve.Clear();
        _rawDeriv.Clear();
        step = Mathf.Max(step, 1e-4f);
        for (float x = xStart; x <= xEnd; x += step)
        {
            float gx = map.GridX(x);
            float y = GraphFunctionMath.Evaluate(p, x);
            float dy = GraphFunctionMath.Derivative(p, x, derivStep);
            _rawCurve.Add(new Vector2(gx, GraphFunctionMath.IsFinite(y) ? map.GridY(y) : float.NaN));
            _rawDeriv.Add(new Vector2(gx, GraphFunctionMath.IsFinite(dy) ? map.DerivGridY(dy) : float.NaN));
        }

        float lo = -PolylineClip.DefaultBandMargin, hi = map.GridSize.y + PolylineClip.DefaultBandMargin;
        if (curveOut != null)
            PolylineClip.ClipToBand(_rawCurve, lo, hi, curveOut);
        if (derivOut != null)
            PolylineClip.ClipToBand(_rawDeriv, lo, hi, derivOut);
    }
}
