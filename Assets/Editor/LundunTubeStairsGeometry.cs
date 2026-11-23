// G8e: walk-down Tube entrances (Lundun → 42). Pure maths like the rest of the generator: no UnityEngine types.
// Every Underground entrance (the dressing's own, menu 13, and the G6a ones, menu 38) was a pen with a picture of steps
// painted on the pavement; you got in with the TAKE THE TUBE button. Now each pen is a real stairwell, cut into the
// pavement: steps going down (about 3 m), a landing at the bottom, tiled walls, a lit way in. Walk down and through the
// way in and you're in the ticket hall (G5's walk-through station). The button and SKIP stay.
// This works out every pen exactly as 13 and 38 do, and gives the builder: the stairwells (meshes, colliders and the
// trigger at the bottom) and, for each, the rectangle of pavement to cut away (Generator.CutRect does the cutting).
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    /// A box collider for the builder: centre, size (across, up, along), turned `yaw` degrees and tipped `pitch` degrees
    /// (nose down) about its own across axis.
    public class StairBox { public string name; public double cx, cy, cz, sx, sy, sz, yaw, pitch; public bool trigger; }

    /// One entrance: where to cut, and the station's name (the trigger reports it).
    public class StairHole
    {
        public string name; public V2 c, f; public double u0, u1, hw, y;      // the cut: u along f from c, v across ±hw
        public double depth; public bool fromDressing;                         // dressing pens had a solid box over them
    }

    public class TubeStairsResult
    {
        public List<StairHole> holes = new List<StairHole>();
        public List<StairBox> boxes = new List<StairBox>();
        public List<MeshData> meshes = new List<MeshData>();
        public List<string> log = new List<string>();
    }

    public static partial class Generator
    {
        public const string StairTileMat = "StairTile", StairStepMat = "StairStep", StairNosingMat = "StairNosing",
            StairDarkMat = "StairDark", StairGlowMat = "StairGlow";

        /// Every entrance, from the city's, Brixtun's and Kingsmere's dressing (and the G6a ones if `withNew`).
        public static TubeStairsResult TubeStairs(LundunWorldData data, bool withNew, bool withExpansions)
        {
            TubeStairsResult R = new TubeStairsResult();
            MeshData tile = new MeshData("StairTiles", "TubeStairs", StairTileMat, false) { navIgnore = true };
            MeshData step = new MeshData("StairSteps", "TubeStairs", StairStepMat, false) { navIgnore = true };
            MeshData nose = new MeshData("StairNosing", "TubeStairs", StairNosingMat, false) { navIgnore = true };
            MeshData dark = new MeshData("StairDark", "TubeStairs", StairDarkMat, false) { navIgnore = true };
            MeshData glow = new MeshData("StairGlow", "TubeStairs", StairGlowMat, false) { navIgnore = true };
            MeshData[] ms = { tile, step, nose, dark, glow };

            TubeEntranceResult tr = new TubeEntranceResult();
            GreyboxResult w = Build(data);
            DressingResult d = Dress(data, w);
            foreach (StationPen p in d.stationPens) Stairwell(R, ms, p.name, p.c, p.f, p.hl, p.hw, p.y, true);
            if (withNew) PlaceTubeEntrances(tr, 0, w.meshes, d.meshes);
            if (withExpansions)
            {
                try
                {
                    Build(data);
                    GreyboxResult x = BuildExpansion(data);
                    if (x != null && x.meshes.Count > 0)
                    {
                        DressingResult dx = Dress(ExpansionWorld, x);
                        foreach (StationPen p in dx.stationPens) Stairwell(R, ms, p.name, p.c, p.f, p.hl, p.hw, p.y, true);
                        if (withNew) PlaceTubeEntrances(tr, 1, x.meshes, dx.meshes);
                    }
                }
                catch (Exception e) { R.log.Add("Brixtun skipped: " + e.Message); }
                try
                {
                    Build(data);
                    GreyboxResult k = BuildKingsmere(data);
                    if (k != null && k.meshes.Count > 0)
                    {
                        DressingResult dk = Dress(ExpansionWorld, k);
                        foreach (StationPen p in dk.stationPens) Stairwell(R, ms, p.name, p.c, p.f, p.hl, p.hw, p.y, true);
                        if (withNew) PlaceTubeEntrances(tr, 2, k.meshes, dk.meshes);
                    }
                }
                catch (Exception e) { R.log.Add("Kingsmere skipped: " + e.Message); }
            }
            Build(data);                                                        // leave the generator on the city
            const double HL = 4.5, HW = 2.4;                                    // the G6a pen (LundunTubeLinesGeometry)
            foreach (TubeEntrance e in tr.entrances)
            {
                V2 f = e.f.Norm;
                Stairwell(R, ms, e.name, e.open + f * HL, f, HL, HW, e.y, false);
            }
            foreach (MeshData m in ms) if (m.tri.Count > 0) R.meshes.Add(m);
            R.log.Add(R.holes.Count + " entrances (" + R.holes.FindAll(h => h.fromDressing).Count + " from the dressing, "
                + R.holes.FindAll(h => !h.fromDressing).Count + " from G6a)");
            return R;
        }

        /// A stairwell in the pen centred c, its open end at c - f·hl, walls inside ±(hw - 0.4) and at hl - 0.4.
        static void Stairwell(TubeStairsResult R, MeshData[] ms, string name, V2 c, V2 f, double hl, double hw, double y, bool fromDressing)
        {
            MeshData tile = ms[0], step = ms[1], nose = ms[2], dark = ms[3], glow = ms[4];
            V2 l = f.Left;
            double inW = hw - 0.4, uTop = -hl, uEnd = hl - 0.4;
            const double Landing = 1.6;
            double run = uEnd - Landing - uTop;
            if (run < 3) { R.log.Add(name + ": too short for steps, skipped"); return; }
            double D = Math.Max(2.0, Math.Min(3.0, run * 0.62));
            int n = Math.Max(8, (int)Math.Round(D / 0.18));
            double rise = D / n, tread = run / n, floor = y - D;
            Func<double, double, V2> at = (u, v) => c + f * u + l * v;

            // the steps: each a tread with a yellow edge, and the riser in front of it (the first riser is the cut's edge)
            for (int k = 0; k < n; k++)
            {
                double ua = uTop + tread * k, ub = ua + tread, top = y - rise * (k + 1), up = y - rise * k;
                Flat(step, at(ua + 0.05, -inW), at(ub, -inW), at(ub, inW), at(ua + 0.05, inW), top);
                Flat(nose, at(ua, -inW), at(ua + 0.05, -inW), at(ua + 0.05, inW), at(ua, inW), top + 0.002);
                Upright(step, at(ua, inW), at(ua, -inW), top, up, f);                    // the riser, facing up the steps
            }
            // the landing, and its floor carried under the far wall
            Flat(step, at(uEnd - Landing, -inW), at(uEnd, -inW), at(uEnd, inW), at(uEnd - Landing, inW), floor);
            // tiled side walls, from the landing to the pavement (they meet the pen's own walls above)
            foreach (int s in new[] { -1, 1 })
                Upright(tile, at(uTop, s * inW), at(uEnd, s * inW), floor - 0.05, y + 0.01, l * -s);
            // the far wall, with the way in: a dark doorway and a lit sign over it
            double door = 0.9, doorTop = floor + 2.4;
            Upright(tile, at(uEnd, inW), at(uEnd, door), floor - 0.05, y + 0.01, f * -1);
            Upright(tile, at(uEnd, -door), at(uEnd, -inW), floor - 0.05, y + 0.01, f * -1);
            Upright(tile, at(uEnd, door), at(uEnd, -door), doorTop, y + 0.01, f * -1);
            Upright(dark, at(uEnd + 0.01, door), at(uEnd + 0.01, -door), floor, doorTop, f * -1);
            Upright(glow, at(uEnd - 0.03, 0.75), at(uEnd - 0.03, -0.75), doorTop + 0.05, Math.Min(doorTop + 0.4, y - 0.05), f * -1);
            // two lamps down the side walls
            foreach (int s in new[] { -1, 1 })
            {
                double um = uTop + run * 0.55;
                Upright(glow, at(um - 0.5, s * (inW - 0.02)), at(um + 0.5, s * (inW - 0.02)), y - 0.55, y - 0.4, l * -s);
            }

            // colliders: the steps as one slope, the landing, walls all round (below the pavement and up to the
            // railings), and the way in (a trigger just in front of the doorway)
            double a = Math.Atan2(D, run), len = Math.Sqrt(run * run + D * D), th = 0.3;
            V2 mid = at(uTop + run / 2, 0);
            double yaw = Yaw(f);
            // the slope: its top face half a step below the line of the step edges (so feet sit on the treads)
            double drop = rise / 2 + th / 2;
            R.boxes.Add(new StairBox { name = "TubeStairs", cx = mid.x - f.x * Math.Sin(a) * drop, cy = y - D / 2 - Math.Cos(a) * drop, cz = mid.z - f.z * Math.Sin(a) * drop,
                sx = inW * 2, sy = th, sz = len, yaw = yaw, pitch = a * 180 / Math.PI });
            V2 lm = at(uEnd - Landing / 2, 0);
            R.boxes.Add(new StairBox { name = "TubeLanding", cx = lm.x, cy = floor - 0.15, cz = lm.z, sx = inW * 2, sy = 0.3, sz = Landing + 0.2, yaw = yaw });
            double wallLo = floor - 0.4, wallHi = y + 1.4;
            foreach (int s in new[] { -1, 1 })
            {
                V2 wc = at((uTop + uEnd) / 2, s * (inW + 0.15));
                R.boxes.Add(new StairBox { name = "TubeStairWall", cx = wc.x, cy = (wallLo + wallHi) / 2, cz = wc.z, sx = 0.3, sy = wallHi - wallLo, sz = uEnd - uTop, yaw = yaw });
            }
            V2 fc = at(uEnd + 0.15, 0);
            R.boxes.Add(new StairBox { name = "TubeStairWall", cx = fc.x, cy = (wallLo + wallHi) / 2, cz = fc.z, sx = inW * 2 + 0.6, sy = wallHi - wallLo, sz = 0.3, yaw = yaw });
            V2 tc = at(uEnd - 0.7, 0);
            R.boxes.Add(new StairBox { name = "TubeWayIn|" + name, cx = tc.x, cy = floor + 1.1, cz = tc.z, sx = door * 2, sy = 2.0, sz = 1.0, yaw = yaw, trigger = true });

            R.holes.Add(new StairHole { name = name, c = c, f = f, u0 = uTop, u1 = uEnd, hw = inW, y = y, depth = D, fromDressing = fromDressing });
        }

        /// A flat quad at height y (corners in order), facing up.
        static void Flat(MeshData m, V2 a, V2 b, V2 c, V2 d, double y)
        {
            m.Quad(new P3(a, y, 0, 0), new P3(b, y, 0, 0), new P3(c, y, 0, 0), new P3(d, y, 0, 0), 0, 1, 0);
        }

        /// An upright quad from a to b, between y0 and y1, facing `n`.
        static void Upright(MeshData m, V2 a, V2 b, double y0, double y1, V2 n)
        {
            if (y1 <= y0 + 1e-4) return;
            m.Quad(new P3(a, y0, 0, 0), new P3(b, y0, 0, 0), new P3(b, y1, 0, 0), new P3(a, y1, 0, 0), n.x, 0, n.z);
        }

        // ------------------------------------------------------------------ cutting a rectangle out of a mesh
        /// Cuts the rectangle (u0..u1 along f from c, ±hw across) out of every triangle `eligible` says yes to: a
        /// triangle across its edge keeps exactly the part outside (new vertices are added to `data`, `stride` floats
        /// each, the first three the world position; the rest are blended). Returns the new triangle list, and whether
        /// anything was cut.
        public static List<int> CutRect(List<float> data, int stride, List<int> tris, V2 c, V2 f, double u0, double u1, double hw,
            Func<int, int, int, bool> eligible, out bool cut)
        {
            cut = false;
            V2 l = f.Left;
            List<int> outTris = new List<int>(tris.Count);
            // the four outsides: u < u0, u > u1, v < -hw, v > hw, as (a·p + b ≥ 0 means inside the rectangle's side)
            double[][] planes =
            {
                new[] { f.x, f.z, -(V2.Dot(c, f) + u0) },          // u - u0 ≥ 0
                new[] { -f.x, -f.z, V2.Dot(c, f) + u1 },           // u1 - u ≥ 0
                new[] { l.x, l.z, -(V2.Dot(c, l) - hw) },          // v + hw ≥ 0
                new[] { -l.x, -l.z, V2.Dot(c, l) + hw },           // hw - v ≥ 0
            };
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], cc = tris[t + 2];
                if (!eligible(a, b, cc)) { outTris.Add(a); outTris.Add(b); outTris.Add(cc); continue; }
                // all inside every side: gone; all outside any one side: kept whole
                bool keepWhole = false, allIn = true;
                foreach (double[] pl in planes)
                {
                    double sa = Side(data, stride, a, pl), sb = Side(data, stride, b, pl), sc = Side(data, stride, cc, pl);
                    if (sa <= 1e-6 && sb <= 1e-6 && sc <= 1e-6) { keepWhole = true; break; }
                    if (sa < -1e-6 || sb < -1e-6 || sc < -1e-6) allIn = false;
                }
                if (keepWhole) { outTris.Add(a); outTris.Add(b); outTris.Add(cc); continue; }
                cut = true;
                if (allIn) continue;
                // peel off the part outside each side in turn; what's left goes on to the next side
                List<int> rest = new List<int> { a, b, cc };
                foreach (double[] pl in planes)
                {
                    if (rest.Count < 3) break;
                    List<int> outside = ClipPoly(data, stride, rest, pl, false);
                    rest = ClipPoly(data, stride, rest, pl, true);
                    for (int i = 1; i + 1 < outside.Count; i++) { outTris.Add(outside[0]); outTris.Add(outside[i]); outTris.Add(outside[i + 1]); }
                }
            }
            return outTris;
        }

        static double Side(List<float> d, int s, int i, double[] pl) { return pl[0] * d[i * s] + pl[1] * d[i * s + 2] + pl[2]; }

        /// The part of a convex polygon on one side of a plane (keepInside: where Side ≥ 0), in the same order.
        static List<int> ClipPoly(List<float> d, int s, List<int> poly, double[] pl, bool keepInside)
        {
            List<int> outp = new List<int>();
            for (int i = 0; i < poly.Count; i++)
            {
                int p = poly[i], q = poly[(i + 1) % poly.Count];
                double sp = Side(d, s, p, pl), sq = Side(d, s, q, pl);
                if (!keepInside) { sp = -sp; sq = -sq; }
                bool pin = sp >= 0, qin = sq >= 0;
                if (pin) outp.Add(p);
                if (pin != qin)
                {
                    double t = sp / (sp - sq);
                    int m = d.Count / s;
                    for (int k = 0; k < s; k++) d.Add((float)(d[p * s + k] + (d[q * s + k] - d[p * s + k]) * t));
                    outp.Add(m);
                }
            }
            return outp;
        }
    }
}
