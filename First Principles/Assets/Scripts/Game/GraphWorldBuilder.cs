using System.Collections.Generic;
using UnityEngine;

/// <summary>One solid / hazard rectangle produced by <see cref="GraphWorldBuilder"/>, with the name its visual uses.</summary>
public struct GraphWorldPiece
{
    public string Name;
    public GridRect Rect;
    public bool IsHazard;
}

/// <summary>
/// Engine-free level geometry: turns a curve (shared <see cref="GraphFunctionMath"/> + <see cref="GraphViewMapping"/>)
/// into platforms, hazards, spawn and finish. <see cref="GraphObstacleGenerator"/> draws exactly these rects and the
/// EditMode reachability audit tests exactly these rects.
/// </summary>
public static class GraphWorldBuilder
{
    public struct Result
    {
        public GraphWorld World;
        public List<GraphWorldPiece> Pieces;
    }

    public static Result Build(LevelDefinition def, in GraphFunctionParams prm, in GraphViewMapping map, float derivNumericStep,
        GameplayPlayBounds? playBoundsOptional)
    {
        var ctx = new Ctx(def, prm, map, derivNumericStep, playBoundsOptional);
        ctx.Run();
        return new Result { World = ctx.World, Pieces = ctx.Pieces };
    }

    public const int ColumnDerivativeSamples = 9;

    /// <summary>Fraction of column <paramref name="col"/> (grid x col..col+1) where f′ &gt; the level's threshold.</summary>
    public static float ColumnSafeFraction(LevelDefinition def, in GraphFunctionParams prm, in GraphViewMapping map, float dStep, int col)
    {
        int safe = 0;
        for (int i = 0; i < ColumnDerivativeSamples; i++)
        {
            float gx = col + (i + 0.5f) / ColumnDerivativeSamples;
            float x = Mathf.Clamp(map.PlotterXFromGridX(gx), def.xStart, def.xEnd);
            float dy = GraphFunctionMath.Derivative(prm, x, dStep);
            if (GraphFunctionMath.IsFinite(dy) && dy > def.derivativeSafeThreshold)
                safe++;
        }
        return safe / (float)ColumnDerivativeSamples;
    }

    sealed class Ctx
    {
        readonly LevelDefinition def;
        readonly GraphFunctionParams prm;
        readonly GraphViewMapping map;
        readonly float dStep;
        readonly Vector2Int gridSize;
        readonly GameplayPlayBounds bounds;
        public readonly GraphWorld World = new GraphWorld();
        public readonly List<GraphWorldPiece> Pieces = new List<GraphWorldPiece>();

        public Ctx(LevelDefinition def, GraphFunctionParams prm, GraphViewMapping map, float dStep, GameplayPlayBounds? pb)
        {
            this.def = def;
            this.prm = prm;
            this.map = map;
            this.dStep = dStep;
            gridSize = map.GridSize;
            bounds = pb ?? GameplayPlayBounds.FullGrid(gridSize);
            World.gridSize = gridSize;
            World.hasPlayBounds = pb.HasValue;
            World.playBounds = bounds;
        }

        /// <summary>
        /// Lowest / highest walkable platform top: the slab must stay inside the play area and the player standing on it
        /// must fit under the top inset (the play-bounds clamp would otherwise push it down through the slab).
        /// </summary>
        float TopMin => bounds.YMin + def.platformThicknessGrid + 0.05f;
        float TopMax => Mathf.Max(TopMin, bounds.YMax - PlayerMotorTuning.Default.Height - 0.2f);
        float ClampTop(float top) => Mathf.Clamp(top, TopMin, TopMax);
        GridRect Slab(float x0, float x1, float top) => new GridRect(x0, x1, top - def.platformThicknessGrid, top);
        /// <summary>Hazard strip on the floor of the play area (it used to start at grid y 0, under the bottom inset, and vanish on phones).</summary>
        GridRect HazardStrip(int col) => new GridRect(col, col + 1f, bounds.YMin, bounds.YMin + def.hazardHeightGrid);

        float PlotterXAtGridX(float gridX) => Mathf.Clamp(map.PlotterXFromGridX(gridX), def.xStart, def.xEnd);

        float CurveGridY(float xPlot)
        {
            float y = GraphFunctionMath.Evaluate(prm, xPlot);
            return GraphFunctionMath.IsFinite(y) ? map.GridY(y) : float.NaN;
        }


