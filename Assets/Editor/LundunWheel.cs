// E7: the Lundun Wheel. A giant observation wheel on the south bank of the river in the middle of the map, just east
// of Rye Bridge: about 133 m to the top, a 120 m rim on cable spokes, 32 glass capsules, two A-frame legs either side,
// and a boarding platform on the road side. An original design (not any real wheel's), built from the building kit's
// atlas B swatches and atlas A's sign strip.
// Everything here is in the Wheel's own space: x along the river, y up, z across (towards the water). Lundun → 27 puts
// it at WheelSite and turns it to WheelYaw. Nothing else in the city depends on it, and its only random numbers
// (the riders' looks) have their own seed.
// E7b: the cabins light up at night (see-through glass, a lit cabin, people riding), and the Wheel sits on a busy
// riverside plaza (paving, benches, lamps) with paths for the crowd that queues, rides and gets off (LundunWheelCrowd).
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
        /// The parts that don't move: legs, footings, axle housings, railings, kiosk and signs (solid, off the NavMesh).
        /// deck = the boarding platform and its steps (solid, walkable: the crowd and anyone panicking can use it);
        /// walk = invisible walking surfaces (the steps' ramp, the platform top; walkable);
        /// block = an invisible wall round the strip under the turning capsules (solid, off the NavMesh).
        public static MeshData WheelStatic(out MeshData walk, out MeshData deck, out MeshData block)
        {
            MeshData m = new MeshData("WheelStatic", "Wheel", KitMatB, true) { navArea = 1 };
            deck = new MeshData("WheelDeck", "Wheel", KitMatB, true) { navArea = PavementArea };
            walk = new MeshData("WheelWalkways", "Wheel", "ColliderOnly", true) { navArea = PavementArea };
            block = new MeshData("WheelKeepOut", "Wheel", "ColliderOnly", true) { navArea = 1 };
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
            WBoxXZ(deck, KitArt.S_LightGrey, conc, -xh, xh, z0, z1, 0, y);
            // the edge strip by the capsules
            WQuad(deck, KitArt.S_Yellow, WPt(-xh, y + 0.005, z1 - 0.5), WPt(xh, y + 0.005, z1 - 0.5), WPt(xh, y + 0.005, z1 - 0.2), WPt(-xh, y + 0.005, z1 - 0.2), 0, 1, 0);
            // steps down to the road side (7 × 17 cm), and the smooth ramp you actually walk on
            int steps = 7; double rise = y / steps, go = 0.3;
            for (int k = 0; k < steps; k++)
            {
                double zz = z0 - (k + 1) * go;
                WBoxXZ(deck, KitArt.S_LightGrey, conc, -4, 4, zz, zz + go, 0, y - (k + 1) * rise + 0.001 + rise);
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
            // E7b: nobody walks under the capsules: a railing round the strip beneath them, and an invisible wall
            WRail(m, -KeepOutHalf, z1, -KeepOutHalf, KeepOutFar, PlazaY);
            WRail(m, KeepOutHalf, z1, KeepOutHalf, KeepOutFar, PlazaY);
            WRail(m, -KeepOutHalf, KeepOutFar, KeepOutHalf, KeepOutFar, PlazaY);
            WBlock3(block, WPt(0, 1.1, (z1 + KeepOutFar) / 2), 0, KeepOutHalf, 1.1, (KeepOutFar - z1) / 2);
            return m;
        }

        /// E7b: the strip under the turning capsules (x ±14, from the platform's edge to 2.6 m beyond the rim), kept clear.
        public const double KeepOutHalf = 14, KeepOutFar = 2.6;

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
        /// E7b: the glass is its own see-through mesh (glass), and there's a cabin inside: floor, ceiling and a bench
        /// (interior, which glows softly at night as if lit from inside) and a warm light panel in the ceiling (cabin).
        public static MeshData WheelCapsule(out MeshData lights, out MeshData glass, out MeshData interior, out MeshData cabin)
        {
            MeshData m = new MeshData("WheelCapsule", "Wheel", KitMatB, false) { navIgnore = true };
            lights = new MeshData("WheelCapsuleLights", "Wheel", "WheelLights", false) { navIgnore = true };
            glass = new MeshData("WheelCapsuleGlass", "Wheel", "WheelGlass", false) { navIgnore = true };
            interior = new MeshData("WheelCapsuleCabin", "Wheel", "WheelInterior", false) { navIgnore = true };
            cabin = new MeshData("WheelCapsuleCabinLight", "Wheel", "WheelCabinLight", false) { navIgnore = true };
            // octagonal section along x
            double hl = 3.0, w = 1.7, h = 1.7, ch = 0.55;
            double[][] sec = { new[] { -w + ch, -h }, new[] { w - ch, -h }, new[] { w, -h + ch }, new[] { w, h - ch }, new[] { w - ch, h }, new[] { -w + ch, h }, new[] { -w, h - ch }, new[] { -w, -h + ch } };
            for (int i = 0; i < 8; i++)
            {
                double[] a = sec[i], b = sec[(i + 1) % 8];
                bool frame = i == 0 || i == 4;
                int sw = i == 0 ? KitArt.S_DarkGrey : i == 4 ? KitArt.S_White : -1;
                double nz = (a[0] + b[0]) / 2, ny = (a[1] + b[1]) / 2;
                WQuad(frame ? m : glass, sw, WPt(-hl, a[1], a[0]), WPt(hl, a[1], a[0]), WPt(hl, b[1], b[0]), WPt(-hl, b[1], b[0]), 0, ny, nz);
            }
            // tapered glass ends
            foreach (int e in new[] { -1, 1 })
            {
                double xe = e * (hl + 0.8), k = 0.55;
                for (int i = 0; i < 8; i++)
                {
                    double[] a = sec[i], b = sec[(i + 1) % 8];
                    WQuad(i == 0 ? m : glass, i == 0 ? KitArt.S_DarkGrey : -1, WPt(e * hl, a[1], a[0]), WPt(e * hl, b[1], b[0]), WPt(xe, b[1] * k, b[0] * k), WPt(xe, a[1] * k, a[0] * k), e, (a[1] + b[1]) * 0.3, (a[0] + b[0]) * 0.3);
                }
                List<P3> cap = new List<P3>();
                foreach (double[] q in sec) cap.Add(WPt(xe, q[1] * k, q[0] * k));
                for (int i = 1; i + 1 < 8; i++) WTri(glass, -1, cap[0], cap[i], cap[i + 1], e, 0, 0);
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
            // the cabin: a floor and a ceiling seen from inside, a bench down the middle, a light panel overhead
            double fy = CabinFloorY, cy = h - 0.06, iw = w - ch;
            WQuad(interior, KitArt.S_MidGrey, WPt(-hl, fy, -iw), WPt(hl, fy, -iw), WPt(hl, fy, iw), WPt(-hl, fy, iw), 0, 1, 0);
            WQuad(interior, KitArt.S_White, WPt(-hl, cy, -iw), WPt(hl, cy, -iw), WPt(hl, cy, iw), WPt(-hl, cy, iw), 0, -1, 0);
            WBoxXZ(interior, KitArt.S_DarkGrey, KitArt.S_Timber, -1.1, 1.1, -0.32, 0.32, fy, fy + 0.46);
            WQuad(cabin, -1, WPt(-2.4, cy - 0.02, -0.28), WPt(2.4, cy - 0.02, -0.28), WPt(2.4, cy - 0.02, 0.28), WPt(-2.4, cy - 0.02, 0.28), 0, -1, 0);
            return m;
        }

        /// E7b: the cabin floor, in the capsule's space (the capsule's centre is 1.7 m above it).
        public const double CabinFloorY = -1.64;

        /// E7b: people riding a capsule (count 1–8; variant 0 or 1 so neighbouring capsules differ). Simple figures
        /// standing round the bench, facing the glass, in the capsule's space; they glow with the cabin at night.
        public static MeshData WheelPassengers(int count, int variant)
        {
            MeshData m = new MeshData("WheelPassengers_" + count + "_" + variant, "Wheel", "WheelInterior", false) { navIgnore = true };
            double[][] slot = { new[] { -2.3, 0.62 }, new[] { 2.25, -0.6 }, new[] { -1.45, -0.92 }, new[] { 1.5, 0.9 }, new[] { -2.4, -0.62 }, new[] { 2.4, 0.66 }, new[] { -0.5, 0.95 }, new[] { 0.55, -0.95 } };
            int[] shirt = { KitArt.S_Navy, KitArt.S_Red, KitArt.S_Teal, KitArt.S_Maroon, KitArt.S_Yellow, KitArt.S_Green, KitArt.S_Orange, KitArt.S_White, KitArt.S_Cream, KitArt.S_Verdigris };
            int[] legs = { KitArt.S_Navy, KitArt.S_DarkGrey, KitArt.S_Black, KitArt.S_Timber, KitArt.S_MidGrey };
            int[] skin = { KitArt.S_Render2, KitArt.S_YelBrick, KitArt.S_Timber, KitArt.S_BrownBrick, KitArt.S_Cream };
            Random rng = new Random(9100 + count * 17 + variant * 5);
            int first = variant == 0 ? 0 : 1;
            for (int n = 0; n < Math.Min(8, count); n++)
            {
                double[] s = slot[(n + first) % 8];
                double x = s[0] + (rng.NextDouble() - 0.5) * 0.2, z = s[1], f = CabinFloorY, hh = 0.92 + rng.NextDouble() * 0.12;
                int top = shirt[rng.Next(shirt.Length)], bot = legs[rng.Next(legs.Length)], sk = skin[rng.Next(skin.Length)];
                foreach (int side in new[] { -1, 1 }) WBlock3(m, WPt(x + side * 0.09, f + 0.4 * hh, z), 0, 0.07, 0.4 * hh, 0.08, bot);   // legs
                WBlock3(m, WPt(x, f + 1.12 * hh, z), 0, 0.2, 0.32 * hh, 0.13, top);                                                     // body
                foreach (int side in new[] { -1, 1 }) WBlock3(m, WPt(x + side * 0.26, f + 1.08 * hh, z), 0, 0.055, 0.3 * hh, 0.065, top);  // arms
                WBlock3(m, WPt(x, f + 1.58 * hh, z), 0, 0.1, 0.12, 0.1, sk);                                                              // head
                WBlock3(m, WPt(x, f + 1.71 * hh, z - Math.Sign(z) * 0.02), 0, 0.105, 0.03, 0.105, rng.Next(3) == 0 ? KitArt.S_Timber : KitArt.S_Black);   // hair
            }
            return m;
        }

        /// A simple version for seeing from across the city (drawn on the Landmark layer, fog held back):
        /// legs (farStatic), and the rim and spokes (farRotor, centred on the hub), with their lights; the capsules
        /// (farCapsules, also centred on the hub) are separate so they can glow with the cabins at night.
        public static MeshData WheelFar(out MeshData farRotor, out MeshData farLights, out MeshData farCapsules)
        {
            MeshData st = new MeshData("WheelFarStatic", "Wheel", "Landmark_Far", false) { navIgnore = true };
            farRotor = new MeshData("WheelFarRotor", "Wheel", "Landmark_Far", false) { navIgnore = true };
            farLights = new MeshData("WheelFarLights", "Wheel", "Landmark_FarLights", false) { navIgnore = true };
            farCapsules = new MeshData("WheelFarCapsules", "Wheel", "Landmark_FarCapsules", false) { navIgnore = true };   // E7b: glow warm at night
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
                WBlock3(farCapsules, c, 1.0, 3.6, 1.7, 1.7, KitArt.S_SkyGlass);
            }
            WCylinder(farRotor, KitArt.S_LightGrey, 2.6, -WheelAxleHalf, WheelAxleHalf, 8);
            return st;
        }

        // ------------------------------------------------------------------ E7b: the riverside plaza
        /// The free ground round the Wheel, between the South Embankment's pavement and the river wall, in the Wheel's
        /// space every 4 m along: { x, z where the pavement ends, z where the river wall starts }. Worked out from the
        /// world data (pavement edge + 0.1 m, river wall + 0.3 m), like WheelSite.
        static readonly double[,] PlazaEdge = {
            { -64, -29.37, 8.21 }, { -60, -28.38, 9.32 }, { -56, -27.39, 10.42 }, { -52, -26.40, 11.53 }, { -48, -25.41, 12.64 },
            { -44, -24.42, 13.74 }, { -40, -23.43, 14.85 }, { -36, -22.44, 15.96 }, { -32, -21.45, 17.06 }, { -28, -21.08, 18.17 },
            { -24, -20.98, 19.28 }, { -20, -20.89, 20.38 }, { -16, -20.79, 21.49 }, { -12, -20.70, 22.60 }, { -8, -20.60, 23.71 },
            { -4, -20.50, 24.40 }, { 0, -20.41, 23.72 }, { 4, -20.31, 23.04 }, { 8, -20.21, 22.35 }, { 12, -20.12, 21.67 },
            { 16, -20.02, 20.99 }, { 20, -19.92, 20.30 }, { 24, -19.83, 19.62 }, { 28, -19.73, 18.94 }, { 32, -19.63, 18.26 },
            { 36, -19.54, 17.57 }, { 40, -19.44, 16.89 }, { 44, -19.35, 16.21 }, { 48, -19.25, 15.52 }, { 52, -19.15, 14.84 },
            { 56, -19.06, 14.16 }, { 60, -18.96, 13.47 }, { 64, -18.86, 12.79 } };
        public const double PlazaY = 0.08;

        static double PlazaEdgeAt(double x, int col)
        {
            int n = PlazaEdge.GetLength(0);
            double t = (x - PlazaEdge[0, 0]) / 4.0;
            int i = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(t)));
            double f = Math.Max(0, Math.Min(1, t - i));
            return PlazaEdge[i, col] + (PlazaEdge[i + 1, col] - PlazaEdge[i, col]) * f;
        }
        /// Where the plaza meets the pavement (road side) and the river wall, at x along.
        public static double PlazaRoadZ(double x) { return PlazaEdgeAt(x, 1); }
        public static double PlazaRiverZ(double x) { return PlazaEdgeAt(x, 2); }

        /// The plaza: pale paving in 4 m squares with a dark border, from Rye Bridge's side to 64 m downriver
        /// (solid, walkable as pavement, so the city's pedestrians come onto it too). props = benches, bins (solid);
        /// poles/heads/pools = riverside lamps (the same heritage lamps and ground glow as Knightsbury's streets, so the
        /// city's lighting turns them on at dusk).
        public static MeshData WheelPlaza(out MeshData props, out MeshData poles, out MeshData heads, out MeshData pools)
        {
            MeshData m = new MeshData("WheelPlaza", "Wheel", KitMatB, true) { navArea = PavementArea };
            props = new MeshData("WheelPlazaProps", "Wheel", KitMatB, true) { navArea = 1 };
            poles = new MeshData("WheelPlazaLampPoles", "Wheel", "LampPole_Heritage", false) { navIgnore = true };
            heads = new MeshData("WheelPlazaLampHeads", "Wheel", "LampHead_Heritage", false) { navIgnore = true };
            pools = new MeshData("WheelPlazaLightPools", "Wheel", "LightPool_Heritage", false) { navIgnore = true };
            int n = PlazaEdge.GetLength(0);
            double y = PlazaY;
            for (int i = 0; i + 1 < n; i++)
            {
                double x0 = PlazaEdge[i, 0], x1 = PlazaEdge[i + 1, 0];
                double r0 = PlazaEdge[i, 1], r1 = PlazaEdge[i + 1, 1], w0 = PlazaEdge[i, 2], w1 = PlazaEdge[i + 1, 2];
                // the squares, clipped to the strip
                for (int iz = -8; iz < 7; iz++)
                {
                    double za = iz * 4, zb = za + 4;
                    List<double[]> poly = new List<double[]> { new[] { x0, za }, new[] { x1, za }, new[] { x1, zb }, new[] { x0, zb } };
                    poly = WClip(poly, x0, r0 + 0.8, x1, r1 + 0.8, 1);
                    poly = WClip(poly, x0, w0 - 1.0, x1, w1 - 1.0, -1);
                    if (poly.Count < 3) continue;
                    int ix = (int)Math.Round(x0 / 4);
                    int sw = ((ix + iz) & 1) == 0 ? KitArt.S_PaleConc : KitArt.S_LightGrey;
                    if (ix % 4 == 0) sw = KitArt.S_Portland;                      // a lighter band every 16 m
                    WFan(m, sw, poly, y);
                }
                // dark borders along the pavement and the river wall
                WQuad(m, KitArt.S_SootStone, WPt(x0, y, r0), WPt(x1, y, r1), WPt(x1, y, r1 + 0.8), WPt(x0, y, r0 + 0.8), 0, 1, 0);
                WQuad(m, KitArt.S_SootStone, WPt(x0, y, w0 - 1.0), WPt(x1, y, w1 - 1.0), WPt(x1, y, w1), WPt(x0, y, w0), 0, 1, 0);
            }
            // the two ends: a little kerb down to the ground
            foreach (int e in new[] { 0, n - 1 })
            {
                double x = PlazaEdge[e, 0];
                WQuad(m, KitArt.S_SootStone, WPt(x, -0.02, PlazaEdge[e, 1]), WPt(x, -0.02, PlazaEdge[e, 2]), WPt(x, y, PlazaEdge[e, 2]), WPt(x, y, PlazaEdge[e, 1]), e == 0 ? -1 : 1, 0, 0);
            }
            // benches facing the river, with a bin beside each
            foreach (double x in WheelBenchX)
            {
                double z = PlazaRiverZ(x) - 6.0;
                WBoxXZ(props, KitArt.S_Timber, KitArt.S_Timber, x - 1.0, x + 1.0, z - 0.25, z + 0.25, y + 0.4, y + 0.47);       // seat
                WBoxXZ(props, KitArt.S_Timber, KitArt.S_Timber, x - 1.0, x + 1.0, z - 0.33, z - 0.26, y + 0.47, y + 0.9);       // back
                foreach (double lx in new[] { -0.85, 0.85 }) WBlock(props, KitArt.S_DarkGrey, x + lx, z - 0.05, 0.05, 0.27, y, y + 0.4);
                WBlock(props, KitArt.S_DarkGrey, x + 1.6, z, 0.24, 0.24, y, y + 0.85);                                          // bin
                WBlock(props, KitArt.S_Black, x + 1.6, z, 0.2, 0.2, y + 0.85, y + 0.87);
            }
            // heritage lamps: along the river wall, and either side of the gate on the road side
            foreach (double[] l in WheelLampSpots())
            {
                double x = l[0], z = l[1], toward = l[2];         // the glow falls on the plaza side
                WBlock(poles, -1, x, z, 0.17, 0.17, y, y + 0.55);
                WBlock(poles, -1, x, z, 0.11, 0.11, y + 0.55, y + 0.9);
                WBlock(poles, -1, x, z, 0.07, 0.07, y + 0.9, y + 4.6);
                WBlock(poles, -1, x, z, 0.1, 0.1, y + 3.4, y + 3.5);
                WBlock(poles, -1, x, z, 0.12, 0.12, y + 4.6, y + 4.68);           // lantern base
                WBlock(heads, -1, x, z, 0.17, 0.17, y + 4.68, y + 5.2);           // glass
                WBlock(poles, -1, x, z, 0.22, 0.22, y + 5.2, y + 5.28);           // roof
                WBlock(poles, -1, x, z, 0.06, 0.06, y + 5.28, y + 5.45);          // finial
                double pz = z + toward * 3.2, pr = 5.6;
                pools.Quad(new P3(x - pr, 0.17, pz - pr, 0, 0), new P3(x + pr, 0.17, pz - pr, 1, 0), new P3(x + pr, 0.17, pz + pr, 1, 1), new P3(x - pr, 0.17, pz + pr, 0, 1), 0, 1, 0);
            }
            return m;
        }

        /// Where the plaza's benches are (x along; each sits 6 m in from the river wall, facing it).
        public static readonly double[] WheelBenchX = { -48, -35, -10, 12, 35, 48 };
        /// The plaza's lamps: { x along, z across, which way the glow falls (−1 away from the river, +1 towards it) }:
        /// along the river wall, along the pavement, and either side of the gate.
        public static IEnumerable<double[]> WheelLampSpots()
        {
            foreach (double x in new[] { -58.0, -42, -28, -12, 12, 28, 42, 58 }) yield return new[] { x, PlazaRiverZ(x) - 0.9, -1 };
            foreach (double x in new[] { -36.0, -10, 10, 36 }) yield return new[] { x, PlazaRoadZ(x) + 1.6, 1 };
            foreach (double x in new[] { -15.5, 15.5 }) yield return new[] { x, -13.5, 1 };
        }

        /// Clip a convex polygon (x, z) to one side of the line a→b: keep = +1 the left (greater z for a line along +x), −1 the right.
        static List<double[]> WClip(List<double[]> poly, double ax, double az, double bx, double bz, int keep)
        {
            List<double[]> o = new List<double[]>();
            Func<double[], double> side = p => keep * ((bx - ax) * (p[1] - az) - (bz - az) * (p[0] - ax));
            for (int i = 0; i < poly.Count; i++)
            {
                double[] p = poly[i], q = poly[(i + 1) % poly.Count];
                double sp = side(p), sq = side(q);
                if (sp >= 0) o.Add(p);
                if ((sp >= 0) != (sq >= 0)) { double t = sp / (sp - sq); o.Add(new[] { p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t }); }
            }
            return o;
        }

        /// A flat convex polygon (x, z) at height y, facing up, one swatch.
        static void WFan(MeshData m, int sw, List<double[]> poly, double y)
        {
            for (int i = 1; i + 1 < poly.Count; i++)
                WTri(m, sw, WPt(poly[0][0], y, poly[0][1]), WPt(poly[i][0], y, poly[i][1]), WPt(poly[i + 1][0], y, poly[i + 1][1]), 0, 1, 0);
        }

        // ------------------------------------------------------------------ E7b: where the Wheel's crowd walks
        // All in the Wheel's space (x along, y up, z across). LundunWheelCrowd walks people along these: round the
        // plaza on a little network of paths (nodes and edges) that keeps clear of the legs, the platform, the benches
        // and the lamps, to spots where they stop and look (at the river or up at the Wheel), into the queue on the
        // platform, onto the capsules and back down the exit.

        /// The path network's corners: { x, y, z }.
        public static List<double[]> WheelCrowdNodes()
        {
            List<double[]> n = new List<double[]>();
            double road = -16.5;
            foreach (double x in new[] { -56.0, -40, -30, -17.5, -6, 0, 6, 17.5, 30, 40, 56 }) n.Add(new[] { x, PlazaY, road });   // 0–10 along the road side
            n.Add(new[] { -17.5, PlazaY, -4 }); n.Add(new[] { -17.5, PlazaY, 8 });                                                       // 11, 12 west of the platform
            n.Add(new[] { 17.5, PlazaY, -4 }); n.Add(new[] { 17.5, PlazaY, 8 });                                                         // 13, 14 east of it
            n.Add(new[] { -8.0, PlazaY, 8 }); n.Add(new[] { 0.0, PlazaY, 8 }); n.Add(new[] { 8.0, PlazaY, 8 });                          // 15–17 in front of the Wheel
            foreach (double x in new[] { -56.0, -40, -30, -17.5, -6, 0, 6, 17.5, 30, 40, 56 }) n.Add(new[] { x, PlazaY, PlazaRiverZ(x) - 3.0 });   // 18–28 along the river
            return n;
        }

        public static List<int[]> WheelCrowdEdges()
        {
            List<int[]> e = new List<int[]>();
            for (int i = 0; i < 10; i++) { e.Add(new[] { i, i + 1 }); e.Add(new[] { 18 + i, 19 + i }); }
            foreach (int i in new[] { 0, 1, 2, 8, 9, 10 }) e.Add(new[] { i, 18 + i });         // straight across, away from the legs
            e.Add(new[] { 3, 11 }); e.Add(new[] { 11, 12 }); e.Add(new[] { 12, 21 });
            e.Add(new[] { 7, 13 }); e.Add(new[] { 13, 14 }); e.Add(new[] { 14, 25 });
            e.Add(new[] { 12, 15 }); e.Add(new[] { 15, 16 }); e.Add(new[] { 16, 17 }); e.Add(new[] { 17, 14 });
            e.Add(new[] { 15, 22 }); e.Add(new[] { 16, 23 }); e.Add(new[] { 17, 24 });
            return e;
        }

        /// Where people stop: { x, y, z, look-at x, look-at z, node they reach it from }.
        public static List<double[]> WheelCrowdSpots()
        {
            List<double[]> s = new List<double[]>();
            List<double[]> nodes = WheelCrowdNodes();
            Func<double, double, int> near = (x, z) =>
            {
                int best = 0; double bd = 1e9;
                for (int i = 0; i < nodes.Count; i++) { double d = (nodes[i][0] - x) * (nodes[i][0] - x) + (nodes[i][2] - z) * (nodes[i][2] - z); if (d < bd) { bd = d; best = i; } }
                return best;
            };
            // at the river wall, looking out over the water
            foreach (double x in new[] { -52.0, -46, -36, -24, -16, -3, 3, 16, 24, 36, 46, 52 })
            {
                double z = PlazaRiverZ(x) - 1.4;
                s.Add(new[] { x, PlazaY, z, x, z + 20, near(x, z) });
            }
            // in front of the Wheel, looking up at it
            foreach (double[] p in new[] { new[] { -10.0, 5.0 }, new[] { -4.0, 4.6 }, new[] { 3.0, 5.0 }, new[] { 10.0, 5.2 }, new[] { -6.0, 11.0 }, new[] { 6.0, 11.5 } })
                s.Add(new[] { p[0], PlazaY, p[1], p[0] * 0.3, 0, near(p[0], p[1]) });
            // on the road side, looking at it
            foreach (double[] p in new[] { new[] { -24.0, -18.0 }, new[] { -13.0, -18.6 }, new[] { 13.0, -18.4 }, new[] { 24.0, -17.9 }, new[] { -46.0, -20.0 }, new[] { 46.0, -17.9 } })
                s.Add(new[] { p[0], PlazaY, p[1], p[0] * 0.3, 0, near(p[0], p[1]) });
            return s;
        }

        /// The queue: from the gate up the west half of the steps, along the two lanes on the platform, to the head
        /// (where people wait for their capsule). { x, y, z } from the tail end.
        public static double[][] WheelQueuePath { get { double y = WheelPlatformY; return new[] {
            new[] { -2.5, PlazaY, -15.0 }, new[] { -2.5, PlazaY, -12.3 }, new[] { -2.5, y, -10.0 }, new[] { -2.5, y, -8.9 },
            new[] { -12.0, y, -8.9 }, new[] { -12.0, y, -6.7 }, new[] { 0.0, y, -6.7 }, new[] { 0.0, y, -4.6 } }; } }
        /// The way out: along the platform's edge, under the EXIT sign, down the east half of the steps to the gate.
        public static double[][] WheelExitPath { get { double y = WheelPlatformY; return new[] {
            new[] { 8.5, y, -3.0 }, new[] { 8.5, y, -9.0 }, new[] { 2.5, y, -9.0 }, new[] { 2.5, y, -10.0 },
            new[] { 2.5, PlazaY, -12.3 }, new[] { 2.5, PlazaY, -15.0 } }; } }
        /// Nodes next to the queue's tail and the exit's end; nodes at the plaza's far ends (where people arrive).
        public const int WheelQueueNode = 5, WheelExitNode = 6;
        public static readonly int[] WheelArriveNodes = { 0, 10, 18, 28 };
        /// The capsule's door (its side towards the platform) and the platform's edge, z in the Wheel's space.
        public const double WheelDoorZ = -1.5, WheelEdgeZ = -3.0;

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
