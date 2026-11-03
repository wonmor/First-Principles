using System;
using System.Collections.Generic;
using System.Numerics;

// -----------------------------------------------------------------------------
// Stage3DWorld + Stage3DMotor + Stage3DSolver: 3D stage physics, pure C# (unit-tested).
// -----------------------------------------------------------------------------
// Collision is analytic: the ground under the player is the highest "support" (the player's
// surface f(x, y), fixed surfaces, pads, block tops) at or below their feet. Walking uphill is
// limited by MaxSlope (rise per horizontal distance on the same support) plus StepUp when moving
// onto a different support (a pad edge). Rails and parametric surfaces attach the player to the
// curve / surface parameters (t, or u and v) instead of a height lookup.
// The solver runs the same rules as a graph search, so "is this stage solvable with these
// sliders?" has one definition shared by the hint system and the tests.
// -----------------------------------------------------------------------------

public sealed class Stage3DWorld
{
    public const float WalkSpeed = 3.4f;
    public const float RailSpeed = 3.8f;
    public const float Gravity = 20f;
    public const float JumpSpeed = 6.5f;      // apex ~1.05 above take-off
    public const float MaxSlope = 1.2f;       // ~50 degrees
    public const float StepUp = 0.3f;         // onto a pad / block edge
    public const float PlayerRadius = 0.22f;
    public const float CenterHeight = 0.45f;  // body centre above the feet
    public const float BodyHeight = 0.9f;
    public const float AttachDist = 0.6f;     // rail / param surface pick-up distance
    public const float GoalTolerance = 0.35f; // |feet - pad top| on the goal footprint
    public const float PickupRadius = 0.7f;

    public const int SupportNone = -1, SupportStartPad = 0, SupportGoalPad = 1, SupportPlayer = 2,
        SupportFixed = 10, SupportBlock = 100;

    public readonly Stage3D Stage;
    public readonly Plot3DSpec Player;   // null when the formula does not compile
    public readonly float[] Prm;
    public readonly List<Plot3DSpec> Fixed;

    // Param surface lookup grid for attaching (u, v from a world point).
    const int AttachGrid = 48;
    Vector3[] attachPts;

    public Stage3DWorld(Stage3D stage, Plot3DSpec player, float[] prm)
    {
        Stage = stage;
        Player = player;
        Prm = prm;
        Fixed = stage.BuildFixedSpecs();
        if (stage.Mode == Stage3DMode.ParamSurface && player != null)
        {
            attachPts = new Vector3[(AttachGrid + 1) * (AttachGrid + 1)];
            for (int j = 0; j <= AttachGrid; j++)
            for (int i = 0; i <= AttachGrid; i++)
                attachPts[j * (AttachGrid + 1) + i] = player.ParamPoint(U(i / (float)AttachGrid), V(j / (float)AttachGrid), prm);
        }
    }

    public float U(float s) => Stage.Range0[0] + (Stage.Range0[1] - Stage.Range0[0]) * s;
    public float V(float s) => Stage.Range1[0] + (Stage.Range1[1] - Stage.Range1[0]) * s;

    static bool Finite(float v) => !(float.IsNaN(v) || float.IsInfinity(v));

    public static bool Finite(Vector3 p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);

    bool InDomain(float x, float y) => Math.Abs(x) <= Stage.Half && Math.Abs(y) <= Stage.Half;

    public bool OnPad(Vector3 pad, float x, float y) =>
        Math.Abs(x - pad.X) <= Stage.PadHalf && Math.Abs(y - pad.Y) <= Stage.PadHalf;

