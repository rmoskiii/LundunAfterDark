// G7a: the hills' shape (Lundun → 40). Pure maths like the rest of the generator: no UnityEngine types.
// A hill is a height above the flat city that rises away from every road and building (so streets, kerbs, junctions
// and front doors stay exactly where they are) and only inside its own area. RYE HILL: Rye Common, east of
// Peckam Rye: it starts rising 6 m in from the pavements round it, and is fully up about 70 m from them.
// Run Build(data) first (it needs the roads); the result is a grid you can ask for the height anywhere.
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
            // how high, how steep (checked here so the menu can say), and where it rises
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
            return g;
        }

        static double Smooth(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
    
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
