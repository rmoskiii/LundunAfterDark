// Pure C#: R9 Lundun Cycles — docking stations on the pavements (with parked bikes) and the hire bike's
// own meshes (frame + one wheel), all coloured from the dressing atlas.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class BikeDockInfo { public double x, z, alongX, alongZ, outX, outZ; }   // centre, along the kerb, towards the road

    public static partial class Generator
    {
        // ------------------------------------------------------------------ a bike, drawn in a local frame
        // a = along (forward), h = height, s = sideways (left positive)
        struct BikeFrame { public V2 c, f, l; public double y; }
        static P3 BP(BikeFrame b, double a, double h, double s, UVRect sw) { return new P3(b.c + b.f * a + b.l * s, b.y + h, sw.Cu, sw.Cv); }

        /// Square tube between two points.
        static void Strut(MeshData m, P3 a, P3 b, double r)
        {
            double dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z, L = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (L < 1e-6) return;
            dx /= L; dy /= L; dz /= L;
            // two perpendiculars
            double ux = -dz, uy = 0, uz = dx;
            double ul = Math.Sqrt(ux * ux + uz * uz);
            if (ul < 1e-6) { ux = 1; uy = 0; uz = 0; ul = 1; }
            ux /= ul; uz /= ul;
            double vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
            double[,] o = { { ux + vx, uy + vy, uz + vz }, { -ux + vx, -uy + vy, -uz + vz }, { -ux - vx, -uy - vy, -uz - vz }, { ux - vx, uy - vy, uz - vz } };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                P3 a0 = new P3(a.x + o[i, 0] * r, a.y + o[i, 1] * r, a.z + o[i, 2] * r, a.u, a.v), a1 = new P3(a.x + o[j, 0] * r, a.y + o[j, 1] * r, a.z + o[j, 2] * r, a.u, a.v);
                P3 b1 = new P3(b.x + o[j, 0] * r, b.y + o[j, 1] * r, b.z + o[j, 2] * r, a.u, a.v), b0 = new P3(b.x + o[i, 0] * r, b.y + o[i, 1] * r, b.z + o[i, 2] * r, a.u, a.v);
                double nx = (o[i, 0] + o[j, 0]), ny = (o[i, 1] + o[j, 1]), nz = (o[i, 2] + o[j, 2]);
                m.Quad(a0, a1, b1, b0, nx, ny, nz);
            }
        }

        /// Upright disc (wheel) facing sideways along l.
        static void Wheel(MeshData m, BikeFrame b, double a, double radius, double halfWidth, int sides, bool detail = true)
        {
            UVRect tyre = S("black"), hub = S("metal");
            for (int i = 0; i < sides; i++)
            {
                double t0 = i * Math.PI * 2 / sides, t1 = (i + 1) * Math.PI * 2 / sides;
                double a0 = a + Math.Cos(t0) * radius, h0 = radius + Math.Sin(t0) * radius;
                double a1 = a + Math.Cos(t1) * radius, h1 = radius + Math.Sin(t1) * radius;
                P3 p0 = BP(b, a0, h0, halfWidth, tyre), p1 = BP(b, a1, h1, halfWidth, tyre), q1 = BP(b, a1, h1, -halfWidth, tyre), q0 = BP(b, a0, h0, -halfWidth, tyre);
                double mid = (t0 + t1) / 2; V2 outA = b.f * Math.Cos(mid);
                m.Quad(p0, p1, q1, q0, outA.x, Math.Sin(mid), outA.z);
                if (!detail) continue;
                foreach (int s in new[] { -1, 1 })
                {
                    // spoked look: the outer ring is tyre, the inner disc is hub-coloured
                    double ri = radius * 0.82;
                    P3 o0 = BP(b, a0, h0, s * halfWidth, tyre), o1 = BP(b, a1, h1, s * halfWidth, tyre);
                    P3 i0 = BP(b, a + Math.Cos(t0) * ri, radius + Math.Sin(t0) * ri, s * halfWidth, tyre), i1 = BP(b, a + Math.Cos(t1) * ri, radius + Math.Sin(t1) * ri, s * halfWidth, tyre);
                    m.Quad(o0, o1, i1, i0, b.l.x * s, 0, b.l.z * s);
                    P3 c0 = BP(b, a, radius, s * halfWidth * 0.6, hub);
                    P3 j0 = BP(b, a + Math.Cos(t0) * ri, radius + Math.Sin(t0) * ri, s * halfWidth * 0.6, hub), j1 = BP(b, a + Math.Cos(t1) * ri, radius + Math.Sin(t1) * ri, s * halfWidth * 0.6, hub);
                    m.Quad(c0, j0, j1, j1, b.l.x * s, 0, b.l.z * s);
                }
            }
        }

        /// The frame (everything but the wheels): step-through hire bike with a front carrier.
        static void BikeFrameMesh(MeshData m, BikeFrame b)
        {
            UVRect teal = S("cycleteal"), yellow = S("cycleyellow"), black = S("black"), metal = S("metal");
            double r = 0.028;
            P3 head = BP(b, 0.40, 0.88, 0, teal), bb = BP(b, -0.02, 0.30, 0, teal), seatTop = BP(b, -0.14, 0.86, 0, teal);
            Strut(m, head, BP(b, 0.10, 0.38, 0, teal), r * 1.4);                 // down tube (step-through)
            Strut(m, BP(b, 0.10, 0.38, 0, teal), bb, r * 1.4);
            Strut(m, bb, seatTop, r * 1.2);                                         // seat tube
            foreach (int s in new[] { -1, 1 })
            {
                Strut(m, bb, BP(b, -0.52, 0.34, s * 0.06, teal), r);                // chain stays
                Strut(m, BP(b, -0.10, 0.70, s * 0.03, teal), BP(b, -0.52, 0.34, s * 0.06, teal), r);   // seat stays
                Strut(m, head, BP(b, 0.52, 0.34, s * 0.05, metal), r);              // fork
            }
            // mudguards (yellow stripe) and chain guard
            SBox(m, b.c + b.f * (-0.50), b.f, 0.30, 0.05, b.y + 0.66, b.y + 0.69, yellow);
            SBox(m, b.c + b.f * 0.50, b.f, 0.28, 0.05, b.y + 0.66, b.y + 0.69, teal);
            SBox(m, b.c + b.f * (-0.24) + b.l * (-0.07), b.f, 0.30, 0.02, b.y + 0.24, b.y + 0.40, teal);
            // seat, stem, handlebars, grips
            SBox(m, b.c + b.f * (-0.16), b.f, 0.14, 0.08, b.y + 0.88, b.y + 0.95, black);
            Strut(m, head, BP(b, 0.36, 1.06, 0, metal), r);
            Strut(m, BP(b, 0.34, 1.06, 0.30, metal), BP(b, 0.34, 1.06, -0.30, metal), r);
            foreach (int s in new[] { -1, 1 }) Strut(m, BP(b, 0.32, 1.06, s * 0.30, black), BP(b, 0.30, 1.06, s * 0.36, black), r * 1.3);
            // front carrier with the yellow "LC" flash
            SBox(m, b.c + b.f * 0.62, b.f, 0.13, 0.18, b.y + 0.80, b.y + 0.92, teal);
            SBox(m, b.c + b.f * 0.755, b.l, 0.12, 0.006, b.y + 0.82, b.y + 0.90, yellow);
            // pedals
            foreach (int s in new[] { -1, 1 }) SBox(m, b.c + b.f * (-0.02) + b.l * (s * 0.14), b.f, 0.06, 0.04, b.y + 0.28, b.y + 0.32, black);
        }

        static void ParkedBike(V2 c, V2 forward, double y)
        {
            BikeFrame b = new BikeFrame { c = c, f = forward.Norm, l = forward.Norm.Left, y = y };
            BikeFrameMesh(mSolid, b);
            Wheel(mSolid, b, 0.52, 0.33, 0.03, 8, false);
            Wheel(mSolid, b, -0.52, 0.33, 0.03, 8, false);
        }

        /// The hire bike for riding: frame at the origin facing +Z (x = right), and one wheel centred on the origin.
        public static void HireBikeMeshes(out MeshData frame, out MeshData wheel)
        {
            frame = new MeshData("HireBikeFrame", "Bikes", "DressSolid", false);
            wheel = new MeshData("HireBikeWheel", "Bikes", "DressSolid", false);
            // Unity: +Z forward, +X right. V2.Left of +Z (0,1) is (-1,0), i.e. left = -X. Good.
            BikeFrameMesh(frame, new BikeFrame { c = new V2(0, 0), f = new V2(0, 1), l = new V2(-1, 0), y = 0 });
            Wheel(wheel, new BikeFrame { c = new V2(0, 0), f = new V2(0, 1), l = new V2(-1, 0), y = -0.33 }, 0, 0.33, 0.025, 12);
        }

        // ------------------------------------------------------------------ docking stations
        static readonly double[,] DockAnchors = {
            { 700, 925 }, { 380, 925 }, { 790, 330 }, { 950, 900 }, { 560, 1000 }, { 330, 300 },
            { 1150, 760 }, { 1250, 545 }, { 640, 545 }, { 1000, 220 } };

        /// E3: Brixtun's docks (besides the one at Windward Square's Tube): by the station, and along the main roads.
        static readonly double[,] BrixtunDockAnchors = {
            { 442, -100 }, { 440, -150 }, { 620, -322 }, { 330, -308 }, { 262, -150 }, { 702, -240 }, { 560, -502 }, { 560, -700 }, { 840, -140 } };

        static void DressBikeDocks()
        {
            List<V2> anchors = new List<V2>();
            foreach (StationInfo st in D.stations) anchors.Add(new V2(st.x, st.z));      // one by every Underground station
            double[,] fixedAnchors = ExpansionMode ? BrixtunDockAnchors : DockAnchors;     // E3: Brixtun's own
            for (int i = 0; i < fixedAnchors.GetLength(0); i++) anchors.Add(new V2(fixedAnchors[i, 0], fixedAnchors[i, 1]));
            int made = 0;
            foreach (V2 anchor in anchors)
            {
                // nearest road with a decent pavement
                RoadInfo best = null; double bestD = 140, bestCh = 0;
                foreach (RoadInfo ri in roads.Values)
                {
                    if (ri.pave < 3 || ri.cls == "bridge") continue;
                    for (double c = 0; c <= ri.line.Length; c += 2)
                    {
                        V2 d; double dd = V2.Dist(ri.line.At(c, out d), anchor);
                        if (dd < bestD) { bestD = dd; best = ri; bestCh = c; }
                    }
                }
                if (best == null) continue;
                V2 d0; V2 q0 = best.line.At(bestCh, out d0);
                int side = V2.Dot(anchor - q0, d0.Left) >= 0 ? 1 : -1;
                List<double[]> ok = Stretches(best, side, 10, 12);
                bool placed = false;
                for (int k = 0; k < 80 && !placed; k++)
                {
                    double c = bestCh + (k % 2 == 0 ? 1 : -1) * (k / 2) * 2.0;
                    if (!Inside(c - 4.5, ok, 0) || !Inside(c + 4.5, ok, 0)) continue;
                    V2 f; V2 lc = best.line.At(c, out f);
                    V2 n = f.Left * side;                                     // away from the road
                    double back = best.w / 2 + best.pave - 0.35;
                    bool clear = true;
                    for (double a = -4.5; a <= 4.5; a += 1.5)
                    {
                        V2 q = lc + n * (back - 0.8) + f * a;
                        if (!OnClearPavement(q, best, 0.9)) { clear = false; break; }
                    }
                    if (!clear) continue;
                    BikeDock(lc + n * back, f, n * -1, best.pave);
                    placed = true; made++;
                }
            }
            D.Count("bike docks", made);
        }

        /// A row of docking posts along the back of the pavement, bikes pointing at the road, terminal at one end.
        static void BikeDock(V2 backCentre, V2 along, V2 toRoad, double pave)
        {
            UVRect teal = S("cycleteal"), grey = S("metal");
            int posts = 8;
            double bikeLen = Math.Min(1.8, pave - 0.9);
            for (int i = 0; i < posts; i++)
            {
                double a = -3.2 + i * 0.85;
                V2 p = backCentre + along * a + toRoad * 0.15;
                SBox(mSolid, p, toRoad, 0.12, 0.07, PaveY, PaveY + 0.62, grey);
                SBox(mSolid, p, toRoad, 0.13, 0.08, PaveY + 0.62, PaveY + 0.7, teal);
                if (i % 2 == 0) ParkedBike(p + toRoad * (0.2 + bikeLen / 2), toRoad, PaveY);
            }
            // terminal with the sign
            V2 t = backCentre + along * 4.1 + toRoad * 0.3;
            SBox(mSolid, t, along, 0.24, 0.16, PaveY, PaveY + 1.65, teal);
            SBox(mSolid, t, along, 0.95, 0.06, PaveY + 1.9, PaveY + 2.3, S("charcoal"));
            Cyl(mSolid, t, 0.05, PaveY + 1.6, PaveY + 1.95, 6, grey, false);
            VQuad(mGlow, t + toRoad * 0.07 - along * 0.9, t + toRoad * 0.07 + along * 0.9, PaveY + 1.93, PaveY + 2.27, A["sign_CYCLES"], toRoad);
            VQuad(mGlow, t - toRoad * 0.07 + along * 0.9, t - toRoad * 0.07 - along * 0.9, PaveY + 1.93, PaveY + 2.27, A["sign_CYCLES"], toRoad * -1);
            for (double a = -3.6; a <= 4.4; a += 1.2) Claim(backCentre + along * a + toRoad * 0.9, 1.1);
            D.bikeDocks.Add(new BikeDockInfo { x = backCentre.x, z = backCentre.z, alongX = along.x, alongZ = along.z, outX = toRoad.x, outZ = toRoad.z });
        }
    }
}
