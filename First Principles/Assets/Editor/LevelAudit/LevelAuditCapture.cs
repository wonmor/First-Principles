using System.IO;
using UnityEngine;

/// <summary>
/// Batch-mode PNGs of audited level layouts (the exact rects the game collides with), for visual spot checks:
///   Unity -batchmode -quit -projectPath "First Principles" -executeMethod LevelAuditCapture.Run -fpAuditLevels 2,15,28
/// Output: Logs/level-audit/level_NN.png. Paper background, blue f, red f′, ink platforms, red hazards,
/// yellow exit, red ∂ tokens, blue springs, ink flag, play-area outline.
/// </summary>
public static class LevelAuditCapture
{
    const int Px = 40; // pixels per grid cell

    public static void Run()
    {
        var args = System.Environment.GetCommandLineArgs();
        int ai = System.Array.IndexOf(args, "-fpAuditLevels");
        string list = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "0,1,2";
        var levels = LevelAudit.LoadCurriculum();
        string dir = Path.Combine(Application.dataPath, "..", "Logs", "level-audit");
        Directory.CreateDirectory(dir);
        foreach (var s in list.Split(','))
        {
            if (!int.TryParse(s.Trim(), out int idx) || idx < 0 || idx >= levels.Count)
                continue;
            var b = LevelAudit.Build(levels[idx], idx, LevelAudit.Profiles[0]);
            File.WriteAllBytes(Path.Combine(dir, $"level_{idx:00}.png"), Render(b).EncodeToPNG());
        }
        Debug.Log("LevelAuditCapture: wrote " + dir);
    }

    static readonly Color Paper = new Color(0.96f, 0.94f, 0.89f);
    static readonly Color Ink = new Color(0.08f, 0.08f, 0.08f);
    static readonly Color Rule = new Color(0.82f, 0.79f, 0.72f);
    static readonly Color Blue = new Color(0.12f, 0.31f, 0.63f);
    static readonly Color Red = new Color(0.84f, 0.15f, 0.12f);
    static readonly Color Yellow = new Color(0.96f, 0.76f, 0.1f);

