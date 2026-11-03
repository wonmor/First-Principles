using System;
using System.Collections.Generic;
using System.Numerics;

// -----------------------------------------------------------------------------
// Stage3D: one 3D platformer stage, and the catalog of all of them.
// -----------------------------------------------------------------------------
// Like the 2D stages, the graph IS the level: the player's function becomes the ground they walk
// on (z = f(x, y)), the rail they ride (r(t)) or the ramp they climb (r(u, v)). Unlike the 2D
// stages, the player builds the path: each stage gives a formula with sliders (a, b, c), or an
// open formula to type, and is solved when the walk from the start pad reaches the goal pad.
// Water below the stage is the fail state ("the function dipped under the water line").
// All coordinates are math coordinates (z up). Every stage carries a known solution that the
// EditMode tests check with Stage3DSolver and with the real motor (Stage3DMotor).
// -----------------------------------------------------------------------------

public enum Stage3DMode
{
    /// <summary>Walk on z = f(x, y).</summary>
    Surface,
    /// <summary>Ride along r(t) from the start pad to the goal pad.</summary>
    Rail,
    /// <summary>Walk on a parametric surface r(u, v) (ramps, spiral stairs).</summary>
    ParamSurface,
}

public sealed class Stage3DParam
{
    public string Name;
    public float Min, Max, Step, Initial;

    public Stage3DParam(string name, float min, float max, float step, float initial)
    {
        Name = name;
        Min = min;
        Max = max;
        Step = step;
        Initial = initial;
    }

    public float Snap(float v)
    {
        v = Math.Max(Min, Math.Min(Max, v));
        return Step > 0f ? Min + (float)Math.Round((v - Min) / Step) * Step : v;
    }
}

/// <summary>Solid axis-aligned block (a wall or pillar) in math coordinates.</summary>
public struct Block3
{
    public Vector3 Min, Max;

    public Block3(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        Min = new Vector3(x0, y0, z0);
        Max = new Vector3(x1, y1, z1);
    }

    public bool ContainsXY(float x, float y, float pad = 0f) =>
        x >= Min.X - pad && x <= Max.X + pad && y >= Min.Y - pad && y <= Max.Y + pad;

    public bool Contains(Vector3 p, float pad = 0f) =>
        ContainsXY(p.X, p.Y, pad) && p.Z >= Min.Z - pad && p.Z <= Max.Z + pad;
}

public sealed class Stage3D
{
    public string Id;
    public string Title;
    public string Concept;      // one line under the title in stage select
    public string Story;        // shown on the intro card
    public string[] Hints;      // progressive hints; the last one is close to the answer

    public Stage3DMode Mode = Stage3DMode.Surface;

    /// <summary>The player's formula. With sliders it uses a, b, c; when <see cref="FreeFormula"/> it is the starting text.</summary>
    public string Formula;
    public bool FreeFormula;
    public Stage3DParam[] Params = new Stage3DParam[0];

    /// <summary>Extra fixed ground drawn in ink (cannot be edited).</summary>
    public string[] FixedSurfaces = new string[0];

    public Vector3 Start;       // centre of the start pad's top face
    public Vector3 Goal;        // centre of the goal pad's top face
    public float PadHalf = 0.75f;
    public float Water = -1f;   // touching z <= Water fails the run
    public float Half = 6f;     // x, y in [-Half, Half]
    public float ZMin = -3f, ZMax = 6f;
    public Block3[] Blocks = new Block3[0];
    public Vector3[] Pickups = new Vector3[0];

    /// <summary>Rails: t range. Param surfaces: u range (Range1 = v range).</summary>
    public float[] Range0 = { 0f, 1f };
    public float[] Range1 = { 0f, 1f };

    /// <summary>A known solution: slider values (in Params order) or a typed formula.</summary>
    public float[] SolutionParams = new float[0];
    public string SolutionFormula;

