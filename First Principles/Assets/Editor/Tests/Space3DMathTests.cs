using System;
using System.Linq;
using NUnit.Framework;
using NV3 = System.Numerics.Vector3;

/// <summary>3D graphing math (1.5): expression compiler, plot parsing, surface / curve / implicit sampling.</summary>
public class Space3DMathTests
{
    static float Eval(string expr, params (string name, float v)[] vars)
    {
        var names = Plot3DVars.Names;
        Assert.IsTrue(MathExpressionCompiler.TryCompile(expr, names, out var f, out string err), err);
        var slots = new float[names.Length];
        foreach (var (n, v) in vars)
            slots[Array.IndexOf(names, n)] = v;
        return f.Evaluate(slots);
    }

    [Test]
    public void Compiler_MatchesTheTwoDimensionalEvaluator()
    {
        string[] exprs =
        {
            "x^2 + 3x - 1", "sin(x)*cos(x)", "2(x+1)^3", "ln(x)", "sqrt(x)", "abs(x)-x", "exp(-x^2)", "e^x",
            "min(x, 2)", "max(x^2, 1)", "tan(x)/2", "atan(x)*pi", "log(x+5)", "1/x", "sinh(x)+cosh(x)",
        };
        foreach (var e in exprs)
        foreach (float x in new[] { -2.5f, -1f, 0.5f, 1f, 3f })
        {
            bool ok2d = MathExpressionEvaluator.TryEvaluate(e, x, out float want, out _);
            float got = Eval(e, ("x", x));
            if (!ok2d || float.IsNaN(want))
            {
                Assert.IsTrue(float.IsNaN(got) || float.IsInfinity(got) || !ok2d, $"{e} at {x}");
                continue;
            }

            Assert.AreEqual(want, got, 1e-4f * Math.Max(1f, Math.Abs(want)), $"{e} at {x}");
        }
    }

    [Test]
    public void Compiler_ImplicitMultiplyBetweenVariables()
    {
        Assert.AreEqual(2f * 1.5f * -0.5f, Eval("2xy", ("x", 1.5f), ("y", -0.5f)), 1e-5f);
        Assert.AreEqual(3f * (float)Math.Cos(2f), Eval("u cos(v)", ("u", 3f), ("v", 2f)), 1e-5f);
        Assert.AreEqual(6f, Eval("abc", ("a", 1f), ("b", 2f), ("c", 3f)), 1e-5f);
        Assert.AreEqual((float)Math.E * 2f, Eval("2e", ("x", 0f)), 1e-5f);
        Assert.AreEqual(4f, Eval("x²", ("x", 2f)), 1e-5f);
        Assert.AreEqual(-1f, Eval("x − 2", ("x", 1f)), 1e-5f);
    }

    [Test]
    public void Parser_RecognisesEveryKind()
    {
        void Kind(string s, Plot3DKind k, int axis = 2)
        {
            Assert.IsTrue(Plot3DParser.TryParse(s, out var spec, out string err), s + ": " + err);
            Assert.AreEqual(k, spec.Kind, s);
            if (k == Plot3DKind.Surface)
                Assert.AreEqual(axis, spec.DependentAxis, s);
        }

        Kind("sin(x)*cos(y)", Plot3DKind.Surface);
        Kind("z = x^2 - y^2", Plot3DKind.Surface);
        Kind("f(x, y) = a*x + b*y", Plot3DKind.Surface);
        Kind("y = x^2 - z", Plot3DKind.Surface, 1);
        Kind("x = sin(y z)", Plot3DKind.Surface, 0);
        Kind("x^2 + y^2 + z^2 = 9", Plot3DKind.Implicit);
        Kind("x^2 + y^2 - z", Plot3DKind.Implicit);
        Kind("(cos(t), sin(t), t/4)", Plot3DKind.Curve);
        Kind("r(t) = (t, t^2, t^3), t = -1..1", Plot3DKind.Curve);
        Kind("(u*cos(v), u*sin(v), v/2), u = 0..3, v = 0..2pi", Plot3DKind.ParamSurface);

        Assert.IsTrue(Plot3DParser.TryParse("(t, 2t, 3t), t = -2..5", out var c, out _));
        Assert.AreEqual(-2f, c.Range0[0], 1e-6f);
        Assert.AreEqual(5f, c.Range0[1], 1e-6f);
        Assert.IsTrue(Plot3DParser.TryParse("(u, v, 0), u = 0..pi, v = -1..1", out var ps, out _));
        Assert.AreEqual((float)Math.PI, ps.Range0[1], 1e-5f);
        Assert.AreEqual(-1f, ps.Range1[0], 1e-6f);
    }

    [Test]
    public void Parser_RejectsWithAReason()
    {
        foreach (var s in new[] { "", "(t, t)", "sin(", "(x, t, 1)", "t*x", "(t, u, v)", "a = b = c", "(t,t,t), t = 3..1", "foo(x)" })
        {
            Assert.IsFalse(Plot3DParser.TryParse(s, out _, out string err), s);
            Assert.IsFalse(string.IsNullOrEmpty(err), s);
        }
    }