        public void Run()
        {
            float originY = gridSize.y / 2f;

            bool useRiemannStairs = def.useRiemannStairPlatforms
                && def.riemannRule != RiemannRule.None
                && def.riemannRectCount > 0
                && (def.xEnd - def.xStart) > 1e-6f;

            const float finishWidth = 1f;
            World.finish = new GridRect(Mathf.Max(bounds.XMax - finishWidth, bounds.XMin), bounds.XMax, bounds.YMin, bounds.YMax);

            if (useRiemannStairs)
                BuildRiemann();
            else
            {
                for (int col = 0; col < gridSize.x; col++)
                {
                    float xPlotter = PlotterXAtGridX(col + 0.5f);
                    float yCurve = CurveGridY(xPlotter);
                    bool hasCurve = GraphFunctionMath.IsFinite(yCurve);
                    bool safeByDerivative = ColumnSafeByDerivative(col);

                    bool forcedSafeStart = col < def.forcePlatformsAtStartColumns;
                    bool forcedSafeEnd = col >= gridSize.x - def.forcePlatformsAtEndColumns;
                    bool safe = safeByDerivative || forcedSafeStart || forcedSafeEnd;

                    if (!safe)
                    {
                        AddHazard($"Hazard_{col}", HazardStrip(col));
                        continue;
                    }

                    float platformTop = ClampTop(hasCurve ? yCurve : originY);
                    AddPlatform($"Platform_{col}", Slab(col, col + 1f, platformTop));
                }
            }

            if (def.maxGapColumns > 0)
                InsertBridgePlatforms();

            ChooseSpawn();
        }

        /// <summary>
        /// Spawn on a real (post-clip) platform: the lowest one in the forced start columns, else the leftmost. The x is
        /// kept on that slab and inside the play area, so the player always starts standing on ground.
        /// </summary>
        void ChooseSpawn()
        {
            int best = -1;
            float startCols = Mathf.Max(1, def.forcePlatformsAtStartColumns);
            for (int i = 0; i < World.platforms.Count; i++)
            {
                var p = World.platforms[i];
                if (p.xMin >= startCols || p.xMax <= bounds.XMin)
                    continue;
                if (best < 0 || p.yMax < World.platforms[best].yMax - 1e-4f)
                    best = i;
            }
            if (best < 0)
                for (int i = 0; i < World.platforms.Count; i++)
                    if (best < 0 || World.platforms[i].xMin < World.platforms[best].xMin)
                        best = i;
            if (best < 0)
            {
                World.spawnXGrid = bounds.XMin + 0.5f;
                World.spawnYTopGrid = TopMin;
                return;
            }

            var sp = World.platforms[best];
            float halfW = PlayerMotorTuning.Default.Width * 0.5f;
            float lo = Mathf.Max(sp.xMin, bounds.XMin + halfW), hi = Mathf.Min(sp.xMax, bounds.XMax - halfW);
            World.spawnXGrid = lo <= hi ? Mathf.Clamp((sp.xMin + sp.xMax) * 0.5f, lo, hi) : (sp.xMin + sp.xMax) * 0.5f;
            World.spawnYTopGrid = sp.yMax;
        }

        bool TryClamp(GridRect r, out GridRect c)
        {
            float x0 = Mathf.Max(r.xMin, bounds.XMin);
            float x1 = Mathf.Min(r.xMax, bounds.XMax);
            float y0 = Mathf.Max(r.yMin, bounds.YMin);
            float y1 = Mathf.Min(r.yMax, bounds.YMax);
            if (x1 - x0 < 0.03f || y1 - y0 < 0.03f)
            {
                c = default;
                return false;
            }
            c = new GridRect(x0, x1, y0, y1);
            return true;
        }

        void AddPlatform(string name, GridRect r)
        {
            if (!TryClamp(r, out var c))
                return;
            World.platforms.Add(c);
            Pieces.Add(new GraphWorldPiece { Name = name, Rect = c, IsHazard = false });
        }

        void AddHazard(string name, GridRect r)
        {
            if (!TryClamp(r, out var c))
                return;
            World.hazards.Add(c);
            Pieces.Add(new GraphWorldPiece { Name = name, Rect = c, IsHazard = true });
        }

        /// <summary>Removes hazards in a column from collision <b>and</b> drawing (a bridge used to leave a harmless red block).</summary>
        void RemoveHazardsInColumn(int col)
        {
            for (int h = World.hazards.Count - 1; h >= 0; h--)
                if (World.hazards[h].xMin < col + 1f && World.hazards[h].xMax > col)
                    World.hazards.RemoveAt(h);
            for (int i = Pieces.Count - 1; i >= 0; i--)
                if (Pieces[i].IsHazard && Pieces[i].Rect.xMin < col + 1f && Pieces[i].Rect.xMax > col)
                    Pieces.RemoveAt(i);
        }

        /// <summary>
        /// Ground rule, read off the drawn f′: a column holds a platform when f′ clears the threshold over most of the
        /// column's width (a single centre sample aliased on curves that oscillate within a column).
        /// </summary>
        bool ColumnSafeByDerivative(int col) =>
            GraphWorldBuilder.ColumnSafeFraction(def, prm, map, dStep, col) > 0.5f;

