using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using NV3 = System.Numerics.Vector3;

// -----------------------------------------------------------------------------
// Space3DRender: Unity meshes for the 3D calculator and stages.
// -----------------------------------------------------------------------------
// Math coordinates (x, y, z with z up) map to Unity world (x, z, y), so the math axes keep their
// usual right-handed look on screen. Colours come from the Bauhaus palette: surfaces are coloured
// by height (blue low, yellow middle, red high) or by function; ink for axes and fixed ground.
// -----------------------------------------------------------------------------

public static class Space3DRender
{
    public static Vector3 ToWorld(NV3 p) => new Vector3(p.X, p.Z, p.Y);

    public static Vector3 ToWorld(float x, float y, float z) => new Vector3(x, z, y);

    public static NV3 ToMath(Vector3 w) => new NV3(w.x, w.z, w.y);

    /// <summary>Per-function colours when "colour by function" is on (index wraps).</summary>
    public static readonly Color[] FunctionColors =
    {
        Bauhaus.Blue, Bauhaus.Red, Bauhaus.YellowLine, Bauhaus.Hex("3A3833"), Bauhaus.Hex("6E8FC7"), Bauhaus.Hex("E07A5F"),
    };

    static readonly Color[] HeightStops =
    {
        Bauhaus.Hex("16397A"), Bauhaus.Blue, Bauhaus.Hex("7FA3D9"), Bauhaus.Yellow, Bauhaus.Hex("EE8A12"), Bauhaus.Red,
    };

    public static Color HeightColor(float t)
    {
        t = Mathf.Clamp01(t);
        float f = t * (HeightStops.Length - 1);
        int i = Mathf.Min(HeightStops.Length - 2, Mathf.FloorToInt(f));
        return Color.Lerp(HeightStops[i], HeightStops[i + 1], f - i);
    }

    static Material _lit, _unlit, _unlitNoDepth;

    public static Material Lit
    {
        get
        {
            if (_lit == null)
            {
                var sh = Resources.Load<Shader>("Space3D_Lit") ?? Shader.Find("FirstPrinciples/Space3DLit");
                _lit = new Material(sh) { name = "Space3DLit" };
            }

            return _lit;
        }
    }

    public static Material Unlit
    {
        get
        {
            if (_unlit == null)
            {
                var sh = Resources.Load<Shader>("Space3D_Unlit") ?? Shader.Find("FirstPrinciples/Space3DUnlit");
                _unlit = new Material(sh) { name = "Space3DUnlit" };
            }

            return _unlit;
        }
    }

    /// <summary>Unlit, no depth write (water, translucent overlays).</summary>
    public static Material UnlitTranslucent
    {
        get
        {
            if (_unlitNoDepth == null)
            {
                _unlitNoDepth = new Material(Unlit) { name = "Space3DUnlitTranslucent" };
                _unlitNoDepth.SetFloat("_ZWrite", 0f);
                _unlitNoDepth.renderQueue = 3100;
            }

            return _unlitNoDepth;
        }
    }

    public static Material LitWithContours(float step, float strength)
    {
        var m = new Material(Lit);
        m.SetFloat("_Contour", step);
        m.SetFloat("_ContourAlpha", strength);
        return m;
    }

