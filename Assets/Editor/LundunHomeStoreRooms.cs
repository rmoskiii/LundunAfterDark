// E6: FLATPAK's first floor: the showroom's room sets (a furniture kit of boxes on the home atlas) and the café.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        /// A local frame: o the centre, a across, b back (a piece's front faces −b), y its floor, m the mesh it draws into.
        class HFrame
        {
            public V2 o, a, b; public double y; public MeshData m;
            public V2 P(double x, double z) { return o + a * x + b * z; }
            /// A frame at (x, z) in this one, turned q quarter turns: 0 = front to −b, 1 = front to +a, 2 = front to +b, 3 = front to −a.
            public HFrame At(double x, double z, int q)
            {
                V2 na, nb;
                switch (q & 3)
                {
                    case 1: nb = a * -1; na = b; break;
                    case 2: nb = b * -1; na = a * -1; break;
                    case 3: nb = a; na = b * -1; break;
                    default: nb = b; na = a; break;
                }
                return new HFrame { o = P(x, z), a = na, b = nb, y = y, m = m };
            }
        }

        static void FB(HFrame F, double x0, double x1, double z0, double z1, double y0, double y1, string sw, bool bottom = false)
        {
            SBox(F.m, F.P((x0 + x1) / 2, (z0 + z1) / 2), F.a, Math.Abs(x1 - x0) / 2, Math.Abs(z1 - z0) / 2, F.y + y0, F.y + y1, HA("s_" + sw), bottom);
        }

        static void FC(HFrame F, string name, double x0, double x1, double z0, double z1, double y0, double y1)
        {
            V2 p = F.P(x0, z0), q = F.P(x1, z1);
            si.colliders.Add(new DressBox { name = name, cx = (p.x + q.x) / 2, cy = F.y + (y0 + y1) / 2, cz = (p.z + q.z) / 2, sx = Math.Abs(p.x - q.x), sy = y1 - y0, sz = Math.Abs(p.z - q.z), yaw = 0 });
        }

        /// A picture facing the front (−b) at depth z.
        static void FF(HFrame F, double x0, double x1, double z, double y0, double y1, string tile)
        {
            VQuad(F.m, F.P(x0, z), F.P(x1, z), F.y + y0, F.y + y1, HA(tile), F.b * -1);
        }

        static void FT(HFrame F, double x, double z, double hx, double hz, double y, string tile)
        {
            HQuad(F.m, F.P(x, z), F.b, hz, hx, F.y + y, HA(tile));
        }

        static string Pick(params string[] s) { return s[hr.Next(s.Length)]; }

        // ------------------------------------------------------------------ the furniture
        static void Sofa(HFrame F, double len, string col)
        {
            double h = len / 2;
            FB(F, -h + 0.05, h - 0.05, -0.4, 0.4, 0, 0.1, "blackwood");
            FB(F, -h + 0.16, h - 0.16, -0.45, 0.25, 0.1, 0.45, col);
            FB(F, -h, h, 0.2, 0.45, 0.1, 0.86, col);
            foreach (int k in new[] { -1, 1 }) FB(F, k * (h - 0.09) - 0.09, k * (h - 0.09) + 0.09, -0.45, 0.45, 0.1, 0.62, col);
            string c2 = Pick("f_mustard", "f_cream", "f_teal", "f_pink", "f_navy");
            if (len > 1.2) foreach (int k in new[] { -1, 1 }) FB(F, k * (h - 0.45) - 0.22, k * (h - 0.45) + 0.22, 0.08, 0.2, 0.45, 0.78, c2);
            FC(F, "Sofa", -h, h, -0.45, 0.45, 0, 0.86);
        }

        static void CoffeeTable(HFrame F, double w, double d, string wood)
        {
            FB(F, -w / 2, w / 2, -d / 2, d / 2, 0.36, 0.42, wood);
            foreach (int i in new[] { -1, 1 }) foreach (int j in new[] { -1, 1 }) FB(F, i * (w / 2 - 0.05) - 0.025, i * (w / 2 - 0.05) + 0.025, j * (d / 2 - 0.05) - 0.025, j * (d / 2 - 0.05) + 0.025, 0, 0.36, wood);
            FB(F, -w / 2 + 0.06, w / 2 - 0.06, -d / 2 + 0.06, d / 2 - 0.06, 0.12, 0.15, wood);
        }

        static void Lamp(HFrame F, double x, double z, double y, bool floor)
        {
            if (floor)
            {
                FB(F, x - 0.15, x + 0.15, z - 0.15, z + 0.15, 0, 0.03, "black");
                FB(F, x - 0.015, x + 0.015, z - 0.015, z + 0.015, 0.03, 1.45, "black");
                FB(F, x - 0.2, x + 0.2, z - 0.2, z + 0.2, 1.4, 1.72, "glow");
            }
            else
            {
                FB(F, x - 0.06, x + 0.06, z - 0.06, z + 0.06, y, y + 0.3, Pick("ceramic", "f_teal", "brass", "black"));
                FB(F, x - 0.14, x + 0.14, z - 0.14, z + 0.14, y + 0.28, y + 0.48, "glow");
            }
        }

        static void Pendant(HFrame F, double x, double z, double y)
        {
            FB(F, x - 0.008, x + 0.008, z - 0.008, z + 0.008, y + 0.25, 4.2, "black");
            FB(F, x - 0.22, x + 0.22, z - 0.22, z + 0.22, y, y + 0.25, Pick("glow", "glow", "orange"));
            SBox(HLights, F.P(x, z), F.a, 0.12, 0.12, F.y + y - 0.01, F.y + y + 0.01, new UVRect(0.5, 0.5, 0.5, 0.5), true);
        }

        static void Bookcase(HFrame F, double w)
        {
            FB(F, -w / 2, w / 2, -0.15, 0.15, 0, 2.0, "white");
            FF(F, -w / 2 + 0.02, w / 2 - 0.02, -0.152, 0.02, 1.98, "k_books");
            FC(F, "Bookcase", -w / 2, w / 2, -0.15, 0.15, 0, 2.0);
        }

        static void Unit(HFrame F, double w, double d, double h, string body, string front, double frontW)
        {
            FB(F, -w / 2, w / 2, -d / 2, d / 2, 0.08, h, body);
            FB(F, -w / 2 + 0.03, w / 2 - 0.03, -d / 2 + 0.03, d / 2 - 0.03, 0, 0.08, "black");
            int n = Math.Max(1, (int)Math.Round(w / frontW));
            for (int i = 0; i < n; i++) FF(F, -w / 2 + w * i / n + 0.01, -w / 2 + w * (i + 1) / n - 0.01, -d / 2 - 0.003, 0.1, h - 0.02, front);
        }

        static void TVBench(HFrame F, double len)
        {
            Unit(F, len, 0.4, 0.45, "offwhite", "k_drawers", 0.9);
            FB(F, -0.6, 0.6, -0.03, 0.03, 0.5, 1.2, "black");
            FB(F, -0.15, 0.15, -0.08, 0.08, 0.45, 0.5, "black");
            FF(F, -0.57, 0.57, -0.032, 0.53, 1.17, "s_screen");
            FC(F, "TVBench", -len / 2, len / 2, -0.2, 0.2, 0, 0.5);
        }

        static void Chair(HFrame F, string col)
        {
            foreach (int i in new[] { -1, 1 }) foreach (int j in new[] { -1, 1 }) FB(F, i * 0.18 - 0.02, i * 0.18 + 0.02, j * 0.18 - 0.02, j * 0.18 + 0.02, 0, 0.44, col);
            FB(F, -0.21, 0.21, -0.21, 0.21, 0.42, 0.47, col);
            FB(F, -0.2, 0.2, 0.17, 0.21, 0.47, 0.9, col);
        }

        static void DiningTable(HFrame F, double len, double wid, string wood)
        {
            FB(F, -len / 2, len / 2, -wid / 2, wid / 2, 0.72, 0.76, wood);
            foreach (int i in new[] { -1, 1 }) foreach (int j in new[] { -1, 1 }) FB(F, i * (len / 2 - 0.08) - 0.03, i * (len / 2 - 0.08) + 0.03, j * (wid / 2 - 0.08) - 0.03, j * (wid / 2 - 0.08) + 0.03, 0, 0.72, wood);
            FC(F, "Table", -len / 2, len / 2, -wid / 2, wid / 2, 0, 0.76);
        }

        static void Desk(HFrame F, double w)
        {
            string top = Pick("white", "oak", "birch");
            FB(F, -w / 2, w / 2, -0.35, 0.35, 0.72, 0.75, top);
            foreach (int i in new[] { -1, 1 }) FB(F, i * (w / 2 - 0.03) - 0.025, i * (w / 2 - 0.03) + 0.025, -0.33, 0.33, 0, 0.72, "white");
            FB(F, -0.3, 0.3, 0.12, 0.15, 0.85, 1.2, "black");
            FB(F, -0.04, 0.04, 0.15, 0.2, 0.75, 0.85, "black");
            FF(F, -0.28, 0.28, 0.118, 0.87, 1.18, "s_screen_on");
            FB(F, -0.22, 0.22, -0.2, -0.05, 0.75, 0.77, "dark");
            Lamp(F, w / 2 - 0.2, 0.15, 0.75, false);
            FC(F, "Desk", -w / 2, w / 2, -0.35, 0.35, 0, 0.75);
        }

        static void OfficeChair(HFrame F)
        {
            FB(F, -0.3, 0.3, -0.03, 0.03, 0.02, 0.06, "black"); FB(F, -0.03, 0.03, -0.3, 0.3, 0.02, 0.06, "black");
            FB(F, -0.03, 0.03, -0.03, 0.03, 0.06, 0.45, "black");
            FB(F, -0.25, 0.25, -0.25, 0.25, 0.45, 0.52, Pick("black", "f_grey", "f_blue"));
            FB(F, -0.22, 0.22, 0.2, 0.26, 0.55, 1.05, Pick("black", "f_grey"));
        }

        static void Bed(HFrame F, double wid, bool big)
        {
            string wood = Pick("oak", "white", "walnut", "blackwood");
            double h = wid / 2;
            FB(F, -h - 0.05, h + 0.05, -1.0, 1.0, 0.1, 0.32, wood);
            foreach (int i in new[] { -1, 1 }) foreach (int j in new[] { -1, 1 }) FB(F, i * h - 0.04, i * h + 0.04, j * 0.95 - 0.04, j * 0.95 + 0.04, 0, 0.1, wood);
            FB(F, -h - 0.05, h + 0.05, 1.0, 1.08, 0.1, big ? 1.15 : 0.9, wood);
            FB(F, -h, h, -0.97, 0.98, 0.32, 0.52, "mattress");
            FB(F, -h - 0.02, h + 0.02, -0.98, 0.5, 0.52, 0.6, "white");
            FT(F, 0, -0.24, h + 0.02, 0.74, 0.601, Pick("d_0", "d_1"));
            int n = wid > 1.2 ? 2 : 1;
            for (int i = 0; i < n; i++) { double x = n == 1 ? 0 : (i == 0 ? -h / 2 : h / 2); FB(F, x - h / n + 0.08, x + h / n - 0.08, 0.6, 0.92, 0.52, 0.68, "white"); }
            FC(F, "Bed", -h - 0.05, h + 0.05, -1.0, 1.08, 0, 0.6);
        }

        static void Wardrobe(HFrame F, double w)
        {
            Unit(F, w, 0.6, 2.2, Pick("white", "offwhite", "oak"), "k_ward", 1.0);
            FC(F, "Wardrobe", -w / 2, w / 2, -0.3, 0.3, 0, 2.2);
        }

        static void KitchenRun(HFrame F, double len, bool white)
        {
            string body = white ? "white" : "dark", front = white ? "k_wall" : "k_cab";
            int n = Math.Max(1, (int)Math.Round(len / 1.2));
            FB(F, -len / 2, len / 2, -0.3, 0.3, 0.1, 0.88, body);
            FB(F, -len / 2 + 0.03, len / 2 - 0.03, -0.27, 0.27, 0, 0.1, "black");
            for (int i = 0; i < n; i++)
            {
                double x0 = -len / 2 + len * i / n, x1 = -len / 2 + len * (i + 1) / n;
                if (i == n / 2) { FF(F, x0 + 0.02, x1 - 0.02, -0.303, 0.12, 0.84, "s_black"); FF(F, x0 + 0.08, x1 - 0.08, -0.305, 0.7, 0.76, "s_steel"); }    // the oven
                else FF(F, x0 + 0.01, x1 - 0.01, -0.303, 0.12, 0.86, front);
            }
            string top = Pick("oak", "white", "charcoal", "birch");
            FB(F, -len / 2, len / 2, -0.32, 0.3, 0.88, 0.92, top);
            double sx = -len / 2 + Math.Min(1.0, len * 0.25), hx = len * 0.08;
            FT(F, sx, -0.02, 0.32, 0.2, 0.922, "s_steel");
            FB(F, sx - 0.02, sx + 0.02, 0.18, 0.22, 0.92, 1.2, "chrome");
            FT(F, hx, 0.0, 0.3, 0.25, 0.923, "s_black");
            // splashback and wall cupboards (to the wall behind)
            FF(F, -len / 2, len / 2, 0.299, 0.92, 1.45, "k_splash");
            FB(F, -len / 2, len / 2, 0.0, 0.3, 1.45, 2.15, white ? "white" : "offwhite");
            for (int i = 0; i < n; i++) FF(F, -len / 2 + len * i / n + 0.01, -len / 2 + len * (i + 1) / n - 0.01, -0.002, 1.47, 2.13, "k_wall");
            SBox(HLights, F.P(0, 0.0), F.a, len / 2 - 0.1, 0.03, F.y + 1.43, F.y + 1.45, new UVRect(0.5, 0.5, 0.5, 0.5), true);
            FC(F, "Kitchen", -len / 2, len / 2, -0.32, 0.3, 0, 0.92);
        }

        static void Island(HFrame F, double len)
        {
            FB(F, -len / 2, len / 2, -0.45, 0.45, 0.1, 0.9, "white");
            FB(F, -len / 2 + 0.03, len / 2 - 0.03, -0.42, 0.42, 0, 0.1, "black");
            FB(F, -len / 2 - 0.05, len / 2 + 0.05, -0.5, 0.5, 0.9, 0.94, Pick("oak", "charcoal", "white"));
            foreach (int k in new[] { -1, 1 })
            {
                double x = k * len / 4;
                FB(F, x - 0.18, x + 0.18, -0.85, -0.55, 0.62, 0.68, Pick("oak", "black"));
                FB(F, x - 0.02, x + 0.02, -0.72, -0.68, 0, 0.62, "black");
            }
            FC(F, "Island", -len / 2, len / 2, -0.5, 0.5, 0, 0.94);
        }

        static void Bath(HFrame F)
        {
            FB(F, -0.85, 0.85, -0.375, 0.375, 0, 0.55, "porcelain");
            FT(F, 0, 0, 0.75, 0.29, 0.553, "s_mirror");
            FB(F, 0.7, 0.75, -0.02, 0.02, 0.55, 0.75, "chrome");
            FC(F, "Bath", -0.85, 0.85, -0.375, 0.375, 0, 0.55);
        }

        static void Basin(HFrame F)
        {
            FB(F, -0.4, 0.4, -0.22, 0.22, 0.25, 0.82, Pick("oak", "white", "f_navy"));
            FB(F, -0.42, 0.42, -0.24, 0.24, 0.82, 0.9, "porcelain");
            FT(F, 0, -0.02, 0.25, 0.14, 0.902, "s_mirror");
            FF(F, -0.38, 0.38, 0.23, 1.1, 1.9, "s_mirror");
            FC(F, "Basin", -0.42, 0.42, -0.24, 0.24, 0, 0.9);
        }

        static void Toilet(HFrame F)
        {
            FB(F, -0.18, 0.18, -0.3, 0.2, 0, 0.4, "porcelain");
            FB(F, -0.2, 0.2, -0.32, 0.18, 0.4, 0.44, "white");
            FB(F, -0.2, 0.2, 0.18, 0.32, 0.4, 0.85, "porcelain");
        }

        static void BunkBed(HFrame F)
        {
            string wood = Pick("white", "pine", "birch");
            foreach (int i in new[] { -1, 1 }) foreach (int j in new[] { -1, 1 }) FB(F, i * 0.47 - 0.03, i * 0.47 + 0.03, j * 1.02 - 0.03, j * 1.02 + 0.03, 0, 1.75, wood);
            foreach (double y in new[] { 0.25, 1.2 })
            {
                FB(F, -0.47, 0.47, -1.0, 1.0, y, y + 0.08, wood);
                FB(F, -0.45, 0.45, -0.98, 0.98, y + 0.08, y + 0.24, "mattress");
                FB(F, -0.46, 0.46, -0.98, 0.5, y + 0.24, y + 0.3, Pick("f_blue", "f_teal", "f_mustard", "f_pink"));
            }
            FB(F, -0.47, 0.47, -1.04, -1.0, 1.3, 1.6, wood);
            for (double y = 0.4; y < 1.3; y += 0.3) FB(F, 0.48, 0.52, -0.7, -0.4, y, y + 0.04, wood);
            FC(F, "Bunk", -0.5, 0.5, -1.05, 1.05, 0, 1.75);
        }

        static void Plant(HFrame F, double x, double z, double h)
        {
            FB(F, x - 0.16, x + 0.16, z - 0.16, z + 0.16, 0, 0.35, "pot");
            FB(F, x - 0.25, x + 0.25, z - 0.25, z + 0.25, 0.35, h, Pick("plant", "plant2"));
            FT(F, x, z, 0.25, 0.25, h + 0.002, "t_plants");
        }

        static void Shelving(HFrame F, double w, double h, int levels, string strip)
        {
            foreach (int k in new[] { -1, 1 }) FB(F, k * w / 2 - 0.02, k * w / 2 + 0.02, -0.2, 0.2, 0, h, "steel");
            UVRect s = HA(strip);
            for (int i = 0; i < levels; i++)
            {
                double y = 0.1 + i * (h - 0.1) / levels;
                FB(F, -w / 2, w / 2, -0.2, 0.2, y, y + 0.03, Pick("birch", "white", "steel"));
                double y0 = y + 0.03, y1 = Math.Min(y + 0.39, y + (h - 0.1) / levels - 0.03);
                double u0 = s.u0 + (s.u1 - s.u0) * hr.NextDouble() * Math.Max(0, 1 - w / 4.0), u1 = u0 + (s.u1 - s.u0) * Math.Min(1, w / 4.0);
                V2 p0 = F.P(-w / 2 + 0.02, -0.1), p1 = F.P(w / 2 - 0.02, -0.1);
                V2 n = F.b * -1;
                bool flip = V2.Dot(p1 - p0, n.Left) < 0;
                F.m.Quad(new P3(p0, F.y + y0, flip ? u1 : u0, s.v0), new P3(p1, F.y + y0, flip ? u0 : u1, s.v0), new P3(p1, F.y + y1, flip ? u0 : u1, s.v1), new P3(p0, F.y + y1, flip ? u1 : u0, s.v1), n.x, 0, n.z);
            }
            FC(F, "Shelving", -w / 2, w / 2, -0.2, 0.2, 0, h);
        }

        static void PriceTag(HFrame F, double x, double z, int tag)
        {
            FB(F, x - 0.015, x + 0.015, z - 0.015, z + 0.015, 0, 1.15, "dark");
            FB(F, x - 0.1, x + 0.1, z - 0.1, z + 0.1, 0, 0.02, "dark");
            V2 c = F.P(x, z), al = F.a;
            foreach (int k in new[] { -1, 1 })
                VQuad(F.m, c + al * (k * 0.26) + F.b * (k * 0.012), c - al * (k * 0.26) + F.b * (k * 0.012), F.y + 0.88, F.y + 1.14, HA("t_tag" + (tag % HomeNames.Length)), F.b * k);
        }

        // ------------------------------------------------------------------ the room sets
        enum RoomKind { Living, Work, Dining, Kitchen, Bedroom, Bathroom, Kids, Storage }
        static readonly int[][] TagsFor = { new[] { 0, 1, 12, 17, 18, 7 }, new[] { 4, 21, 10 }, new[] { 5, 11, 16 }, new[] { 6, 22 }, new[] { 2, 3, 13 }, new[] { 9, 15, 19 }, new[] { 8, 20 }, new[] { 3, 10, 13, 19 } };
        static readonly string[] WallPaints = { "w_sage", "w_blue", "w_terra", "w_cream", "w_grey", "w_pink", "w_navy", "w_olive", "w_yellow", "w_white" };

        /// One room set: mouth at the corridor (z = 0), back wall at z = depth, across x = ±w/2.
        static void Room(HFrame R, double w, double depth, RoomKind kind)
        {
            roomCount++;
            string paint = WallPaints[hr.Next(WallPaints.Length)];
            string floor = kind == RoomKind.Kitchen ? Pick("f_check", "f_oak", "f_tile") : kind == RoomKind.Bathroom ? "f_tile" : kind == RoomKind.Kids ? "f_kids"
                         : kind == RoomKind.Bedroom ? Pick("f_carpet", "f_oak") : kind == RoomKind.Work ? "f_lino" : "f_oak";
            // the floor (2 m tiles of the pattern), the walls (feature colour at the back), a price tag at the front corner
            for (double x = -w / 2 + 0.06; x < w / 2 - 0.07; x += 2.0)
                for (double z = 0.0; z < depth - 0.07; z += 2.0)
                {
                    double x1 = Math.Min(x + 2.0, w / 2 - 0.06), z1 = Math.Min(z + 2.0, depth - 0.06);
                    UVRect t = HA(floor);
                    double fu = (x1 - x) / 2.0, fv = (z1 - z) / 2.0;
                    UVRect part = new UVRect(t.u0, t.v0, t.u0 + (t.u1 - t.u0) * fu, t.v0 + (t.v1 - t.v0) * fv);
                    HQuad(R.m, R.P((x + x1) / 2, (z + z1) / 2), R.b, (z1 - z) / 2, (x1 - x) / 2, R.y + 0.006, part);
                }
            FB(R, -w / 2, w / 2, depth - 0.06, depth, 0, 2.8, kind == RoomKind.Bathroom ? "white" : paint);
            FB(R, -w / 2, -w / 2 + 0.06, 0, depth - 0.06, 0, 2.8, "w_white");
            FB(R, w / 2 - 0.06, w / 2, 0, depth - 0.06, 0, 2.8, "w_white");
            FC(R, "RoomWall", -w / 2, w / 2, depth - 0.06, depth, 0, 2.8);
            FC(R, "RoomWall", -w / 2, -w / 2 + 0.06, 0, depth, 0, 2.8);
            FC(R, "RoomWall", w / 2 - 0.06, w / 2, 0, depth, 0, 2.8);
            foreach (double[] wl in new[] { new[] { -w / 2, depth, w / 2, depth }, new[] { -w / 2 + 0.03, 0, -w / 2 + 0.03, depth }, new[] { w / 2 - 0.03, 0, w / 2 - 0.03, depth } })
            {
                V2 p = R.P(wl[0], wl[1]), q = R.P(wl[2], wl[3]);
                si.walls.Add(new[] { p.x, p.z, q.x, q.z, R.y, R.y + 2.8 });
            }
            int[] tags = TagsFor[(int)kind];
            PriceTag(R, w / 2 - 0.45, 0.45, tags[hr.Next(tags.Length)]);
            double D = depth;
            string fab = Pick("f_grey", "f_blue", "f_green", "f_rust", "f_beige", "f_pink", "f_mustard", "f_navy", "f_teal", "f_cream");
            string wood = Pick("oak", "birch", "walnut", "pine", "blackwood");
            switch (kind)
            {
                case RoomKind.Living:
                    FT(R, 0, 4.9, 1.6, 1.25, 0.012, "r_" + hr.Next(4));
                    if (hr.NextDouble() < 0.5)
                    {
                        Sofa(R.At(0, D - 0.6, 0), 2.2, fab);
                        CoffeeTable(R.At(0, 4.9, 0), 1.1, 0.6, wood);
                        Sofa(R.At(w / 2 - 0.75, 4.7, 3), 0.9, Pick("f_mustard", "f_cream", "f_teal", fab));
                        Bookcase(R.At(-w / 2 + 0.22, 4.4, 1), 0.8);
                        FF(R, -0.55, 0.55, D - 0.065, 1.25, 2.3, "k_art" + hr.Next(4));
                    }
                    else
                    {
                        TVBench(R.At(0, D - 0.3, 0), 1.8);
                        Sofa(R.At(0, 4.3, 2), 2.2, fab);
                        CoffeeTable(R.At(0, 5.6, 0), 1.0, 0.55, wood);
                        Bookcase(R.At(-w / 2 + 0.22, 6.4, 1), 0.8);
                        Bookcase(R.At(w / 2 - 0.22, 6.4, 3), 0.8);
                    }
                    Lamp(R, -w / 2 + 0.45, D - 0.45, 0, true);
                    Unit(R.At(w / 2 - 0.4, D - 0.3, 0), 0.5, 0.45, 0.55, wood, "k_drawers", 0.5);
                    Lamp(R, w / 2 - 0.4, D - 0.3, 0.55, false);
                    Plant(R, -w / 2 + 0.45, 1.0, 1.1);
                    break;
                case RoomKind.Work:
                    {
                        int desks = w > 5.6 ? 2 : 1;
                        for (int i = 0; i < desks; i++)
                        {
                            double x = desks == 1 ? 0 : (i == 0 ? -1.3 : 1.3);
                            Desk(R.At(x, D - 0.45, 0), 1.4);
                            OfficeChair(R.At(x, D - 1.15, 2));
                        }
                        Shelving(R.At(-w / 2 + 0.26, 3.8, 1), 1.6, 1.8, 4, Pick("p_BOX0", "p_BOX1", "p_FRAME0"));
                        Bookcase(R.At(w / 2 - 0.22, 3.6, 3), 0.8);
                        Lamp(R, w / 2 - 0.5, 1.3, 0, true);
                        Plant(R, -w / 2 + 0.45, 1.2, 0.9);
                        FF(R, -0.5, 0.5, D - 0.065, 1.6, 2.5, "k_art" + hr.Next(4));
                        break;
                    }
                case RoomKind.Dining:
                    FT(R, 0, 4.6, 1.7, 1.25, 0.012, "r_" + hr.Next(4));
                    DiningTable(R.At(0, 4.6, 0), 1.8, 0.9, wood);
                    {
                        string cc = Pick(wood, "black", "white", "f_teal");
                        foreach (double x in new[] { -0.6, 0.0, 0.6 }) { Chair(R.At(x, 3.85, 2), cc); Chair(R.At(x, 5.35, 0), cc); }
                    }
                    Pendant(R, 0, 4.6, 1.9);
                    Unit(R.At(0, D - 0.3, 0), 1.8, 0.45, 0.8, wood, "k_drawers", 0.6);
                    FF(R, -0.5, 0.5, D - 0.065, 1.3, 2.3, "k_art" + hr.Next(4));
                    Plant(R, w / 2 - 0.5, D - 0.5, 1.3);
                    break;
                case RoomKind.Kitchen:
                    {
                        bool white = hr.NextDouble() < 0.5;
                        KitchenRun(R.At(-0.35, D - 0.36, 0), w - 1.0, white);
                        FB(R, w / 2 - 0.66, w / 2 - 0.06, D - 0.66, D - 0.06, 0, 2.2, "white");          // the tall fridge
                        FF(R, w / 2 - 0.64, w / 2 - 0.08, D - 0.663, 0.1, 2.15, white ? "k_wall" : "k_ward");
                        FC(R, "Fridge", w / 2 - 0.66, w / 2 - 0.06, D - 0.66, D - 0.06, 0, 2.2);
                        if (w > 5.8) { Island(R.At(0, 4.4, 0), 1.8); Pendant(R, -0.5, 4.4, 1.8); Pendant(R, 0.5, 4.4, 1.8); }
                        else { DiningTable(R.At(0, 4.0, 0), 1.2, 0.8, wood); Chair(R.At(-0.35, 3.4, 2), "white"); Chair(R.At(0.35, 3.4, 2), "white"); }
                        Plant(R, -w / 2 + 0.4, 1.0, 0.8);
                        break;
                    }
                case RoomKind.Bedroom:
                    FT(R, 0, 3.6, 1.3, 0.9, 0.012, "r_" + hr.Next(4));
                    {
                        bool dbl = w > 5.2;
                        Bed(R.At(0, D - 1.1, 0), dbl ? 1.6 : 1.2, dbl);
                        foreach (int k in new[] { -1, 1 })
                        {
                            double x = k * ((dbl ? 0.8 : 0.6) + 0.4);
                            Unit(R.At(x, D - 0.3, 0), 0.45, 0.4, 0.5, wood, "k_drawers", 0.45);
                            Lamp(R, x, D - 0.3, 0.5, false);
                        }
                        Wardrobe(R.At(-w / 2 + 0.36, 2.6, 1), 2.0);
                        Unit(R.At(w / 2 - 0.3, 2.6, 3), 1.2, 0.48, 0.85, wood, "k_drawers", 1.2);
                        FF(R, -0.6, 0.6, D - 0.065, 1.5, 2.4, "k_art" + hr.Next(4));
                        break;
                    }
                case RoomKind.Bathroom:
                    FF(R, -w / 2 + 0.06, w / 2 - 0.06, D - 0.065, 0.0, 2.1, "k_splash");
                    Bath(R.At(-w / 2 + 0.95, D - 0.45, 0));
                    Basin(R.At(w / 2 - 1.1, D - 0.3, 0));
                    Toilet(R.At(w / 2 - 0.4, 4.6, 3));
                    FB(R, w / 2 - 0.1, w / 2 - 0.06, 2.6, 3.4, 1.0, 1.04, "chrome");
                    FB(R, w / 2 - 0.12, w / 2 - 0.07, 2.65, 3.35, 0.55, 1.0, Pick("f_cream", "f_teal", "white", "f_pink"));
                    Shelving(R.At(-w / 2 + 0.26, 3.6, 1), 0.9, 1.6, 4, "p_TOWEL" + hr.Next(2));
                    Plant(R, w / 2 - 0.45, 1.0, 0.7);
                    break;
                case RoomKind.Kids:
                    {
                        string[] kc = { "f_mustard", "f_teal", "f_pink", "f_blue", "orange", "f_green" };
                        BunkBed(R.At(-w / 2 + 0.6, D - 1.2, 0));
                        DiningTable(R.At(1.0, 3.6, 0), 0.7, 0.6, "white");
                        foreach (int k in new[] { -1, 1 }) Chair(R.At(1.0 + k * 0.6, 3.6, k < 0 ? 1 : 3), kc[hr.Next(kc.Length)]);
                        for (int i = 0; i < 3; i++)
                        {
                            HFrame b = R.At(w / 2 - 0.45, D - 0.4 - i * 0.65, 3);
                            FB(b, -0.3, 0.3, -0.2, 0.2, 0, 0.4, kc[(i + hr.Next(6)) % kc.Length]);
                            FT(b, 0, 0, 0.28, 0.18, 0.402, "t_toys");
                        }
                        FT(R, 1.0, 3.6, 1.1, 1.1, 0.012, "r_3");
                        Shelving(R.At(-w / 2 + 0.26, 2.6, 1), 1.2, 1.1, 2, "p_TOY" + hr.Next(2));
                        FF(R, 0.4, 1.4, D - 0.065, 1.2, 2.2, "k_art" + hr.Next(4));
                        break;
                    }
                default:          // storage: wardrobes and drawers
                    Wardrobe(R.At(0, D - 0.36, 0), Math.Min(4.0, w - 1.2));
                    Unit(R.At(-w / 2 + 0.3, 3.4, 1), 1.2, 0.48, 1.1, wood, "k_drawers", 1.2);
                    Shelving(R.At(w / 2 - 0.26, 3.4, 3), 1.6, 1.9, 5, "p_BOX" + hr.Next(2));
                    FT(R, 0, 3.6, 1.2, 0.8, 0.012, "r_" + hr.Next(4));
                    break;
            }
            // somebody looking in
            V2 sp = R.P(0, 1.2), lk = R.P(0, 4.5);
            HSpotXZ(sp, lk);
        }

        // ------------------------------------------------------------------ the showroom: the path, the rows of room sets, signs
        static void HomeShowroom()
        {
            hLevel = 1;
            double y = HY1;
            MeshData[] parts = { HPart("ShowroomA"), HPart("ShowroomB"), HPart("ShowroomC"), HPart("ShowroomD") };
            // rows: mouth v, into (±1), u from, u to, gap?, chunk, kinds by u
            object[][] rows =
            {
                new object[] { 13.0, -1, 26.0, 160.0, false, 0 }, new object[] { 17.0, 1, 26.0, 156.0, true, 0 },
                new object[] { 33.0, -1, 58.15, 156.0, true, 1 }, new object[] { 37.0, 1, 68.0, HW - Hw, true, 1 },
                new object[] { 53.0, -1, 68.0, HW - Hw, true, 2 }, new object[] { 57.0, 1, 58.15, 156.0, true, 2 },
                new object[] { 73.0, -1, 58.15, 156.0, true, 3 }, new object[] { 77.0, 1, 68.0, HW - Hw, true, 3 },
            };
            foreach (object[] row in rows)
            {
                double vm = (double)row[0]; int into = (int)row[1]; double ua = (double)row[2], ub = (double)row[3]; bool gap = (bool)row[4]; int chunk = (int)row[5];
                List<double[]> segs = gap ? new List<double[]> { new[] { ua, 108.0 }, new[] { 112.0, ub } } : new List<double[]> { new[] { ua, ub } };
                foreach (double[] sg in segs)
                {
                    int n = Math.Max(1, (int)Math.Floor((sg[1] - sg[0]) / 6.0 + 0.01));
                    double w = (sg[1] - sg[0]) / n;
                    for (int i = 0; i < n; i++)
                    {
                        double uc = sg[0] + w * (i + 0.5);
                        RoomKind k = KindAt(chunk, into, uc);
                        HFrame R = new HFrame { o = Q(uc, vm), a = into > 0 ? EU : EU * -1, b = EV * into, y = y, m = parts[chunk] };
                        Room(R, w, 8.0, k);
                    }
                }
            }
            // the path: a pale strip with arrows, the way round
            double[][] path = { new[] { 22.0, 15, 158, 15 }, new[] { 158.0, 15, 158, 35 }, new[] { 158.0, 35, 66, 35 }, new[] { 66.0, 35, 66, 55 }, new[] { 66.0, 55, 158, 55 },
                                new[] { 158.0, 55, 158, 75 }, new[] { 158.0, 75, 66, 75 }, new[] { 66.0, 75, 66, 87 }, new[] { 66.0, 87, 150, 87 }, new[] { 150.0, 87, 150, 89.6 } };
            for (int i = 0; i < path.Length; i++)
            {
                double[] p = path[i];
                MeshData m = parts[Math.Min(3, i / 2)];
                V2 a = Q(p[0], p[1]), b = Q(p[2], p[3]), f = (b - a).Norm;
                double len = V2.Dist(a, b);
                HQuad(m, (a + b) * 0.5, f, len / 2 + 1.5, 1.5, y + 0.004, HA("s_path"));
                for (double t = 5; t < len - 2; t += 10) HQuad(m, a + f * t, f * -1, 0.55, 0.55, y + 0.008, HA("x_arrow"));
            }
            // the shortcuts through the rows
            foreach (double[] sc in new[] { new[] { 17.0, 33 }, new[] { 37.0, 53 }, new[] { 57.0, 73 }, new[] { 77.0, 85 } })
            {
                MeshData m = parts[sc[0] < 30 ? 0 : sc[0] < 50 ? 1 : sc[0] < 70 ? 2 : 3];
                HQuad(m, Q(110, (sc[0] + sc[1]) / 2), EV, (sc[1] - sc[0]) / 2, 1.2, y + 0.004, HA("s_path"));
                hd = m; HSign(m, 110, sc[0] + 0.6, EU, 3.2, 0.4, y + 2.9, "g_SHORTCUT");
            }
            // section signs over the path
            object[][] signs =
            {
                new object[] { 30.0, 15.0, "g_LIVING ROOMS", 0 }, new object[] { 94.0, 15.0, "g_WORKSPACES", 0 }, new object[] { 132.0, 15.0, "g_DINING", 0 },
                new object[] { 152.0, 35.0, "g_KITCHENS", 1 }, new object[] { 72.0, 55.0, "g_BEDROOMS", 2 }, new object[] { 152.0, 75.0, "g_CHILDREN'S", 3 },
                new object[] { 104.0, 75.0, "g_BATHROOMS", 3 }, new object[] { 86.0, 75.0, "g_STORAGE", 3 }, new object[] { 66.0, 83.0, "g_CAFE", 3 },
            };
            foreach (object[] sg in signs)
            {
                double u = (double)sg[0], v = (double)sg[1];
                bool alongU = v == 15.0 || v == 35.0 || v == 55.0 || v == 75.0;
                HSign(parts[(int)sg[3]], u, v, alongU ? EV : EU, 3.2, 0.4, y + 2.9, (string)sg[2]);
            }
            // the arrival: the store map, pencils and paper, the showroom sign; lift A is just behind
            {
                MeshData m = parts[0];
                hd = m;
                HB(m, 5.0, 5.08, 21.5, 24.5, y, y + 2.2, "charcoal");
                VQuad(m, Q(5.09, 22.0), Q(5.09, 24.0), y + 0.3, y + 1.9 + 0.1, HA("m_dir"), EU);
                HCol("MapBoard", 4.9, 5.2, 21.5, 24.5, y, y + 2.2);
                HB(m, 8.0, 9.2, 25.0, 25.6, y, y + 0.95, "orange");
                HB(m, 8.1, 9.1, 25.1, 25.5, y + 0.95, y + 1.05, "white");
                HCol("PencilStand", 8.0, 9.2, 25.0, 25.6, y, y + 0.95);
                HSign(m, 20.0, 26.0, EU, 3.2, 0.4, y + 2.6, "g_SHOWROOM");
                HSpot(8.6, 24.2, 8.6, 25.3); HSpot(6.3, 23.0, 5.0, 23.0);
                HItem("measure", "Paper tape measure", false, 8.3, 25.3, y + 1.05, 8.3, 24.3, "orange");
                HStaff(10.5, 26.0, -1, 0);
                // a few displays in the corner by the windows... (no windows: by the front wall) and the dead strip's end wall
                HB(m, 26.0, 26.06, 0.35, 5.0, y, y + 2.8, "w_white"); HCol("RoomWall", 26.0, 26.06, 0.35, 5.0, y, y + 2.8); HWallNote(26.03, 0.35, 26.03, 5.0, y, y + 2.8);
                HB(m, 26.0, 26.06, 25.0, 29.85, y, y + 2.8, "w_white"); HCol("RoomWall", 26.0, 26.06, 25.0, 29.85, y, y + 2.8); HWallNote(26.03, 25.0, 26.03, 29.85, y, y + 2.8);
                for (int i = 0; i < 3; i++) { HFrame P = new HFrame { o = Q(2.0, 6.0 + i * 4.5), a = EV * -1, b = EU * -1, y = y, m = m }; Shelving(P, 2.4, 1.9, 5, "p_BOX" + (i % 2)); }
            }
            // the information point by the warehouse wall (between the kitchens and the bedrooms)
            {
                MeshData m = parts[1]; hd = m;
                HB(m, 59.0, 62.6, 44.0, 45.0, y, y + 1.05, "orange"); HB(m, 58.9, 62.7, 43.9, 45.1, y + 1.05, y + 1.1, "white");
                HCol("InfoDesk", 58.9, 62.7, 43.9, 45.1, y, y + 1.1);
                HSign(m, 60.8, 44.5, EV, 3.2, 0.4, y + 2.6, "g_INFORMATION");
                HStaff(60.8, 46.2, 0, -1);
                HSpot(60.8, 42.6, 60.8, 44.5);
            }
            HStaff(161.5, 66.0, -1, 0);
            // lights: rows of strips hung below the dark roof, and the ducts up there
            for (double v = 3; v < HD - 2; v += 6)
            {
                double u0 = v < 30 ? 3 : 60, u1 = HW - 3;
                SBox(hLights1, Q((u0 + u1) / 2, v), EU, (u1 - u0) / 2, 0.08, 10.8, 10.9, new UVRect(0.5, 0.5, 0.5, 0.5), true);
            }
            MeshData roof = HPart("Roof");
            foreach (double v in new[] { 10.0, 26.0, 46.0, 66.0, 86.0 })
            {
                double u0 = v < 30 ? 2 : 60, u1 = HW - 2;
                HB(roof, u0, u1, v - 0.45, v + 0.45, 11.6, 12.3, "steel", true);
                for (double u = u0 + 6; u < u1; u += 12) HB(roof, u - 0.05, u + 0.05, v - 0.05, v + 0.05, 12.3, HC1, "dark");
            }
            for (double u = 20; u < HW - 5; u += 24) HB(roof, u - 0.3, u + 0.3, 1.0, u < 58 ? 29.0 : HD - 1.0, 12.35, 12.75, "mid", true);
            hLevel = 0;
        }

        static RoomKind KindAt(int chunk, int into, double u)
        {
            switch (chunk)
            {
                case 0: return u < 92 ? RoomKind.Living : u < 130 ? RoomKind.Work : RoomKind.Dining;
                case 1: return RoomKind.Kitchen;
                case 2: return RoomKind.Bedroom;
                default: return u > 110 ? RoomKind.Kids : into < 0 ? RoomKind.Bathroom : RoomKind.Storage;
            }
        }

        // ------------------------------------------------------------------ the café
        static void HomeCafe()
        {
            hLevel = 1;
            double y = HY1;
            MeshData m = HPart("Cafe"); hd = m;
            // the counter: trays, a rail, hot food under glass, desserts, drinks, the tills
            HB(m, 72, 104, 100.3, 101.2, y, y + 0.9, "steel"); HB(m, 71.9, 104.1, 100.25, 101.25, y + 0.9, y + 0.93, "chrome");
            for (int i = 0; i < 8; i++) HQuad(m, Q(74 + i * 3.8, 100.75), EV, 0.35, 1.6, y + 0.935, HA(new[] { "t_hot", "t_hot", "t_salad", "t_hot", "t_cakes", "t_salad", "t_hot", "t_cakes" }[i]));
            HB(m, 71.5, 104.5, 99.55, 100.2, y + 0.82, y + 0.86, "chrome");
            for (double u = 72.5; u < 104; u += 4) HB(m, u - 0.02, u + 0.02, 99.85, 99.9, y, y + 0.82, "chrome");
            HCol("CafeCounter", 71.5, 104.5, 99.55, 101.25, y, y + 0.93);
            // the sneeze guard (breaks)
            {
                MeshData g = new MeshData("CafeGuard_" + si.id, "Shop", ShopGlassMat, false) { navIgnore = true };
                TwoSidedV(g, Q(72.2, 100.25), Q(103.8, 100.25), y + 1.0, y + 1.42, new UVRect(0.5, 0.5, 0.5, 0.5));
                V2 c = Q(88, 100.25);
                si.breakables.Add(new ShopProp { kind = "glass", name = "CafeGuard", mesh = g, cx = c.x, cy = y + 1.21, cz = c.z, sx = 31.6, sy = 0.42, sz = 0.06, yaw = 0, debrisUV = Rect(HA("s_white")) });
                for (double u = 72.2; u <= 104; u += 3.95) HB(m, u - 0.02, u + 0.02, 100.23, 100.27, y + 0.93, y + 1.45, "chrome");
            }
            // trays at the start
            for (int k = 0; k < 10; k++) HB(m, 69.8, 70.8, 100.4, 101.0, y + 0.8 + k * 0.025, y + 0.82 + k * 0.025, "tray");
            HB(m, 69.7, 70.9, 100.3, 101.1, y, y + 0.8, "dark"); HCol("Trays", 69.7, 70.9, 100.3, 101.1, y, y + 1.05);
            // drinks and the dessert fridge (glass, breaks)
            HB(m, 106, 110, 100.3, 101.2, y, y + 0.9, "steel");
            for (int i = 0; i < 4; i++) { HB(m, 106.3 + i, 106.9 + i, 100.6, 101.1, y + 0.9, y + 1.6, i % 2 == 0 ? "charcoal" : "steel"); HB(m, 106.5 + i, 106.7 + i, 100.5, 100.62, y + 1.1, y + 1.2, "black"); }
            HCol("Drinks", 106, 110, 100.3, 101.2, y, y + 1.6);
            HB(m, 110.4, 113.4, 100.6, 101.3, y, y + 0.3, "dark"); HB(m, 110.4, 113.4, 100.6, 101.3, y + 1.9, y + 2.1, "white");
            for (int lv = 0; lv < 4; lv++) HQuad(m, Q(111.9, 100.95), EV, 0.3, 1.45, y + 0.4 + lv * 0.38, HA("t_cakes"));
            {
                MeshData g = new MeshData("CafeFridge_" + si.id, "Shop", ShopGlassMat, false) { navIgnore = true };
                VQuad(g, Q(110.45, 100.58), Q(113.35, 100.58), y + 0.3, y + 1.9, new UVRect(0.5, 0.5, 0.5, 0.5), EV * -1);
                V2 c = Q(111.9, 100.58);
                si.breakables.Add(new ShopProp { kind = "glass", name = "CafeFridge", mesh = g, cx = c.x, cy = y + 1.1, cz = c.z, sx = 2.9, sy = 1.6, sz = 0.06, yaw = 0, debrisUV = Rect(HA("s_white")) });
            }
            HCol("Fridge", 110.4, 113.4, 100.6, 101.3, y, y + 2.1);
            // the tills
            foreach (double u in new[] { 115.5, 118.5 })
            {
                HB(m, u - 0.8, u + 0.8, 100.3, 101.2, y, y + 0.95, "orange");
                HB(m, u - 0.2, u + 0.2, 100.6, 100.9, y + 0.95, y + 1.25, "dark");
                HCol("CafeTill", u - 0.8, u + 0.8, 100.3, 101.2, y, y + 1.25);
                HStaff(u, 102.0, 0, -1);
            }
            HStaff(80, 102.0, 0, -1); HStaff(94, 102.0, 0, -1);
            // the kitchen pass behind, menus on the back wall
            HB(m, 70, 120, 103.2, 103.9, y, y + 0.95, "steel"); HCol("BackCounter", 70, 120, 103.2, 103.9, y, y + 0.95);
            for (int i = 0; i < 4; i++) HB(m, 76 + i * 9, 77.2 + i * 9, 103.3, 103.8, y + 0.95, y + 1.5, i % 2 == 0 ? "charcoal" : "steel");
            HB(m, 70, 120, 105.5, 105.56, y, y + 3.2, "offwhite"); HCol("CafeWall", 70, 120, 105.5, 105.56, y, y + 3.2); HWallNote(70, 105.53, 120, 105.53, y, y + 3.2);
            foreach (double u in new[] { 80.0, 96.0 }) VQuad(m, Q(u - 2, 105.48), Q(u + 2, 105.48), y + 1.4, y + 3.4, HA("m_menu"), EV * -1);
            HSign(m, 88, 99.0, EU, 6.4, 0.8, y + 2.9, "g_FLATPAK");
            HSign(m, 66, 89.0, EU, 3.2, 0.4, y + 2.6, "g_CAFE");
            HSign(m, 150, 88.6, EU, 3.2, 0.4, y + 2.6, "g_MARKET_DOWN");
            // queue spots, tables and chairs
            for (double u = 74; u <= 102; u += 4) HSpot(u, 98.9, u, 100.5);
            HSpot(108, 98.9, 108, 100.7); HSpot(115.5, 99.2, 115.5, 100.7); HSpot(118.5, 99.2, 118.5, 100.7);
            string[] tc = { "birch", "white", "oak" };
            List<double[]> tables = new List<double[]>();
            for (double u = 72; u <= 142.01; u += 5) foreach (double v in new[] { 90.5, 94.5 }) tables.Add(new[] { u, v });
            for (double u = 124; u <= 144.01; u += 5) foreach (double v in new[] { 104.5, 108.0 }) tables.Add(new[] { u, v });
            int ti = 0;
            foreach (double[] t in tables)
            {
                HFrame T = new HFrame { o = Q(t[0], t[1]), a = EU, b = EV, y = y, m = m };
                string w = tc[ti++ % tc.Length];
                FB(T, -0.4, 0.4, -0.4, 0.4, 0.72, 0.75, w);
                FB(T, -0.04, 0.04, -0.04, 0.04, 0, 0.72, "dark"); FB(T, -0.25, 0.25, -0.25, 0.25, 0, 0.03, "dark");
                FC(T, "CafeTable", -0.4, 0.4, -0.4, 0.4, 0, 0.75);
                Chair(T.At(0, -0.62, 2), "black"); Chair(T.At(0, 0.62, 0), "black");
                if (ti % 3 == 0) { FB(T, -0.2, 0.0, -0.15, 0.05, 0.75, 0.77, "tray"); FT(T, -0.1, -0.05, 0.08, 0.08, 0.772, "t_plates"); }
                HSpot(t[0] + 0.95, t[1], t[0], t[1]);
            }
            // the tray return by the way down
            HB(m, 152, 158, 106.6, 107.2, y, y + 1.8, "steel"); HCol("TrayReturn", 152, 158, 106.6, 107.2, y, y + 1.8);
            for (int k = 0; k < 6; k++) HB(m, 152.1, 157.9, 106.65, 107.15, y + 0.2 + k * 0.3, y + 0.23 + k * 0.3, "dark");
            // lights over the tables
            for (double v = 89; v < 109; v += 4) SBox(hLights1, Q(108, v), EU, 40, 0.08, 10.8, 10.9, new UVRect(0.5, 0.5, 0.5, 0.5), true);
            foreach (double[] t in tables) if ((int)(t[0]) % 10 == 2) Pendant(new HFrame { o = Q(t[0], t[1]), a = EU, b = EV, y = y, m = m }, 0, 0, 2.2);
            hLevel = 0;
        }
    }
}