    [Test]
    public void SurfaceSampling_PointsLieOnTheSurface()
    {
        Assert.IsTrue(Plot3DParser.TryParse("sin(x)*cos(y) + a", out var spec, out _));
        var prm = new[] { 0.5f, 1f, 1f };
        var m = Plot3DSampler.SampleSurface(spec, Box3.Cube(4f), prm, 40);
        Assert.AreEqual(41 * 41, m.Vertices.Count);
        Assert.AreEqual(40 * 40 * 2, m.TriangleCount, "smooth surface inside the box keeps every cell");
        foreach (int i in m.Triangles.Distinct())
        {
            var p = m.Vertices[i];
            Assert.AreEqual((float)(Math.Sin(p.X) * Math.Cos(p.Y)) + 0.5f, p.Z, 1e-4f);
        }

        // Normals point up-ish and are unit length.
        foreach (var n in m.Normals)
            Assert.AreEqual(1f, n.Length(), 1e-3f);
    }

    [Test]
    public void SurfaceSampling_DropsUndefinedCellsAndPoles()
    {
        Assert.IsTrue(Plot3DParser.TryParse("sqrt(x)", out var root, out _));
        var m = Plot3DSampler.SampleSurface(root, Box3.Cube(4f), null, 40);
        Assert.Greater(m.TriangleCount, 0);
        foreach (int i in m.Triangles)
            Assert.GreaterOrEqual(m.Vertices[i].X, 0f, "no triangles where sqrt(x) is undefined");

        Assert.IsTrue(Plot3DParser.TryParse("1/x", out var pole, out _));
        var mp = Plot3DSampler.SampleSurface(pole, Box3.Cube(4f), null, 41);
        for (int k = 0; k < mp.Triangles.Count; k += 3)
        {
            float x0 = mp.Vertices[mp.Triangles[k]].X, x1 = mp.Vertices[mp.Triangles[k + 1]].X, x2 = mp.Vertices[mp.Triangles[k + 2]].X;
            Assert.IsFalse(Math.Min(x0, Math.Min(x1, x2)) < 0f && Math.Max(x0, Math.Max(x1, x2)) > 0f, "no triangle bridges the pole at x = 0");
        }
    }

    [Test]
    public void CurveSampling_FollowsTheHelixAndSplitsAtGaps()
    {
        Assert.IsTrue(Plot3DParser.TryParse("(2cos(t), 2sin(t), t/4), t = 0..4pi", out var helix, out _));
        var m = Plot3DSampler.SampleCurve(helix, Box3.Cube(5f), null, 400);
        Assert.AreEqual(1, m.Polylines.Count);
        for (int k = 0; k < m.Polylines[0].Count; k++)
        {
            var p = m.Polylines[0][k];
            float t = m.PolylineParams[0][k];
            Assert.AreEqual(2f, Math.Sqrt(p.X * p.X + p.Y * p.Y), 1e-4f);
            Assert.AreEqual(t / 4f, p.Z, 1e-4f);
        }

        // |r'(t)| = sqrt(4 + 1/16).
        Assert.AreEqual(Math.Sqrt(4.0 + 1.0 / 16.0), helix.CurveVelocity(1f, null).Length(), 1e-2f);

        Assert.IsTrue(Plot3DParser.TryParse("(t, 0, sqrt(1 - t^2)), t = -2..2", out var arc, out _));
        var ma = Plot3DSampler.SampleCurve(arc, Box3.Cube(5f), null, 400);
        Assert.AreEqual(1, ma.Polylines.Count, "only the defined part, |t| <= 1");
        Assert.LessOrEqual(ma.Polylines[0].Max(p => Math.Abs(p.X)), 1.0001f);
    }

    [Test]
    public void ParamSurface_SphereVerticesOnTheSphere()
    {
        Assert.IsTrue(Plot3DParser.TryParse("(3cos(u)cos(v), 3cos(u)sin(v), 3sin(u)), u = -pi/2..pi/2, v = 0..2pi", out var s, out _));
        var m = Plot3DSampler.SampleParamSurface(s, Box3.Cube(5f), null, 30);
        Assert.Greater(m.TriangleCount, 1000);
        foreach (var p in m.Vertices)
            Assert.AreEqual(3f, p.Length(), 1e-3f);
    }

    [Test]
    public void Implicit_SphereAreaAndPoints()
    {
        Assert.IsTrue(Plot3DParser.TryParse("x^2 + y^2 + z^2 = 9", out var s, out _));
        var m = MarchingTetrahedra.Polygonise(p => s.EvalImplicit(p, null), Box3.Cube(5f), 40);
        double area = 0;
        for (int k = 0; k < m.Triangles.Count; k += 3)
        {
            var a = m.Vertices[m.Triangles[k]];
            var b = m.Vertices[m.Triangles[k + 1]];
            var c = m.Vertices[m.Triangles[k + 2]];
            area += NV3.Cross(b - a, c - a).Length() * 0.5;
        }

        Assert.AreEqual(4 * Math.PI * 9, area, 4 * Math.PI * 9 * 0.03, "surface area within 3%");
        foreach (var p in m.Vertices)
            Assert.AreEqual(3f, p.Length(), 0.08f);
        // Normals follow the gradient (outward for a sphere).
        for (int i = 0; i < m.Vertices.Count; i += 37)
            Assert.Greater(NV3.Dot(m.Normals[i], NV3.Normalize(m.Vertices[i])), 0.95f);
    }

    [Test]
    public void SurfaceGradient_MatchesAnalyticPartials()
    {
        Assert.IsTrue(Plot3DParser.TryParse("x^2*y + sin(y)", out var s, out _));
        var g = s.SurfaceGradient(1.5f, -0.5f, null);
        Assert.AreEqual(2f * 1.5f * -0.5f, g.X, 2e-3f);
        Assert.AreEqual(1.5f * 1.5f + (float)Math.Cos(-0.5f), g.Y, 2e-3f);
    }
}
