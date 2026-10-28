using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// =============================================================================
// GraphObstacleGenerator — curve & derivative → platform / hazard columns
// =============================================================================
// Draws the rects GraphWorldBuilder computes (f′ over each column decides platform vs gap,
// f sets the platform height; or the Riemann stair rule). Visuals are child UI Images under
// obstaclesRoot; the same rects live in GraphWorld for PlayerControllerUI2D.
// Coordinate space: same as LineRendererUI points (grid cells, origin at grid center).
// =============================================================================

/// <summary>Axis-aligned rectangle in graph grid units (column slices, finish band, etc.).</summary>
public struct GridRect
{
    public float xMin;
    public float xMax;
    public float yMin;
    public float yMax;

    public GridRect(float xMin, float xMax, float yMin, float yMax)
    {
        this.xMin = xMin;
        this.xMax = xMax;
        this.yMin = yMin;
        this.yMax = yMax;
    }

    public bool ContainsY(float y) => y >= yMin && y <= yMax;
}

public class GraphWorld
{
    public Vector2Int gridSize;
    public List<GridRect> platforms = new List<GridRect>();
    public List<GridRect> hazards = new List<GridRect>();
    public GridRect finish;

    public float spawnXGrid;
    public float spawnYTopGrid;

    /// <summary>When true, keeps the avatar inside <see cref="playBounds"/> horizontally and uses them for fall death.</summary>
    public bool hasPlayBounds;
    public GameplayPlayBounds playBounds;

    /// <summary>Call after <see cref="playBounds"/> is recomputed (e.g. rotation) so the exit band stays in the padded region.</summary>
    public void RefreshFinishFromPlayBounds()
    {
        if (!hasPlayBounds)
            return;
        const float finishWidth = 1f;
        var b = playBounds;
        finish = new GridRect(Mathf.Max(b.XMax - finishWidth, b.XMin), b.XMax, b.YMin, b.YMax);
    }
}

/// <summary>Caches grid-space AABB for a platform/hazard/finish visual; reapplies pixel rect when the plane size changes.</summary>
public sealed class GraphObstacleUILayout : MonoBehaviour
{
    public GridRect gridRect;

    public void ApplyPixelLayout(float unitWidth, float unitHeight)
    {
        var rt = transform as RectTransform;
        if (rt == null)
            return;

        float pxX = gridRect.xMin * unitWidth;
        float pxY = gridRect.yMin * unitHeight;
        float pxW = (gridRect.xMax - gridRect.xMin) * unitWidth;
        float pxH = (gridRect.yMax - gridRect.yMin) * unitHeight;
        rt.anchoredPosition = new Vector2(pxX, pxY);
        rt.sizeDelta = new Vector2(pxW, pxH);
    }
}

/// <summary>
/// Converts plotted curve/derivative points into a 2D "platformer world" (platforms + gaps).
/// Obstacles live in the same grid coordinate space as the graph (0..gridSize.x/y).
/// </summary>
public class GraphObstacleGenerator : MonoBehaviour
{
    private RectTransform obstaclesRoot;
    private Vector2Int gridSize;
    private float unitWidth;
    private float unitHeight;
    private Sprite obstacleSprite;

    public void SetLayout(RectTransform obstaclesRoot, Vector2Int gridSize, float unitWidth, float unitHeight)
    {
        this.obstaclesRoot = obstaclesRoot;
        this.gridSize = gridSize;
        this.unitWidth = unitWidth;
        this.unitHeight = unitHeight;

        obstacleSprite = RuntimeUiPolish.Rounded9Slice != null ? RuntimeUiPolish.Rounded9Slice : TryGetSquareSprite();
    }

    /// <summary>Repositions platform/hazard/finish quads after the Cartesian plane is scaled (rotation, safe area).</summary>
    public void RefreshObstaclePixelLayout()
    {
        if (obstaclesRoot == null)
            return;
        var layouts = obstaclesRoot.GetComponentsInChildren<GraphObstacleUILayout>(true);
        for (int i = 0; i < layouts.Length; i++)
            layouts[i].ApplyPixelLayout(unitWidth, unitHeight);
    }

