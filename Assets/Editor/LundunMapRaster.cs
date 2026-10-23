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
        static readonly byte[] Ring = { 222, 186, 104 };
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

        static MapImage img;

        /// E1: X / XR = an expansion area (Generator.ExpansionWorld and BuildExpansion's result), drawn with the city.
        public static MapImage Render(LundunWorldData W, GreyboxResult R, double metresPerPixel, LundunWorldData X = null, GreyboxResult XR = null)
        {
            List<LwPoly> allParks = new List<LwPoly>(W.parks);
            List<BuildingInfo> allBuildings = new List<BuildingInfo>(R.buildings);
            List<LwRoad> allRoads = new List<LwRoad>(W.roads);
            Dictionary<string, LwRoadClass> cls = new Dictionary<string, LwRoadClass>();
            foreach (LwRoadClass c in W.roadClasses) cls[c.name] = c;
            if (X != null && XR != null)
            {
                allParks.AddRange(X.parks); allBuildings.AddRange(XR.buildings); allRoads.AddRange(X.roads);
                foreach (LwRoadClass c in X.roadClasses) if (!cls.ContainsKey(c.name)) cls[c.name] = c;
            }
            LwBounds b = W.worldBounds;
            img = new MapImage { metresPerPixel = metresPerPixel, minX = b.wallMinX - 30, minZ = b.wallMinZ - 30 };
            img.width = (int)Math.Ceiling((b.wallMaxX + 30 - img.minX) / metresPerPixel);
            img.height = (int)Math.Ceiling((b.wallMaxZ + 30 - img.minZ) / metresPerPixel);
            img.rgba = new byte[img.width * img.height * 4];
            for (int i = 0; i < img.width * img.height; i++) { img.rgba[i * 4] = Land[0]; img.rgba[i * 4 + 1] = Land[1]; img.rgba[i * 4 + 2] = Land[2]; img.rgba[i * 4 + 3] = 255; }

            // river (centreline extended past the world edge)
            List<V2> rc = new List<V2>();
            foreach (LwPoint p in W.river.centreline) rc.Add(new V2(p.x, p.z));
            rc.Insert(0, rc[0] + (rc[0] - rc[1]).Norm * 400);
            rc.Add(rc[rc.Count - 1] + (rc[rc.Count - 1] - rc[rc.Count - 2]).Norm * 400);
            for (int i = 0; i + 1 < rc.Count; i++) Capsule(rc[i], rc[i + 1], 39, Bank);
            for (int i = 0; i + 1 < rc.Count; i++) Capsule(rc[i], rc[i + 1], 35, Water);

            foreach (LwPoly park in allParks)
            {
                List<V2> poly = new List<V2>();
                foreach (LwPoint p in park.polygon) poly.Add(new V2(p.x, p.z));
                Polygon(poly, park.name != null && park.name.EndsWith("Square") ? Service : Park);     // E1: town squares are paved
            }

            foreach (BuildingInfo bd in allBuildings)
            {
                V2 f = bd.dir.Norm, l = f.Left;
                double hl = bd.len / 2, hd = bd.dep / 2;
                Polygon(new List<V2> { bd.c - f * hl - l * hd, bd.c + f * hl - l * hd, bd.c + f * hl + l * hd, bd.c - f * hl + l * hd }, Building);
            }

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
                double s0 = Generator.RailStationStart + 1, s1 = rp.length - 1;
                for (double s = s0; s < s1; s += 4)
                {
                    double e = Math.Min(s1, s + 4);
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

        static byte[] ColourFor(string roadClass)
        {
            switch (roadClass)
            {
                case "ring": return Ring;
                case "arterial": case "bridge": return Arterial;
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