    /// <summary>Highest support top at (x, y) not above <paramref name="ceiling"/>; NaN when there is none.</summary>
    public float SupportBelow(float x, float y, float ceiling, out int id)
    {
        float best = float.NaN;
        int bestId = SupportNone;
        void Consider(float top, int sid)
        {
            if (Finite(top) && top <= ceiling && (float.IsNaN(best) || top > best))
            {
                best = top;
                bestId = sid;
            }
        }

        if (OnPad(Stage.Start, x, y)) Consider(Stage.Start.Z, SupportStartPad);
        if (OnPad(Stage.Goal, x, y)) Consider(Stage.Goal.Z, SupportGoalPad);
        for (int k = 0; k < Stage.Blocks.Length; k++)
        {
            if (Stage.Blocks[k].ContainsXY(x, y))
                Consider(Stage.Blocks[k].Max.Z, SupportBlock + k);
        }

        if (InDomain(x, y))
        {
            if (Stage.Mode == Stage3DMode.Surface && Player != null)
                Consider(Player.EvalSurface(x, y, Prm), SupportPlayer);
            for (int k = 0; k < Fixed.Count; k++)
                Consider(Fixed[k].EvalSurface(x, y, Prm), SupportFixed + k);
        }

        id = bestId;
        return best;
    }

    /// <summary>True when a block's side is in the way of a body standing at (x, y, feet).</summary>
    public bool BlockedAt(float x, float y, float feet)
    {
        foreach (var b in Stage.Blocks)
        {
            if (b.ContainsXY(x, y, PlayerRadius) && b.Max.Z > feet + StepUp && b.Min.Z < feet + BodyHeight)
                return true;
        }

        return false;
    }

    public bool InsideBlock(Vector3 p, float pad)
    {
        foreach (var b in Stage.Blocks)
        {
            if (b.Contains(p, pad))
                return true;
        }

        return false;
    }

    public bool AtGoal(Vector3 feet) =>
        OnPad(Stage.Goal, feet.X, feet.Y) && Math.Abs(feet.Z - Stage.Goal.Z) <= GoalTolerance;

    public Vector3 RailPoint(float t) => Player.CurvePoint(t, Prm);

    public Vector3 SurfacePoint(float u, float v) => Player.ParamPoint(u, v, Prm);

    /// <summary>Nearest (u, v) on the param surface to <paramref name="p"/>, from the lookup grid; returns the distance.</summary>
    public float NearestUV(Vector3 p, out float u, out float v)
    {
        u = v = float.NaN;
        if (attachPts == null)
            return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        int bi = -1;
        for (int k = 0; k < attachPts.Length; k++)
        {
            if (!Finite(attachPts[k]))
                continue;
            float d = (attachPts[k] - p).LengthSquared();
            if (d < best)
            {
                best = d;
                bi = k;
            }
        }

        if (bi < 0)
            return float.PositiveInfinity;
        u = U((bi % (AttachGrid + 1)) / (float)AttachGrid);
        v = V((bi / (AttachGrid + 1)) / (float)AttachGrid);
        // Refine with a few gradient-free shrinking steps.
        float du = (Stage.Range0[1] - Stage.Range0[0]) / AttachGrid, dv = (Stage.Range1[1] - Stage.Range1[0]) / AttachGrid;
        float bestD = (SurfacePoint(u, v) - p).Length();
        for (int it = 0; it < 12; it++)
        {
            bool moved = false;
            foreach (var (su, sv) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                float nu = Clamp(u + su * du, Stage.Range0[0], Stage.Range0[1]);
                float nv = Clamp(v + sv * dv, Stage.Range1[0], Stage.Range1[1]);
                float d = (SurfacePoint(nu, nv) - p).Length();
                if (d < bestD)
                {
                    bestD = d;
                    u = nu;
                    v = nv;
                    moved = true;
                }
            }

            if (!moved)
            {
                du *= 0.5f;
                dv *= 0.5f;
            }
        }

        return bestD;
    }

    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
}

public enum Stage3DMotorState
{
    Walking,
    Air,
    Rail,
    OnSurface,
    Dead,
    Won,
}

