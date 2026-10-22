// L3: street furniture by district, added at the end of the dressing (Lundun → 13), after everything else is placed,
// with its own random seed, so nothing that was already there moves.
//   Knightsbury  black cast-iron area railings in front of the terraces and mansion blocks
//   Peckam       low brick front-garden walls with gates and wheelie bins; Rye Lane market stalls; bollard runs on the
//                parades; corner pubs get their names and hanging signs
//   Gilmore      chain-link fencing in front of the blocks
//   Canary       planters along the pavements; cycle stands outside offices
//   everywhere   green street cabinets, roadworks (barriers, cones, signs, a dug-up patch), scaffolding on a few
//                buildings, cycle stands by the shops
// Bollards themselves now differ by district (in Bollard): heritage cast iron in Knightsbury and Peckam, stainless
// steel in Canary Quay, concrete on the estate.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        static Random frng;
        static readonly string[] KitPubNames = { "THE BOLD STAG", "THE IRON KETTLE", "THE PECKAM VAULTS", "THE COCK & LANTERN", "THE GREYHOUND & HARE", "THE RYE TAVERN" };
        const double FPaveY = 0.15;

        // ------------------------------------------------------------------ atlas additions (appended: nothing earlier moves)
        static void FurnitureAtlas(DressAtlas a)
        {
            foreach (var kv in new Dictionary<string, string> {
                { "castiron", "1b1c1e" }, { "steel", "b6bbbf" }, { "fencegreen", "2c4a33" }, { "cabinet", "2f4d3b" }, { "cabinettop", "23382c" },
                { "wheelie", "2c2f31" }, { "wheeliegreen", "2f5a33" }, { "wheelieblue", "27497e" }, { "wheeliebrown", "5a3a24" }, { "recycle", "1e5aa8" },
                { "scaffold", "9da3a8" }, { "board", "8f7a55" }, { "barrierred", "c8102e" }, { "conebase", "202020" }, { "planter", "8a8680" },
                { "produceR", "b3261e" }, { "produceO", "e07b1a" }, { "produceY", "e8c11e" }, { "produceG", "4f8a2a" }, { "producePurple", "5a2a5a" },
                { "pubsign", "1f3b2c" }, { "soil", "3a2c20" }, { "redbrick", "8a4632" } })
                a.Swatch(kv.Key, Col.Hex(kv.Value));
            Stripes(a, "awning_red", Col.Hex("c8102e"), Col.Hex("f2efe6"));
            Stripes(a, "awning_green", Col.Hex("1f6b3a"), Col.Hex("f2efe6"));
            Stripes(a, "awning_blue", Col.Hex("1d4fb8"), Col.Hex("f2efe6"));
            BarrierPlank(a, "barrierplank");
            a.Sign("sign_ROADWORKS", 128, 48, "ROAD WORKS", Col.Hex("ffffff"), Col.Hex("111111"), Col.Hex("c8102e"), 0.5);
            a.Sign("sign_FOOTWAY", 128, 48, "FOOTWAY CLOSED", Col.Hex("ffffff"), Col.Hex("111111"), Col.Hex("c8102e"), 0.45);
            foreach (string p in KitPubNames) a.Sign("kpub_" + p, 320, 40, p, Col.Hex("1f3b2c"), Col.Hex("d9b45a"), new Col(0, 0, 0, 0), 0.42);
            foreach (string p in KitPubNames) HangingSign(a, "kpubhang_" + p, p);
        }

        static void Stripes(DressAtlas a, string name, Col c0, Col c1)
        {
            int x, y; a.Alloc(name, 64, 24, out x, out y);
            for (int i = 0; i < 8; i++) a.Fill(x + i * 8, y, 8, 24, i % 2 == 0 ? c0 : c1);
            a.Fill(x, y, 64, 3, c0.Scale(0.7));                         // scalloped edge shadow
        }

        static void BarrierPlank(DressAtlas a, string name)
        {
            int x, y; a.Alloc(name, 128, 16, out x, out y);
            for (int i = 0; i < 128; i++)
                for (int j = 0; j < 16; j++)
                {
                    bool red = ((i + j) / 12) % 2 == 0;                          // diagonal red / white bands
                    a.Put(x + i, y + j, red ? Col.Hex("c8102e") : Col.Hex("f4f4f0"), 1);
                }
            a.Frame(x, y, 128, 16, 1, Col.Hex("8a0b1a"));
        }

        static void HangingSign(DressAtlas a, string name, string pub)
        {
            int x, y; a.Alloc(name, 48, 60, out x, out y);
            a.Fill(x, y, 48, 60, Col.Hex("1f3b2c"));
            a.Frame(x + 2, y + 2, 44, 56, 2, Col.Hex("c9a24a"));
            string[] words = pub.Replace("THE ", "").Split(' ');
            int lines = Math.Min(3, words.Length);
            for (int i = 0; i < lines; i++)
                a.Text(words[i], x + 4, y + 60 - 8 - (i + 1) * 15, 40, 13, Col.Hex("d9b45a"), 0.45, DressAtlas.Align.Centre);
        }

        // ------------------------------------------------------------------ bollards by district (called by Bollard)
        static void BollardStyled(V2 p, double y)
        {
            switch (District(p))
            {
                case "D_KNIGHT":
                case "D_PECKAM":
                    {
                        // heritage cast iron: wider foot, tapering post, collar, domed cap (Knightsbury's have a gold ring)
                        bool posh = District(p) == "D_KNIGHT";
                        Cyl(mSolid, p, 0.14, y, y + 0.1, 8, S("castiron"), false);
                        Band(mSolid, p, 0.11, y + 0.1, 0.085, y + 0.78, 8, S("castiron"), 0);
                        Cyl(mSolid, p, 0.105, y + 0.72, y + 0.8, 8, S(posh ? "gold" : "castiron"), false);
                        Band(mSolid, p, 0.1, y + 0.8, 0.02, y + 0.95, 8, S("castiron"), 0);
                        break;
                    }
                case "D_CANARY":
                case "D_CANARY_PEN":
                    Cyl(mSolid, p, 0.1, y, y + 0.95, 10, S("steel"));
                    Cyl(mSolid, p, 0.102, y + 0.82, y + 0.86, 10, S("charcoal"), false);
                    break;
                case "D_GILMOR":
                    Cyl(mSolid, p, 0.17, y, y + 0.68, 8, S("concrete"), false);
                    Band(mSolid, p, 0.17, y + 0.68, 0.06, y + 0.78, 8, S("concrete"), 0);
                    break;
                default:
                    Cyl(mSolid, p, 0.1, y, y + 0.9, 6, S("black"));
                    Cyl(mSolid, p, 0.105, y + 0.72, y + 0.8, 6, S("white"), false);
                    break;
            }
        }

        // ------------------------------------------------------------------ the pass
        static void DressFurniture()
        {
            frng = new Random(3030);
            foreach (BuildingInfo b in blds)
            {
                KitPlan p = KitPlanFor(b);
                if (p == null) continue;
                switch (p.type)
                {
                    case KitType.Stucco: if (!p.mixed) FrontRailings(p, 6.4, 1.16, 1.6); break;
                    case KitType.Georgian: FrontRailings(p, 5.333, 0.9, 1.3); break;
                    case KitType.Mansion: if (!p.mixed) FrontRailings(p, 8.0, 4.0, 1.8); break;
                    case KitType.Terrace: FrontGardens(p); break;
                    case KitType.Pub: PubSigns(p); break;
                    case KitType.Parade: if (frng.NextDouble() < 0.35) KerbBollards(p); if (frng.NextDouble() < 0.25) CycleStands(p); break;
                    case KitType.Office: if (frng.NextDouble() < 0.5) CycleStands(p); break;
                    case KitType.Deck: case KitType.Maisonette: case KitType.EstTower: EstateFence(p); break;
                }
            }
            Market();
            Planters();
            Cabinets();
            Roadworks();
            Scaffolds();
        }

        // ------------------------------------------------------------------ helpers in a plot's frame
        static V2 PP(KitPlan p, double s, double d) { return p.c + p.r * s + p.n * (p.D / 2 - d); }
        static double FrontEdge(KitPlan p) { return -(Math.Max(0, p.gap) - 0.12); }     // d of the pavement's back edge

        /// Door positions along a terrace (s), the same rule as the building kit (houses alternate handedness).
        static List<double> Doors(KitPlan p, double unitW, double doorAt)
        {
            List<double> r = new List<double>();
            if (unitW >= 7.9)
            {
                int n = Math.Max(1, (int)Math.Round(p.W / unitW));
                for (int i = 0; i < n; i++) r.Add(-p.W / 2 + (i + 0.5) * p.W / n);
                return r;
            }
            int houses = Math.Max(2, (int)Math.Round(p.W / unitW));
            double hw = p.W / houses, k = hw / unitW;
            for (int i = 0; i < houses; i++)
            {
                double s0 = -p.W / 2 + i * hw, s1 = s0 + hw;
                r.Add(i % 2 == 1 ? s1 - doorAt * k : s0 + doorAt * k);
            }
            return r;
        }

        /// Pieces of [-W/2, W/2] with openings of width `gap` at each door.
        static List<double[]> Runs(KitPlan p, List<double> doors, double gap)
        {
            List<double[]> r = new List<double[]>();
            double s = -p.W / 2 + 0.15;
            List<double> d = new List<double>(doors); d.Sort();
            foreach (double c in d)
            {
                if (c - gap / 2 > s + 0.4) r.Add(new[] { s, c - gap / 2 });
                s = Math.Max(s, c + gap / 2);
            }
            if (p.W / 2 - 0.15 > s + 0.4) r.Add(new[] { s, p.W / 2 - 0.15 });
            return r;
        }

        // ------------------------------------------------------------------ Knightsbury: area railings
        static void FrontRailings(KitPlan p, double unitW, double doorAt, double opening)
        {
            if (p.gap < 1.1 || p.gap > 6) return;
            double d = FrontEdge(p) + 0.1;
            foreach (double[] run in Runs(p, Doors(p, unitW, doorAt), opening))
            {
                V2 a = PP(p, run[0], d), b = PP(p, run[1], d);
                Railing(a, b, FPaveY, 1.05);
                // railings face the street both ways: a second set of panels facing the house
                double L = V2.Dist(a, b); V2 f = (b - a).Norm;
                int panels = Math.Max(1, (int)Math.Round(L / 2.5));
                for (int i = 0; i < panels; i++) VQuad(mCut, a + f * (L * i / panels), a + f * (L * (i + 1) / panels), FPaveY, FPaveY + 1.05, A["railing"], f.Left * -1);
                Collider("Railing", (a + b) * 0.5, f, L, 0.08, FPaveY, FPaveY + 1.05);
            }
            D.Count("area railings (houses)");
        }

        // ------------------------------------------------------------------ Peckam: front garden walls, gates, bins
        static void FrontGardens(KitPlan p)
        {
            if (p.gap < 1.4 || p.gap > 6) return;
            double d = FrontEdge(p) + 0.12;
            List<double> doors = Doors(p, 5.333, 0.925);
            UVRect brick = S(frng.NextDouble() < 0.6 ? "redbrick" : "brick");
            foreach (double[] run in Runs(p, doors, 1.0))
            {
                V2 a = PP(p, run[0], d), b = PP(p, run[1], d);
                V2 f = (b - a).Norm; double L = V2.Dist(a, b);
                SBox(mSolid, (a + b) * 0.5, f, L / 2, 0.11, FPaveY, FPaveY + 0.55, brick);
                SBox(mSolid, (a + b) * 0.5, f, L / 2 + 0.02, 0.14, FPaveY + 0.55, FPaveY + 0.62, S("concrete"));   // coping
                foreach (V2 e in new[] { a, b }) SBox(mSolid, e, f, 0.17, 0.17, FPaveY, FPaveY + 0.78, brick);       // gate piers
                Collider("GardenWall", (a + b) * 0.5, f, L, 0.25, FPaveY, FPaveY + 0.62);
            }
            // wheelie bins in the front gardens, the odd recycling box
            foreach (double s in doors)
            {
                double gd = (FrontEdge(p) + 0.12) / 2;                     // halfway between wall and house
                if (frng.NextDouble() < 0.75)
                {
                    int n = 1 + (frng.NextDouble() < 0.5 ? 1 : 0);
                    for (int i = 0; i < n; i++)
                    {
                        double bs = s + (i == 0 ? 1.3 : 2.0) * (frng.NextDouble() < 0.5 ? 1 : -1);
                        if (Math.Abs(bs) > p.W / 2 - 0.5) continue;
                        V2 q = PP(p, bs, gd);
                        string col = new[] { "wheelie", "wheelie", "wheeliegreen", "wheelieblue", "wheeliebrown" }[frng.Next(5)];
                        WheelieBin(q, p.n * -1, col);
                    }
                }
                if (frng.NextDouble() < 0.25)
                {
                    V2 q = PP(p, s + 0.9, gd + 0.3);
                    SBox(mSolid, q, p.r, 0.3, 0.22, 0, 0.32, S("recycle"));
                }
            }
            D.Count("front gardens");
        }

        static void WheelieBin(V2 q, V2 facing, string col)
        {
            V2 f = facing.Norm;
            SBox(mSolid, q, f.Left, 0.29, 0.36, 0.0, 1.0, S(col));
            SBox(mSolid, q - f * 0.03, f.Left, 0.31, 0.4, 1.0, 1.06, S(col));                    // lid
            Cyl(mSolid, q - f * 0.3 + f.Left * 0.24, 0.1, 0.0, 0.2, 6, S("black"));                  // wheels (hint)
            Cyl(mSolid, q - f * 0.3 - f.Left * 0.24, 0.1, 0.0, 0.2, 6, S("black"));
            Claim(q, 0.45);
            D.Count("wheelie bins");
        }

        // ------------------------------------------------------------------ pubs: names and hanging signs
        static void PubSigns(KitPlan p)
        {
            string name = KitPubNames[(int)(Math.Abs(p.c.x * 13 + p.c.z * 7)) % KitPubNames.Length];
            V2 a = PP(p, -p.W / 2 + 0.4, -0.06), b = PP(p, p.W / 2 - 0.4, -0.06);
            VQuad(mGlow, a, b, 3.12, 3.78, A["kpub_" + name], p.n);
            // hanging sign on a bracket at the front corner nearest a junction
            foreach (int side in new[] { -1, 1 })
            {
                V2 wallPt = PP(p, side * (p.W / 2 - 0.6), 0);
                V2 arm = wallPt + p.n * 0.9;
                SBox(mSolid, (wallPt + arm) * 0.5, p.n, 0.45, 0.03, 5.3, 5.36, S("castiron"));
                SBox(mSolid, wallPt + p.n * 0.05, p.n, 0.05, 0.04, 4.6, 5.4, S("castiron"));
                V2 sc = arm + p.n * -0.1;
                SBox(mSolid, sc, p.n, 0.38, 0.03, 4.4, 5.25, S("pubsign"));
                VQuad(mGlow, sc - p.n * 0.36 + p.r * 0.04, sc + p.n * 0.36 + p.r * 0.04, 4.45, 5.2, A["kpubhang_" + name], p.r);
                VQuad(mGlow, sc + p.n * 0.36 - p.r * 0.04, sc - p.n * 0.36 - p.r * 0.04, 4.45, 5.2, A["kpubhang_" + name], p.r * -1);
                // a lantern either side of the door
                SBox(mGlow, PP(p, side * 1.2, -0.25), p.r, 0.12, 0.12, 2.75, 3.0, S("yellow"));
                break;
            }
            D.Count("kit pubs signed");
        }

        // ------------------------------------------------------------------ parade kerbs
        static void KerbBollards(KitPlan p)
        {
            double ch, lat;
            RoadInfo ri = NearestStreet(PP(p, 0, -p.gap - 1), out ch, out lat);
            if (ri == null || ri.pave < 2.5) return;
            int s = lat >= 0 ? 1 : -1;
            int n = 4 + frng.Next(4);
            for (int i = 0; i < n; i++)
            {
                V2 dir; V2 q = ri.line.At(ch + (i - n / 2.0) * 1.6, out dir) + dir.Left * (s * (ri.w / 2 + 0.45));
                if (!Free(q, 0.35)) continue;
                Bollard(q, FPaveY);
                Collider("Bollard", q, dir, 0.25, 0.25, FPaveY, FPaveY + 0.95);
            }
        }

        static RoadInfo NearestStreet(V2 q, out double ch, out double lat)
        {
            RoadInfo best = null; double bd = double.MaxValue; ch = 0; lat = 0;
            foreach (RoadInfo ri in roads.Values)
            {
                if (ri.cls == "bridge" || ri.pave <= 0) continue;
                double d = ri.line.DistanceTo(q);
                if (d < bd) { bd = d; best = ri; }
            }
            if (best == null || bd > best.w / 2 + best.pave + 8) return null;
            ch = Project(best, q, out lat);
            return best;
        }

        static void CycleStands(KitPlan p)
        {
            double ch, lat;
            RoadInfo ri = NearestStreet(PP(p, 0, -p.gap - 1), out ch, out lat);
            if (ri == null || ri.pave < 3) return;
            int s = lat >= 0 ? 1 : -1;
            for (int i = 0; i < 3 + frng.Next(3); i++)
            {
                V2 dir; V2 q = ri.line.At(ch + 3 + i * 1.0, out dir) + dir.Left * (s * (ri.w / 2 + 0.9));
                if (!Free(q, 0.4)) continue;
                V2 a = dir.Left;              // stands sit across the pavement
                foreach (int k in new[] { -1, 1 }) SBox(mSolid, q + a * (0.33 * k), dir, 0.025, 0.025, FPaveY, FPaveY + 0.75, S("steel"));
                SBox(mSolid, q, a, 0.36, 0.025, FPaveY + 0.72, FPaveY + 0.77, S("steel"));
                Claim(q, 0.45);
                D.Count("cycle stands");
            }
        }

        // ------------------------------------------------------------------ Gilmore: chain-link along the block fronts
        static void EstateFence(KitPlan p)
        {
            if (p.gap < 1.5 || p.gap > 8 || frng.NextDouble() < 0.35) return;
            double d = FrontEdge(p) + 0.1;
            double gate = (frng.NextDouble() - 0.5) * p.W * 0.5;
            foreach (double[] run in new[] { new[] { -p.W / 2, gate - 1.2 }, new[] { gate + 1.2, p.W / 2 } })
            {
                if (run[1] - run[0] < 1) continue;
                V2 a = PP(p, run[0], d), b = PP(p, run[1], d);
                V2 f = (b - a).Norm; double L = V2.Dist(a, b);
                int panels = Math.Max(1, (int)Math.Round(L / 2.5));
                for (int i = 0; i < panels; i++)
                {
                    V2 q0 = a + f * (L * i / panels), q1 = a + f * (L * (i + 1) / panels);
                    VQuad(mCut, q0, q1, FPaveY, FPaveY + 1.8, A["chainlink"], p.n);
                    VQuad(mCut, q1, q0, FPaveY, FPaveY + 1.8, A["chainlink"], p.n * -1);
                }
                for (int i = 0; i <= panels; i++) Cyl(mSolid, a + f * (L * i / panels), 0.04, FPaveY, FPaveY + 1.85, 6, S("fencegreen"));
                SBox(mSolid, (a + b) * 0.5, f, L / 2, 0.025, FPaveY + 1.78, FPaveY + 1.83, S("fencegreen"));
                Collider("Fence", (a + b) * 0.5, f, L, 0.1, FPaveY, FPaveY + 1.8);
            }
            D.Count("estate fences");
        }

        // ------------------------------------------------------------------ Rye Lane market
        static void Market()
        {
            RoadInfo rye = null;
            foreach (RoadInfo ri in roads.Values) if (ri.r.name == "Rye Lane") rye = ri;
            if (rye == null) return;
            double L = rye.line.Length;
            // the longest clear stretch on the wider side, away from junctions
            foreach (int s in new[] { 1, -1 })
            {
                List<double[]> cuts = Concat(rye.boxCut, s > 0 ? rye.trimL : rye.trimR, s > 0 ? rye.boxCutL : rye.boxCutR);
                foreach (double[] iv in Keep(L, cuts, 40))
                {
                    double a = iv[0] + 12, b = Math.Min(iv[1] - 12, a + 55);
                    int made = 0;
                    for (double ch = a; ch + 2.8 < b; ch += 3.3)
                    {
                        V2 dir; V2 kerb = rye.line.At(ch + 1.4, out dir);
                        V2 out_ = dir.Left * s;
                        V2 c = kerb + out_ * (rye.w / 2 + 1.1);
                        if (!Free(c, 1.3)) continue;
                        Stall(c, dir, out_);
                        made++;
                    }
                    if (made > 0) { D.Count("market stalls", made); return; }
                }
            }
        }

        /// A market stall: back to the kerb, customers on the shop side. facing = towards the shops.
        static void Stall(V2 c, V2 along, V2 facing)
        {
            V2 f = facing.Norm, a = along.Norm;
            double hl = 1.35, hw = 0.75;
            // posts
            foreach (int i in new[] { -1, 1 }) foreach (int j in new[] { -1, 1 })
                    SBox(mSolid, c + a * (hl * i) + f * (hw * j), a, 0.025, 0.025, FPaveY, FPaveY + (j > 0 ? 2.1 : 2.4), S("metal"));
            // table and produce
            SBox(mSolid, c + f * 0.2, a, hl, 0.5, FPaveY + 0.78, FPaveY + 0.84, S("wood"));
            string[] cols = { "produceR", "produceO", "produceY", "produceG", "producePurple", "leaf2" };
            for (int k = 0; k < 4; k++)
            {
                V2 q = c + f * 0.25 + a * (-hl + 0.35 + k * 0.67);
                SBox(mSolid, q, a, 0.3, 0.22, FPaveY + 0.84, FPaveY + 0.98, S("board"));
                SBox(mSolid, q, a, 0.27, 0.19, FPaveY + 0.98, FPaveY + 1.06, S(cols[frng.Next(cols.Length)]));
            }
            SBox(mSolid, c - f * 0.45, a, hl - 0.1, 0.25, FPaveY, FPaveY + 0.6, S("board"));        // crates behind
            // striped awning, sloping down towards the customers (both faces)
            UVRect aw = A[new[] { "awning_red", "awning_green", "awning_blue" }[frng.Next(3)]];
            V2 b0 = c - f * (hw + 0.1), b1 = c + f * (hw + 0.25);
            double y0 = FPaveY + 2.45, y1 = FPaveY + 2.12;
            P3 p00 = new P3(b0 - a * (hl + 0.1), y0, aw.u0, aw.v1), p01 = new P3(b0 + a * (hl + 0.1), y0, aw.u1, aw.v1),
               p11 = new P3(b1 + a * (hl + 0.1), y1, aw.u1, aw.v0), p10 = new P3(b1 - a * (hl + 0.1), y1, aw.u0, aw.v0);
            mSolid.Quad(p00, p01, p11, p10, f.x * 0.3, 1, f.z * 0.3);
            mSolid.Quad(p00, p01, p11, p10, -f.x * 0.3, -1, -f.z * 0.3);
            VQuad(mSolid, b1 - a * (hl + 0.1), b1 + a * (hl + 0.1), y1 - 0.25, y1, aw, f);           // valance
            Claim(c, 1.6);
            Collider("Stall", c, a, hl * 2, hw * 2, FPaveY, FPaveY + 1.0);
        }

        // ------------------------------------------------------------------ Canary planters
        static void Planters()
        {
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.pave < 3 || ri.cls == "bridge") continue;
                V2 dm; V2 mid = ri.line.At(ri.line.Length / 2, out dm);
                string dist = District(mid);
                if (dist != "D_CANARY" && dist != "D_CANARY_PEN") continue;
                double L = ri.line.Length;
                for (int s = 1; s >= -1; s -= 2)
                {
                    List<double[]> cuts = Concat(ri.boxCut, s > 0 ? ri.trimL : ri.trimR, s > 0 ? ri.boxCutL : ri.boxCutR);
                    foreach (double[] iv in Keep(L, cuts, 20))
                        for (double ch = iv[0] + 8; ch < iv[1] - 6; ch += 22 + frng.NextDouble() * 10)
                        {
                            V2 dir; V2 q = ri.line.At(ch, out dir) + dir.Left * (s * (ri.w / 2 + ri.pave - 0.7));
                            if (!Free(q, 1.1)) continue;
                            SBox(mSolid, q, dir, 0.85, 0.42, FPaveY, FPaveY + 0.6, S("planter"));
                            SBox(mSolid, q, dir, 0.78, 0.35, FPaveY + 0.6, FPaveY + 0.62, S("soil"));
                            UVRect leaf = S("leaf" + (1 + frng.Next(3)));
                            for (int k = -1; k <= 1; k++)
                                Band(mSolid, q + dir * (0.5 * k), 0.32, FPaveY + 0.6, 0.12, FPaveY + 1.1 + frng.NextDouble() * 0.3, 6, leaf, frng.NextDouble());
                            Claim(q, 1.0);
                            Collider("Planter", q, dir, 1.7, 0.84, FPaveY, FPaveY + 0.6);
                            D.Count("planters");
                        }
                }
            }
        }

        // ------------------------------------------------------------------ green street cabinets
        /// L4: street cabinets (x, z, along x, along z, side of the road face): tags and stickers go on them.
        static List<double[]> cabinetSpots = new List<double[]>();
        static void Cabinets()
        {
            cabinetSpots = new List<double[]>();
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.pave < 2.5 || ri.cls == "bridge") continue;
                double L = ri.line.Length;
                int s = frng.NextDouble() < 0.5 ? 1 : -1;
                List<double[]> cuts = Concat(ri.boxCut, s > 0 ? ri.trimL : ri.trimR, s > 0 ? ri.boxCutL : ri.boxCutR);
                foreach (double[] iv in Keep(L, cuts, 30))
                    for (double ch = iv[0] + 10 + frng.NextDouble() * 40; ch < iv[1] - 8; ch += 120 + frng.NextDouble() * 80)
                    {
                        V2 dir; V2 q = ri.line.At(ch, out dir) + dir.Left * (s * (ri.w / 2 + ri.pave - 0.35));
                        if (!Free(q, 0.7)) continue;
                        SBox(mSolid, q, dir, 0.5, 0.22, FPaveY, FPaveY + 1.25, S("cabinet"));
                        cabinetSpots.Add(new[] { q.x, q.z, dir.x, dir.z, -s });      // L4: the face towards the road gets tagged
                        SBox(mSolid, q, dir, 0.53, 0.25, FPaveY + 1.25, FPaveY + 1.3, S("cabinettop"));
                        Claim(q, 0.7);
                        Collider("Cabinet", q, dir, 1.0, 0.44, FPaveY, FPaveY + 1.3);
                        D.Count("street cabinets");
                    }
            }
        }

        // ------------------------------------------------------------------ roadworks
        /// Where roadworks went (x, z, radius): L2/L3 keep parked cars out of them.
        static List<double[]> worksZones = new List<double[]>();
        public static List<double[]> WorksZones { get { return worksZones; } }

        static void Roadworks()
        {
            worksZones = new List<double[]>();
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            List<RoadInfo> cands = new List<RoadInfo>();
            foreach (string id in ids) { RoadInfo ri = roads[id]; if ((ri.cls == "street" || ri.cls == "arterial") && ri.pave > 0) cands.Add(ri); }
            int sites = 0;
            for (int t = 0; t < 60 && sites < 9; t++)
            {
                RoadInfo ri = cands[frng.Next(cands.Count)];
                double L = ri.line.Length;
                int s = frng.NextDouble() < 0.5 ? 1 : -1;
                List<double[]> keep = Keep(L, Concat(ri.boxCut, s > 0 ? ri.trimL : ri.trimR, s > 0 ? ri.boxCutL : ri.boxCutR), 40);
                if (keep.Count == 0) continue;
                double[] iv = keep[frng.Next(keep.Count)];
                double ch = iv[0] + 12 + frng.NextDouble() * (iv[1] - iv[0] - 24);
                V2 dir; V2 c = ri.line.At(ch, out dir);
                bool near = false; foreach (double[] z in worksZones) if (V2.Dist(new V2(z[0], z[1]), c) < 150) near = true;
                if (near || InBox(c)) continue;
                V2 l = dir.Left * s;
                bool footway = frng.NextDouble() < 0.35 && ri.pave >= 3;
                if (footway) FootwayWorks(ri, c, dir, l);
                else CarriagewayWorks(ri, c, dir, l);
                worksZones.Add(new[] { c.x, c.z, 9.0 });
                sites++;
            }
            D.Count("roadworks", sites);
        }

        static void CarriagewayWorks(RoadInfo ri, V2 c, V2 dir, V2 l)
        {
            // a barriered rectangle against the kerb, 6 m long, 2.2 m out; a dug patch inside; cones taper in; a sign
            double hw = ri.w / 2, inner = hw - 1.75, y = 0.02;        // no wider than a parking bay: traffic gets past
            V2 k0 = c - dir * 3 + l * (hw - 0.15), k1 = c + dir * 3 + l * (hw - 0.15), o0 = c - dir * 3 + l * inner, o1 = c + dir * 3 + l * inner;
            BarrierRun(k0, o0, y); BarrierRun(o0, o1, y); BarrierRun(o1, k1, y);
            HQuad(mSolid, c + l * (hw - 1.2), dir, 2.4, 0.8, y + 0.012, S("pit"));
            HQuad(mSolid, c + l * (hw - 1.2), dir, 2.6, 0.95, y + 0.008, S("soil"));
            for (int i = 1; i <= 5; i++)
            {
                V2 q = c - dir * (3 + i * 2.2) + l * (inner + (hw - 0.4 - inner) * i / 6.0);
                Cone(q, y);
            }
            Sign(c - dir * 18 + l * (hw - 0.6), dir * -1, y, "sign_ROADWORKS");
            Collider("Works", c + l * ((hw + inner) / 2), dir, 6.2, hw - inner, y, y + 1.0);
        }

        static void FootwayWorks(RoadInfo ri, V2 c, V2 dir, V2 l)
        {
            double hw = ri.w / 2, y = FPaveY;
            V2 a0 = c - dir * 2.5 + l * (hw + 0.4), a1 = c + dir * 2.5 + l * (hw + 0.4), b0 = c - dir * 2.5 + l * (hw + ri.pave - 0.3), b1 = c + dir * 2.5 + l * (hw + ri.pave - 0.3);
            BarrierRun(a0, a1, y); BarrierRun(a1, b1, y); BarrierRun(b1, b0, y); BarrierRun(b0, a0, y);
            HQuad(mSolid, c + l * (hw + ri.pave / 2), dir, 2.0, (ri.pave - 1.0) / 2, y + 0.01, S("pit"));
            Sign(c - dir * 4 + l * (hw + 0.8), dir * -1, y, "sign_FOOTWAY");
            Collider("Works", c + l * (hw + ri.pave / 2), dir, 5.2, ri.pave - 0.6, y, y + 1.0);
        }

        /// Red/white plastic barrier panels on feet, 1 m tall, both faces.
        static void BarrierRun(V2 a, V2 b, double y)
        {
            double L = V2.Dist(a, b); if (L < 0.3) return;
            V2 f = (b - a).Norm, n = f.Left;
            int n2 = Math.Max(1, (int)Math.Round(L / 2.0));
            UVRect pl = A["barrierplank"];
            for (int i = 0; i < n2; i++)
            {
                V2 p = a + f * (L * i / n2), q = a + f * (L * (i + 1) / n2);
                foreach (double h in new[] { 0.55, 0.85 })
                {
                    VQuad(mSolid, p, q, y + h, y + h + 0.15, pl, n);
                    VQuad(mSolid, q, p, y + h, y + h + 0.15, pl, n * -1);
                }
                SBox(mSolid, p, f, 0.03, 0.03, y, y + 1.02, S("barrierred"));
                SBox(mSolid, p, n, 0.32, 0.06, y, y + 0.06, S("conebase"));                       // foot
            }
            SBox(mSolid, b, f, 0.03, 0.03, y, y + 1.02, S("barrierred"));
            for (double s = 0; s <= L; s += 1) Claim(a + f * s, 0.4);
        }

        static void Cone(V2 q, double y)
        {
            SBox(mSolid, q, new V2(0, 1), 0.2, 0.2, y, y + 0.04, S("conebase"));
            Band(mSolid, q, 0.15, y + 0.04, 0.03, y + 0.72, 6, S("orange"), 0);
            Band(mSolid, q, 0.1, y + 0.32, 0.075, y + 0.46, 6, S("white"), 0);
            Claim(q, 0.3);
        }

        static void Sign(V2 q, V2 facing, double y, string sign)
        {
            V2 f = facing.Norm, a = f.Left;
            foreach (int k in new[] { -1, 1 }) SBox(mSolid, q + a * (0.35 * k), f, 0.02, 0.02, y, y + 0.9, S("castiron"));
            VQuad(mSolid, q - a * 0.45, q + a * 0.45, y + 0.9, y + 1.25, A[sign], f);
            VQuad(mSolid, q + a * 0.45 - f * 0.01, q - a * 0.45 - f * 0.01, y + 0.9, y + 1.25, A[sign], f * -1);
            Claim(q, 0.6);
        }

        // ------------------------------------------------------------------ scaffolding
        static void Scaffolds()
        {
            int made = 0;
            foreach (BuildingInfo b in blds)
            {
                if (made >= 6) break;
                KitPlan p = KitPlanFor(b);
                if (p == null || p.eave <= 0 || p.gap < 1.6 || p.gap > 6) continue;
                bool ok = (p.type == KitType.Terrace || p.type == KitType.Parade || p.type == KitType.Stucco || p.type == KitType.Georgian);
                if (!ok || frng.NextDouble() > 0.2) continue;
                Scaffold(p);
                made++;
            }
            D.Count("scaffolds", made);
        }

        static void Scaffold(KitPlan p)
        {
            double top = p.eave + 1.0, dIn = -0.3, dOut = -1.35, lift = 2.0;
            double s0 = -p.W / 2 + 0.3, s1 = p.W / 2 - 0.3;
            int bays = Math.Max(1, (int)Math.Round((s1 - s0) / 2.4));
            UVRect tube = S("scaffold"), board = S("board");
            for (int i = 0; i <= bays; i++)
            {
                double s = s0 + (s1 - s0) * i / bays;
                foreach (double d in new[] { dIn, dOut })
                {
                    V2 q = PP(p, s, d);
                    SBox(mSolid, q, p.r, 0.024, 0.024, FPaveY, FPaveY + top, tube);
                    SBox(mSolid, q, p.r, 0.12, 0.12, FPaveY, FPaveY + 0.02, board);              // sole board
                }
            }
            for (double y = FPaveY + lift; y < FPaveY + top; y += lift)
            {
                foreach (double d in new[] { dIn, dOut })
                {
                    V2 a = PP(p, s0, d), b = PP(p, s1, d);
                    SBox(mSolid, (a + b) * 0.5, p.r, (s1 - s0) / 2, 0.024, y - 0.024, y + 0.024, tube);     // ledgers
                }
                V2 m0 = PP(p, s0, (dIn + dOut) / 2), m1 = PP(p, s1, (dIn + dOut) / 2);
                SBox(mSolid, (m0 + m1) * 0.5, p.r, (s1 - s0) / 2, (dIn - dOut) / 2 - 0.05, y + 0.03, y + 0.08, board);   // boards
                SBox(mSolid, (PP(p, s0, dOut + 0.05) + PP(p, s1, dOut + 0.05)) * 0.5, p.r, (s1 - s0) / 2, 0.02, y + 0.08, y + 0.23, board);   // toe board
                SBox(mSolid, (PP(p, s0, dOut) + PP(p, s1, dOut)) * 0.5, p.r, (s1 - s0) / 2, 0.024, y + 0.95, y + 1.0, tube);   // guard rail
            }
            // a brace across the outer face
            for (int i = 0; i + 1 <= bays; i += 3)
            {
                double sa = s0 + (s1 - s0) * i / bays, sb = s0 + (s1 - s0) * Math.Min(bays, i + 1) / bays;
                V2 a = PP(p, sa, dOut - 0.04), b = PP(p, sb, dOut - 0.04);
                double y0 = FPaveY + 0.2, y1 = FPaveY + Math.Min(top, 2 * lift);
                V2 o = p.n * 0.024;
                mSolid.Quad(new P3(a - o, y0, tube.Cu, tube.Cv), new P3(a + o, y0, tube.Cu, tube.Cv), new P3(b + o, y1, tube.Cu, tube.Cv), new P3(b - o, y1, tube.Cu, tube.Cv), p.n.x, 0, p.n.z);
            }
            Collider("Scaffold", (PP(p, s0, (dIn + dOut) / 2) + PP(p, s1, (dIn + dOut) / 2)) * 0.5, p.r, s1 - s0, 0.2, FPaveY, FPaveY + 0.3);
            for (double s = s0; s <= s1; s += 1.2) Claim(PP(p, s, (dIn + dOut) / 2), 0.7);
            D.Count("scaffold bays", bays);
        }
    }
}
