// The road network as a graph, built from LundunWorld.json, for GPS routes.
// Pure maths (no UnityEngine) so it can be tested outside Unity.
using System;
using System.Collections.Generic;

public struct NavPoint
{
    public double x, z;
    public NavPoint(double x, double z) { this.x = x; this.z = z; }
    public static double Dist(NavPoint a, NavPoint b) { double dx = a.x - b.x, dz = a.z - b.z; return Math.Sqrt(dx * dx + dz * dz); }
}

public class LundunRoadGraph
{
    class Edge { public int a, b; public double len, width, pave; public string road, cls; }

    readonly List<NavPoint> nodes = new List<NavPoint>();
    readonly List<Edge> edges = new List<Edge>();
    readonly List<List<int>> adj = new List<List<int>>();   // node → edge indices

    public int NodeCount { get { return nodes.Count; } }
    public int EdgeCount { get { return edges.Count; } }

    public LundunRoadGraph(LundunWorldData w)
    {
        Dictionary<string, int> key = new Dictionary<string, int>();
        Dictionary<string, LwJunction> junc = new Dictionary<string, LwJunction>();
        foreach (LwJunction j in w.junctions) junc[j.id] = j;
        Dictionary<string, double> pave = new Dictionary<string, double>();
        foreach (LwRoadClass c in w.roadClasses) pave[c.name] = c.pavement;

        foreach (LwRoad r in w.roads)
        {
            // chainage of every polyline point
            int n = r.points.Length;
            double[] ch = new double[n];
            for (int i = 1; i < n; i++) ch[i] = ch[i - 1] + NavPoint.Dist(P(r.points[i - 1]), P(r.points[i]));

            // stops along the road: junctions + bends, in order
            List<KeyValuePair<double, int>> stops = new List<KeyValuePair<double, int>>();
            for (int k = 0; k < r.junctions.Length; k++)
            {
                LwJunction j = junc[r.junctions[k]];
                stops.Add(new KeyValuePair<double, int>(r.junctionChainage[k], Node(key, "J:" + j.id, new NavPoint(j.x, j.z))));
            }
            for (int i = 1; i < n - 1; i++)
            {
                bool atJunction = false;
                foreach (KeyValuePair<double, int> s in stops) if (Math.Abs(s.Key - ch[i]) < 0.5) { atJunction = true; break; }
                if (!atJunction) stops.Add(new KeyValuePair<double, int>(ch[i], Node(key, "R:" + r.id + ":" + i, P(r.points[i]))));
            }
            stops.Sort((x, y) => x.Key.CompareTo(y.Key));
            for (int s = 0; s + 1 < stops.Count; s++)
            {
                int a = stops[s].Value, b = stops[s + 1].Value;
                if (a == b) continue;
                Edge e = new Edge { a = a, b = b, len = Math.Max(0.01, stops[s + 1].Key - stops[s].Key), width = r.width, pave = pave.ContainsKey(r.roadClass) ? pave[r.roadClass] : 0, road = r.id, cls = r.roadClass };
                adj[a].Add(edges.Count); adj[b].Add(edges.Count);
                edges.Add(e);
            }
        }
    }

    static NavPoint P(LwPoint p) { return new NavPoint(p.x, p.z); }

    int Node(Dictionary<string, int> key, string k, NavPoint p)
    {
        int i;
        if (key.TryGetValue(k, out i)) return i;
        i = nodes.Count; key[k] = i; nodes.Add(p); adj.Add(new List<int>());
        return i;
    }

