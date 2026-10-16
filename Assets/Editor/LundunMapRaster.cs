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

        static MapImage img;

        public static MapImage Render(LundunWorldData W, GreyboxResult R, double metresPerPixel)
        {
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

            foreach (LwPoly park in W.parks)
            {
                List<V2> poly = new List<V2>();
                foreach (LwPoint p in park.polygon) poly.Add(new V2(p.x, p.z));
                Polygon(poly, Park);
            }

            foreach (BuildingInfo bd in R.buildings)
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
            Dictionary<string, LwRoadClass> cls = new Dictionary<string, LwRoadClass>();
            foreach (LwRoadClass c in W.roadClasses) cls[c.name] = c;
            foreach (LwRoad r in W.roads)
            {
                double p = cls[r.roadClass].pavement;
                if (p > 0) Polyline(r.points, r.width / 2 + p, Pavement);
            }
            string[] order = { "service", "street", "arterial", "bridge", "ring" };
            foreach (string o in order)
                foreach (LwRoad r in W.roads)
                    if (r.roadClass == o) Polyline(r.points, r.width / 2, ColourFor(o));

            // the hand-built high street's side road (not in the world data)
            LwExistingStreet es = W.existingStreet;
            Capsule(new V2(es.centreX, es.centreZ - es.sizeZ / 2 - 4), new V2(es.centreX, es.centreZ + es.sizeZ / 2 + 4), 4, Street);
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
