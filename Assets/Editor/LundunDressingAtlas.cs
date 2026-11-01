// Pure C#: paints the one texture every piece of street dressing shares (so the whole lot draws in a
// handful of batches): flat colour swatches, shop / pub / street-name signs, billboards, our own
// "Lundun Underground" sign, bubble-letter graffiti, chain-link and railing panels.
// No UnityEngine types, so it can be tested outside Unity. LundunDressingBuilder saves it as a PNG.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public struct Col
    {
        public byte r, g, b, a;
        public Col(int r, int g, int b, int a = 255) { this.r = (byte)r; this.g = (byte)g; this.b = (byte)b; this.a = (byte)a; }
        public static Col Hex(string h, int a = 255)
        {
            h = h.TrimStart('#');
            return new Col(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16), a);
        }
        public Col Lerp(Col o, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return new Col((int)(r + (o.r - r) * t), (int)(g + (o.g - g) * t), (int)(b + (o.b - b) * t), (int)(a + (o.a - a) * t));
        }
        public Col Scale(double k) { return new Col(Clamp(r * k), Clamp(g * k), Clamp(b * k), a); }
        static int Clamp(double v) { return (int)Math.Max(0, Math.Min(255, v)); }
    }

    /// Normalised UV rectangle inside the atlas (v = 0 at the bottom, like Unity).
    public struct UVRect
    {
        public double u0, v0, u1, v1;
        public UVRect(double u0, double v0, double u1, double v1) { this.u0 = u0; this.v0 = v0; this.u1 = u1; this.v1 = v1; }
        public double Cu { get { return (u0 + u1) / 2; } }
        public double Cv { get { return (v0 + v1) / 2; } }
    }

    public class DressAtlas
    {
        public readonly int W, H;
        public readonly byte[] px;                 // RGBA, row 0 = bottom (Unity's SetPixels32 order)
        public readonly Dictionary<string, UVRect> rects = new Dictionary<string, UVRect>();
        int shelfX = 0, shelfY = 0, shelfH = 0;
        int cx0, cy0, cx1, cy1;                    // painting is clipped to the tile being drawn
        const int Pad = 4;
        readonly Random rng;

        public DressAtlas(int w, int h, int seed) { W = w; H = h; px = new byte[w * h * 4]; rng = new Random(seed); cx1 = w; cy1 = h; }

        public UVRect this[string name] { get { return rects[name]; } }
        public bool Has(string name) { return rects.ContainsKey(name); }

        /// Shelf packer, filling from the top of the texture down. Returns the pixel rect (x, y = bottom-left).
        public bool Alloc(string name, int w, int h, out int x, out int y)
        {
            if (shelfX + w + Pad > W) { shelfX = 0; shelfY += shelfH + Pad; shelfH = 0; }
            x = shelfX; y = H - (shelfY + h);
            if (y < 0 || w > W) throw new Exception("Dressing atlas is full (at '" + name + "'). Make it bigger.");
            shelfX += w + Pad; shelfH = Math.Max(shelfH, h);
            cx0 = x; cy0 = y; cx1 = x + w; cy1 = y + h;
            // UVs inset half a pixel so filtering never reaches the neighbour
            rects[name] = new UVRect((x + 0.5) / W, (y + 0.5) / H, (x + w - 0.5) / W, (y + h - 0.5) / H);
            return true;
        }
        public int UsedHeight { get { return shelfY + shelfH; } }

        // ------------------------------------------------------------------ pixels
        public void Put(int x, int y, Col c, double cover)
        {
            if (x < cx0 || y < cy0 || x >= cx1 || y >= cy1 || cover <= 0) return;
            int i = (y * W + x) * 4;
            double a = cover * c.a / 255.0;
            if (a >= 0.999) { px[i] = c.r; px[i + 1] = c.g; px[i + 2] = c.b; px[i + 3] = 255; return; }
            double da = px[i + 3] / 255.0, oa = a + da * (1 - a);
            if (oa <= 0) return;
            px[i] = (byte)((c.r * a + px[i] * da * (1 - a)) / oa);
            px[i + 1] = (byte)((c.g * a + px[i + 1] * da * (1 - a)) / oa);
            px[i + 2] = (byte)((c.b * a + px[i + 2] * da * (1 - a)) / oa);
            px[i + 3] = (byte)(oa * 255);
        }
        public void Fill(int x, int y, int w, int h, Col c)
        {
            for (int j = Math.Max(Math.Max(0, cy0 - 2), y); j < Math.Min(Math.Min(H, cy1 + 2), y + h); j++)
                for (int i = Math.Max(Math.Max(0, cx0 - 2), x); i < Math.Min(Math.Min(W, cx1 + 2), x + w); i++) { int k = (j * W + i) * 4; px[k] = c.r; px[k + 1] = c.g; px[k + 2] = c.b; px[k + 3] = c.a; }
        }
        public void FillV(int x, int y, int w, int h, Col bottom, Col top)
        {
            for (int j = 0; j < h; j++) Fill(x, y + j, w, 1, bottom.Lerp(top, h <= 1 ? 0 : (double)j / (h - 1)));
        }
        public void Frame(int x, int y, int w, int h, int t, Col c)
        {
            Fill(x, y, w, t, c); Fill(x, y + h - t, w, t, c); Fill(x, y, t, h, c); Fill(x + w - t, y, t, h, c);
        }
        public void Disc(double cx, double cy, double r, Col c)
        {
            for (int j = (int)(cy - r - 1); j <= (int)(cy + r + 1); j++)
                for (int i = (int)(cx - r - 1); i <= (int)(cx + r + 1); i++)
                {
                    double d = Math.Sqrt((i + 0.5 - cx) * (i + 0.5 - cx) + (j + 0.5 - cy) * (j + 0.5 - cy));
                    Put(i, j, c, r + 0.5 - d);
                }
        }
        public void Seg(double ax, double ay, double bx, double by, double r, Col c)
        {
            int x0 = (int)(Math.Min(ax, bx) - r - 1), x1 = (int)(Math.Max(ax, bx) + r + 1);
            int y0 = (int)(Math.Min(ay, by) - r - 1), y1 = (int)(Math.Max(ay, by) + r + 1);
            for (int j = y0; j <= y1; j++)
                for (int i = x0; i <= x1; i++)
                    Put(i, j, c, r + 0.5 - SegD(i + 0.5, j + 0.5, ax, ay, bx, by));
        }
        static double SegD(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l2));
            double ex = ax + dx * t - px, ey = ay + dy * t - py;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        // ------------------------------------------------------------------ text
        public enum Align { Left, Centre }

        /// Text fitted into a box: smooth rounded strokes (reads like vinyl sign lettering).
        /// weight = stroke radius in glyph cells (0.35 light … 0.6 heavy). Returns the cell size used.
        public double Text(string s, int bx, int by, int bw, int bh, Col c, double weight, Align align, double maxCell = 999)
        {
            s = s.ToUpperInvariant();
            int cols = PixelFont.Columns(s);
            double cs = Math.Min(Math.Min(bh / (7.0 + 2 * weight), bw / (cols + 2 * weight)), maxCell);
            double width = cols * cs;
            double x = align == Align.Centre ? bx + (bw - width) / 2 : bx + weight * cs;
            double top = by + bh / 2.0 + 3.5 * cs;
            foreach (char ch in s)
            {
                Glyph(ch, x, top, cs, weight * cs, c, 0, 1);
                x += PixelFont.Advance(ch) * cs;
            }
            return cs;
        }

        /// One character. top = y of the glyph's top edge; shear tilts it (italic); r = stroke radius in pixels.
        public void Glyph(char ch, double x, double top, double cs, double r, Col c, double shear, double scaleY)
        {
            List<double[]> segs = PixelFont.Segments(ch);
            if (segs == null) return;
            double h = 7 * cs * scaleY;
            int x0 = (int)(x - r - 2 - Math.Abs(shear) * h), x1 = (int)(x + 5 * cs + r + 2 + Math.Abs(shear) * h);
            int y0 = (int)(top - h - r - 2), y1 = (int)(top + r + 2);
            for (int j = y0; j <= y1; j++)
                for (int i = x0; i <= x1; i++)
                {
                    double gy = (top - (j + 0.5)) / (cs * scaleY);            // glyph rows, 0 = top
                    double gx = (i + 0.5 - x + shear * (top - (j + 0.5) - h / 2)) / cs;
                    double best = double.MaxValue;
                    foreach (double[] sg in segs)
                    {
                        double d = SegD(gx, gy * scaleY, sg[0], sg[1] * scaleY, sg[2], sg[3] * scaleY);
                        if (d < best) best = d;
                    }
                    Put(i, j, c, r + 0.5 - best * cs);
                }
        }

        // ------------------------------------------------------------------ building blocks
        public void Swatch(string name, Col c)
        {
            int x, y; if (!Alloc(name, 32, 32, out x, out y)) return;
            Fill(x - 2, y - 2, 36, 36, c);
        }

        public void Sign(string name, int w, int h, string text, Col bg, Col fg, Col edge, double weight = 0.5)
        {
            int x, y; if (!Alloc(name, w, h, out x, out y)) return;
            Fill(x, y, w, h, bg);
            if (edge.a > 0) { Fill(x, y + h - h / 8, w, h / 8, edge); Fill(x, y, w, h / 10, edge); }
            Text(text, x + h / 6, y + h / 10, w - h / 3, h - h / 4, fg, weight, Align.Centre);
        }

        public void StreetPlate(string name, string road, string code)
        {
            int w = 384, h = 84, x, y;
            if (!Alloc(name, w, h, out x, out y)) return;
            Fill(x, y, w, h, Col.Hex("f4f3ee"));
            Frame(x + 4, y + 4, w - 8, h - 8, 4, Col.Hex("111111"));
            int codeW = code.Length * 15 + 16;
            Text(road, x + 12, y + 16, w - 34 - codeW, h - 32, Col.Hex("141414"), 0.5, Align.Centre, 7);
            Text(code, x + w - codeW - 10, y + 22, codeW, h - 44, Col.Hex("c0161d"), 0.5, Align.Centre, 4.5);
        }

        public void Billboard(string name, string line1, string line2, Col bgA, Col bgB, Col fg, Col accent)
        {
            int w = 512, h = 256, x, y;
            if (!Alloc(name, w, h, out x, out y)) return;
            FillV(x, y, w, h, bgB, bgA);
            for (int k = 0; k < 4; k++) Seg(x + w * 0.80 + k * 24, y, x + w * 0.92 + k * 24, y + h, 6, accent.Lerp(bgA, 0.55));
            Fill(x, y, w, 10, Col.Hex("101010")); Fill(x, y + h - 10, w, 10, Col.Hex("101010"));
            Text(line1, x + 20, y + h / 2 - 10, (int)(w * 0.78) - 20, h / 2 - 10, fg, 0.55, Align.Left, 14);
            Text(line2, x + 20, y + 24, (int)(w * 0.78) - 20, h / 4, accent, 0.5, Align.Left, 7);
        }

        /// Our own Underground mark: navy lozenge, yellow diamond, navy U. Deliberately nothing like a ring-and-bar.
        public void UndergroundLogo(string name)
        {
            int s = 256, x, y; if (!Alloc(name, s, s, out x, out y)) return;
            Col navy = Col.Hex("1b2766"), yellow = Col.Hex("ffc72c"), white = Col.Hex("ffffff");
            Fill(x, y, s, s, new Col(0, 0, 0, 0));
            // rounded square plate
            for (int j = 0; j < s; j++)
                for (int i = 0; i < s; i++)
                {
                    double dx = Math.Max(0, Math.Abs(i + 0.5 - s / 2.0) - (s / 2.0 - 40)), dy = Math.Max(0, Math.Abs(j + 0.5 - s / 2.0) - (s / 2.0 - 40));
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    Put(x + i, y + j, white, 40.5 - d);
                    Put(x + i, y + j, navy, 32.5 - d);
                }
            // diamond
            double cx = x + s / 2.0, cy = y + s / 2.0, R = 92;
            for (int j = (int)(cy - R); j <= (int)(cy + R); j++)
                for (int i = (int)(cx - R); i <= (int)(cx + R); i++)
                {
                    double d = Math.Abs(i + 0.5 - cx) + Math.Abs(j + 0.5 - cy);
                    Put(i, j, yellow, (R - d) * 0.7 + 0.5);
                }
            Glyph('U', cx - 2.5 * 16, cy + 3.5 * 16, 16, 9, navy, 0, 1);
        }

        public void StationBoard(string name, string station)
        {
            int w = 1024, h = 160, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            Col navy = Col.Hex("1b2766"), yellow = Col.Hex("ffc72c");
            Fill(x, y, w, h, navy);
            Fill(x, y + 18, w, 10, yellow);
            // mini logo on the left
            double cx = x + 80, cy = y + h / 2.0 + 8, R = 52;
            for (int j = (int)(cy - R); j <= (int)(cy + R); j++)
                for (int i = (int)(cx - R); i <= (int)(cx + R); i++)
                    Put(i, j, yellow, (R - Math.Abs(i + 0.5 - cx) - Math.Abs(j + 0.5 - cy)) * 0.7 + 0.5);
            Glyph('U', cx - 2.5 * 9, cy + 3.5 * 9, 9, 5, navy, 0, 1);
            Text("LUNDUN UNDERGROUND", x + 150, y + 92, w - 170, 54, Col.Hex("ffffff"), 0.48, Align.Left, 8);
            Text(station, x + 150, y + 36, w - 170, 50, yellow, 0.48, Align.Left, 7);
        }

        public void ChainLink(string name)
        {
            int s = 256, x, y; if (!Alloc(name, s, s, out x, out y)) return;
            Fill(x, y, s, s, new Col(0, 0, 0, 0));
            Col wire = Col.Hex("9aa0a4");
            int n = 12; double step = (double)s / n;
            for (int k = -n; k <= 2 * n; k++)
            {
                Seg(x + k * step, y, x + k * step + s, y + s, 1.3, wire);
                Seg(x + k * step, y + s, x + k * step + s, y, 1.3, wire);
            }
            Fill(x, y, s, 6, Col.Hex("2e4a33")); Fill(x, y + s - 6, s, 6, Col.Hex("2e4a33"));
            Clip(x, y, s, s);
        }

        public void Railing(string name)
        {
            int w = 256, h = 128, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            Fill(x, y, w, h, new Col(0, 0, 0, 0));
            Col iron = Col.Hex("121314");
            Fill(x, y + 6, w, 7, iron); Fill(x, y + h - 26, w, 7, iron);
            for (int k = 0; k < 16; k++)
            {
                double bx = x + 8 + k * 16;
                Seg(bx, y, bx, y + h - 14, 2.2, iron);
                Seg(bx - 5, y + h - 14, bx, y + h - 3, 1.6, iron);   // spear tips
                Seg(bx + 5, y + h - 14, bx, y + h - 3, 1.6, iron);
            }
            Clip(x, y, w, h);
        }

        /// Fully opaque or fully clear (alpha-tested material).
        void Clip(int x, int y, int w, int h)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++) { int k = (j * W + i) * 4; px[k + 3] = (byte)(px[k + 3] > 110 ? 255 : 0); }
        }

        /// Bubble-letter piece: outline, two-tone fill, highlights, drips. Transparent background.
        public void Graffiti(string name, string word, int style)
        {
            int w = 448, h = 192, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            Fill(x, y, w, h, new Col(0, 0, 0, 0));
            Col[][] palettes = {
                new[] { Col.Hex("ff3d7f"), Col.Hex("ffd23f"), Col.Hex("101010"), Col.Hex("ffffff") },
                new[] { Col.Hex("3ee0ff"), Col.Hex("2a5bff"), Col.Hex("0b0b20"), Col.Hex("e8f7ff") },
                new[] { Col.Hex("9cff3a"), Col.Hex("1fa84a"), Col.Hex("101010"), Col.Hex("ffffff") },
                new[] { Col.Hex("ffffff"), Col.Hex("c9c9c9"), Col.Hex("111111"), Col.Hex("ff2a2a") },   // silver throw-up
                new[] { Col.Hex("ff7a1a"), Col.Hex("d1121f"), Col.Hex("1a0606"), Col.Hex("ffe6a0") },
                new[] { Col.Hex("c77dff"), Col.Hex("5a189a"), Col.Hex("0d0018"), Col.Hex("ffffff") } };
            Col[] p = palettes[style % palettes.Length];
            int cols = PixelFont.Columns(word);
            double cs = Math.Min((h - 70) / 7.0, (w - 70) / (double)cols);
            double startX = x + (w - cols * cs) / 2, top = y + h / 2.0 + 3.5 * cs + 6;
            double shear = (rng.NextDouble() - 0.3) * 0.35;
            double[] dy = new double[word.Length], sc = new double[word.Length];
            for (int k = 0; k < word.Length; k++) { dy[k] = (rng.NextDouble() - 0.5) * cs * 1.2; sc[k] = 0.92 + rng.NextDouble() * 0.2; }
            double br = cs * 0.95;
            // pass 1: drop shadow, 2: outline, 3: fill, 4: highlight
            for (int pass = 0; pass < 4; pass++)
            {
                double cx = startX;
                for (int k = 0; k < word.Length; k++)
                {
                    char ch = char.ToUpperInvariant(word[k]);
                    double t = top + dy[k], ox = pass == 0 ? cs * 0.35 : 0, oy = pass == 0 ? -cs * 0.35 : 0;
                    if (pass == 0) Glyph(ch, cx + ox, t + oy, cs, br + 7, p[2].Lerp(new Col(0, 0, 0, 0), 0.35), shear, sc[k]);
                    else if (pass == 1) Glyph(ch, cx, t, cs, br + 6, p[2], shear, sc[k]);
                    else if (pass == 2) GradientGlyph(ch, cx, t, cs, br, p[0], p[1], shear, sc[k]);
                    else Glyph(ch, cx - cs * 0.18, t + cs * 0.22, cs, br * 0.22, p[3], shear, sc[k]);
                    cx += PixelFont.Advance(ch) * cs;
                }
            }
            // drips
            for (int d = 0; d < 6 + rng.Next(6); d++)
            {
                double dx = startX + rng.NextDouble() * cols * cs, len = 8 + rng.NextDouble() * 34;
                double y0 = top - 7 * cs + 4;
                Seg(dx, y0, dx, y0 - len, 2.2, p[1]); Disc(dx, y0 - len, 3.2, p[1]);
            }
            // a few stars / sparkles
            for (int s = 0; s < 4; s++)
            {
                double sx = x + 20 + rng.NextDouble() * (w - 40), sy = y + 20 + rng.NextDouble() * (h - 40), r = 6 + rng.NextDouble() * 6;
                Seg(sx - r, sy, sx + r, sy, 1.5, p[3]); Seg(sx, sy - r, sx, sy + r, 1.5, p[3]);
            }
            Clip(x, y, w, h);
        }

        void GradientGlyph(char ch, double x, double top, double cs, double r, Col a, Col b, double shear, double scaleY)
        {
            // fill in bands so the colour fades from a (top) to b (bottom)
            int bands = 8;
            List<double[]> segs = PixelFont.Segments(ch);
            if (segs == null) return;
            double h = 7 * cs * scaleY;
            int x0 = (int)(x - r - 2 - Math.Abs(shear) * h), x1 = (int)(x + 5 * cs + r + 2 + Math.Abs(shear) * h);
            int y0 = (int)(top - h - r - 2), y1 = (int)(top + r + 2);
            for (int j = y0; j <= y1; j++)
            {
                double t = Math.Floor((top + r - j) / (h + 2 * r) * bands) / bands;
                Col c = a.Lerp(b, t);
                for (int i = x0; i <= x1; i++)
                {
                    double gy = (top - (j + 0.5)) / (cs * scaleY);
                    double gx = (i + 0.5 - x + shear * (top - (j + 0.5) - h / 2)) / cs;
                    double best = double.MaxValue;
                    foreach (double[] sg in segs) { double d = SegD(gx, gy * scaleY, sg[0], sg[1] * scaleY, sg[2], sg[3] * scaleY); if (d < best) best = d; }
                    Put(i, j, c, r + 0.5 - best * cs);
                }
            }
        }

        /// Steps going down into the dark, painted flat (laid on the ground inside an Underground entrance).
        public void Stairwell(string name)
        {
            int w = 128, h = 256, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            int steps = 14;
            for (int k = 0; k < steps; k++)
            {
                double t = (double)k / steps;                       // 0 = top step (light)
                int sy = y + h - (int)((k + 1) * (double)h / steps), sh = (int)Math.Ceiling((double)h / steps);
                Col tread = Col.Hex("b9b5ab").Lerp(Col.Hex("060606"), Math.Pow(t, 0.7));
                Fill(x, sy, w, sh, tread);
                Fill(x + 6, sy + sh - 3, w - 12, 3, Col.Hex("e0b52a").Lerp(Col.Hex("201a08"), t));   // yellow nosing
            }
            Fill(x, y, 10, h, Col.Hex("8d8a84")); Fill(x + w - 10, y, 10, h, Col.Hex("8d8a84"));     // side walls
        }

        public void Shutter(string name)
        {
            int w = 256, h = 160, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            for (int j = 0; j < h; j++) Fill(x, y + j, w, 1, (j % 8) < 5 ? Col.Hex("8e9296") : Col.Hex("6b6f73"));
            Fill(x, y, w, 10, Col.Hex("3a3d40"));
        }

        public void ShopWindow(string name)
        {
            int w = 256, h = 160, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            FillV(x, y, w, h, Col.Hex("ffe2a8"), Col.Hex("fff3d6"));
            // counters, shelves, people-ish blobs in silhouette
            Fill(x, y, w, 40, Col.Hex("c23a2b"));
            for (int k = 0; k < 3; k++) Fill(x, y + 70 + k * 26, w, 5, Col.Hex("8a6a40"));
            for (int k = 0; k < 2; k++) { double cx = x + 60 + k * 120; Disc(cx, y + 76, 13, Col.Hex("3a2a20")); Fill((int)cx - 15, y + 30, 30, 46, Col.Hex("3a2a20")); }
            Frame(x, y, w, h, 6, Col.Hex("1a1a1a"));
            Fill(x + w / 2 - 3, y, 6, h, Col.Hex("1a1a1a"));
        }

        public void PubWindow(string name)
        {
            int w = 256, h = 160, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            FillV(x, y, w, h, Col.Hex("b5741f"), Col.Hex("f2b45a"));
            for (int i = 0; i < 4; i++) for (int j = 0; j < 3; j++) Frame(x + i * 64, y + j * 53, 64, 54, 4, Col.Hex("1f3b2c"));
            Frame(x, y, w, h, 8, Col.Hex("1f3b2c"));
        }

        /// Five-a-side surface with the pitch lines painted on (26 x 16 m).
        public void Pitch(string name)
        {
            int w = 416, h = 256, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            for (int i = 0; i < w; i += 32) Fill(x + i, y, 16, h, Col.Hex("2f7d3a"));
            for (int i = 16; i < w; i += 32) Fill(x + i, y, 16, h, Col.Hex("2a7234"));
            Col line = Col.Hex("f2f2f2");
            Frame(x + 8, y + 8, w - 16, h - 16, 3, line);
            Fill(x + w / 2 - 1, y + 8, 3, h - 16, line);
            for (int a = 0; a < 64; a++) { double t0 = a * Math.PI * 2 / 64, t1 = (a + 1) * Math.PI * 2 / 64; Seg(x + w / 2 + Math.Cos(t0) * 36, y + h / 2 + Math.Sin(t0) * 36, x + w / 2 + Math.Cos(t1) * 36, y + h / 2 + Math.Sin(t1) * 36, 1.4, line); }
            Frame(x + 8, y + h / 2 - 50, 50, 100, 3, line); Frame(x + w - 58, y + h / 2 - 50, 50, 100, 3, line);
        }

        /// Grid of office / ward windows, some lit warmer than others.
        public void OfficeWindows(string name)
        {
            int w = 256, h = 128, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            Fill(x, y, w, h, Col.Hex("2b3036"));
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 8; c++)
                {
                    double t = rng.NextDouble();
                    Col lit = t < 0.25 ? Col.Hex("1c2733") : t < 0.8 ? Col.Hex("e8f0ff") : Col.Hex("ffe2a8");
                    Fill(x + c * 32 + 4, y + r * 32 + 6, 24, 20, lit);
                }
        }

        public void HomeWindows(string name)
        {
            int w = 256, h = 128, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            Fill(x, y, w, h, new Col(0, 0, 0, 0));
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                {
                    int wx = x + 14 + c * 82, wy = y + 10 + r * 62;
                    Fill(wx - 4, wy - 4, 62, 48, Col.Hex("f2efe6"));
                    FillV(wx, wy, 54, 40, Col.Hex("ffcf7a"), Col.Hex("fff0c8"));
                    Fill(wx + 25, wy, 4, 40, Col.Hex("f2efe6")); Fill(wx, wy + 18, 54, 4, Col.Hex("f2efe6"));
                }
            Clip(x, y, w, h);
        }

        /// The yellow / green check pattern on an ambulance's side.
        public void Battenburg(string name)
        {
            int w = 256, h = 64, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            for (int c = 0; c < 8; c++) for (int r = 0; r < 2; r++)
                    Fill(x + c * 32, y + r * 32, 32, 32, (c + r) % 2 == 0 ? Col.Hex("1f9e45") : Col.Hex("ffd400"));
        }

        /// Blue road sign with a white H (hospital this way).
        public void HSign(string name)
        {
            int s = 128, x, y; if (!Alloc(name, s, s, out x, out y)) return;
            Fill(x, y, s, s, Col.Hex("1d4fb8"));
            Frame(x + 6, y + 6, s - 12, s - 12, 5, Col.Hex("ffffff"));
            Text("H", x + 20, y + 20, s - 40, s - 40, Col.Hex("ffffff"), 0.6, Align.Centre);
        }

        public void Mattress(string name)
        {
            int w = 64, h = 128, x, y; if (!Alloc(name, w, h, out x, out y)) return;
            Fill(x, y, w, h, Col.Hex("d8cfb8"));
            for (int j = 8; j < h; j += 16) for (int i = 8; i < w; i += 16) Disc(x + i, y + j, 2, Col.Hex("b3a88c"));
            Disc(x + 40, y + 80, 14, Col.Hex("b39a62").Lerp(Col.Hex("d8cfb8"), 0.4));
        }
    }

    /// 5 x 7 letter shapes drawn as joined strokes between "on" cells.
    public static class PixelFont
    {
        static readonly Dictionary<char, string[]> rows = new Dictionary<char, string[]>
        {
            {'A', new[]{"01110","10001","10001","11111","10001","10001","10001"}},
            {'B', new[]{"11110","10001","10001","11110","10001","10001","11110"}},
            {'C', new[]{"01110","10001","10000","10000","10000","10001","01110"}},
            {'D', new[]{"11110","10001","10001","10001","10001","10001","11110"}},
            {'E', new[]{"11111","10000","10000","11110","10000","10000","11111"}},
            {'F', new[]{"11111","10000","10000","11110","10000","10000","10000"}},
            {'G', new[]{"01110","10001","10000","10111","10001","10001","01111"}},
            {'H', new[]{"10001","10001","10001","11111","10001","10001","10001"}},
            {'I', new[]{"11111","00100","00100","00100","00100","00100","11111"}},
            {'J', new[]{"00111","00010","00010","00010","00010","10010","01100"}},
            {'K', new[]{"10001","10010","10100","11000","10100","10010","10001"}},
            {'L', new[]{"10000","10000","10000","10000","10000","10000","11111"}},
            {'M', new[]{"10001","11011","10101","10101","10001","10001","10001"}},
            {'N', new[]{"10001","10001","11001","10101","10011","10001","10001"}},
            {'O', new[]{"01110","10001","10001","10001","10001","10001","01110"}},
            {'P', new[]{"11110","10001","10001","11110","10000","10000","10000"}},
            {'Q', new[]{"01110","10001","10001","10001","10101","10010","01101"}},
            {'R', new[]{"11110","10001","10001","11110","10100","10010","10001"}},
            {'S', new[]{"01111","10000","10000","01110","00001","00001","11110"}},
            {'T', new[]{"11111","00100","00100","00100","00100","00100","00100"}},
            {'U', new[]{"10001","10001","10001","10001","10001","10001","01110"}},
            {'V', new[]{"10001","10001","10001","10001","10001","01010","00100"}},
            {'W', new[]{"10001","10001","10001","10101","10101","10101","01010"}},
            {'X', new[]{"10001","10001","01010","00100","01010","10001","10001"}},
            {'Y', new[]{"10001","10001","01010","00100","00100","00100","00100"}},
            {'Z', new[]{"11111","00001","00010","00100","01000","10000","11111"}},
            {'0', new[]{"01110","10001","10011","10101","11001","10001","01110"}},
            {'1', new[]{"00100","01100","00100","00100","00100","00100","01110"}},
            {'2', new[]{"01110","10001","00001","00010","00100","01000","11111"}},
            {'3', new[]{"11111","00010","00100","00010","00001","10001","01110"}},
            {'4', new[]{"00010","00110","01010","10010","11111","00010","00010"}},
            {'5', new[]{"11111","10000","11110","00001","00001","10001","01110"}},
            {'6', new[]{"00110","01000","10000","11110","10001","10001","01110"}},
            {'7', new[]{"11111","00001","00010","00100","01000","01000","01000"}},
            {'8', new[]{"01110","10001","10001","01110","10001","10001","01110"}},
            {'9', new[]{"01110","10001","10001","01111","00001","00010","01100"}},
            {'&', new[]{"01100","10010","10100","01000","10101","10010","01101"}},
            {'\'', new[]{"00100","00100","00000","00000","00000","00000","00000"}},
            {'-', new[]{"00000","00000","00000","11111","00000","00000","00000"}},
            {'.', new[]{"00000","00000","00000","00000","00000","00000","00100"}},
            {',', new[]{"00000","00000","00000","00000","00000","00100","01000"}},
            {'!', new[]{"00100","00100","00100","00100","00100","00000","00100"}},
            {'£', new[]{"00110","01001","01000","11100","01000","01000","11111"}},
            {'/', new[]{"00001","00010","00010","00100","01000","01000","10000"}},
            {':', new[]{"00000","00100","00000","00000","00000","00100","00000"}},
            {'?', new[]{"01110","10001","00001","00010","00100","00000","00100"}},
            {'+', new[]{"00000","00100","00100","11111","00100","00100","00000"}},
            {'%', new[]{"11001","11010","00010","00100","01000","01011","10011"}},
        };
        static readonly Dictionary<char, List<double[]>> cache = new Dictionary<char, List<double[]>>();

        public static int Columns(string s) { int n = 0; foreach (char c in s) n += Advance(c); return Math.Max(1, n - 1); }
        public static int Advance(char c) { return c == ' ' ? 4 : (c == '\'' || c == '.' || c == ',' || c == '!' || c == ':') ? 3 : 6; }

        /// Stroke segments in cell units (x across 0..5, y down 0..7), centre to centre.
        public static List<double[]> Segments(char c)
        {
            c = char.ToUpperInvariant(c);
            List<double[]> s;
            if (cache.TryGetValue(c, out s)) return s;
            string[] g;
            if (!rows.TryGetValue(c, out g)) { cache[c] = null; return null; }
            // narrow punctuation sits in the middle column: shift so it starts at 0
            int shift = Advance(c) == 3 ? 2 : 0;
            Func<int, int, bool> on = (x, y) => x >= 0 && x < 5 && y >= 0 && y < 7 && g[y][x] == '1';
            s = new List<double[]>();
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 5; x++)
                {
                    if (!on(x, y)) continue;
                    double cx = x + 0.5 - shift, cy = y + 0.5;
                    bool any = false;
                    if (on(x + 1, y)) { s.Add(new[] { cx, cy, cx + 1, cy }); any = true; }
                    if (on(x, y + 1)) { s.Add(new[] { cx, cy, cx, cy + 1 }); any = true; }
                    if (on(x + 1, y + 1) && !on(x + 1, y) && !on(x, y + 1)) { s.Add(new[] { cx, cy, cx + 1, cy + 1 }); any = true; }
                    if (on(x - 1, y + 1) && !on(x - 1, y) && !on(x, y + 1)) { s.Add(new[] { cx, cy, cx - 1, cy + 1 }); any = true; }
                    if (!any && !on(x - 1, y) && !on(x, y - 1) && !on(x - 1, y - 1) && !on(x + 1, y - 1)) s.Add(new[] { cx, cy, cx, cy });
                }
            cache[c] = s;
            return s;
        }
    }
}
