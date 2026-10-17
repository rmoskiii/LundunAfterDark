// V4 surface detail: the textures, drawn in code (pure C#, no downloads, no branding).
// Values are brightness multipliers (the material colour tints them), RGBA bytes, row 0 = bottom.
//   Tarmac   512²  one tile = 10 m (roads' existing UVs)
//   Paving  1024²  one tile = 9 m: 0.6 × 0.75 m slabs laid in courses, a granite kerb band along v = 0 (the kerb edge)
//   Facades 1024²  the same 8 × 8 bay grid as before (4 m × 3.5 m bays, same window openings, so the lit-window
//                  glow still lines up), each with a darker band along the bottom (shading where the wall meets the street)
using System;

namespace LundunGreybox
{
    public static class SurfaceArt
    {
        public class Img
        {
            public int w, h; public byte[] rgba;
            public Img(int w, int h) { this.w = w; this.h = h; rgba = new byte[w * h * 4]; }
            public void Set(int x, int y, double r, double g, double b, double a = 1)
            {
                x = ((x % w) + w) % w; y = ((y % h) + h) % h;
                int i = (y * w + x) * 4;
                rgba[i] = B(r); rgba[i + 1] = B(g); rgba[i + 2] = B(b); rgba[i + 3] = B(a);
            }
            public void Get(int x, int y, out double r, out double g, out double b)
            {
                x = ((x % w) + w) % w; y = ((y % h) + h) % h;
                int i = (y * w + x) * 4;
                r = rgba[i] / 255.0; g = rgba[i + 1] / 255.0; b = rgba[i + 2] / 255.0;
            }
            public void Mul(int x, int y, double k) { double r, g, b; Get(x, y, out r, out g, out b); Set(x, y, r * k, g * k, b * k); }
            static byte B(double v) { return (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255); }
        }

        // ------------------------------------------------------------------ noise (tiles seamlessly)
        static uint Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
        static double R01(int x, int y, int seed) { return (Hash(x, y, seed) & 0xFFFFFF) / (double)0xFFFFFF; }

