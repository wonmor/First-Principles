using System.Collections.Generic;
using NUnit.Framework;
using NV2 = System.Numerics.Vector2;
using NV3 = System.Numerics.Vector3;

/// <summary>
/// 3D stages (1.5): every stage has a known solution that the solver accepts and the real motor completes,
/// every star pickup is reachable with it, and no stage is already solved by its starting sliders.
/// Plus motor collision checks against surfaces, walls, rails and water.
/// </summary>
public class Space3DStageTests
{
    static IEnumerable<int> StageIndices()
    {
        for (int i = 0; i < Stage3DCatalog.Count; i++)
            yield return i;
    }

    static string SolutionFormula(Stage3D s) => s.FreeFormula ? s.SolutionFormula : s.Formula;
    static float[] SolutionSlots(Stage3D s) => s.FreeFormula ? s.InitialParams() : s.ToSlots(s.SolutionParams);

    [Test]
    public void Catalog_HasTenToFifteenStagesWithHintsAndUniqueIds()
    {
        Assert.That(Stage3DCatalog.Count, Is.InRange(10, 15));
        var ids = new HashSet<string>();
        foreach (var s in Stage3DCatalog.All)
        {
            Assert.IsTrue(ids.Add(s.Id), "duplicate id " + s.Id);
            Assert.GreaterOrEqual(s.Hints.Length, 2, s.Id);
            Assert.IsFalse(string.IsNullOrEmpty(s.Story), s.Id);
            Assert.IsFalse((s.Story + s.Concept + s.Title + string.Join("", s.Hints)).Contains("\u00B7") ||
                           (s.Story + s.Concept + s.Title + string.Join("", s.Hints)).Contains("\u2022"), s.Id + " uses a middle dot or bullet");
            Assert.AreEqual(s.FreeFormula ? 0 : s.Params.Length, s.SolutionParams.Length, s.Id);
            foreach (var p in s.Params)
                Assert.That(p.Initial, Is.InRange(p.Min, p.Max), s.Id + " " + p.Name);
            for (int k = 0; k < s.Params.Length; k++)
            {
                var p = s.Params[k];
                Assert.That(s.SolutionParams[k], Is.InRange(p.Min, p.Max), s.Id);
                Assert.AreEqual(s.SolutionParams[k], p.Snap(s.SolutionParams[k]), 1e-4f, s.Id + ": the solution must sit on a slider step");
            }
        }
    }

    [Test]
    public void EveryStage_SolutionSolvesAndReplays([ValueSource(nameof(StageIndices))] int i)
    {
        var s = Stage3DCatalog.All[i];
        var res = Stage3DSolver.Solve(s, SolutionFormula(s), SolutionSlots(s));
        Assert.IsTrue(res.Solved, $"{s.Id}: {res.Reason}");
        for (int p = 0; p < res.PickupReachable.Length; p++)
            Assert.IsTrue(res.PickupReachable[p], $"{s.Id}: pickup {p} unreachable with the solution");
        var m = Stage3DSolver.Replay(s, SolutionFormula(s), SolutionSlots(s), res);
        Assert.AreEqual(Stage3DMotorState.Won, m.State, $"{s.Id}: motor ended {m.State} ({m.DeathReason}) at {m.Feet}");
    }

    [Test]
    public void EveryStage_StartingSlidersDoNotSolveIt([ValueSource(nameof(StageIndices))] int i)
    {
        var s = Stage3DCatalog.All[i];
        var res = Stage3DSolver.Solve(s, s.Formula, s.InitialParams());
        Assert.IsFalse(res.Solved, s.Id + " is solved before the player does anything");
    }

    [Test]
    public void Free_AccessIsTheFirstFiveStages()
    {
        for (int i = 0; i < Stage3DCatalog.Count; i++)
            Assert.AreEqual(i < 5, Stage3DCatalog.IsFree(i), i.ToString());
    }

    static Stage3DWorld FlatWorld(float c, params Block3[] blocks)
    {
        var st = new Stage3D
        {
            Id = "test", Formula = "c", Params = new[] { new Stage3DParam("c", -5, 5, 0.1f, c) },
            Start = new NV3(-4, 0, c), Goal = new NV3(4, 0, c), Water = -2f, Half = 6f, Blocks = blocks,
        };
        Assert.IsTrue(st.TryBuildSpec("c", out var spec, out _));
        return new Stage3DWorld(st, spec, st.InitialParams());
    }

