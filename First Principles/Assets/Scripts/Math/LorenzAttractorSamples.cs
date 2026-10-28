using UnityEngine;

/// <summary>
/// Pre-integrated Lorenz attractor (σ=10, ρ=28, β=8/3) for the Chaos Theory boss stage.
/// Horizontal axis in-game maps to time; height samples the <b>x</b> coordinate (chaotic, sensitive ICs).
/// Uses RK4 — explicit Euler with the previous dt blew up (~10²⁸ by burn-in step 50), yielding NaNs and an empty graph.
/// </summary>
public static class LorenzAttractorSamples
{
    private static float[] _xSeries;
    private static float _tMax = 1f;
    private static bool _built;

    private const float Sigma = 10f;
    private const float Rho = 28f;
    private const float Beta = 8f / 3f;

    /// <summary>Normalized x in roughly [-1,1] after burn-in, for time in [0, <see cref="TimeMax"/>].</summary>
    public static float SampleNormalizedX(float time)
    {
        EnsureBuilt();
        if (_xSeries == null || _xSeries.Length < 4)
            return 0f;
        time = Mathf.Clamp(time, 0f, _tMax);
        float u = time / _tMax * (_xSeries.Length - 1);
        int i = Mathf.FloorToInt(u);
        float f = u - i;
        i = Mathf.Clamp(i, 0, _xSeries.Length - 2);
        return Mathf.Lerp(_xSeries[i], _xSeries[i + 1], f);
    }

    /// <summary>
    /// Level curve window (time units, about one loop of a wing). The raw x(t) oscillates ~130 times across the boss
    /// level, far faster than the plot samples it (the drawn line was an alias and the platform rules were noise).
    /// A triangle average (this box applied twice) keeps the chaotic wing switching, which is what the level reads
    /// and plays, and has a smooth, exact derivative.
    /// </summary>
    public const float LevelSmoothingTime = 0.8f;

    private static double[] _prefix;  // F(t_i) = ∫₀^{t_i} x
    private static double[] _prefix2; // G(t_i) = ∫₀^{t_i} F

    static void Locate(float t, out int i, out double f, out double dt)
    {
        int n = _xSeries.Length;
        dt = _tMax / (n - 1);
        double u = t / dt;
        i = System.Math.Min((int)u, n - 2);
        f = u - i;
    }

    /// <summary>F(t) = ∫₀ᵗ x of the piecewise-linear series, x held constant outside [0, TimeMax].</summary>
    static double Integral(float t)
    {
        EnsureBuilt();
        int n = _xSeries.Length;
        if (t <= 0f)
            return t * _xSeries[0];
        if (t >= _tMax)
            return _prefix[n - 1] + (t - _tMax) * _xSeries[n - 1];
        Locate(t, out int i, out double f, out double dt);
        double d = _xSeries[i + 1] - _xSeries[i];
        return _prefix[i] + dt * (_xSeries[i] * f + 0.5 * d * f * f);
    }

    /// <summary>G(t) = ∫₀ᵗ F.</summary>
    static double Integral2(float t)
    {
        EnsureBuilt();
        int n = _xSeries.Length;
        if (t <= 0f)
            return 0.5 * t * t * _xSeries[0];
        if (t >= _tMax)
        {
            double r = t - _tMax;
            return _prefix2[n - 1] + _prefix[n - 1] * r + 0.5 * _xSeries[n - 1] * r * r;
        }
        Locate(t, out int i, out double f, out double dt);
        double d = _xSeries[i + 1] - _xSeries[i];
        return _prefix2[i] + dt * (_prefix[i] * f + dt * (_xSeries[i] * f * f / 2.0 + d * f * f * f / 6.0));
    }

    /// <summary>Triangle-weighted average of the normalized x over [t − w, t + w].</summary>
    public static float SampleSmoothedX(float time, float window)
    {
        double w = Mathf.Max(window, 1e-3f);
        return (float)((Integral2(time + (float)w) - 2.0 * Integral2(time) + Integral2(time - (float)w)) / (w * w));
    }

    /// <summary>Exact d/dt of <see cref="SampleSmoothedX"/>.</summary>
    public static float SmoothedSlope(float time, float window)
    {
        double w = Mathf.Max(window, 1e-3f);
        return (float)((Integral(time + (float)w) - 2.0 * Integral(time) + Integral(time - (float)w)) / (w * w));
    }

    public static float TimeMax
    {
        get
        {
            EnsureBuilt();
            return _tMax;
        }
    }

    private static void Deriv(float x, float y, float z, out float dx, out float dy, out float dz)
    {
        dx = Sigma * (y - x);
        dy = x * (Rho - z) - y;
        dz = x * y - Beta * z;
    }

    private static void StepRk4(ref float x, ref float y, ref float z, float dt)
    {
        Deriv(x, y, z, out float k1x, out float k1y, out float k1z);

        float x2 = x + 0.5f * dt * k1x;
        float y2 = y + 0.5f * dt * k1y;
        float z2 = z + 0.5f * dt * k1z;
        Deriv(x2, y2, z2, out float k2x, out float k2y, out float k2z);

        float x3 = x + 0.5f * dt * k2x;
        float y3 = y + 0.5f * dt * k2y;
        float z3 = z + 0.5f * dt * k2z;
        Deriv(x3, y3, z3, out float k3x, out float k3y, out float k3z);

        float x4 = x + dt * k3x;
        float y4 = y + dt * k3y;
        float z4 = z + dt * k3z;
        Deriv(x4, y4, z4, out float k4x, out float k4y, out float k4z);

        x += dt * (k1x + 2f * k2x + 2f * k3x + k4x) / 6f;
        y += dt * (k1y + 2f * k2y + 2f * k3y + k4y) / 6f;
        z += dt * (k1z + 2f * k2z + 2f * k3z + k4z) / 6f;
    }

    private static void EnsureBuilt()
    {
        if (_built)
            return;

        const int burnInSteps = 2000;
        const int n = 6000;
        const float dt = 0.015f;

        float x = 0.2f, y = 0.15f, z = 0.1f;

        for (int s = 0; s < burnInSteps; s++)
            StepRk4(ref x, ref y, ref z, dt);

        _xSeries = new float[n];
        float xMin = x, xMax = x;
        float bx = x, by = y, bz = z;

        for (int i = 0; i < n; i++)
        {
            StepRk4(ref bx, ref by, ref bz, dt);
            _xSeries[i] = bx;
            if (bx < xMin) xMin = bx;
            if (bx > xMax) xMax = bx;
        }

        float span = Mathf.Max(xMax - xMin, 1e-3f);
        float mid = 0.5f * (xMax + xMin);
        for (int i = 0; i < n; i++)
            _xSeries[i] = (_xSeries[i] - mid) / span * 2f;

        _tMax = (n - 1) * dt;
        _prefix = new double[n];
        _prefix2 = new double[n];
        for (int i = 1; i < n; i++)
        {
            double d = _xSeries[i] - _xSeries[i - 1];
            _prefix[i] = _prefix[i - 1] + 0.5 * dt * (_xSeries[i - 1] + _xSeries[i]);
            _prefix2[i] = _prefix2[i - 1] + dt * (_prefix[i - 1] + dt * (_xSeries[i - 1] / 2.0 + d / 6.0));
        }
        _built = true;
    }
}
