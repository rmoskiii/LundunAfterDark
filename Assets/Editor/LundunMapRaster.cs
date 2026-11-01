// Draws the top-down map picture (for the minimap and, in Pass 3, the full map) from the world data.
// Pure maths, no UnityEngine — tested outside Unity. LundunMinimapBuilder saves the result as a PNG.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class MapImage
    {
        public int width, height;
        public double minX, minZ, metresPerPixel;
        public byte[] rgba;       // row 0 = south edge (Unity textures start at the bottom)
    }

    /// E8: one expansion area for the map picture: its world, what was built, and its extra shapes (Generator.RetailMap's
    /// kinds 0..2 for Brixtun's retail park, Generator.KingsmereMap's 10..16 for Kingsmere).
    public class MapArea
    {
        public LundunWorldData world;
        public GreyboxResult result;
        public List<Generator.RetailMapPoly> shapes = new List<Generator.RetailMapPoly>();
    }

    public static class MapRaster
    {
        // GTA-style dark map
        static readonly byte[] Land = { 38, 44, 40 };
        static readonly byte[] Bank = { 70, 92, 112 };
        static readonly byte[] Water = { 30, 66, 104 };
        static readonly byte[] Park = { 52, 92, 56 };
        static readonly byte[] Building = { 62, 68, 66 };
        static readonly byte[] Pavement = { 78, 84, 82 };
        static readonly byte[] Service = { 140, 146, 150 };
        static readonly byte[] Street = { 186, 192, 198 };
        static readonly byte[] Arterial = { 226, 230, 234 };
        static readonly byte[] HospitalPlot = { 112, 66, 72 };
        static readonly byte[] PolicePlot = { 60, 80, 124 };
        static readonly byte[] SafePlot = { 56, 98, 70 };
        static readonly byte[] RailBed = { 92, 80, 72 };          // L6: the railway (brick and ballast)
        static readonly byte[] RailDark = { 40, 40, 44 };
        static readonly byte[] RailLight = { 214, 214, 210 };
        static readonly byte[] Platform = { 196, 92, 82 };
        static readonly byte[] WheelRim = { 236, 236, 240 };      // E7: the Lundun Wheel from above
        static readonly byte[] WheelDeck = { 150, 156, 160 };
        static readonly byte[] WheelPlaza = { 96, 100, 96 };      // E7b: the paved plaza round it
        static readonly byte[] Waste = { 50, 54, 46 };            // E4: the retail park's wasteland
        static readonly byte[] StoreRoof = { 92, 98, 104 };       // E4: the big stores (lighter than houses, so they stand out)
        static readonly byte[] Gravel = { 96, 90, 76 };          // E8: Kingsmere: the forecourt, the parade ground
        static readonly byte[] Palace = { 156, 150, 136 };        //     the palace, the barracks, the Arch (pale stone)
        static readonly byte[] ParadeRed = { 138, 62, 54 };       //     the Parade's red surface
        static readonly byte[] Marble = { 220, 218, 210 };        //     the Memorial, the bridges
        static readonly byte[] ParkPath = { 92, 112, 86 };        //     gravel paths in St Jarvis's Park
        // E8: Greater Lundun round the playable city (the backdrop and the picture's margin)
        static readonly byte[] Hedge = { 28, 58, 34 };            // the hedge round the edge of the world
        static readonly byte[] OuterLand = { 30, 35, 33 };        // the outer boroughs: quieter than the city
        static readonly byte[] OuterStreet = { 50, 56, 54 };
        static readonly byte[] OuterRoad = { 74, 80, 78 };
        static readonly byte[] OuterPark = { 36, 58, 42 };
        static readonly byte[] OuterRim = { 58, 64, 62 };
        static readonly byte[] Beyond = { 8, 10, 13 };            // outside Greater Lundun (the same as the full map's background)
        static readonly byte[] BeyondWater = { 18, 34, 54 };

        static MapImage img;

        /// E1: X / XR = an expansion area (Generator.ExpansionWorld and BuildExpansion's result), drawn with the city.
        public static MapImage Render(LundunWorldData W, GreyboxResult R, double metresPerPixel, LundunWorldData X = null, GreyboxResult XR = null)
        {
            List<MapArea> areas = new List<MapArea>();
            if (X != null && XR != null) areas.Add(new MapArea { world = X, result = XR, shapes = new List<Generator.RetailMapPoly>(Generator.RetailMap) });
            return Render(W, R, metresPerPixel, areas);
        }

        /// E8: the city and any number of expansion areas. Outside the hedge (the picture's margin) it's drawn exactly as
        /// the Greater Lundun backdrop is (RenderBackdrop), so the two meet without a seam.
        public static MapImage Render(LundunWorldData W, GreyboxResult R, double metresPerPixel, List<MapArea> areas)
        {
            List<LwPoly> allParks = new List<LwPoly>(W.parks);
            List<BuildingInfo> allBuildings = new List<BuildingInfo>(R.buildings);
            List<LwRoad> allRoads = new List<LwRoad>(W.roads);
            Dictionary<string, LwRoadClass> cls = new Dictionary<string, LwRoadClass>();
            foreach (LwRoadClass c in W.roadClasses) cls[c.name] = c;
            if (areas == null) areas = new List<MapArea>();
            foreach (MapArea a in areas)
            {
                if (a == null || a.world == null || a.result == null) continue;
                allParks.AddRange(a.world.parks); allBuildings.AddRange(a.result.buildings); allRoads.AddRange(a.world.roads);
                foreach (LwRoadClass c in a.world.roadClasses) if (!cls.ContainsKey(c.name)) cls[c.name] = c;
            }
            LwBounds b = W.worldBounds;
            img = new MapImage { metresPerPixel = metresPerPixel, minX = b.wallMinX - 30, minZ = b.wallMinZ - 30 };
            img.width = (int)Math.Ceiling((b.wallMaxX + 30 - img.minX) / metresPerPixel);
            img.height = (int)Math.Ceiling((b.wallMaxZ + 30 - img.minZ) / metresPerPixel);
            img.rgba = new byte[img.width * img.height * 4];
            Outer outer = new Outer(W);
            for (int y = 0; y < img.height; y++)
                for (int x = 0; x < img.width; x++)
                {
                    double wx = img.minX + (x + 0.5) * metresPerPixel, wz = img.minZ + (y + 0.5) * metresPerPixel;
                    byte[] col = outer.InsideWalls(wx, wz) ? Land : outer.Colour(wx, wz, metresPerPixel);
                    int i = (y * img.width + x) * 4;
                    img.rgba[i] = col[0]; img.rgba[i + 1] = col[1]; img.rgba[i + 2] = col[2]; img.rgba[i + 3] = 255;
                }

            // E8: the hedge round the edge of the world, then the river (the same course as on the backdrop: on past the
            // world's edge, widening downstream), which cuts through it
            Capsule(new V2(b.wallMinX, b.wallMinZ), new V2(b.wallMaxX, b.wallMinZ), 1.6, Hedge);
            Capsule(new V2(b.wallMaxX, b.wallMinZ), new V2(b.wallMaxX, b.wallMaxZ), 1.6, Hedge);
            Capsule(new V2(b.wallMaxX, b.wallMaxZ), new V2(b.wallMinX, b.wallMaxZ), 1.6, Hedge);
            Capsule(new V2(b.wallMinX, b.wallMaxZ), new V2(b.wallMinX, b.wallMinZ), 1.6, Hedge);
            DrawRiver(outer);

            foreach (LwPoly park in allParks)
            {
                List<V2> poly = new List<V2>();
                foreach (LwPoint p in park.polygon) poly.Add(new V2(p.x, p.z));
                Polygon(poly, park.name != null && (park.name.EndsWith("Square") || park.name.EndsWith("Market")) ? Service : Park);     // E1: town squares are paved (E3: and the market)
            }

            foreach (BuildingInfo bd in allBuildings)
            {
                V2 f = bd.dir.Norm, l = f.Left;
                double hl = bd.len / 2, hd = bd.dep / 2;
                Polygon(new List<V2> { bd.c - f * hl - l * hd, bd.c + f * hl - l * hd, bd.c + f * hl + l * hd, bd.c - f * hl + l * hd }, Building);
            }

            // E4: the retail park: wasteland a shade lighter, car parks and yards paved, the stores and ruins as buildings
            // E8: Kingsmere: paths, the forecourt and parade ground, the red Parade, the Memorial, the lake, the palace
            int[] kindOrder = { 2, 0, 1, 15, 10, 14, 12, 16, 11 };      // (the red Parade goes on after the roads)
            foreach (int k in kindOrder)
                foreach (MapArea a in areas)
                    if (a != null && a.shapes != null)
                        foreach (Generator.RetailMapPoly rmp in a.shapes)
                            if (rmp.kind == k) Polygon(rmp.poly, ShapeColour(k));

            // V2b: hospitals, police stations and safehouses (their plots), tinted so they read on the map
            try
            {
                foreach (PlaceSite s in Generator.MapPlaces())
                {
                    if (!s.ok) continue;
                    byte[] tint = s.kind == "hospital" ? HospitalPlot : s.kind == "police" ? PolicePlot : SafePlot;
                    Polygon(new List<V2> { s.Corner(-s.len / 2, 0), s.Corner(s.len / 2, 0), s.Corner(s.len / 2, s.depth), s.Corner(-s.len / 2, s.depth) }, tint);
                }
            }
            catch (Exception) { /* no places planned: draw the map without them */ }

            // roads: pavements first, then carriageways from minor to major so big roads sit on top
            foreach (LwRoad r in allRoads)
            {
                double p = cls[r.roadClass].pavement;
                if (p > 0) Polyline(r.points, r.width / 2 + p, Pavement);
            }
            string[] order = { "service", "street", "arterial", "bridge", "ring" };
            foreach (string o in order)
                foreach (LwRoad r in allRoads)
                    if (r.roadClass == o) Polyline(r.points, r.width / 2, ColourFor(o));
            // E8: the Parade and the Memorial Circle are red, not tarmac
            foreach (MapArea a in areas)
                if (a != null && a.shapes != null)
                    foreach (Generator.RetailMapPoly rmp in a.shapes)
                        if (rmp.kind == 13) Polygon(rmp.poly, ParadeRed);

            // the hand-built high street's side road (not in the world data)
            LwExistingStreet es = W.existingStreet;
            Capsule(new V2(es.centreX, es.centreZ - es.sizeZ / 2 - 4), new V2(es.centreX, es.centreZ + es.sizeZ / 2 + 4), 4, Street);

            // L6: the railway over everything (its bridges cross the roads), the classic black-and-white map line,
            // and Rye Lane station's platform picked out
            Generator.RailPlan rp = Generator.Railway;
            if (rp != null)
            {
                List<V2> pts = rp.line.p;
                for (int i = 0; i + 1 < pts.Count; i++) Capsule(pts[i], pts[i + 1], 5.6, RailBed);
                for (int i = 0; i + 1 < pts.Count; i++) Capsule(pts[i], pts[i + 1], 1.8, RailDark);
                for (double s = 0; s < rp.length - 1; s += 16)
                    Capsule(Generator.RailPoint(s, 0), Generator.RailPoint(Math.Min(rp.length, s + 8), 0), 1.0, RailLight);
                List<double[]> plats = new List<double[]> { new[] { Generator.RailStationStart + 1, rp.length - 1 } };
                double[] bp = Generator.BrixtunPlatform();            // E2: Brixtun station's platform too
                if (bp != null) plats.Add(bp);
                foreach (double[] pl in plats)
                    for (double s = pl[0]; s < pl[1]; s += 4)
                    {
                        double e = Math.Min(pl[1], s + 4);
                        Polygon(new List<V2> { Generator.RailPoint(s, -3.45), Generator.RailPoint(e, -3.45), Generator.RailPoint(e, -7.2), Generator.RailPoint(s, -7.2) }, Platform);
                    }
            }
            // E7: the Lundun Wheel: its platform, its legs and the rim seen from above (a long white bar along the river)
            {
                // E7b: the plaza it stands on, from the pavement to the river wall
                List<V2> plaza = new List<V2>();
                for (double x = -64; x <= 64.01; x += 4) plaza.Add(Generator.WheelToWorld(x, Generator.PlazaRoadZ(x)));
                for (double x = 64; x >= -64.01; x -= 4) plaza.Add(Generator.WheelToWorld(x, Generator.PlazaRiverZ(x)));
                Polygon(plaza, WheelPlaza);
                double ph = Generator.WheelPlatformHalf;
                Polygon(new List<V2> { Generator.WheelToWorld(-ph, Generator.WheelPlatformFar), Generator.WheelToWorld(ph, Generator.WheelPlatformFar),
                                       Generator.WheelToWorld(ph, Generator.WheelPlatformNear), Generator.WheelToWorld(-ph, Generator.WheelPlatformNear) }, WheelDeck);
                foreach (int sa in new[] { -1, 1 })
                    foreach (int sc in new[] { -1, 1 })
                        Capsule(Generator.WheelToWorld(sa * Generator.WheelLegAlong, sc * Generator.WheelLegAcross), Generator.WheelToWorld(0, sc * Generator.WheelAxleHalf), 1.0, WheelRim);
                Capsule(Generator.WheelToWorld(-Generator.WheelCapsuleR - 2, 0), Generator.WheelToWorld(Generator.WheelCapsuleR + 2, 0), 2.6, WheelRim);
                Capsule(Generator.WheelToWorld(0, -Generator.WheelAxleHalf), Generator.WheelToWorld(0, Generator.WheelAxleHalf), 1.6, WheelRim);
            }
            return img;
        }

        static byte[] ShapeColour(int kind)
        {
            switch (kind)
            {
                case 0: return Service;
                case 1: return StoreRoof;
                case 2: return Waste;
                case 10: return Gravel;
                case 11: return Palace;
                case 12: return Water;
                case 13: return ParadeRed;
                case 14: return Marble;
                case 15: return ParkPath;
                default: return Park;
            }
        }

        // ================================================================== E8: Greater Lundun
        /// The full map's backdrop: Greater Lundun, a rounded city with the playable part in the middle, the river crossing
        /// it from edge to edge (widening into an estuary downstream), the outer boroughs' streets and parks drawn quietly
        /// round it, and the dark beyond. Its area (minX, minZ, width, height in metres) comes back in the image.
        public static MapImage RenderBackdrop(LundunWorldData W, double metresPerPixel)
        {
            Outer o = new Outer(W);
            img = new MapImage { metresPerPixel = metresPerPixel, minX = o.areaMinX, minZ = o.areaMinZ };
            img.width = (int)Math.Ceiling((o.areaMaxX - o.areaMinX) / metresPerPixel);
            img.height = (int)Math.Ceiling((o.areaMaxZ - o.areaMinZ) / metresPerPixel);
            img.rgba = new byte[img.width * img.height * 4];
            for (int y = 0; y < img.height; y++)
                for (int x = 0; x < img.width; x++)
                {
                    double wx = img.minX + (x + 0.5) * metresPerPixel, wz = img.minZ + (y + 0.5) * metresPerPixel;
                    byte[] col = o.InsideWalls(wx, wz) ? Land : o.Colour(wx, wz, metresPerPixel);
                    int i = (y * img.width + x) * 4;
                    img.rgba[i] = col[0]; img.rgba[i + 1] = col[1]; img.rgba[i + 2] = col[2]; img.rgba[i + 3] = 255;
                }
            DrawRiver(o);
            return img;
        }

        /// The outer boroughs' names and where they go on the full map (between the hedge and Greater Lundun's edge).
        public static List<KeyValuePair<string, V2>> OuterNames(LundunWorldData W)
        {
            Outer o = new Outer(W);
            string[] names = { "NORTHOLM", "FENWICK HEATH", "MARSHREACH", "ASHCOMBE", "SOUTHCROFT", "BRAMLEY VALE", "WESTHOLLOW", "KESTREL HILL" };
            double[] deg = { 90, 38, -22, -62, -90, -125, 200, 142 };
            List<KeyValuePair<string, V2>> l = new List<KeyValuePair<string, V2>>();
            for (int i = 0; i < names.Length; i++)
            {
                double a = deg[i] * Math.PI / 180;
                V2 d = new V2(Math.Cos(a), Math.Sin(a));
                double r0 = o.RectReach(d), r1 = o.EdgeRadius(a);
                l.Add(new KeyValuePair<string, V2>(names[i], o.centre + d * (r0 + (r1 - r0) * 0.5)));
            }
            return l;
        }

        /// The river: banks and water, along its course out of the world both ways. Downstream (east) it widens into an
        /// estuary; beyond Greater Lundun's edge it darkens and fades out.
        static void DrawRiver(Outer o)
        {
            List<V2> rc = o.river; List<double> hw = o.riverHalf;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i + 1 < rc.Count; i++)
                {
                    V2 a = rc[i], c = rc[i + 1];
                    double h = (hw[i] + hw[i + 1]) / 2 + (pass == 0 ? 4 : 0);
                    RiverCapsule(o, a, c, h, pass == 0 ? Bank : Water);
                }
        }

        /// A capsule of river that darkens outside Greater Lundun (and fades into the dark towards the picture's edge).
        static void RiverCapsule(Outer o, V2 a, V2 b, double halfWidth, byte[] col)
        {
            double m = img.metresPerPixel, pad = halfWidth + 2 * m;
            int x0 = Px(Math.Min(a.x, b.x) - pad), x1 = Px(Math.Max(a.x, b.x) + pad);
            int y0 = Py(Math.Min(a.z, b.z) - pad), y1 = Py(Math.Max(a.z, b.z) + pad);
            byte[] dark = col == Water ? BeyondWater : Beyond;
            byte[] mix = new byte[3];
            for (int y = Math.Max(0, y0); y <= Math.Min(img.height - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(img.width - 1, x1); x++)
                {
                    V2 q = new V2(img.minX + (x + 0.5) * m, img.minZ + (y + 0.5) * m);
                    double d = Line.SegDist(q, a, b) - halfWidth;
                    double cover = Math.Max(0, Math.Min(1, 0.5 - d / m));
                    if (cover <= 0) continue;
                    double inside = o.Inside(q.x, q.z);                     // 1 in Greater Lundun, 0 beyond (soft edge)
                    double fade = o.EdgeFade(q.x, q.z);                    // 0 at the backdrop's edge
                    for (int k = 0; k < 3; k++)
                    {
                        double beyond = Beyond[k] + (dark[k] - Beyond[k]) * fade;
                        mix[k] = (byte)Math.Round(beyond + (col[k] - beyond) * inside);
                    }
                    Blend(x, y, mix, cover);
                }
        }

        /// Greater Lundun's shape and its outer boroughs (no random numbers: the same picture every time).
        class Outer
        {
            public V2 centre;
            public double wx0, wx1, wz0, wz1;                   // the hedge
            double A, B;                                        // the round edge's half-widths
            public double areaMinX, areaMinZ, areaMaxX, areaMaxZ;
            public readonly List<V2> river = new List<V2>();
            public readonly List<double> riverHalf = new List<double>();

            public Outer(LundunWorldData W)
            {
                LwBounds b = W.worldBounds;
                wx0 = b.wallMinX; wx1 = b.wallMaxX; wz0 = b.wallMinZ; wz1 = b.wallMaxZ;
                centre = new V2((wx0 + wx1) / 2, (wz0 + wz1) / 2);
                double hx = (wx1 - wx0) / 2 + 30, hz = (wz1 - wz0) / 2 + 30;
                // round enough to hold the playable rectangle's corners well inside (and a good belt of borough round it)
                A = Math.Max(hx * 1.75, hz * 1.05); B = Math.Max(hz * 1.42, A * 0.9);
                areaMinX = centre.x - A * 1.12; areaMaxX = centre.x + A * 1.12;
                areaMinZ = centre.z - B * 1.1; areaMaxZ = centre.z + B * 1.1;
                // the river: the world's own course, then on past the hedge both ways (straight for 120 m, so the map
                // picture's margin matches; then meandering), half-width 35 m, widening downstream past the hedge
                List<V2> rc = new List<V2>();
                foreach (LwPoint p in W.river.centreline) rc.Add(new V2(p.x, p.z));
                List<V2> west = Extend(rc[0], (rc[0] - rc[1]).Norm, areaMinX - 200, -1);
                List<V2> east = Extend(rc[rc.Count - 1], (rc[rc.Count - 1] - rc[rc.Count - 2]).Norm, areaMaxX + 200, 1);
                west.Reverse();
                river.AddRange(west); river.AddRange(rc); river.AddRange(east);
                foreach (V2 p in river)
                {
                    double past = p.x - (wx1 + 120);
                    riverHalf.Add(past > 0 ? 35 + Math.Min(110, past * 0.075 + past * past * 0.00002) : 35);
                }
            }

            /// The river's course beyond the end of the world's centreline: straight out for 120 m beyond the hedge, then
            /// bending gently (two slow waves), until it's off the picture.
            List<V2> Extend(V2 from, V2 dir, double untilX, int side)
            {
                List<V2> l = new List<V2>();
                double edge = side > 0 ? wx1 + 120 : wx0 - 120;
                V2 p = from; double sAfter = 0;
                // keep it running mostly east-west
                V2 d = new V2(dir.x, dir.z * 0.35).Norm;
                for (int i = 0; i < 400; i++)
                {
                    V2 n = p + (i == 0 ? dir : d) * 40;
                    bool beyond = side > 0 ? n.x > edge : n.x < edge;
                    if (beyond)
                    {
                        sAfter += 40;
                        double w = Math.Min(1, sAfter / 300);
                        n = new V2(n.x, n.z + w * (Math.Sin(sAfter / 520 + (side > 0 ? 0.4 : 2.1)) * 7.5 + Math.Sin(sAfter / 190) * 2.5) * (side > 0 ? 1 : -1));
                    }
                    else if (i > 0) n = p + dir * 40;
                    l.Add(n);
                    p = n;
                    if (side > 0 ? p.x > untilX : p.x < untilX) break;
                }
                return l;
            }

            public bool InsideWalls(double x, double z) { return x >= wx0 && x <= wx1 && z >= wz0 && z <= wz1; }

            /// How far Greater Lundun's edge is from the centre at angle a (radians): an ellipse with a gentle, irregular
            /// wobble, never much closer than 200 m outside the hedge.
            public double EdgeRadius(double a)
            {
                double c = Math.Cos(a), s = Math.Sin(a);
                double r = 1 / Math.Sqrt(c * c / (A * A) + s * s / (B * B));
                r *= 1 + 0.045 * Math.Sin(3 * a + 1.1) + 0.03 * Math.Sin(5 * a + 2.3) + 0.018 * Math.Sin(9 * a + 0.4) + 0.008 * Math.Sin(17 * a + 1.7);
                double m = RectReach(new V2(c, s)) + 200, k = 260;
                return 0.5 * (r + m + Math.Sqrt((r - m) * (r - m) + k * k));        // a smooth "at least m" (no kinks)
            }

            /// From the centre, how far to the hedge in direction d.
            public double RectReach(V2 d)
            {
                double tx = Math.Abs(d.x) < 1e-9 ? double.MaxValue : ((d.x > 0 ? wx1 : wx0) - centre.x) / d.x;
                double tz = Math.Abs(d.z) < 1e-9 ? double.MaxValue : ((d.z > 0 ? wz1 : wz0) - centre.z) / d.z;
                return Math.Min(tx, tz);
            }

            /// 1 inside Greater Lundun, 0 beyond, with a 40 m soft edge.
            public double Inside(double x, double z)
            {
                double dx = x - centre.x, dz = z - centre.z, r = Math.Sqrt(dx * dx + dz * dz);
                double e = EdgeRadius(Math.Atan2(dz, dx));
                return Math.Max(0, Math.Min(1, (e - r) / 40 + 0.5));
            }

            /// 1 well inside the backdrop, falling to 0 at its edge (the river fades out).
            public double EdgeFade(double x, double z)
            {
                double d = Math.Min(Math.Min(x - areaMinX, areaMaxX - x), Math.Min(z - areaMinZ, areaMaxZ - z));
                return Math.Max(0, Math.Min(1, d / 600));
            }

            static double Hash(int a, int b)
            {
                unchecked
                {
                    uint h = (uint)(a * 374761393 + b * 668265263);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    return ((h ^ (h >> 16)) & 0xffffff) / (double)0x1000000;
                }
            }

            /// The colour of the ground outside the hedge: the outer boroughs (streets, main roads, parks) inside Greater
            /// Lundun, a soft rim at its edge, the dark beyond.
            public byte[] Colour(double x, double z, double mpp)
            {
                double dx = x - centre.x, dz = z - centre.z, r = Math.Sqrt(dx * dx + dz * dz), ang = Math.Atan2(dz, dx);
                double e = EdgeRadius(ang);
                double inside = Math.Max(0, Math.Min(1, (e - r) / 40 + 0.5));
                double[] c = { OuterLand[0], OuterLand[1], OuterLand[2] };
                if (inside > 0)
                {
                    // parks: blobs on a coarse grid
                    double gp = 260;
                    int pi = (int)Math.Floor(x / gp), pj = (int)Math.Floor(z / gp);
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            int ci = pi + di, cj = pj + dj;
                            if (Hash(ci, cj) > 0.22) continue;
                            double px = (ci + 0.2 + 0.6 * Hash(cj, ci + 7)) * gp, pz = (cj + 0.2 + 0.6 * Hash(ci + 3, cj)) * gp;
                            double rad = 45 + 70 * Hash(ci + 11, cj + 5);
                            double pa = Math.Atan2(z - pz, x - px);
                            double rr = rad * (1 + 0.18 * Math.Sin(3 * pa + ci) + 0.1 * Math.Sin(5 * pa + cj));
                            double dd = Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                            if (dd < rr) { double t = Math.Min(1, (rr - dd) / (2 * mpp)); Mix(c, OuterPark, t); }
                        }
                    // streets: a gently warped grid with missing bits (organic-looking blocks)
                    double ux = x + 34 * Math.Sin(z / 310 + 0.7) + 18 * Math.Sin(z / 97), uz = z + 30 * Math.Sin(x / 270 + 1.9) + 16 * Math.Sin(x / 113);
                    const double g = 96;
                    double fx = ux / g - Math.Floor(ux / g), fz = uz / g - Math.Floor(uz / g);
                    double sx = Math.Min(fx, 1 - fx) * g, sz = Math.Min(fz, 1 - fz) * g;
                    int cx = (int)Math.Floor(ux / g + 0.5), cz = (int)Math.Floor(uz / g);
                    int kx = (int)Math.Floor(ux / g), kz = (int)Math.Floor(uz / g + 0.5);
                    double street = 0;
                    if (Hash(cx * 3 + 1, cz * 5) > 0.18) street = Math.Max(street, Math.Min(1, (2.4 - sx) / mpp + 0.5));
                    if (Hash(kx * 7, kz * 3 + 2) > 0.18) street = Math.Max(street, Math.Min(1, (2.4 - sz) / mpp + 0.5));
                    if (street > 0) Mix(c, OuterStreet, street * 0.9);
                    // main roads out of the city: spokes from the middle, gently curving
                    for (int k = 0; k < 12; k++)
                    {
                        double a0 = k * Math.PI * 2 / 12 + 0.13;
                        double a = a0 + 0.06 * Math.Sin(r / 420 + k);
                        double da = Math.Atan2(Math.Sin(ang - a), Math.Cos(ang - a));
                        double off = Math.Abs(da) * r;
                        if (off < 7 + mpp) Mix(c, OuterRoad, Math.Max(0, Math.Min(1, (5 - off) / mpp + 0.5)));
                    }
                    // an orbital road round it, inside the edge
                    double orb = Math.Abs(r - (e - 140));
                    if (orb < 7 + mpp) Mix(c, OuterRoad, Math.Max(0, Math.Min(1, (5 - orb) / mpp + 0.5)));
                    // the rim
                    double rim = Math.Abs(e - 8 - r);
                    if (rim < 3 + mpp) Mix(c, OuterRim, Math.Max(0, Math.Min(1, (2 - rim) / mpp + 0.5)));
                }
                for (int k = 0; k < 3; k++) c[k] = Beyond[k] + (c[k] - Beyond[k]) * inside;
                return new[] { (byte)Math.Round(c[0]), (byte)Math.Round(c[1]), (byte)Math.Round(c[2]) };
            }

            static void Mix(double[] c, byte[] col, double t)
            {
                if (t <= 0) return; if (t > 1) t = 1;
                for (int k = 0; k < 3; k++) c[k] += (col[k] - c[k]) * t;
            }
        }

        static byte[] ColourFor(string roadClass)
        {
            switch (roadClass)
            {
                case "ring": case "arterial": case "bridge": return Arterial;     // E8: the Ring like any main road (no gold border round the city)
                case "service": return Service;
                default: return Street;
            }
        }

        static void Polyline(LwPoint[] pts, double halfWidth, byte[] col)
        {
            for (int i = 0; i + 1 < pts.Length; i++)
                Capsule(new V2(pts[i].x, pts[i].z), new V2(pts[i + 1].x, pts[i + 1].z), halfWidth, col);
        }

        /// Thick line with round ends and smooth (anti-aliased) edges.
        static void Capsule(V2 a, V2 b, double halfWidth, byte[] col)
        {
            double m = img.metresPerPixel, pad = halfWidth + 2 * m;
            int x0 = Px(Math.Min(a.x, b.x) - pad), x1 = Px(Math.Max(a.x, b.x) + pad);
            int y0 = Py(Math.Min(a.z, b.z) - pad), y1 = Py(Math.Max(a.z, b.z) + pad);
            for (int y = Math.Max(0, y0); y <= Math.Min(img.height - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(img.width - 1, x1); x++)
                {
                    V2 q = new V2(img.minX + (x + 0.5) * m, img.minZ + (y + 0.5) * m);
                    double d = Line.SegDist(q, a, b) - halfWidth;
                    double cover = Math.Max(0, Math.Min(1, 0.5 - d / m));
                    if (cover > 0) Blend(x, y, col, cover);
                }
        }

        static void Polygon(List<V2> poly, byte[] col)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (V2 q in poly) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
            double m = img.metresPerPixel;
            for (int y = Math.Max(0, Py(minZ)); y <= Math.Min(img.height - 1, Py(maxZ)); y++)
                for (int x = Math.Max(0, Px(minX)); x <= Math.Min(img.width - 1, Px(maxX)); x++)
                    if (Generator.InPoly(new V2(img.minX + (x + 0.5) * m, img.minZ + (y + 0.5) * m), poly)) Blend(x, y, col, 1);
        }

        static int Px(double wx) { return (int)Math.Floor((wx - img.minX) / img.metresPerPixel); }
        static int Py(double wz) { return (int)Math.Floor((wz - img.minZ) / img.metresPerPixel); }

        static void Blend(int x, int y, byte[] col, double a)
        {
            int i = (y * img.width + x) * 4;
            for (int k = 0; k < 3; k++) img.rgba[i + k] = (byte)Math.Round(img.rgba[i + k] * (1 - a) + col[k] * a);
        }
    }
}
