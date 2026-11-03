using System;
using System.Collections.Generic;
using System.Numerics;

// -----------------------------------------------------------------------------
// Plot3D: what the 3D calculator understands, and how it is sampled into triangles / polylines.
// -----------------------------------------------------------------------------
//   sin(x)*cos(y)                    surface z = f(x, y)       (also "z = ...")
//   y = x^2 - z   /   x = sin(y z)   surface solved for y or x ("implicit-ish", one sheet)
//   x^2 + y^2 + z^2 = 9              implicit F(x, y, z) = 0   (marching tetrahedra)
//   (cos(t), sin(t), t/4)            parametric curve r(t)     (optional ", t = 0..4pi")
//   (u cos(v), u sin(v), v/2)        parametric surface r(u,v) (optional ", u = 0..3, v = 0..2pi")
// Sliders a, b, c may appear anywhere. Coordinates here are math coordinates (z up); the Unity
// layer maps (x, y, z) to world (x, z, y). Pure C#: no UnityEngine, so it is unit-testable.
// -----------------------------------------------------------------------------

public enum Plot3DKind
{
    Surface,
    Curve,
    ParamSurface,
    Implicit,
}

/// <summary>Variable slots shared by every compiled 3D expression.</summary>
public static class Plot3DVars
{
    public static readonly string[] Names = { "x", "y", "z", "t", "u", "v", "a", "b", "c" };
    public const int X = 0, Y = 1, Z = 2, T = 3, U = 4, V = 5, A = 6, B = 7, C = 8;
    public const int ParamCount = 3;
    public static readonly string[] ParamNames = { "a", "b", "c" };
}

/// <summary>Visible region: x, y in [-Half, Half] (plus centre), z in [ZMin, ZMax].</summary>
public struct Box3
{
    public float XMin, XMax, YMin, YMax, ZMin, ZMax;

    public static Box3 Cube(float half) => new Box3
    { XMin = -half, XMax = half, YMin = -half, YMax = half, ZMin = -half, ZMax = half };

    public bool Contains(Vector3 p, float slack = 0f) =>
        p.X >= XMin - slack && p.X <= XMax + slack &&
        p.Y >= YMin - slack && p.Y <= YMax + slack &&
        p.Z >= ZMin - slack && p.Z <= ZMax + slack;

    public float Size => Math.Max(XMax - XMin, Math.Max(YMax - YMin, ZMax - ZMin));
}

public sealed class Plot3DSpec
{
    public Plot3DKind Kind;

    /// <summary>Surface only: which coordinate is the function value (2 = z = f(x,y), 1 = y = f(x,z), 0 = x = f(y,z)).</summary>
    public int DependentAxis = 2;

    /// <summary>Surface / Implicit: 1 function. Curve / ParamSurface: 3 (x, y, z components).</summary>
    public CompiledExpression[] Fns;

    /// <summary>Curve: t range. ParamSurface: u range in [0], v range in [1].</summary>
    public float[] Range0 = { 0f, (float)(2 * Math.PI) };
    public float[] Range1 = { 0f, (float)(2 * Math.PI) };

    public string Source;

    readonly float[] slots = new float[Plot3DVars.Names.Length];

    public bool UsesParam(int paramIndex)
    {
        foreach (var f in Fns)
        {
            if (f != null && f.Uses[Plot3DVars.A + paramIndex])
                return true;
        }

        return false;
    }

    public string KindLabel
    {
        get
        {
            switch (Kind)
            {
                case Plot3DKind.Curve: return "curve r(t)";
                case Plot3DKind.ParamSurface: return "surface r(u, v)";
                case Plot3DKind.Implicit: return "F(x, y, z) = 0";
                default:
                    return DependentAxis == 2 ? "z = f(x, y)" : DependentAxis == 1 ? "y = f(x, z)" : "x = f(y, z)";
            }
        }
    }

    void SetParams(float[] p)
    {
        for (int k = 0; k < Plot3DVars.ParamCount; k++)
            slots[Plot3DVars.A + k] = p != null && k < p.Length ? p[k] : 1f;
    }

