// E5: the supermarkets' insides (Basketful, Grocera, ValuStack). Pure C#: what Lundun → 28 builds into each store.
// A shared kit, laid out to each store's footprint (the front with its doorway, the depth behind):
//   shell     floor, inner walls, ceiling and the doorway's reveals: always there (cheap), so the inside never shows the
//             sky through the doors
//   detail    switched on only when you're near or inside (LundunShopInterior): strip lights; the lobby (trolleys,
//             baskets, a welcome sign); checkouts and self-checkouts; produce tables; two banks of double-sided aisles
//             packed with products (a painted atlas of original packaging, one quad per shelf per 4 m, so it stays
//             fast); hanging aisle signs; chiller cabinets along the back wall, chest freezers in front of them; offer
//             posters and a staff door
//   props     things that break (LundunBreakable): promo stacks by the entrance, the chillers' glass fronts
//   items     a short list you can pick up (for missions) or steal (LundunShopItem)
//   doors     the sliding entrance doors (LundunShopDoor)
//   crowd     where shoppers walk and browse, where the staff stand (for LundunMarketCrowd)
// Everything uses four materials: Shop_Atlas (products, signs, fixtures), Shop_Floor, Shop_Ceiling (tiled) and
// Shop_Glass, plus the LED lamp-head material for the strip lights. All lit from within (emissive), as shops are.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class ShopProp { public string kind, name; public MeshData mesh; public double cx, cy, cz, sx, sy, sz, yaw; public double[] debrisUV; }
    public class ShopItem { public string id, name; public bool steal; public MeshData mesh; public double x, y, z; }
    public class ShopDoor { public double x, z, ax, az, ox, oz, half, top; public MeshData left, right; }

    public class ShopInterior
    {
        public string id, name;
        public double x0, x1, z0, z1, doorX, doorZ, outX, outZ, ceiling;
        public List<MeshData> shell = new List<MeshData>(), detail = new List<MeshData>();
        public List<DressBox> colliders = new List<DressBox>();
        public List<ShopProp> breakables = new List<ShopProp>();
        public List<ShopItem> items = new List<ShopItem>();
        public List<ShopDoor> doors = new List<ShopDoor>();
        public MarketInfo crowd = new MarketInfo();
        public List<string> log = new List<string>();
    }

    public static partial class Generator
    {
        public const string ShopAtlasMat = "Shop_Atlas", ShopFloorMat = "Shop_Floor", ShopCeilingMat = "Shop_Ceiling", ShopGlassMat = "Shop_Glass";
        const double ShopFloorY = 0.16, ShopCeilY = 5.2, ShopWall = 0.35;

        /// The aisle categories (a product strip and a hanging sign each).
        static readonly string[] ShopCats = { "CEREAL", "TINS", "DRINKS", "CRISPS", "CLEANING", "PASTA", "BISCUITS", "TEA", "JARS", "TOILETRIES", "PET", "WINE", "SWEETS", "BAKING" };
        static readonly string[] ShopCatNames = { "CEREAL & BREAKFAST", "TINS & CANS", "SOFT DRINKS", "CRISPS & SNACKS", "HOUSEHOLD", "PASTA RICE & NOODLES", "BISCUITS",
                                                  "TEA & COFFEE", "SAUCES & JARS", "HEALTH & BEAUTY", "PET FOOD", "BEER WINE & SPIRITS", "SWEETS & CHOCOLATE", "BAKING" };
        static readonly string[] FridgeCats = { "DAIRY", "CHILLED", "DRINKSCOLD", "FROZEN" };

        /// Items you can take, per store: id, name, steal?, category it sits in.
        static readonly string[][] ShopItemList =
        {
            new[] { "BASKETFUL", "water", "Bottle of water", "0", "DRINKS" }, new[] { "BASKETFUL", "painkillers", "Box of painkillers", "0", "TOILETRIES" },
            new[] { "BASKETFUL", "gin", "Bottle of gin", "1", "WINE" }, new[] { "BASKETFUL", "razor", "Electric razor", "1", "TOILETRIES" }, new[] { "BASKETFUL", "chocs", "Box of chocolates", "1", "SWEETS" },
            new[] { "GROCERA", "rice", "Bag of rice", "0", "PASTA" }, new[] { "GROCERA", "firstaid", "First-aid kit", "0", "TOILETRIES" },
            new[] { "GROCERA", "whisky", "Bottle of whisky", "1", "WINE" }, new[] { "GROCERA", "coffee", "Jar of posh coffee", "1", "TEA" }, new[] { "GROCERA", "perfume", "Bottle of perfume", "1", "TOILETRIES" },
            new[] { "VALUSTACK", "tape", "Roll of duct tape", "0", "CLEANING" }, new[] { "VALUSTACK", "torch", "Torch", "0", "CLEANING" },
            new[] { "VALUSTACK", "vodka", "Bottle of vodka", "1", "WINE" }, new[] { "VALUSTACK", "batteries", "Pack of batteries", "1", "CLEANING" }, new[] { "VALUSTACK", "charger", "Phone charger", "1", "SWEETS" },
        };

        // ================================================================== the atlas (painted once)
        static DressAtlas shopAtlas;
        public static DressAtlas ShopAtlas()
        {
            if (shopAtlas != null) return shopAtlas;
            DressAtlas a = new DressAtlas(2048, 4096, 515);
            Random r = new Random(5151);
            string[][] sw = {
                new[] { "white", "f2f2ee" }, new[] { "cream", "ece4d2" }, new[] { "light", "c8cacb" }, new[] { "mid", "8e9194" }, new[] { "dark", "3a3d40" }, new[] { "black", "16171a" },
                new[] { "metal", "a8adb0" }, new[] { "chrome", "d8dcde" }, new[] { "wood", "9a7448" }, new[] { "shadow", "2a2620" }, new[] { "belt", "1c1c1e" },
                new[] { "screen", "8fd0ff" }, new[] { "fridge", "e8eef0" }, new[] { "red", "c8102e" }, new[] { "basket", "d22630" }, new[] { "bf_green", "1f7a3a" }, new[] { "bf_orange", "f29f05" },
                new[] { "gr_purple", "5b2a86" }, new[] { "gr_teal", "2fb5a8" }, new[] { "vs_yellow", "ffd200" }, new[] { "vs_red", "c8102e" }, new[] { "tag", "ffe14d" }, new[] { "door", "5d6268" },
                new[] { "debris_can", "b8c0c4" }, new[] { "item_glow", "fff4b0" } };
            foreach (string[] s in sw) a.Swatch("s_" + s[0], Col.Hex(s[1]));
            // product strips: 4 m of shelf, 0.36 m tall
            foreach (string c in ShopCats) for (int k = 0; k < 2; k++) ProductStrip(a, "p_" + c + k, c, r);
            foreach (string c in FridgeCats) for (int k = 0; k < 2; k++) ProductStrip(a, "p_" + c + k, c, r);
            // produce crates (seen from above), aisle signs, store signs and posters
            string[][] produce = { new[] { "apples", "c8202a", "e8443a" }, new[] { "oranges", "f28a12", "ffb03a" }, new[] { "bananas", "f2d23a", "c8a420" }, new[] { "greens", "3f8f2f", "6ab04a" },
                                   new[] { "potatoes", "b08a5a", "8a6a42" }, new[] { "tomatoes", "d1271b", "f24a2a" }, new[] { "limes", "6aa83a", "8cc63f" }, new[] { "onions", "c79a6a", "e0c08a" } };
            foreach (string[] p in produce) ProduceTile(a, "v_" + p[0], Col.Hex(p[1]), Col.Hex(p[2]), r);
            for (int i = 0; i < ShopCats.Length; i++) a.Sign("a_" + ShopCats[i], 512, 64, ShopCatNames[i], Col.Hex("1b2766"), Col.Hex("ffffff"), Col.Hex("ffd23f"), 0.45);
            a.Sign("a_DAIRY", 512, 64, "MILK & DAIRY", Col.Hex("1b2766"), Col.Hex("ffffff"), Col.Hex("ffd23f"), 0.45);
            a.Sign("a_FROZEN", 512, 64, "FROZEN", Col.Hex("1b2766"), Col.Hex("ffffff"), Col.Hex("8fd0ff"), 0.45);
            a.Sign("a_PRODUCE", 512, 64, "FRUIT & VEG", Col.Hex("1f7a3a"), Col.Hex("ffffff"), Col.Hex("ffd23f"), 0.45);
            a.Sign("w_BASKETFUL", 512, 64, "WELCOME TO BASKETFUL", Col.Hex("1f7a3a"), Col.Hex("ffffff"), Col.Hex("f29f05"), 0.45);
            a.Sign("w_GROCERA", 512, 64, "WELCOME TO GROCERA", Col.Hex("5b2a86"), Col.Hex("ffffff"), Col.Hex("2fb5a8"), 0.45);
            a.Sign("w_VALUSTACK", 512, 64, "VALUSTACK - PILE IT HIGH", Col.Hex("ffd200"), Col.Hex("c8102e"), Col.Hex("c8102e"), 0.45);
            string[][] posters = { new[] { "2 FOR 3 POUNDS", "c8102e", "ffffff" }, new[] { "HALF PRICE", "ffd200", "c8102e" }, new[] { "FRESH TODAY", "1f7a3a", "ffffff" },
                                   new[] { "SCAN & GO", "1b2766", "8fd0ff" }, new[] { "TILLS", "16171a", "ffd23f" }, new[] { "STAFF ONLY", "3a3d40", "ffffff" }, new[] { "WAY OUT", "1f7a3a", "ffffff" } };
            foreach (string[] p in posters) a.Sign("x_" + p[0], 512, 64, p[0], Col.Hex(p[1]), Col.Hex(p[2]), new Col(0, 0, 0, 0), 0.5);
            shopAtlas = a;
            return a;
        }

        /// A strip of product fronts packed side by side (1020 × 120 px = 4 m × 0.36 m), each with a fictional brand.
        static void ProductStrip(DressAtlas a, string name, string cat, Random r)
        {
            int x0, y0; a.Alloc(name, 1020, 120, out x0, out y0);
            a.Fill(x0, y0, 1020, 120, Col.Hex("2a2620"));
            string[] words; string[] cols; int wMin, wMax, hMin, hMax; int shape;   // 0 box, 1 can, 2 bottle, 3 bag, 4 jar
            switch (cat)
            {
                case "CEREAL": words = new[] { "CRUNCHOS", "OAT HILL", "SUNRISE", "BRAN BITES", "HONEY O'S" }; cols = new[] { "f2c200", "c8102e", "1d4fb8", "f28a12", "6b2d8a", "1f7a3a" }; wMin = 64; wMax = 84; hMin = 100; hMax = 118; shape = 0; break;
                case "TINS": words = new[] { "BEANS", "SOUP", "TOMS", "PEAS", "CORN" }; cols = new[] { "1d4fb8", "c8102e", "f2c200", "1f7a3a", "f28a12" }; wMin = 26; wMax = 30; hMin = 34; hMax = 40; shape = 1; break;
                case "DRINKS": words = new[] { "FIZZ", "LEMO", "COLA", "ORA", "SPARK" }; cols = new[] { "8a0b20", "f2d23a", "16171a", "f28a12", "2fb5a8" }; wMin = 22; wMax = 28; hMin = 96; hMax = 112; shape = 2; break;
                case "CRISPS": words = new[] { "KRUNCH", "SALTY", "CHEESY", "PRAWN", "BBQ" }; cols = new[] { "c8102e", "1d4fb8", "f2c200", "e0218a", "6b2d8a", "1f7a3a" }; wMin = 46; wMax = 58; hMin = 74; hMax = 92; shape = 3; break;
                case "CLEANING": words = new[] { "SPARKLO", "BRITE", "SCRUB", "FRESH" }; cols = new[] { "ffffff", "2fb5a8", "1d4fb8", "8cc63f", "f2c200" }; wMin = 30; wMax = 40; hMin = 90; hMax = 112; shape = 2; break;
                case "PASTA": words = new[] { "NONNA", "RISO", "FUSILLI", "BASMATI" }; cols = new[] { "1d4fb8", "f2e9d0", "c8102e", "1f7a3a" }; wMin = 44; wMax = 60; hMin = 76; hMax = 104; shape = 0; break;
                case "BISCUITS": words = new[] { "DUNKERS", "CRUMBLE", "GINGER", "CREAMS" }; cols = new[] { "8a5a3a", "c8102e", "f28a12", "1d4fb8" }; wMin = 70; wMax = 96; hMin = 40; hMax = 56; shape = 0; break;
                case "TEA": words = new[] { "BREW", "MORNING", "ROAST", "LEAF" }; cols = new[] { "c8102e", "1f7a3a", "3a2a1a", "1d4fb8", "f2c200" }; wMin = 46; wMax = 64; hMin = 64; hMax = 90; shape = 0; break;
                case "JARS": words = new[] { "RAGU", "CURRY", "PESTO", "JAM", "PICKLE" }; cols = new[] { "c8102e", "f28a12", "1f7a3a", "8a0b20", "f2c200" }; wMin = 30; wMax = 38; hMin = 46; hMax = 60; shape = 4; break;
                case "TOILETRIES": words = new[] { "GLOSS", "SMILE", "SOFT", "PURE" }; cols = new[] { "e0218a", "ffffff", "2fb5a8", "6b2d8a", "8fd0ff" }; wMin = 24; wMax = 34; hMin = 64; hMax = 100; shape = 2; break;
                case "PET": words = new[] { "WOOF", "MIAOW", "CHOMP" }; cols = new[] { "c8102e", "1d4fb8", "f2c200", "1f7a3a" }; wMin = 70; wMax = 96; hMin = 90; hMax = 116; shape = 3; break;
                case "WINE": words = new[] { "ROUGE", "BLANC", "LAGER", "GIN", "RUM" }; cols = new[] { "4a0b18", "2f4a2a", "6a4a1a", "1d3a2a", "16171a" }; wMin = 24; wMax = 28; hMin = 104; hMax = 118; shape = 2; break;
                case "SWEETS": words = new[] { "CHOC", "FUDGE", "TOFFEE", "MINTS" }; cols = new[] { "6b2d8a", "8a5a3a", "c8102e", "f2c200", "1d4fb8" }; wMin = 40; wMax = 60; hMin = 30; hMax = 50; shape = 0; break;
                case "BAKING": words = new[] { "FLOUR", "SUGAR", "OATS" }; cols = new[] { "f2e9d0", "1d4fb8", "c8102e" }; wMin = 50; wMax = 64; hMin = 80; hMax = 106; shape = 3; break;
                case "DAIRY": words = new[] { "MILK", "YOG", "BUTTER", "CHEESE" }; cols = new[] { "ffffff", "1d4fb8", "1f7a3a", "c8102e", "f2c200" }; wMin = 28; wMax = 40; hMin = 70; hMax = 104; shape = 2; break;
                case "CHILLED": words = new[] { "HAM", "PIZZA", "SALAD", "PIE" }; cols = new[] { "e8a0a0", "f28a12", "6ab04a", "c8a420" }; wMin = 70; wMax = 96; hMin = 30; hMax = 46; shape = 0; break;
                case "DRINKSCOLD": words = new[] { "JUICE", "COLA", "FIZZ" }; cols = new[] { "f28a12", "16171a", "2fb5a8", "c8102e" }; wMin = 22; wMax = 28; hMin = 90; hMax = 110; shape = 2; break;
                default: words = new[] { "PEAS", "CHIPS", "ICE", "FISH" }; cols = new[] { "8fd0ff", "1d4fb8", "f2f2ee", "f28a12" }; wMin = 70; wMax = 96; hMin = 44; hMax = 70; shape = 0; break;   // frozen
            }
            int x = x0 + 2;
            while (x < x0 + 1018)
            {
                int w = wMin + r.Next(wMax - wMin + 1), h = hMin + r.Next(hMax - hMin + 1);
                int reps = 1 + r.Next(shape == 1 ? 5 : 3);          // a facing: the same product side by side
                Col c = Col.Hex(cols[r.Next(cols.Length)]); Col c2 = Col.Hex(cols[r.Next(cols.Length)]);
                string word = words[r.Next(words.Length)];
                for (int k = 0; k < reps && x < x0 + 1018; k++)
                {
                    int ww = Math.Min(w, x0 + 1018 - x);
                    if (shape == 1)          // cans, two high
                        for (int t = 0; t < 2; t++)
                        {
                            int yy = y0 + 2 + t * (h + 2);
                            a.Fill(x, yy, ww - 2, h, Col.Hex("b8c0c4")); a.Fill(x, yy + h / 5, ww - 2, h * 3 / 5, c);
                            a.Fill(x + 2, yy + h / 2 - 2, ww - 6, 4, Col.Hex("ffffff"));
                        }
                    else if (shape == 2)     // bottles: a body and a neck
                    {
                        int body = h * 2 / 3;
                        a.Fill(x + 1, y0 + 2, ww - 3, body, c);
                        a.Fill(x + ww / 2 - 4, y0 + 2 + body, 7, h - body - 6, c.Scale(0.85));
                        a.Fill(x + ww / 2 - 4, y0 + h - 6, 7, 6, c2);
                        a.Fill(x + 2, y0 + body / 3, ww - 5, body / 3, Col.Hex("f2f2ee"));
                        a.Fill(x + 4, y0 + body / 3 + 3, ww - 9, body / 3 - 6, c2);
                    }
                    else if (shape == 3)     // bags: rounded shoulders, a big window
                    {
                        a.Fill(x + 1, y0 + 2, ww - 3, h - 8, c);
                        a.Fill(x + 4, y0 + h - 8, ww - 9, 6, c.Scale(0.8));
                        a.Fill(x + 5, y0 + 10, ww - 11, (h - 8) / 3, c2);
                        if (ww > 40) a.Text(word, x + 3, y0 + h / 2, ww - 7, Math.Max(10, h / 4), Col.Hex("ffffff"), 0.45, DressAtlas.Align.Centre);
                    }
                    else if (shape == 4)     // jars: glass, a lid, a label
                    {
                        a.Fill(x + 1, y0 + 2, ww - 3, h - 8, Col.Hex("6a3a1a"));
                        a.Fill(x + 2, y0 + h - 8, ww - 5, 7, Col.Hex("d8a91f"));
                        a.Fill(x + 1, y0 + h / 4, ww - 3, h / 3, c);
                    }
                    else                     // boxes: a colour, a band, the name
                    {
                        a.Fill(x + 1, y0 + 2, ww - 3, h, c);
                        a.Fill(x + 1, y0 + 2 + h * 2 / 3, ww - 3, h / 8, c2);
                        a.Frame(x + 1, y0 + 2, ww - 3, h, 1, c.Scale(0.6));
                        if (ww > 34 && h > 34) a.Text(word, x + 3, y0 + h / 3, ww - 7, Math.Max(10, h / 4), Col.Hex("ffffff"), 0.45, DressAtlas.Align.Centre);
                        a.Disc(x + ww * 0.5, y0 + h * 0.2, Math.Min(ww, h) * 0.12, c2.Scale(1.1));
                    }
                    x += ww;
                }
                x += r.Next(3);
            }
        }

        /// A crate of fruit or veg seen from above: lots of little round things.
        static void ProduceTile(DressAtlas a, string name, Col c, Col c2, Random r)
        {
            int x0, y0; a.Alloc(name, 256, 128, out x0, out y0);
            a.Fill(x0, y0, 256, 128, Col.Hex("6a4a2a"));
            for (int i = 0; i < 260; i++)
            {
                double cx = x0 + 6 + r.NextDouble() * 244, cy = y0 + 6 + r.NextDouble() * 116, rad = 7 + r.NextDouble() * 5;
                a.Disc(cx, cy, rad, (r.NextDouble() < 0.5 ? c : c2).Scale(0.85 + r.NextDouble() * 0.3));
                a.Disc(cx - rad * 0.3, cy + rad * 0.3, rad * 0.25, Col.Hex("ffffff", 90));
            }
        }

        /// Tiled textures for the floor (0.6 m tiles, 2.4 m repeat) and the ceiling (1.2 m panels, light fittings).
        public static byte[] ShopTileTexture(bool ceiling, int size)
        {
            byte[] px = new byte[size * size * 4];
            Random r = new Random(ceiling ? 77 : 55);
            int cells = ceiling ? 2 : 4, cell = size / cells;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int cx = x / cell, cy = y / cell, lx = x % cell, ly = y % cell;
                    bool joint = lx < (ceiling ? 3 : 2) || ly < (ceiling ? 3 : 2);
                    double v;
                    if (ceiling) v = joint ? 0.62 : 0.9 + ((x * 13 + y * 7) % 11) / 400.0;
                    else v = joint ? 0.6 : 0.86 + ((cx * 7 + cy * 3) % 5) * 0.012 + (r.NextDouble() - 0.5) * 0.02;
                    int i = (y * size + x) * 4;
                    px[i] = (byte)(255 * v * (ceiling ? 1.0 : 0.97)); px[i + 1] = (byte)(255 * v * (ceiling ? 1.0 : 0.97)); px[i + 2] = (byte)(255 * v); px[i + 3] = 255;
                }
            return px;
        }

        // ================================================================== laying out a store
        class ShopFrame
        {
            public double x0, W, D, frontZ, uDoor; public int front;
            public V2 P(double u, double v) { return new V2(x0 + u, frontZ - front * v); }
            public V2 In { get { return new V2(0, -front); } }
        }

        static ShopInterior si;
        static MeshData sAtlas, sFloor, sCeil, sLights, shAtlas;
        static Random srand;

        /// The enterable stores' insides (after BuildExpansion).
        public static List<ShopInterior> ShopInteriors()
        {
            List<ShopInterior> list = new List<ShopInterior>();
            if (retailMap.Count == 0) return list;
            DressAtlas atlas = ShopAtlas();
            foreach (Store s in Stores)
            {
                if (!s.enterable) continue;
                list.Add(BuildShop(s, atlas));
            }
            return list;
        }

        static UVRect SA(string name) { return shopAtlas[name]; }

        static ShopInterior BuildShop(Store s, DressAtlas atlas)
        {
            ShopFrame F = new ShopFrame { x0 = s.x0, W = s.x1 - s.x0, D = s.z1 - s.z0, front = s.front, frontZ = s.front > 0 ? s.z1 : s.z0, uDoor = s.door - s.x0 };
            si = new ShopInterior { id = s.id, name = s.name, x0 = s.x0, x1 = s.x1, z0 = s.z0, z1 = s.z1, doorX = s.Door.x, doorZ = s.Door.z, outX = s.Out.x, outZ = s.Out.z, ceiling = ShopCeilY };
            srand = new Random(s.id.GetHashCode() & 0xffff);
            shAtlas = new MeshData("Shell_" + s.id, "Shop", ShopAtlasMat, true) { navArea = 1 };
            MeshData shFloor = new MeshData("ShellFloor_" + s.id, "Shop", ShopFloorMat, true) { navArea = PavementArea };
            MeshData shCeil = new MeshData("ShellCeiling_" + s.id, "Shop", ShopCeilingMat, false) { navIgnore = true };
            sAtlas = new MeshData("Fittings_" + s.id, "Shop", ShopAtlasMat, false) { navIgnore = true };
            sLights = new MeshData("Lights_" + s.id, "Shop", LampHeadMats[3], false) { navIgnore = true };
            sFloor = shFloor; sCeil = shCeil;
            si.shell.Add(shAtlas); si.shell.Add(shFloor); si.shell.Add(shCeil);
            si.detail.Add(sAtlas); si.detail.Add(sLights);
            string brand = s.id == "BASKETFUL" ? "bf_green" : s.id == "GROCERA" ? "gr_purple" : "vs_yellow";
            string brand2 = s.id == "BASKETFUL" ? "bf_orange" : s.id == "GROCERA" ? "gr_teal" : "vs_red";

            // ---- shell: floor, ceiling, inner walls (with the doorway), the doorway's reveals
            double w0 = ShopWall, W = F.W, D = F.D;
            V2 c00 = F.P(w0, w0), c10 = F.P(W - w0, w0), c11 = F.P(W - w0, D - w0), c01 = F.P(w0, D - w0);
            TiledQuad(shFloor, c00, c10, c11, c01, ShopFloorY, 2.4, true);
            TiledQuad(shCeil, c00, c10, c11, c01, ShopCeilY, 2.4, false);
            // the floor in the doorway
            TiledQuad(shFloor, F.P(F.uDoor - DoorHalf, -0.05), F.P(F.uDoor + DoorHalf, -0.05), F.P(F.uDoor + DoorHalf, w0), F.P(F.uDoor - DoorHalf, w0), ShopFloorY, 2.4, true);
            InnerWall(F, 0, brand, true);       // front
            InnerWall(F, 1, brand, false);      // right (u = W)
            InnerWall(F, 2, brand, false);      // back
            InnerWall(F, 3, brand, false);      // left (u = 0)
            foreach (int k in new[] { -1, 1 })
            {
                double u = F.uDoor + k * DoorHalf;
                V2 a = F.P(u, -0.02), b = F.P(u, w0);
                VQuad(shAtlas, a, b, ShopFloorY, DoorTop, SA("s_light"), new V2(-k, 0));
            }
            HQuad(shAtlas, F.P(F.uDoor, w0 / 2), new V2(1, 0), DoorHalf, w0 / 2 + 0.02, DoorTop, SA("s_light"));     // (seen from below: both ways)
            Flip(shAtlas);

            // ---- detail
            // strip lights: rows along the aisles
            for (double u = 6; u < W - 4; u += 4.0)
                SBox(sLights, F.P(u, D / 2), F.In, D / 2 - 4, 0.09, ShopCeilY - 0.06, ShopCeilY - 0.01, new UVRect(0.5, 0.5, 0.5, 0.5), true);

            Lobby(F, s, brand, brand2);
            int lanes = Checkouts(F, s, brand);
            double vAisles0 = 15, vBack = D - 11;           // aisles from here to here; then the freezers, then the chillers
            double produceW = Math.Min(24, W * 0.22);
            Produce(F, 2.5, produceW, vAisles0, vBack);
            List<double[]> sides = Aisles(F, produceW + 3, W - 3, vAisles0, vBack, brand);
            Chillers(F, produceW + 3, W - 2.5, D, brand);
            Freezers(F, produceW + 4, W - 4, D - 6.2);
            Posters(F);
            Items(F, s, sides);
            Stacks(F, s, brand, brand2);
            Doors(F, s);
            CrowdPaths(F, s, lanes, produceW, vAisles0, vBack, sides);

            int quads = 0; foreach (MeshData m in si.detail) quads += m.tri.Count / 6;
            si.log.Add(string.Format("{0}: {1} checkouts, {2} shelf sides, {3} breakables, {4} items, {5} walk points, {6} staff, about {7} quads of detail",
                s.name, lanes, sides.Count, si.breakables.Count, si.items.Count, si.crowd.nodes.Count, si.crowd.traders.Count, quads));
            return si;
        }

        /// A flat quad with tiled UVs (world metres / tile), facing up (floor) or down (ceiling).
        static void TiledQuad(MeshData m, V2 a, V2 b, V2 c, V2 d, double y, double tile, bool up)
        {
            Func<V2, P3> P = q => new P3(q, y, q.x / tile, q.z / tile);
            m.Quad(P(a), P(b), P(c), P(d), 0, up ? 1 : -1, 0);
        }

        /// Adds back faces for everything so far (the shell is seen from inside and out), on their own vertices so
        /// each side keeps its own normal.
        static void Flip(MeshData m)
        {
            int n = m.tri.Count;
            for (int i = 0; i < n; i += 3)
            {
                int[] src = { m.tri[i], m.tri[i + 2], m.tri[i + 1] };
                foreach (int k in src)
                {
                    int nv = m.pos.Count / 3;
                    m.pos.Add(m.pos[k * 3]); m.pos.Add(m.pos[k * 3 + 1]); m.pos.Add(m.pos[k * 3 + 2]);
                    m.uv.Add(m.uv[k * 2]); m.uv.Add(m.uv[k * 2 + 1]);
                    m.tri.Add(nv);
                }
            }
        }

        /// One inner wall: cream, a brand band at 3 m, a skirting; the front has the doorway.
        static void InnerWall(ShopFrame F, int side, string brand, bool front)
        {
            double w0 = ShopWall, W = F.W, D = F.D;
            V2 a, b, inward;
            switch (side)
            {
                case 0: a = F.P(w0, w0); b = F.P(W - w0, w0); inward = F.In; break;
                case 1: a = F.P(W - w0, w0); b = F.P(W - w0, D - w0); inward = new V2(-1, 0); break;
                case 2: a = F.P(W - w0, D - w0); b = F.P(w0, D - w0); inward = F.In * -1; break;
                default: a = F.P(w0, D - w0); b = F.P(w0, w0); inward = new V2(1, 0); break;
            }
            double L = V2.Dist(a, b); V2 f = (b - a).Norm;
            List<double[]> spans = new List<double[]>();
            if (front)
            {
                double t = V2.Dot(F.P(F.uDoor, w0) - a, f);
                spans.Add(new[] { 0, t - DoorHalf, ShopFloorY }); spans.Add(new[] { t + DoorHalf, L, ShopFloorY }); spans.Add(new[] { t - DoorHalf, t + DoorHalf, DoorTop });
            }
            else spans.Add(new[] { 0, L, ShopFloorY });
            foreach (double[] sp in spans)
            {
                V2 p0 = a + f * sp[0], p1 = a + f * sp[1];
                double yb = sp[2];
                if (yb < 1) VQuad(shAtlas, p0, p1, yb, yb + 0.15, SA("s_dark"), inward);
                VQuad(shAtlas, p0, p1, Math.Max(yb, ShopFloorY + 0.15), Math.Max(yb, 3.0), SA("s_cream"), inward);
                VQuad(shAtlas, p0, p1, Math.Max(yb, 3.0), Math.Max(yb, 3.6), SA("s_" + brand), inward);
                VQuad(shAtlas, p0, p1, Math.Max(yb, 3.6), ShopCeilY, SA("s_cream"), inward);
            }
        }

        // ------------------------------------------------------------------ the lobby: trolleys, baskets, the welcome sign
        static void Lobby(ShopFrame F, Store s, string brand, string brand2)
        {
            double u0 = F.uDoor - 13, v = 2.2;
            for (int row = 0; row < 3; row++)
                for (int k = 0; k < 7; k++)
                {
                    V2 c = F.P(u0 + row * 1.6, v + k * 0.35);
                    SBox(sAtlas, c, F.In, 0.42, 0.28, ShopFloorY + 0.12, ShopFloorY + 0.95, SA("s_chrome"));
                    SBox(sAtlas, c, F.In, 0.42, 0.29, ShopFloorY + 0.8, ShopFloorY + 0.86, SA("s_" + brand), true);
                }
            si.colliders.Add(new DressBox { name = "Trolleys", cx = F.P(u0 + 1.6, 3.2).x, cy = 0.6, cz = F.P(u0 + 1.6, 3.2).z, sx = 4.4, sy = 1.0, sz = 2.6, yaw = 0 });
            for (int k = 0; k < 8; k++) SBox(sAtlas, F.P(F.uDoor - 4.5, 2.0), F.In, 0.25, 0.2, ShopFloorY + k * 0.11, ShopFloorY + k * 0.11 + 0.1, SA("s_basket"));
            // the welcome sign, hanging over the way in
            V2 sc = F.P(F.uDoor, 4.5);
            foreach (int k in new[] { -1, 1 })
                VQuad(sAtlas, sc + new V2(k * 3.2, 0) + F.In * (k * 0.03), sc + new V2(-k * 3.2, 0) + F.In * (k * 0.03), 3.6, 4.4, SA("w_" + s.id), F.In * -k);
            SBox(sAtlas, sc + new V2(-2.5, 0), F.In, 0.02, 0.02, 4.4, ShopCeilY, SA("s_metal"));
            SBox(sAtlas, sc + new V2(2.5, 0), F.In, 0.02, 0.02, 4.4, ShopCeilY, SA("s_metal"));
        }

        // ------------------------------------------------------------------ checkouts and self-checkouts
        static int Checkouts(ShopFrame F, Store s, string brand)
        {
            int lanes = Math.Max(3, Math.Min(7, (int)((F.W - F.uDoor - 6) / 3.4)));
            double v0 = 6.5, len = 3.6;
            for (int i = 0; i < lanes; i++)
            {
                double u = F.uDoor + 4 + i * 3.4;
                if (u > F.W - 3) { lanes = i; break; }
                V2 c = F.P(u, v0 + len / 2);
                SBox(sAtlas, c, F.In, len / 2, 0.45, ShopFloorY, ShopFloorY + 0.9, SA("s_dark"));
                SBox(sAtlas, c + new V2(0.46, 0), F.In, len / 2 - 0.1, 0.01, ShopFloorY + 0.2, ShopFloorY + 0.75, SA("s_" + brand));
                SBox(sAtlas, c + F.In * 0.3, F.In, len / 2 - 0.5, 0.3, ShopFloorY + 0.9, ShopFloorY + 0.94, SA("s_belt"));
                V2 till = c - F.In * (len / 2 - 0.5);
                SBox(sAtlas, till + new V2(0.1, 0), F.In, 0.2, 0.2, ShopFloorY + 0.94, ShopFloorY + 1.1, SA("s_mid"));
                SBox(sAtlas, till + new V2(0.1, 0), F.In, 0.02, 0.18, ShopFloorY + 1.1, ShopFloorY + 1.4, SA("s_screen"));
                // the lane light, with its number lit
                SBox(sAtlas, c + F.In * (len / 2 - 0.2), F.In, 0.04, 0.04, ShopFloorY + 0.9, ShopFloorY + 2.3, SA("s_metal"));
                SBox(sLights, c + F.In * (len / 2 - 0.2), F.In, 0.12, 0.12, ShopFloorY + 2.3, ShopFloorY + 2.55, new UVRect(0.5, 0.5, 0.5, 0.5), true);
                si.colliders.Add(new DressBox { name = "Checkout", cx = c.x, cy = ShopFloorY + 0.47, cz = c.z, sx = 0.9, sy = 0.94, sz = len, yaw = 0 });
                // the operator's seat, between this counter and the next
                si.crowd.traders.Add(new[] { till.x - 0.9, till.z, -1.0, 0.0 });
            }
            // self-checkouts on the other side of the door
            for (int i = 0; i < 6; i++)
            {
                V2 c = F.P(F.uDoor - 5 - (i % 3) * 1.6, 8 + (i / 3) * 2.6);
                SBox(sAtlas, c, F.In, 0.3, 0.35, ShopFloorY, ShopFloorY + 1.0, SA("s_white"));
                SBox(sAtlas, c - F.In * 0.15, F.In, 0.05, 0.25, ShopFloorY + 1.0, ShopFloorY + 1.45, SA("s_screen"));
                si.colliders.Add(new DressBox { name = "SelfCheckout", cx = c.x, cy = ShopFloorY + 0.5, cz = c.z, sx = 0.7, sy = 1.0, sz = 0.6, yaw = 0 });
            }
            V2 scan = F.P(F.uDoor - 6.6, 11.4);
            foreach (int k in new[] { -1, 1 }) VQuad(sAtlas, scan + new V2(k * 1.6, 0) + F.In * (k * 0.02), scan + new V2(-k * 1.6, 0) + F.In * (k * 0.02), 2.6, 3.0, SA("x_SCAN & GO"), F.In * -k);
            V2 tills = F.P(F.uDoor + 4 + (lanes - 1) * 1.7, 11.4);
            foreach (int k in new[] { -1, 1 }) VQuad(sAtlas, tills + new V2(k * 1.6, 0) + F.In * (k * 0.02), tills + new V2(-k * 1.6, 0) + F.In * (k * 0.02), 2.6, 3.0, SA("x_TILLS"), F.In * -k);
            return lanes;
        }

        // ------------------------------------------------------------------ fruit and veg
        static void Produce(ShopFrame F, double u0, double u1, double v0, double v1)
        {
            string[] tiles = { "v_apples", "v_oranges", "v_bananas", "v_greens", "v_potatoes", "v_tomatoes", "v_limes", "v_onions" };
            int t = 0;
            for (double v = v0 + 2; v + 2.6 <= v1; v += 5.0)
                for (double u = u0 + 2; u + 2.6 <= u1; u += 5.0)
                {
                    V2 c = F.P(u + 1.3, v + 1.3);
                    SBox(sAtlas, c, new V2(1, 0), 1.3, 1.3, ShopFloorY, ShopFloorY + 0.75, SA("s_wood"));
                    for (int q = 0; q < 4; q++)
                    {
                        V2 cc = c + new V2((q % 2 - 0.5) * 1.3, (q / 2 - 0.5) * 1.3);
                        HQuad(sAtlas, cc, new V2(1, 0), 0.62, 0.62, ShopFloorY + 0.86, SA(tiles[t++ % tiles.Length]));
                        SBox(sAtlas, cc, new V2(1, 0), 0.64, 0.64, ShopFloorY + 0.75, ShopFloorY + 0.84, SA("s_wood"));
                    }
                    si.colliders.Add(new DressBox { name = "ProduceTable", cx = c.x, cy = ShopFloorY + 0.45, cz = c.z, sx = 2.6, sy = 0.9, sz = 2.6, yaw = 0 });
                    foreach (int k in new[] { 0, 1 })
                    {
                        V2 sp = c + F.In * ((k == 0 ? -1 : 1) * 2.0);
                        si.crowd.spots.Add(new[] { sp.x, sp.z, c.x, c.z, -1 });
                    }
                }
            V2 sg = F.P((u0 + u1) / 2, v0 + 1.0);
            foreach (int k in new[] { -1, 1 }) VQuad(sAtlas, sg + new V2(k * 2.4, 0) + F.In * (k * 0.02), sg + new V2(-k * 2.4, 0) + F.In * (k * 0.02), 3.0, 3.6, SA("a_PRODUCE"), F.In * -k);
        }

        // ------------------------------------------------------------------ the aisles
        /// Two banks of double-sided gondolas (front–back), a cross aisle between. Returns each shelf side:
        /// u of the shelf face, v0, v1, facing (+1 = faces +u), category index.
        static List<double[]> Aisles(ShopFrame F, double u0, double u1, double v0, double v1, string brand)
        {
            List<double[]> sides = new List<double[]>();
            double span = u1 - u0;
            int n = Math.Max(2, Math.Min(16, (int)Math.Floor(span / 4.0)));
            double pitch = span / n;
            double vm = (v0 + v1) / 2;
            double[][] banks = { new[] { v0, vm - 2.2 }, new[] { vm + 2.2, v1 } };
            int cat = 0;
            for (int g = 0; g < n; g++)
            {
                double ug = u0 + pitch * (g + 0.5);
                foreach (double[] bank in banks)
                {
                    double a = bank[0], b = bank[1];
                    if (b - a < 4) continue;
                    Gondola(F, ug, a, b, brand, ShopCats[cat % ShopCats.Length], ShopCats[(cat + 1) % ShopCats.Length]);
                    sides.Add(new[] { ug - 0.65, a, b, -1, cat % ShopCats.Length });
                    sides.Add(new[] { ug + 0.65, a, b, 1, (cat + 1) % ShopCats.Length });
                    cat += 2;
                    si.colliders.Add(new DressBox { name = "Shelves", cx = F.P(ug, (a + b) / 2).x, cy = ShopFloorY + 1.05, cz = F.P(ug, (a + b) / 2).z, sx = 1.3, sy = 2.1, sz = b - a, yaw = 0 });
                }
            }
            // signs over each aisle (between gondolas): the category of the shelf on its left
            for (int g = 0; g + 1 < n; g++)
            {
                double ua = u0 + pitch * (g + 1);
                foreach (double[] bank in banks)
                {
                    if (bank[1] - bank[0] < 4) continue;
                    double[] side = sides.Find(sd => Math.Abs(sd[0] - (ua - pitch / 2 + 0.65)) < 0.01 && Math.Abs(sd[1] - bank[0]) < 0.01);
                    string c = side != null ? ShopCats[(int)side[4]] : ShopCats[0];
                    V2 sc = F.P(ua, (bank[0] + bank[1]) / 2);
                    foreach (int k in new[] { -1, 1 })
                        VQuad(sAtlas, sc + new V2(0.03 * k, 0) - F.In * (k * 1.6), sc + new V2(0.03 * k, 0) + F.In * (k * 1.6), 3.3, 3.7, SA("a_" + c), new V2(k, 0));
                    foreach (int k in new[] { -1, 1 }) SBox(sAtlas, sc + F.In * (k * 1.2), F.In, 0.02, 0.02, 3.7, ShopCeilY, SA("s_metal"));
                }
            }
            return sides;
        }

        /// One double-sided gondola from v a to b at u ug: plinth, spine, four shelves a side packed with products,
        /// promo ends, a brand-coloured top.
        static void Gondola(ShopFrame F, double ug, double a, double b, string brand, string catL, string catR)
        {
            V2 along = F.In;
            V2 mid = F.P(ug, (a + b) / 2);
            double half = (b - a) / 2;
            SBox(sAtlas, mid, along, half, 0.6, ShopFloorY, ShopFloorY + 0.14, SA("s_dark"));
            SBox(sAtlas, mid, along, half, 0.05, ShopFloorY + 0.14, ShopFloorY + 2.0, SA("s_light"));
            SBox(sAtlas, mid, along, half + 0.02, 0.66, ShopFloorY + 2.0, ShopFloorY + 2.08, SA("s_" + brand), true);
            double[] levels = { 0.3, 0.72, 1.14, 1.56 };
            foreach (int side in new[] { -1, 1 })
            {
                string cat = side < 0 ? catL : catR;
                V2 n = new V2(side, 0);
                for (double v = a; v < b - 0.01; v += 4.0)
                {
                    double v1 = Math.Min(b, v + 4.0), len = v1 - v;
                    foreach (double lv in levels)
                    {
                        UVRect strip = SA("p_" + cat + srand.Next(2));                 // each shelf its own run of products
                        double uStart = srand.NextDouble() * (1 - len / 4.0);
                        double y0 = ShopFloorY + lv + 0.03, y1 = y0 + 0.36;
                        V2 p0 = F.P(ug + side * 0.6, v), p1 = F.P(ug + side * 0.6, v1);
                        // product fronts: the strip, a slice of it as long as this run
                        double su0 = strip.u0 + (strip.u1 - strip.u0) * uStart, su1 = strip.u0 + (strip.u1 - strip.u0) * (uStart + len / 4.0);
                        bool flip = V2.Dot(p1 - p0, n.Left) < 0;
                        sAtlas.Quad(new P3(p0, y0, flip ? su1 : su0, strip.v0), new P3(p1, y0, flip ? su0 : su1, strip.v0),
                                    new P3(p1, y1, flip ? su0 : su1, strip.v1), new P3(p0, y1, flip ? su1 : su0, strip.v1), n.x, 0, n.z);
                        // tops of the products, and the shelf with its price edge
                        UVRect sh = SA("s_shadow");
                        sAtlas.Quad(new P3(p0, y1, sh.Cu, sh.Cv), new P3(p1, y1, sh.Cu, sh.Cv), new P3(F.P(ug + side * 0.08, v1), y1, sh.Cu, sh.Cv), new P3(F.P(ug + side * 0.08, v), y1, sh.Cu, sh.Cv), 0, 1, 0);
                        UVRect wh = SA("s_white"), tag = SA("s_tag");
                        V2 e0 = F.P(ug + side * 0.64, v), e1 = F.P(ug + side * 0.64, v1);
                        sAtlas.Quad(new P3(e0, y0 - 0.06, tag.Cu, tag.Cv), new P3(e1, y0 - 0.06, tag.Cu, tag.Cv), new P3(e1, y0 - 0.01, tag.Cu, tag.Cv), new P3(e0, y0 - 0.01, tag.Cu, tag.Cv), n.x, 0, n.z);
                        sAtlas.Quad(new P3(e0, y0 - 0.01, wh.Cu, wh.Cv), new P3(e1, y0 - 0.01, wh.Cu, wh.Cv), new P3(F.P(ug + side * 0.08, v1), y0 - 0.01, wh.Cu, wh.Cv), new P3(F.P(ug + side * 0.08, v), y0 - 0.01, wh.Cu, wh.Cv), 0, 1, 0);
                    }
                }
            }
            // promo ends (the end facing the cross aisle or the front)
            foreach (int end in new[] { -1, 1 })
            {
                double v = end < 0 ? a : b;
                V2 c = F.P(ug, v), o = F.In * end;
                UVRect strip = SA("p_" + catL + srand.Next(2));
                foreach (double lv in levels)
                {
                    double y0 = ShopFloorY + lv + 0.03, y1 = y0 + 0.36;
                    V2 p0 = c + o * 0.02 - new V2(0.6, 0), p1 = c + o * 0.02 + new V2(0.6, 0);
                    bool flip = V2.Dot(p1 - p0, o.Left) < 0;
                    double u0 = strip.u0, u1 = strip.u0 + (strip.u1 - strip.u0) * 0.3;
                    sAtlas.Quad(new P3(p0, y0, flip ? u1 : u0, strip.v0), new P3(p1, y0, flip ? u0 : u1, strip.v0), new P3(p1, y1, flip ? u0 : u1, strip.v1), new P3(p0, y1, flip ? u1 : u0, strip.v1), o.x, 0, o.z);
                }
            }
        }

        // ------------------------------------------------------------------ chillers along the back wall, freezers in front
        static void Chillers(ShopFrame F, double u0, double u1, double D, string brand)
        {
            double v = D - ShopWall - 0.45;
            int k = 0;
            for (double u = u0; u + 3.0 <= u1; u += 3.0, k++)
            {
                string cat = k % 3 == 0 ? "DAIRY" : k % 3 == 1 ? "CHILLED" : "DRINKSCOLD";
                V2 c = F.P(u + 1.5, v);
                SBox(sAtlas, c, new V2(1, 0), 1.5, 0.45, ShopFloorY, ShopFloorY + 0.25, SA("s_dark"));
                SBox(sAtlas, c, new V2(1, 0), 1.5, 0.45, ShopFloorY + 2.05, ShopFloorY + 2.4, SA("s_fridge"));
                SBox(sLights, c - F.In * 0.46, new V2(1, 0), 1.45, 0.02, ShopFloorY + 2.12, ShopFloorY + 2.2, new UVRect(0.5, 0.5, 0.5, 0.5));
                // inside: shelves of product behind the glass
                UVRect strip = SA("p_" + cat + (k % 2));
                for (int lv = 0; lv < 4; lv++)
                {
                    double y0 = ShopFloorY + 0.3 + lv * 0.44, y1 = y0 + 0.36;
                    V2 p0 = c - F.In * 0.2 - new V2(1.45, 0), p1 = c - F.In * 0.2 + new V2(1.45, 0);
                    V2 o = F.In * -1;
                    bool flip = V2.Dot(p1 - p0, o.Left) < 0;
                    double su1 = strip.u0 + (strip.u1 - strip.u0) * 0.72;
                    sAtlas.Quad(new P3(p0, y0, flip ? su1 : strip.u0, strip.v0), new P3(p1, y0, flip ? strip.u0 : su1, strip.v0), new P3(p1, y1, flip ? strip.u0 : su1, strip.v1), new P3(p0, y1, flip ? su1 : strip.u0, strip.v1), o.x, 0, o.z);
                }
                // door frames (three doors)
                for (int d = 0; d <= 3; d++) SBox(sAtlas, c - F.In * 0.45 + new V2(-1.5 + d * 1.0, 0), F.In, 0.03, 0.04, ShopFloorY + 0.25, ShopFloorY + 2.05, SA("s_metal"));
                // the glass: breakable, one pane per three cabinets (fewer pieces for the phone to draw)
                if (k % 3 == 0)
                {
                    int nCab = (int)Math.Min(3, Math.Floor((u1 - u) / 3.0));
                    double gw = nCab * 3.0 - 0.04;
                    V2 gc = F.P(u + nCab * 1.5, v) - F.In * 0.47;
                    MeshData glass = new MeshData("ChillerGlass_" + si.id + "_" + k, "Shop", ShopGlassMat, false) { navIgnore = true };
                    V2 g0 = gc - new V2(gw / 2, 0), g1 = gc + new V2(gw / 2, 0);
                    glass.Quad(new P3(g0, ShopFloorY + 0.25), new P3(g1, ShopFloorY + 0.25), new P3(g1, ShopFloorY + 2.05), new P3(g0, ShopFloorY + 2.05), -F.In.x, 0, -F.In.z);
                    si.breakables.Add(new ShopProp { kind = "glass", name = "Chiller", mesh = glass, cx = gc.x, cy = ShopFloorY + 1.15, cz = gc.z, sx = gw, sy = 1.8, sz = 0.06, yaw = 0, debrisUV = Rect(SA("s_white")) });
                }
                si.colliders.Add(new DressBox { name = "Chiller", cx = c.x, cy = ShopFloorY + 1.2, cz = c.z, sx = 3.0, sy = 2.4, sz = 0.8, yaw = 0 });
                si.crowd.spots.Add(new[] { c.x - F.In.x * 1.6, c.z - F.In.z * 1.6, c.x, c.z, -1 });
            }
            // signs over them
            V2 sc = F.P((u0 + u1) / 2, D - ShopWall - 0.05);
            VQuad(sAtlas, sc + new V2(3.2, 0) - F.In * 0.0, sc - new V2(3.2, 0), 2.8, 3.6, SA("a_DAIRY"), F.In * -1);
        }

        static void Freezers(ShopFrame F, double u0, double u1, double v)
        {
            int k = 0;
            for (double u = u0; u + 2.4 <= u1; u += 2.6, k++)
            {
                if (k % 6 == 5) continue;             // gaps to get through
                V2 c = F.P(u + 1.2, v);
                SBox(sAtlas, c, new V2(1, 0), 1.2, 0.5, ShopFloorY, ShopFloorY + 0.85, SA("s_fridge"));
                HQuad(sAtlas, c, new V2(1, 0), 1.12, 0.42, ShopFloorY + 0.86, SA("p_FROZEN" + (k % 2)));
                SBox(sAtlas, c, new V2(1, 0), 1.2, 0.5, ShopFloorY + 0.86, ShopFloorY + 0.9, SA("s_metal"));
                si.colliders.Add(new DressBox { name = "Freezer", cx = c.x, cy = ShopFloorY + 0.45, cz = c.z, sx = 2.4, sy = 0.9, sz = 1.0, yaw = 0 });
                si.crowd.spots.Add(new[] { c.x + F.In.x * 1.1, c.z + F.In.z * 1.1, c.x, c.z, -1 });
            }
            V2 sc = F.P((u0 + u1) / 2, v);
            foreach (int kk in new[] { -1, 1 }) VQuad(sAtlas, sc + new V2(kk * 2.4, 0) + F.In * (kk * 0.02), sc + new V2(-kk * 2.4, 0) + F.In * (kk * 0.02), 3.0, 3.6, SA("a_FROZEN"), F.In * -kk);
        }

        // ------------------------------------------------------------------ posters and the staff door
        static void Posters(ShopFrame F)
        {
            string[] ps = { "x_2 FOR 3 POUNDS", "x_HALF PRICE", "x_FRESH TODAY" };
            for (int i = 0; i < 3; i++)
            {
                V2 c = F.P(ShopWall + 0.03, 20 + i * 12);
                VQuad(sAtlas, c - F.In * 1.6, c + F.In * 1.6, 2.0, 2.4, SA(ps[i]), new V2(1, 0));
            }
            V2 sd = F.P(F.W - 8, F.D - ShopWall - 0.02);
            VQuad(sAtlas, sd - new V2(0.9, 0), sd + new V2(0.9, 0), ShopFloorY, ShopFloorY + 2.2, SA("s_door"), F.In * -1);
            VQuad(sAtlas, sd - new V2(0.9, 0) - F.In * 0.01, sd + new V2(0.9, 0) - F.In * 0.01, 2.45, 2.65, SA("x_STAFF ONLY"), F.In * -1);
            V2 wo = F.P(F.uDoor, ShopWall + 0.03);
            VQuad(sAtlas, wo - new V2(1.6, 0), wo + new V2(1.6, 0), 3.7, 4.1, SA("x_WAY OUT"), F.In);
        }

        // ------------------------------------------------------------------ things you can take
        static void Items(ShopFrame F, Store s, List<double[]> sides)
        {
            int n = 0;
            foreach (string[] it in ShopItemList)
            {
                if (it[0] != s.id) continue;
                int cat = Array.IndexOf(ShopCats, it[4]);
                List<double[]> fit = sides.FindAll(sd => (int)sd[4] == cat);
                if (fit.Count == 0) fit = sides;
                double[] side = fit[(n * 3) % fit.Count];
                double v = side[1] + 2 + srand.NextDouble() * Math.Max(0.5, side[2] - side[1] - 4);
                double lv = 1.14 + 0.03;
                V2 p = F.P(side[0] + side[3] * 0.1, v);
                MeshData m = new MeshData("Item_" + s.id + "_" + it[1], "Shop", ShopAtlasMat, false) { navIgnore = true };
                bool steal = it[3] == "1";
                // a box in its own colours, set forward of the shelf, with a lit tag
                SBox(m, p, F.In, 0.16, 0.1, ShopFloorY + lv, ShopFloorY + lv + 0.3, SA(steal ? "s_red" : "s_" + (s.id == "BASKETFUL" ? "bf_green" : s.id == "GROCERA" ? "gr_teal" : "vs_yellow")), true);
                SBox(m, p + new V2(side[3] * 0.11, 0), F.In, 0.1, 0.01, ShopFloorY + lv + 0.08, ShopFloorY + lv + 0.2, SA("s_item_glow"));
                si.items.Add(new ShopItem { id = s.id.ToLowerInvariant() + "_" + it[1], name = it[2], steal = steal, mesh = m, x = p.x + side[3] * 0.5, y = ShopFloorY, z = p.z });
                n++;
            }
        }

        // ------------------------------------------------------------------ promo stacks by the entrance (they break)
        static void Stacks(ShopFrame F, Store s, string brand, string brand2)
        {
            string[] cats = { "TINS", "DRINKS" };
            double[][] at = { new[] { F.uDoor + 4.8, 3.0 }, new[] { F.uDoor + 8.4, 3.0 } };
            for (int i = 0; i < at.Length; i++)
            {
                V2 c = F.P(at[i][0], at[i][1]);
                MeshData m = new MeshData("Stack_" + s.id + "_" + i, "Shop", ShopAtlasMat, false) { navIgnore = true };
                UVRect strip = SA("p_" + cats[i % 2] + "0");
                // a stepped pyramid of product, on a pallet with a price card
                SBox(m, c, new V2(1, 0), 0.6, 0.6, ShopFloorY, ShopFloorY + 0.12, SA("s_wood"));
                double y = ShopFloorY + 0.12;
                for (int t = 0; t < 4; t++)
                {
                    double hw = 0.58 - t * 0.12, hh = 0.28;
                    foreach (V2 o in new[] { new V2(1, 0), new V2(-1, 0), new V2(0, 1), new V2(0, -1) })
                    {
                        V2 l = o.Left;
                        double su = strip.u0 + (strip.u1 - strip.u0) * (0.1 * t);
                        double su1 = su + (strip.u1 - strip.u0) * 0.12;
                        m.Quad(new P3(c + o * hw - l * hw, y, su, strip.v0), new P3(c + o * hw + l * hw, y, su1, strip.v0), new P3(c + o * hw + l * hw, y + hh, su1, strip.v1), new P3(c + o * hw - l * hw, y + hh, su, strip.v1), o.x, 0, o.z);
                    }
                    UVRect top = SA("s_debris_can");
                    m.Quad(new P3(c + new V2(-hw, -hw), y + hh, top.Cu, top.Cv), new P3(c + new V2(hw, -hw), y + hh, top.Cu, top.Cv), new P3(c + new V2(hw, hw), y + hh, top.Cu, top.Cv), new P3(c + new V2(-hw, hw), y + hh, top.Cu, top.Cv), 0, 1, 0);
                    y += hh;
                }
                V2 card = c - F.In * 0.62;
                SBox(m, card, new V2(1, 0), 0.25, 0.01, ShopFloorY + 0.9, ShopFloorY + 1.25, SA("s_" + brand2));
                si.breakables.Add(new ShopProp { kind = "stack", name = "Display", mesh = m, cx = c.x, cy = ShopFloorY + 0.65, cz = c.z, sx = 1.2, sy = 1.3, sz = 1.2, yaw = 0,
                                                 debrisUV = Rect(strip) });
            }
        }

        static double[] Rect(UVRect r) { return new[] { r.u0, r.v0, r.u1, r.v1 }; }

        // ------------------------------------------------------------------ the sliding doors
        static void Doors(ShopFrame F, Store s)
        {
            V2 door = s.Door - F.In * 0.0, f = new V2(1, 0);
            ShopDoor d = new ShopDoor { x = door.x, z = door.z, ax = 1, az = 0, ox = s.Out.x, oz = s.Out.z, half = DoorHalf, top = DoorTop };
            foreach (int side in new[] { -1, 1 })
            {
                MeshData leaf = new MeshData("DoorLeaf_" + s.id + (side < 0 ? "_L" : "_R"), "Shop", ShopGlassMat, false) { navIgnore = true };
                // leaves are built in the door's own space: centred on the doorway, +x along it, +z out; each leaf covers its half
                double x0 = side < 0 ? -DoorHalf : 0, x1 = side < 0 ? 0 : DoorHalf;
                leaf.Quad(new P3(x0, ShopFloorY, 0, 0, 0), new P3(x1, ShopFloorY, 0, 0, 0), new P3(x1, DoorTop, 0, 0, 0), new P3(x0, DoorTop, 0, 0, 0), 0, 0, 1);
                leaf.Quad(new P3(x0, ShopFloorY, 0, 0, 0), new P3(x1, ShopFloorY, 0, 0, 0), new P3(x1, DoorTop, 0, 0, 0), new P3(x0, DoorTop, 0, 0, 0), 0, 0, -1);
                if (side < 0) d.left = leaf; else d.right = leaf;
            }
            si.doors.Add(d);
        }

        // ------------------------------------------------------------------ where people walk
        static void CrowdPaths(ShopFrame F, Store s, int lanes, double produceW, double v0, double v1, List<double[]> sides)
        {
            MarketInfo mi = si.crowd;
            mi.name = s.name;
            List<V2> nodes = mi.nodes;
            Func<V2, int> N = q => { nodes.Add(q); return nodes.Count - 1; };
            Action<int, int> E = (a, b) => { if (a != b) mi.edges.Add(new[] { a, b }); };
            double D = F.D, W = F.W;
            int gate = N(F.P(F.uDoor, -6));                     // outside, on the apron
            int inside = N(F.P(F.uDoor, 2.5));
            E(gate, inside);
            mi.gates.Add(gate);
            // the walkway in front of the checkouts, and the front walkway past the aisles
            List<int> front = new List<int>(), cross = new List<int>(), back = new List<int>();
            for (double u = 3; u <= W - 3 + 0.01; u += 4) front.Add(N(F.P(u, 13.0)));
            for (int i = 0; i + 1 < front.Count; i++) E(front[i], front[i + 1]);
            // in through the gap beside the self-checkouts, out through the checkout lanes
            int lobbyIn = N(F.P(F.uDoor - 1.0, 6)), lobbyOut = N(F.P(F.uDoor + 2, 4));
            E(inside, lobbyIn); E(inside, lobbyOut);
            E(lobbyIn, NearestIdx(nodes, front, F.P(F.uDoor - 1, 13)));
            for (int i = 0; i < lanes; i++)
            {
                double u = F.uDoor + 4 + i * 3.4 - 1.7 + 0.0;
                int a = N(F.P(u + 0.0, 5.0)), b = N(F.P(u, 11.0));
                E(a, b); E(a, lobbyOut); E(b, NearestIdx(nodes, front, F.P(u, 13)));
            }
            // aisles: up each one, across the middle, along the back
            double vm = (v0 + v1) / 2;
            for (double u = 3; u <= W - 3 + 0.01; u += 4) back.Add(N(F.P(u, v1 + 1.6)));
            for (double u = produceW + 1.5; u <= W - 3 + 0.01; u += 4) cross.Add(N(F.P(u, vm)));          // not through the produce tables
            for (int i = 0; i + 1 < cross.Count; i++) E(cross[i], cross[i + 1]);
            for (int i = 0; i + 1 < back.Count; i++) E(back[i], back[i + 1]);
            // the aisles' centres: between neighbouring gondolas, and either side of the first and last
            List<double> gu = new List<double>();
            foreach (double[] sd in sides) if (sd[3] > 0 && !gu.Exists(x => Math.Abs(x - (sd[0] - 0.65)) < 0.1)) gu.Add(sd[0] - 0.65);
            gu.Sort();
            List<double> aisleU = new List<double>();
            for (int i = 0; i + 1 < gu.Count; i++) aisleU.Add((gu[i] + gu[i + 1]) / 2);
            if (gu.Count > 0) { aisleU.Add(gu[0] - 1.55); if (gu[gu.Count - 1] + 1.55 < W - 1) aisleU.Add(gu[gu.Count - 1] + 1.55); }
            foreach (double ua in aisleU)
            {
                int a = N(F.P(ua, 13.0)), b = N(F.P(ua, vm)), c = N(F.P(ua, v1 + 1.6));
                E(a, b); E(b, c);
                E(a, NearestIdx(nodes, front, F.P(ua, 13))); E(b, NearestIdx(nodes, cross, F.P(ua, vm))); E(c, NearestIdx(nodes, back, F.P(ua, v1 + 1.6)));
            }
            // the produce area: rows between the tables
            for (double u = 3.5; u < produceW; u += 5) { int a = N(F.P(u + 0.3, 13)), b = N(F.P(u + 0.3, v1 + 1.6)); E(a, b); E(a, NearestIdx(nodes, front, F.P(u, 13))); E(b, NearestIdx(nodes, back, F.P(u, v1 + 1.6))); }
            // past the freezers to the chillers
            List<int> fridge = new List<int>();
            for (double u = produceW + 3; u <= W - 3; u += 4) fridge.Add(N(F.P(u, D - 3.0)));
            for (int i = 0; i + 1 < fridge.Count; i++) E(fridge[i], fridge[i + 1]);
            // through the gaps in the freezer row (and round its ends)
            List<double> gapU = new List<double> { produceW + 3.0, W - 3.0 };
            int fk = 0;
            for (double u = produceW + 4; u + 2.4 <= W - 4; u += 2.6, fk++) if (fk % 6 == 5) gapU.Add(u + 1.2);
            foreach (double gu2 in gapU)
            {
                int a = N(F.P(gu2, D - 3.0)), b = N(F.P(gu2, v1 + 1.6));
                E(a, b); E(a, NearestIdx(nodes, fridge, F.P(gu2, D - 3.0))); E(b, NearestIdx(nodes, back, F.P(gu2, v1 + 1.6)));
            }
            // browsing spots in front of the shelves (every few metres, both sides)
            foreach (double[] sd in sides)
                for (double v = sd[1] + 1.5; v < sd[2] - 1; v += 3.2)
                {
                    V2 sp = F.P(sd[0] + sd[3] * 0.75, v), look = F.P(sd[0] - sd[3] * 0.3, v);
                    mi.spots.Add(new[] { sp.x, sp.z, look.x, look.z, -1 });
                }
            // every spot walks from its nearest node
            for (int i = 0; i < mi.spots.Count; i++)
            {
                double[] sp = mi.spots[i];
                int best = 0; double bd = double.MaxValue;
                for (int k = 1; k < nodes.Count; k++) { double d = V2.Dist(nodes[k], new V2(sp[0], sp[1])); if (d < bd) { bd = d; best = k; } }
                sp[4] = best;
            }
            // staff: the till operators (from Checkouts), a guard by the door, two stacking shelves
            V2 guard = F.P(F.uDoor - 2.6, 3.0);
            mi.traders.Add(new[] { guard.x, guard.z, F.In.x, F.In.z });
            for (int k = 0; k < 2 && mi.spots.Count > 0; k++)
            {
                double[] sp = mi.spots[(k * 37 + 11) % mi.spots.Count];
                V2 at = new V2(sp[0], sp[1]), look = (new V2(sp[2], sp[3]) - at).Norm;
                mi.traders.Add(new[] { at.x, at.z, look.x, look.z });
            }
            // traders' facing: the operators look across their counter (set in Checkouts as −x); fine either way
        }

        static int NearestIdx(List<V2> nodes, List<int> among, V2 p)
        {
            int best = among[0]; double bd = double.MaxValue;
            foreach (int k in among) { double d = V2.Dist(nodes[k], p); if (d < bd) { bd = d; best = k; } }
            return best;
        }
    }
}
