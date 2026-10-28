using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>f′ accuracy for every function family, and f / f′ / platforms agreeing on one source of truth.</summary>
public class GraphDerivativeTests
{
    static List<LevelDefinition> _levels;
    static List<LevelDefinition> Levels => _levels ??= LevelAudit.LoadCurriculum();

    /// <summary>Five-point central difference (O(h⁴)); NaN when f is undefined on the stencil.</summary>
    static float FivePoint(in GraphFunctionParams p, float x, float h)
    {
        float a = GraphFunctionMath.Evaluate(p, x - 2f * h), b = GraphFunctionMath.Evaluate(p, x - h);
        float c = GraphFunctionMath.Evaluate(p, x + h), d = GraphFunctionMath.Evaluate(p, x + 2f * h);
        if (!GraphFunctionMath.IsFinite(a) || !GraphFunctionMath.IsFinite(b) || !GraphFunctionMath.IsFinite(c) || !GraphFunctionMath.IsFinite(d))
            return float.NaN;
        return (a - 8f * b + 8f * c - d) / (12f * h);
    }

    /// <summary>
    /// Compares the analytic f′ with a five-point difference at <paramref name="samples"/> points. Points where two step
    /// sizes disagree (kinks, clamp edges, knot joins) are skipped; at least <paramref name="minCheckedFraction"/> must be checked.
    /// </summary>
    static void AssertAnalyticMatchesNumeric(in GraphFunctionParams p, float x0, float x1, int samples, string label,
        float minCheckedFraction = 0.8f)
    {
        int finite = 0, checkedCount = 0;
        for (int i = 0; i < samples; i++)
        {
            float x = Mathf.Lerp(x0, x1, (i + 0.37f) / samples);
            float a = GraphFunctionMath.Derivative(p, x);
            float f = GraphFunctionMath.Evaluate(p, x);
            if (!GraphFunctionMath.IsFinite(a) || !GraphFunctionMath.IsFinite(f))
                continue;
            finite++;

            float h = 4e-3f * Mathf.Max(1f, Mathf.Abs(x)) / Mathf.Max(1f, Mathf.Abs(p.K));
            float n1 = FivePoint(p, x, h);
            float n2 = FivePoint(p, x, h * 0.5f);
            if (!GraphFunctionMath.IsFinite(n1) || !GraphFunctionMath.IsFinite(n2))
                continue;
            float roundoff = 4e-7f * (Mathf.Abs(f) + 1f) / h;
            if (Mathf.Abs(n1 - n2) > 2e-3f * (1f + Mathf.Abs(n2)) + 2f * roundoff)
                continue; // not smooth on this stencil
            checkedCount++;
            float tol = 2e-3f * (1f + Mathf.Abs(n2)) + 2f * roundoff;
            Assert.LessOrEqual(Mathf.Abs(a - n2), tol, $"{label}: f′({x}) analytic {a} vs numeric {n2}");
        }

        Assert.Greater(finite, 0, $"{label}: no finite samples");
        Assert.GreaterOrEqual(checkedCount, minCheckedFraction * finite, $"{label}: only {checkedCount}/{finite} smooth samples checked");
    }

    [Test]
    public void AnalyticDerivative_MatchesNumeric_ForEveryFamily()
    {
        foreach (FunctionType type in Enum.GetValues(typeof(FunctionType)))
        {
            if (!GraphFunctionMath.HasAnalyticDerivative(type))
                continue;
            foreach (int power in new[] { 0, 1, 2, 3, 5 })
            {
                var p = new GraphFunctionParams(type, 1.3f, 0.7f, -0.4f, 0.3f, power, 3);
                // Domains that keep each family inside its interesting range.
                float x0 = -6f, x1 = 6f;
                switch (type)
                {
                    case FunctionType.ThermoAdiabaticPV:
                        p.BaseN = 140;
                        break;
                    case FunctionType.Logistic:
                    case FunctionType.TransformLaplaceCausalDecay:
                        p.BaseN = 120;
                        break;
                    case FunctionType.PolarGoldenLogSpiral:
                        p.BaseN = 105;
                        break;
                    case FunctionType.CircleUpper:
                        p.A = 4f;
                        break;
                    case FunctionType.Tangent:
                        x0 = -1.6f;
                        x1 = 2.4f; // stays between the asymptotes at u = ±π/2
                        break;
                }
                AssertAnalyticMatchesNumeric(p, x0, x1, 160, $"{type} n={power}", type == FunctionType.Absolute ? 0.7f : 0.6f);
            }
        }
    }

