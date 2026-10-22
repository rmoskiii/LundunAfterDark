// L2: street detail laid over the existing roads (pure C#, testable outside Unity). Nothing under it changes:
// the carriageways, pavements, kerbs, colliders and NavMesh stay exactly as they are; this only adds flat decals
// a few millimetres above them, plus granite setts on the cobbled streets.
//   lines     double yellows at junctions and on main roads; residential streets get marked parking bays with single
//             yellows between them (L3 parks cars in the bays); bus stop clearways; red bus lanes on the busy arterials;
//             give-way lines where side roads meet; SLOW, 20 roundels, lane arrows, LOOK RIGHT at zebras, KEEP CLEAR
//             outside police stations and hospitals
//   ironwork  manholes, drain gullies along the kerbs, utility covers on the pavements
//   wear      patches, cracks, oil stains: lots in Peckam and Gilmore, a little in Canary, hardly any in Knightsbury
//   setts     Knightsbury Mews, Choumert Grove and the warehouse streets are cobbled, Knightsbury and Canary have a
//             granite gutter along the kerbs
// Randomness has its own seed, so nothing else in the city moves.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class ParkingBay { public string road; public int side; public double ch0, ch1, lateral; public double x0, z0, x1, z1; }

    public class StreetResult
    {
        public List<MeshData> meshes = new List<MeshData>();
        public List<ParkingBay> bays = new List<ParkingBay>();
        public List<string> log = new List<string>();
        public Dictionary<string, int> counts = new Dictionary<string, int>();
        public void Count(string k, int n = 1) { int c; counts.TryGetValue(k, out c); counts[k] = c + n; }
    }

    public static partial class Generator
    {
        public const string StreetDecalMat = "Street_Decals", StreetSettsMat = "Street_Setts";

        static readonly HashSet<string> CobbledRoads = new HashSet<string> { "Knightsbury Mews", "Choumert Grove", "Warehouse Lane", "Dock Street", "Wharf Road" };
        static readonly HashSet<string> BusLaneRoads = new HashSet<string> { "Old Kent Lane", "Rye Lane", "Oxbridge Street", "Commercial Way" };
        static readonly HashSet<string> NoLineRoads = new HashSet<string> { "Knightsbury Mews" };
        static readonly HashSet<string> ShopStreets = new HashSet<string> { "Peckam High Street", "Brompton Road", "Tower Plaza" };

        // heights: road 0.02, junction plates 0.025, setts 0.042 (over the old centre line), bus lane 0.032, wear 0.034, iron 0.036,
        // existing white lines 0.04, new lines and text 0.044, zebras 0.048
        const double YSetts = 0.042, YBus = 0.032, YWear = 0.034, YIron = 0.036, YLine = 0.044, YText = 0.046, YLook = 0.052;
        /// Cobbled streets: the setts cover the old painted centre line (0.04), so anything on them sits higher.
        static double Yc(RoadInfo ri, double y) { return CobbledRoads.Contains(ri.r.name) ? y + 0.012 : y; }

        static MeshData sDec, sSet, sPave;
        static Random srng;
        static StreetResult SR;
        static Dictionary<string, List<V2>> sDpoly;

        public static StreetResult Streets(LundunWorldData world, GreyboxResult built, DressingResult dressed)
        {
            if (roads == null || W != world) throw new Exception("Call Generator.Build(world) first.");
            if (dressed == null) throw new Exception("Call Generator.Dress(world, built) first.");
            SR = new StreetResult();
            srng = new Random(2207);
            sDec = new MeshData("StreetDecals", "Streets", StreetDecalMat, false) { navIgnore = true };
            sPave = new MeshData("PavementDecals", "Streets", StreetDecalMat, false) { navIgnore = true };
            sSet = new MeshData("StreetSetts", "Streets", StreetSettsMat, false) { navIgnore = true };
            sDpoly = new Dictionary<string, List<V2>>(); sDpolyWorld = W;
            foreach (LwPoly d in W.districts) sDpoly[d.id] = Pts(d.polygon);

            // where lines must not go: zebra zig-zags, bus stops, the hospital / police / safehouse kerbs
            List<double[]> crossings = dressed != null ? dressed.crossings : new List<double[]>();
            List<BusStopInfo> stops = dressed != null ? dressed.busStops : new List<BusStopInfo>();
            List<PlaceInfo> places = dressed != null ? dressed.places : new List<PlaceInfo>();

            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.cls == "bridge") continue;
                StreetSurface(ri);
                if (ri.pave > 0 && (ri.cls == "street" || ri.cls == "arterial") && !NoLineRoads.Contains(ri.r.name))
                    for (int s = 1; s >= -1; s -= 2) KerbLines(ri, s, crossings, stops, places);
                if (BusLaneRoads.Contains(ri.r.name)) BusLanes(ri, crossings);
                Ironwork(ri);
                Wear(ri);
                if (ri.pave > 0) PavementBits(ri);
            }
            BusStops(stops);
            GiveWays();
            LookRight(crossings);
            KeepClear(places);

            MeshData sWet = new MeshData("StreetPuddles", "Streets", StreetWetMat, false) { navIgnore = true };
            StreetGrit(sWet);                // L4: litter, leaves, puddles, posters, tags (own random seed, last)

            foreach (MeshData m in new[] { sDec, sPave, sSet, sWet }) SR.meshes.AddRange(Chunk(m));
            int v = 0; foreach (MeshData m in SR.meshes) v += m.VertexCount;
            List<string> keys = new List<string>(SR.counts.Keys); keys.Sort();
            SR.log.Add(string.Format("Streets (L2): {0} mesh blocks, {1} vertices, {2} parking bays", SR.meshes.Count, v, SR.bays.Count));
            foreach (string k in keys) SR.log.Add("  " + k + ": " + SR.counts[k]);
            return SR;
        }

        // ------------------------------------------------------------------ pavement UVs (used by Lundun → 20)
        /// The paving texture coordinate for a pavement vertex: u along its street, v in the district's paving band.
        /// Returns false if the point isn't on any street's pavement (the caller keeps its old UV).
        public static bool PavementUV(double x, double z, bool kerbFace, out double u, out double v)
        {
            u = v = 0;
            if (roads == null) return false;
            if (sDpoly == null || sDpolyWorld != W) { sDpoly = new Dictionary<string, List<V2>>(); foreach (LwPoly d in W.districts) sDpoly[d.id] = Pts(d.polygon); sDpolyWorld = W; }
            V2 q = new V2(x, z);
            double best = double.MaxValue, along = 0, across = 0; bool found = false;
            foreach (RoadInfo ri in roads.Values)
            {
                if (ri.pave <= 0) continue;
                Line ln = ri.line;
                for (int i = 0; i + 1 < ln.p.Count; i++)
                {
                    V2 a = ln.p[i], b = ln.p[i + 1], ab = b - a;
                    double l2 = V2.Dot(ab, ab); if (l2 < 1e-6) continue;
                    double t = Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
                    double outBy = V2.Dist(q, a + ab * t) - ri.w / 2;
                    if (outBy < -0.6 || outBy > ri.pave + 1.5) continue;
                    double score = Math.Abs(outBy - ri.pave * 0.5);
                    if (score < best) { best = score; along = ln.ch[i] + Math.Sqrt(l2) * t; across = outBy; found = true; }
                }
            }
            if (!found) return false;
            int band = StreetArt.BandPeckam;
            switch (DistrictAt(q, sDpoly))
            {
                case "D_KNIGHT": band = StreetArt.BandKnight; break;
                case "D_CANARY": case "D_CANARY_PEN": band = StreetArt.BandCanary; break;
                case "D_GILMOR": band = StreetArt.BandGilmore; break;
            }
            u = along / StreetArt.PaveTileM;
            v = StreetArt.PaveV(band, kerbFace ? -1 : across);
            return true;
        }
        static LundunWorldData sDpolyWorld;

        // ------------------------------------------------------------------ helpers
        static double Grit(V2 q)
        {
            string d = DistrictAt(q, sDpoly);
            switch (d)
            {
                case "D_PECKAM": return 1.0;
                case "D_GILMOR": return 1.3;
                case "D_KNIGHT": return 0.2;
                case "D_CANARY": return 0.45;
                case "D_CANARY_PEN": return 0.3;
            }
            return 0.5;
        }

        /// Chainage of the closest point on a road, and the signed offset from its centreline (positive = left).
        static double Project(RoadInfo ri, V2 q, out double lateral)
        {
            double best = double.MaxValue, bestCh = 0; lateral = 0;
            Line ln = ri.line;
            for (int i = 0; i + 1 < ln.p.Count; i++)
            {
                V2 a = ln.p[i], b = ln.p[i + 1], ab = b - a;
                double l2 = V2.Dot(ab, ab); if (l2 < 1e-9) continue;
                double t = Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
                V2 c = a + ab * t; double d = V2.Dist(c, q);
                if (d < best) { best = d; bestCh = ln.ch[i] + Math.Sqrt(l2) * t; lateral = V2.Dot(q - c, ab.Norm.Left); }
            }
            return bestCh;
        }

        static RoadInfo NearestRoad(V2 q, out double ch, out double lateral, bool needPavement)
        {
            RoadInfo best = null; double bd = double.MaxValue; ch = 0; lateral = 0;
            foreach (RoadInfo ri in roads.Values)
            {
                if (ri.cls == "bridge" || (needPavement && ri.pave <= 0)) continue;
                double d = ri.line.DistanceTo(q);
                if (d < bd) { bd = d; best = ri; }
            }
            if (best != null) ch = Project(best, q, out lateral);
            return best;
        }

        /// A strip from the decal atlas laid along a road between two chainages: v = 0 at offA, 1 at offB.
        static void StripAlong(MeshData m, RoadInfo ri, double a, double b, double offA, double offB, string strip, double y)
        {
            if (b - a < 0.3) return;
            List<V2> pts = Densify(ri.line.Sub(a, b), 8);
            if (pts.Count < 2) return;
            List<V2> A = Line.Offset(pts, offA), B = Line.Offset(pts, offB);
            StreetArt.Strip st = StreetArt.GetStrip(strip);
            double v0 = (st.y) / (double)StreetArt.DecH, v1 = (st.y + st.h) / (double)StreetArt.DecH;
            double ch = a;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double seg = V2.Dist(pts[i], pts[i + 1]);
                double u0 = ch / StreetArt.DecRepeatM, u1 = (ch + seg) / StreetArt.DecRepeatM;
                m.Quad(new P3(A[i], y, u0, v0), new P3(A[i + 1], y, u1, v0), new P3(B[i + 1], y, u1, v1), new P3(B[i], y, u0, v1), 0, 1, 0);
                ch += seg;
            }
        }

        /// A sprite from the decal atlas: centre c, `up` = where the top of the picture points, w across × h along up.
        static void Sprite(MeshData m, V2 c, V2 up, double w, double h, string name, double y)
        {
            StreetArt.Rect r = StreetArt.GetSprite(name);
            up = up.Norm; V2 right = up.Left * -1;
            double u0 = (r.x + 0.5) / StreetArt.DecW, u1 = (r.x + r.w - 0.5) / StreetArt.DecW, v0 = (r.y + 0.5) / StreetArt.DecH, v1 = (r.y + r.h - 0.5) / StreetArt.DecH;
            V2 bl = c - right * (w / 2) - up * (h / 2), br = c + right * (w / 2) - up * (h / 2), tr = c + right * (w / 2) + up * (h / 2), tl = c - right * (w / 2) + up * (h / 2);
            m.Quad(new P3(bl, y, u0, v0), new P3(br, y, u1, v0), new P3(tr, y, u1, v1), new P3(tl, y, u0, v1), 0, 1, 0);
        }

        /// A strip laid straight across between two points (give way lines, bay ends): v across the line's width.
        static void StripStraight(MeshData m, V2 a, V2 b, double width, string strip, double y)
        {
            StreetArt.Strip st = StreetArt.GetStrip(strip);
            V2 d = (b - a).Norm, n = d.Left;
            double L = V2.Dist(a, b);
            double v0 = st.y / (double)StreetArt.DecH, v1 = (st.y + st.h) / (double)StreetArt.DecH, u1 = L / StreetArt.DecRepeatM;
            m.Quad(new P3(a - n * (width / 2), y, 0, v0), new P3(b - n * (width / 2), y, u1, v0), new P3(b + n * (width / 2), y, u1, v1), new P3(a + n * (width / 2), y, 0, v1), 0, 1, 0);
        }

        static V2 At(RoadInfo ri, double ch, double lateral, out V2 dir) { V2 c = ri.line.At(ch, out dir); return c + dir.Left * lateral; }

        // ------------------------------------------------------------------ setts and gutters
        static void StreetSurface(RoadInfo ri)
        {
            double L = ri.line.Length, hw = ri.w / 2;
            List<double[]> keep = Keep(L, Concat(ri.boxCut, ri.trimAll), 0.5);
            if (CobbledRoads.Contains(ri.r.name))
            {
                foreach (double[] iv in keep)
                {
                    List<V2> pts = Densify(ri.line.Sub(iv[0], iv[1]), 8);
                    if (pts.Count < 2) continue;
                    List<V2> A = Line.Offset(pts, hw), B = Line.Offset(pts, -hw);
                    double ch = iv[0];
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        double seg = V2.Dist(pts[i], pts[i + 1]);
                        double u0 = ch / StreetArt.SettsM, u1 = (ch + seg) / StreetArt.SettsM, vA = hw / StreetArt.SettsM, vB = -hw / StreetArt.SettsM;
                        sSet.Quad(new P3(A[i], YSetts, u0, vA), new P3(A[i + 1], YSetts, u1, vA), new P3(B[i + 1], YSetts, u1, vB), new P3(B[i], YSetts, u0, vB), 0, 1, 0);
                        ch += seg;
                    }
                    SR.Count("cobbled metres", (int)(iv[1] - iv[0]));
                }
                return;
            }
            // granite gutters along the kerbs in Knightsbury and Canary
            V2 dmid; V2 mid = ri.line.At(L / 2, out dmid);
            string dist = DistrictAt(mid, sDpoly);
            if (ri.pave > 0 && (dist == "D_KNIGHT" || dist == "D_CANARY" || dist == "D_CANARY_PEN"))
                foreach (double[] iv in keep)
                    for (int s = 1; s >= -1; s -= 2)
                        StripAlong(sDec, ri, iv[0], iv[1], s * hw, s * (hw - 0.32), "gutterSetts", YWear);
        }

        // ------------------------------------------------------------------ yellows, bays
        static void KerbLines(RoadInfo ri, int s, List<double[]> crossings, List<BusStopInfo> stops, List<PlaceInfo> places)
        {
            double L = ri.line.Length, hw = ri.w / 2;
            List<double[]> cuts = Concat(ri.boxCut, s > 0 ? ri.trimL : ri.trimR, s > 0 ? ri.boxCutL : ri.boxCutR);
            // exclusions on this side: zig-zags (both sides), bus stops (their side), place kerbs
            List<double[]> excl = new List<double[]>();
            foreach (double[] c in crossings)
            {
                V2 q = new V2(c[0], c[1]); double lat;
                if (ri.line.DistanceTo(q) > 1.0) continue;
                double ch = Project(ri, q, out lat);
                excl.Add(new[] { ch - 19.5, ch + 19.5 });
            }
            List<double[]> stopZones = new List<double[]>();
            foreach (BusStopInfo b in stops)
            {
                V2 q = new V2(b.x, b.z); double lat;
                if (ri.line.DistanceTo(q) > hw) continue;
                double ch = Project(ri, q, out lat);
                if (Math.Sign(lat) != s) continue;
                V2 dir; ri.line.At(ch, out dir);
                bool forward = V2.Dot(new V2(b.dx, b.dz), dir) > 0;
                stopZones.Add(forward ? new[] { ch - 15, ch + 12 } : new[] { ch - 12, ch + 15 });
            }
            excl.AddRange(stopZones);
            List<double[]> placeZones = new List<double[]>();
            foreach (PlaceInfo p in places)
            {
                V2 q = new V2(p.doorX, p.doorZ); double lat;
                if (ri.line.DistanceTo(q) > hw + ri.pave + 12) continue;
                double ch = Project(ri, q, out lat);
                if (Math.Sign(lat) != s) continue;
                placeZones.Add(new[] { ch - 14, ch + 14 });
            }

            V2 dm; V2 mid = ri.line.At(L / 2, out dm);
            string dist = DistrictAt(mid, sDpoly);
            bool residential = ri.cls == "street" && !ShopStreets.Contains(ri.r.name) && !CobbledRoads.Contains(ri.r.name) &&
                               (dist == "D_PECKAM" || dist == "D_KNIGHT" || dist == "D_GILMOR");

            foreach (double[] iv in Keep(L, Concat(cuts, excl), 2.0))
            {
                double a = iv[0], b = iv[1];
                if (!residential || b - a < 40)
                {
                    bool dbl = ri.cls == "arterial" || b - a < 40 || ShopStreets.Contains(ri.r.name);
                    DY(ri, s, a, b, dbl);
                    continue;
                }
                // junction ends get double yellows; the middle alternates bays and single yellows
                DY(ri, s, a, a + 10, true);
                DY(ri, s, b - 10, b, true);
                double ch = a + 10;
                while (ch < b - 10)
                {
                    double bayLen = 16 + srng.NextDouble() * 26, gap = 5 + srng.NextDouble() * 6;
                    double e = Math.Min(b - 10, ch + bayLen);
                    bool blocked = false;
                    foreach (double[] z in placeZones) if (e > z[0] && ch < z[1]) blocked = true;
                    if (e - ch >= 10 && !blocked) Bay(ri, s, ch, e);
                    else DY(ri, s, ch, e, false);
                    double g1 = Math.Min(b - 10, e + gap);
                    if (g1 > e) DY(ri, s, e, g1, false);
                    ch = g1;
                }
            }
        }

        static void DY(RoadInfo ri, int s, double a, double b, bool dbl)
        {
            double hw = ri.w / 2;
            if (dbl) StripAlong(sDec, ri, a, b, s * (hw - 0.0), s * (hw - 0.4), "doubleYellow", Yc(ri, YLine));
            else StripAlong(sDec, ri, a, b, s * (hw - 0.05), s * (hw - 0.25), "singleYellow", Yc(ri, YLine));
            SR.Count(dbl ? "double yellow metres" : "single yellow metres", (int)(b - a));
        }

        static void Bay(RoadInfo ri, int s, double a, double b)
        {
            double hw = ri.w / 2, depth = 1.9;
            // dashed outer edge, solid ends
            StripAlong(sDec, ri, a, b, s * (hw - depth + 0.1), s * (hw - depth - 0.1), "bayEdge", YLine);
            foreach (double ch in new[] { a, b })
            {
                V2 dir; V2 k = At(ri, ch, s * (hw - 0.05), out dir), o = At(ri, ch, s * (hw - depth), out dir);
                StripStraight(sDec, k, o, 0.15, "whiteSolid", YLine);
            }
            V2 d0, d1; V2 p0 = At(ri, a, s * (hw - depth / 2), out d0), p1 = At(ri, b, s * (hw - depth / 2), out d1);
            SR.bays.Add(new ParkingBay { road = ri.r.id, side = s, ch0 = a, ch1 = b, lateral = s * (hw - depth / 2), x0 = p0.x, z0 = p0.z, x1 = p1.x, z1 = p1.z });
            SR.Count("parking bays");
            SR.Count("bay metres", (int)(b - a));
            // oil drips where cars sit
            for (double ch = a + 2.5; ch < b - 2; ch += 5.5)
                if (srng.NextDouble() < 0.35 * Math.Max(0.3, Grit(At(ri, ch, 0, out d0))))
                {
                    V2 dir; V2 q = At(ri, ch, s * (hw - depth / 2), out dir);
                    Sprite(sDec, q, dir, 0.9, 1.2, "stain" + srng.Next(3), YWear);
                }
        }

        // ------------------------------------------------------------------ bus stops, bus lanes
        static void BusStops(List<BusStopInfo> stops)
        {
            foreach (BusStopInfo b in stops)
            {
                V2 q = new V2(b.x, b.z); double ch, lat;
                RoadInfo ri = NearestRoad(q, out ch, out lat, true);
                if (ri == null) continue;
                int s = lat >= 0 ? 1 : -1;
                double hw = ri.w / 2, L = ri.line.Length;
                V2 dir; ri.line.At(ch, out dir);
                V2 travel = new V2(b.dx, b.dz).Norm;
                bool forward = V2.Dot(travel, dir) > 0;
                double a = Math.Max(0, forward ? ch - 15 : ch - 12), e = Math.Min(L, forward ? ch + 12 : ch + 15);
                StripAlong(sDec, ri, a, e, s * (hw - 0.05), s * (hw - 0.35), "clearway", YLine);
                StripAlong(sDec, ri, a, e, s * (hw - 2.75), s * (hw - 3.05), "clearway", YLine);
                foreach (double c in new[] { a, e })
                {
                    V2 dd; V2 k = At(ri, c, s * (hw - 0.05), out dd), o = At(ri, c, s * (hw - 3.05), out dd);
                    StripStraight(sDec, k, o, 0.25, "clearway", YLine);
                }
                V2 d2; V2 tc = At(ri, (a + e) / 2, s * (hw - 1.55), out d2);
                Sprite(sDec, tc, forward ? d2 : d2 * -1, 2.4, 2.8, "BUS STOP", YText);
                SR.Count("bus stop boxes");
            }
        }

        static void BusLanes(RoadInfo ri, List<double[]> crossings)
        {
            if (ri.cls != "arterial") return;
            double L = ri.line.Length, hw = ri.w / 2;
            List<double[]> zebras = new List<double[]>();       // bus lanes stop for the zig-zags
            foreach (double[] c in crossings)
            {
                V2 q = new V2(c[0], c[1]); double lat;
                if (ri.line.DistanceTo(q) > 1.0) continue;
                double ch = Project(ri, q, out lat);
                zebras.Add(new[] { ch - 21, ch + 21 });
            }
            for (int s = 1; s >= -1; s -= 2)
            {
                List<double[]> cuts = Concat(ri.boxCut, s > 0 ? ri.trimL : ri.trimR, s > 0 ? ri.boxCutL : ri.boxCutR, zebras);
                foreach (double[] iv in Keep(L, cuts, 80))
                {
                    double a = iv[0] + 25, b = iv[1] - 25;
                    if (b - a < 40) continue;
                    StripAlong(sDec, ri, a, b, s * (hw - 0.05), s * 3.85, "busLane", YBus);
                    StripAlong(sDec, ri, a, b, s * 3.9, s * 3.6, "whiteSolid", YLine);
                    for (double ch = a + 12; ch < b - 6; ch += 70)
                    {
                        V2 dir; V2 q = At(ri, ch, s * (hw + 3.75) / 2, out dir);
                        Sprite(sDec, q, s > 0 ? dir : dir * -1, 2.4, 2.8, "BUS LANE", YText);
                    }
                    SR.Count("bus lane metres", (int)(b - a));
                }
            }
        }

        // ------------------------------------------------------------------ junction markings
        static void GiveWays()
        {
            List<string> jids = new List<string>(juncs.Keys); jids.Sort();
            foreach (string jid in jids)
            {
                JInfo ji = juncs[jid];
                if (InBox(ji.c)) continue;
                Dictionary<RoadInfo, int> legs = new Dictionary<RoadInfo, int>();
                foreach (RoadInfo r in ji.legRoad) { int n; legs.TryGetValue(r, out n); legs[r] = n + 1; }
                bool hasThrough = false; foreach (var kv in legs) if (kv.Value >= 2) hasThrough = true;
                int maxRank = 0; foreach (RoadInfo r in ji.legRoad) maxRank = Math.Max(maxRank, Rank(r.cls));
                for (int k = 0; k < ji.legDir.Count; k++)
                {
                    RoadInfo ri = ji.legRoad[k];
                    if (ri.cls == "bridge" || ri.cls == "ring") continue;
                    V2 d = ji.legDir[k], l = d.Left;
                    double hw = ri.w / 2;
                    V2 end = ji.c + d * (ji.radius + 0.6);
                    V2 lane = l * -1;                                         // traffic coming in keeps left: that side of the leg
                    bool minor = (hasThrough && legs[ri] == 1) || Rank(ri.cls) < maxRank;
                    if (minor)
                    {
                        StripStraight(sDec, end + lane * 0.15, end + lane * (hw - 0.2), 0.9, "giveWay", Yc(ri, YLine));
                        SR.Count("give way lines");
                        if (ri.cls != "service" && srng.NextDouble() < 0.45)
                            Sprite(sDec, end + d * 11 + lane * (hw / 2), d * -1, Math.Min(2.4, hw - 0.6), 2.4, "SLOW", Yc(ri, YText));
                    }
                    else if (ri.cls == "arterial")
                    {
                        // lane arrows on the approach to a junction on the main roads (two lanes each way)
                        foreach (double off in new[] { 1.875, 5.625 })
                            Sprite(sDec, ji.c + d * (ji.radius + 22) + lane * off, d * -1, 0.7, 3.0, "arrowStraight", YText);
                        SR.Count("lane arrows", 2);
                    }
                    // 20 mph roundel at the entrance to residential streets
                    if (ri.cls == "street" && !ShopStreets.Contains(ri.r.name))
                    {
                        string dist = DistrictAt(end, sDpoly);
                        if ((dist == "D_PECKAM" || dist == "D_KNIGHT" || dist == "D_GILMOR") && srng.NextDouble() < 0.4)
                        {
                            Sprite(sDec, end + d * 16 + l * (hw / 2), d, 1.4, 2.2, "roundel20", Yc(ri, YText));  // for traffic going in (their left = our l)
                            SR.Count("20 roundels");
                        }
                    }
                }
            }
        }

        static int Rank(string cls) { return cls == "ring" ? 3 : cls == "arterial" ? 2 : cls == "street" ? 1 : 0; }

        static void LookRight(List<double[]> crossings)
        {
            foreach (double[] c in crossings)
            {
                V2 cc = new V2(c[0], c[1]), dir = new V2(c[2], c[3]).Norm, l = dir.Left;
                double hw = c[4] / 2;
                for (int s = -1; s <= 1; s += 2)
                {
                    V2 facing = l * -s;                                       // a pedestrian at this kerb, facing the road
                    Sprite(sDec, cc + l * (s * (hw - 0.2)), facing, 2.8, 0.36, "LOOK RIGHT", YLook);
                }
                SR.Count("LOOK RIGHT");
            }
        }

        static void KeepClear(List<PlaceInfo> places)
        {
            foreach (PlaceInfo p in places)
            {
                if (p.kind != "police" && p.kind != "hospital") continue;
                V2 q = new V2(p.doorX, p.doorZ); double ch, lat;
                RoadInfo ri = NearestRoad(q, out ch, out lat, true);
                if (ri == null || ri.line.DistanceTo(q) > ri.w / 2 + ri.pave + 15) continue;
                int s = lat >= 0 ? 1 : -1;
                V2 dir; V2 at = At(ri, ch, s * (ri.w / 2 - 1.5), out dir);
                Sprite(sDec, at, s > 0 ? dir : dir * -1, 2.6, 2.0, "KEEP CLEAR", YText);
                SR.Count("KEEP CLEAR");
            }
        }

        // ------------------------------------------------------------------ manholes, gullies
        static void Ironwork(RoadInfo ri)
        {
            double L = ri.line.Length, hw = ri.w / 2;
            List<double[]> keep = Keep(L, Concat(ri.boxCut, ri.trimAll), 3);
            foreach (double[] iv in keep)
            {
                for (double ch = iv[0] + 6 + srng.NextDouble() * 20; ch < iv[1] - 3; ch += 28 + srng.NextDouble() * 30)
                {
                    double lat = (srng.NextDouble() * 2 - 1) * (hw - 1.0);
                    if (Math.Abs(lat) < 0.5) lat += 0.9 * Math.Sign(lat == 0 ? 1 : lat);
                    V2 dir; V2 q = At(ri, ch, lat, out dir);
                    if (InBox(q)) continue;
                    int k = srng.Next(4);
                    string sp = k < 2 ? "manholeRound" + k : "manholeSquare" + (k - 2);
                    double w = k < 2 ? 0.8 : k == 2 ? 0.75 : 0.9, h = k < 2 ? 0.8 : k == 2 ? 0.75 : 0.65;
                    Sprite(sDec, q, dir, w, h, sp, Yc(ri, YIron));
                    SR.Count("manholes");
                }
                if (ri.pave <= 0 && ri.cls != "ring") continue;
                for (int s = 1; s >= -1; s -= 2)
                    for (double ch = iv[0] + 4 + srng.NextDouble() * 10; ch < iv[1] - 2; ch += 19 + srng.NextDouble() * 9)
                    {
                        V2 dir; V2 q = At(ri, ch, s * (hw - 0.2), out dir);
                        if (InBox(q)) continue;
                        Sprite(sDec, q, dir.Left * s, 0.55, 0.35, "gully" + srng.Next(2), Yc(ri, YIron));
                        SR.Count("gullies");
                    }
            }
        }

        // ------------------------------------------------------------------ wear
        static void Wear(RoadInfo ri)
        {
            double L = ri.line.Length, hw = ri.w / 2;
            List<double[]> keep = Keep(L, Concat(ri.boxCut, ri.trimAll), 3);
            bool cobbled = CobbledRoads.Contains(ri.r.name);
            foreach (double[] iv in keep)
            {
                double ch = iv[0] + srng.NextDouble() * 10;
                while (ch < iv[1] - 2)
                {
                    V2 dir; V2 mid = At(ri, ch, 0, out dir);
                    double g = Grit(mid);
                    ch += 5 + srng.NextDouble() * 30 / Math.Max(0.15, g);
                    if (ch > iv[1] - 2) break;
                    double lat = (srng.NextDouble() * 2 - 1) * (hw - 1.2);
                    V2 q = At(ri, ch, lat, out dir);
                    if (InBox(q)) continue;
                    double r = srng.NextDouble();
                    if (cobbled)
                    {
                        if (r < 0.3) { Sprite(sDec, q, dir, 1.6 + srng.NextDouble(), 1.0 + srng.NextDouble(), "patch" + srng.Next(4), Yc(ri, YWear)); SR.Count("patches"); }  // tarmac filling a dug-up bit
                        continue;
                    }
                    if (r < 0.45)
                    {
                        double w = 1.4 + srng.NextDouble() * 2.6, h = 1.0 + srng.NextDouble() * 2.5;
                        Sprite(sDec, q, srng.NextDouble() < 0.5 ? dir : dir.Left, w, h, "patch" + srng.Next(4), YWear);
                        SR.Count("patches");
                    }
                    else if (r < 0.7 && g > 0.4)
                    {
                        V2 up = (dir + dir.Left * ((srng.NextDouble() - 0.5) * 1.2)).Norm;
                        Sprite(sDec, q, up.Left, 3.0, 0.5, "crack" + srng.Next(3), YWear);
                        SR.Count("cracks");
                    }
                    else if (g > 0.4)
                    {
                        // oil where cars sit and brake: lane centres
                        double lane = (srng.NextDouble() < 0.5 ? 1 : -1) * (ri.cls == "arterial" ? (srng.NextDouble() < 0.5 ? 1.875 : 5.6) : hw / 2);
                        V2 o = At(ri, ch, lane, out dir);
                        Sprite(sDec, o, dir, 0.9 + srng.NextDouble() * 0.5, 1.1 + srng.NextDouble() * 0.6, "stain" + srng.Next(3), YWear);
                        SR.Count("oil stains");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ pavements: covers and stains
        static void PavementBits(RoadInfo ri)
        {
            double L = ri.line.Length, hw = ri.w / 2, y = W.heights.pavementTop + 0.008;
            for (int s = 1; s >= -1; s -= 2)
            {
                List<double[]> cuts = Concat(ri.boxCut, s > 0 ? ri.trimL : ri.trimR, s > 0 ? ri.boxCutL : ri.boxCutR);
                foreach (double[] iv in Keep(L, cuts, 3))
                    for (double ch = iv[0] + 3 + srng.NextDouble() * 12; ch < iv[1] - 2; ch += 14 + srng.NextDouble() * 22)
                    {
                        double across = 0.45 + srng.NextDouble() * Math.Max(0.1, ri.pave - 0.9);
                        V2 dir; V2 q = At(ri, ch, s * (hw + across), out dir);
                        if (InBox(q) || !Free(q, 0.45)) continue;
                        double g = Grit(q);
                        if (srng.NextDouble() < 0.7)
                        {
                            int k = srng.Next(4);
                            double w = k == 0 ? 0.35 : k == 1 ? 0.5 : k == 2 ? 0.25 : 0.6, h = k == 0 ? 0.35 : k == 1 ? 0.3 : k == 2 ? 0.25 : 0.6;
                            Sprite(sPave, q, dir, w, h, "cover" + k, y);
                            SR.Count("pavement covers");
                        }
                        else if (srng.NextDouble() < g)
                        {
                            Sprite(sPave, q, dir.Left, 0.7 + srng.NextDouble() * 0.5, 0.6 + srng.NextDouble() * 0.5, "paveStain" + srng.Next(2), y);
                            SR.Count("pavement stains");
                        }
                    }
            }
        }
    }
}
