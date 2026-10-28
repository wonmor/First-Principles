using System.Collections.Generic;
using UnityEngine;

/// <summary>Movement constants in grid units (defaults = shipped feel). <see cref="PlayerControllerUI2D"/> fills it from its inspector fields.</summary>
public struct PlayerMotorTuning
{
    public float MoveSpeed, Gravity, JumpVelocity;
    public float GroundAccel, GroundDecel, AirAccel;
    public float CoyoteSeconds, JumpBufferSeconds, JumpCutMultiplier;
    public float ApexGravityMultiplier, ApexSpeedBand, MaxFallSpeed;
    public float DoubleJumpVelocityMultiplier, DerivativeTouchRadius;
    public float Width, Height;
    public float GroundBounciness, BounceVelocityCutoff;
    public float StrongFirstJumpMultiplier;

    public static PlayerMotorTuning Default => new PlayerMotorTuning
    {
        MoveSpeed = 7f,
        Gravity = 15.5f,
        JumpVelocity = 11.2f,
        GroundAccel = 80f,
        GroundDecel = 95f,
        AirAccel = 60f,
        CoyoteSeconds = 0.1f,
        JumpBufferSeconds = 0.13f,
        JumpCutMultiplier = 0.6f,
        ApexGravityMultiplier = 0.7f,
        ApexSpeedBand = 1.6f,
        MaxFallSpeed = 24f,
        DoubleJumpVelocityMultiplier = 0.9f,
        DerivativeTouchRadius = 0.58f,
        Width = 0.78f,
        Height = 1.12f,
        GroundBounciness = 0.42f,
        BounceVelocityCutoff = 2.2f,
        StrongFirstJumpMultiplier = 1.12f,
    };

    public Vector2 HalfSize => new Vector2(Width * 0.5f, Height * 0.5f);
}

public struct PlayerMotorState
{
    public Vector2 Pos, Vel;
    public bool Grounded;
    public float CoyoteTimer, JumpBufferTimer;
    public bool JumpCutAvailable;
    public bool AirJumpConsumedThisBand;
    public bool StrongFirstJumpPending;
    /// <summary>Spring multiplier for the next ground jump (reset to 1 every step).</summary>
    public float GroundJumpBoost;
    /// <summary>A touch-screen jump tap counts as held until landing (taps have no duration: full arc).</summary>
    public bool TouchJumpHeld;
}

public struct PlayerMotorInput
{
    public float MoveX;
    /// <summary>Keyboard / gamepad jump went down this frame.</summary>
    public bool KeyJumpPressed;
    /// <summary>On-screen jump tap consumed this frame (only read when <see cref="KeyJumpPressed"/> is false).</summary>
    public bool TouchJumpPressed;
    /// <summary>Keyboard / gamepad jump held (always true on touch devices: full arc).</summary>
    public bool JumpHeld;
    /// <summary>Player centre is inside the f′ band this frame.</summary>
    public bool TouchingDerivative;
}

[System.Flags]
public enum PlayerMotorEvents
{
    None = 0,
    GroundJump = 1,
    AirJump = 2,
    Landed = 4,
    Died = 8,
    Finished = 16,
}

public struct PlayerMotorStep
{
    public PlayerMotorEvents Events;
    public float LandingImpact;
    public float GroundJumpBoostUsed;

    public bool Has(PlayerMotorEvents e) => (Events & e) != 0;
}

/// <summary>
/// One fixed-order physics step of the graph platformer (grid units): run, coyote / buffered jump, f′ air jump,
/// variable jump cut, apex hang, AABB vs slabs, hazards, fall death, finish, play-bounds clamp.
/// Engine-free and shared by <see cref="PlayerControllerUI2D"/> and the EditMode reachability audit, so the
/// audit's jump arcs are the game's jump arcs.
/// </summary>
public static class PlayerMotor
{
    public static PlayerMotorState Spawn(GraphWorld world, in PlayerMotorTuning t, bool strongFirstJump)
    {
        return new PlayerMotorState
        {
            Pos = new Vector2(world.spawnXGrid, world.spawnYTopGrid + t.Height * 0.5f + 0.01f),
            Vel = Vector2.zero,
            StrongFirstJumpPending = strongFirstJump,
            GroundJumpBoost = 1f,
        };
    }