/// <summary>The player's body in a 3D stage. Deterministic: same inputs, same run (tests replay it).</summary>
public sealed class Stage3DMotor
{
    public readonly Stage3DWorld World;
    public Vector3 Feet;
    public float VelZ;
    public Vector2 AirVel;
    public Stage3DMotorState State = Stage3DMotorState.Walking;
    public float RailT, SurfU, SurfV;
    public int SupportId = Stage3DWorld.SupportStartPad;
    public readonly bool[] Collected;
    public string DeathReason = "";
    public float RunSeconds;
    public bool JustLanded, JustJumped, JustCollected;

    /// <summary>Set after stepping off a rail / ramp on purpose, so it does not grab the player straight back.</summary>
    bool attachLocked;

    public Stage3DMotor(Stage3DWorld world)
    {
        World = world;
        Feet = world.Stage.Start;
        Collected = new bool[world.Stage.Pickups.Length];
        TryAttach();
    }

    public bool Finished => State == Stage3DMotorState.Dead || State == Stage3DMotorState.Won;

    public int CollectedCount
    {
        get
        {
            int n = 0;
            foreach (bool c in Collected)
                if (c) n++;
            return n;
        }
    }

    public Vector3 Center => Feet + new Vector3(0f, 0f, Stage3DWorld.CenterHeight);

    /// <summary>Direction of travel along the rail in math coordinates (for the camera).</summary>
    public Vector3 RailTangent()
    {
        var d = World.Player.CurveVelocity(RailT, World.Prm);
        float l = d.Length();
        return l > 1e-6f ? d / l : Vector3.UnitX;
    }

    /// <param name="move">Desired horizontal direction in math x, y (length 0..1).</param>
    /// <param name="ride">Rails: +1 forward along t, −1 back.</param>
    public void Step(float dt, Vector2 move, bool jump, float ride)
    {
        JustLanded = JustJumped = JustCollected = false;
        if (Finished)
            return;
        RunSeconds += dt;
        if (move.LengthSquared() > 1f)
            move = Vector2.Normalize(move);

        switch (State)
        {
            case Stage3DMotorState.Rail:
                StepRail(dt, ride, jump);
                break;
            case Stage3DMotorState.OnSurface:
                StepParamSurface(dt, move, jump);
                break;
            default:
                StepWalkAir(dt, move, jump);
                break;
        }

        if (Finished)
            return;
        if (Feet.Z <= World.Stage.Water)
        {
            Die("You fell in the water");
            return;
        }

        var c = Center;
        for (int k = 0; k < Collected.Length; k++)
        {
            if (!Collected[k] && (World.Stage.Pickups[k] - c).Length() <= Stage3DWorld.PickupRadius)
            {
                Collected[k] = true;
                JustCollected = true;
            }
        }

        if ((State == Stage3DMotorState.Walking) && World.AtGoal(Feet))
            State = Stage3DMotorState.Won;
    }

    void Die(string why)
    {
        State = Stage3DMotorState.Dead;
        DeathReason = why;
    }