        /// Smooth value noise with period p (in cells) so the texture wraps.
        static double Value(double x, double y, int cells, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            Func<int, int, double> c = (a, b) => R01(((a % cells) + cells) % cells, ((b % cells) + cells) % cells, seed);
            double v00 = c(x0, y0), v10 = c(x0 + 1, y0), v01 = c(x0, y0 + 1), v11 = c(x0 + 1, y0 + 1);
            return (v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy;
        }
        static double Fbm(int px, int py, int size, int baseCells, int seed)
        {
            double s = 0, amp = 0.5, tot = 0; int cells = baseCells;
            for (int o = 0; o < 4; o++)
            {
                s += amp * Value(px * (double)cells / size, py * (double)cells / size, cells, seed + o * 31);
                tot += amp; amp *= 0.5; cells *= 2;
            }
            return s / tot;
        }

        // ------------------------------------------------------------------ tarmac
        public static Img Tarmac(int n = 512)
        {
            Img im = new Img(n, n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double v = 0.86 + (Fbm(x, y, n, 4, 11) - 0.5) * 0.12 + (R01(x, y, 3) - 0.5) * 0.10;
                    double r = R01(x, y, 5);
                    if (r < 0.03) v += 0.16;                       // light aggregate chips
                    else if (r > 0.98) v -= 0.14;                  // dark specks
                    im.Set(x, y, v, v, v * 1.01);
                }
            // repair patches with a dark sealant edge
            Random rnd = new Random(17);
            for (int p = 0; p < 4; p++)
            {
                int px = rnd.Next(n), py = rnd.Next(n), pw = 50 + rnd.Next(90), ph = 40 + rnd.Next(80);
                double tone = rnd.NextDouble() < 0.5 ? 0.9 : 1.07;
                for (int y = 0; y < ph; y++)
                    for (int x = 0; x < pw; x++)
                    {
                        bool edge = x < 2 || y < 2 || x >= pw - 2 || y >= ph - 2;
                        im.Mul(px + x, py + y, edge ? 0.7 : tone);
                    }
            }
            // hairline cracks
            for (int c = 0; c < 5; c++)
            {
                double x = rnd.Next(n), y = rnd.Next(n), a = rnd.NextDouble() * Math.PI * 2;
                int len = 60 + rnd.Next(160);
                for (int i = 0; i < len; i++)
                {
                    a += (rnd.NextDouble() - 0.5) * 0.6;
                    x += Math.Cos(a); y += Math.Sin(a);
                    im.Mul((int)x, (int)y, 0.62);
                }
            }
            return im;
        }

        // ------------------------------------------------------------------ paving slabs + granite kerb
        public const double PavingTileM = 9.0, KerbM = 0.3;

        public static Img Paving(int n = 1024)
        {
            Img im = new Img(n, n);
            double ppm = n / PavingTileM;                   // pixels per metre
            int kerbPx = (int)Math.Round(KerbM * ppm);
            double slabU = 0.6 * ppm, slabV = 0.75 * ppm;
            Random rnd = new Random(23);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    double v;
                    if (y < kerbPx)
                    {
                        // granite kerbstones, 0.9 m long
                        double kx = x % (0.9 * ppm);
                        bool joint = kx < 2 || y >= kerbPx - 2;
                        double grain = R01(x, y, 41);
                        v = 0.74 + (grain < 0.15 ? -0.1 : grain > 0.9 ? 0.16 : 0) + (Fbm(x, y, n, 16, 43) - 0.5) * 0.08;
                        if (joint) v = 0.5;
                        im.Set(x, y, v, v, v * 1.02);
                        continue;
                    }
                    int row = (int)((y - kerbPx) / slabV);
                    double off = (row % 2) * slabU * 0.5;
                    double lx = (x + off) % slabU, ly = (y - kerbPx) - row * slabV;
                    int col = (int)Math.Floor((x + off) / slabU);
                    bool gap = lx < 2 || ly < 2;
                    double slabTone = 0.9 + (R01(col, row, 7) - 0.5) * 0.12;
                    v = slabTone + (Fbm(x, y, n, 32, 9) - 0.5) * 0.06 + (R01(x, y, 13) - 0.5) * 0.05;
                    if (lx < 4 || ly < 4) v -= 0.04;                // worn bevel by the joint
                    if (gap) v = 0.55;
                    // the odd stained slab
                    if (R01(col, row, 19) < 0.04) v -= 0.08 * Value(x / 12.0, y / 12.0, 1000, 5);
                    im.Set(x, y, v, v, v * 1.01);
                }
            // chewing gum (very London)
            for (int i = 0; i < 160; i++)
            {
                int gx = rnd.Next(n), gy = kerbPx + 6 + rnd.Next(n - kerbPx - 6);
                double r = 1.2 + rnd.NextDouble() * 1.8;
                for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
                        if (dx * dx + dy * dy <= r * r) { double c, g, b; im.Get(gx + dx, gy + dy, out c, out g, out b); im.Set(gx + dx, gy + dy, c * 0.4 + 0.58, g * 0.4 + 0.58, b * 0.4 + 0.58); }
            }
            // a few cracked slabs
            for (int c = 0; c < 6; c++)
            {
                double x = rnd.Next(n), y = kerbPx + 10 + rnd.Next(n - kerbPx - 20), a = rnd.NextDouble() * Math.PI;
                for (int i = 0; i < 50; i++) { a += (rnd.NextDouble() - 0.5) * 0.5; x += Math.Cos(a); y += Math.Sin(a); im.Mul((int)x, (int)y, 0.6); }
            }
            return im;
        }

        // ------------------------------------------------------------------ facades
        // bay = n/8 px (4 m wide, 3.5 m tall). Window opening: x 7/32..25/32, y 8/32..26/32 of a bay (same as before).
        public static Img Facade(string style, int n = 1024)
        {
            Img im = new Img(n, n);
            int bay = n / 8;
            int wx0 = bay * 7 / 32, wx1 = bay * 25 / 32, wy0 = bay * 8 / 32, wy1 = bay * 26 / 32;
            double pxPerM = bay / 3.5;
            Random rnd = new Random(style.GetHashCode() & 0xFFFF);

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int bx = x % bay, by = y % bay, floor = y / bay;
                    bool inWin = bx >= wx0 && bx < wx1 && by >= wy0 && by < wy1;
                    double r, g, b;
                    if (inWin) Window(style, bx - wx0, by - wy0, wx1 - wx0, wy1 - wy0, out r, out g, out b);
                    else Wall(style, x, y, bx, by, floor, bay, wx0, wx1, wy0, wy1, out r, out g, out b);
                    im.Set(x, y, r, g, b);
                }

            // shading where the wall meets the street: darkest at the bottom, gone by ~1.3 m
            int band = (int)(1.3 * pxPerM);
            for (int y = 0; y < band; y++)
            {
                double t = y / (double)band;
                double k = 0.5 + 0.5 * (t * t * (3 - 2 * t));
                for (int x = 0; x < n; x++) im.Mul(x, y, k);
            }
            return im;
        }

        /// Where roofs read their colour: a plain bit of wall above the first window (not in the dark base band).
        public const double RoofU = 3.5 / 256.0, RoofV = 28.5 / 256.0;

