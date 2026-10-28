using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>Platforms and gaps agree with the drawn f and f′ (same source of truth for visuals and collision).</summary>
public class LevelGeometryTests
{
    static List<LevelDefinition> _levels;
    static List<LevelDefinition> Levels => _levels ??= LevelAudit.LoadCurriculum();

    /// <summary>Linear read of a drawn polyline at grid x; NaN if x falls in a break or outside it.</summary>
    static float ReadPolyline(List<Vector2> pts, float gx)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            if (PolylineClip.IsBreak(a) || PolylineClip.IsBreak(b))
                continue;
            if (gx >= a.x && gx <= b.x)
                return Mathf.Lerp(a.y, b.y, b.x - a.x < 1e-6f ? 0f : (gx - a.x) / (b.x - a.x));
        }
        return float.NaN;
    }

    static readonly Regex ColumnPiece = new Regex(@"^(Platform|Hazard)_(\d+)$");

    [Test]
    public void ColumnPlatforms_SitOnDrawnCurve_AndGapsFollowDrawnDerivative()
    {
        int platformsChecked = 0, columnsChecked = 0;
        for (int li = 0; li < Levels.Count; li++)
        {
            var def = Levels[li];
            if (def.useRiemannStairPlatforms)
                continue;
            foreach (var prof in LevelAudit.Profiles)
            {
                var b = LevelAudit.Build(def, li, prof);
                var bounds = b.World.playBounds;
                float topMin = bounds.YMin + def.platformThicknessGrid + 0.05f;
                float topMax = bounds.YMax - PlayerMotorTuning.Default.Height - 0.2f;
                float thrY = b.Map.DerivThresholdGridY(def.derivativeSafeThreshold);
                float domL = Mathf.Min(b.Map.GridX(def.xStart), b.Map.GridX(def.xEnd));
                float domR = Mathf.Max(b.Map.GridX(def.xStart), b.Map.GridX(def.xEnd));

                foreach (var piece in b.Pieces)
                {
                    var m = ColumnPiece.Match(piece.Name);
                    if (!m.Success)
                        continue;
                    int col = int.Parse(m.Groups[2].Value);
                    bool forced = col < def.forcePlatformsAtStartColumns || col >= LevelAudit.GameGrid.x - def.forcePlatformsAtEndColumns;
                    bool inDomain = col >= domL && col + 1f <= domR;

                    // f′ rule against the drawn red curve, where the whole column is drawn and clearly on one side.
                    if (!forced && inDomain)
                    {
                        int above = 0, n = 0;
                        for (int k = 0; k < 9; k++)
                        {
                            float y = ReadPolyline(b.Deriv, col + (k + 0.5f) / 9f);
                            if (float.IsNaN(y)) { n = -1; break; }
                            n++;
                            if (y > thrY) above++;
                        }
                        if (n == 9 && Mathf.Abs(above / 9f - 0.5f) > 0.15f)
                        {
                            columnsChecked++;
                            Assert.AreEqual(above / 9f > 0.5f, !piece.IsHazard,
                                $"level {li} [{prof.Name}] col {col}: drawn f′ is {(above > 4 ? "above" : "below")} the threshold line but the column is a {(piece.IsHazard ? "gap" : "platform")}");
                        }
                    }

                    // Platform tops are f at the column centre (clamped into the walkable band), and the drawn f passes there.
                    if (!piece.IsHazard && inDomain)
                    {
                        float y = ReadPolyline(b.Curve, col + 0.5f);
                        if (float.IsNaN(y))
                            continue;
                        float band(float v) => Mathf.Clamp(v, topMin, Mathf.Max(topMin, topMax));
                        float exact = b.Map.GridY(GraphFunctionMath.Evaluate(b.Params, b.Map.PlotterXFromGridX(col + 0.5f)));
                        platformsChecked++;
                        Assert.AreEqual(band(exact), piece.Rect.yMax, 1e-3f, $"level {li} [{prof.Name}] platform col {col} top vs f");
                        // The polyline is sampled every plot step, so allow its chord error on the wiggly table curves.
                        Assert.AreEqual(band(y), piece.Rect.yMax, 0.3f, $"level {li} [{prof.Name}] platform col {col} top vs drawn f");
                    }
                }
            }
        }
        Assert.Greater(platformsChecked, 500);
        Assert.Greater(columnsChecked, 500);
    }

    [Test]
    public void EveryDrawnHazard_IsLethal_AndEveryCollider_IsDrawn()
    {
        for (int li = 0; li < Levels.Count; li++)
            foreach (var prof in LevelAudit.Profiles)
            {
                var b = LevelAudit.Build(Levels[li], li, prof);
                int drawnHaz = 0, drawnPlat = 0;
                foreach (var p in b.Pieces)
                {
                    if (p.IsHazard)
                    {
                        drawnHaz++;
                        Assert.IsTrue(b.World.hazards.Contains(p.Rect), $"level {li} [{prof.Name}] {p.Name} is drawn but not lethal");
                    }
                    else
                    {
                        drawnPlat++;
                        Assert.IsTrue(b.World.platforms.Contains(p.Rect), $"level {li} [{prof.Name}] {p.Name} is drawn but not solid");
                    }
                }
                Assert.AreEqual(b.World.hazards.Count, drawnHaz, $"level {li} [{prof.Name}] invisible hazards");
                Assert.AreEqual(b.World.platforms.Count, drawnPlat, $"level {li} [{prof.Name}] invisible platforms");
            }
    }

    [Test]
    public void LevelsAreDeterministic_IncludingTheLorenzBoss()
    {
        for (int li = 0; li < Levels.Count; li++)
        {
            var a = LevelAudit.Build(Levels[li], li, LevelAudit.Profiles[0]);
            var c = LevelAudit.Build(Levels[li], li, LevelAudit.Profiles[0]);
            Assert.AreEqual(a.World.platforms.Count, c.World.platforms.Count);
            for (int i = 0; i < a.World.platforms.Count; i++)
                Assert.AreEqual(a.World.platforms[i], c.World.platforms[i], $"level {li} platform {i}");
            Assert.AreEqual(0f, a.Params.LorenzPhase);
        }
    }
}
