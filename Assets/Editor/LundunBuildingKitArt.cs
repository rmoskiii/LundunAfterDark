// L1: the building kit's two texture atlases, painted in code (pure C#, no downloads, no branding).
//   Atlas A "traditional": London brick terraces, shop parades, white stucco, red-brick mansion blocks, Georgian brick,
//                          slate roofs, bays, mansards, cornices.
//   Atlas B "modern":      the estate (deck access, maisonettes, garages, the tower), Canary warehouses, glass and stone
//                          offices, lobbies, rooftop plant.
// Each atlas is 1024 × 4096: 32 horizontal strips, 128 px apart (4 px padding, 120 px of picture). Every strip is one
// storey of one facade type and repeats every 32 m along a wall (32 px per metre), so walls of any length tile cleanly.
// A strip is divided into "units" (a house, a shop, a bay) that the geometry lines up with.
// RGB = colour, A = smoothness (glass shiny, brick matte). A second, half-size map holds the night-time window glow.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static class KitArt
    {
        public const int W = 1024, H = 4096, Pitch = 128, Pad = 4, Content = 120, GlowW = 512, GlowH = 2048;
        public const double PxPerM = 32.0, RepeatM = 32.0;

        // ------------------------------------------------------------------ strips (row numbers)
        public const int Swatch = 0;
        // atlas A
        public const int A_BrickRed = 1, A_BrickYel = 2, A_Render = 3, A_Stucco = 4, A_Slate = 5, A_RoofFlat = 6,
            A_TUpRed = 7, A_TUpYel = 8, A_TUpPaint = 9, A_TGfRed = 10, A_TGfYel = 11, A_TGfPaint = 12, A_Bay = 13,
            A_PUpYel = 14, A_PUpRed = 15, A_PUpPaint = 16, A_ShopGf = 17, A_StuccoUp = 18, A_StuccoGf = 19,
            A_MansionUp = 20, A_MansionGf = 21, A_GeorgUp = 22, A_GeorgGf = 23, A_Mansard = 24, A_Cornice = 25,
            A_MansionPlain = 26, A_GeorgPlain = 27, A_PubGf = 28, A_WheelSigns = 29,     // E7: the Lundun Wheel's signs
            A_BrixtunSigns = 30,                                                         // E1: Brixtun's landmark buildings' names (unit 4: E2 station board)
            A_BrixGirder = 31;                                                           // E2: Brixtun's bridge girders
        // atlas B
        public const int B_Concrete = 1, B_ConcPanel = 2, B_EstDeck = 3, B_EstBack = 4, B_EstBrickUp = 5, B_EstGf = 6,
            B_Garages = 7, B_TowerUp = 8, B_StairCore = 9, B_WhUpYel = 10, B_WhUpRed = 11, B_WhGf = 12, B_GlassBlue = 13,
            B_GlassGreen = 14, B_OfficeStone = 15, B_OfficeDark = 16, B_Lobby = 17, B_RoofFlat = 18, B_Plant = 19,
            B_Balcony = 20, B_GlassSilver = 21, B_EstBrick = 22, B_WhYel = 23, B_WhRed = 24, B_TowerUpB = 25, B_EstShop = 26,
            B_RailArchLo = 27, B_RailArchHi = 28, B_Girder = 29, B_Train = 30, B_Station = 31;     // railway: viaduct arches (two strips, 0–3.85 m and 3.85–7.7 m), bridge girders, train sides, station boards

        // design storey height (m) and unit width (m) of every strip
        static readonly double[] FloorA = { 3.75, 3.2, 3.2, 3.2, 3.6, 7.0, 16, 3.1, 3.1, 3.1, 3.3, 3.3, 3.3, 3.2, 3.2, 3.2, 3.2, 4.0, 3.6, 3.8, 3.4, 3.8, 3.5, 3.6, 3.4, 1.0, 3.4, 3.5, 4.0, 1.2, 1.2, 2.6 };
        static readonly double[] UnitA = { 1, 32, 32, 32, 32, 32, 32, 5.3333333, 5.3333333, 5.3333333, 5.3333333, 5.3333333, 5.3333333, 1, 6.4, 6.4, 6.4, 6.4, 6.4, 6.4, 8, 8, 5.3333333, 5.3333333, 6.4, 32, 32, 32, 8, 6.4, 6.4, 32 };
        static readonly double[] FloorB = { 3.75, 3.0, 3.0, 2.9, 2.9, 2.9, 3.0, 2.6, 2.9, 3.0, 3.6, 3.6, 4.2, 3.8, 3.8, 3.8, 3.8, 4.8, 16, 3.0, 1.1, 3.8, 2.9, 3.6, 3.6, 2.9, 4.0, 3.85, 3.85, 2.6, 2.9, 1.2 };
        static readonly double[] UnitB = { 1, 32, 4, 5.3333333, 5.3333333, 5.3333333, 5.3333333, 2.6666667, 4, 4, 10.666667, 10.666667, 10.666667, 1.6, 1.6, 1.6, 1.6, 8, 32, 32, 5.3333333, 1.6, 32, 32, 32, 4, 6.4, 8, 8, 32, 10.666667, 6.4 };

        public static double FloorH(int atlas, int row) { return atlas == 0 ? FloorA[row] : FloorB[row]; }
        public static double UnitW(int atlas, int row) { return atlas == 0 ? UnitA[row] : UnitB[row]; }
        public static int Units(int atlas, int row) { return (int)Math.Round(RepeatM / UnitW(atlas, row)); }
        /// V of a strip: t = 0 bottom of the storey, 1 = top.
        public static double V(int row, double t) { return (row * Pitch + Pad + Math.Max(0, Math.Min(1, t)) * Content) / H; }
        /// A flat colour: every vertex gets this UV.
        public static void SwatchUV(int i, out double u, out double v) { u = (i * 32 + 16) / (double)W; v = V(Swatch, 0.5); }

        // swatch numbers (same in both atlases)
        public const int S_Terracotta = 0, S_Lead = 1, S_Black = 2, S_White = 3, S_Portland = 4, S_Slate = 5, S_Bitumen = 6,
            S_RedBrick = 7, S_YelBrick = 8, S_Cream = 9, S_Concrete = 10, S_Glass = 11, S_Green = 12, S_Verdigris = 13,
            S_Timber = 14, S_Steel = 15, S_Galv = 16, S_Rust = 17, S_Navy = 18, S_Yellow = 19, S_Orange = 20, S_Red = 21,
            S_SkyGlass = 22, S_BrownBrick = 23, S_PaleConc = 24, S_SootStone = 25, S_LightGrey = 26, S_MidGrey = 27,
            S_DarkGrey = 28, S_Maroon = 29, S_Teal = 30, S_Render2 = 31;
        static readonly string[] SwatchHex = {
            "8a4a32", "5d6268", "1c1d1f", "dcdcd6", "c9c3b4", "3d4248", "26272a", "8c4a38", "b39b72", "d8cfb8", "8e8c88", "1d2329",
            "2c4a36", "5f8f7f", "6a4a30", "7d838a", "a6abae", "7a4128", "1f2a4a", "d8a91f", "c8622a", "8e2a24", "6f8aa5", "6b4a38",
            "b4b0a8", "8c867c", "b6b8b8", "818385", "4a4c4f", "5c2224", "2f6d6d", "e2d8c0" };

        // ------------------------------------------------------------------ output
        public class Atlas { public byte[] rgba; public byte[] glow; }       // rgba W×H, glow GlowW×GlowH (both row 0 = bottom)

        struct Tx { public double r, g, b, s, er, eg, eb; }

        class Op
        {
            public char k;                  // W window, D door, F fanlight, S shop, U shutter, G garage, P panel, B band, I pier,
                                            // R recess, L louvre, C curtain panel, T tiles, O loading door, X fascia, A arch window
            public double x0, x1, y0, y1;
            public int style, id;
            public double r, g, b;
            public bool sill, rail, tag, frosted, arch;
            public int lintel;              // 0 none, 1 stone, 2 gauged brick, 3 stucco hood, 4 segmental brick arch, 5 keystone stone
            public double frame = 0.92, lit = 0.35;
            public string text;
            public bool fullText;           // railway: the text fills the whole op (station boards)
            public string piece;            // railway: a big graffiti piece of this word, drawn by the overlay
        }

        class Facade
        {
            public int row; public char wall; public double r, g, b; public int brick;  // wall: K brick, E render, U stucco, Q rusticated stucco, N concrete, M panel concrete, T stone, S slate, F flat roof, V louvre wall, Z cornice
            public Func<int, List<Op>> layout;
            public Func<int, double[]> unitColour;    // painted walls: a colour per unit
            public bool dirt, soot = true;
            public double yOff;                       // railway arches: this strip starts yOff metres up the wall
            public List<Op>[] cache;
        }

        // ------------------------------------------------------------------ noise
        static uint Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
        static double R01(int x, int y, int seed) { return (Hash(x, y, seed) & 0xFFFFFF) / (double)0xFFFFFF; }
        static double Value(double x, double y, int period, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int ax = ((x0 % period) + period) % period, bx = (((x0 + 1) % period) + period) % period;
            double v00 = R01(ax, y0, seed), v10 = R01(bx, y0, seed), v01 = R01(ax, y0 + 1, seed), v11 = R01(bx, y0 + 1, seed);
            return (v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy;
        }
        /// Noise that wraps horizontally every 1024 px (so the strips tile along walls).
        static double Fbm(double px, double py, double cellPx, int seed)
        {
            double s = 0, amp = 0.5, tot = 0; double cell = cellPx;
            for (int o = 0; o < 3; o++)
            {
                int period = Math.Max(1, (int)Math.Round(W / cell));
                s += amp * Value(px / cell, py / cell, period, seed + o * 31);
                tot += amp; amp *= 0.5; cell *= 0.5;
            }
            return s / tot;
        }
        static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double Smooth(double e0, double e1, double x) { double t = Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
        static void Hex(string h, out double r, out double g, out double b)
        {
            r = Convert.ToInt32(h.Substring(0, 2), 16) / 255.0; g = Convert.ToInt32(h.Substring(2, 2), 16) / 255.0; b = Convert.ToInt32(h.Substring(4, 2), 16) / 255.0;
        }
        static double[] C(string h) { double r, g, b; Hex(h, out r, out g, out b); return new[] { r, g, b }; }

        // ------------------------------------------------------------------ palettes
        static readonly double[][] Paint6 = { C("dcd2b6"), C("a9bccb"), C("aebf9c"), C("d6afa6"), C("e0dfd8"), C("b3adbd") };   // painted houses
        static readonly double[][] Door6 = { C("17181a"), C("1d2b4f"), C("6b1d1d"), C("1f3d2c"), C("c99a24"), C("2d6466") };
        static readonly double[][] EstDoor6 = { C("8e2a24"), C("2a4a8a"), C("2c6a3a"), C("c9a227"), C("2b2b2b"), C("7d838a") };
        static readonly double[][] Garage12 = { C("d8d6cf"), C("3b5e3e"), C("2d4a7a"), C("8a4b2a"), C("9da3a6"), C("5c2224"), C("cfcab8"), C("3a3c40"), C("6f7f5a"), C("b8b2a0"), C("27456b"), C("8e8c88") };
        static readonly double[][] Panel6 = { C("a8643a"), C("40607e"), C("587048"), C("b0954a"), C("c8c4b8"), C("86443a") };
        static readonly double[][] Fascia5 = { C("1f2a3a"), C("2c4a36"), C("5c2224"), C("232323"), C("6a4a30") };

        // brick: base colour, variation, mortar
        static readonly double[][] BrickBase = { C("8a4632"), C("b09668"), C("7e7058"), C("6e4a38"), C("9a5a3e") };   // red, yellow stock, dark stock, brown estate, warm warehouse red
        static readonly double[][] BrickMortar = { C("b8b0a2"), C("c9c0aa"), C("a9a395"), C("a8a196"), C("bdb3a0") };

        // ------------------------------------------------------------------ paint everything
        public static Atlas Paint(int atlas)
        {
            DressAtlas cv = new DressAtlas(W, H, 4100 + atlas);
            double[] smooth = new double[W * H];
            float[] glow = new float[W * H * 3];
            List<Facade> list = atlas == 0 ? DefsA() : DefsB();

            PaintSwatches(cv, smooth);
            foreach (Facade f in list) PaintStrip(cv, smooth, glow, f, atlas);
            foreach (Facade f in list) Overlay(cv, f, atlas);

            // padding rows copy the strip's edge rows (no bleeding into the neighbour)
            for (int row = 0; row < 32; row++)
            {
                int y0 = row * Pitch + Pad, y1 = y0 + Content - 1;
                for (int p = 1; p <= Pad; p++)
                {
                    CopyRow(cv.px, smooth, glow, y0, y0 - p);
                    if (y1 + p < H) CopyRow(cv.px, smooth, glow, y1, y1 + p);
                }
            }

            Atlas a = new Atlas { rgba = cv.px, glow = new byte[GlowW * GlowH * 4] };
            for (int i = 0; i < W * H; i++) a.rgba[i * 4 + 3] = (byte)Math.Round(Clamp01(smooth[i]) * 255);
            for (int y = 0; y < GlowH; y++)
                for (int x = 0; x < GlowW; x++)
                {
                    double r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                        { int k = ((y * 2 + dy) * W + x * 2 + dx) * 3; r += glow[k]; g += glow[k + 1]; b += glow[k + 2]; }
                    int o = (y * GlowW + x) * 4;
                    a.glow[o] = B(r / 4); a.glow[o + 1] = B(g / 4); a.glow[o + 2] = B(b / 4); a.glow[o + 3] = 255;
                }
            return a;
        }

        static byte B(double v) { return (byte)Math.Round(Clamp01(v) * 255); }

        static void CopyRow(byte[] px, double[] s, float[] gl, int from, int to)
        {
            if (to < 0 || to >= H) return;
            Array.Copy(px, from * W * 4, px, to * W * 4, W * 4);
            Array.Copy(s, from * W, s, to * W, W);
            Array.Copy(gl, from * W * 3, gl, to * W * 3, W * 3);
        }

        static void PaintSwatches(DressAtlas cv, double[] smooth)
        {
            for (int i = 0; i < 32; i++)
            {
                double r, g, b; Hex(SwatchHex[i], out r, out g, out b);
                double sm = i == S_Glass || i == S_SkyGlass ? 0.85 : i == S_Black || i == S_Navy || i == S_Green || i == S_Maroon || i == S_Teal ? 0.45 : i == S_Steel || i == S_Galv || i == S_Lead ? 0.4 : 0.12;
                for (int y = 0; y < Content; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        int gx = i * 32 + x, gy = Swatch * Pitch + Pad + y;
                        double n = 1 + (Fbm(gx, gy, 16, 300 + i) - 0.5) * 0.06;
                        int k = (gy * W + gx) * 4;
                        cv.px[k] = B(r * n); cv.px[k + 1] = B(g * n); cv.px[k + 2] = B(b * n); cv.px[k + 3] = 255;
                        smooth[gy * W + gx] = sm;
                    }
            }
        }

        // ------------------------------------------------------------------ one strip
        static void PaintStrip(DressAtlas cv, double[] smooth, float[] glow, Facade f, int atlas)
        {
            double fh = FloorH(atlas, f.row), uw = UnitW(atlas, f.row);
            int units = Units(atlas, f.row);
            f.cache = new List<Op>[units];
            for (int u = 0; u < units; u++) f.cache[u] = f.layout != null ? f.layout(u) : new List<Op>();
            double pyPerM = Content / fh;
            for (int py = 0; py < Content; py++)
            {
                int gy = f.row * Pitch + Pad + py;
                double ym = (py + 0.5) / pyPerM;
                for (int px = 0; px < W; px++)
                {
                    double xm = (px + 0.5) / PxPerM;
                    int u = Math.Min(units - 1, (int)(xm / uw));
                    double ux = xm - u * uw;
                    Tx t = new Tx();
                    Pixel(ref t, f, f.cache[u], u, ux, ym, uw, fh, px, gy, pyPerM);
                    int k = gy * W + px;
                    cv.px[k * 4] = B(t.r); cv.px[k * 4 + 1] = B(t.g); cv.px[k * 4 + 2] = B(t.b); cv.px[k * 4 + 3] = 255;
                    smooth[k] = t.s;
                    glow[k * 3] = (float)t.er; glow[k * 3 + 1] = (float)t.eg; glow[k * 3 + 2] = (float)t.eb;
                }
            }
        }

        static void Pixel(ref Tx t, Facade f, List<Op> ops, int u, double x, double y, double uw, double fh, int gx, int gy, double pyPerM)
        {
            Wall(ref t, f, u, x, y, gx, gy, pyPerM);
            double streak = 0;
            // surrounds first (sills, lintels, hoods), then the openings over them
            foreach (Op o in ops)
            {
                if (o.k == 'W' || o.k == 'A')
                {
                    if (o.sill && y < o.y0 && y >= o.y0 - 0.1 && x >= o.x0 - 0.07 && x < o.x1 + 0.07)
                    {
                        double sv = y > o.y0 - 0.03 ? 0.9 : 0.78;
                        Set(ref t, 0.78 * sv + 0.1, 0.76 * sv + 0.1, 0.71 * sv + 0.1, 0.15);
                    }
                    if (y < o.y0 - 0.1 && y > o.y0 - 0.1 - 1.4 && x >= o.x0 + 0.05 && x < o.x1 - 0.05)
                        streak = Math.Max(streak, (1 - (o.y0 - 0.1 - y) / 1.4) * (0.6 + 0.4 * Value(gx / 3.0, 0, 1 << 20, 7)));
                    Lintel(ref t, o, x, y, gx, gy);
                    if (o.k == 'W' && o.lintel == 3 && x >= o.x0 - 0.16 && x < o.x1 + 0.16 && y >= o.y0 - 0.06 && y < o.y1 + 0.1 && !(x >= o.x0 && x < o.x1 && y >= o.y0 && y < o.y1))
                    {
                        bool edge = x < o.x0 - 0.12 || x >= o.x1 + 0.12 || y >= o.y1 + 0.06;
                        Set(ref t, edge ? 0.74 : 0.86, edge ? 0.73 : 0.85, edge ? 0.7 : 0.82, 0.25);   // architrave
                    }
                }
            }
            if (streak > 0 && f.wall != 'S' && f.wall != 'F') { double k = 1 - 0.13 * streak; t.r *= k; t.g *= k; t.b *= k; }
            foreach (Op o in ops) OpPixel(ref t, o, u, x, y, gx, gy, pyPerM);
            // ground-floor grime and a general darkening towards the bottom of the storey (shadow under the floor above)
            if (f.dirt && y < 0.6) { double k = 0.8 + 0.2 * Smooth(0, 0.6, y) + (Fbm(gx, gy, 12, 61) - 0.5) * 0.08; t.r *= k; t.g *= k; t.b *= k; }
        }

        static void Set(ref Tx t, double r, double g, double b, double s) { t.r = r; t.g = g; t.b = b; t.s = s; t.er = t.eg = t.eb = 0; }

        // ------------------------------------------------------------------ walls
        static void Wall(ref Tx t, Facade f, int u, double x, double y, int gx, int gy, double pyPerM)
        {
            double grime = 1 + (Fbm(gx, gy, 96, 11) - 0.5) * 0.14 + (Value(gx / 5.0, gy / 70.0, 1 << 20, 13) - 0.5) * 0.03;
            switch (f.wall)
            {
                case 'K': Brick(ref t, f.brick, x + u * 1000.37, y, gx, gy, pyPerM, f.row); break;
                case 'E':
                case 'U':
                case 'Q':
                    {
                        double[] c = f.unitColour != null ? f.unitColour(u) : new[] { f.r, f.g, f.b };
                        double v = 1 + (Fbm(gx, gy, 24, 21) - 0.5) * 0.05;
                        if (f.wall == 'Q')
                        {
                            double gy2 = (y % 0.4), gx2 = ((x + (Math.Floor(y / 0.4) % 2) * 0.6) % 1.2);
                            if (gy2 < 0.035 || gx2 < 0.03) v *= 0.82; else if (gy2 < 0.06) v *= 1.04;
                        }
                        Set(ref t, c[0] * v, c[1] * v, c[2] * v, f.wall == 'E' ? 0.18 : 0.3);
                        // hairline cracks and patches of older paint
                        if (Fbm(gx, gy, 40, 29) > 0.7) { t.r *= 0.975; t.g *= 0.975; t.b *= 0.97; }
                        break;
                    }
                case 'N':
                case 'M':
                    {
                        double v = 0.56 + (Fbm(gx, gy, 32, 41) - 0.5) * 0.1 + (Value(gx / 4.0, gy / 40.0, 1 << 20, 43) - 0.5) * 0.035;
                        if (((int)(y / 0.15)) % 2 == 0) v *= 0.985;                                   // board marks
                        if (f.wall == 'M')
                        {
                            double px = x % 4.0, py = y % 2.9;
                            if (px < 0.03 || py < 0.03) v *= 0.7;                                     // panel joints
                        }
                        Set(ref t, v, v * 0.99, v * 0.96, 0.12);
                        break;
                    }
                case 'T':
                    {
                        double v = 0.76 + (Fbm(gx, gy, 32, 51) - 0.5) * 0.08;
                        double cy = y % 0.38, cx = (x + (Math.Floor(y / 0.38) % 2) * 0.45) % 0.9;
                        if (cy < 0.012 || cx < 0.01) v *= 0.86;
                        Set(ref t, v, v * 0.97, v * 0.9, 0.15);
                        break;
                    }
                case 'S':
                    {
                        // slates, drawn small vertically (the strip is stretched over a long roof slope)
                        double course = 0.24, cy = y / course; int ci = (int)Math.Floor(cy);
                        double off = (ci % 2) * 0.15, sx = (x + off) / 0.3; int si = (int)Math.Floor(sx);
                        double fy = cy - ci, fx = sx - si;
                        double v = 0.27 + (R01(si, ci, 71) - 0.5) * 0.06 + (Fbm(gx, gy, 64, 73) - 0.5) * 0.06;
                        if (fy < 0.12) v *= 0.55;                                                     // shadow under the course above
                        if (fx < 0.05) v *= 0.75;
                        if (R01(si, ci, 75) < 0.02) v *= 1.25;                                        // a replaced slate
                        double moss = Fbm(gx, gy, 48, 77);
                        Set(ref t, v * 1.0, v * 1.04, v * 1.12, 0.3);
                        if (moss > 0.72) { t.r = t.r * 0.9 + 0.03; t.g = t.g * 0.9 + 0.045; t.b *= 0.9; }
                        break;
                    }
                case 'F':
                    {
                        // flat roof: bitumen felt with lighter gravel patches and seams
                        double v = 0.22 + (Fbm(gx, gy, 48, 81) - 0.5) * 0.08 + (R01(gx, gy, 83) - 0.5) * 0.05;
                        if (((int)(x / 1.0)) % 1 == 0 && (x % 1.0) < 0.04) v *= 0.85;
                        if (Fbm(gx, gy, 96, 85) > 0.62) v = v * 0.85 + 0.05;
                        Set(ref t, v, v, v * 1.02, 0.2);
                        break;
                    }
                case 'V':
                    {
                        double v = (y % 0.12) < 0.04 ? 0.32 : 0.52 + (Fbm(gx, gy, 32, 91) - 0.5) * 0.06;
                        if ((x % 2.0) < 0.08) v = 0.4;
                        Set(ref t, v, v * 1.01, v * 1.04, 0.35);
                        break;
                    }
                case 'Z':
                    {
                        // cornice: frieze, mouldings with dentils, coping
                        double v = 0.86;
                        if (y < 0.42) v = 0.84;
                        else if (y < 0.47) v = 0.62;
                        else if (y < 0.6) v = ((x % 0.18) < 0.09) ? 0.9 : 0.58;                        // dentils
                        else if (y < 0.66) v = 0.95;
                        else if (y < 0.72) v = 0.66;
                        else if (y < 0.86) v = 0.92 - (y - 0.72) * 1.2;
                        else v = 0.9;
                        v *= 1 + (Fbm(gx, gy, 24, 93) - 0.5) * 0.06;
                        Set(ref t, v * f.r, v * f.g, v * f.b, 0.25);
                        break;
                    }
                case 'G':
                    { Set(ref t, 0.2, 0.22, 0.25, 0.9); break; }
                case 'Y': RailArchWall(ref t, f, u, x, y + f.yOff, gx, gy, pyPerM); break;
                case 'H': GirderWall(ref t, f, x, y, gx, gy); break;
                case 'J': TrainWall(ref t, x, y, gx, gy); break;
                default: Set(ref t, f.r, f.g, f.b, 0.2); break;
            }
            if (f.soot && f.wall != 'S' && f.wall != 'F') { t.r *= grime; t.g *= grime; t.b *= grime; }
        }

        static void Brick(ref Tx t, int pal, double x, double y, int gx, int gy, double pyPerM, int seed)
        {
            const double ch = 0.075, bl = 0.225;
            int course = (int)Math.Floor(y / ch);
            bool flemish = pal == 2;
            double len = bl, off = (course % 2) * bl * 0.5;
            double bx, fx; int bid;
            if (flemish)
            {
                // alternating header (½) and stretcher (1) bricks, offset by ¾ brick every other course
                double period = bl * 1.5, xx = x + (course % 2) * bl * 0.75;
                double pp = ((xx % period) + period) % period; int pi = (int)Math.Floor(xx / period);
                if (pp < bl) { fx = pp; len = bl; bid = pi * 2; } else { fx = pp - bl; len = bl * 0.5; bid = pi * 2 + 1; }
            }
            else { bx = (x + off) / bl; bid = (int)Math.Floor(bx); fx = (bx - bid) * bl; }
            double fy = y - course * ch;
            double dx = Math.Min(fx, len - fx) * PxPerM, dy = Math.Min(fy, ch - fy) * pyPerM;
            double mortar = Clamp01(1 - Math.Min(dx, dy) / 0.65) * 0.8;
            double[] bc = BrickBase[pal], mc = BrickMortar[pal];
            double tone = 1 + (R01(bid, course, 5 + seed) - 0.5) * (pal == 1 ? 0.22 : 0.18) + (R01(bid, course, 9) - 0.5) * 0.05;
            double warm = (R01(bid, course, 17) - 0.5) * 0.12;
            double r = bc[0] * tone * (1 + warm), g = bc[1] * tone, b = bc[2] * tone * (1 - warm * 0.5);
            if (pal == 1 || pal == 2)
            {
                // London stock: dark flecks and the odd burnt brick
                if (R01(gx, gy, 19) < 0.06) { r *= 0.7; g *= 0.7; b *= 0.72; }
                if (R01(bid, course, 23) < 0.07) { r *= 0.62; g *= 0.6; b *= 0.62; }
            }
            if (pal == 0 && R01(bid, course, 29) < 0.05) { r *= 0.72; g *= 0.68; b *= 0.7; }       // blue-black engineering bricks
            Set(ref t, r * (1 - mortar) + mc[0] * mortar, g * (1 - mortar) + mc[1] * mortar, b * (1 - mortar) + mc[2] * mortar, 0.08);
        }

        static void Lintel(ref Tx t, Op o, double x, double y, int gx, int gy)
        {
            if (o.lintel == 0) return;
            double ext = o.lintel == 1 || o.lintel == 5 ? 0.1 : 0.04, top = o.y1 + (o.lintel == 4 ? 0.3 : 0.2);
            if (x < o.x0 - ext || x >= o.x1 + ext || y < o.y1 || y >= top + 0.02) return;
            switch (o.lintel)
            {
                case 1:
                case 5:
                    {
                        double v = y > top - 0.03 ? 0.66 : 0.82 + (Fbm(gx, gy, 16, 101) - 0.5) * 0.08;
                        if (o.lintel == 5 && Math.Abs(x - (o.x0 + o.x1) / 2) < 0.1) v = 0.9;
                        Set(ref t, v, v * 0.98, v * 0.92, 0.15);
                        break;
                    }
                case 2:
                    {
                        double v = (x - o.x0) % 0.075 < 0.012 ? 0.62 : 0.84;                          // soldier course
                        Set(ref t, 0.62 * v, 0.33 * v, 0.24 * v, 0.08);
                        break;
                    }
                case 3: { double v = y > top - 0.05 ? 0.96 : y < o.y1 + 0.05 ? 0.66 : 0.88; Set(ref t, v, v * 0.99, v * 0.95, 0.25); break; }
                case 4:
                    {
                        double cx = (o.x0 + o.x1) / 2, w = (o.x1 - o.x0) / 2 + 0.05;
                        double arcY = o.y1 + 0.22 * (1 - Math.Pow((x - cx) / w, 2));
                        if (y > arcY + 0.02 || y < arcY - 0.22) return;
                        double v = ((Math.Atan2(y - o.y1 + 0.6, x - cx) * 30) % 1 + 1) % 1 < 0.15 ? 0.6 : 0.9;
                        Set(ref t, 0.6 * v, 0.34 * v, 0.24 * v, 0.08);
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------ openings
        static void OpPixel(ref Tx t, Op o, int u, double x, double y, int gx, int gy, double pyPerM)
        {
            if (x < o.x0 || x >= o.x1 || y < o.y0 || y >= o.y1)
            {
                // arched windows: the arch rises above y1
                if (o.k == 'A' || o.arch)
                {
                    double cx = (o.x0 + o.x1) / 2, r = (o.x1 - o.x0) / 2;
                    if (y >= o.y1 && x >= o.x0 && x < o.x1 && (x - cx) * (x - cx) + (y - o.y1) * (y - o.y1) < r * r) { }
                    else return;
                }
                else return;
            }
            double lx = x - o.x0, ly = y - o.y0, w = o.x1 - o.x0, h = o.y1 - o.y0;
            switch (o.k)
            {
                case 'W': case 'A': Window(ref t, o, lx, ly, w, h + (o.k == 'A' ? w / 2 : 0), gx, gy); break;
                case 'D': Door(ref t, o, lx, ly, w, h, gx, gy); break;
                case 'F':
                    {
                        bool bar = lx < 0.05 || lx > w - 0.05 || ly < 0.05 || ly > h - 0.05 || Math.Abs(lx - w / 2) < 0.025;
                        if (bar) Set(ref t, o.frame, o.frame, o.frame * 0.97, 0.4);
                        else Glass(ref t, lx, ly, w, h, o.id, 0, o.lit, gx, gy, false);
                        break;
                    }
                case 'S': Shop(ref t, o, u, lx, ly, w, h, gx, gy); break;
                case 'U':
                    {
                        double v = 0.56 + ((ly % 0.08) < 0.025 ? -0.1 : 0.03) + (Fbm(gx, gy, 24, 111) - 0.5) * 0.1;
                        if (ly < 0.08) v = 0.4;
                        Set(ref t, v * o.r, v * o.g, v * o.b, 0.35);
                        break;
                    }
                case 'G':
                    {
                        double v = 1 + ((ly % 0.18) < 0.03 ? -0.18 : 0) + (Fbm(gx, gy, 20, 113) - 0.5) * 0.12;
                        if (lx < 0.04 || lx > w - 0.04 || ly > h - 0.04) v = 0.55;
                        double rust = Clamp01((0.5 - ly) * 2) * Fbm(gx, gy, 10, 115);
                        Set(ref t, o.r * v * (1 - rust * 0.4) + 0.3 * rust * 0.4, o.g * v * (1 - rust * 0.5) + 0.12 * rust * 0.5, o.b * v * (1 - rust * 0.6), 0.3);
                        if (Math.Abs(lx - w / 2) < 0.06 && Math.Abs(ly - 1.0) < 0.03) Set(ref t, 0.75, 0.75, 0.72, 0.6);   // handle
                        break;
                    }
                case 'P':
                    {
                        bool edge = lx < 0.04 || lx > w - 0.04 || ly < 0.04 || ly > h - 0.04;
                        double v = edge ? 0.7 : 1 + (Fbm(gx, gy, 24, 117) - 0.5) * 0.1;
                        Set(ref t, o.r * v, o.g * v, o.b * v, o.style == 1 ? 0.75 : 0.3);
                        if (o.style == 2 && !edge) { double s = Clamp01((ly - h * 0.3) / h) * 0.15; t.r *= 1 - s; t.g *= 1 - s; t.b *= 1 - s; }   // rust streaks
                        break;
                    }
                case 'B':
                    {
                        double v = ly < 0.02 ? 0.7 : ly > h - 0.02 ? 1.05 : 1;
                        v *= 1 + (Fbm(gx, gy, 24, 119) - 0.5) * 0.06;
                        Set(ref t, o.r * v, o.g * v, o.b * v, 0.2);
                        break;
                    }
                case 'I': { double v = (lx < 0.02 || lx > w - 0.02) ? 0.75 : 1; Set(ref t, o.r * v, o.g * v, o.b * v, 0.35); break; }
                case 'R':
                    {
                        double v = 0.06 + ly / h * 0.05;
                        Set(ref t, v, v, v * 1.1, 0.2);
                        if (o.lit > 0 && R01(o.id, 0, 121) < o.lit) { t.er = 0.55; t.eg = 0.5; t.eb = 0.4; }
                        break;
                    }
                case 'L':
                    {
                        double v = (ly % 0.1) < 0.035 ? 0.25 : 0.55;
                        if (lx < 0.04 || lx > w - 0.04 || ly < 0.04 || ly > h - 0.04) v = 0.4;
                        Set(ref t, v * o.r / 0.6, v * o.g / 0.6, v * o.b / 0.6, 0.35);
                        break;
                    }
                case 'C': Curtain(ref t, o, lx, ly, w, h, gx, gy); break;
                case 'T':
                    {
                        double tx = lx % 0.15, ty = ly % 0.075;
                        double v = (tx < 0.008 || ty < 0.008) ? 0.75 : 1 + (R01((int)(lx / 0.15), (int)(ly / 0.075), o.id) - 0.5) * 0.12;
                        Set(ref t, o.r * v, o.g * v, o.b * v, 0.8);
                        break;
                    }
                case 'O':
                    {
                        // loading doors: vertical planks, a centre split, iron straps
                        double v = 1 + ((lx % 0.15) < 0.015 ? -0.25 : 0) + (Fbm(gx, gy, 16, 123) - 0.5) * 0.15;
                        if (Math.Abs(lx - w / 2) < 0.02) v = 0.5;
                        if (Math.Abs(ly - h * 0.2) < 0.03 || Math.Abs(ly - h * 0.8) < 0.03) v = 0.45;
                        Set(ref t, o.r * v, o.g * v, o.b * v, 0.25);
                        break;
                    }
                case 'X':
                    {
                        bool edge = lx < 0.05 || lx > w - 0.05 || ly < 0.05 || ly > h - 0.05;
                        double v = edge ? 0.55 : 1 + (Fbm(gx, gy, 24, 125) - 0.5) * 0.08;
                        Set(ref t, o.r * v, o.g * v, o.b * v, 0.5);
                        break;
                    }
            }
        }

        static void Window(ref Tx t, Op o, double lx, double ly, double w, double h, int gx, int gy)
        {
            // reveal (the wall's thickness, in shadow on the top and left), frame, glazing bars, glass
            double fr = o.frame;
            double rv = 0.07, fw = 0.05;
            if (lx < rv * 0.6 || ly > h - rv)
            {
                double d = 0.32 + 0.1 * ly / h;
                Set(ref t, d * 0.9, d * 0.88, d * 0.86, 0.1);
                return;
            }
            double ix = lx - rv * 0.6, iy = ly, iw = w - rv * 0.6, ih = h - rv;
            bool frame = ix < fw || ix > iw - fw || iy < fw || iy > ih - fw;
            bool bar = false;
            switch (o.style)
            {
                case 0: bar = Math.Abs(iy - ih * 0.5) < 0.03 || Math.Abs(ix - iw * 0.5) < 0.015; break;                       // 2 over 2 sash
                case 1: bar = Math.Abs(iy - ih * 0.5) < 0.03; break;                                                            // 1 over 1
                case 2: bar = Math.Abs(iy - ih * 0.5) < 0.03 || Math.Abs(ix - iw / 3) < 0.012 || Math.Abs(ix - iw * 2 / 3) < 0.012 || Math.Abs(iy - ih * 0.25) < 0.012 || Math.Abs(iy - ih * 0.75) < 0.012; break; // 6 over 6
                case 3: bar = Math.Abs(ix - iw * 0.35) < 0.035 || (ix < iw * 0.35 && Math.Abs(iy - ih * 0.72) < 0.03); break;   // uPVC casement
                case 4: bar = (ix % (iw / 4)) < 0.018 || (iy % (ih / 5)) < 0.018; break;                                           // metal small panes
                case 5: bar = Math.Abs(ix - iw / 3) < 0.03 || Math.Abs(ix - iw * 2 / 3) < 0.03; break;                          // wide triple
                case 6: bar = (ix % (iw / 3)) < 0.02 || (iy % (ih / 4)) < 0.02; break;                                             // warehouse multi-pane
                case 7: bar = false; break;                                                                                     // plain modern
            }
            if (frame || bar) { Set(ref t, fr * (frame ? 1 : 0.96), fr * (frame ? 0.99 : 0.95), fr * 0.96, 0.45); return; }
            Glass(ref t, ix, iy, iw, ih, o.id, o.style, o.lit, gx, gy, o.frosted);
            // balconette railings over the lower part
            if (o.rail && iy < 0.5)
            {
                bool rb = (ix % 0.11) < 0.02 || Math.Abs(iy - 0.46) < 0.03 || (iy < 0.2 && Math.Abs(((ix % 0.33) - 0.165)) < 0.05 && Math.Abs(iy - 0.1) < 0.04);
                if (rb) Set(ref t, 0.07, 0.07, 0.08, 0.5);
            }
        }

        /// Glass with something behind it. kind comes from the window's id, so each window is its own.
        static void Glass(ref Tx t, double x, double y, double w, double h, int id, int style, double litRate, int gx, int gy, bool frosted)
        {
            double fy = y / h, fx = x / w;
            double refl = 0.05 + 0.13 * fy + 0.06 * Math.Max(0, 1 - Math.Abs((fx - fy * 0.7 - 0.1) / 0.18));
            double r = 0.07 + refl * 0.8, g = 0.08 + refl * 0.9, b = 0.1 + refl;
            int kind = (int)(R01(id, 3, 131) * 100);
            bool lit = R01(id, 5, 133) < litRate;
            double er = 0, eg = 0, eb = 0;
            if (frosted)
            {
                r = 0.55; g = 0.57; b = 0.58;
                if (lit) { er = 0.7; eg = 0.72; eb = 0.68; }
            }
            else if (kind < 30)
            {
                if (lit) { double k = 0.55 + 0.45 * fy; er = 1.0 * k; eg = 0.78 * k; eb = 0.5 * k; r += 0.12; g += 0.09; b += 0.05; }
            }
            else if (kind < 55)
            {
                // net curtains
                double n = 0.48 + 0.06 * Math.Sin(x * 40) + (Fbm(gx, gy, 6, 135) - 0.5) * 0.06;
                r = r * 0.35 + n * 0.65; g = g * 0.35 + n * 0.65; b = b * 0.35 + n * 0.62;
                if (lit) { er = 0.9; eg = 0.78; eb = 0.6; }
            }
            else if (kind < 75)
            {
                // blinds, some half down
                double down = 0.35 + R01(id, 7, 137) * 0.65;
                if (fy > 1 - down)
                {
                    double slat = (y % 0.05) < 0.012 ? 0.75 : 1;
                    double[] bc = R01(id, 9, 139) < 0.5 ? new[] { 0.6, 0.56, 0.46 } : new[] { 0.72, 0.72, 0.7 };
                    r = bc[0] * slat; g = bc[1] * slat; b = bc[2] * slat;
                    if (lit) { er = 0.85 * slat; eg = 0.72 * slat; eb = 0.5 * slat; }
                }
                else if (lit) { er = 0.9; eg = 0.7; eb = 0.45; }
            }
            else if (kind < 90)
            {
                // curtains drawn to the sides
                double[][] cc = { new[] { 0.5, 0.14, 0.12 }, new[] { 0.16, 0.3, 0.2 }, new[] { 0.2, 0.24, 0.42 }, new[] { 0.62, 0.5, 0.2 }, new[] { 0.45, 0.42, 0.38 } };
                double[] c = cc[(int)(R01(id, 11, 141) * cc.Length) % cc.Length];
                double side = 0.18 + R01(id, 13, 143) * 0.15;
                if (fx < side || fx > 1 - side)
                {
                    double fold = 0.85 + 0.15 * Math.Sin(x * 70);
                    r = c[0] * fold; g = c[1] * fold; b = c[2] * fold;
                    if (lit) { er = c[0] * 1.2 + 0.25; eg = c[1] * 1.2 + 0.15; eb = c[2] * 1.2 + 0.05; }
                }
                else if (lit) { er = 1.0; eg = 0.8; eb = 0.55; }
            }
            else
            {
                // bright reflection of the sky
                r = 0.3 + 0.15 * fy; g = 0.36 + 0.15 * fy; b = 0.44 + 0.15 * fy;
                if (lit) { er = 0.75; eg = 0.82; eb = 0.95; }
            }
            if (lit) LitTint(id, x, y, w, h, ref er, ref eg, ref eb);
            t.r = r; t.g = g; t.b = b; t.s = 0.88; t.er = er; t.eg = eg; t.eb = eb;
        }

        /// L5: every lit window is its own room: warm bulbs, soft white, cool LED, the blue of a TV, the odd red lamp,
        /// some bright, some barely on.
        static void LitTint(int id, double x, double y, double w, double h, ref double er, ref double eg, ref double eb)
        {
            double lum = 0.3 * er + 0.59 * eg + 0.11 * eb;
            double pick = R01(id, 17, 191), bright = 0.55 + 0.6 * R01(id, 19, 193);
            double[] tint;
            if (pick < 0.34) tint = new[] { 1.0, 0.7, 0.4 };                // tungsten
            else if (pick < 0.62) tint = new[] { 1.0, 0.88, 0.72 };         // soft white
            else if (pick < 0.82) tint = new[] { 0.8, 0.88, 1.0 };          // LED / office white
            else if (pick < 0.9)
            {
                // a telly: blue, brighter low down where the screen is
                tint = new[] { 0.42, 0.58, 1.0 };
                bright *= 0.55 + 0.6 * Math.Max(0, 1 - Math.Abs(y / h - 0.35) * 2.2);
            }
            else if (pick < 0.93) tint = new[] { 1.0, 0.55, 0.42 };         // red shade
            else { tint = new[] { 1.0, 0.75, 0.5 }; bright *= 0.3; }        // just a hall light on
            // a ceiling light: a little brighter at the top
            double k = lum * 1.35 * bright * (0.85 + 0.25 * (y / h));
            er = tint[0] * k; eg = tint[1] * k; eb = tint[2] * k;
        }

        static void Door(ref Tx t, Op o, double lx, double ly, double w, double h, int gx, int gy)
        {
            double v = 1 + (Fbm(gx, gy, 12, 151) - 0.5) * 0.06;
            if (o.style == 2)
            {
                // steel security door
                v = 0.55 + (Fbm(gx, gy, 10, 153) - 0.5) * 0.12 + ((lx % 0.12) < 0.02 ? -0.12 : 0);
                Set(ref t, v * 0.95, v, v * 1.02, 0.45);
                return;
            }
            bool edge = lx < 0.05 || lx > w - 0.05 || ly > h - 0.05;
            if (edge) { Set(ref t, o.r * 0.6, o.g * 0.6, o.b * 0.6, 0.5); return; }
            if (o.style == 0)
            {
                // four-panel door with a letterbox and knocker
                double px = lx < w / 2 ? lx : w - lx, pw = w / 2;
                bool panel = (px > 0.12 && px < pw - 0.05) && ((ly > 0.15 && ly < h * 0.45) || (ly > h * 0.52 && ly < h - 0.15));
                bool bevel = panel && (px < 0.15 || px > pw - 0.08 || Math.Abs(ly - 0.15) < 0.03 || Math.Abs(ly - h * 0.52) < 0.03);
                if (bevel) v *= 0.8; else if (panel) v *= 1.06;
                Set(ref t, o.r * v, o.g * v, o.b * v, 0.55);
                if (Math.Abs(ly - h * 0.48) < 0.03 && Math.Abs(lx - w / 2) < 0.15) Set(ref t, 0.78, 0.66, 0.3, 0.7);   // brass letterbox
                if (Math.Abs(ly - h * 0.65) < 0.04 && Math.Abs(lx - w / 2) < 0.04) Set(ref t, 0.8, 0.68, 0.32, 0.7);
                return;
            }
            // flat estate door with a small wired-glass window
            Set(ref t, o.r * v, o.g * v, o.b * v, 0.4);
            if (lx > w * 0.3 && lx < w * 0.7 && ly > h * 0.62 && ly < h * 0.88) Set(ref t, 0.45, 0.5, 0.52, 0.7);
            if (Math.Abs(ly - h * 0.45) < 0.03 && lx > w * 0.75 && lx < w * 0.85) Set(ref t, 0.7, 0.7, 0.68, 0.6);
        }

        static void Shop(ref Tx t, Op o, int u, double lx, double ly, double w, double h, int gx, int gy)
        {
            // o.style: 0 open (display + interior), 1 vacant (whitewashed), 2 café, 3 dark (closed)
            bool mull = lx < 0.05 || lx > w - 0.05 || ly < 0.05 || ly > h - 0.05 || Math.Abs(lx - w / 2) < 0.03;
            if (mull) { Set(ref t, 0.12, 0.12, 0.13, 0.5); return; }
            int sx = (int)(lx / 0.4), sy = (int)(ly / 0.35);
            double fy = ly / h;
            if (o.style == 1)
            {
                double v = 0.78 + (Fbm(gx, gy, 6, 161) - 0.5) * 0.25;                               // whitewash smears
                Set(ref t, v, v, v * 0.97, 0.4);
                return;
            }
            if (o.style == 3) { Glass(ref t, lx, ly, w, h, o.id + 999, 0, 0, gx, gy, false); return; }
            // shelves of colourful stock, lit
            double r = 0.12, g = 0.12, b = 0.13;
            bool shelf = (ly % 0.35) < 0.03;
            if (!shelf && ly > 0.15)
            {
                double k = R01(sx, sy, o.id + u * 7);
                double[][] stock = { new[] { 0.7, 0.2, 0.15 }, new[] { 0.85, 0.75, 0.3 }, new[] { 0.2, 0.45, 0.7 }, new[] { 0.9, 0.9, 0.85 }, new[] { 0.3, 0.6, 0.3 }, new[] { 0.8, 0.45, 0.2 } };
                double[] c = stock[(int)(k * stock.Length) % stock.Length];
                double fill = R01(sx * 3 + (int)(lx * 20) % 3, sy, o.id + 5) > 0.25 ? 1 : 0.3;
                r = c[0] * 0.55 * fill + 0.1; g = c[1] * 0.55 * fill + 0.1; b = c[2] * 0.55 * fill + 0.1;
            }
            if (o.style == 2) { r = 0.32 + 0.1 * fy; g = 0.22 + 0.06 * fy; b = 0.15; if (ly < 0.8 && ((int)(lx / 0.7)) % 2 == 0) { r = 0.18; g = 0.12; b = 0.08; } }
            double refl = 0.1 * fy;
            Set(ref t, r + refl, g + refl, b + refl * 1.2, 0.85);
            double lit = o.lit;
            if (R01(o.id, u, 163) < lit)
            {
                double k = o.style == 2 ? 0.75 : 0.95;
                t.er = (r * 0.8 + 0.5) * k; t.eg = (g * 0.8 + 0.45) * k; t.eb = (b * 0.8 + 0.35) * k;
            }
        }

        static void Curtain(ref Tx t, Op o, double lx, double ly, double w, double h, int gx, int gy)
        {
            // o.style: 0 blue glass, 1 green ribbon, 2 silver, 3 dark with fins, 4 lobby
            double[] tint = o.style == 1 ? new[] { 0.16, 0.26, 0.24 } : o.style == 2 ? new[] { 0.42, 0.45, 0.48 } : o.style == 3 ? new[] { 0.1, 0.11, 0.12 } : new[] { 0.14, 0.22, 0.32 };
            double spandrel = o.style == 1 ? 1.1 : o.style == 4 ? 0 : 0.9;
            double mull = o.style == 3 ? 0.18 : 0.05;
            if (lx < mull || lx > w - 0.03)
            {
                double mv = o.style == 3 ? 0.18 : 0.42;
                Set(ref t, mv, mv * 1.02, mv * 1.05, 0.6);
                if (o.style == 3) { t.r = 0.2; t.g = 0.16; t.b = 0.12; }
                return;
            }
            if (ly > h - 0.12 || (ly < spandrel && ly > spandrel - 0.06)) { Set(ref t, 0.3, 0.31, 0.33, 0.5); return; }
            // reflections drift across the whole strip (clouds, other towers)
            double cloud = Fbm(gx, gy * 0.6, 160, 171);
            double fy = ly / h;
            if (ly < spandrel)
            {
                double v = 0.8 + 0.2 * cloud;
                Set(ref t, tint[0] * 1.3 * v, tint[1] * 1.3 * v, tint[2] * 1.3 * v, 0.8);
                return;
            }
            double rk = 0.6 + 0.8 * cloud + 0.2 * fy;
            double r = tint[0] * rk, g = tint[1] * rk, b = tint[2] * rk;
            // a faint view in: ceiling, desks
            bool ceiling = ly > h - 0.5;
            if (ceiling) { r *= 0.85; g *= 0.85; b *= 0.85; }
            Set(ref t, r, g, b, 0.92);
            // offices are lit in clusters (a team working late), not panel by panel
            double zone = Value(gx / 128.0, o.style * 3.1 + 0.5, 8, 173);
            bool lit = o.style == 4 || zone > 0.58 || R01(gx / 51, o.id, 175) < 0.06;
            if (lit)
            {
                double k = ceiling ? ((lx % 0.8) < 0.5 ? 0.7 : 0.4) : 0.28 + 0.06 * Math.Sin(lx * 9);
                t.er = 0.78 * k; t.eg = 0.86 * k; t.eb = 0.95 * k;
                if (o.style == 4) { t.er = 0.8; t.eg = 0.72; t.eb = 0.58; }
            }
        }

        // ------------------------------------------------------------------ overlays (tags, signs) drawn with the dressing canvas
        static void Overlay(DressAtlas cv, Facade f, int atlas)
        {
            if (f.cache == null) return;
            double uw = UnitW(atlas, f.row), fh = FloorH(atlas, f.row), pyPerM = Content / fh;
            Random r = new Random(5000 + atlas * 100 + f.row);
            for (int u = 0; u < f.cache.Length; u++)
                foreach (Op o in f.cache[u])
                {
                    int x0 = (int)((u * uw + o.x0) * PxPerM), x1 = (int)((u * uw + o.x1) * PxPerM);
                    int y0 = f.row * Pitch + Pad + (int)(o.y0 * pyPerM), y1 = f.row * Pitch + Pad + (int)(o.y1 * pyPerM);
                    if (o.tag) Tag(cv, r, x0, y0, x1, Math.Min(y1, y0 + (int)(2.0 * pyPerM)));
                    if (!string.IsNullOrEmpty(o.piece)) Piece(cv, r, x0, y0, x1, y1, o.piece);
                    if (!string.IsNullOrEmpty(o.text))
                    {
                        // a board in the window or on the fascia
                        int bw = o.fullText ? x1 - x0 - 6 : Math.Min(x1 - x0 - 6, 60), bh = o.fullText ? y1 - y0 - 6 : Math.Min(y1 - y0 - 6, 22);
                        int bx = x0 + (x1 - x0 - bw) / 2, by = o.k == 'X' ? y0 + 3 : y0 + (int)(1.1 * pyPerM);
                        if (o.k != 'X') { cv.Fill(bx, by, bw, bh, new Col(235, 232, 220)); cv.Frame(bx, by, bw, bh, 2, new Col(180, 30, 30)); }
                        cv.Text(o.text, bx + 3, by + 2, bw - 6, bh - 4, o.k == 'X' && o.r < 0.5 ? new Col(225, 215, 190) : new Col(170, 25, 25), 0.5, DressAtlas.Align.Centre);
                    }
                }
        }

        static readonly string[] TagWords = { "OSK", "ZEPH", "KRE", "RYE", "MEK", "SNO", "DUSK", "VEX", "LOOP", "TEK", "NOIZ", "PEK" };
        static readonly Col[] TagCols = { new Col(20, 20, 22), new Col(235, 235, 235), new Col(200, 30, 40), new Col(40, 90, 210), new Col(190, 190, 195), new Col(250, 200, 40), new Col(140, 60, 200) };

        static void Tag(DressAtlas cv, Random r, int x0, int y0, int x1, int y1)
        {
            int w = x1 - x0, h = y1 - y0;
            if (w < 20 || h < 12) return;
            int n = 1 + r.Next(3);
            for (int k = 0; k < n; k++)
            {
                string word = TagWords[r.Next(TagWords.Length)];
                Col c = TagCols[r.Next(TagCols.Length)];
                double cs = 1.6 + r.NextDouble() * 1.8;
                int cols = PixelFont.Columns(word);
                double tw = cols * cs;
                if (tw > w - 4) cs = (w - 4) / (double)cols;
                double x = x0 + 2 + r.NextDouble() * Math.Max(1, w - cols * cs - 4);
                double top = y0 + 7 * cs + 2 + r.NextDouble() * Math.Max(1, h - 7 * cs - 4);
                double shear = (r.NextDouble() - 0.2) * 0.5;
                bool bubble = r.NextDouble() < 0.3;
                foreach (char ch in word)
                {
                    if (bubble) { cv.Glyph(ch, x, top, cs, cs * 0.9 + 1.2, new Col(15, 15, 15), shear, 1); cv.Glyph(ch, x, top, cs, cs * 0.9, c, shear, 1); }
                    else cv.Glyph(ch, x, top, cs, 0.7, c, shear, 1.1);
                    x += PixelFont.Advance(ch) * cs;
                }
                if (!bubble) cv.Seg(x0 + 4, top - 7 * cs - 3, x, top - 7 * cs - 2 + r.NextDouble() * 3, 0.6, c);   // underline swoosh
            }
        }

        // ------------------------------------------------------------------ railway (the line over Rye Lane)
        /// The viaduct, one 8 m bay per unit: piers, a 6.4 m arch springing at 2.6 m, and what's been built into it.
        /// Unit 0 a shuttered lock-up, 1 a car repair bay, 2 a café, 3 the station entrance. Two strips (0–3.85 m, 3.85–7.7 m)
        /// so the bricks keep their size; the ops are the same, shifted down for the upper strip.
        static List<Op> RailArchOps(int u, double yOff)
        {
            double[] grey = C("8e8c88"), red = C("5c2224"), black = C("1c1d1f"), cream = C("d8cfb8"), galv = C("a6abae"), navy = C("1f2a4a"), green = C("2c4a36");
            List<Op> o = new List<Op>();
            Func<double, double> Y = v => v - yOff;
            bool lo = yOff == 0;
            Op back = Box('P', 0.8, 7.2, Y(0), Y(2.6), u == 0 ? grey : u == 1 ? red : u == 2 ? black : cream); back.arch = true; o.Add(back);
            Op fan = new Op { k = 'A', x0 = 1.1, x1 = 6.9, y0 = Y(2.6), y1 = Y(2.6), style = u == 2 ? 4 : 6, id = Id(B_RailArchLo, u, 1), sill = false, frame = u == 3 ? 0.85 : 0.25,
                              lit = u == 0 ? 0.45 : u == 1 ? 0.3 : 1.0, arch = true };
            o.Add(fan);
            switch (u)
            {
                case 0:
                    o.Add(new Op { k = 'U', x0 = 1.3, x1 = 6.7, y0 = Y(0), y1 = Y(3.0), r = galv[0], g = galv[1], b = galv[2], tag = lo });
                    break;
                case 1:
                    o.Add(new Op { k = 'R', x0 = 1.4, x1 = 6.6, y0 = Y(0), y1 = Y(3.3), id = Id(B_RailArchLo, u, 2), lit = 0.8 });
                    o.Add(new Op { k = 'X', x0 = 1.4, x1 = 6.6, y0 = Y(3.35), y1 = Y(3.85), r = navy[0], g = navy[1], b = navy[2], text = lo ? "MOT TYRES" : null, fullText = true });
                    break;
                case 2:
                    o.Add(new Op { k = 'W', x0 = 1.3, x1 = 5.0, y0 = Y(0.6), y1 = Y(2.55), style = 7, id = Id(B_RailArchLo, u, 3), sill = false, frame = 0.15, lit = 1.0 });
                    o.Add(new Op { k = 'D', x0 = 5.4, x1 = 6.5, y0 = Y(0), y1 = Y(2.4), style = 1, r = 0.12, g = 0.12, b = 0.13 });
                    o.Add(new Op { k = 'X', x0 = 1.3, x1 = 6.7, y0 = Y(2.62), y1 = Y(3.2), r = green[0], g = green[1], b = green[2], text = lo ? "ARCH COFFEE" : null, fullText = true });
                    break;
                default:
                    o.Add(new Op { k = 'W', x0 = 1.0, x1 = 7.0, y0 = Y(0), y1 = Y(3.0), style = 5, id = Id(B_RailArchLo, u, 2), sill = false, frame = 0.85, lit = 1.0 });
                    o.Add(new Op { k = 'X', x0 = 1.0, x1 = 7.0, y0 = Y(3.05), y1 = Y(3.75), r = navy[0], g = navy[1], b = navy[2], text = lo ? "RYE LANE STATION" : null, fullText = true });
                    break;
            }
            return o;
        }

        static void RailDefs(List<Facade> L)
        {
            L.Add(new Facade { row = B_RailArchLo, wall = 'Y', brick = 1, dirt = true, layout = u => RailArchOps(u, 0) });
            L.Add(new Facade { row = B_RailArchHi, wall = 'Y', brick = 1, yOff = 3.85, layout = u => RailArchOps(u, 3.85) });

            // bridge girders: painted steel, rivets, stiffeners, rust, and the pieces everyone sees from the bus
            double[] gr = C("2c4a36");
            Facade g = new Facade { row = B_Girder, wall = 'H', r = gr[0], g = gr[1], b = gr[2], soot = false };
            g.layout = u => new List<Op> {
                new Op { k = '-', x0 = 0.6, x1 = 7.4, y0 = 0.3, y1 = 2.3, piece = "PEK" },
                new Op { k = '-', x0 = 8.4, x1 = 15.2, y0 = 0.3, y1 = 2.3, piece = "RIZE" },
                new Op { k = 'X', x0 = 15.5, x1 = 17.1, y0 = 0.7, y1 = 1.5, r = 0.95, g = 0.95, b = 0.93, text = "5.7M", fullText = true },
                new Op { k = '-', x0 = 17.4, x1 = 24.6, y0 = 0.3, y1 = 2.3, piece = "SE15" },
                new Op { k = '-', x0 = 25.0, x1 = 31.6, y0 = 0.3, y1 = 2.3, piece = "DUSK" } };
            L.Add(g);

            // train sides: unit 0/1 a carriage half, unit 2 the cab end
            Facade tr = new Facade { row = B_Train, wall = 'J', soot = false };
            double[] teal = C("2f6d6d"), yel = C("d8a91f");
            tr.layout = u =>
            {
                List<Op> o = new List<Op>();
                if (u == 2)
                {
                    o.Add(Box('P', 0.5, 1.4, 0.45, 2.55, yel));
                    o.Add(new Op { k = 'W', x0 = 0.65, x1 = 1.25, y0 = 1.55, y1 = 2.25, style = 7, id = Id(B_Train, u, 0), sill = false, frame = 0.2, lit = 0 });
                    o.Add(new Op { k = 'W', x0 = 2.2, x1 = 4.2, y0 = 1.25, y1 = 2.25, style = 7, id = Id(B_Train, u, 1), sill = false, frame = 0.2, lit = 0.9 });
                }
                else o.Add(new Op { k = 'W', x0 = 0.4, x1 = 4.2, y0 = 1.25, y1 = 2.25, style = 7, id = Id(B_Train, u, 1), sill = false, frame = 0.2, lit = 0.9 });
                o.Add(Box('P', 4.55, 5.33, 0.45, 2.55, teal)); o.Add(Box('P', 5.35, 6.13, 0.45, 2.55, teal));
                o.Add(new Op { k = 'W', x0 = 4.75, x1 = 5.15, y0 = 1.4, y1 = 2.3, style = 7, id = Id(B_Train, u, 2), sill = false, frame = 0.2, lit = 0.9 });
                o.Add(new Op { k = 'W', x0 = 5.53, x1 = 5.93, y0 = 1.4, y1 = 2.3, style = 7, id = Id(B_Train, u, 3), sill = false, frame = 0.2, lit = 0.9 });
                o.Add(new Op { k = 'W', x0 = 6.5, x1 = 10.3, y0 = 1.25, y1 = 2.25, style = 7, id = Id(B_Train, u, 4), sill = false, frame = 0.2, lit = 0.9 });
                return o;
            };
            L.Add(tr);

            // station: name boards, way out, canopy valance, the company fascia, platform number
            double[] navy = C("1f2a4a"), green = C("2c4a36"), cream = C("e2d8c0");
            Facade st = new Facade { row = B_Station, wall = '0', r = navy[0], g = navy[1], b = navy[2], soot = false };
            st.layout = u =>
            {
                List<Op> o = new List<Op>();
                switch (u)
                {
                    case 0: o.Add(new Op { k = 'X', x0 = 0.1, x1 = 6.3, y0 = 0.12, y1 = 1.08, r = navy[0], g = navy[1], b = navy[2], text = "RYE LANE", fullText = true }); break;
                    case 1: o.Add(new Op { k = 'X', x0 = 0.1, x1 = 6.3, y0 = 0.12, y1 = 1.08, r = navy[0], g = navy[1], b = navy[2], text = "WAY OUT", fullText = true }); break;
                    case 2:
                        o.Add(Box('P', 0, 6.4, 0, 1.2, green));
                        for (int k = 0; k < 24; k++) { Op b = Box('B', k * 0.2667 + 0.03, k * 0.2667 + 0.2, k % 2 == 0 ? 0.1 : 0.22, 1.2, cream); o.Add(b); }
                        break;
                    case 3: o.Add(new Op { k = 'X', x0 = 0.1, x1 = 6.3, y0 = 0.12, y1 = 1.08, r = green[0], g = green[1], b = green[2], text = "LUNDUN RAIL", fullText = true }); break;
                    default: o.Add(new Op { k = 'X', x0 = 0.1, x1 = 6.3, y0 = 0.12, y1 = 1.08, r = navy[0], g = navy[1], b = navy[2], text = "PLATFORM 1", fullText = true }); break;
                }
                return o;
            };
            L.Add(st);
        }

        /// Yellow stock brick going black at the bottom, a ring of engineering-brick voussoirs, stone imposts,
        /// a blue-brick string course where the deck is, and stone coping on the parapet. y is metres up the wall.
        static void RailArchWall(ref Tx t, Facade f, int u, double x, double y, int gx, int gy, double pyPerM)
        {
            Brick(ref t, 1, x + u * 1000.37, y, gx, gy, pyPerM, f.row);
            double dx = x - 4, dy = y - 2.6, d = Math.Sqrt(dx * dx + dy * dy);
            if (dy >= 0 && d >= 3.2 && d < 3.64)
            {
                double ang = Math.Atan2(dy, dx), along = ang * 3.42 / 0.115, fr = along - Math.Floor(along);
                bool joint = fr < 0.14 || Math.Abs(d - 3.42) < 0.018;
                double v = joint ? 0.5 : 0.36 + (R01((int)Math.Floor(along), d > 3.42 ? 1 : 0, 401) - 0.5) * 0.08;
                Set(ref t, v * 1.02, v * 0.66, v * 0.54, 0.2);
            }
            if ((x < 0.8 || x > 7.2) && y >= 2.42 && y < 2.6) { double v = 0.6 + (Fbm(gx, gy, 10, 403) - 0.5) * 0.06; Set(ref t, v, v * 0.98, v * 0.92, 0.2); }
            if (y >= 6.15 && y < 6.42) { double v = y > 6.38 ? 0.32 : 0.21 + (R01((int)(x / 0.225), (int)(y / 0.075), 405) - 0.5) * 0.04; Set(ref t, v, v * 1.02, v * 1.12, 0.35); }
            if (y >= 7.5) { double v = (x % 0.9) < 0.02 ? 0.45 : 0.6 + (Fbm(gx, gy, 12, 407) - 0.5) * 0.06; Set(ref t, v, v * 0.99, v * 0.95, 0.2); }
            // soot: darker low down and in streaks from the string course
            double k = 0.72 + 0.28 * Smooth(0, 1.6, y);
            if (y < 6.15 && y > 3.0) k *= 1 - 0.18 * Clamp01((6.15 - y) / 3.0 * 0.8) * Value(gx / 4.0, 0, 1 << 20, 409);
            k *= 1 + (Fbm(gx, gy, 64, 411) - 0.5) * 0.18;
            t.r *= k; t.g *= k; t.b *= k;
        }

        /// A painted plate girder: flanges, stiffeners every 1.6 m, rivets, rust and pigeon mess.
        static void GirderWall(ref Tx t, Facade f, double x, double y, int gx, int gy)
        {
            double v = 1 + (Fbm(gx, gy, 32, 421) - 0.5) * 0.12;
            if (y < 0.22 || y > 2.38) v *= 1.1;
            if (Math.Abs(y - 0.22) < 0.025 || Math.Abs(y - 2.38) < 0.025) v *= 0.6;
            double sx = x % 1.6;
            if (sx < 0.14) v *= 1.12; else if (sx < 0.2) v *= 0.7;
            Set(ref t, f.r * v, f.g * v, f.b * v, 0.35);
            // rivets
            foreach (double ry in new[] { 0.32, 2.28 })
            {
                double rx = (x % 0.12) - 0.06, ddy = y - ry;
                if (rx * rx + ddy * ddy < 0.022 * 0.022) { t.r *= 1.35; t.g *= 1.35; t.b *= 1.35; }
            }
            if (sx < 0.14 && (y % 0.15) < 0.04 && Math.Abs(sx - 0.07) < 0.025) { t.r *= 1.3; t.g *= 1.3; t.b *= 1.3; }
            // rust: blooms and streaks running down from the top flange
            double rust = Smooth(0.6, 0.78, Fbm(gx, gy, 24, 423)) * 0.7 + Clamp01((y - 1.2) / 1.2) * Smooth(0.55, 0.85, Value(gx / 3.0, 0, 1 << 20, 425)) * 0.5;
            rust = Clamp01(rust);
            t.r = t.r * (1 - rust) + 0.42 * rust; t.g = t.g * (1 - rust) + 0.22 * rust; t.b = t.b * (1 - rust) + 0.12 * rust;
            // pigeons
            if (y < 0.24 && Value(gx / 3.0, gy / 3.0, 1 << 20, 427) > 0.9 && Value(gx / 40.0, 0, 1 << 20, 429) > 0.55) { t.r = 0.8; t.g = 0.8; t.b = 0.76; }
        }

        /// A train's side: dark skirt, navy band, white pinstripe, off-white body, grey cant rail.
        static void TrainWall(ref Tx t, double x, double y, int gx, int gy)
        {
            double v = 1 + (Fbm(gx, gy, 32, 431) - 0.5) * 0.05;
            if (y < 0.4) Set(ref t, 0.2 * v, 0.21 * v, 0.23 * v, 0.3);
            else if (y < 0.9) Set(ref t, 0.12 * v, 0.17 * v, 0.29 * v, 0.6);
            else if (y < 0.97) Set(ref t, 0.92, 0.92, 0.9, 0.6);
            else if (y < 2.6) Set(ref t, 0.87 * v, 0.87 * v, 0.84 * v, 0.6);
            else Set(ref t, 0.55 * v, 0.56 * v, 0.58 * v, 0.5);
            if (y < 0.9 && y > 0.4 && Value(gx / 6.0, gy / 6.0, 1 << 20, 433) > 0.75) { t.r *= 0.85; t.g *= 0.85; t.b *= 0.85; }   // brake dust
        }

        /// A big graffiti piece: shadow, black outline, two-tone fill, highlights, a few drips.
        static void Piece(DressAtlas cv, Random r, int x0, int y0, int x1, int y1, string word)
        {
            Col[][] pal = { new[] { new Col(255, 61, 127), new Col(255, 210, 63), new Col(16, 16, 16), new Col(255, 255, 255) },
                            new[] { new Col(62, 224, 255), new Col(42, 91, 255), new Col(11, 11, 32), new Col(232, 247, 255) },
                            new[] { new Col(156, 255, 58), new Col(31, 168, 74), new Col(16, 16, 16), new Col(255, 255, 255) },
                            new[] { new Col(255, 255, 255), new Col(201, 201, 201), new Col(17, 17, 17), new Col(255, 42, 42) },
                            new[] { new Col(255, 122, 26), new Col(209, 18, 31), new Col(26, 6, 6), new Col(255, 230, 160) } };
            Col[] p = pal[r.Next(pal.Length)];
            int w = x1 - x0, h = y1 - y0, cols = PixelFont.Columns(word);
            double cs = Math.Min(h / 9.0, w / (cols + 2.0));
            double startX = x0 + (w - cols * cs) / 2, top = y0 + h / 2.0 + 3.5 * cs;
            double shear = (r.NextDouble() - 0.3) * 0.3;
            double[] dy = new double[word.Length];
            for (int k = 0; k < word.Length; k++) dy[k] = (r.NextDouble() - 0.5) * cs * 0.8;
            for (int pass = 0; pass < 4; pass++)
            {
                double cx = startX;
                for (int k = 0; k < word.Length; k++)
                {
                    char ch = word[k];
                    double tt = top + dy[k];
                    if (pass == 0) cv.Glyph(ch, cx + cs * 0.3, tt - cs * 0.3, cs, cs * 0.95 + 3, p[2], shear, 1);
                    else if (pass == 1) cv.Glyph(ch, cx, tt, cs, cs * 0.95 + 2.2, p[2], shear, 1);
                    else if (pass == 2) { cv.Glyph(ch, cx, tt, cs, cs * 0.95, p[1], shear, 1); cv.Glyph(ch, cx, tt + cs * 0.25, cs, cs * 0.6, p[0], shear, 0.75); }
                    else cv.Glyph(ch, cx - cs * 0.15, tt - cs * 0.2, cs, cs * 0.2, p[3], shear, 1);
                    cx += PixelFont.Advance(ch) * cs;
                }
            }
            for (int d = 0; d < 4; d++)
            {
                double dx = startX + r.NextDouble() * cols * cs, len = 3 + r.NextDouble() * 10, yy = top - 7 * cs;
                cv.Seg(dx, yy, dx, yy - len, 1.0, p[1]);
            }
        }

        // ------------------------------------------------------------------ E7: the Lundun Wheel's signs
        /// One 6.4 m × 1.2 m board per unit: the name, tickets, boarding, exit, the queue.
        static void WheelSigns(List<Facade> L)
        {
            double[] teal = C("1f5f6b"), white = C("f2f0ea"), navy = C("1f2a4a");
            Facade f = new Facade { row = A_WheelSigns, wall = '0', r = teal[0], g = teal[1], b = teal[2], soot = false };
            string[] words = { "THE LUNDUN WHEEL", "TICKETS", "BOARDING", "EXIT", "QUEUE HERE" };
            f.layout = u =>
            {
                double[] bg = u == 0 ? teal : u == 3 ? C("2c4a36") : navy;
                return new List<Op> {
                    new Op { k = 'X', x0 = 0.08, x1 = 6.32, y0 = 0.1, y1 = 1.1, r = bg[0], g = bg[1], b = bg[2], text = words[u], fullText = true },
                    Box('B', 0.08, 6.32, 0.1, 0.16, white) };
            };
            L.Add(f);
        }

        /// E1: the names on Brixtun's department stores, music hall and library (one 6.4 m board each).
        static void BrixtunSigns(List<Facade> L)
        {
            string[] words = { "ASHDOWN'S", "KEMBLE & HART", "THE REGAL HALL", "BRIXTUN LIBRARY", "BRIXTUN" };
            string[] bgs = { "16402f", "5c1a1f", "101820", "1f2a4a", "1f2a4a" };
            double[] gold = C("d8b45a"), white = C("f2f0ea");
            Facade f = new Facade { row = A_BrixtunSigns, wall = '0', r = 0.1, g = 0.1, b = 0.12, soot = false };
            f.layout = u =>
            {
                double[] bg = C(bgs[u % bgs.Length]);
                if (u == 4)       // E2: the station's name board, in the railway's navy like Rye Lane's
                    return new List<Op> { new Op { k = 'X', x0 = 0.1, x1 = 6.3, y0 = 0.12, y1 = 1.08, r = bg[0], g = bg[1], b = bg[2], text = words[u], fullText = true } };
                return new List<Op> {
                    new Op { k = 'X', x0 = 0.08, x1 = 6.32, y0 = 0.1, y1 = 1.1, r = bg[0], g = bg[1], b = bg[2], text = words[u % words.Length], fullText = true },
                    Box('B', 0.08, 6.32, 0.1, 0.16, u == 2 ? white : gold), Box('B', 0.08, 6.32, 1.04, 1.1, u == 2 ? white : gold) };
            };
            L.Add(f);
        }

        /// E2: Brixtun's bridges: the same painted plate girders as Peckam's, with BRIXTUN across the middle and the
        /// local writers' pieces either side (centred on each span by the railway).
        static void BrixtunGirder(List<Facade> L)
        {
            double[] gr = C("2c4a36");
            Facade g = new Facade { row = A_BrixGirder, wall = 'H', r = gr[0], g = gr[1], b = gr[2], soot = false };
            g.layout = u => new List<Op> {
                new Op { k = '-', x0 = 4.2, x1 = 10.0, y0 = 0.3, y1 = 2.3, piece = "SOUL" },
                new Op { k = 'X', x0 = 10.6, x1 = 21.4, y0 = 0.55, y1 = 2.05, r = gr[0], g = gr[1], b = gr[2], text = "BRIXTUN", fullText = true },
                new Op { k = '-', x0 = 22.0, x1 = 27.8, y0 = 0.3, y1 = 2.3, piece = "SW2" } };
            L.Add(g);
        }

        // ------------------------------------------------------------------ helpers to build layouts
        static Op Win(double x0, double x1, double y0, double y1, int style, int id, int lintel, bool sill = true, double frame = 0.92, double lit = 0.35)
        { return new Op { k = 'W', x0 = x0, x1 = x1, y0 = y0, y1 = y1, style = style, id = id, lintel = lintel, sill = sill, frame = frame, lit = lit }; }
        static Op Box(char k, double x0, double x1, double y0, double y1, double[] c, int style = 0, int id = 0)
        { return new Op { k = k, x0 = x0, x1 = x1, y0 = y0, y1 = y1, r = c[0], g = c[1], b = c[2], style = style, id = id }; }
        static int Id(int row, int u, int k) { return row * 1000 + u * 20 + k; }

        static Facade Plain(int row, char wall, int brick, double[] c)
        { return new Facade { row = row, wall = wall, brick = brick, r = c[0], g = c[1], b = c[2] }; }

        // ------------------------------------------------------------------ atlas A: traditional
        static List<Facade> DefsA()
        {
            List<Facade> L = new List<Facade>();
            double[] white = C("dedbd2"), stucco = C("e6e1d4"), cream = C("d8cfb8"), stone = C("cfc8b6"), black = C("16171a");
            L.Add(Plain(A_BrickRed, 'K', 0, white));
            L.Add(Plain(A_BrickYel, 'K', 1, white));
            L.Add(Plain(A_Render, 'E', 0, cream));
            L.Add(Plain(A_Stucco, 'U', 0, stucco));
            L.Add(new Facade { row = A_Slate, wall = 'S', soot = false });
            L.Add(new Facade { row = A_RoofFlat, wall = 'F', soot = false });
            L.Add(Plain(A_Cornice, 'Z', 0, stucco));
            L.Add(Plain(A_MansionPlain, 'K', 0, white));
            L.Add(Plain(A_GeorgPlain, 'K', 2, white));
            WheelSigns(L);
            BrixtunSigns(L);
            BrixtunGirder(L);

            // Victorian terrace, upper floor: a window over the door, a wide window over the bay
            for (int pal = 0; pal < 3; pal++)
            {
                int row = pal == 0 ? A_TUpRed : pal == 1 ? A_TUpYel : A_TUpPaint;
                int p = pal;
                Facade f = new Facade { row = row, wall = pal == 2 ? 'E' : 'K', brick = pal == 0 ? 0 : 1 };
                if (pal == 2) f.unitColour = u => Paint6[u % 6];
                f.layout = u =>
                {
                    int lin = p == 2 ? 3 : (u % 3 == 0 ? 2 : 1);
                    List<Op> o = new List<Op>
                    {
                        Win(0.5, 1.42, 0.78, 2.55, u % 2, Id(row, u, 0), lin),
                        Win(2.45, 4.65, 0.78, 2.55, 5, Id(row, u, 1), lin),
                    };
                    if (u % 3 == 1) o.Add(Box('I', 0.0, 0.07, 0, 3.1, C("2a2b2d")));                // downpipe at the party wall
                    return o;
                };
                L.Add(f);
            }
            // Victorian terrace, ground floor: front door with fanlight, the window behind the bay, a plinth
            for (int pal = 0; pal < 3; pal++)
            {
                int row = pal == 0 ? A_TGfRed : pal == 1 ? A_TGfYel : A_TGfPaint;
                int p = pal;
                Facade f = new Facade { row = row, wall = pal == 2 ? 'E' : 'K', brick = pal == 0 ? 0 : 1, dirt = true };
                if (pal == 2) f.unitColour = u => Paint6[(u + 2) % 6];
                f.layout = u =>
                {
                    double[] door = Door6[(u + p * 2) % 6];
                    List<Op> o = new List<Op>
                    {
                        Box('B', 0, 5.334, 0, 0.42, p == 1 ? C("3a3b3d") : C("4a4744")),                 // painted plinth
                        Box('P', 0.33, 1.52, 0.42, 3.05, p == 2 ? Paint6[(u + 2) % 6] : C("d6d2c6")),      // door surround
                        Box('D', 0.45, 1.4, 0.42, 2.55, door, 0),
                        new Op { k = 'F', x0 = 0.45, x1 = 1.4, y0 = 2.6, y1 = 2.95, frame = 0.9, id = Id(row, u, 3), lit = 0.5 },
                        Win(2.3, 4.8, 0.8, 2.7, 5, Id(row, u, 1), 1),
                    };
                    return o;
                };
                L.Add(f);
            }
            // bay window faces (white-painted stone, one sash per metre)
            {
                Facade f = new Facade { row = A_Bay, wall = 'U', r = 0.86, g = 0.85, b = 0.81 };
                f.layout = u => new List<Op>
                {
                    Box('P', 0.1, 0.9, 0.12, 0.68, C("cfccc3")),
                    Win(0.12, 0.88, 0.8, 2.72, u % 2, Id(A_Bay, u, 0), 0, false, 0.93),
                    Box('B', 0, 1.0, 2.82, 3.2, C("e2e0d8")),
                    Box('B', 0, 1.0, 2.76, 2.82, C("8a8880")),
                };
                L.Add(f);
            }
            // shop parade, upper floors (flats over the shops)
            for (int pal = 0; pal < 3; pal++)
            {
                int row = pal == 0 ? A_PUpYel : pal == 1 ? A_PUpRed : A_PUpPaint;
                int p = pal;
                Facade f = new Facade { row = row, wall = pal == 2 ? 'E' : 'K', brick = pal == 0 ? 1 : 0 };
                if (pal == 2) f.unitColour = u => Paint6[(u * 2 + 1) % 6];
                f.layout = u =>
                {
                    List<Op> o = new List<Op>();
                    int lin = p == 1 ? 5 : p == 2 ? 3 : 4;
                    for (int k = 0; k < 3; k++)
                    {
                        double cx = 1.1 + k * 2.1;
                        Op w = Win(cx - 0.52, cx + 0.52, 0.85, 2.6, p == 2 ? 3 : (u + k) % 2, Id(row, u, k), lin, true, p == 2 ? 0.95 : 0.9);
                        o.Add(w);
                    }
                    if (p == 1) o.Add(Box('B', 0, 6.4, 0, 0.16, C("c9c2b0")));                         // stone string course
                    if (u == 3 && p != 2) o.Add(Box('X', 1.6, 4.8, 2.75, 3.1, C("e8e4da"), 0, 0));       // a painted board on the brick
                    if (u == 3 && p != 2) o[o.Count - 1].text = "TO LET";
                    return o;
                };
                L.Add(f);
            }
            // shop fronts (Lundun's own shops are added on top by the dressing; these show where there aren't any)
            L.Add(ShopRow(A_ShopGf, false));
            // white stucco: tall windows, architraves with hoods, black balconettes, a string course
            {
                Facade f = new Facade { row = A_StuccoUp, wall = 'U', r = 0.9, g = 0.88, b = 0.83 };
                f.layout = u =>
                {
                    List<Op> o = new List<Op> { Box('B', 0, 6.4, 0, 0.14, C("d8d4c8")) };
                    for (int k = 0; k < 3; k++)
                    {
                        double cx = 1.07 + k * 2.13;
                        Op w = Win(cx - 0.55, cx + 0.55, 0.55, 2.9, 2, Id(A_StuccoUp, u, k), 3, true, 0.94, 0.4);
                        w.rail = u % 2 == 0 || k == 1;
                        o.Add(w);
                    }
                    return o;
                };
                L.Add(f);
            }
            {
                Facade f = new Facade { row = A_StuccoGf, wall = 'Q', r = 0.88, g = 0.86, b = 0.81, dirt = true };
                f.layout = u => new List<Op>
                {
                    Box('B', 0, 6.4, 0, 0.3, C("5a5852")),
                    Box('D', 0.62, 1.7, 0.3, 2.85, black, 0),
                    new Op { k = 'F', x0 = 0.62, x1 = 1.7, y0 = 2.9, y1 = 3.4, frame = 0.9, id = Id(A_StuccoGf, u, 5), lit = 0.6 },
                    Win(2.7, 3.85, 0.9, 3.1, 2, Id(A_StuccoGf, u, 0), 0, true, 0.94),
                    Win(4.85, 6.0, 0.9, 3.1, 2, Id(A_StuccoGf, u, 1), 0, true, 0.94),
                };
                L.Add(f);
            }
            // red-brick mansion block: stone bands, stone-dressed windows with keystones
            {
                Facade f = new Facade { row = A_MansionUp, wall = 'K', brick = 0 };
                f.layout = u =>
                {
                    List<Op> o = new List<Op> { Box('B', 0, 8, 0, 0.22, stone), Box('B', 0, 8, 2.75, 2.92, stone) };
                    for (int k = 0; k < 4; k++)
                    {
                        double cx = 1.0 + k * 2.0;
                        Op w = Win(cx - 0.5, cx + 0.5, 0.75, 2.55, k % 2 == 0 ? 0 : 1, Id(A_MansionUp, u, k), 5, true, 0.92);
                        o.Add(w);
                        o.Add(Box('P', cx - 0.62, cx - 0.5, 0.65, 2.7, stone)); o.Add(Box('P', cx + 0.5, cx + 0.62, 0.65, 2.7, stone));
                    }
                    return o;
                };
                L.Add(f);
            }
            {
                Facade f = new Facade { row = A_MansionGf, wall = 'T', dirt = true };
                f.layout = u => new List<Op>
                {
                    Box('B', 0, 8, 0, 0.35, C("6a665e")),
                    new Op { k = 'A', x0 = 3.3, x1 = 4.7, y0 = 0.35, y1 = 2.6, style = 2, id = Id(A_MansionGf, u, 9), frame = 0.25, lit = 0.8 },
                    Win(0.9, 2.1, 0.9, 2.9, 0, Id(A_MansionGf, u, 0), 5, true, 0.92),
                    Win(5.9, 7.1, 0.9, 2.9, 0, Id(A_MansionGf, u, 1), 5, true, 0.92),
                };
                L.Add(f);
            }
            // Georgian stock brick: Flemish bond, 6-over-6 sashes, gauged brick arches
            {
                Facade f = new Facade { row = A_GeorgUp, wall = 'K', brick = 2 };
                f.layout = u =>
                {
                    List<Op> o = new List<Op>();
                    for (int k = 0; k < 3; k++)
                    {
                        double cx = 0.9 + k * 1.77;
                        Op w = Win(cx - 0.48, cx + 0.48, 0.6, 2.75, 2, Id(A_GeorgUp, u, k), 2, true, 0.94);
                        w.rail = k == 1 && u % 2 == 1;
                        o.Add(w);
                    }
                    return o;
                };
                L.Add(f);
            }
            {
                Facade f = new Facade { row = A_GeorgGf, wall = 'Q', r = 0.88, g = 0.86, b = 0.81, dirt = true };
                f.layout = u => new List<Op>
                {
                    Box('B', 0, 5.334, 0, 0.3, C("5a5852")),
                    Box('D', 0.4, 1.4, 0.3, 2.75, Door6[(u + 3) % 6], 0),
                    new Op { k = 'F', x0 = 0.4, x1 = 1.4, y0 = 2.8, y1 = 3.25, frame = 0.9, id = Id(A_GeorgGf, u, 5), lit = 0.6 },
                    Win(2.2, 3.15, 0.8, 2.95, 2, Id(A_GeorgGf, u, 0), 0, true, 0.94),
                    Win(3.95, 4.9, 0.8, 2.95, 2, Id(A_GeorgGf, u, 1), 0, true, 0.94),
                };
                L.Add(f);
            }
            // mansard: slate face with two dormers per house
            {
                Facade f = new Facade { row = A_Mansard, wall = 'S', soot = false };
                f.layout = u =>
                {
                    List<Op> o = new List<Op>();
                    for (int k = 0; k < 2; k++)
                    {
                        double cx = 1.6 + k * 3.2;
                        o.Add(Box('P', cx - 0.62, cx + 0.62, 0.7, 2.6, C("4a4f55")));                     // lead cheeks
                        o.Add(Win(cx - 0.47, cx + 0.47, 0.82, 2.35, 1, Id(A_Mansard, u, k), 0, false, 0.92, 0.4));
                        o.Add(Box('B', cx - 0.66, cx + 0.66, 2.48, 2.72, C("e2ded4")));                    // dormer cornice
                    }
                    return o;
                };
                L.Add(f);
            }
            // pub ground floor (glazed tiles, big lit windows) — used on corner buildings
            {
                Facade f = new Facade { row = A_PubGf, wall = 'E', r = 0.2, g = 0.24, b = 0.2, dirt = true };
                f.layout = u => new List<Op>
                {
                    Box('T', 0, 8, 0, 0.9, C("2c4a36"), 0, 7),
                    Box('T', 0, 0.35, 0, 3.0, C("2c4a36"), 0, 9), Box('T', 7.65, 8, 0, 3.0, C("2c4a36"), 0, 9),
                    Box('X', 0, 8, 3.0, 3.85, C("1f2a2a"), 0, 0),
                    new Op { k = 'S', x0 = 0.45, x1 = 5.7, y0 = 0.9, y1 = 2.85, style = 2, id = Id(A_PubGf, u, 0), lit = 0.95 },
                    Box('D', 6.1, 7.4, 0.05, 2.7, C("3a2416"), 0),
                };
                L.Add(f);
            }
            return L;
        }

        static Facade ShopRow(int row, bool estate)
        {
            Facade f = new Facade { row = row, wall = 'E', r = 0.22, g = 0.22, b = 0.23, dirt = true };
            f.layout = u =>
            {
                double[] fas = Fascia5[(u + (estate ? 2 : 0)) % 5];
                int kind = estate ? new[] { 2, 0, 1, 2, 3 }[u % 5] : new[] { 0, 2, 3, 1, 0 }[u % 5];   // 0 open, 1 vacant, 2 shuttered, 3 café
                List<Op> o = new List<Op>
                {
                    Box('I', 0, 0.32, 0, 3.0, estate ? C("8e9196") : C("23262b")),
                    Box('I', 6.08, 6.4, 0, 3.0, estate ? C("8e9196") : C("23262b")),
                    Box('X', 0, 6.4, 2.95, 3.85, fas, 0, 0),
                    Box('B', 0, 6.4, 3.85, 4.0, C("cfc9bb")),
                    Box('P', 0.32, 6.08, 0, 0.5, estate ? C("6f7377") : C("2b2d30")),                 // stall riser
                };
                if (kind == 2)
                {
                    Op sh = Box('U', 0.32, 6.08, 0, 2.95, C("b8bcc0"));
                    sh.tag = true; o.Add(sh);
                }
                else
                {
                    o.Add(new Op { k = 'S', x0 = 0.38, x1 = 4.85, y0 = 0.5, y1 = 2.88, style = kind == 3 ? 2 : kind == 1 ? 1 : 0, id = Id(row, u, 0), lit = 0.9 });
                    o.Add(new Op { k = 'D', x0 = 5.0, x1 = 6.0, y0 = 0.0, y1 = 2.6, style = 1, r = 0.14, g = 0.15, b = 0.16 });
                    if (kind == 1) { Op s = o[o.Count - 2]; s.text = "TO LET"; }
                }
                return o;
            };
            return f;
        }

        // ------------------------------------------------------------------ atlas B: modern, estate, docks
        static List<Facade> DefsB()
        {
            List<Facade> L = new List<Facade>();
            double[] conc = C("9a978f"), white = C("dedbd2"), brownBrick = C("6e4a38");
            L.Add(new Facade { row = B_Concrete, wall = 'N' });
            L.Add(new Facade { row = B_ConcPanel, wall = 'M' });
            L.Add(new Facade { row = B_RoofFlat, wall = 'F', soot = false });
            L.Add(new Facade { row = B_Plant, wall = 'V', soot = false });
            L.Add(Plain(B_EstBrick, 'K', 3, white));
            L.Add(Plain(B_WhYel, 'K', 1, white));
            L.Add(Plain(B_WhRed, 'K', 4, white));

            // deck access: front doors and kitchen windows off the walkway
            {
                Facade f = new Facade { row = B_EstDeck, wall = 'K', brick = 3 };
                f.layout = u => new List<Op>
                {
                    Box('I', 0, 0.22, 0, 2.9, conc), Box('B', 0, 5.334, 2.55, 2.9, C("8c8a84")),
                    Box('D', 0.45, 1.35, 0.0, 2.1, EstDoor6[u % 6], u % 4 == 3 ? 2 : 1),
                    Win(1.8, 3.4, 1.0, 2.1, 3, Id(B_EstDeck, u, 0), 0, true, 0.88, 0.45),
                    new Op { k = 'W', x0 = 3.85, x1 = 4.6, y0 = 1.45, y1 = 2.1, style = 7, id = Id(B_EstDeck, u, 1), frosted = true, frame = 0.88, lit = 0.3 },
                };
                L.Add(f);
            }
            // the back of the deck blocks: living-room windows and balcony doors
            {
                Facade f = new Facade { row = B_EstBack, wall = 'K', brick = 3 };
                f.layout = u => new List<Op>
                {
                    Box('I', 0, 0.22, 0, 2.9, conc), Box('B', 0, 5.334, 2.55, 2.9, C("8c8a84")),
                    Win(0.45, 2.4, 0.9, 2.35, 3, Id(B_EstBack, u, 0), 0, true, 0.9, 0.45),
                    Win(2.75, 3.6, 0.05, 2.35, 7, Id(B_EstBack, u, 1), 0, false, 0.9, 0.45),
                    Win(3.95, 4.95, 0.9, 2.35, 3, Id(B_EstBack, u, 2), 0, true, 0.9, 0.45),
                };
                L.Add(f);
            }
            // brown-brick maisonette blocks with concrete floor bands
            {
                Facade f = new Facade { row = B_EstBrickUp, wall = 'K', brick = 3 };
                f.layout = u => new List<Op>
                {
                    Box('B', 0, 5.334, 0, 0.26, conc),
                    Win(0.6, 2.05, 0.85, 2.25, 3, Id(B_EstBrickUp, u, 0), 0, true, 0.9, 0.45),
                    Win(3.2, 4.65, 0.85, 2.25, 3, Id(B_EstBrickUp, u, 1), 0, true, 0.9, 0.45),
                };
                L.Add(f);
            }
            // estate ground floor: bin stores, entrances, blank brick, tagged metal doors
            {
                Facade f = new Facade { row = B_EstGf, wall = 'K', brick = 3, dirt = true };
                f.layout = u =>
                {
                    List<Op> o = new List<Op> { Box('B', 0, 5.334, 2.7, 3.0, conc) };
                    switch (u % 6)
                    {
                        case 0: { Op l = Box('L', 0.5, 4.8, 0, 2.3, C("5f6f66")); l.tag = true; o.Add(l); break; }
                        case 1:
                            o.Add(Box('P', 0.4, 2.6, 0, 2.5, C("2a2c2e")));
                            o.Add(Win(0.5, 2.5, 0.05, 2.4, 7, Id(B_EstGf, u, 0), 0, false, 0.5, 0.95));
                            o.Add(Box('P', 3.0, 3.25, 1.3, 1.6, C("9aa0a4")));                                 // intercom
                            break;
                        case 2: { o.Add(Win(1.0, 2.2, 1.85, 2.4, 7, Id(B_EstGf, u, 1), 0, false, 0.8, 0.2)); Op tg = Box('B', 0.3, 5.0, 0.1, 1.8, C("6e4a38")); tg.tag = true; o.Add(tg); break; }
                        case 3:
                            o.Add(Box('D', 0.5, 1.4, 0, 2.1, EstDoor6[(u + 2) % 6], 1));
                            o.Add(Win(2.0, 3.6, 0.9, 2.1, 3, Id(B_EstGf, u, 2), 0, true, 0.88, 0.4));
                            o.Add(Box('L', 2.0, 3.6, 0.9, 2.1, C("2a2c2e")));                                 // security grille
                            break;
                        case 4: { Op d = Box('D', 0.8, 2.6, 0, 2.2, C("6a6f72"), 2); o.Add(d); Op tg = Box('B', 0.8, 2.6, 0.2, 2.0, C("6a6f72")); tg.tag = true; o.Add(tg); o.Add(Box('D', 3.2, 4.8, 0, 2.2, C("6a6f72"), 2)); break; }
                        default: { Op tg = Box('B', 0.2, 5.1, 0.2, 2.4, C("6e4a38")); tg.tag = true; tg.style = 0; o.Add(Box('I', 2.6, 2.75, 0, 2.7, C("2a2b2d"))); o.Add(tg); break; }
                    }
                    return o;
                };
                // the tag-only boxes shouldn't paint over the brick: they're just markers for the overlay pass
                f.layout = Wrap(f.layout);
                L.Add(f);
            }
            // lock-up garages
            {
                Facade f = new Facade { row = B_Garages, wall = 'K', brick = 3, dirt = true };
                f.layout = u =>
                {
                    Op g = Box('G', 0.16, 2.5, 0, 2.12, Garage12[u % 12]);
                    g.tag = u % 3 != 1;
                    return new List<Op> { Box('B', 0, 2.667, 2.12, 2.6, conc), g };
                };
                L.Add(f);
            }
            // tower: precast panels, window bands, recessed balconies
            for (int v = 0; v < 2; v++)
            {
                int row = v == 0 ? B_TowerUp : B_TowerUpB, vv = v;
                Facade f = new Facade { row = row, wall = 'M' };
                f.layout = u =>
                {
                    List<Op> o = new List<Op>();
                    if (u % 2 == 0)
                    {
                        o.Add(Win(0.35, 2.75, 0.95, 2.4, 4, Id(row, u, 0), 0, false, 0.62, 0.45));
                        o.Add(Win(3.0, 3.7, 1.5, 2.4, 7, Id(row, u, 1), 0, false, 0.62, 0.3));
                        if (vv == 1) o.Add(Box('P', 0.35, 2.75, 0.15, 0.85, Panel6[(u / 2) % 6]));
                    }
                    else
                    {
                        o.Add(new Op { k = 'R', x0 = 0.25, x1 = 3.75, y0 = 0.0, y1 = 2.6, id = Id(row, u, 2), lit = 0.35 });
                        o.Add(Win(0.8, 2.6, 0.05, 2.3, 7, Id(row, u, 3), 0, false, 0.6, 0.4));
                        o.Add(Box('P', 0.25, 3.75, 0.0, 1.1, vv == 1 ? Panel6[(u + 3) % 6] : C("a8a59c"), 2));   // balcony front
                    }
                    return o;
                };
                L.Add(f);
            }
            // stair cores: concrete with a tall slot of glass blocks (always lit)
            {
                Facade f = new Facade { row = B_StairCore, wall = 'N' };
                f.layout = u => new List<Op> { new Op { k = 'W', x0 = 1.55, x1 = 2.45, y0 = 0.25, y1 = 2.8, style = 4, id = Id(B_StairCore, u, 0), frosted = true, frame = 0.6, lit = 0.85 } };
                L.Add(f);
            }
            // Canary warehouses: arched, small-paned windows, a loading door with a hoist, brick piers
            for (int v = 0; v < 2; v++)
            {
                int row = v == 0 ? B_WhUpYel : B_WhUpRed;
                int pal = v == 0 ? 1 : 4;
                Facade f = new Facade { row = row, wall = 'K', brick = pal };
                f.layout = u =>
                {
                    List<Op> o = new List<Op>();
                    for (int k = 0; k < 4; k++)
                    {
                        double cx = 1.333 + k * 2.667;
                        o.Add(Box('I', k * 2.667, k * 2.667 + 0.22, 0, 3.6, v == 0 ? C("8f7c58") : C("74402c")));
                        if (k == 2) { o.Add(Box('O', cx - 0.62, cx + 0.62, 0.1, 2.95, u % 2 == 0 ? C("2c4a36") : C("4a3324"))); continue; }
                        bool modern = (u + k) % 5 == 0;
                        o.Add(Win(cx - 0.66, cx + 0.66, 0.85, 2.7, modern ? 7 : 6, Id(row, u, k), 4, true, modern ? 0.2 : 0.3, 0.4));
                    }
                    return o;
                };
                L.Add(f);
            }
            {
                Facade f = new Facade { row = B_WhGf, wall = 'K', brick = 1, dirt = true };
                f.layout = u =>
                {
                    List<Op> o = new List<Op> { Box('B', 0, 10.667, 0, 0.5, C("4d4a44")) };
                    for (int k = 0; k < 4; k++)
                    {
                        double cx = 1.333 + k * 2.667;
                        if ((k + u) % 4 == 1) { Op d = Box('O', cx - 0.9, cx + 0.9, 0.0, 2.9, C("3a2a1e")); d.arch = true; o.Add(d); }
                        else o.Add(new Op { k = 'A', x0 = cx - 0.9, x1 = cx + 0.9, y0 = 0.5, y1 = 2.9, style = 6, id = Id(B_WhGf, u, k), frame = 0.2, lit = 0.55 });
                    }
                    return o;
                };
                L.Add(f);
            }
            // glass and stone offices
            L.Add(new Facade { row = B_GlassBlue, wall = 'G', layout = u => new List<Op> { new Op { k = 'C', x0 = 0, x1 = 1.6, y0 = 0, y1 = 3.8, style = 0, id = u } } });
            L.Add(new Facade { row = B_GlassGreen, wall = 'G', layout = u => new List<Op> { new Op { k = 'C', x0 = 0, x1 = 1.6, y0 = 0, y1 = 3.8, style = 1, id = u + 40 } } });
            L.Add(new Facade { row = B_GlassSilver, wall = 'G', layout = u => new List<Op> { new Op { k = 'C', x0 = 0, x1 = 1.6, y0 = 0, y1 = 3.8, style = 2, id = u + 80 } } });
            L.Add(new Facade { row = B_OfficeDark, wall = 'G', layout = u => new List<Op> { new Op { k = 'C', x0 = 0, x1 = 1.6, y0 = 0, y1 = 3.8, style = 3, id = u + 120 } } });
            L.Add(new Facade
            {
                row = B_OfficeStone, wall = 'T',
                layout = u => new List<Op> { Win(0.22, 1.38, 0.75, 3.25, 7, Id(B_OfficeStone, u, 0), 0, false, 0.35, 0.45) }
            });
            L.Add(new Facade
            {
                row = B_Lobby, wall = 'G', dirt = false,
                layout = u => new List<Op>
                {
                    new Op { k = 'C', x0 = 0, x1 = 2, y0 = 0, y1 = 4.8, style = 4, id = u * 4 }, new Op { k = 'C', x0 = 2, x1 = 4, y0 = 0, y1 = 4.8, style = 4, id = u * 4 + 1 },
                    new Op { k = 'C', x0 = 4, x1 = 6, y0 = 0, y1 = 4.8, style = 4, id = u * 4 + 2 }, new Op { k = 'C', x0 = 6, x1 = 8, y0 = 0, y1 = 4.8, style = 4, id = u * 4 + 3 },
                }
            });
            // balcony fronts (painted panels; some rusty)
            {
                Facade f = new Facade { row = B_Balcony, wall = 'N' };
                f.layout = u => new List<Op> { Box('P', 0.05, 5.28, 0.0, 0.98, Panel6[u % 6], u % 3 == 0 ? 2 : 0), Box('B', 0, 5.334, 0.98, 1.1, C("5d6268")) };
                L.Add(f);
            }
            L.Add(ShopRow(B_EstShop, true));
            RailDefs(L);
            return L;
        }

        /// Ops of kind 'B' with tag=true over brick are only markers for the overlay: keep the wall underneath.
        static Func<int, List<Op>> Wrap(Func<int, List<Op>> inner)
        {
            return u =>
            {
                List<Op> o = inner(u);
                foreach (Op op in o) if (op.tag && op.k == 'B') op.k = '-';
                return o;
            };
        }
    }
}