    /// <summary>Surface value at the two free coordinates (x, y for z = f(x, y)).</summary>
    public float EvalSurface(float p, float q, float[] prm)
    {
        SetParams(prm);
        slots[0] = slots[1] = slots[2] = 0f;
        // Free axes in order: the two that are not dependent.
        int fa = DependentAxis == 0 ? 1 : 0;
        int fb = DependentAxis == 2 ? 1 : 2;
        slots[fa] = p;
        slots[fb] = q;
        return Fns[0].Evaluate(slots);
    }

    /// <summary>Surface point in math coordinates from its two free coordinates.</summary>
    public Vector3 SurfacePoint(float p, float q, float[] prm)
    {
        float w = EvalSurface(p, q, prm);
        switch (DependentAxis)
        {
            case 0: return new Vector3(w, p, q);
            case 1: return new Vector3(p, w, q);
            default: return new Vector3(p, q, w);
        }
    }

    public float EvalImplicit(Vector3 pt, float[] prm)
    {
        SetParams(prm);
        slots[0] = pt.X;
        slots[1] = pt.Y;
        slots[2] = pt.Z;
        return Fns[0].Evaluate(slots);
    }

    public Vector3 CurvePoint(float t, float[] prm)
    {
        SetParams(prm);
        slots[Plot3DVars.T] = t;
        return new Vector3(Fns[0].Evaluate(slots), Fns[1].Evaluate(slots), Fns[2].Evaluate(slots));
    }

    public Vector3 ParamPoint(float u, float v, float[] prm)
    {
        SetParams(prm);
        slots[Plot3DVars.U] = u;
        slots[Plot3DVars.V] = v;
        return new Vector3(Fns[0].Evaluate(slots), Fns[1].Evaluate(slots), Fns[2].Evaluate(slots));
    }

    /// <summary>Surface z = f(x, y) partials by central differences (inspect readout, physics slopes).</summary>
    public Vector2 SurfaceGradient(float p, float q, float[] prm, float h = 1e-3f)
    {
        float fx = (EvalSurface(p + h, q, prm) - EvalSurface(p - h, q, prm)) / (2f * h);
        float fy = (EvalSurface(p, q + h, prm) - EvalSurface(p, q - h, prm)) / (2f * h);
        return new Vector2(fx, fy);
    }

    public Vector3 CurveVelocity(float t, float[] prm, float h = 1e-3f) =>
        (CurvePoint(t + h, prm) - CurvePoint(t - h, prm)) / (2f * h);
}

public static class Plot3DParser
{
    static readonly char[] PrefixNames = { 'r', 'f', 'g', 'h', 'p', 's', 'F', 'R', 'S', 'G' };

