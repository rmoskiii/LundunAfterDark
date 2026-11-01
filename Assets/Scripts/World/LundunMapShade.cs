using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// R4b: a filled area on the full map (a gang's turf): its outline as map-picture fractions (0-1), filled in one
/// colour with a stronger edge. A stretched child of the map's content, so it pans and zooms with it, like the route.
[RequireComponent(typeof(CanvasRenderer))]
public class LundunMapShade : MaskableGraphic
{
    private readonly List<Vector2> outline = new List<Vector2>();
    private Color edge;
    private float edgeWidth = 4f;

    /// The area (world x, z → fractions of the map picture), its fill and edge colours.
    public void Set(LwPoint[] polygon, float mapMinX, float mapMinZ, float mapWidth, float mapHeight, Color fill, Color edgeColour)
    {
        outline.Clear();
        foreach (LwPoint p in polygon) outline.Add(new Vector2((p.x - mapMinX) / mapWidth, (p.z - mapMinZ) / mapHeight));
        color = fill; edge = edgeColour;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int n = outline.Count;
        if (n < 3) return;
        Rect r = rectTransform.rect;
        List<Vector2> pts = new List<Vector2>(n);
        foreach (Vector2 u in outline) pts.Add(new Vector2(r.xMin + u.x * r.width, r.yMin + u.y * r.height));
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        foreach (Vector2 p in pts) { v.position = p; vh.AddVert(v); }
        foreach (int[] t in Triangulate(pts)) vh.AddTriangle(t[0], t[1], t[2]);
        // the edge: a band along each side
        float half = edgeWidth * 0.5f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % n], d = b - a;
            if (d.sqrMagnitude < 1e-6f) continue;
            Vector2 nn = new Vector2(-d.y, d.x).normalized * half;
            int k = vh.currentVertCount;
            v.color = edge;
            v.position = a - nn; vh.AddVert(v);
            v.position = a + nn; vh.AddVert(v);
            v.position = b + nn; vh.AddVert(v);
            v.position = b - nn; vh.AddVert(v);
            vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k, k + 2, k + 3);
        }
    }

    /// Ear clipping: works for any simple polygon (the districts aren't all convex).
    public static List<int[]> Triangulate(List<Vector2> p)
    {
        List<int[]> tris = new List<int[]>();
        int n = p.Count;
        if (n < 3) return tris;
        List<int> idx = new List<int>(n);
        float area = 0f;
        for (int i = 0; i < n; i++) { Vector2 a = p[i], b = p[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
        if (area > 0f) for (int i = 0; i < n; i++) idx.Add(i);            // anticlockwise
        else for (int i = n - 1; i >= 0; i--) idx.Add(i);
        int guard = 0;
        while (idx.Count > 3 && guard++ < 1000)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                Vector2 a = p[ia], b = p[ib], c = p[ic];
                if (Cross(b - a, c - b) <= 0f) continue;                    // a reflex corner: not an ear
                bool inside = false;
                foreach (int j in idx)
                {
                    if (j == ia || j == ib || j == ic) continue;
                    if (InTri(p[j], a, b, c)) { inside = true; break; }
                }
                if (inside) continue;
                tris.Add(new[] { ia, ib, ic });
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;                                            // not simple: give up on the rest
        }
        if (idx.Count == 3) tris.Add(new[] { idx[0], idx[1], idx[2] });
        return tris;
    }

    static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

    static bool InTri(Vector2 q, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(b - a, q - a), d2 = Cross(c - b, q - b), d3 = Cross(a - c, q - c);
        return d1 >= 0f && d2 >= 0f && d3 >= 0f;
    }
}
