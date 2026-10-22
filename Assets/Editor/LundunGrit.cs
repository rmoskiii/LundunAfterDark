// L4: grit. Two passes, each added last with its own random seed, so nothing already placed moves.
//   DressGrit   (part of Lundun → 13, dressing atlas)   piles of bin bags outside the shops and round lamp posts,
//               bags at front gates, fly-tipping between buildings and at the estate blocks (sofas, fridges,
//               mattresses, tyres, cardboard)
//   StreetGrit  (part of Lundun → 22, street decal atlas)  litter on the pavements and in the gutters, autumn leaves,
//               puddles in the gutters and potholes (shiny), bin juice by the bag piles, fly-posters on side walls,
//               tags and stickers on street cabinets
// How much: Gilmore most, then Peckam, a little in Canary Quay, Knightsbury mostly just leaves.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        public const string StreetWetMat = "Street_Wet";
        static Random grng;
        static List<double[]> bagPiles = new List<double[]>();       // x, z: StreetGrit puts bin juice under them
        static List<double[]> flyTips = new List<double[]>();
        /// For tests and logs: where the bag piles and fly-tips went (x, z).
        public static List<double[]> BagPiles { get { return bagPiles; } }
        public static List<double[]> FlyTips { get { return flyTips; } }

        static double GritAt(V2 q)
        {
            switch (District(q))
            {
                case "D_GILMOR": return 1.3;
                case "D_PECKAM": return 1.0;
                case "D_CANARY": return 0.35;
                case "D_CANARY_PEN": return 0.2;
                case "D_KNIGHT": return 0.12;
            }
            return 0.4;
        }

        // ------------------------------------------------------------------ dressing atlas: a few more colours (appended)
        static void GritAtlas(DressAtlas a)
        {
            foreach (var kv in new Dictionary<string, string> {
                { "baggrey", "3c4046" }, { "bagblue", "2b3d6b" }, { "sofa1", "5a3a2a" }, { "sofa2", "3d4a3a" }, { "sofa3", "6b2a2e" },
                { "cardboard", "a07a4a" }, { "fridge", "d9d9d4" }, { "tyre", "1a1a1a" } })
                a.Swatch(kv.Key, Col.Hex(kv.Value));
        }

        // ================================================================== 3D: bags and fly-tipping
        static void DressGrit()
        {
            grng = new Random(6060);
            bagPiles = new List<double[]>(); flyTips = new List<double[]>();
            foreach (BuildingInfo b in blds)
            {
                KitPlan p = KitPlanFor(b);
                if (p == null) continue;
                double g = GritAt(p.c);
                switch (p.type)
                {
                    case KitType.Parade: case KitType.Pub: case KitType.EstParade:
                        if (grng.NextDouble() < 0.55 * g + 0.15) ShopBags(p);
                        break;
                    case KitType.Stucco: case KitType.Mansion:
                        if (p.mixed && grng.NextDouble() < 0.3) ShopBags(p);
                        break;
                    case KitType.Terrace:
                        GateBags(p);
                        if (grng.NextDouble() < 0.18 * g) FlyTip(p);
                        break;
                    case KitType.Deck: case KitType.Maisonette: case KitType.EstTower: case KitType.Garages: case KitType.Infill:
                        if (grng.NextDouble() < 0.38 * g) FlyTip(p);
                        break;
                }
            }
            // bags dumped round lamp posts on the gritty streets
            foreach (LampInfo l in lampList)
            {
                V2 q = new V2(l.x, l.z);
                double g = GritAt(q);
                if (g < 0.9 || grng.NextDouble() > 0.06 * g) continue;
                BagPile(q + new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * 0.8, 2 + grng.Next(3), FPaveY);
            }
        }

        /// A lumpy black sack, tied at the top.
        static void Bag(V2 q, double y, double scale, string col)
        {
            double r = (0.2 + grng.NextDouble() * 0.1) * scale, h = (0.42 + grng.NextDouble() * 0.2) * scale, tw = grng.NextDouble() * 3;
            V2 lean = new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * (0.06 * scale);
            UVRect c = S(col);
            Band(mSolid, q, r * 0.7, y, r, y + h * 0.25, 7, c, tw);
            Band(mSolid, q + lean * 0.5, r, y + h * 0.25, r * 0.88, y + h * 0.7, 7, c, tw);
            Band(mSolid, q + lean, r * 0.88, y + h * 0.7, r * 0.12, y + h, 7, c, tw);
            Cyl(mSolid, q + lean, r * 0.12, y + h, y + h + 0.06 * scale, 5, c);
        }

        static void BagPile(V2 c, int n, double y)
        {
            for (int i = 0; i < n; i++)
            {
                V2 q = c + new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * (0.35 + n * 0.1);
                double rr = grng.NextDouble();
                Bag(q, y, 0.85 + grng.NextDouble() * 0.35, rr < 0.72 ? "bag" : rr < 0.88 ? "baggrey" : "bagblue");
            }
            if (n >= 4 && grng.NextDouble() < 0.6) Bag(c + new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * 0.2, y + 0.35, 0.8, "bag");   // one on top
            bagPiles.Add(new[] { c.x, c.z });
            D.Count("bin bags", n);
        }

        /// Bags (and flattened boxes) at the kerb outside a shop parade.
        static void ShopBags(KitPlan p)
        {
            double ch, lat;
            RoadInfo ri = NearestStreet(PP(p, 0, -p.gap - 1), out ch, out lat);
            if (ri == null || ri.pave < 2.5) return;
            int s = lat >= 0 ? 1 : -1;
            int piles = 1 + (grng.NextDouble() < 0.4 ? 1 : 0);
            for (int k = 0; k < piles; k++)
            {
                double at = ch + (grng.NextDouble() - 0.5) * Math.Max(2, p.W - 3);
                V2 dir; V2 q = ri.line.At(at, out dir) + dir.Left * (s * (ri.w / 2 + 0.6));
                if (InBox(q) || !Free(q, 0.6)) continue;
                BagPile(q, 3 + grng.Next(5), FPaveY);
                if (grng.NextDouble() < 0.5)
                {
                    // a stack of flattened boxes leant on the pile
                    V2 bq = q + dir * (0.9 + grng.NextDouble() * 0.3);
                    for (int i = 0; i < 2 + grng.Next(3); i++)
                        SBox(mSolid, bq + new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * 0.15, dir + new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * 0.3,
                             0.45, 0.32, FPaveY + i * 0.03, FPaveY + i * 0.03 + 0.03, S("cardboard"));
                    D.Count("cardboard piles");
                }
            }
        }

        /// A bag or two at the front gate on bin day.
        static void GateBags(KitPlan p)
        {
            if (p.gap < 1.4 || p.gap > 6) return;
            double g = GritAt(p.c);
            foreach (double s in Doors(p, 5.333, 0.925))
            {
                if (grng.NextDouble() > 0.12 * g) continue;
                V2 q = PP(p, s + 0.9, FrontEdge(p) - 0.45);           // just outside the garden wall, on the pavement edge
                if (!Free(q, 0.3)) continue;
                for (int i = 0; i < 1 + grng.Next(2); i++) Bag(q + new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * 0.3, FPaveY, 0.9, "bag");
                D.Count("bin bags", 1);
            }
        }

        /// Fly-tipping in the gap beside a building (off the pavement): a sofa, a fridge, a mattress, tyres, boxes, bags.
        static void FlyTip(KitPlan p)
        {
            int side = grng.NextDouble() < 0.5 ? -1 : 1;
            V2 q = PP(p, side * (p.W / 2 + 0.9), 1.2 + grng.NextDouble() * Math.Max(0.5, p.D - 3));
            if (InBox(q) || !Free(q, 1.0)) return;
            foreach (RoadInfo ri in roads.Values) if (ri.line.DistanceTo(q) < ri.w / 2 + Math.Max(ri.pave, ri.verge) + 0.6) return;   // never on the pavement
            V2 along = p.n * -1;                                      // the gap runs front to back
            int items = 1 + grng.Next(3);
            for (int i = 0; i < items; i++)
            {
                V2 at = q + along * ((i - (items - 1) / 2.0) * 1.3);
                switch (grng.Next(6))
                {
                    case 0: Sofa(at, along); break;
                    case 1: Fridge(at, along); break;
                    case 2: Mattress(at, along); break;
                    case 3: Tyres(at); break;
                    case 4: for (int k = 0; k < 3; k++) SBox(mSolid, at + new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * 0.4, along, 0.25, 0.25, k * 0.3, k * 0.3 + 0.3, S("cardboard")); break;
                    default: BagPile(at, 3 + grng.Next(4), 0); break;
                }
            }
            flyTips.Add(new[] { q.x, q.z, along.x, along.z });
            D.Count("fly-tips");
        }

        static void Sofa(V2 q, V2 along)
        {
            V2 f = along.Norm, a = f.Left;
            UVRect c = S(new[] { "sofa1", "sofa2", "sofa3" }[grng.Next(3)]);
            SBox(mSolid, q, f, 0.9, 0.42, 0, 0.42, c);                             // seat base
            SBox(mSolid, q - a * 0.33, f, 0.9, 0.1, 0.42, 0.85, c);                // back
            foreach (int s in new[] { -1, 1 }) SBox(mSolid, q + f * (s * 0.82), f, 0.09, 0.42, 0.42, 0.62, c);   // arms
            if (grng.NextDouble() < 0.6) SBox(mSolid, q + a * 0.05 + f * 0.35, f, 0.38, 0.3, 0.42, 0.52, c);    // one cushion left
            Collider("FlyTip", q, f, 1.8, 0.85, 0, 0.85);
            D.Count("sofas");
        }

        static void Fridge(V2 q, V2 along)
        {
            V2 f = along.Norm;
            bool lying = grng.NextDouble() < 0.5;
            if (lying) { SBox(mSolid, q, f, 0.8, 0.32, 0, 0.62, S("fridge")); SBox(mSolid, q + f.Left * 0.33, f, 0.5, 0.01, 0.2, 0.45, S("metal")); }
            else { SBox(mSolid, q, f, 0.32, 0.32, 0, 1.6, S("fridge")); SBox(mSolid, q + f.Left * 0.33, f, 0.25, 0.01, 1.0, 1.02, S("charcoal")); }
            Collider("FlyTip", q, f, lying ? 1.6 : 0.64, 0.64, 0, lying ? 0.62 : 1.6);
            D.Count("fridges");
        }

        static void Mattress(V2 q, V2 along)
        {
            V2 f = along.Norm, a = f.Left;
            // propped against the wall at a lean
            SBox(mSolid, q - a * 0.15, f, 0.7, 0.1, 0, 1.85, S("white"));
            VQuad(mSolid, q - a * 0.04 - f * 0.7, q - a * 0.04 + f * 0.7, 0, 1.85, A["mattress"], a);
            D.Count("mattresses (tipped)");
        }

        static void Tyres(V2 q)
        {
            int n = 2 + grng.Next(3);
            for (int i = 0; i < n; i++)
            {
                V2 c = q + new V2(grng.NextDouble() - 0.5, grng.NextDouble() - 0.5) * 0.12;
                Cyl(mSolid, c, 0.32, i * 0.21, i * 0.21 + 0.2, 10, S("tyre"));
            }
            D.Count("tyres");
        }

        // ================================================================== flat: litter, leaves, puddles, posters, tags
        static void StreetGrit(MeshData sWet)
        {
            Random r = new Random(7070);
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.cls == "bridge") continue;
                double L = ri.line.Length, hw = ri.w / 2;
                bool cobbled = CobbledRoads.Contains(ri.r.name);
                for (int s = 1; s >= -1; s -= 2)
                {
                    List<double[]> cuts = Concat(ri.boxCut, s > 0 ? ri.trimL : ri.trimR, s > 0 ? ri.boxCutL : ri.boxCutR);
                    foreach (double[] iv in Keep(L, cuts, 4))
                        for (double ch = iv[0] + 1 + r.NextDouble() * 3; ch < iv[1] - 1; ch += 2.5 + r.NextDouble() * 3)
                        {
                            V2 dir; V2 c = ri.line.At(ch, out dir);
                            if (InBox(c)) continue;
                            double g = Grit(c);
                            bool leafy = District(c) == "D_KNIGHT" || NearPark(c);
                            double yGut = Yc(ri, 0.047), yPave = W.heights.pavementTop + 0.01;
                            // gutter: litter, leaves, the odd puddle
                            if ((ri.pave > 0 || ri.cls == "service") && r.NextDouble() < 0.16 * g)
                                Sprite(sDec, c + dir.Left * (s * (hw - 0.3)), dir.Left * s, 0.6, 0.45, "litter" + r.Next(StreetArt.GritLitter), yGut);
                            if (r.NextDouble() < (leafy ? 0.2 : 0.03))
                                Sprite(sDec, c + dir.Left * (s * (hw - 0.35)), dir.Left * s, 0.9, 0.6, "leaves" + r.Next(StreetArt.GritLeaves), yGut + 0.001);
                            if (!cobbled && r.NextDouble() < 0.035 * (0.6 + g))
                            {
                                double len = 1.2 + r.NextDouble() * 2.2;
                                Sprite(sWet, c + dir.Left * (s * (hw - 0.4)), dir.Left * s, len, 0.6 + r.NextDouble() * 0.4, "puddle" + r.Next(StreetArt.GritPuddles), Yc(ri, 0.045));
                                SR.Count("puddles");
                            }
                            // pavement
                            if (ri.pave > 0)
                            {
                                double across = 0.35 + r.NextDouble() * Math.Max(0.1, ri.pave - 0.7);
                                V2 q = c + dir.Left * (s * (hw + across));
                                if (r.NextDouble() < 0.1 * g) { Sprite(sPave, q, new V2(r.NextDouble() - 0.5, r.NextDouble() - 0.5), 0.6, 0.45, "litter" + r.Next(StreetArt.GritLitter), yPave); SR.Count("litter (pavements)"); }
                                else if (leafy && r.NextDouble() < 0.12) Sprite(sPave, q, dir, 0.9, 0.7, "leaves" + r.Next(StreetArt.GritLeaves), yPave);
                                else if (District(q) == "D_GILMOR" && r.NextDouble() < 0.025) { Sprite(sWet, q, dir, 1.4, 0.8, "puddle" + r.Next(StreetArt.GritPuddles), yPave); SR.Count("puddles"); }
                            }
                        }
                }
                // potholes full of water out in the road (Peckam, Gilmore)
                if (ri.pave > 0 && !cobbled)
                    foreach (double[] iv in Keep(L, Concat(ri.boxCut, ri.trimAll), 10))
                        for (double ch = iv[0] + 5; ch < iv[1] - 5; ch += 25 + r.NextDouble() * 30)
                        {
                            V2 dir; V2 c = ri.line.At(ch, out dir);
                            double g = Grit(c);
                            if (g < 0.9 || r.NextDouble() > 0.25) continue;
                            V2 q = c + dir.Left * ((r.NextDouble() * 2 - 1) * (hw - 1.2));
                            Sprite(sDec, q, dir, 1.0, 0.8, "patch" + r.Next(4), YWear);
                            Sprite(sWet, q, dir, 0.75, 0.5, "puddle" + r.Next(StreetArt.GritPuddles), YWear + 0.003);
                            SR.Count("potholes");
                        }
            }
            // bin juice under the bag piles
            foreach (double[] b in bagPiles) Sprite(sPave, new V2(b[0], b[1]), new V2(r.NextDouble() - 0.5, r.NextDouble() - 0.5), 1.0, 0.8, "binjuice", W.heights.pavementTop + 0.009);
            FlyPosters(r);
            CabinetTags(r);
        }

        static bool NearPark(V2 q)
        {
            foreach (LwPoly park in W.parks) { List<V2> poly = Pts(park.polygon); if (InPoly(q, poly) || EdgeDist(q, poly) < 25) return true; }
            return false;
        }

        /// Fly-posters pasted on the side walls of shops and terraces near the pavement, plus a tag or two.
        static void FlyPosters(Random r)
        {
            foreach (BuildingInfo b in blds)
            {
                KitPlan p = KitPlanFor(b);
                if (p == null || p.gap > 4) continue;
                if (!(p.type == KitType.Terrace || p.type == KitType.Parade || p.type == KitType.Pub || p.type == KitType.Infill || p.type == KitType.Warehouse ||
                      p.type == KitType.Deck || p.type == KitType.Maisonette || p.type == KitType.Garages || p.type == KitType.EstParade)) continue;
                double g = GritAt(p.c);
                foreach (int side in new[] { -1, 1 })
                {
                    if (r.NextDouble() > 0.3 * g) continue;
                    V2 n = p.r * side;
                    double start = 0.4 + r.NextDouble() * 0.6, len = 1.8 + r.NextDouble() * 2.6;
                    int cols = Math.Max(2, (int)(len / 0.62)), rows = r.NextDouble() < 0.5 ? 1 : 2;
                    for (int i = 0; i < cols; i++)
                        for (int k = 0; k < rows; k++)
                        {
                            double d = start + i * 0.6 + (r.NextDouble() - 0.5) * 0.06, y = 1.05 + k * 0.88 + (r.NextDouble() - 0.5) * 0.08;
                            V2 c = PP(p, side * (p.W / 2 + 0.012 + (i + k) % 3 * 0.002), d);
                            V2 t = p.n * -1;                                          // along the side wall, front → back
                            VQuadPic(sDec, c - t * 0.3, c + t * 0.3, y, y + 0.85, "poster" + r.Next(StreetArt.GritPosters), n);
                        }
                    if (r.NextDouble() < 0.7)
                    {
                        V2 tc = PP(p, side * (p.W / 2 + 0.02), start + len + 0.4 + r.NextDouble() * 1.2);
                        V2 t = p.n * -1;
                        VQuadPic(sDec, tc - t * 0.5, tc + t * 0.5, 0.35, 0.85, "tagS" + r.Next(StreetArt.GritTags), n);
                    }
                    SR.Count("fly-poster walls");
                }
            }
        }

        static void CabinetTags(Random r)
        {
            foreach (double[] c in cabinetSpots)
            {
                V2 q = new V2(c[0], c[1]), dir = new V2(c[2], c[3]), n = dir.Left * c[4];
                if (r.NextDouble() > 0.55 * GritAt(q) + 0.1) continue;
                V2 f = q + n * 0.235;
                VQuadPic(sDec, f - dir * 0.46, f + dir * 0.46, FPaveY + 0.45, FPaveY + 0.95, r.NextDouble() < 0.7 ? "tagS" + r.Next(StreetArt.GritTags) : "stickers", n);
                SR.Count("tagged cabinets");
            }
        }

        /// An upright picture from the street decal atlas, facing n, reading the right way round.
        static void VQuadPic(MeshData m, V2 a, V2 b, double y0, double y1, string sprite, V2 n)
        {
            StreetArt.Rect rc = StreetArt.GetSprite(sprite);
            double u0 = (rc.x + 0.5) / StreetArt.DecW, u1 = (rc.x + rc.w - 0.5) / StreetArt.DecW, v0 = (rc.y + 0.5) / StreetArt.DecH, v1 = (rc.y + rc.h - 0.5) / StreetArt.DecH;
            if (V2.Dot(b - a, n.Left) < 0) { V2 t = a; a = b; b = t; }               // a = viewer's left, b = right (right of someone facing the wall = n.Left)
            m.Quad(new P3(a, y0, u0, v0), new P3(b, y0, u1, v0), new P3(b, y1, u1, v1), new P3(a, y1, u0, v1), n.x, 0, n.z);
        }
    }
}
