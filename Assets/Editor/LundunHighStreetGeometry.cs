// G8b: shops along every high street (Lundun → 41). Pure maths like the rest of the generator: no UnityEngine types.
// The dressing (Lundun → 13) gave Peckam shop fronts on most buildings, but left gaps elsewhere: Gilmore Road's parade
// was capped, the Canary Quay peninsula was skipped, and every high street lost a few units to the dice. This works out
// the dressing exactly as 13 does (same seeds), finds every shop-type building facing one of the agreed high streets
// (the G8b map), and adds a shop front to each unit that hasn't got one, using the dressing's own shop fronts (lit sign,
// window with its own interior, glass door; some shuttered, with graffiti), with its own atlas of signs: the area's
// names and some new ones. Houses and flats stay homes; side streets are untouched; Peckam and Knightsbury are left as
// they are. Run it after 13 (and 28 for Brixtun); it only adds.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class HighStreetResult
    {
        public List<MeshData> meshes = new List<MeshData>();
        public DressAtlas atlas;
        public Dictionary<string, int> perRoad = new Dictionary<string, int>();   // road name → shop fronts added
        public int added, shut;
        public List<ShopFrontInfo> fronts = new List<ShopFrontInfo>();
    }

    public static partial class Generator
    {
        // the agreed high streets (G8b map): the city's, then Brixtun's
        static readonly HashSet<string> HighCity = new HashSet<string> { "R10", "R11", "R12", "R14", "R15", "R16", "R23" };
        static readonly HashSet<string> HighBrixtun = new HashSet<string> { "XR01", "XR02", "XR03", "XR04", "XR05", "XR06" };

        // new names, on top of each area's own (all made up)
        static readonly string[][] MoreGilmore = {
            new[] { "PARADE CONVENIENCE", "1a5e9a", "ffffff" }, new[] { "THURLOW CASH & CARRY", "f2c200", "1a1a1a" },
            new[] { "SUNRISE NAILS", "f5b7d0", "6b1f4a" }, new[] { "QUICKFIX PHONES", "0aa0d8", "ffffff" },
            new[] { "MAMA KEZ KITCHEN", "2b7a3b", "ffe9a8" }, new[] { "ESTATE BAKERY", "f1e3c6", "6b3a1e" },
            new[] { "TOP TOWN PIZZA", "c8102e", "ffffff" }, new[] { "GILMORE DRY CLEAN", "14506e", "ffffff" } };
        static readonly string[][] MoreCanary = {
            new[] { "PLATFORM COFFEE", "1c1c1c", "e8b04a" }, new[] { "TIDE & TONIC", "0f3b4f", "c9f2ff" },
            new[] { "QUAYSIDE EXPRESS", "e5251d", "ffffff" }, new[] { "BYTE REPAIR", "111111", "35e0a0" },
            new[] { "HARBOUR NOODLE BAR", "a3161b", "ffd9a0" }, new[] { "LEDGER & LOAF", "f6f1e7", "2b2b2b" },
            new[] { "SPIN STUDIO", "0b0b0b", "ff2d75" }, new[] { "WESTFERRY PHARMACY", "0c9156", "ffffff" },
            new[] { "POOLSIDE FLORIST", "e9f2e4", "2f5a24" } };
        static readonly string[][] MoreBrixtun = {
            new[] { "COLDHARBOR PATTY HOUSE", "f2b300", "8e1b10" }, new[] { "BOOKS & BREW", "2a3d2e", "f2e6c4" },
            new[] { "YARD VIBES BARBERS", "0e0e0e", "ffd200" }, new[] { "PLANTAIN PALACE", "e0a100", "1d4d1d" },
            new[] { "NINE NIGHT ROTI", "6d1f6d", "ffe08a" }, new[] { "STOCKWELL SOUNDS", "1a1a1a", "ff7a1a" },
            new[] { "ACRE LANE LAUNDERETTE", "8fd3f0", "0d3550" }, new[] { "BRIXTUN BOXING CLUB", "a01010", "ffffff" } };

        class HighUnit { public V2 c, n, t; public double w; public string road, area; }

        public static HighStreetResult G8HighStreetShops(LundunWorldData data)
        {
            HighStreetResult res = new HighStreetResult();
            List<HighUnit> units = new List<HighUnit>();

            // the city: work the dressing out exactly as menu 13 does, then find the gaps
            GreyboxResult city = Build(data);
            DressingResult cityDress = Dress(data, city);
            FindGaps(city, cityDress, HighCity, "CITY", units);
            // Brixtun (menu 28's dressing)
            GreyboxResult brix = BuildExpansion(data);
            if (brix != null && ExpansionWorld != null)
            {
                DressingResult brixDress = Dress(ExpansionWorld, brix);
                FindGaps(brix, brixDress, HighBrixtun, "BRIX", units);
            }
            Build(data);                                                         // leave the generator on the city

            // the signs: every name these streets use, painted into an atlas of their own
            List<string[]> gil = new List<string[]>(GilmoreShops); gil.AddRange(MoreGilmore);
            List<string[]> can = new List<string[]>(CanaryShops); can.AddRange(MoreCanary);
            List<string[]> bri = new List<string[]>(BrixtunShops); bri.AddRange(MoreBrixtun);
            DressAtlas a = new DressAtlas(2048, 2048, 4141);
            foreach (var kv in Swatches) a.Swatch(kv.Key, Col.Hex(kv.Value));
            a.NewShelf();
            HashSet<string> painted = new HashSet<string>();
            foreach (List<string[]> list in new[] { gil, can, bri })
                foreach (string[] s in list)
                    if (painted.Add(s[0])) a.Sign("shop_" + s[0], 400, 56, s[0], Col.Hex(s[1]), Col.Hex(s[2]), Col.Hex(s[2]));
            a.NewShelf();
            for (int i = 0; i < Tags.Length; i++) a.Graffiti("tag_" + i, Tags[i], i);
            a.Shutter("shutter");
            a.Alloc("_room", 8, 360, out int _x, out int _y);                    // leaves a wide tail for the shop interiors
            a.NewShelf();
            ShopWindowArt.Paint(a);
            res.atlas = a;

            // build them with the dressing's own shop fronts, into meshes of their own
            DressAtlas keepA = A; DressingResult keepDr = D; Random keepRng = drng;
            MeshData s0 = mSolid, c0 = mCut, g0 = mGlow;
            try
            {
                A = a; D = new DressingResult { atlas = a }; drng = new Random(4108);
                mSolid = new MeshData("HighStreetSolid", "HighStreets", "HighSolid", false) { navIgnore = true };
                mCut = new MeshData("HighStreetCutout", "HighStreets", "HighCutout", false) { navIgnore = true };
                mGlow = new MeshData("HighStreetGlow", "HighStreets", "HighGlow", false) { navIgnore = true };
                Dictionary<string, int> next = new Dictionary<string, int>();
                foreach (HighUnit u in units)
                {
                    List<string[]> list = u.area == "BRIX" ? bri : u.road == "R23" ? gil : can;
                    int k; next.TryGetValue(u.area + u.road, out k);
                    // round the area's names, starting somewhere different on each street, so neighbours differ
                    int seed = 0; foreach (char ch in u.road) seed = seed * 31 + ch;
                    string[] shop = list[(k * 7 + (seed & 0x7fff)) % list.Count];
                    next[u.area + u.road] = k + 1;
                    double shutChance = u.road == "R23" ? 0.25 : u.area == "BRIX" ? 0.15 : 0.1;
                    bool shut = drng.NextDouble() < shutChance;
                    Shop(u.c, u.n, u.t, u.w, shop, shut);
                    res.added++; if (shut) res.shut++;
                    string rn = RoadName(u);
                    int c; res.perRoad.TryGetValue(rn, out c); res.perRoad[rn] = c + 1;
                }
                res.fronts.AddRange(D.shopFronts);
                foreach (MeshData m in new[] { mSolid, mCut, mGlow }) if (m.tri.Count > 0) res.meshes.AddRange(Chunk(m));
            }
            finally { A = keepA; D = keepDr; drng = keepRng; mSolid = s0; mCut = c0; mGlow = g0; }
            return res;
        }

        static readonly Dictionary<HighUnit, string> unitRoadName = new Dictionary<HighUnit, string>();
        static string RoadName(HighUnit u) { string n; return unitRoadName.TryGetValue(u, out n) ? n : u.road; }

        /// Every shop-type building facing one of `high`'s roads: each unit of its front without a shop front gets one.
        static void FindGaps(GreyboxResult world, DressingResult dressed, HashSet<string> high, string area, List<HighUnit> units)
        {
            List<ShopFrontInfo> have = dressed.shopFronts;
            List<double[]> lit = dressed.shopLights;
            foreach (BuildingInfo b in world.buildings)
            {
                if (!KitAllowsShops(b)) continue;                               // houses and flats stay homes
                // the face nearest a road with a pavement (exactly as the dressing picks it)
                V2 f = b.dir.Norm, l = f.Left;
                V2[] normals = { l, l * -1, f, f * -1 };
                double[] halfDepth = { b.dep / 2, b.dep / 2, b.len / 2, b.len / 2 };
                double[] faceLen = { b.len, b.len, b.dep, b.dep };
                int bestFace = -1; double bestGap = 6; RoadInfo road = null;
                for (int i = 0; i < 4; i++)
                {
                    V2 fc = b.c + normals[i] * halfDepth[i];
                    foreach (RoadInfo ri in roads.Values)
                    {
                        if (ri.pave < 3 || ri.cls == "bridge") continue;
                        double gap = ri.line.DistanceTo(fc) - ri.w / 2 - ri.pave;
                        if (gap < bestGap && V2.Dot(normals[i], ClosestOn(ri, fc) - fc) > 0) { bestGap = gap; bestFace = i; road = ri; }
                    }
                }
                if (bestFace < 0 || !high.Contains(road.r.id)) continue;
                V2 n = normals[bestFace], t = n.Left;
                V2 face = b.c + n * halfDepth[bestFace];
                double width = faceLen[bestFace];
                int count = Math.Max(1, (int)Math.Floor((width - 1.0) / 7.0));
                double unitW = (width - 1.0) / count;
                for (int u = 0; u < count; u++)
                {
                    V2 uc = face + t * (-width / 2 + 0.5 + unitW * (u + 0.5));
                    bool taken = false;
                    foreach (ShopFrontInfo s in have) if (V2.Dist(new V2(s.x, s.z), uc) < unitW * 0.6) { taken = true; break; }
                    if (!taken) foreach (double[] p in lit) if (p[5] > 0.5 && V2.Dist(new V2(p[0], p[1]), uc) < unitW * 0.6) { taken = true; break; }   // a pub
                    if (taken) continue;
                    HighUnit hu = new HighUnit { c = uc, n = n, t = t, w = unitW, road = road.r.id, area = area };
                    unitRoadName[hu] = road.r.name;
                    units.Add(hu);
                }
            }
        }
    }
}
