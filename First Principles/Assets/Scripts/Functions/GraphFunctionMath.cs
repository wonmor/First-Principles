/*
 * GraphFunctionMath.cs — First Principles
 *
 * Engine-free evaluation of every FunctionType curve and its derivative.
 * One source of truth for the drawn f / f′ polylines (FunctionPlotter), the platform /
 * hazard columns (GraphWorldBuilder) and the EditMode audits.
 *
 *   f(x) uses u = K × (x − D); each family documents its own role for A, C, power and baseN.
 *   f′(x) is analytic (chain rule, du/dx = K) wherever the family has a closed form, including the
 *   flat regions of clamped models and the smoothed Lorenz series. Only the Mandelbrot iteration count and
 *   typed expressions use a central difference, evaluated with a step scaled to |x| so float cancellation
 *   stays far below the slope (the old fixed h = 1e-4 lost 2-3 digits on curves with large f).
 */

using UnityEngine;

/// <summary>Curve parameters shared by the plotter, the world builder and the tests.</summary>
public struct GraphFunctionParams
{
    public FunctionType Type;
    public float A, K, C, D;
    public int Power;
    public int BaseN;
    /// <summary>Typed expression for <see cref="FunctionType.CustomExpression"/>.</summary>
    public string Expression;
    /// <summary>Time offset for <see cref="FunctionType.ChaosLorenzButterflyX"/> (0 in level mode).</summary>
    public float LorenzPhase;

    public GraphFunctionParams(FunctionType type, float a, float k, float c, float d, int power, int baseN,
        string expression = null, float lorenzPhase = 0f)
    {
        Type = type;
        A = a;
        K = k;
        C = c;
        D = d;
        Power = power;
        BaseN = baseN;
        Expression = expression;
        LorenzPhase = lorenzPhase;
    }

    public static GraphFunctionParams FromLevel(LevelDefinition def) =>
        new GraphFunctionParams(def.functionType, def.transA, def.transK, def.transC, def.transD, def.power, def.baseN);
}

public static class GraphFunctionMath
{
    /// <summary>True when <see cref="Derivative"/> uses a closed form for this family.</summary>
    public static bool HasAnalyticDerivative(FunctionType type) =>
        type != FunctionType.MandelbrotEscapeImSlice
        && type != FunctionType.CustomExpression;

    public static bool IsFinite(float f) => !(float.IsNaN(f) || float.IsInfinity(f));

    /// <summary>Central-difference step for x: relative to |x| so x ± h stays well resolved in float.</summary>
    public static float NumericStep(float x, float minStep = 1e-3f) => Mathf.Max(minStep, 1e-3f * Mathf.Abs(x));

    /// <summary>Plain central difference of <see cref="Evaluate"/> (used by the tests as an oracle, and as the fallback).</summary>
    public static float CentralDifference(in GraphFunctionParams p, float x, float h)
    {
        float yp = Evaluate(p, x + h);
        float ym = Evaluate(p, x - h);
        if (!IsFinite(yp) || !IsFinite(ym))
            return float.NaN;
        return (yp - ym) / (2f * h);
    }

    // ------------------------------------------------------------------ f(x)

