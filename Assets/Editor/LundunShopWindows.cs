// V2c: what you see through a lit shop window. Every shop used to share one picture; now there are six interiors
// (grocer, takeaway, barber / salon, boutique, café, counter service), picked from the shop's name, so a parade reads
// as different businesses at night. Pure C# (no UnityEngine). The pictures go in the unused ends of the dressing
// atlas's shelves (DressAtlas.AllocTail), so the atlas doesn't grow and nothing already in it moves.
using System;

namespace LundunGreybox
{
    public static class ShopWindowArt
    {
        public const int Kinds = 6;
        public static string Key(int kind) { return "shopwin_" + kind; }

        /// 0 grocer / off licence, 1 takeaway, 2 barber / salon, 3 boutique / phones / books, 4 café / bakery / bar, 5 counter service
        public static int Kind(string name)
        {
            string n = name.ToUpperInvariant();
            if (Any(n, "CHICKEN", "KEBAB", "PIZZA", "GRILL", "WOK", "FISH", "CHIPPY", "RIBS", "CARIBBEAN", "PLANTAIN", "SUSHI", "LUNCH", "BURGER", "NOODLE", "CURRY", "JERK", "TAKEAWAY", "PERI", "CLUCK", "PATTY", "ROTI")) return 1;
            if (Any(n, "BARBER", "FADEZ", "CUTS", "HAIR", "NAILS", "BEAUTY", "SPA", "SALON", "TATTOO", "GYM")) return 2;
            if (Any(n, "CAFE", "CAFF", "COFFEE", "BAKERY", "DELI", "WINE BAR", "ROAST", "TEA", "PATISSERIE", "BRUNCH", "DINER")) return 4;
            if (Any(n, "FONE", "PHONE", "VAPE", "BOOKS", "GALLERY", "ATELIER", "MAISON", "SAVILE", "HARTLEY", "FLORIST", "BLOOMS", "PRINT", "FASHION", "TRAINERS", "RECORD", "HIFI", "VINTAGE", "JEWEL", "TAILOR")) return 3;
            if (Any(n, "BET", "SLOTS", "PHARMACY", "BANK", "CASH", "LAUNDER", "LAUNDR", "CABS", "ESTATES", "DRY CLEAN", "TRAVEL", "POST", "SOLICITOR", "LETTINGS", "MONEY", "PAWN")) return 5;
            return 0;                                                  // food & wine, off licence, news, pound shop, market stores…
        }
        static bool Any(string n, params string[] words) { foreach (string w in words) if (n.Contains(w)) return true; return false; }

        public static void Paint(DressAtlas a)
        {
            for (int k = 0; k < Kinds; k++)
            {
                int w = 256, h = 160, x, y;
                if (!a.AllocTail(Key(k), w, h, out x, out y)) return;
                Random r = new Random(8800 + k);
                switch (k)
                {
                    case 0: Grocer(a, x, y, w, h, r); break;
                    case 1: Takeaway(a, x, y, w, h, r); break;
                    case 2: Barber(a, x, y, w, h, r); break;
                    case 3: Boutique(a, x, y, w, h, r); break;
                    case 4: Cafe(a, x, y, w, h, r); break;
                    default: Counter(a, x, y, w, h, r); break;
                }
                // the window frame and its middle bar (the same as the old picture, so the fronts still line up)
                a.Frame(x, y, w, h, 6, Col.Hex("1a1a1a"));
                a.Fill(x + w / 2 - 3, y, 6, h, Col.Hex("1a1a1a"));
            }
        }

        // someone inside, in silhouette (the window starts half a metre up, so they're seen from the thigh)
        static void Person(DressAtlas a, double cx, int y, int tall, Col c)
        {
            a.Fill((int)cx - 11, y, 22, tall - 20, c);
            a.Fill((int)cx - 15, y + tall - 44, 30, 22, c);            // shoulders
            a.Disc(cx, y + tall - 9, 9.5, c);
        }
        static readonly string[] Goods = { "d9422e", "f2b632", "3f8f4a", "2f6fc4", "e9e2d0", "f07a2a", "7a3fa0", "c9d83f" };

