// E6: FLATPAK Home Store's inside. Pure C#: what Lundun → 28 builds into the big box at Ferndale Retail Park.
// Two floors and a full-height warehouse, laid out on the store's footprint (u along the front from its west end,
// v in from the front wall; the doors are at u 40):
//   ground floor   the entrance lobby and the exit lobby (split by a barrier); the escalator and stairs up, lift A;
//                  the checkouts and self-checkouts; the bistro (hot dogs, soft ice) by the way out; a bargain corner and
//                  the delivery & returns desk; the market hall (cookshop, textiles, rugs, lighting, decoration, plants,
//                  storage: tables, bins, shelves and rug racks in rows, with walkways between); the down escalator and
//                  lift B at its back corner
//   self serve     the warehouse (west, full height): eight numbered aisles of pallet racking with bay numbers,
//                  flat-pack boxes at pick level and wrapped pallets above, a large-items area, forklifts and flat
//                  trolleys; in from the market hall, out to the tills
//   first floor    the showroom: one winding, arrowed path through room sets (living rooms, workspaces, dining,
//                  kitchens, bedrooms, bathrooms, children's, storage), each a little furnished room with a price tag;
//                  shortcuts through the rows; the café (a food counter with trays, tills, tables) at the end, and the
//                  escalator down to the market hall
// Layers, as the supermarkets (E5): a cheap shell that's always there (floors, walls, the slab, escalators, stairs,
// lift shafts), and detail per floor, switched on only near you and on the floor you're on (LundunShopInterior).
// Things that break, things you can take, staff and shoppers (who ride the escalators) as in E5.
// Materials: Home_Atlas (everything painted), Home_Concrete (warehouse and showroom floor), Home_Steps (the moving
// escalator steps), plus Shop_Floor, Shop_Ceiling, Shop_Glass and the LED lamp heads from E5.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    /// E6: a lift: its cabin centre, which way its doors face (out), the floors it stops at.
    public class ShopLift { public double x, z, ox, oz; public double[] floors; public string[] names; }

    public static partial class Generator
    {
        public const string HomeAtlasMat = "Home_Atlas", HomeConcreteMat = "Home_Concrete", HomeStepsMat = "Home_Steps";
        public const double HomeUpperY = 7.0;                         // the first floor
        const double HY0 = 0.16, HY1 = HomeUpperY, HC0 = 6.5, HC1 = 13.0;   // floors; the slab's underside; the ceiling up top
        const double EscLand = 2.0;                                   // escalator landings (flat), each end
        static readonly double EscRun = (HY1 - HY0) * Math.Sqrt(3.0); // 30°: 11.85 m on the slope

        // ================================================================== the atlas (painted once)
        static DressAtlas homeAtlas;
        static readonly HashSet<string> homeMissing = new HashSet<string>();
        /// A patch of the FLATPAK atlas (a name that isn't there is noted in the build log and drawn white).
        static UVRect HA(string name)
        {
            UVRect r;
            if (homeAtlas.rects.TryGetValue(name, out r)) return r;
            homeMissing.Add(name);
            return homeAtlas.rects["s_white"];
        }

        static readonly string[][] HomeSwatches =
        {
            new[] { "white", "f4f3ef" }, new[] { "offwhite", "ece8df" }, new[] { "light", "d6d6d2" }, new[] { "mid", "8f9295" }, new[] { "dark", "3b3e42" },
            new[] { "charcoal", "2a2c2e" }, new[] { "black", "16171a" }, new[] { "steel", "a9aeb2" }, new[] { "chrome", "dcdfe1" }, new[] { "orange", "ff7a1a" },
            new[] { "yellow", "ffcc00" }, new[] { "red", "c8102e" }, new[] { "cream", "efe6d2" }, new[] { "concrete", "9a9996" }, new[] { "path", "e4dfd4" },
            new[] { "oak", "c9a173" }, new[] { "birch", "e2c9a0" }, new[] { "walnut", "6b4a31" }, new[] { "pine", "d9b77a" }, new[] { "blackwood", "2b2420" },
            new[] { "f_grey", "7d8288" }, new[] { "f_blue", "3d5a80" }, new[] { "f_green", "5f7f5a" }, new[] { "f_rust", "b0532c" }, new[] { "f_beige", "cdbb9c" },
            new[] { "f_pink", "d99a9a" }, new[] { "f_mustard", "d1a13a" }, new[] { "f_navy", "25324a" }, new[] { "f_teal", "2f7f86" }, new[] { "f_cream", "e9e0cc" },
            new[] { "w_sage", "a9b8a0" }, new[] { "w_blue", "9fb4c7" }, new[] { "w_terra", "c98a6b" }, new[] { "w_cream", "efe4cf" }, new[] { "w_grey", "b9bcbf" },
            new[] { "w_pink", "e3bfb8" }, new[] { "w_navy", "3c4b66" }, new[] { "w_olive", "8a8a5a" }, new[] { "w_yellow", "e9d37a" }, new[] { "w_white", "f2f1ec" },
            new[] { "screen", "1b2a3a" }, new[] { "screen_on", "8fd0ff" }, new[] { "mirror", "c9d6dc" }, new[] { "porcelain", "f7f7f5" }, new[] { "mattress", "f0efe8" },
            new[] { "plant", "3f7a3a" }, new[] { "plant2", "5a9a48" }, new[] { "pot", "c46a42" }, new[] { "cardboard", "b88b58" }, new[] { "wrap", "d7d3c8" },
            new[] { "glow", "fff1c2" }, new[] { "rack_blue", "1f4e9a" }, new[] { "rack_orange", "e86a1a" }, new[] { "lane", "ffd200" }, new[] { "item_glow", "fff4b0" },
            new[] { "ceramic", "ece8df" }, new[] { "glassgreen", "a9c9b8" }, new[] { "forklift", "f2b600" }, new[] { "rubber", "1c1c1e" }, new[] { "belt", "1c1c1e" },
            new[] { "hotdog", "c4703a" }, new[] { "bun", "e8c27a" }, new[] { "ice", "f8f2e2" }, new[] { "tray", "c8102e" }, new[] { "bag", "1d4f91" },
            new[] { "wax", "f2e6c9" }, new[] { "steelblade", "c7ccd0" }, new[] { "brass", "c8a24a" }, new[] { "door", "5d6268" },
        };

        /// The FLATPAK atlas: swatches, floors, rugs, bedding, fronts, product strips, table tops, the warehouse's boxes,
        /// signs, aisle and bay numbers, price tags (2048 × 4096).
        public static DressAtlas HomeAtlas()
        {
            if (homeAtlas != null) return homeAtlas;
            DressAtlas a = new DressAtlas(2048, 4096, 616);
            Random r = new Random(6161);
            // (allocated tallest first: the atlas packs in shelves)
            Directory(a, "m_dir");
            BooksTile(a, "k_books", r);
            for (int i = 0; i < 4; i++) RackFace(a, "k_flat" + i, false, r);
            for (int i = 0; i < 2; i++) RackFace(a, "k_pal" + i, true, r);
            // floors, rugs, bedding (256 × 256, tiled per room), the warehouse's shutters, the café menu, wardrobe doors
            HomeFloorTile(a, "f_oak", 0, r); HomeFloorTile(a, "f_carpet", 1, r); HomeFloorTile(a, "f_tile", 2, r); HomeFloorTile(a, "f_check", 3, r);
            HomeFloorTile(a, "f_kids", 4, r); HomeFloorTile(a, "f_lino", 5, r); HomeFloorTile(a, "k_splash", 6, r);
            for (int i = 0; i < 4; i++) RugTile(a, "r_" + i, i, r);
            for (int i = 0; i < 2; i++) DuvetTile(a, "d_" + i, i, r);
            ShutterTile(a, "k_shutter");
            MenuBoard(a, "m_menu");
            FrontTile(a, "k_ward", 2);
            for (int i = 0; i < 4; i++) ArtTile(a, "k_art" + i, i, r);
            // fronts, table and bin tops seen from above, aisle numbers, the floor arrow (128 high)
            FrontTile(a, "k_cab", 0); FrontTile(a, "k_wall", 1); FrontTile(a, "k_drawers", 3);
            string[] tops = { "mugs", "plates", "bowls", "glasses", "cushions", "towels", "toys", "pillows", "candles", "plants", "frames", "rugroll",
                              "hot", "salad", "cakes", "hotdogs", "pans", "boxes" };
            foreach (string t in tops) TopTile(a, "t_" + t, t, r);
            for (int i = 1; i <= 8; i++) AisleBoard(a, "n_" + i, i);
            ArrowTile(a, "x_arrow");
            // product strips for the market hall's shelves (4 m × 0.36 m, as E5's)
            foreach (string k in new[] { "COOK", "GLASS", "TOWEL", "BOX", "FRAME", "CANDLE", "LAMPBOX", "TOY" }) for (int i = 0; i < 2; i++) HomeStrip(a, "p_" + k + i, k, r);
            // price tags (product, what it is, price)
            PriceTags(a, r);
            // signs (512 × 64): sections, ways, places
            Col ch = Col.Hex("2a2c2e"), wh = Col.Hex("ffffff"), og = Col.Hex("ff7a1a"), yl = Col.Hex("ffcc00");
            string[] sections = { "LIVING ROOMS", "WORKSPACES", "DINING", "KITCHENS", "BEDROOMS", "BATHROOMS", "CHILDREN'S", "STORAGE",
                                  "COOKSHOP", "TEXTILES", "RUGS", "LIGHTING", "DECORATION", "PLANTS", "HOME STORAGE", "SELF SERVE", "LARGE ITEMS",
                                  "CAFE", "BISTRO", "TILLS", "WAY OUT", "SHOWROOM", "MARKET HALL", "LIFT", "BARGAIN CORNER", "DELIVERY & RETURNS",
                                  "TOILETS", "SCAN & PAY", "SHORTCUT", "INFORMATION" };
            foreach (string sn in sections) a.Sign("g_" + sn, 512, 64, sn, ch, wh, og, 0.45);
            a.Sign("g_WELCOME", 512, 64, "WELCOME TO FLATPAK", og, wh, ch, 0.48);
            a.Sign("g_FLATPAK", 512, 64, "FLATPAK", ch, og, og, 0.55);
            a.Sign("g_SHOWROOM_UP", 512, 64, "SHOWROOM - FIRST FLOOR", og, wh, ch, 0.45);
            a.Sign("g_MARKET_DOWN", 512, 64, "MARKET HALL - DOWN", og, wh, ch, 0.45);
            a.Sign("g_HOTDOG", 512, 64, "HOT DOG + DRINK  £1.50", yl, ch, og, 0.45);
            // bay numbers (white plates), swatches
            for (int i = 1; i <= 26; i++) BayPlate(a, "y_" + i, i);
            foreach (string[] sw in HomeSwatches) a.Swatch("s_" + sw[0], Col.Hex(sw[1]));
            homeAtlas = a;
            return a;
        }

        static Col HC(string hex) { return Col.Hex(hex); }
        static Col HC(string hex, int alpha) { return Col.Hex(hex, alpha); }

        /// A strip of homeware on a shelf (680 × 80 px = 4 m × 0.36 m; drawn at 120 high and scaled).
        static void HomeStrip(DressAtlas a, string name, string kind, Random r)
        {
            const int SW = 680, SH = 80;
            int x0, y0; a.Alloc(name, SW, SH, out x0, out y0);
            a.Fill(x0, y0, SW, SH, HC("3a3530"));
            string[] cols; int shape, wMin, wMax, hMin, hMax;            // 0 boxes, 1 pots, 2 glasses, 3 folded, 4 frames, 5 candles
            switch (kind)
            {
                case "COOK": cols = new[] { "2a2c2e", "c8102e", "8f9295", "1d4f91", "dcdfe1", "e86a1a" }; shape = 1; wMin = 40; wMax = 74; hMin = 27; hMax = 54; break;
                case "GLASS": cols = new[] { "a9c9b8", "c9d6dc", "e8eef0", "9fb4c7" }; shape = 2; wMin = 15; wMax = 23; hMin = 27; hMax = 60; break;
                case "TOWEL": cols = new[] { "f4f3ef", "3d5a80", "a9b8a0", "d99a9a", "d1a13a", "2f7f86", "7d8288" }; shape = 3; wMin = 47; wMax = 67; hMin = 12; hMax = 17; break;
                case "BOX": cols = new[] { "f4f3ef", "2a2c2e", "c9a173", "9fb4c7", "a9b8a0", "e86a1a" }; shape = 0; wMin = 47; wMax = 80; hMin = 34; hMax = 67; break;
                case "FRAME": cols = new[] { "16171a", "f4f3ef", "c9a173", "6b4a31", "c8a24a" }; shape = 4; wMin = 40; wMax = 74; hMin = 47; hMax = 75; break;
                case "CANDLE": cols = new[] { "f2e6c9", "c8102e", "a9b8a0", "f4f3ef", "d1a13a", "e3bfb8" }; shape = 5; wMin = 15; wMax = 30; hMin = 20; hMax = 60; break;
                case "LAMPBOX": cols = new[] { "f4f3ef", "e9e0cc", "2a2c2e" }; shape = 0; wMin = 54; wMax = 87; hMin = 47; hMax = 75; break;
                default: cols = new[] { "c8102e", "1d4f91", "ffcc00", "3f7a3a", "e86a1a", "d99a9a" }; shape = 0; wMin = 34; wMax = 60; hMin = 27; hMax = 60; break;   // toys
            }
            int x = x0 + 2;
            while (x < x0 + SW - 3)
            {
                int w = wMin + r.Next(wMax - wMin + 1), h = hMin + r.Next(hMax - hMin + 1);
                int reps = 1 + r.Next(3);
                Col c = HC(cols[r.Next(cols.Length)]), c2 = HC(cols[r.Next(cols.Length)]);
                for (int k = 0; k < reps && x < x0 + SW - 3; k++)
                {
                    int ww = Math.Min(w, x0 + SW - 3 - x);
                    switch (shape)
                    {
                        case 1:      // pans and pots: a body, a lid, a handle
                            a.Fill(x + 1, y0 + 2, ww - 4, h - 7, c); a.Fill(x + 3, y0 + h - 5, ww - 7, 3, c.Scale(1.2));
                            a.Fill(x + ww / 2 - 3, y0 + h - 2, 6, 3, HC("16171a")); break;
                        case 2:      // glasses and jugs: pale, with a bright rim
                            a.Fill(x + 1, y0 + 2, ww - 3, h, c); a.Fill(x + 1, y0 + h, ww - 3, 2, HC("ffffff")); a.Fill(x + 3, y0 + 4, 2, h - 5, HC("ffffff", 120)); break;
                        case 3:      // folded towels, stacked
                            for (int t = 0, yy = y0 + 2; yy + h < y0 + SH - 2 && t < 4; t++, yy += h + 1) { Col cc = t % 2 == 0 ? c : c2; a.Fill(x + 1, yy, ww - 3, h, cc); a.Fill(x + 1, yy + h - 2, ww - 3, 1, cc.Scale(0.8)); }
                            break;
                        case 4:      // frames: a moulding, a mount, a picture
                            a.Fill(x + 1, y0 + 2, ww - 3, h, c); a.Fill(x + 5, y0 + 6, ww - 10, h - 7, HC("f4f3ef"));
                            a.Fill(x + 8, y0 + 9, ww - 16, h - 13, HC(new[] { "9fb4c7", "a9b8a0", "c98a6b", "e9d37a" }[r.Next(4)])); break;
                        case 5:      // candles in jars and pillars
                            a.Fill(x + 2, y0 + 2, ww - 4, h, c); a.Fill(x + ww / 2 - 1, y0 + h + 2, 2, 3, HC("16171a")); break;
                        default:     // boxes with a band and a little picture
                            a.Fill(x + 1, y0 + 2, ww - 2, h, c); a.Fill(x + 1, y0 + 2 + h * 2 / 3, ww - 2, Math.Max(2, h / 8), c2); a.Frame(x + 1, y0 + 2, ww - 2, h, 1, c.Scale(0.65));
                            a.Disc(x + ww * 0.5, y0 + 2 + h * 0.35, Math.Min(ww, h) * 0.16, c2.Scale(1.1)); break;
                    }
                    x += ww;
                }
                x += 1 + r.Next(3);
            }
        }

        /// 256 × 256 floors for the room sets (and the kitchens' splashback).
        static void HomeFloorTile(DressAtlas a, string name, int kind, Random r)
        {
            int x0, y0; a.Alloc(name, 256, 256, out x0, out y0);
            switch (kind)
            {
                case 0:      // oak boards
                    for (int row = 0; row < 8; row++)
                    {
                        int off = r.Next(128);
                        for (int bx = -off; bx < 256; bx += 128)
                        {
                            Col c = HC("c9a173").Scale(0.88 + r.NextDouble() * 0.2);
                            int s0 = Math.Max(0, bx), s1 = Math.Min(256, bx + 127);
                            a.Fill(x0 + s0, y0 + row * 32, s1 - s0, 31, c);
                        }
                        a.Fill(x0, y0 + row * 32 + 31, 256, 1, HC("8a6a42"));
                    }
                    break;
                case 1:      // carpet: fine noise
                    a.Fill(x0, y0, 256, 256, HC("8e8a84"));
                    for (int i = 0; i < 2600; i++) a.Fill(x0 + r.Next(255), y0 + r.Next(255), 2, 2, HC("8e8a84").Scale(0.85 + r.NextDouble() * 0.3));
                    break;
                case 2: case 6:      // white tiles (bathroom floors; the splashback a little smaller)
                    a.Fill(x0, y0, 256, 256, HC("b9bcbf"));
                    int n = kind == 2 ? 4 : 8, cell = 256 / n;
                    for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) a.Fill(x0 + i * cell + 2, y0 + j * cell + 2, cell - 4, cell - 4, HC(kind == 2 ? "e8e9e6" : "f4f3ef").Scale(0.97 + r.NextDouble() * 0.05));
                    break;
                case 3:      // kitchen check
                    for (int i = 0; i < 8; i++) for (int j = 0; j < 8; j++) a.Fill(x0 + i * 32, y0 + j * 32, 32, 32, (i + j) % 2 == 0 ? HC("2a2c2e") : HC("ece8df"));
                    break;
                case 4:      // kids' play mat: soft squares
                    string[] kc = { "e86a1a", "3d5a80", "5f7f5a", "d1a13a", "d99a9a", "2f7f86" };
                    for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { a.Fill(x0 + i * 64, y0 + j * 64, 64, 64, HC(kc[(i * 3 + j * 5) % kc.Length])); a.Frame(x0 + i * 64, y0 + j * 64, 64, 64, 2, HC("ffffff", 160)); }
                    break;
                default:     // grey lino (workspaces)
                    a.Fill(x0, y0, 256, 256, HC("9a9c9e"));
                    for (int i = 0; i < 900; i++) a.Fill(x0 + r.Next(254), y0 + r.Next(254), 3, 2, HC("9a9c9e").Scale(0.92 + r.NextDouble() * 0.16));
                    break;
            }
        }

        static void RugTile(DressAtlas a, string name, int kind, Random r)
        {
            int x0, y0; a.Alloc(name, 256, 256, out x0, out y0);
            string[][] pal = { new[] { "e9e0cc", "2a2c2e", "b0532c" }, new[] { "3d5a80", "e9e0cc", "d1a13a" }, new[] { "a9b8a0", "5f7f5a", "f4f3ef" }, new[] { "d99a9a", "f4f3ef", "25324a" } };
            string[] p = pal[kind];
            a.Fill(x0, y0, 256, 256, HC(p[0]));
            a.Frame(x0 + 6, y0 + 6, 244, 244, 10, HC(p[1]));
            if (kind == 0) for (int i = 0; i < 6; i++) a.Fill(x0 + 30 + i * 34, y0 + 30, 14, 196, HC(p[i % 2 == 0 ? 1 : 2]));
            else if (kind == 1) for (int i = 0; i < 5; i++) for (int j = 0; j < 5; j++) { double cx = x0 + 40 + i * 44, cy = y0 + 40 + j * 44; for (int d = 0; d < 16; d++) a.Fill((int)cx - d, (int)cy - 16 + d, 2 * d + 1, 1, HC(p[(i + j) % 2 == 0 ? 1 : 2])); for (int d = 0; d < 16; d++) a.Fill((int)cx - (15 - d), (int)cy + d, 2 * (15 - d) + 1, 1, HC(p[(i + j) % 2 == 0 ? 1 : 2])); }
            else if (kind == 2) for (int i = 0; i < 300; i++) a.Disc(x0 + 20 + r.NextDouble() * 216, y0 + 20 + r.NextDouble() * 216, 3 + r.NextDouble() * 4, HC(p[1 + r.Next(2)]));
            else { a.Disc(x0 + 128, y0 + 128, 80, HC(p[1])); a.Disc(x0 + 128, y0 + 128, 50, HC(p[2])); a.Disc(x0 + 128, y0 + 128, 22, HC(p[0])); }
        }

        static void DuvetTile(DressAtlas a, string name, int kind, Random r)
        {
            int x0, y0; a.Alloc(name, 256, 256, out x0, out y0);
            if (kind == 0) { a.Fill(x0, y0, 256, 256, HC("f4f3ef")); for (int i = 0; i < 16; i++) a.Fill(x0, y0 + i * 16, 256, 5, HC("9fb4c7")); }
            else { a.Fill(x0, y0, 256, 256, HC("a9b8a0")); for (int i = 0; i < 90; i++) a.Disc(x0 + r.NextDouble() * 256, y0 + r.NextDouble() * 256, 5, HC("f4f3ef")); }
        }

        /// Unit fronts: a kitchen base run (two doors), wall cupboards, a wardrobe (two tall doors), drawers.
        static void FrontTile(DressAtlas a, string name, int kind)
        {
            int w = kind == 2 ? 128 : 256, h = kind == 2 ? 256 : 128;
            int x0, y0; a.Alloc(name, w, h, out x0, out y0);
            Col body = kind == 1 ? HC("f4f3ef") : kind == 2 ? HC("ece8df") : kind == 3 ? HC("c9a173") : HC("3d5a80");
            a.Fill(x0, y0, w, h, body.Scale(0.75));
            if (kind == 3)
            {
                for (int i = 0; i < 3; i++) { a.Fill(x0 + 3, y0 + 3 + i * 42, w - 6, 38, body); a.Fill(x0 + w / 2 - 20, y0 + 26 + i * 42, 40, 4, HC("2a2c2e")); }
                return;
            }
            int doors = 2, dw = (w - 6) / doors;
            for (int i = 0; i < doors; i++)
            {
                int dx = x0 + 3 + i * dw;
                a.Fill(dx, y0 + 3, dw - 3, h - 6, body);
                int hx = i == 0 ? dx + dw - 14 : dx + 8;
                if (kind == 2) a.Fill(hx, y0 + h / 2 - 20, 4, 40, HC("2a2c2e"));
                else a.Fill(hx, kind == 1 ? y0 + 10 : y0 + h - 30, 4, 20, HC("dcdfe1"));
            }
        }

        static void BooksTile(DressAtlas a, string name, Random r)
        {
            int x0, y0; a.Alloc(name, 128, 320, out x0, out y0);       // 0.8 m × 2.0 m: five shelves
            a.Fill(x0, y0, 128, 320, HC("f4f3ef"));
            for (int s = 0; s < 5; s++)
            {
                int sy = y0 + 4 + s * 63;
                a.Fill(x0 + 4, sy, 120, 56, HC("ece8df").Scale(0.8));
                int x = x0 + 6;
                while (x < x0 + 118)
                {
                    int bw = 4 + r.Next(7), bh = 30 + r.Next(24);
                    if (r.NextDouble() < 0.12) { a.Fill(x, sy, 22, 18, HC("c9a173")); x += 26; continue; }      // a box, a gap
                    a.Fill(x, sy, Math.Min(bw, x0 + 122 - x), bh, HC(new[] { "c8102e", "1d4f91", "3f7a3a", "d1a13a", "2a2c2e", "e9e0cc", "b0532c" }[r.Next(7)]));
                    x += bw + 1;
                }
            }
        }

        static void ArtTile(DressAtlas a, string name, int kind, Random r)
        {
            int x0, y0; a.Alloc(name, 128, 160, out x0, out y0);
            a.Fill(x0, y0, 128, 160, HC("16171a"));
            a.Fill(x0 + 6, y0 + 6, 116, 148, HC("f4f3ef"));
            string[][] pal = { new[] { "b0532c", "d1a13a", "25324a" }, new[] { "3d5a80", "9fb4c7", "f2e6c9" }, new[] { "5f7f5a", "a9b8a0", "2a2c2e" }, new[] { "d99a9a", "e86a1a", "f4f3ef" } };
            string[] p = pal[kind];
            if (kind % 2 == 0) { a.Disc(x0 + 64, y0 + 90, 34, HC(p[0])); a.Fill(x0 + 18, y0 + 20, 92, 30, HC(p[1])); a.Seg(x0 + 20, y0 + 120, x0 + 108, y0 + 60, 4, HC(p[2])); }
            else for (int i = 0; i < 5; i++) a.Fill(x0 + 16 + i * 4, y0 + 16 + i * 26, 96 - i * 8, 18, HC(p[i % 3]));
        }

        /// Things on a table or in a bin, seen from above (256 × 128).
        static void TopTile(DressAtlas a, string name, string kind, Random r)
        {
            int x0, y0; a.Alloc(name, 256, 128, out x0, out y0);
            Func<string[], Col> pick = cs => HC(cs[r.Next(cs.Length)]);
            switch (kind)
            {
                case "mugs": case "bowls": case "plates": case "glasses": case "candles":
                    {
                        a.Fill(x0, y0, 256, 128, HC("e2c9a0"));
                        string[] cs = kind == "glasses" ? new[] { "c9d6dc", "a9c9b8", "e8eef0" } : kind == "candles" ? new[] { "f2e6c9", "c8102e", "a9b8a0", "f4f3ef" } : new[] { "f4f3ef", "3d5a80", "2a2c2e", "d99a9a", "a9b8a0", "d1a13a" };
                        double rad = kind == "plates" ? 26 : kind == "bowls" ? 20 : 12;
                        for (double cy = y0 + rad + 3; cy < y0 + 128 - rad; cy += rad * 2 + 4)
                            for (double cx = x0 + rad + 3; cx < x0 + 256 - rad; cx += rad * 2 + 4)
                            {
                                Col c = pick(cs); a.Disc(cx, cy, rad, c); a.Disc(cx, cy, rad * 0.7, c.Scale(kind == "plates" ? 0.92 : 0.6));
                                if (kind == "mugs") a.Fill((int)(cx + rad - 2), (int)cy - 3, 6, 6, c);
                                if (kind == "candles") a.Disc(cx, cy, 2, HC("16171a"));
                            }
                        break;
                    }
                case "cushions": case "pillows": case "towels": case "toys":
                    {
                        a.Fill(x0, y0, 256, 128, HC("6a6058"));
                        string[] cs = kind == "toys" ? new[] { "c8102e", "1d4f91", "ffcc00", "3f7a3a", "e86a1a", "b88b58" } : new[] { "f4f3ef", "3d5a80", "a9b8a0", "d99a9a", "d1a13a", "2f7f86", "7d8288", "b0532c" };
                        for (int i = 0; i < (kind == "toys" ? 60 : 26); i++)
                        {
                            int w = kind == "toys" ? 14 + r.Next(16) : kind == "towels" ? 44 : 34 + r.Next(14), h = kind == "toys" ? 14 + r.Next(16) : kind == "towels" ? 26 : 30 + r.Next(12);
                            int x = x0 + r.Next(256 - w), y = y0 + r.Next(128 - h);
                            Col c = pick(cs); a.Fill(x, y, w, h, c); a.Frame(x, y, w, h, 2, c.Scale(0.78));
                            if (kind == "toys") a.Disc(x + w / 2.0, y + h / 2.0, Math.Min(w, h) * 0.2, HC("16171a"));
                        }
                        break;
                    }
                case "plants":
                    a.Fill(x0, y0, 256, 128, HC("4a3a2a"));
                    for (int i = 0; i < 40; i++) { double cx = x0 + 10 + r.NextDouble() * 236, cy = y0 + 10 + r.NextDouble() * 108; a.Disc(cx, cy, 10 + r.NextDouble() * 9, HC(new[] { "3f7a3a", "5a9a48", "2f5f2a", "6ab04a" }[r.Next(4)])); }
                    break;
                case "frames":
                    a.Fill(x0, y0, 256, 128, HC("e2c9a0"));
                    for (int i = 0; i < 8; i++) { int x = x0 + 6 + (i % 4) * 62, y = y0 + 6 + (i / 4) * 60; a.Fill(x, y, 56, 54, pick(new[] { "16171a", "f4f3ef", "c9a173", "c8a24a" })); a.Fill(x + 6, y + 6, 44, 42, HC("f4f3ef")); }
                    break;
                case "rugroll":
                    a.Fill(x0, y0, 256, 128, HC("6a6058"));
                    for (int i = 0; i < 6; i++) { int y = y0 + 4 + i * 20; Col c = pick(new[] { "e9e0cc", "3d5a80", "b0532c", "a9b8a0", "d99a9a", "2a2c2e" }); a.FillV(x0 + 4, y, 248, 18, c.Scale(0.7), c); }
                    break;
                case "hot": case "salad": case "cakes": case "hotdogs":
                    a.Fill(x0, y0, 256, 128, HC("a9aeb2"));
                    for (int i = 0; i < 4; i++)
                    {
                        int x = x0 + 4 + i * 63;
                        a.Fill(x, y0 + 6, 58, 116, HC("dcdfe1"));
                        string[] cs = kind == "hot" ? new[] { "8a4a2a", "c4703a", "e8c27a", "6a3a1a" } : kind == "salad" ? new[] { "5a9a48", "c8102e", "f2d23a", "f4f3ef" } : kind == "cakes" ? new[] { "6a3a1a", "f4f3ef", "d99a9a", "e8c27a" } : new[] { "c4703a", "e8c27a" };
                        Col c = HC(cs[i % cs.Length]);
                        for (int k = 0; k < 18; k++) a.Disc(x + 8 + r.NextDouble() * 42, y0 + 12 + r.NextDouble() * 104, kind == "hotdogs" ? 6 : 7, k % 3 == 0 ? HC(cs[(i + 1) % cs.Length]) : c);
                    }
                    break;
                case "pans":
                    a.Fill(x0, y0, 256, 128, HC("e2c9a0"));
                    for (int i = 0; i < 6; i++) { double cx = x0 + 28 + (i % 3) * 84, cy = y0 + 34 + (i / 3) * 60; a.Disc(cx, cy, 26, HC("2a2c2e")); a.Disc(cx, cy, 21, HC("3b3e42")); a.Fill((int)cx + 22, (int)cy - 3, 30, 6, HC("16171a")); }
                    break;
                default:         // boxes
                    a.Fill(x0, y0, 256, 128, HC("6a6058"));
                    for (int i = 0; i < 8; i++) { int x = x0 + 4 + (i % 4) * 63, y = y0 + 4 + (i / 4) * 62; Col c = pick(new[] { "f4f3ef", "2a2c2e", "c9a173", "9fb4c7", "a9b8a0" }); a.Fill(x, y, 58, 58, c); a.Frame(x, y, 58, 58, 3, c.Scale(0.75)); }
                    break;
            }
        }

        /// One bay of pallet racking, front on (512 × 416 = 2.7 m × 2.2 m): the uprights at the sides, the orange beam
        /// along the bottom, and flat-pack boxes (labelled, stacked flat) or wrapped pallets on it.
        static void RackFace(DressAtlas a, string name, bool pallets, Random r)
        {
            const int W = 384, H = 312;                         // 2.7 m × 2.2 m
            int x0, y0; a.Alloc(name, W, H, out x0, out y0);
            a.Fill(x0, y0, W, H, HC("1e1e20"));
            if (pallets)
            {
                for (int p = 0; p < 2; p++)
                {
                    int px = x0 + 18 + p * 178, pw = 170;
                    a.Fill(px, y0 + 16, pw, 16, HC("b08a5a")); for (int k = 0; k < 3; k++) a.Fill(px + 6 + k * 75, y0 + 16, 14, 16, HC("6a4a2a"));
                    int h = 170 + r.Next(100);
                    for (int yy = y0 + 34; yy < y0 + 34 + h; yy += 26 + r.Next(12))
                        for (int xx = px; xx < px + pw; xx += 52 + r.Next(30)) a.Fill(xx + 1, yy, Math.Min(50, px + pw - xx - 2), 24, HC("b88b58").Scale(0.85 + r.NextDouble() * 0.25));
                    a.Fill(px, y0 + 34, pw, h, HC("d7d3c8", 110));                    // the shrink-wrap
                    for (int k = 0; k < 5; k++) a.Fill(px, y0 + 34 + k * (h / 5), pw, 2, HC("ffffff", 90));
                    a.Fill(px + pw / 2 - 22, y0 + 34 + h / 2, 44, 30, HC("ffffff"));         // the pallet label
                    a.Fill(px + pw / 2 - 19, y0 + 37 + h / 2, 38, 5, HC("16171a"));
                }
            }
            else
            {
                int y = y0 + 16;
                while (y < y0 + H - 18)
                {
                    int h = 14 + r.Next(22), x = x0 + 14;
                    bool tall = r.NextDouble() < 0.25;
                    if (tall) h = 68 + r.Next(60);                          // boxes stood on end
                    if (y + h > y0 + H - 14) break;
                    while (x < x0 + W - 14)
                    {
                        int w = tall ? 20 + r.Next(22) : 90 + r.Next(150);
                        w = Math.Min(w, x0 + W - 14 - x);
                        Col c = HC("b88b58").Scale(0.82 + r.NextDouble() * 0.3);
                        a.Fill(x, y, w - 2, h - 2, c);
                        if (w > 40 && h > 12) { a.Fill(x + 5, y + h / 2 - 3, Math.Min(34, w - 10), 5, HC("2a2c2e")); a.Fill(x + w - 20, y + 2, 14, Math.Min(8, h - 5), HC("ff7a1a")); }
                        x += w;
                    }
                    y += h;
                }
            }
            // the beam and the uprights (with their holes)
            a.Fill(x0, y0 + 2, W, 13, HC("e86a1a")); a.Fill(x0, y0 + 2, W, 2, HC("a84a10"));
            foreach (int ux in new[] { x0, x0 + W - 12 })
            {
                a.Fill(ux, y0, 12, H, HC("1f4e9a"));
                for (int k = 0; k < 15; k++) a.Fill(ux + 4, y0 + 8 + k * 20, 3, 5, HC("0e2a5a"));
            }
        }

        static void ShutterTile(DressAtlas a, string name)
        {
            int x0, y0; a.Alloc(name, 256, 256, out x0, out y0);
            a.Fill(x0, y0, 256, 256, HC("a9aeb2"));
            for (int k = 0; k < 32; k++) a.Fill(x0, y0 + k * 8, 256, 2, HC("6a6e72"));
            a.Fill(x0, y0, 256, 22, HC("ffcc00")); for (int k = 0; k < 8; k++) a.Fill(x0 + k * 32, y0, 16, 22, HC("16171a"));
        }

        static void AisleBoard(DressAtlas a, string name, int n)
        {
            int x0, y0; a.Alloc(name, 256, 128, out x0, out y0);
            a.Fill(x0, y0, 256, 128, HC("ffcc00"));
            a.Frame(x0, y0, 256, 128, 6, HC("16171a"));
            a.Text("AISLE", x0 + 12, y0 + 74, 232, 40, HC("16171a"), 0.5, DressAtlas.Align.Centre);
            a.Text(n.ToString(), x0 + 12, y0 + 10, 232, 66, HC("16171a"), 0.6, DressAtlas.Align.Centre);
        }

        static void BayPlate(DressAtlas a, string name, int n)
        {
            int x0, y0; a.Alloc(name, 64, 48, out x0, out y0);
            a.Fill(x0, y0, 64, 48, HC("f4f3ef")); a.Frame(x0, y0, 64, 48, 2, HC("16171a"));
            a.Text(n.ToString(), x0 + 4, y0 + 4, 56, 40, HC("16171a"), 0.55, DressAtlas.Align.Centre);
        }

        /// Product name, what it is, a price: the yellow tags on each room set (and the market hall).
        static readonly string[] HomeNames = { "KNUTT", "PLUMPA", "SNOOZBERG", "WOBBLA", "GRUNKA", "TOFFEL", "BLOMP", "SKRONK", "FLUFFA", "DUNKA", "SNORRA", "KLONK",
                                               "BROMMA", "TWIDDA", "HUGGLA", "SPLONK", "NOOKA", "DRIBBLA", "FJUMP", "GLOBBA", "PLINKA", "SNUGGA", "BOFFA", "TRUNDLA" };
        static readonly string[] HomeWhat = { "SOFA", "ARMCHAIR", "BED FRAME", "WARDROBE", "DESK", "DINING TABLE", "KITCHEN", "BOOKCASE", "BUNK BED", "BATH CABINET", "SHELVING", "CHAIR",
                                              "TV BENCH", "CHEST OF DRAWERS", "FLOOR LAMP", "MIRROR", "SIDEBOARD", "COFFEE TABLE", "DAYBED", "CABINET", "PLAY TABLE", "OFFICE CHAIR", "ISLAND", "RUG" };
        static readonly int[] HomePrices = { 399, 149, 229, 289, 89, 249, 1850, 59, 199, 119, 69, 35, 79, 129, 39, 45, 219, 49, 179, 99, 29, 109, 699, 75 };

        static void PriceTags(DressAtlas a, Random r)
        {
            for (int i = 0; i < HomeNames.Length; i++)
            {
                int x0, y0; a.Alloc("t_tag" + i, 256, 128, out x0, out y0);
                a.Fill(x0, y0, 256, 128, HC("ffcc00"));
                a.Fill(x0, y0 + 112, 256, 16, HC("2a2c2e"));
                a.Text(HomeNames[i], x0 + 8, y0 + 64, 240, 44, HC("16171a"), 0.55, DressAtlas.Align.Left);
                a.Text(HomeWhat[i], x0 + 8, y0 + 38, 160, 24, HC("16171a"), 0.45, DressAtlas.Align.Left);
                a.Text("£" + HomePrices[i], x0 + 132, y0 + 4, 118, 40, HC("16171a"), 0.55, DressAtlas.Align.Centre);
            }
        }

        static void MenuBoard(DressAtlas a, string name)
        {
            int x0, y0; a.Alloc(name, 512, 256, out x0, out y0);
            a.Fill(x0, y0, 512, 256, HC("2a2c2e"));
            a.Text("FLATPAK CAFE", x0 + 20, y0 + 200, 472, 44, HC("ff7a1a"), 0.55, DressAtlas.Align.Centre);
            string[] lines = { "HOT DISH OF THE DAY   £4.95", "VEGGIE BALLS & MASH   £4.50", "SOUP & ROLL   £2.95", "SALAD BOWL   £3.95", "CAKE & COFFEE   £2.50" };
            for (int i = 0; i < lines.Length; i++) a.Text(lines[i], x0 + 30, y0 + 160 - i * 34, 452, 26, HC("f4f3ef"), 0.45, DressAtlas.Align.Left);
        }

        static void Directory(DressAtlas a, string name)
        {
            int x0, y0; a.Alloc(name, 256, 384, out x0, out y0);
            a.Fill(x0, y0, 256, 384, HC("2a2c2e"));
            a.Text("FLATPAK", x0 + 10, y0 + 330, 236, 44, HC("ff7a1a"), 0.55, DressAtlas.Align.Centre);
            string[] lines = { "1  SHOWROOM", "1  CAFE", "0  MARKET HALL", "0  SELF SERVE", "0  TILLS", "0  BISTRO", "0  BARGAINS", "0  RETURNS" };
            for (int i = 0; i < lines.Length; i++)
            {
                a.Fill(x0 + 10, y0 + 286 - i * 36, 236, 30, i < 2 ? HC("3c4b66") : HC("3b3e42"));
                a.Text(lines[i], x0 + 16, y0 + 289 - i * 36, 224, 24, HC("f4f3ef"), 0.45, DressAtlas.Align.Left);
            }
        }

        /// The walkway's arrow, painted on its own colour (it's laid on the path strip).
        static void ArrowTile(DressAtlas a, string name)
        {
            int x0, y0; a.Alloc(name, 128, 128, out x0, out y0);
            a.Fill(x0, y0, 128, 128, HC("e4dfd4"));
            // pointing up the texture (+v): a shaft and a head
            a.Fill(x0 + 52, y0 + 14, 24, 60, HC("8f9295"));
            for (int k = 0; k < 44; k++) a.Fill(x0 + 64 - (44 - k), y0 + 70 + k, 2 * (44 - k), 1, HC("8f9295"));
        }

        /// Home_Concrete: polished grey concrete with saw-cut joints (one 6 m bay per tile).
        public static byte[] HomeConcreteTexture(int size)
        {
            byte[] px = new byte[size * size * 4];
            Random r = new Random(66);
            double[] blot = new double[64];
            for (int i = 0; i < 64; i++) blot[i] = r.NextDouble();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double v = 0.62 + 0.04 * blot[((x * 8 / size) * 8 + (y * 8 / size)) % 64] + (r.NextDouble() - 0.5) * 0.035;
                    if (x < 2 || y < 2) v = 0.48;
                    int i = (y * size + x) * 4;
                    px[i] = (byte)(255 * v * 0.98); px[i + 1] = (byte)(255 * v * 0.98); px[i + 2] = (byte)(255 * v); px[i + 3] = 255;
                }
            return px;
        }

        /// Home_Steps: escalator treads (one step per tile along the travel; the yellow edges at the sides).
        public static byte[] HomeStepsTexture(int size)
        {
            byte[] px = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double v = (x / 6) % 2 == 0 ? 0.36 : 0.22;            // the cleats
                    bool edge = x < size / 16 || x >= size - size / 16;
                    bool riser = y < size / 10;
                    int i = (y * size + x) * 4;
                    if (edge || riser) { px[i] = 240; px[i + 1] = 200; px[i + 2] = 20; }
                    else { px[i] = (byte)(255 * v); px[i + 1] = (byte)(255 * v); px[i + 2] = (byte)(255 * v * 1.05); }
                    px[i + 3] = 255;
                }
            return px;
        }
    }
}