    void StepWalkAir(float dt, Vector2 move, bool jump)
    {
        var w = World;
        if (State == Stage3DMotorState.Walking && jump)
        {
            State = Stage3DMotorState.Air;
            VelZ = Stage3DWorld.JumpSpeed;
            AirVel = move * Stage3DWorld.WalkSpeed;
            JustJumped = true;
        }

        if (State == Stage3DMotorState.Walking)
        {
            Vector2 d = move * Stage3DWorld.WalkSpeed * dt;
            if (d.LengthSquared() > 0f)
            {
                if (!TryWalk(d) && !TryWalk(new Vector2(d.X, 0f)))
                    TryWalk(new Vector2(0f, d.Y));
            }

            // Stay on the ground, or step off an edge.
            float g = w.SupportBelow(Feet.X, Feet.Y, Feet.Z + 0.01f, out int id);
            float allowDrop = 0.05f + Stage3DWorld.MaxSlope * Math.Max(d.Length(), 0.02f);
            if (float.IsNaN(g) || Feet.Z - g > allowDrop)
            {
                State = Stage3DMotorState.Air;
                VelZ = 0f;
                AirVel = move * Stage3DWorld.WalkSpeed;
            }
            else
            {
                Feet.Z = g;
                SupportId = id;
            }
        }
        else
        {
            // Air: keep some control, fall, land on the highest support crossed.
            AirVel = Vector2.Lerp(AirVel, move * Stage3DWorld.WalkSpeed, Math.Min(1f, dt * 4f));
            var nxy = new Vector2(Feet.X, Feet.Y) + AirVel * dt;
            if (!w.BlockedAt(nxy.X, nxy.Y, Feet.Z))
            {
                Feet.X = nxy.X;
                Feet.Y = nxy.Y;
            }
            else
                AirVel = Vector2.Zero;

            float prevZ = Feet.Z;
            VelZ -= Stage3DWorld.Gravity * dt;
            Feet.Z += VelZ * dt;
            if (VelZ <= 0f)
            {
                float g = w.SupportBelow(Feet.X, Feet.Y, prevZ + 0.01f, out int id);
                if (!float.IsNaN(g) && Feet.Z <= g)
                {
                    Feet.Z = g;
                    SupportId = id;
                    State = Stage3DMotorState.Walking;
                    VelZ = 0f;
                    JustLanded = true;
                }
            }
        }

        TryAttach();
    }

    bool TryWalk(Vector2 d)
    {
        var w = World;
        float nx = Feet.X + d.X, ny = Feet.Y + d.Y;
        if (w.BlockedAt(nx, ny, Feet.Z))
            return false;
        float dist = d.Length();
        float slopeRise = Stage3DWorld.MaxSlope * dist + 1e-3f;
        float g = w.SupportBelow(nx, ny, Feet.Z + Stage3DWorld.StepUp + slopeRise, out int id);
        if (!float.IsNaN(g))
        {
            float rise = g - Feet.Z;
            float allowed = slopeRise + (id != SupportId ? Stage3DWorld.StepUp : 0f);
            if (rise > allowed)
                return false;
            if (rise > 0f)
            {
                Feet.Z = g;
                SupportId = id;
            }
        }

        Feet.X = nx;
        Feet.Y = ny;
        return true;
    }

    /// <summary>Rails and param surfaces pick the player up when their feet come close.</summary>
    void TryAttach()
    {
        var w = World;
        if (w.Player == null || Finished)
            return;
        if (w.Stage.Mode == Stage3DMode.Rail && (State == Stage3DMotorState.Walking || (State == Stage3DMotorState.Air && VelZ <= 0f)))
        {
            float t0 = w.Stage.Range0[0];
            var p0 = w.RailPoint(t0);
            bool near = Stage3DWorld.Finite(p0) && (p0 - Feet).Length() <= Stage3DWorld.AttachDist;
            if (!near)
                attachLocked = false;
            else if (!attachLocked)
            {
                State = Stage3DMotorState.Rail;
                RailT = t0;
                Feet = p0;
            }
        }
        else if (w.Stage.Mode == Stage3DMode.ParamSurface && (State == Stage3DMotorState.Walking || (State == Stage3DMotorState.Air && VelZ <= 0f)))
        {
            float d = w.NearestUV(Feet, out float u, out float v);
            if (d > 0.5f)
                attachLocked = false;
            if (d <= 0.35f && !attachLocked)
            {
                var p = w.SurfacePoint(u, v);
                if (p.Z <= Feet.Z + Stage3DWorld.StepUp && !w.BlockedAt(p.X, p.Y, p.Z))
                {
                    State = Stage3DMotorState.OnSurface;
                    SurfU = u;
                    SurfV = v;
                    Feet = p;
                }
            }
        }
    }