    public static bool TryParse(string text, out Plot3DSpec spec, out string error)
    {
        spec = null;
        error = null;
        string s = MathExpressionCompiler.Normalize(text);
        if (s.Length == 0)
        {
            error = "Type a function, for example sin(x)*cos(y)";
            return false;
        }

        s = StripFunctionPrefix(s);

        // Ranges after a tuple: "(...), t = 0..4pi" or "(...), u=0..3, v=0..2pi".
        var parts = MathExpressionCompiler.SplitTopLevel(s);
        var ranges = new Dictionary<char, float[]>();
        if (parts.Count > 1 && parts[0].StartsWith("("))
        {
            for (int k = 1; k < parts.Count; k++)
            {
                if (!TryParseRange(parts[k], out char name, out float[] r, out error))
                    return false;
                ranges[name] = r;
            }

            s = parts[0];
        }

        // Parametric tuple.
        if (s.StartsWith("(") && WrappedInParens(s))
        {
            var comps = MathExpressionCompiler.SplitTopLevel(s.Substring(1, s.Length - 2));
            if (comps.Count == 3)
                return ParseTuple(comps, ranges, text, out spec, out error);
            if (comps.Count == 2)
            {
                error = "A point in space needs three parts: (x, y, z)";
                return false;
            }
        }

        var eq = MathExpressionCompiler.SplitTopLevel(s, '=');
        if (eq.Count > 2)
        {
            error = "Use one '=' sign";
            return false;
        }

        if (eq.Count == 2)
        {
            string lhs = eq[0].Trim().ToLowerInvariant();
            string rhs = eq[1].Trim();
            if (lhs.Length == 0 || rhs.Length == 0)
            {
                error = "Something is missing on one side of '='";
                return false;
            }

            int axis = lhs == "z" ? 2 : lhs == "y" ? 1 : lhs == "x" ? 0 : -1;
            if (axis >= 0)
            {
                if (!Compile(rhs, out var f, out error))
                    return false;
                if (!f.Uses[axis])
                    return MakeSurface(f, axis, text, out spec, out error);
            }

            // General implicit: lhs - rhs = 0.
            if (!Compile($"({eq[0]}) - ({eq[1]})", out var g, out error))
                return false;
            return MakeImplicit(g, text, out spec, out error);
        }

        if (!Compile(s, out var fn, out error))
            return false;
        if (fn.Uses[Plot3DVars.Z])
            return MakeImplicit(fn, text, out spec, out error); // "x^2 + y^2 + z^2 - 9" means = 0
        return MakeSurface(fn, 2, text, out spec, out error);
    }

    static bool Compile(string expr, out CompiledExpression f, out string error) =>
        MathExpressionCompiler.TryCompile(expr, Plot3DVars.Names, out f, out error);

    static bool MakeSurface(CompiledExpression f, int axis, string src, out Plot3DSpec spec, out string error)
    {
        spec = null;
        error = null;
        if (f.Uses[Plot3DVars.T] || f.Uses[Plot3DVars.U] || f.Uses[Plot3DVars.V])
        {
            error = "t, u and v belong inside a tuple, like (cos(t), sin(t), t)";
            return false;
        }

        spec = new Plot3DSpec { Kind = Plot3DKind.Surface, DependentAxis = axis, Fns = new[] { f }, Source = src };
        return true;
    }

    static bool MakeImplicit(CompiledExpression f, string src, out Plot3DSpec spec, out string error)
    {
        spec = null;
        error = null;
        if (f.Uses[Plot3DVars.T] || f.Uses[Plot3DVars.U] || f.Uses[Plot3DVars.V])
        {
            error = "t, u and v belong inside a tuple, like (cos(t), sin(t), t)";
            return false;
        }

        spec = new Plot3DSpec { Kind = Plot3DKind.Implicit, Fns = new[] { f }, Source = src };
        return true;
    }

    static bool ParseTuple(List<string> comps, Dictionary<char, float[]> ranges, string src, out Plot3DSpec spec, out string error)
    {
        spec = null;
        var fns = new CompiledExpression[3];
        bool usesUV = false, usesT = false, usesXYZ = false;
        for (int k = 0; k < 3; k++)
        {
            if (!Compile(comps[k], out fns[k], out error))
                return false;
            usesT |= fns[k].Uses[Plot3DVars.T];
            usesUV |= fns[k].Uses[Plot3DVars.U] || fns[k].Uses[Plot3DVars.V];
            usesXYZ |= fns[k].Uses[Plot3DVars.X] || fns[k].Uses[Plot3DVars.Y] || fns[k].Uses[Plot3DVars.Z];
        }

        error = null;
        if (usesXYZ)
        {
            error = "Inside (x, y, z) use t for a curve, or u and v for a surface";
            return false;
        }

        if (usesUV && usesT)
        {
            error = "Use t for a curve, or u and v for a surface, not both";
            return false;
        }

        spec = new Plot3DSpec { Kind = usesUV ? Plot3DKind.ParamSurface : Plot3DKind.Curve, Fns = fns, Source = src };
        if (usesUV)
        {
            if (ranges.TryGetValue('u', out var ru)) spec.Range0 = ru;
            if (ranges.TryGetValue('v', out var rv)) spec.Range1 = rv;
        }
        else
        {
            spec.Range0 = new[] { (float)(-2 * Math.PI), (float)(2 * Math.PI) };
            if (ranges.TryGetValue('t', out var rt)) spec.Range0 = rt;
        }

        return true;
    }

