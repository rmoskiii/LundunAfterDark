// G3: autumn, always (Lundun → 37). Pure maths like the rest of the dressing: no UnityEngine types.
// Run Autumn() straight after a Dress(...) (the city's, Brixtun's or Kingsmere's): it reads the roads, the districts
// and the trees that Dress just placed, and adds, on its own random numbers (nothing Dress made moves or changes):
//   • fallen leaves: drifts in the gutters, patches on the pavements and under the street trees, the odd few on the
//     road. Most in Peckam, fewest in Knightsbury and Kingsmere (they sweep there)
//   • about one street tree in five turned amber, rust or gold, and one in seven just turning at the top. The rest
//     (most of them) stay green
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
        public int drifts, patches, roadLeaves, turned, turning;
        public List<string> log = new List<string>();
        internal HashSet<long> parkSeen = new HashSet<long>();      // park trees can be listed by more than one area
    }

    public static partial class Generator
    {
        public const string AutumnLitterMat = "AutumnLitter";
        public const string AutumnCrownMat = "AutumnCrown";
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
            int d0 = into.drifts, p0 = into.patches, r0 = into.roadLeaves, t0 = into.turned, n0 = into.turning;
            Random r = new Random(4417 + areaSeed);
            MeshData litter = new MeshData("AutumnLitter", "Autumn", AutumnLitterMat, false) { navIgnore = true };
            MeshData crowns = new MeshData("AutumnCrown", "Autumn", AutumnCrownMat, false) { navIgnore = true };

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
                        if (roll < amt * 0.85)
                        {
                            double hl = 1.3 + size * 0.6;
                            HQuad(litter, kerb - left * 0.45, left, 0.45, hl, PaintY + 0.004, la["drift" + (int)(pick * 3)]);
                            into.drifts++;
                        }
                        // scattered over the pavement (up to two patches a step in Peckam)
                        for (int k = 0; k < 2; k++)
                        {
                            if (ri.pave <= 0.9 || (k == 0 ? roll2 : pick2) >= amt * (k == 0 ? 0.7 : 0.35)) continue;
                            double off = k == 0 ? pick2 : roll2 / Math.Max(0.01, amt * 0.35);
                            V2 p = kerb + left * (0.5 + Math.Min(1, off) * (ri.pave - 1.0)) + dir * ((k == 0 ? -0.8 : 0.8) * size);
                            if (InBox(p) || NearOtherRoad(p, ri, 0.6)) continue;
                            double s = 0.75 + size * 0.5;
                            HQuad(litter, p, Spin(spin + k * 0.37), s, s, PaveY + 0.012 + k * 0.002, la["patch" + ((int)(pick * 2) + k) % 2]);
                            into.patches++;
                        }
                        // blown off the gutter onto the road (mostly the nearside lane)
                        if (roll3 < amt * 0.16)
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
                int under = (r.NextDouble() < amt ? 1 : 0) + (r.NextDouble() < amt * 0.5 ? 1 : 0);
                for (int k = 0; k < under; k++)
                {
                    double a = r.NextDouble() * Math.PI * 2, d = 0.3 + r.NextDouble() * 1.0, s = 0.6 + r.NextDouble() * 0.4;
                    HQuad(litter, p + new V2(Math.Sin(a), Math.Cos(a)) * d, Spin(r.NextDouble()), s, s, baseY + 0.012 + k * 0.002, la["patch" + k]);
                    into.patches++;
                }

                double turn = PosHash(p.x, p.z, 1);
                UVRect col = la[AutumnCrownSwatches[(int)(PosHash(p.x, p.z, 2) * 3) % 3]];
                double y0 = baseY + 2.2, g = 1.04;                      // just outside the green crown, so it hides it
                if (turn < 0.2)                                         // turned
                {
                    Band(crowns, p, rad * 0.55 * g, y0 - 0.01, rad * g, y0 + h * 0.35, 7, col, tw);
                    Band(crowns, p, rad * g, y0 + h * 0.35, rad * 0.85 * g, y0 + h * 0.7, 7, col, tw);
                    Crest(crowns, p, rad, y0, h, g, col, tw);
                    into.turned++;
                }
                else if (turn < 0.34)                                   // just turning, at the top
                {
                    Crest(crowns, p, rad, y0, h, g, col, tw);
                    into.turning++;
                }
            }

            // 3. the park trees (their own canopies stay green): only where leaves fall from
            foreach (V2 q in parkTrees)
                if (into.parkSeen.Add((long)Math.Round(q.x * 2) * 1000003L + (long)Math.Round(q.z * 2))) into.fallFrom.Add(new[] { q.x, q.z });

            foreach (MeshData m in new[] { litter, crowns }) if (m.tri.Count > 0) into.meshes.AddRange(Chunk(m));
            into.log.Add(string.Format("  {0}: {1} gutter drifts, {2} leaf patches, {3} on the road; {4} trees turned, {5} turning, {6} still green",
                Kingsmere ? "Kingsmere" : Brix ? "Brixtun" : "the city", into.drifts - d0, into.patches - p0, into.roadLeaves - r0,
                into.turned - t0, into.turning - n0, treeCrowns.Count - (into.turned - t0) - (into.turning - n0)));
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
    }
}