        static void Grocer(DressAtlas a, int x, int y, int w, int h, Random r)
        {
            a.FillV(x, y, w, h, Col.Hex("f4e7c2"), Col.Hex("fff6dc"));
            a.Fill(x, y + h - 14, w, 14, Col.Hex("fffbe8"));                                   // strip lights
            // shelves of tins and packets, three deep
            for (int s = 0; s < 3; s++)
            {
                int sy = y + 56 + s * 30;
                a.Fill(x, sy, w, 4, Col.Hex("8a7350"));
                for (int i = 6; i < w - 12; )
                {
                    int bw = 7 + r.Next(9), bh = 14 + r.Next(10);
                    a.Fill(x + i, sy + 4, bw, bh, Col.Hex(Goods[r.Next(Goods.Length)]));
                    i += bw + 1 + (r.Next(7) == 0 ? 6 : 0);
                }
            }
            // crates of fruit and veg in the window
            for (int i = 0; i < 6; i++)
            {
                int cx = x + 8 + i * 41;
                a.Fill(cx, y, 37, 34, Col.Hex("5c8a3c").Lerp(Col.Hex("7a5a34"), i % 2));
                Col fruit = Col.Hex(new[] { "e0452e", "f2a81d", "86b83a", "f2d43a", "c2302a", "e87f24" }[i]);
                for (int j = 0; j < 9; j++) a.Disc(cx + 5 + (j % 5) * 7, y + 36 + (j / 5) * 7, 4.5, fruit);
            }
            Person(a, x + 196, y + 34, 92, Col.Hex("33261d"));
        }

        static void Takeaway(DressAtlas a, int x, int y, int w, int h, Random r)
        {
            a.FillV(x, y, w, h, Col.Hex("ffe9b0"), Col.Hex("fff8e0"));
            // the lit menu boards over the counter
            for (int i = 0; i < 4; i++)
            {
                int bx = x + 10 + i * 61;
                a.Fill(bx, y + 112, 56, 38, Col.Hex(i % 2 == 0 ? "fff2a0" : "ffffff"));
                a.Fill(bx + 4, y + 130, 20, 16, Col.Hex(new[] { "c8502a", "d99a2b", "8a3b1d", "c23a2b" }[i]));   // the picture of the meal
                for (int l = 0; l < 4; l++) a.Fill(bx + 28, y + 118 + l * 7, 22, 2, Col.Hex("3a2a20"));
                for (int l = 0; l < 2; l++) a.Fill(bx + 4, y + 118 + l * 6, 18, 2, Col.Hex("c23a2b"));
            }
            a.Fill(x, y + 104, w, 5, Col.Hex("c23a2b"));
            // the counter, with the hot cabinet glowing on it
            a.Fill(x, y, w, 36, Col.Hex("b8281e"));
            a.Fill(x, y + 30, w, 6, Col.Hex("e8e2d4"));
            a.Fill(x + 140, y + 36, 92, 30, Col.Hex("ffb648"));
            a.Frame(x + 140, y + 36, 92, 30, 3, Col.Hex("70737a"));
            for (int i = 0; i < 6; i++) a.Disc(x + 152 + i * 14, y + 46, 5, Col.Hex("9a5a1e"));
            a.Fill(x + 120, y + 78, 3, 26, Col.Hex("70737a")); a.Fill(x + 96, y + 74, 50, 6, Col.Hex("70737a"));   // the extractor
            Person(a, x + 176, y + 36, 76, Col.Hex("2a2019"));                                 // behind the counter
            Person(a, x + 46, y, 106, Col.Hex("241b15"));                                      // queuing
            Person(a, x + 86, y, 98, Col.Hex("2e231b"));
        }