    public Box3 Box => new Box3 { XMin = -Half, XMax = Half, YMin = -Half, YMax = Half, ZMin = ZMin, ZMax = ZMax };

    public float[] InitialParams()
    {
        var p = new float[Plot3DVars.ParamCount];
        for (int i = 0; i < p.Length; i++)
            p[i] = 1f;
        for (int i = 0; i < Params.Length; i++)
            p[ParamSlot(Params[i].Name)] = Params[i].Initial;
        return p;
    }

    /// <summary>Slider values laid out in a, b, c slots for <see cref="Plot3DSpec"/>.</summary>
    public float[] ToSlots(float[] valuesInParamOrder)
    {
        var p = InitialParams();
        for (int i = 0; i < Params.Length && i < valuesInParamOrder.Length; i++)
            p[ParamSlot(Params[i].Name)] = valuesInParamOrder[i];
        return p;
    }

    public static int ParamSlot(string name) => Math.Max(0, Array.IndexOf(Plot3DVars.ParamNames, name));

    /// <summary>The formula text as it is compiled for this stage's mode.</summary>
    public string CompiledText(string formula)
    {
        switch (Mode)
        {
            case Stage3DMode.Rail:
                return $"{formula}, t = {F(Range0[0])}..{F(Range0[1])}";
            case Stage3DMode.ParamSurface:
                return $"{formula}, u = {F(Range0[0])}..{F(Range0[1])}, v = {F(Range1[0])}..{F(Range1[1])}";
            default:
                return formula;
        }
    }

    static string F(float v) => v.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);

    public bool TryBuildSpec(string formula, out Plot3DSpec spec, out string error)
    {
        if (!Plot3DParser.TryParse(CompiledText(formula), out spec, out error))
            return false;
        var want = Mode == Stage3DMode.Rail ? Plot3DKind.Curve
            : Mode == Stage3DMode.ParamSurface ? Plot3DKind.ParamSurface : Plot3DKind.Surface;
        if (spec.Kind != want || (want == Plot3DKind.Surface && spec.DependentAxis != 2))
        {
            error = Mode == Stage3DMode.Rail ? "This stage needs a curve (x(t), y(t), z(t))"
                : Mode == Stage3DMode.ParamSurface ? "This stage needs a surface (x(u,v), y(u,v), z(u,v))"
                : "This stage needs a surface z = f(x, y)";
            spec = null;
            return false;
        }

        return true;
    }

    public List<Plot3DSpec> BuildFixedSpecs()
    {
        var list = new List<Plot3DSpec>();
        foreach (var s in FixedSurfaces)
        {
            if (Plot3DParser.TryParse(s, out var spec, out _))
                list.Add(spec);
        }

        return list;
    }
}

public static class Stage3DCatalog
{
    /// <summary>Stages 1-5 are free on the stores that sell the Full Game; the rest follow the Full Game unlock.</summary>
    public const int FreeStageCount = 5;

    public static bool IsFree(int index) => index >= 0 && index < FreeStageCount;

    static List<Stage3D> _all;

    public static IReadOnlyList<Stage3D> All => _all ??= Build();

    public static int Count => All.Count;

    static readonly float Pi = (float)Math.PI;