    /// <summary>"t = 0..4pi", "t=0 to 2pi", "0 < t < 2pi".</summary>
    public static bool TryParseRange(string s, out char name, out float[] range, out string error)
    {
        name = '\0';
        range = null;
        error = null;
        s = s.Trim();
        string lo, hi;
        string afterName = s.Length > 1 ? s.Substring(1).TrimStart() : "";
        if (char.IsLetter(s[0]) && afterName.StartsWith("="))
        {
            name = char.ToLowerInvariant(s[0]);
            string body = afterName.Substring(1);
            int sep = body.IndexOf("..", StringComparison.Ordinal);
            int sepLen = 2;
            if (sep < 0)
            {
                sep = body.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
                sepLen = 4;
            }

            if (sep < 0)
            {
                error = $"Write the range like {name} = 0..2pi";
                return false;
            }

            lo = body.Substring(0, sep);
            hi = body.Substring(sep + sepLen);
        }
        else
        {
            var lt = s.Split('<');
            if (lt.Length != 3 || lt[1].Trim().Length != 1)
            {
                error = "Write the range like t = 0..2pi";
                return false;
            }

            name = char.ToLowerInvariant(lt[1].Trim()[0]);
            lo = lt[0];
            hi = lt[2];
        }

        if (name != 't' && name != 'u' && name != 'v')
        {
            error = "Only t, u and v take a range";
            return false;
        }

        if (!TryConstant(lo, out float a, out error) || !TryConstant(hi, out float b, out error))
            return false;
        if (!(b > a))
        {
            error = "The range must go from low to high";
            return false;
        }

        range = new[] { a, b };
        return true;
    }

    public static bool TryConstant(string expr, out float value, out string error)
    {
        value = float.NaN;
        if (!MathExpressionCompiler.TryCompile(MathExpressionCompiler.Normalize(expr), new string[0], out var f, out error))
            return false;
        value = f.Evaluate(new float[0]);
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            error = "Range ends must be numbers";
            return false;
        }

        return true;
    }

    static bool WrappedInParens(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')')
            {
                depth--;
                if (depth == 0 && i < s.Length - 1)
                    return false;
            }
        }

        return depth == 0 && s.EndsWith(")");
    }

    /// <summary>Drops "r(t) =", "f(x, y) =" and similar names in front of the formula.</summary>
    static string StripFunctionPrefix(string s)
    {
        if (s.Length < 5 || Array.IndexOf(PrefixNames, s[0]) < 0)
            return s;
        int i = 1;
        while (i < s.Length && s[i] == ' ') i++;
        if (i >= s.Length || s[i] != '(')
            return s;
        int close = s.IndexOf(')', i);
        if (close < 0)
            return s;
        string inside = s.Substring(i + 1, close - i - 1);
        foreach (char ch in inside)
        {
            if (!(ch == ',' || ch == ' ' || "xyztuv".IndexOf(ch) >= 0))
                return s;
        }

        int j = close + 1;
        while (j < s.Length && s[j] == ' ') j++;
        if (j >= s.Length || s[j] != '=')
            return s;
        return s.Substring(j + 1).Trim();
    }
}

/// <summary>Triangles / polylines in math coordinates.</summary>
public sealed class Plot3DMesh
{
    public readonly List<Vector3> Vertices = new List<Vector3>();
    public readonly List<Vector3> Normals = new List<Vector3>();
    public readonly List<int> Triangles = new List<int>();

    /// <summary>Curves: each list is one unbroken polyline.</summary>
    public readonly List<List<Vector3>> Polylines = new List<List<Vector3>>();

    /// <summary>Curves: the t value of every polyline point (same shape as <see cref="Polylines"/>).</summary>
    public readonly List<List<float>> PolylineParams = new List<List<float>>();