    public static float Evaluate(in GraphFunctionParams p, float x)
    {
        float a = p.A, c = p.C;
        int power = p.Power, baseN = p.BaseN;
        float u = p.K * (x - p.D);

        switch (p.Type)
        {
            case FunctionType.CustomExpression:
                return MathExpressionEvaluator.TryEvaluate(p.Expression ?? "0", u, out float fy, out _) ? a * fy + c : float.NaN;
            case FunctionType.Power: return a * (Mathf.Pow(u, power) + c);
            case FunctionType.Absolute: return a * (Mathf.Abs(u) + c);
            case FunctionType.Exponential: return a * (Mathf.Pow(baseN, u) + c);
            case FunctionType.NaturalExp: return a * (Mathf.Exp(u) + c);
            case FunctionType.Log: return a * (Mathf.Log10(u) + c);
            case FunctionType.NaturalLog: return a * (Mathf.Log(u) + c);
            case FunctionType.SquareRoot: return a * (Mathf.Sqrt(u) + c);
            case FunctionType.Sine: return a * (Mathf.Sin(u) + c);
            case FunctionType.Cosine: return a * (Mathf.Cos(u) + c);
            case FunctionType.Tangent: return a * (Mathf.Tan(u) + c);

            // Maclaurin (Taylor at 0) partial sums: `power` = number of terms beyond the constant where applicable.
            case FunctionType.MaclaurinExpSeries: return a * (MaclaurinExpPartialSum(u, power) + c);
            case FunctionType.MaclaurinSinSeries: return a * (MaclaurinSinPartialSum(u, power) + c);
            case FunctionType.MaclaurinCosSeries: return a * (MaclaurinCosPartialSum(u, power) + c);
            case FunctionType.GeometricSeriesPartial: return a * (GeometricPartialSum(u, power) + c);

            // Multivariable slices: fix y0 = C, plot z along x.
            case FunctionType.MultivarParaboloidSlice: return a * (u * u + c * c);
            case FunctionType.MultivarSaddleSlice: return a * (u * u - c * c);

            // Engineering: `power` sets the oscillation index, `baseN` the decay strength.
            case FunctionType.SpringMassUndamped: return a * Mathf.Cos(SpringMassUndampedOmega(power) * u) + c;
            case FunctionType.DampedOscillator:
            {
                float omega = DampedOmega(power), decay = DampedDecay(baseN);
                return a * Mathf.Exp(-decay * Mathf.Abs(u)) * Mathf.Sin(omega * u) + c;
            }
            case FunctionType.HyperbolicCosine: return a * ((float)System.Math.Cosh(Mathf.Clamp(u, -8f, 8f)) + c);
            case FunctionType.FullWaveRectifiedSine: return a * (Mathf.Abs(Mathf.Sin(u)) + c);

            // AP Calculus BC and polar (u plays θ in the polar captions).
            case FunctionType.Arctangent: return a * Mathf.Atan(u) + c;
            case FunctionType.Logistic:
            {
                float s = LogisticSteepness(baseN);
                float z = Mathf.Clamp(-s * u, -30f, 30f);
                return a / (1f + Mathf.Exp(z)) + c;
            }
            case FunctionType.HyperbolicSine: return a * (float)System.Math.Sinh(Mathf.Clamp(u, -4f, 4f)) + c;
            case FunctionType.ExponentialDecay: return a * Mathf.Exp(-Mathf.Max(0.02f, p.K) * Mathf.Abs(u)) + c;
            case FunctionType.PolarCardioid: return a * (1f + Mathf.Cos(u)) + c;
            case FunctionType.PolarRose: return a * Mathf.Cos(Mathf.Max(1, power) * u) + c;
            case FunctionType.PolarGoldenLogSpiral:
            {
                float k = GoldenSpiralGrowth(baseN);
                float r = a * Mathf.Pow(GoldenRatioPhi, Mathf.Clamp(k * u, -5.2f, 5.2f));
                return IsFinite(r) ? r + c : float.NaN;
            }

            // Upper half of u² + (y − C)² = R², R = |A|.
            case FunctionType.CircleUpper:
            {
                float r = Mathf.Max(0.02f, Mathf.Abs(a));
                float s = r * r - u * u;
                return s < 0f ? float.NaN : c + Mathf.Sqrt(s);
            }

            // Aerospace teaching curves.
            case FunctionType.AeroLiftVsAlpha:
                if (Mathf.Abs(u) <= LiftStall)
                    return a * u + c;
                return a * LiftStall * Mathf.Sign(u) * Mathf.Exp(-(Mathf.Abs(u) - LiftStall) * LiftDecay) + c;
            case FunctionType.AeroIsothermalDensity:
                return a * Mathf.Exp(-IsothermalInvH(baseN) * Mathf.Max(0f, u)) + c;
            case FunctionType.AeroNewtonianSinSquared:
            {
                float s = Mathf.Sin(Mathf.Clamp(u, 0f, 1.48f));
                return a * s * s + c;
            }
            case FunctionType.ThermoAdiabaticPV:
                return a * Mathf.Pow(Mathf.Max(0.04f, u), -AdiabaticGamma(baseN)) + c;
            case FunctionType.AeroDragPolarTriple: return a * (Mathf.Pow(u, power) + c);

            // Mandelbrot: smooth escape time vs Im(c) with Re(c) = A (|Im| for conjugate symmetry).
            case FunctionType.MandelbrotEscapeImSlice:
            {
                int maxIter = Mathf.Clamp(power, 16, 160);
                float amp = 0.17f * Mathf.Max(1, baseN);
                return c + amp * MandelbrotEscapeMath.SmoothIterations(a, Mathf.Abs(u), maxIter) / maxIter;
            }

            // Stylized economics paths (not market data).
            case FunctionType.EconomyDotcomBubbleStylized: return c + a * PiecewiseSmoothstep(u, DotcomKnotsU, DotcomKnotsY, out _);
            case FunctionType.EconomySubprime2008Stylized: return c + a * PiecewiseSmoothstep(u, SubprimeKnotsU, SubprimeKnotsY, out _);

            case FunctionType.TransformFourierSinc:
                return Mathf.Abs(u) < 1e-5f ? a + c : a * (Mathf.Sin(u) / u) + c;
            case FunctionType.TransformLaplaceCausalDecay:
                return u < -1e-5f ? float.NaN : a * Mathf.Exp(-LaplaceRate(baseN) * u) + c;

            case FunctionType.ChaosLorenzButterflyX:
            {
                float tMax = LorenzAttractorSamples.TimeMax;
                if (tMax < 1e-5f)
                    return float.NaN;
                float t = p.LorenzPhase != 0f ? Mathf.Repeat(u + p.LorenzPhase, tMax) : u;
                return a * LorenzAttractorSamples.SampleSmoothedX(t, LorenzAttractorSamples.LevelSmoothingTime) + c;
            }

            case FunctionType.PhysicsGravityWellInverseSqrt:
            {
                float eps = Mathf.Max(0.08f, Mathf.Abs(p.D));
                return a * (-1f / Mathf.Sqrt(u * u + eps * eps)) + c;
            }
            case FunctionType.BigONLogN:
            {
                float n = Mathf.Max(u, 0.02f);
                return a * (n * Mathf.Log(n)) + c;
            }
        }

        return 0f;
    }

