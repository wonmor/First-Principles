using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Spawns TMP axis labels from <see cref="GridRendererUI"/> extents.
/// Y-axis numbers track <see cref="FunctionPlotter.AxisTickOffsetToMathY"/> when vertical auto-fit is on
/// so ticks match the exaggerated / fitted graph scale.
/// </summary>
public class LabelManager : MonoBehaviour
{
    [SerializeField] private GameObject labelPrefab;

    //Label placement every how many grid increments
    [SerializeField] private int horizontalIncrement = 1;
    [SerializeField] private int verticalIncrement = 1;

    //Show the 0 label for the x axis
    [SerializeField] private bool xAxisOriginLabel = true;

    [SerializeField] private Vector2 xStartPos = new Vector2(0, 0);
    [SerializeField] private Vector2 yStartPos = new Vector2(0, 0);

    private List<TextMeshProUGUI> xLabels = new List<TextMeshProUGUI>();
    private List<TextMeshProUGUI> yLabels = new List<TextMeshProUGUI>();
    /// <summary>Signed plotter‑y tick offset from axis center (same units as pre–auto‑fit grid labels).</summary>
    private readonly List<float> _yTickOffsetsFromCenter = new List<float>();

    private GridRendererUI gridRenderer;
    private float _lastAutoMid = float.NaN;
    private float _lastAutoScale = float.NaN;

    private void Awake()
    {
        gridRenderer = FindAnyObjectByType<GridRendererUI>();
    }

    private void Start()
    {
        GenerateLabels();
    }

    private void LateUpdate()
    {
        var plotter = FindAnyObjectByType<FunctionPlotter>();
        float mid = plotter != null ? plotter.VerticalAxisLabelPivot : 0f;
        float sc = plotter != null ? plotter.VerticalAxisLabelScale : 1f;

        if (!Mathf.Approximately(mid, _lastAutoMid) || !Mathf.Approximately(sc, _lastAutoScale))
        {
            _lastAutoMid = mid;
            _lastAutoScale = sc;
            RefreshYAxisLabelText();
        }
    }

    public void GenerateLabels()
    {
        DeleteCurrentLabels();

        Vector2 xPos = xStartPos;
        Vector2 yPos = yStartPos;

        float xPosOffset = gridRenderer.rectTransform.rect.width / gridRenderer.gridSize.x;
        float yPosOffset = gridRenderer.rectTransform.rect.height / gridRenderer.gridSize.y;

        int xSize = gridRenderer.gridSize.x;
        int ySize = gridRenderer.gridSize.y;

        int xPositive = xSize / (2 * horizontalIncrement) + 1;
        int xNegative = (xPositive * 2) - 1;

        int yPositive = ySize / (2 * verticalIncrement) + 1;
        int yNegative = (yPositive * 2) - 1;

        //Horizontal Labels

        for (int i = 0; i < xPositive; i++)
        {
            if (i == 0 && !xAxisOriginLabel)
            {
                xLabels.Add(null);
                xPos.x += xPosOffset * horizontalIncrement;
                continue;
            }

            xLabels.Add(Instantiate(labelPrefab, transform.TransformPoint(xPos), Quaternion.identity, transform).GetComponent<TextMeshProUGUI>());
            string labelTxt = (i * horizontalIncrement).ToString();
            xLabels[i].text = labelTxt;
            xPos.x += xPosOffset * horizontalIncrement;
        }

        xPos = xStartPos;
        xPos.x -= xPosOffset * horizontalIncrement;

        for (int i = xPositive; i < xNegative; i++)
        {
            xLabels.Add(Instantiate(labelPrefab, transform.TransformPoint(xPos), Quaternion.identity, transform).GetComponent<TextMeshProUGUI>());
            string labelTxt = (-(i - (xPositive - 1)) * horizontalIncrement).ToString();//
            xLabels[i].text = labelTxt;
            xPos.x -= xPosOffset * horizontalIncrement;
        }

        var plotter = FindAnyObjectByType<FunctionPlotter>();

        //Vertical Labels
        for (int i = 0; i < yPositive; i++)
        {
            float tickOffset = i * verticalIncrement;
            yLabels.Add(Instantiate(labelPrefab, transform.TransformPoint(yPos), Quaternion.identity, transform).GetComponent<TextMeshProUGUI>());
            _yTickOffsetsFromCenter.Add(tickOffset);
            yLabels[yLabels.Count - 1].text = FormatYTick(plotter, tickOffset);
            yPos.y += yPosOffset * verticalIncrement;
        }

        yPos = yStartPos;
        yPos.y -= yPosOffset * verticalIncrement;

        for (int i = yPositive; i < yNegative; i++)
        {
            float tickOffset = -(i - (yPositive - 1)) * verticalIncrement;
            yLabels.Add(Instantiate(labelPrefab, transform.TransformPoint(yPos), Quaternion.identity, transform).GetComponent<TextMeshProUGUI>());
            _yTickOffsetsFromCenter.Add(tickOffset);
            yLabels[yLabels.Count - 1].text = FormatYTick(plotter, tickOffset);
            yPos.y -= yPosOffset * verticalIncrement;
        }

        var pAfter = FindAnyObjectByType<FunctionPlotter>();
        _lastAutoMid = pAfter != null ? pAfter.VerticalAxisLabelPivot : 0f;
        _lastAutoScale = pAfter != null ? pAfter.VerticalAxisLabelScale : 1f;
        RefreshYAxisLabelText();
    }

    void RefreshYAxisLabelText()
    {
        var plotter = FindAnyObjectByType<FunctionPlotter>();
        if (yLabels.Count != _yTickOffsetsFromCenter.Count)
            return;
        for (int i = 0; i < yLabels.Count; i++)
        {
            if (yLabels[i] == null)
                continue;
            yLabels[i].text = FormatYTick(plotter, _yTickOffsetsFromCenter[i]);
        }
    }

    static string FormatYTick(FunctionPlotter plotter, float tickOffsetFromCenter)
    {
        float v = plotter != null ? plotter.AxisTickOffsetToMathY(tickOffsetFromCenter) : tickOffsetFromCenter;
        return FormatAxisNumber(v);
    }

    static string FormatAxisNumber(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v))
            return "—";
        if (Mathf.Abs(v) < 1e-5f)
            return "0";
        if (Mathf.Approximately(v, Mathf.Round(v)))
            return Mathf.RoundToInt(v).ToString();
        float av = Mathf.Abs(v);
        if (av >= 100f)
            return v.ToString("0");
        if (av >= 10f)
            return v.ToString("0.#");
        return v.ToString("0.##");
    }

    private void DeleteCurrentLabels()
    {
        xLabels.Clear();
        yLabels.Clear();
        _yTickOffsetsFromCenter.Clear();

        if (transform.childCount > 0)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
        }
    }
}