    /// <summary>Grid meshes (surfaces, param surfaces): cells per side, so vertex k is (k % (GridN+1), k / (GridN+1)).</summary>
    public int GridN;

    public int TriangleCount => Triangles.Count / 3;
}

public static class Plot3DSampler
{
    /// <summary>Grid resolution for surfaces (cells per side) by device class; implicit uses its own cube grid.</summary>
    public static int SurfaceResolution(bool mobile, int plotCount) =>
        Math.Max(24, (mobile ? 64 : 112) - (plotCount - 1) * (mobile ? 10 : 16));

    public static int ImplicitResolution(bool mobile) => mobile ? 26 : 40;

    public static Plot3DMesh Sample(Plot3DSpec spec, Box3 box, float[] prm, int res)
    {
        switch (spec.Kind)
        {
            case Plot3DKind.Curve: return SampleCurve(spec, box, prm, res * 8);
            case Plot3DKind.ParamSurface: return SampleParamSurface(spec, box, prm, res);
            case Plot3DKind.Implicit: return MarchingTetrahedra.Polygonise(p => spec.EvalImplicit(p, prm), box, res);
            default: return SampleSurface(spec, box, prm, res);
        }
    }

    static bool Finite(Vector3 p) =>
        !(float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) ||
          float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z));

    /// <summary>
    /// Explicit surface on an (n+1)^2 grid over the two free axes. A cell's triangles are dropped when
    /// a corner is undefined, leaves the box, or the cell straddles a jump (tan, 1/x) so poles are not bridged.
    /// </summary>
    public static Plot3DMesh SampleSurface(Plot3DSpec spec, Box3 box, float[] prm, int n)
    {
        var m = new Plot3DMesh { GridN = n };
        int fa = spec.DependentAxis == 0 ? 1 : 0;
        int fb = spec.DependentAxis == 2 ? 1 : 2;
        Range(box, fa, out float a0, out float a1);
        Range(box, fb, out float b0, out float b1);
        Range(box, spec.DependentAxis, out float w0, out float w1);
        float jump = (w1 - w0) * 0.45f;
        var ok = new bool[(n + 1) * (n + 1)];
        for (int j = 0; j <= n; j++)
        {
            float q = b0 + (b1 - b0) * j / n;
            for (int i = 0; i <= n; i++)
            {
                float p = a0 + (a1 - a0) * i / n;
                var pt = spec.SurfacePoint(p, q, prm);
                bool good = Finite(pt);
                ok[j * (n + 1) + i] = good;
                m.Vertices.Add(good ? pt : new Vector3(p, q, 0f));
            }
        }

        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int i00 = j * (n + 1) + i, i10 = i00 + 1, i01 = i00 + n + 1, i11 = i01 + 1;
                AddTri(m, ok, i00, i01, i11, spec.DependentAxis, jump, w0, w1);
                AddTri(m, ok, i00, i11, i10, spec.DependentAxis, jump, w0, w1);
            }
        }

        ComputeGridNormals(m);
        return m;
    }

    static void AddTri(Plot3DMesh m, bool[] ok, int a, int b, int c, int axis, float jump, float lo, float hi)
    {
        if (!ok[a] || !ok[b] || !ok[c])
            return;
        float wa = Comp(m.Vertices[a], axis), wb = Comp(m.Vertices[b], axis), wc = Comp(m.Vertices[c], axis);
        if (Math.Abs(wa - wb) > jump || Math.Abs(wb - wc) > jump || Math.Abs(wa - wc) > jump)
            return;
        float mn = Math.Min(wa, Math.Min(wb, wc)), mx = Math.Max(wa, Math.Max(wb, wc));
        if (mx < lo || mn > hi)
            return;
        if (mn >= lo && mx <= hi)
        {
            m.Triangles.Add(a);
            m.Triangles.Add(b);
            m.Triangles.Add(c);
            return;
        }

        // Straddles the top or bottom of the box: clip so the edge is a clean level line, not a sawtooth.
        var poly = new List<Vector3> { m.Vertices[a], m.Vertices[b], m.Vertices[c] };
        poly = ClipAxis(poly, axis, lo, true);
        poly = ClipAxis(poly, axis, hi, false);
        if (poly.Count < 3)
            return;
        int baseIdx = m.Vertices.Count;
        m.Vertices.AddRange(poly);
        for (int k = 1; k < poly.Count - 1; k++)
        {
            m.Triangles.Add(baseIdx);
            m.Triangles.Add(baseIdx + k);
            m.Triangles.Add(baseIdx + k + 1);
        }
    }

    /// <summary>Sutherland-Hodgman against one axis-aligned plane (keep the side &gt;= v when <paramref name="keepAbove"/>).</summary>
    static List<Vector3> ClipAxis(List<Vector3> poly, int axis, float v, bool keepAbove)
    {
        var res = new List<Vector3>();
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            float dp = keepAbove ? Comp(p, axis) - v : v - Comp(p, axis);
            float dq = keepAbove ? Comp(q, axis) - v : v - Comp(q, axis);
            if (dp >= 0f)
                res.Add(p);
            if ((dp >= 0f) != (dq >= 0f))
                res.Add(p + (q - p) * (dp / (dp - dq)));
        }

        return res;
    }

    public static Plot3DMesh SampleParamSurface(Plot3DSpec spec, Box3 box, float[] prm, int n)
    {
        var m = new Plot3DMesh { GridN = n };
        float u0 = spec.Range0[0], u1 = spec.Range0[1], v0 = spec.Range1[0], v1 = spec.Range1[1];
        float jump = box.Size * 0.35f;
        var ok = new bool[(n + 1) * (n + 1)];
        for (int j = 0; j <= n; j++)
        {
            float v = v0 + (v1 - v0) * j / n;
            for (int i = 0; i <= n; i++)
            {
                float u = u0 + (u1 - u0) * i / n;
                var pt = spec.ParamPoint(u, v, prm);
                bool good = Finite(pt) && box.Contains(pt, box.Size * 1e-3f);
                ok[j * (n + 1) + i] = good;
                m.Vertices.Add(good ? pt : Vector3.Zero);
            }
        }

        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int i00 = j * (n + 1) + i, i10 = i00 + 1, i01 = i00 + n + 1, i11 = i01 + 1;
                AddTriSpatial(m, ok, i00, i01, i11, jump);
                AddTriSpatial(m, ok, i00, i11, i10, jump);
            }
        }

        ComputeGridNormals(m);
        return m;
    }

    static void AddTriSpatial(Plot3DMesh m, bool[] ok, int a, int b, int c, float jump)
    {
        if (!ok[a] || !ok[b] || !ok[c])
            return;
        var pa = m.Vertices[a];
        var pb = m.Vertices[b];
        var pc = m.Vertices[c];
        if ((pa - pb).Length() > jump || (pb - pc).Length() > jump || (pa - pc).Length() > jump)
            return;
        m.Triangles.Add(a);
        m.Triangles.Add(b);
        m.Triangles.Add(c);
    }

    /// <summary>Curve samples split into unbroken polylines at undefined points, box exits and jumps.</summary>
    public static Plot3DMesh SampleCurve(Plot3DSpec spec, Box3 box, float[] prm, int samples)
    {
        var m = new Plot3DMesh();
        float t0 = spec.Range0[0], t1 = spec.Range0[1];
        float jump = box.Size * 0.25f;
        List<Vector3> cur = null;
        List<float> curT = null;
        Vector3 prev = default;
        for (int k = 0; k <= samples; k++)
        {
            float t = t0 + (t1 - t0) * k / samples;
            var p = spec.CurvePoint(t, prm);
            bool good = Finite(p) && box.Contains(p, box.Size * 1e-3f);
            if (good && cur != null && (p - prev).Length() > jump)
                good = false;
            if (!good)
            {
                if (cur != null && cur.Count > 1)
                {
                    m.Polylines.Add(cur);
                    m.PolylineParams.Add(curT);
                }

                cur = null;
                curT = null;
                if (Finite(p) && box.Contains(p))
                {
                    cur = new List<Vector3> { p };
                    curT = new List<float> { t };
                    prev = p;
                }

                continue;
            }

            cur ??= new List<Vector3>();
            curT ??= new List<float>();
            cur.Add(p);
            curT.Add(t);
            prev = p;
        }

        if (cur != null && cur.Count > 1)
        {
            m.Polylines.Add(cur);
            m.PolylineParams.Add(curT);
        }

        return m;
    }

    /// <summary>Area-weighted vertex normals over the used triangles (grid meshes share vertices).</summary>
    static void ComputeGridNormals(Plot3DMesh m)
    {
        var acc = new Vector3[m.Vertices.Count];
        for (int k = 0; k < m.Triangles.Count; k += 3)
        {
            int a = m.Triangles[k], b = m.Triangles[k + 1], c = m.Triangles[k + 2];
            var nrm = Vector3.Cross(m.Vertices[b] - m.Vertices[a], m.Vertices[c] - m.Vertices[a]);
            acc[a] += nrm;
            acc[b] += nrm;
            acc[c] += nrm;
        }

        for (int k = 0; k < acc.Length; k++)
        {
            float len = acc[k].Length();
            m.Normals.Add(len > 1e-12f ? acc[k] / len : Vector3.UnitZ);
        }
    }

    static float Comp(Vector3 p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;

    static void Range(Box3 b, int axis, out float lo, out float hi)
    {
        switch (axis)
        {
            case 0: lo = b.XMin; hi = b.XMax; break;
            case 1: lo = b.YMin; hi = b.YMax; break;
            default: lo = b.ZMin; hi = b.ZMax; break;
        }
    }
}