    /// Closest point on any road centreline. Returns the edge index and how far along it (0..1).
    public NavPoint NearestOnRoad(NavPoint q, out int edge, out double t, out double dist)
    {
        edge = -1; t = 0; dist = double.MaxValue; NavPoint best = q;
        for (int i = 0; i < edges.Count; i++)
        {
            NavPoint a = nodes[edges[i].a], b = nodes[edges[i].b];
            double dx = b.x - a.x, dz = b.z - a.z, l2 = dx * dx + dz * dz;
            double tt = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((q.x - a.x) * dx + (q.z - a.z) * dz) / l2));
            NavPoint p = new NavPoint(a.x + dx * tt, a.z + dz * tt);
            double d = NavPoint.Dist(p, q);
            if (d < dist) { dist = d; edge = i; t = tt; best = p; }
        }
        return best;
    }

    /// A random spot on a road between minD and maxD (straight-line) from centre. dir = along the road.
    /// Used for spawning police / traffic out of sight. Bridges are skipped (no spawning mid-river).
    public bool RandomRoadPoint(NavPoint centre, double minD, double maxD, Random rng, out NavPoint p, out NavPoint dir, out double halfWidth)
    {
        double pavement;
        return RandomRoadPoint(centre, minD, maxD, rng, false, out p, out dir, out halfWidth, out pavement);
    }

    /// needPavement = only roads that have pavements (for pedestrians). pavement = pavement width there.
    public bool RandomRoadPoint(NavPoint centre, double minD, double maxD, Random rng, bool needPavement,
                                out NavPoint p, out NavPoint dir, out double halfWidth, out double pavement)
    {
        int edge; double t; string cls;
        return RandomRoadSpot(centre, minD, maxD, rng, needPavement, null, out p, out dir, out halfWidth, out pavement, out edge, out t, out cls);
    }

    /// The full version: also says which edge (and how far along, 0..1) and the road class.
    /// classes = only these road classes (null = any). Bridges are always skipped.
    public bool RandomRoadSpot(NavPoint centre, double minD, double maxD, Random rng, bool needPavement, string[] classes,
                               out NavPoint p, out NavPoint dir, out double halfWidth, out double pavement,
                               out int edgeIndex, out double along, out string roadClass)
    {
        pavement = 0; edgeIndex = -1; along = 0; roadClass = "";
        p = centre; dir = new NavPoint(0, 1); halfWidth = 5;
        List<int> cand = new List<int>(); double total = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].road.StartsWith("B")) continue;
            if (needPavement && edges[i].pave <= 0) continue;
            if (classes != null && Array.IndexOf(classes, edges[i].cls) < 0) continue;
            NavPoint a = nodes[edges[i].a], b = nodes[edges[i].b];
            double near = SegDist(centre, a, b), far = Math.Max(NavPoint.Dist(centre, a), NavPoint.Dist(centre, b));
            if (near <= maxD && far >= minD) { cand.Add(i); total += edges[i].len; }
        }
        if (cand.Count == 0) return false;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            double pick = rng.NextDouble() * total; int ei = cand[cand.Count - 1];
            foreach (int c in cand) { pick -= edges[c].len; if (pick <= 0) { ei = c; break; } }
            Edge e = edges[ei];
            NavPoint a = nodes[e.a], b = nodes[e.b];
            double t = rng.NextDouble();
            NavPoint q = new NavPoint(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t);
            double d = NavPoint.Dist(q, centre);
            if (d < minD || d > maxD) continue;
            double len = NavPoint.Dist(a, b);
            p = q; dir = new NavPoint((b.x - a.x) / len, (b.z - a.z) / len); halfWidth = e.width / 2; pavement = e.pave;
            edgeIndex = ei; along = t; roadClass = e.cls;
            return true;
        }
        return false;
    }

    // ----- for buses -----
    public int NearestNode(NavPoint q)
    {
        int best = -1; double bd = double.MaxValue;
        for (int i = 0; i < nodes.Count; i++) { double d = NavPoint.Dist(nodes[i], q); if (d < bd) { bd = d; best = i; } }
        return best;
    }

    /// Shortest list of nodes from node s to node t (both included). Empty if unreachable.
    /// banFirst / banLast: a neighbour you may not leave s towards / arrive at t from (stops buses doing U-turns).
    public List<int> NodePath(int s, int t, int banFirst = -1, int banLast = -1)
    {
        int n = nodes.Count;
        double[] dist = new double[n]; int[] prev = new int[n]; bool[] done = new bool[n];
        for (int i = 0; i < n; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
        dist[s] = 0;
        while (true)
        {
            int u = -1; double bu = double.MaxValue;
            for (int i = 0; i < n; i++) if (!done[i] && dist[i] < bu) { bu = dist[i]; u = i; }
            if (u < 0 || u == t) break;
            done[u] = true;
            foreach (int ei in adj[u])
            {
                Edge e = edges[ei]; int v = e.a == u ? e.b : e.a;
                if (u == s && v == banFirst) continue;
                if (v == t && u == banLast) continue;
                if (dist[u] + e.len < dist[v]) { dist[v] = dist[u] + e.len; prev[v] = u; }
            }
        }
        List<int> path = new List<int>();
        if (dist[t] == double.MaxValue) return path;
        for (int v = t; v >= 0; v = prev[v]) { path.Add(v); if (v == s) break; }
        path.Reverse();
        return path;
    }

    /// A loop through the given places (as nodes; the last node leads back to the first), with no U-turns.
    public List<int> LoopRoute(NavPoint[] stops)
    {
        int k = stops.Length;
        int[] at = new int[k];
        for (int i = 0; i < k; i++) at[i] = NearestNode(stops[i]);
        List<int> loop = new List<int>();
        int cameFrom = -1;
        for (int i = 0; i < k; i++)
        {
            bool last = i == k - 1;
            // the last leg must not arrive at the start the same way the first leg left it
            int banLast = last && loop.Count > 1 ? loop[1] : -1;
            List<int> leg = NodePath(at[i], at[(i + 1) % k], cameFrom, banLast);
            if (leg.Count < 2) leg = NodePath(at[i], at[(i + 1) % k]);       // no way round: accept it
            if (leg.Count < 2) continue;
            for (int j = 0; j < leg.Count - 1; j++) loop.Add(leg[j]);
            cameFrom = leg[leg.Count - 2];
        }
        return loop;
    }

    /// Counts places where the loop doubles straight back (should be 0 for buses).
    public static int UTurns(List<int> loop)
    {
        int n = 0;
        for (int i = 0; i < loop.Count; i++) if (loop[(i + loop.Count - 1) % loop.Count] == loop[(i + 1) % loop.Count]) n++;
        return n;
    }

    // ----- for traffic -----
    public NavPoint NodePosition(int n) { return nodes[n]; }
    public void EdgeEnds(int e, out int a, out int b) { a = edges[e].a; b = edges[e].b; }

    /// Nodes you can drive to from 'node', except 'cameFrom' (no U-turns unless it's a dead end).
    public void NextNodes(int node, int cameFrom, List<int> result)
    {
        result.Clear();
        foreach (int ei in adj[node]) { Edge e = edges[ei]; int o = e.a == node ? e.b : e.a; if (o != cameFrom) result.Add(o); }
        if (result.Count == 0 && cameFrom >= 0) result.Add(cameFrom);
    }

    /// Width and class of the road between two neighbouring nodes.
    public bool EdgeInfo(int a, int b, out double width, out string roadClass)
    {
        foreach (int ei in adj[a]) { Edge e = edges[ei]; if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) { width = e.width; roadClass = e.cls; return true; } }
        width = 10; roadClass = "street"; return false;
    }

    static double SegDist(NavPoint q, NavPoint a, NavPoint b)
    {
        double dx = b.x - a.x, dz = b.z - a.z, l2 = dx * dx + dz * dz;
        double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((q.x - a.x) * dx + (q.z - a.z) * dz) / l2));
        return NavPoint.Dist(q, new NavPoint(a.x + dx * t, a.z + dz * t));
    }

    /// Shortest route along the roads from 'from' to 'to'. Includes both end points.
    public List<NavPoint> Route(NavPoint from, NavPoint to, out double metres)
    {
        int ea, eb; double ta, tb, da, db;
        NavPoint sa = NearestOnRoad(from, out ea, out ta, out da);
        NavPoint sb = NearestOnRoad(to, out eb, out tb, out db);
        List<NavPoint> path = new List<NavPoint>();
        metres = 0;
        if (ea < 0 || eb < 0) return path;

        if (ea == eb)
        {
            path.Add(from); path.Add(sa); path.Add(sb); path.Add(to);
            metres = da + NavPoint.Dist(sa, sb) + db;
            return Clean(path);
        }

        // Dijkstra from the two ends of the start edge (graph is ~200 nodes, so the simple version is fine)
        int n = nodes.Count;
        double[] dist = new double[n]; int[] prev = new int[n]; bool[] done = new bool[n];
        for (int i = 0; i < n; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
        Edge se = edges[ea], ee = edges[eb];
        dist[se.a] = se.len * ta; dist[se.b] = se.len * (1 - ta);
        prev[se.a] = -2; prev[se.b] = -2;           // -2 = reached straight from the start point
        while (true)
        {
            int u = -1; double bu = double.MaxValue;
            for (int i = 0; i < n; i++) if (!done[i] && dist[i] < bu) { bu = dist[i]; u = i; }
            if (u < 0) break;
            done[u] = true;
            foreach (int ei in adj[u])
            {
                Edge e = edges[ei];
                int v = e.a == u ? e.b : e.a;
                double nd = dist[u] + e.len;
                if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
            }
        }

        double viaA = dist[ee.a] + ee.len * tb, viaB = dist[ee.b] + ee.len * (1 - tb);
        int last = viaA <= viaB ? ee.a : ee.b;
        if (dist[last] == double.MaxValue) return path;
        metres = da + Math.Min(viaA, viaB) + db;

        List<NavPoint> mid = new List<NavPoint>();
        for (int v = last; v >= 0; v = prev[v]) mid.Add(nodes[v]);
        mid.Reverse();
        path.Add(from); path.Add(sa); path.AddRange(mid); path.Add(sb); path.Add(to);
        return Clean(path);
    }

    static List<NavPoint> Clean(List<NavPoint> p)
    {
        List<NavPoint> r = new List<NavPoint>();
        foreach (NavPoint q in p) if (r.Count == 0 || NavPoint.Dist(r[r.Count - 1], q) > 0.5) r.Add(q);
        return r;
    }
}
