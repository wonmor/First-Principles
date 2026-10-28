using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// EditMode level audit: rebuilds every curriculum level with the shipping pipeline (GraphFunctionMath,
/// GraphViewMapping, GraphWorldBuilder, CourseLayout) and runs a reachability search driven by the real
/// player physics (<see cref="PlayerMotor"/>), so jump height, coyote time, the f′ air jump, springs, the
/// apex hang, bounces and the play-bounds clamp all match the game.
/// </summary>
public static class LevelAudit
{
    /// <summary>Gameplay grid of Game.unity (GridRendererUI.gridSize).</summary>
    public static readonly Vector2Int GameGrid = new Vector2Int(26, 14);

    public struct Profile
    {
        public string Name;
        public float PlaneWidth, PlaneHeight;
        public bool Tablet, Touch;

        public GameplayPlayBounds Bounds(Vector2Int grid) =>
            GameplayPlayBounds.Compute(PlaneWidth, PlaneHeight, grid, Tablet, Touch, Touch, Tablet ? 188f : 168f);
    }

    /// <summary>Desktop at the authored 1820 × 980 plane, a large phone, a small phone (tightest insets) and a tablet.</summary>
    public static readonly Profile[] Profiles =
    {
        new Profile { Name = "desktop", PlaneWidth = 1820f, PlaneHeight = 980f },
        new Profile { Name = "phone", PlaneWidth = 1300f, PlaneHeight = 700f, Touch = true },
        new Profile { Name = "small-phone", PlaneWidth = 900f, PlaneHeight = 485f, Touch = true },
        new Profile { Name = "tablet", PlaneWidth = 1600f, PlaneHeight = 862f, Tablet = true, Touch = true },
    };

    // ------------------------------------------------------------------ levels