/// <summary>
/// Implicit surfaces F(x, y, z) = 0: each grid cube is split into six tetrahedra around its main
/// diagonal, and each tetrahedron emits 0, 1 or 2 triangles where F changes sign. No lookup tables,
/// no ambiguous cases. Normals come from the gradient of F, so the surface shades smoothly.
/// </summary>
public static class MarchingTetrahedra
{
    static readonly int[][] Tets =
    {
        new[] { 0, 5, 1, 6 }, new[] { 0, 1, 2, 6 }, new[] { 0, 2, 3, 6 },
        new[] { 0, 3, 7, 6 }, new[] { 0, 7, 4, 6 }, new[] { 0, 4, 5, 6 },
    };

    static readonly int[,] Corner =
    {
        { 0, 0, 0 }, { 1, 0, 0 }, { 1, 1, 0 }, { 0, 1, 0 },
        { 0, 0, 1 }, { 1, 0, 1 }, { 1, 1, 1 }, { 0, 1, 1 },
    };

    public static Plot3DMesh Polygonise(Func<Vector3, float> f, Box3 box, int n)
    {
        var m = new Plot3DMesh();
        int n1 = n + 1;
        var val = new float[n1 * n1 * n1];
        var pos = new Vector3[n1 * n1 * n1];
        for (int k = 0; k <= n; k++)
        for (int j = 0; j <= n; j++)
        for (int i = 0; i <= n; i++)
        {
            var p = new Vector3(
                box.XMin + (box.XMax - box.XMin) * i / n,
                box.YMin + (box.YMax - box.YMin) * j / n,
                box.ZMin + (box.ZMax - box.ZMin) * k / n);
            int idx = (k * n1 + j) * n1 + i;
            pos[idx] = p;
            val[idx] = f(p);
        }

        var cv = new float[8];
        var cp = new Vector3[8];
        for (int k = 0; k < n; k++)
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            bool any = false, pos0 = false, neg0 = false;
            for (int c = 0; c < 8; c++)
            {
                int idx = ((k + Corner[c, 2]) * n1 + j + Corner[c, 1]) * n1 + i + Corner[c, 0];
                cv[c] = val[idx];
                cp[c] = pos[idx];
                if (float.IsNaN(cv[c]) || float.IsInfinity(cv[c]))
                    any = true;
                else if (cv[c] > 0f) pos0 = true;
                else neg0 = true;
            }

            if (any || !(pos0 && neg0))
                continue;
            foreach (var t in Tets)
                EmitTet(m, cp[t[0]], cp[t[1]], cp[t[2]], cp[t[3]], cv[t[0]], cv[t[1]], cv[t[2]], cv[t[3]]);
        }

