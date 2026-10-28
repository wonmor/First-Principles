using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Clips a sampled polyline to a horizontal band of the grid. Points with a NaN y break the line, so the
/// renderers (<see cref="LineRendererUI"/>, <see cref="DerivRendererUI"/>) and the player's f′ touch test skip
/// that segment. This keeps f′ spikes (tan, √x near 0, the circle's vertical tangents) from drawing over the
/// HUD, and keeps the air-jump band to the part of f′ the player can actually see.
/// </summary>
public static class PolylineClip
{
    /// <summary>Grid cells of slack above / below the plane (about half the stroke width).</summary>
    public const float DefaultBandMargin = 0.35f;

    public static bool IsBreak(Vector2 p) => float.IsNaN(p.y) || float.IsNaN(p.x);

    public static readonly Vector2 Break = new Vector2(float.NaN, float.NaN);

    public static void ClipToBand(List<Vector2> raw, float yMin, float yMax, List<Vector2> output)
    {
        output.Clear();
        if (raw == null)
            return;

        bool open = false; // output currently ends with a drawable point
        for (int i = 0; i + 1 < raw.Count; i++)
        {
            Vector2 a = raw[i];
            Vector2 b = raw[i + 1];
            if (!Finite(a) || !Finite(b) || !ClipSegment(ref a, ref b, yMin, yMax))
            {
                open = EndRun(output, open);
                continue;
            }

            if (!open || (output[output.Count - 1] - a).sqrMagnitude > 1e-8f)
            {
                if (open)
                    output.Add(Break);
                output.Add(a);
            }
            output.Add(b);
            open = true;

            // A segment that left the band ends the run (the next one re-enters with its own clipped start).
            if (b.y <= yMin + 1e-6f || b.y >= yMax - 1e-6f)
            {
                if (raw[i + 1].y < yMin || raw[i + 1].y > yMax)
                    open = EndRun(output, open);
            }
        }

        // A lone finite sample inside the band (no neighbours) is not drawable; nothing to add.
        while (output.Count > 0 && IsBreak(output[output.Count - 1]))
            output.RemoveAt(output.Count - 1);
    }

    static bool EndRun(List<Vector2> output, bool open)
    {
        if (open && output.Count > 0 && !IsBreak(output[output.Count - 1]))
            output.Add(Break);
        return false;
    }

    static bool Finite(Vector2 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y));

    /// <summary>Cuts segment ab to yMin..yMax. False when it lies outside, or jumps from below to above (a discontinuity).</summary>
    static bool ClipSegment(ref Vector2 a, ref Vector2 b, float yMin, float yMax)
    {
        bool aIn = a.y >= yMin && a.y <= yMax;
        bool bIn = b.y >= yMin && b.y <= yMax;
        if (aIn && bIn)
            return true;
        if (!aIn && !bIn)
            return false; // both outside: same side (hidden) or opposite sides (asymptote jump)

        Vector2 inside = aIn ? a : b;
        Vector2 outside = aIn ? b : a;
        float edge = outside.y > yMax ? yMax : yMin;
        float t = (edge - inside.y) / (outside.y - inside.y);
        Vector2 cut = new Vector2(Mathf.Lerp(inside.x, outside.x, t), edge);
        if (aIn)
            b = cut;
        else
            a = cut;
        return true;
    }
}