    static List<Stage3D> Build()
    {
        var s = new List<Stage3D>();

        // 1. Constant: z = c is a flat floor at height c.
        s.Add(new Stage3D
        {
            Id = "plane-c",
            Title = "Level ground",
            Concept = "z = c is a flat plane",
            Story = "The start and goal pads both sit at height 2, and the plane lies under the water. " +
                    "A constant function has the same height everywhere: raise c until the floor meets the pads.",
            Hints = new[] { "Both pads are at height z = 2.", "Set c = 2." },
            Formula = "c",
            Params = new[] { new Stage3DParam("c", -2f, 4f, 0.25f, -1.5f) },
            Start = new Vector3(-4f, 0f, 2f),
            Goal = new Vector3(4f, 0f, 2f),
            Water = 0f,
            Half = 5.5f, ZMin = -2f, ZMax = 4f,
            Pickups = new[] { new Vector3(-1f, 1.5f, 2.5f), new Vector3(1.5f, -1.5f, 2.5f) },
            SolutionParams = new[] { 2f },
        });

        // 2. Slope in x: z = m x.
        s.Add(new Stage3D
        {
            Id = "plane-mx",
            Title = "The ramp",
            Concept = "z = m x rises m for every step in x",
            Story = "The goal is 4 higher than the start, 8 steps across in x. The slope m is rise over run. " +
                    "Too steep and you cannot climb it; tilt it the wrong way and it sinks under the water.",
            Hints = new[] { "Rise over run: (2 − (−2)) / (4 − (−4)).", "Set m = 0.5 (shown as a)." },
            Formula = "a*x",
            Params = new[] { new Stage3DParam("a", -2f, 2f, 0.1f, -0.5f) },
            Start = new Vector3(-4f, 0f, -2f),
            Goal = new Vector3(4f, 0f, 2f),
            Water = -3f,
            Half = 5.5f, ZMin = -4f, ZMax = 4f,
            Pickups = new[] { new Vector3(-1.5f, 1.2f, -0.25f), new Vector3(2f, -1.2f, 1.5f) },
            SolutionParams = new[] { 0.5f },
        });

        // 3. Two slopes: z = a x + b y.
        s.Add(new Stage3D
        {
            Id = "plane-axby",
            Title = "Two directions",
            Concept = "z = a x + b y: a slope in x and a slope in y",
            Story = "A plane through the origin has a slope a along x and a slope b along y. " +
                    "The start pad is on the x axis, the goal pad on the y axis: each pad pins down one slope.",
            Hints = new[] { "At the start, y = 0, so the height there is a × (−4).", "a = 0.5 and b = 0.75." },
            Formula = "a*x + b*y",
            Params = new[] { new Stage3DParam("a", -1.5f, 1.5f, 0.25f, 0f), new Stage3DParam("b", -1.5f, 1.5f, 0.25f, 0f) },
            Start = new Vector3(-4f, 0f, -2f),
            Goal = new Vector3(0f, 4f, 3f),
            Water = -3f,
            Half = 5.5f, ZMin = -5f, ZMax = 5f,
            Pickups = new[] { new Vector3(-2f, 2f, 1f), new Vector3(0f, 0f, 0.5f) },
            SolutionParams = new[] { 0.5f, 0.75f },
        });

        // 4. Paraboloid bowl.
        s.Add(new Stage3D
        {
            Id = "bowl",
            Title = "The bowl",
            Concept = "z = a(x² + y²) + c: a paraboloid",
            Story = "x² + y² is the squared distance from the centre, so this surface is a bowl. " +
                    "a sets how fast the sides rise and c lifts the bottom. Two walls leave one way through, " +
                    "across the bottom: keep it above the water and the rim at the height of the pads.",
            Hints = new[] { "At the pads x² + y² = 16, so the rim height is 16a + c.",
                "The bottom is at height c. Try a = 0.1 and c = 1.4." },
            Formula = "a*(x^2 + y^2) + c",
            Params = new[] { new Stage3DParam("a", -0.3f, 0.3f, 0.025f, 0.25f), new Stage3DParam("c", -2f, 3f, 0.1f, -1f) },
            Start = new Vector3(-4f, 0f, 3f),
            Goal = new Vector3(4f, 0f, 3f),
            Water = 0.5f,
            Half = 5f, ZMin = -2f, ZMax = 6f,
            Blocks = new[] { new Block3(-0.3f, 1.5f, -2f, 0.3f, 5f, 6f), new Block3(-0.3f, -5f, -2f, 0.3f, -1.5f, 6f) },
            Pickups = new[] { new Vector3(0f, 0f, 1.9f), new Vector3(-2f, 1f, 2.35f) },
            SolutionParams = new[] { 0.1f, 1.4f },
        });

        // 5. Dome / hill to a summit goal.
        s.Add(new Stage3D
        {
            Id = "hill",
            Title = "Summit",
            Concept = "z = c − a(x² + y²): an upside-down bowl",
            Story = "The goal waits on the summit at the origin. The peak height is c; the slope a decides " +
                    "where the hill meets the start pad, five steps out.",
            Hints = new[] { "The summit height is c = 3.", "At the start x² + y² = 25: 3 − 25a = 0.5, so a = 0.1." },
            Formula = "c - a*(x^2 + y^2)",
            Params = new[] { new Stage3DParam("a", 0f, 0.4f, 0.025f, 0.3f), new Stage3DParam("c", 0f, 4f, 0.25f, 1f) },
            Start = new Vector3(-5f, 0f, 0.5f),
            Goal = new Vector3(0f, 0f, 3f),
            Water = 0f,
            Half = 6f, ZMin = -2f, ZMax = 5f,
            Pickups = new[] { new Vector3(-2.5f, 0f, 2.8f), new Vector3(0f, -2f, 3.1f) },
            SolutionParams = new[] { 0.1f, 3f },
        });

        // 6. Saddle.
        s.Add(new Stage3D
        {
            Id = "saddle",
            Title = "The saddle pass",
            Concept = "z = a x² − b y²: up one way, down the other",
            Story = "A saddle curves up along x and down along y, like a mountain pass. The start pad is high " +
                    "on the x axis and the goal low on the y axis. Fit the curvature to each one.",
            Hints = new[] { "On the x axis, z = 16a. On the y axis, z = −16b.", "a = 0.125 and b = 0.125." },
            Formula = "a*x^2 - b*y^2",
            Params = new[] { new Stage3DParam("a", -0.3f, 0.3f, 0.025f, 0f), new Stage3DParam("b", -0.3f, 0.3f, 0.025f, 0f) },
            Start = new Vector3(-4f, 0f, 2f),
            Goal = new Vector3(0f, 4f, -2f),
            Water = -3f,
            Half = 5.5f, ZMin = -5f, ZMax = 5f,
            Pickups = new[] { new Vector3(-2f, 2f, 0.4f), new Vector3(0f, 0f, 0.4f) },
            SolutionParams = new[] { 0.125f, 0.125f },
        });

        // 7. Sine wave.
        s.Add(new Stage3D
        {
            Id = "sine",
            Title = "Ripple road",
            Concept = "z = c + a sin(x): amplitude and midline",
            Story = "The start sits on a crest of the sine wave and the goal in a trough. " +
                    "The midline c is halfway between them and the amplitude a is half the drop.",
            Hints = new[] { "sin(−3π/2) = 1 and sin(3π/2) = −1, so c + a = 2 and c − a = 0.5.",
                "c = 1.25 and a = 0.75." },
            Formula = "c + a*sin(x)",
            Params = new[] { new Stage3DParam("a", 0f, 2f, 0.25f, 2f), new Stage3DParam("c", -1f, 3f, 0.25f, 0f) },
            Start = new Vector3(-1.5f * Pi, 0f, 2f),
            Goal = new Vector3(1.5f * Pi, 0f, 0.5f),
            Water = 0f,
            Half = 5.5f, ZMin = -2f, ZMax = 4f,
            Pickups = new[] { new Vector3(0.5f * Pi, 1f, 2.4f), new Vector3(-0.5f * Pi, -1f, 0.9f) },
            SolutionParams = new[] { 0.75f, 1.25f },
        });

        // 8. Egg crate.
        s.Add(new Stage3D
        {
            Id = "eggcrate",
            Title = "Egg crate",
            Concept = "z = c + a sin(x) cos(y): waves in two directions",
            Story = "Multiply a wave in x by a wave in y and you get hills and hollows on a grid. " +
                    "Both pads sit on hilltops. The hollows between them must stay above the water.",
            Hints = new[] { "On a hilltop sin(x)cos(y) = 1, so c + a = 2.5.",
                "The hollows are at c − a, which must be above 0.5. Try a = 1, c = 1.5." },
            Formula = "c + a*sin(x)*cos(y)",
            Params = new[] { new Stage3DParam("a", 0f, 2f, 0.25f, 2f), new Stage3DParam("c", -1f, 3f, 0.25f, 0.5f) },
            Start = new Vector3(-0.5f * Pi, -Pi, 2.5f),
            Goal = new Vector3(0.5f * Pi, 0f, 2.5f),
            Water = 0.5f,
            Half = 5.5f, ZMin = -2f, ZMax = 5f,
            Pickups = new[] { new Vector3(0f, -0.5f * Pi, 1.9f), new Vector3(0.5f * Pi, 0f, 2.95f) },
            SolutionParams = new[] { 1f, 1.5f },
        });

        // 9. Free formula: a bump over a wall.
        s.Add(new Stage3D
        {
            Id = "bump",
            Title = "Over the wall",
            Concept = "Type your own surface",
            Story = "A wall 3 high splits the stage, and the pads are low on either side. Type any surface " +
                    "z = f(x, y) that climbs over the wall and comes back down, without getting too steep.",
            Hints = new[] { "A bell curve A exp(−x²/s) is high in the middle and flat far away.",
                "Try 3.4*exp(-x^2/8)." },
            Formula = "0",
            FreeFormula = true,
            Start = new Vector3(-4.5f, 0f, 0.3f),
            Goal = new Vector3(4.5f, 0f, 0.3f),
            Water = -1f,
            Half = 6f, ZMin = -2f, ZMax = 5f,
            Blocks = new[] { new Block3(-0.25f, -6f, -2f, 0.25f, 6f, 3f) },
            Pickups = new[] { new Vector3(0f, 2f, 3.85f), new Vector3(-2f, -1f, 2.3f) },
            SolutionFormula = "3.4*exp(-x^2/8)",
        });

        // 10. Straight rail: a line through two points.
        s.Add(new Stage3D
        {
            Id = "rail-line",
            Title = "Vector rail",
            Concept = "r(t) = P + t d: a line in space",
            Story = "Now the graph is a rail you ride. The line starts at the start pad, P = (−4, −3, 1), " +
                    "and moves by the direction d = (8, b, c) as t goes from 0 to 1. Aim it at the goal pad.",
            Hints = new[] { "At t = 1 the rail is at (4, −3 + b, 1 + c).", "b = 6 and c = 3." },
            Formula = "(-4 + 8t, -3 + b*t, 1 + c*t)",
            Mode = Stage3DMode.Rail,
            Range0 = new[] { 0f, 1f },
            Params = new[] { new Stage3DParam("b", -6f, 8f, 0.5f, 0f), new Stage3DParam("c", -2f, 5f, 0.5f, 0f) },
            Start = new Vector3(-4f, -3f, 1f),
            Goal = new Vector3(4f, 3f, 4f),
            Water = 0f,
            Half = 5.5f, ZMin = -2f, ZMax = 6f,
            Pickups = new[] { new Vector3(0f, 0f, 2.5f), new Vector3(2f, 1.5f, 3.25f) },
            SolutionParams = new[] { 6f, 3f },
        });

        // 11. Parabolic arc rail over a wall.
        s.Add(new Stage3D
        {
            Id = "rail-arc",
            Title = "Arc of a throw",
            Concept = "r(t) = (t, 0, c − a t²): a parabola in space",
            Story = "A tall wall stands between the pads. Bend the rail into an arc, like the path of a thrown " +
                    "ball: high enough to clear the wall at t = 0, and back down to the pads at t = ±4.",
            Hints = new[] { "At t = ±4 the rail is at height c − 16a; the pads are at 0.5.",
                "The top must clear 3. Try c = 3.7 and a = 0.2." },
            Formula = "(t, 0, c - a*t^2)",
            Mode = Stage3DMode.Rail,
            Range0 = new[] { -4f, 4f },
            Params = new[] { new Stage3DParam("a", 0f, 0.5f, 0.025f, 0f), new Stage3DParam("c", 0f, 6f, 0.1f, 0.5f) },
            Start = new Vector3(-4f, 0f, 0.5f),
            Goal = new Vector3(4f, 0f, 0.5f),
            Water = -0.5f,
            Half = 5.5f, ZMin = -2f, ZMax = 6f,
            Blocks = new[] { new Block3(-0.3f, -2.5f, -2f, 0.3f, 2.5f, 3f) },
            Pickups = new[] { new Vector3(0f, 0f, 3.7f), new Vector3(-2f, 0f, 2.9f) },
            SolutionParams = new[] { 0.2f, 3.7f },
        });

        // 12. Helix bridge.
        s.Add(new Stage3D
        {
            Id = "helix",
            Title = "Helix bridge",
            Concept = "r(t) = (2cos t, 2sin t, a t): a helix",
            Story = "cos t and sin t walk a circle; a t climbs while they do. Two full turns (t from 0 to 4π) " +
                    "must lift the rail from the start pad to the goal pad, 2π above it.",
            Hints = new[] { "After two turns the height is a × 4π.", "a = 0.5." },
            Formula = "(2cos(t), 2sin(t), a*t)",
            Mode = Stage3DMode.Rail,
            Range0 = new[] { 0f, 4f * Pi },
            Params = new[] { new Stage3DParam("a", 0f, 1f, 0.05f, 0.2f) },
            Start = new Vector3(2f, 0f, 0f),
            Goal = new Vector3(2f, 0f, 2f * Pi),
            Water = -1f,
            Half = 4f, ZMin = -2f, ZMax = 8f,
            Blocks = new[] { new Block3(-0.4f, -0.4f, -2f, 0.4f, 0.4f, 7f) },
            Pickups = new[] { new Vector3(-2f, 0f, 0.5f * Pi), new Vector3(0f, -2f, 3.5f * Pi * 0.5f) },
            SolutionParams = new[] { 0.5f },
        });

        // 13. Helicoid: walk up a spiral ramp.
        s.Add(new Stage3D
        {
            Id = "helicoid",
            Title = "Spiral stairs",
            Concept = "r(u, v) = (u cos v, u sin v, a v): a helicoid",
            Story = "Sweep a line around an axis while lifting it and you get a spiral ramp. " +
                    "Walk it from the start pad to the goal, one and a half turns up on the far side.",
            Hints = new[] { "v runs from 0 to 3π. The goal is at height 3πa.", "The goal is at 2.83, so a = 0.3." },
            Formula = "(u*cos(v), u*sin(v), a*v)",
            Mode = Stage3DMode.ParamSurface,
            Range0 = new[] { 0.8f, 3f },
            Range1 = new[] { 0f, 3f * Pi },
            Params = new[] { new Stage3DParam("a", 0f, 0.6f, 0.05f, 0.1f) },
            Start = new Vector3(3.7f, 0f, 0f),
            Goal = new Vector3(-3.7f, 0f, 0.9f * Pi),
            PadHalf = 0.9f,
            Water = -0.5f,
            Half = 5f, ZMin = -2f, ZMax = 5f,
            Blocks = new[] { new Block3(-0.3f, -0.3f, -2f, 0.3f, 0.3f, 4.5f) },
            Pickups = new[] { new Vector3(0f, 2f, 0.15f * Pi + 0.4f), new Vector3(0f, -2f, 0.45f * Pi + 0.4f) },
            SolutionParams = new[] { 0.3f },
        });

        return s;
    }
}