    void StepRail(float dt, float ride, bool jump)
    {
        var w = World;
        float t0 = w.Stage.Range0[0], t1 = w.Stage.Range0[1];
        if (jump)
        {
            Detach(Stage3DWorld.JumpSpeed);
            JustJumped = true;
            return;
        }

        var vel = w.Player.CurveVelocity(RailT, w.Prm);
        float speed = vel.Length();
        if (!(speed > 1e-5f) || float.IsNaN(speed))
            speed = 1f;
        float nt = RailT + ride * Stage3DWorld.RailSpeed * dt / speed;
        // Never skip more than a small arc in one step (keeps crash checks honest).
        float maxDt = (t1 - t0) / 400f;
        nt = RailT + Stage3DWorld.Clamp(nt - RailT, -maxDt * 4f, maxDt * 4f);
        if (nt >= t1)
        {
            nt = t1;
            var end = w.RailPoint(t1);
            if (Stage3DWorld.Finite(end) && w.OnPad(w.Stage.Goal, end.X, end.Y) &&
                Math.Abs(end.Z - w.Stage.Goal.Z) <= Stage3DWorld.AttachDist)
            {
                Feet = new Vector3(end.X, end.Y, w.Stage.Goal.Z);
                State = Stage3DMotorState.Walking;
                SupportId = Stage3DWorld.SupportGoalPad;
                return;
            }

            RailT = t1;
            Feet = end;
            Detach(0f);
            return;
        }

        if (nt <= t0)
        {
            nt = t0;
            if (ride < 0f)
            {
                // Step back onto the start pad.
                RailT = t0;
                Feet = w.RailPoint(t0);
                Detach(0f);
                attachLocked = true; // do not re-attach until the player walks away and back
                return;
            }
        }

        var p = w.RailPoint(nt);
        if (!Stage3DWorld.Finite(p))
        {
            Die("The rail is undefined there");
            return;
        }

        if (w.InsideBlock(p + new Vector3(0, 0, Stage3DWorld.CenterHeight), 0.25f))
        {
            Die("The rail runs into a wall");
            return;
        }

        RailT = nt;
        Feet = p;
    }

    void Detach(float velZ)
    {
        State = Stage3DMotorState.Air;
        VelZ = velZ;
        AirVel = Vector2.Zero;
        SupportId = Stage3DWorld.SupportNone;
    }

    void StepParamSurface(float dt, Vector2 move, bool jump)
    {
        var w = World;
        if (jump)
        {
            Detach(Stage3DWorld.JumpSpeed);
            AirVel = move * Stage3DWorld.WalkSpeed;
            JustJumped = true;
            return;
        }

        if (move.LengthSquared() < 1e-8f)
            return;
        var d = move * Stage3DWorld.WalkSpeed * dt;
        // Horizontal Jacobian of (x, y) with respect to (u, v).
        float h = 1e-3f;
        var pu = (w.SurfacePoint(SurfU + h, SurfV) - w.SurfacePoint(SurfU - h, SurfV)) / (2f * h);
        var pv = (w.SurfacePoint(SurfU, SurfV + h) - w.SurfacePoint(SurfU, SurfV - h)) / (2f * h);
        float det = pu.X * pv.Y - pv.X * pu.Y;
        float du, dv;
        if (Math.Abs(det) > 1e-4f)
        {
            du = (pv.Y * d.X - pv.X * d.Y) / det;
            dv = (-pu.Y * d.X + pu.X * d.Y) / det;
        }
        else
        {
            // Degenerate (vertical) patch: move along the steepest horizontal parameter direction.
            du = (pu.X * d.X + pu.Y * d.Y) / Math.Max(1e-4f, pu.X * pu.X + pu.Y * pu.Y);
            dv = (pv.X * d.X + pv.Y * d.Y) / Math.Max(1e-4f, pv.X * pv.X + pv.Y * pv.Y);
        }

        float nu = SurfU + du, nv = SurfV + dv;
        var st = w.Stage;
        bool off = nu < st.Range0[0] || nu > st.Range0[1] || nv < st.Range1[0] || nv > st.Range1[1];
        if (off)
        {
            // Walk off the edge: continue into the air from here.
            Detach(0f);
            AirVel = move * Stage3DWorld.WalkSpeed;
            attachLocked = true;
            Feet.X += d.X;
            Feet.Y += d.Y;
            // Step straight onto a pad or block at the edge (like walking onto a kerb).
            float g = w.SupportBelow(Feet.X, Feet.Y, Feet.Z + Stage3DWorld.StepUp, out int gid);
            if (!float.IsNaN(g) && g >= Feet.Z - 0.05f)
            {
                Feet.Z = g;
                SupportId = gid;
                State = Stage3DMotorState.Walking;
            }

            return;
        }

        var np = w.SurfacePoint(nu, nv);
        if (!Stage3DWorld.Finite(np))
            return;
        float horiz = new Vector2(np.X - Feet.X, np.Y - Feet.Y).Length();
        if (np.Z - Feet.Z > Stage3DWorld.MaxSlope * horiz + 1e-3f)
            return; // too steep
        if (w.BlockedAt(np.X, np.Y, np.Z))
            return;
        SurfU = nu;
        SurfV = nv;
        Feet = np;
    }
}

