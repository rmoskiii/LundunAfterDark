// E7: the Lundun Wheel. A giant observation wheel on the south bank of the river in the middle of the map, just east
// of Rye Bridge: about 133 m to the top, a 120 m rim on cable spokes, 32 glass capsules, two A-frame legs either side,
// and a boarding platform on the road side. An original design (not any real wheel's), built from the building kit's
// atlas B swatches and atlas A's sign strip.
// Everything here is in the Wheel's own space: x along the river, y up, z across (towards the water). Lundun → 27 puts
// it at WheelSite and turns it to WheelYaw. No random numbers, and nothing else in the city depends on it.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        // ------------------------------------------------------------------ where and how big
        /// The Wheel's centre on the ground, and the direction it faces along the river (chosen so the legs and the
        /// platform keep 7.5 m from both the road and the river wall).
        public static readonly V2 WheelSite = new V2(880, 538);
        public const double WheelAngle = -24;            // degrees: the along-the-river direction, anticlockwise from east
        public static V2 WheelAlong { get { double a = WheelAngle * Math.PI / 180; return new V2(Math.Cos(a), Math.Sin(a)); } }
        public static V2 WheelAcross { get { return WheelAlong.Left; } }          // towards the water
        /// Unity yaw (degrees) for the Wheel's root so its local x runs along the river and z towards the water.
        public static double WheelYaw { get { V2 a = WheelAlong; return Math.Atan2(-a.z, a.x) * 180 / Math.PI; } }
        /// A point in the Wheel's space (x along, z across) on the map.
        public static V2 WheelToWorld(double x, double z) { return WheelSite + WheelAlong * x + WheelAcross * z; }

        public const double WheelHubY = 66.9, WheelRimR = 60, WheelCapsuleR = 64, WheelRimHalf = 3, WheelAxleHalf = 12.5;
        public const int WheelCapsules = 32, WheelSegments = 64;
        public const double WheelLegAlong = 22, WheelLegAcross = 12, WheelPlatformY = 1.2;
        public const double WheelPlatformNear = -2.4, WheelPlatformFar = -10.0, WheelPlatformHalf = 13;
        /// Where you walk up to board (the top of the steps) and where you get off.
        public static double[] WheelBoardSpot { get { return new[] { 0.0, WheelPlatformY, WheelPlatformFar + 1.2 }; } }
        public static double[] WheelExitSpot { get { return new[] { 8.0, WheelPlatformY, -4.5 }; } }
        /// The gate on the road side (for the map and GPS).
        public static double[] WheelGate { get { return new[] { 0.0, 0.0, WheelPlatformFar - 4.5 }; } }

        // ------------------------------------------------------------------ the parts
        /// The parts that don't move: legs, footings, axle housings, the platform, its steps, railings and signs.
        /// Collider meshes. walk = invisible walking surfaces (the steps' ramp).
        public static MeshData WheelStatic(out MeshData walk)
        {
            MeshData m = new MeshData("WheelStatic", "Wheel", KitMatB, true) { navArea = 1 };
            walk = new MeshData("WheelWalkways", "Wheel", "ColliderOnly", true) { navArea = 1 };
            int steel = KitArt.S_White, conc = KitArt.S_PaleConc;
            // legs: an A on each side, from the footings up to the ends of the axle
            foreach (int side in new[] { -1, 1 })
            {
                P3 top = WPt(0, WheelHubY, side * (WheelAxleHalf + 0.6));
                P3 f0 = WPt(-WheelLegAlong, 0, side * WheelLegAcross), f1 = WPt(WheelLegAlong, 0, side * WheelLegAcross);
                WBeam(m, steel, f0, top, 1.5);
                WBeam(m, steel, f1, top, 1.5);
                foreach (double h in new[] { 18.0, 42.0 })                  // cross-ties
                {
                    double t = h / WheelHubY;
                    WBeam(m, steel, WLerp(f0, top, t), WLerp(f1, top, t), 0.8);
                }
                WBeam(m, steel, WLerp(f0, top, 0.27), WLerp(f1, top, 0.62), 0.5);       // a brace
                WBeam(m, steel, WLerp(f1, top, 0.27), WLerp(f0, top, 0.62), 0.5);
                foreach (P3 f in new[] { f0, f1 }) WBlock(m, conc, f.x, f.z, 2.4, 2.4, -0.3, 1.4);   // footings
                WBlock(m, KitArt.S_LightGrey, 0, side * (WheelAxleHalf + 0.6), 1.8, 1.6, WheelHubY - 1.8, WheelHubY + 1.8);   // bearing housing
            }
            // the platform on the road side of the capsules' path
            double z0 = WheelPlatformFar, z1 = WheelPlatformNear, xh = WheelPlatformHalf, y = WheelPlatformY;
            WBoxXZ(m, KitArt.S_LightGrey, conc, -xh, xh, z0, z1, 0, y);
            // the edge strip by the capsules
            WQuad(m, KitArt.S_Yellow, WPt(-xh, y + 0.005, z1 - 0.5), WPt(xh, y + 0.005, z1 - 0.5), WPt(xh, y + 0.005, z1 - 0.2), WPt(-xh, y + 0.005, z1 - 0.2), 0, 1, 0);
            // steps down to the road side (7 × 17 cm), and the smooth ramp you actually walk on
            int steps = 7; double rise = y / steps, go = 0.3;
            for (int k = 0; k < steps; k++)
            {
                double zz = z0 - (k + 1) * go;
                WBoxXZ(m, KitArt.S_LightGrey, conc, -4, 4, zz, zz + go, 0, y - (k + 1) * rise + 0.001 + rise);
            }
            WRamp3(walk, -4, 4, z0 - steps * go, z0, 0, y);
            WRamp3(walk, -xh, xh, z0, z1, y, y);
            // railings round the platform (not the capsule side, not the steps)
            WRail(m, -xh, z0, -xh, z1, y);
            WRail(m, xh, z0, xh, z1, y);
            WRail(m, -xh, z0, -4, z0, y);
            WRail(m, 4, z0, xh, z0, y);
            // queue lanes on the platform
            for (int k = 0; k < 2; k++) WRail(m, -11, z0 + 2.2 + k * 2.2, -1, z0 + 2.2 + k * 2.2, y, 0.9);
            // ticket kiosk (left of the steps) and the name board over the gate
            WBlock(m, KitArt.S_Teal, -9.5, z0 - 3.5, 1.6, 1.2, 0, 2.6);
            WBlock(m, KitArt.S_White, -9.5, z0 - 3.5, 1.8, 1.4, 2.6, 2.8);
            WSign(m, -11.1, -7.9, z0 - 4.72, 1.7, 2.3, 1, -1);                                 // TICKETS
            foreach (double x in new[] { -4.2, 4.2 }) WBlock(m, KitArt.S_DarkGrey, x, z0 - 2.4, 0.12, 0.12, 0, 4.4);
            WSign(m, -4.2, 4.2, z0 - 2.4, 3.2, 4.4, 0, -1);                                    // THE LUNDUN WHEEL (road side)
            WSign(m, -4.2, 4.2, z0 - 2.4, 3.2, 4.4, 2, 1);                                     // BOARDING (platform side)
            WSign(m, 6.5, 10.5, z1 - 1.6, 2.2, 3.0, 3, -1);                                    // EXIT
            foreach (double x in new[] { 6.6, 10.4 }) WBlock(m, KitArt.S_DarkGrey, x, z1 - 1.6, 0.08, 0.08, y, 3.0);
            return m;
        }

        /// The turning part: hub, two rings of rim with diagonals between them, and the cable spokes. Centred on the
        /// hub (y 0 = hub height); it turns about its z axis. lights = the rim's outline lights.
        public static MeshData WheelRotor(out MeshData lights)
        {
            MeshData m = new MeshData("WheelRotor", "Wheel", KitMatB, false) { navIgnore = true };
            lights = new MeshData("WheelRotorLights", "Wheel", "WheelLights", false) { navIgnore = true };
            int steel = KitArt.S_White;
            WCylinder(m, KitArt.S_LightGrey, 2.4, -WheelAxleHalf + 1, WheelAxleHalf - 1, 16);    // hub
            WCylinder(m, KitArt.S_White, 1.0, -WheelAxleHalf - 0.6, WheelAxleHalf + 0.6, 10);   // axle
            int n = WheelSegments;
            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                foreach (int side in new[] { -1, 1 })
                {
                    WBeam(m, steel, WRimPt(a0, side), WRimPt(a1, side), 0.75);
                    WBeam(m, steel, WPt(0, 0, side * (WheelAxleHalf - 1.2)), WRimPt(a0, side), 0.12);   // spoke
                    if (i % 2 == 0) WBlock3(lights, WRimPt(a0, side, WheelRimR + 0.55), 0.45);
                }
                WBeam(m, steel, WRimPt(a0, 1), WRimPt(a1, -1), 0.3);                                   // diagonals
                WBeam(m, steel, WRimPt(a0, 1), WRimPt(a0, -1), 0.35);
                // the capsule mounts: a short arm from the rim to where each capsule hangs
                if (i % (n / WheelCapsules) == 0)
                {
                    P3 c = WPt(Math.Sin(a0) * (WheelCapsuleR - 2.0), -Math.Cos(a0) * (WheelCapsuleR - 2.0), 0);
                    WBeam(m, KitArt.S_LightGrey, WRimPt(a0, 1, WheelRimR + 0.3), c, 0.35);
                    WBeam(m, KitArt.S_LightGrey, WRimPt(a0, -1, WheelRimR + 0.3), c, 0.35);
                }
            }
            return m;
        }

        /// One capsule, centred: a glass pod 7.4 m long (along the river), 3.4 m wide and 3.4 m tall, white frame,
        /// a glowing band round its waist at night (lights).
        public static MeshData WheelCapsule(out MeshData lights)
        {
            MeshData m = new MeshData("WheelCapsule", "Wheel", KitMatB, false) { navIgnore = true };
            lights = new MeshData("WheelCapsuleLights", "Wheel", "WheelLights", false) { navIgnore = true };
            // octagonal section along x
            double hl = 3.0, w = 1.7, h = 1.7, ch = 0.55;
            double[][] sec = { new[] { -w + ch, -h }, new[] { w - ch, -h }, new[] { w, -h + ch }, new[] { w, h - ch }, new[] { w - ch, h }, new[] { -w + ch, h }, new[] { -w, h - ch }, new[] { -w, -h + ch } };
            for (int i = 0; i < 8; i++)
            {
                double[] a = sec[i], b = sec[(i + 1) % 8];
                int sw = i == 0 ? KitArt.S_DarkGrey : i == 4 ? KitArt.S_White : KitArt.S_SkyGlass;
                double nz = (a[0] + b[0]) / 2, ny = (a[1] + b[1]) / 2;
                WQuad(m, sw, WPt(-hl, a[1], a[0]), WPt(hl, a[1], a[0]), WPt(hl, b[1], b[0]), WPt(-hl, b[1], b[0]), 0, ny, nz);
            }
            // tapered glass ends
            foreach (int e in new[] { -1, 1 })
            {
                double xe = e * (hl + 0.8), k = 0.55;
                for (int i = 0; i < 8; i++)
                {
                    double[] a = sec[i], b = sec[(i + 1) % 8];
                    int sw = i == 0 ? KitArt.S_DarkGrey : KitArt.S_SkyGlass;
                    WQuad(m, sw, WPt(e * hl, a[1], a[0]), WPt(e * hl, b[1], b[0]), WPt(xe, b[1] * k, b[0] * k), WPt(xe, a[1] * k, a[0] * k), e, (a[1] + b[1]) * 0.3, (a[0] + b[0]) * 0.3);
                }
                List<P3> cap = new List<P3>();
                foreach (double[] q in sec) cap.Add(WPt(xe, q[1] * k, q[0] * k));
                for (int i = 1; i + 1 < 8; i++) WTri(m, KitArt.S_SkyGlass, cap[0], cap[i], cap[i + 1], e, 0, 0);
            }
            // white ribs
            foreach (double x in new[] { -3.0, -1.0, 1.0, 3.0 })
                for (int i = 0; i < 8; i++)
                {
                    double[] a = sec[i], b = sec[(i + 1) % 8];
                    WBeam(m, KitArt.S_White, WPt(x, a[1] * 1.02, a[0] * 1.02), WPt(x, b[1] * 1.02, b[0] * 1.02), 0.12);
                }
            // the glowing waist band
            foreach (int s in new[] { -1, 1 }) WBlock3(lights, WPt(0, -0.85, s * (w + 0.03)), 0.12, hl, 0.07, 0.02);
            foreach (int e in new[] { -1, 1 }) WBlock3(lights, WPt(e * (hl + 0.4), -0.85, 0), 0.12, 0.02, 0.07, w * 0.8);
            return m;
        }

        /// A simple version for seeing from across the city (drawn on the Landmark layer, fog held back):
        /// legs (farStatic), and the rim, spokes and capsules (farRotor, centred on the hub), with their lights.
        public static MeshData WheelFar(out MeshData farRotor, out MeshData farLights)
        {
            MeshData st = new MeshData("WheelFarStatic", "Wheel", "Landmark_Far", false) { navIgnore = true };
            farRotor = new MeshData("WheelFarRotor", "Wheel", "Landmark_Far", false) { navIgnore = true };
            farLights = new MeshData("WheelFarLights", "Wheel", "Landmark_FarLights", false) { navIgnore = true };
            foreach (int side in new[] { -1, 1 })
            {
                P3 top = WPt(0, WheelHubY, side * (WheelAxleHalf + 0.6));
                WBeam(st, KitArt.S_White, WPt(-WheelLegAlong, 0, side * WheelLegAcross), top, 2.4);
                WBeam(st, KitArt.S_White, WPt(WheelLegAlong, 0, side * WheelLegAcross), top, 2.4);
            }
            int n = 32;
            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                foreach (int side in new[] { -1, 1 })
                {
                    WBeam(farRotor, KitArt.S_White, WRimPt(a0, side), WRimPt(a1, side), 1.6);
                    if (i % 2 == 0) WBeam(farRotor, KitArt.S_LightGrey, WPt(0, 0, side * (WheelAxleHalf - 1.2)), WRimPt(a0, side), 0.5);
                    WBlock3(farLights, WRimPt(a0, side, WheelRimR + 0.9), 1.1);
                }
                P3 c = WPt(Math.Sin(a0) * WheelCapsuleR, -Math.Cos(a0) * WheelCapsuleR, 0);
                WBlock3(farRotor, c, 1.0, 3.6, 1.7, 1.7, KitArt.S_SkyGlass);
            }
            WCylinder(farRotor, KitArt.S_LightGrey, 2.6, -WheelAxleHalf, WheelAxleHalf, 8);
            return st;
        }

        // ------------------------------------------------------------------ helpers (Wheel space, kit swatches)
        static P3 WPt(double x, double y, double z) { return new P3(x, y, z, 0, 0); }
        static P3 WLerp(P3 a, P3 b, double t) { return WPt(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t); }
        /// A point on the rim at angle a (0 = bottom), on one side's ring. y relative to the hub.
        static P3 WRimPt(double a, int side, double r = WheelRimR) { return WPt(Math.Sin(a) * r, -Math.Cos(a) * r, side * WheelRimHalf); }

        static void WSw(int sw, out double u, out double v) { if (sw < 0) { u = 0.5; v = 0.5; } else KitArt.SwatchUV(sw, out u, out v); }

        static void WQuad(MeshData m, int sw, P3 a, P3 b, P3 c, P3 d, double nx, double ny, double nz)
        {
            double u, v; WSw(sw, out u, out v);
            a.u = b.u = c.u = d.u = u; a.v = b.v = c.v = d.v = v;
            m.Quad(a, b, c, d, nx, ny, nz);
        }

        static void WTri(MeshData m, int sw, P3 a, P3 b, P3 c, double nx, double ny, double nz)
        {
            double u, v; WSw(sw, out u, out v);
            a.u = b.u = c.u = u; a.v = b.v = c.v = v;
            m.Tri(a, b, c, nx, ny, nz);
        }

        /// A square-section beam from a to b, w wide.
        static void WBeam(MeshData m, int sw, P3 a, P3 b, double w)
        {
            double dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z, L = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (L < 1e-6) return;
            dx /= L; dy /= L; dz /= L;
            // two directions across the beam
            double ux, uy, uz;
            if (Math.Abs(dy) < 0.9) { ux = -dz; uy = 0; uz = dx; } else { ux = 0; uy = dz; uz = -dy; }
            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz); ux /= ul; uy /= ul; uz /= ul;
            double vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
            double h = w / 2;
            double[][] off = { new[] { -h, -h }, new[] { h, -h }, new[] { h, h }, new[] { -h, h } };
            for (int i = 0; i < 4; i++)
            {
                double[] p = off[i], q = off[(i + 1) % 4];
                Func<P3, double[], P3> O = (o, k) => WPt(o.x + ux * k[0] + vx * k[1], o.y + uy * k[0] + vy * k[1], o.z + uz * k[0] + vz * k[1]);
                double nx = ux * (p[0] + q[0]) + vx * (p[1] + q[1]), ny = uy * (p[0] + q[0]) + vy * (p[1] + q[1]), nz = uz * (p[0] + q[0]) + vz * (p[1] + q[1]);
                WQuad(m, sw, O(a, p), O(b, p), O(b, q), O(a, q), nx, ny, nz);
            }
        }

        /// An upright box centred at (x, z), half sizes hx (along) and hz (across), from y0 to y1.
        static void WBlock(MeshData m, int sw, double x, double z, double hx, double hz, double y0, double y1)
        {
            WBoxXZ(m, sw, sw, x - hx, x + hx, z - hz, z + hz, y0, y1);
        }

        static void WBoxXZ(MeshData m, int side, int top, double x0, double x1, double z0, double z1, double y0, double y1)
        {
            WQuad(m, side, WPt(x0, y0, z0), WPt(x1, y0, z0), WPt(x1, y1, z0), WPt(x0, y1, z0), 0, 0, -1);
            WQuad(m, side, WPt(x0, y0, z1), WPt(x1, y0, z1), WPt(x1, y1, z1), WPt(x0, y1, z1), 0, 0, 1);
            WQuad(m, side, WPt(x0, y0, z0), WPt(x0, y0, z1), WPt(x0, y1, z1), WPt(x0, y1, z0), -1, 0, 0);
            WQuad(m, side, WPt(x1, y0, z0), WPt(x1, y0, z1), WPt(x1, y1, z1), WPt(x1, y1, z0), 1, 0, 0);
            WQuad(m, top, WPt(x0, y1, z0), WPt(x1, y1, z0), WPt(x1, y1, z1), WPt(x0, y1, z1), 0, 1, 0);
        }

        /// A small box centred on c (half sizes hx, hy, hz; one number = a cube), with a swatch (sw < 0 = plain).
        static void WBlock3(MeshData m, P3 c, double s, double hx = -1, double hy = -1, double hz = -1, int sw = -1)
        {
            if (hx < 0) { hx = hy = hz = s / 2; }
            double x0 = c.x - hx, x1 = c.x + hx, y0 = c.y - hy, y1 = c.y + hy, z0 = c.z - hz, z1 = c.z + hz;
            WBoxXZ(m, sw, sw, x0, x1, z0, z1, y0, y1);
            WQuad(m, sw, WPt(x0, y0, z0), WPt(x1, y0, z0), WPt(x1, y0, z1), WPt(x0, y0, z1), 0, -1, 0);
        }

        /// A cylinder round the z axis (the hub), radius r, from z0 to z1, with end caps.
        static void WCylinder(MeshData m, int sw, double r, double z0, double z1, int n)
        {
            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                P3 p0 = WPt(Math.Cos(a0) * r, Math.Sin(a0) * r, z0), p1 = WPt(Math.Cos(a1) * r, Math.Sin(a1) * r, z0);
                P3 q0 = WPt(p0.x, p0.y, z1), q1 = WPt(p1.x, p1.y, z1);
                WQuad(m, sw, p0, p1, q1, q0, Math.Cos((a0 + a1) / 2), Math.Sin((a0 + a1) / 2), 0);
                WTri(m, sw, WPt(0, 0, z0), p0, p1, 0, 0, -1);
                WTri(m, sw, WPt(0, 0, z1), q0, q1, 0, 0, 1);
            }
        }

        /// A railing along the platform from (xa, za) to (xb, zb): posts every 2 m and two rails.
        static void WRail(MeshData m, double xa, double za, double xb, double zb, double y, double h = 1.1)
        {
            double L = Math.Sqrt((xb - xa) * (xb - xa) + (zb - za) * (zb - za));
            int n = Math.Max(1, (int)Math.Ceiling(L / 2));
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                WBeam(m, KitArt.S_DarkGrey, WPt(xa + (xb - xa) * t, y, za + (zb - za) * t), WPt(xa + (xb - xa) * t, y + h, za + (zb - za) * t), 0.06);
            }
            foreach (double hh in new[] { h, h * 0.5 }) WBeam(m, KitArt.S_DarkGrey, WPt(xa, y + hh, za), WPt(xb, y + hh, zb), 0.05);
        }

        /// A sign board from x0 to x1 at z, y0 to y1, showing unit u of the Wheel's sign strip, facing face (−1 road side, +1 water side).
        static void WSign(MeshData m, double x0, double x1, double z, double y0, double y1, int u, int face)
        {
            double t = 0.06;
            WBoxXZ(m, KitArt.S_DarkGrey, KitArt.S_DarkGrey, x0, x1, z - t, z + t, y0, y1);
            double zz = z + face * (t + 0.005);
            double ua = (u * 6.4) / KitArt.RepeatM, ub = (u * 6.4 + 6.4) / KitArt.RepeatM, va = KitArt.V(KitArt.A_WheelSigns, 0), vb = KitArt.V(KitArt.A_WheelSigns, 1);
            // read left to right from the side it faces
            double xl = face < 0 ? x0 : x1, xr = face < 0 ? x1 : x0;
            signs.Add(new[] { xl, y0, zz, ua, va, xr, y1, zz, ub, vb, face });
        }

        /// Sign faces use atlas A (the sign strip): collected here and emitted by WheelSigns().
        static readonly List<double[]> signs = new List<double[]>();

        /// The sign faces (atlas A). Call after WheelStatic().
        public static MeshData WheelSignFaces()
        {
            MeshData m = new MeshData("WheelSigns", "Wheel", KitMatA, false) { navIgnore = true };
            foreach (double[] s in signs)
            {
                double xl = s[0], y0 = s[1], z = s[2], ua = s[3], va = s[4], xr = s[5], y1 = s[6], ub = s[8], vb = s[9], face = s[10];
                m.Quad(new P3(xl, y0, z, ua, va), new P3(xr, y0, z, ub, va), new P3(xr, y1, z, ub, vb), new P3(xl, y1, z, ua, vb), 0, 0, face);
            }
            signs.Clear();
            return m;
        }

        /// A closed invisible wedge (for walking up steps): x0..x1 along, from z0 (height ya) to z1 (height yb).
        static void WRamp3(MeshData m, double x0, double x1, double z0, double z1, double ya, double yb)
        {
            double rise = yb - ya, len = Math.Max(0.01, Math.Abs(z1 - z0));
            m.Quad(new P3(x0, ya, z0, 0, 0), new P3(x1, ya, z0, 0, 0), new P3(x1, yb, z1, 0, 0), new P3(x0, yb, z1, 0, 0), 0, 1, -rise / len * Math.Sign(z1 - z0));
            m.Quad(new P3(x0, -0.1, z0, 0, 0), new P3(x0, -0.1, z1, 0, 0), new P3(x0, yb, z1, 0, 0), new P3(x0, ya, z0, 0, 0), -1, 0, 0);
            m.Quad(new P3(x1, -0.1, z0, 0, 0), new P3(x1, -0.1, z1, 0, 0), new P3(x1, yb, z1, 0, 0), new P3(x1, ya, z0, 0, 0), 1, 0, 0);
            m.Quad(new P3(x0, -0.1, z0, 0, 0), new P3(x1, -0.1, z0, 0, 0), new P3(x1, ya, z0, 0, 0), new P3(x0, ya, z0, 0, 0), 0, 0, -Math.Sign(z1 - z0));
            m.Quad(new P3(x0, -0.1, z1, 0, 0), new P3(x1, -0.1, z1, 0, 0), new P3(x1, yb, z1, 0, 0), new P3(x0, yb, z1, 0, 0), 0, 0, Math.Sign(z1 - z0));
        }
    }
}