    [Test]
    public void AnalyticDerivative_MatchesNumeric_OnEveryCurriculumLevel()
    {
        for (int i = 0; i < Levels.Count; i++)
        {
            var def = Levels[i];
            var p = GraphFunctionParams.FromLevel(def);
            if (!GraphFunctionMath.HasAnalyticDerivative(p.Type))
                continue;
            AssertAnalyticMatchesNumeric(p, def.xStart, def.xEnd, 240, $"level {i} {def.levelName}", 0.6f);
        }
    }

    [Test]
    public void TableCurves_UseSmoothedDifference_NotSampleNoise()
    {
        // Lorenz (piecewise-linear table) and Mandelbrot (iteration count): f′ must stay finite and bounded where f is.
        foreach (int idx in new[] { 47, 48 })
        {
            var def = Levels[idx];
            var p = GraphFunctionParams.FromLevel(def);
            float h = FunctionPlotter.TableCurveDerivativeStepFor(def.step);
            float fLo = float.MaxValue, fHi = float.MinValue, dMax = 0f;
            for (float x = def.xStart; x <= def.xEnd; x += def.step)
            {
                float f = GraphFunctionMath.Evaluate(p, x);
                float d = GraphFunctionMath.Derivative(p, x, h);
                Assert.IsTrue(GraphFunctionMath.IsFinite(f) && GraphFunctionMath.IsFinite(d), $"level {idx} x={x}");
                fLo = Mathf.Min(fLo, f);
                fHi = Mathf.Max(fHi, f);
                dMax = Mathf.Max(dMax, Mathf.Abs(d));
            }
            // A slope steeper than the whole band per half plot step would be sample noise, not the drawn curve.
            Assert.Less(dMax, (fHi - fLo) / h + 1e-3f, $"level {idx}: f′ max {dMax}");
        }
    }

    [Test]
    public void LegacyFloatStep_WasLossyOnLargeCurves_NewDerivativeIsNot()
    {
        // Catenary level: f reaches ~160, the old fixed h = 1e-4 central difference in float lost digits there.
        var def = Levels[15];
        var p = GraphFunctionParams.FromLevel(def);
        float worstOld = 0f, worstNew = 0f;
        for (float x = def.xStart; x <= def.xEnd; x += 0.37f)
        {
            double u = p.K * (x - p.D);
            double exact = p.A * Math.Sinh(Math.Max(-8.0, Math.Min(8.0, u))) * p.K;
            if (Math.Abs(u) > 8.0) exact = 0.0;
            worstOld = Mathf.Max(worstOld, (float)Math.Abs(GraphFunctionMath.CentralDifference(p, x, 1e-4f) - exact));
            worstNew = Mathf.Max(worstNew, (float)Math.Abs(GraphFunctionMath.Derivative(p, x) - exact));
        }
        Assert.Less(worstNew, 1e-3f, "analytic f′ error");
        Assert.Less(worstNew, worstOld, "analytic f′ beats the old step");
    }