/// <summary>Is a stage solvable with these settings? Graph search over the same rules the motor uses.</summary>
public static class Stage3DSolver
{
    public sealed class Result
    {
        public bool Solved;
        public bool[] PickupReachable;
        public List<Vector3> Path = new List<Vector3>(); // feet positions from start to goal
        public string Reason = "";
    }

    // Slightly stricter than the motor so a solver path always replays.
    const float SlopeMargin = 0.95f;
    const float StepMargin = 0.8f;

    public static Result Solve(Stage3D stage, string formula, float[] prmSlots)
    {
        if (!stage.TryBuildSpec(formula, out var spec, out string err))
            return new Result { Reason = err, PickupReachable = new bool[stage.Pickups.Length] };
        var world = new Stage3DWorld(stage, spec, prmSlots);
        switch (stage.Mode)
        {
            case Stage3DMode.Rail: return SolveRail(world);
            case Stage3DMode.ParamSurface: return SolveParam(world);
            default: return SolveSurface(world);
        }
    }

    static Result SolveSurface(Stage3DWorld w)
    {
        var st = w.Stage;
        const float cell = 0.1f;
        int n = (int)Math.Ceiling(st.Half * 2f / cell) + 1;
        var top = new float[n * n];
        var sid = new int[n * n];
        float X(int i) => -st.Half + i * cell;
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            top[j * n + i] = w.SupportBelow(X(i), X(j), float.PositiveInfinity, out int id);
            sid[j * n + i] = id;
        }

        bool Standable(int k) => !float.IsNaN(top[k]) && top[k] > st.Water;
        int Idx(float x, float y) =>
            Math.Max(0, Math.Min(n - 1, (int)Math.Round((y + st.Half) / cell))) * n +
            Math.Max(0, Math.Min(n - 1, (int)Math.Round((x + st.Half) / cell)));

        var res = new Result { PickupReachable = new bool[st.Pickups.Length] };
        int start = Idx(st.Start.X, st.Start.Y);
        if (!Standable(start))
        {
            res.Reason = "The start is under water";
            return res;
        }

