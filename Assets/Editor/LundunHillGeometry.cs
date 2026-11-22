// G7: the hills' shape (Lundun → 40). Pure maths like the rest of the generator: no UnityEngine types.
// Each hill is a height above the flat city, kept as a grid you can ask for the height anywhere.
//   RYE HILL (G7a): Rye Common, east of Peckam Rye. Open grass only: it rises away from every road and building (so
//     the streets round it stay exactly where they are), starting 6 m in from the pavements.
//   BROCKMERE HILL (G7b): Brockmere Park on top (+18 m); Brixtun Hill climbs to it from Denmore Hill, the estate
//     streets north of it climb too (never steeper than about 8%).
//   DENMORE HILL (G7b): Denmore Hill, the street from Peckam down into Brixtun, goes over a +12 m crest (about 9% along
//     the street) with the houses either side stepping down.
// On the G7b hills the streets slope with the ground, and every building stays level: it sits at its front door's
// height (the highest point of the pavement along its front, less 30 cm), with a plinth under its downhill side.
// Hills(data) works all of it out (it runs Build and BuildExpansion, and leaves the generator on the city).
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class HillGrid
    {
        public string name;
        public double x0, z0, cell; public int nx, nz;
        public float[] h;                                  // nx * nz heights (metres above the flat city)
        public double top, steepest;                       // the highest point, the steepest slope (rise over run)
        public double xMin, zMin, xMax, zMax;               // where the ground actually rises (with a cell to spare)

        public double At(double x, double z)
        {
            double fx = (x - x0) / cell, fz = (z - z0) / cell;
            if (fx < 0 || fz < 0 || fx >= nx - 1 || fz >= nz - 1) return 0;
            int i = (int)fx, j = (int)fz; double tx = fx - i, tz = fz - j;
            double a = h[j * nx + i], b = h[j * nx + i + 1], c = h[(j + 1) * nx + i], d = h[(j + 1) * nx + i + 1];
            return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
        }

        /// Does a box (x, z) reach the part that rises?
        public bool Touches(double bxMin, double bzMin, double bxMax, double bzMax)
        {
            return bxMax >= xMin && bzMax >= zMin && bxMin <= xMax && bzMin <= zMax;
        }

        int[] sat;                                          // how many rising cells (summed), for Rises()

        /// Is there any rising ground under a box (x, z)? (Quick: a summed table of the cells that rise.)
        public bool Rises(double bxMin, double bzMin, double bxMax, double bzMax)
        {
            if (!Touches(bxMin, bzMin, bxMax, bzMax)) return false;
            if (sat == null)
            {
                sat = new int[(nx + 1) * (nz + 1)];
                for (int j = 0; j < nz; j++)
                    for (int i = 0; i < nx; i++)
                        sat[(j + 1) * (nx + 1) + i + 1] = (h[j * nx + i] > 0.001f ? 1 : 0) + sat[j * (nx + 1) + i + 1] + sat[(j + 1) * (nx + 1) + i] - sat[j * (nx + 1) + i];
            }
            int i0 = Math.Max(0, (int)Math.Floor((bxMin - x0) / cell)), i1 = Math.Min(nx - 1, (int)Math.Ceiling((bxMax - x0) / cell));
            int j0 = Math.Max(0, (int)Math.Floor((bzMin - z0) / cell)), j1 = Math.Min(nz - 1, (int)Math.Ceiling((bzMax - z0) / cell));
            if (i0 > i1 || j0 > j1) return false;
            return sat[(j1 + 1) * (nx + 1) + i1 + 1] - sat[j0 * (nx + 1) + i1 + 1] - sat[(j1 + 1) * (nx + 1) + i0] + sat[j0 * (nx + 1) + i0] > 0;
        }

        /// The slope at a point: how much the ground rises per metre east (gx) and per metre north (gz).
        public void Slope(double x, double z, out double gx, out double gz)
        {
            const double e = 0.75;
            gx = (At(x + e, z) - At(x - e, z)) / (2 * e);
            gz = (At(x, z + e) - At(x, z - e)) / (2 * e);
        }
    }

    /// A building on a hill: it stays level, at `lift` (its front door's height); `low` is the lowest ground under it.
    public class HillBuilding
    {
        public V2 c, f, l; public double hl, hd, lift, low;
        public double Outside(double x, double z)                           // how far outside its footprint (0 inside)
        {
            V2 d = new V2(x - c.x, z - c.z);
            double u = Math.Abs(V2.Dot(d, f)) - hl, v = Math.Abs(V2.Dot(d, l)) - hd;
            return Math.Sqrt(Math.Max(u, 0) * Math.Max(u, 0) + Math.Max(v, 0) * Math.Max(v, 0));
        }
    }

    /// All the hills together: the ground's height anywhere (they don't overlap), and the buildings standing on them.
    public class HillSet
    {
        public List<HillGrid> grids = new List<HillGrid>();
        public List<HillBuilding> buildings = new List<HillBuilding>();
        const double Margin = 1.5;                                          // the furthest a building's own things reach
        Dictionary<long, List<HillBuilding>> cells = new Dictionary<long, List<HillBuilding>>();
        const double CellSize = 32;

        public double At(double x, double z) { double h = 0; foreach (HillGrid g in grids) h += g.At(x, z); return h; }

        public bool Rises(double x0, double z0, double x1, double z1)
        {
            foreach (HillGrid g in grids) if (g.Rises(x0, z0, x1, z1)) return true;
            return false;
        }

        public void Slope(double x, double z, out double gx, out double gz)
        {
            gx = gz = 0;
            foreach (HillGrid g in grids) { double a, b; g.Slope(x, z, out a, out b); gx += a; gz += b; }
        }

        /// The nearest building (on a hill) within `reach` of a point, and how far outside its walls the point is.
        public HillBuilding Near(double x, double z, double reach, out double dist)
        {
            dist = reach;
            List<HillBuilding> l;
            if (!cells.TryGetValue(Key(x, z), out l)) return null;
            HillBuilding best = null;
            foreach (HillBuilding b in l) { double d = b.Outside(x, z); if (d <= dist) { dist = d; best = b; } }
            return best;
        }

        /// How much a point goes up. A building's own mesh (`ofBuilding`) goes up by the building's height wherever it's
        /// within 1 m of its walls (porches, bays, shop fronts), fading to the ground's by 1.5 m. Anything else (the
        /// dressing, signs, parked cars) is the ground's at street level, turning into the building's from 0.5 m up to
        /// 3 m up, when it's within 0.8 m of the walls (fading out by 1.2 m): so a shop front, a sign or an awning stays
        /// on its building, and a bin or a garden wall stays on the pavement, with nothing torn in between.
        /// `whole` is the building when the point is entirely its (so its faces needn't be cut up).
        public double Lift(double x, double y, double z, bool ofBuilding, out HillBuilding whole)
        {
            whole = null;
            double ground = At(x, z), d;
            HillBuilding b = Near(x, z, ofBuilding ? 1.5 : 1.2, out d);
            if (b == null) return ground;
            double w = ofBuilding ? Smooth01((1.5 - d) / 0.5) : Smooth01((1.2 - d) / 0.4) * Smooth01((y - 0.5) / 2.5);
            if (w >= 0.999) { whole = b; return b.lift; }
            return ground + (b.lift - ground) * w;
        }

        public double Lift(double x, double y, double z) { HillBuilding w; return Lift(x, y, z, false, out w); }

        /// Is a point within 1 m of a building's walls (for telling a building's own mesh from the rest)?
        public bool InBuilding(double x, double z) { double d; return Near(x, z, 1.0, out d) != null; }

        static double Smooth01(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }

        static long Key(double x, double z) { return ((long)Math.Floor(x / CellSize) << 32) ^ (uint)(int)Math.Floor(z / CellSize); }

        public void Add(HillBuilding b)
        {
            buildings.Add(b);
            double r = Math.Sqrt(b.hl * b.hl + b.hd * b.hd) + Margin;
            for (double x = Math.Floor((b.c.x - r) / CellSize); x <= Math.Floor((b.c.x + r) / CellSize); x++)
                for (double z = Math.Floor((b.c.z - r) / CellSize); z <= Math.Floor((b.c.z + r) / CellSize); z++)
                {
                    long k = ((long)x << 32) ^ (uint)(int)z;
                    List<HillBuilding> l;
                    if (!cells.TryGetValue(k, out l)) cells[k] = l = new List<HillBuilding>();
                    l.Add(b);
                }
        }

        public string Summary()
        {
            List<string> parts = new List<string>();
            foreach (HillGrid g in grids) parts.Add(g.name + " " + g.top.ToString("F1") + " m (" + (g.steepest * 100).ToString("F0") + "%)");
            return string.Join(", ", parts.ToArray());
        }
    }

    public static partial class Generator
    {
        /// Rye Hill: Rye Common rises to a rounded top. `height` is the top's height above the city (metres).
        public static HillGrid RyeHill(GreyboxResult built, double height)
        {
            if (roads == null) throw new Exception("Run Build(data) first.");
            List<BuildingInfo> near = new List<BuildingInfo>();     // buildings stay level too (none should sit on a slope)
            foreach (BuildingInfo b in built.buildings)
                if (b.c.x > 1080 && b.c.x < 1620 && b.c.z > -20 && b.c.z < 380) near.Add(b);
            HillGrid g = new HillGrid { name = "Rye Hill", x0 = 1120, z0 = 20, cell = 2.0 };
            g.nx = (int)((1580 - g.x0) / g.cell) + 1; g.nz = (int)((340 - g.z0) / g.cell) + 1;
            g.h = new float[g.nx * g.nz];
            const double Start = 6, Full = 100;            // metres from the nearest pavement edge (a long, gentle rise)
            double[] clr = new double[g.nx * g.nz];
            for (int j = 0; j < g.nz; j++)
                for (int i = 0; i < g.nx; i++)
                {
                    double x = g.x0 + i * g.cell, z = g.z0 + j * g.cell;
                    V2 p = new V2(x, z);
                    double clear = double.MaxValue;
                    foreach (RoadInfo ri in roads.Values)
                    {
                        double c = ri.line.DistanceTo(p) - (ri.w / 2 + Math.Max(ri.pave, ri.verge));
                        if (c < clear) clear = c;
                        if (clear < Start) break;
                    }
                    foreach (BuildingInfo b in near)
                    {
                        if (clear < Start) break;
                        double dx = x - b.c.x, dz = z - b.c.z;
                        double u = Math.Abs(dx * b.dir.x + dz * b.dir.z) - b.len / 2, v = Math.Abs(-dx * b.dir.z + dz * b.dir.x) - b.dep / 2;
                        double c = Math.Sqrt(Math.Max(u, 0) * Math.Max(u, 0) + Math.Max(v, 0) * Math.Max(v, 0)) - 2;   // 2 m apron round each building
                        if (c < clear) clear = c;
                    }
                    clr[j * g.nx + i] = clear;
                }
            // only the common itself: the open ground joined to its middle without crossing a road (not the far side
            // of the Quay Bridge road, not the next block east)
            bool[] mine = new bool[g.nx * g.nz];
            Queue<int> todo = new Queue<int>();
            int seed = (int)((130 - g.z0) / g.cell) * g.nx + (int)((1350 - g.x0) / g.cell);
            if (clr[seed] > Start) { mine[seed] = true; todo.Enqueue(seed); }
            while (todo.Count > 0)
            {
                int c = todo.Dequeue(), ci = c % g.nx, cj = c / g.nx;
                for (int n = 0; n < 4; n++)
                {
                    int ni = ci + (n == 0 ? 1 : n == 1 ? -1 : 0), nj = cj + (n == 2 ? 1 : n == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= g.nx || nj >= g.nz) continue;
                    int q = nj * g.nx + ni;
                    if (mine[q] || clr[q] <= Start) continue;
                    mine[q] = true; todo.Enqueue(q);
                }
            }
            for (int j = 0; j < g.nz; j++)
                for (int i = 0; i < g.nx; i++)
                {
                    int q = j * g.nx + i;
                    if (!mine[q]) continue;
                    double x = g.x0 + i * g.cell;
                    double k = Smooth((clr[q] - Start) / (Full - Start));
                    k *= Smooth((x - 1160) / 120.0);           // the west end (Peckam Rye and its station) stays flat
                    g.h[q] = (float)(height * k);
                }
            // round off the crest (distance-from-the-road makes a sharp ridge line), twice over ±4 m; the road edges stay at 0
            for (int pass = 0; pass < 2; pass++)
            {
                float[] o = (float[])g.h.Clone();
                for (int j = 0; j < g.nz; j++)
                    for (int i = 0; i < g.nx; i++)
                    {
                        int q = j * g.nx + i;
                        if (!mine[q]) continue;
                        double sum = 0; int n = 0;
                        for (int dj = -2; dj <= 2; dj++)
                            for (int di = -2; di <= 2; di++)
                            {
                                int ii = i + di, jj = j + dj;
                                if (ii < 0 || jj < 0 || ii >= g.nx || jj >= g.nz) continue;
                                sum += o[jj * g.nx + ii]; n++;
                            }
                        g.h[q] = (float)(sum / n * Smooth((clr[q] - Start) / 8.0));
                    }
            }
            Finish(g);
            return g;
        }

        /// How high, how steep (so the menu can say), and where the ground rises.
        static void Finish(HillGrid g)
        {
            g.xMin = g.zMin = double.MaxValue; g.xMax = g.zMax = double.MinValue;
            for (int j = 0; j < g.nz; j++)
                for (int i = 0; i < g.nx; i++)
                {
                    double v = g.h[j * g.nx + i];
                    if (v > g.top) g.top = v;
                    if (v > 0.001)
                    {
                        double x = g.x0 + i * g.cell, z = g.z0 + j * g.cell;
                        g.xMin = Math.Min(g.xMin, x - g.cell); g.xMax = Math.Max(g.xMax, x + g.cell);
                        g.zMin = Math.Min(g.zMin, z - g.cell); g.zMax = Math.Max(g.zMax, z + g.cell);
                    }
                    if (i + 1 < g.nx) g.steepest = Math.Max(g.steepest, Math.Abs(g.h[j * g.nx + i + 1] - v) / g.cell);
                    if (j + 1 < g.nz) g.steepest = Math.Max(g.steepest, Math.Abs(g.h[(j + 1) * g.nx + i] - v) / g.cell);
                }
        }

        static double Smooth(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
    
        /// A hill with streets on it: `height` at the top, a flat top out to `rIn`, sloping down to nothing at the edge.
        /// The distance is measured across an ellipse (rx east-west, rz north-south, in metres at the edge). The slope is
        /// even in the middle and eases in and out over `ease` (a share of the slope's length). Then the streets are
        /// cut level across (run Build/BuildExpansion first, for its roads): a road and its pavements (nearly) take the height of
        /// the road's middle, and a 9 m bank either side meets the hill, so cars don't lean on a hillside.
        public static HillGrid StreetHill(string name, double cx, double cz, double rx, double rz, double height, double rIn, double ease)
        {
            HillGrid g = new HillGrid { name = name, cell = 2.0 };
            g.x0 = Math.Floor((cx - rx - 8) / 2) * 2; g.z0 = Math.Floor((cz - rz - 8) / 2) * 2;
            g.nx = (int)Math.Ceiling((2 * rx + 16) / 2) + 2; g.nz = (int)Math.Ceiling((2 * rz + 16) / 2) + 2;
            g.h = new float[g.nx * g.nz];
            double run = 1 - rIn;
            Func<double, double, double> hill = (x, z) =>
            {
                double dx = (x - cx) / rx, dz = (z - cz) / rz;
                return height * Ease(1 - Math.Sqrt(dx * dx + dz * dz), run, ease);
            };
            // the roads near the hill (with their half widths, pavements included)
            List<RoadInfo> near = new List<RoadInfo>();
            foreach (RoadInfo ri in roads.Values)
            {
                foreach (V2 q in ri.line.p)
                    if (Math.Abs(q.x - cx) < rx + 400 && Math.Abs(q.z - cz) < rz + 400) { near.Add(ri); break; }
            }
            List<JInfo> nearJ = new List<JInfo>();
            foreach (JInfo ji in juncs.Values)
                if (Math.Abs(ji.c.x - cx) < rx + 100 && Math.Abs(ji.c.z - cz) < rz + 100) nearJ.Add(ji);
            for (int j = 0; j < g.nz; j++)
                for (int i = 0; i < g.nx; i++)
                {
                    double x = g.x0 + i * g.cell, z = g.z0 + j * g.cell;
                    double h0 = hill(x, z);
                    // a junction keeps the hill's own (gently tilted) surface, so the streets meeting there agree;
                    // each street turns level across over the 12 m after it
                    double open = 1;
                    foreach (JInfo ji in nearJ)
                    {
                        double dj = V2.Dist(ji.c, new V2(x, z)) - (ji.radius + 2);
                        if (dj < 12) open = Math.Min(open, Smooth(dj / 12));
                    }
                    double wsum = 0, hsum = 0, wmax = 0;
                    V2 q = new V2(x, z);
                    foreach (RoadInfo ri in near)
                    {
                        double half = ri.w / 2 + Math.Max(ri.pave, ri.verge);
                        V2 on; double d = Nearest(ri.line, q, out on);
                        if (d >= half + Bank) continue;
                        double k = d <= half ? 1 : Smooth(1 - (d - half) / Bank);
                        wsum += k; hsum += k * hill(on.x, on.z); wmax = Math.Max(wmax, k);
                    }
                    double h = wsum > 0 ? h0 + (hsum / wsum - h0) * wmax * open * Level : h0;
                    g.h[j * g.nx + i] = (float)h;
                }
            Finish(g);
            return g;
        }

        const double Bank = 9;                                               // the bank between a level street and the hill
        const double Level = 0.75;                                           // how much of the hill's lean a street loses (a little camber stays)

        static double Nearest(Line line, V2 q, out V2 on)
        {
            double best = double.MaxValue; on = q;
            for (int i = 0; i + 1 < line.p.Count; i++)
            {
                V2 a = line.p[i], b = line.p[i + 1], ab = b - a;
                double l2 = V2.Dot(ab, ab);
                double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
                V2 c = a + ab * t;
                double d = V2.Dist(q, c);
                if (d < best) { best = d; on = c; }
            }
            return best;
        }

        /// 0 → 1 over [0, run]: an even slope in the middle, eased over `e` at each end (no kinks for cars).
        static double Ease(double d, double run, double e)
        {
            if (d <= 0) return 0;
            if (d >= run) return 1;
            double s = 1 / (run - e);                                    // the middle's slope
            if (d < e) return s * d * d / (2 * e);
            if (d > run - e) return 1 - s * (run - d) * (run - d) / (2 * e);
            return s * (d - e / 2);
        }

        /// Every hill (G7a Rye Hill, G7b Brockmere and Denmore Hills) and the buildings standing on them.
        public static HillSet Hills(LundunWorldData data, double ryeHeight)
        {
            HillSet set = new HillSet();
            GreyboxResult city = Build(data);
            set.grids.Add(RyeHill(city, ryeHeight));
            AddHillBuildings(set, city.buildings);                        // (the city's: none of them stand on Rye Hill)
            GreyboxResult brixtun = BuildExpansion(data);                   // roads are Brixtun's from here
            set.grids.Add(StreetHill("Brockmere Hill", 560, -810, 340, 340, 18, 0.08, 0.26));
            set.grids.Add(StreetHill("Denmore Hill", 965, -250, 230, 230, 12, 0.10, 0.30));
            if (brixtun != null) AddHillBuildings(set, brixtun.buildings);
            Build(data);                                                    // leave the generator on the city
            return set;
        }

        /// Each building that has rising ground under it (or within 2 m of it): level at its front door. The front is
        /// the long side nearer a road; the door's height is the highest point of the ground along it, less 30 cm (so
        /// the pavement never covers more than a step of its front, and the rest shows as a plinth).
        static void AddHillBuildings(HillSet set, List<BuildingInfo> list)
        {
            foreach (BuildingInfo bi in list)
            {
                HillBuilding b = new HillBuilding { c = bi.c, f = bi.dir.Norm, hl = bi.len / 2, hd = bi.dep / 2 };
                b.l = b.f.Left;
                double r = Math.Sqrt(b.hl * b.hl + b.hd * b.hd) + 2;
                bool near = false;                                           // (flat ones near a hill are kept too, at 0,
                foreach (HillGrid g in set.grids)                            //  so a mesh can be told to be buildings)
                    if (g.Touches(b.c.x - r - 150, b.c.z - r - 150, b.c.x + r + 150, b.c.z + r + 150)) near = true;
                if (!near) continue;
                if (!set.Rises(b.c.x - r, b.c.z - r, b.c.x + r, b.c.z + r)) { b.lift = b.low = 0; set.Add(b); continue; }
                // the lowest and highest ground under and round it
                double low = double.MaxValue, high = 0;
                for (double u = -b.hl - 1; u <= b.hl + 1.01; u += Math.Max(1, b.hl / 8))
                    for (double v = -b.hd - 1; v <= b.hd + 1.01; v += Math.Max(1, b.hd / 6))
                    {
                        V2 q = b.c + b.f * u + b.l * v;
                        double h = set.At(q.x, q.z);
                        low = Math.Min(low, h); high = Math.Max(high, h);
                    }
                if (high < 0.002) { b.lift = b.low = 0; set.Add(b); continue; }
                // which long side faces the street
                V2 sideA = b.c + b.l * b.hd, sideB = b.c - b.l * b.hd;
                double da = double.MaxValue, db = double.MaxValue;
                foreach (RoadInfo ri in roads.Values) { da = Math.Min(da, ri.line.DistanceTo(sideA)); db = Math.Min(db, ri.line.DistanceTo(sideB)); }
                double sign = da <= db ? 1 : -1;
                double door = double.MinValue;
                for (double u = -b.hl; u <= b.hl + 0.01; u += Math.Max(0.5, b.hl / 10))
                {
                    V2 q = b.c + b.f * u + b.l * (sign * (b.hd + 1.0));          // the pavement just in front
                    door = Math.Max(door, set.At(q.x, q.z));
                }
                b.lift = Math.Max(0, door - 0.3);
                b.low = Math.Min(low, b.lift);
                set.Add(b);
            }
        }

        /// Cuts big triangles into small ones (no edge longer than maxEdge) so a flat ground plane can bend over a hill.
        /// `data` is the vertices, `stride` floats each, the first three the position (world metres); every other float
        /// (normal, uv, colour, ...) is simply averaged for a new vertex. Only triangles (and pieces) `want` says yes to are cut. Each
        /// cut is at the middle of the longest edge (measured across the ground), and a shared edge is always cut at the same new vertex, so the
        /// pieces meet with no cracks. Returns the new triangle list; new vertices are added to `data`.
        public static List<int> Tessellate(List<float> data, int stride, List<int> tris, double maxEdge, Func<int, int, int, bool> want)
        {
            List<int> outTris = new List<int>(tris.Count);
            Dictionary<long, int> mids = new Dictionary<long, int>();
            Stack<int[]> todo = new Stack<int[]>();
            double max2 = maxEdge * maxEdge;
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                todo.Push(new[] { tris[t], tris[t + 1], tris[t + 2] });
                while (todo.Count > 0)
                {
                    int[] tr = todo.Pop();
                    // asked again of every piece, so a big triangle is only cut up where it's over the hill (where a cut
                    // piece meets an uncut one the ground is flat, so the extra vertex sits exactly on the uncut edge)
                    if (!want(tr[0], tr[1], tr[2])) { outTris.Add(tr[0]); outTris.Add(tr[1]); outTris.Add(tr[2]); continue; }
                    // the longest edge (k = the corner opposite it)
                    int k = -1; double best = max2;
                    for (int e = 0; e < 3; e++)
                    {
                        double d2 = Dist2(data, stride, tr[(e + 1) % 3], tr[(e + 2) % 3]);
                        if (d2 > best + 1e-9) { best = d2; k = e; }
                    }
                    if (k < 0) { outTris.Add(tr[0]); outTris.Add(tr[1]); outTris.Add(tr[2]); continue; }
                    int p = tr[(k + 1) % 3], q = tr[(k + 2) % 3], o = tr[k];
                    int m = Mid(data, stride, mids, p, q);
                    // (o, p, q) keeps its winding as (o, p, m) and (o, m, q)
                    todo.Push(new[] { o, p, m });
                    todo.Push(new[] { o, m, q });
                }
            }
            return outTris;
        }

        static double Dist2(List<float> d, int s, int a, int b)
        {
            double dx = d[a * s] - d[b * s], dz = d[a * s + 2] - d[b * s + 2];   // across the ground only: a tall upright edge
            return dx * dx + dz * dz;                                            // rises evenly, so it never needs cutting
        }

        static int Mid(List<float> d, int s, Dictionary<long, int> mids, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            int m;
            if (mids.TryGetValue(key, out m)) return m;
            m = d.Count / s;
            for (int i = 0; i < s; i++) d.Add((d[a * s + i] + d[b * s + i]) * 0.5f);
            mids[key] = m;
            return m;
        }
    }
}
