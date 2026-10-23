// Pure maths: turns LundunWorldData into greybox meshes, boxes and markers.
// No UnityEngine types in here, so it can be tested outside Unity.
// LundunGreyboxBuilder.cs turns the result into GameObjects.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public struct V2
    {
        public double x, z;
        public V2(double x, double z) { this.x = x; this.z = z; }
        public static V2 operator +(V2 a, V2 b) { return new V2(a.x + b.x, a.z + b.z); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.x - b.x, a.z - b.z); }
        public static V2 operator *(V2 a, double k) { return new V2(a.x * k, a.z * k); }
        public double Len { get { return Math.Sqrt(x * x + z * z); } }
        public V2 Norm { get { double l = Len; return l < 1e-9 ? new V2(0, 1) : new V2(x / l, z / l); } }
        /// Left of the direction of travel (x east, z north): heading north → west.
        public V2 Left { get { return new V2(-z, x); } }
        public static double Dot(V2 a, V2 b) { return a.x * b.x + a.z * b.z; }
        public static double Dist(V2 a, V2 b) { return (a - b).Len; }
    }

    public struct P3
    {
        public double x, y, z, u, v;
        public P3(double x, double y, double z, double u, double v) { this.x = x; this.y = y; this.z = z; this.u = u; this.v = v; }
        public P3(V2 p, double y) { x = p.x; this.y = y; z = p.z; u = p.x * 0.1; v = p.z * 0.1; }
        public P3(V2 p, double y, double u, double v) { x = p.x; this.y = y; z = p.z; this.u = u; this.v = v; }
    }

    public class MeshData
    {
        public string name, group, material;
        public bool collider;
        public bool navIgnore;      // left out of the NavMesh bake
        public int navArea = -1;    // -1 = default walkable
        public bool chunk = true;   // split into 100 m blocks (each block gets its own lights and is culled separately)
        public List<float> pos = new List<float>();
        public List<float> uv = new List<float>();
        public List<int> tri = new List<int>();
        public int VertexCount { get { return pos.Count / 3; } }

        public MeshData(string name, string group, string material, bool collider)
        { this.name = name; this.group = group; this.material = material; this.collider = collider; }

        int Add(P3 p) { pos.Add((float)p.x); pos.Add((float)p.y); pos.Add((float)p.z); uv.Add((float)p.u); uv.Add((float)p.v); return VertexCount - 1; }

        /// Quad whose front face points along n (winding fixed automatically).
        public void Quad(P3 a, P3 b, P3 c, P3 d, double nx, double ny, double nz)
        {
            double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
            double vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            if (cx * nx + cy * ny + cz * nz < 0) { P3 t = b; b = d; d = t; }
            int i = Add(a), j = Add(b), k = Add(c), l = Add(d);
            tri.Add(i); tri.Add(j); tri.Add(k);
            tri.Add(i); tri.Add(k); tri.Add(l);
        }

        /// L1: a single triangle whose front face points along n (winding fixed automatically).
        public void Tri(P3 a, P3 b, P3 c, double nx, double ny, double nz)
        {
            double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
            double vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            if (cx * nx + cy * ny + cz * nz < 0) { P3 t = b; b = c; c = t; }
            int i = Add(a), j = Add(b), k = Add(c);
            tri.Add(i); tri.Add(j); tri.Add(k);
        }

        /// Convex polygon facing up.
        public void FanUp(List<P3> pts)
        {
            if (pts.Count < 3) return;
            // order clockwise seen from above (= front face up in Unity)
            double area = 0;
            for (int i = 0; i < pts.Count; i++) { P3 a = pts[i], b = pts[(i + 1) % pts.Count]; area += a.x * b.z - b.x * a.z; }
            List<P3> p = new List<P3>(pts);
            if (area > 0) p.Reverse();      // counter-clockwise (x right, z up) → make clockwise
            int s = Add(p[0]);
            int prev = Add(p[1]); P3 pp = p[1];
            for (int i = 2; i < p.Count; i++)
            {
                double a2 = (pp.x - p[0].x) * (p[i].z - p[0].z) - (pp.z - p[0].z) * (p[i].x - p[0].x);
                if (Math.Abs(a2) < 1e-4) continue;      // skip slivers from collinear points
                int cur = Add(p[i]); tri.Add(s); tri.Add(prev); tri.Add(cur); prev = cur; pp = p[i];
            }
        }

        /// Box with its long axis along dir. Bottom face omitted unless asked.
        public void Box(V2 c, V2 dir, double halfLen, double halfWid, double y0, double y1, bool bottom, double uvScale, double uvOffset, bool roofFlat)
        {
            V2 f = dir.Norm, l = f.Left;
            V2 p0 = c - f * halfLen - l * halfWid, p1 = c + f * halfLen - l * halfWid, p2 = c + f * halfLen + l * halfWid, p3 = c - f * halfLen + l * halfWid;
            V2[] ring = { p0, p1, p2, p3 };
            double[] lens = { halfLen * 2, halfWid * 2, halfLen * 2, halfWid * 2 };
            double u = uvOffset;
            for (int i = 0; i < 4; i++)
            {
                V2 a = ring[i], b = ring[(i + 1) % 4];
                V2 n = (b - a).Norm.Left * -1;   // outward for this winding
                V2 mid = (a + b) * 0.5;
                if (V2.Dot(n, mid - c) < 0) n = n * -1;
                double u0 = u * uvScale, u1 = (u + lens[i]) * uvScale;
                double v0 = y0 / 28.0, v1 = y1 / 28.0;
                Quad(new P3(a, y0, u0, v0), new P3(b, y0, u1, v0), new P3(b, y1, u1, v1), new P3(a, y1, u0, v1), n.x, 0, n.z);
                u += lens[i];
            }
            if (roofFlat)
                Quad(new P3(p0, y1, SurfaceArt.RoofU, SurfaceArt.RoofV), new P3(p1, y1, SurfaceArt.RoofU, SurfaceArt.RoofV), new P3(p2, y1, SurfaceArt.RoofU, SurfaceArt.RoofV), new P3(p3, y1, SurfaceArt.RoofU, SurfaceArt.RoofV), 0, 1, 0);   // V4: a clean bit of wall
            else
                Quad(new P3(p0, y1), new P3(p1, y1), new P3(p2, y1), new P3(p3, y1), 0, 1, 0);
            if (bottom) Quad(new P3(p0, y0), new P3(p1, y0), new P3(p2, y0), new P3(p3, y0), 0, -1, 0);
        }
    }

    public class Marker { public string id, group, kind; public double x, y, z, yaw; }
    public class Beacon { public string id, kind; public double x, z, radius, height; }
    public class ColliderBox { public string name; public double cx, cy, cz, sx, sy, sz; }
    public class BuildingInfo { public string district; public V2 c, dir; public double len, dep, h; }
    public class LampInfo { public double x, y, z; public int style; }   // L5 style: 0 sodium, 1 sodium (estate concrete), 2 heritage, 3 LED

    public class GreyboxResult
    {
        public List<MeshData> meshes = new List<MeshData>();
        public List<Marker> markers = new List<Marker>();
        public List<Beacon> beacons = new List<Beacon>();
        public List<ColliderBox> walls = new List<ColliderBox>();
        public List<BuildingInfo> buildings = new List<BuildingInfo>();
        public List<LampInfo> lamps = new List<LampInfo>();
        public List<string> log = new List<string>();
    }

    /// A polyline with cumulative chainage.
    public class Line
    {
        public List<V2> p; public double[] ch;
        public Line(List<V2> pts)
        {
            p = new List<V2>();
            foreach (V2 q in pts) if (p.Count == 0 || V2.Dist(p[p.Count - 1], q) > 1e-4) p.Add(q);
            ch = new double[p.Count];
            for (int i = 1; i < p.Count; i++) ch[i] = ch[i - 1] + V2.Dist(p[i - 1], p[i]);
        }
        public double Length { get { return ch[ch.Length - 1]; } }

        public V2 At(double s, out V2 dir)
        {
            if (s <= 0) { dir = (p[1] - p[0]).Norm; return p[0] + dir * s; }
            for (int i = 1; i < p.Count; i++)
                if (s <= ch[i] || i == p.Count - 1)
                {
                    dir = (p[i] - p[i - 1]).Norm;
                    return p[i - 1] + dir * (s - ch[i - 1]);
                }
            dir = new V2(0, 1); return p[p.Count - 1];
        }

        public List<V2> Sub(double a, double b)
        {
            V2 d; List<V2> r = new List<V2>();
            r.Add(At(a, out d));
            for (int i = 1; i < p.Count - 1; i++) if (ch[i] > a + 1e-3 && ch[i] < b - 1e-3) r.Add(p[i]);
            r.Add(At(b, out d));
            return r;
        }

        public double DistanceTo(V2 q)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < p.Count; i++) best = Math.Min(best, SegDist(q, p[i], p[i + 1]));
            return best;
        }

        public static double SegDist(V2 q, V2 a, V2 b)
        {
            V2 ab = b - a; double l2 = V2.Dot(ab, ab);
            double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
            return V2.Dist(q, a + ab * t);
        }

        /// Parallel polyline at signed distance d (positive = left). Mitred corners.
        public static List<V2> Offset(List<V2> pts, double d)
        {
            List<V2> r = new List<V2>();
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                V2 nrm;
                if (i == 0) nrm = (pts[1] - pts[0]).Norm.Left;
                else if (i == n - 1) nrm = (pts[n - 1] - pts[n - 2]).Norm.Left;
                else
                {
                    V2 n0 = (pts[i] - pts[i - 1]).Norm.Left, n1 = (pts[i + 1] - pts[i]).Norm.Left;
                    V2 m = (n0 + n1).Norm;
                    double k = V2.Dot(m, n1);
                    nrm = m * (1.0 / Math.Max(0.5, k));
                }
                r.Add(pts[i] + nrm * d);
            }
            return r;
        }
    }

    public static partial class Generator
    {
        // ----- tuning -----
        const double RiverHalf = 35, WallOuter = 39, GroundEdge = 37;
        const double GroundTile = 100;
        const double Cell = 2;          // building grid (m)
        const int Seed = 1066;
        /// NavMesh area for roads, pavements, junctions, bridges. Everything else walkable is area 0,
        /// which LundunWorldRuntime makes more expensive so police prefer roads.
        public const int RoadArea = 3;
        /// Pavements get their own area so pedestrians can prefer them (and police cars avoid them).
        public const int PavementArea = 4;

        class RoadInfo
        {
            public LwRoad r; public Line line; public double w, pave, verge; public string cls;
            public List<double[]> boxCut = new List<double[]>();       // chainage intervals inside the existing street
            public List<double[]> boxCutL = new List<double[]>(), boxCutR = new List<double[]>();   // same for each pavement
            public List<double[]> trimL = new List<double[]>(), trimR = new List<double[]>(), trimAll = new List<double[]>();
        }

        class JInfo { public LwJunction j; public V2 c; public double radius; public List<V2> legDir = new List<V2>(); public List<RoadInfo> legRoad = new List<RoadInfo>(); }

        static LundunWorldData W;
        static Dictionary<string, RoadInfo> roads;
        static Dictionary<string, JInfo> juncs;
        static Line river;
        static double boxX0, boxX1, boxZ0, boxZ1;
        static Random rng;

        public static GreyboxResult Build(LundunWorldData world)
        {
            W = world; rng = new Random(Seed);
            ExpansionMode = false;
            GreyboxResult R = new GreyboxResult();
            Prepare();
            PrepareJoins();             // E1: where the expansions' roads meet ours (nothing changes without them)
            BuildGround(R);
            BuildRiver(R);
            BuildRoads(R);
            BuildJunctions(R);
            BuildBridges(R);
            BuildLamps(R);
            BuildParks(R);
            BuildBuildings(R);
            BuildMarkers(R);
            BuildBounds(R);
            BuildRailway(R);            // L6: last, with its own numbers
            List<MeshData> split = new List<MeshData>();
            foreach (MeshData m in R.meshes) { if (m.chunk) split.AddRange(Chunk(m)); else split.Add(m); }
            R.meshes = split;
            int v = 0; foreach (MeshData m in R.meshes) v += m.VertexCount;
            R.log.Add(string.Format("{0} meshes, {1} vertices, {2} buildings, {3} markers", R.meshes.Count, v, R.buildings.Count, R.markers.Count));
            return R;
        }

        // ------------------------------------------------------------------ chunking
        public const double ChunkSize = 100, ChunkOrigin = -300;

        static List<MeshData> Chunk(MeshData m)
        {
            Dictionary<long, MeshData> parts = new Dictionary<long, MeshData>();
            Dictionary<long, Dictionary<int, int>> remap = new Dictionary<long, Dictionary<int, int>>();
            List<long> order = new List<long>();
            for (int t = 0; t + 2 < m.tri.Count; t += 3)
            {
                double cx = 0, cz = 0;
                for (int k = 0; k < 3; k++) { int i = m.tri[t + k]; cx += m.pos[i * 3]; cz += m.pos[i * 3 + 2]; }
                long ix = (long)Math.Floor((cx / 3 - ChunkOrigin) / ChunkSize), iz = (long)Math.Floor((cz / 3 - ChunkOrigin) / ChunkSize);
                long key = ix * 100000 + iz;
                MeshData p; Dictionary<int, int> map;
                if (!parts.TryGetValue(key, out p))
                {
                    p = new MeshData(m.name + "_" + ix + "_" + iz, m.group, m.material, m.collider) { navIgnore = m.navIgnore, navArea = m.navArea, chunk = false };
                    parts[key] = p; remap[key] = new Dictionary<int, int>(); order.Add(key);
                }
                map = remap[key];
                for (int k = 0; k < 3; k++)
                {
                    int i = m.tri[t + k], j;
                    if (!map.TryGetValue(i, out j))
                    {
                        j = p.VertexCount; map[i] = j;
                        p.pos.Add(m.pos[i * 3]); p.pos.Add(m.pos[i * 3 + 1]); p.pos.Add(m.pos[i * 3 + 2]);
                        p.uv.Add(m.uv[i * 2]); p.uv.Add(m.uv[i * 2 + 1]);
                    }
                    p.tri.Add(j);
                }
            }
            List<MeshData> r = new List<MeshData>();
            foreach (long k in order) r.Add(parts[k]);
            return r;
        }

        /// Adds points so no segment is longer than maxSeg (long triangles would ignore the 100 m blocks).
        static List<V2> Densify(List<V2> pts, double maxSeg)
        {
            List<V2> r = new List<V2>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                r.Add(pts[i]);
                double L = V2.Dist(pts[i], pts[i + 1]);
                int n = (int)Math.Ceiling(L / maxSeg);
                for (int k = 1; k < n; k++) r.Add(Lerp(pts[i], pts[i + 1], (double)k / n));
            }
            if (pts.Count > 0) r.Add(pts[pts.Count - 1]);
            return r;
        }

        // ------------------------------------------------------------------ prep
        static LwRoadClass Cls(string name) { foreach (LwRoadClass c in W.roadClasses) if (c.name == name) return c; return null; }
        static V2 Pt(LwPoint p) { return new V2(p.x, p.z); }

        static List<V2> Pts(LwPoint[] a) { List<V2> r = new List<V2>(); foreach (LwPoint p in a) r.Add(Pt(p)); return r; }

        static void Prepare()
        {
            // River centreline extended 400 m past both ends so it cuts the whole ground
            List<V2> rc = Pts(W.river.centreline);
            V2 d0 = (rc[0] - rc[1]).Norm, d1 = (rc[rc.Count - 1] - rc[rc.Count - 2]).Norm;
            rc.Insert(0, rc[0] + d0 * 400); rc.Add(rc[rc.Count - 1] + d1 * 400);
            river = new Line(rc);

            LwExistingStreet es = W.existingStreet;
            boxX0 = es.centreX - es.sizeX / 2 - 4; boxX1 = es.centreX + es.sizeX / 2 + 4;   // Kenney tiles reach 4 m past the footprint
            boxZ0 = es.centreZ - es.sizeZ / 2 - 4; boxZ1 = es.centreZ + es.sizeZ / 2 + 4;

            roads = new Dictionary<string, RoadInfo>();
            foreach (LwRoad r in W.roads)
            {
                LwRoadClass c = Cls(r.roadClass);
                RoadInfo ri = new RoadInfo { r = r, line = new Line(Pts(r.points)), w = r.width, pave = c.pavement, verge = c.verge, cls = r.roadClass };
                // parts inside the existing street prefab are not generated
                bool inside = false; double start = 0; double L = ri.line.Length;
                for (double s = 0; s <= L + 0.5; s += 0.5)
                {
                    V2 d; V2 q = ri.line.At(Math.Min(s, L), out d);
                    bool now = InBox(q);
                    if (now && !inside) { start = s; inside = true; }
                    if (!now && inside) { ri.boxCut.Add(new[] { start, s }); inside = false; }
                }
                if (inside) ri.boxCut.Add(new[] { start, L });
                ri.boxCutL = BoxIntervals(ri, ri.w / 2 + c.pavement);
                ri.boxCutR = BoxIntervals(ri, -(ri.w / 2 + c.pavement));
                roads[r.id] = ri;
            }

            juncs = new Dictionary<string, JInfo>();
            foreach (LwJunction j in W.junctions)
            {
                JInfo ji = new JInfo { j = j, c = new V2(j.x, j.z) };
                double rad = 0;
                foreach (LwLeg leg in j.legs)
                {
                    RoadInfo ri = LegRoad(leg.road);                 // E1: (in an expansion, a city road it joins)
                    double ch = LegChainage(ri, j);
                    double ahead = leg.toward == "end" ? Math.Min(ch + 6, ri.line.Length) : Math.Max(ch - 6, 0);
                    V2 dd; V2 q = ri.line.At(ahead, out dd);
                    ji.legDir.Add((q - ji.c).Norm); ji.legRoad.Add(ri);
                    rad = Math.Max(rad, ri.w / 2 + Math.Max(ri.pave, 0));
                }
                ji.radius = rad + 0.5;
                juncs[j.id] = ji;
            }

            // pavement / marking trims at every junction
            foreach (RoadInfo ri in roads.Values)
            {
                for (int k = 0; k < ri.r.junctions.Length; k++)
                {
                    JInfo ji = juncs[ri.r.junctions[k]];
                    double c = ri.r.junctionChainage[k];
                    double[] iv = { c - ji.radius, c + ji.radius };
                    ri.trimAll.Add(iv);
                    V2 dir; ri.line.At(c, out dir);
                    V2 left = dir.Left;
                    bool tl = false, tr = false;
                    for (int m = 0; m < ji.legDir.Count; m++)
                    {
                        if (ji.legRoad[m] == ri) continue;
                        double dt = V2.Dot(ji.legDir[m], left);
                        if (dt > 0.3) tl = true;
                        if (dt < -0.3) tr = true;
                    }
                    if (tl) ri.trimL.Add(iv);
                    if (tr) ri.trimR.Add(iv);
                }
            }
        }

        /// Chainage intervals where the line offset sideways by 'lateral' is inside the existing street box.
        static List<double[]> BoxIntervals(RoadInfo ri, double lateral)
        {
            List<double[]> r = new List<double[]>();
            bool inside = false; double start = 0, L = ri.line.Length;
            for (double s = 0; s <= L + 0.5; s += 0.5)
            {
                V2 d; V2 q = ri.line.At(Math.Min(s, L), out d);
                bool now = InBox(q + d.Left * lateral);
                if (now && !inside) { start = s - 1; inside = true; }
                if (!now && inside) { r.Add(new[] { start, s + 1 }); inside = false; }
            }
            if (inside) r.Add(new[] { start, L });
            return r;
        }

        static double ChainageAt(RoadInfo ri, string jid)
        {
            for (int k = 0; k < ri.r.junctions.Length; k++) if (ri.r.junctions[k] == jid) return ri.r.junctionChainage[k];
            throw new Exception("Junction " + jid + " not listed on road " + ri.r.id);
        }

        static bool InBox(V2 q) { return q.x > boxX0 && q.x < boxX1 && q.z > boxZ0 && q.z < boxZ1; }

        /// [0, L] minus the given intervals; pieces shorter than minLen dropped.
        static List<double[]> Keep(double L, List<double[]> cuts, double minLen)
        {
            List<double[]> c = new List<double[]>(cuts);
            c.Sort((a, b) => a[0].CompareTo(b[0]));
            List<double[]> r = new List<double[]>();
            double s = 0;
            foreach (double[] iv in c)
            {
                if (iv[0] > s) r.Add(new[] { s, Math.Min(iv[0], L) });
                s = Math.Max(s, iv[1]);
            }
            if (s < L) r.Add(new[] { s, L });
            r.RemoveAll(iv => iv[1] - iv[0] < minLen);
            return r;
        }

        static List<double[]> Concat(params List<double[]>[] lists) { List<double[]> r = new List<double[]>(); foreach (var l in lists) r.AddRange(l); return r; }

        // ------------------------------------------------------------------ ground
        static void BuildGround(GreyboxResult R)
        {
            LwBounds b = W.worldBounds;
            MeshData inner = new MeshData("Ground_Inner", "Ground", "Ground", true);
            MeshData outer = new MeshData("Ground_Outer", "Ground", "Ground", true) { navIgnore = true, chunk = false };
            double nearR = GroundEdge + GroundTile * 0.75;
            for (double tx = b.groundMinX; tx < b.groundMaxX - 1e-6; tx += GroundTile)
                for (double tz = b.groundMinZ; tz < b.groundMaxZ - 1e-6; tz += GroundTile)
                {
                    double tx1 = Math.Min(tx + GroundTile, b.groundMaxX), tz1 = Math.Min(tz + GroundTile, b.groundMaxZ);
                    bool isInner = tx1 > b.wallMinX && tx < b.wallMaxX && tz1 > b.wallMinZ && tz < b.wallMaxZ;
                    MeshData m = isInner ? inner : outer;
                    V2 centre = new V2((tx + tx1) / 2, (tz + tz1) / 2);
                    if (river.DistanceTo(centre) > nearR)
                    {
                        GroundQuad(m, tx, tx1, tz, tz1);
                        continue;
                    }
                    for (double z = tz; z < tz1 - 1e-6; z += 1)
                    {
                        double zc = z + 0.5;
                        bool prevIn = river.DistanceTo(new V2(tx, zc)) >= GroundEdge;
                        double runStart = tx;
                        double step = 1;
                        for (double x = tx + step; x <= tx1 + 1e-6; x += step)
                        {
                            bool cur = river.DistanceTo(new V2(x, zc)) >= GroundEdge;
                            if (cur != prevIn)
                            {
                                double edge = Bisect(x - step, x, zc, prevIn);
                                if (prevIn) GroundQuad(m, runStart, edge, z, z + 1);
                                else runStart = edge;
                                prevIn = cur;
                            }
                        }
                        if (prevIn) GroundQuad(m, runStart, tx1, z, z + 1);
                    }
                }
            R.meshes.Add(inner); R.meshes.Add(outer);
        }

        static double Bisect(double a, double b, double z, bool aIn)
        {
            for (int i = 0; i < 14; i++)
            {
                double m = (a + b) / 2;
                bool mi = river.DistanceTo(new V2(m, z)) >= GroundEdge;
                if (mi == aIn) a = m; else b = m;
            }
            return (a + b) / 2;
        }

        static void GroundQuad(MeshData m, double x0, double x1, double z0, double z1)
        {
            if (x1 - x0 < 0.01) return;
            m.Quad(new P3(new V2(x0, z0), 0), new P3(new V2(x0, z1), 0), new P3(new V2(x1, z1), 0), new P3(new V2(x1, z0), 0), 0, 1, 0);
        }

        // ------------------------------------------------------------------ river
        static void BuildRiver(GreyboxResult R)
        {
            MeshData water = new MeshData("Water", "River", "Water", false) { navIgnore = true };
            Ribbon(water, river.p, GroundEdge + 0.5, -(GroundEdge + 0.5), W.heights.waterSurface);
            R.meshes.Add(water);

            MeshData walls = new MeshData("EmbankmentWalls", "River", "Wall", true) { navIgnore = true };
            double top = W.heights.embankmentWallTop, bed = W.heights.riverBed;
            // deck corridors where the wall is cut for bridges
            List<V2[]> corridors = new List<V2[]>(); List<double> corrHalf = new List<double>();
            foreach (LwBridge br in W.bridges)
            {
                RoadInfo ri = roads[br.roadId];
                V2 a = Pt(br.waterEntry), e = Pt(br.waterExit), dir = (e - a).Norm;
                corridors.Add(new[] { a - dir * 6, e + dir * 6 });
                corrHalf.Add(ri.w / 2 + ri.pave + 0.4 + 0.3);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                // densify: 2 m pieces, mitred at original corners
                List<V2> pin = Line.Offset(river.p, side * RiverHalf), pout = Line.Offset(river.p, side * WallOuter);
                List<V2> pmid = Line.Offset(river.p, side * (RiverHalf + WallOuter) / 2);
                bool open = false;
                V2 lastIn = new V2(), lastOut = new V2();
                for (int i = 0; i + 1 < river.p.Count; i++)
                {
                    double segLen = V2.Dist(river.p[i], river.p[i + 1]);
                    int n = Math.Max(1, (int)Math.Ceiling(segLen / 2));
                    for (int k = 0; k < n; k++)
                    {
                        double t0 = (double)k / n, t1 = (double)(k + 1) / n;
                        V2 a0 = Lerp(pin[i], pin[i + 1], t0), a1 = Lerp(pin[i], pin[i + 1], t1);
                        V2 b0 = Lerp(pout[i], pout[i + 1], t0), b1 = Lerp(pout[i], pout[i + 1], t1);
                        V2 mid = Lerp(pmid[i], pmid[i + 1], (t0 + t1) / 2);
                        bool cut = false;
                        for (int c = 0; c < corridors.Count; c++)
                            if (Line.SegDist(mid, corridors[c][0], corridors[c][1]) < corrHalf[c]) { cut = true; break; }
                        V2 along = (a1 - a0).Norm;
                        if (cut)
                        {
                            if (open) WallCap(walls, lastIn, lastOut, along, top, bed);   // close the end before the gap
                            open = false;
                            continue;
                        }
                        if (!open) WallCap(walls, a0, b0, along * -1, top, bed);
                        open = true;
                        V2 towardRiver = (a0 - b0).Norm;
                        // top
                        walls.Quad(new P3(a0, top), new P3(a1, top), new P3(b1, top), new P3(b0, top), 0, 1, 0);
                        // river face
                        walls.Quad(new P3(a0, bed, 0, bed), new P3(a1, bed, 1, bed), new P3(a1, top, 1, top), new P3(a0, top, 0, top), towardRiver.x, 0, towardRiver.z);
                        // land face (only the part above ground shows)
                        walls.Quad(new P3(b0, -0.5), new P3(b1, -0.5), new P3(b1, top), new P3(b0, top), -towardRiver.x, 0, -towardRiver.z);
                        lastIn = a1; lastOut = b1;
                    }
                }
            }
            R.meshes.Add(walls);
        }

        static void WallCap(MeshData m, V2 a, V2 b, V2 facing, double top, double bed)
        {
            m.Quad(new P3(a, bed), new P3(b, bed), new P3(b, top), new P3(a, top), facing.x, 0, facing.z);
        }

        static V2 Lerp(V2 a, V2 b, double t) { return a + (b - a) * t; }

        /// Flat strip between two signed offsets of a polyline.
        static void Ribbon(MeshData m, List<V2> pts, double offA, double offB, double y)
        {
            if (pts.Count < 2) return;
            pts = Densify(pts, 20);
            List<V2> A = Line.Offset(pts, offA), B = Line.Offset(pts, offB);
            for (int i = 0; i + 1 < pts.Count; i++)
                m.Quad(new P3(A[i], y), new P3(A[i + 1], y), new P3(B[i + 1], y), new P3(B[i], y), 0, 1, 0);
        }

        // ------------------------------------------------------------------ roads
        static void BuildRoads(GreyboxResult R)
        {
            MeshData asphalt = new MeshData("Carriageways", "Roads", "Road", true) { navArea = RoadArea };
            MeshData pave = new MeshData("Pavements", "Roads", "Pavement", true) { navArea = PavementArea };
            MeshData marks = new MeshData("Markings", "Roads", "Marking", false) { navIgnore = true };
            double yRoad = W.heights.roadSurface, yPave = W.heights.pavementTop, yMark = yRoad + 0.02;
            foreach (RoadInfo ri in roads.Values)
            {
                double L = ri.line.Length;
                foreach (double[] iv in Keep(L, ri.boxCut, 0.2))
                    Ribbon(asphalt, ri.line.Sub(iv[0], iv[1]), ri.w / 2, -ri.w / 2, yRoad);

                if (ri.pave > 0)
                    for (int s = 1; s >= -1; s -= 2)
                    {
                        List<double[]> cuts = Concat(ri.boxCut, s > 0 ? ri.trimL : ri.trimR, s > 0 ? ri.boxCutL : ri.boxCutR);
                        foreach (double[] iv in Keep(L, cuts, 1.0))
                            Pavement(pave, ri, iv[0], iv[1], s, yRoad, yPave);
                    }

                // markings: (offset, dashed, width)
                List<double[]> lines = new List<double[]>();
                double hw = ri.w / 2;
                switch (ri.cls)
                {
                    case "ring": lines.Add(new[] { 0.6, 0, 0.15 }); lines.Add(new[] { -0.6, 0, 0.15 }); lines.Add(new[] { 6.0, 1, 0.15 }); lines.Add(new[] { -6.0, 1, 0.15 }); lines.Add(new[] { hw - 0.4, 0, 0.2 }); lines.Add(new[] { -hw + 0.4, 0, 0.2 }); break;
                    case "arterial": lines.Add(new[] { 0.0, 0, 0.15 }); lines.Add(new[] { 3.75, 1, 0.15 }); lines.Add(new[] { -3.75, 1, 0.15 }); lines.Add(new[] { hw - 0.4, 0, 0.2 }); lines.Add(new[] { -hw + 0.4, 0, 0.2 }); break;
                    case "street": lines.Add(new[] { 0.0, 1, 0.12 }); break;
                    case "bridge": lines.Add(new[] { 0.0, 0, 0.15 }); lines.Add(new[] { hw - 0.4, 0, 0.2 }); lines.Add(new[] { -hw + 0.4, 0, 0.2 }); break;
                }
                List<double[]> keep = Keep(L, Concat(ri.boxCut, ri.trimAll, JoinCuts(ri)), 1.0);
                foreach (double[] ln in lines)
                    foreach (double[] iv in keep)
                    {
                        if (ln[1] < 0.5) Ribbon(marks, ri.line.Sub(iv[0], iv[1]), ln[0] + ln[2] / 2, ln[0] - ln[2] / 2, yMark);
                        else
                            for (double s = iv[0] + 1; s + 3 <= iv[1] - 1; s += 9)
                                Ribbon(marks, ri.line.Sub(s, s + 3), ln[0] + ln[2] / 2, ln[0] - ln[2] / 2, yMark);
                    }
            }
            R.meshes.Add(asphalt); R.meshes.Add(pave); R.meshes.Add(marks);
        }

        static void Pavement(MeshData m, RoadInfo ri, double a, double b, int s, double yRoad, double yTop)
        {
            List<V2> c = ri.line.Sub(a, b);
            if (c.Count < 2) return;
            c = Densify(c, 20);
            List<V2> inn = Line.Offset(c, s * ri.w / 2), outr = Line.Offset(c, s * (ri.w / 2 + ri.pave));
            double tile = SurfaceArt.PavingTileM, kv = 0.012, ov = ri.pave / tile;
            double[] ch = new double[c.Count];
            for (int i = 1; i < c.Count; i++) ch[i] = ch[i - 1] + V2.Dist(c[i - 1], c[i]);
            for (int i = 0; i + 1 < c.Count; i++)
            {
                V2 side = (c[i + 1] - c[i]).Norm.Left * s;      // points away from the road
                double u0 = (a + ch[i]) / tile, u1 = (a + ch[i + 1]) / tile;
                m.Quad(new P3(inn[i], yTop, u0, 0), new P3(inn[i + 1], yTop, u1, 0), new P3(outr[i + 1], yTop, u1, ov), new P3(outr[i], yTop, u0, ov), 0, 1, 0);
                m.Quad(new P3(inn[i], yRoad - 0.02, u0, kv), new P3(inn[i + 1], yRoad - 0.02, u1, kv), new P3(inn[i + 1], yTop, u1, kv), new P3(inn[i], yTop, u0, kv), -side.x, 0, -side.z);
                m.Quad(new P3(outr[i], -0.02, u0, ov), new P3(outr[i + 1], -0.02, u1, ov), new P3(outr[i + 1], yTop, u1, ov), new P3(outr[i], yTop, u0, ov), side.x, 0, side.z);
            }
            V2 d0 = (c[1] - c[0]).Norm, d1 = (c[c.Count - 1] - c[c.Count - 2]).Norm;
            m.Quad(new P3(inn[0], -0.02, a / tile, kv), new P3(outr[0], -0.02, a / tile, kv), new P3(outr[0], yTop, a / tile, kv), new P3(inn[0], yTop, a / tile, kv), -d0.x, 0, -d0.z);
            int e = c.Count - 1;
            double ue = (a + ch[e]) / tile;
            m.Quad(new P3(inn[e], -0.02, ue, kv), new P3(outr[e], -0.02, ue, kv), new P3(outr[e], yTop, ue, kv), new P3(inn[e], yTop, ue, kv), d1.x, 0, d1.z);
        }

        // ------------------------------------------------------------------ junctions
        static void BuildJunctions(GreyboxResult R)
        {
            MeshData m = new MeshData("JunctionPlates", "Junctions", "Road", true) { navArea = RoadArea };
            double y = W.heights.roadSurface + 0.005;
            foreach (JInfo ji in juncs.Values)
            {
                if (InBox(ji.c)) continue;
                if (JoinPlate(m, ji.j.id, y)) continue;           // E1: a junction an expansion road joins (the city builds it, wider)
                if (ExpansionMode && TouchesCity(ji)) continue;
                List<V2> pts = new List<V2>();
                for (int k = 0; k < ji.legDir.Count; k++)
                {
                    RoadInfo ri = ji.legRoad[k];
                    V2 d = ji.legDir[k], l = d.Left;
                    double half = ri.w / 2 + ri.pave;
                    V2 end = ji.c + d * ji.radius;
                    pts.Add(end + l * half); pts.Add(end - l * half);
                    pts.Add(ji.c + l * half); pts.Add(ji.c - l * half);   // keeps the plate under the through road
                }
                List<V2> hull = Hull(pts);
                List<P3> poly = new List<P3>();
                foreach (V2 q in hull) poly.Add(new P3(q, y));
                m.FanUp(poly);
            }
            NewJoinPlates(m, y);                                  // E1: new junctions on the city's roads
            R.meshes.Add(m);
        }

        static List<V2> Hull(List<V2> pts)
        {
            List<V2> p = new List<V2>(pts);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
            if (p.Count < 3) return p;
            List<V2> h = new List<V2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    V2 q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        static double Cross(V2 o, V2 a, V2 b) { return (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x); }

        // ------------------------------------------------------------------ bridges
        static void BuildBridges(GreyboxResult R)
        {
            MeshData deck = new MeshData("BridgeDecks", "Bridges", "Concrete", true) { navArea = RoadArea };
            MeshData parapets = new MeshData("BridgeParapets", "Bridges", "Parapet", true) { navIgnore = true };
            foreach (LwBridge br in W.bridges)
            {
                RoadInfo ri = roads[br.roadId];
                V2 a = Pt(br.waterEntry), e = Pt(br.waterExit), dir = (e - a).Norm;
                V2 s0 = a - dir * 6, s1 = e + dir * 6, mid = (s0 + s1) * 0.5;
                double halfLen = V2.Dist(s0, s1) / 2, halfWid = ri.w / 2 + ri.pave + 0.4;
                double top = W.heights.bridgeDeck, th = br.deckThickness > 0 ? br.deckThickness : 1.2;
                deck.Box(mid, dir, halfLen, halfWid, top - th, top, true, 0.1, 0, false);
                for (int k = 1; k <= 2; k++)
                {
                    V2 pc = Lerp(s0, s1, k / 3.0);
                    deck.Box(pc, dir, 1.5, halfWid - 1, W.heights.riverBed, top - th, false, 0.1, 0, false);
                }
                double ph = br.parapetHeight > 0 ? br.parapetHeight : 1.1;
                for (int s = -1; s <= 1; s += 2)
                    parapets.Box(mid + dir.Left * (s * (halfWid - 0.2)), dir, halfLen, 0.2, top, top + ph, false, 0.1, 0, false);
            }
            R.meshes.Add(deck); R.meshes.Add(parapets);
        }

        // ------------------------------------------------------------------ lamps
        /// L5: lamps by district. Same places as ever (the light pool and halos line up with R.lamps); the look changes:
        ///   0 Peckam (and anywhere else)  grey column, box head, orange sodium
        ///   1 Gilmore                     thick concrete column, box head, orange sodium
        ///   2 Knightsbury                 black cast-iron column with a swan-neck arm and a glass lantern, warm white
        ///   3 Canary Quay / Peninsula      slim steel column, flat LED head, cool white
        public static readonly string[] LampPoleMats = { "LampPole", "LampPole_Concrete", "LampPole_Heritage", "LampPole_Steel" };
        public static readonly string[] LampHeadMats = { "LampHead", "LampHead", "LampHead_Heritage", "LampHead_LED" };
        public static readonly string[] LampPoolMats = { "LightPool", "LightPool", "LightPool_Heritage", "LightPool_LED" };

        static void BuildLamps(GreyboxResult R)
        {
            Dictionary<string, MeshData> parts = new Dictionary<string, MeshData>();
            Func<string, string, MeshData> part = (mesh, mat) =>
            {
                MeshData m; if (!parts.TryGetValue(mesh, out m)) { m = new MeshData(mesh, "Lamps", mat, false) { navIgnore = true }; parts[mesh] = m; }
                return m;
            };
            Dictionary<string, List<V2>> dpoly = new Dictionary<string, List<V2>>();
            foreach (LwPoly d in W.districts) dpoly[d.id] = Pts(d.polygon);
            if (!ExpansionMode) railPlan = UseRailway ? MakeRailPlan() : null;      // L6: the railway (no random numbers: nothing else moves); E1: the expansions keep the city's
            railLamps.Clear();
            joinGhostLamps.Clear();
            foreach (RoadInfo ri in roads.Values)
            {
                if (ri.cls == "service") continue;
                double L = ri.line.Length;
                double lateral = ri.pave > 0 ? ri.w / 2 + ri.pave - 0.5 : ri.w / 2 + 1.8;
                for (int s = 1; s >= -1; s -= 2)
                    for (double ch = (s > 0 ? 8 : 24); ch < L - 8; ch += 32)
                    {
                        bool skip = false;
                        foreach (double[] iv in Concat(ri.trimAll, ri.boxCut)) if (ch > iv[0] - 4 && ch < iv[1] + 4) { skip = true; break; }
                        if (skip) continue;
                        V2 dir; V2 c = ri.line.At(ch, out dir);
                        V2 pos = c + dir.Left * (s * lateral);
                        if (InBox(pos) || NearOtherRoad(pos, ri, 1.0)) continue;
                        // E1: an expansion road joins here: no lamp (its spot stays taken, so the dressing doesn't shift)
                        bool joined = false;
                        foreach (double[] iv in JoinCuts(ri)) if (ch > iv[0] - 4 && ch < iv[1] + 4) { joined = true; break; }
                        if (joined) { joinGhostLamps.Add(pos); continue; }
                        V2 toRoad = dir.Left * -s;
                        int style;
                        switch (DistrictAt(pos, dpoly))
                        {
                            case "D_GILMOR": style = 1; break;
                            case "D_KNIGHT": style = 2; break;
                            case "D_CANARY": case "D_CANARY_PEN": style = 3; break;
                            default: style = 0; break;
                        }
                        MeshData poles = part("LampPoles" + (style == 0 ? "" : "_" + style), LampPoleMats[style]);
                        MeshData heads = part("LampHeads" + (style < 2 ? "" : "_" + style), LampHeadMats[style]);
                        MeshData pools = part("LightPools" + (style < 2 ? "" : "_" + style), LampPoolMats[style]);
                        // L6: under a railway bridge the light hangs from the bridge instead (the lamp itself stays, for the lights)
                        bool underRail = railPlan != null && (RailHides(pos) || RailHides(pos + toRoad * 0.9));
                        if (!underRail)
                        switch (style)
                        {
                            case 0:
                                poles.Box(pos, dir, 0.09, 0.09, 0, 6.2, false, 0.1, 0, false);
                                heads.Box(pos + toRoad * 0.7, toRoad, 0.8, 0.22, 5.95, 6.2, true, 0.1, 0, false);
                                break;
                            case 1:
                                poles.Box(pos, dir, 0.14, 0.14, 0, 6.0, false, 0.1, 0, false);
                                poles.Box(pos + toRoad * 0.45, toRoad, 0.45, 0.06, 5.92, 6.05, true, 0.1, 0, false);
                                heads.Box(pos + toRoad * 0.85, toRoad, 0.45, 0.2, 5.85, 6.08, true, 0.1, 0, false);
                                break;
                            case 2:
                                // plinth, fluted column, a collar, the swan-neck arm, a lantern hanging from it
                                poles.Box(pos, dir, 0.17, 0.17, 0, 0.55, false, 0.1, 0, false);
                                poles.Box(pos, dir, 0.11, 0.11, 0.55, 0.9, false, 0.1, 0, false);
                                poles.Box(pos, dir, 0.07, 0.07, 0.9, 5.75, false, 0.1, 0, false);
                                poles.Box(pos, dir, 0.1, 0.1, 4.2, 4.3, false, 0.1, 0, false);
                                poles.Box(pos + toRoad * 0.22, toRoad, 0.22, 0.04, 5.95, 6.05, true, 0.1, 0, false);
                                poles.Box(pos + toRoad * 0.6, toRoad, 0.3, 0.035, 6.02, 6.1, true, 0.1, 0, false);
                                poles.Box(pos + toRoad * 0.1, toRoad, 0.12, 0.035, 5.7, 5.98, true, 0.1, 0, false);
                                V2 lh = pos + toRoad * 0.9;
                                poles.Box(lh, toRoad, 0.03, 0.03, 5.92, 6.06, false, 0.1, 0, false);           // hanger
                                poles.Box(lh, toRoad, 0.21, 0.21, 5.85, 5.93, true, 0.1, 0, false);            // lantern roof
                                heads.Box(lh, toRoad, 0.16, 0.16, 5.38, 5.85, true, 0.1, 0, false);            // glass
                                poles.Box(lh, toRoad, 0.11, 0.11, 5.3, 5.38, true, 0.1, 0, false);             // base
                                break;
                            default:
                                poles.Box(pos, dir, 0.065, 0.065, 0, 6.4, false, 0.1, 0, false);
                                poles.Box(pos + toRoad * 0.35, toRoad, 0.35, 0.04, 6.3, 6.36, true, 0.1, 0, false);
                                heads.Box(pos + toRoad * 0.95, toRoad, 0.42, 0.13, 6.22, 6.3, true, 0.1, 0, false);
                                break;
                        }
                        V2 pc = pos + toRoad * 1.5, f = dir, l = dir.Left;
                        double pr = style == 2 ? PoolRadius * 0.8 : PoolRadius;
                        pools.Quad(new P3(pc - f * pr - l * pr, PoolY, 0, 0), new P3(pc + f * pr - l * pr, PoolY, 1, 0),
                                   new P3(pc + f * pr + l * pr, PoolY, 1, 1), new P3(pc - f * pr + l * pr, PoolY, 0, 1), 0, 1, 0);
                        V2 hp = pos + toRoad * 0.9;
                        R.lamps.Add(new LampInfo { x = hp.x, y = 5.9, z = hp.z, style = style });
                        if (underRail) railLamps.Add(new[] { hp.x, hp.z, style });
                    }
            }
            // the original three meshes always exist (other tools look for them), then the district ones
            part("LampPoles", "LampPole"); part("LampHeads", "LampHead"); part("LightPools", "LightPool");
            List<string> keys = new List<string>(parts.Keys); keys.Sort();
            foreach (string k in keys) R.meshes.Add(parts[k]);
            R.log.Add(R.lamps.Count + " street lamps");
        }

        const double PoolRadius = 9, PoolY = 0.17;

        static bool NearOtherRoad(V2 q, RoadInfo self, double margin)
        {
            foreach (RoadInfo o in roads.Values)
            {
                if (o == self) continue;
                if (o.line.DistanceTo(q) < o.w / 2 + margin) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ parks
        static void BuildParks(GreyboxResult R)
        {
            MeshData grass = new MeshData("ParkGrass", "Parks", "Park", false) { navIgnore = true };
            MeshData trunks = new MeshData("TreeTrunks", "Parks", "Trunk", true) { navIgnore = true };
            MeshData leaves = new MeshData("TreeCanopies", "Parks", "Leaves", false) { navIgnore = true };
            List<ParkPlan> plans = PlanParks();        // room kept clear for the Underground entrance + football cage (R5)
            parkTrees = new List<V2>();
            foreach (LwPoly park in W.parks)
            {
                List<V2> poly = Pts(park.polygon);
                bool square = IsSquare(park);                     // E1: a paved town square, not a park
                List<P3> p3 = new List<P3>(); foreach (V2 q in poly) p3.Add(new P3(q, 0.012));
                if (square) SquarePaving(R, poly); else grass.FanUp(p3);
                double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (V2 q in poly) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
                double area = Math.Abs(PolyArea(poly));
                int want = IsMarket(park) ? 0 : (int)(area / (square ? 520 : 140));      // E3: the market has stalls, not trees
                List<V2> placed = new List<V2>();
                for (int tries = 0; tries < want * 20 && placed.Count < want; tries++)
                {
                    V2 q = new V2(minX + rng.NextDouble() * (maxX - minX), minZ + rng.NextDouble() * (maxZ - minZ));
                    if (!InPoly(q, poly) || EdgeDist(q, poly) < 4) continue;
                    bool close = false; foreach (V2 o in placed) if (V2.Dist(o, q) < 7) { close = true; break; }
                    if (close) continue;
                    placed.Add(q);
                    double yaw = rng.NextDouble() * Math.PI;
                    V2 d = new V2(Math.Sin(yaw), Math.Cos(yaw));
                    double h = 5 + rng.NextDouble() * 4, r = 1.6 + rng.NextDouble() * 1.4;
                    if (InReserved(q, plans, r + 1)) continue;   // same random numbers drawn either way, so buildings don't move
                    parkTrees.Add(q);
                    trunks.Box(q, d, 0.2, 0.2, 0, h * 0.5, false, 0.1, 0, false);
                    leaves.Box(q, d, r, r, h * 0.4, h, true, 0.1, 0, false);
                }
            }
            R.meshes.Add(grass); R.meshes.Add(trunks); R.meshes.Add(leaves);
        }

        static double PolyArea(List<V2> p) { double a = 0; for (int i = 0; i < p.Count; i++) { V2 u = p[i], v = p[(i + 1) % p.Count]; a += u.x * v.z - v.x * u.z; } return a / 2; }

        public static bool InPoly(V2 q, List<V2> p)
        {
            bool c = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
                if (((p[i].z > q.z) != (p[j].z > q.z)) && (q.x < (p[j].x - p[i].x) * (q.z - p[i].z) / (p[j].z - p[i].z) + p[i].x)) c = !c;
            return c;
        }

        static double EdgeDist(V2 q, List<V2> p) { double b = double.MaxValue; for (int i = 0; i < p.Count; i++) b = Math.Min(b, Line.SegDist(q, p[i], p[(i + 1) % p.Count])); return b; }

        // ------------------------------------------------------------------ buildings
        static int NX, NZ; static bool[] blocked, occupied;
        /// E1: where the building grid starts (0, 0 for the city; the expansions start theirs further south).
        static double GX0, GZ0;

        static int CellIndex(V2 q)
        {
            int ix = (int)Math.Floor((q.x - GX0) / Cell), iz = (int)Math.Floor((q.z - GZ0) / Cell);
            if (ix < 0 || iz < 0 || ix >= NX || iz >= NZ) return -1;
            return iz * NX + ix;
        }

        static void MarkSegment(V2 a, V2 b, double r)
        {
            int x0 = Math.Max(0, (int)((Math.Min(a.x, b.x) - r - GX0) / Cell)), x1 = Math.Min(NX - 1, (int)((Math.Max(a.x, b.x) + r - GX0) / Cell));
            int z0 = Math.Max(0, (int)((Math.Min(a.z, b.z) - r - GZ0) / Cell)), z1 = Math.Min(NZ - 1, (int)((Math.Max(a.z, b.z) + r - GZ0) / Cell));
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    V2 c = new V2((ix + 0.5) * Cell + GX0, (iz + 0.5) * Cell + GZ0);
                    if (Line.SegDist(c, a, b) <= r) blocked[iz * NX + ix] = true;
                }
        }

        static void MarkRect(double x0, double x1, double z0, double z1)
        {
            for (int iz = Math.Max(0, (int)((z0 - GZ0) / Cell)); iz <= Math.Min(NZ - 1, (int)((z1 - GZ0) / Cell)); iz++)
                for (int ix = Math.Max(0, (int)((x0 - GX0) / Cell)); ix <= Math.Min(NX - 1, (int)((x1 - GX0) / Cell)); ix++)
                    blocked[iz * NX + ix] = true;
        }

        class Rule { public double lenMin, lenMax, depMin, depMax, hMin, hMax, gap; public bool square; }

        static Rule RuleFor(string districtId, V2 c, bool towerZone)
        {
            switch (districtId)
            {
                case "D_PECKAM": return new Rule { lenMin = 18, lenMax = 42, depMin = 10, depMax = 13, hMin = 9, hMax = 12, gap = 2 };
                case "D_KNIGHT":
                    if (towerZone && rng.NextDouble() < 0.45) return new Rule { lenMin = 24, lenMax = 32, square = true, hMin = 60, hMax = 120, gap = 6 };
                    return new Rule { lenMin = 24, lenMax = 44, depMin = 18, depMax = 28, hMin = 20, hMax = 28, gap = 3 };
                case "D_GILMOR": return new Rule { lenMin = 50, lenMax = 60, depMin = 12, depMax = 12, hMin = 15, hMax = 15, gap = 8 };
                case "D_CANARY": return new Rule { lenMin = 36, lenMax = 44, depMin = 18, depMax = 22, hMin = 12, hMax = 18, gap = 5 };
                case "D_CANARY_PEN": return new Rule { lenMin = 24, lenMax = 26, square = true, hMin = 70, hMax = 140, gap = 10 };
            }
            return null;
        }

        static void BuildBuildings(GreyboxResult R)
        {
            GX0 = 0; GZ0 = 0;
            NX = (int)Math.Ceiling(1600 / Cell); NZ = (int)Math.Ceiling(1200 / Cell);
            if (ExpansionMode) { GX0 = ExpGridX0; GZ0 = ExpGridZ0; NX = (int)Math.Ceiling(ExpGridW / Cell); NZ = (int)Math.Ceiling(ExpGridH / Cell); }
            blocked = new bool[NX * NZ]; occupied = new bool[NX * NZ];
            foreach (RoadInfo ri in roads.Values)
            {
                double r = ri.w / 2 + Math.Max(ri.pave, ri.verge) + 1.5;
                for (int i = 0; i + 1 < ri.line.p.Count; i++) MarkSegment(ri.line.p[i], ri.line.p[i + 1], r);
            }
            for (int i = 0; i + 1 < river.p.Count; i++) MarkSegment(river.p[i], river.p[i + 1], WallOuter + 2);
            foreach (LwPoly park in W.parks)
            {
                List<V2> poly = Pts(park.polygon);
                for (int k = 0; k < NX * NZ; k++)
                {
                    V2 c = new V2((k % NX + 0.5) * Cell + GX0, (k / NX + 0.5) * Cell + GZ0);
                    if (InPoly(c, poly) || EdgeDist(c, poly) < 2) blocked[k] = true;
                }
            }
            if (!ExpansionMode)
            {
                MarkRect(boxX0 - 10, boxX1 + 10, boxZ0 - 10, boxZ1 + 10);
                MarkRect(300, 360, 355, 405);                                   // estate courtyard (M1 on foot)
            }
            else ExpansionBlocks(R);                                            // E1: the railway, the city's roads, the special buildings
            foreach (LwPoi p in W.pois) if (!p.id.StartsWith("LM_")) MarkSegment(new V2(p.x, p.z), new V2(p.x, p.z), 12);
            foreach (LwSpawn s in W.spawns) MarkSegment(new V2(s.x, s.z), new V2(s.x, s.z), 6);

            Dictionary<string, List<V2>> dpoly = new Dictionary<string, List<V2>>();
            foreach (LwPoly d in W.districts) dpoly[d.id] = Pts(d.polygon);
            V2 circus = new V2(560, 920);
            foreach (LwJunction j in W.junctions) if (j.id == "J10") circus = new V2(j.x, j.z);

            // 1. Estate tower (one, fixed): first free spot spiralling out from the west of the estate
            if (!ExpansionMode)
            {
                List<V2> est; if (dpoly.TryGetValue("D_GILMOR", out est))
                    for (int k = 0; k < 400; k++)
                    {
                        double ang = k * 0.7, rad = k * 0.6;
                        V2 c = new V2(250 + Math.Cos(ang) * rad, 420 + Math.Sin(ang) * rad);
                        if (TryPlace(R, "D_GILMOR", est, c, new V2(0, 1), 20, 20, 60, 8)) break;
                    }
            }

            // 2. Frontage: buildings lined up along both sides of every road
            foreach (RoadInfo ri in roads.Values)
            {
                if (ri.cls == "bridge") continue;
                double L = ri.line.Length;
                for (int s = 1; s >= -1; s -= 2)
                {
                    double ch = 4;
                    while (ch < L - 4)
                    {
                        V2 dir; V2 p = ri.line.At(ch, out dir);
                        string did = DistrictAt(p + dir.Left * (s * (ri.w / 2 + 20)), dpoly);
                        if (did == null) { ch += 8; continue; }
                        bool tz = V2.Dist(p, circus) < 200;
                        Rule rule = RuleFor(did, p, tz);
                        double len = Range(rule.lenMin, rule.lenMax);
                        double dep = rule.square ? len : Range(rule.depMin, rule.depMax);
                        double h = Range(rule.hMin, rule.hMax);
                        V2 d2; V2 mid = ri.line.At(Math.Min(ch + len / 2, L), out d2);
                        double setback = ri.w / 2 + Math.Max(ri.pave, ri.verge) + 2.2 + dep / 2 + rng.NextDouble() * 1.5;
                        V2 c = mid + d2.Left * (s * setback);
                        if (rng.NextDouble() > (ExpansionMode ? ExpEmptyLots : 0.08) && TryPlace(R, did, dpoly[did], c, d2, len, dep, h, rule.gap))
                            ch += len + rule.gap + rng.NextDouble() * 3;
                        else ch += 4;
                    }
                }
            }

            // 3. Fill: random interior plots facing the nearest road
            foreach (LwPoly d in W.districts)
            {
                List<V2> poly = dpoly[d.id];
                double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (V2 q in poly) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
                for (int t = 0; t < 2500; t++)
                {
                    V2 c = new V2(minX + rng.NextDouble() * (maxX - minX), minZ + rng.NextDouble() * (maxZ - minZ));
                    if (!InPoly(c, poly)) continue;
                    int ci = CellIndex(c); if (ci < 0 || blocked[ci] || occupied[ci]) continue;
                    bool tz = V2.Dist(c, circus) < 200;
                    Rule rule = RuleFor(d.id, c, tz);
                    double len = Range(rule.lenMin, rule.lenMax);
                    double dep = rule.square ? len : Range(rule.depMin, rule.depMax);
                    TryPlace(R, d.id, poly, c, NearestRoadDir(c), len, dep, Range(rule.hMin, rule.hMax), rule.gap);
                }
            }

            int cleared = ExpansionMode ? 0 : ClearPlots(R);                   // R7: room for hospitals, police stations, safehouses
            if (cleared > 0) R.log.Add("  " + cleared + " buildings cleared for hospitals / police stations / safehouses");

            // L1: the building kit (terraces, parades, estates, towers…) on exactly these plots. The plain boxes below
            // are kept as the fallback (Generator.UseBuildingKit = false).
            if (UseBuildingKit)
            {
                BuildKit(R);
                foreach (BuildingInfo b in R.buildings) rng.Next(0, 8);   // the boxes drew one number each: keep the sequence identical for everything built after
                Dictionary<string, int> perK = new Dictionary<string, int>();
                foreach (BuildingInfo b in R.buildings) { int n; perK.TryGetValue(b.district, out n); perK[b.district] = n + 1; }
                foreach (var kv in perK) R.log.Add("  " + kv.Key + ": " + kv.Value + " buildings");
                return;
            }

            // meshes: one per district material variant per 200 m chunk
            Dictionary<string, MeshData> chunks = new Dictionary<string, MeshData>();
            foreach (BuildingInfo b in R.buildings)
            {
                int variant = (int)(Math.Abs(b.c.x * 7.13 + b.c.z * 3.71)) % 3;
                string mat = "Bld_" + b.district.Replace("D_", "") + "_" + variant;
                string key = mat + "_" + (int)Math.Floor((b.c.x - ChunkOrigin) / ChunkSize) + "_" + (int)Math.Floor((b.c.z - ChunkOrigin) / ChunkSize);
                MeshData m;
                if (!chunks.TryGetValue(key, out m)) { m = new MeshData("Buildings_" + key, "Buildings", mat, true) { navIgnore = false, chunk = false }; chunks[key] = m; }
                m.Box(b.c, b.dir, b.len / 2, b.dep / 2, 0, b.h, false, 1.0 / 32.0, rng.Next(0, 8) * 4, true);
            }
            List<string> keys = new List<string>(chunks.Keys); keys.Sort();
            foreach (string k in keys) R.meshes.Add(chunks[k]);
            Dictionary<string, int> perD = new Dictionary<string, int>();
            foreach (BuildingInfo b in R.buildings) { int n; perD.TryGetValue(b.district, out n); perD[b.district] = n + 1; }
            foreach (var kv in perD) R.log.Add("  " + kv.Key + ": " + kv.Value + " buildings");
        }

        static double Range(double a, double b) { return a + rng.NextDouble() * (b - a); }

        static string DistrictAt(V2 q, Dictionary<string, List<V2>> dpoly)
        {
            foreach (var kv in dpoly) if (InPoly(q, kv.Value)) return kv.Key;
            return null;
        }

        static V2 NearestRoadDir(V2 q)
        {
            double best = double.MaxValue; V2 dir = new V2(1, 0);
            foreach (RoadInfo ri in roads.Values)
                for (int i = 0; i + 1 < ri.line.p.Count; i++)
                {
                    double d = Line.SegDist(q, ri.line.p[i], ri.line.p[i + 1]);
                    if (d < best) { best = d; dir = (ri.line.p[i + 1] - ri.line.p[i]).Norm; }
                }
            return dir;
        }

        static bool TryPlace(GreyboxResult R, string district, List<V2> poly, V2 c, V2 dir, double len, double dep, double h, double gap)
        {
            V2 f = dir.Norm, l = f.Left;
            int nu = Math.Max(2, (int)Math.Ceiling(len / 1.5)), nv = Math.Max(2, (int)Math.Ceiling(dep / 1.5));
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    V2 q = c + f * (-len / 2 + len * i / nu) + l * (-dep / 2 + dep * j / nv);
                    int ci = CellIndex(q);
                    if (ci < 0 || blocked[ci] || occupied[ci]) return false;
                    if (!InPoly(q, poly)) return false;
                }
            // claim footprint + half the gap all round
            double eu = len / 2 + gap / 2, ev = dep / 2 + gap / 2;
            int mu = (int)Math.Ceiling(eu * 2 / 1.0), mv = (int)Math.Ceiling(ev * 2 / 1.0);
            for (int i = 0; i <= mu; i++)
                for (int j = 0; j <= mv; j++)
                {
                    V2 q = c + f * (-eu + 2 * eu * i / mu) + l * (-ev + 2 * ev * j / mv);
                    int ci = CellIndex(q); if (ci >= 0) occupied[ci] = true;
                }
            R.buildings.Add(new BuildingInfo { district = district, c = c, dir = f, len = len, dep = dep, h = h });
            return true;
        }

        // ------------------------------------------------------------------ markers
        static void BuildMarkers(GreyboxResult R)
        {
            foreach (LwSpawn s in W.spawns)
                R.markers.Add(new Marker { id = s.id, group = s.id.StartsWith("SP_POLICE") ? "PoliceSpawns" : "Spawns", kind = "spawn", x = s.x, y = 1.0, z = s.z, yaw = s.yaw });
            foreach (LwPoi p in W.pois)
            {
                R.markers.Add(new Marker { id = p.id, group = "POIs", kind = p.kind, x = p.x, y = 0, z = p.z, yaw = p.yaw });
                bool lm = p.id.StartsWith("LM_");
                R.beacons.Add(new Beacon { id = p.id, kind = p.kind, x = p.x, z = p.z, radius = lm ? 3 : 1.2, height = lm ? 160 : 45 });
            }

            // Extra police spawn points: every ~150 m on the left lane, away from junctions
            int n = 0;
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.cls == "bridge" || ri.cls == "service") continue;
                double L = ri.line.Length; bool flip = false;
                for (double ch = 75; ch < L - 45; ch += 150)
                {
                    bool near = false;
                    foreach (double c in ri.r.junctionChainage) if (Math.Abs(c - ch) < 45) { near = true; break; }
                    if (near) continue;
                    V2 dir; V2 p = ri.line.At(ch, out dir);
                    if (InBox(p)) continue;
                    if (flip) dir = dir * -1;
                    flip = !flip;
                    V2 pos = p + dir.Left * (ri.w / 4);       // UK: drive on the left
                    double yaw = Math.Atan2(dir.x, dir.z) * 180 / Math.PI;
                    if (yaw < 0) yaw += 360;
                    n++;
                    R.markers.Add(new Marker { id = "SP_POLICE_AUTO_" + n.ToString("00"), group = "PoliceSpawns", kind = "spawn", x = pos.x, y = 1.0, z = pos.z, yaw = Math.Round(yaw, 1) });
                }
            }
            R.log.Add(n + " extra police spawn points");
        }

        // ------------------------------------------------------------------ world edge
        static void BuildBounds(GreyboxResult R)
        {
            LwBounds b = W.worldBounds;
            MeshData hedge = new MeshData("EdgeHedge", "Bounds", "Hedge", false) { navIgnore = true, chunk = false };
            double x0 = b.wallMinX, x1 = b.wallMaxX, z0 = b.wallMinZ, z1 = b.wallMaxZ, t = 1.5;
            V2[][] sides = {
                new[] { new V2(x0, z0), new V2(x1, z0) }, new[] { new V2(x1, z0), new V2(x1, z1) },
                new[] { new V2(x1, z1), new V2(x0, z1) }, new[] { new V2(x0, z1), new V2(x0, z0) } };
            string[] names = { "South", "East", "North", "West" };
            for (int i = 0; i < 4; i++)
            {
                V2 a = sides[i][0], e = sides[i][1], mid = (a + e) * 0.5, dir = (e - a).Norm;
                double half = V2.Dist(a, e) / 2 + t;
                // cut the hedge where the river leaves the world
                List<double[]> gaps = new List<double[]>();
                for (double s = -half; s <= half; s += 1)
                    if (river.DistanceTo(mid + dir * s) < WallOuter + 1) gaps.Add(new[] { s + half - 1, s + half + 1 });
                foreach (double[] iv in Keep(2 * half, gaps, 1))
                {
                    V2 c = mid + dir * ((iv[0] + iv[1]) / 2 - half);
                    hedge.Box(c, dir, (iv[1] - iv[0]) / 2, t, -0.5, 3.5, false, 0.1, 0, false);
                }
                R.walls.Add(new ColliderBox { name = "Edge_" + names[i], cx = mid.x, cy = 10, cz = mid.z, sx = Math.Abs(dir.x) > 0.5 ? 2 * half : 2 * t, sy = 40, sz = Math.Abs(dir.z) > 0.5 ? 2 * half : 2 * t });
            }
            R.meshes.Add(hedge);
        }
    }
}
