// V2a step 2: what the land is for. Pure C# (no UnityEngine), part of the street dressing (Lundun → 13 / 28 / 29).
// Step 1 built the streets up and put a city floor under everything; this gives the land between a use:
//   estates       Gilmore and Angel Fields get their grass back between the blocks (tarmac aprons at the garages and shops)
//   back gardens  behind every terrace and town house: a strip of lawn, a fence or wall along the back, the odd tree
//   the river     a paved promenade along both embankment walls (benches facing the water), grass and trees behind it
//   Rye Common    the big empty block east of James's street: a real common (paths, two pitches, a cage, trees, benches,
//                 railings on the Ring side)
//   Quay Freight  the empty land north of Canary Quay: a container depot (stacks, gantry cranes, floodlights, sheds,
//                 chain-link fence), seen from the Ring
//   Loughborough Rec   Brixtun's empty block: a recreation ground with a running track, pitches and a pavilion
// Everything is flat colour from the dressing atlas's existing swatches (no new textures), laid 3 cm above the ground,
// and added after everything else in the dressing with its own random numbers, so nothing already there moves.
// The building plan (LundunCityPlan) keeps the Common and the depot clear of new buildings.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        // ------------------------------------------------------------------ the zones
        /// Rye Common: between Choumert Grove's houses, the Ring (south and east) and the South Embankment's buildings.
        static readonly double[] CommonPoly = { 1070, 58, 1542, 58, 1542, 468, 1526, 440, 1500, 334, 1412, 262, 1380, 206, 1070, 196 };
        /// Quay Freight: north of Canary Quay, inside the Ring.
        const double DepotX0 = 992, DepotX1 = 1540, DepotZ0 = 1010, DepotZ1 = 1142;
        /// Loughborough Rec (Brixtun).
        const double RecX0 = 713, RecX1 = 968, RecZ0 = -680, RecZ1 = -412;

        static List<V2> commonPts;
        static List<V2> CommonPts { get { if (commonPts == null) { commonPts = new List<V2>(); for (int i = 0; i + 1 < CommonPoly.Length; i += 2) commonPts.Add(new V2(CommonPoly[i], CommonPoly[i + 1])); } return commonPts; } }
        static bool InCommon(V2 q) { return !ExpansionMode && InPoly(q, CommonPts); }
        static bool InDepot(V2 q) { return !ExpansionMode && q.x > DepotX0 && q.x < DepotX1 && q.z > DepotZ0 && q.z < DepotZ1; }

        /// For the map picture: the plan's open spaces (true = green, false = paved). The rec only when Brixtun is built.
        public static List<KeyValuePair<List<V2>, bool>> PlanMapZones(bool withExpansions)
        {
            List<KeyValuePair<List<V2>, bool>> list = new List<KeyValuePair<List<V2>, bool>>();
            if (!UseCityPlan) return list;
            list.Add(new KeyValuePair<List<V2>, bool>(new List<V2>(CommonPts), true));
            list.Add(new KeyValuePair<List<V2>, bool>(new List<V2> { new V2(DepotX0, DepotZ0), new V2(DepotX1, DepotZ0), new V2(DepotX1, DepotZ1), new V2(DepotX0, DepotZ1) }, false));
            if (withExpansions) list.Add(new KeyValuePair<List<V2>, bool>(new List<V2> { new V2(RecX0, RecZ0), new V2(RecX1, RecZ0), new V2(RecX1, RecZ1), new V2(RecX0, RecZ1) }, true));
            return list;
        }

        /// For LundunCityPlan: no new buildings on the Common or in the depot.
        static void PlanReserveZones()
        {
            if (ExpansionMode) return;
            for (int k = 0; k < NX * NZ; k++)
            {
                V2 c = new V2((k % NX + 0.5) * Cell + GX0, (k / NX + 0.5) * Cell + GZ0);
                if (InDepot(c) || InPoly(c, CommonPts) || EdgeDist(c, CommonPts) < 2) blocked[k] = true;
            }
        }

        // ------------------------------------------------------------------ the ground grid
        const byte GNone = 0, GLawn = 1, GSlab = 2, GPath = 3, GYard = 4, GTrack = 5;
        const double GroundCoverY = 0.03;
        static byte[] gcell; static bool[] gno;
        /// The ground-cover grid: 1 m cells over the same area as the building grid (finer, so edges follow the streets).
        const double GC = 1.0;
        static int GNX, GNZ;
        static int GIndex(V2 q)
        {
            int ix = (int)Math.Floor((q.x - GX0) / GC), iz = (int)Math.Floor((q.z - GZ0) / GC);
            if (ix < 0 || iz < 0 || ix >= GNX || iz >= GNZ) return -1;
            return iz * GNX + ix;
        }

        static void GMark(bool[] g, V2 a, V2 b, double r)
        {
            int x0 = Math.Max(0, (int)((Math.Min(a.x, b.x) - r - GX0) / GC)), x1 = Math.Min(GNX - 1, (int)((Math.Max(a.x, b.x) + r - GX0) / GC));
            int z0 = Math.Max(0, (int)((Math.Min(a.z, b.z) - r - GZ0) / GC)), z1 = Math.Min(GNZ - 1, (int)((Math.Max(a.z, b.z) + r - GZ0) / GC));
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    V2 c = new V2((ix + 0.5) * GC + GX0, (iz + 0.5) * GC + GZ0);
                    if (Line.SegDist(c, a, b) <= r) g[iz * GNX + ix] = true;
                }
        }

        static V2 GCentre(int k) { return new V2((k % GNX + 0.5) * GC + GX0, (k / GNX + 0.5) * GC + GZ0); }

        /// Paint a rectangle (centre c, long axis f, half length hl, half width hw) with a ground type, where allowed.
        static void GRect(V2 c, V2 f, double hl, double hw, byte type, bool force = false)
        {
            f = f.Norm; V2 l = f.Left;
            double ext = Math.Abs(f.x) * hl + Math.Abs(l.x) * hw, ezt = Math.Abs(f.z) * hl + Math.Abs(l.z) * hw;
            int x0 = Math.Max(0, (int)((c.x - ext - GX0) / GC)), x1 = Math.Min(GNX - 1, (int)((c.x + ext - GX0) / GC));
            int z0 = Math.Max(0, (int)((c.z - ezt - GZ0) / GC)), z1 = Math.Min(GNZ - 1, (int)((c.z + ezt - GZ0) / GC));
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    int k = iz * GNX + ix;
                    V2 d = GCentre(k) - c;
                    if (Math.Abs(V2.Dot(d, f)) > hl || Math.Abs(V2.Dot(d, l)) > hw) continue;
                    if (force) gcell[k] = type; else if (!gno[k]) gcell[k] = type;
                }
        }

        static byte GAt(V2 q) { int ci = GIndex(q); return ci < 0 ? GNone : gcell[ci]; }

        // ------------------------------------------------------------------ the pass
        /// Called last by Dress (after the railway / market / Kingsmere dressing).
        static void DressCityPlan()
        {
            if (!UseCityPlan || blocked == null) return;
            Random pr = new Random(Brix ? 2261 : Kingsmere ? 2262 : 2260);
            GNX = (int)Math.Round(NX * Cell / GC); GNZ = (int)Math.Round(NZ * Cell / GC);
            int n = GNX * GNZ;
            gcell = new byte[n]; gno = new bool[n];

            // ---- where ground cover never goes: roads and pavements, parks, buildings, the railway, special plots
            List<RoadInfo> all = new List<RoadInfo>(roads.Values);
            if (ExpansionMode && contextRoads != null) all.AddRange(contextRoads.Values);
            foreach (RoadInfo ri in all)
            {
                double r = ri.w / 2 + Math.Max(ri.pave, ri.verge) + 0.5;
                for (int i = 0; i + 1 < ri.line.p.Count; i++) GMark(gno, ri.line.p[i], ri.line.p[i + 1], r);
            }
            foreach (JInfo ji in juncs.Values) GMark(gno, ji.c, ji.c, ji.radius + 4.5);
            if (river != null) for (int i = 0; i + 1 < river.p.Count; i++) GMark(gno, river.p[i], river.p[i + 1], WallOuter + 0.2);
            foreach (LwPoly park in W.parks)
            {
                List<V2> poly = Pts(park.polygon);
                double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (V2 q in poly) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
                for (int iz = Math.Max(0, (int)((minZ - 3 - GZ0) / GC)); iz <= Math.Min(GNZ - 1, (int)((maxZ + 3 - GZ0) / GC)); iz++)
                    for (int ix = Math.Max(0, (int)((minX - 3 - GX0) / GC)); ix <= Math.Min(GNX - 1, (int)((maxX + 3 - GX0) / GC)); ix++)
                    {
                        V2 c = GCentre(iz * GNX + ix);
                        if (InPoly(c, poly) || EdgeDist(c, poly) < 1.6) gno[iz * GNX + ix] = true;
                    }
            }
            foreach (BuildingInfo b in blds)
            {
                V2 f = b.dir.Norm, l = f.Left;
                for (double a = -b.len / 2 - 0.4; a <= b.len / 2 + 0.4; a += 0.9)
                    for (double c = -b.dep / 2 - 0.4; c <= b.dep / 2 + 0.4; c += 0.9) { int ci = GIndex(b.c + f * a + l * c); if (ci >= 0) gno[ci] = true; }
            }
            if (railPlan != null)
                for (int i = 0; i + 1 < railPlan.line.p.Count; i++)
                {
                    double s = (railPlan.line.ch[i] + railPlan.line.ch[i + 1]) / 2;
                    GMark(gno, railPlan.line.p[i], railPlan.line.p[i + 1], RailKind(s) == 'E' ? RailHW + RailDeck * BankSlope + 1.5 : RailStationHW + 9);
                }
            for (int i = 0; i + 1 < PlanKeepClear.Length; i += 2) { V2 k = new V2(PlanKeepClear[i], PlanKeepClear[i + 1]); GMark(gno, k, k, 9); }
            if (Kingsmere) foreach (double[] z in KingsmereRestricted) GRectNo(z[0] - 8, z[2] + 8, z[1] - 8, z[3] + 8);
            if (!ExpansionMode)
            {
                GRectNo(boxX0 - 2, boxX1 + 2, boxZ0 - 2, boxZ1 + 2);          // the old high street
                GRectNo(298, 362, 353, 407);                                  // the estate courtyard (mission 04's fight)
                GMark(gno, WheelSite, WheelSite, 44);
                foreach (PlaceSite s in PlanPlaces())
                {
                    if (!s.ok) continue;
                    for (double a = -s.len / 2 - 1.5; a <= s.len / 2 + 1.5; a += 0.9)
                        for (double b = -1; b <= s.depth + 1.5; b += 0.9) { int ci = GIndex(s.Corner(a, b)); if (ci >= 0) gno[ci] = true; }
                }
            }

            // ---- estates: grass between the blocks
            List<V2> est;
            if (dpolys.TryGetValue("D_GILMOR", out est))
                for (int k = 0; k < n; k++) if (!gno[k] && InPoly(GCentre(k), est)) gcell[k] = GLawn;

            // ---- the residential districts: behind the building line it's gardens, not tarmac (the first few metres off
            //      the pavement stay hard: forecourts, front paths). Canary Quay's yards stay hard.
            bool[] nearRoad = new bool[n];
            foreach (RoadInfo ri in all)
            {
                double r = ri.w / 2 + Math.Max(ri.pave, ri.verge) + 3.2;
                for (int i = 0; i + 1 < ri.line.p.Count; i++) GMark(nearRoad, ri.line.p[i], ri.line.p[i + 1], r);
            }
            Dictionary<long, bool> resBlock = new Dictionary<long, bool>();
            Func<V2, bool> residential = q =>
            {
                long key = ((long)Math.Floor(q.x / 8) << 20) ^ (long)(Math.Floor(q.z / 8) + 100000);
                bool res;
                if (resBlock.TryGetValue(key, out res)) return res;
                V2 bc = new V2(Math.Floor(q.x / 8) * 8 + 4, Math.Floor(q.z / 8) * 8 + 4);
                string did = DistrictAt(bc, dpolys);
                if (did == null && !ExpansionMode && bc.x > 3 && bc.x < 1597 && bc.z > 44 && bc.z < 1156) did = NearestDistrict(bc, dpolys);
                res = did == "D_PECKAM" || did == "D_KNIGHT";
                resBlock[key] = res;
                return res;
            };
            List<V2> yardTrees = new List<V2>();
            for (int k = 0; k < n; k++)
            {
                if (gno[k] || nearRoad[k] || gcell[k] != GNone) continue;
                V2 c = GCentre(k);
                if (!residential(c)) continue;
                gcell[k] = GLawn;
                if ((KHash(k % GNX, k / GNX) % 900) == 0) yardTrees.Add(c);          // about one tree to every 900 m² of garden
            }

            // ---- back gardens
            List<double[]> fences = new List<double[]>();       // ax, az, bx, bz, kind (0 wood, 1 brick)
            List<V2> gardenTrees = new List<V2>();
            foreach (BuildingInfo b in blds)
            {
                KitPlan p = KitPlanFor(b); if (p == null) continue;
                bool house = p.type == KitType.Terrace || p.type == KitType.Georgian || (p.type == KitType.Stucco && !p.mixed);
                if (!house || p.gap > 12) continue;                          // houses on a street; the scattered ones inside a block have no clear back
                bool posh = b.district == "D_KNIGHT";
                double depth = posh ? 9 : 7;
                V2 back = p.c - p.n * (p.D / 2), t = p.n.Left;
                // how much of the garden's far edge is free? (another building or a road may be in the way)
                int free = 0, tot = 0;
                for (double a = -p.W / 2 + 1; a <= p.W / 2 - 1; a += 2) { tot++; int ci = GIndex(back - p.n * depth + t * a); if (ci >= 0 && !gno[ci]) free++; }
                if (tot == 0 || free * 2 < tot) depth = posh ? 5 : 4;
                GRect(back - p.n * (depth / 2 + 0.2), t, p.W / 2, depth / 2, GLawn);
                // the fence along the back, where its line is clear
                double run0 = double.NaN;
                for (double a = -p.W / 2; a <= p.W / 2 + 0.01; a += 1)
                {
                    int ci = GIndex(back - p.n * (depth + 0.3) + t * a);
                    bool ok = ci >= 0 && !gno[ci] && a < p.W / 2 - 0.01;
                    if (ok && double.IsNaN(run0)) run0 = a;
                    if (!ok && !double.IsNaN(run0))
                    {
                        if (a - run0 >= 3) { V2 fa = back - p.n * (depth + 0.3) + t * run0, fb = back - p.n * (depth + 0.3) + t * a; fences.Add(new[] { fa.x, fa.z, fb.x, fb.z, posh ? 1.0 : 0.0 }); }
                        run0 = double.NaN;
                    }
                }
                if (pr.NextDouble() < 0.4) gardenTrees.Add(back - p.n * (depth * 0.55) + t * ((pr.NextDouble() - 0.5) * (p.W - 4)));
            }

            // ---- the river: promenade along the wall, grass behind it
            if (!ExpansionMode && river != null)
            {
                double rx0 = double.MaxValue, rx1 = double.MinValue, rz0 = double.MaxValue, rz1 = double.MinValue;
                foreach (V2 q in river.p) { rx0 = Math.Min(rx0, q.x); rx1 = Math.Max(rx1, q.x); rz0 = Math.Min(rz0, q.z); rz1 = Math.Max(rz1, q.z); }
                double reach = WallOuter + 46;
                for (int iz = Math.Max(0, (int)((rz0 - reach - GZ0) / GC)); iz <= Math.Min(GNZ - 1, (int)((rz1 + reach - GZ0) / GC)); iz++)
                    for (int ix = Math.Max(0, (int)((rx0 - reach - GX0) / GC)); ix <= Math.Min(GNX - 1, (int)((rx1 + reach - GX0) / GC)); ix++)
                    {
                        int k = iz * GNX + ix; if (gno[k]) continue;
                        double d = river.DistanceTo(GCentre(k));
                        if (d > reach) continue;
                        if (gcell[k] == GNone) gcell[k] = GLawn;     // (the promenade is laid over the first 6 m as a ribbon: DressRiverside)
                    }
            }

            // ---- shops, garages, offices: a hard apron in front (no grass up to the shutters)
            foreach (BuildingInfo b in blds)
            {
                KitPlan p = KitPlanFor(b); if (p == null) continue;
                bool hard = p.type == KitType.Parade || p.type == KitType.Pub || p.type == KitType.EstParade || p.type == KitType.Garages || p.type == KitType.Office ||
                            p.type == KitType.Warehouse || p.type == KitType.Tower || p.type == KitType.DeptStore || p.type == KitType.MusicHall || p.type == KitType.Civic || p.mixed;
                if (!hard) continue;
                double reachF = p.type == KitType.Garages ? 9 : Math.Min(12, Math.Max(3, p.gap + 1));
                GRect(p.c + p.n * (p.D / 2 + reachF / 2), p.n.Left, p.W / 2 + 1, reachF / 2, GNone, true);
            }

            if (!ExpansionMode) { PaintCommon(pr); PaintDepot(pr); }
            if (Brix) PaintRec(pr);

            EmitGround();

            // ---- things standing on it (after the ground, so they sit on the final layout)
            foreach (double[] f in fences) GardenFence(new V2(f[0], f[1]), new V2(f[2], f[3]), f[4] > 0.5);
            foreach (V2 q in gardenTrees) if (GAt(q) == GLawn && Free(q, 1.6)) Tree(q, GroundCoverY, false, true);
            foreach (V2 q in yardTrees)
            {
                bool room = true;
                foreach (V2 o in new[] { new V2(2.6, 0), new V2(-2.6, 0), new V2(0, 2.6), new V2(0, -2.6) }) if (GAt(q + o) != GLawn) room = false;
                if (room && Free(q, 2.6)) Tree(q, GroundCoverY, false, true);
            }
            if (!ExpansionMode && river != null) DressRiverside(pr);
            if (!ExpansionMode) { DressCommon(pr); DressDepot(pr); }
            if (Brix) DressRec(pr);
        }

        static void GRectNo(double x0, double x1, double z0, double z1)
        {
            for (int iz = Math.Max(0, (int)((z0 - GZ0) / GC)); iz <= Math.Min(GNZ - 1, (int)((z1 - GZ0) / GC)); iz++)
                for (int ix = Math.Max(0, (int)((x0 - GX0) / GC)); ix <= Math.Min(GNX - 1, (int)((x1 - GX0) / GC)); ix++) gno[iz * GNX + ix] = true;
        }

        /// One quad per block of the same ground: runs along each row, joined to the identical run in the row above.
        static void EmitGround()
        {
            UVRect[] sw = { S("lawn"), S("lawn"), S("slab"), S("drive"), S("concrete"), S("tactile") };
            int quads = 0, lawn = 0;
            Dictionary<long, int> open = new Dictionary<long, int>();        // (x0, x1, type) → the row the block started on
            Action<long, int, int> close = (key, zStart, zEnd) =>
            {
                int t = (int)(key & 7), ix0 = (int)((key >> 3) & 0xFFFFF), ix1 = (int)(key >> 23);
                double x0 = ix0 * GC + GX0, x1 = ix1 * GC + GX0, z0 = zStart * GC + GZ0, z1 = zEnd * GC + GZ0;
                double u = sw[t].Cu, v = sw[t].Cv, y = GroundCoverY + (t == GLawn ? 0 : 0.006);
                mSolid.Quad(new P3(new V2(x0, z0), y, u, v), new P3(new V2(x0, z1), y, u, v), new P3(new V2(x1, z1), y, u, v), new P3(new V2(x1, z0), y, u, v), 0, 1, 0);
                quads++;
            };
            for (int iz = 0; iz <= GNZ; iz++)
            {
                Dictionary<long, int> next = new Dictionary<long, int>();
                if (iz < GNZ)
                {
                    int ix = 0;
                    while (ix < GNX)
                    {
                        byte t = gcell[iz * GNX + ix];
                        if (t == GNone) { ix++; continue; }
                        int j = ix; while (j < GNX && gcell[iz * GNX + j] == t && j - ix < 100) j++;     // (blocks at most 100 m long, so the 100 m chunks cut them cleanly)
                        long key = ((long)j << 23) | ((long)ix << 3) | t;
                        int started;
                        if (open.TryGetValue(key, out started) && iz - started < 100) { next[key] = started; open.Remove(key); }
                        else next[key] = iz;
                        if (t == GLawn) lawn += j - ix;
                        ix = j;
                    }
                }
                foreach (KeyValuePair<long, int> kv in open) close(kv.Key, kv.Value, iz);
                open = next;
            }
            D.Count("ground cover pieces", quads);
            D.Count("lawn (square metres)", (int)(lawn * GC * GC));
        }

        static void GardenFence(V2 a, V2 b, bool brick)
        {
            double L = V2.Dist(a, b); if (L < 2) return;
            V2 f = (b - a).Norm, mid = (a + b) * 0.5;
            if (brick) { SBox(mSolid, mid, f, L / 2, 0.11, 0, 1.9, S("brick")); SBox(mSolid, mid, f, L / 2, 0.14, 1.9, 1.98, S("slab")); }
            else SBox(mSolid, mid, f, L / 2, 0.035, 0, 1.7, S("wood"));
            Collider("GardenFence", mid, f, L, 0.25, 0, brick ? 1.98 : 1.7);
            D.Count("garden fence metres", (int)L);
        }

        // ------------------------------------------------------------------ the river
        static void DressRiverside(Random pr)
        {
            double L = river.Length;
            // the promenade: a paved ribbon along each wall, following the river (broken where a road or bridge crosses)
            UVRect slab = S("slab"); double py = GroundCoverY + 0.012;
            foreach (int side in new[] { 1, -1 })
                for (double s = 0; s < L - 0.01; s += 4)
                {
                    V2 d0, d1; V2 p0 = river.At(s, out d0), p1 = river.At(Math.Min(L, s + 4), out d1);
                    V2 a0 = p0 + d0.Left * (side * (WallOuter + 0.05)), a1 = p1 + d1.Left * (side * (WallOuter + 0.05));
                    V2 b0 = p0 + d0.Left * (side * (WallOuter + 6.2)), b1 = p1 + d1.Left * (side * (WallOuter + 6.2));
                    int c0 = GIndex((a0 + b1) * 0.5), c1 = GIndex((b0 + a1) * 0.5), c2 = GIndex((b0 + b1) * 0.5);
                    if (c0 < 0 || c1 < 0 || c2 < 0 || gno[c0] || gno[c1] || gno[c2]) continue;
                    mSolid.Quad(new P3(a0, py, slab.Cu, slab.Cv), new P3(a1, py, slab.Cu, slab.Cv), new P3(b1, py, slab.Cu, slab.Cv), new P3(b0, py, slab.Cu, slab.Cv), 0, 1, 0);
                }
            foreach (int side in new[] { 1, -1 })
            {
                int k = 0;
                for (double s = 6; s < L - 6; s += 8.5, k++)
                {
                    V2 dir; V2 p = river.At(s, out dir); V2 outN = dir.Left * side;
                    if (k % 2 == 0)
                    {
                        V2 q = p + outN * (WallOuter + 13 + pr.NextDouble() * 8);
                        if (GAt(q) == GLawn && Free(q, 2.2) && !InPlanned(q, 3)) Tree(q, GroundCoverY, false, true);
                    }
                    if (k % 4 == 1)
                    {
                        V2 q = p + outN * (WallOuter + 4.2);
                        if (GAt(q) == GLawn && Free(q, 1.4)) { Bench(q, outN * -1, GroundCoverY); if (k % 8 == 1 && Free(q + dir * 1.5, 0.4)) Bin(q + dir * 1.5, GroundCoverY, false); }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ Rye Common
        static readonly double[][] CommonPaths =
        {
            new double[] { 1073, 128, 1539, 128 }, new double[] { 1190, 60, 1190, 196 }, new double[] { 1310, 60, 1310, 200 }, new double[] { 1440, 60, 1440, 270 },
            new double[] { 1440, 270, 1520, 420 }, new double[] { 1073, 190, 1190, 128 }, new double[] { 1310, 128, 1440, 60 },
        };
        static readonly double[][] CommonPitches = { new double[] { 1250, 94, 27, 16 }, new double[] { 1375, 162, 27, 16 } };   // x, z, half length (east-west), half width
        static readonly double[] CommonCage = { 1128, 92 };

        static void PaintCommon(Random pr)
        {
            List<V2> poly = CommonPts;
            for (int k = 0; k < GNX * GNZ; k++) if (!gno[k] && InPoly(GCentre(k), poly)) gcell[k] = GLawn;
            bool[] path = new bool[GNX * GNZ];
            foreach (double[] s in CommonPaths) GMark(path, new V2(s[0], s[1]), new V2(s[2], s[3]), 1.7);
            for (int k = 0; k < GNX * GNZ; k++) if (path[k] && gcell[k] == GLawn) gcell[k] = GPath;
        }

        static void DressCommon(Random pr)
        {
            List<V2> poly = CommonPts;
            // the pitches, with goals
            foreach (double[] pt in CommonPitches)
            {
                V2 c = new V2(pt[0], pt[1]);
                HQuad(mSolid, c, new V2(0, 1), pt[3], pt[2], GroundCoverY + 0.015, A["pitch"]);
                foreach (int s in new[] { -1, 1 })
                {
                    V2 g = c + new V2(1, 0) * (s * (pt[2] - 0.6));
                    foreach (int e in new[] { -1, 1 }) SBox(mSolid, g + new V2(0, 1) * (e * 3.66), new V2(0, 1), 0.06, 0.06, 0, 2.44, S("white"));
                    SBox(mSolid, g, new V2(0, 1), 3.72, 0.06, 2.38, 2.5, S("white"));
                    Collider("Goal", g, new V2(0, 1), 7.4, 0.3, 0, 2.5);
                }
                for (double a = -pt[2] - 3; a <= pt[2] + 3; a += 6) for (double b = -pt[3] - 3; b <= pt[3] + 3; b += 6) Claim(c + new V2(a, b), 4.5);
            }
            // a ball cage by the west gate
            ParkPlan cage = new ParkPlan { kind = "cage", c = new V2(CommonCage[0], CommonCage[1]), dir = new V2(1, 0), hl = 13, hw = 8 };
            Cage(cage, GroundCoverY);
            // railings along the Ring (south and east), gates where the paths come out
            RailingRun(new V2(1071, 57.2), new V2(1542.6, 57.2), new[] { 1190.0 - 1071, 1310.0 - 1071, 1440.0 - 1071 });
            RailingRun(new V2(1542.8, 58), new V2(1542.8, 466), new[] { 70.0, 250.0 });
            // an avenue along the main path, trees round the edge, loose clumps on the grass
            for (double x = 1084; x < 1536; x += 19)
                foreach (int s in new[] { -1, 1 }) CommonTree(new V2(x + (s > 0 ? 0 : 9.5), 128 + s * 5.2), poly);
            for (double x = 1078; x < 1538; x += 13) CommonTree(new V2(x + pr.NextDouble() * 3, 62.5 + pr.NextDouble() * 2), poly);
            for (double z = 66; z < 462; z += 13) CommonTree(new V2(1537.5 - pr.NextDouble() * 2, z + pr.NextDouble() * 3), poly);
            for (int i = 0; i < 46; i++)
            {
                V2 q = new V2(1075 + pr.NextDouble() * 465, 62 + pr.NextDouble() * 400);
                if (i % 3 != 0) q = q + new V2(pr.NextDouble() * 8 - 4, pr.NextDouble() * 8 - 4);
                CommonTree(q, poly);
            }
            // benches along the paths
            foreach (double[] s in CommonPaths)
            {
                V2 a = new V2(s[0], s[1]), b = new V2(s[2], s[3]); double L = V2.Dist(a, b); V2 f = (b - a).Norm;
                int k = 0;
                for (double t = 14; t < L - 10; t += 38, k++)
                {
                    V2 q = a + f * t + f.Left * ((k % 2 == 0 ? 1 : -1) * 2.9);
                    if (GAt(q) == GLawn && Free(q, 1.3)) { Bench(q, f.Left * (k % 2 == 0 ? -1 : 1), GroundCoverY); if (k % 2 == 0 && Free(q + f * 1.5, 0.4)) Bin(q + f * 1.5, GroundCoverY, false); }
                }
            }
            D.Count("Rye Common");
        }

        static void CommonTree(V2 q, List<V2> poly)
        {
            if (!InPoly(q, poly) || GAt(q) != GLawn || !Free(q, 2.4)) return;
            foreach (double[] s in CommonPaths) if (Line.SegDist(q, new V2(s[0], s[1]), new V2(s[2], s[3])) < 3.2) return;
            Tree(q, GroundCoverY, false, true);
        }

        /// A railing from a to b with 3.2 m gaps centred on the given distances along it.
        static void RailingRun(V2 a, V2 b, double[] gates)
        {
            double L = V2.Dist(a, b); V2 f = (b - a).Norm;
            List<double[]> gaps = new List<double[]>();
            foreach (double g in gates) gaps.Add(new[] { g - 1.6, g + 1.6 });
            foreach (double[] iv in Keep(L, gaps, 1.5)) Railing(a + f * iv[0], a + f * iv[1], 0.0, 1.4);
        }

        // ------------------------------------------------------------------ Quay Freight (the container depot)
        static readonly string[] BoxColours = { "postred", "navy", "orange", "cycleteal", "benchgreen", "grey", "yellow", "darkred", "nhsblue", "white", "charcoal" };

        static void PaintDepot(Random pr)
        {
            for (int k = 0; k < GNX * GNZ; k++) if (!gno[k] && InDepot(GCentre(k))) gcell[k] = GYard;
        }

        static void DressDepot(Random pr)
        {
            const double CL = 12.2, CW = 2.44, CH = 2.6;
            V2 east = new V2(1, 0), north = new V2(0, 1);
            // the stacks: rows running east-west, a lorry lane between each pair, cross lanes every four boxes
            int boxes = 0;
            for (int row = 0; row < 7; row++)
            {
                double z = DepotZ0 + 16 + row * 16.5;
                if (z > DepotZ1 - 12) break;
                double runStart = double.NaN, runH = 0; double x = DepotX0 + 14;
                int inBlock = 0;
                while (x + CL < DepotX1 - 60)
                {
                    bool lane = inBlock == 4;
                    int high = lane ? 0 : (pr.NextDouble() < 0.2 ? 0 : 1 + pr.Next(3));
                    V2 c = new V2(x + CL / 2, z);
                    if (high > 0 && (GAt(c) != GYard || !Free(c, 5))) high = 0;
                    if (high > 0)
                    {
                        for (int lv = 0; lv < high; lv++)
                        {
                            double off = lv == 0 ? 0 : (pr.NextDouble() - 0.5) * 0.3;
                            SBox(mSolid, c + east * off, east, CL / 2, CW / 2, lv * CH, (lv + 1) * CH - 0.04, S(BoxColours[pr.Next(BoxColours.Length)]));
                            boxes++;
                        }
                        // a second box beside it, most of the time
                        if (pr.NextDouble() < 0.7)
                        {
                            int h2 = 1 + pr.Next(high);
                            for (int lv = 0; lv < h2; lv++) { SBox(mSolid, c + north * (CW + 0.15), east, CL / 2, CW / 2, lv * CH, (lv + 1) * CH - 0.04, S(BoxColours[pr.Next(BoxColours.Length)])); boxes++; }
                        }
                        if (double.IsNaN(runStart)) { runStart = x; runH = 0; }
                        runH = Math.Max(runH, high * CH);
                    }
                    if ((high == 0) && !double.IsNaN(runStart))
                    {
                        Collider("Containers", new V2((runStart + x - 0.5) / 2, z + (CW + 0.15) / 2), east, x - 0.5 - runStart, CW * 2 + 0.3, 0, runH);
                        runStart = double.NaN;
                    }
                    if (lane) { inBlock = 0; x += 9; } else { inBlock++; x += CL + 0.5; }
                }
                if (!double.IsNaN(runStart)) Collider("Containers", new V2((runStart + x - 0.5) / 2, z + (CW + 0.15) / 2), east, x - 0.5 - runStart, CW * 2 + 0.3, 0, runH);
            }
            D.Count("containers", boxes);

            // two gantry cranes straddling the stacks
            foreach (double gx in new[] { DepotX0 + 150, DepotX0 + 380 })
            {
                double z0 = DepotZ0 + 8, z1 = DepotZ0 + 8 + 16.5 * 3 + 8, top = 21;
                UVRect y = S("yellow"), dk = S("charcoal");
                foreach (int e in new[] { -1, 1 })
                {
                    double x = gx + e * 9;
                    foreach (double z in new[] { z0, z1 })
                    {
                        SBox(mSolid, new V2(x, z), north, 0.45, 0.45, 0.6, top, y);
                        SBox(mSolid, new V2(x, z), east, 1.4, 0.5, 0, 0.6, dk);                       // bogie
                        Collider("Gantry", new V2(x, z), north, 1.2, 1.2, 0, top);
                    }
                    SBox(mSolid, new V2(x, (z0 + z1) / 2), north, (z1 - z0) / 2 + 2, 0.55, top, top + 1.5, y);   // the beams across the rows
                }
                foreach (double z in new[] { z0, z1 }) SBox(mSolid, new V2(gx, z), east, 9, 0.3, top - 3, top - 2.4, y);
                SBox(mSolid, new V2(gx, z0 + (z1 - z0) * 0.4), east, 9.6, 2.2, top + 1.5, top + 3.6, dk);        // the trolley and cab
                SBox(mSolid, new V2(gx + 3, z0 + (z1 - z0) * 0.4), east, 1.3, 1.1, top - 1.2, top + 1.5, S("white"));
            }

            // sheds at the east end
            foreach (double[] sh in new[] { new double[] { DepotX1 - 30, DepotZ0 + 30, 22, 13, 9 }, new double[] { DepotX1 - 30, DepotZ1 - 34, 20, 12, 8 } })
            {
                V2 c = new V2(sh[0], sh[1]);
                if (GAt(c) != GYard) continue;
                SBox(mSolid, c, north, sh[2], sh[3], 0, sh[4], S("modernwall"));
                SBox(mSolid, c, north, sh[2] + 0.4, sh[3] + 0.4, sh[4], sh[4] + 0.5, S("charcoal"));
                for (double a = -sh[2] + 5; a < sh[2] - 4; a += 9) SBox(mSolid, c + north * a - east * (sh[3] + 0.05), north, 3, 0.06, 0, 5, S("navy"));   // roller doors
                Collider("DepotShed", c, north, sh[2] * 2, sh[3] * 2, 0, sh[4] + 0.5);
                for (double a = -sh[2]; a <= sh[2]; a += 5) for (double b = -sh[3]; b <= sh[3]; b += 5) Claim(c + north * a + east * b, 4);
            }

            // floodlight masts
            for (double x = DepotX0 + 40; x < DepotX1 - 20; x += 125)
                foreach (double z in new[] { DepotZ0 + 5, DepotZ1 - 6 })
                {
                    V2 q = new V2(x, z);
                    if (GAt(q) != GYard) continue;
                    Cyl(mSolid, q, 0.22, 0, 24, 6, S("metal"), false);
                    SBox(mGlow, q, east, 1.6, 0.35, 24, 24.7, S("white"));
                    Collider("Floodlight", q, north, 0.6, 0.6, 0, 6);
                }

            // chain-link fence along the Ring (north and east)
            ChainFence(new V2(DepotX0, DepotZ1 + 1.6), new V2(DepotX1 + 1.6, DepotZ1 + 1.6));
            ChainFence(new V2(DepotX1 + 1.6, DepotZ1 + 1.6), new V2(DepotX1 + 1.6, DepotZ0));
            D.Count("Quay Freight depot");
        }

        static void ChainFence(V2 a, V2 b)
        {
            double L = V2.Dist(a, b); V2 f = (b - a).Norm, nrm = f.Left;
            int panels = Math.Max(1, (int)Math.Round(L / 3.0));
            UVRect link = A["chainlink"], post = S("grey");
            for (int i = 0; i < panels; i++)
            {
                V2 p = a + f * (L * i / panels), q = a + f * (L * (i + 1) / panels);
                VQuad(mCut, p, q, 0, 2.6, link, nrm); VQuad(mCut, q, p, 0, 2.6, link, nrm * -1);
                SBox(mSolid, p, f, 0.05, 0.05, 0, 2.75, post);
            }
            SBox(mSolid, b, f, 0.05, 0.05, 0, 2.75, post);
            Collider("DepotFence", (a + b) * 0.5, f, L, 0.2, 0, 2.6);
            D.Count("chain-link fence metres", (int)L);
        }

        // ------------------------------------------------------------------ Loughborough Rec (Brixtun)
        static readonly V2 RecTrack = new V2(840, -566);
        const double TrackA = 60, TrackB = 37, TrackW = 7;

        static void PaintRec(Random pr)
        {
            for (int k = 0; k < GNX * GNZ; k++)
            {
                if (gno[k]) continue;
                V2 c = GCentre(k);
                if (c.x < RecX0 || c.x > RecX1 || c.z < RecZ0 || c.z > RecZ1) continue;
                gcell[k] = GLawn;
                // the running track: a ring between two ellipses
                double dx = c.x - RecTrack.x, dz = c.z - RecTrack.z;
                double o = dx * dx / (TrackA * TrackA) + dz * dz / (TrackB * TrackB);
                double ia = TrackA - TrackW, ib = TrackB - TrackW, inn = dx * dx / (ia * ia) + dz * dz / (ib * ib);
                if (o <= 1 && inn >= 1) gcell[k] = GTrack;
            }
            bool[] path = new bool[GNX * GNZ];
            GMark(path, new V2(RecX0 + 1, -478), new V2(RecX1 - 1, -478), 1.7);
            GMark(path, new V2(840, RecZ1 - 1), new V2(840, -478), 1.7);
            for (int k = 0; k < GNX * GNZ; k++) if (path[k] && gcell[k] == GLawn) gcell[k] = GPath;
        }

        static void DressRec(Random pr)
        {
            V2 east = new V2(1, 0), north = new V2(0, 1);
            // a pitch inside the track, two more to the north
            foreach (double[] pt in new[] { new double[] { RecTrack.x, RecTrack.z, 36, 21 }, new double[] { 775, -444, 26, 15 }, new double[] { 905, -444, 26, 15 } })
            {
                V2 c = new V2(pt[0], pt[1]);
                if (GAt(c) == GNone) continue;
                HQuad(mSolid, c, north, pt[3], pt[2], GroundCoverY + 0.015, A["pitch"]);
                foreach (int s in new[] { -1, 1 })
                {
                    V2 g = c + east * (s * (pt[2] - 0.6));
                    foreach (int e in new[] { -1, 1 }) SBox(mSolid, g + north * (e * 3.66), north, 0.06, 0.06, 0, 2.44, S("white"));
                    SBox(mSolid, g, north, 3.72, 0.06, 2.38, 2.5, S("white"));
                    Collider("Goal", g, north, 7.4, 0.3, 0, 2.5);
                }
                for (double a = -pt[2] - 3; a <= pt[2] + 3; a += 6) for (double b = -pt[3] - 3; b <= pt[3] + 3; b += 6) Claim(c + new V2(a, b), 4.5);
            }
            for (double a = 0; a < Math.PI * 2; a += 0.12) Claim(RecTrack + new V2(Math.Cos(a) * (TrackA - TrackW / 2), Math.Sin(a) * (TrackB - TrackW / 2)), 4.5);
            // the pavilion, south of the track
            V2 pav = new V2(RecTrack.x, RecZ0 + 16);
            if (GAt(pav) != GNone)
            {
                SBox(mSolid, pav, east, 11, 5, 0, 3.6, S("brick"));
                SBox(mSolid, pav, east, 11.6, 5.8, 3.6, 3.95, S("charcoal"));
                SBox(mSolid, pav + north * 5.05, east, 4, 0.06, 0.2, 2.4, S("benchgreen"));       // shuttered front
                Collider("Pavilion", pav, east, 22, 10, 0, 3.95);
                for (double a = -12; a <= 12; a += 4) for (double b = -6; b <= 6; b += 4) Claim(pav + east * a + north * b, 3.5);
            }
            // railings on the three street sides, gates at the paths
            RailingRun(new V2(RecX0 - 0.6, RecZ0), new V2(RecX0 - 0.6, RecZ1), new[] { -478.0 - RecZ0 });
            RailingRun(new V2(RecX1 + 0.6, RecZ0), new V2(RecX1 + 0.6, RecZ1), new[] { -478.0 - RecZ0 });
            RailingRun(new V2(RecX0, RecZ0 - 0.6), new V2(RecX1, RecZ0 - 0.6), new[] { 60.0, 190.0 });
            // trees round the edge and in the corners
            for (double x = RecX0 + 5; x < RecX1 - 4; x += 14) foreach (double z in new[] { RecZ0 + 4.5, RecZ1 - 5 }) RecTree(new V2(x + pr.NextDouble() * 3, z));
            for (double z = RecZ0 + 14; z < RecZ1 - 8; z += 14) foreach (double x in new[] { RecX0 + 4.5, RecX1 - 4.5 }) RecTree(new V2(x, z + pr.NextDouble() * 3));
            for (int i = 0; i < 26; i++) RecTree(new V2(RecX0 + 8 + pr.NextDouble() * (RecX1 - RecX0 - 16), RecZ0 + 8 + pr.NextDouble() * (RecZ1 - RecZ0 - 16)));
            // benches by the track and along the path
            for (double x = RecX0 + 20; x < RecX1 - 10; x += 36)
            {
                V2 q = new V2(x, -478 + 2.9);
                if (GAt(q) == GLawn && Free(q, 1.3)) Bench(q, north * -1, GroundCoverY);
            }
            D.Count("Loughborough Rec");
        }

        static void RecTree(V2 q)
        {
            if (GAt(q) != GLawn || !Free(q, 2.4)) return;
            Tree(q, GroundCoverY, false, true);
        }
    }
}