        static void Wall(string style, int x, int y, int bx, int by, int floor, int bay, int wx0, int wx1, int wy0, int wy1, out double r, out double g, out double b)
        {
            double n1 = (Fbm(x, y, 1024, 8, 71) - 0.5);
            bool sill = by >= wy0 - 6 && by < wy0 && bx >= wx0 - 5 && bx < wx1 + 5;
            bool lintel = by >= wy1 && by < wy1 + 8 && bx >= wx0 - 3 && bx < wx1 + 3;
            switch (style)
            {
                case "stucco":
                {
                    // smooth painted render, rusticated ground floor, a moulded band at each floor, window surrounds
                    double v = 0.96 + n1 * 0.04;
                    if (floor == 0 && by < bay - 8 && ((by % 14) < 2 || ((bx + (by / 14 % 2) * 20) % 40) < 1)) v = 0.8;
                    if (by >= bay - 6) v = by >= bay - 2 ? 0.78 : 1.0;                 // floor band + shadow under it
                    bool surround = bx >= wx0 - 5 && bx < wx1 + 5 && by >= wy0 - 4 && by < wy1 + 6;
                    if (surround) v = (bx == wx0 - 5 || by == wy0 - 4) ? 0.82 : 1.0;
                    if (sill) v = 1.0;
                    r = v; g = v * 0.99; b = v * 0.96;
                    return;
                }
                case "panels":
                {
                    // modern: spandrel panels between floors, slim mullions
                    bool mullion = (bx % (bay / 4)) < 2;
                    bool spandrel = by >= wy1 || by < wy0;
                    double v = spandrel ? 0.88 + n1 * 0.05 : 0.26;
                    if (mullion) v = 0.42;
                    if (by >= bay - 3 && by < bay - 1) v = 0.36;
                    r = spandrel ? v : v * 0.9; g = spandrel ? v : v * 1.0; b = spandrel ? v : v * 1.15;
                    return;
                }
                default:
                {
                    // brick: 215 × 65 mm bricks, 10 mm mortar, stretcher bond (rows of 3 px, bricks 8 px)
                    bool estate = style == "estate", warehouse = style == "warehouse";
                    if (estate && by < 10) { double cv = 0.95 + n1 * 0.05; if (by < 2) cv = 0.7; r = cv; g = cv; b = cv * 0.98; return; }   // concrete floor slab
                    int course = y / 3;
                    bool mortar = (y % 3) == 2 || ((x + (course % 2) * 4) % 8) == 7;
                    int brickId = (x + (course % 2) * 4) / 8;
                    double tone = 0.84 + (R01(brickId, course, 5) - 0.5) * 0.18 + n1 * 0.06;
                    double warm = (R01(brickId, course, 9) - 0.5) * 0.08;
                    if (lintel)
                    {
                        // soldier course over the window
                        mortar = (y % 8) == 7 || ((x % 3) == 2);
                        tone -= 0.04;
                    }
                    if (sill) { r = 0.98; g = 0.97; b = 0.94; return; }               // stone sill
                    if (mortar) { r = 0.97; g = 0.95; b = 0.9; return; }
                    if (estate) { r = tone * 1.0; g = tone * 0.86; b = tone * 0.78; return; }
                    if (warehouse) { r = tone * 1.04; g = tone * 0.97; b = tone * 0.78; return; }     // yellow London stock
                    r = tone + warm; g = tone * 0.95; b = tone * 0.9 - warm * 0.5;
                    return;
                }
            }
        }

        static void Window(string style, int x, int y, int w, int h, out double r, out double g, out double b)
        {
            // glass (same dark glass as before) with frames: sash windows with glazing bars, metal grids, or plain
            double gr = 0.16, gg = 0.18, gb = 0.22;
            double frame = style == "estate" || style == "panels" ? 0.55 : 0.92;
            bool edge = x < 3 || y < 3 || x >= w - 3 || y >= h - 3;
            bool bar = false;
            if (style == "brick" || style == "stucco")
            {
                bool meeting = Math.Abs(y - h / 2) <= 1;
                bool vert = Math.Abs(x - w / 3) < 1 || Math.Abs(x - 2 * w / 3) < 1;
                bool hor = Math.Abs(y - h / 4) < 1 || Math.Abs(y - 3 * h / 4) < 1;
                bar = meeting || vert || hor;
            }
            else if (style == "warehouse") bar = (x % (w / 4)) == 0 || (y % (h / 4)) == 0;
            else if (style == "estate") bar = Math.Abs(y - h * 2 / 3) <= 1;
            else bar = false;
            bool reveal = x < 5 || y < 5 || x >= w - 5 || y >= h - 5;
            if (edge || bar) { r = frame; g = frame; b = frame; }
            else if (reveal && !edge) { r = gr * 0.7; g = gg * 0.7; b = gb * 0.7; }
            else
            {
                double sheen = 1 + 0.25 * Math.Max(0, 1 - Math.Abs((x - y * 0.6) / (w * 0.25)));   // a faint reflection
                r = gr * sheen; g = gg * sheen; b = gb * sheen;
            }
        }
    }
}