        float h = box.Size / n * 0.25f;
        for (int q = 0; q < m.Vertices.Count; q++)
        {
            var p = m.Vertices[q];
            var g = new Vector3(
                f(p + new Vector3(h, 0, 0)) - f(p - new Vector3(h, 0, 0)),
                f(p + new Vector3(0, h, 0)) - f(p - new Vector3(0, h, 0)),
                f(p + new Vector3(0, 0, h)) - f(p - new Vector3(0, 0, h)));
            float len = g.Length();
            m.Normals.Add(len > 1e-12f && !float.IsNaN(len) ? g / len : Vector3.UnitZ);
        }

        return m;
    }

    static void EmitTet(Plot3DMesh m, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float v0, float v1, float v2, float v3)
    {
        var p = new[] { p0, p1, p2, p3 };
        var v = new[] { v0, v1, v2, v3 };
        int mask = 0;
        for (int c = 0; c < 4; c++)
        {
            if (v[c] > 0f)
                mask |= 1 << c;
        }

        if (mask == 0 || mask == 15)
            return;
        // Normalise so at most two corners are "inside" (positive); flip winding when we invert.
        bool flip = false;
        int count = 0;
        for (int c = 0; c < 4; c++)
            count += (mask >> c) & 1;
        if (count > 2 || (count == 2 && (mask & 1) == 0))
        {
            mask ^= 15;
            flip = true;
        }

        var ins = new List<int>();
        var outs = new List<int>();
        for (int c = 0; c < 4; c++)
        {
            if (((mask >> c) & 1) != 0) ins.Add(c);
            else outs.Add(c);
        }

        if (ins.Count == 1)
        {
            int a = ins[0];
            Tri(m, Lerp(p, v, a, outs[0]), Lerp(p, v, a, outs[1]), Lerp(p, v, a, outs[2]), p[a], flip);
        }
        else
        {
            int a = ins[0], b = ins[1], c = outs[0], d = outs[1];
            var ac = Lerp(p, v, a, c);
            var ad = Lerp(p, v, a, d);
            var bc = Lerp(p, v, b, c);
            var bd = Lerp(p, v, b, d);
            var inside = (p[a] + p[b]) * 0.5f;
            Tri(m, ac, ad, bd, inside, flip);
            Tri(m, ac, bd, bc, inside, flip);
        }
    }

    static Vector3 Lerp(Vector3[] p, float[] v, int a, int b)
    {
        float d = v[a] - v[b];
        float t = Math.Abs(d) < 1e-20f ? 0.5f : v[a] / d;
        return p[a] + (p[b] - p[a]) * t;
    }

    /// <summary>Adds a triangle facing away from the positive side (consistent winding).</summary>
    static void Tri(Plot3DMesh m, Vector3 a, Vector3 b, Vector3 c, Vector3 insidePoint, bool flip)
    {
        var nrm = Vector3.Cross(b - a, c - a);
        bool towardsInside = Vector3.Dot(nrm, insidePoint - a) > 0f;
        if (towardsInside ^ flip)
        {
            var tmp = b;
            b = c;
            c = tmp;
        }

        int i0 = m.Vertices.Count;
        m.Vertices.Add(a);
        m.Vertices.Add(b);
        m.Vertices.Add(c);
        m.Triangles.Add(i0);
        m.Triangles.Add(i0 + 1);
        m.Triangles.Add(i0 + 2);
    }
}
