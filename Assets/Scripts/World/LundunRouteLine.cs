using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// Draws the GPS route as a thick line on a map picture. Put it as a stretched child of the map image:
/// it then pans, zooms and turns with the map automatically. Rebuilds when the route changes.
[RequireComponent(typeof(CanvasRenderer))]
public class LundunRouteLine : MaskableGraphic
{
    [SerializeField] private float width = 6f;
    [Header("Map picture area (world metres) — same as the map image")]
    [SerializeField] private float mapMinX = -150f;
    [SerializeField] private float mapMinZ = -150f;
    [SerializeField] private float mapWidth = 1900f;
    [SerializeField] private float mapHeight = 1500f;

    private readonly List<Vector2> uv = new List<Vector2>();
    private int seenVersion = -1;

    void Update()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        int v = nav != null ? nav.RouteVersion : -1;
        if (v == seenVersion) return;
        seenVersion = v;
        uv.Clear();
        if (nav != null && nav.HasWaypoint)
            foreach (Vector3 p in nav.Route) uv.Add(new Vector2((p.x - mapMinX) / mapWidth, (p.z - mapMinZ) / mapHeight));
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (uv.Count < 2) return;
        Rect r = rectTransform.rect;
        float half = width * 0.5f;
        Vector2 prev = Vector2.zero;
        for (int i = 0; i < uv.Count; i++)
        {
            Vector2 p = new Vector2(r.xMin + uv[i].x * r.width, r.yMin + uv[i].y * r.height);
            if (i > 0) Segment(vh, prev, p, half);
            Square(vh, p, half);              // fills the gap at each bend
            prev = p;
        }
    }

    void Segment(VertexHelper vh, Vector2 a, Vector2 b, float half)
    {
        Vector2 d = b - a;
        if (d.sqrMagnitude < 1e-6f) return;
        Vector2 n = new Vector2(-d.y, d.x).normalized * half;
        Quad(vh, a - n, a + n, b + n, b - n);
    }

    void Square(VertexHelper vh, Vector2 c, float half)
    {
        Quad(vh, c + new Vector2(-half, -half), c + new Vector2(-half, half), c + new Vector2(half, half), c + new Vector2(half, -half));
    }

    void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        int i = vh.currentVertCount;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        v.position = a; vh.AddVert(v);
        v.position = b; vh.AddVert(v);
        v.position = c; vh.AddVert(v);
        v.position = d; vh.AddVert(v);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