    [Test]
    public void Motor_WalksOnTheSurfaceAndFollowsItsHeight()
    {
        var st = new Stage3D { Id = "ramp", Formula = "0.5*x", Start = new NV3(-4, 0, -2), Goal = new NV3(4, 0, 2), Water = -5f, Half = 6f };
        Assert.IsTrue(st.TryBuildSpec(st.Formula, out var spec, out _));
        var m = new Stage3DMotor(new Stage3DWorld(st, spec, st.InitialParams()));
        for (int k = 0; k < 600 && !m.Finished; k++)
        {
            m.Step(1f / 60f, new NV2(1, 0), false, 0);
            if (m.State == Stage3DMotorState.Walking && m.SupportId == Stage3DWorld.SupportPlayer)
                Assert.AreEqual(0.5f * m.Feet.X, m.Feet.Z, 1e-3f, "feet on z = 0.5x");
        }

        Assert.AreEqual(Stage3DMotorState.Won, m.State);
    }

    [Test]
    public void Motor_CannotClimbTooSteepASlope()
    {
        var st = new Stage3D { Id = "wall", Formula = "2*x", Start = new NV3(-4, 0, -8), Goal = new NV3(4, 0, 8), Water = -20f, Half = 6f, ZMin = -20, ZMax = 20 };
        Assert.IsTrue(st.TryBuildSpec(st.Formula, out var spec, out _));
        var m = new Stage3DMotor(new Stage3DWorld(st, spec, st.InitialParams()));
        for (int k = 0; k < 600; k++)
            m.Step(1f / 60f, new NV2(1, 0), false, 0);
        Assert.Less(m.Feet.X, -3f, "slope 2 is steeper than MaxSlope, so the player stays near the start pad");
        Assert.AreNotEqual(Stage3DMotorState.Won, m.State);
    }

    [Test]
    public void Motor_FallsIntoWaterAndDies()
    {
        var w = FlatWorld(-3f); // floor under the water line (-2)
        var m = new Stage3DMotor(w);
        m.Feet = new NV3(-4, 0, 1f);
        for (int k = 0; k < 300 && !m.Finished; k++)
            m.Step(1f / 60f, new NV2(1, 0), false, 0);
        Assert.AreEqual(Stage3DMotorState.Dead, m.State);
    }

    [Test]
    public void Motor_WallsBlockAndJumpsClearLowOnes()
    {
        var tall = FlatWorld(0f, new Block3(-0.2f, -6, -2, 0.2f, 6, 3f));
        var m = new Stage3DMotor(tall);
        for (int k = 0; k < 400; k++)
            m.Step(1f / 60f, new NV2(1, 0), k % 30 == 0, 0);
        Assert.Less(m.Feet.X, 0f, "a 3-high wall cannot be jumped");

        var low = FlatWorld(0f, new Block3(-0.2f, -6, -2, 0.2f, 6, 0.8f));
        var j = new Stage3DMotor(low);
        bool jumped = false;
        for (int k = 0; k < 600 && !j.Finished; k++)
        {
            bool jump = !jumped && j.Feet.X > -1.2f;
            jumped |= jump;
            j.Step(1f / 60f, new NV2(1, 0), jump, 0);
        }

        Assert.AreEqual(Stage3DMotorState.Won, j.State, "a 0.8-high wall is cleared with one jump");
    }

    [Test]
    public void Motor_RidesTheHelixRailToTheGoal()
    {
        var s = Stage3DCatalog.All[Stage3DCatalog.All.Count - 2];
        Assert.AreEqual(Stage3DMode.Rail, s.Mode);
        Assert.IsTrue(s.TryBuildSpec(s.Formula, out var spec, out _));
        var m = new Stage3DMotor(new Stage3DWorld(s, spec, s.ToSlots(s.SolutionParams)));
        Assert.AreEqual(Stage3DMotorState.Rail, m.State, "the rail picks the player up on the start pad");
        float last = m.RailT;
        for (int k = 0; k < 6000 && !m.Finished; k++)
        {
            m.Step(1f / 60f, NV2.Zero, false, 1f);
            if (m.State == Stage3DMotorState.Rail)
            {
                Assert.GreaterOrEqual(m.RailT, last);
                var p = spec.CurvePoint(m.RailT, s.ToSlots(s.SolutionParams));
                Assert.Less((p - m.Feet).Length(), 1e-4f, "feet stay on r(t)");
                last = m.RailT;
            }
        }

        Assert.AreEqual(Stage3DMotorState.Won, m.State);
    }

    [Test]
    public void Motor_RailIntoAWallCrashes()
    {
        var s = Stage3DCatalog.All[10]; // arc over a wall
        Assert.AreEqual("rail-arc", s.Id);
        Assert.IsTrue(s.TryBuildSpec(s.Formula, out var spec, out _));
        var m = new Stage3DMotor(new Stage3DWorld(s, spec, s.InitialParams())); // flat rail at the pad height
        for (int k = 0; k < 3000 && !m.Finished; k++)
            m.Step(1f / 60f, NV2.Zero, false, 1f);
        Assert.AreEqual(Stage3DMotorState.Dead, m.State);
    }
}