    [Test]
    public void DrawnDerivative_IsTheGameplayDerivative()
    {
        foreach (var def in Levels)
        {
            var b = LevelAudit.Build(def, 0, LevelAudit.Profiles[0]);
            float dStep = FunctionPlotter.TableCurveDerivativeStepFor(def.step);
            int checkedPts = 0;
            for (int i = 0; i < b.Deriv.Count; i++)
            {
                var pt = b.Deriv[i];
                if (PolylineClip.IsBreak(pt) || pt.y <= -PolylineClip.DefaultBandMargin + 1e-3f || pt.y >= LevelAudit.GameGrid.y + PolylineClip.DefaultBandMargin - 1e-3f)
                    continue;
                float x = b.Map.PlotterXFromGridX(pt.x);
                float expected = b.Map.DerivGridY(GraphFunctionMath.Derivative(b.Params, x, dStep));
                Assert.AreEqual(expected, pt.y, 2e-3f * (1f + Mathf.Abs(expected)), $"{def.levelName} f′ polyline at x={x}");
                checkedPts++;
            }
            Assert.Greater(checkedPts, 0, $"{def.levelName}: f′ never visible");
        }
    }

    [Test]
    public void DrawnDerivative_MatchesSlopeOfDrawnCurve()
    {
        foreach (var def in Levels)
        {
            var b = LevelAudit.Build(def, 0, LevelAudit.Profiles[0]);
            if (!GraphFunctionMath.HasAnalyticDerivative(b.Params.Type))
                continue;
            int good = 0, total = 0;
            for (int i = 0; i + 1 < b.Curve.Count; i++)
            {
                Vector2 a = b.Curve[i], c = b.Curve[i + 1];
                if (PolylineClip.IsBreak(a) || PolylineClip.IsBreak(c) || Mathf.Abs(c.x - a.x) < 1e-5f)
                    continue;
                float xa = b.Map.PlotterXFromGridX(a.x), xc = b.Map.PlotterXFromGridX(c.x);
                // Mean value theorem: the drawn secant (grid units back to plotter units) lies within f′'s range on the segment.
                float lo = float.MaxValue, hi = float.MinValue;
                bool finite = true;
                for (int k = 0; k <= 8; k++)
                {
                    float d = GraphFunctionMath.Derivative(b.Params, Mathf.Lerp(xa, xc, k / 8f));
                    if (!GraphFunctionMath.IsFinite(d)) { finite = false; break; }
                    lo = Mathf.Min(lo, d);
                    hi = Mathf.Max(hi, d);
                }
                if (!finite)
                    continue;
                total++;
                float secant = (c.y - a.y) / (c.x - a.x) * b.Map.ScaleX / b.Map.ScaleY;
                float slack = 1e-3f * (1f + Mathf.Abs(secant)) + 0.02f * (hi - lo);
                if (secant >= lo - slack && secant <= hi + slack)
                    good++;
            }
            Assert.Greater(total, 0, def.levelName);
            Assert.GreaterOrEqual(good, 0.97f * total, $"{def.levelName}: drawn f′ disagrees with the slope of drawn f ({good}/{total})");
        }
    }

    [Test]
    public void PolylineClip_BreaksAtPlaneEdges_AndAsymptotes()
    {
        var raw = new List<Vector2>();
        var p = new GraphFunctionParams(FunctionType.Tangent, 1f, 1f, 0f, 0f, 2, 2);
        for (float x = -4f; x <= 4f; x += 0.05f)
            raw.Add(new Vector2(x, GraphFunctionMath.Evaluate(p, x) + 7f));
        var outList = new List<Vector2>();
        PolylineClip.ClipToBand(raw, 0f, 14f, outList);

        int breaks = 0;
        for (int i = 0; i < outList.Count; i++)
        {
            if (PolylineClip.IsBreak(outList[i]))
            {
                breaks++;
                continue;
            }
            Assert.That(outList[i].y, Is.InRange(-1e-4f, 14f + 1e-4f));
            if (i + 1 < outList.Count && !PolylineClip.IsBreak(outList[i + 1]))
                Assert.Less(Mathf.Abs(outList[i + 1].y - outList[i].y), 7f, "segment jumps across the band (asymptote drawn)");
        }
        Assert.GreaterOrEqual(breaks, 2, "tan branches should be separate runs");
    }
}