        void BuildRiemann()
        {
            int n = Mathf.Max(1, def.riemannRectCount);
            float dxPlot = (def.xEnd - def.xStart) / n;
            float cov = Mathf.Clamp(def.riemannPlatformCoverage, 0.22f, 1f);

            for (int i = 0; i < n; i++)
            {
                float xL = def.xStart + i * dxPlot;
                float xR = def.xStart + (i + 1) * dxPlot;
                float xS = RiemannSampleX(def.riemannRule, xL, xR);
                float yGrid = CurveGridY(xS);
                if (!GraphFunctionMath.IsFinite(yGrid))
                    continue;

                float colL = map.GridX(xL);
                float colR = map.GridX(xR);
                if (colL > colR)
                    (colL, colR) = (colR, colL);
                float center = 0.5f * (colL + colR);
                float halfSpan = 0.5f * (colR - colL) * cov;
                float pMin = Mathf.Clamp(center - halfSpan, 0f, gridSize.x);
                float pMax = Mathf.Clamp(center + halfSpan, 0f, gridSize.x);
                if (pMax - pMin < 0.18f)
                    continue;

                AddPlatform($"Platform_Riemann_{i}", Slab(pMin, pMax, ClampTop(yGrid)));
            }

            float starterRefTop = float.NaN;
            foreach (var p in World.platforms)
            {
                if (p.xMax <= 0f || p.xMin >= def.forcePlatformsAtStartColumns + 4)
                    continue;
                if (float.IsNaN(starterRefTop) || p.yMax < starterRefTop)
                    starterRefTop = p.yMax;
            }
            if (float.IsNaN(starterRefTop))
                starterRefTop = Mathf.Clamp(gridSize.y * 0.24f, def.platformThicknessGrid * 2.5f, gridSize.y * 0.42f);

            for (int col = 0; col < def.forcePlatformsAtStartColumns; col++)
            {
                if (ColumnOverlapsAnyPlatform(col))
                    continue;
                AddPlatform($"Platform_StartPad_{col}", Slab(col, col + 1f, ClampTop(starterRefTop)));
            }

            float endRefTop = starterRefTop;
            foreach (var p in World.platforms)
            {
                if (p.xMin < gridSize.x - def.forcePlatformsAtEndColumns - 4)
                    continue;
                endRefTop = Mathf.Max(endRefTop, p.yMax);
            }

            for (int col = gridSize.x - def.forcePlatformsAtEndColumns; col < gridSize.x; col++)
            {
                if (col < 0 || ColumnOverlapsAnyPlatform(col))
                    continue;
                AddPlatform($"Platform_EndPad_{col}", Slab(col, col + 1f, ClampTop(endRefTop)));
            }

            for (int col = 0; col < gridSize.x; col++)
            {
                if (col + 1f <= bounds.XMin || col >= bounds.XMax)
                    continue;
                if (ColumnOverlapsAnyPlatform(col))
                    continue;
                if (!ColumnSafeByDerivative(col))
                    AddHazard($"Hazard_{col}", HazardStrip(col));
            }
        }

        static float RiemannSampleX(RiemannRule rule, float xL, float xR) => rule switch
        {
            RiemannRule.Left => xL,
            RiemannRule.Right => xR,
            _ => 0.5f * (xL + xR)
        };

        void InsertBridgePlatforms()
        {
            int maxGap = Mathf.Max(2, def.maxGapColumns);
            int gapStart = -1;

            for (int col = 0; col <= gridSize.x; col++)
            {
                bool hasPlatform = col < gridSize.x && ColumnOverlapsAnyPlatform(col);
                if (!hasPlatform && col < gridSize.x)
                {
                    if (gapStart < 0)
                        gapStart = col;
                    continue;
                }

                if (gapStart >= 0 && col - gapStart > maxGap)
                {
                    for (int b = gapStart + maxGap - 1; b < col; b += maxGap - 1)
                    {
                        float yCurve = CurveGridY(PlotterXAtGridX(b + 0.5f));
                        if (!GraphFunctionMath.IsFinite(yCurve))
                            yCurve = gridSize.y * 0.35f;

                        float platformTop = ClampTop(yCurve);
                        float nearestPlatY = FindNearestPlatformTop(b, 6);
                        if (!float.IsNaN(nearestPlatY))
                            platformTop = Mathf.Lerp(platformTop, nearestPlatY, 0.45f);
                        RemoveHazardsInColumn(b);
                        AddPlatform($"Platform_Bridge_{b}", Slab(b, b + 1f, ClampTop(platformTop)));
                    }
                }
                gapStart = -1;
            }
        }

        float FindNearestPlatformTop(int col, int searchRadius)
        {
            float best = float.NaN, bestDist = float.PositiveInfinity;
            foreach (var p in World.platforms)
            {
                float d = Mathf.Abs((p.xMin + p.xMax) * 0.5f - (col + 0.5f));
                if (d < bestDist && d <= searchRadius)
                {
                    bestDist = d;
                    best = p.yMax;
                }
            }
            return best;
        }

        bool ColumnOverlapsAnyPlatform(int col)
        {
            foreach (var p in World.platforms)
                if (col + 1f > p.xMin && col < p.xMax)
                    return true;
            return false;
        }
    }
}
