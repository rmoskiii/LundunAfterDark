// E6: FLATPAK's ground floor (lobbies, checkouts, bistro, bargains, market hall, the self-serve warehouse), the
// things you can take or break, and where people walk.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        // ------------------------------------------------------------------ people, things
        static void HSpotXZ(V2 at, V2 look)
        {
            si.crowd.spots.Add(new[] { at.x, at.z, look.x, look.z, -1, HFl });         // (the sixth: which floor)
            spotY.Add(HFl);
        }

        static void HSpot(double u, double v, double lu, double lv) { HSpotXZ(Q(u, v), Q(lu, lv)); }

        static void HStaff(double u, double v, double fu, double fv)
        {
            V2 p = Q(u, v);
            si.crowd.traders.Add(new[] { p.x, p.z, fu, fv, HFl });
        }

        /// Something you can take: a small box with a lit tag, at (u, v) on a surface at height top; you stand at (su, sv).
        static void HItem(string id, string name, bool steal, double u, double v, double top, double su, double sv, string colour)
        {
            MeshData m = new MeshData("Item_" + si.id + "_" + id, "Shop", HomeAtlasMat, false) { navIgnore = true };
            V2 p = Q(u, v), toward = (Q(su, sv) - p).Norm;
            SBox(m, p, toward.Left, 0.15, 0.1, top, top + 0.26, HA(steal ? "s_red" : "s_" + colour), true);
            SBox(m, p + toward * 0.11, toward.Left, 0.1, 0.01, top + 0.06, top + 0.18, HA("s_item_glow"));
            V2 st = Q(su, sv);
            si.items.Add(new ShopItem { id = si.id.ToLowerInvariant() + "_" + id, name = name, steal = steal, mesh = m, x = st.x, y = HFl, z = st.z });
        }

        /// A display that breaks: a stepped pyramid of boxes (painted from a strip or a swatch) on a low plinth.
        static void HStack(string name, string kind, double u, double v, double y, string strip, string plinth)
        {
            MeshData m = new MeshData("Stack_" + si.id + "_" + name, "Shop", HomeAtlasMat, false) { navIgnore = true };
            V2 c = Q(u, v);
            SBox(m, c, EU, 0.62, 0.62, y, y + 0.14, HA("s_" + plinth));
            UVRect s = HA(strip);
            double yy = y + 0.14;
            for (int t = 0; t < 4; t++)
            {
                double hw = 0.58 - t * 0.12, hh = 0.26;
                foreach (V2 o in new[] { new V2(1, 0), new V2(-1, 0), new V2(0, 1), new V2(0, -1) })
                {
                    V2 l = o.Left;
                    double su = s.u0 + (s.u1 - s.u0) * (0.12 * t), su1 = su + (s.u1 - s.u0) * 0.12;
                    m.Quad(new P3(c + o * hw - l * hw, yy, su, s.v0), new P3(c + o * hw + l * hw, yy, su1, s.v0), new P3(c + o * hw + l * hw, yy + hh, su1, s.v1), new P3(c + o * hw - l * hw, yy + hh, su, s.v1), o.x, 0, o.z);
                }
                UVRect top = HA("s_ceramic");
                m.Quad(new P3(c + new V2(-hw, -hw), yy + hh, top.Cu, top.Cv), new P3(c + new V2(hw, -hw), yy + hh, top.Cu, top.Cv), new P3(c + new V2(hw, hw), yy + hh, top.Cu, top.Cv), new P3(c + new V2(-hw, hw), yy + hh, top.Cu, top.Cv), 0, 1, 0);
                yy += hh;
            }
            si.breakables.Add(new ShopProp { kind = kind, name = name, mesh = m, cx = c.x, cy = y + 0.62, cz = c.z, sx = 1.24, sy = 1.2, sz = 1.24, yaw = 0, debrisUV = Rect(s) });
        }

        // ------------------------------------------------------------------ fixtures (ground floor)
        /// A table of things (seen from above), with a riser in the middle. Along u.
        static void HTable(double u0, double u1, double v, string top, double h = 0.75)
        {
            double hd2 = 0.6;
            HB(hd, u0, u1, v - hd2, v + hd2, HY0, HY0 + h - 0.04, "birch");
            HB(hd, u0 + 0.05, u1 - 0.05, v - hd2 + 0.05, v + hd2 - 0.05, HY0, HY0 + 0.1, "dark");
            for (double u = u0; u < u1 - 0.05; u += 2.0) { double ub = Math.Min(u + 2.0, u1); HQuad(hd, Q((u + ub) / 2, v), EV, hd2 - 0.03, (ub - u) / 2 - 0.02, HY0 + h - 0.035, HA("t_" + top)); }
            HB(hd, u0 + 0.4, u1 - 0.4, v - 0.25, v + 0.25, HY0 + h - 0.04, HY0 + h + 0.25, "birch");
            for (double u = u0 + 0.4; u < u1 - 0.45; u += 2.0) { double ub = Math.Min(u + 2.0, u1 - 0.4); HQuad(hd, Q((u + ub) / 2, v), EV, 0.23, (ub - u) / 2 - 0.02, HY0 + h + 0.252, HA("t_" + top)); }
            HCol("Table", u0, u1, v - hd2, v + hd2, HY0, HY0 + h + 0.25);
            for (double u = u0 + 1.2; u < u1 - 0.6; u += 3.4) { HSpot(u, v - hd2 - 0.9, u, v); HSpot(u + 1.0, v + hd2 + 0.9, u + 1.0, v); }
        }

        /// Wire bins of soft things, two by two along u.
        static void HBins(double u0, double u1, double v, string top)
        {
            for (double u = u0; u + 1.5 <= u1 + 0.01; u += 1.6)
            {
                HB(hd, u, u + 1.5, v - 0.75, v + 0.75, HY0, HY0 + 0.8, "steel");
                HQuad(hd, Q(u + 0.75, v), EV, 0.72, 0.72, HY0 + 0.72, HA("t_" + top));
                HB(hd, u + 0.02, u + 1.48, v - 0.73, v + 0.73, HY0 + 0.8, HY0 + 0.82, "chrome");
            }
            HCol("Bins", u0, u1, v - 0.75, v + 0.75, HY0, HY0 + 0.82);
            for (double u = u0 + 1.0; u < u1 - 0.5; u += 3.2) { HSpot(u, v - 1.65, u, v); HSpot(u + 1.2, v + 1.65, u + 1.2, v); }
        }

        /// Shelves of homeware along u, one side (face = ±1, which way it faces in v) or both (face = 0).
        static void HShelves(double u0, double u1, double v, int face, double h, string stripA, string stripB)
        {
            double dep = face == 0 ? 0.6 : 0.4;
            HB(hd, u0, u1, v - dep, v + dep, HY0, HY0 + 0.12, "dark");
            HB(hd, u0, u1, v - 0.04, v + 0.04, HY0 + 0.12, HY0 + h, "offwhite");
            HB(hd, u0 - 0.02, u1 + 0.02, v - dep - 0.02, v + dep + 0.02, HY0 + h, HY0 + h + 0.06, "orange", true);
            int levels = (int)Math.Floor((h - 0.2) / 0.42);
            foreach (int side in face == 0 ? new[] { -1, 1 } : new[] { face })
            {
                for (double u = u0; u < u1 - 0.01; u += 4.0)
                {
                    double ub = Math.Min(u1, u + 4.0), len = ub - u;
                    for (int lv = 0; lv < levels; lv++)
                    {
                        UVRect s = HA((lv % 2 == 0 ? stripA : stripB));
                        double y0 = HY0 + 0.2 + lv * 0.42, y1 = y0 + 0.36;
                        double st = hr.NextDouble() * (1 - len / 4.0);
                        double su0 = s.u0 + (s.u1 - s.u0) * st, su1 = s.u0 + (s.u1 - s.u0) * (st + len / 4.0);
                        V2 p0 = Q(u, v + side * (dep - 0.02)), p1 = Q(ub, v + side * (dep - 0.02)), n = EV * side;
                        bool flip = V2.Dot(p1 - p0, n.Left) < 0;
                        hd.Quad(new P3(p0, y0, flip ? su1 : su0, s.v0), new P3(p1, y0, flip ? su0 : su1, s.v0), new P3(p1, y1, flip ? su0 : su1, s.v1), new P3(p0, y1, flip ? su1 : su0, s.v1), n.x, 0, n.z);
                        HB(hd, u, ub, Math.Min(v + side * 0.04, v + side * dep), Math.Max(v + side * 0.04, v + side * dep), y0 - 0.03, y0 - 0.0, "white");
                    }
                }
                for (double u = u0 + 1.0; u < u1 - 0.5; u += 3.0) HSpot(u, v + side * (dep + 0.9), u, v);
            }
            HCol("Shelves", u0, u1, v - dep, v + dep, HY0, HY0 + h + 0.06);
        }

        /// Rug racks: rugs hanging like pages, along u.
        static void HRugs(double u0, double u1, double v)
        {
            HB(hd, u0, u1, v - 1.0, v + 1.0, HY0, HY0 + 0.08, "dark");
            for (double u = u0 + 0.3; u < u1 - 0.2; u += 0.35)
            {
                V2 a = Q(u, v - 0.95), b = Q(u, v + 0.95);
                TwoSidedV(hd, a, b, HY0 + 0.15, HY0 + 2.0, HA("r_" + hr.Next(4)));
            }
            foreach (double u in new[] { u0 + 0.05, u1 - 0.05 }) HB(hd, u - 0.04, u + 0.04, v - 1.0, v + 1.0, HY0, HY0 + 2.2, "black");
            HB(hd, u0, u1, v - 0.03, v + 0.03, HY0 + 2.1, HY0 + 2.2, "black");
            HCol("RugRack", u0, u1, v - 1.0, v + 1.0, HY0, HY0 + 2.2);
            for (double u = u0 + 1.0; u < u1 - 0.5; u += 3.0) { HSpot(u, v - 1.9, u, v); HSpot(u, v + 1.9, u, v); }
        }

        /// A table of lamps (lit), with pendants hanging above.
        static void HLamps(double u0, double u1, double v)
        {
            HTableOnly(u0, u1, v);
            for (double u = u0 + 0.5; u < u1 - 0.3; u += 0.9)
                Lamp(new HFrame { o = Q(u, v + ((int)(u * 10) % 2 == 0 ? -0.25 : 0.25)), a = EU, b = EV, y = HY0, m = hd }, 0, 0, 0.75, false);
            for (double u = u0 + 1.0; u < u1 - 0.5; u += 2.0)
                Pendant(new HFrame { o = Q(u, v), a = EU, b = EV, y = HY0, m = hd }, 0, 0, 3.4 + ((int)u % 3) * 0.4);
            for (double u = u0 + 1.2; u < u1 - 0.6; u += 3.4) { HSpot(u, v - 1.5, u, v); HSpot(u + 1.0, v + 1.5, u + 1.0, v); }
        }

        static void HTableOnly(double u0, double u1, double v)
        {
            HB(hd, u0, u1, v - 0.6, v + 0.6, HY0, HY0 + 0.71, "white");
            HB(hd, u0 - 0.01, u1 + 0.01, v - 0.61, v + 0.61, HY0 + 0.71, HY0 + 0.75, "birch");
            HCol("Table", u0, u1, v - 0.6, v + 0.6, HY0, HY0 + 1.3);
        }

        static void HPlants(double u0, double u1, double v)
        {
            HB(hd, u0, u1, v - 0.7, v + 0.7, HY0, HY0 + 0.45, "pine");
            for (double u = u0; u < u1 - 0.1; u += 2.0) { double ub = Math.Min(u + 2.0, u1); HQuad(hd, Q((u + ub) / 2, v), EV, 0.68, (ub - u) / 2 - 0.02, HY0 + 0.46, HA("t_plants")); }
            for (double u = u0 + 0.6; u < u1 - 0.4; u += 2.4)
                Plant(new HFrame { o = Q(u, v + (((int)u) % 2 == 0 ? 0.3 : -0.3)), a = EU, b = EV, y = HY0 + 0.45, m = hd }, 0, 0, 0.9 + (((int)u) % 3) * 0.35);
            HCol("Plants", u0, u1, v - 0.7, v + 0.7, HY0, HY0 + 1.6);
            for (double u = u0 + 1.2; u < u1 - 0.6; u += 3.4) { HSpot(u, v - 1.6, u, v); HSpot(u + 1.0, v + 1.6, u + 1.0, v); }
        }

        // ------------------------------------------------------------------ the lobbies (in and out), the way up
        static void HomeLobbies(Store s)
        {
            hLevel = 0;
            MeshData m = HPart("Lobby"); hd = m;
            double y = HY0;
            // the barrier between the way in and the way out (glass on posts), and along the way in's back
            foreach (double[] b in new[] { new[] { 40.4, 3.0, 40.4, 13.5 }, new[] { 26.0, 13.5, 40.4, 13.5 } })
            {
                V2 a = Q(b[0], b[1]), c = Q(b[2], b[3]), f = (c - a).Norm;
                TwoSidedV(HGlassD, a, c, y + 0.1, y + 1.05, new UVRect(0.5, 0.5, 0.5, 0.5));
                Beam(m, a, y + 1.08, c, y + 1.08, 0.04, 0.03, HA("s_steel"));
                for (double t = 0; t <= V2.Dist(a, c) + 0.01; t += 1.6) SBox(m, a + f * Math.Min(t, V2.Dist(a, c)), f, 0.03, 0.03, y, y + 1.1, HA("s_steel"));
                HCol("Barrier", Math.Min(b[0], b[2]) - 0.05, Math.Max(b[0], b[2]) + 0.05, Math.Min(b[1], b[3]) - 0.05, Math.Max(b[1], b[3]) + 0.05, y, y + 1.1);
                HWallNote(b[0], b[1], b[2], b[3], y, y + 1.1);
            }
            HSign(m, 33, 6.5, EU, 6.4, 0.8, y + 4.3, "g_WELCOME");
            HSign(m, 18, 3.0, EU, 3.2, 0.4, y + 3.6, "g_SHOWROOM_UP");
            HSign(m, 48, 3.0, EU, 3.2, 0.4, y + 3.6, "g_WAY OUT");
            // trolleys and bags, the information desk, the store directory
            for (int row = 0; row < 2; row++)
                for (int k = 0; k < 7; k++)
                {
                    V2 c = Q(35.0 + row * 1.6, 8.0 + k * 0.35);
                    SBox(m, c, EV, 0.42, 0.28, y + 0.12, y + 0.95, HA("s_chrome"));
                    SBox(m, c, EV, 0.42, 0.29, y + 0.8, y + 0.86, HA("s_orange"), true);
                }
            HCol("Trolleys", 34.4, 38.2, 7.5, 10.9, y, y + 1.0);
            HB(m, 31.0, 32.2, 5.4, 6.0, y, y + 1.1, "charcoal");
            for (int k = 0; k < 10; k++) HB(m, 31.05, 32.15, 5.45, 5.95, y + 1.1 + k * 0.02, y + 1.115 + k * 0.02, "bag");
            HCol("Bags", 31.0, 32.2, 5.4, 6.0, y, y + 1.3);
            HB(m, 27.0, 31.0, 10.0, 11.2, y, y + 1.05, "orange"); HB(m, 26.9, 31.1, 9.9, 11.3, y + 1.05, y + 1.1, "white");
            HB(m, 28.6, 29.4, 10.3, 10.6, y + 1.1, y + 1.4, "dark");
            HCol("InfoDesk", 26.9, 31.1, 9.9, 11.3, y, y + 1.1);
            HStaff(29.0, 12.2, 0, -1);
            HSign(m, 29, 10.6, EU, 3.2, 0.4, y + 2.8, "g_INFORMATION");
            HB(m, 32.4, 33.6, 12.7, 12.8, y, y + 2.2, "charcoal");
            VQuad(m, Q(32.4, 12.68), Q(33.6, 12.68), y + 0.35, y + 2.15, HA("m_dir"), EV * -1);
            HCol("Directory", 32.4, 33.6, 12.6, 12.85, y, y + 2.2);
            HSpot(33.0, 11.5, 33.0, 12.7); HSpot(29.0, 8.8, 29.0, 10.5);
            // the way up: zone by the escalator (lockers, toilets)
            for (int k = 0; k < 6; k++) { HB(m, 0.36, 0.95, 2.0 + k * 1.6, 3.5 + k * 1.6, y, y + 1.9, k % 2 == 0 ? "steel" : "light"); }
            HCol("Lockers", 0.35, 0.95, 2.0, 11.6, y, y + 1.9);
            VQuad(m, Q(0.37, 20.0), Q(0.37, 21.4), y, y + 2.1, HA("s_door"), EU);
            HSignAt(m, Q(0.38, 20.7), EV, 1.6, 0.2, y + 2.3, "g_TOILETS", EU);
            HSpot(2.0, 5.0, 0.5, 5.0);
            // lights
            for (double v = 3; v < 29; v += 4) SBox(hLights0, Q(30, v), EU, 28, 0.08, HC0 - 0.08, HC0 - 0.02, new UVRect(0.5, 0.5, 0.5, 0.5), true);
        }

        // ------------------------------------------------------------------ the tills
        static void HomeCheckouts()
        {
            hLevel = 0;
            MeshData m = HPart("Tills"); hd = m;
            double y = HY0;
            for (int i = 0; i < 8; i++)
            {
                double u = 42.5 + 3.6 * i;
                HB(m, u - 0.45, u + 0.45, 15.5, 19.5, y, y + 0.9, "dark");
                HB(m, u - 0.46, u - 0.44, 15.6, 19.4, y + 0.2, y + 0.75, "orange");
                HB(m, u - 0.3, u + 0.3, 16.5, 19.4, y + 0.9, y + 0.94, "belt");
                HB(m, u - 0.2, u + 0.2, 15.7, 16.1, y + 0.94, y + 1.1, "mid");
                HB(m, u - 0.18, u + 0.18, 15.85, 15.9, y + 1.1, y + 1.4, "screen_on");
                HB(m, u + 0.3, u + 0.34, 19.3, 19.34, y + 0.9, y + 2.3, "steel");
                SBox(hLights0, Q(u + 0.32, 19.32), EU, 0.14, 0.14, y + 2.3, y + 2.55, new UVRect(0.5, 0.5, 0.5, 0.5), true);
                VQuad(m, Q(u + 0.2, 19.6), Q(u + 0.44, 19.6), y + 2.6, y + 2.78, HA("y_" + (i + 1)), EV);
                HCol("Checkout", u - 0.46, u + 0.46, 15.5, 19.5, y, y + 1.1);
                // impulse bins at the lane's start
                HB(m, u - 0.42, u + 0.42, 20.0, 20.8, y, y + 0.7, "steel");
                HQuad(m, Q(u, 20.4), EV, 0.38, 0.4, y + 0.66, HA(i % 2 == 0 ? "t_candles" : "t_boxes"));
                HCol("Impulse", u - 0.42, u + 0.42, 20.0, 20.8, y, y + 0.7);
                if (i < 5) HStaff(u - 0.85, 16.4, 1, 0);
            }
            HSign(m, 56, 18.0, EU, 3.2, 0.4, y + 3.4, "g_TILLS");
            HSign(m, 48, 27.5, EU, 6.4, 0.8, y + 3.6, "g_SELF SERVE");
            // self-checkouts
            for (int i = 0; i < 8; i++)
            {
                double u = 73.2 + (i % 2) * 3.4, v = 15.8 + (i / 2) * 1.6;
                HB(m, u - 0.35, u + 0.35, v - 0.3, v + 0.3, y, y + 1.0, "white");
                HB(m, u - 0.25, u + 0.25, v - 0.05, v + 0.0, y + 1.0, y + 1.45, "screen_on");
                HCol("SelfCheckout", u - 0.35, u + 0.35, v - 0.3, v + 0.3, y, y + 1.0);
            }
            HSign(m, 75, 22.6, EU, 3.2, 0.4, y + 3.0, "g_SCAN & PAY");
            HSpot(74.9, 21.0, 74.9, 20.6);
            // a display that breaks by the tills: tealight boxes
            HStack("Tealights", "stack", 40.0, 22.5, y, "p_CANDLE0", "pine");
            HItem("tealights", "Bag of tealights", true, 49.7 - 0.0, 20.4, y + 0.7, 49.7, 21.5, "orange");
            HItem("allenkey", "Allen key set", false, 56.9, 20.4, y + 0.7, 56.9, 21.5, "orange");
        }

        // ------------------------------------------------------------------ the bistro: hot dogs on the way out
        static void HomeBistro()
        {
            hLevel = 0;
            MeshData m = HPart("Bistro"); hd = m;
            double y = HY0;
            HB(m, 58, 72, 2.0, 2.9, y, y + 0.95, "charcoal"); HB(m, 57.9, 72.1, 1.95, 2.95, y + 0.95, y + 0.99, "steel");
            HQuad(m, Q(62, 2.45), EV, 0.35, 1.6, y + 0.995, HA("t_hotdogs"));
            HB(m, 66.0, 67.0, 2.1, 2.8, y + 0.99, y + 1.8, "white"); HB(m, 66.3, 66.7, 2.0, 2.15, y + 1.3, y + 1.45, "chrome");      // soft ice
            HB(m, 68.5, 70.5, 2.1, 2.8, y + 0.99, y + 1.7, "charcoal");                                                           // drinks
            HCol("BistroCounter", 57.9, 72.1, 1.95, 2.95, y, y + 1.8);
            {
                MeshData g = new MeshData("BistroGlass_" + si.id, "Shop", ShopGlassMat, false) { navIgnore = true };
                TwoSidedV(g, Q(59.0, 1.95), Q(65.0, 1.95), y + 1.0, y + 1.45, new UVRect(0.5, 0.5, 0.5, 0.5));
                V2 c = Q(62, 1.95);
                si.breakables.Add(new ShopProp { kind = "glass", name = "BistroGlass", mesh = g, cx = c.x, cy = y + 1.22, cz = c.z, sx = 6.0, sy = 0.45, sz = 0.06, yaw = 0, debrisUV = Rect(HA("s_white")) });
            }
            HStaff(65, 1.2, 0, 1);
            HSign(m, 65, 2.4, EU, 6.4, 0.8, y + 2.6, "g_HOTDOG", false);
            HSign(m, 65, 5.0, EU, 3.2, 0.4, y + 3.6, "g_BISTRO");
            // tall tables
            for (int i = 0; i < 6; i++)
            {
                double u = 59.5 + i * 2.6, v = 6.6;
                HB(m, u - 0.35, u + 0.35, v - 0.35, v + 0.35, y + 1.05, y + 1.1, "white");
                HB(m, u - 0.04, u + 0.04, v - 0.04, v + 0.04, y, y + 1.05, "dark"); HB(m, u - 0.25, u + 0.25, v - 0.25, v + 0.25, y, y + 0.03, "dark");
                HCol("TallTable", u - 0.35, u + 0.35, v - 0.35, v + 0.35, y, y + 1.1);
                HSpot(u, 7.6, u, v);
            }
            for (double u = 59; u < 72; u += 3) HSpot(u, 3.9, u, 2.4);
            HItem("hotdog", "Hot dog", true, 60.0, 2.45, y + 0.99, 60.0, 3.8, "hotdog");
            for (double v = 3; v < 13; v += 4) SBox(hLights0, Q(68, v), EU, 12, 0.08, HC0 - 0.08, HC0 - 0.02, new UVRect(0.5, 0.5, 0.5, 0.5), true);
        }

        // ------------------------------------------------------------------ bargains, delivery and returns, the wall behind
        static void HomeFrontEast()
        {
            hLevel = 0;
            MeshData m = HPart("FrontEast"); hd = m;
            double y = HY0;
            HB(m, 80, HW - Hw, 24.4, 24.6, y, y + 4.0, "offwhite"); HCol("Partition", 80, HW - Hw, 24.4, 24.6, y, y + 4.0); HWallNote(80, 24.5, HW - Hw, 24.5, y, y + 4.0);
            // the bargain corner: odd pieces on low plinths
            HSign(m, 100, 13.0, EU, 6.4, 0.8, y + 3.4, "g_BARGAIN CORNER");
            for (int i = 0; i < 6; i++)
            {
                double u = 85 + i * 6.0;
                HB(m, u - 2.2, u + 2.2, 17.2, 22.8, y, y + 0.2, "light");
                HCol("Plinth", u - 2.2, u + 2.2, 17.2, 22.8, y, y + 0.2);
                HFrame F = new HFrame { o = Q(u, 20.0), a = EU, b = EV, y = y + 0.2, m = m };
                switch (i % 3)
                {
                    case 0: Sofa(F.At(0, 0.8, 0), 2.0, Pick("f_grey", "f_rust", "f_blue")); CoffeeTable(F.At(0, -0.8, 0), 0.9, 0.5, "oak"); break;
                    case 1: Wardrobe(F.At(0, 1.6, 0), 2.0); Unit(F.At(1.2, -0.6, 0), 1.0, 0.45, 0.8, "walnut", "k_drawers", 1.0); break;
                    default: DiningTable(F.At(0, 0, 0), 1.6, 0.8, "pine"); Chair(F.At(-0.5, -0.65, 2), "white"); Chair(F.At(0.5, 0.65, 0), "white"); break;
                }
                PriceTag(F, 1.8, -2.4, i * 5 + 1);
                HSpot(u, 15.6, u, 20.0);
            }
            // delivery and returns: a long counter, screens, a queue, chairs
            HSign(m, 142, 13.0, EU, 6.4, 0.8, y + 3.4, "g_DELIVERY & RETURNS");
            HB(m, 128, 156, 18.0, 19.0, y, y + 1.05, "charcoal"); HB(m, 127.9, 156.1, 17.9, 19.1, y + 1.05, y + 1.1, "white");
            for (int i = 0; i < 4; i++) { double u = 131 + i * 7; HB(m, u - 0.25, u + 0.25, 18.4, 18.5, y + 1.1, y + 1.5, "screen_on"); }
            HCol("ReturnsDesk", 127.9, 156.1, 17.9, 19.1, y, y + 1.1);
            HStaff(134, 20.0, 0, -1); HStaff(148, 20.0, 0, -1);
            for (int i = 0; i < 4; i++)
            {
                double u = 131 + i * 7;
                foreach (int k in new[] { -1, 1 }) { HB(m, u + k * 1.2 - 0.04, u + k * 1.2 + 0.04, 13.0, 13.08, y, y + 1.0, "steel"); HB(m, u + k * 1.2 - 0.04, u + k * 1.2 + 0.04, 16.4, 16.48, y, y + 1.0, "steel"); }
                foreach (int k in new[] { -1, 1 }) HB(m, u + k * 1.2 - 0.01, u + k * 1.2 + 0.01, 13.08, 16.4, y + 0.85, y + 0.92, "orange");
                HSpot(u, 16.9, u, 18.5);
            }
            for (int i = 0; i < 8; i++) { HFrame C = new HFrame { o = Q(130 + i * 2.6, 0.9), a = EU, b = EV, y = y, m = m }; Chair(C.At(0, 0, 2), "orange"); }
            HCol("Chairs", 129.6, 148.8, 0.6, 1.3, y, y + 0.9);
            for (double v = 3; v < 24; v += 4) SBox(hLights0, Q(122, v), EU, 42, 0.08, HC0 - 0.08, HC0 - 0.02, new UVRect(0.5, 0.5, 0.5, 0.5), true);
        }

        // ------------------------------------------------------------------ the market hall
        static void HomeMarket()
        {
            hLevel = 0;
            MeshData m = HPart("Market"); hd = m;
            double y = HY0;
            double[][] segs = { new[] { 66.0, 76 }, new[] { 80.0, 100 }, new[] { 104.0, 124 }, new[] { 128.0, 152 } };
            // the back wall's shelves (cookshop) and the tables in front
            HShelves(60, 146, 108.9, -1, 2.2, "p_COOK0", "p_GLASS0");
            string[] cook = { "pans", "mugs", "plates", "bowls" };
            for (int i = 0; i < 4; i++) HTable(segs[i][0], Math.Min(segs[i][1], 144), 103.0, cook[i]);
            // row by the first walkway: glasses, bowls, more shelves
            HTable(66, 76, 89, "glasses"); HShelves(80, 100, 89, 0, 1.8, "p_GLASS0", "p_COOK1"); HTable(104, 124, 89, "bowls"); HShelves(128, 146, 89, 0, 1.8, "p_COOK1", "p_GLASS1");
            // textiles and rugs
            HBins(66, 76, 79, "cushions"); HBins(80, 100, 79, "towels"); HRugs(104, 124, 79); HRugs(128, 140, 79); HBins(141, 152, 79, "rugroll");
            // lighting
            foreach (double[] sg in segs) HLamps(sg[0], sg[1], 65);
            // decoration
            HTable(66, 76, 55, "candles"); HShelves(80, 100, 55, 0, 1.8, "p_FRAME0", "p_CANDLE0"); HTable(104, 124, 55, "frames"); HShelves(128, 152, 55, 0, 1.8, "p_CANDLE1", "p_FRAME1");
            // plants and home storage
            HPlants(66, 76, 42); HPlants(80, 100, 42); HShelves(104, 124, 42, 0, 1.8, "p_BOX0", "p_TOY0"); HShelves(128, 152, 42, 0, 1.8, "p_BOX1", "p_LAMPBOX0");
            HShelves(80, 152, 25.0, 1, 2.0, "p_BOX0", "p_LAMPBOX1");
            // the section signs, hanging over the rows
            object[][] signs = { new object[] { 103.0, "g_COOKSHOP" }, new object[] { 89.0, "g_COOKSHOP" }, new object[] { 79.0, "g_TEXTILES" }, new object[] { 65.0, "g_LIGHTING" },
                                 new object[] { 55.0, "g_DECORATION" }, new object[] { 42.0, "g_PLANTS" }, new object[] { 25.6, "g_HOME STORAGE" } };
            foreach (object[] sg in signs) HSign(m, 90, (double)sg[0], EU, 3.2, 0.4, y + 3.9, (string)sg[1]);
            HSign(m, 132, 79.0, EU, 3.2, 0.4, y + 3.9, "g_RUGS");
            HSign(m, 114, 42.0, EU, 3.2, 0.4, y + 3.9, "g_HOME STORAGE");
            HSign(m, 62, 36.0, EV, 3.2, 0.4, y + 3.4, "g_SELF SERVE");
            HSign(m, 102, 97.0, EV, 3.2, 0.4, y + 3.4, "g_SHORTCUT");
            HSign(m, 102, 48.0, EV, 3.2, 0.4, y + 3.4, "g_SHORTCUT");
            HSign(m, 150, 107.6, EU, 3.2, 0.4, y + 3.0, "g_MARKET HALL");
            // the walkways' arrows (LA west, LB east, LC west, LD west)
            UVRect pth = HA("s_path");
            foreach (double[] w in new[] { new[] { 146.0, 97, 62, 97 }, new[] { 62.0, 72, 156, 72 }, new[] { 156.0, 48, 62, 48 }, new[] { 156.0, 36, 62, 36 } })
            {
                V2 a = Q(w[0], w[1]), b = Q(w[2], w[3]), f = (b - a).Norm; double len = V2.Dist(a, b);
                HQuad(m, (a + b) * 0.5, f, len / 2, 1.4, y + 0.004, pth);
                for (double t = 6; t < len - 2; t += 12) HQuad(m, a + f * t, f * -1, 0.5, 0.5, y + 0.008, HA("x_arrow"));
            }
            // breakables and things to take
            HStack("Mugs", "glass", 88.0, 99.8, y, "p_COOK0", "pine");
            HStack("Plates", "glass", 114.0, 86.0, y, "p_GLASS0", "pine");
            HStack("Vases", "glass", 90.0, 51.0, y, "p_GLASS1", "pine");
            HStack("Candles", "stack", 70.0, 51.0, y, "p_CANDLE1", "pine");
            HItem("knives", "Chef's knife block", true, 110.0, 103.0, y + 1.0, 110.0, 101.5, "steelblade");
            HItem("candles", "Scented candle set", true, 70.0, 55.0, y + 1.0, 70.0, 53.5, "wax");
            HItem("lamp", "Designer table lamp", true, 90.0, 65.0, y + 0.75, 90.0, 63.5, "brass");
            HItem("rug", "Rolled-up rug", false, 146.0, 79.0, y + 0.82, 146.0, 77.5, "f_rust");
            HItem("toolkit", "Flat-pack toolkit", false, 116.0, 41.4, y + 0.62, 116.0, 40.2, "orange");
            HStaff(102, 84.0, 1, 0); HStaff(126, 60.0, -1, 0);
            // lights under the slab
            for (double v = 28; v < HD - 2; v += 6) SBox(hLights0, Q(111, v), EU, 50, 0.08, HC0 - 0.08, HC0 - 0.02, new UVRect(0.5, 0.5, 0.5, 0.5), true);
        }

        // ------------------------------------------------------------------ the self-serve warehouse
        static readonly double[] RackU = { 7.0, 13.0, 19.0, 25.0, 31.0, 37.0, 43.0 };
        static readonly double[] AisleU = { 3.9, 10.0, 16.0, 22.0, 28.0, 34.0, 40.0, 46.7 };
        static readonly double[][] RackV = { new[] { 34.0, 66 }, new[] { 70.0, 104 } };

        static void HomeWarehouse()
        {
            hLevel = 0;
            MeshData m = HPart("Warehouse"); hd = m;
            double y = HY0;
            double[] lv0 = { y, 2.5, 4.84, 7.18 }, lv1 = { 2.36, 4.7, 7.04, 9.38 };
            for (int r = 0; r < RackU.Length; r++)
            {
                double uc = RackU[r];
                foreach (double[] sv in RackV)
                {
                    int bays = (int)Math.Round((sv[1] - sv[0]) / 2.65);
                    double bw = (sv[1] - sv[0]) / bays;
                    HB(m, uc - 1.2, uc + 1.2, sv[0], sv[1], 9.45, 9.5, "dark");
                    foreach (double v in new[] { sv[0], sv[1] })
                    {
                        HB(m, uc - 1.2, uc + 1.2, v - 0.05, v + 0.05, y, 9.5, "rack_blue");
                        HB(m, uc - 1.2, uc + 1.2, v - 0.06, v + 0.06, 2.36, 2.5, "rack_orange");
                    }
                    for (int b = 0; b < bays; b++)
                    {
                        double v0 = sv[0] + b * bw, v1 = v0 + bw;
                        int bayNo = (sv[0] < 50 ? 0 : 12) + b + 1;
                        foreach (int side in new[] { -1, 1 })
                        {
                            V2 n = EU * side;
                            for (int l = 0; l < 4; l++)
                            {
                                string tile = l == 0 ? "k_flat" + hr.Next(4) : "k_pal" + hr.Next(2);
                                VQuad(m, Q(uc + side * 1.2, v0), Q(uc + side * 1.2, v1), lv0[l], lv1[l], HA(tile), n);
                            }
                            VQuad(m, Q(uc + side * 1.215, (v0 + v1) / 2 - 0.24), Q(uc + side * 1.215, (v0 + v1) / 2 + 0.24), 2.55, 2.9, HA("y_" + Math.Min(26, bayNo)), n);
                        }
                    }
                    HCol("Racking", uc - 1.2, uc + 1.2, sv[0] - 0.06, sv[1] + 0.06, y, 9.5);
                    si.walls.Add(new[] { Q(uc - 1.2, sv[0]).x, Q(uc - 1.2, sv[0]).z, Q(uc - 1.2, sv[1]).x, Q(uc - 1.2, sv[1]).z, y, 9.5 });
                    si.walls.Add(new[] { Q(uc + 1.2, sv[0]).x, Q(uc + 1.2, sv[0]).z, Q(uc + 1.2, sv[1]).x, Q(uc + 1.2, sv[1]).z, y, 9.5 });
                }
                // floor lines along the faces
                foreach (int side in new[] { -1, 1 }) HQuad(m, Q(uc + side * 1.4, 69), EV, 35.5, 0.05, y + 0.005, HA("s_lane"));
            }
            HQuad(m, Q(5.6, 69), EV, 37.5, 0.06, y + 0.005, HA("s_lane"));
            // the aisle numbers (yellow boards hanging over each aisle, front, middle and back)
            for (int a = 0; a < AisleU.Length; a++)
                foreach (double v in new[] { 33.2, 68.0, 106.0 })
                    HSign(m, AisleU[a], v, EU, 1.8, 0.9, 5.2, "n_" + (a + 1), true);
            // the large items: wrapped boxes on pallets, along the east wall
            HSign(m, 53.3, 46.0, EU, 3.2, 0.4, 4.2, "g_LARGE ITEMS");
            for (double v = 47; v < 103; v += 3.2)
            {
                double h = 0.6 + ((int)(v * 7) % 5) * 0.25;
                HB(m, 49.8, 56.8, v, v + 2.6, y, y + 0.14, "pine");
                HB(m, 50.0, 56.6, v + 0.1, v + 2.5, y + 0.14, y + 0.14 + h, "cardboard");
                HQuad(m, Q(53.3, v + 1.3), EV, 1.18, 3.28, y + 0.142 + h, HA("s_wrap"));
                HSpot(48.6, v + 1.3, 50.5, v + 1.3);
            }
            HCol("LargeItems", 49.8, 56.8, 47, 103.4, y, y + 1.8);
            si.walls.Add(new[] { Q(49.8, 47).x, Q(49.8, 47).z, Q(49.8, 103.4).x, Q(49.8, 103.4).z, y, y + 1.8 });
            // flat trolleys by the way in
            for (int k = 0; k < 5; k++)
            {
                double u = 50.6 + k * 1.3;
                HB(m, u - 0.4, u + 0.4, 41.4, 43.6, y + 0.2, y + 0.26, "charcoal");
                HB(m, u - 0.4, u + 0.4, 43.5, 43.6, y + 0.26, y + 1.0, "steel");
                HB(m, u - 0.35, u + 0.35, 41.6, 43.4, y, y + 0.2, "black");
            }
            HCol("FlatTrolleys", 50.2, 56.6, 41.4, 43.6, y, y + 1.0);
            // forklifts, parked
            Forklift(m, 1.6, 52.0, EV); Forklift(m, 1.6, 92.0, EV); Forklift(m, 53.0, 106.6, EU * -1);
            // the dock doors from inside, and the market hall way in
            foreach (double v in new[] { 45.8, 64.2, 82.5, 100.8 })
            {
                VQuad(m, Q(0.37, v - 1.6), Q(0.37, v + 1.6), y, y + 3.4, HA("k_shutter"), EU);
                foreach (int k in new[] { -1, 1 }) { HB(m, 0.5, 0.75, v + k * 1.9 - 0.12, v + k * 1.9 + 0.12, y, y + 1.0, "lane"); }
            }
            VQuad(m, Q(57.83, 34), Q(57.83, 38), 4.5, 5.0, HA("g_SELF SERVE"), EU * -1);
            HStaff(3.4, 56.5, 0, -1); HStaff(48.4, 80.0, 1, 0);
            // aisles: people looking at the shelves
            for (int a = 0; a < AisleU.Length; a++)
            {
                double ua = AisleU[a];
                foreach (double[] sv in RackV)
                    for (double v = sv[0] + 2.5; v < sv[1] - 1.5; v += 6.0)
                    {
                        if (a > 0) HSpot(ua - 0.9, v, ua - 3.0, v);
                        if (a < RackU.Length) HSpot(ua + 0.9, v + 2.0, ua + 3.0, v + 2.0);
                    }
            }
            // high bay lights, the roof's trusses
            foreach (double ua in AisleU) SBox(hLights0, Q(ua, 69), EV, 36, 0.08, 12.5, 12.6, new UVRect(0.5, 0.5, 0.5, 0.5), true);
            for (double v = 35; v < 109; v += 10) HB(m, Hw, 57.85, v - 0.1, v + 0.1, 12.6, 12.95, "steel", true);
        }

        static void Forklift(MeshData m, double u, double v, V2 fwd)
        {
            HFrame F = new HFrame { o = Q(u, v), a = fwd.Left, b = fwd * -1, y = HY0, m = m };       // the forks point forward (−b)
            FB(F, -0.55, 0.55, -0.7, 0.9, 0.15, 1.05, "forklift");
            FB(F, -0.58, 0.58, 0.6, 1.1, 0.15, 1.2, "charcoal");                                         // the counterweight
            foreach (int i in new[] { -1, 1 }) foreach (double z in new[] { -0.5, 0.75 }) FB(F, i * 0.6 - 0.12, i * 0.6 + 0.12, z - 0.27, z + 0.27, 0, 0.5, "rubber");
            foreach (int i in new[] { -1, 1 }) foreach (double z in new[] { -0.65, 0.85 }) FB(F, i * 0.5 - 0.03, i * 0.5 + 0.03, z - 0.03, z + 0.03, 1.05, 2.2, "black");
            FB(F, -0.55, 0.55, -0.7, 0.9, 2.2, 2.26, "black");
            FB(F, -0.25, 0.25, 0.1, 0.5, 1.05, 1.5, "black");
            foreach (int i in new[] { -1, 1 }) FB(F, i * 0.35 - 0.05, i * 0.35 + 0.05, -0.85, -0.75, 0.1, 2.6, "charcoal");
            FB(F, -0.4, 0.4, -0.88, -0.82, 0.1, 1.2, "charcoal");
            foreach (int i in new[] { -1, 1 }) FB(F, i * 0.3 - 0.05, i * 0.3 + 0.05, -2.1, -0.85, 0.08, 0.13, "charcoal");
            FC(F, "Forklift", -0.62, 0.62, -2.1, 1.1, 0, 2.3);
        }

        // ================================================================== where people walk
        static MarketInfo hmi;

        static int HNode(double u, double v, double y)
        {
            V2 p = Q(u, v);
            for (int i = 0; i < hmi.nodes.Count; i++)
                if (Math.Abs(si.nodeY[i] - y) < 0.3 && V2.Dist(hmi.nodes[i], p) < 0.3) return i;
            hmi.nodes.Add(p); si.nodeY.Add(y);
            return hmi.nodes.Count - 1;
        }

        static void HEdge(int a, int b)
        {
            if (a == b) return;
            foreach (int[] e in hmi.edges) if ((e[0] == a && e[1] == b) || (e[0] == b && e[1] == a)) return;
            hmi.edges.Add(new[] { a, b });
        }

        /// A walkway: nodes every `step` or less along the polyline (u, v pairs), joined up; returns the nodes.
        static List<int> HChain(double y, double step, params double[] pts)
        {
            List<int> list = new List<int>();
            for (int i = 0; i + 3 < pts.Length; i += 2)
            {
                double u0 = pts[i], v0 = pts[i + 1], u1 = pts[i + 2], v1 = pts[i + 3];
                double len = Math.Sqrt((u1 - u0) * (u1 - u0) + (v1 - v0) * (v1 - v0));
                int n = Math.Max(1, (int)Math.Ceiling(len / step - 1e-6));
                for (int k = (i == 0 ? 0 : 1); k <= n; k++)
                {
                    int id = HNode(u0 + (u1 - u0) * k / n, v0 + (v1 - v0) * k / n, y);
                    if (list.Count > 0) HEdge(list[list.Count - 1], id);
                    list.Add(id);
                }
            }
            return list;
        }

        /// Joins the node at (u, v) to the nearest one of `among`.
        static void HJoin(int a, List<int> among)
        {
            int best = -1; double bd = double.MaxValue;
            foreach (int k in among) { double d = V2.Dist(hmi.nodes[k], hmi.nodes[a]); if (d < bd && k != a) { bd = d; best = k; } }
            if (best >= 0) HEdge(a, best);
        }

        /// A route up or down: points (u, v, y) in order, joined.
        static List<int> HRoute(params double[] p)
        {
            List<int> list = new List<int>();
            for (int i = 0; i + 2 < p.Length; i += 3)
            {
                int id = HNode(p[i], p[i + 1], p[i + 2]);
                if (list.Count > 0) HEdge(list[list.Count - 1], id);
                list.Add(id);
            }
            return list;
        }

        static void HomeCrowd()
        {
            hmi = si.crowd;
            double y0 = HY0, y1 = HY1;
            double lowA = 8.0, highA = 8.0 + EscRun, highB = 92.0, lowB = 92.0 + EscRun;
            double stairGo = EscRun / Math.Ceiling((HY1 - HY0) / 0.18);
            // ---- outside, the doors, the way in
            int gate = HNode(DoorU, -6, y0);
            hmi.gates.Add(gate);
            HRoute(DoorU, -6, y0, DoorU, 1.6, y0);
            HChain(y0, 4, DoorU, 1.6, 37, 4, 24, 4, 18, 4, 13, 4);
            HChain(y0, 4, 37, 4, 33, 4.6, 33, 8.8, 29.0, 8.8);
            HChain(y0, 4, 33, 8.8, 33, 11.5);
            HChain(y0, 4, 24, 4, 24, 22, 23.5, 23.6);
            HChain(y0, 4, 24, 4, 2.0, 5.0);
            HChain(y0, 4, 24, 22, 26.0, 26.5);
            // the escalator and the stairs up
            HRoute(18, 4, y0, 18, lowA - EscLand, y0, 18, lowA, y0, 18, highA, y1, 18, highA + EscLand, y1, 18, 23.5, y1);
            HRoute(13, 4, y0, 13, lowA - stairGo / 2 - 0.3, y0, 13, lowA - stairGo / 2, y0, 13, highA - stairGo / 2, y1, 13, highA + 0.3, y1, 13, 23.5, y1);
            // ---- the showroom
            HChain(y1, 4, 13, 23.5, 18, 23.5, 22, 23.5, 22, 15);
            HChain(y1, 4, 22, 23.5, 23.5, 23.5);
            HChain(y1, 4, 13, 23.5, 8.6, 24.2);
            HChain(y1, 4, 8.6, 24.2, 6.3, 23.0);
            HChain(y1, 4, 22, 15, 158, 15, 158, 35, 66, 35, 66, 55, 158, 55, 158, 75, 66, 75, 66, 87, 150, 87, 150, highB - EscLand - 0.6, 150, highB - EscLand, y1);
            HRoute(150, highB - EscLand, y1, 150, highB, y1, 150, lowB, y0, 150, lowB + EscLand, y0, 150, lowB + EscLand + 0.95, y0);
            foreach (double[] sc in new[] { new[] { 15.0, 35 }, new[] { 35.0, 55 }, new[] { 55.0, 75 }, new[] { 75.0, 87 } }) HChain(y1, 4, 110, sc[0], 110, sc[1]);
            HChain(y1, 4, 66, 35, 62, 37, 60.8, 42.6);
            HChain(y1, 4, 158, 55, 161.5, 60, 161.5, 66);
            // the café
            HChain(y1, 4, 70, 87, 70, 92.5, 146, 92.5, 146, 87);
            HChain(y1, 4, 70, 92.5, 70, 98.0, 120, 98.0, 120, 102.0, 146, 102.0, 146, 92.5);
            HChain(y1, 4, 110, 87, 110, 92.5);
            HChain(y1, 4, 158.5, 93.5, 155.0, 93.5, 152.5, 88.0, 150, 87);
            // ---- the ground floor: the way out, the tills
            List<int> front = HChain(y0, 4, DoorU, 1.6, 42.6, 3.6, 76, 3.6, 76, 10.0);
            HChain(y0, 4, 42.6, 3.6, 42, 10.0);
            List<int> pre = HChain(y0, 4, 26.0, 26.5, 72, 26.5);
            List<int> row10 = HChain(y0, 4, 42, 10.0, 76, 10.0);
            row10.AddRange(HChain(y0, 4, 76, 10.0, 160, 10.0));
            for (int i = 0; i < 8; i++)
            {
                double w = 42.5 + 3.6 * i + 1.8;
                List<int> lane = HRoute(w, 22.6, y0, w, 13.4, y0);
                HJoin(lane[0], pre); HJoin(lane[1], row10);
            }
            List<int> self = HChain(y0, 4, 77.5, 26.0, 74.9, 22.6, 74.9, 21.0, 74.9, 13.4);
            HJoin(self[1], pre); HJoin(self[self.Count - 1], row10);
            // bistro queue and tables, bargains, returns
            List<int> bis = HChain(y0, 4, 57.5, 4.2, 57.5, 7.8, 74.5, 7.8, 74.5, 4.2);
            HJoin(bis[0], front); HJoin(bis[bis.Count - 1], front);
            for (double u = 85; u <= 115.01; u += 6) HJoin(HChain(y0, 4, u, 15.6, u, 11.0)[1], row10);
            for (double u = 131; u <= 152.01; u += 7) HJoin(HChain(y0, 4, u, 16.9, u, 11.0)[1], row10);
            HJoin(HChain(y0, 4, 130.0, 2.0, 130.0, 6.0)[1], row10);
            // ---- the warehouse
            Func<double, double[]> across = v => { List<double> pts = new List<double>(); foreach (double ua in AisleU) { pts.Add(ua); pts.Add(v); } return pts.ToArray(); };
            List<double> fpts = new List<double>(across(32.0)); fpts.AddRange(new[] { 56.0, 32.0, 56.0, 36.0, 62.0, 36.0 });
            List<int> wf = HChain(y0, 4, fpts.ToArray());
            for (double u = 28; u <= 56.01; u += 4) { int a = HNode(u, 29.0, y0); HJoin(a, wf); HJoin(a, pre); }
            foreach (double ua in AisleU) HChain(y0, 4, ua, 32.0, ua, 68.0, ua, 106.8);
            HChain(y0, 4, across(68.0));
            HChain(y0, 4, across(106.8));
            HChain(y0, 4, AisleU[7], 36.0, 56.0, 36.0);
            // ---- the market hall: walkways (east-west), the ends (north-south), the cross aisles
            double[] mv = { 106.8, 97.0, 84.0, 72.0, 60.0, 48.0, 36.0 };
            foreach (double v in mv)
            {
                if (v == 106.8) { HChain(y0, 4, 62, v, 78, v, 102, v, 126, v, 146, v, 150, v, 156, v); continue; }
                if (v == 97.0) { HChain(y0, 4, 62, v, 78, v, 102, v, 126, v, 146, v); continue; }
                HChain(y0, 4, 62, v, 78, v, 102, v, 126, v, 156, v);
            }
            Func<double, bool, double[]> down = (u, all) => { List<double> pts = new List<double>(); foreach (double v in mv) { if (!all && v == 97.0) continue; pts.Add(u); pts.Add(v); } return pts.ToArray(); };
            HChain(y0, 4, down(62, true));
            List<int> east = HChain(y0, 4, down(156, false));
            HChain(y0, 4, 146, 97.0, 146, 106.8);
            foreach (double u in new[] { 78.0, 102.0, 126.0 }) HChain(y0, 4, down(u, true));
            HChain(y0, 4, 62, 36.0, 62, 28.0, 72, 26.5);
            HChain(y0, 4, 78.0, 36.0, 78.0, 28.5, 72, 26.5);
            HChain(y0, 4, 78, 28.5, 102, 28.5, 126, 28.5, 156, 28.5);
            foreach (double u in new[] { 102.0, 126.0, 156.0 }) HChain(y0, 4, u, 36.0, u, 28.5);
            HJoin(HNode(158.5, 93.5, y0), east);

            // every spot walks from its nearest node on the same floor
            for (int i = 0; i < hmi.spots.Count; i++)
            {
                double[] sp = hmi.spots[i];
                int best = 0; double bd = double.MaxValue;
                for (int k = 0; k < hmi.nodes.Count; k++)
                {
                    if (Math.Abs(si.nodeY[k] - spotY[i]) > 0.5) continue;
                    double d = V2.Dist(hmi.nodes[k], new V2(sp[0], sp[1]));
                    if (d < bd) { bd = d; best = k; }
                }
                sp[4] = best;
            }
        }
    }
}
