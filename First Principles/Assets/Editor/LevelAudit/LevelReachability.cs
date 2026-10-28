using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Graph search over resting positions, where every edge is a full simulated move with <see cref="PlayerMotor"/>
/// (the controller's own step function at 60 Hz): run, buffered / coyote jumps with full hold (touch play),
/// mid-air steering, the f′ air jump and spring launches. A target counts as reachable only on a move that
/// ends alive (a pickup on a fatal arc is lost on the restart).
/// </summary>
public static class LevelReachability
{
    public sealed class Result
    {
        public bool SpawnSettled;
        public string SpawnNote = "";
        public bool FinishReached;
        public bool CheckpointReached;
        public bool[] TokenReached;
        public int StandStates;
        public float MaxX;
        public readonly List<Vector2> Rests = new List<Vector2>();
    }

    const float Dt = 1f / 60f;
    const int MaxFrames = 420;

    struct AirPlan
    {
        public float First, Then;
        public int SwitchFrame;
        public AirPlan(float first, float then, int switchFrame)
        {
            First = first;
            Then = then;
            SwitchFrame = switchFrame;
        }
    }

    /// <summary>f′ segments bucketed by grid column (exactly the same test as <see cref="PlayerMotor.IsTouchingPolyline"/>, faster).</summary>
    sealed class DerivIndex
    {
        readonly List<Vector2>[] _cols;
        readonly float _r2;

