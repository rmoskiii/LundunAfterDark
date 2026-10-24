// E4: Ferndale Retail Park, in Brixtun's empty south-east. Pure C# (runs inside BuildExpansion, after everything else,
// with its own random numbers, so nothing in Brixtun moves).
// The roads (Ferndale Way, the roundabout, Windmill Row, Stockyard Lane, Brixtun Hill's extension) are ordinary
// expansion roads from LundunExpansion.json; this file builds what sits between them:
//   stores     Basketful and Grocera (supermarkets), ValuStack (budget) and FLATPAK Home Store (a huge box, charcoal and
//              orange): clad walls with a band in the shop's colours, a glazed entrance under a canopy, rooftop plant,
//              dock doors on the yard side (not enterable yet)
//   car parks  level with the pavements, with marked bays, kerbed islands with trees and tall LED lamp columns, a lane
//              along each shop front and a walkway to the doors; dropped kerbs where you drive in
//   yards      service yards behind or beside the stores, each with a way through to the car park (good for chases);
//              FLATPAK's has a click-and-collect canopy
//   wasteland  round the edges: the ground, earth mounds and ramps, and the shell of a half-demolished warehouse
//              (DressRetailPark adds the fences with gaps, containers, skips, burnt-out cars and the overgrowth)
// RetailParkSlots: the car parks' parked cars (mostly near the doors, emptier further out; one in five left for
// LundunPopulation's stealable cars). RetailMap: what the map picture draws.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        // ------------------------------------------------------------------ what's what
        static readonly HashSet<string> RetailRoadIds = new HashSet<string> { "XR15", "XR16", "XR17", "XR18", "XR19", "XR20", "XR21", "XR22", "XR23" };
        static bool IsRetailRoad(RoadInfo ri) { return ri != null && ri.r != null && RetailRoadIds.Contains(ri.r.id); }
        /// A roundabout's island (grass; its trees come from the retail park).
        static bool IsIsland(LwPoly park) { return ExpansionMode && park.id != null && park.id.StartsWith("P_ISLAND"); }

        public const string RetailParkName = "Ferndale Retail Park";
        static readonly V2 Roundabout = new V2(1200, -480);

        class Store
        {
            public string id, name, sign;
            public double x0, x1, z0, z1, h, door;     // footprint, height, the entrance's position along the front
            public int front;                          // which face the entrance is on: +1 = north (z1), −1 = south (z0)
            public int wall, band, accent;             // kit swatches
            public bool yardWest, yardEast;            // dock doors on that side
            public bool enterable;                     // E5: a doorway you can walk through (the interior comes from LundunShopInteriors)
            public V2 Door { get { return new V2(door, front > 0 ? z1 : z0); } }
            public V2 Out { get { return new V2(0, front); } }
        }

        static readonly Store[] Stores =
        {
            new Store { id = "FLATPAK", name = "FLATPAK Home Store", sign = "sign_FLATPAK", x0 = 1380, x1 = 1545, z0 = -325, z1 = -215, h = 16, door = 1420, front = -1,
                        wall = KitArt.S_DarkGrey, band = KitArt.S_Orange, accent = KitArt.S_Orange, yardWest = true },
            new Store { id = "BASKETFUL", name = "Basketful", sign = "sign_BASKETFUL", x0 = 1060, x1 = 1185, z0 = -675, z1 = -590, h = 12, door = 1110, front = 1,
                        wall = KitArt.S_LightGrey, band = KitArt.S_Green, accent = KitArt.S_Orange, yardWest = true, enterable = true },
            new Store { id = "GROCERA", name = "Grocera", sign = "sign_GROCERA", x0 = 1215, x1 = 1330, z0 = -676, z1 = -600, h = 11, door = 1272, front = 1,
                        wall = KitArt.S_Cream, band = KitArt.S_Maroon, accent = KitArt.S_Teal, yardEast = true, enterable = true },
            new Store { id = "VALUSTACK", name = "ValuStack", sign = "sign_VALUSTACK", x0 = 1440, x1 = 1545, z0 = -676, z1 = -600, h = 9, door = 1492, front = 1,
                        wall = KitArt.S_LightGrey, band = KitArt.S_Yellow, accent = KitArt.S_Red, yardWest = true, enterable = true },
        };

        /// A car park: its outline, the side the store is on (zStore, away = which way you walk out from the store),
        /// cross aisles (x), the door's walkway (x) and side entrances (which side, wanted z).
        class CarPark
        {
            public string store;
            public List<V2> poly;
            public double zStore, x0, x1; public int away;
            public double[] cross; public double walk;
            public List<double> aisles = new List<double>();   // centre z of each aisle (filled in)
        }

        static readonly CarPark[] CarParks =
        {
            new CarPark { store = "FLATPAK", poly = Pts4(new[] { 1208, -335, 1552, -335, 1552, -469, 1245, -469, 1208, -432 }), zStore = -335, away = -1, x0 = 1210, x1 = 1550, cross = new[] { 1300.0, 1480.0 }, walk = 1420 },
            new CarPark { store = "BASKETFUL", poly = Pts4(new[] { 993, -584, 1192, -584, 1192, -530, 1150, -491, 993, -491 }), zStore = -584, away = 1, x0 = 995, x1 = 1190, cross = new[] { 1060.0 }, walk = 1110 },
            new CarPark { store = "SE", poly = Pts4(new[] { 1208, -594, 1552, -594, 1552, -491, 1250, -491, 1208, -530 }), zStore = -594, away = 1, x0 = 1210, x1 = 1550, cross = new[] { 1330.0, 1490.0 }, walk = 1272 },
        };

        /// Service yards (asphalt, level with the car parks) and the paved aprons in front of the stores.
        static readonly List<V2>[] Yards =
        {
            Pts4(new[] { 1208, -335, 1376, -335, 1376, -215, 1222, -215, 1208, -229 }),        // FLATPAK: loading docks and click-and-collect
            Pts4(new[] { 993, -584, 1055, -584, 1055, -682, 1003, -682, 993, -672 }),          // Basketful
            Pts4(new[] { 1335, -682, 1435, -682, 1435, -594, 1335, -594 }),                    // between Grocera and ValuStack
        };
        static readonly List<V2>[] Aprons =
        {
            Pts4(new[] { 1376, -335, 1552, -335, 1552, -325, 1376, -325 }),
            Pts4(new[] { 1055, -590, 1192, -590, 1192, -584, 1055, -584 }),
            Pts4(new[] { 1208, -600, 1335, -600, 1335, -594, 1208, -594 }),
            Pts4(new[] { 1435, -600, 1552, -600, 1552, -594, 1435, -594 }),
        };

        /// Dropped kerbs: where you drive off the road into a car park or yard. x, z on the kerb line, into-the-lot x, z, width.
        static readonly double[][] RetailEntrances =
        {
            new double[] { 1300, -473, 0, 1, 9 }, new double[] { 1480, -473, 0, 1, 9 }, new double[] { 1555, -400, -1, 0, 8 }, new double[] { 1205, -272, 1, 0, 11 },
            new double[] { 1060, -487, 0, -1, 9 }, new double[] { 1195, -545, -1, 0, 8 }, new double[] { 1025, -685, 0, 1, 11 },
            new double[] { 1330, -487, 0, -1, 9 }, new double[] { 1490, -487, 0, -1, 9 }, new double[] { 1555, -545, -1, 0, 8 }, new double[] { 1205, -560, 1, 0, 8 },
            new double[] { 1385, -685, 0, 1, 11 },
        };

        /// The wasteland round the park (outlines, for the dressing and the map).
        static readonly List<V2>[] Wastes =
        {
            Pts4(new[] { 1075, 6, 1700, 6, 1700, -192, 1075, -192 }),          // north, up to the Ring
            Pts4(new[] { 1571, -212, 1704, -212, 1704, -690, 1571, -690 }),    // east
            Pts4(new[] { 996, -702, 1704, -702, 1704, -935, 996, -935 }),      // south
            Pts4(new[] { 1072, -214, 1190, -214, 1190, -432, 1152, -466, 1072, -466 }),    // west, behind Brixtun's back gardens (clear of the roundabout)
        };

        static List<V2> Pts4(int[] xz) { List<V2> l = new List<V2>(); for (int i = 0; i + 1 < xz.Length; i += 2) l.Add(new V2(xz[i], xz[i + 1])); return l; }

        // ------------------------------------------------------------------ results
        public class RetailMapPoly { public List<V2> poly; public int kind; }     // kind 0 = paved, 1 = building, 2 = waste
        static readonly List<RetailMapPoly> retailMap = new List<RetailMapPoly>();
        static readonly List<ParkSlot> retailSlots = new List<ParkSlot>();
        static readonly List<double[]> retailIslands = new List<double[]>();       // x, z, along x, along z, kind (0 lamp, 1 tree)
        static readonly List<double[]> retailRamps = new List<double[]>();         // x, z, radius (kept clear by the dressing)
        static readonly List<double[]> ruinRect = new List<double[]>();            // x0, x1, z0, z1 of the ruined warehouses

        /// What the map picture draws for the retail park (after BuildExpansion).
        public static List<RetailMapPoly> RetailMap { get { return retailMap; } }
        /// The car parks' parked cars (after BuildExpansion).
        public static List<ParkSlot> RetailParkSlots() { return new List<ParkSlot>(retailSlots); }

        // ================================================================== building it
        static MeshData rpWall, rpLot, rpPave, rpMark, rpRuin, rpRamp, rpPoles, rpHeads, rpPools, rpTrunks, rpLeaves;

        static void BuildRetailPark(GreyboxResult R)
        {
            retailMap.Clear(); retailSlots.Clear(); retailIslands.Clear(); retailRamps.Clear(); ruinRect.Clear(); lampsOut.Clear();
            bool any = false;
            foreach (RoadInfo ri in roads.Values) if (IsRetailRoad(ri)) any = true;
            if (!any) return;
            Random rr = new Random(8080);
            double yLot = W.heights.pavementTop, yRoad = W.heights.roadSurface;
            rpWall = new MeshData("RP_Stores", "RetailPark", KitMatB, true) { navArea = 1 };
            rpLot = new MeshData("RP_CarParks", "RetailPark", "Road", true) { navArea = RoadArea };
            rpPave = new MeshData("RP_Paving", "RetailPark", KitMatB, true) { navArea = PavementArea };
            rpMark = new MeshData("RP_Markings", "RetailPark", "Marking", false) { navIgnore = true };
            rpRuin = new MeshData("RP_Ruins", "RetailPark", KitMatA, true) { navArea = 1 };
            rpRamp = new MeshData("RP_Ramps", "RetailPark", KitMatB, true);
            rpPoles = new MeshData("RP_LampPoles", "Lamps", LampPoleMats[3], false) { navIgnore = true };
            rpHeads = new MeshData("RP_LampHeads", "Lamps", LampHeadMats[3], false) { navIgnore = true };
            rpPools = new MeshData("RP_LightPools", "Lamps", LampPoolMats[3], false) { navIgnore = true };
            rpTrunks = new MeshData("RP_TreeTrunks", "RetailPark", "Trunk", true) { navIgnore = true };
            rpLeaves = new MeshData("RP_TreeCanopies", "RetailPark", "Leaves", false) { navIgnore = true };

            // 1. the ground: car parks, yards, aprons
            foreach (CarPark cp in CarParks) { LotSurface(rpLot, cp.poly, yLot); retailMap.Add(new RetailMapPoly { poly = cp.poly, kind = 0 }); }
            foreach (List<V2> y in Yards) { LotSurface(rpLot, y, yLot); retailMap.Add(new RetailMapPoly { poly = y, kind = 0 }); }
            foreach (List<V2> a in Aprons) { PavedSurface(rpPave, a, yLot); retailMap.Add(new RetailMapPoly { poly = a, kind = 0 }); }
            foreach (double[] e in RetailEntrances) DroppedKerb(new V2(e[0], e[1]), new V2(e[2], e[3]), e[4], yRoad, yLot);

            // 2. the stores
            foreach (Store s in Stores)
            {
                StoreBox(s, rr);
                retailMap.Add(new RetailMapPoly { poly = Pts4(new[] { (int)s.x0, (int)s.z0, (int)s.x1, (int)s.z0, (int)s.x1, (int)s.z1, (int)s.x0, (int)s.z1 }), kind = 1 });
            }
            ClickAndCollect();

            // 3. the car parks' bays, islands, lamps, trees and parked cars
            Random cars = new Random(8181);
            foreach (CarPark cp in CarParks) Bays(cp, rr, cars, yLot);

            // 4. the roundabout's island: a few trees
            foreach (LwPoly park in W.parks)
                if (IsIsland(park))
                    for (int i = 0; i < 5; i++)
                    {
                        double a = i * Math.PI * 2 / 5 + 0.4;
                        RpTree(Roundabout + new V2(Math.Cos(a), Math.Sin(a)) * 10.5, 0.012, 6 + rr.NextDouble() * 3, 2.2 + rr.NextDouble());
                    }

            // 5. wasteland: mounds and ramps to jump, the half-demolished warehouses
            foreach (List<V2> w in Wastes) retailMap.Add(new RetailMapPoly { poly = w, kind = 2 });
            double[][] ramps =
            {
                // x, z, heading (degrees, 0 = +z), length, width, height, kind (0 = ramp with a drop, 1 = mound)
                new double[] { 1250, -150, 90, 9, 5, 1.6, 0 }, new double[] { 1310, -40, 0, 14, 7, 1.2, 1 }, new double[] { 1610, -110, 0, 9, 5, 1.8, 0 },
                new double[] { 1180, -60, 90, 16, 8, 1.0, 1 }, new double[] { 1630, -470, 180, 9, 5, 1.6, 0 }, new double[] { 1640, -620, 0, 14, 7, 1.3, 1 },
                new double[] { 1080, -780, 90, 9, 5, 1.7, 0 }, new double[] { 1300, -760, 0, 16, 8, 1.1, 1 }, new double[] { 1500, -850, 270, 9, 5, 1.9, 0 },
                new double[] { 1660, -880, 0, 14, 7, 1.2, 1 }, new double[] { 1130, -330, 0, 9, 5, 1.5, 0 },
            };
            foreach (double[] rp in ramps)
            {
                double a = rp[2] * Math.PI / 180;
                Ramp(new V2(rp[0], rp[1]), new V2(Math.Sin(a), Math.Cos(a)), rp[3], rp[4], rp[5], rp[6] > 0.5);
                retailRamps.Add(new[] { rp[0], rp[1], Math.Max(rp[3], rp[4]) / 2 + 3 });
            }
            RuinedWarehouse(1380, 1470, -110, -58, rr);
            RuinedWarehouse(1130, 1172, -830, -800, rr);

            foreach (MeshData m in new[] { rpWall, rpLot, rpPave, rpMark, rpRuin, rpRamp, rpPoles, rpHeads, rpPools, rpTrunks, rpLeaves }) R.meshes.Add(m);
            R.lamps.AddRange(lampsOut);                    // the real lamp lights use the columns too (after the street lamps, so theirs don't move)
            R.log.Add(string.Format("{0}: {1} stores, {2} car parks, {3} parked cars ({4} bays kept for real ones), {5} lamp columns, {6} ramps",
                RetailParkName, Stores.Length, CarParks.Length, retailSlots.FindAll(s => !s.forPopulation).Count, retailSlots.FindAll(s => s.forPopulation).Count,
                retailIslands.FindAll(i => i[4] < 0.5).Count, ramps.Length));
        }

        // ------------------------------------------------------------------ surfaces
        static void LotSurface(MeshData m, List<V2> poly, double y)
        {
            List<P3> top = new List<P3>(); foreach (V2 q in poly) top.Add(new P3(q, y));
            m.FanUp(top);
            Kerbs(m, poly, y, -1);
        }

        static void PavedSurface(MeshData m, List<V2> poly, double y)
        {
            double u, v; KitArt.SwatchUV(KitArt.S_PaleConc, out u, out v);
            List<P3> top = new List<P3>(); foreach (V2 q in poly) top.Add(new P3(q, y + 0.004, u, v));
            m.FanUp(top);
            Kerbs(m, poly, y, KitArt.S_SootStone);
        }

        /// The kerb faces round a raised surface, facing out.
        static void Kerbs(MeshData m, List<V2> poly, double y, int sw)
        {
            double u = 0.5, v = 0.5; if (sw >= 0) KitArt.SwatchUV(sw, out u, out v);
            V2 cen = new V2(0, 0); foreach (V2 q in poly) cen = cen + q; cen = cen * (1.0 / poly.Count);
            for (int i = 0; i < poly.Count; i++)
            {
                V2 a = poly[i], b = poly[(i + 1) % poly.Count];
                V2 o = (b - a).Norm.Left; if (V2.Dot(o, Lerp(a, b, 0.5) - cen) < 0) o = o * -1;
                m.Quad(new P3(a, -0.02, u, v), new P3(b, -0.02, u, v), new P3(b, y, u, v), new P3(a, y, u, v), o.x, 0, o.z);
            }
        }

        /// A wedge in the gutter so you can drive up off the road (k = the kerb line, into = towards the lot).
        static void DroppedKerb(V2 k, V2 into, double width, double yRoad, double yLot)
        {
            V2 l = into.Left; double hw = width / 2;
            V2 a0 = k - into * 1.3 - l * hw, a1 = k - into * 1.3 + l * hw, b0 = k - l * hw, b1 = k + l * hw;
            double rise = yLot - yRoad;
            rpLot.Quad(new P3(a0, yRoad + 0.002), new P3(a1, yRoad + 0.002), new P3(b1, yLot + 0.002), new P3(b0, yLot + 0.002), -into.x * rise / 1.3, 1, -into.z * rise / 1.3);
            foreach (int s in new[] { -1, 1 })
            {
                V2 e0 = k - into * 1.3 + l * (s * hw), e1 = k + l * (s * hw);
                rpLot.Tri(new P3(e0, yRoad), new P3(e1, yRoad), new P3(e1, yLot + 0.002), l.x * s, 0, l.z * s);
            }
        }

        // ------------------------------------------------------------------ a store
        static void StoreBox(Store s, Random rr)
        {
            double y0 = -0.05, h = s.h, band = 3.0;
            V2[] c = { new V2(s.x0, s.z0), new V2(s.x1, s.z0), new V2(s.x1, s.z1), new V2(s.x0, s.z1) };
            V2[] outN = { new V2(0, -1), new V2(1, 0), new V2(0, 1), new V2(-1, 0) };
            for (int i = 0; i < 4; i++)
            {
                V2 a = c[i], b = c[(i + 1) % 4], o = outN[i];
                bool isFront = (o.z > 0 && s.front > 0) || (o.z < 0 && s.front < 0);
                double L = V2.Dist(a, b); V2 f = (b - a).Norm;
                // cladding (E5: with a doorway in an enterable store's front), the brand band at the top, a white line under it
                bool open = isFront && s.enterable;
                double tD = V2.Dot(s.Door - a, f);
                foreach (double[] sp in open ? new[] { new[] { 0, tD - DoorHalf, y0 }, new[] { tD + DoorHalf, L, y0 }, new[] { tD - DoorHalf, tD + DoorHalf, DoorTop } } : new[] { new[] { 0, L, y0 } })
                    SwQuad(rpWall, s.wall, new P3(a + f * sp[0], sp[2], 0, 0), new P3(a + f * sp[1], sp[2], 0, 0), new P3(a + f * sp[1], h - band - 0.25, 0, 0), new P3(a + f * sp[0], h - band - 0.25, 0, 0), o.x, 0, o.z);
                SwQuad(rpWall, KitArt.S_White, new P3(a, h - band - 0.25, 0, 0), new P3(b, h - band - 0.25, 0, 0), new P3(b, h - band, 0, 0), new P3(a, h - band, 0, 0), o.x, 0, o.z);
                SwQuad(rpWall, s.band, new P3(a, h - band, 0, 0), new P3(b, h - band, 0, 0), new P3(b, h, 0, 0), new P3(a, h, 0, 0), o.x, 0, o.z);
                // ribs every 6 m, and a dark plinth
                for (double t = 3; t < L - 1; t += 6) { if (open && Math.Abs(t - tD) < DoorHalf + 0.5) continue; RBox(rpWall, KitArt.S_MidGrey, a + f * t + o * 0.06, o, 0.06, 0.08, 0, h - band - 0.25, false); }
                foreach (double[] sp in open ? new[] { new[] { 0, tD - DoorHalf }, new[] { tD + DoorHalf, L } } : new[] { new[] { 0, L } })
                {
                    V2 pa = a + f * sp[0], pb = a + f * sp[1];
                    SwQuad(rpWall, KitArt.S_Concrete, new P3(pa + o * 0.12, y0, 0, 0), new P3(pb + o * 0.12, y0, 0, 0), new P3(pb + o * 0.12, 0.9, 0, 0), new P3(pa + o * 0.12, 0.9, 0, 0), o.x, 0, o.z);
                    SwQuad(rpWall, KitArt.S_Concrete, new P3(pa + o * 0.12, 0.9, 0, 0), new P3(pb + o * 0.12, 0.9, 0, 0), new P3(pb, 0.9, 0, 0), new P3(pa, 0.9, 0, 0), 0, 1, 0);
                }
                if (isFront) StoreFront(s, o);
                bool dockSide = (o.x < 0 && s.yardWest) || (o.x > 0 && s.yardEast);
                if (dockSide) DockDoors(a, b, o, s.id == "FLATPAK" ? 6 : 3);
            }
            // roof, parapet, rooftop plant
            double u, v; KitArt.SwatchUV(KitArt.S_Bitumen, out u, out v);
            rpWall.FanUp(new List<P3> { new P3(c[0], h - 0.4, u, v), new P3(c[1], h - 0.4, u, v), new P3(c[2], h - 0.4, u, v), new P3(c[3], h - 0.4, u, v) });
            for (int i = 0; i < 4; i++)
            {
                V2 a = c[i], b = c[(i + 1) % 4];
                RBox(rpWall, s.band, Lerp(a, b, 0.5) - outN[i] * 0.15, b - a, V2.Dist(a, b) / 2, 0.15, h - 0.4, h + 0.3, false);
            }
            int units = 2 + (int)((s.x1 - s.x0) / 30);
            for (int i = 0; i < units; i++)
            {
                V2 q = new V2(s.x0 + 8 + rr.NextDouble() * (s.x1 - s.x0 - 16), s.z0 + 8 + rr.NextDouble() * (s.z1 - s.z0 - 16));
                RBox(rpWall, KitArt.S_Galv, q, new V2(1, 0), 2 + rr.NextDouble() * 2, 1.2 + rr.NextDouble(), h - 0.4, h + 1.2 + rr.NextDouble(), false);
            }
        }

        /// The glazed entrance under a canopy, with its door frames.
        static void StoreFront(Store s, V2 o)
        {
            V2 door = s.Door, f = o.Left;
            double hw = s.id == "FLATPAK" ? 10 : 7, top = s.id == "FLATPAK" ? 6.0 : 4.4;
            // glass, set a little proud of the wall (E5: an enterable store's has the doorway in it; the sliding doors are separate)
            if (s.enterable)
            {
                foreach (double[] sp in new[] { new[] { -hw, -DoorHalf, 0.15 }, new[] { DoorHalf, hw, 0.15 }, new[] { -DoorHalf, DoorHalf, DoorTop + 0.15 } })
                    SwQuad(rpWall, KitArt.S_Glass, new P3(door + f * sp[0] + o * 0.05, sp[2], 0, 0), new P3(door + f * sp[1] + o * 0.05, sp[2], 0, 0),
                           new P3(door + f * sp[1] + o * 0.05, top, 0, 0), new P3(door + f * sp[0] + o * 0.05, top, 0, 0), o.x, 0, o.z);
                RBox(rpWall, s.accent, door + o * 0.1, f, DoorHalf + 0.06, 0.1, DoorTop, DoorTop + 0.15, true);       // the door head
            }
            else
                SwQuad(rpWall, KitArt.S_Glass, new P3(door - f * hw + o * 0.05, 0.15, 0, 0), new P3(door + f * hw + o * 0.05, 0.15, 0, 0),
                       new P3(door + f * hw + o * 0.05, top, 0, 0), new P3(door - f * hw + o * 0.05, top, 0, 0), o.x, 0, o.z);
            for (double t = -hw; t <= hw + 0.01; t += 1.5)
            {
                if (s.enterable && Math.Abs(t) < DoorHalf - 0.01) continue;
                RBox(rpWall, Math.Abs(t) < 2.3 ? s.accent : KitArt.S_DarkGrey, door + f * t + o * 0.1, o, 0.06, 0.05, 0.15, top, false);
            }
            if (s.enterable) foreach (int k in new[] { -1, 1 }) RBox(rpWall, s.accent, door + f * (k * DoorHalf) + o * 0.1, o, 0.08, 0.08, 0.15, DoorTop, false);
            RBox(rpWall, KitArt.S_DarkGrey, door + o * 0.1, f, hw, 0.08, top - 0.12, top + 0.05, true);
            if (!s.enterable) RBox(rpWall, s.accent, door + o * 0.12, f, 2.4, 0.06, 2.6, 2.9, true);           // over the doors
            // the canopy, on two columns
            RBox(rpWall, KitArt.S_LightGrey, door + o * 1.8, f, hw + 1.0, 1.8, top + 0.2, top + 0.65, true);
            RBox(rpWall, s.accent, door + o * 3.62, f, hw + 1.0, 0.04, top + 0.2, top + 0.65, false);
            foreach (int k in new[] { -1, 1 }) RBox(rpWall, KitArt.S_DarkGrey, door + o * 3.3 + f * (k * (hw - 0.5)), o, 0.15, 0.15, 0.15, top + 0.2, false);
        }

        /// E5: an enterable store's doorway (half-width, height).
        public const double DoorHalf = 2.2, DoorTop = 3.2;

        /// Roller-shutter dock doors along a yard wall, with bumpers and a canopy strip.
        static void DockDoors(V2 a, V2 b, V2 o, int n)
        {
            double L = V2.Dist(a, b); V2 f = (b - a).Norm;
            for (int i = 0; i < n; i++)
            {
                double t = L * (i + 0.5) / n;
                V2 c = a + f * t;
                SwQuad(rpWall, KitArt.S_Galv, new P3(c - f * 1.6 + o * 0.04, 1.2, 0, 0), new P3(c + f * 1.6 + o * 0.04, 1.2, 0, 0), new P3(c + f * 1.6 + o * 0.04, 4.6, 0, 0), new P3(c - f * 1.6 + o * 0.04, 4.6, 0, 0), o.x, 0, o.z);
                for (double y = 1.4; y < 4.5; y += 0.3) RBox(rpWall, KitArt.S_MidGrey, c + o * 0.06, f, 1.55, 0.015, y, y + 0.04, false);
                foreach (int k in new[] { -1, 1 })
                {
                    RBox(rpWall, KitArt.S_Black, c + f * (k * 1.25) + o * 0.2, o, 0.2, 0.15, 0.8, 1.3, true);
                    RBox(rpWall, KitArt.S_Yellow, c + f * (k * 1.9) + o * 0.1, o, 0.1, 0.08, 0.15, 1.2, false);
                }
                RBox(rpWall, KitArt.S_DarkGrey, c + o * 0.6, f, 1.9, 0.6, 4.9, 5.05, true);
            }
        }

        /// FLATPAK's click-and-collect: a canopy over four bays in its yard (the sign comes from the dressing).
        static void ClickAndCollect()
        {
            double x0 = 1318, x1 = 1374, z0 = -330, z1 = -312;
            for (double x = x0 + 1; x <= x1 - 1 + 0.01; x += (x1 - x0 - 2) / 2)
                foreach (double z in new[] { z0 + 0.6, z1 - 0.6 }) RBox(rpWall, KitArt.S_DarkGrey, new V2(x, z), new V2(1, 0), 0.15, 0.15, 0.15, 5.2, false);
            RBox(rpWall, KitArt.S_LightGrey, new V2((x0 + x1) / 2, (z0 + z1) / 2), new V2(1, 0), (x1 - x0) / 2 + 0.5, (z1 - z0) / 2 + 0.5, 5.2, 5.6, true);
            RBox(rpWall, KitArt.S_Orange, new V2((x0 + x1) / 2, z0 - 0.5), new V2(1, 0), (x1 - x0) / 2 + 0.5, 0.05, 5.0, 5.7, false);
            RBox(rpWall, KitArt.S_DarkGrey, new V2((x0 + x1) / 2, z0 - 0.45), new V2(1, 0), 5.8, 0.05, 5.6, 7.1, false);        // the sign board on top
            for (int i = 0; i <= 4; i++) BayLine(new V2(x0 + 2 + i * (x1 - x0 - 4) / 4.0, (z0 + z1) / 2), new V2(0, 1), 5.0, 0.15);
        }

        // ------------------------------------------------------------------ car park bays
        static void Bays(CarPark cp, Random rr, Random cars, double y)
        {
            Store st = null; foreach (Store s in Stores) if (s.id == cp.store) st = s;
            V2 doorPt = st != null ? st.Door : new V2(cp.walk, cp.zStore);
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (V2 q in cp.poly) { minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
            double depth = maxZ - minZ;
            // rows: a lane along the shop front, then double rows of bays with an aisle after each
            double d = 7.5;
            cp.aisles.Clear();
            cp.aisles.Add(cp.zStore + cp.away * 3.75);
            List<double[]> rows = new List<double[]>();          // z of the row's front line, direction the bays go (±1 along away)
            while (d + 10 + 7 <= depth + 0.01)
            {
                rows.Add(new[] { cp.zStore + cp.away * d, cp.away });
                rows.Add(new[] { cp.zStore + cp.away * (d + 10), -cp.away });
                cp.aisles.Add(cp.zStore + cp.away * (d + 10 + 3.25));
                d += 16.5;
            }
            int bayIndex = 0;
            for (int ri = 0; ri < rows.Count; ri += 2)
            {
                double zA = rows[ri][0], zMid = zA + cp.away * 5;
                int k = 0;
                for (double x = cp.x0 + 2; x + 2.5 <= cp.x1 - 2 + 0.01; x += 2.5, k++)
                {
                    double xc = x + 1.25;
                    bool cross = false;
                    foreach (double cx in cp.cross) if (Math.Abs(xc - cx) < 4.5) cross = true;
                    if (Math.Abs(xc - cp.walk) < 2.6) cross = true;
                    if (cross) { k = -1; continue; }
                    bool inside = InPoly(new V2(xc - 1.5, zA), cp.poly) && InPoly(new V2(xc + 1.5, zA), cp.poly) &&
                                  InPoly(new V2(xc - 1.5, zA + cp.away * 10), cp.poly) && InPoly(new V2(xc + 1.5, zA + cp.away * 10), cp.poly);
                    if (!inside) { k = -1; continue; }
                    if (k % 11 == 10)
                    {
                        // an island across both rows: a lamp column, or a tree
                        bool lamp = (k / 11) % 2 == 0;
                        V2 ic = new V2(xc, zMid);
                        Island(ic, new V2(0, 1), 9.6, 2.2, y, lamp, rr);
                        retailIslands.Add(new[] { ic.x, ic.z, 0.0, 1.0, lamp ? 0.0 : 1.0 });
                        continue;
                    }
                    foreach (int side in new[] { 0, 1 })
                    {
                        double z0 = side == 0 ? zA : zMid, dir = side == 0 ? cp.away : -cp.away;     // from the aisle into the bay
                        double zc = z0 + dir * 2.5;
                        BayLine(new V2(x, zc), new V2(0, 1), 4.9, y);
                        if (k == 0 || x + 5 > cp.x1 - 2) BayLine(new V2(x + 2.5, zc), new V2(0, 1), 4.9, y);
                        // a parked car? busier near the doors, emptier further out
                        double dist = V2.Dist(new V2(xc, zc), doorPt);
                        double p = 0.04 + 0.45 * Math.Exp(-dist / 45.0);
                        double roll = cars.NextDouble();
                        bool pop = (bayIndex++ % 5) == 2 && roll < p + 0.2;
                        if (roll < p || pop)
                        {
                            string model = ParkModels[Pick(cars, new double[] { 30, 30, 20, 6, 8, 4, 2 })];
                            double face = cars.NextDouble() < 0.3 ? -dir : dir;                         // nose in, or reversed in
                            double yaw = face > 0 ? 0 : 180;
                            retailSlots.Add(new ParkSlot { x = xc, z = zc, yaw = yaw, length = ModelLength(model), forPopulation = pop && roll >= p, model = model, district = "D_RETAIL",
                                                           colour = Pick(cars, model == "van" || model == "delivery" ? VanColourWeights : CarColourWeights) });
                        }
                    }
                }
                // the line along the back of the double row
                BayLine(new V2((cp.x0 + cp.x1) / 2, zMid), new V2(1, 0), cp.x1 - cp.x0 - 4, y);
            }
            // the walkway to the doors: zebra stripes across each aisle
            foreach (double az in cp.aisles)
                for (double t = -2.1; t <= 2.11; t += 0.7)
                    rpMark.Quad(new P3(cp.walk + t - 0.22, y + 0.012, az - 3.0, 0, 0), new P3(cp.walk + t + 0.22, y + 0.012, az - 3.0, 0, 0),
                                new P3(cp.walk + t + 0.22, y + 0.012, az + 3.0, 0, 0), new P3(cp.walk + t - 0.22, y + 0.012, az + 3.0, 0, 0), 0, 1, 0);
        }

        static void BayLine(V2 c, V2 f, double len, double y)
        {
            V2 l = f.Left;
            rpMark.Quad(new P3(c - f * (len / 2) - l * 0.05, y + 0.01), new P3(c + f * (len / 2) - l * 0.05, y + 0.01),
                        new P3(c + f * (len / 2) + l * 0.05, y + 0.01), new P3(c - f * (len / 2) + l * 0.05, y + 0.01), 0, 1, 0);
        }

        /// A kerbed planting strip across a double row, with a tall LED column (two heads) or a tree.
        static void Island(V2 c, V2 f, double len, double wid, double y, bool lamp, Random rr)
        {
            RBox(rpPave, KitArt.S_Concrete, c, f, len / 2, wid / 2, y - 0.02, y + 0.15, false);
            double u, v; KitArt.SwatchUV(KitArt.S_Green, out u, out v);
            V2 l = f.Left;
            rpPave.Quad(new P3(c - f * (len / 2 - 0.2) - l * (wid / 2 - 0.2), y + 0.17, u, v), new P3(c + f * (len / 2 - 0.2) - l * (wid / 2 - 0.2), y + 0.17, u, v),
                        new P3(c + f * (len / 2 - 0.2) + l * (wid / 2 - 0.2), y + 0.17, u, v), new P3(c - f * (len / 2 - 0.2) + l * (wid / 2 - 0.2), y + 0.17, u, v), 0, 1, 0);
            if (lamp) LedColumn(c, f, y + 0.15);
            else RpTree(c, y + 0.15, 6 + rr.NextDouble() * 2.5, 1.8 + rr.NextDouble() * 0.8);
        }

        /// A 10 m steel column with two LED heads (and their light pools); joins the lamp list, so the real lights use it.
        static void LedColumn(V2 c, V2 f, double y)
        {
            rpPoles.Box(c, f, 0.09, 0.09, y, y + 10, false, 0.1, 0, false);
            foreach (int s in new[] { -1, 1 })
            {
                V2 arm = c + f * (s * 0.7);
                rpPoles.Box(c + f * (s * 0.35), f, 0.35, 0.04, y + 9.8, y + 9.86, true, 0.1, 0, false);
                rpHeads.Box(arm + f * (s * 0.25), f, 0.45, 0.16, y + 9.7, y + 9.8, true, 0.1, 0, false);
                V2 pc = c + f * (s * 4.5), l = f.Left; double pr = PoolRadius * 1.1;
                rpPools.Quad(new P3(pc - f * pr - l * pr, y + 0.03, 0, 0), new P3(pc + f * pr - l * pr, y + 0.03, 1, 0),
                             new P3(pc + f * pr + l * pr, y + 0.03, 1, 1), new P3(pc - f * pr + l * pr, y + 0.03, 0, 1), 0, 1, 0);
            }
            lampsOut.Add(new LampInfo { x = c.x, y = y + 9.6, z = c.z, style = 3 });
        }
        static readonly List<LampInfo> lampsOut = new List<LampInfo>();

        static void RpTree(V2 q, double baseY, double h, double r)
        {
            V2 d = new V2(0.6, 0.8);
            rpTrunks.Box(q, d, 0.18, 0.18, baseY, baseY + h * 0.5, false, 0.1, 0, false);
            rpLeaves.Box(q, d, r, r, baseY + h * 0.4, baseY + h, true, 0.1, 0, false);
        }

        // ------------------------------------------------------------------ chase spaces
        /// A dirt ramp (a slope up and a drop) or a mound (up and down), c = middle, f = the way you drive over it.
        static void Ramp(V2 c, V2 f, double len, double wid, double h, bool mound)
        {
            f = f.Norm; V2 l = f.Left;
            double u, v; KitArt.SwatchUV(KitArt.S_BrownBrick, out u, out v);
            double hl = len / 2, hw = wid / 2;
            List<double[]> prof = mound ? new List<double[]> { new[] { -hl, 0.0 }, new[] { -hl * 0.3, h }, new[] { hl * 0.3, h }, new[] { hl, 0.0 } }
                                        : new List<double[]> { new[] { -hl, 0.0 }, new[] { hl, h }, new[] { hl, 0.0 } };
            for (int i = 0; i + 1 < prof.Count; i++)
            {
                double a0 = prof[i][0], y0 = prof[i][1], a1 = prof[i + 1][0], y1 = prof[i + 1][1];
                double ny = a1 - a0, nf = -(y1 - y0); double nl = Math.Sqrt(ny * ny + nf * nf) + 1e-9;
                rpRamp.Quad(new P3(c + f * a0 - l * hw, y0, u, v), new P3(c + f * a1 - l * hw, y1, u, v), new P3(c + f * a1 + l * hw, y1, u, v), new P3(c + f * a0 + l * hw, y0, u, v),
                            f.x * nf / nl, ny / nl, f.z * nf / nl);
            }
            foreach (int s in new[] { -1, 1 })
            {
                List<P3> side = new List<P3>();
                foreach (double[] p in prof) side.Add(new P3(c + f * p[0] + l * (s * hw), p[1], u, v));
                for (int i = 1; i + 1 < side.Count; i++) rpRamp.Tri(side[0], side[i], side[i + 1], l.x * s, 0, l.z * s);
                if (side.Count == 3) { }
            }
        }

        /// The shell of a warehouse half knocked down: brick walls broken off at different heights, gaps you can drive
        /// through, steel columns still standing, a bit of roof left at one end.
        static void RuinedWarehouse(double x0, double x1, double z0, double z1, Random rr)
        {
            ruinRect.Add(new[] { x0, x1, z0, z1 });
            retailMap.Add(new RetailMapPoly { poly = Pts4(new[] { (int)x0, (int)z0, (int)x1, (int)z0, (int)x1, (int)z1, (int)x0, (int)z1 }), kind = 1 });
            V2[] c = { new V2(x0, z0), new V2(x1, z0), new V2(x1, z1), new V2(x0, z1) };
            V2[] outN = { new V2(0, -1), new V2(1, 0), new V2(0, 1), new V2(-1, 0) };
            for (int i = 0; i < 4; i++)
            {
                V2 a = c[i], b = c[(i + 1) % 4], o = outN[i], f = (b - a).Norm;
                double L = V2.Dist(a, b);
                int n = Math.Max(2, (int)Math.Round(L / 4.5));
                for (int k = 0; k < n; k++)
                {
                    double t0 = L * k / n, t1 = L * (k + 1) / n;
                    double r = rr.NextDouble();
                    if (r < 0.2 || (i % 2 == 0 && k == n / 2)) continue;                       // knocked through (one gap each long side, always)
                    double top = r < 0.45 ? 1.0 + rr.NextDouble() * 1.5 : r < 0.75 ? 3 + rr.NextDouble() * 3 : 7.5;
                    V2 p0 = a + f * t0, p1 = a + f * t1;
                    BrickWall(rpRuin, p0, p1, o, -0.05, top);
                    BrickWall(rpRuin, p1 - o * 0.35, p0 - o * 0.35, o * -1, -0.05, top);
                    double u, v; KitArt.SwatchUV(KitArt.S_YelBrick, out u, out v);
                    rpRuin.Quad(new P3(p0, top, u, v), new P3(p1, top, u, v), new P3(p1 - o * 0.35, top, u, v), new P3(p0 - o * 0.35, top, u, v), 0, 1, 0);
                    for (int ei = 0; ei < 2; ei++)
                    {
                        V2 e = ei == 0 ? p0 : p1, ff = ei == 0 ? f * -1 : f;
                        rpRuin.Quad(new P3(e, -0.05, u, v), new P3(e - o * 0.35, -0.05, u, v), new P3(e - o * 0.35, top, u, v), new P3(e, top, u, v), ff.x, 0, ff.z);
                    }
                }
            }
            // steel columns, and the roof's last bay
            for (double x = x0 + 8; x < x1 - 4; x += 8)
                foreach (double z in new[] { z0 + 0.8, z1 - 0.8 })
                    if (rr.NextDouble() < 0.75) RBox(rpRuin, KitArt.S_Rust, new V2(x, z), new V2(1, 0), 0.15, 0.15, 0, 7.6, false);
            double rx = x1 - 10;
            RBox(rpRuin, KitArt.S_Rust, new V2(rx, (z0 + z1) / 2), new V2(0, 1), (z1 - z0) / 2, 0.12, 7.4, 7.8, true);
            double ru, rv; KitArt.SwatchUV(KitArt.S_DarkGrey, out ru, out rv);
            rpRuin.Quad(new P3(rx, 7.85, z0, ru, rv), new P3(x1, 7.85, z0, ru, rv), new P3(x1, 7.85, z1, ru, rv), new P3(rx, 7.85, z1, ru, rv), 0, 1, 0);
            rpRuin.Quad(new P3(rx, 7.8, z0, ru, rv), new P3(x1, 7.8, z0, ru, rv), new P3(x1, 7.8, z1, ru, rv), new P3(rx, 7.8, z1, ru, rv), 0, -1, 0);
        }

        // ================================================================== dressing (run last in an expansion's Dress)
        /// The retail park's colours and signs (expansion atlases only).
        static void RetailAtlas(DressAtlas a)
        {
            string[][] cols = {
                new[] { "rp_lit", "fff1c8" }, new[] { "rp_c1", "b03a2e" }, new[] { "rp_c2", "2e6f9e" }, new[] { "rp_c3", "3c7a3e" }, new[] { "rp_c4", "c8812a" },
                new[] { "rp_skip", "e0b000" }, new[] { "rp_burnt", "241f1c" }, new[] { "rp_rust", "7a4128" }, new[] { "rp_trailer", "e8e8e4" },
                new[] { "rp_dirt", "4a3d2e" }, new[] { "rp_gravel", "5f5c54" }, new[] { "rp_scrub", "4f6a2a" } };
            foreach (string[] c in cols) a.Swatch(c[0], Col.Hex(c[1]));
            a.Sign("sign_BASKETFUL", 640, 80, "BASKETFUL", Col.Hex("1f7a3a"), Col.Hex("ffffff"), Col.Hex("f29f05"), 0.5);
            a.Sign("sign_GROCERA", 640, 80, "GROCERA", Col.Hex("5b2a86"), Col.Hex("ffffff"), Col.Hex("2fb5a8"), 0.5);
            a.Sign("sign_VALUSTACK", 640, 80, "VALUSTACK", Col.Hex("ffd200"), Col.Hex("c8102e"), Col.Hex("c8102e"), 0.5);
            a.Sign("sign_FLATPAK", 640, 80, "FLATPAK", Col.Hex("2a2c2e"), Col.Hex("ff7a1a"), Col.Hex("ff7a1a"), 0.5);
            a.Sign("sign_FERNDALE", 640, 80, "FERNDALE RETAIL PARK", Col.Hex("1b2766"), Col.Hex("ffffff"), Col.Hex("ffffff"), 0.45);
            a.Sign("sign_CLICK", 640, 80, "CLICK & COLLECT", Col.Hex("ff7a1a"), Col.Hex("2a2c2e"), Col.Hex("2a2c2e"), 0.5);
            a.Sign("sign_TROLLEYS", 640, 80, "TROLLEYS", Col.Hex("1b2766"), Col.Hex("ffffff"), new Col(0, 0, 0, 0), 0.5);
        }

        static void DressRetailPark()
        {
            if (retailMap.Count == 0) return;
            Random rd = new Random(8282);
            double y = PaveY;
            // keep the stores, islands, ramps and ruins clear
            foreach (Store s in Stores) for (double x = s.x0; x <= s.x1; x += 6) for (double z = s.z0; z <= s.z1; z += 6) Claim(new V2(x, z), 4.5);
            foreach (double[] i in retailIslands) Claim(new V2(i[0], i[1]), 5.2);
            foreach (double[] r in retailRamps) Claim(new V2(r[0], r[1]), r[2]);
            foreach (double[] rr in ruinRect) for (double x = rr[0]; x <= rr[1]; x += 5) for (double z = rr[2]; z <= rr[3]; z += 5) Claim(new V2(x, z), 4);

            // 1. the stores' signs and their lit entrances
            foreach (Store s in Stores)
            {
                V2 o = s.Out, f = o.Left, door = s.Door;
                double sw = s.id == "FLATPAK" ? 12 : 8, sh = sw / 4;          // half-width, height (the signs are 8:1)
                double sy = s.h - 1.5 - sh / 2;
                VQuad(mGlow, door - f * sw + o * 0.1, door + f * sw + o * 0.1, sy, sy + sh, A[s.sign], o);
                double hw = s.id == "FLATPAK" ? 10 : 7, top = s.id == "FLATPAK" ? 6.0 : 4.4;
                if (s.enterable)          // E5: lit glass either side of the doorway (you can see in through the doors)
                {
                    VQuad(mGlow, door - f * (hw - 0.1) + o * 0.08, door - f * (DoorHalf + 0.15) + o * 0.08, 0.3, top - 0.2, S("rp_lit"), o);
                    VQuad(mGlow, door + f * (DoorHalf + 0.15) + o * 0.08, door + f * (hw - 0.1) + o * 0.08, 0.3, top - 0.2, S("rp_lit"), o);
                    VQuad(mGlow, door - f * (DoorHalf - 0.1) + o * 0.08, door + f * (DoorHalf - 0.1) + o * 0.08, DoorTop + 0.3, top - 0.2, S("rp_lit"), o);
                }
                else VQuad(mGlow, door - f * (hw - 0.1) + o * 0.08, door + f * (hw - 0.1) + o * 0.08, 0.3, top - 0.2, S("rp_lit"), o);
                // a second sign on the side that faces the road in
                V2 sideN = s.id == "FLATPAK" ? new V2(-1, 0) : s.id == "BASKETFUL" ? new V2(1, 0) : s.id == "GROCERA" ? new V2(-1, 0) : new V2(1, 0);
                V2 sc = new V2(sideN.x < 0 ? s.x0 : s.x1, (s.z0 + s.z1) / 2), sf = sideN.Left;
                VQuad(mGlow, sc - sf * 4.8 + sideN * 0.1, sc + sf * 4.8 + sideN * 0.1, s.h - 2.2, s.h - 1.0, A[s.sign], sideN);
                // bollards along the apron
                for (double t = -hw - 2; t <= hw + 2.01; t += 2)
                {
                    V2 bp = door + f * t + o * 4.6;
                    if (!Free(bp, 0.3)) continue;
                    Cyl(mSolid, bp, 0.11, y, y + 0.9, 6, S("charcoal"));
                    Cyl(mSolid, bp, 0.115, y + 0.7, y + 0.8, 6, S("yellow"), false);
                    Claim(bp, 0.3);
                }
                // trolley bays near the door
                foreach (int k in new[] { -1, 1 }) TrolleyBay(door + f * (k * (hw + 6)) + o * 9, f, y);
            }
            VQuad(mGlow, new V2(1340.4, -330.62), new V2(1351.6, -330.62), 5.65, 7.05, A["sign_CLICK"], new V2(0, -1));     // 11.2 × 1.4 (8:1)

            // 2. the car parks: trolley bays on some islands, CCTV on poles
            int ti = 0;
            foreach (double[] isl in retailIslands)
            {
                V2 c = new V2(isl[0], isl[1]);
                if (ti++ % 3 == 1) TrolleyBay(c + new V2(3.4, 0), new V2(0, 1), y);
            }
            foreach (CarPark cp in CarParks)
                for (int i = 0; i < cp.poly.Count; i++)
                {
                    V2 q = cp.poly[i], cen = new V2(0, 0); foreach (V2 p in cp.poly) cen = cen + p; cen = cen * (1.0 / cp.poly.Count);
                    V2 at = q + (cen - q).Norm * 1.5;
                    if (Free(at, 0.6)) Cctv(at, (cen - q).Norm, y);
                }

            // 3. the totem sign in the middle of the roundabout, facing the way in from Denmore Hill (and back)
            {
                V2 c = Roundabout, face = new V2(-1, 0), f = face.Left;
                SBox(mSolid, c, f, 1.3, 0.35, 0, 8.6, S("charcoal"));
                Collider("Totem", c, f, 2.6, 0.7, 0, 8.6);
                string[] signs = { "sign_FERNDALE", "sign_FLATPAK", "sign_BASKETFUL", "sign_GROCERA", "sign_VALUSTACK" };
                for (int i = 0; i < signs.Length; i++)
                {
                    double y1 = 8.4 - i * 0.62, y0 = y1 - (i == 0 ? 0.32 : 0.3);
                    foreach (int s in new[] { -1, 1 })
                        VQuad(mGlow, c + face * (s * 0.37) - f * (s * 1.2), c + face * (s * 0.37) + f * (s * 1.2), y0, y1, A[signs[i]], face * s);
                }
                Claim(c, 2);
            }

            // 4. yards: lorry trailers at the docks, cages, pallets
            TrailerAt(new V2(1366, -232), new V2(-1, 0), "rp_trailer");
            TrailerAt(new V2(1366, -262), new V2(-1, 0), "rp_c1");
            TrailerAt(new V2(1046, -620), new V2(1, 0), "rp_trailer");
            TrailerAt(new V2(1424, -630), new V2(-1, 0), "rp_c2");
            for (int i = 0; i < 6; i++) Pallets(new V2(1230 + i * 4.2, -224), y, rd);
            for (int i = 0; i < 4; i++) Pallets(new V2(1342 + i * 4.2, -676), y, rd);

            // 5. the wasteland: fences with gaps, containers, skips, burnt-out cars, scrub, rubble
            foreach (List<V2> w in Wastes) WasteFence(w, rd);
            ContainerYard(1600, 1690, -300, -430, rd);
            ContainerYard(1100, 1180, -100, -170, rd);
            ContainerYard(1090, 1180, -380, -440, rd);
            foreach (List<V2> w in Wastes)
            {
                double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (V2 q in w) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
                double area = (maxX - minX) * (maxZ - minZ);
                for (int i = 0; i < area / 160; i++)
                {
                    V2 q = new V2(minX + 3 + rd.NextDouble() * (maxX - minX - 6), minZ + 3 + rd.NextDouble() * (maxZ - minZ - 6));
                    double k = rd.NextDouble();
                    if (k < 0.45) { HQuad(mSolid, q, new V2(rd.NextDouble() - 0.5, 0.5).Norm, 3 + rd.NextDouble() * 6, 2 + rd.NextDouble() * 4, 0.01 + rd.NextDouble() * 0.005, S(rd.NextDouble() < 0.5 ? "rp_dirt" : "rp_gravel")); continue; }
                    if (!Free(q, 1.6)) continue;
                    if (k < 0.75) Scrub(q, rd);
                    else if (k < 0.85) Rubble(q, rd);
                    else if (k < 0.92) Skip(q, new V2(rd.NextDouble() - 0.5, rd.NextDouble() - 0.5).Norm);
                    else BurntCar(q, new V2(rd.NextDouble() - 0.5, rd.NextDouble() - 0.5).Norm);
                    Claim(q, 1.6);
                }
            }
            // graffiti on the ruins
            foreach (double[] rr in ruinRect)
                for (int i = 0; i < 5; i++)
                {
                    bool longSide = rd.NextDouble() < 0.6;
                    double t = rd.NextDouble();
                    V2 a, b, o;
                    if (longSide) { double z = rd.NextDouble() < 0.5 ? rr[2] : rr[3]; o = new V2(0, z == rr[2] ? -1 : 1); a = new V2(rr[0] + 3 + t * (rr[1] - rr[0] - 8), z); b = a + new V2(3.5, 0); }
                    else { double x = rd.NextDouble() < 0.5 ? rr[0] : rr[1]; o = new V2(x == rr[0] ? -1 : 1, 0); a = new V2(x, rr[2] + 3 + t * (rr[3] - rr[2] - 8)); b = a + new V2(0, 3.5); }
                    VQuad(mCut, a + o * 0.05, b + o * 0.05, 0.2, 1.7, A["tag_" + rd.Next(AreaTags.Length)], o);
                }
            D.Count("retail park dressing");
        }

        static void TrolleyBay(V2 c, V2 f, double y)
        {
            if (!Free(c, 1.8)) return;
            f = f.Norm; V2 l = f.Left;
            foreach (int a in new[] { -1, 1 }) foreach (int b in new[] { -1, 1 }) Cyl(mSolid, c + f * (a * 2.0) + l * (b * 0.9), 0.04, 0.15, 2.4, 4, S("metal"), false);
            SBox(mSolid, c, f, 2.2, 1.1, 2.4, 2.5, S("charcoal"), true);
            VQuad(mGlow, c + l * 1.12 - f * 1.0, c + l * 1.12 + f * 1.0, 2.15, 2.4, A["sign_TROLLEYS"], l);
            for (int i = 0; i < 6; i++) SBox(mSolid, c + f * (-1.6 + i * 0.6), l, 0.5, 0.28, 0.45, 1.0, S("metal"));
            Collider("TrolleyBay", c, f, 4.2, 2.0, 0.15, 1.1);
            Claim(c, 2.4);
        }

        static void Cctv(V2 c, V2 look, double y)
        {
            Cyl(mSolid, c, 0.08, y, y + 6.5, 6, S("grey"));
            SBox(mSolid, c + look * 0.3, look, 0.3, 0.1, y + 6.3, y + 6.5, S("white"), true);
            SBox(mSolid, c + look * 0.25, look, 0.28, 0.08, y + 6.0, y + 6.2, S("charcoal"), true);
            Claim(c, 0.6);
        }

        static void TrailerAt(V2 c, V2 f, string colour)
        {
            f = f.Norm;
            SBox(mSolid, c, f, 6.8, 1.25, 1.1, 4.0, S(colour), true);
            foreach (double t in new[] { -5.0, -3.8, -2.6 }) SBox(mSolid, c + f * t, f.Left, 1.2, 0.5, 0.15, 1.1, S("black"));
            SBox(mSolid, c + f * 4.5, f.Left, 1.0, 0.1, 0.15, 1.1, S("metal"));
            Collider("Trailer", c, f, 13.6, 2.5, 0.15, 4.0);
            Claim(c, 4); Claim(c + f * 4.5, 3); Claim(c - f * 4.5, 3);
        }

        static void Pallets(V2 c, double y, Random rd)
        {
            if (!Free(c, 0.8)) return;
            int n = 1 + rd.Next(4);
            for (int i = 0; i < n; i++) SBox(mSolid, c, new V2(1, 0), 0.6, 0.5, y + i * 0.15, y + i * 0.15 + 0.13, S("wood"));
            if (rd.NextDouble() < 0.5) SBox(mSolid, c + new V2(0, 1.4), new V2(1, 0), 0.5, 0.4, y, y + 1.7, S("metal"));
            Claim(c, 1.1);
        }

        /// Chain-link round a patch of wasteland, with gaps you can drive through and the odd panel knocked flat.
        static void WasteFence(List<V2> w, Random rd)
        {
            UVRect link = A["chainlink"], post = S("grey");
            for (int i = 0; i < w.Count; i++)
            {
                V2 a = w[i], b = w[(i + 1) % w.Count], f = (b - a).Norm, o = f.Left;
                double L = V2.Dist(a, b);
                double nextGap = 25 + rd.NextDouble() * 40;
                double runStart = 0;
                for (double t = 0; t + 2.5 <= L + 0.01; t += 2.5)
                {
                    V2 p0 = a + f * t, p1 = a + f * (t + 2.5);
                    if (t >= nextGap)
                    {
                        // a gap (7–10 m): close the run's collider, then skip
                        if (t > runStart) Collider("Fence", Lerp(a + f * runStart, p0, 0.5), f, t - runStart, 0.12, 0, 2.0);
                        double gap = 7.5 + rd.NextDouble() * 2.5;
                        if (rd.NextDouble() < 0.4) HQuad(mCut, a + f * (t + gap / 2) + o * 1.2, f, 1.25, 1.0, 0.03, link);       // knocked flat
                        t += gap - 2.5; runStart = t + 2.5; nextGap = t + 30 + rd.NextDouble() * 45;
                        continue;
                    }
                    bool clear = Free(Lerp(p0, p1, 0.5), 0.2);
                    if (!clear) { if (t > runStart) Collider("Fence", Lerp(a + f * runStart, p0, 0.5), f, t - runStart, 0.12, 0, 2.0); runStart = t + 2.5; continue; }
                    VQuad(mCut, p0, p1, 0, 2.0, link, o);
                    VQuad(mCut, p1, p0, 0, 2.0, link, o * -1);
                    Cyl(mSolid, p0, 0.035, 0, 2.05, 5, post, false);
                    D.Count("wasteland fence panels");
                }
                if (L > runStart + 0.5) Collider("Fence", Lerp(a + f * runStart, b, 0.5), f, L - runStart, 0.12, 0, 2.0);
            }
        }

        /// Stacks of shipping containers in rows, with lanes between them (a maze to lose someone in).
        static void ContainerYard(double x0, double x1, double z0, double z1, Random rd)
        {
            string[] cols = { "rp_c1", "rp_c2", "rp_c3", "rp_c4", "grey" };
            double zt = Math.Max(z0, z1), zb = Math.Min(z0, z1);
            for (double x = x0 + 2; x + 2.5 <= x1; x += 8.5)
                for (double z = zb + 2; z + 12.2 <= zt; z += 15)
                {
                    if (rd.NextDouble() < 0.2) continue;
                    V2 c = new V2(x + 1.25, z + 6.1);
                    int tiers = 1 + rd.Next(3);
                    for (int k = 0; k < tiers; k++)
                    {
                        V2 ck = c + new V2((rd.NextDouble() - 0.5) * 0.3, (rd.NextDouble() - 0.5) * 0.4);
                        UVRect col = S(cols[rd.Next(cols.Length)]);
                        SBox(mSolid, ck, new V2(0, 1), 6.05, 1.22, k * 2.6, k * 2.6 + 2.59, col, k == 0);
                        for (double t = -5.4; t <= 5.41; t += 0.9) SBox(mSolid, ck + new V2(1.23, t), new V2(0, 1), 0.06, 0.02, k * 2.6 + 0.15, k * 2.6 + 2.45, col);
                    }
                    Collider("Container", c, new V2(0, 1), 12.1, 2.44, 0, tiers * 2.6);
                    Claim(c, 3.5); Claim(c + new V2(0, 4), 2.5); Claim(c - new V2(0, 4), 2.5);
                    D.Count("containers", tiers);
                }
        }

        static void Scrub(V2 q, Random rd)
        {
            int n = 2 + rd.Next(4);
            for (int i = 0; i < n; i++)
            {
                V2 p = q + new V2(rd.NextDouble() - 0.5, rd.NextDouble() - 0.5) * 2.4;
                double r = 0.5 + rd.NextDouble() * 0.9;
                SBox(mSolid, p, new V2(rd.NextDouble() - 0.5, 0.5).Norm, r, r * 0.8, 0, 0.5 + rd.NextDouble() * 1.2, S(rd.NextDouble() < 0.5 ? "rp_scrub" : "leaf" + (1 + rd.Next(3))));
            }
        }

        static void Rubble(V2 q, Random rd)
        {
            int n = 3 + rd.Next(5);
            for (int i = 0; i < n; i++)
            {
                V2 p = q + new V2(rd.NextDouble() - 0.5, rd.NextDouble() - 0.5) * 2.6;
                double r = 0.2 + rd.NextDouble() * 0.6;
                SBox(mSolid, p, new V2(rd.NextDouble() - 0.5, rd.NextDouble() - 0.5).Norm, r, r * 0.7, 0, r * 0.8, S(rd.NextDouble() < 0.5 ? "concrete" : "brick"));
            }
        }

        static void Skip(V2 c, V2 f)
        {
            SBox(mSolid, c, f, 1.9, 0.9, 0, 1.3, S("rp_skip"));
            SBox(mSolid, c, f, 1.7, 0.75, 1.1, 1.32, S("bag"));
            Collider("Skip", c, f, 3.8, 1.8, 0, 1.3);
        }

        static void BurntCar(V2 c, V2 f)
        {
            SBox(mSolid, c, f, 2.2, 0.9, 0.25, 0.95, S("rp_burnt"), true);
            SBox(mSolid, c - f * 0.3, f, 1.2, 0.8, 0.95, 1.45, S("rp_rust"));
            Collider("BurntCar", c, f, 4.4, 1.8, 0, 1.45);
        }
    }
}