    public static Texture2D Render(LevelAudit.LevelBuild b)
    {
        var g = LevelAudit.GameGrid;
        var tex = new Texture2D(g.x * Px, g.y * Px, TextureFormat.RGB24, false);
        var px = new Color[tex.width * tex.height];
        for (int i = 0; i < px.Length; i++) px[i] = Paper;

        for (int x = 0; x <= g.x; x++) FillRect(px, tex.width, tex.height, x, x, 0, g.y, Rule, 1);
        for (int y = 0; y <= g.y; y++) FillRect(px, tex.width, tex.height, 0, g.x, y, y, Rule, 1);
        FillRect(px, tex.width, tex.height, 0, g.x, g.y / 2, g.y / 2, Ink, 1);

        var w = b.World;
        var f = w.finish;
        FillRect(px, tex.width, tex.height, f.xMin, f.xMax, f.yMin, f.yMax, Color.Lerp(Paper, Yellow, 0.55f), 0);
        foreach (var piece in b.Pieces)
            FillRect(px, tex.width, tex.height, piece.Rect.xMin, piece.Rect.xMax, piece.Rect.yMin, piece.Rect.yMax, piece.IsHazard ? Red : Ink, 0);
        foreach (var sp in b.Course.Springs)
            FillRect(px, tex.width, tex.height, sp.Visual.xMin, sp.Visual.xMax, sp.Visual.yMin, sp.Visual.yMax, Blue, 0);
        foreach (var tk in b.Course.Tokens)
            FillRect(px, tex.width, tex.height, tk.Pos.x - 0.3f, tk.Pos.x + 0.3f, tk.Pos.y - 0.3f, tk.Pos.y + 0.3f, Red, 0);
        if (b.Course.HasCheckpoint)
        {
            var c = b.Course.Checkpoint;
            FillRect(px, tex.width, tex.height, c.x - 0.05f, c.x + 0.05f, c.y, c.y + 1.5f, Ink, 0);
            FillRect(px, tex.width, tex.height, c.x, c.x + 0.6f, c.y + 1f, c.y + 1.5f, Ink, 0);
        }

        DrawPolyline(px, tex.width, tex.height, b.Curve, Blue);
        DrawPolyline(px, tex.width, tex.height, b.Deriv, Red);

        // Player at spawn (outline) and the play-area frame.
        var t = PlayerMotorTuning.Default;
        float sx = w.spawnXGrid, sy = w.spawnYTopGrid;
        Outline(px, tex.width, tex.height, sx - t.Width / 2, sx + t.Width / 2, sy, sy + t.Height, Ink);
        var pb = w.playBounds;
        Outline(px, tex.width, tex.height, pb.XMin, pb.XMax, pb.YMin, pb.YMax, Blue);

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    static void FillRect(Color[] px, int W, int H, float x0, float x1, float y0, float y1, Color c, int minThickness)
    {
        int ix0 = Mathf.FloorToInt(x0 * Px), ix1 = Mathf.CeilToInt(x1 * Px);
        int iy0 = Mathf.FloorToInt(y0 * Px), iy1 = Mathf.CeilToInt(y1 * Px);
        if (ix1 - ix0 < minThickness) ix1 = ix0 + minThickness;
        if (iy1 - iy0 < minThickness) iy1 = iy0 + minThickness;
        for (int y = Mathf.Max(0, iy0); y < Mathf.Min(H, iy1); y++)
            for (int x = Mathf.Max(0, ix0); x < Mathf.Min(W, ix1); x++)
                px[y * W + x] = c;
    }

    static void Outline(Color[] px, int W, int H, float x0, float x1, float y0, float y1, Color c)
    {
        float e = 1.5f / Px;
        FillRect(px, W, H, x0, x1, y0, y0 + e, c, 1);
        FillRect(px, W, H, x0, x1, y1 - e, y1, c, 1);
        FillRect(px, W, H, x0, x0 + e, y0, y1, c, 1);
        FillRect(px, W, H, x1 - e, x1, y0, y1, c, 1);
    }

    static void DrawPolyline(Color[] px, int W, int H, System.Collections.Generic.List<Vector2> pts, Color c)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            if (PolylineClip.IsBreak(pts[i]) || PolylineClip.IsBreak(pts[i + 1]))
                continue;
            Vector2 a = pts[i] * Px, d = pts[i + 1] * Px - a;
            int n = Mathf.Max(1, Mathf.CeilToInt(d.magnitude));
            for (int k = 0; k <= n; k++)
            {
                Vector2 p = a + d * (k / (float)n);
                for (int oy = -2; oy <= 2; oy++)
                    for (int ox = -2; ox <= 2; ox++)
                    {
                        int x = Mathf.RoundToInt(p.x) + ox, y = Mathf.RoundToInt(p.y) + oy;
                        if (x >= 0 && x < W && y >= 0 && y < H)
                            px[y * W + x] = c;
                    }
            }
        }
    }
}

public static class LevelAuditDebug
{
    /// <summary>-executeMethod LevelAuditDebug.Dump -fpAuditLevels 39 : prints platforms and every resting spot the search found.</summary>
    public static void Dump()
    {
        var args = System.Environment.GetCommandLineArgs();
        int ai = System.Array.IndexOf(args, "-fpAuditLevels");
        var levels = LevelAudit.LoadCurriculum();
        foreach (var s in args[ai + 1].Split(','))
        {
            int idx = int.Parse(s);
            var b = LevelAudit.Build(levels[idx], idx, LevelAudit.Profiles[0]);
            var sb = new System.Text.StringBuilder($"LEVEL {idx} spawn ({b.World.spawnXGrid:0.00},{b.World.spawnYTopGrid:0.00}) bounds {b.World.playBounds.XMin:0.00}-{b.World.playBounds.XMax:0.00} x {b.World.playBounds.YMin:0.00}-{b.World.playBounds.YMax:0.00}\n");
            foreach (var p in b.Pieces) sb.AppendLine($"  {p.Name} {p.Rect.xMin:0.00}-{p.Rect.xMax:0.00} y {p.Rect.yMin:0.00}-{p.Rect.yMax:0.00}");
            if (b.Course.HasCheckpoint) sb.AppendLine($"  checkpoint {b.Course.Checkpoint}");
            var r = LevelReachability.Explore(b.World, b.Course, b.Deriv, PlayerMotorTuning.Default);
            foreach (var p in r.Rests) sb.AppendLine($"  rest {p.x:0.00},{p.y:0.00}");
            sb.AppendLine($"  finish {r.FinishReached} cp {r.CheckpointReached} maxX {r.MaxX}");
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "Logs", $"level-debug-{idx}.txt"), sb.ToString());
        }
    }
}