        static void Barber(DressAtlas a, int x, int y, int w, int h, Random r)
        {
            a.FillV(x, y, w, h, Col.Hex("e9e4da"), Col.Hex("fbf8f0"));
            a.Fill(x, y, w, 30, Col.Hex("2a2a2e"));                                            // dark floor / dado
            for (int i = 0; i < w; i += 20) a.Fill(x + i, y, 10, 30, Col.Hex("3a3a40"));
            // a row of mirrors with bulbs round them, a chair in front of each
            for (int i = 0; i < 3; i++)
            {
                int mx = x + 14 + i * 82;
                a.Fill(mx, y + 62, 62, 76, Col.Hex("bfe0ee"));
                a.FillV(mx + 4, y + 66, 54, 68, Col.Hex("9fcbe0"), Col.Hex("e4f4fa"));
                a.Frame(mx, y + 62, 62, 76, 3, Col.Hex("3a2f28"));
                for (int b = 0; b < 5; b++) a.Disc(mx + 7 + b * 12, y + 144, 3.2, Col.Hex("fffbe0"));
                a.Fill(mx - 2, y + 54, 66, 5, Col.Hex("5a4634"));                              // the shelf of bottles
                for (int b = 0; b < 5; b++) a.Fill(mx + 4 + b * 12, y + 59, 5, 9, Col.Hex(Goods[(i * 3 + b) % Goods.Length]));
                // the chair
                a.Fill(mx + 16, y + 14, 30, 34, Col.Hex("1c1c1f"));
                a.Fill(mx + 12, y + 30, 38, 6, Col.Hex("8a1c1c"));
                a.Fill(mx + 28, y, 6, 14, Col.Hex("9a9da3"));
            }
            Person(a, x + 45, y + 22, 62, Col.Hex("2a2019"));                                  // in the chair
            Person(a, x + 76, y + 10, 100, Col.Hex("1f1813"));                                 // the barber
            Person(a, x + 209, y + 22, 62, Col.Hex("30251d"));
        }

        static void Boutique(DressAtlas a, int x, int y, int w, int h, Random r)
        {
            a.FillV(x, y, w, h, Col.Hex("f2f0ea"), Col.Hex("ffffff"));
            for (int i = 0; i < 4; i++) a.Disc(x + 32 + i * 64, y + h - 12, 5, Col.Hex("fff6d0"));       // spotlights
            // a back wall of lit shelves, a few things on each
            for (int c = 0; c < 4; c++)
                for (int s = 0; s < 3; s++)
                {
                    int bx = x + 12 + c * 60, by = y + 58 + s * 30;
                    a.Fill(bx, by, 52, 26, Col.Hex("e4e0d6"));
                    a.Fill(bx, by, 52, 3, Col.Hex("b9b2a2"));
                    for (int i = 0; i < 3; i++)
                    {
                        Col g = Col.Hex(new[] { "1f1f24", "b03a48", "2f4a7a", "c9a24a", "3f6b52", "e8e2d4" }[r.Next(6)]);
                        if (r.Next(2) == 0) a.Fill(bx + 5 + i * 16, by + 4, 11, 12 + r.Next(8), g); else a.Disc(bx + 10 + i * 16, by + 11, 6, g);
                    }
                }
            // plinths in the window, a mannequin on each
            for (int i = 0; i < 3; i++)
            {
                int px = x + 26 + i * 86;
                a.Fill(px, y, 34, 12, Col.Hex("d2ccbe"));
                Col dress = Col.Hex(new[] { "1d1d22", "8a1f3a", "243a66" }[i]);
                a.Fill(px + 7, y + 12, 20, 50, dress);
                a.Fill(px + 3, y + 46, 28, 14, dress);
                a.Disc(px + 17, y + 70, 7.5, Col.Hex("cfc6b6"));
            }
            Person(a, x + 232, y, 104, Col.Hex("2b2119"));
        }