    // ------------------------------------------------------------------ f′(x)

    /// <summary>
    /// d f / d x at plotter <paramref name="x"/>. Analytic where the family allows; NaN where f is undefined
    /// or has a vertical tangent. <paramref name="numericStep"/> (&gt; 0) overrides the fallback step for the
    /// table / iteration curves (pass about half the plot step so f′ reads the drawn slope, not sample noise).
    /// </summary>
    public static float Derivative(in GraphFunctionParams p, float x, float numericStep = 0f)
    {
        float a = p.A, k = p.K;
        int power = p.Power, baseN = p.BaseN;
        float u = k * (x - p.D);
        float dfdu;

        switch (p.Type)
        {
            case FunctionType.Power:
            case FunctionType.AeroDragPolarTriple:
                dfdu = power == 0 ? 0f : a * power * Mathf.Pow(u, power - 1);
                break;
            case FunctionType.Absolute: dfdu = a * Sign0(u); break;
            case FunctionType.Exponential: dfdu = a * Mathf.Log(baseN) * Mathf.Pow(baseN, u); break;
            case FunctionType.NaturalExp: dfdu = a * Mathf.Exp(u); break;
            case FunctionType.Log: dfdu = u > 0f ? a / (u * 2.30258509f) : float.NaN; break;
            case FunctionType.NaturalLog: dfdu = u > 0f ? a / u : float.NaN; break;
            case FunctionType.SquareRoot: dfdu = u > 0f ? a / (2f * Mathf.Sqrt(u)) : float.NaN; break;
            case FunctionType.Sine: dfdu = a * Mathf.Cos(u); break;
            case FunctionType.Cosine: dfdu = -a * Mathf.Sin(u); break;
            case FunctionType.Tangent:
            {
                float cu = Mathf.Cos(u);
                dfdu = a / (cu * cu);
                break;
            }

            // d/du of a Maclaurin partial sum is the neighbouring partial sum.
            case FunctionType.MaclaurinExpSeries:
                dfdu = Mathf.Clamp(power, 0, 18) == 0 ? 0f : a * MaclaurinExpPartialSum(u, Mathf.Clamp(power, 0, 18) - 1);
                break;
            case FunctionType.MaclaurinSinSeries: dfdu = a * MaclaurinCosPartialSum(u, Mathf.Clamp(power, 0, 12)); break;
            case FunctionType.MaclaurinCosSeries:
                dfdu = Mathf.Clamp(power, 0, 12) == 0 ? 0f : -a * MaclaurinSinPartialSum(u, Mathf.Clamp(power, 0, 12) - 1);
                break;
            case FunctionType.GeometricSeriesPartial:
            {
                int n = Mathf.Clamp(power, 0, 24);
                float sum = 0f, uPow = 1f;
                for (int j = 1; j <= n; j++)
                {
                    sum += j * uPow;
                    uPow *= u;
                }
                dfdu = a * sum;
                break;
            }

            case FunctionType.MultivarParaboloidSlice:
            case FunctionType.MultivarSaddleSlice:
                dfdu = 2f * a * u;
                break;

            case FunctionType.SpringMassUndamped:
            {
                float w = SpringMassUndampedOmega(power);
                dfdu = -a * w * Mathf.Sin(w * u);
                break;
            }
            case FunctionType.DampedOscillator:
            {
                float w = DampedOmega(power), d = DampedDecay(baseN);
                float env = Mathf.Exp(-d * Mathf.Abs(u));
                dfdu = a * env * (w * Mathf.Cos(w * u) - d * Sign0(u) * Mathf.Sin(w * u));
                break;
            }
            case FunctionType.HyperbolicCosine:
                dfdu = Mathf.Abs(u) > 8f ? 0f : a * (float)System.Math.Sinh(u);
                break;
            case FunctionType.FullWaveRectifiedSine: dfdu = a * Sign0(Mathf.Sin(u)) * Mathf.Cos(u); break;

            case FunctionType.Arctangent: dfdu = a / (1f + u * u); break;
            case FunctionType.Logistic:
            {
                float s = LogisticSteepness(baseN);
                float zRaw = -s * u;
                if (zRaw < -30f || zRaw > 30f)
                {
                    dfdu = 0f;
                    break;
                }
                float e = Mathf.Exp(zRaw);
                float den = 1f + e;
                dfdu = a * s * e / (den * den);
                break;
            }
            case FunctionType.HyperbolicSine:
                dfdu = Mathf.Abs(u) > 4f ? 0f : a * (float)System.Math.Cosh(u);
                break;
            case FunctionType.ExponentialDecay:
            {
                float s = Mathf.Max(0.02f, k);
                dfdu = -a * s * Sign0(u) * Mathf.Exp(-s * Mathf.Abs(u));
                break;
            }
            case FunctionType.PolarCardioid: dfdu = -a * Mathf.Sin(u); break;
            case FunctionType.PolarRose:
            {
                int n = Mathf.Max(1, power);
                dfdu = -a * n * Mathf.Sin(n * u);
                break;
            }
            case FunctionType.PolarGoldenLogSpiral:
            {
                float g = GoldenSpiralGrowth(baseN);
                float arg = g * u;
                dfdu = arg < -5.2f || arg > 5.2f ? 0f : a * g * 0.48121183f * Mathf.Pow(GoldenRatioPhi, arg); // ln φ
                break;
            }
            case FunctionType.CircleUpper:
            {
                float r = Mathf.Max(0.02f, Mathf.Abs(a));
                float s = r * r - u * u;
                dfdu = s <= 0f ? float.NaN : -u / Mathf.Sqrt(s);
                break;
            }

            case FunctionType.AeroLiftVsAlpha:
                dfdu = Mathf.Abs(u) <= LiftStall
                    ? a
                    : a * LiftStall * Mathf.Sign(u) * Mathf.Exp(-(Mathf.Abs(u) - LiftStall) * LiftDecay) * (-LiftDecay * Mathf.Sign(u));
                break;
            case FunctionType.AeroIsothermalDensity:
            {
                float invH = IsothermalInvH(baseN);
                dfdu = u <= 0f ? 0f : -a * invH * Mathf.Exp(-invH * u);
                break;
            }
            case FunctionType.AeroNewtonianSinSquared:
                dfdu = u <= 0f || u >= 1.48f ? 0f : a * Mathf.Sin(2f * u);
                break;
            case FunctionType.ThermoAdiabaticPV:
            {
                float gamma = AdiabaticGamma(baseN);
                dfdu = u <= 0.04f ? 0f : -a * gamma * Mathf.Pow(u, -gamma - 1f);
                break;
            }

            case FunctionType.EconomyDotcomBubbleStylized:
                PiecewiseSmoothstep(u, DotcomKnotsU, DotcomKnotsY, out float dDot);
                dfdu = a * dDot;
                break;
            case FunctionType.EconomySubprime2008Stylized:
                PiecewiseSmoothstep(u, SubprimeKnotsU, SubprimeKnotsY, out float dSub);
                dfdu = a * dSub;
                break;

            case FunctionType.TransformFourierSinc:
                dfdu = Mathf.Abs(u) < 1e-3f ? a * (-u / 3f) : a * (u * Mathf.Cos(u) - Mathf.Sin(u)) / (u * u);
                break;
            case FunctionType.TransformLaplaceCausalDecay:
            {
                float s = LaplaceRate(baseN);
                dfdu = u < -1e-5f ? float.NaN : -a * s * Mathf.Exp(-s * u);
                break;
            }

            case FunctionType.PhysicsGravityWellInverseSqrt:
            {
                float eps = Mathf.Max(0.08f, Mathf.Abs(p.D));
                float r2 = u * u + eps * eps;
                dfdu = a * u / (r2 * Mathf.Sqrt(r2));
                break;
            }
            case FunctionType.BigONLogN:
                dfdu = u <= 0.02f ? 0f : a * (Mathf.Log(u) + 1f);
                break;
            case FunctionType.ChaosLorenzButterflyX:
            {
                float tMax = LorenzAttractorSamples.TimeMax;
                float t = p.LorenzPhase != 0f ? Mathf.Repeat(u + p.LorenzPhase, tMax) : u;
                dfdu = a * LorenzAttractorSamples.SmoothedSlope(t, LorenzAttractorSamples.LevelSmoothingTime);
                break;
            }

            default:
            {
                // Mandelbrot iteration, typed expressions: central difference on f(x).
                float h = numericStep > 0f ? numericStep : NumericStep(x);
                return CentralDifference(p, x, h);
            }
        }

        float dydx = dfdu * k;
        return IsFinite(dydx) ? dydx : float.NaN;
    }

