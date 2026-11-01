// E6: FLATPAK Home Store's layout (see LundunHomeStore.cs for the overview and the atlas).
// Store coordinates: u east from the west wall's outside (x = x0 + u), v north from the front wall's outside
// (z = z0 + v). The footprint is 165 × 110; the doors are at u 40 in the front (south) wall.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        static double hx0, hz0;
        static V2 Q(double u, double v) { return new V2(hx0 + u, hz0 + v); }
        static readonly V2 EU = new V2(1, 0), EV = new V2(0, 1);
        static MeshData hShell, hFloor, hConc, hCeil, hGlass, hWalk, hd, hLights0, hLights1, hGlass0, hGlass1;
        static int hLevel;                    // 0 = ground floor, 1 = first floor: where detail goes
        static Random hr;
        static double HFl { get { return hLevel == 0 ? HY0 : HY1; } }
        static MeshData HLights { get { return hLevel == 0 ? hLights0 : hLights1; } }
        static MeshData HGlassD { get { return hLevel == 0 ? hGlass0 : hGlass1; } }
        static readonly List<double> spotY = new List<double>();

        const double HW = 165, HD = 110, Hw = 0.35;       // footprint, wall thickness
        const double DoorU = 40;

        // ------------------------------------------------------------------ small helpers
        /// A detail mesh for the current floor.
        static MeshData HPart(string name)
        {
            MeshData m = new MeshData(si.id + "_" + name + "_L" + hLevel, "Shop", HomeAtlasMat, false) { navIgnore = true };
            (hLevel == 0 ? si.detail : si.upper).Add(m);
            return m;
        }

        /// An axis-aligned box in store coordinates (absolute heights).
        static void HB(MeshData m, double u0, double u1, double v0, double v1, double y0, double y1, string sw, bool bottom = false)
        {
            SBox(m, Q((u0 + u1) / 2, (v0 + v1) / 2), EU, Math.Abs(u1 - u0) / 2, Math.Abs(v1 - v0) / 2, y0, y1, HA("s_" + sw), bottom);
        }

        static void HCol(string name, double u0, double u1, double v0, double v1, double y0, double y1)
        {
            V2 c = Q((u0 + u1) / 2, (v0 + v1) / 2);
            si.colliders.Add(new DressBox { name = name, cx = c.x, cy = (y0 + y1) / 2, cz = c.z, sx = Math.Abs(u1 - u0), sy = Math.Abs(y1 - y0), sz = Math.Abs(v1 - v0), yaw = 0 });
        }

        static void HWallNote(double u0, double v0, double u1, double v1, double y0, double y1)
        {
            V2 a = Q(u0, v0), b = Q(u1, v1);
            si.walls.Add(new[] { a.x, a.z, b.x, b.z, y0, y1 });
        }

        /// A sign board (both faces), centred at (u, v), running along `along`, w wide, its bottom at y.
        static void HSign(MeshData m, double u, double v, V2 along, double w, double h, double y, string tile, bool rods = true)
        {
            V2 c = Q(u, v), n = along.Left;
            foreach (int k in new[] { -1, 1 })
                VQuad(m, c + along * (k * w / 2) + n * (k * 0.02), c - along * (k * w / 2) + n * (k * 0.02), y, y + h, HA(tile), n * k);
            double top = hLevel == 0 && (u > 58 || v < 30) ? HC0 : HC1;          // up to the ceiling (the slab, downstairs)
            if (rods && top > y + h + 0.05) foreach (int k in new[] { -1, 1 }) SBox(m, c + along * (k * w * 0.4), along, 0.015, 0.015, y + h, top, HA("s_steel"));
        }

        static void TwoSidedV(MeshData m, V2 a, V2 b, double y0, double y1, UVRect r)
        {
            V2 n = (b - a).Norm.Left;
            VQuad(m, a, b, y0, y1, r, n); VQuad(m, a, b, y0, y1, r, n * -1);
        }

        /// A box along a 3D line (a at ya → b at yb), hw across (horizontal), hh up and down.
        static void Beam(MeshData m, V2 a, double ya, V2 b, double yb, double hw, double hh, UVRect sw)
        {
            V2 f = (b - a).Norm, l = f.Left; double u = sw.Cu, v = sw.Cv;
            P3 a0 = new P3(a - l * hw, ya - hh, u, v), a1 = new P3(a + l * hw, ya - hh, u, v), a2 = new P3(a + l * hw, ya + hh, u, v), a3 = new P3(a - l * hw, ya + hh, u, v);
            P3 b0 = new P3(b - l * hw, yb - hh, u, v), b1 = new P3(b + l * hw, yb - hh, u, v), b2 = new P3(b + l * hw, yb + hh, u, v), b3 = new P3(b - l * hw, yb + hh, u, v);
            m.Quad(a3, a2, b2, b3, 0, 1, 0); m.Quad(a0, a1, b1, b0, 0, -1, 0);
            m.Quad(a1, b1, b2, a2, l.x, 0, l.z); m.Quad(a0, b0, b3, a3, -l.x, 0, -l.z);
            m.Quad(a0, a1, a2, a3, -f.x, 0, -f.z); m.Quad(b0, b1, b2, b3, f.x, 0, f.z);
        }

        /// Rectangles (u0, u1, v0, v1) with holes cut out of them.
        static List<double[]> CutRects(List<double[]> rs, List<double[]> holes)
        {
            List<double[]> cur = new List<double[]>(rs);
            foreach (double[] h in holes)
            {
                List<double[]> next = new List<double[]>();
                foreach (double[] r in cur)
                {
                    if (h[1] <= r[0] || h[0] >= r[1] || h[3] <= r[2] || h[2] >= r[3]) { next.Add(r); continue; }
                    double u0 = Math.Max(r[0], h[0]), u1 = Math.Min(r[1], h[1]);
                    if (h[0] > r[0]) next.Add(new[] { r[0], u0, r[2], r[3] });
                    if (h[1] < r[1]) next.Add(new[] { u1, r[1], r[2], r[3] });
                    if (h[2] > r[2]) next.Add(new[] { u0, u1, r[2], h[2] });
                    if (h[3] < r[3]) next.Add(new[] { u0, u1, h[3], r[3] });
                }
                cur = next;
            }
            return cur;
        }

        static void FloorRect(MeshData m, double[] r, double y, double tile, bool up)
        {
            TiledQuad(m, Q(r[0], r[2]), Q(r[1], r[2]), Q(r[1], r[3]), Q(r[0], r[3]), y, tile, up);
        }

        // ================================================================== the store
        static ShopInterior BuildHomeStore(Store s)
        {
            if (s.front != -1) throw new Exception("FLATPAK's layout expects its doors on the south side.");
            HomeAtlas();
            hx0 = s.x0; hz0 = s.z0;
            si = new ShopInterior { id = s.id, name = s.name, x0 = s.x0, x1 = s.x1, z0 = s.z0, z1 = s.z1, doorX = s.Door.x, doorZ = s.Door.z, outX = s.Out.x, outZ = s.Out.z,
                                    ceiling = HC1, upperY = HY1, insideTop = HC1 + 1 };
            si.crowd.name = s.name;
            hr = new Random(6006);
            srand = hr;
            spotY.Clear();
            hShell = new MeshData("Shell_" + s.id, "Shop", HomeAtlasMat, true) { navArea = 1 };
            hFloor = new MeshData("ShellFloor_" + s.id, "Shop", ShopFloorMat, true) { navArea = PavementArea };
            hConc = new MeshData("ShellConcrete_" + s.id, "Shop", HomeConcreteMat, true) { navArea = PavementArea };
            hCeil = new MeshData("ShellCeiling_" + s.id, "Shop", ShopCeilingMat, false) { navIgnore = true };
            hGlass = new MeshData("ShellGlass_" + s.id, "Shop", ShopGlassMat, false) { navIgnore = true };
            hWalk = new MeshData("Ramp_" + s.id, "Shop", HomeAtlasMat, true) { navArea = PavementArea };
            si.shell.Add(hShell); si.shell.Add(hFloor); si.shell.Add(hConc); si.shell.Add(hCeil); si.shell.Add(hGlass);
            si.walk.Add(hWalk);
            hLights0 = new MeshData("Lights_" + s.id + "_L0", "Shop", LampHeadMats[3], false) { navIgnore = true };
            hLights1 = new MeshData("Lights_" + s.id + "_L1", "Shop", LampHeadMats[3], false) { navIgnore = true };
            hGlass0 = new MeshData("Glass_" + s.id + "_L0", "Shop", ShopGlassMat, false) { navIgnore = true };
            hGlass1 = new MeshData("Glass_" + s.id + "_L1", "Shop", ShopGlassMat, false) { navIgnore = true };
            si.detail.Add(hLights0); si.detail.Add(hGlass0); si.upper.Add(hLights1); si.upper.Add(hGlass1);

            HomeShell(s);
            hLevel = 0;
            HomeLobbies(s);
            HomeCheckouts();
            HomeBistro();
            HomeFrontEast();
            HomeMarket();
            HomeWarehouse();
            hLevel = 1;
            HomeShowroom();
            HomeCafe();
            hLevel = 0;
            HomeCrowd();
            // the sliding doors at the front (as the supermarkets')
            ShopFrame F = new ShopFrame { x0 = s.x0, W = HW, D = HD, front = -1, frontZ = s.z0, uDoor = s.door - s.x0 };
            Doors(F, s);

            if (homeMissing.Count > 0) si.log.Add("FLATPAK atlas is missing: " + string.Join(", ", new List<string>(homeMissing).ToArray()));
            int q0 = 0, q1 = 0; foreach (MeshData m in si.detail) q0 += m.tri.Count / 6; foreach (MeshData m in si.upper) q1 += m.tri.Count / 6;
            si.log.Add(string.Format("{0}: two floors, {1} room sets, {2} warehouse aisles, {3} breakables, {4} items, {5} walk points, {6} staff, {7} escalators, {8} lifts, about {9} + {10} quads of detail",
                s.name, roomCount, 8, si.breakables.Count, si.items.Count, si.crowd.nodes.Count, si.crowd.traders.Count, si.escalators.Count, si.lifts.Count, q0, q1));
            return si;
        }

        // ================================================================== the shell: floors, the slab, walls, escalators, lifts
        static readonly double[] WellB = { 148.7, 151.3, 92.0, 97.5 };

        static void HomeShell(Store s)
        {
            double vTopA = 8 + EscRun;
            double[] wellA = { 11.4, 19.4, 13.0, vTopA };
            List<double[]> holes = new List<double[]> { wellA, WellB };
            // ---- floors: the ground floor (tiles; the warehouse concrete), the first floor (concrete), the slab's underside
            FloorRect(hFloor, new[] { Hw, HW - Hw, Hw, 30.15 }, HY0, 2.4, true);
            FloorRect(hFloor, new[] { 57.85, HW - Hw, 30.15, HD - Hw }, HY0, 2.4, true);
            FloorRect(hConc, new[] { Hw, 57.85, 30.15, HD - Hw }, HY0, 6.0, true);
            FloorRect(hFloor, new[] { 30.2, 49.8, -0.05, Hw }, HY0, 2.4, true);                     // in the glazed front
            List<double[]> slab = CutRects(new List<double[]> { new[] { Hw, HW - Hw, Hw, 30.0 }, new[] { 58.0, HW - Hw, 30.0, HD - Hw } }, holes);
            foreach (double[] r in slab) { FloorRect(hConc, r, HY1, 6.0, true); FloorRect(hCeil, r, HC0, 2.4, false); }
            // the roof inside: dark and open (ducts and the lights hang below it), over the showroom and the warehouse alike
            foreach (double[] r in new[] { new[] { Hw, HW - Hw, Hw, 30.0 }, new[] { 58.0, HW - Hw, 30.0, HD - Hw }, new[] { Hw, 58.0, 30.0, HD - Hw } })
            {
                UVRect dk = HA("s_dark");
                V2 a = Q(r[0], r[2]), b = Q(r[1], r[2]), c = Q(r[1], r[3]), d = Q(r[0], r[3]);
                hShell.Quad(new P3(a, HC1, dk.Cu, dk.Cv), new P3(b, HC1, dk.Cu, dk.Cv), new P3(c, HC1, dk.Cu, dk.Cv), new P3(d, HC1, dk.Cu, dk.Cv), 0, -1, 0);
            }
            // ---- the outer walls, inside: the shop's (cream, an orange band; the slab line; white upstairs), the warehouse's (grey)
            PWall(Q(Hw, Hw), Q(30.2, Hw), EV, false, HY0);
            PWall(Q(30.2, Hw), Q(49.8, Hw), EV, false, 5.9);
            PWall(Q(49.8, Hw), Q(HW - Hw, Hw), EV, false, HY0);
            PWall(Q(HW - Hw, Hw), Q(HW - Hw, HD - Hw), new V2(-1, 0), false, HY0);
            PWall(Q(HW - Hw, HD - Hw), Q(58, HD - Hw), new V2(0, -1), false, HY0);
            PWall(Q(58, HD - Hw), Q(Hw, HD - Hw), new V2(0, -1), true, HY0);
            PWall(Q(Hw, HD - Hw), Q(Hw, 30.0), new V2(1, 0), true, HY0);
            PWall(Q(Hw, 30.0), Q(Hw, Hw), new V2(1, 0), false, HY0);
            // the glazed front's reveals and head
            foreach (int k in new[] { -1, 1 })
            {
                double u = DoorU + k * 9.8;
                VQuad(hShell, Q(u, -0.02), Q(u, Hw), HY0, 5.9, HA("s_light"), new V2(-k, 0));
            }
            {
                UVRect lt = HA("s_light");
                hShell.Quad(new P3(Q(30.2, -0.02), 5.9, lt.Cu, lt.Cv), new P3(Q(49.8, -0.02), 5.9, lt.Cu, lt.Cv), new P3(Q(49.8, Hw), 5.9, lt.Cu, lt.Cv), new P3(Q(30.2, Hw), 5.9, lt.Cu, lt.Cv), 0, -1, 0);
            }
            Flip(hShell);
            // glass inside the glazing (the outer panes are seen from outside only)
            foreach (double[] g in new[] { new[] { 30.2, DoorU - DoorHalf, HY0 }, new[] { DoorU + DoorHalf, 49.8, HY0 }, new[] { DoorU - DoorHalf, DoorU + DoorHalf, DoorTop + 0.15 } })
                VQuad(hGlass, Q(g[0], 0.0), Q(g[1], 0.0), g[2], 5.9, new UVRect(0.5, 0.5, 0.5, 0.5), EV);
            si.walls.Add(new[] { Q(Hw, Hw).x, Q(Hw, Hw).z, Q(DoorU - DoorHalf, Hw).x, Q(DoorU - DoorHalf, Hw).z, HY0, HC1 });
            si.walls.Add(new[] { Q(DoorU + DoorHalf, Hw).x, Q(DoorU + DoorHalf, Hw).z, Q(HW - Hw, Hw).x, Q(HW - Hw, Hw).z, HY0, HC1 });

            // ---- the warehouse's walls (full height; the way out at the front, the way in from the market hall)
            HB(hShell, Hw, 26, 29.85, 30.15, HY0, HC1, "light"); HWallNote(Hw, 30, 26, 30, HY0, HC1);
            HB(hShell, 26, 58.15, 29.85, 30.15, 5.5, HC1, "light");
            HB(hShell, 57.85, 58.15, 30.15, 32, HY0, HC1, "light"); HWallNote(58, 30, 58, 32, HY0, HC1);
            HB(hShell, 57.85, 58.15, 40, HD - Hw, HY0, HC1, "light"); HWallNote(58, 40, 58, HD - Hw, HY0, HC1);
            HB(hShell, 57.85, 58.15, 32, 40, 4.5, HC1, "light");
            HWallNote(26, 30, 58, 30, 5.5, HC1); HWallNote(58, 32, 58, 40, 4.5, HC1);
            HWallNote(Hw, 30, 58, 30, HY1, HC1);
            // the slab's edges round the wells
            foreach (double[] w in holes)
            {
                UVRect dk = HA("s_dark");
                V2 a = Q(w[0], w[2]), b = Q(w[1], w[2]), c = Q(w[1], w[3]), d = Q(w[0], w[3]);
                VQuad(hShell, a, b, HC0, HY1, dk, EV); VQuad(hShell, b, c, HC0, HY1, dk, new V2(-1, 0));
                VQuad(hShell, c, d, HC0, HY1, dk, new V2(0, -1)); VQuad(hShell, d, a, HC0, HY1, dk, EU);
            }

            // ---- escalators, stairs, lifts
            Escalator("A", 18.0, 8.0, 1, false, true);           // up from the entrance to the showroom
            Escalator("Stairs", 13.0, 8.0, 1, true, true);       // and stairs beside it (both ways)
            Escalator("B", 150.0, 92.0 + EscRun, -1, false, false);   // down from the café to the market hall
            // the wells' balustrades (glass, a rail; solid to walk into)
            Balustrade(wellA[0], wellA[2], wellA[0], wellA[3]);
            Balustrade(wellA[1], wellA[2], wellA[1], wellA[3]);
            Balustrade(wellA[0], wellA[2], wellA[1], wellA[2]);
            Balustrade(wellA[0], wellA[3], 11.8, wellA[3]); Balustrade(14.2, wellA[3], 17.3, wellA[3]); Balustrade(18.7, wellA[3], wellA[1], wellA[3]);
            Balustrade(WellB[0], WellB[2], WellB[0], WellB[3]);
            Balustrade(WellB[1], WellB[2], WellB[1], WellB[3]);
            Balustrade(WellB[0], WellB[3], WellB[1], WellB[3]);
            Balustrade(WellB[0], WellB[2], 149.3, WellB[2]); Balustrade(150.7, WellB[2], WellB[1], WellB[2]);
            si.wells.Add(new[] { Q(16, 15).x, Q(16, 15).z, 16.0 });
            si.wells.Add(new[] { Q(150, 97).x, Q(150, 97).z, 14.0 });
            Lift("A", 23.5, 26.5, new V2(0, -1));
            Lift("B", 161.5, 93.5, new V2(-1, 0));
        }

        /// One stretch of an outer wall, inside face.
        static void PWall(V2 a, V2 b, V2 inward, bool warehouse, double yStart)
        {
            double[][] bands = warehouse
                ? new[] { new[] { HY0, 0.4 }, new[] { 0.4, 0.55 }, new[] { 0.55, HC1 } }
                : new[] { new[] { HY0, 0.31 }, new[] { 0.31, 3.0 }, new[] { 3.0, 3.4 }, new[] { 3.4, HC0 }, new[] { HC0, 7.15 }, new[] { 7.15, 10.4 }, new[] { 10.4, HC1 } };
            string[] sw = warehouse ? new[] { "dark", "lane", "concrete" } : new[] { "dark", "cream", "orange", "cream", "dark", "white", "dark" };
            for (int i = 0; i < bands.Length; i++)
            {
                double y0 = Math.Max(bands[i][0], yStart), y1 = bands[i][1];
                if (y1 <= y0 + 0.001) continue;
                VQuad(hShell, a, b, y0, y1, HA("s_" + sw[i]), inward);
            }
        }

        /// An escalator (or a flight of stairs): its low end at (u, vLow), rising towards dir (±v) to the first floor.
        static void Escalator(string name, double u, double vLow, int dir, bool stairs, bool up)
        {
            double hb = stairs ? 1.2 : 0.5, side = hb + 0.06;
            double vHigh = vLow + dir * EscRun, vL0 = vLow - dir * EscLand, vH1 = vHigh + dir * EscLand;
            Func<double, double> Y = v => { double t = (v - vLow) / (vHigh - vLow); return t <= 0 ? HY0 : t >= 1 ? HY1 : HY0 + t * (HY1 - HY0); };
            V2 A = EU;
            UVRect steel = HA("s_steel"), black = HA("s_black"), light = HA("s_light"), dark = HA("s_dark");
            if (!stairs)
            {
                MeshData st = new MeshData("Steps_" + si.id + "_" + name, "Shop", HomeStepsMat, false) { navIgnore = true };
                double[] vs = up ? new[] { vL0, vLow, vHigh, vH1 } : new[] { vH1, vHigh, vLow, vL0 };
                double s = 0;
                for (int k = 0; k < 3; k++)
                {
                    double va = vs[k], vb = vs[k + 1], ya = Y(va) + 0.03, yb = Y(vb) + 0.03;
                    double len = Math.Sqrt((vb - va) * (vb - va) + (yb - ya) * (yb - ya));
                    V2 pa = Q(u, va), pb = Q(u, vb);
                    st.Quad(new P3(pa - A * hb, ya, 0, s / 0.4), new P3(pa + A * hb, ya, 1, s / 0.4), new P3(pb + A * hb, yb, 1, (s + len) / 0.4), new P3(pb - A * hb, yb, 0, (s + len) / 0.4), 0, 1, 0);
                    s += len;
                }
                si.steps.Add(st);
                // comb plates at each end
                foreach (double v in new[] { vL0, vH1 }) HB(hShell, u - hb, u + hb, v - 0.15, v + 0.15, Y(v), Y(v) + 0.035, "steel");
                double[] tr = up ? new[] { vL0, vLow, vHigh, vH1 } : new[] { vH1, vHigh, vLow, vL0 };
                List<double> e = new List<double>();
                foreach (double v in tr) { V2 p = Q(u, v); e.Add(p.x); e.Add(Y(v)); e.Add(p.z); }
                e.Add(hb);
                si.escalators.Add(e.ToArray());
            }
            else
            {
                int n = (int)Math.Ceiling((HY1 - HY0) / 0.18); double rise = (HY1 - HY0) / n, go = EscRun / n;
                for (int k = 0; k < n; k++)
                {
                    double v0 = vLow + dir * go * k, v1 = v0 + dir * go, yt = HY0 + rise * (k + 1);
                    SBox(hShell, Q(u, (v0 + v1) / 2), EU, hb, go / 2, yt - rise, yt, dark);
                    HQuad(hShell, Q(u, v1 - dir * 0.03), EV, 0.03, hb, yt + 0.002, HA("s_lane"));          // the nosing
                }
            }
            // ---- the walk surface (colliders only): the slope, the sides, the truss under it
            double sh = stairs ? (EscRun / Math.Ceiling((HY1 - HY0) / 0.18)) / 2 : 0;     // stairs: through the middle of the steps
            V2 lo = Q(u, vLow - dir * sh), hi = Q(u, vHigh - dir * sh), hiEnd = Q(u, vHigh + dir * 0.25);
            hWalk.Quad(new P3(lo - A * side, HY0), new P3(lo + A * side, HY0), new P3(hi + A * side, HY1), new P3(hi - A * side, HY1), 0, 1, 0);
            if (stairs) hWalk.Quad(new P3(hi - A * side, HY1), new P3(hi + A * side, HY1), new P3(hiEnd + A * side, HY1), new P3(hiEnd - A * side, HY1), 0, 1, 0);
            foreach (int k in new[] { -1, 1 })
            {
                V2 o = A * (k * side);
                V2 l0 = Q(u, vLow - dir * 1.4) + o, l1 = Q(u, vLow) + o, h0 = Q(u, vHigh) + o;
                // the sides, up to handrail height (both faces), and the truss down to the floor
                foreach (int f in new[] { -1, 1 })
                {
                    hWalk.Quad(new P3(l0, HY0), new P3(l1, HY0), new P3(l1, HY0 + 1.05), new P3(l0, HY0 + 1.05), f * k, 0, 0);
                    hWalk.Quad(new P3(l1, HY0), new P3(h0, HY1), new P3(h0, HY1 + 1.05), new P3(l1, HY0 + 1.05), f * k, 0, 0);
                    hWalk.Tri(new P3(l1, HY0), new P3(h0, HY1), new P3(h0, HY0), f * k, 0, 0);
                }
                // what you see: the truss's cladding, a black skirt, the glass, the handrail
                V2 so = A * (k * (side + 0.02));
                hShell.Tri(new P3(Q(u, vLow) + so, HY0, steel.Cu, steel.Cv), new P3(Q(u, vHigh) + so, HY1, steel.Cu, steel.Cv), new P3(Q(u, vHigh) + so, HY0, steel.Cu, steel.Cv), k, 0, 0);
                hShell.Quad(new P3(Q(u, vLow) + so, HY0 - 0.0, steel.Cu, steel.Cv), new P3(Q(u, vHigh) + so, HY1, steel.Cu, steel.Cv), new P3(Q(u, vHigh) + so, HY1 + 0.16, steel.Cu, steel.Cv), new P3(Q(u, vLow) + so, HY0 + 0.16, steel.Cu, steel.Cv), k, 0, 0);
                V2 si0 = A * (k * hb);
                hShell.Quad(new P3(Q(u, vL0) + si0, HY0, black.Cu, black.Cv), new P3(Q(u, vLow) + si0, HY0, black.Cu, black.Cv), new P3(Q(u, vLow) + si0, HY0 + 0.15, black.Cu, black.Cv), new P3(Q(u, vL0) + si0, HY0 + 0.15, black.Cu, black.Cv), -k, 0, 0);
                hShell.Quad(new P3(Q(u, vLow) + si0, HY0, black.Cu, black.Cv), new P3(Q(u, vHigh) + si0, HY1, black.Cu, black.Cv), new P3(Q(u, vHigh) + si0, HY1 + 0.15, black.Cu, black.Cv), new P3(Q(u, vLow) + si0, HY0 + 0.15, black.Cu, black.Cv), -k, 0, 0);
                double[] gv = { vLow - dir * 1.2, vLow, vHigh, vHigh + dir * 0.9 };
                UVRect rail = stairs ? light : black;
                for (int g = 0; g < 3; g++)
                {
                    V2 ga = Q(u, gv[g]) + A * (k * (hb + 0.12)), gb = Q(u, gv[g + 1]) + A * (k * (hb + 0.12));
                    double ya = Y(gv[g]), yb = Y(gv[g + 1]);
                    MeshData gm = stairs ? hShell : hGlass;
                    UVRect gr = stairs ? light : new UVRect(0.5, 0.5, 0.5, 0.5);
                    foreach (int f in new[] { -1, 1 })
                        gm.Quad(new P3(ga, ya + 0.15, gr.Cu, gr.Cv), new P3(gb, yb + 0.15, gr.Cu, gr.Cv), new P3(gb, yb + 1.0, gr.Cu, gr.Cv), new P3(ga, ya + 1.0, gr.Cu, gr.Cv), f, 0, 0);
                    Beam(hShell, ga, ya + 1.02, gb, yb + 1.02, 0.05, 0.04, stairs ? steel : rail);
                }
                SBox(hShell, Q(u, vLow - dir * 1.25) + A * (k * (hb + 0.12)), EV, 0.12, 0.08, HY0, HY0 + 1.06, stairs ? steel : black);
            }
            // under the high end: closed off down to the floor
            V2 e0 = Q(u, vHigh) - A * (side + 0.02), e1 = Q(u, vHigh) + A * (side + 0.02);
            VQuad(hShell, e0, e1, HY0, HC0, steel, EV * dir);
            hWalk.Quad(new P3(e0, HY0), new P3(e1, HY0), new P3(e1, HC0), new P3(e0, HC0), 0, 0, dir);
            HWallNote(u - side, vLow, u - side, vHigh, HY0, HY1 + 1.0); HWallNote(u + side, vLow, u + side, vHigh, HY0, HY1 + 1.0);
            HWallNote(u - side, vHigh, u + side, vHigh, HY0, HC0);
        }

        /// Glass between rails round a well, from (u0, v0) to (u1, v1) on the first floor.
        static void Balustrade(double u0, double v0, double u1, double v1)
        {
            V2 a = Q(u0, v0), b = Q(u1, v1);
            if (V2.Dist(a, b) < 0.05) return;
            TwoSidedV(hGlass, a, b, HY1 + 0.08, HY1 + 1.0, new UVRect(0.5, 0.5, 0.5, 0.5));
            Beam(hShell, a, HY1 + 1.04, b, HY1 + 1.04, 0.04, 0.04, HA("s_steel"));
            Beam(hShell, a, HY1 + 0.05, b, HY1 + 0.05, 0.03, 0.05, HA("s_dark"));
            V2 f = (b - a).Norm;
            for (double t = 0; t <= V2.Dist(a, b) + 0.01; t += 1.5) SBox(hShell, a + f * Math.Min(t, V2.Dist(a, b)), f, 0.03, 0.03, HY1, HY1 + 1.04, HA("s_steel"));
            foreach (int k in new[] { -1, 1 })
                hWalk.Quad(new P3(a, HY1), new P3(b, HY1), new P3(b, HY1 + 1.1), new P3(a, HY1 + 1.1), f.Left.x * k, 0, f.Left.z * k);
            HWallNote(u0, v0, u1, v1, HY1, HY1 + 1.1);
        }

        /// A lift: a shaft through both floors with a door on each, facing o. The cabin is the shaft's inside.
        static void Lift(string name, double cu, double cv, V2 o)
        {
            const double half = 1.5, wt = 0.2, dh = 0.7, dt = 2.3;
            V2 c = Q(cu, cv);
            foreach (V2 n in new[] { o, o * -1, o.Left, o.Left * -1 })
            {
                V2 t = n.Left;
                V2 wc = c + n * (half - wt / 2);
                if (n.x == o.x && n.z == o.z)
                {
                    foreach (double[] sp in new[] { new[] { -half, -dh }, new[] { dh, half } })
                        SBox(hShell, wc + t * ((sp[0] + sp[1]) / 2), t, (sp[1] - sp[0]) / 2, wt / 2, HY0, HC1, HA("s_charcoal"));
                    SBox(hShell, wc, t, dh, wt / 2, HY0 + dt, HY1, HA("s_charcoal"), true);
                    SBox(hShell, wc, t, dh, wt / 2, HY1 + dt, HC1, HA("s_charcoal"), true);
                }
                else SBox(hShell, wc, t, half, wt / 2, HY0, HC1, HA("s_charcoal"));
                V2 a = wc - t * half, b = wc + t * half;
                si.walls.Add(new[] { a.x, a.z, b.x, b.z, HY0, HC1 });
            }
            ShopLift lift = new ShopLift { x = c.x, z = c.z, ox = o.x, oz = o.z, floors = new[] { HY0, HY1 },
                                           names = new[] { "Ground floor: market hall, self serve, tills", "First floor: showroom, café" } };
            si.lifts.Add(lift);
            // the cabin at each floor (detail), the doors, the signs outside
            for (int lv = 0; lv < 2; lv++)
            {
                hLevel = lv;
                MeshData m = HPart("Lift" + name);
                double y = HFl, inner = half - wt - 0.01;
                foreach (V2 n in new[] { o * -1, o.Left, o.Left * -1 })
                {
                    V2 t = n.Left, wc = c + n * inner;
                    VQuad(m, wc - t * inner, wc + t * inner, y, y + 2.55, HA("s_steel"), n * -1);
                }
                VQuad(m, c - o * (inner - 0.01) - o.Left * 0.5, c - o * (inner - 0.01) + o.Left * 0.5, y + 1.0, y + 2.1, HA("s_mirror"), o);
                SBox(m, c + o * (inner - 0.05) + o.Left * (dh + 0.25), o, 0.03, 0.08, y + 1.0, y + 1.3, HA("s_dark"));
                SBox(HLights, c + o * (inner - 0.08) + o.Left * (dh + 0.25), o, 0.035, 0.03, y + 1.15, y + 1.2, new UVRect(0.5, 0.5, 0.5, 0.5));
                // the cabin's ceiling: a lit panel (and the ground floor's hides the slab above it)
                {
                    UVRect lt = HA("s_white");
                    V2 p0 = c - o * inner - o.Left * inner, p1 = c + o * inner - o.Left * inner, p2 = c + o * inner + o.Left * inner, p3 = c - o * inner + o.Left * inner;
                    m.Quad(new P3(p0, y + 2.55, lt.Cu, lt.Cv), new P3(p1, y + 2.55, lt.Cu, lt.Cv), new P3(p2, y + 2.55, lt.Cu, lt.Cv), new P3(p3, y + 2.55, lt.Cu, lt.Cv), 0, -1, 0);
                    SBox(HLights, c, o, 0.6, 0.6, y + 2.5, y + 2.54, new UVRect(0.5, 0.5, 0.5, 0.5), true);
                }
                V2 dc = c + o * (half - wt / 2);
                HSignAt(m, dc + o * (wt / 2 + 0.02), o.Left, 1.6, 0.2, y + dt + 0.12, "g_LIFT", o);
                SBox(m, dc + o * (wt / 2 + 0.03) + o.Left * (dh + 0.3), o, 0.02, 0.07, y + 1.05, y + 1.3, HA("s_steel"));
                SBox(HLights, dc + o * (wt / 2 + 0.05) + o.Left * (dh + 0.3), o, 0.02, 0.03, y + 1.12, y + 1.18, new UVRect(0.5, 0.5, 0.5, 0.5));
                // the doors (steel, they slide open for anyone near)
                ShopDoor d = new ShopDoor { x = dc.x, z = dc.z, ax = o.Left.x, az = o.Left.z, ox = o.x, oz = o.z, half = dh, top = HY0 + dt, y = y - HY0, glass = false, reach = 2.1 };
                UVRect stl = HA("s_steel");
                foreach (int sd in new[] { -1, 1 })
                {
                    MeshData leaf = new MeshData("LiftDoor_" + si.id + "_" + name + lv + (sd < 0 ? "_L" : "_R"), "Shop", HomeAtlasMat, false) { navIgnore = true };
                    double x0 = sd < 0 ? -dh : 0, x1 = sd < 0 ? 0 : dh;
                    leaf.Quad(new P3(x0, HY0, 0, stl.Cu, stl.Cv), new P3(x1, HY0, 0, stl.Cu, stl.Cv), new P3(x1, HY0 + dt, 0, stl.Cu, stl.Cv), new P3(x0, HY0 + dt, 0, stl.Cu, stl.Cv), 0, 0, 1);
                    leaf.Quad(new P3(x0, HY0, 0, stl.Cu, stl.Cv), new P3(x1, HY0, 0, stl.Cu, stl.Cv), new P3(x1, HY0 + dt, 0, stl.Cu, stl.Cv), new P3(x0, HY0 + dt, 0, stl.Cu, stl.Cv), 0, 0, -1);
                    if (sd < 0) d.left = leaf; else d.right = leaf;
                }
                si.doors.Add(d);
            }
            hLevel = 0;
        }

        /// A sign flat on a wall (one face), centred at c, along `along`, its bottom at y, facing n.
        static void HSignAt(MeshData m, V2 c, V2 along, double w, double h, double y, string tile, V2 n)
        {
            VQuad(m, c - along * (w / 2), c + along * (w / 2), y, y + h, HA(tile), n);
        }

        static int roomCount;
    }
}