    /// <param name="functionPlotter">Curve source: its current params and plot map drive every column.</param>
    /// <param name="playBoundsOptional">Inset AABB in grid units; null = full grid, no player hard clamp.</param>
    public GraphWorld GenerateWorld(
        LevelDefinition def,
        List<Vector2> curvePoints,
        List<Vector2> derivPoints,
        FunctionPlotter functionPlotter = null,
        GameplayPlayBounds? playBoundsOptional = null)
    {
        if (obstaclesRoot == null)
        {
            Debug.LogError("GraphObstacleGenerator: obstaclesRoot is null. Call SetLayout first.");
            return new GraphWorld();
        }

        // Clear previous obstacles (but keep other scene children under the parent).
        for (int i = obstaclesRoot.childCount - 1; i >= 0; i--)
        {
            var child = obstaclesRoot.GetChild(i);
            if (child == null)
                continue;
            if (child.name.StartsWith("Platform", System.StringComparison.OrdinalIgnoreCase) ||
                child.name.StartsWith("Hazard", System.StringComparison.OrdinalIgnoreCase) ||
                child.name.StartsWith("FinishExitGradient", System.StringComparison.OrdinalIgnoreCase))
                Destroy(child.gameObject);
        }

        // Same params + map as the drawn f / f′ (one source of truth for visuals and collision).
        GraphFunctionParams prm = functionPlotter != null ? functionPlotter.CurrentParams : GraphFunctionParams.FromLevel(def);
        GraphViewMapping map = functionPlotter != null ? functionPlotter.CurrentMapping : GraphViewMapping.Identity(gridSize);
        map.GridSize = gridSize;
        float dStep = functionPlotter != null ? functionPlotter.NumericalDerivativeStep : 0f;

        var built = GraphWorldBuilder.Build(def, prm, map, dStep, playBoundsOptional);
        foreach (var piece in built.Pieces)
            CreateRectVisual(piece.Name, piece.Rect, piece.IsHazard ? def.derivativeColor : def.curveColor);

        CreateFinishExitGradient(built.World.finish);
        return built.World;
    }

    /// <summary>
    /// Sky-blue horizontal gradient on the far right so the exit / finish band reads clearly.
    /// Drawn above platforms (last sibling); player is parented to the plane, not this root.
    /// </summary>
    private void CreateFinishExitGradient(GridRect finishBand)
    {
        if (obstaclesRoot == null)
            return;

        const float fadeWidthGrid = 2.85f;
        float xMax = finishBand.xMax;
        float xMin = Mathf.Max(0f, xMax - fadeWidthGrid);
        var rect = new GridRect(xMin, xMax, finishBand.yMin, finishBand.yMax);

        float pxX = rect.xMin * unitWidth;
        float pxY = rect.yMin * unitHeight;
        float pxW = (rect.xMax - rect.xMin) * unitWidth;
        float pxH = (rect.yMax - rect.yMin) * unitHeight;

        var go = new GameObject("FinishExitGradient");
        go.transform.SetParent(obstaclesRoot, false);
        go.transform.SetAsLastSibling();

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(pxX, pxY);
        rt.sizeDelta = new Vector2(pxW, pxH);

        var finishLayout = go.AddComponent<GraphObstacleUILayout>();
        finishLayout.gridRect = rect;

        var grad = go.AddComponent<UiHorizontalGradientGraphic>();
        grad.raycastTarget = false;
        // Bauhaus: flat yellow exit block (no gradient).
        var exit = Bauhaus.WithAlpha(Bauhaus.Yellow, 0.55f);
        grad.SetGradientColors(exit, exit);
    }


    private void CreateRectVisual(string name, GridRect rect, Color color)
    {
        if (obstaclesRoot == null)
            return;

        // Convert grid rect to anchored pixel rect.
        float pxX = rect.xMin * unitWidth;
        float pxY = rect.yMin * unitHeight;
        float pxW = (rect.xMax - rect.xMin) * unitWidth;
        float pxH = (rect.yMax - rect.yMin) * unitHeight;

        var go = new GameObject(name);
        go.transform.SetParent(obstaclesRoot, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(pxX, pxY);
        rt.sizeDelta = new Vector2(pxW, pxH);

        var layout = go.AddComponent<GraphObstacleUILayout>();
        layout.gridRect = rect;

        var img = go.AddComponent<Image>();
        img.sprite = obstacleSprite;
        bool isHazard = name.StartsWith("Hazard", System.StringComparison.OrdinalIgnoreCase);
        // Flat Bauhaus blocks: platforms in the curve colour, hazards in the f′ colour.
        img.color = new Color(color.r, color.g, color.b, isHazard ? 0.92f : 1f);
        if (obstacleSprite != null && obstacleSprite.border.sqrMagnitude > 0.001f &&
            RuntimeUiPolish.ShouldUseSlicedForSize(pxW, pxH))
            img.type = Image.Type.Sliced;
        else
            img.type = Image.Type.Simple;
        img.raycastTarget = false;
    }

    private Sprite TryGetSquareSprite()
    {
        var outsideSquare1 = GameObject.Find("OutsideSquare1");
        if (outsideSquare1 != null)
        {
            var img = outsideSquare1.GetComponent<Image>();
            if (img != null && img.sprite != null)
                return img.sprite;
        }

        // Fallback: any sprite-backed Image.
        var allImages = FindObjectsByType<Image>();
        foreach (var img in allImages)
        {
            if (img != null && img.sprite != null)
                return img.sprite;
        }

        return null;
    }
}

