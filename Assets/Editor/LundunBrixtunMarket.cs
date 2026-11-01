// E3: Brixtun market. Pure C# (runs inside Dress, in expansion mode, after everything else, with its own random numbers).
// 1. the market square in front of Brixtun station (the "… Market" park in the expansion data): two rows of stalls
//    facing a wide aisle, strings of bulbs across it, and a BRIXTUN MARKET sign over each end
// 2. stalls along the kerb of the side roads round it (MarketStreets): Atlantic Road between the viaduct and Electric
//    Parade, and Electric Parade itself
// Each stall: a striped canopy on four poles, a trestle table with crates of fruit and veg (or rails of cloth, or
// boxes of phone cases and pots and pans), a cloth skirt, crates stacked beside it, and bulbs under the canopy (they glow at night).
// It also works out where the market's crowd walks (MarketInfo): paths along the aisle and the pavements, a spot in
// front of each stall, where each trader stands, and the ways in and out (the station passage, the street corners).
// LundunMarketCrowd (runtime) brings it to life.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    /// E3: a market, for LundunMarketCrowd. Points on the ground (y = pavement top).
    public class MarketInfo
    {
        public string name;
        public List<double[]> stalls = new List<double[]>();   // x, z, facing x, facing z, length, depth
        public List<double[]> traders = new List<double[]>();  // x, z, facing x, facing z (one per stall)
        public List<V2> nodes = new List<V2>();                 // where people walk
        public List<int[]> edges = new List<int[]>();
        public List<double[]> spots = new List<double[]>();     // x, z, look x, look z, node: in front of a stall
        public List<int> gates = new List<int>();               // nodes where people come and go (the station, street corners)
    }

    public static partial class Generator
    {
        /// The side roads with stalls along the kerb, and the stretch of each that has them (z from, z to; 0, 0 = all of it).
        static readonly string[] MarketStreets = { "Atlantic Road", "Electric Parade" };
        static readonly double[][] MarketStreetZ = { new[] { -170.0, -88.0 }, new[] { 0.0, 0.0 } };

        static readonly string[] CanopyCols = { "mk_red", "mk_green", "mk_blue", "yellow", "orange", "mk_purple" };
        static readonly string[] ProduceCols = { "mk_mango", "mk_tomato", "mk_greens", "mk_yam", "mk_plantain", "mk_onion", "mk_pepper", "mk_aubergine", "mk_lime" };
        static readonly string[] ClothCols = { "mk_kente1", "mk_kente2", "mk_kente3", "mk_red", "mk_blue", "mk_purple", "mk_lime" };
        static readonly string[] GoodsCols = { "mk_red", "mk_blue", "white", "metal", "black", "yellow", "mk_purple" };

        /// The market's colours and its sign (expansion atlases only).
        static void MarketAtlas(DressAtlas a)
        {
            string[][] cols = {
                new[] { "mk_red", "c8102e" }, new[] { "mk_green", "1f7a3a" }, new[] { "mk_blue", "1d4fb8" }, new[] { "mk_purple", "6b2d8a" },
                new[] { "mk_mango", "f29f05" }, new[] { "mk_tomato", "d1271b" }, new[] { "mk_greens", "3f8f2f" }, new[] { "mk_yam", "8a5a3a" },
                new[] { "mk_plantain", "d9c94a" }, new[] { "mk_onion", "c79a6a" }, new[] { "mk_pepper", "e23b1b" }, new[] { "mk_aubergine", "3b1f4a" },
                new[] { "mk_lime", "8cc63f" }, new[] { "mk_kente1", "e0a21b" }, new[] { "mk_kente2", "0f7a4a" }, new[] { "mk_kente3", "b0281e" },
                new[] { "mk_crate", "b08850" }, new[] { "mk_bulb", "ffd98a" } };
            foreach (string[] c in cols) a.Swatch(c[0], Col.Hex(c[1]));
            a.Sign("sign_MARKET", 640, 80, "BRIXTUN MARKET", Col.Hex("1f2a4a"), Col.Hex("ffd23f"), Col.Hex("ffd23f"), 0.5);
        }

        static MarketInfo mk;
        static Random mrng;

        static void DressMarket()
        {
            LwPoly square = null;
            foreach (LwPoly p in W.parks) if (IsMarket(p)) square = p;
            if (square == null) return;
            mrng = new Random(7373);
            mk = new MarketInfo { name = square.name };
            D.market = mk;
            int before = mk.stalls.Count;

            // ---- 1. the square: two rows facing an aisle along its length
            List<V2> poly = Pts(square.polygon);
            double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (V2 q in poly) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
            const double L = 3.0, Dp = 2.0, Pitch = 3.5;
            double y = PaveY, aisleZ = (minZ + maxZ) / 2;
            // keep the station's way in clear (its passage arch opens onto the square)
            double[] way = BrixtunStationEntrance();
            V2 gateIn = way != null ? new V2(way[0], way[2]) : new V2(-1e9, -1e9);
            List<double[]> rowStalls = new List<double[]>();
            foreach (int row in new[] { -1, 1 })                 // −1: backs to the viaduct (south side), +1: north side
            {
                double cz = row < 0 ? minZ + 0.45 + Dp / 2 : maxZ - 0.6 - Dp / 2;
                V2 n = new V2(0, row < 0 ? 1 : -1);
                int k = 0, count = (int)Math.Floor((maxX - minX - 7.0) / Pitch);
                for (int i = 0; i < count; i++)
                {
                    double cx = minX + 4.0 + L / 2 + i * Pitch;
                    if (i == count / 2) continue;                                       // a gap to cut through the middle
                    V2 c = new V2(cx, cz);
                    if (Math.Abs(cx - gateIn.x) < 8.0 && Math.Abs(cz - gateIn.z) < 6) continue;
                    if (!Free(c, 0.6)) continue;
                    Stall(c, n, L, Dp, y, k++);
                    rowStalls.Add(new[] { cx, cz, (double)row });
                }
            }
            // strings of bulbs across the aisle, between facing stalls' front poles
            for (int i = 0; i < rowStalls.Count; i++)
            {
                double[] s = rowStalls[i];
                if (s[2] > 0) continue;
                for (int j = 0; j < rowStalls.Count; j++)
                {
                    double[] t = rowStalls[j];
                    if (t[2] < 0 || Math.Abs(t[0] - s[0]) > 0.5 || ((int)Math.Round((s[0] - minX) / Pitch)) % 2 != 0) continue;
                    V2 a = new V2(s[0], s[1] + Dp / 2 + 0.15), b = new V2(t[0], t[1] - Dp / 2 - 0.15);
                    Festoon(a, b, y + 2.35, y + 1.75, 10);
                }
            }
            // the signs over each end
            foreach (int end in new[] { -1, 1 })
            {
                double gx = end < 0 ? minX + 1.2 : maxX - 1.2;
                MarketArch(new V2(gx, aisleZ), new V2(1, 0) * end, maxZ - minZ - 4.0, y);
            }
            D.Count("market stalls (square)", mk.stalls.Count - before);

            // walkways: along the aisle, in from Brixtun Road, out to Atlantic Road, and to the station's passage
            List<int> aisle = new List<int>();
            for (double x = minX + 1.0; x <= maxX - 1.0 + 0.01; x += 4.0) aisle.Add(Node(new V2(x, aisleZ)));
            Chain(aisle);
            int west = Node(new V2(minX - 2.0, aisleZ)); Link(west, aisle[0]); mk.gates.Add(west);
            if (way != null)
            {
                int st = Node(new V2(gateIn.x, gateIn.z - 0.6)); Link(st, NearestOf(aisle, new V2(gateIn.x, aisleZ))); mk.gates.Add(st);
            }
            int squareFirst = 0, squareLast = mk.nodes.Count;

            // ---- 2. the side roads: stalls along the kerb, people walking between them and the shops
            List<List<int>> chains = new List<List<int>> { aisle };
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            for (int m = 0; m < MarketStreets.Length; m++)
                foreach (string id in ids)
                {
                    RoadInfo ri = roads[id];
                    if (ri.r.name != MarketStreets[m] || ri.pave < 2.5) continue;
                    double z0 = MarketStreetZ[m][0], z1 = MarketStreetZ[m][1];
                    foreach (int side in new[] { 1, -1 })
                    {
                        int made = 0;
                        List<double[]> ok = Stretches(ri, side, 6, 6);
                        double dep = 1.4, len = 2.6, kerb = ri.w / 2 + 0.25 + dep / 2, walk = ri.w / 2 + Math.Min(ri.pave - 0.5, 1.75 + 0.7);
                        List<int> chain = new List<int>();
                        double Lr = ri.line.Length;
                        for (double ch = 3; ch < Lr - 3; ch += 4.0)
                        {
                            V2 f; V2 lc = ri.line.At(ch, out f);
                            if (z1 != z0 && (lc.z < z0 - 12 || lc.z > z1 + 18)) continue;           // walk a little beyond the stalls
                            V2 o = f.Left * side;
                            V2 w = lc + o * walk;
                            if (NearOtherRoad(w, ri, 0.3)) continue;
                            chain.Add(Node(w));
                        }
                        for (double ch = 8; ch < Lr - 8; ch += len + 1.0)
                        {
                            if (!Inside(ch - len / 2, ok, 0) || !Inside(ch + len / 2, ok, 0)) continue;
                            V2 f; V2 lc = ri.line.At(ch, out f);
                            if (z1 != z0 && (lc.z < z0 || lc.z > z1)) continue;
                            if (RailHides(lc + f.Left * side * kerb)) continue;
                            V2 o = f.Left * side, c = lc + o * kerb;
                            bool clear = true;
                            foreach (double t in new[] { -len / 2, 0, len / 2 })
                                if (!OnClearPavement(c + f * t, ri, 0.55)) { clear = false; break; }
                            if (!clear) continue;
                            if (mrng.NextDouble() < 0.18) continue;                               // the odd empty pitch
                            Stall(c, o, len, dep, y, made++);
                        }
                        if (chain.Count >= 2) { Chain(chain); chains.Add(chain); }
                        D.Count("market stalls (" + ri.r.name + ")", made);
                    }
                }
            // join the walkways where they meet (street corners, the ends of the square), never across a carriageway
            for (int a = 0; a < chains.Count; a++)
                for (int b = a + 1; b < chains.Count; b++)
                {
                    List<int> ca = chains[a], cb = chains[b];
                    double best = 14; int ia = -1, ib = -1;
                    foreach (int u in ca)
                        foreach (int v in cb)
                        {
                            double d = V2.Dist(mk.nodes[u], mk.nodes[v]);
                            if (d < best && !CrossesRoad(mk.nodes[u], mk.nodes[v])) { best = d; ia = u; ib = v; }
                        }
                    if (ia >= 0) Link(ia, ib);
                }
            // each street side's far ends are ways in and out too
            for (int c = 1; c < chains.Count; c++) { mk.gates.Add(chains[c][0]); mk.gates.Add(chains[c][chains[c].Count - 1]); }
            // one crossing in the middle of each market street (people do)
            for (int m = 0; m < MarketStreets.Length; m++)
            {
                List<List<int>> sides = new List<List<int>>();
                for (int c = 1; c < chains.Count; c++) if (ChainOnRoad(chains[c], MarketStreets[m])) sides.Add(chains[c]);
                if (sides.Count < 2) continue;
                List<int> s0 = sides[0], s1 = sides[1];
                int u = s0[s0.Count / 2]; int v = NearestOf(s1, mk.nodes[u]);
                if (V2.Dist(mk.nodes[u], mk.nodes[v]) < 20) Link(u, v);
            }

            // spots in front of each stall, joined to the nearest walkway node (the square's stalls to the aisle)
            for (int i = 0; i < mk.stalls.Count; i++)
            {
                double[] s = mk.stalls[i];
                V2 c = new V2(s[0], s[1]), n = new V2(s[2], s[3]);
                V2 p = c + n * (s[5] / 2 + 0.55);
                bool inSquare = InPoly(c, poly);
                int best = -1; double bd = double.MaxValue;
                for (int k = 0; k < mk.nodes.Count; k++)
                {
                    bool sq = k >= squareFirst && k < squareLast;
                    if (sq != inSquare) continue;
                    double d = V2.Dist(mk.nodes[k], p);
                    if (d < bd) { bd = d; best = k; }
                }
                if (best < 0) continue;
                mk.spots.Add(new[] { p.x, p.z, c.x, c.z, best });
            }
            D.log.Add(string.Format("  market: {0} stalls, {1} walkway points, {2} browsing spots, {3} ways in", mk.stalls.Count, mk.nodes.Count, mk.spots.Count, mk.gates.Count));
        }

        // ------------------------------------------------------------------ the walkway graph
        static int Node(V2 p) { mk.nodes.Add(p); return mk.nodes.Count - 1; }
        static void Link(int a, int b) { if (a != b) mk.edges.Add(new[] { a, b }); }
        static void Chain(List<int> c) { for (int i = 0; i + 1 < c.Count; i++) Link(c[i], c[i + 1]); }
        static int NearestOf(List<int> c, V2 p)
        {
            int best = c[0]; double bd = double.MaxValue;
            foreach (int k in c) { double d = V2.Dist(mk.nodes[k], p); if (d < bd) { bd = d; best = k; } }
            return best;
        }
        static bool CrossesRoad(V2 a, V2 b)
        {
            for (int k = 1; k < 8; k++)
            {
                V2 q = Lerp(a, b, k / 8.0);
                foreach (RoadInfo ri in roads.Values) if (ri.line.DistanceTo(q) < ri.w / 2 + 0.2) return true;
            }
            return false;
        }
        static bool ChainOnRoad(List<int> c, string name)
        {
            V2 q = mk.nodes[c[c.Count / 2]];
            foreach (RoadInfo ri in roads.Values) if (ri.r.name == name && ri.line.DistanceTo(q) < ri.w / 2 + ri.pave + 0.5) return true;
            return false;
        }

        // ------------------------------------------------------------------ a stall
        /// c = middle of the stall, n = towards the customers, len along the front, dep front to back. The table is at the
        /// front; the trader stands behind it.
        static void Stall(V2 c, V2 n, double len, double dep, double y, int k)
        {
            n = n.Norm; V2 f = n.Left;
            string canopy = CanopyCols[mrng.Next(CanopyCols.Length)];
            int kind = mrng.Next(10);                     // 0–5 fruit and veg, 6–7 cloth, 8–9 household and phone bits
            UVRect wood = S("wood"), metal = S("metal"), crate = S("mk_crate");
            double hl = len / 2, hd = dep / 2;
            double front = hd, back = -hd, tableD = Math.Min(0.8, dep * 0.5);
            Func<double, double, V2> P = (a, b) => c + f * a + n * b;          // a along, b towards the customers

            // poles and canopy (sloping down to the front), striped, with a valance
            double yb = y + 2.45, yf = y + 2.2;
            foreach (double a in new[] { -hl + 0.05, hl - 0.05 })
                foreach (double b in new[] { back + 0.05, front - 0.05 })
                    Cyl(mSolid, P(a, b), 0.03, y, b > 0 ? yf : yb, 4, metal, false);
            int stripes = 6;
            for (int i = 0; i < stripes; i++)
            {
                double a0 = -hl - 0.1 + (len + 0.2) * i / stripes, a1 = -hl - 0.1 + (len + 0.2) * (i + 1) / stripes;
                UVRect col = i % 2 == 0 ? S(canopy) : S("white");
                P3 pb0 = new P3(P(a0, back - 0.1), yb, col.Cu, col.Cv), pb1 = new P3(P(a1, back - 0.1), yb, col.Cu, col.Cv);
                P3 pf1 = new P3(P(a1, front + 0.3), yf - 0.05, col.Cu, col.Cv), pf0 = new P3(P(a0, front + 0.3), yf - 0.05, col.Cu, col.Cv);
                double rise = yb - yf + 0.05, run = dep + 0.4;
                mSolid.Quad(pb0, pf0, pf1, pb1, n.x * rise / run, 1, n.z * rise / run);
                mSolid.Quad(pb0, pb1, pf1, pf0, -n.x * rise / run, -1, -n.z * rise / run);
            }
            VQuad(mSolid, P(-hl - 0.1, front + 0.31), P(hl + 0.1, front + 0.31), yf - 0.3, yf - 0.05, S(canopy), n);
            VQuad(mSolid, P(hl + 0.1, front + 0.29), P(-hl - 0.1, front + 0.29), yf - 0.3, yf - 0.05, S(canopy), n * -1);
            // bulbs under the front edge
            for (int i = 0; i < 3; i++) SBox(mGlow, P(-hl + len * (i + 0.5) / 3, front + 0.1), f, 0.05, 0.05, yf - 0.42, yf - 0.32, S("mk_bulb"), true);

            // the table, its cloth skirt, and what's on it
            V2 tc = P(0, front - tableD / 2);
            SBox(mSolid, tc, f, hl - 0.05, tableD / 2, y + 0.78, y + 0.84, wood, true);
            SBox(mSolid, tc + n * (tableD / 2 - 0.02), f, hl - 0.05, 0.015, y + 0.12, y + 0.8, S(kind < 6 ? "mk_green" : canopy));
            if (kind < 6)
            {
                // crates of fruit and veg, heaped
                int nc = Math.Max(3, (int)(len / 0.55));
                for (int i = 0; i < nc; i++)
                {
                    double a = -hl + 0.3 + (len - 0.6) * (i + 0.5) / nc, h = 0.12 + mrng.NextDouble() * 0.06;
                    V2 q = P(a, front - tableD / 2 + (i % 2 == 0 ? 0.08 : -0.1));
                    double w = (len - 0.6) / nc / 2 - 0.02;
                    SBox(mSolid, q, f, w, 0.22, y + 0.84, y + 0.84 + h, crate);
                    string pc = ProduceCols[mrng.Next(ProduceCols.Length)];
                    SBox(mSolid, q, f, w - 0.03, 0.19, y + 0.84 + h, y + 0.84 + h + 0.07, S(pc));
                    SBox(mSolid, q, f, w - 0.1, 0.12, y + 0.84 + h + 0.07, y + 0.84 + h + 0.12, S(pc));
                }
            }
            else if (kind < 8)
            {
                // folded cloth on the table, lengths hanging from a rail at the back
                for (int i = 0; i < 5; i++)
                {
                    double a = -hl + 0.35 + (len - 0.7) * i / 4.0;
                    SBox(mSolid, P(a, front - tableD / 2), f, 0.22, 0.25, y + 0.84, y + 0.9 + mrng.NextDouble() * 0.12, S(ClothCols[mrng.Next(ClothCols.Length)]));
                }
                Cyl(mSolid, P(-hl + 0.1, back + 0.15), 0.025, y, y + 2.0, 4, metal, false);
                Cyl(mSolid, P(hl - 0.1, back + 0.15), 0.025, y, y + 2.0, 4, metal, false);
                SBox(mSolid, P(0, back + 0.15), f, hl - 0.1, 0.02, y + 1.96, y + 2.0, metal);
                for (int i = 0; i < 4; i++)
                {
                    double a0 = -hl + 0.2 + (len - 0.4) * i / 4.0, a1 = a0 + (len - 0.4) / 4.0 - 0.08;
                    VQuad(mSolid, P(a0, back + 0.17), P(a1, back + 0.17), y + 0.7 + mrng.NextDouble() * 0.3, y + 1.96, S(ClothCols[mrng.Next(ClothCols.Length)]), n);
                    VQuad(mSolid, P(a1, back + 0.13), P(a0, back + 0.13), y + 0.7, y + 1.96, S(ClothCols[mrng.Next(ClothCols.Length)]), n * -1);
                }
            }
            else
            {
                // boxes, pots and pans, phone cases on a board
                for (int i = 0; i < 7; i++)
                {
                    double a = -hl + 0.25 + (len - 0.5) * mrng.NextDouble();
                    double hb = 0.08 + mrng.NextDouble() * 0.25, wb = 0.1 + mrng.NextDouble() * 0.15;
                    SBox(mSolid, P(a, front - tableD / 2 + (mrng.NextDouble() - 0.5) * 0.4), f, wb, wb * 0.8, y + 0.84, y + 0.84 + hb, S(GoodsCols[mrng.Next(GoodsCols.Length)]));
                }
                SBox(mSolid, P(0, back + 0.12), f, hl - 0.2, 0.02, y + 0.9, y + 1.9, S("charcoal"));
                for (int i = 0; i < 10; i++)
                    SBox(mSolid, P(-hl + 0.4 + (len - 0.8) * (i % 5) / 4.0, back + 0.15), f, 0.07, 0.015, y + 1.15 + (i / 5) * 0.4, y + 1.4 + (i / 5) * 0.4, S(GoodsCols[mrng.Next(GoodsCols.Length)]));
            }
            // crates stacked at one end, behind
            double side = mrng.NextDouble() < 0.5 ? -1 : 1;
            int stack = 1 + mrng.Next(3);
            for (int i = 0; i < stack; i++)
                SBox(mSolid, P(side * (hl - 0.35), back + 0.3), f, 0.28, 0.2, y + i * 0.28, y + i * 0.28 + 0.26, crate);

            Collider("Stall", tc, f, len - 0.1, tableD, y, y + 0.9);
            Claim(c, Math.Max(hl, hd) + 0.2);
            Claim(c + f * (hl * 0.6), hd + 0.2); Claim(c - f * (hl * 0.6), hd + 0.2);
            mk.stalls.Add(new[] { c.x, c.z, n.x, n.z, len, dep });
            V2 tr = P(0, back + Math.Max(0.35, (dep - tableD) * 0.5));
            mk.traders.Add(new[] { tr.x, tr.z, n.x, n.z });
            D.Count("market stalls");
        }

        /// A string of bulbs sagging between two points (a and b at height top, the middle down to low).
        static void Festoon(V2 a, V2 b, double top, double low, int bulbs)
        {
            V2 d = (b - a);
            double Lr = d.Len; V2 f = d.Norm;
            UVRect wire = S("black"), bulb = S("mk_bulb");
            int seg = 8;
            for (int i = 0; i < seg; i++)
            {
                double t0 = (double)i / seg, t1 = (double)(i + 1) / seg;
                double y0 = top - (top - low) * 4 * t0 * (1 - t0), y1 = top - (top - low) * 4 * t1 * (1 - t1);
                V2 p0 = a + d * t0, p1 = a + d * t1;
                mSolid.Quad(new P3(p0, y0, wire.Cu, wire.Cv), new P3(p1, y1, wire.Cu, wire.Cv), new P3(p1, y1 + 0.02, wire.Cu, wire.Cv), new P3(p0, y0 + 0.02, wire.Cu, wire.Cv), f.Left.x, 0, f.Left.z);
            }
            for (int i = 1; i < bulbs; i++)
            {
                double t = (double)i / bulbs, yy = top - (top - low) * 4 * t * (1 - t);
                SBox(mGlow, a + d * t, f, 0.045, 0.045, yy - 0.12, yy - 0.03, bulb, true);
            }
            D.Count("market bulbs", bulbs - 1);
        }

        /// The BRIXTUN MARKET sign on two posts across the end of the square (o = facing out of the square).
        static void MarketArch(V2 c, V2 o, double width, double y)
        {
            V2 f = o.Left; double hw = width / 2;
            UVRect metal = S("charcoal");
            foreach (int s in new[] { -1, 1 })
            {
                Cyl(mSolid, c + f * (s * hw), 0.09, y, y + 4.7, 6, metal);
                Collider("MarketArch", c + f * (s * hw), f, 0.2, 0.2, y, y + 4.7);
                Claim(c + f * (s * hw), 0.5);
            }
            SBox(mSolid, c, f, hw, 0.06, y + 4.55, y + 4.68, metal, true);
            double sw = Math.Min(9.0, width - 1.0) / 2;
            SBox(mSolid, c, f, sw + 0.08, 0.05, y + 3.6, y + 4.7, S("navy"), true);
            VQuad(mGlow, c + o * 0.06 - f * sw, c + o * 0.06 + f * sw, y + 3.65, y + 4.65, A["sign_MARKET"], o);
            VQuad(mGlow, c - o * 0.06 + f * sw, c - o * 0.06 - f * sw, y + 3.65, y + 4.65, A["sign_MARKET"], o * -1);
            D.Count("market signs");
        }
    }
}
