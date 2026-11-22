// G3: autumn, always (Lundun → 37). Pure maths like the rest of the dressing: no UnityEngine types.
// Run Autumn() straight after a Dress(...) (the city's, Brixtun's or Kingsmere's): it reads the roads, the districts
// and the trees that Dress just placed, and adds, on its own random numbers (nothing Dress made moves or changes):
//   • fallen leaves: drifts in the gutters, patches on the pavements and under the street trees, the odd few on the
//     road. Most in Peckam, fewest in Knightsbury and Kingsmere (they sweep there). G8d: about half as many as G3 had
//   • G8d: the trees are late-autumn trees. Half the street trees are nearly bare (trunk, branches and twigs, a last
//     clump or two of leaves), a third are half-bare (thin amber, rust and gold clumps on the branches), and the rest
//     keep a full crown, turned. In the parks three trees in ten stay green, the rest go half-bare or bare. Their old
//     green crowns are listed (hideStreet / hidePark) for Lundun → 37 to take out of the dressing's meshes
//   • where the trees are, for the few leaves that fall (LundunFallingLeaves)
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class AutumnResult
    {
        public List<MeshData> meshes = new List<MeshData>();
        public List<double[]> fallFrom = new List<double[]>();     // x, z of every tree leaves can fall from
        public DressAtlas atlas;
        public int drifts, patches, roadLeaves, turned, turning, bare, halfBare, parkBare, parkHalf;
        public List<double[]> hideStreet = new List<double[]>();   // G8d: crowns to take out: x, z, baseY, h, r
        public List<double[]> hidePark = new List<double[]>();     // G8d: park canopies to take out: x, z, R, y0, y1
        public List<double[]> leafUV = new List<double[]>();       // G8d: the dressings' leaf colours (u, v): how a crown is told apart
        public List<string> log = new List<string>();
        internal HashSet<long> parkSeen = new HashSet<long>();      // park trees can be listed by more than one area
    }

    public static partial class Generator
    {
        public const string AutumnLitterMat = "AutumnLitter";
        public const string AutumnCrownMat = "AutumnCrown";
        public const string AutumnClumpMat = "AutumnClump";                 // G8d: the last leaves on the branches (cut out, both sides)
        static readonly string[] AutumnCrownSwatches = { "amber", "rust", "gold" };

        /// The leaves' own little texture (512×512): three gutter drifts, two patches, three crown colours.
        public static DressAtlas AutumnAtlas()
        {
            DressAtlas a = new DressAtlas(512, 512, 4417);
            for (int i = 0; i < 3; i++) a.LeafLitter("drift" + i, 248, 64, true, 501 + i);
            a.NewShelf();
            for (int i = 0; i < 2; i++) a.LeafLitter("patch" + i, 128, 128, false, 601 + i);
            a.NewShelf();
            a.Swatch("amber", Col.Hex("b7772c"));
            a.Swatch("rust", Col.Hex("9c4a20"));
            a.Swatch("gold", Col.Hex("c79a34"));
            a.Swatch("bark", Col.Hex("3e3226"));                        // G8d: branches
            a.Swatch("twig", Col.Hex("54453a"));
            return a;
        }

        /// How much falls (and stays) by district: 1 = Peckam.
        static double LeafAmount(V2 q)
        {
            if (Kingsmere) return 0.15;
            if (Brix) return 0.8;
            switch (District(q))
            {
                case "D_PECKAM": return 1.0;
                case "D_GILMOR": return 0.85;
                case "D_CANARY": case "D_CANARY_PEN": return 0.4;
                case "D_KNIGHT": return 0.2;
                default: return 0.7;                                     // Hydemoor, the parks, the rest
            }
        }

        /// A number 0–1 from a position (so which trees turn never depends on any random numbers).
        static double PosHash(double x, double z, double salt)
        {
            double s = Math.Sin(x * 12.9898 + z * 78.233 + salt * 37.719) * 43758.5453;
            return s - Math.Floor(s);
        }

        /// Call straight after Dress(world, built). Adds this area's leaves to `into` (pass the same one for each area).
        public static void Autumn(AutumnResult into, int areaSeed)
        {
            if (roads == null || treeCrowns == null) throw new Exception("Run Dress(...) first.");
            if (into.atlas == null) into.atlas = AutumnAtlas();
            DressAtlas la = into.atlas;
            int d0 = into.drifts, p0 = into.patches, r0 = into.roadLeaves, t0 = into.turned, b0 = into.bare, h0 = into.halfBare;
            Random r = new Random(4417 + areaSeed);
            MeshData litter = new MeshData("AutumnLitter", "Autumn", AutumnLitterMat, false) { navIgnore = true };
            MeshData crowns = new MeshData("AutumnCrown", "Autumn", AutumnCrownMat, false) { navIgnore = true };
            MeshData clumps = new MeshData("AutumnClumps", "Autumn", AutumnClumpMat, false) { navIgnore = true };
            for (int i = 1; i <= 3; i++)                                     // G8d: the dressing's leaf colours
                if (A != null && A.Has("leaf" + i)) into.leafUV.Add(new[] { A["leaf" + i].Cu, A["leaf" + i].Cv });

            // 1. along the roads: the gutters, the pavements, the odd few on the tarmac
            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.cls == "bridge") continue;
                for (int side = 1; side >= -1; side -= 2)
                {
                    List<double[]> ok = Stretches(ri, side, 6, 4);
                    for (double ch = 2; ch < ri.line.Length - 2; ch += 3.2)
                    {
                        double c2 = ch + (r.NextDouble() - 0.5) * 1.2;
                        double roll = r.NextDouble(), roll2 = r.NextDouble(), roll3 = r.NextDouble();
                        double pick = r.NextDouble(), pick2 = r.NextDouble(), size = r.NextDouble(), spin = r.NextDouble();
                        if (!Inside(c2, ok, 1.5)) continue;
                        V2 dir; V2 at = ri.line.At(c2, out dir);
                        V2 left = dir.Left * side;
                        V2 kerb = at + left * (ri.w / 2);
                        if (InBox(kerb)) continue;                               // the hand-built old high street keeps its own
                        double amt = LeafAmount(kerb);
                        bool zebra = false;                                      // the zebra crossings stay clear
                        if (D != null) foreach (double[] zc in D.crossings) if (V2.Dist(at, new V2(zc[0], zc[1])) < 4.0) { zebra = true; break; }
                        if (zebra) continue;

                        // gutter drift: thick against the kerb, thinning towards the road
                        if (roll < amt * 0.45)                                    // G8d: was 0.85
                        {
                            double hl = 0.9 + size * 0.5;
                            HQuad(litter, kerb - left * 0.45, left, 0.45, hl, PaintY + 0.004, la["drift" + (int)(pick * 3)]);
                            into.drifts++;
                        }
                        // scattered over the pavement (up to two patches a step in Peckam)
                        for (int k = 0; k < 2; k++)
                        {
                            if (ri.pave <= 0.9 || (k == 0 ? roll2 : pick2) >= amt * (k == 0 ? 0.3 : 0.1)) continue;      // G8d: was 0.7 / 0.35
                            double off = k == 0 ? pick2 : roll2 / Math.Max(0.01, amt * 0.1);
                            V2 p = kerb + left * (0.5 + Math.Min(1, off) * (ri.pave - 1.0)) + dir * ((k == 0 ? -0.8 : 0.8) * size);
                            if (InBox(p) || NearOtherRoad(p, ri, 0.6)) continue;
                            double s = 0.75 + size * 0.5;
                            HQuad(litter, p, Spin(spin + k * 0.37), s, s, PaveY + 0.012 + k * 0.002, la["patch" + ((int)(pick * 2) + k) % 2]);
                            into.patches++;
                        }
                        // blown off the gutter onto the road (mostly the nearside lane)
                        if (roll3 < amt * 0.07)                                   // G8d: was 0.16
                        {
                            V2 p = kerb - left * (0.9 + pick2 * pick2 * Math.Max(0.2, ri.w / 2 - 1.2));
                            double s = 0.6 + size * 0.4;
                            HQuad(litter, p, Spin(spin), s, s, PaintY + 0.006, la["patch" + (int)(pick * 2)]);
                            into.roadLeaves++;
                        }
                    }
                }
            }

            // 2. the trees Dress planted: what's fallen under them, and whether they've turned
            foreach (double[] t in treeCrowns)
            {
                V2 p = new V2(t[0], t[1]);
                double baseY = t[2], h = t[3], rad = t[4], tw = t[5];
                double amt = LeafAmount(p);
                into.fallFrom.Add(new[] { p.x, p.z });
                int under = (r.NextDouble() < amt * 0.8 ? 1 : 0) + (r.NextDouble() < amt * 0.25 ? 1 : 0);   // G8d: a few less
                for (int k = 0; k < under; k++)
                {
                    double a = r.NextDouble() * Math.PI * 2, d = 0.3 + r.NextDouble() * 1.0, s = 0.6 + r.NextDouble() * 0.4;
                    HQuad(litter, p + new V2(Math.Sin(a), Math.Cos(a)) * d, Spin(r.NextDouble()), s, s, baseY + 0.012 + k * 0.002, la["patch" + k]);
                    into.patches++;
                }

                double turn = PosHash(p.x, p.z, 1);
                UVRect col = la[AutumnCrownSwatches[(int)(PosHash(p.x, p.z, 2) * 3) % 3]];
                double y0 = baseY + 2.2, g = 1.04;                      // just outside the green crown, so it hides it
                if (turn < 0.15)                                        // keeps its crown, turned
                {
                    Band(crowns, p, rad * 0.55 * g, y0 - 0.01, rad * g, y0 + h * 0.35, 7, col, tw);
                    Band(crowns, p, rad * g, y0 + h * 0.35, rad * 0.85 * g, y0 + h * 0.7, 7, col, tw);
                    Crest(crowns, p, rad, y0, h, g, col, tw);
                    into.turned++;
                }
                else                                                    // G8d: half-bare or nearly bare
                {
                    bool half = turn < 0.5;
                    into.hideStreet.Add(new[] { p.x, p.z, baseY, h, rad });
                    Bones(crowns, clumps, la, p, baseY + 2.8, y0, h, rad, tw, half ? 5 + (int)(PosHash(p.x, p.z, 3) * 4) : (int)(PosHash(p.x, p.z, 3) * 3), new Random((int)(PosHash(p.x, p.z, 4) * 1e6)));
                    if (half) into.halfBare++; else into.bare++;
                }
            }

            // 3. the park trees: where leaves fall from; G8d: three in ten stay green, the rest go half-bare or bare
            for (int i = 0; i < parkTrees.Count; i++)
            {
                V2 q = parkTrees[i];
                if (!into.parkSeen.Add((long)Math.Round(q.x * 2) * 1000003L + (long)Math.Round(q.z * 2))) continue;
                into.fallFrom.Add(new[] { q.x, q.z });
                if (parkCrowns == null || i >= parkCrowns.Count) continue;
                double pr = parkCrowns[i][2], ph = parkCrowns[i][3];
                double k = PosHash(q.x, q.z, 5);
                if (k < 0.3) continue;                                   // stays green
                bool half = k < 0.75;
                double R = pr * 1.18;
                into.hidePark.Add(new[] { q.x, q.z, R, ph * 0.4, ph });
                double tw = PosHash(q.x, q.z, 6) * Math.PI * 2;
                Bones(crowns, clumps, la, q, ph * 0.5, ph * 0.4, ph * 0.6, R * 0.95, tw, half ? 6 + (int)(PosHash(q.x, q.z, 7) * 4) : (int)(PosHash(q.x, q.z, 7) * 3), new Random((int)(PosHash(q.x, q.z, 8) * 1e6)), 0.2);
                if (half) into.parkHalf++; else into.parkBare++;
            }

            foreach (MeshData m in new[] { litter, crowns, clumps }) if (m.tri.Count > 0) into.meshes.AddRange(Chunk(m));
            into.log.Add(string.Format("  {0}: {1} gutter drifts, {2} leaf patches, {3} on the road; street trees: {4} nearly bare, {5} half-bare, {6} turned",
                Kingsmere ? "Kingsmere" : Brix ? "Brixtun" : "the city", into.drifts - d0, into.patches - p0, into.roadLeaves - r0,
                into.bare - b0, into.halfBare - h0, into.turned - t0));
        }

        /// The top of a crown (the top band and its cap), just outside the green one.
        static void Crest(MeshData m, V2 p, double rad, double y0, double h, double g, UVRect col, double tw)
        {
            Band(m, p, rad * 0.85 * g, y0 + h * 0.7, rad * 0.35 * g, y0 + h + 0.04, 7, col, tw);
            List<P3> cap = new List<P3>();
            for (int i = 0; i < 7; i++) { double a = i * Math.PI * 2 / 7 + tw; cap.Add(new P3(p + new V2(Math.Sin(a), Math.Cos(a)) * rad * 0.35 * g, y0 + h + 0.04, col.Cu, col.Cv)); }
            m.FanUp(cap);
        }

        static V2 Spin(double t) { double a = t * Math.PI * 2; return new V2(Math.Sin(a), Math.Cos(a)); }

        /// G8d: a late-autumn tree's bones: the trunk carried on up as a leader, five or six branches reaching up and out
        /// to the old crown's edge, a twig off each, and `clumps` last clusters of leaves at the tips (cut out; their
        /// material draws both sides). trunkTop: where the trunk already drawn ends; crownBase: where the crown used to start.
        static void Bones(MeshData wood, MeshData leaves, DressAtlas la, V2 p, double trunkTop, double crownBase, double h, double rad, double tw, int clumps, Random r, double trunkR = 0.15)
        {
            UVRect bark = la["bark"], twig = la["twig"];
            double top = crownBase + h * 0.92;
            V2 lean = new V2(Math.Sin(tw), Math.Cos(tw)) * (rad * 0.12);
            Limb(wood, new P3(p, trunkTop - 0.15, bark.Cu, bark.Cv), new P3(p + lean, top, bark.Cu, bark.Cv), trunkR * 0.8, 0.035, bark);   // the leader
            int n = 5 + r.Next(2);
            List<P3> tips = new List<P3>();
            for (int i = 0; i < n; i++)
            {
                double a = tw + i * Math.PI * 2 / n + (r.NextDouble() - 0.5) * 0.6;
                V2 d = new V2(Math.Sin(a), Math.Cos(a));
                double sy = crownBase - 0.35 + r.NextDouble() * (h * 0.35);
                double reach = rad * (0.75 + r.NextDouble() * 0.3), ey = sy + h * (0.3 + r.NextDouble() * 0.35);
                V2 s0 = p + d * 0.05, e0 = p + d * reach;
                Limb(wood, new P3(s0, sy, bark.Cu, bark.Cv), new P3(e0, ey, bark.Cu, bark.Cv), 0.075, 0.022, bark);
                tips.Add(new P3(e0, ey, 0, 0));
                // a twig off it, from two thirds of the way along, swung to one side and up
                double side = (r.NextDouble() < 0.5 ? -1 : 1) * (0.45 + r.NextDouble() * 0.35);
                V2 td = new V2(Math.Sin(a + side), Math.Cos(a + side));
                V2 ts = p + d * (reach * 0.65); double tsy = sy + (ey - sy) * 0.65;
                V2 te = ts + td * (reach * 0.45); double tey = tsy + h * (0.12 + r.NextDouble() * 0.12);
                Limb(wood, new P3(ts, tsy, twig.Cu, twig.Cv), new P3(te, tey, twig.Cu, twig.Cv), 0.03, 0.01, twig);
                tips.Add(new P3(te, tey, 0, 0));
            }
            // the last leaves, at the tips
            for (int c = 0; c < clumps && tips.Count > 0; c++)
            {
                P3 t = tips[r.Next(tips.Count)];
                double s = 0.5 + r.NextDouble() * 0.45;
                V2 at = new V2(t.x, t.z) + new V2(r.NextDouble() - 0.5, r.NextDouble() - 0.5) * 0.5;
                UVRect tile = la["patch" + r.Next(2)];
                double y = t.y + (r.NextDouble() - 0.3) * 0.5;
                for (int k = 0; k < 2; k++)
                {
                    double ya = r.NextDouble() * Math.PI + k * Math.PI / 2;
                    V2 f = new V2(Math.Sin(ya), Math.Cos(ya)), nrm = f.Left;
                    P3 a0 = new P3(at - f * s, y - s * 0.6, tile.u0, tile.v0), b0 = new P3(at + f * s, y - s * 0.6, tile.u1, tile.v0);
                    P3 c0 = new P3(at + f * s, y + s * 0.6, tile.u1, tile.v1), d0 = new P3(at - f * s, y + s * 0.6, tile.u0, tile.v1);
                    leaves.Quad(a0, b0, c0, d0, nrm.x, 0, nrm.z);                // (its material draws both sides)
                }
            }
        }

        /// A tapering three-sided branch from a to b (radius ra at a, rb at b), in one colour.
        static void Limb(MeshData m, P3 a, P3 b, double ra, double rb, UVRect col)
        {
            double dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-4) return;
            dx /= len; dy /= len; dz /= len;
            // two directions across the branch
            double ux = -dz, uy = 0, uz = dx; double ul = Math.Sqrt(ux * ux + uz * uz);
            if (ul < 1e-4) { ux = 1; uy = 0; uz = 0; ul = 1; }
            ux /= ul; uz /= ul;
            double vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
            P3[] ring0 = new P3[3], ring1 = new P3[3];
            double[] nx = new double[3], ny = new double[3], nz = new double[3];
            for (int i = 0; i < 3; i++)
            {
                double ang = i * Math.PI * 2 / 3, cs = Math.Cos(ang), sn = Math.Sin(ang);
                nx[i] = ux * cs + vx * sn; ny[i] = uy * cs + vy * sn; nz[i] = uz * cs + vz * sn;
                ring0[i] = new P3(a.x + nx[i] * ra, a.y + ny[i] * ra, a.z + nz[i] * ra, col.Cu, col.Cv);
                ring1[i] = new P3(b.x + nx[i] * rb, b.y + ny[i] * rb, b.z + nz[i] * rb, col.Cu, col.Cv);
            }
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                m.Quad(ring0[i], ring0[j], ring1[j], ring1[i], nx[i] + nx[j], ny[i] + ny[j], nz[i] + nz[j]);
            }
        }
    }
}