    public static PlayerMotorStep Step(ref PlayerMotorState s, in PlayerMotorInput input, in PlayerMotorTuning t,
        GraphWorld world, float deathMinY, float dt)
    {
        var result = new PlayerMotorStep();

        float target = input.MoveX * t.MoveSpeed;
        float rate = !s.Grounded ? t.AirAccel
            : Mathf.Abs(target) < 0.01f ? t.GroundDecel
            : Mathf.Sign(target) != Mathf.Sign(s.Vel.x) && Mathf.Abs(s.Vel.x) > 0.1f ? t.GroundDecel * 1.4f
            : t.GroundAccel;
        s.Vel.x = Mathf.MoveTowards(s.Vel.x, target, rate * dt);

        if (s.Grounded)
        {
            s.AirJumpConsumedThisBand = false;
            s.CoyoteTimer = t.CoyoteSeconds;
        }
        else
            s.CoyoteTimer -= dt;

        // Leave f′ → can earn another air jump on the next approach while aloft.
        if (!input.TouchingDerivative)
            s.AirJumpConsumedThisBand = false;

        bool jumpPressed = input.KeyJumpPressed;
        if (!jumpPressed)
        {
            jumpPressed = input.TouchJumpPressed;
            if (jumpPressed)
                s.TouchJumpHeld = true;
        }
        else
            s.TouchJumpHeld = false;

        if (jumpPressed)
            s.JumpBufferTimer = t.JumpBufferSeconds;
        else
            s.JumpBufferTimer -= dt;

        if (s.JumpBufferTimer > 0f)
        {
            if (s.Grounded || s.CoyoteTimer > 0f)
            {
                float jv = t.JumpVelocity * s.GroundJumpBoost;
                if (s.StrongFirstJumpPending)
                {
                    jv *= t.StrongFirstJumpMultiplier;
                    s.StrongFirstJumpPending = false;
                }
                s.Vel.y = jv;
                s.Grounded = false;
                s.CoyoteTimer = 0f;
                s.JumpBufferTimer = 0f;
                s.JumpCutAvailable = true;
                result.Events |= PlayerMotorEvents.GroundJump;
                result.GroundJumpBoostUsed = s.GroundJumpBoost;
            }
            else if (jumpPressed && input.TouchingDerivative && !s.AirJumpConsumedThisBand)
            {
                s.Vel.y = t.JumpVelocity * t.DoubleJumpVelocityMultiplier;
                s.AirJumpConsumedThisBand = true;
                s.JumpBufferTimer = 0f;
                s.JumpCutAvailable = true;
                result.Events |= PlayerMotorEvents.AirJump;
            }
        }
        s.GroundJumpBoost = 1f;

        // Variable jump height: let go early for a short hop (holding keeps the full tuned arc).
        if (s.Grounded && s.Vel.y <= 0f)
            s.TouchJumpHeld = false;
        bool held = s.TouchJumpHeld || input.JumpHeld;
        if (s.JumpCutAvailable && !held && s.Vel.y > 0f)
        {
            s.Vel.y *= t.JumpCutMultiplier;
            s.JumpCutAvailable = false;
        }
        if (s.Vel.y <= 0f)
            s.JumpCutAvailable = false;

        // Gravity (a little hang time at the apex while jump is held).
        float g = t.Gravity;
        if (held && Mathf.Abs(s.Vel.y) < t.ApexSpeedBand)
            g *= t.ApexGravityMultiplier;
        s.Vel.y -= g * dt;
        if (s.Vel.y < -t.MaxFallSpeed)
            s.Vel.y = -t.MaxFallSpeed;

        Vector2 next = s.Pos;
        next.x = s.Pos.x + s.Vel.x * dt;
        ResolveHorizontal(world.platforms, t, s.Vel.x, ref next);

        // Vertical sweep uses Y from before this step but X after the horizontal move.
        Vector2 prevForVertical = new Vector2(next.x, s.Pos.y);
        next.y = s.Pos.y + s.Vel.y * dt;
        ResolveVertical(world.platforms, t, prevForVertical, ref next, ref s, ref result);

        if (OverlapsAny(world.hazards, next, t.HalfSize) || next.y - t.Height * 0.5f < deathMinY)
            result.Events |= PlayerMotorEvents.Died;

        // Finish uses centre x only (the goal is to reach the end).
        if (next.x >= world.finish.xMin && next.x <= world.finish.xMax)
        {
            result.Events |= PlayerMotorEvents.Finished;
            return result;
        }

        s.Pos = next;
        s.Pos = ClampToPlayBounds(world, t, s.Pos);
        return result;
    }