    // ------------------------------------------------------------------ family helpers

    const float LiftStall = 0.58f;
    const float LiftDecay = 1.12f;

    public static float GoldenRatioPhi => 1.61803399f;

    public static float SpringMassUndampedOmega(int power) => 0.118f * Mathf.Max(1, power);
    static float DampedOmega(int power) => 0.28f * Mathf.Max(1, power);
    static float DampedDecay(int baseN) => 0.042f * Mathf.Max(1, baseN);
    static float LogisticSteepness(int baseN) => 0.05f * Mathf.Max(1, baseN > 0 ? baseN : 1);
    static float IsothermalInvH(int baseN) => 0.055f * Mathf.Max(1, baseN > 0 ? baseN : 1);
    public static float AdiabaticGamma(int baseN) => Mathf.Clamp(baseN / 100f, 1.02f, 2.2f);
    public static float GoldenSpiralGrowth(int baseN) => Mathf.Clamp(Mathf.Max(1, baseN) / 750f, 0.055f, 0.19f);
    public static float LaplaceRate(int baseN) => Mathf.Clamp(baseN / 100f, 0.1f, 4.5f);

    static float Sign0(float v) => v > 0f ? 1f : v < 0f ? -1f : 0f;

    static readonly float[] DotcomKnotsU = { -2.65f, -1.55f, -0.65f, 0.02f, 0.32f, 0.52f, 0.88f, 1.35f, 2.65f };
    static readonly float[] DotcomKnotsY = { 0.11f, 0.24f, 0.48f, 0.93f, 0.62f, 0.37f, 0.35f, 0.46f, 0.71f };
    static readonly float[] SubprimeKnotsU = { -2.65f, -1.15f, -0.35f, 0.02f, 0.22f, 0.42f, 0.72f, 1.25f, 2.65f };
    static readonly float[] SubprimeKnotsY = { 0.17f, 0.38f, 0.78f, 0.9f, 0.36f, 0.29f, 0.33f, 0.49f, 0.67f };