    /// <summary>The 59 curriculum levels exactly as LevelManager builds them (private factory, via reflection).</summary>
    public static List<LevelDefinition> LoadCurriculum()
    {
        var go = new GameObject("LevelAudit_LevelManager") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var lm = go.AddComponent<LevelManager>();
            typeof(LevelManager).GetMethod("BuildSampleLevels", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(lm, null);
            var list = (List<LevelDefinition>)typeof(LevelManager)
                .GetField("levels", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(lm);
            return new List<LevelDefinition>(list);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    public sealed class LevelBuild
    {
        public int Index;
        public LevelDefinition Def;
        public Profile Profile;
        public GraphFunctionParams Params;
        public GraphViewMapping Map;
        public GraphWorld World;
        public List<GraphWorldPiece> Pieces;
        public CourseLayout.Layout Course;
        public readonly List<Vector2> Curve = new List<Vector2>();
        public readonly List<Vector2> Deriv = new List<Vector2>();
    }

    public static LevelBuild Build(LevelDefinition def, int index, Profile profile)
    {
        var b = new LevelBuild { Index = index, Def = def, Profile = profile };
        b.Params = GraphFunctionParams.FromLevel(def);
        float dStep = FunctionPlotter.TableCurveDerivativeStepFor(def.step);
        b.Map = GraphViewMapping.Fit(b.Params, def.xStart, def.xEnd, def.step, GameGrid, GraphViewMapping.FitSettings.ForLevel(def), dStep);
        GraphPolylines.Sample(b.Params, b.Map, def.xStart, def.xEnd, def.step, dStep, b.Curve, b.Deriv);
        var built = GraphWorldBuilder.Build(def, b.Params, b.Map, dStep, profile.Bounds(GameGrid));
        b.World = built.World;
        b.Pieces = built.Pieces;
        b.Course = CourseLayout.Compute(b.World, index);
        return b;
    }

    // ------------------------------------------------------------------ checks

    public sealed class LevelReport
    {
        public int Index;
        public string Name;
        public string Profile;
        public readonly List<string> Problems = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public int StandStates;
        public bool Ok => Problems.Count == 0;
    }

    public static LevelReport Check(LevelBuild b)
    {
        var r = new LevelReport { Index = b.Index, Name = b.Def.levelName, Profile = b.Profile.Name };
        var w = b.World;
        var t = PlayerMotorTuning.Default;
        Vector2 half = t.HalfSize;
        var bounds = w.playBounds;

        if (w.platforms.Count == 0)
        {
            r.Problems.Add("no platforms");
            return r;
        }

        // --- Geometry sanity --------------------------------------------------------------
        foreach (var p in w.platforms)
            if (p.yMax + t.Height > bounds.YMax + 1e-3f)
            {
                r.Problems.Add($"platform x {p.xMin:0.0}-{p.xMax:0.0} top {p.yMax:0.00} is within a player height of the play-area top {bounds.YMax:0.00} (cannot stand on it)");
                break;
            }

        int ghost = 0;
        foreach (var piece in b.Pieces)
            if (piece.IsHazard && !w.hazards.Contains(piece.Rect))
                ghost++;
        if (ghost > 0)
            r.Problems.Add($"{ghost} hazard block(s) drawn but not lethal (bridge pass removed the collider, not the visual)");

        // --- Spawn ----------------------------------------------------------------------------
        Vector2 spawn = new Vector2(w.spawnXGrid, w.spawnYTopGrid + half.y + 0.01f);
        bool spawnSupported = false;
        foreach (var p in w.platforms)
            if (spawn.x + half.x > p.xMin && spawn.x - half.x < p.xMax && Mathf.Abs(p.yMax - w.spawnYTopGrid) < 0.05f)
                spawnSupported = true;
        if (!spawnSupported)
            r.Problems.Add($"spawn ({w.spawnXGrid:0.00}, top {w.spawnYTopGrid:0.00}) is not on a platform top");
        if (PlayerMotor.OverlapsAny(w.hazards, spawn, half))
            r.Problems.Add("spawn overlaps a hazard");
        if (spawn.x - half.x < bounds.XMin - 1e-3f || spawn.x + half.x > bounds.XMax + 1e-3f || spawn.y + half.y > bounds.YMax + 1e-3f)
            r.Problems.Add("spawn is outside the play area");
        if (w.spawnXGrid >= w.finish.xMin)
            r.Problems.Add("spawn is inside the exit band");

        // --- Course extras placement ----------------------------------------------------------
        foreach (var tk in b.Course.Tokens)
        {
            float hs = CourseLayout.TokenSize * 0.5f;
            if (tk.Pos.x - hs < bounds.XMin || tk.Pos.x + hs > bounds.XMax || tk.Pos.y - hs < bounds.YMin || tk.Pos.y + hs > bounds.YMax)
                r.Problems.Add($"token {tk.Order} at ({tk.Pos.x:0.0}, {tk.Pos.y:0.0}) is outside the play area (under the HUD / off the plane)");
            foreach (var p in w.platforms)
                if (tk.Pos.x > p.xMin && tk.Pos.x < p.xMax && tk.Pos.y > p.yMin && tk.Pos.y < p.yMax)
                    r.Problems.Add($"token {tk.Order} is inside a platform");
        }

        // --- Reachability ---------------------------------------------------------------------
        var reach = LevelReachability.Explore(w, b.Course, b.Deriv, t);
        r.StandStates = reach.StandStates;
        if (!reach.SpawnSettled)
            r.Problems.Add($"player does not come to rest at spawn ({reach.SpawnNote})");
        if (!reach.FinishReached)
            r.Problems.Add($"exit unreachable (furthest x {reach.MaxX:0.0} of {w.finish.xMin:0.0})");
        if (b.Course.HasCheckpoint && !reach.CheckpointReached)
            r.Problems.Add($"checkpoint at x {b.Course.Checkpoint.x:0.0} unreachable");
        for (int i = 0; i < b.Course.Tokens.Count; i++)
            if (!reach.TokenReached[i])
            {
                var tk = b.Course.Tokens[i];
                r.Problems.Add($"{(tk.High ? "high" : "low")} token {tk.Order} at ({tk.Pos.x:0.0}, {tk.Pos.y:0.0}) unreachable");
            }

        // --- Notes (informational) -------------------------------------------------------------
        float visible = VisibleFraction(b.Deriv, b.Map, b.Def);
        if (visible < 0.5f)
            r.Notes.Add($"f′ visible on {visible:P0} of the domain");
        return r;
    }

    /// <summary>Fraction of [xStart, xEnd] (in grid columns) covered by drawable f′ segments.</summary>
    public static float VisibleFraction(List<Vector2> poly, GraphViewMapping map, LevelDefinition def)
    {
        float span = Mathf.Abs(map.GridX(def.xEnd) - map.GridX(def.xStart));
        if (span < 1e-4f)
            return 0f;
        float covered = 0f;
        for (int i = 0; i + 1 < poly.Count; i++)
            if (!PolylineClip.IsBreak(poly[i]) && !PolylineClip.IsBreak(poly[i + 1]))
                covered += Mathf.Abs(poly[i + 1].x - poly[i].x);
        return Mathf.Clamp01(covered / span);
    }

    /// <summary>Audits every level on every profile; returns the reports and a printable summary.</summary>
    public static List<LevelReport> AuditAll(out string summary, out int failingLevels)
    {
        var levels = LoadCurriculum();
        var reports = new List<LevelReport>();
        var failing = new SortedSet<int>();
        var sb = new StringBuilder();
        // Pure math only (no Unity objects), so the level x layout grid runs in parallel.
        LorenzAttractorSamples.SampleNormalizedX(0f); // build the lazy table before worker threads read it
        var slots = new LevelReport[levels.Count * Profiles.Length];
        System.Threading.Tasks.Parallel.For(0, slots.Length, k =>
            slots[k] = Check(Build(levels[k / Profiles.Length], k / Profiles.Length, Profiles[k % Profiles.Length])));
        foreach (var rep in slots)
        {
            reports.Add(rep);
            if (!rep.Ok)
                failing.Add(rep.Index);
        }

        sb.AppendLine($"Level reachability audit: {levels.Count} levels x {Profiles.Length} layouts, {failing.Count} level(s) failing.");
        for (int i = 0; i < levels.Count; i++)
        {
            var mine = reports.FindAll(x => x.Index == i);
            bool ok = mine.TrueForAll(x => x.Ok);
            sb.Append(ok ? "  PASS " : "  FAIL ").Append(i.ToString("00")).Append(' ').Append(levels[i].levelName);
            sb.Append("  (states ").Append(mine[0].StandStates).Append(')').AppendLine();
            var seen = new HashSet<string>();
            foreach (var rep in mine)
            {
                foreach (var pr in rep.Problems)
                    if (seen.Add(pr))
                        sb.Append("       [").Append(rep.Profile).Append("] ").AppendLine(pr);
                foreach (var n in rep.Notes)
                    if (seen.Add(n))
                        sb.Append("       note: ").AppendLine(n);
            }
        }
        summary = sb.ToString();
        failingLevels = failing.Count;
        return reports;
    }
}