    public static Vector2 ClampToPlayBounds(GraphWorld world, in PlayerMotorTuning t, Vector2 pos)
    {
        if (world == null || !world.hasPlayBounds)
            return pos;
        var b = world.playBounds;
        pos.x = Mathf.Clamp(pos.x, b.XMin + t.Width * 0.5f, b.XMax - t.Width * 0.5f);
        pos.y = Mathf.Clamp(pos.y, b.YMin + t.Height * 0.5f, b.YMax - t.Height * 0.5f);
        return pos;
    }

    /// <summary>f′ proximity: centre within <see cref="PlayerMotorTuning.DerivativeTouchRadius"/> of the drawn polyline (NaN breaks skipped).</summary>
    public static bool IsTouchingPolyline(Vector2 p, List<Vector2> pts, float radius)
    {
        if (pts == null || pts.Count < 2 || radius <= 0.01f)
            return false;
        float r2 = radius * radius;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            if (PolylineClip.IsBreak(a) || PolylineClip.IsBreak(b))
                continue;
            Vector2 ab = b - a;
            float den = ab.sqrMagnitude;
            float tt = den < 1e-10f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / den);
            if ((p - (a + tt * ab)).sqrMagnitude <= r2)
                return true;
        }
        return false;
    }

    static void ResolveHorizontal(List<GridRect> platforms, in PlayerMotorTuning t, float velX, ref Vector2 pos)
    {
        float halfW = t.Width * 0.5f, halfH = t.Height * 0.5f;
        float pyMin = pos.y - halfH, pyMax = pos.y + halfH;
        float pxMin = pos.x - halfW, pxMax = pos.x + halfW;
        foreach (var p in platforms)
        {
            if (!(pyMin < p.yMax && pyMax > p.yMin))
                continue;
            if (!(pxMin < p.xMax && pxMax > p.xMin))
                continue;
            if (velX > 0f)
                pos.x = p.xMin - halfW;
            else if (velX < 0f)
                pos.x = p.xMax + halfW;
        }
    }

    /// <summary>Land on slab tops when falling; bonk on true ceilings when rising (not on the lip of the next stair tread).</summary>
    static void ResolveVertical(List<GridRect> platforms, in PlayerMotorTuning t, Vector2 prevPos, ref Vector2 pos,
        ref PlayerMotorState s, ref PlayerMotorStep result)
    {
        bool grounded = false;
        float halfW = t.Width * 0.5f, halfH = t.Height * 0.5f;
        float prevBottom = prevPos.y - halfH, prevTop = prevPos.y + halfH;
        float nextBottom = pos.y - halfH, nextTop = pos.y + halfH;
        float pxMin = pos.x - halfW, pxMax = pos.x + halfW;

        const float eps = 0.001f;
        const float maxLedgeSlabThicknessGrid = 0.95f;
        const float maxLedgeSlabWidthGrid = 1.28f;
        const float feetBelowStepTopClearanceGrid = 0.1f;

        foreach (var p in platforms)
        {
            if (!(pxMin < p.xMax && pxMax > p.xMin))
                continue;

            if (s.Vel.y <= 0f)
            {
                if (prevBottom >= p.yMax - eps && nextBottom <= p.yMax + eps)
                {
                    pos.y = p.yMax + halfH;
                    float impact = -s.Vel.y;
                    s.CoyoteTimer = t.CoyoteSeconds; // touching ground (even mid-bounce) counts for jumping
                    result.Events |= PlayerMotorEvents.Landed;
                    result.LandingImpact = impact;
                    if (impact > t.BounceVelocityCutoff && t.GroundBounciness > 0f)
                    {
                        s.Vel.y = impact * t.GroundBounciness;
                        grounded = false;
                    }
                    else
                    {
                        s.Vel.y = 0f;
                        grounded = true;
                    }
                }
            }
            else
            {
                float platW = p.xMax - p.xMin, platH = p.yMax - p.yMin;
                bool thinStairSlab = platW <= maxLedgeSlabWidthGrid && platH <= maxLedgeSlabThicknessGrid;
                if (thinStairSlab && nextBottom < p.yMax - feetBelowStepTopClearanceGrid)
                    continue;
                if (prevTop <= p.yMin + eps && nextTop >= p.yMin - eps)
                {
                    pos.y = p.yMin - halfH;
                    s.Vel.y = 0f;
                }
            }
        }
        s.Grounded = grounded;
    }

    public static bool OverlapsAny(List<GridRect> rects, Vector2 c, Vector2 half)
    {
        for (int i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            if (c.x - half.x < r.xMax && c.x + half.x > r.xMin && c.y - half.y < r.yMax && c.y + half.y > r.yMin)
                return true;
        }
        return false;
    }
}
