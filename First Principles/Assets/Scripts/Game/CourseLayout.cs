using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Engine-free placement of the obstacle-course layer (∂ tokens, springs, sentries, checkpoint) on a built
/// <see cref="GraphWorld"/>. <see cref="CourseExtras"/> draws and runs exactly this layout; the EditMode
/// reachability audit checks it. Deterministic per level, so restarts and the daily challenge are fair.
/// </summary>
public static class CourseLayout
{
    public const float TokenSize = 0.95f;
    public const float SentrySize = 0.72f;
    public const float SpringBoost = 1.32f;
    /// <summary>Token pickup: player AABB half-size plus this much of the token (per axis).</summary>
    public const float TokenPickupReach = TokenSize * 0.4f;
    /// <summary>Checkpoint fires once the grounded player's centre passes flag x minus this.</summary>
    public const float CheckpointTriggerLead = 0.2f;

    public struct TokenSpot
    {
        public Vector2 Pos;
        public bool High;
        public int Order;
        /// <summary>Index of the take-off platform (left side of the gap) in <see cref="Layout.SortedPlatforms"/>.</summary>
        public int GapIndex;
    }

    public struct SpringSpot
    {
        /// <summary>Zero-height pad on the take-off slab top (x range = coil).</summary>
        public GridRect Pad;
        /// <summary>Coil visual (sits in the slab and pokes above it).</summary>
        public GridRect Visual;
        public int PlatformIndex;
    }

    public struct SentrySpot
    {
        public Vector2 Center;
        public float Amp;
        public float Speed;
        public float Phase;
    }

    public sealed class Layout
    {
        public readonly List<GridRect> SortedPlatforms = new List<GridRect>();
        public readonly List<TokenSpot> Tokens = new List<TokenSpot>();
        public readonly List<SpringSpot> Springs = new List<SpringSpot>();
        public readonly List<SentrySpot> Sentries = new List<SentrySpot>();
        public bool HasCheckpoint;
        /// <summary>x = flag centre, y = platform top.</summary>
        public Vector2 Checkpoint;
        public GridRect CheckpointPlatform;
    }

    public static Layout Compute(GraphWorld world, int levelIndex)
    {
        var L = new Layout();
        if (world == null || world.platforms.Count < 3)
            return L;

        var plats = L.SortedPlatforms;
        plats.AddRange(world.platforms);
        plats.Sort((a, b) => a.xMin.CompareTo(b.xMin));
        float yTopLimit = world.hasPlayBounds ? world.playBounds.YMax - 0.7f : world.gridSize.y - 0.7f;
        float xMinLimit = world.spawnXGrid + 1.5f;
        float xMaxLimit = world.finish.xMin - 0.8f;

        // Gaps (pits) and big steps along the main line.
        var gaps = new List<int>();
        var springAt = new List<int>();
        for (int i = 0; i + 1 < plats.Count; i++)
        {
            var a = plats[i];
            var b = plats[i + 1];
            float gapW = b.xMin - a.xMax;
            float step = b.yMax - a.yMax;
            if (a.xMax < xMinLimit || b.xMin > xMaxLimit)
                continue;
            if (gapW > 0.5f)
                gaps.Add(i);
            bool big = gapW > 3.2f || step > 2.4f;
            if (levelIndex >= 1 && big && springAt.Count < 3 && (springAt.Count == 0 || i - springAt[springAt.Count - 1] > 3))
                springAt.Add(i);
        }

        foreach (int i in springAt)
        {
            var a = plats[i];
            float w = Mathf.Min(1f, a.xMax - a.xMin);
            L.Springs.Add(new SpringSpot
            {
                Pad = new GridRect(a.xMax - w, a.xMax, a.yMax, a.yMax),
                Visual = new GridRect(a.xMax - w, a.xMax, a.yMax - 0.42f, a.yMax + 0.14f),
                PlatformIndex = i,
            });
        }

        // Tokens: evenly spread over the gaps; first one easy, the rest on the high route.
        int want = levelIndex == 0 ? 1 : Mathf.Clamp(2 + levelIndex / 12, 2, 5);
        want = Mathf.Min(want, gaps.Count);
        var used = new HashSet<int>();
        for (int k = 0; k < want; k++)
        {
            int gi = gaps[Mathf.Clamp(Mathf.RoundToInt((k + 0.5f) * gaps.Count / want - 0.5f), 0, gaps.Count - 1)];
            if (!used.Add(gi))
                continue;
            var a = plats[gi];
            var b = plats[gi + 1];
            float top = Mathf.Max(a.yMax, b.yMax);
            bool high = k > 0;
            float y = top + (high ? 5.2f : 2.4f);
            // Never above the play area (it used to be pushed to top + 2.2, i.e. under the HUD / off the plane).
            y = Mathf.Min(y, yTopLimit);
            var pos = new Vector2((a.xMax + b.xMin) * 0.5f, y);
            L.Tokens.Add(new TokenSpot { Pos = pos, High = high, Order = k, GapIndex = gi });
            // Sentries guard every other high token from the 5th level on (not over spring gaps).
            if (high && levelIndex >= 4 && k % 2 == 1 && !springAt.Contains(gi) && y + 1.3f < yTopLimit + 0.6f)
                L.Sentries.Add(new SentrySpot { Center = new Vector2(pos.x, y + 1.3f), Amp = 1.3f, Speed = 1.5f + 0.02f * levelIndex, Phase = k * 0.9f });
        }

        // Checkpoint halfway on longer levels.
        float span = xMaxLimit - world.spawnXGrid;
        if (span > 22f)
        {
            float targetX = world.spawnXGrid + span * 0.5f;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 1; i < plats.Count - 1; i++)
            {
                if (springAt.Contains(i)) continue;
                var p = plats[i];
                float cx = (p.xMin + p.xMax) * 0.5f;
                float d = Mathf.Abs(cx - targetX);
                if (d < bestD && p.yMax + 2f < yTopLimit + 0.7f)
                {
                    bestD = d;
                    best = i;
                }
            }
            if (best >= 0)
            {
                var p = plats[best];
                L.HasCheckpoint = true;
                L.Checkpoint = new Vector2((p.xMin + p.xMax) * 0.5f, p.yMax);
                L.CheckpointPlatform = p;
            }
        }

        return L;
    }

    /// <summary>True while a grounded player (centre <paramref name="p"/>, half-size <paramref name="half"/>) stands on the spring pad.</summary>
    public static bool IsOnSpring(in SpringSpot s, bool grounded, Vector2 p, Vector2 half) =>
        grounded && p.x + half.x * 0.6f > s.Pad.xMin && p.x - half.x * 0.6f < s.Pad.xMax
        && Mathf.Abs((p.y - half.y) - s.Pad.yMin) < 0.2f;

    public static bool TouchesToken(Vector2 tokenPos, Vector2 p, Vector2 half) =>
        Mathf.Abs(p.x - tokenPos.x) < half.x + TokenPickupReach && Mathf.Abs(p.y - tokenPos.y) < half.y + TokenPickupReach;
}