    public static GameObject NewMeshObject(string name, Transform parent, Material mat, out MeshFilter mf)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mf.sharedMesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mf.sharedMesh.MarkDynamic();
        return go;
    }

    /// <summary>Fills <paramref name="mesh"/> with a sampled surface. Colour by height over [zLo, zHi] or a flat colour.</summary>
    public static void FillSurface(Mesh mesh, Plot3DMesh m, bool byHeight, Color flat, float zLo, float zHi)
    {
        mesh.Clear();
        int n = m.Vertices.Count;
        var v = new Vector3[n];
        var nr = new Vector3[n];
        var c = new Color[n];
        float span = Mathf.Max(1e-4f, zHi - zLo);
        for (int i = 0; i < n; i++)
        {
            v[i] = ToWorld(m.Vertices[i]);
            nr[i] = i < m.Normals.Count ? ToWorld(m.Normals[i]) : Vector3.up;
            c[i] = byHeight ? HeightColor((m.Vertices[i].Z - zLo) / span) : flat;
        }

        mesh.vertices = v;
        mesh.normals = nr;
        mesh.colors = c;
        mesh.SetTriangles(m.Triangles, 0);
        mesh.RecalculateBounds();
    }

    /// <summary>Tube along each polyline (curves, rails, axes).</summary>
    public static void FillTubes(Mesh mesh, List<List<NV3>> lines, float radius, Color color, int sides = 8)
    {
        mesh.Clear();
        var v = new List<Vector3>();
        var nr = new List<Vector3>();
        var c = new List<Color>();
        var tris = new List<int>();
        foreach (var line in lines)
            AppendTube(line, radius, color, sides, v, nr, c, tris);
        mesh.SetVertices(v);
        mesh.SetNormals(nr);
        mesh.SetColors(c);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    public static void AppendTube(List<NV3> line, float radius, Color color, int sides,
        List<Vector3> v, List<Vector3> nr, List<Color> c, List<int> tris)
    {
        if (line == null || line.Count < 2)
            return;
        Vector3 prevSide = Vector3.zero;
        int baseIdx = v.Count;
        for (int k = 0; k < line.Count; k++)
        {
            var p = ToWorld(line[k]);
            var a = ToWorld(line[Mathf.Max(0, k - 1)]);
            var b = ToWorld(line[Mathf.Min(line.Count - 1, k + 1)]);
            var tan = (b - a).sqrMagnitude > 1e-12f ? (b - a).normalized : Vector3.forward;
            // Parallel-transport-ish frame: keep the previous side vector when possible (no twisting).
            var side = prevSide == Vector3.zero ? Vector3.Cross(tan, Mathf.Abs(tan.y) < 0.9f ? Vector3.up : Vector3.right).normalized
                : Vector3.ProjectOnPlane(prevSide, tan).normalized;
            if (side.sqrMagnitude < 1e-6f)
                side = Vector3.Cross(tan, Vector3.right).normalized;
            prevSide = side;
            var up = Vector3.Cross(side, tan);
            for (int s = 0; s < sides; s++)
            {
                float ang = s * Mathf.PI * 2f / sides;
                var dir = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                v.Add(p + dir * radius);
                nr.Add(dir);
                c.Add(color);
            }
        }

        for (int k = 0; k < line.Count - 1; k++)
        {
            for (int s = 0; s < sides; s++)
            {
                int i0 = baseIdx + k * sides + s;
                int i1 = baseIdx + k * sides + (s + 1) % sides;
                int j0 = i0 + sides;
                int j1 = i1 + sides;
                tris.Add(i0); tris.Add(j0); tris.Add(i1);
                tris.Add(i1); tris.Add(j0); tris.Add(j1);
            }
        }
    }

    /// <summary>Axis-aligned box in math coordinates, appended to vertex lists (flat normals per face).</summary>
    public static void AppendBox(NV3 min, NV3 max, Color color, List<Vector3> v, List<Vector3> nr, List<Color> c, List<int> tris)
    {
        var a = ToWorld(min);
        var b = ToWorld(max);
        var lo = Vector3.Min(a, b);
        var hi = Vector3.Max(a, b);
        void Face(Vector3 n, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float shade)
        {
            int i = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            for (int k = 0; k < 4; k++)
            {
                nr.Add(n);
                c.Add(color * shade + new Color(0, 0, 0, color.a * (1f - shade)));
            }

            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        Face(Vector3.up, new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, hi.y, lo.z), 1f);
        Face(Vector3.down, new Vector3(lo.x, lo.y, lo.z), new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, lo.y, hi.z), new Vector3(lo.x, lo.y, hi.z), 1f);
        Face(Vector3.left, new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, lo.y, hi.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, hi.y, lo.z), 1f);
        Face(Vector3.right, new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, lo.y, hi.z), 1f);
        Face(Vector3.back, new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, lo.y, lo.z), 1f);
        Face(Vector3.forward, new Vector3(lo.x, lo.y, hi.z), new Vector3(hi.x, lo.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(lo.x, hi.y, hi.z), 1f);
    }

    public static void FillLists(Mesh mesh, List<Vector3> v, List<Vector3> nr, List<Color> c, List<int> tris)
    {
        mesh.Clear();
        mesh.SetVertices(v);
        mesh.SetNormals(nr);
        mesh.SetColors(c);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
    }

    /// <summary>Line segments (pairs of points) drawn with MeshTopology.Lines.</summary>
    public static void FillLines(Mesh mesh, List<Vector3> pairs, List<Color> colors)
    {
        mesh.Clear();
        mesh.SetVertices(pairs);
        mesh.SetColors(colors);
        var idx = new int[pairs.Count];
        for (int i = 0; i < idx.Length; i++)
            idx[i] = i;
        mesh.SetIndices(idx, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();
    }

    /// <summary>Floor grid (z = <paramref name="z"/>) every <paramref name="step"/> units across the box, plus the box's base outline.</summary>
    public static void FillGrid(Mesh mesh, Box3 box, float step, float z, Color minor, Color major)
    {
        var p = new List<Vector3>();
        var c = new List<Color>();
        int i0 = Mathf.CeilToInt(box.XMin / step), i1 = Mathf.FloorToInt(box.XMax / step);
        for (int i = i0; i <= i1; i++)
        {
            float x = i * step;
            var col = i == 0 ? Color.clear : (i % 5 == 0 ? major : minor);
            p.Add(ToWorld(x, box.YMin, z)); p.Add(ToWorld(x, box.YMax, z));
            c.Add(col); c.Add(col);
        }

        int j0 = Mathf.CeilToInt(box.YMin / step), j1 = Mathf.FloorToInt(box.YMax / step);
        for (int j = j0; j <= j1; j++)
        {
            float y = j * step;
            var col = j == 0 ? Color.clear : (j % 5 == 0 ? major : minor);
            p.Add(ToWorld(box.XMin, y, z)); p.Add(ToWorld(box.XMax, y, z));
            c.Add(col); c.Add(col);
        }

        // Vertical edges of the viewing box (faint) so depth reads.
        foreach (var (x, y) in new[] { (box.XMin, box.YMin), (box.XMax, box.YMin), (box.XMin, box.YMax), (box.XMax, box.YMax) })
        {
            p.Add(ToWorld(x, y, box.ZMin)); p.Add(ToWorld(x, y, box.ZMax));
            c.Add(minor); c.Add(minor);
        }

        FillLines(mesh, p, c);
    }

    /// <summary>Flat translucent quad at height <paramref name="z"/> across the box (stage water).</summary>
    public static void FillWater(Mesh mesh, Box3 box, float z, float margin, Color color)
    {
        var v = new List<Vector3>
        {
            ToWorld(box.XMin - margin, box.YMin - margin, z), ToWorld(box.XMin - margin, box.YMax + margin, z),
            ToWorld(box.XMax + margin, box.YMax + margin, z), ToWorld(box.XMax + margin, box.YMin - margin, z),
        };
        var nr = new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        var c = new List<Color> { color, color, color, color };
        FillLists(mesh, v, nr, c, new List<int> { 0, 1, 2, 0, 2, 3 });
    }
}