        var prev = new int[n * n];
        for (int k = 0; k < prev.Length; k++) prev[k] = -2;
        var q = new Queue<int>();
        q.Enqueue(start);
        prev[start] = -1;
        int goal = -1;
        while (q.Count > 0)
        {
            int k = q.Dequeue();
            int i = k % n, j = k / n;
            float h = top[k];
            var feet = new Vector3(X(i), X(j), h);
            MarkPickups(st, feet, res.PickupReachable);
            if (goal < 0 && w.AtGoal(feet) && Math.Abs(h - st.Goal.Z) <= Stage3DWorld.GoalTolerance * 0.8f)
                goal = k;
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                if (di == 0 && dj == 0) continue;
                int ni = i + di, nj = j + dj;
                if (ni < 0 || nj < 0 || ni >= n || nj >= n) continue;
                int nk = nj * n + ni;
                if (prev[nk] != -2 || !Standable(nk)) continue;
                float dist = cell * (float)Math.Sqrt(di * di + dj * dj);
                float rise = top[nk] - h;
                float allowed = Stage3DWorld.MaxSlope * SlopeMargin * dist +
                                (sid[nk] != sid[k] ? Stage3DWorld.StepUp * StepMargin : 0f);
                if (rise > allowed) continue;
                if (w.BlockedAt(X(ni), X(nj), h)) continue;
                prev[nk] = k;
                q.Enqueue(nk);
            }
        }

        if (goal < 0)
        {
            res.Reason = "No walkable path reaches the goal";
            return res;
        }

        res.Solved = true;
        for (int k = goal; k >= 0; k = prev[k])
            res.Path.Add(new Vector3(X(k % n), X(k / n), top[k]));
        res.Path.Reverse();
        return res;
    }

    static void MarkPickups(Stage3D st, Vector3 feet, bool[] reach)
    {
        for (int p = 0; p < st.Pickups.Length; p++)
        {
            if (reach[p]) continue;
            var c = feet + new Vector3(0f, 0f, Stage3DWorld.CenterHeight);
            if ((st.Pickups[p] - c).Length() <= Stage3DWorld.PickupRadius * 0.9f)
                reach[p] = true;
        }
    }

    static Result SolveRail(Stage3DWorld w)
    {
        var st = w.Stage;
        var res = new Result { PickupReachable = new bool[st.Pickups.Length] };
        float t0 = st.Range0[0], t1 = st.Range0[1];
        var p0 = w.RailPoint(t0);
        if (!Stage3DWorld.Finite(p0) || (p0 - st.Start).Length() > Stage3DWorld.AttachDist * 0.9f)
        {
            res.Reason = "The rail does not start at the start pad";
            return res;
        }

        const int samples = 2000;
        Vector3 prev = p0;
        for (int k = 0; k <= samples; k++)
        {
            float t = t0 + (t1 - t0) * k / samples;
            var p = w.RailPoint(t);
            if (!Stage3DWorld.Finite(p))
            {
                res.Reason = "The rail is undefined somewhere";
                return res;
            }

            if ((p - prev).Length() > 0.5f)
            {
                res.Reason = "The rail jumps";
                return res;
            }

            if (p.Z <= st.Water)
            {
                res.Reason = "The rail dips into the water";
                return res;
            }

            if (w.InsideBlock(p + new Vector3(0, 0, Stage3DWorld.CenterHeight), 0.3f))
            {
                res.Reason = "The rail runs into a wall";
                return res;
            }

            MarkPickups(st, p, res.PickupReachable);
            if (k % 20 == 0)
                res.Path.Add(p);
            prev = p;
        }

        var end = w.RailPoint(t1);
        if (!w.OnPad(st.Goal, end.X, end.Y) || Math.Abs(end.Z - st.Goal.Z) > Stage3DWorld.AttachDist * 0.9f)
        {
            res.Reason = "The rail does not end at the goal";
            return res;
        }

        res.Solved = true;
        return res;
    }

    static Result SolveParam(Stage3DWorld w)
    {
        var st = w.Stage;
        var res = new Result { PickupReachable = new bool[st.Pickups.Length] };
        const int nu = 40, nv = 160;
        var pts = new Vector3[(nu + 1) * (nv + 1)];
        var ok = new bool[pts.Length];
        for (int j = 0; j <= nv; j++)
        for (int i = 0; i <= nu; i++)
        {
            var p = w.SurfacePoint(w.U(i / (float)nu), w.V(j / (float)nv));
            int k = j * (nu + 1) + i;
            pts[k] = p;
            // Keep one cell off the u edges so the path never grazes a drop.
            ok[k] = i > 0 && i < nu && Stage3DWorld.Finite(p) && p.Z > st.Water && !w.BlockedAt(p.X, p.Y, p.Z);
        }

        float PadDist(Vector3 pad, Vector3 p)
        {
            float dx = Math.Max(0f, Math.Abs(p.X - pad.X) - st.PadHalf);
            float dy = Math.Max(0f, Math.Abs(p.Y - pad.Y) - st.PadHalf);
            return (float)Math.Sqrt(dx * dx + dy * dy + (p.Z - pad.Z) * (p.Z - pad.Z));
        }

        var prev = new int[pts.Length];
        for (int k = 0; k < prev.Length; k++) prev[k] = -2;
        var q = new Queue<int>();
        for (int k = 0; k < pts.Length; k++)
        {
            if (ok[k] && PadDist(st.Start, pts[k]) <= 0.3f)
            {
                prev[k] = -1;
                q.Enqueue(k);
            }
        }

        if (q.Count == 0)
        {
            res.Reason = "The surface does not touch the start pad";
            return res;
        }

        int goal = -1;
        while (q.Count > 0)
        {
            int k = q.Dequeue();
            int i = k % (nu + 1), j = k / (nu + 1);
            MarkPickups(st, pts[k], res.PickupReachable);
            if (goal < 0 && PadDist(st.Goal, pts[k]) <= 0.12f)
                goal = k;
            for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                if (di == 0 && dj == 0) continue;
                int ni = i + di, nj = j + dj;
                if (ni < 0 || nj < 0 || ni > nu || nj > nv) continue;
                int nk = nj * (nu + 1) + ni;
                if (prev[nk] != -2 || !ok[nk]) continue;
                var a = pts[k];
                var b = pts[nk];
                float horiz = new Vector2(b.X - a.X, b.Y - a.Y).Length();
                if (b.Z - a.Z > Stage3DWorld.MaxSlope * SlopeMargin * horiz) continue;
                prev[nk] = k;
                q.Enqueue(nk);
            }
        }

        if (goal < 0)
        {
            res.Reason = "The ramp does not reach the goal";
            return res;
        }

        res.Solved = true;
        for (int k = goal; k >= 0; k = prev[k])
            res.Path.Add(pts[k]);
        res.Path.Reverse();
        return res;
    }

    /// <summary>
    /// Replays a solver path with the real motor (autopilot steering toward each waypoint, then on to
    /// the goal pad centre). Returns the motor at the end so tests can check it won.
    /// </summary>
    public static Stage3DMotor Replay(Stage3D stage, string formula, float[] prmSlots, Result solved, float maxSeconds = 90f)
    {
        stage.TryBuildSpec(formula, out var spec, out _);
        var world = new Stage3DWorld(stage, spec, prmSlots);
        var m = new Stage3DMotor(world);
        const float dt = 1f / 60f;
        var way = new List<Vector2>();
        foreach (var p in solved.Path)
            way.Add(new Vector2(p.X, p.Y));
        way.Add(new Vector2(stage.Goal.X, stage.Goal.Y));
        int wi = 0;
        for (float t = 0f; t < maxSeconds && !m.Finished; t += dt)
        {
            if (m.State == Stage3DMotorState.Rail)
            {
                m.Step(dt, Vector2.Zero, false, 1f);
                continue;
            }

            var pos = new Vector2(m.Feet.X, m.Feet.Y);
            while (wi < way.Count - 1 && (way[wi] - pos).Length() < 0.12f)
                wi++;
            // Look ahead: skip waypoints already behind us (path is on a 0.1 grid).
            for (int k = Math.Min(way.Count - 1, wi + 6); k > wi && stage.Mode == Stage3DMode.Surface; k--)
            {
                if ((way[k] - pos).Length() < 0.12f * (k - wi + 1))
                {
                    wi = k;
                    break;
                }
            }

            var dir = way[wi] - pos;
            var move = dir.Length() > 1e-4f ? Vector2.Normalize(dir) : Vector2.Zero;
            if (dir.Length() < 0.05f)
                move *= dir.Length() / 0.05f;
            m.Step(dt, move, false, 0f);
        }

        return m;
    }
}