    /// <summary>Piecewise smoothstep through knots (continuous value and slope); <paramref name="dydu"/> is its derivative.</summary>
    static float PiecewiseSmoothstep(float u, float[] xs, float[] ys, out float dydu)
    {
        dydu = 0f;
        if (u <= xs[0])
            return ys[0];
        if (u >= xs[xs.Length - 1])
            return ys[ys.Length - 1];
        for (int i = 0; i < xs.Length - 1; i++)
        {
            if (u > xs[i + 1])
                continue;
            float denom = xs[i + 1] - xs[i];
            float t = denom > 1e-6f ? Mathf.Clamp01((u - xs[i]) / denom) : 0f;
            float s = t * t * (3f - 2f * t);
            dydu = denom > 1e-6f ? (ys[i + 1] - ys[i]) * 6f * t * (1f - t) / denom : 0f;
            return ys[i] + (ys[i + 1] - ys[i]) * s;
        }
        return ys[ys.Length - 1];
    }

    /// <summary>e^u ≈ Σ_{k=0}^{N} u^k/k!</summary>
    public static float MaclaurinExpPartialSum(float u, int maxDegree)
    {
        maxDegree = Mathf.Clamp(maxDegree, 0, 18);
        float sum = 1f, term = 1f;
        for (int j = 1; j <= maxDegree; j++)
        {
            term *= u / j;
            sum += term;
            if (!IsFinite(sum)) break;
        }
        return sum;
    }

