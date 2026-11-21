// G6a: the new Underground entrances (Lundun → 38). Pure maths like the rest of the dressing: no UnityEngine types.
// Run PlaceTubeEntrances() straight after a Dress(...) of the area a station belongs to (the city, Brixtun or
// Kingsmere). For each new station it looks round its spot on the map for the nearest place where the steps fit:
// just behind a pavement (stairs heading away from the road, the open end on the pavement), clear of every building,
// road, pavement, lamp, tree, bench, railing and the old high street, and not in the water. Then it builds the
// entrance in the same look as the park ones (tiled walls, railings, name board over the open end, the totem), on
// its own little texture. Nothing Dress made moves or changes (it draws no random numbers of Dress's).
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class TubeSite
    {
        public string name; public double x, z; public int area;      // area: 0 the city, 1 Brixtun, 2 Kingsmere
        public TubeSite(string n, double x, double z, int area) { name = n; this.x = x; this.z = z; this.area = area; }
    }

    public class TubeEntrance
    {
        public string name; public int area;
        public V2 open, f;                 // the open end (on the pavement) and the way down (away from the road)
        public double y, off;              // ground height there; how far it is from the spot on the plan
        public string road;
    }

    public class TubeEntranceResult
    {
        public List<TubeEntrance> entrances = new List<TubeEntrance>();
        public List<string> missed = new List<string>();
        public List<MeshData> meshes = new List<MeshData>();
        public List<DressBox> colliders = new List<DressBox>();
        public DressAtlas atlas;
        public List<string> log = new List<string>();
        internal MeshData solid, cut, glow;
    }

    public static partial class Generator
    {
        public const string TubeSolidMat = "TubeEntranceSolid", TubeCutMat = "TubeEntranceCutout", TubeGlowMat = "TubeEntranceGlow";

        /// The nine new stations (G6 plan), roughly where they go; each one is fitted to the nearest place that works.
        public static readonly TubeSite[] NewTubeSites =
        {
            new TubeSite("KNIGHTSBURY CIRCUS", 560, 920, 0),
            new TubeSite("QUAY CROSS", 1240, 850, 0),
            new TubeSite("RIVER BRIDGE", 804, 653, 0),
            new TubeSite("PECKAM HIGH ST", 800, 330, 0),
            new TubeSite("ANGEL FIELDS", 86, -370, 1),
            new TubeSite("BROCKMERE PARK", 560, -810, 1),
            new TubeSite("FERNDALE CIRCUS", 1200, -480, 1),
            new TubeSite("KINGSMERE PALACE", 560, 1660, 2),
            new TubeSite("ST JARVIS'S PARK", 312, 1389, 2),
        };

        // the entrance's footprint (the same size as the park ones): stairs 9 m long, walls 4.8 m apart
        const double TubeHL = 4.5, TubeHW = 2.4;

        public static DressAtlas TubeEntranceAtlas()
        {
            DressAtlas a = new DressAtlas(2048, 2048, 3301);
            foreach (TubeSite s in NewTubeSites) a.StationBoard("ug_" + s.name, s.name);
            a.NewShelf();
            a.UndergroundLogo("ug_logo");
            a.Stairwell("stairs");
            a.Railing("railing");
            a.NewShelf();
            a.Swatch("slab", Col.Hex("9a968e"));
            a.Swatch("tile", Col.Hex("d8d4c8"));
            a.Swatch("navy", Col.Hex("1b2766"));
            a.Swatch("black", Col.Hex("1a1a1a"));
            a.Swatch("charcoal", Col.Hex("3a3a3a"));
            return a;
        }

        /// Call straight after Dress(...) of `area`, with that area's world and dressing meshes (to see what's really on
        /// the ground there). Places and builds that area's new stations into `into`.
        public static void PlaceTubeEntrances(TubeEntranceResult into, int area, List<MeshData> world, List<MeshData> dressing)
        {
            if (roads == null || occ == null || blds == null) throw new Exception("Run Dress(...) first.");
            List<V2> spots = new List<V2>();
            foreach (TubeSite s in NewTubeSites) if (s.area == area) spots.Add(new V2(s.x, s.z));
            if (spots.Count == 0) return;
            TubeGround(spots, world, dressing);
            if (into.atlas == null)
            {
                into.atlas = TubeEntranceAtlas();
                into.solid = new MeshData("TubeEntrances", "Tube", TubeSolidMat, false) { navIgnore = true };
                into.cut = new MeshData("TubeEntranceRails", "Tube", TubeCutMat, false) { navIgnore = true };
                into.glow = new MeshData("TubeEntranceSigns", "Tube", TubeGlowMat, false) { navIgnore = true };
            }
            foreach (TubeSite s in NewTubeSites)
            {
                if (s.area != area) continue;
                TubeEntrance e = FindTubeSpot(s);
                if (e == null) { into.missed.Add(s.name); into.log.Add("  " + s.name + ": no room found near its spot (left out)"); continue; }
                BuildTubeEntrance(into, e);
                into.entrances.Add(e);
                into.log.Add(string.Format("  {0}: by {1}, {2:0} m from its spot on the plan", s.name, e.road, e.off));
            }
        }

        /// The nearest place to the site where the steps fit, just behind a pavement (null if none within 160 m).
        static TubeEntrance FindTubeSpot(TubeSite s)
        {
            V2 target = new V2(s.x, s.z);
            List<KeyValuePair<double, object[]>> cands = new List<KeyValuePair<double, object[]>>();
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.pave < 1.4 || ri.cls == "bridge" || ri.cls == "ring") continue;
                if (ri.line.DistanceTo(target) > 250) continue;
                for (int side = 1; side >= -1; side -= 2)
                {
                    List<double[]> ok = Stretches(ri, side, 9, 8);
                    for (double ch = 4; ch < ri.line.Length - 4; ch += 1.5)
                    {
                        if (!Inside(ch, ok, 5.5)) continue;
                        V2 dir; V2 at = ri.line.At(ch, out dir);
                        V2 left = dir.Left * side;
                        V2 edge = at + left * (ri.w / 2 + ri.pave);              // the back of the pavement
                        double d = V2.Dist(edge, target);
                        if (d > 240) continue;
                        double score = d - (ri.cls == "arterial" ? 6 : 0);       // a main road's better, if it's near
                        cands.Add(new KeyValuePair<double, object[]>(score + cands.Count * 1e-9, new object[] { ri, edge, left, id, d }));
                    }
                }
            }
            cands.Sort((x, y) => x.Key.CompareTo(y.Key));                       // nearest first: the first that fits wins
            foreach (KeyValuePair<double, object[]> kv in cands)
            {
                RoadInfo ri = (RoadInfo)kv.Value[0]; V2 edge = (V2)kv.Value[1], left = (V2)kv.Value[2];
                if (!TubeFits(ri, edge, left)) continue;
                string id = (string)kv.Value[3];
                return new TubeEntrance { name = s.name, area = s.area, open = edge, f = left, y = TubeFloorY(edge, left), off = (double)kv.Value[4], road = string.IsNullOrEmpty(ri.r.name) ? id : ri.r.name };
            }
            return null;
        }

        /// Room for the stairs behind the pavement at `edge` (heading `f` away from the road), and a clear way in.
        static bool TubeFits(RoadInfo ri, V2 edge, V2 f)
        {
            V2 l = f.Left;
            // the stairwell and its walls, plus half a metre round
            for (double a = 0.2; a <= TubeHL * 2 + 0.7; a += 0.6)
                for (double b = -(TubeHW + 0.7); b <= TubeHW + 0.71; b += 0.6)
                {
                    V2 q = edge + f * a + l * b;
                    if (InBuilding(q, 0.5) || InBox(q)) return false;
                    if (a >= 0.4 && TubeBadAt(q)) return false;                          // a road, a car park, water, something standing
                    foreach (RoadInfo o in roads.Values)
                        if (o.line.DistanceTo(q) < o.w / 2 + Math.Max(o.pave, o.verge) + (o == ri ? -0.05 : 0.6)) return false;
                    if (!Free(q, 0.35)) return false;
                    if (Kingsmere && KingsmereKeepsClear(q, 0.5)) return false;
                    foreach (V2 t in parkTrees) if (V2.Dist(t, q) < 1.6) return false;
                }
            // the way in from the pavement (the header's posts, the totem beside it) clear of lamps, trees, bins
            for (double b = -(TubeHW + 1.6); b <= TubeHW + 1.61; b += 0.6)
                for (double a = -1.2; a <= 0.0; a += 0.6)
                    if (!Free(edge + f * a + l * b, 0.25)) return false;
            return true;
        }

        // ------------------------------------------------------------------ what's really on the ground
        // A quarter-metre grid round the sites, from the area's own meshes: "bad" where there's a road (car parks too),
        // a pavement, water, a building, markings or anything standing up; and the height of the flat ground elsewhere.
        const double TubeCell = 0.25;
        static readonly HashSet<long> tubeBad = new HashSet<long>();
        static readonly Dictionary<long, double> tubeFloor = new Dictionary<long, double>();
        static long TubeKey(double x, double z) { return ((long)Math.Floor(x / TubeCell) + 1000000L) * 4000000L + ((long)Math.Floor(z / TubeCell) + 1000000L); }
        static bool TubeBadAt(V2 q) { return tubeBad.Contains(TubeKey(q.x, q.z)); }

        static void TubeGround(List<V2> spots, List<MeshData> world, List<MeshData> dressing)
        {
            tubeBad.Clear(); tubeFloor.Clear();
            const double reach = 270;
            foreach (List<MeshData> list in new[] { world, dressing })
            {
                if (list == null) continue;
                foreach (MeshData m in list)
                {
                    string mat = m.material ?? "";
                    if (mat.StartsWith("LightPool") || mat.StartsWith("Tube") || mat.StartsWith("Autumn")) continue;   // light on the ground; ours
                    bool alwaysBad = mat == "Road" || mat == "Pavement" || mat == "Water" || mat == "Marking" || mat.StartsWith("Bld_") ||
                                     mat == "Wall" || mat == "Hedge" || mat == "Parapet" || mat.Contains("Rail");
                    for (int t = 0; t + 2 < m.tri.Count; t += 3)
                    {
                        int i0 = m.tri[t], i1 = m.tri[t + 1], i2 = m.tri[t + 2];
                        double ax = m.pos[i0 * 3], ay = m.pos[i0 * 3 + 1], az = m.pos[i0 * 3 + 2];
                        double bx = m.pos[i1 * 3], by = m.pos[i1 * 3 + 1], bz = m.pos[i1 * 3 + 2];
                        double cx = m.pos[i2 * 3], cy = m.pos[i2 * 3 + 1], cz = m.pos[i2 * 3 + 2];
                        double x0 = Math.Min(ax, Math.Min(bx, cx)), x1 = Math.Max(ax, Math.Max(bx, cx));
                        double z0 = Math.Min(az, Math.Min(bz, cz)), z1 = Math.Max(az, Math.Max(bz, cz));
                        bool near = false;
                        foreach (V2 s in spots) if (x1 > s.x - reach && x0 < s.x + reach && z1 > s.z - reach && z0 < s.z + reach) { near = true; break; }
                        if (!near) continue;
                        double top = Math.Max(ay, Math.Max(by, cy)), low = Math.Min(ay, Math.Min(by, cy));
                        if (low > 6.0) continue;                                                       // high up (roofs, canopies above head height)
                        bool bad = alwaysBad || top > 0.1;
                        double den = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
                        if (Math.Abs(den) < 1e-9) continue;
                        for (double x = Math.Floor(x0 / TubeCell) * TubeCell; x <= x1; x += TubeCell)
                            for (double z = Math.Floor(z0 / TubeCell) * TubeCell; z <= z1; z += TubeCell)
                            {
                                double px = x + TubeCell / 2, pz = z + TubeCell / 2;
                                double w0 = ((bz - cz) * (px - cx) + (cx - bx) * (pz - cz)) / den, w1 = ((cz - az) * (px - cx) + (ax - cx) * (pz - cz)) / den;
                                double w2 = 1 - w0 - w1;
                                bool inside = w0 >= -0.02 && w1 >= -0.02 && w2 >= -0.02;
                                bool thin = (x1 - x0) < TubeCell * 1.5 || (z1 - z0) < TubeCell * 1.5;   // posts, rails: mark their whole box
                                if (!inside && !(thin && bad)) continue;
                                long k = TubeKey(px, pz);
                                if (bad) tubeBad.Add(k);
                                else
                                {
                                    double y = w0 * ay + w1 * by + w2 * cy, old;
                                    if (!tubeFloor.TryGetValue(k, out old) || y > old) tubeFloor[k] = y;
                                }
                            }
                    }
                }
            }
        }

        /// The flat ground's height under the stairwell (the highest bit of it, so nothing pokes through).
        static double TubeFloorY(V2 edge, V2 f)
        {
            V2 l = f.Left; double y = 0.012, h;
            for (double a = 0.4; a <= TubeHL * 2; a += 0.5)
                for (double b = -TubeHW; b <= TubeHW; b += 0.5)
                {
                    V2 q = edge + f * a + l * b;
                    if (tubeFloor.TryGetValue(TubeKey(q.x, q.z), out h) && h > y) y = h;
                }
            return Math.Min(y, 0.09) + 0.004;
        }

        /// The entrance itself (the same look as the park ones in LundunDressingGeometry.Station).
        static void BuildTubeEntrance(TubeEntranceResult R, TubeEntrance e)
        {
            DressAtlas A2 = R.atlas;
            V2 f = e.f.Norm, l = f.Left, open = e.open, c = open + f * TubeHL;
            double y = e.y;
            MeshData S = R.solid, C = R.cut, G = R.glow;
            UVRect tile = A2["tile"], navy = A2["navy"], black = A2["black"];
            HQuad(S, c - f * 0.6, f, TubeHL + 0.9, TubeHW + 0.4, y + 0.02, A2["slab"]);                   // paving round it
            HQuad(S, c, f, TubeHL - 0.3, TubeHW - 0.4, y + 0.04, A2["stairs"]);                           // the steps going down
            foreach (int s in new[] { -1, 1 })
            {
                V2 wc = c + l * (s * (TubeHW - 0.2));
                SBox(S, wc, f, TubeHL, 0.2, y, y + 0.6, tile);
                TubeRailing(R, wc - f * TubeHL, wc + f * TubeHL, y + 0.6, 0.8);
                TubeBox(R, wc, f, TubeHL * 2, 0.4, y, y + 1.4);
            }
            SBox(S, c + f * (TubeHL - 0.2), l, TubeHW, 0.2, y, y + 0.6, tile);
            TubeRailing(R, c + f * (TubeHL - 0.2) - l * TubeHW, c + f * (TubeHL - 0.2) + l * TubeHW, y + 0.6, 0.8);
            TubeBox(R, c + f * (TubeHL - 0.2), l, TubeHW * 2, 0.4, y, y + 1.4);

            // the header board over the open end, readable from both sides
            UVRect board = A2["ug_" + e.name];
            foreach (int s in new[] { -1, 1 })
            {
                SBox(S, open + l * (s * (TubeHW + 0.05)), f, 0.08, 0.08, y, y + 3.2, navy);
                TubeBox(R, open + l * (s * (TubeHW + 0.05)), f, 0.16, 0.16, y, y + 3.2);
            }
            SBox(S, open, l, TubeHW + 0.15, 0.06, y + 2.45, y + 3.15, navy);
            VQuad(G, open - f * 0.07 - l * TubeHW, open - f * 0.07 + l * TubeHW, y + 2.5, y + 3.1, board, f * -1);
            VQuad(G, open + f * 0.07 + l * TubeHW, open + f * 0.07 - l * TubeHW, y + 2.5, y + 3.1, board, f);

            // the totem: our logo on a pole, seen from down the street (on the pavement, beside the way in)
            V2 tp = open - f * 0.6 + l * (TubeHW + 1.0);
            Cyl(S, tp, 0.09, y, y + 4.6, 8, navy);
            SBox(S, tp, f, 0.05, 0.62, y + 3.55, y + 4.85, navy);
            VQuad(G, tp - f * 0.06 - l * 0.6, tp - f * 0.06 + l * 0.6, y + 3.6, y + 4.8, A2["ug_logo"], f * -1);
            VQuad(G, tp + f * 0.06 + l * 0.6, tp + f * 0.06 - l * 0.6, y + 3.6, y + 4.8, A2["ug_logo"], f);
            TubeBox(R, tp, f, 0.25, 0.25, y, y + 4.6);
            // a bin by the way in
            V2 bq = open - f * 0.6 - l * (TubeHW + 1.0);
            Cyl(S, bq, 0.24, y + 0.15, y + 1.1, 8, black);
            Cyl(S, bq, 0.26, y + 0.93, y + 1.01, 8, A2["charcoal"], false);
            TubeBox(R, bq, f, 0.5, 0.5, y + 0.15, y + 1.1);
        }

        static void TubeRailing(TubeEntranceResult R, V2 a, V2 b, double y, double h)
        {
            double L = V2.Dist(a, b); if (L < 0.5) return;
            V2 f = (b - a).Norm, n = f.Left;
            int panels = Math.Max(1, (int)Math.Round(L / 2.5));
            UVRect rr = R.atlas["railing"], black = R.atlas["black"];
            for (int i = 0; i < panels; i++)
            {
                V2 p = a + f * (L * i / panels), q = a + f * (L * (i + 1) / panels);
                VQuad(R.cut, p, q, y, y + h, rr, n);
                VQuad(R.cut, q, p, y, y + h, rr, n * -1);
            }
            SBox(R.solid, (a + b) * 0.5, f, L / 2, 0.025, y + h - 0.2, y + h - 0.14, black);
            for (int i = 0; i <= panels; i++) SBox(R.solid, a + f * (L * i / panels), f, 0.035, 0.035, y, y + h + 0.05, black);
        }

        static void TubeBox(TubeEntranceResult R, V2 c, V2 f, double len, double wid, double y0, double y1)
        {
            R.colliders.Add(new DressBox { name = "TubeEntrance", cx = c.x, cy = (y0 + y1) / 2, cz = c.z, sx = wid, sy = y1 - y0, sz = len, yaw = Yaw(f) });
        }

        /// The meshes, once every area's been placed.
        public static void FinishTubeEntrances(TubeEntranceResult R)
        {
            foreach (MeshData m in new[] { R.solid, R.cut, R.glow })
                if (m != null && m.tri.Count > 0) R.meshes.AddRange(Chunk(m));
        }
    }
}
