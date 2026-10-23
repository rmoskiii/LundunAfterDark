// Pure C#: R5 street dressing, placed by rules from the same road / district / park data as the city.
// Everything is low-poly and textured from ONE atlas (LundunDressingAtlas), merged into 100 m blocks,
// so the whole lot costs a few draw calls per block. Your Belisha / BusStop / PhoneBox prefabs are
// listed as placements and merged the same way by LundunDressingBuilder.
// No UnityEngine types, so it can be tested outside Unity.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class PropPlacement { public string kind; public double x, y, z, yaw; }
    public class DressBox { public string name; public double cx, cy, cz, sx, sy, sz, yaw; }
    public class BusStopInfo { public double x, z, dx, dz; }
    public class StationInfo { public string name; public double x, z, yaw; }

    public class DressingResult
    {
        public List<MeshData> meshes = new List<MeshData>();
        public List<PropPlacement> prefabs = new List<PropPlacement>();
        public List<DressBox> colliders = new List<DressBox>();
        public List<BusStopInfo> busStops = new List<BusStopInfo>();
        public List<StationInfo> stations = new List<StationInfo>();
        public List<PlaceInfo> places = new List<PlaceInfo>();
        public List<BikeDockInfo> bikeDocks = new List<BikeDockInfo>();
        public List<double[]> crossings = new List<double[]>();       // L2: zebra crossings (x, z, along x, along z, road width)
        public List<double[]> shopLights = new List<double[]>();      // L5: lit shop / pub fronts (x, z, out x, out z, width, kind 0 shop 1 pub)
        public DressAtlas atlas;
        public List<string> log = new List<string>();
        public Dictionary<string, int> counts = new Dictionary<string, int>();
        public void Count(string k, int n = 1) { int c; counts.TryGetValue(k, out c); counts[k] = c + n; }
    }

    /// A rectangle kept free inside a park: Underground entrance or football cage.
    public class ParkPlan { public string park, kind, label; public V2 c, dir; public double hl, hw; public V2 edge; }

    public static partial class Generator
    {
        static List<V2> parkTrees = new List<V2>();

        // ================================================================== park plans (also used by BuildParks)
        static readonly Dictionary<string, string> StationNames = new Dictionary<string, string>
        {
            { "Hydemoor Park", "HYDEMOOR PARK" }, { "Gilmore Green", "GILMORE GREEN" },
            { "Quay Gardens", "CANARY QUAY" }, { "Rye Common", "PECKAM RYE" } };

        static List<ParkPlan> PlanParks()
        {
            List<ParkPlan> plans = new List<ParkPlan>();
            foreach (LwPoly park in W.parks)
            {
                List<V2> poly = Pts(park.polygon);
                V2 cen = new V2(0, 0); foreach (V2 q in poly) cen = cen + q; cen = cen * (1.0 / poly.Count);

                // Underground entrance: at the edge nearest a road with a pavement, stairs heading into the park
                ParkPlan st = null;
                if (StationTable.ContainsKey(park.name))
                {
                    double best = double.MaxValue; V2 bestP = cen, bestIn = new V2(0, 1);
                    for (int i = 0; i < poly.Count; i++)
                    {
                        V2 a = poly[i], b = poly[(i + 1) % poly.Count];
                        double L = V2.Dist(a, b);
                        V2 inward = (b - a).Norm.Left;
                        if (!InPoly(Lerp(a, b, 0.5) + inward * 2, poly)) inward = inward * -1;
                        for (double t = 12; t < L - 12; t += 2)
                        {
                            V2 p = a + (b - a).Norm * t;
                            double gap = double.MaxValue;
                            foreach (RoadInfo ri in roads.Values)
                                if (ri.pave >= 3 && ri.cls != "bridge") gap = Math.Min(gap, ri.line.DistanceTo(p) - ri.w / 2 - ri.pave);
                            // prefer the middle of an edge (so the stairs don't sit in a corner)
                            gap += Math.Abs(t - L / 2) * 0.02;
                            if (gap < best) { best = gap; bestP = p; bestIn = inward; }
                        }
                    }
                    st = new ParkPlan { park = park.name, kind = "station", label = StationTable[park.name], edge = bestP, dir = bestIn, c = bestP + bestIn * 7.5, hl = 4.5, hw = 2.4 };
                    plans.Add(st);
                }

                // Football cage: as near the middle as fits, long side along the park's long side (E1: not in a town square)
                if (IsSquare(park)) continue;
                double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (V2 q in poly) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
                V2 along = (maxX - minX) >= (maxZ - minZ) ? new V2(1, 0) : new V2(0, 1);
                ParkPlan cage = null; double bestD = double.MaxValue;
                for (double x = minX; x <= maxX; x += 3)
                    for (double z = minZ; z <= maxZ; z += 3)
                    {
                        ParkPlan cand = new ParkPlan { park = park.name, kind = "cage", c = new V2(x, z), dir = along, hl = 13, hw = 8 };
                        if (!RectInPoly(cand, poly, 3)) continue;
                        if (st != null && RectsOverlap(cand, st, 4)) continue;
                        double d = V2.Dist(cand.c, cen);
                        if (d < bestD) { bestD = d; cage = cand; }
                    }
                if (cage != null) plans.Add(cage);
            }
            return plans;
        }

        static V2[] Corners(ParkPlan p, double grow)
        {
            V2 f = p.dir.Norm, l = f.Left;
            double a = p.hl + grow, b = p.hw + grow;
            return new[] { p.c + f * a + l * b, p.c + f * a - l * b, p.c - f * a - l * b, p.c - f * a + l * b };
        }

        static bool RectInPoly(ParkPlan p, List<V2> poly, double margin)
        {
            foreach (V2 q in Corners(p, 0)) if (!InPoly(q, poly) || EdgeDist(q, poly) < margin) return false;
            return true;
        }

        static bool InRect(V2 q, ParkPlan p, double grow)
        {
            V2 f = p.dir.Norm, l = f.Left, d = q - p.c;
            return Math.Abs(V2.Dot(d, f)) <= p.hl + grow && Math.Abs(V2.Dot(d, l)) <= p.hw + grow;
        }

        static bool RectsOverlap(ParkPlan a, ParkPlan b, double gap)
        {
            foreach (V2 q in Corners(a, gap)) if (InRect(q, b, 0)) return true;
            foreach (V2 q in Corners(b, 0)) if (InRect(q, a, gap)) return true;
            return V2.Dist(a.c, b.c) < Math.Min(a.hw, b.hw);
        }

        static bool InReserved(V2 q, List<ParkPlan> plans, double grow)
        {
            foreach (ParkPlan p in plans) if (InRect(q, p, grow + (p.kind == "station" ? 2 : 1.5))) return true;
            return false;
        }

        // ================================================================== dressing
        static DressingResult D;
        static DressAtlas A;
        static MeshData mSolid, mCut, mGlow;
        static Random drng;
        static List<double[]> occ;          // x, z, radius of everything placed so far
        static List<LampInfo> lampList;
        static Dictionary<string, List<V2>> dpolys;
        static List<BuildingInfo> blds;

        const double PaveY = 0.15;          // pavement top
        const double PaintY = 0.048;        // road paint (over the road markings)

        /// Run after Build(world) (needs its roads, buildings, lamps and the building grid).
        public static DressingResult Dress(LundunWorldData world, GreyboxResult built)
        {
            if (roads == null || W != world) throw new Exception("Call Generator.Build(world) first.");
            D = new DressingResult();
            drng = new Random(1812);
            occ = new List<double[]>();
            lampList = built.lamps;
            blds = built.buildings;
            dpolys = new Dictionary<string, List<V2>>();
            foreach (LwPoly d in W.districts) dpolys[d.id] = Pts(d.polygon);
            mSolid = new MeshData("DressSolid", "Dressing", "DressSolid", false) { navIgnore = true };
            mCut = new MeshData("DressCutout", "Dressing", "DressCutout", false) { navIgnore = true };
            mGlow = new MeshData("DressGlow", "Dressing", "DressGlow", false) { navIgnore = true };

            A = PaintAtlas();
            D.atlas = A;
            foreach (LampInfo l in lampList) Claim(new V2(l.x, l.z), 1.2);
            if (!ExpansionMode) foreach (V2 g in joinGhostLamps) Claim(g, 1.2);     // E1: lamps taken out where expansion roads join

            if (!ExpansionMode) DressPlaces();      // R7: hospitals, police stations, safehouses (first, so nothing else lands on them)
            List<ParkPlan> plans = PlanParks();
            DressParks(plans);
            DressEstateCage();
            DressBusStops();
            DressCrossings();
            if (!ExpansionMode) DressBikeDocks();   // R9: Lundun Cycles (E1: the expansions' docks come later)
            DressJunctionCorners();
            DressStreetPlates();
            DressShopFronts();
            DressPhoneBoxes();
            DressBenchesAndBins();
            DressStreetTrees();
            DressBillboards();
            DressEstates();
            DressGraffitiWalls();
            DressFurniture();               // L3: district furniture (its own random seed, after everything else)
            DressGrit();                    // L4: bin bags and fly-tipping (its own random seed, last)
            if (!ExpansionMode) DressRailway();     // L6: the railway's fences and graffiti (its own random seed, after everything; the city does all of it)

            foreach (MeshData m in new[] { mSolid, mCut, mGlow }) D.meshes.AddRange(Chunk(m));
            int v = 0; foreach (MeshData m in D.meshes) v += m.VertexCount;
            List<string> keys = new List<string>(D.counts.Keys); keys.Sort();
            foreach (string k in keys) D.log.Add("  " + k + ": " + D.counts[k]);
            D.log.Insert(0, string.Format("Dressing: {0} mesh blocks, {1} vertices, {2} prefab props, {3} colliders, atlas {4}x{5} ({6}px used)",
                D.meshes.Count, v, D.prefabs.Count, D.colliders.Count, A.W, A.H, A.UsedHeight));
            return D;
        }

        // ------------------------------------------------------------------ atlas contents
        static readonly string[][] PeckamShops = {
            new[] { "PECKAM FRIED CHICKEN", "c8102e", "ffd200" }, new[] { "CLUCK ROYALE", "ffcc00", "c8102e" },
            new[] { "TASTY BIRD CHICKEN", "1d4fb8", "ffffff" }, new[] { "KING KEBAB & PIZZA", "0b0b0b", "ff4b2b" },
            new[] { "ANATOLIA GRILL", "7a0c12", "ffe08a" }, new[] { "RYE LANE OFF LICENCE", "0a5c36", "ffffff" },
            new[] { "FADEZ BARBERS", "111111", "e8e8e8" }, new[] { "FONE FIX", "00a3e0", "ffffff" },
            new[] { "AFRO HAIR & BEAUTY", "e0218a", "ffffff" }, new[] { "MAMA'S CARIBBEAN", "008a3e", "ffd200" },
            new[] { "LAGOS FOODS", "2b7a2b", "ffffff" }, new[] { "QUIK CASH TRANSFER", "ffe600", "1a1a1a" },
            new[] { "BETS & CO", "123b8c", "ffcc00" }, new[] { "SUDZ LAUNDERETTE", "6fc3df", "0b2a4a" },
            new[] { "EVERYTHING £1", "ff6a00", "ffffff" }, new[] { "CHICKEN SHACK", "e31d1a", "ffffff" } };
        static readonly string[][] KnightShops = {
            new[] { "KNIGHTSBURY DELI", "1d1d1b", "e6d3a3" }, new[] { "MAISON VERT", "2f4a3a", "f2efe6" },
            new[] { "HARTLEY & FINCH", "0f2340", "ffffff" }, new[] { "BLOOMS FLORIST", "f2e9e4", "6b2d5c" },
            new[] { "CAFE LUMEN", "f4f1ea", "1d1d1b" }, new[] { "SAVILE & SONS", "111111", "c9a24a" } };
        static readonly string[][] GilmoreShops = {
            new[] { "GILMORE FOOD & WINE", "1b4fa0", "ffffff" }, new[] { "CHIPPY'S FISH BAR", "0e6aa8", "ffd200" },
            new[] { "GILMORE NEWS", "b21f24", "ffffff" } };
        static readonly string[][] CanaryShops = {
            new[] { "DOCK ROAST COFFEE", "2b2b2b", "f2c14e" }, new[] { "THE DESK DELI", "f5f5f0", "1a1a1a" },
            new[] { "IRONWORKS GYM", "0d0d0d", "ff3b30" } };
        static readonly string[] Pubs = { "THE CROWN & KNIGHT", "THE DUKE OF KNIGHTSBURY", "THE GILMORE ARMS", "THE BRASS MONKEY", "THE GILDED LAMB", "THE PRINCE REGENT", "THE OLD BELL" };
        static readonly string[][] Ads = {
            new[] { "LUNDUN LAGER", "PROPERLY COLD. SINCE 1891.", "f5c518", "e08a00", "1a1a1a", "ffffff" },
            new[] { "KESTREL GT", "OWN THE NIGHT.", "101820", "2a3a52", "ffffff", "ff3b30" },
            new[] { "CLUCK ROYALE", "WINGS FROM £2. TILL 4AM.", "c8102e", "8a0b20", "ffd200", "ffffff" },
            new[] { "LUNDUN RADIO 99.1", "THE SOUND AFTER DARK", "5a189a", "240046", "ffffff", "3ee0ff" },
            new[] { "DON'T DRINK & DRIVE", "THINK. ARRIVE ALIVE.", "ffffff", "d8d8d8", "c8102e", "1a1a1a" },
            new[] { "QUAY TOWERS", "LUXURY LIVING. ZONE 2.", "0f2340", "1f4a7a", "ffffff", "c9a24a" } };
        static readonly string[] Tags = { "SE15", "PECK", "RIZE", "DUSK", "LDN", "NOK", "KAYO", "GILMO", "ZERO", "RYE" };
        static readonly Dictionary<string, string> Swatches = new Dictionary<string, string>
        {
            { "black", "141414" }, { "charcoal", "2a2c2e" }, { "grey", "6b6d70" }, { "light", "a7a9ab" }, { "white", "ececea" },
            { "postred", "b5121b" }, { "darkred", "6e1016" }, { "benchgreen", "23412b" }, { "wood", "7a5532" }, { "bark", "4a3a2a" },
            { "leaf1", "3f6b2a" }, { "leaf2", "2f5a24" }, { "leaf3", "557d33" }, { "yellow", "f2c200" }, { "navy", "1b2766" },
            { "glass", "1d2833" }, { "concrete", "8d8a84" }, { "tile", "d8d4c8" }, { "estatebin", "4c5a4f" }, { "lid", "2c332e" },
            { "bag", "111111" }, { "metal", "8e9496" }, { "gold", "c9a24a" }, { "pubgreen", "1f3b2c" }, { "tactile", "b5524a" },
            { "cagegreen", "2e4a33" }, { "pit", "2a241e" }, { "slab", "9a968e" }, { "orange", "d96a1b" },
            { "hospwall", "e9e6dc" }, { "nhsblue", "1d5fb0" }, { "ambyellow", "f2d21b" }, { "bluelight", "3a7bff" }, { "bluelamp", "2f6bff" },
            { "cycleteal", "00a3a3" }, { "cycleyellow", "ffd23f" }, { "safegreen", "3cff7a" }, { "lawn", "3e6b2e" }, { "drive", "6f6a62" }, { "stucco", "efe9dc" }, { "modernwall", "7c8187" }, { "brick", "7a3b2c" } };

        static Dictionary<string, string> districtCode = new Dictionary<string, string>
        { { "D_KNIGHT", "SW3" }, { "D_CANARY", "E14" }, { "D_CANARY_PEN", "E14" }, { "D_GILMOR", "SE17" }, { "D_PECKAM", "SE15" } };

        static DressAtlas PaintAtlas()
        {
            DressAtlas a = new DressAtlas(2048, 4096, 99);
            foreach (var kv in Swatches) a.Swatch(kv.Key, Col.Hex(kv.Value));
            a.UndergroundLogo("ug_logo");
            foreach (var kv in StationTable) a.StationBoard("ug_" + kv.Value, kv.Value);
            foreach (string[][] list in new[] { AreaShops(PeckamShops), KnightShops, GilmoreShops, CanaryShops })
                foreach (string[] s in list) a.Sign("shop_" + s[0], 640, 80, s[0], Col.Hex(s[1]), Col.Hex(s[2]), Col.Hex(s[2]));
            foreach (string p in Pubs) a.Sign("pub_" + p, 640, 80, p, Col.Hex("1f3b2c"), Col.Hex("d9b45a"), new Col(0, 0, 0, 0), 0.42);
            List<string> rids = new List<string>(roads.Keys); rids.Sort();
            foreach (string rid in rids)
            {
                RoadInfo ri = roads[rid];
                if (ri.pave > 0 && ri.cls != "bridge") a.StreetPlate(PlateKey(ri), ri.r.name.ToUpperInvariant(), RoadCode(ri));
            }
            for (int i = 0; i < Ads.Length; i++) { string[] d = Ads[i]; a.Billboard("ad_" + i, d[0], d[1], Col.Hex(d[2]), Col.Hex(d[3]), Col.Hex(d[4]), Col.Hex(d[5])); }
            for (int i = 0; i < AreaTags.Length; i++) a.Graffiti("tag_" + i, AreaTags[i], i);
            a.ChainLink("chainlink"); a.Railing("railing"); a.Stairwell("stairs");
            a.OfficeWindows("officewin"); a.HomeWindows("homewin"); a.Battenburg("battenburg"); a.HSign("sign_H");
            a.Sign("sign_AE", 640, 80, "EMERGENCY  A&E", Col.Hex("c8102e"), Col.Hex("ffffff"), new Col(0, 0, 0, 0), 0.5);
            a.Sign("sign_CYCLES", 640, 80, "LUNDUN CYCLES", Col.Hex("00a3a3"), Col.Hex("ffffff"), Col.Hex("ffd23f"), 0.5);
            a.Sign("sign_POLICE", 640, 80, "POLICE", Col.Hex("1b2766"), Col.Hex("ffffff"), new Col(0, 0, 0, 0), 0.5);
            foreach (PlaceSite ps in PlaceSpecs())
                if (ps.kind == "hospital") a.Sign("sign_" + ps.id, 640, 80, ps.name.ToUpperInvariant(), Col.Hex("1d5fb0"), Col.Hex("ffffff"), new Col(0, 0, 0, 0), 0.48);
                else if (ps.kind == "police") a.Sign("sign_" + ps.id, 640, 80, ps.name.ToUpperInvariant(), Col.Hex("10183d"), Col.Hex("ffffff"), Col.Hex("2f6bff"), 0.48);
            a.Shutter("shutter"); a.ShopWindow("shopwin"); a.PubWindow("pubwin"); a.Pitch("pitch"); a.Mattress("mattress");
            FurnitureAtlas(a);              // L3 (appended)
            GritAtlas(a);                   // L4 (appended)
            return a;
        }

        static string PlateKey(RoadInfo ri) { return "plate_" + ri.r.id; }

        /// Postcode district for a road's name plates: whichever district most of its frontage is in.
        static string RoadCode(RoadInfo ri)
        {
            Dictionary<string, int> votes = new Dictionary<string, int>();
            for (double ch = 5; ch < ri.line.Length; ch += 10)
                foreach (int s in new[] { -1, 1 })
                {
                    V2 d; V2 q = ri.line.At(ch, out d);
                    string id = District(q + d.Left * (s * (ri.w / 2 + 15)));
                    if (id == null) continue;
                    int n; votes.TryGetValue(id, out n); votes[id] = n + 1;
                }
            string best = null; int bn = 0;
            foreach (var kv in votes) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; }
            if (ExpansionMode) return "SW2";                                   // E1: Brixtun
            string code;
            return best != null && districtCode.TryGetValue(best, out code) ? code : "SE1";
        }

        // ------------------------------------------------------------------ geometry helpers
        static double Yaw(V2 f) { double y = Math.Atan2(f.x, f.z) * 180 / Math.PI; return y < 0 ? y + 360 : y; }
        static UVRect S(string swatch) { return A[swatch]; }

        static void Claim(V2 p, double r) { occ.Add(new[] { p.x, p.z, r }); }
        static bool Free(V2 p, double r)
        {
            foreach (double[] o in occ) { double dx = o[0] - p.x, dz = o[1] - p.z, rr = o[2] + r; if (dx * dx + dz * dz < rr * rr) return false; }
            return true;
        }

        /// Box with constant colour (one swatch). f = long axis.
        static void SBox(MeshData m, V2 c, V2 f, double hl, double hw, double y0, double y1, UVRect sw, bool bottom = false)
        {
            f = f.Norm; V2 l = f.Left;
            V2 p0 = c - f * hl - l * hw, p1 = c + f * hl - l * hw, p2 = c + f * hl + l * hw, p3 = c - f * hl + l * hw;
            V2[] ring = { p0, p1, p2, p3 };
            double u = sw.Cu, v = sw.Cv;
            for (int i = 0; i < 4; i++)
            {
                V2 a = ring[i], b = ring[(i + 1) % 4], mid = (a + b) * 0.5, n = (mid - c).Norm;
                m.Quad(new P3(a, y0, u, v), new P3(b, y0, u, v), new P3(b, y1, u, v), new P3(a, y1, u, v), n.x, 0, n.z);
            }
            m.Quad(new P3(p0, y1, u, v), new P3(p1, y1, u, v), new P3(p2, y1, u, v), new P3(p3, y1, u, v), 0, 1, 0);
            if (bottom) m.Quad(new P3(p0, y0, u, v), new P3(p1, y0, u, v), new P3(p2, y0, u, v), new P3(p3, y0, u, v), 0, -1, 0);
        }

        static void Cyl(MeshData m, V2 c, double r, double y0, double y1, int n, UVRect sw, bool top = true)
        {
            double u = sw.Cu, v = sw.Cv;
            for (int i = 0; i < n; i++)
            {
                double a0 = i * Math.PI * 2 / n, a1 = (i + 1) * Math.PI * 2 / n;
                V2 p = c + new V2(Math.Sin(a0), Math.Cos(a0)) * r, q = c + new V2(Math.Sin(a1), Math.Cos(a1)) * r;
                V2 nn = ((p + q) * 0.5 - c).Norm;
                m.Quad(new P3(p, y0, u, v), new P3(q, y0, u, v), new P3(q, y1, u, v), new P3(p, y1, u, v), nn.x, 0, nn.z);
            }
            if (top)
            {
                List<P3> cap = new List<P3>();
                for (int i = 0; i < n; i++) { double a = i * Math.PI * 2 / n; cap.Add(new P3(c + new V2(Math.Sin(a), Math.Cos(a)) * r, y1, u, v)); }
                m.FanUp(cap);
            }
        }

        /// Ring-to-ring band (for canopies): radius r0 at y0 to r1 at y1.
        static void Band(MeshData m, V2 c, double r0, double y0, double r1, double y1, int n, UVRect sw, double twist)
        {
            double u = sw.Cu, v = sw.Cv;
            for (int i = 0; i < n; i++)
            {
                double a0 = i * Math.PI * 2 / n + twist, a1 = (i + 1) * Math.PI * 2 / n + twist;
                V2 d0 = new V2(Math.Sin(a0), Math.Cos(a0)), d1 = new V2(Math.Sin(a1), Math.Cos(a1));
                V2 nn = ((d0 + d1) * 0.5).Norm;
                double ny = (r0 - r1) / Math.Max(0.01, y1 - y0);
                m.Quad(new P3(c + d0 * r0, y0, u, v), new P3(c + d1 * r0, y0, u, v), new P3(c + d1 * r1, y1, u, v), new P3(c + d0 * r1, y1, u, v), nn.x, ny, nn.z);
            }
        }

        /// Upright picture between a and b, facing n. Always reads the right way round to someone looking at it.
        static void VQuad(MeshData m, V2 a, V2 b, double y0, double y1, UVRect r, V2 n)
        {
            bool flipU = V2.Dot(b - a, n.Left) < 0;
            double ua = flipU ? r.u1 : r.u0, ub = flipU ? r.u0 : r.u1;
            m.Quad(new P3(a, y0, ua, r.v0), new P3(b, y0, ub, r.v0), new P3(b, y1, ub, r.v1), new P3(a, y1, ua, r.v1), n.x, 0, n.z);
        }

        /// Flat picture on the ground: v runs from c - f*hl (v1) to c + f*hl (v0).
        static void HQuad(MeshData m, V2 c, V2 f, double hl, double hw, double y, UVRect r)
        {
            f = f.Norm; V2 l = f.Left;
            m.Quad(new P3(c - f * hl - l * hw, y, r.u0, r.v1), new P3(c - f * hl + l * hw, y, r.u1, r.v1),
                   new P3(c + f * hl + l * hw, y, r.u1, r.v0), new P3(c + f * hl - l * hw, y, r.u0, r.v0), 0, 1, 0);
        }

        static void Collider(string name, V2 c, V2 f, double sl, double sw, double y0, double y1)
        {
            D.colliders.Add(new DressBox { name = name, cx = c.x, cy = (y0 + y1) / 2, cz = c.z, sx = sw, sy = y1 - y0, sz = sl, yaw = Yaw(f) });
        }

        static void Prefab(string kind, V2 p, double y, V2 forward)
        {
            D.prefabs.Add(new PropPlacement { kind = kind, x = p.x, y = y, z = p.z, yaw = Yaw(forward) });
            D.Count(kind);
        }

        static string District(V2 q)
        {
            foreach (var kv in dpolys) if (InPoly(q, kv.Value)) return kv.Key;
            return null;
        }

        /// Where on this road (and on which side) furniture may go: away from junctions and the old high street.
        static List<double[]> Stretches(RoadInfo ri, int side, double junctionMargin, double minLen)
        {
            List<double[]> cuts = new List<double[]>();
            foreach (double[] iv in ri.trimAll) cuts.Add(new[] { iv[0] - junctionMargin, iv[1] + junctionMargin });
            cuts.AddRange(ri.boxCut);
            cuts.AddRange(side > 0 ? ri.boxCutL : ri.boxCutR);
            return Keep(ri.line.Length, cuts, minLen);
        }

        static bool Inside(double ch, List<double[]> ivs, double margin)
        {
            foreach (double[] iv in ivs) if (ch >= iv[0] + margin && ch <= iv[1] - margin) return true;
            return false;
        }

        static bool OnClearPavement(V2 p, RoadInfo ri, double r)
        {
            return !InBox(p) && !NearOtherRoad(p, ri, 0.6) && river.DistanceTo(p) > WallOuter + 2 && Free(p, r);
        }

        // ------------------------------------------------------------------ props (meshes)
        static void Tree(V2 p, double baseY, bool pit, bool collider)
        {
            double h = 4.2 + drng.NextDouble() * 2.4, r = 1.6 + drng.NextDouble() * 0.9;
            UVRect leaf = S("leaf" + (1 + drng.Next(3)));
            if (pit) SBox(mSolid, p, new V2(0, 1), 0.6, 0.6, baseY - 0.02, baseY + 0.005, S("pit"));
            Cyl(mSolid, p, 0.15, baseY, baseY + 2.8, 6, S("bark"), false);
            double y0 = baseY + 2.2, tw = drng.NextDouble();
            Band(mSolid, p, r * 0.55, y0, r, y0 + h * 0.35, 7, leaf, tw);
            Band(mSolid, p, r, y0 + h * 0.35, r * 0.85, y0 + h * 0.7, 7, leaf, tw);
            Band(mSolid, p, r * 0.85, y0 + h * 0.7, r * 0.35, y0 + h, 7, leaf, tw);
            List<P3> cap = new List<P3>(), under = new List<P3>();
            for (int i = 0; i < 7; i++) { double a = i * Math.PI * 2 / 7 + tw; cap.Add(new P3(p + new V2(Math.Sin(a), Math.Cos(a)) * r * 0.35, y0 + h, leaf.Cu, leaf.Cv)); }
            mSolid.FanUp(cap);
            Claim(p, 1.2);
            if (collider) Collider("Tree", p, new V2(0, 1), 0.4, 0.4, baseY, baseY + 3);
            D.Count("trees");
        }

        static void Bench(V2 p, V2 facing, double y)
        {
            V2 f = facing.Norm, along = f.Left;
            SBox(mSolid, p + f * 0.05, along, 0.9, 0.22, y + 0.42, y + 0.48, S("wood"));                 // seat
            SBox(mSolid, p - f * 0.2, along, 0.9, 0.04, y + 0.55, y + 0.9, S("wood"));                   // back
            foreach (int s in new[] { -1, 1 }) SBox(mSolid, p + along * (0.78 * s), along, 0.04, 0.25, y, y + 0.9, S("black"));
            Claim(p, 1.0);
            D.Count("benches");
        }

        static void Bin(V2 p, double y, bool posh)
        {
            Cyl(mSolid, p, 0.24, y, y + 0.95, 8, S("black"));
            Cyl(mSolid, p, 0.26, y + 0.78, y + 0.86, 8, S(posh ? "gold" : "charcoal"), false);
            Claim(p, 0.5);
            D.Count("bins");
        }

        static void PostBox(V2 p, V2 facing, double y)
        {
            Cyl(mSolid, p, 0.3, y, y + 0.12, 8, S("black"));
            Cyl(mSolid, p, 0.27, y + 0.12, y + 1.42, 8, S("postred"));
            Cyl(mSolid, p, 0.31, y + 1.42, y + 1.5, 8, S("postred"));
            Cyl(mSolid, p, 0.22, y + 1.5, y + 1.58, 8, S("postred"));
            V2 f = facing.Norm;
            SBox(mSolid, p + f * 0.25, f.Left, 0.13, 0.03, y + 1.15, y + 1.2, S("black"));               // slot
            Claim(p, 0.7);
            D.Count("post boxes");
        }

        static void Bollard(V2 p, double y)
        {
            BollardStyled(p, y);            // L3: cast iron / steel / concrete by district (the old black-and-white one is the default)
            Claim(p, 0.3);
            D.Count("bollards");
        }

        /// Railing run from a to b (both on the ground at y), height h, posts every panel.
        static void Railing(V2 a, V2 b, double y, double h)
        {
            double L = V2.Dist(a, b); if (L < 0.5) return;
            V2 f = (b - a).Norm, n = f.Left;
            int panels = Math.Max(1, (int)Math.Round(L / 2.5));
            UVRect rr = A["railing"];
            for (int i = 0; i < panels; i++)
            {
                V2 p = a + f * (L * i / panels), q = a + f * (L * (i + 1) / panels);
                VQuad(mCut, p, q, y, y + h, rr, n);
            }
            SBox(mSolid, (a + b) * 0.5, f, L / 2, 0.025, y + h - 0.2, y + h - 0.14, S("black"));
            for (int i = 0; i <= panels; i++) SBox(mSolid, a + f * (L * i / panels), f, 0.035, 0.035, y, y + h + 0.05, S("black"));
            for (double s = 0; s <= L; s += 1.5) Claim(a + f * s, 0.4);
            D.Count("railing metres", (int)L);
        }

        static void Graffiti(V2 a, V2 b, double y0, double y1, V2 n)
        {
            VQuad(mCut, a + n * 0.04, b + n * 0.04, y0, y1, A["tag_" + drng.Next(Tags.Length)], n);
            D.Count("graffiti");
        }

        // ------------------------------------------------------------------ parks: stations, cages, benches, railings
        static void DressParks(List<ParkPlan> plans)
        {
            foreach (ParkPlan p in plans)
            {
                if (p.kind == "station") Station(p);
                else Cage(p, 0.012);
            }
            foreach (V2 t in parkTrees) Claim(t, 1.5);

            foreach (LwPoly park in W.parks)
            {
                List<V2> poly = Pts(park.polygon);
                bool posh = park.name == "Hydemoor Park";
                for (int i = 0; i < poly.Count; i++)
                {
                    V2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    double L = V2.Dist(a, b); V2 f = (b - a).Norm, inward = f.Left;
                    if (!InPoly(Lerp(a, b, 0.5) + inward * 2, poly)) inward = inward * -1;
                    // benches facing in, every ~24 m, a bin with every other one
                    int k = 0;
                    for (double t = 10; t < L - 10; t += 24)
                    {
                        V2 q = a + f * t + inward * 3.2;
                        if (!Free(q, 1.2) || InReserved(q, plans, 1.5)) continue;
                        Bench(q, inward, 0.012);
                        if (k++ % 2 == 0 && Free(q + f * 1.4, 0.4)) Bin(q + f * 1.4, 0.012, posh);
                    }
                    // Knightsbury garden-square railings, with gates
                    if (posh)
                    {
                        List<double[]> gaps = new List<double[]>();
                        for (double g = 20; g < L; g += 45) gaps.Add(new[] { g - 1.6, g + 1.6 });
                        foreach (ParkPlan sp in plans)
                            if (sp.park == park.name && sp.kind == "station") { double t0 = V2.Dot(sp.edge - a, f); gaps.Add(new[] { t0 - 6, t0 + 6 }); }
                        foreach (double[] iv in Keep(L, gaps, 1))
                            Railing(a + f * iv[0] + inward * 0.5, a + f * iv[1] + inward * 0.5, 0.012, 1.4);
                    }
                }
            }
        }

        static void Station(ParkPlan p)
        {
            V2 f = p.dir.Norm, l = f.Left, open = p.c - f * p.hl;          // the open end faces the road
            double y = 0.012;
            HQuad(mSolid, p.c - f * 1.2, f, p.hl + 2.4, p.hw + 1.6, y + 0.02, S("slab"));             // paving apron
            HQuad(mSolid, p.c, f, p.hl - 0.3, p.hw - 0.4, y + 0.04, A["stairs"]);                   // steps going down
            UVRect tile = S("tile");
            foreach (int s in new[] { -1, 1 })
            {
                V2 wc = p.c + l * (s * (p.hw - 0.2));
                SBox(mSolid, wc, f, p.hl, 0.2, y, y + 0.6, tile);
                V2 a = wc - f * p.hl + l * (s * 0.2), b = wc + f * p.hl + l * (s * 0.2);
                Graffiti(Lerp(a, b, 0.25 + 0.1 * s), Lerp(a, b, 0.75 + 0.1 * s), y + 0.03, y + 0.58, l * s);
                Railing(wc - f * p.hl, wc + f * p.hl, y + 0.6, 0.8);
            }
            SBox(mSolid, p.c + f * (p.hl - 0.2), l, p.hw, 0.2, y, y + 0.6, tile);
            Railing(p.c + f * (p.hl - 0.2) - l * p.hw, p.c + f * (p.hl - 0.2) + l * p.hw, y + 0.6, 0.8);

            // header board over the open end, readable from both sides
            UVRect board = A["ug_" + p.label];
            foreach (int s in new[] { -1, 1 }) SBox(mSolid, open + l * (s * (p.hw + 0.05)), f, 0.08, 0.08, y, y + 3.2, S("navy"));
            SBox(mSolid, open, l, p.hw + 0.15, 0.06, y + 2.45, y + 3.15, S("navy"));
            VQuad(mGlow, open - f * 0.07 - l * p.hw, open - f * 0.07 + l * p.hw, y + 2.5, y + 3.1, board, f * -1);
            VQuad(mGlow, open + f * 0.07 + l * p.hw, open + f * 0.07 - l * p.hw, y + 2.5, y + 3.1, board, f);

            // the totem: pole with our logo, seen from down the street
            V2 tp = open - f * 1.6 + l * (p.hw + 1.2);
            Cyl(mSolid, tp, 0.09, y, y + 4.6, 8, S("navy"));
            UVRect logo = A["ug_logo"];
            SBox(mSolid, tp + new V2(0, 0), f, 0.05, 0.62, y + 3.55, y + 4.85, S("navy"));
            VQuad(mGlow, tp - f * 0.06 - l * 0.6, tp - f * 0.06 + l * 0.6, y + 3.6, y + 4.8, logo, f * -1);
            VQuad(mGlow, tp + f * 0.06 + l * 0.6, tp + f * 0.06 - l * 0.6, y + 3.6, y + 4.8, logo, f);

            Collider("Underground", p.c, f, p.hl * 2 + 0.4, p.hw * 2 + 0.4, y, y + 1.4);
            Claim(p.c, Math.Max(p.hl, p.hw) + 1.5);
            Claim(tp, 0.8);
            D.stations.Add(new StationInfo { name = p.label, x = open.x, z = open.z, yaw = Yaw(f * -1) });
            D.Count("Underground entrances");
            // a bin and a bench by the entrance
            V2 bq = open - f * 1.5 - l * (p.hw + 1.5);
            if (Free(bq, 0.5)) Bin(bq, y, false);
        }

        static void Cage(ParkPlan p, double y)
        {
            V2 f = p.dir.Norm, l = f.Left;
            HQuad(mSolid, p.c, l, p.hw, p.hl, y + 0.03, A["pitch"]);          // pitch picture: long side along f
            UVRect link = A["chainlink"], green = S("cagegreen");
            V2[] cs = Corners(p, 0);   // +f+l, +f-l, -f-l, -f+l
            for (int side = 0; side < 4; side++)
            {
                V2 a = cs[side], b = cs[(side + 1) % 4];
                double L = V2.Dist(a, b); V2 d = (b - a).Norm, outN = ((a + b) * 0.5 - p.c).Norm;
                int panels = Math.Max(1, (int)Math.Round(L / 3.2));
                bool longSide = L > p.hw * 2 + 1;
                for (int i = 0; i < panels; i++)
                {
                    V2 q0 = a + d * (L * i / panels), q1 = a + d * (L * (i + 1) / panels);
                    // ways in: the first panel on one long side and the last on the other
                    bool gap = longSide && ((side % 2 == 0 && i == 0) || (side % 2 == 1 && i == panels - 1));
                    SBox(mSolid, q0, d, 0.06, 0.06, y, y + 3.6, green);
                    if (gap) continue;
                    V2 mid = (q0 + q1) * 0.5;
                    SBox(mSolid, mid, d, L / panels / 2, 0.04, y, y + 1.0, green);                 // kick boards
                    VQuad(mCut, q0, q1, y + 1.0, y + 3.6, link, outN);
                    SBox(mSolid, mid, d, L / panels / 2, 0.03, y + 3.55, y + 3.62, green);
                    Collider("CageFence", mid, d, L / panels, 0.12, y, y + 3.6);
                    if (drng.NextDouble() < 0.3) Graffiti(Lerp(q0, q1, 0.1) + outN * 0.03, Lerp(q1, q0, 0.1) + outN * 0.03, y + 0.05, y + 0.97, outN);
                }
            }
            // goals at both ends
            foreach (int s in new[] { -1, 1 })
            {
                V2 g = p.c + f * (s * (p.hl - 0.4));
                foreach (int k in new[] { -1, 1 }) SBox(mSolid, g + l * (k * 1.5), f, 0.05, 0.05, y, y + 2.0, S("white"));
                SBox(mSolid, g, l, 1.55, 0.05, y + 1.95, y + 2.05, S("white"));
            }
            Claim(p.c, Math.Max(p.hl, p.hw) + 1);
            D.Count("football cages");
        }

        /// A cage on a free patch of the estate (between the blocks).
        static void DressEstateCage()
        {
            List<V2> est; if (!dpolys.TryGetValue("D_GILMOR", out est)) return;
            V2 centre = new V2(370, 330);
            ParkPlan best = null; double bestD = double.MaxValue;
            for (double x = 140; x < 600; x += 4)
                for (double z = 130; z < 560; z += 4)
                    foreach (V2 dir in new[] { new V2(1, 0), new V2(0, 1) })
                    {
                        ParkPlan c = new ParkPlan { kind = "cage", c = new V2(x, z), dir = dir, hl = 13, hw = 8 };
                        double d = V2.Dist(c.c, centre);
                        if (d >= bestD || !GridFree(c, 2.5) || !Free(c.c, 15)) continue;
                        bool inside = true; foreach (V2 q in Corners(c, 2)) if (!InPoly(q, est)) inside = false;
                        if (inside) { best = c; bestD = d; }
                    }
            if (best != null) { best.park = "estate"; Cage(best, 0); }
        }

        static bool GridFree(ParkPlan p, double grow)
        {
            V2 f = p.dir.Norm, l = f.Left;
            for (double a = -p.hl - grow; a <= p.hl + grow; a += 1)
                for (double b = -p.hw - grow; b <= p.hw + grow; b += 1)
                {
                    int ci = CellIndex(p.c + f * a + l * b);
                    if (ci < 0 || blocked[ci] || occupied[ci]) return false;
                }
            return true;
        }

        // ------------------------------------------------------------------ bus stops
        static void DressBusStops()
        {
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.cls != "arterial") continue;
                for (int side = 1; side >= -1; side -= 2)
                {
                    List<double[]> ok = Stretches(ri, side, 25, 30);
                    for (double ch = side > 0 ? 60 : 200; ch < ri.line.Length - 30; ch += 300)
                    {
                        double c2 = ch;
                        bool placed = false;
                        for (int tries = 0; tries < 8 && !placed; tries++, c2 += 12)
                        {
                            if (!Inside(c2, ok, 6)) continue;
                            V2 dir; V2 lc = ri.line.At(c2, out dir);
                            V2 left = dir.Left * side;
                            V2 pos = lc + left * (ri.w / 2 + ri.pave - 1.0);
                            if (!OnClearPavement(pos, ri, 2.4)) continue;
                            bool clashLamp = false;
                            foreach (LampInfo lp in lampList) if (V2.Dist(new V2(lp.x, lp.z), pos) < 2.6) clashLamp = true;
                            if (clashLamp) continue;
                            // prefab opens to its -Z side: point +Z away from the road
                            Prefab("BusStop", pos, PaveY, left);
                            V2 f = left; V2 along = f.Left;
                            Collider("BusShelter", pos + f * 0.45, along, 3.8, 0.9, PaveY, PaveY + 2.5);
                            Claim(pos, 2.6); Claim(pos - left * 1.2 - along * 2.5, 0.8);
                            V2 binP = pos + along * 2.6 - left * 0.3;
                            if (Free(binP, 0.4)) Bin(binP, PaveY, false);
                            V2 kerb = lc + left * (ri.w / 2 - 1.6);
                            V2 travel = dir * side;
                            D.busStops.Add(new BusStopInfo { x = kerb.x, z = kerb.z, dx = travel.x, dz = travel.z });
                            // BUS STOP painted in the lane
                            placed = true;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ zebra crossings
        static void DressCrossings()
        {
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            UVRect white = S("white"), tactile = S("tactile");
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if ((ri.cls != "street" && ri.cls != "arterial") || ri.pave <= 0) continue;
                List<double[]> both = Keep(ri.line.Length, Concat(Widen(ri.trimAll, 22), ri.boxCut, ri.boxCutL, ri.boxCutR), 40);
                foreach (double[] iv in both)
                {
                    double len = iv[1] - iv[0];
                    int n = len > 300 ? 2 : 1;
                    for (int k = 0; k < n; k++)
                    {
                        double ch = iv[0] + len * (k + 1) / (n + 1);
                        V2 dir; V2 c = ri.line.At(ch, out dir);
                        V2 l = dir.Left;
                        V2 kL = c + l * (ri.w / 2 + 0.45), kR = c - l * (ri.w / 2 + 0.45);
                        if (InBox(c) || NearOtherRoad(c, ri, 3) || !Free(kL, 1.5) || !Free(kR, 1.5)) continue;
                        bool nearStop = false;
                        foreach (BusStopInfo b in D.busStops) if (V2.Dist(new V2(b.x, b.z), c) < 35) nearStop = true;
                        if (nearStop) continue;

                        // stripes across the road, 0.6 m wide bars, crossing 3 m wide
                        double half = ri.w / 2 - 0.3;
                        for (double a = -half; a < half - 0.3; a += 1.2)
                        {
                            V2 sc = c + l * (a + 0.3);
                            HQuad(mSolid, sc, dir, 1.5, 0.3, PaintY, white);
                        }
                        // zig-zags along both kerbs on both approaches
                        foreach (int approach in new[] { -1, 1 })
                            foreach (int side in new[] { -1, 1 })
                            {
                                double lat = side * (ri.w / 2 - 0.5);
                                V2 prev = c + dir * (approach * 3.0) + l * lat;
                                for (int z = 1; z <= 8; z++)
                                {
                                    double latz = z % 2 == 1 ? lat - side * 0.3 : lat;       // in towards the middle and back
                                    V2 next = c + dir * (approach * (3.0 + z * 2.0)) + l * latz;
                                    HQuad(mSolid, (prev + next) * 0.5, next - prev, V2.Dist(prev, next) / 2, 0.05, PaintY, white);
                                    prev = next;
                                }
                            }
                        // Belisha beacons on both kerbs + tactile paving
                        foreach (V2 k2 in new[] { kL, kR })
                        {
                            Prefab("Belisha", k2, PaveY, dir);
                            Claim(k2, 1.5);
                            V2 tp = c + (k2 - c).Norm * (ri.w / 2 + 0.9);
                            HQuad(mSolid, tp, dir, 1.4, 0.45, PaveY + 0.006, tactile);
                        }
                        Claim(c + l * (ri.w / 2 + ri.pave / 2), 4); Claim(c - l * (ri.w / 2 + ri.pave / 2), 4);
                        D.crossings.Add(new[] { c.x, c.z, dir.x, dir.z, ri.w });
                        D.Count("zebra crossings");
                    }
                }
            }
        }

        static List<double[]> Widen(List<double[]> ivs, double m)
        {
            List<double[]> r = new List<double[]>(); foreach (double[] iv in ivs) r.Add(new[] { iv[0] - m, iv[1] + m }); return r;
        }

        // ------------------------------------------------------------------ junction corners: guard rails, bollards, post boxes
        static void DressJunctionCorners()
        {
            List<string> jids = new List<string>(juncs.Keys); jids.Sort();
            foreach (string jid in jids)
            {
                JInfo ji = juncs[jid];
                string dist = District(ji.c);
                if (dist == null) continue;
                bool postDone = drng.NextDouble() > (dist == "D_CANARY_PEN" ? 0.15 : 0.45);
                for (int k = 0; k < ji.legRoad.Count; k++)
                {
                    RoadInfo ri = ji.legRoad[k];
                    if (ri.pave <= 0 || ri.cls == "bridge" || ri.cls == "service") continue;
                    double jc = ChainageAt(ri, jid);
                    int towards = V2.Dot(ji.legDir[k], DirAt(ri, jc)) >= 0 ? 1 : -1;
                    foreach (int side in new[] { -1, 1 })
                    {
                        V2 dir; double start = jc + towards * (ji.radius + 0.8);
                        if (start < 1 || start > ri.line.Length - 1) continue;
                        ri.line.At(start, out dir);
                        V2 l = dir.Left * side;
                        V2 a = ri.line.At(start, out dir) + l * (ri.w / 2 + 0.3);
                        if (InBox(a) || NearOtherRoad(a, ri, 0.5)) continue;
                        // guard railing on arterials (classic London junction)
                        if (ri.cls == "arterial" && dist != "D_GILMOR")
                        {
                            double end = start + towards * 9;
                            V2 b = ri.line.At(end, out dir) + l * (ri.w / 2 + 0.3);
                            if (Free(Lerp(a, b, 0.5), 3) && !NearOtherRoad(b, ri, 0.5) && !InBox(b)) Railing(a, b, PaveY, 1.05);
                        }
                        else if (dist == "D_PECKAM" || dist == "D_KNIGHT")
                        {
                            for (int q = 0; q < 2; q++)
                            {
                                V2 bp = ri.line.At(start + towards * (0.2 + q * 1.3), out dir) + l * (ri.w / 2 + 0.35);
                                if (Free(bp, 0.4)) Bollard(bp, PaveY);
                            }
                        }
                        if (!postDone && dist != "D_CANARY_PEN")
                        {
                            V2 pp = ri.line.At(start + towards * 4.5, out dir) + l * (ri.w / 2 + 0.7);
                            if (OnClearPavement(pp, ri, 0.8)) { PostBox(pp, l * -1, PaveY); postDone = true; }
                        }
                    }
                }
            }
        }

        static V2 DirAt(RoadInfo ri, double ch) { V2 d; ri.line.At(ch, out d); return d; }

        // ------------------------------------------------------------------ street name plates
        static void DressStreetPlates()
        {
            HashSet<string> done = new HashSet<string>();
            List<string> jids = new List<string>(juncs.Keys); jids.Sort();
            foreach (string jid in jids)
            {
                JInfo ji = juncs[jid];
                for (int k = 0; k < ji.legRoad.Count; k++)
                {
                    RoadInfo ri = ji.legRoad[k];
                    if (ri.pave < 1.5 || ri.cls == "bridge") continue;
                    if (!A.Has(PlateKey(ri)) || done.Contains(jid + ri.r.id)) continue;
                    double jc = ChainageAt(ri, jid);
                    int towards = V2.Dot(ji.legDir[k], DirAt(ri, jc)) >= 0 ? 1 : -1;
                    int side = (k % 2 == 0) ? 1 : -1;
                    V2 dir; double ch = jc + towards * (ji.radius + 3.5);
                    if (ch < 1 || ch > ri.line.Length - 1) continue;
                    V2 lc = ri.line.At(ch, out dir);
                    V2 l = dir.Left * side;
                    V2 p = lc + l * (ri.w / 2 + ri.pave - 0.35);
                    if (!OnClearPavement(p, ri, 0.9) || District(p) == null) continue;
                    done.Add(jid + ri.r.id);
                    V2 n = l * -1, t = dir;     // faces the road
                    foreach (int s in new[] { -1, 1 }) SBox(mSolid, p + t * (0.62 * s), t, 0.03, 0.03, PaveY, PaveY + 1.42, S("black"));
                    SBox(mSolid, p, t, 0.72, 0.025, PaveY + 1.02, PaveY + 1.4, S("black"));
                    UVRect r = A[PlateKey(ri)];
                    VQuad(mSolid, p + n * 0.03 - t * 0.7, p + n * 0.03 + t * 0.7, PaveY + 1.04, PaveY + 1.38, r, n);
                    VQuad(mSolid, p - n * 0.03 + t * 0.7, p - n * 0.03 - t * 0.7, PaveY + 1.04, PaveY + 1.38, r, n * -1);
                    Claim(p, 0.9);
                    D.Count("street name plates");
                }
            }
        }

        // ------------------------------------------------------------------ shop fronts and pubs
        static void DressShopFronts()
        {
            int pubs = 0, gilShops = 0;
            foreach (BuildingInfo b in blds)
            {
                ShopSink(!KitAllowsShops(b));      // L1: houses get no shop fronts (rolled as before, built nowhere, so nothing else moves)
                string dist = b.district;
                if (dist == "D_CANARY_PEN") continue;
                // the face nearest a road with a pavement
                V2 f = b.dir.Norm, l = f.Left;
                V2[] normals = { l, l * -1, f, f * -1 };
                double[] halfDepth = { b.dep / 2, b.dep / 2, b.len / 2, b.len / 2 };
                double[] faceLen = { b.len, b.len, b.dep, b.dep };
                int bestFace = -1; double bestGap = 6; RoadInfo road = null;
                for (int i = 0; i < 4; i++)
                {
                    V2 fc = b.c + normals[i] * halfDepth[i];
                    foreach (RoadInfo ri in roads.Values)
                    {
                        if (ri.pave < 3 || ri.cls == "bridge") continue;
                        double gap = ri.line.DistanceTo(fc) - ri.w / 2 - ri.pave;
                        if (gap < bestGap && V2.Dot(normals[i], ClosestOn(ri, fc) - fc) > 0) { bestGap = gap; bestFace = i; road = ri; }
                    }
                }
                if (bestFace < 0) continue;
                V2 n = normals[bestFace], t = n.Left;
                V2 face = b.c + n * halfDepth[bestFace];
                double width = faceLen[bestFace];
                bool nearJunction = false;
                foreach (JInfo ji in juncs.Values) if (V2.Dist(ji.c, face) < ji.radius + width / 2 + 6) nearJunction = true;

                double chance = dist == "D_PECKAM" ? 0.85 : dist == "D_KNIGHT" ? (road.cls == "arterial" ? 0.55 : 0.2) : dist == "D_CANARY" ? 0.3 : 0.15;
                int units = Math.Max(1, (int)Math.Floor((width - 1.0) / 7.0));
                double unitW = (width - 1.0) / units;
                for (int u = 0; u < units; u++)
                {
                    V2 uc = face + t * (-width / 2 + 0.5 + unitW * (u + 0.5));
                    bool pub = (dist == "D_KNIGHT" || dist == "D_GILMOR") && nearJunction && pubs < Pubs.Length && u == 0 && drng.NextDouble() < 0.6;
                    if (pub) { Pub(uc, n, t, unitW, Pubs[pubs++]); continue; }
                    if (drng.NextDouble() > chance) continue;
                    string[][] list = dist == "D_PECKAM" ? AreaShops(PeckamShops) : dist == "D_KNIGHT" ? KnightShops : dist == "D_CANARY" ? CanaryShops : GilmoreShops;
                    if (dist == "D_GILMOR" && gilShops >= 4) continue;
                    if (dist == "D_GILMOR") gilShops++;
                    string[] shop = list[drng.Next(list.Length)];
                    bool shut = dist == "D_PECKAM" && drng.NextDouble() < 0.22;
                    Shop(uc, n, t, unitW, shop, shut);
                }
            }
            ShopSink(false);
        }

        static MeshData keepSolid, keepCut, keepGlow;
        static DressingResult keepD;
        /// While on, shop fronts and pubs are built into throwaway meshes (and not counted).
        static void ShopSink(bool on)
        {
            if (on && keepSolid == null)
            {
                keepSolid = mSolid; keepCut = mCut; keepGlow = mGlow; keepD = D;
                mSolid = new MeshData("Sink", "Dressing", "DressSolid", false); mCut = new MeshData("Sink", "Dressing", "DressCutout", false); mGlow = new MeshData("Sink", "Dressing", "DressGlow", false);
                D = new DressingResult { atlas = keepD.atlas };
            }
            else if (!on && keepSolid != null)
            {
                mSolid = keepSolid; mCut = keepCut; mGlow = keepGlow; D = keepD;
                keepSolid = keepCut = keepGlow = null; keepD = null;
            }
        }

        static V2 ClosestOn(RoadInfo ri, V2 q)
        {
            double best = double.MaxValue; V2 r = q;
            for (int i = 0; i + 1 < ri.line.p.Count; i++)
            {
                V2 a = ri.line.p[i], b = ri.line.p[i + 1], ab = b - a;
                double l2 = V2.Dot(ab, ab), tt = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
                V2 c = a + ab * tt; double d = V2.Dist(c, q);
                if (d < best) { best = d; r = c; }
            }
            return r;
        }

        static void Shop(V2 c, V2 n, V2 t, double w, string[] shop, bool shut)
        {
            double hw = w / 2 - 0.25;
            // fascia (lit sign) on a dark backing
            SBox(mSolid, c + n * 0.08, t, hw + 0.1, 0.08, 2.95, 3.85, S("charcoal"));
            VQuad(mGlow, c + n * 0.17 - t * hw, c + n * 0.17 + t * hw, 3.0, 3.8, A["shop_" + shop[0]], n);
            if (shut)
            {
                VQuad(mSolid, c + n * 0.05 - t * hw, c + n * 0.05 + t * hw, 0.0, 2.9, A["shutter"], n);
                if (drng.NextDouble() < 0.6) Graffiti(c + n * 0.05 - t * (hw * 0.8), c + n * 0.05 + t * (hw * 0.8), 0.4, 2.2, n);
                D.Count("shop fronts (shuttered)");
                return;
            }
            double doorW = 1.1, winW = hw * 2 - doorW - 0.2;
            V2 winC = c - t * (doorW / 2 + 0.1);
            D.shopLights.Add(new[] { c.x, c.z, n.x, n.z, w, 0 });
            SBox(mSolid, winC + n * 0.05, t, winW / 2, 0.06, 0, 0.5, S("charcoal"));                       // stall riser
            VQuad(mGlow, winC + n * 0.06 - t * (winW / 2), winC + n * 0.06 + t * (winW / 2), 0.5, 2.8, A["shopwin"], n);
            V2 doorC = c + t * (hw - doorW / 2);
            VQuad(mSolid, doorC + n * 0.05 - t * (doorW / 2), doorC + n * 0.05 + t * (doorW / 2), 0, 2.4, S("glass"), n);
            D.Count("shop fronts");
        }

        static void Pub(V2 c, V2 n, V2 t, double w, string name)
        {
            double hw = w / 2 - 0.2;
            SBox(mSolid, c + n * 0.1, t, hw + 0.15, 0.1, 0, 3.9, S("pubgreen"));
            D.shopLights.Add(new[] { c.x, c.z, n.x, n.z, w, 1 });
            VQuad(mGlow, c + n * 0.21 - t * hw, c + n * 0.21 + t * hw, 3.05, 3.8, A["pub_" + name], n);
            VQuad(mGlow, c + n * 0.21 - t * (hw - 0.2), c + n * 0.21 + t * (hw - 1.6), 0.7, 2.7, A["pubwin"], n);
            VQuad(mSolid, c + n * 0.21 + t * (hw - 1.4), c + n * 0.21 + t * (hw - 0.2), 0, 2.5, S("glass"), n);
            // hanging lanterns
            foreach (int s in new[] { -1, 1 }) SBox(mGlow, c + n * 0.5 + t * (s * hw * 0.6), t, 0.12, 0.12, 2.8, 3.05, S("yellow"));
            D.Count("pubs");
        }

        // ------------------------------------------------------------------ phone boxes
        static void DressPhoneBoxes()
        {
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.pave < 3 || ri.cls == "bridge") continue;
                for (int side = 1; side >= -1; side -= 2)
                {
                    List<double[]> ok = Stretches(ri, side, 12, 20);
                    for (double ch = 90 + (side < 0 ? 70 : 0); ch < ri.line.Length - 20; ch += 190)
                    {
                        if (!Inside(ch, ok, 3)) continue;
                        V2 dir; V2 lc = ri.line.At(ch, out dir);
                        V2 left = dir.Left * side;
                        V2 p = lc + left * (ri.w / 2 + ri.pave - 0.75);
                        string dist = District(p);
                        double chance = dist == "D_KNIGHT" ? 0.7 : dist == "D_PECKAM" ? 0.35 : dist == "D_GILMOR" ? 0.15 : 0;
                        if (drng.NextDouble() > chance) continue;
                        int n = dist == "D_KNIGHT" && drng.NextDouble() < 0.5 ? 2 : 1;
                        bool ok2 = true;
                        for (int i = 0; i < n; i++) if (!OnClearPavement(p + dir * (i * 1.15), ri, 1.0)) ok2 = false;
                        if (!ok2) continue;
                        for (int i = 0; i < n; i++)
                        {
                            V2 q = p + dir * (i * 1.15);
                            Prefab("PhoneBox", q, PaveY, left);       // door (−Z) faces the road
                            Collider("PhoneBox", q, dir, 0.95, 0.95, PaveY, PaveY + 2.7);
                            Claim(q, 0.9);
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ benches + bins along the pavements
        static void DressBenchesAndBins()
        {
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.pave < 3 || ri.cls == "bridge") continue;
                for (int side = 1; side >= -1; side -= 2)
                {
                    List<double[]> ok = Stretches(ri, side, 10, 12);
                    for (double ch = 30 + (side < 0 ? 25 : 0); ch < ri.line.Length - 10; ch += 50)
                    {
                        if (!Inside(ch, ok, 2)) continue;
                        V2 dir; V2 lc = ri.line.At(ch, out dir);
                        V2 left = dir.Left * side;
                        V2 back = lc + left * (ri.w / 2 + ri.pave - 0.55), kerb = lc + left * (ri.w / 2 + 0.55);
                        string dist = District(back);
                        if (dist == null) continue;
                        bool posh = dist == "D_KNIGHT" || dist == "D_CANARY";
                        double benchChance = dist == "D_KNIGHT" ? 0.45 : dist == "D_CANARY" ? 0.5 : dist == "D_PECKAM" ? 0.2 : 0.12;
                        if (drng.NextDouble() < benchChance && OnClearPavement(back, ri, 1.0))
                        {
                            Bench(back, left * -1, PaveY);
                            V2 bp = back + dir * 1.3;
                            if (Free(bp, 0.35)) Bin(bp, PaveY, posh);
                        }
                        else if (drng.NextDouble() < 0.35 && OnClearPavement(kerb, ri, 0.5)) Bin(kerb, PaveY, posh);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ street trees
        static void DressStreetTrees()
        {
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.cls == "bridge" || ri.cls == "service") continue;
                bool ring = ri.cls == "ring";
                for (int side = 1; side >= -1; side -= 2)
                {
                    List<double[]> ok = Stretches(ri, side, ring ? 25 : 8, 10);
                    double step = ring ? 24 : 16;
                    for (double ch = 6 + (side < 0 ? step / 2 : 0); ch < ri.line.Length - 6; ch += step)
                    {
                        double c2 = ch + (drng.NextDouble() - 0.5) * 3;
                        if (!Inside(c2, ok, 1)) continue;
                        V2 dir; V2 lc = ri.line.At(c2, out dir);
                        V2 left = dir.Left * side;
                        V2 p = lc + left * (ring ? ri.w / 2 + 1.6 : ri.w / 2 + 0.75);
                        string dist = District(p);
                        double every = ring ? 0.85 : dist == "D_KNIGHT" ? 0.95 : dist == "D_CANARY" ? 0.7 : dist == "D_GILMOR" ? 0.4 : dist == "D_PECKAM" ? 0.25 : 0;
                        if (drng.NextDouble() > every) continue;
                        if (ri.cls == "street" && dist == "D_CANARY_PEN") continue;
                        if (!OnClearPavement(p, ri, 2.0)) continue;
                        if (ring && (river.DistanceTo(p) < WallOuter + 6)) continue;
                        bool nearLamp = false;
                        foreach (LampInfo lp in lampList) if (V2.Dist(new V2(lp.x, lp.z), p) < 3.5) nearLamp = true;
                        if (nearLamp) continue;
                        Tree(p, ring ? 0 : PaveY, !ring, true);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ billboards
        static void DressBillboards()
        {
            int made = 0;
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.cls != "ring" && ri.cls != "arterial") continue;
                for (int side = 1; side >= -1 && made < 10; side -= 2)
                {
                    List<double[]> ok = Stretches(ri, side, 30, 40);
                    for (double ch = 150; ch < ri.line.Length - 40 && made < 10; ch += 420)
                    {
                        if (!Inside(ch, ok, 10)) continue;
                        V2 dir; V2 lc = ri.line.At(ch, out dir);
                        V2 left = dir.Left * side;
                        V2 p = lc + left * (ri.w / 2 + Math.Max(ri.pave, ri.verge) + 3.4);
                        ParkPlan foot = new ParkPlan { c = p, dir = dir, hl = 4.6, hw = 0.5 };
                        if (!GridFree(foot, 0.2) || !Free(p, 5) || InBox(p) || NearOtherRoad(p, ri, 3) || river.DistanceTo(p) < WallOuter + 4) continue;
                        bool inPark = false; foreach (LwPoly pk in W.parks) if (InPoly(p, Pts(pk.polygon))) inPark = true;
                        if (inPark) continue;
                        V2 n = left * -1;    // faces the road
                        // angled a little towards oncoming traffic (UK: traffic on this side comes along -dir*side... face both ways by angling 15°)
                        foreach (int s in new[] { -1, 1 })
                        {
                            V2 post = p + dir * (s * 3.2);
                            SBox(mSolid, post, dir, 0.15, 0.15, 0, 3.0, S("charcoal"));
                            Collider("BillboardPost", post, dir, 0.3, 0.3, 0, 3.0);
                        }
                        SBox(mSolid, p - n * 0.12, dir, 4.25, 0.1, 2.85, 7.15, S("charcoal"));
                        VQuad(mGlow, p - dir * 4.0, p + dir * 4.0, 3.0, 7.0, A["ad_" + (made % Ads.Length)], n);
                        Claim(p, 5);
                        made++;
                        D.Count("billboards");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the estate: big bins, bags, trolleys, a mattress
        static void DressEstates()
        {
            foreach (BuildingInfo b in blds)
            {
                if (b.district != "D_GILMOR") continue;
                V2 f = b.dir.Norm, l = f.Left;
                foreach (int end in new[] { -1, 1 })
                {
                    if (drng.NextDouble() < 0.6) continue;
                    V2 e = b.c + f * (end * (b.len / 2 + 1.8));
                    bool roadside = false;
                    foreach (RoadInfo ri in roads.Values) if (ri.line.DistanceTo(e) < ri.w / 2 + Math.Max(ri.pave, ri.verge) + 1.2) roadside = true;
                    if (roadside || !Free(e, 3)) continue;
                    int bins = 2 + drng.Next(2);
                    for (int i = 0; i < bins; i++)
                    {
                        V2 q = e + l * ((i - (bins - 1) / 2.0) * 1.45);
                        SBox(mSolid, q, f, 0.55, 0.65, 0, 1.15, S("estatebin"));
                        SBox(mSolid, q - f * 0.05, f, 0.62, 0.7, 1.15, 1.25, S("lid"));
                    }
                    Collider("EstateBins", e, l, bins * 1.45, 1.2, 0, 1.25);
                    if (drng.NextDouble() < 0.5)
                        for (int k = 0; k < 2 + drng.Next(3); k++)
                        {
                            V2 q = e + f * (end * (1.0 + drng.NextDouble() * 0.6)) + l * ((drng.NextDouble() - 0.5) * 3);
                            SBox(mSolid, q, new V2(drng.NextDouble() - 0.5, drng.NextDouble() - 0.5), 0.25, 0.22, 0, 0.45, S("bag"));
                        }
                    if (drng.NextDouble() < 0.2) Trolley(e + f * (end * 2.2) + l * 2.2, new V2(drng.NextDouble() - 0.5, drng.NextDouble() - 0.5));
                    if (drng.NextDouble() < 0.1)
                    {
                        V2 wallP = b.c + f * (end * (b.len / 2 + 0.25)) + l * 3;
                        SBox(mSolid, wallP, l, 0.7, 0.12, 0, 1.9, S("white"));
                        VQuad(mSolid, wallP + f * (end * 0.13) - l * 0.7, wallP + f * (end * 0.13) + l * 0.7, 0, 1.9, A["mattress"], f * end);
                        D.Count("mattresses");
                    }
                    Claim(e, 2.6);
                    D.Count("estate bin stores");
                }
            }
        }

        static void Trolley(V2 p, V2 dir)
        {
            dir = dir.Norm; V2 l = dir.Left;
            UVRect m = S("metal");
            SBox(mSolid, p, dir, 0.45, 0.28, 0.35, 0.37, m);
            foreach (int s in new[] { -1, 1 }) SBox(mSolid, p + l * (s * 0.28), dir, 0.45, 0.015, 0.35, 0.95, m);
            SBox(mSolid, p - dir * 0.45, l, 0.3, 0.015, 0.35, 1.0, m);
            SBox(mSolid, p + dir * 0.45, l, 0.3, 0.015, 0.35, 0.95, m);
            SBox(mSolid, p - dir * 0.55, l, 0.3, 0.02, 1.0, 1.04, S("postred"));
            foreach (int a in new[] { -1, 1 }) foreach (int s in new[] { -1, 1 }) SBox(mSolid, p + dir * (a * 0.38) + l * (s * 0.24), dir, 0.015, 0.015, 0, 0.35, m);
            Claim(p, 0.7);
            D.Count("shopping trolleys");
        }

        // ------------------------------------------------------------------ graffiti on walls
        static void DressGraffitiWalls()
        {
            foreach (BuildingInfo b in blds)
            {
                double chance = b.district == "D_GILMOR" ? 0.6 : b.district == "D_PECKAM" ? (ExpansionMode ? 0.5 : 0.3) : 0;      // E1: Brixtun has more
                if (drng.NextDouble() > chance) continue;
                V2 f = b.dir.Norm, l = f.Left;
                // the ends of terraces / blocks (side walls), plus long walls on the estate
                List<int> faces = new List<int> { 2, 3 };
                if (b.district == "D_GILMOR") { faces.Add(0); faces.Add(1); }
                int face = faces[drng.Next(faces.Count)];
                V2 n = face == 0 ? l : face == 1 ? l * -1 : face == 2 ? f : f * -1;
                double half = face < 2 ? b.dep / 2 : b.len / 2, wlen = face < 2 ? b.len : b.dep;
                V2 t = n.Left;
                double w = Math.Min(wlen - 2, 3.5 + drng.NextDouble() * 2.5);
                if (w < 2.5) continue;
                double off = (drng.NextDouble() - 0.5) * (wlen - w - 1);
                V2 c = b.c + n * half + t * off;
                bool roadside = false;
                foreach (RoadInfo ri in roads.Values) if (ri.line.DistanceTo(c) < ri.w / 2 + ri.pave + 0.2) roadside = true;
                if (roadside) continue;
                double hgt = w * 0.44;
                Graffiti(c - t * (w / 2), c + t * (w / 2), 0.3, 0.3 + hgt, n);
            }
        }
    }
}