    /// <summary>sin u ≈ Σ (-1)^n u^{2n+1}/(2n+1)! up to n = maxN.</summary>
    public static float MaclaurinSinPartialSum(float u, int maxN)
    {
        maxN = Mathf.Clamp(maxN, 0, 12);
        float term = u, sum = u;
        for (int n = 1; n <= maxN; n++)
        {
            term *= -u * u / ((2f * n) * (2f * n + 1f));
            sum += term;
            if (!IsFinite(sum)) break;
        }
        return sum;
    }

    /// <summary>cos u ≈ Σ (-1)^n u^{2n}/(2n)! up to n = maxN.</summary>
    public static float MaclaurinCosPartialSum(float u, int maxN)
    {
        maxN = Mathf.Clamp(maxN, 0, 12);
        float term = 1f, sum = 1f;
        for (int n = 1; n <= maxN; n++)
        {
            term *= -u * u / ((2f * n - 1f) * (2f * n));
            sum += term;
            if (!IsFinite(sum)) break;
        }
        return sum;
    }

    /// <summary>Σ_{k=0}^{N} u^k.</summary>
    public static float GeometricPartialSum(float u, int maxPower)
    {
        maxPower = Mathf.Clamp(maxPower, 0, 24);
        float sum = 1f, uPow = 1f;
        for (int j = 1; j <= maxPower; j++)
        {
            uPow *= u;
            sum += uPow;
            if (!IsFinite(sum)) break;
        }
        return sum;
    }
}
