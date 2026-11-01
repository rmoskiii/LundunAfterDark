// E8: Kingsmere, the royal quarter, north of the Ring (Lundun → 29). Pure C#, tested outside Unity.
// Built like Brixtun (its own roads, junctions, lamps, parks and Knightsbury-style streets from LundunExpansion.json,
// with its own seed and building grid), plus what's built by hand here:
//   Kingsmere Palace   a long pale-stone front facing down the Parade: three storeys of windows, a giant-columned centre
//                      with a pediment and the balcony (drapes and all), end pavilions, a balustraded roof, the royal
//                      standard; walled gardens behind
//   the forecourt      gravel inside tall railings on stone piers with gilded tops; a great centre gate and a west gate
//                      (closed) and an east gate (open: go in and the guards and the police come for you); sentry boxes
//                      with guards in an original uniform (teal tunics, white cross-belts, tall white helmets with a
//                      crimson plume), two of them marching their beat
//   the Memorial       on the island in the Memorial Circle: white marble steps, a pedestal, a gilded winged figure with
//                      a torch, four fountains, red flower beds round it
//   the Parade         red surface from the Ring to the Circle, flagpoles both sides, plane trees along the park side,
//                      heritage lanterns; Kingsmere Arch across its south end (one great opening, two small ones)
//   St Jarvis's Park   a long lake with an island, two bridges, gravel paths, deckchairs, railings with gates; a zebra
//                      over the Ring to Hydemoor Park
//   the barracks       a parade ground behind railings, a long brick barracks with a clock tower
// Guards, the restricted forecourt and the warning zone in front of it go to LundunRoyalQuarter (runtime).
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    /// E8: a guard: where he stands, which way he faces (degrees), how far he marches (0 = stands his post).
    public class GuardInfo { public double x, z, yaw, march; }

    public static partial class Generator
    {
        public const string KingsmereName = "Kingsmere";
        /// Kingsmere's few shopping streets (Knightsbury-style shops on the ground floor); everywhere else is grand houses.
        static readonly HashSet<string> KingsmereShopRoads = new HashSet<string> { "Admiral's Walk", "Pall Row" };

        // the plan (no random numbers)
        static readonly V2 MemC = new V2(560, 1660);
        const double PalX0 = 470, PalX1 = 650, PalZ0 = 1765, PalZ1 = 1815, PalH = 22;
        const double CourtX0 = 455, CourtX1 = 665, CourtZ0 = 1708;       // the forecourt (railings along CourtZ0)
        const double KmPave = 0.15;
        static readonly List<V2> lakeC = new List<V2>();
        static readonly List<double> lakeHW = new List<double>();
        static readonly List<double[]> kmPaths = new List<double[]>();    // x0, z0, x1, z1, half-width
        static readonly double[] KmBridges = { 250, 420 };                  // x where the bridges cross the lake
        const double BridgeHW = 2.6;

        /// E8: after BuildKingsmere: the guards, the forecourt (restricted) and the strip in front of it (warning), as
        /// rectangles x0, z0, x1, z1; and Kingsmere's places for the map.
        public static readonly List<GuardInfo> KingsmereGuards = new List<GuardInfo>();
        public static readonly List<double[]> KingsmereRestricted = new List<double[]>();
        public static readonly List<double[]> KingsmereWarning = new List<double[]>();

        // ================================================================== building it
        /// The city first (for the roads it joins), then Kingsmere as an area of its own. Afterwards the generator is left
        /// set up for Kingsmere, so Dress(ExpansionWorld, result) and Streets(ExpansionWorld, …) dress it next.
        public static GreyboxResult BuildKingsmere(LundunWorldData world)
        {
            Build(world);
            GreyboxResult R = new GreyboxResult();
            LundunExpansionData x = world.expansion;
            bool any = false;
            if (x != null && x.roads != null) foreach (LwRoad r in x.roads) if (InKingsmere(r.id)) any = true;
            if (!any) return R;
            contextRoads = roads; cityJuncs = juncs;
            ExpansionArea = "KINGSMERE";
            LundunWorldData xw = AreaWorld(world);
            ExpansionWorld = xw;
            ExpansionMode = true;
            W = xw; rng = new Random(KingsmereSeed);
            PlanKingsmere();
            Prepare();
            BuildRoads(R);
            BuildJunctions(R);
            BuildLamps(R);
            BuildParks(R);
            BuildBuildings(R);
            BuildMarkers(R);
            BuildRoyal(R);
            List<MeshData> split = new List<MeshData>();
            foreach (MeshData m in R.meshes) { if (m.chunk) split.AddRange(Chunk(m)); else split.Add(m); }
            R.meshes = split;
            int v = 0; foreach (MeshData m in R.meshes) v += m.VertexCount;
            R.log.Add(string.Format("Kingsmere: {0} roads, {1} meshes, {2} vertices, {3} buildings, {4} lamps, {5} guards", roads.Count, R.meshes.Count, v, R.buildings.Count, R.lamps.Count, KingsmereGuards.Count));
            return R;
        }

        /// The lake, the park's paths, the guards and the zones (fixed, so they're there before the trees are planted).
        static void PlanKingsmere()
        {
            lakeC.Clear(); lakeHW.Clear(); kmPaths.Clear();
            double[][] lk = { new[] { 168.0, 1418, 10 }, new[] { 215.0, 1403, 20 }, new[] { 265.0, 1396, 26 }, new[] { 318.0, 1407, 25 },
                              new[] { 370.0, 1427, 23 }, new[] { 420.0, 1440, 25 }, new[] { 468.0, 1437, 20 }, new[] { 505.0, 1426, 10 } };
            foreach (double[] p in lk) { lakeC.Add(new V2(p[0], p[1])); lakeHW.Add(p[2]); }
            // gravel paths: round the park inside its railings, across it over the bridges, along both banks
            double[][] paths =
            {
                new[] { 150.0, 1196, 520, 1196, 2 }, new[] { 520.0, 1196, 520, 1630, 2 }, new[] { 520.0, 1630, 142, 1630, 2 }, new[] { 142.0, 1630, 142, 1196, 2 },
                new[] { 178.0, 1180, 250, 1330, 2 }, new[] { 250.0, 1330, 250, 1630, 2 },                                // the Hydemoor gate to the west bridge, on north
                new[] { 520.0, 1250, 420, 1360, 2 }, new[] { 420.0, 1360, 420, 1630, 2 },                                // the arch to the east bridge, on north
                new[] { 160.0, 1370, 500, 1386, 1.6 }, new[] { 160.0, 1460, 500, 1478, 1.6 },                            // along the banks
                new[] { 330.0, 1196, 330, 1350, 1.6 }, new[] { 330.0, 1500, 330, 1630, 1.6 },
            };
            kmPaths.AddRange(paths);
            // the guards: four at the sentry boxes, two marching the front of the palace, two at the barracks gate
            KingsmereGuards.Clear();
            foreach (double gx in new[] { 520.0, 534.0, 586.0, 600.0 }) KingsmereGuards.Add(new GuardInfo { x = gx, z = 1755.6, yaw = 180, march = 0 });
            KingsmereGuards.Add(new GuardInfo { x = 478, z = 1748, yaw = 90, march = 22 });
            KingsmereGuards.Add(new GuardInfo { x = 642, z = 1748, yaw = 270, march = 22 });
            KingsmereGuards.Add(new GuardInfo { x = 284, z = 1683.5, yaw = 180, march = 0 });
            KingsmereGuards.Add(new GuardInfo { x = 296, z = 1683.5, yaw = 180, march = 0 });
            KingsmereRestricted.Clear(); KingsmereWarning.Clear();
            KingsmereRestricted.Add(new[] { CourtX0, CourtZ0, CourtX1, PalZ1 + 75 });         // the forecourt, the palace and its gardens
            KingsmereRestricted.Add(new[] { 150.0, 1676, 430, 1880 });                          // the barracks
            KingsmereWarning.Add(new[] { CourtX0 - 6, CourtZ0 - 16, CourtX1 + 6, CourtZ0 });   // in front of the railings
        }

        /// Where trees mustn't go in Kingsmere's parks: the lake and its banks, the paths.
        static bool KingsmereKeepsClear(V2 q, double r)
        {
            if (LakeDist(q) < r + 3) return true;
            foreach (double[] p in kmPaths) if (Line.SegDist(q, new V2(p[0], p[1]), new V2(p[2], p[3])) < p[4] + r) return true;
            return false;
        }

        /// How far a point is from the lake's edge (negative inside).
        static double LakeDist(V2 q)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < lakeC.Count; i++)
            {
                V2 a = lakeC[i], b = lakeC[i + 1], ab = b - a;
                double t = Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / V2.Dot(ab, ab)));
                double hw = lakeHW[i] + (lakeHW[i + 1] - lakeHW[i]) * t;
                best = Math.Min(best, V2.Dist(q, a + ab * t) - hw);
            }
            return best;
        }

        // ------------------------------------------------------------------ the hand-built parts
        static MeshData kStone, kPave, kDeco, kRed, kWater, kBlock, kTrunk, kLeaf;
        static UVRect KS(int sw) { double u, v; KitArt.SwatchUV(sw, out u, out v); return new UVRect(u, v, u, v); }

        static void BuildRoyal(GreyboxResult R)
        {
            kStone = new MeshData("KM_Stone", "Kingsmere", KitMatB, true) { navArea = 1 };
            kPave = new MeshData("KM_Paving", "Kingsmere", KitMatB, true) { navArea = PavementArea };
            kDeco = new MeshData("KM_Detail", "Kingsmere", KitMatB, false) { navIgnore = true };
            kRed = new MeshData("KM_ParadeRed", "Kingsmere", KitMatB, false) { navIgnore = true };
            kWater = new MeshData("KM_Lake", "Kingsmere", "Water", false) { navIgnore = true };
            kBlock = new MeshData("KM_Blocks", "Kingsmere", "ColliderOnly", true) { navArea = 1 };
            kTrunk = new MeshData("KM_TreeTrunks", "Parks", "Trunk", true) { navIgnore = true };
            kLeaf = new MeshData("KM_TreeCanopies", "Parks", "Leaves", false) { navIgnore = true };
            Palace();
            Forecourt();
            Memorial();
            KingsmereArch();
            Parade();
            Lake();
            Barracks();
            HydemoorCrossing();
            foreach (MeshData m in new[] { kStone, kPave, kDeco, kRed, kWater, kBlock, kTrunk, kLeaf }) R.meshes.Add(m);
        }

        /// A solid wall (stone, with a collider) from a to b, thickness t, y0..y1.
        static void KWall(MeshData m, int sw, V2 a, V2 b, double t, double y0, double y1)
        {
            RBox(m, sw, (a + b) * 0.5, b - a, V2.Dist(a, b) / 2, t / 2, y0, y1, false);
        }

        /// An invisible wall (collider only), both faces.
        static void KBlock(V2 a, V2 b, double y0, double y1)
        {
            V2 n = (b - a).Norm.Left;
            foreach (int k in new[] { -1, 1 }) kBlock.Quad(new P3(a, y0), new P3(b, y0), new P3(b, y1), new P3(a, y1), n.x * k, 0, n.z * k);
        }

        /// A window on a wall facing o: dark glass in a white surround (and, if asked, a little pediment).
        static void KWindow(V2 c, V2 o, double w, double y0, double y1, bool pediment)
        {
            V2 f = o.Left;
            UVRect glass = KS(KitArt.S_DarkGrey), surround = KS(KitArt.S_White);
            VQuad(kDeco, c - f * (w / 2) + o * 0.03, c + f * (w / 2) + o * 0.03, y0, y1, glass, o);
            RBox(kDeco, KitArt.S_White, c + o * 0.06 - f * (w / 2 + 0.1), o, 0.06, 0.1, y0 - 0.12, y1 + 0.12, false);
            RBox(kDeco, KitArt.S_White, c + o * 0.06 + f * (w / 2 + 0.1), o, 0.06, 0.1, y0 - 0.12, y1 + 0.12, false);
            RBox(kDeco, KitArt.S_White, c + o * 0.08, f, w / 2 + 0.25, 0.1, y0 - 0.25, y0 - 0.05, false);
            if (pediment) RBox(kDeco, KitArt.S_White, c + o * 0.12, f, w / 2 + 0.35, 0.16, y1 + 0.18, y1 + 0.45, true);
            else RBox(kDeco, KitArt.S_White, c + o * 0.06, f, w / 2 + 0.2, 0.08, y1 + 0.05, y1 + 0.22, true);
        }

        // ------------------------------------------------------------------ Kingsmere Palace
        static void Palace()
        {
            V2 fr = new V2(0, -1);                      // the front faces south, down the Parade
            double cx = (PalX0 + PalX1) / 2;
            // the main block, the roof and its balustrade
            RBox(kStone, KitArt.S_Portland, new V2(cx, (PalZ0 + PalZ1) / 2), new V2(1, 0), (PalX1 - PalX0) / 2, (PalZ1 - PalZ0) / 2, 0, PalH, false);
            SwQuad(kDeco, KitArt.S_Lead, new P3(PalX0 + 1, PalH + 0.02, PalZ0 + 1, 0, 0), new P3(PalX1 - 1, PalH + 0.02, PalZ0 + 1, 0, 0), new P3(PalX1 - 1, PalH + 0.02, PalZ1 - 1, 0, 0), new P3(PalX0 + 1, PalH + 0.02, PalZ1 - 1, 0, 0), 0, 1, 0);
            V2[] corners = { new V2(PalX0, PalZ0), new V2(PalX1, PalZ0), new V2(PalX1, PalZ1), new V2(PalX0, PalZ1) };
            for (int i = 0; i < 4; i++)
            {
                V2 a = corners[i], b = corners[(i + 1) % 4], f = (b - a).Norm;
                RBox(kStone, KitArt.S_Portland, (a + b) * 0.5, f, V2.Dist(a, b) / 2, 0.35, PalH, PalH + 0.35, false);
                RBox(kStone, KitArt.S_Portland, (a + b) * 0.5, f, V2.Dist(a, b) / 2, 0.3, PalH + 1.3, PalH + 1.6, false);
                for (double t = 1.5; t < V2.Dist(a, b) - 1; t += 1.5) RBox(kDeco, KitArt.S_White, a + f * t, f, 0.12, 0.12, PalH + 0.35, PalH + 1.3, false);
                for (double t = 6; t < V2.Dist(a, b) - 3; t += 18) RBox(kDeco, KitArt.S_Portland, a + f * t, f, 0.4, 0.4, PalH + 1.6, PalH + 2.6, false);    // urns
            }
            // string courses and the cornice, all round
            foreach (double y in new[] { 7.0, 14.6 }) RBox(kDeco, KitArt.S_White, new V2(cx, (PalZ0 + PalZ1) / 2), new V2(1, 0), (PalX1 - PalX0) / 2 + 0.25, (PalZ1 - PalZ0) / 2 + 0.25, y, y + 0.35, true);
            RBox(kDeco, KitArt.S_White, new V2(cx, (PalZ0 + PalZ1) / 2), new V2(1, 0), (PalX1 - PalX0) / 2 + 0.5, (PalZ1 - PalZ0) / 2 + 0.5, PalH - 0.9, PalH - 0.2, true);
            // the ground floor's rustication (dark joints)
            for (double y = 1.0; y < 7.0; y += 0.75) VQuad(kDeco, new V2(PalX0, PalZ0 - 0.02), new V2(PalX1, PalZ0 - 0.02), y, y + 0.06, KS(KitArt.S_SootStone), fr);
            // windows: the front (three storeys, every 6 m), the sides and back (two storeys)
            for (double x = PalX0 + 4; x < PalX1 - 2; x += 6)
            {
                if (x > 526 && x < 594) continue;                       // the centre has its own
                V2 c = new V2(x, PalZ0);
                KWindow(c, fr, 1.6, 2.2, 5.4, false);
                KWindow(c, fr, 1.7, 8.6, 13.0, ((int)((x - PalX0) / 6)) % 2 == 0);
                KWindow(c, fr, 1.5, 16.0, 19.2, false);
            }
            for (double x = PalX0 + 5; x < PalX1 - 3; x += 8) { KWindow(new V2(x, PalZ1), new V2(0, 1), 1.6, 8.6, 13.0, false); KWindow(new V2(x, PalZ1), new V2(0, 1), 1.5, 16.0, 19.2, false); }
            foreach (double sx in new[] { PalX0, PalX1 })
                for (double z = PalZ0 + 6; z < PalZ1 - 3; z += 8) { V2 o = new V2(sx < cx ? -1 : 1, 0); KWindow(new V2(sx, z), o, 1.6, 8.6, 13.0, false); KWindow(new V2(sx, z), o, 1.5, 16.0, 19.2, false); }
            // the end pavilions, standing forward a little, with pediments
            foreach (double px in new[] { PalX0 + 9, PalX1 - 9 })
            {
                RBox(kStone, KitArt.S_Portland, new V2(px, PalZ0 - 1.0), new V2(1, 0), 9, 1.0, 0, PalH - 0.2, false);
                Pediment(new V2(px, PalZ0 - 2.0), 9.4, PalH - 0.2, 3.0);
                for (double x = px - 6; x <= px + 6.01; x += 6) { V2 c = new V2(x, PalZ0 - 2.0); KWindow(c, fr, 1.6, 2.2, 5.4, false); KWindow(c, fr, 1.7, 8.6, 13.0, true); KWindow(c, fr, 1.5, 16.0, 19.2, false); }
            }
            // the centre: a rusticated ground floor with three great doors, giant columns, a pediment, the balcony
            RBox(kStone, KitArt.S_Portland, new V2(cx, PalZ0 - 1.5), new V2(1, 0), 32, 1.5, 0, PalH + 0.4, false);
            foreach (double dx in new[] { -10.0, 0, 10 })
            {
                VQuad(kDeco, new V2(cx + dx - 2.0, PalZ0 - 3.03), new V2(cx + dx + 2.0, PalZ0 - 3.03), 0.15, 5.6, KS(KitArt.S_Black), fr);
                RBox(kDeco, KitArt.S_White, new V2(cx + dx, PalZ0 - 3.1), new V2(1, 0), 2.4, 0.12, 5.6, 6.2, false);
            }
            for (int i = 0; i < 8; i++)
            {
                double x = cx - 28 + i * 8;
                Cyl(kStone, new V2(x, PalZ0 - 3.9), 0.75, 7.4, PalH - 0.8, 12, KS(KitArt.S_White), false);
                RBox(kStone, KitArt.S_White, new V2(x, PalZ0 - 3.9), new V2(1, 0), 0.95, 0.95, 7.0, 7.4, false);
                RBox(kStone, KitArt.S_White, new V2(x, PalZ0 - 3.9), new V2(1, 0), 0.95, 0.95, PalH - 0.8, PalH - 0.4, false);
                if (i < 7) { KWindow(new V2(x + 4, PalZ0 - 3.0), fr, 1.8, 8.6, 13.4, true); KWindow(new V2(x + 4, PalZ0 - 3.0), fr, 1.6, 16.0, 19.2, false); }
            }
            RBox(kStone, KitArt.S_Portland, new V2(cx, PalZ0 - 3.6), new V2(1, 0), 32.5, 1.2, PalH - 0.4, PalH + 1.2, false);
            Pediment(new V2(cx, PalZ0 - 4.8), 33, PalH + 1.2, 5.5);
            RBox(kDeco, KitArt.S_Yellow, new V2(cx, PalZ0 - 4.85), new V2(1, 0), 2.6, 0.1, PalH + 2.0, PalH + 4.4, false);     // the gilded arms in the pediment
            // the balcony: on the first floor, out over the doors, with its drapes
            double by = 7.6;
            RBox(kStone, KitArt.S_Portland, new V2(cx, PalZ0 - 6.2), new V2(1, 0), 20, 1.2, by - 0.4, by, true);
            for (double x = cx - 19.5; x <= cx + 19.51; x += 0.75) RBox(kDeco, KitArt.S_White, new V2(x, PalZ0 - 7.2), new V2(1, 0), 0.09, 0.09, by, by + 0.95, false);
            RBox(kDeco, KitArt.S_White, new V2(cx, PalZ0 - 7.2), new V2(1, 0), 20, 0.15, by + 0.95, by + 1.12, false);
            SwQuad(kDeco, KitArt.S_Maroon, new P3(cx - 14, by - 0.35, PalZ0 - 7.42, 0, 0), new P3(cx + 14, by - 0.35, PalZ0 - 7.42, 0, 0), new P3(cx + 14, by + 0.9, PalZ0 - 7.42, 0, 0), new P3(cx - 14, by + 0.9, PalZ0 - 7.42, 0, 0), 0, 0, -1);
            SwQuad(kDeco, KitArt.S_Yellow, new P3(cx - 14, by - 0.4, PalZ0 - 7.44, 0, 0), new P3(cx + 14, by - 0.4, PalZ0 - 7.44, 0, 0), new P3(cx + 14, by - 0.25, PalZ0 - 7.44, 0, 0), new P3(cx - 14, by - 0.25, PalZ0 - 7.44, 0, 0), 0, 0, -1);
            foreach (double dx in new[] { -10.0, 0, 10 }) VQuad(kDeco, new V2(cx + dx - 1.4, PalZ0 - 3.05), new V2(cx + dx + 1.4, PalZ0 - 3.05), by, by + 3.2, KS(KitArt.S_DarkGrey), fr);      // the balcony's windows
            // the flagpole on the roof (its flag comes from the dressing)
            Cyl(kDeco, new V2(cx, PalZ0 + 10), 0.12, PalH, PalH + 14, 8, KS(KitArt.S_White));
            Cyl(kDeco, new V2(cx, PalZ0 + 10), 0.25, PalH + 14, PalH + 14.4, 8, KS(KitArt.S_Yellow));
        }

        /// A triangular pediment facing south, centred at c, half-width hw, its base at y, rise h.
        static void Pediment(V2 c, double hw, double y, double h)
        {
            UVRect st = KS(KitArt.S_Portland), wh = KS(KitArt.S_White);
            V2 l = new V2(-hw, 0), r = new V2(hw, 0), back = new V2(0, 2.5);
            kStone.Tri(new P3(c + l, y, st.Cu, st.Cv), new P3(c + r, y, st.Cu, st.Cv), new P3(c, y + h, st.Cu, st.Cv), 0, 0, -1);
            foreach (int s in new[] { -1, 1 })
            {
                V2 e = s < 0 ? c + l : c + r;
                kStone.Quad(new P3(e, y, st.Cu, st.Cv), new P3(c, y + h, st.Cu, st.Cv), new P3(c + back, y + h, st.Cu, st.Cv), new P3(e + back, y, st.Cu, st.Cv), s * h, hw, 0);
                Beam(kDeco, e + new V2(0, -0.1), y + 0.1, c + new V2(0, -0.1), y + h + 0.1, 0.25, 0.18, wh);
            }
            RBox(kDeco, KitArt.S_White, c + new V2(0, 0.6), new V2(1, 0), hw, 0.8, y - 0.3, y + 0.1, true);
        }

        // ------------------------------------------------------------------ the forecourt: gravel, piers, gates, sentry boxes
        static void Forecourt()
        {
            double y = KmPave;
            // gravel at pavement height, with a kerb
            SwQuad(kPave, KitArt.S_PaleConc, new P3(CourtX0, y, CourtZ0, 0, 0), new P3(CourtX1, y, CourtZ0, 0, 0), new P3(CourtX1, y, PalZ0 - 0.05, 0, 0), new P3(CourtX0, y, PalZ0 - 0.05, 0, 0), 0, 1, 0);
            SwQuad(kPave, KitArt.S_SootStone, new P3(CourtX0, 0, CourtZ0 - 0.02, 0, 0), new P3(CourtX1, 0, CourtZ0 - 0.02, 0, 0), new P3(CourtX1, y, CourtZ0 - 0.02, 0, 0), new P3(CourtX0, y, CourtZ0 - 0.02, 0, 0), 0, 0, -1);
            // stone piers with gilded tops along the front, and the side walls back to the palace
            for (double x = CourtX0; x <= CourtX1 + 0.01; x += 15)
            {
                bool gate = (x > 548 && x < 572) || (x > 498 && x < 510) || (x > 610 && x < 622);
                if (gate) continue;
                PierAt(new V2(x, CourtZ0), 3.8);
            }
            foreach (double gx in new[] { 548.0, 572.0, 498.0, 510.0, 610.0, 622.0 }) PierAt(new V2(gx, CourtZ0), gx > 540 && gx < 580 ? 5.0 : 4.2);
            foreach (double sx in new[] { CourtX0, CourtX1 })
            {
                for (double z = CourtZ0 + 12; z < PalZ0; z += 14) PierAt(new V2(sx, z), 3.8);
                KBlock(new V2(sx, CourtZ0), new V2(sx, PalZ1), 0, 3.4);
            }
            // the railings' colliders (the railings themselves come from the dressing); the east gate stands open
            KBlock(new V2(CourtX0, CourtZ0), new V2(610, CourtZ0), 0, 3.4);
            KBlock(new V2(622, CourtZ0), new V2(CourtX1, CourtZ0), 0, 3.4);
            KBlock(new V2(CourtX0, PalZ1), new V2(PalX0, PalZ1), 0, 3.4); KBlock(new V2(PalX1, PalZ1), new V2(CourtX1, PalZ1), 0, 3.4);
            // sentry boxes (dark teal, gilded edges, a pitched roof)
            foreach (double bx in new[] { 520.0, 534.0, 586.0, 600.0 })
            {
                V2 c = new V2(bx, 1757.4);
                RBox(kStone, KitArt.S_Teal, c, new V2(1, 0), 0.65, 0.65, KmPave, KmPave + 2.6, false);
                VQuad(kDeco, c + new V2(-0.48, -0.66), c + new V2(0.48, -0.66), KmPave + 0.05, KmPave + 2.2, KS(KitArt.S_Black), new V2(0, -1));
                RBox(kDeco, KitArt.S_Yellow, c, new V2(1, 0), 0.7, 0.7, KmPave + 2.6, KmPave + 2.7, false);
                UVRect rf = KS(KitArt.S_Teal);
                foreach (int s in new[] { -1, 1 })
                    kStone.Quad(new P3(c + new V2(-0.75, 0.75 * s), KmPave + 2.7, rf.Cu, rf.Cv), new P3(c + new V2(0.75, 0.75 * s), KmPave + 2.7, rf.Cu, rf.Cv),
                                new P3(c + new V2(0.75, 0), KmPave + 3.3, rf.Cu, rf.Cv), new P3(c + new V2(-0.75, 0), KmPave + 3.3, rf.Cu, rf.Cv), 0, 1, 0.6 * s);
                RBox(kDeco, KitArt.S_Yellow, c, new V2(1, 0), 0.08, 0.08, KmPave + 3.3, KmPave + 3.7, false);
            }
            // a lamp standard either side of the centre gate
            foreach (double lx in new[] { 543.0, 577.0 })
            {
                Cyl(kDeco, new V2(lx, CourtZ0 - 1.2), 0.3, 0, 1.0, 10, KS(KitArt.S_Black));
                Cyl(kDeco, new V2(lx, CourtZ0 - 1.2), 0.12, 1.0, 5.2, 8, KS(KitArt.S_Black));
                RBox(kDeco, KitArt.S_Yellow, new V2(lx, CourtZ0 - 1.2), new V2(1, 0), 0.4, 0.4, 5.2, 6.0, true);
            }
        }

        static void PierAt(V2 c, double h)
        {
            RBox(kStone, KitArt.S_Portland, c, new V2(1, 0), 0.55, 0.55, 0, h, false);
            RBox(kStone, KitArt.S_Portland, c, new V2(1, 0), 0.7, 0.7, h, h + 0.25, true);
            RBox(kDeco, KitArt.S_Yellow, c, new V2(1, 0), 0.3, 0.3, h + 0.25, h + 0.75, false);
            RBox(kDeco, KitArt.S_Yellow, c, new V2(1, 0), 0.12, 0.12, h + 0.75, h + 1.1, false);
        }

        // ------------------------------------------------------------------ the Memorial
        static void Memorial()
        {
            V2 c = MemC;
            UVRect wh = KS(KitArt.S_White), gd = KS(KitArt.S_Yellow);
            // stepped marble podium
            double[][] tiers = { new[] { 15.0, 0, 0.45 }, new[] { 13.2, 0.45, 0.9 }, new[] { 11.4, 0.9, 1.35 }, new[] { 9.6, 1.35, 1.8 } };
            foreach (double[] t in tiers) Cyl(kStone, c, t[0], t[1], t[2], 28, wh);
            // the pedestal, its cornice, an upper stage, a globe, the gilded figure with her torch and wings
            RBox(kStone, KitArt.S_White, c, new V2(1, 0), 3.6, 3.6, 1.8, 9.0, false);
            RBox(kStone, KitArt.S_White, c, new V2(1, 0), 4.0, 4.0, 9.0, 9.6, true);
            RBox(kStone, KitArt.S_White, c, new V2(1, 0), 2.6, 2.6, 9.6, 12.4, false);
            Cyl(kDeco, c, 1.3, 12.4, 14.4, 12, gd);
            RBox(kDeco, KitArt.S_Yellow, c, new V2(1, 0), 0.55, 0.4, 14.4, 17.2, false);           // body
            RBox(kDeco, KitArt.S_Yellow, c, new V2(1, 0), 0.75, 0.55, 14.4, 15.6, false);          // robes
            RBox(kDeco, KitArt.S_Yellow, c, new V2(1, 0), 0.22, 0.22, 17.2, 17.75, false);         // head
            RBox(kDeco, KitArt.S_Yellow, c + new V2(0.45, -0.1), new V2(0, 1), 0.12, 0.12, 16.8, 18.9, false);    // the raised arm
            RBox(kDeco, KitArt.S_Orange, c + new V2(0.45, -0.1), new V2(0, 1), 0.2, 0.2, 18.9, 19.5, false);      // the torch's flame
            foreach (int s in new[] { -1, 1 })
            {
                V2 w0 = c + new V2(0.35 * s, 0.35), w1 = c + new V2(2.4 * s, 0.9);
                Beam(kDeco, w0, 16.6, w1, 18.8, 0.05, 0.6, gd);                                    // wings
            }
            // the four sides: a seated stone figure on each, facing out
            foreach (V2 o in new[] { new V2(0, -1), new V2(1, 0), new V2(0, 1), new V2(-1, 0) })
            {
                V2 p = c + o * 5.2;
                RBox(kStone, KitArt.S_White, p, o.Left, 1.6, 0.9, 1.8, 3.2, false);
                RBox(kStone, KitArt.S_White, p - o * 0.3, o.Left, 0.6, 0.45, 3.2, 5.0, false);
                RBox(kStone, KitArt.S_White, p - o * 0.3, o.Left, 0.25, 0.25, 5.0, 5.5, false);
            }
            // fountains: basins on the diagonals, a jet in each
            foreach (double a in new[] { 45.0, 135, 225, 315 })
            {
                V2 p = c + new V2(Math.Cos(a * Math.PI / 180), Math.Sin(a * Math.PI / 180)) * 21;
                Cyl(kStone, p, 4.2, 0, 0.7, 20, wh);
                Cyl(kWater, p, 3.8, 0.71, 0.72, 20, wh);
                Cyl(kDeco, p, 0.6, 0.7, 1.5, 10, wh);
                Cyl(kWater, p, 0.18, 1.5, 3.6, 8, wh);
                Cyl(kWater, p, 0.7, 3.6, 3.9, 8, wh);
            }
            // flower beds: red, in a ring
            UVRect rd = KS(KitArt.S_Red), gn = KS(KitArt.S_Green);
            for (int i = 0; i < 48; i++)
            {
                if (i % 12 == 5 || i % 12 == 6) continue;                 // paths in through the beds
                double a0 = i * Math.PI * 2 / 48, a1 = (i + 1) * Math.PI * 2 / 48;
                V2 p0 = c + new V2(Math.Cos(a0), Math.Sin(a0)) * 27, p1 = c + new V2(Math.Cos(a1), Math.Sin(a1)) * 27;
                V2 q0 = c + new V2(Math.Cos(a0), Math.Sin(a0)) * 30.5, q1 = c + new V2(Math.Cos(a1), Math.Sin(a1)) * 30.5;
                kDeco.Quad(new P3(p0, 0.05, rd.Cu, rd.Cv), new P3(p1, 0.05, rd.Cu, rd.Cv), new P3(q1, 0.05, rd.Cu, rd.Cv), new P3(q0, 0.05, rd.Cu, rd.Cv), 0, 1, 0);
                kDeco.Quad(new P3(p0, 0.02, gn.Cu, gn.Cv), new P3(p0, 0.25, gn.Cu, gn.Cv), new P3(p1, 0.25, gn.Cu, gn.Cv), new P3(p1, 0.02, gn.Cu, gn.Cv), -(p0.x + p1.x - 2 * c.x), 0, -(p0.z + p1.z - 2 * c.z));
            }
        }

        // ------------------------------------------------------------------ Kingsmere Arch, across the Parade's south end
        static void KingsmereArch()
        {
            double z0 = 1212, z1 = 1228, x0 = 528, x1 = 592, h = 22;
            double[][] piers = { new[] { x0, 533.0 }, new[] { 541.0, 547.5 }, new[] { 572.5, 579.0 }, new[] { 587.0, x1 } };
            foreach (double[] p in piers) RBox(kStone, KitArt.S_Portland, new V2((p[0] + p[1]) / 2, (z0 + z1) / 2), new V2(1, 0), (p[1] - p[0]) / 2, (z1 - z0) / 2, 0, h, false);
            // over the openings: the great one (15 m clear), the two small ones (8 m)
            RBox(kStone, KitArt.S_Portland, new V2(560, (z0 + z1) / 2), new V2(1, 0), 12.5, (z1 - z0) / 2, 15.0, h, true);
            RBox(kStone, KitArt.S_Portland, new V2(537, (z0 + z1) / 2), new V2(1, 0), 4, (z1 - z0) / 2, 8.0, h, true);
            RBox(kStone, KitArt.S_Portland, new V2(583, (z0 + z1) / 2), new V2(1, 0), 4, (z1 - z0) / 2, 8.0, h, true);
            // the curve of the great opening: a half-round of thin stone bands (12.5 m radius, springing at 9.5 m)
            for (int k = 0; k < 14; k++)
            {
                double a0 = Math.PI * k / 14, a1 = Math.PI * (k + 1) / 14, am = (a0 + a1) / 2;
                double xx = 12.5 * Math.Cos(am), yy = 9.5 + 5.5 * Math.Sin(am);
                // fill from the curve up to the solid above it (15 m)
                double xa = 560 + 12.5 * Math.Cos(a0), xb = 560 + 12.5 * Math.Cos(a1);
                if (yy < 15.0) RBox(kStone, KitArt.S_Portland, new V2((xa + xb) / 2, (z0 + z1) / 2), new V2(1, 0), Math.Abs(xa - xb) / 2 + 0.02, (z1 - z0) / 2, yy, 15.0, true);
            }
            foreach (double fz in new[] { z0 - 0.05, z1 + 0.05 })
                for (int k = 0; k < 14; k++)
                {
                    double a0 = Math.PI * k / 14, a1 = Math.PI * (k + 1) / 14;
                    V2 p0 = new V2(560 + 12.9 * Math.Cos(a0), fz), p1 = new V2(560 + 12.9 * Math.Cos(a1), fz);
                    Beam(kDeco, p0, 9.5 + 5.9 * Math.Sin(a0), p1, 9.5 + 5.9 * Math.Sin(a1), 0.12, 0.25, KS(KitArt.S_White));      // the moulding round it
                }
            RBox(kDeco, KitArt.S_White, new V2(560, (z0 + z1) / 2), new V2(1, 0), 32.6, (z1 - z0) / 2 + 0.6, 16.6, 17.3, true);
            RBox(kDeco, KitArt.S_White, new V2(560, (z0 + z1) / 2), new V2(1, 0), 32.4, (z1 - z0) / 2 + 0.4, h, h + 0.6, false);
            foreach (double fz in new[] { z0, z1 })
            {
                double o = fz == z0 ? -1 : 1;
                foreach (double cxp in new[] { 536.8, 544.2, 575.8, 583.2 })
                    Cyl(kDeco, new V2(cxp, fz + o * 0.7), 0.55, 0.6, 16.6, 10, KS(KitArt.S_White));
            }
        }

        // ------------------------------------------------------------------ the Parade: red surface, flagpoles, plane trees
        static void Parade()
        {
            UVRect rd = KS(KitArt.S_Red);
            double hw = 8 - 0.2, y = 0.032;
            kRed.Quad(new P3(560 - hw, y, 1176, rd.Cu, rd.Cv), new P3(560 + hw, y, 1176, rd.Cu, rd.Cv), new P3(560 + hw, y, 1620, rd.Cu, rd.Cv), new P3(560 - hw, y, 1620, rd.Cu, rd.Cv), 0, 1, 0);
            // round the Circle too
            for (int i = 0; i < 72; i++)
            {
                double a0 = i * Math.PI * 2 / 72, a1 = (i + 1) * Math.PI * 2 / 72;
                V2 d0 = new V2(Math.Cos(a0), Math.Sin(a0)), d1 = new V2(Math.Cos(a1), Math.Sin(a1));
                kRed.Quad(new P3(MemC + d0 * 36.2, y, rd.Cu, rd.Cv), new P3(MemC + d1 * 36.2, y, rd.Cu, rd.Cv), new P3(MemC + d1 * 49.8, y, rd.Cu, rd.Cv), new P3(MemC + d0 * 49.8, y, rd.Cu, rd.Cv), 0, 1, 0);
            }
            // flagpoles on the pavement edges (their flags come from the dressing)
            foreach (double z in ParadeFlagZ())
                foreach (int s in new[] { -1, 1 })
                {
                    V2 p = new V2(560 + s * 9.0, z);
                    Cyl(kDeco, p, 0.22, KmPave, KmPave + 0.6, 8, KS(KitArt.S_Black));
                    Cyl(kDeco, p, 0.07, KmPave + 0.6, KmPave + 10, 6, KS(KitArt.S_White));
                    Cyl(kDeco, p, 0.13, KmPave + 10, KmPave + 10.25, 6, KS(KitArt.S_Yellow));
                }
            // plane trees along the park side
            for (double z = 1244; z < 1600; z += 11)
            {
                V2 p = new V2(541.5, z);
                kTrunk.Box(p, new V2(0, 1), 0.28, 0.28, 0, 6, false, 0.1, 0, false);
                kLeaf.Box(p, new V2(0, 1), 3.2, 3.2, 4.5, 11.5, true, 0.1, 0, false);
            }
        }

        static List<double> ParadeFlagZ() { List<double> l = new List<double>(); for (double z = 1246; z < 1600; z += 22) l.Add(z); return l; }

        // ------------------------------------------------------------------ St Jarvis's Park: the lake, its island and bridges, the paths
        static void Lake()
        {
            UVRect wd = KS(KitArt.S_White), pc = KS(KitArt.S_PaleConc);
            // the banks, either side of the centreline
            List<V2> L = new List<V2>(), Rt = new List<V2>();
            for (int i = 0; i < lakeC.Count; i++)
            {
                V2 d = i == 0 ? (lakeC[1] - lakeC[0]).Norm : i == lakeC.Count - 1 ? (lakeC[i] - lakeC[i - 1]).Norm : (lakeC[i + 1] - lakeC[i - 1]).Norm;
                L.Add(lakeC[i] + d.Left * lakeHW[i]); Rt.Add(lakeC[i] - d.Left * lakeHW[i]);
            }
            const double yw = 0.24;
            for (int i = 0; i + 1 < lakeC.Count; i++)
                kWater.Quad(new P3(L[i], yw), new P3(L[i + 1], yw), new P3(Rt[i + 1], yw), new P3(Rt[i], yw), 0, 1, 0);
            // rounded ends
            foreach (int e in new[] { 0, lakeC.Count - 1 })
            {
                V2 c = lakeC[e], d = e == 0 ? (lakeC[0] - lakeC[1]).Norm : (lakeC[e] - lakeC[e - 1]).Norm;
                double r = lakeHW[e];
                List<V2> arcP = new List<V2>();
                for (int k = 0; k <= 8; k++) { double a = -Math.PI / 2 + Math.PI * k / 8; arcP.Add(c + d * (Math.Cos(a) * r) + d.Left * (Math.Sin(a) * r)); }
                for (int k = 0; k + 1 < arcP.Count; k++) kWater.Tri(new P3(c, yw), new P3(arcP[k], yw), new P3(arcP[k + 1], yw), 0, 1, 0);
                for (int k = 0; k + 1 < arcP.Count; k++) { KerbSeg(arcP[k], arcP[k + 1]); KBlock(arcP[k], arcP[k + 1], 0, 1.3); }
            }
            // kerbs along both banks, an invisible wall behind them, gaps where the bridges land
            foreach (List<V2> bank in new[] { L, Rt })
                for (int i = 0; i + 1 < bank.Count; i++)
                    foreach (double[] iv in BankSpans(bank[i], bank[i + 1]))
                    {
                        V2 a = bank[i] + (bank[i + 1] - bank[i]) * iv[0], b = bank[i] + (bank[i + 1] - bank[i]) * iv[1];
                        KerbSeg(a, b); KBlock(a, b, 0, 1.3);
                    }
            // the island, with three trees
            V2 isl = new V2(298, 1401);
            Cyl(kStone, isl, 8, 0, 0.45, 18, KS(KitArt.S_SootStone));
            Cyl(kDeco, isl, 7.6, 0.45, 0.47, 18, KS(KitArt.S_Green));
            for (int k = 0; k < 3; k++) { V2 p = isl + new V2(Math.Cos(k * 2.1) * 3.5, Math.Sin(k * 2.1) * 3.5); kTrunk.Box(p, new V2(0, 1), 0.22, 0.22, 0.45, 4.5, false, 0.1, 0, false); kLeaf.Box(p, new V2(0, 1), 2.4, 2.4, 3.2, 8.5, true, 0.1, 0, false); }
            // the bridges: a low stone causeway with a pale deck, ramps at each end, railings both sides
            foreach (double bx in KmBridges)
            {
                double zc, hw; LakeAcross(bx, out zc, out hw);
                double za = zc - hw - 3.5, zb = zc + hw + 3.5, top = 0.55;
                RBox(kStone, KitArt.S_Portland, new V2(bx, zc), new V2(0, 1), (zb - za) / 2 - 2, BridgeHW, 0, top, false);
                SwQuad(kPave, KitArt.S_PaleConc, new P3(bx - BridgeHW, top + 0.01, za + 2, 0, 0), new P3(bx + BridgeHW, top + 0.01, za + 2, 0, 0), new P3(bx + BridgeHW, top + 0.01, zb - 2, 0, 0), new P3(bx - BridgeHW, top + 0.01, zb - 2, 0, 0), 0, 1, 0);
                foreach (int e in new[] { -1, 1 })
                {
                    double zr0 = e < 0 ? za : zb, zr1 = e < 0 ? za + 2 : zb - 2;
                    SwQuad(kPave, KitArt.S_PaleConc, new P3(bx - BridgeHW, 0.02, zr0, 0, 0), new P3(bx + BridgeHW, 0.02, zr0, 0, 0), new P3(bx + BridgeHW, top + 0.01, zr1, 0, 0), new P3(bx - BridgeHW, top + 0.01, zr1, 0, 0), 0, 1, -e * 0.27);
                }
                foreach (int s in new[] { -1, 1 })
                {
                    V2 a = new V2(bx + s * BridgeHW, za + 1.2), b = new V2(bx + s * BridgeHW, zb - 1.2);
                    RBox(kStone, KitArt.S_Portland, (a + b) * 0.5, b - a, V2.Dist(a, b) / 2, 0.18, top, top + 0.95, false);
                    RBox(kDeco, KitArt.S_White, (a + b) * 0.5, b - a, V2.Dist(a, b) / 2 + 0.05, 0.22, top + 0.95, top + 1.05, false);
                    foreach (V2 e in new[] { a, b }) RBox(kStone, KitArt.S_Portland, e, new V2(0, 1), 0.3, 0.3, 0, top + 1.3, false);
                }
            }
            // gravel paths (just drawn on the grass; the ground under them is walkable anyway)
            foreach (double[] p in kmPaths)
            {
                V2 a = new V2(p[0], p[1]), b = new V2(p[2], p[3]), f = (b - a).Norm, l = f.Left;
                // skip the bits over the lake (the bridges carry them)
                double len = V2.Dist(a, b);
                for (double t = 0; t < len - 0.01; t += 4)
                {
                    double t1 = Math.Min(len, t + 4);
                    V2 q0 = a + f * t, q1 = a + f * t1;
                    if (LakeDist((q0 + q1) * 0.5) < 1.5) continue;
                    kDeco.Quad(new P3(q0 - l * p[4], 0.03, pc.Cu, pc.Cv), new P3(q1 - l * p[4], 0.03, pc.Cu, pc.Cv), new P3(q1 + l * p[4], 0.03, pc.Cu, pc.Cv), new P3(q0 + l * p[4], 0.03, pc.Cu, pc.Cv), 0, 1, 0);
                }
            }
        }

        static void KerbSeg(V2 a, V2 b)
        {
            if (V2.Dist(a, b) < 0.05) return;
            RBox(kStone, KitArt.S_SootStone, (a + b) * 0.5, b - a, V2.Dist(a, b) / 2 + 0.15, 0.25, 0, 0.45, false);
        }

        /// The parts of a bank segment that aren't under a bridge (as fractions 0..1).
        static List<double[]> BankSpans(V2 a, V2 b)
        {
            List<double[]> cut = new List<double[]>();
            foreach (double bx in KmBridges)
            {
                double lo = Math.Min(a.x, b.x), hi = Math.Max(a.x, b.x);
                if (bx + BridgeHW < lo || bx - BridgeHW > hi || Math.Abs(b.x - a.x) < 1e-6) continue;
                double t0 = (bx - BridgeHW - 0.3 - a.x) / (b.x - a.x), t1 = (bx + BridgeHW + 0.3 - a.x) / (b.x - a.x);
                cut.Add(new[] { Math.Max(0, Math.Min(t0, t1)), Math.Min(1, Math.Max(t0, t1)) });
            }
            List<double[]> keep = new List<double[]>();
            double s = 0;
            cut.Sort((p, q) => p[0].CompareTo(q[0]));
            foreach (double[] c in cut) { if (c[0] > s) keep.Add(new[] { s, c[0] }); s = Math.Max(s, c[1]); }
            if (s < 1) keep.Add(new[] { s, 1.0 });
            return keep;
        }

        /// Where the lake is at x: the centre's z and the half-width there.
        static void LakeAcross(double x, out double zc, out double hw)
        {
            zc = lakeC[0].z; hw = lakeHW[0];
            for (int i = 0; i + 1 < lakeC.Count; i++)
                if (x >= lakeC[i].x && x <= lakeC[i + 1].x)
                {
                    double t = (x - lakeC[i].x) / (lakeC[i + 1].x - lakeC[i].x);
                    zc = lakeC[i].z + (lakeC[i + 1].z - lakeC[i].z) * t; hw = lakeHW[i] + (lakeHW[i + 1] - lakeHW[i]) * t;
                    return;
                }
        }

        // ------------------------------------------------------------------ the barracks: parade ground, barracks, clock tower; the gardens' walls
        static void Barracks()
        {
            double y = KmPave;
            SwQuad(kPave, KitArt.S_PaleConc, new P3(150, y, 1676, 0, 0), new P3(430, y, 1676, 0, 0), new P3(430, y, 1798, 0, 0), new P3(150, y, 1798, 0, 0), 0, 1, 0);
            SwQuad(kPave, KitArt.S_SootStone, new P3(150, 0, 1675.98, 0, 0), new P3(430, 0, 1675.98, 0, 0), new P3(430, y, 1675.98, 0, 0), new P3(150, y, 1675.98, 0, 0), 0, 0, -1);
            // the barracks block: yellow brick, stone bands, three floors of windows, a clock tower in the middle
            RBox(kStone, KitArt.S_YelBrick, new V2(290, 1838), new V2(1, 0), 140, 40, 0, 14, false);
            SwQuad(kDeco, KitArt.S_Lead, new P3(150.5, 14.02, 1798.5, 0, 0), new P3(429.5, 14.02, 1798.5, 0, 0), new P3(429.5, 14.02, 1877.5, 0, 0), new P3(150.5, 14.02, 1877.5, 0, 0), 0, 1, 0);
            foreach (double yb in new[] { 4.6, 9.2 }) RBox(kDeco, KitArt.S_Portland, new V2(290, 1838), new V2(1, 0), 140.2, 40.2, yb, yb + 0.3, true);
            RBox(kDeco, KitArt.S_Portland, new V2(290, 1838), new V2(1, 0), 140.3, 40.3, 13.4, 14.2, true);
            for (double x = 154; x < 428; x += 5)
            {
                if (x > 280 && x < 300) continue;
                foreach (double wy in new[] { 1.6, 6.0, 10.4 }) VQuad(kDeco, new V2(x - 0.8, 1797.97), new V2(x + 0.8, 1797.97), wy, wy + 2.2, KS(KitArt.S_DarkGrey), new V2(0, -1));
            }
            RBox(kStone, KitArt.S_YelBrick, new V2(290, 1797), new V2(1, 0), 7, 7, 0, 30, false);
            RBox(kDeco, KitArt.S_Portland, new V2(290, 1797), new V2(1, 0), 7.3, 7.3, 22, 22.6, true);
            UVRect ld = KS(KitArt.S_Lead);
            V2 tc = new V2(290, 1797);
            foreach (V2 o in new[] { new V2(0, -1), new V2(1, 0), new V2(0, 1), new V2(-1, 0) })
            {
                V2 l = o.Left;
                kDeco.Tri(new P3(tc + o * 7 + l * 7, 30, ld.Cu, ld.Cv), new P3(tc + o * 7 - l * 7, 30, ld.Cu, ld.Cv), new P3(tc, 36, ld.Cu, ld.Cv), o.x, 0.8, o.z);
            }
            VQuad(kDeco, new V2(286.5, 1789.97), new V2(293.5, 1789.97), 7.5, 13, KS(KitArt.S_DarkGrey), new V2(0, -1));          // the archway in
            // walls: the barracks' sides, the gardens round the back of the palace
            KWall(kStone, KitArt.S_YelBrick, new V2(150, 1676), new V2(150, 1798), 0.6, 0, 3.2);
            KWall(kStone, KitArt.S_YelBrick, new V2(430, 1676), new V2(430, 1798), 0.6, 0, 3.2);
            KWall(kStone, KitArt.S_YelBrick, new V2(430, 1700), new V2(CourtX0, 1700), 0.6, 0, 3.2);
            KWall(kStone, KitArt.S_RedBrick, new V2(448, PalZ1), new V2(448, 1888), 0.6, 0, 3.8);
            KWall(kStone, KitArt.S_RedBrick, new V2(672, PalZ1), new V2(672, 1888), 0.6, 0, 3.8);
            KWall(kStone, KitArt.S_RedBrick, new V2(448, 1888), new V2(672, 1888), 0.6, 0, 3.8);
            KWall(kStone, KitArt.S_RedBrick, new V2(CourtX0, PalZ1), new V2(PalX0, PalZ1), 0.6, 0, 3.8);
            KWall(kStone, KitArt.S_RedBrick, new V2(PalX1, PalZ1), new V2(CourtX1, PalZ1), 0.6, 0, 3.8);
            // the parade ground's railings (dressing) on stone piers, with a gate in the middle; their collider
            for (double x = 150; x <= 430.01; x += 14) if (x < 282 || x > 298) PierAt(new V2(x, 1676), 3.0);
            PierAt(new V2(282, 1676), 3.6); PierAt(new V2(298, 1676), 3.6);
            KBlock(new V2(150, 1676), new V2(430, 1676), 0, 3.2);
            // the guards' boxes at the gate
            foreach (double bx in new[] { 278.0, 302.0 })
            {
                V2 c = new V2(bx, 1679.4);
                RBox(kStone, KitArt.S_Teal, c, new V2(1, 0), 0.65, 0.65, y, y + 2.6, false);
                VQuad(kDeco, c + new V2(-0.48, -0.66), c + new V2(0.48, -0.66), y + 0.05, y + 2.2, KS(KitArt.S_Black), new V2(0, -1));
                RBox(kDeco, KitArt.S_Yellow, c, new V2(1, 0), 0.7, 0.7, y + 2.6, y + 2.75, false);
            }
        }

        // ------------------------------------------------------------------ a zebra over the Ring to Hydemoor Park
        static void HydemoorCrossing()
        {
            UVRect wh = KS(KitArt.S_White);
            double x0 = 175, x1 = 181;                       // lined up with Hydemoor Park's north gate (x 178) and St Jarvis's
            for (double z = 1150; z < 1170.5; z += 1.0)
                kDeco.Quad(new P3(x0, 0.046, z, wh.Cu, wh.Cv), new P3(x1, 0.046, z, wh.Cu, wh.Cv), new P3(x1, 0.046, z + 0.5, wh.Cu, wh.Cv), new P3(x0, 0.046, z + 0.5, wh.Cu, wh.Cv), 0, 1, 0);
        }

        // ================================================================== the dressing's part (flags, railings, deckchairs, lettering)
        static void KingsmereAtlas(DressAtlas a)
        {
            a.Sign("km_arch", 640, 80, "KINGSMERE", Col.Hex("cfc8b6"), Col.Hex("b8902e"), new Col(0, 0, 0, 0), 0.55);
            // the Lundun flag: navy, a gold band, a white wave for the river, a gold star
            int x, y;
            a.Alloc("km_flag", 192, 128, out x, out y);
            a.Fill(x, y, 192, 128, Col.Hex("1b2766"));
            a.Fill(x, y + 54, 192, 20, Col.Hex("d8a91f"));
            for (int i = 0; i < 192; i++) { double wy = y + 30 + Math.Sin(i * 0.11) * 6; a.Disc(x + i, wy, 3.2, Col.Hex("f2f2ee")); }
            a.Disc(x + 34, y + 98, 12, Col.Hex("d8a91f"));
            // the royal standard: quartered crimson and gold, a white tower in two of them
            a.Alloc("km_standard", 192, 128, out x, out y);
            for (int qx = 0; qx < 2; qx++) for (int qy = 0; qy < 2; qy++) a.Fill(x + qx * 96, y + qy * 64, 96, 64, (qx + qy) % 2 == 0 ? Col.Hex("9b1b2a") : Col.Hex("d8a91f"));
            foreach (int[] q in new[] { new[] { 0, 0 }, new[] { 1, 1 } })
            {
                int tx = x + q[0] * 96 + 34, ty = y + q[1] * 64 + 12;
                a.Fill(tx, ty, 28, 34, Col.Hex("f2f2ee"));
                for (int k = 0; k < 4; k++) a.Fill(tx - 2 + k * 9, ty + 34, 6, 7, Col.Hex("f2f2ee"));
                a.Fill(tx + 10, ty, 8, 12, Col.Hex("16171a"));
            }
            // deckchair canvas: green and white stripes
            a.Alloc("km_stripe", 64, 128, out x, out y);
            for (int i = 0; i < 8; i++) a.Fill(x + i * 8, y, 8, 128, i % 2 == 0 ? Col.Hex("1f6b3a") : Col.Hex("f2f2ee"));
            // the barracks' clock
            a.Alloc("km_clock", 128, 128, out x, out y);
            a.Fill(x, y, 128, 128, Col.Hex("16171a"));
            a.Disc(x + 64, y + 64, 58, Col.Hex("f2f2ee"));
            for (int k = 0; k < 12; k++) { double an = k * Math.PI / 6; a.Disc(x + 64 + Math.Sin(an) * 48, y + 64 + Math.Cos(an) * 48, 3.5, Col.Hex("16171a")); }
            a.Seg(x + 64, y + 64, x + 64, y + 104, 3, Col.Hex("16171a"));
            a.Seg(x + 64, y + 64, x + 92, y + 56, 4, Col.Hex("16171a"));
        }

        static void DressKingsmere()
        {
            // the palace railings: tall, between the piers, with gilded gates (the east one open)
            double ry = KmPave;
            List<double[]> gaps = new List<double[]> { new[] { 548.6, 571.4 }, new[] { 498.6, 509.4 }, new[] { 610.6, 621.4 } };
            for (double x = CourtX0; x < CourtX1 - 0.01; x += 15)
            {
                double x1 = Math.Min(CourtX1, x + 15);
                List<double[]> keep = Keep(x1 - x, ShiftGaps(gaps, x), 0.6);
                foreach (double[] iv in keep) Railing(new V2(x + iv[0] + 0.55, CourtZ0), new V2(x + iv[1] - 0.55, CourtZ0), ry, 3.2);
            }
            foreach (double sx in new[] { CourtX0, CourtX1 }) Railing(new V2(sx, CourtZ0 + 0.6), new V2(sx, PalZ0 - 0.2), ry, 3.2);
            GoldGate(548.6, 571.4, CourtZ0, ry, 4.4);
            GoldGate(498.6, 509.4, CourtZ0, ry, 3.6);
            // the barracks' railings and gate
            Railing(new V2(150.6, 1676), new V2(281.4, 1676), ry, 2.6);
            Railing(new V2(298.6, 1676), new V2(429.4, 1676), ry, 2.6);
            GoldGate(282.6, 297.4, 1676, ry, 3.0);
            // flags along the Parade (they fly away from the road), the royal standard over the palace
            foreach (double z in ParadeFlagZ())
                foreach (int s in new[] { -1, 1 }) Flag(new V2(560 + s * 9.0, z), new V2(0, -1), KmPave + 8.4, 2.0, "km_flag");
            Flag(new V2((PalX0 + PalX1) / 2, PalZ0 + 10), new V2(1, 0), PalH + 11.8, 3.6, "km_standard");
            // the arch's lettering, both faces
            foreach (int s in new[] { -1, 1 })
            {
                double fz = s < 0 ? 1212 - 0.03 : 1228 + 0.03;
                VQuad(mSolid, new V2(552, fz), new V2(568, fz), 18.2, 20.2, A["km_arch"], new V2(0, s));
            }
            // the barracks' clock, four faces
            V2 tc = new V2(290, 1797);
            foreach (V2 o in new[] { new V2(0, -1), new V2(1, 0), new V2(0, 1), new V2(-1, 0) })
                VQuad(mSolid, tc + o * 7.04 - o.Left * 2.4, tc + o * 7.04 + o.Left * 2.4, 24.2, 28.6, A["km_clock"], o);
            // deckchairs on the lawns south of the lake, in loose rows, facing the water
            for (double x = 190; x < 480; x += 26)
            {
                double zc, hw; LakeAcross(x, out zc, out hw);
                double zr = zc - hw - 9;
                for (int k = 0; k < 6; k++)
                {
                    V2 p = new V2(x + k * 1.6 + ((int)x % 3) * 0.4, zr - (k % 2) * 0.8);
                    if (!Free(p, 0.8)) continue;
                    Deckchair(p, new V2(0, 1));
                }
            }
            // a pair of benches facing each fountain on the island? (the island's grass is the memorial's) — benches by the Circle's paths
            foreach (double a in new[] { 90.0, 270 })
            {
                V2 d = new V2(Math.Cos(a * Math.PI / 180), Math.Sin(a * Math.PI / 180));
                V2 p = MemC + d * 31.5 + d.Left * 4;
                if (Free(p, 1.0)) Bench(p, d * -1, 0.012);
            }
            // Belisha beacons at the zebra to Hydemoor Park
            foreach (double[] b in new[] { new[] { 173.6, 1147.6 }, new[] { 182.4, 1172.6 } })
                Prefab("Belisha", new V2(b[0], b[1]), 0.02, new V2(0, b[1] < 1160 ? 1 : -1));
            D.Count("Kingsmere flags", ParadeFlagZ().Count * 2 + 1);
        }

        static List<double[]> ShiftGaps(List<double[]> gaps, double x)
        {
            List<double[]> r = new List<double[]>();
            foreach (double[] g in gaps) r.Add(new[] { g[0] - x, g[1] - x });
            return r;
        }

        /// A pair of gilded gates closing x0..x1 on the line z (both faces), with a crest on top.
        static void GoldGate(double x0, double x1, double z, double y, double h)
        {
            UVRect gd = S("gold"), bk = S("black");
            double mid = (x0 + x1) / 2;
            for (double x = x0 + 0.2; x < x1 - 0.1; x += 0.22) SBox(mSolid, new V2(x, z), new V2(1, 0), 0.025, 0.025, y, y + h - 0.2 + Math.Abs(x - mid) * -0.06 + 0.4, bk);
            foreach (double yy in new[] { y + 0.4, y + h * 0.55, y + h - 0.4 }) SBox(mSolid, new V2(mid, z), new V2(1, 0), (x1 - x0) / 2, 0.04, yy, yy + 0.12, gd);
            SBox(mSolid, new V2(mid, z), new V2(1, 0), 0.6, 0.06, y + h - 0.3, y + h + 0.6, gd);
            D.colliders.Add(new DressBox { name = "Gate", cx = mid, cy = y + h / 2, cz = z, sx = x1 - x0, sy = h, sz = 0.2, yaw = 0 });
            D.Count("gates");
        }

        /// A flag on a pole at p, flying towards fly, its top at y, w wide (both faces).
        static void Flag(V2 p, V2 fly, double y, double w, string tile)
        {
            V2 a = p + fly * 0.1, b = p + fly * (w + 0.1);
            double h = w * 2 / 3;
            VQuad(mSolid, a, b, y - h, y, A[tile], fly.Left);
            VQuad(mSolid, b, a, y - h, y, A[tile], fly.Left * -1);
        }

        static void Deckchair(V2 p, V2 facing)
        {
            V2 f = facing.Norm, l = f.Left;
            UVRect wd = S("wood"), cv = A["km_stripe"];
            foreach (int s in new[] { -1, 1 })
            {
                SBox(mSolid, p + l * (0.3 * s) + f * 0.2, f, 0.55, 0.025, 0.0, 0.06, wd);
                Beam(mSolid, p + l * (0.3 * s) - f * 0.35, 0.05, p + l * (0.3 * s) + f * 0.05, 0.95, 0.025, 0.025, wd);
            }
            // the canvas: from the seat front up to the back rail
            V2 s0 = p + f * 0.35, s1 = p - f * 0.3;
            mSolid.Quad(new P3(s0 - l * 0.28, 0.32, cv.u0, cv.v0), new P3(s0 + l * 0.28, 0.32, cv.u1, cv.v0), new P3(s1 + l * 0.28, 0.2, cv.u1, cv.Cv), new P3(s1 - l * 0.28, 0.2, cv.u0, cv.Cv), f.x * 0.2, 1, f.z * 0.2);
            mSolid.Quad(new P3(s1 - l * 0.28, 0.2, cv.u0, cv.Cv), new P3(s1 + l * 0.28, 0.2, cv.u1, cv.Cv), new P3(p + f * 0.0 + l * 0.28, 0.9, cv.u1, cv.v1), new P3(p + f * 0.0 - l * 0.28, 0.9, cv.u0, cv.v1), f.x, 0.6, f.z);
            Claim(p, 0.6);
            D.Count("deckchairs");
        }

        // ================================================================== the guards (a figure in parts, for LundunRoyalGuard)
        /// One guard as five meshes in his own space (feet at 0, facing +z): body (with head, helmet and plume), the legs
        /// (each hanging from its hip, at y 0.95) and the arms (from the shoulders, at y 1.45; the right one carries the
        /// rifle). An original uniform: dark teal tunic with gold buttons and epaulettes, white cross-belts and gloves,
        /// navy trousers with a crimson stripe, black boots, a tall white helmet with a crimson plume.
        public static Dictionary<string, MeshData> GuardParts()
        {
            Dictionary<string, MeshData> d = new Dictionary<string, MeshData>();
            Func<string, MeshData> M = n => { MeshData m = new MeshData("Guard_" + n, "Kingsmere", KitMatB, false) { navIgnore = true, chunk = false }; d[n] = m; return m; };
            V2 o = new V2(0, 0), fwd = new V2(0, 1), side = new V2(1, 0);
            MeshData body = M("Body");
            RBox(body, KitArt.S_Teal, o, side, 0.24, 0.14, 0.92, 1.5, false);                          // tunic
            RBox(body, KitArt.S_Teal, o + fwd * 0.0, side, 0.25, 0.15, 0.86, 1.0, false);              // its skirts
            RBox(body, KitArt.S_White, o, side, 0.255, 0.155, 0.93, 1.0, false);                        // belt
            RBox(body, KitArt.S_Yellow, o, side, 0.05, 0.03, 0.95, 0.99, false);
            Beam(body, new V2(-0.2, 0.152), 1.48, new V2(0.2, 0.152), 1.0, 0.035, 0.012, KS(KitArt.S_White));     // cross-belts
            Beam(body, new V2(0.2, 0.152), 1.48, new V2(-0.2, 0.152), 1.0, 0.035, 0.012, KS(KitArt.S_White));
            for (int k = 0; k < 5; k++) RBox(body, KitArt.S_Yellow, new V2(0, 0.145), side, 0.018, 0.012, 1.03 + k * 0.09, 1.06 + k * 0.09, false);
            foreach (int s in new[] { -1, 1 }) RBox(body, KitArt.S_Yellow, new V2(0.24 * s, 0), side, 0.07, 0.12, 1.47, 1.52, false);    // epaulettes
            RBox(body, KitArt.S_Teal, o, side, 0.07, 0.07, 1.5, 1.58, false);                          // collar
            RBox(body, KitArt.S_Cream, o, side, 0.1, 0.11, 1.58, 1.82, false);                         // face
            RBox(body, KitArt.S_White, o, side, 0.125, 0.135, 1.76, 2.18, false);                      // the helmet
            RBox(body, KitArt.S_Yellow, o, side, 0.13, 0.14, 1.76, 1.8, false);
            RBox(body, KitArt.S_Black, new V2(0, 0.12), side, 0.11, 0.03, 1.74, 1.77, false);          // its peak
            RBox(body, KitArt.S_Yellow, new V2(0, 0.138), side, 0.04, 0.005, 1.92, 2.04, false);       // badge
            RBox(body, KitArt.S_Maroon, o, side, 0.05, 0.05, 2.18, 2.42, false);                       // plume
            foreach (int s in new[] { -1, 1 })
            {
                MeshData leg = M(s < 0 ? "LegL" : "LegR");
                V2 c = new V2(0.1 * s, 0);
                RBox(leg, KitArt.S_Navy, c, side, 0.085, 0.09, -0.85, 0.0, false);                       // (hanging from the hip at 0)
                RBox(leg, KitArt.S_Maroon, c + side * (0.086 * s), fwd, 0.02, 0.004, -0.8, -0.05, false);   // the stripe
                RBox(leg, KitArt.S_Black, c + fwd * 0.03, side, 0.075, 0.13, -0.95, -0.82, false);       // boot
                MeshData arm = M(s < 0 ? "ArmL" : "ArmR");
                V2 ac = new V2(0.31 * s, 0);
                RBox(arm, KitArt.S_Teal, ac, side, 0.06, 0.07, -0.62, 0.02, false);
                RBox(arm, KitArt.S_Yellow, ac, side, 0.065, 0.075, -0.6, -0.55, false);                  // cuff
                RBox(arm, KitArt.S_White, ac, side, 0.05, 0.06, -0.74, -0.6, false);                     // glove
                if (s > 0)
                {
                    RBox(arm, KitArt.S_Timber, ac + fwd * 0.08, side, 0.025, 0.04, -0.7, 0.35, false);   // the rifle, at the shoulder
                    RBox(arm, KitArt.S_Black, ac + fwd * 0.08, side, 0.015, 0.02, 0.35, 0.7, false);
                }
            }
            return d;
        }

        // ================================================================== the map
        /// Kingsmere's places (after BuildKingsmere): id, name, kind, where SET ROUTE takes you, written on the map?
        public static List<MapPlace> KingsmerePlaces()
        {
            return new List<MapPlace>
            {
                new MapPlace { id = "LANDMARK_KINGSMERE_PALACE", name = "Kingsmere Palace", kind = "landmark", x = 560, z = 1703, label = true },
                new MapPlace { id = "LANDMARK_THE_PARADE", name = "The Parade", kind = "landmark", x = 560, z = 1420, label = true },
                new MapPlace { id = "LANDMARK_ST_JARVISS_PARK", name = "St Jarvis's Park", kind = "landmark", x = 330, z = 1653, label = true },
                new MapPlace { id = "LANDMARK_KINGSMERE_ARCH", name = "Kingsmere Arch", kind = "landmark", x = 560, z = 1190, label = false },
                new MapPlace { id = "LANDMARK_KINGSMERE_MEMORIAL", name = "Kingsmere Memorial", kind = "landmark", x = 560, z = 1617, label = false },
            };
        }

        /// For the map picture (MapRaster): Kingsmere's hand-built parts as shapes, kinds as RetailMapPoly's plus
        /// 10 = gravel / paving, 11 = stone building, 12 = water, 13 = the red Parade, 14 = white marble, 15 = park path.
        public static List<RetailMapPoly> KingsmereMap()
        {
            if (lakeC.Count < 2) PlanKingsmere();
            List<RetailMapPoly> l = new List<RetailMapPoly>();
            Action<int, double, double, double, double> Rect = (k, x0, z0, x1, z1) =>
                l.Add(new RetailMapPoly { kind = k, poly = new List<V2> { new V2(x0, z0), new V2(x1, z0), new V2(x1, z1), new V2(x0, z1) } });
            Action<int, V2, V2, double> Bar = (k, a0, b0, hw) =>
            {
                V2 f = (b0 - a0).Norm, s0 = f.Left * hw;
                l.Add(new RetailMapPoly { kind = k, poly = new List<V2> { a0 - s0, b0 - s0, b0 + s0, a0 + s0 } });
            };
            Action<int, V2, double, double> Ring = (k, c, r0, r1) =>
            {
                for (int i = 0; i < 48; i++)
                {
                    double a0 = i * Math.PI * 2 / 48, a1 = (i + 1) * Math.PI * 2 / 48;
                    V2 d0 = new V2(Math.Cos(a0), Math.Sin(a0)), d1 = new V2(Math.Cos(a1), Math.Sin(a1));
                    l.Add(new RetailMapPoly { kind = k, poly = new List<V2> { c + d0 * r0, c + d1 * r0, c + d1 * r1, c + d0 * r1 } });
                }
            };
            // paths first (the lake and the rest go over them)
            foreach (double[] p in kmPaths) Bar(15, new V2(p[0], p[1]), new V2(p[2], p[3]), p[4] + 0.6);
            Rect(10, CourtX0, CourtZ0, CourtX1, PalZ0);                 // the forecourt
            Rect(10, 150, 1676, 430, 1798);                             // the parade ground
            Rect(13, 552.2, 1176, 567.8, 1620);                         // the Parade
            Ring(13, MemC, 36.2, 49.8);
            Ring(14, MemC, 0, 15);                                      // the Memorial
            l.Add(new RetailMapPoly { kind = 12, poly = KingsmereLakeOutline() });
            Ring(12, lakeC[0], 0, lakeHW[0]); Ring(12, lakeC[lakeC.Count - 1], 0, lakeHW[lakeHW.Count - 1]);     // its rounded ends
            foreach (double bx in KmBridges)
            {
                double zc, hw; LakeAcross(bx, out zc, out hw);
                Rect(14, bx - BridgeHW, zc - hw - 3.5, bx + BridgeHW, zc + hw + 3.5);
            }
            Ring(16, new V2(298, 1401), 0, 8);                          // the island (park green)
            Rect(11, PalX0, PalZ0, PalX1, PalZ1);                       // the palace
            Rect(11, 150, 1798, 430, 1878);                             // the barracks
            Rect(11, 528, 1212, 592, 1228);                             // the Arch
            return l;
        }

        /// For the map picture: the lake (as a polygon) and the forecourt, parade ground and Parade surface.
        public static List<V2> KingsmereLakeOutline()
        {
            List<V2> L = new List<V2>(), Rt = new List<V2>();
            if (lakeC.Count < 2) PlanKingsmere();
            for (int i = 0; i < lakeC.Count; i++)
            {
                V2 d = i == 0 ? (lakeC[1] - lakeC[0]).Norm : i == lakeC.Count - 1 ? (lakeC[i] - lakeC[i - 1]).Norm : (lakeC[i + 1] - lakeC[i - 1]).Norm;
                L.Add(lakeC[i] + d.Left * lakeHW[i]); Rt.Add(lakeC[i] - d.Left * lakeHW[i]);
            }
            Rt.Reverse(); L.AddRange(Rt);
            return L;
        }
    }
}
