// L2: street surface textures, painted in code (pure C#, no downloads, no branding).
//   PavingBands  1024 × 2048  four pavements stacked (each 4.5 m deep, repeating every 9 m along the street), granite or
//                             concrete kerb at the bottom of each: 0 Peckam concrete slabs, 1 Knightsbury York stone,
//                             2 Canary granite, 3 Gilmore patched asphalt
//   Setts        512 × 512    granite setts, 2 m square, courses running across the road
//   WornPaint    256 × 256    alpha for the white road markings (10 m square): worn patches show the road through
//   Decals       1024 × 4096  line strips that repeat every 8 m along a road (yellows, bays, give way, bus lane…) and
//                             sprites (road text, arrows, manholes, gullies, covers, patches, stains, cracks), 128 px per m
// All images are RGBA, row 0 = bottom.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static class StreetArt
    {
        // ------------------------------------------------------------------ paving bands
        public const int PaveW = 1024, PaveH = 2048, BandPx = 512;
        public const double PaveTileM = 9.0, BandM = 4.5, KerbM = 0.3;
        public const int BandPeckam = 0, BandKnight = 1, BandCanary = 2, BandGilmore = 3;
        static double PxM { get { return PaveW / PaveTileM; } }

        /// V for a point `across` metres out from the kerb edge in a band (kerb faces use across = −1).
        public static double PaveV(int band, double across)
        {
            double m = across < 0 ? KerbM * 0.5 : KerbM + Math.Min(Math.Max(across, 0), BandM - KerbM - 0.05);
            if (across >= 0 && across < KerbM) m = across;
            return (band * BandPx + m * PxM) / PaveH;
        }

        // ------------------------------------------------------------------ decal atlas layout
        public const int DecW = 1024, DecH = 4096;
        public const double DecPxM = 128, DecRepeatM = 8;
        public struct Rect { public int x, y, w, h; public Rect(int x, int y, int w, int h) { this.x = x; this.y = y; this.w = w; this.h = h; } }
        public class Strip { public string name; public int y, h; public double metres; }    // y = bottom row, h rows, metres = height in metres

        public static readonly Dictionary<string, Strip> Strips = new Dictionary<string, Strip>();
        public static readonly Dictionary<string, Rect> Sprites = new Dictionary<string, Rect>();
        static bool laidOut;

        static void Layout()
        {
            if (laidOut) return;
            laidOut = true;
            int y = DecH;
            Action<string, double> strip = (name, m) => { int h = (int)Math.Ceiling(m * DecPxM) + 8; y -= h; Strips[name] = new Strip { name = name, y = y + 4, h = h - 8, metres = m }; };
            strip("doubleYellow", 0.4);
            strip("singleYellow", 0.2);
            strip("giveWay", 0.9);
            strip("bayEdge", 0.2);
            strip("whiteSolid", 0.35);
            strip("busLane", 3.5);
            strip("gutterSetts", 0.32);
            strip("clearway", 0.3);
            strip("whiteStop", 0.3);
            // sprites: a shelf packer below the strips
            int sx = 0, sy = y - 4, rowH = 0;
            Action<string, double, double> sprite = (name, wm, hm) =>
            {
                int w = (int)Math.Ceiling(wm * DecPxM), h = (int)Math.Ceiling(hm * DecPxM);
                if (sx + w + 6 > DecW) { sx = 0; sy -= rowH + 6; rowH = 0; }
                if (sy - h < 0) throw new Exception("Street decal atlas full at " + name);
                Sprites[name] = new Rect(sx + 3, sy - h, w, h);
                sx += w + 6; rowH = Math.Max(rowH, h);
            };
            // road text is long in the direction of travel (texture up = forward)
            sprite("SLOW", 2.4, 2.4); sprite("BUS STOP", 2.6, 2.8); sprite("BUS LANE", 2.6, 2.8); sprite("KEEP CLEAR", 2.8, 2.0);
            sprite("arrowStraight", 0.7, 3.0); sprite("arrowLeft", 1.1, 3.0); sprite("arrowRight", 1.1, 3.0);
            sprite("LOOK RIGHT", 2.6, 0.45); sprite("roundel20", 1.6, 2.4);
            sprite("manholeRound0", 0.8, 0.8); sprite("manholeRound1", 0.8, 0.8); sprite("manholeSquare0", 0.75, 0.75); sprite("manholeSquare1", 0.9, 0.65);
            sprite("gully0", 0.55, 0.35); sprite("gully1", 0.55, 0.35);
            sprite("cover0", 0.35, 0.35); sprite("cover1", 0.5, 0.3); sprite("cover2", 0.25, 0.25); sprite("cover3", 0.6, 0.6);
            for (int i = 0; i < 4; i++) sprite("patch" + i, 2.0, 1.4);
            for (int i = 0; i < 3; i++) sprite("stain" + i, 1.2, 0.9);
            for (int i = 0; i < 3; i++) sprite("crack" + i, 3.0, 0.5);
            for (int i = 0; i < 2; i++) sprite("paveStain" + i, 0.9, 0.9);
            // L4: grit
            for (int i = 0; i < GritLitter; i++) sprite("litter" + i, 0.7, 0.5);
            for (int i = 0; i < GritLeaves; i++) sprite("leaves" + i, 0.9, 0.7);
            for (int i = 0; i < GritPuddles; i++) sprite("puddle" + i, 2.4, 1.2);
            for (int i = 0; i < GritPosters; i++) sprite("poster" + i, 0.6, 0.85);
            for (int i = 0; i < GritTags; i++) sprite("tagS" + i, 0.9, 0.45);
            sprite("stickers", 0.4, 0.6);
            sprite("binjuice", 0.9, 0.7);
        }
        public const int GritLitter = 6, GritLeaves = 3, GritPuddles = 3, GritPosters = 8, GritTags = 4;

        public static Strip GetStrip(string n) { Layout(); return Strips[n]; }
        public static Rect GetSprite(string n) { Layout(); return Sprites[n]; }

        // ------------------------------------------------------------------ noise (wraps horizontally on period)
        static uint Hash(int x, int y, int seed) { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041); h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); }
        static double R01(int x, int y, int seed) { return (Hash(x, y, seed) & 0xFFFFFF) / (double)0xFFFFFF; }
        static double Value(double x, double y, int period, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int ax = ((x0 % period) + period) % period, bx = (((x0 + 1) % period) + period) % period;
            int ay = y0, by = y0 + 1;
            return (R01(ax, ay, seed) * (1 - fx) + R01(bx, ay, seed) * fx) * (1 - fy) + (R01(ax, by, seed) * (1 - fx) + R01(bx, by, seed) * fx) * fy;
        }
        /// Fractal noise; wraps horizontally every `wrap` pixels (and vertically if wrapY).
        static double Fbm(double px, double py, double cellPx, int seed, int wrap, bool wrapY = false)
        {
            double s = 0, amp = 0.5, tot = 0, cell = cellPx;
            for (int o = 0; o < 4; o++)
            {
                int period = Math.Max(1, (int)Math.Round(wrap / cell));
                double y = py / cell;
                if (wrapY) { int py0 = (int)Math.Floor(y); double fy = y - py0; y = ((py0 % period) + period) % period + fy; }
                s += amp * (wrapY ? ValueXY(px / cell, y, period, seed + o * 31) : Value(px / cell, y, period, seed + o * 31));
                tot += amp; amp *= 0.5; cell *= 0.5;
            }
            return s / tot;
        }
        static double ValueXY(double x, double y, int period, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            Func<int, int> w = a => ((a % period) + period) % period;
            int ax = w(x0), bx = w(x0 + 1), ay = w(y0), by = w(y0 + 1);
            return (R01(ax, ay, seed) * (1 - fx) + R01(bx, ay, seed) * fx) * (1 - fy) + (R01(ax, by, seed) * (1 - fx) + R01(bx, by, seed) * fx) * fy;
        }
        static double C01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static byte B(double v) { return (byte)Math.Round(C01(v) * 255); }
        static void Px(byte[] im, int w, int x, int y, double r, double g, double b, double a = 1)
        { int i = (y * w + x) * 4; im[i] = B(r); im[i + 1] = B(g); im[i + 2] = B(b); im[i + 3] = B(a); }

        // ------------------------------------------------------------------ paving bands
        public static byte[] PavingBands()
        {
            byte[] im = new byte[PaveW * PaveH * 4];
            double ppm = PxM;
            int kerbPx = (int)Math.Round(KerbM * ppm);
            for (int band = 0; band < 4; band++)
            {
                Random rnd = new Random(41 + band);
                int y0 = band * BandPx;
                for (int y = 0; y < BandPx; y++)
                    for (int x = 0; x < PaveW; x++)
                    {
                        int gy = y0 + y;
                        double r, g, b;
                        if (y < kerbPx) Kerb(band, x, y, kerbPx, ppm, out r, out g, out b);
                        else
                        {
                            double ly = (y - kerbPx) / ppm, lx = x / ppm;    // metres from the kerb's back edge, along
                            switch (band)
                            {
                                case BandPeckam: Slabs(x, gy, lx, ly, 0.6, 0.75, 0.88, 0.1, out r, out g, out b); break;
                                case BandKnight: YorkStone(x, gy, lx, ly, out r, out g, out b); break;
                                case BandCanary: Granite(x, gy, lx, ly, out r, out g, out b); break;
                                default: Asphalt(x, gy, lx, ly, out r, out g, out b); break;
                            }
                        }
                        Px(im, PaveW, x, gy, r, g, b);
                    }
                // grime: chewing gum (Peckam lots, Knightsbury little), cracks, stains
                int gum = band == BandPeckam ? 260 : band == BandGilmore ? 160 : band == BandCanary ? 40 : 60;
                for (int i = 0; i < gum; i++)
                {
                    int gx = rnd.Next(PaveW), gyy = y0 + kerbPx + 6 + rnd.Next(BandPx - kerbPx - 12);
                    double rad = 1.2 + rnd.NextDouble() * 2.0;
                    for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
                            if (dx * dx + dy * dy <= rad * rad) Mix(im, PaveW, (gx + dx + PaveW) % PaveW, gyy + dy, 0.62, 0.62, 0.6, 0.55);
                }
                int cracks = band == BandKnight || band == BandCanary ? 3 : 10;
                for (int c = 0; c < cracks; c++)
                {
                    double cx = rnd.Next(PaveW), cy = y0 + kerbPx + 10 + rnd.Next(BandPx - kerbPx - 20), a = rnd.NextDouble() * Math.PI;
                    for (int i = 0; i < 70; i++)
                    {
                        a += (rnd.NextDouble() - 0.5) * 0.5; cx += Math.Cos(a); cy += Math.Sin(a);
                        int ix = ((int)cx % PaveW + PaveW) % PaveW, iy = (int)cy;
                        if (iy > y0 + kerbPx && iy < y0 + BandPx - 2) Mul(im, PaveW, ix, iy, 0.55);
                    }
                }
                int stains = band == BandPeckam ? 8 : band == BandGilmore ? 10 : 2;
                for (int s = 0; s < stains; s++)
                {
                    int sx = rnd.Next(PaveW), sy = y0 + kerbPx + rnd.Next(BandPx - kerbPx - 40) + 20, sr = 12 + rnd.Next(28);
                    for (int dy = -sr; dy <= sr; dy++) for (int dx = -sr; dx <= sr; dx++)
                        {
                            double d = Math.Sqrt(dx * dx + dy * dy) / sr + (Fbm(sx + dx, sy + dy, 8, 77 + s, PaveW) - 0.5) * 0.6;
                            if (d < 1 && sy + dy > y0 + kerbPx && sy + dy < y0 + BandPx) Mul(im, PaveW, ((sx + dx) % PaveW + PaveW) % PaveW, sy + dy, 0.86 + 0.14 * d);
                        }
                }
            }
            return im;
        }

        static void Mix(byte[] im, int w, int x, int y, double r, double g, double b, double k)
        {
            int i = (y * w + x) * 4;
            im[i] = B(im[i] / 255.0 * (1 - k) + r * k); im[i + 1] = B(im[i + 1] / 255.0 * (1 - k) + g * k); im[i + 2] = B(im[i + 2] / 255.0 * (1 - k) + b * k);
        }
        static void Mul(byte[] im, int w, int x, int y, double k)
        {
            int i = (y * w + x) * 4; im[i] = B(im[i] / 255.0 * k); im[i + 1] = B(im[i + 1] / 255.0 * k); im[i + 2] = B(im[i + 2] / 255.0 * k);
        }

        static void Kerb(int band, int x, int y, int kerbPx, double ppm, out double r, out double g, out double b)
        {
            // granite kerbstones 0.9 m long (Gilmore: concrete kerbs 0.9 m, chipped); arris at the road edge
            double len = 0.9 * ppm, kx = x % len;
            int id = (int)(x / len);
            bool joint = kx < 2;
            double grain = R01(x, y, 41 + band), v;
            if (band == BandGilmore)
            {
                v = 0.66 + (Fbm(x, y, 16, 43, PaveW) - 0.5) * 0.12 + (R01(id, 0, 47) - 0.5) * 0.08;
                if (R01(x / 6, y / 6, 49) < 0.04) v -= 0.12;     // chips
            }
            else
            {
                double tone = band == BandKnight ? 0.72 : band == BandCanary ? 0.68 : 0.7;
                v = tone + (grain < 0.15 ? -0.1 : grain > 0.9 ? 0.15 : 0) + (Fbm(x, y, 16, 43, PaveW) - 0.5) * 0.08 + (R01(id, 0, 45) - 0.5) * 0.06;
            }
            if (y < 3) v *= 0.82;                                         // the rounded arris, in shadow
            if (y >= kerbPx - 2) v *= 0.7;                                // joint to the paving
            if (joint) v = 0.45;
            r = v; g = v; b = v * 1.02;
            if (band == BandCanary && grain > 0.6) { r *= 1.04; g *= 0.99; }   // pinkish granite
        }

        static void Slabs(int x, int gy, double lx, double ly, double sw, double sl, double tone0, double var, out double r, out double g, out double b)
        {
            // concrete flags laid in courses, offset half a slab each course
            int row = (int)Math.Floor(ly / sl);
            double off = (row % 2) * sw * 0.5;
            double fx = ((lx + off) % sw + sw) % sw, fy = ly - row * sl;
            int col = (int)Math.Floor((lx + off) / sw) % (int)Math.Round(PaveTileM / sw);
            double ppm = PxM;
            double d = Math.Min(Math.Min(fx, sw - fx), Math.Min(fy, sl - fy)) * ppm;
            double tone = tone0 + (R01(col, row, 7) - 0.5) * 2 * var;
            bool replaced = R01(col, row, 11) < 0.06;                    // a newer, paler slab
            bool tarmacked = R01(col, row, 13) < 0.035;                  // a slab replaced with tarmac
            double v = tone + (Fbm(x, gy, 24, 9, PaveW) - 0.5) * 0.07 + (R01(x, gy, 13) - 0.5) * 0.05;
            if (replaced) v += 0.07;
            if (d < 3.5) v -= 0.05 * (1 - d / 3.5);                      // worn bevel
            if (d < 1.2) v = 0.5;                                        // joint
            if (R01(col, row, 19) < 0.05) v -= 0.08 * Value(x / 12.0, gy / 12.0, 1 << 20, 5);
            if (tarmacked && d >= 1.2) { double t = 0.3 + (R01(x, gy, 15) - 0.5) * 0.06; r = t; g = t; b = t * 1.02; return; }
            r = v * 0.98; g = v * 0.97; b = v * 0.95;
        }

        static void YorkStone(int x, int gy, double lx, double ly, out double r, out double g, out double b)
        {
            // big buff flags in courses of varying depth, random lengths
            double[] courses = { 0.75, 0.9, 0.6, 0.9, 0.75, 0.6 };
            double acc = 0; int row = 0; double fy = 0, depth = 0.75;
            for (int i = 0; i < 12; i++) { depth = courses[i % courses.Length]; if (ly < acc + depth) { row = i; fy = ly - acc; break; } acc += depth; }
            // lengths: walk along with seeded lengths summing to the 9 m tile
            double pos = 0, len = 1; int col = 0; double fx = 0;
            for (int i = 0; i < 20; i++)
            {
                len = i == 19 ? PaveTileM - pos : 0.7 + R01(i, row, 31) * 0.6;
                if (pos + len > PaveTileM) len = PaveTileM - pos;
                if (lx < pos + len || i == 19) { col = i; fx = lx - pos; break; }
                pos += len;
            }
            double ppm = PxM;
            double d = Math.Min(Math.Min(fx, len - fx), Math.Min(fy, depth - fy)) * ppm;
            double tone = 0.78 + (R01(col, row, 33) - 0.5) * 0.14;
            double v = tone + (Fbm(x, gy, 30, 35, PaveW) - 0.5) * 0.1 + (R01(x, gy, 37) - 0.5) * 0.04;
            double bed = Value(x / 40.0, gy / 9.0, 1 << 20, 39);         // bedding lines in the sandstone
            v -= 0.03 * Math.Max(0, bed - 0.55) / 0.45;
            if (d < 2.5) v -= 0.06 * (1 - d / 2.5);
            if (d < 1.0) v = 0.45;
            r = v * 1.0; g = v * 0.96; b = v * 0.88;
        }

        static void Granite(int x, int gy, double lx, double ly, out double r, out double g, out double b)
        {
            // a dark granite band by the kerb, then large pale granite slabs (0.6 × 1.2) in stretcher bond
            double ppm = PxM;
            if (ly < 0.3)
            {
                double sx = lx % 0.3, d = Math.Min(sx, 0.3 - sx) * ppm;
                double v0 = 0.36 + (R01(x, gy, 51) - 0.5) * 0.1;
                if (d < 1) v0 = 0.25;
                r = v0; g = v0; b = v0 * 1.04; return;
            }
            double ly2 = ly - 0.3;
            int row = (int)Math.Floor(ly2 / 0.6);
            double off = (row % 2) * 0.6, fx = ((lx + off) % 1.2 + 1.2) % 1.2, fy = ly2 - row * 0.6;
            int col = (int)Math.Floor((lx + off) / 1.2);
            double dd = Math.Min(Math.Min(fx, 1.2 - fx), Math.Min(fy, 0.6 - fy)) * ppm;
            double grain = R01(x, gy, 53);
            double v = 0.74 + (R01(col, row, 55) - 0.5) * 0.06 + (grain < 0.2 ? -0.08 : grain > 0.92 ? 0.08 : 0) + (Fbm(x, gy, 40, 57, PaveW) - 0.5) * 0.04;
            if (dd < 1.0) v = 0.5;
            r = v * 1.01; g = v * 0.99; b = v * 0.98;
        }

        static void Asphalt(int x, int gy, double lx, double ly, out double r, out double g, out double b)
        {
            // tarmac footway, patched; a strip of old concrete slabs next to the kerb in places
            double v = 0.36 + (Fbm(x, gy, 40, 61, PaveW) - 0.5) * 0.08 + (R01(x, gy, 63) - 0.5) * 0.08;
            double ag = R01(x, gy, 65);
            if (ag < 0.04) v += 0.12; else if (ag > 0.97) v -= 0.08;
            double patch = Fbm(x, gy, 90, 67, PaveW);
            if (patch > 0.6) v = v * 0.82 + 0.02;                                   // newer, darker patches
            if (patch > 0.595 && patch < 0.605) v = 0.2;                              // sealant edge
            if (ly < 0.6 && Value(lx / 3.0, 0, 3, 69) > 0.55)                        // old slabs survive by the kerb
            {
                Slabs(x, gy, lx, ly, 0.6, 0.6, 0.78, 0.08, out r, out g, out b);
                return;
            }
            if (ly > 3.6) { double weed = Fbm(x, gy, 10, 71, PaveW); if (weed > 0.62) { r = 0.25; g = 0.32; b = 0.18; return; } }   // weeds at the back edge
            r = v; g = v; b = v * 1.03;
        }

        // ------------------------------------------------------------------ setts
        public const int SettsPx = 512; public const double SettsM = 2.0;
        public static byte[] Setts()
        {
            byte[] im = new byte[SettsPx * SettsPx * 4];
            double ppm = SettsPx / SettsM;
            // u (x) = along the road: setts 0.1 m; v (y) = across: setts 0.2 m, staggered by half per row
            for (int y = 0; y < SettsPx; y++)
                for (int x = 0; x < SettsPx; x++)
                {
                    double lx = x / ppm, ly = y / ppm;
                    int col = (int)Math.Floor(lx / 0.1);
                    double off = (col % 2) * 0.1;
                    int row = (int)Math.Floor((ly + off) / 0.2) % 10;
                    double fx = lx - col * 0.1, fy = ((ly + off) % 0.2 + 0.2) % 0.2;
                    double dx = Math.Min(fx, 0.1 - fx) * ppm, dy = Math.Min(fy, 0.2 - fy) * ppm;
                    double d = Math.Min(dx, dy);
                    double tone = 0.44 + (R01(col % 20, row, 81) - 0.5) * 0.13;
                    bool pink = R01(col % 20, row, 83) < 0.12;
                    double dome = 1 - 0.18 * Math.Pow(1 - C01(d / 9.0), 2);              // rounded, worn tops
                    double v = tone * dome + (Fbm(x, y, 16, 85, SettsPx, true) - 0.5) * 0.08 + (R01(x, y, 87) - 0.5) * 0.06;
                    double r = v, g = v * 0.98, b = v * 1.0;
                    if (pink) { r *= 1.05; g *= 0.98; b *= 0.96; }
                    if (d < 2.2)
                    {
                        // joints: dark, gritty, the odd bit of moss
                        double jv = 0.16 + (R01(x, y, 89) - 0.5) * 0.06;
                        bool moss = Fbm(x, y, 40, 91, SettsPx, true) > 0.62;
                        r = moss ? 0.17 : jv; g = moss ? 0.22 : jv; b = moss ? 0.12 : jv;
                    }
                    Px(im, SettsPx, x, y, r, g, b, 0.35);                                 // alpha = smoothness (polished granite)
                }
            return im;
        }

        // ------------------------------------------------------------------ worn white paint (alpha clips the worn bits)
        public const int WornPx = 256;
        public static byte[] WornPaint()
        {
            byte[] im = new byte[WornPx * WornPx * 4];
            for (int y = 0; y < WornPx; y++)
                for (int x = 0; x < WornPx; x++)
                {
                    double n = Fbm(x, y, 48, 101, WornPx, true), fine = R01(x, y, 103);
                    double a = n > 0.64 ? 0 : (fine < 0.06 ? 0 : 1);                  // worn patches and speckle
                    double grime = 0.82 + (Fbm(x, y, 20, 105, WornPx, true) - 0.5) * 0.25;
                    Px(im, WornPx, x, y, grime, grime, grime * 0.97, a);
                }
            return im;
        }

        // ------------------------------------------------------------------ decals
        public static byte[] Decals()
        {
            Layout();
            DressAtlas cv = new DressAtlas(DecW, DecH, 7300);       // used as a canvas (text, discs, lines); starts transparent
            byte[] im = cv.px;
            Col yellow = new Col(222, 178, 30), white = new Col(236, 236, 230);

            // line strips (u repeats every 8 m: worn using noise that wraps on 1024 px)
            foreach (Strip s in Strips.Values)
                for (int y = 0; y < s.h; y++)
                    for (int x = 0; x < DecW; x++)
                    {
                        double vm = (y + 0.5) / DecPxM, um = (x + 0.5) / DecPxM;
                        double wear = Fbm(x, s.y + y, 40, 111, DecW), speck = R01(x, s.y + y, 113);
                        bool worn = wear > 0.71 || speck < 0.035;
                        double r = 0, g = 0, b = 0, a = 0;
                        Col c = white;
                        bool on = false;
                        switch (s.name)
                        {
                            case "doubleYellow": on = (vm > 0.05 && vm < 0.15) || (vm > 0.25 && vm < 0.35); c = yellow; break;
                            case "singleYellow": on = vm > 0.05 && vm < 0.15; c = yellow; break;
                            case "clearway": on = vm > 0.05 && vm < 0.25 && (um % 1.0) < 0.6; c = yellow; break;
                            case "giveWay": on = ((vm > 0.1 && vm < 0.3) || (vm > 0.6 && vm < 0.8)) && (um % 0.8) < 0.53; break;
                            case "bayEdge": on = vm > 0.05 && vm < 0.15 && (um % 2.0) < 1.0; break;
                            case "whiteSolid": on = vm > 0.05 && vm < 0.3; break;
                            case "whiteStop": on = vm > 0.0 && vm < 0.3; break;
                            case "busLane":
                                {
                                    // red surfacing, worn through to the black where wheels run
                                    double rut = Math.Abs(vm - 0.9) < 0.35 || Math.Abs(vm - 2.6) < 0.35 ? 0.15 : 0;
                                    bool hole = Fbm(x, s.y + y, 30, 115, DecW) > 0.73 - rut;
                                    if (!hole)
                                    {
                                        double v = 0.85 + (R01(x, s.y + y, 117) - 0.5) * 0.25 + (Fbm(x, s.y + y, 60, 119, DecW) - 0.5) * 0.2;
                                        r = 0.5 * v; g = 0.14 * v; b = 0.12 * v; a = 1;
                                    }
                                    Put(im, x, s.y + y, r, g, b, a);
                                    continue;
                                }
                            case "gutterSetts":
                                {
                                    // three courses of small granite setts along the kerb
                                    double row = Math.Floor(vm / 0.1), off = (row % 2) * 0.05, fx = ((um + off) % 0.1), fy = vm % 0.1;
                                    double d = Math.Min(Math.Min(fx, 0.1 - fx), Math.Min(fy, 0.1 - fy)) * DecPxM;
                                    if (vm > 0.3) { Put(im, x, s.y + y, 0, 0, 0, 0); continue; }
                                    double v = 0.48 + (R01((int)((um + off) / 0.1), (int)row, 121) - 0.5) * 0.2;
                                    if (d < 1.4) v = 0.2;
                                    Put(im, x, s.y + y, v, v * 0.98, v, 1);
                                    continue;
                                }
                        }
                        if (on && !worn)
                        {
                            double gr = 0.86 + (Fbm(x, s.y + y, 16, 123, DecW) - 0.5) * 0.2;
                            r = c.r / 255.0 * gr; g = c.g / 255.0 * gr; b = c.b / 255.0 * gr; a = 1;
                        }
                        Put(im, x, s.y + y, r, g, b, a);
                    }

            // road text: stretched letters, up = direction of travel
            RoadText(cv, "SLOW", GetSprite("SLOW"), white, 1);
            RoadText(cv, "BUS|STOP", GetSprite("BUS STOP"), yellow, 2);
            RoadText(cv, "BUS|LANE", GetSprite("BUS LANE"), white, 2);
            RoadText(cv, "KEEP|CLEAR", GetSprite("KEEP CLEAR"), white, 2);
            {
                Rect lr = GetSprite("LOOK RIGHT");
                cv.Text("LOOK RIGHT", lr.x, lr.y + 2, (int)(lr.w * 0.78), lr.h - 4, white, 0.55, DressAtlas.Align.Left);
                double ay = lr.y + lr.h / 2.0, ax0 = lr.x + lr.w * 0.8, ax1 = lr.x + lr.w - 6;
                cv.Seg(ax0, ay, ax1, ay, 3.5, white); cv.Seg(ax1, ay, ax1 - 14, ay + 12, 3.5, white); cv.Seg(ax1, ay, ax1 - 14, ay - 12, 3.5, white);
            }
            Arrow(cv, GetSprite("arrowStraight"), white, 0);
            Arrow(cv, GetSprite("arrowLeft"), white, -1);
            Arrow(cv, GetSprite("arrowRight"), white, 1);
            {
                Rect rr = GetSprite("roundel20");
                double cx = rr.x + rr.w / 2.0, cy = rr.y + rr.h / 2.0, rx = rr.w / 2.0 - 3, ry = rr.h / 2.0 - 3;
                for (int y = rr.y; y < rr.y + rr.h; y++)
                    for (int x = rr.x; x < rr.x + rr.w; x++)
                    {
                        double d = Math.Sqrt(Math.Pow((x + 0.5 - cx) / rx, 2) + Math.Pow((y + 0.5 - cy) / ry, 2));
                        if (d < 1 && d > 0.8) Put(im, x, y, 0.7, 0.12, 0.1, 1);
                        else if (d <= 0.8) Put(im, x, y, 0.9, 0.9, 0.87, 1);
                    }
                RoadText(cv, "20", new Rect(rr.x + rr.w / 4, rr.y + rr.h / 4, rr.w / 2, rr.h / 2), new Col(20, 20, 20), 1);
            }
            Wear(im, Sprites["SLOW"], 131); Wear(im, Sprites["BUS STOP"], 133); Wear(im, Sprites["BUS LANE"], 135); Wear(im, Sprites["KEEP CLEAR"], 137);
            Wear(im, Sprites["LOOK RIGHT"], 139); Wear(im, Sprites["arrowStraight"], 141); Wear(im, Sprites["arrowLeft"], 143); Wear(im, Sprites["arrowRight"], 145); Wear(im, Sprites["roundel20"], 147);

            // iron: manholes, gullies, covers
            for (int i = 0; i < 2; i++) Manhole(im, Sprites["manholeRound" + i], true, 150 + i);
            for (int i = 0; i < 2; i++) Manhole(im, Sprites["manholeSquare" + i], false, 160 + i);
            for (int i = 0; i < 2; i++) Gully(im, Sprites["gully" + i], 170 + i);
            for (int i = 0; i < 4; i++) Cover(im, Sprites["cover" + i], i, 180 + i);
            // surface: patches, stains, cracks
            for (int i = 0; i < 4; i++) Patch(im, Sprites["patch" + i], 190 + i);
            for (int i = 0; i < 3; i++) Stain(im, Sprites["stain" + i], 200 + i, 0.08, 0.07, 0.06);
            for (int i = 0; i < 2; i++) Stain(im, Sprites["paveStain" + i], 210 + i, 0.3, 0.27, 0.22);
            for (int i = 0; i < 3; i++) Crack(im, Sprites["crack" + i], 220 + i);
            // L4: grit
            for (int i = 0; i < GritLitter; i++) Litter(cv, Sprites["litter" + i], 300 + i);
            for (int i = 0; i < GritLeaves; i++) Leaves(cv, Sprites["leaves" + i], 320 + i);
            for (int i = 0; i < GritPuddles; i++) Puddle(im, Sprites["puddle" + i], 340 + i);
            for (int i = 0; i < GritPosters; i++) Poster(cv, Sprites["poster" + i], i);
            for (int i = 0; i < GritTags; i++) SmallTag(cv, Sprites["tagS" + i], 360 + i);
            Stickers(cv, Sprites["stickers"], 380);
            Stain(im, Sprites["binjuice"], 390, 0.16, 0.13, 0.07);
            return im;
        }

        // ------------------------------------------------------------------ L4: litter, leaves, puddles, posters, tags
        /// A filled, rotated rectangle (px units), colour by local position (u, v in 0..1).
        static void RotRect(byte[] im, double cx, double cy, double w, double h, double ang, Func<double, double, double[]> col)
        {
            double ca = Math.Cos(ang), sa = Math.Sin(ang), r = Math.Sqrt(w * w + h * h) / 2 + 1;
            for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
                for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
                {
                    double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                    double lx = dx * ca + dy * sa, ly = -dx * sa + dy * ca;
                    if (Math.Abs(lx) > w / 2 || Math.Abs(ly) > h / 2) continue;
                    double[] c = col(lx / w + 0.5, ly / h + 0.5);
                    if (c != null) Put(im, x, y, c[0], c[1], c[2], 1);
                }
        }

        static void Litter(DressAtlas cv, Rect r, int seed)
        {
            Random rnd = new Random(seed);
            byte[] im = cv.px;
            double ppm = DecPxM;
            int n = 4 + rnd.Next(5);
            for (int k = 0; k < n; k++)
            {
                double cx = r.x + 10 + rnd.NextDouble() * (r.w - 20), cy = r.y + 10 + rnd.NextDouble() * (r.h - 20), ang = rnd.NextDouble() * Math.PI;
                int kind = rnd.Next(7);
                switch (kind)
                {
                    case 0:
                        {   // a can lying on its side: coloured body, silver ends, a highlight
                            double[][] cols = { new[] { 0.7, 0.08, 0.08 }, new[] { 0.75, 0.76, 0.78 }, new[] { 0.12, 0.25, 0.6 }, new[] { 0.1, 0.45, 0.2 }, new[] { 0.8, 0.62, 0.15 } };
                            double[] c = cols[rnd.Next(cols.Length)];
                            RotRect(im, cx, cy, 0.12 * ppm, 0.066 * ppm, ang, (u, v) =>
                                u < 0.08 || u > 0.92 ? new[] { 0.7, 0.7, 0.72 } : Math.Abs(v - 0.35) < 0.08 ? new[] { c[0] * 1.5 + 0.2, c[1] * 1.5 + 0.2, c[2] * 1.5 + 0.2 } : c);
                            break;
                        }
                    case 1:
                        {   // crisp packet: shiny, crinkled, coloured bands
                            double[] c = new[] { rnd.NextDouble() * 0.6 + 0.3, rnd.NextDouble() * 0.6 + 0.2, rnd.NextDouble() * 0.5 + 0.1 };
                            RotRect(im, cx, cy, 0.16 * ppm, 0.2 * ppm, ang, (u, v) =>
                            { double k2 = 0.8 + 0.3 * Math.Sin(u * 30 + v * 7); return v > 0.85 || v < 0.1 ? new[] { 0.8 * k2, 0.8 * k2, 0.82 * k2 } : new[] { c[0] * k2, c[1] * k2, c[2] * k2 }; });
                            break;
                        }
                    case 2:
                        {   // takeaway box (white with a red stripe)
                            RotRect(im, cx, cy, 0.22 * ppm, 0.16 * ppm, ang, (u, v) => Math.Abs(v - 0.5) < 0.12 ? new[] { 0.72, 0.1, 0.1 } : new[] { 0.88, 0.86, 0.82 });
                            break;
                        }
                    case 3:
                        {   // paper / receipt, crumpled
                            RotRect(im, cx, cy, 0.1 * ppm, 0.2 * ppm, ang, (u, v) => { double k2 = 0.75 + 0.2 * Math.Sin(u * 21 + v * 13) * Math.Cos(v * 17); return new[] { k2, k2, k2 * 0.97 }; });
                            break;
                        }
                    case 4:
                        {   // coffee cup lying down
                            RotRect(im, cx, cy, 0.14 * ppm, 0.08 * ppm, ang, (u, v) => u > 0.85 ? new[] { 0.15, 0.12, 0.1 } : u > 0.4 && u < 0.6 ? new[] { 0.45, 0.3, 0.18 } : new[] { 0.9, 0.9, 0.88 });
                            break;
                        }
                    case 5:
                        {   // cigarette ends
                            for (int b = 0; b < 4 + rnd.Next(5); b++)
                            {
                                double bx = cx + (rnd.NextDouble() - 0.5) * 40, by = cy + (rnd.NextDouble() - 0.5) * 30;
                                RotRect(im, bx, by, 0.035 * ppm, 0.01 * ppm, rnd.NextDouble() * Math.PI, (u, v) => u < 0.4 ? new[] { 0.78, 0.55, 0.3 } : new[] { 0.9, 0.88, 0.84 });
                            }
                            break;
                        }
                    default:
                        {   // plastic bottle (green or clear)
                            bool green = rnd.NextDouble() < 0.5;
                            RotRect(im, cx, cy, 0.24 * ppm, 0.07 * ppm, ang, (u, v) => u > 0.88 ? new[] { 0.2, 0.3, 0.7 } : green ? new[] { 0.2, 0.45, 0.25 } : new[] { 0.62, 0.68, 0.7 });
                            break;
                        }
                }
            }
        }

        static void Leaves(DressAtlas cv, Rect r, int seed)
        {
            Random rnd = new Random(seed);
            double[][] cols = { new[] { 0.62, 0.32, 0.08 }, new[] { 0.48, 0.25, 0.08 }, new[] { 0.72, 0.52, 0.12 }, new[] { 0.35, 0.22, 0.1 }, new[] { 0.55, 0.42, 0.15 } };
            for (int k = 0; k < 70; k++)
            {
                double cx = r.x + 6 + rnd.NextDouble() * (r.w - 12), cy = r.y + 6 + rnd.NextDouble() * (r.h - 12);
                double dx = (cx - r.x - r.w / 2) / (r.w / 2), dy = (cy - r.y - r.h / 2) / (r.h / 2);
                if (dx * dx + dy * dy > 1 && rnd.NextDouble() < 0.7) continue;            // gathered in a drift
                double[] c = cols[rnd.Next(cols.Length)];
                double sz = 0.06 + rnd.NextDouble() * 0.06;
                RotRect(cv.px, cx, cy, sz * DecPxM, sz * 0.7 * DecPxM, rnd.NextDouble() * Math.PI, (u, v) =>
                {
                    double e = (u - 0.5) * (u - 0.5) * 4 + (v - 0.5) * (v - 0.5) * 4;
                    if (e > 1) return null;
                    double k2 = Math.Abs(v - 0.5) < 0.05 ? 0.7 : 1;                         // the leaf's midrib
                    return new[] { c[0] * k2, c[1] * k2, c[2] * k2 };
                });
            }
        }

        static void Puddle(byte[] im, Rect r, int seed)
        {
            double cx = r.x + r.w / 2.0, cy = r.y + r.h / 2.0;
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                {
                    double lx = (x + 0.5 - cx) / (r.w / 2.0), ly = (y + 0.5 - cy) / (r.h / 2.0);
                    double d = Math.Sqrt(lx * lx + ly * ly) + (Fbm(x, y, 40, seed, DecW) - 0.5) * 0.8;
                    if (d > 0.85) continue;
                    double v = d > 0.72 ? 0.13 : 0.07 + (Fbm(x, y, 12, seed + 3, DecW) - 0.5) * 0.03;   // darker wet rim, glassy middle
                    Put(im, x, y, v, v * 1.05, v * 1.15, 1);
                }
        }

        static readonly string[][] PosterText = {
            new[] { "BASEMENT|SESSIONS|SAT", "111111", "ff3d9a" }, new[] { "LUNDUN|JUNGLE|ALL NIGHT", "f2c200", "111111" },
            new[] { "CASH|FOR|CARS", "f4f1ea", "c8102e" }, new[] { "MAN|& VAN|CHEAP", "e07b1a", "111111" },
            new[] { "LOST|CAT|BISCUIT", "f4f1ea", "222222" }, new[] { "ROOM|TO LET|SE15", "ffffff", "1d4fb8" },
            new[] { "COMEDY|CLUB|FRIDAYS", "1d4fb8", "ffffff" }, new[] { "DUB|SOUND|SYSTEM", "1f6b3a", "f2c200" } };

        static void Poster(DressAtlas cv, Rect r, int i)
        {
            string[] p = PosterText[i % PosterText.Length];
            Col bg = Col.Hex(p[1]), fg = Col.Hex(p[2]);
            cv.Fill(r.x, r.y, r.w, r.h, bg);
            string[] lines = p[0].Split('|');
            int lh = (r.h - 12) / lines.Length;
            for (int k = 0; k < lines.Length; k++)
                cv.Text(lines[k], r.x + 3, r.y + r.h - 6 - (k + 1) * lh, r.w - 6, lh - 2, fg, 0.5, DressAtlas.Align.Centre);
            if (i == 4) cv.Fill(r.x + r.w / 4, r.y + 8, r.w / 2, r.h / 4, Col.Hex("6b5a48"));            // the cat's photo
            // weather, glue and a torn corner
            Random rnd = new Random(400 + i);
            byte[] im = cv.px;
            int tear = rnd.Next(4);
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                {
                    int k = (y * DecW + x) * 4;
                    double lx = (x - r.x) / (double)r.w, ly = (y - r.y) / (double)r.h;
                    double cu = tear % 2 == 0 ? lx : 1 - lx, cvv = tear / 2 == 0 ? ly : 1 - ly;
                    if (cu + cvv < 0.28 + (Fbm(x, y, 8, 410 + i, DecW) - 0.5) * 0.12) { im[k + 3] = 0; continue; }
                    double g = 0.82 + (Fbm(x, y, 20, 420 + i, DecW) - 0.5) * 0.3;
                    im[k] = B(im[k] / 255.0 * g); im[k + 1] = B(im[k + 1] / 255.0 * g); im[k + 2] = B(im[k + 2] / 255.0 * g * 0.95); im[k + 3] = 255;
                }
        }

        static void SmallTag(DressAtlas cv, Rect r, int seed)
        {
            Random rnd = new Random(seed);
            string[] words = { "OSK", "PEK", "RYE", "ZEPH", "MEK", "SE15", "DUSK", "VEX" };
            Col[] cols = { new Col(20, 20, 22), new Col(235, 235, 235), new Col(200, 30, 40), new Col(40, 90, 210), new Col(190, 190, 195), new Col(140, 60, 200) };
            string w = words[rnd.Next(words.Length)];
            Col c = cols[rnd.Next(cols.Length)];
            int n = PixelFont.Columns(w);
            double cs = Math.Min((r.w - 10) / (double)n, (r.h - 10) / 8.0);
            double x = r.x + 5, top = r.y + r.h - 6, shear = 0.25 + rnd.NextDouble() * 0.3;
            bool bubble = rnd.NextDouble() < 0.4;
            foreach (char ch in w)
            {
                if (bubble) { cv.Glyph(ch, x, top, cs, cs * 0.75 + 2, new Col(15, 15, 15), shear, 1); cv.Glyph(ch, x, top, cs, cs * 0.75, c, shear, 1); }
                else cv.Glyph(ch, x, top, cs, Math.Max(1.2, cs * 0.18), c, shear, 1.1);
                x += PixelFont.Advance(ch) * cs;
            }
            cv.Seg(r.x + 4, r.y + 6, x, r.y + 4 + rnd.NextDouble() * 6, 1.2, c);
        }

        static void Stickers(DressAtlas cv, Rect r, int seed)
        {
            Random rnd = new Random(seed);
            for (int k = 0; k < 9; k++)
            {
                int w = 10 + rnd.Next(18), h = 8 + rnd.Next(14);
                int x = r.x + rnd.Next(Math.Max(1, r.w - w)), y = r.y + rnd.Next(Math.Max(1, r.h - h));
                Col c = new Col(rnd.Next(60, 255), rnd.Next(30, 240), rnd.Next(30, 240));
                cv.Fill(x, y, w, h, c);
                cv.Fill(x + 2, y + h / 2 - 1, w - 4, 2, new Col(20, 20, 20));
            }
        }

        static void Put(byte[] im, int x, int y, double r, double g, double b, double a)
        { if (x < 0 || y < 0 || x >= DecW || y >= DecH) return; Px(im, DecW, x, y, r, g, b, a); }

        /// Text whose lines stack in the direction of travel ('|' = new line; first line nearest the driver… so last line on top).
        static void RoadText(DressAtlas cv, string text, Rect r, Col c, int lines)
        {
            string[] parts = text.Split('|');
            int n = parts.Length;
            double lineH = r.h / (double)n;
            for (int i = 0; i < n; i++)
            {
                // the first word is read first, so it's nearest the driver (bottom of the sprite)
                string s = parts[i];
                int cols = Math.Max(1, PixelFont.Columns(s));
                double cs = (r.w - 8) / (double)cols;
                double scaleY = (lineH * 0.82) / (7 * cs);
                double x = r.x + 4 + (r.w - 8 - cols * cs) / 2;
                double top = r.y + (i + 1) * lineH - lineH * 0.08;
                foreach (char ch in s.ToUpperInvariant())
                {
                    cv.Glyph(ch, x, top, cs, cs * 0.45, c, 0, scaleY);
                    x += PixelFont.Advance(ch) * cs;
                }
            }
        }

        static void Arrow(DressAtlas cv, Rect r, Col c, int turn)
        {
            double cx = r.x + r.w / 2.0, w = 0.12 * DecPxM;
            if (turn == 0)
            {
                cv.Seg(cx, r.y + 6, cx, r.y + r.h * 0.72, w / 2, c);
                for (int k = 0; k < 40; k++) { double t = k / 39.0; cv.Seg(cx - r.w * 0.42 * (1 - t), r.y + r.h * 0.7 + t * r.h * 0.28, cx + r.w * 0.42 * (1 - t), r.y + r.h * 0.7 + t * r.h * 0.28, 1.5, c); }
                return;
            }
            double sx = r.x + r.w / 2.0 - turn * r.w * 0.2;
            cv.Seg(sx, r.y + 6, sx, r.y + r.h * 0.55, w / 2, c);
            double ex = sx + turn * r.w * 0.28, ey = r.y + r.h * 0.75;
            cv.Seg(sx, r.y + r.h * 0.55, ex, ey, w / 2, c);
            for (int k = 0; k < 30; k++)
            {
                double t = k / 29.0;
                double bx = ex + turn * t * r.w * 0.18, by = ey + t * r.h * 0.2;
                cv.Seg(bx - r.w * 0.2 * (1 - t), by, bx + r.w * 0.2 * (1 - t), by, 1.5, c);
            }
        }

        static void Wear(byte[] im, Rect r, int seed)
        {
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                {
                    int i = (y * DecW + x) * 4;
                    if (im[i + 3] < 128) continue;
                    if (Fbm(x, y, 36, seed, DecW) > 0.71 || R01(x, y, seed) < 0.035) im[i + 3] = 0;
                    else { double k = 0.86 + (Fbm(x, y, 14, seed + 1, DecW) - 0.5) * 0.2; im[i] = B(im[i] / 255.0 * k); im[i + 1] = B(im[i + 1] / 255.0 * k); im[i + 2] = B(im[i + 2] / 255.0 * k); }
                }
        }

        static void Manhole(byte[] im, Rect r, bool round, int seed)
        {
            double cx = r.x + r.w / 2.0, cy = r.y + r.h / 2.0;
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                {
                    double lx = (x + 0.5 - cx) / (r.w / 2.0), ly = (y + 0.5 - cy) / (r.h / 2.0);
                    double d = round ? Math.Sqrt(lx * lx + ly * ly) : Math.Max(Math.Abs(lx), Math.Abs(ly));
                    if (d > 1) continue;
                    double v;
                    if (d > 0.9) v = 0.32;                                           // frame
                    else if (d > 0.86) v = 0.12;                                     // gap
                    else
                    {
                        // cast pattern: round = concentric rings + grid, square = chequer
                        bool raised = round ? (((int)(d * 9)) % 2 == 0) ^ (((int)((lx + 2) * 8)) % 2 == 0 && d < 0.5)
                                            : ((((int)((lx + 2) * 7)) + ((int)((ly + 2) * 7))) % 2 == 0);
                        v = raised ? 0.4 : 0.26;
                        if (Math.Abs(lx) < 0.25 && Math.Abs(ly) < 0.06) v = 0.2;          // lifting slot
                    }
                    v *= 0.85 + (Fbm(x, y, 8, seed, DecW) - 0.5) * 0.4;
                    double rust = Fbm(x, y, 14, seed + 3, DecW);
                    double rr = v, gg = v * 0.97, bb = v * 0.95;
                    if (rust > 0.6) { rr = v * 1.25 + 0.04; gg = v * 0.85; bb = v * 0.65; }
                    Put(im, x, y, rr, gg, bb, 1);
                }
        }

        static void Gully(byte[] im, Rect r, int seed)
        {
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                {
                    double lx = (x - r.x) / (double)r.w, ly = (y - r.y) / (double)r.h;
                    bool frame = lx < 0.08 || lx > 0.92 || ly < 0.12 || ly > 0.88;
                    bool slot = !frame && ((x - r.x) % 9) < 5;
                    double v = frame ? 0.3 : slot ? 0.05 : 0.33;
                    v *= 0.85 + (Fbm(x, y, 6, seed, DecW) - 0.5) * 0.4;
                    Put(im, x, y, v * 1.05, v, v * 0.92, 1);
                }
        }

        static void Cover(byte[] im, Rect r, int kind, int seed)
        {
            // small utility covers on pavements: steel with a chequer, or concrete-filled with a steel rim
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                {
                    double lx = (x - r.x) / (double)r.w, ly = (y - r.y) / (double)r.h;
                    bool rim = lx < 0.07 || lx > 0.93 || ly < 0.07 || ly > 0.93;
                    double v;
                    if (rim) v = 0.35;
                    else if (kind == 1 || kind == 3) v = 0.55 + (R01(x, y, seed) - 0.5) * 0.12;   // concrete infill
                    else v = (((x / 4) + (y / 4)) % 2 == 0) ? 0.42 : 0.3;
                    v *= 0.9 + (Fbm(x, y, 6, seed, DecW) - 0.5) * 0.3;
                    Put(im, x, y, v, v, v * 0.97, 1);
                }
        }

        static void Patch(byte[] im, Rect r, int seed)
        {
            // a reinstatement patch: darker, smoother tarmac with a sealant border, slightly irregular
            Random rnd = new Random(seed);
            double ix = 0.06 + rnd.NextDouble() * 0.12, iy = 0.08 + rnd.NextDouble() * 0.15;
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                {
                    double lx = (x - r.x) / (double)r.w, ly = (y - r.y) / (double)r.h;
                    double edge = (Fbm(x, y, 18, seed, DecW) - 0.5) * 0.05;
                    double dx = Math.Min(lx - ix, 1 - ix - lx) + edge, dy = Math.Min(ly - iy, 1 - iy - ly) + edge;
                    double d = Math.Min(dx, dy);
                    if (d < 0) continue;
                    double v = d < 0.015 ? 0.07 : 0.16 + (R01(x, y, seed) - 0.5) * 0.06 + (Fbm(x, y, 24, seed + 1, DecW) - 0.5) * 0.05;
                    Put(im, x, y, v, v, v * 1.03, 1);
                }
        }

        static void Stain(byte[] im, Rect r, int seed, double cr, double cg, double cb)
        {
            double cx = r.x + r.w / 2.0, cy = r.y + r.h / 2.0;
            for (int y = r.y; y < r.y + r.h; y++)
                for (int x = r.x; x < r.x + r.w; x++)
                {
                    double lx = (x + 0.5 - cx) / (r.w / 2.0), ly = (y + 0.5 - cy) / (r.h / 2.0);
                    double d = Math.Sqrt(lx * lx + ly * ly) + (Fbm(x, y, 20, seed, DecW) - 0.5) * 0.7;
                    if (d > 0.85) continue;
                    double dither = R01(x, y, seed + 7);
                    if (d > 0.6 && dither < (d - 0.6) / 0.25) continue;               // feathered edge (clipped speckle)
                    double k = 0.7 + 0.3 * d;
                    Put(im, x, y, cr * k, cg * k, cb * k, 1);
                }
        }

        static void Crack(byte[] im, Rect r, int seed)
        {
            Random rnd = new Random(seed);
            double y = r.y + r.h / 2.0, x = r.x + 2, a = 0;
            while (x < r.x + r.w - 2)
            {
                a += (rnd.NextDouble() - 0.5) * 0.6; a = Math.Max(-0.6, Math.Min(0.6, a));
                x += Math.Cos(a) * 1.5; y += Math.Sin(a) * 1.5;
                y = Math.Max(r.y + 3, Math.Min(r.y + r.h - 3, y));
                for (int dy = -1; dy <= 1; dy++) Put(im, (int)x, (int)y + dy, 0.06, 0.06, 0.06, 1);
                if (rnd.NextDouble() < 0.04)
                {
                    // a branch
                    double bx = x, by = y, ba = a + (rnd.NextDouble() < 0.5 ? 1 : -1) * 0.9;
                    for (int k = 0; k < 25; k++) { bx += Math.Cos(ba); by += Math.Sin(ba); if (by < r.y + 2 || by > r.y + r.h - 2) break; Put(im, (int)bx, (int)by, 0.07, 0.07, 0.07, 1); }
                }
            }
        }
    }
}