        public DerivIndex(List<Vector2> pts, int columns, float radius)
        {
            _r2 = radius * radius;
            _cols = new List<Vector2>[columns + 2];
            for (int c = 0; c < _cols.Length; c++)
                _cols[c] = new List<Vector2>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                if (PolylineClip.IsBreak(a) || PolylineClip.IsBreak(b))
                    continue;
                int c0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius), 0, _cols.Length - 1);
                int c1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.x, b.x) + radius), 0, _cols.Length - 1);
                for (int c = c0; c <= c1; c++)
                {
                    _cols[c].Add(a);
                    _cols[c].Add(b);
                }
            }
        }

        public bool Touching(Vector2 p)
        {
            int c = Mathf.FloorToInt(p.x);
            if (c < 0 || c >= _cols.Length)
                return false;
            var segs = _cols[c];
            for (int i = 0; i + 1 < segs.Count; i += 2)
            {
                Vector2 a = segs[i], ab = segs[i + 1] - a;
                float den = ab.sqrMagnitude;
                float tt = den < 1e-10f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / den);
                if ((p - (a + tt * ab)).sqrMagnitude <= _r2)
                    return true;
            }
            return false;
        }
    }

    public static Result Explore(GraphWorld world, CourseLayout.Layout course, List<Vector2> derivPolyline, PlayerMotorTuning t)
    {
        var dIndex = new DerivIndex(derivPolyline, world.gridSize.x, t.DerivativeTouchRadius);
        var res = new Result { TokenReached = new bool[course.Tokens.Count] };
        float deathMinY = world.hasPlayBounds ? world.playBounds.YMin - 0.4f : -2f;

        // Settle at spawn with no input.
        var s0 = PlayerMotor.Spawn(world, t, strongFirstJump: false);
        float startY = s0.Pos.y;
        bool settled = false;
        for (int f = 0; f < 240; f++)
        {
            var step = PlayerMotor.Step(ref s0, default, t, world, deathMinY, Dt);
            if (step.Has(PlayerMotorEvents.Died))
            {
                res.SpawnNote = "dies without input";
                return res;
            }
            if (step.Has(PlayerMotorEvents.Finished))
            {
                res.SpawnNote = "spawns in the exit";
                return res;
            }
            if (s0.Grounded && Mathf.Abs(s0.Vel.x) < 1e-3f && Mathf.Abs(s0.Vel.y) < 1e-3f)
            {
                settled = true;
                break;
            }
        }
        if (!settled)
        {
            res.SpawnNote = "keeps falling";
            return res;
        }
        if (startY - s0.Pos.y > 0.35f)
        {
            res.SpawnNote = $"drops {startY - s0.Pos.y:0.0} cells after spawning";
            return res;
        }
        res.SpawnSettled = true;

        var queue = new Queue<PlayerMotorState>();
        var seen = new HashSet<long>();
        Enqueue(s0, queue, seen);
        res.MaxX = s0.Pos.x;

        var plans = new List<(float dir, int pre, AirPlan air)>();
        var tokensHit = new bool[course.Tokens.Count];

        while (queue.Count > 0 && seen.Count < 4000)
        {
            var start = queue.Dequeue();
            BuildPlans(plans);
            foreach (var plan in plans)
            {
                for (int airJumpMode = 0; airJumpMode < 3; airJumpMode++)
                {
                    System.Array.Clear(tokensHit, 0, tokensHit.Length);
                    var outcome = Simulate(start, plan.dir, plan.pre, plan.air, airJumpMode, world, course, dIndex, t, deathMinY,
                        tokensHit, out var end, out bool touchedDeriv, out bool checkpoint, out float maxX);
                    if (outcome == Outcome.Died)
                    {
                        if (!touchedDeriv) break;
                        continue;
                    }

                    for (int i = 0; i < tokensHit.Length; i++)
                        res.TokenReached[i] |= tokensHit[i];
                    res.CheckpointReached |= checkpoint;
                    res.MaxX = Mathf.Max(res.MaxX, maxX);
                    if (outcome == Outcome.Finished)
                        res.FinishReached = true;
                    else if (outcome == Outcome.Rest && Enqueue(end, queue, seen))
                        res.Rests.Add(end.Pos);

                    // Air-jump variants only matter when the arc actually met f′.
                    if (!touchedDeriv)
                        break;
                }
            }
        }

        res.StandStates = seen.Count;
        return res;
    }

    static void BuildPlans(List<(float, int, AirPlan)> plans)
    {
        plans.Clear();
        // Standing jumps: straight up, or up then drift over a ledge.
        plans.Add((0f, 0, new AirPlan(0f, 0f, 0)));
        foreach (float d in new[] { -1f, 1f })
        {
            plans.Add((0f, 0, new AirPlan(0f, d, 14)));
            plans.Add((0f, 0, new AirPlan(0f, d, 30)));
        }
        foreach (float d in new[] { -1f, 1f })
        {
            // pre = -1: walk (no jump): step or drop off the edge, steer in the air.
            foreach (int pre in new[] { -1, 0, 16 })
            {
                plans.Add((d, pre, new AirPlan(d, d, 0)));
                // Release the stick after k frames in the air: covers short, medium and long hops.
                foreach (int k in new[] { 8, 18, 30, 42, 56 })
                    plans.Add((d, pre, new AirPlan(d, 0f, k)));
                plans.Add((d, pre, new AirPlan(d, -d, 22)));
                if (pre >= 0)
                    plans.Add((d, pre, new AirPlan(0f, d, 20)));
            }
        }
    }

    enum Outcome { Died, Finished, Rest, Timeout }

    static Outcome Simulate(PlayerMotorState s, float dir, int preFrames, AirPlan air, int airJumpMode,
        GraphWorld world, CourseLayout.Layout course, DerivIndex deriv, in PlayerMotorTuning t, float deathMinY,
        bool[] tokensHit, out PlayerMotorState end, out bool touchedDeriv, out bool checkpoint, out float maxX)
    {
        touchedDeriv = false;
        checkpoint = false;
        maxX = s.Pos.x;
        end = s;
        Vector2 half = t.HalfSize;
        float startX = s.Pos.x;
        bool walkOnly = preFrames < 0;
        bool airborne = false, landed = false, airJumped = false;
        int airFrames = 0, restFrames = 0;
        float prevVy = 0f;

        for (int f = 0; f < MaxFrames; f++)
        {
            // Springs act like CourseExtras.Update: standing on the pad boosts the next ground jump.
            foreach (var sp in course.Springs)
                if (CourseLayout.IsOnSpring(sp, s.Grounded, s.Pos, half))
                    s.GroundJumpBoost = Mathf.Max(s.GroundJumpBoost, CourseLayout.SpringBoost);

            bool touching = deriv.Touching(s.Pos);
            touchedDeriv |= touching && airborne;

            var input = new PlayerMotorInput { JumpHeld = true, TouchingDerivative = touching };
            if (!airborne)
            {
                input.MoveX = dir;
                if (!walkOnly && f == preFrames)
                    input.TouchJumpPressed = true;
            }
            else if (!landed)
            {
                input.MoveX = airFrames < air.SwitchFrame ? air.First : air.Then;
                if (!airJumped && touching && airJumpMode > 0 && (airJumpMode == 2 || s.Vel.y <= 0f))
                {
                    input.TouchJumpPressed = true;
                    airJumped = true;
                }
            }

            var step = PlayerMotor.Step(ref s, input, t, world, deathMinY, Dt);
            if (step.Has(PlayerMotorEvents.Died))
                return Outcome.Died;
            if (step.Has(PlayerMotorEvents.Finished))
            {
                maxX = Mathf.Max(maxX, world.finish.xMin);
                return Outcome.Finished;
            }
            maxX = Mathf.Max(maxX, s.Pos.x);

            for (int i = 0; i < course.Tokens.Count; i++)
                if (!tokensHit[i] && CourseLayout.TouchesToken(course.Tokens[i].Pos, s.Pos, half))
                    tokensHit[i] = true;
            if (course.HasCheckpoint && s.Grounded && s.Pos.x >= course.Checkpoint.x - CourseLayout.CheckpointTriggerLead)
                checkpoint = true;

            if (!airborne && !s.Grounded && (s.Vel.y > 0.01f || s.Vel.y < -0.5f || prevVy < -0.5f))
                airborne = true;
            prevVy = s.Vel.y;
            if (airborne)
            {
                airFrames++;
                if (s.Grounded)
                    landed = true;
            }

            // Walk-only plans that never leave the ground: rest every half cell so long floors chain.
            if (!airborne && walkOnly && Mathf.Abs(s.Pos.x - startX) >= 0.5f && s.Grounded)
            {
                end = Rested(s);
                return Outcome.Rest;
            }
            if (!airborne && f > preFrames + 30 && !walkOnly)
                return Outcome.Timeout; // jump never fired (blocked)
            if (!airborne && walkOnly && f > 60)
                return Outcome.Timeout; // pushing into a wall

            if (landed)
            {
                if (s.Grounded && Mathf.Abs(s.Vel.x) < 1e-3f)
                    restFrames++;
                else
                    restFrames = 0;
                if (restFrames >= 2)
                {
                    end = Rested(s);
                    return Outcome.Rest;
                }
            }
        }
        return Outcome.Timeout;
    }

    static PlayerMotorState Rested(PlayerMotorState s)
    {
        s.Vel = Vector2.zero;
        s.JumpBufferTimer = 0f;
        s.JumpCutAvailable = false;
        s.AirJumpConsumedThisBand = false;
        s.TouchJumpHeld = false;
        s.GroundJumpBoost = 1f;
        return s;
    }

    static bool Enqueue(PlayerMotorState s, Queue<PlayerMotorState> q, HashSet<long> seen)
    {
        long kx = Mathf.RoundToInt(s.Pos.x / 0.4f);
        long ky = Mathf.RoundToInt(s.Pos.y / 0.05f);
        if (!seen.Add((kx << 32) ^ (ky & 0xffffffffL)))
            return false;
        q.Enqueue(s);
        return true;
    }
}