        static void Cafe(DressAtlas a, int x, int y, int w, int h, Random r)
        {
            a.FillV(x, y, w, h, Col.Hex("e9a94a"), Col.Hex("ffd98a"));
            a.Fill(x, y + 96, w, 44, Col.Hex("3a2a20"));                                       // the chalk board wall
            for (int l = 0; l < 4; l++) for (int c = 0; c < 3; c++) a.Fill(x + 14 + c * 84, y + 104 + l * 8, 40 + r.Next(26), 2, Col.Hex("efe6d2"));
            // pendant lamps
            for (int i = 0; i < 4; i++)
            {
                int lx = x + 34 + i * 62;
                a.Fill(lx, y + 128, 2, 32, Col.Hex("2a1f18"));
                a.Disc(lx + 1, y + 122, 9, Col.Hex("fff3c0"));
                a.Fill(lx - 10, y + 124, 22, 5, Col.Hex("2a1f18"));
            }
            // the counter with the coffee machine and a cake case
            a.Fill(x + 150, y, 106, 34, Col.Hex("5a3a22"));
            a.Fill(x + 150, y + 32, 106, 4, Col.Hex("e8dcc0"));
            a.Fill(x + 200, y + 36, 44, 26, Col.Hex("b9bcc2")); a.Fill(x + 204, y + 52, 36, 6, Col.Hex("3a3d42"));
            a.Fill(x + 156, y + 36, 38, 20, Col.Hex("fff0c8")); for (int i = 0; i < 4; i++) a.Disc(x + 162 + i * 9, y + 43, 3.5, Col.Hex("b8502a"));
            Person(a, x + 226, y + 36, 68, Col.Hex("2a1f18"));
            // tables by the window, people sat at them
            for (int i = 0; i < 2; i++)
            {
                int tx = x + 22 + i * 62;
                a.Fill(tx, y + 24, 44, 4, Col.Hex("3a2a20")); a.Fill(tx + 20, y, 4, 24, Col.Hex("3a2a20"));
                Person(a, tx - 4, y, 64, Col.Hex("2b2019"));
                Person(a, tx + 48, y, 60, Col.Hex("33271e"));
                a.Fill(tx + 14, y + 28, 5, 7, Col.Hex("f4f0e6")); a.Fill(tx + 26, y + 28, 5, 7, Col.Hex("f4f0e6"));
            }
        }

        static void Counter(DressAtlas a, int x, int y, int w, int h, Random r)
        {
            a.FillV(x, y, w, h, Col.Hex("dfeee6"), Col.Hex("f6fbf8"));
            a.Fill(x, y + h - 12, w, 12, Col.Hex("ffffff"));                                   // strip lights
            // a wall of screens and posters
            for (int c = 0; c < 5; c++)
            {
                int bx = x + 10 + c * 49;
                Col screen = Col.Hex(new[] { "1f6fd0", "0f8a55", "d0a21f", "1f6fd0", "b03030" }[c]);
                a.Fill(bx, y + 96, 42, 44, Col.Hex("16181c"));
                a.FillV(bx + 3, y + 99, 36, 38, screen.Scale(0.7), screen);
                for (int l = 0; l < 4; l++) a.Fill(bx + 6, y + 104 + l * 8, 12 + r.Next(18), 2, Col.Hex("f2f2f2"));
            }
            for (int c = 0; c < 4; c++) { a.Fill(x + 16 + c * 60, y + 46, 40, 42, Col.Hex("ffffff")); a.Fill(x + 20 + c * 60, y + 66, 32, 18, Col.Hex(Goods[(c * 3 + 1) % Goods.Length])); for (int l = 0; l < 3; l++) a.Fill(x + 20 + c * 60, y + 50 + l * 5, 30, 2, Col.Hex("6a6f78")); }
            // the counter and its glass screen
            a.Fill(x, y, w, 34, Col.Hex("2f4a5a"));
            a.Fill(x, y + 32, w, 4, Col.Hex("e6e9ec"));
            a.Fill(x + 150, y + 36, 3, 44, Col.Hex("9aa3ab")); a.Fill(x + 250, y + 36, 3, 44, Col.Hex("9aa3ab"));
            Person(a, x + 200, y + 36, 70, Col.Hex("26303a"));
            Person(a, x + 60, y, 104, Col.Hex("1f262d"));
        }
    }
}
