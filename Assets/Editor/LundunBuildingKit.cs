// L1: the building kit. Pure C# (no UnityEngine), so it can be tested outside Unity.
// Every building keeps the exact plot the city generator gave it (same position, same footprint, same order), so
// missions, police stations, spawn points and the minimap are untouched. What changes is what stands on the plot:
//   Peckam        Victorian terraces (bays, chimneys, painted and brick houses), shop parades, corner pubs, 60s infill
//   Knightsbury   white stucco terraces with porticos and mansards, red-brick mansion blocks with gables,
//                 Georgian brick terraces, shops on the high streets, glass towers round the Circus
//   Gilmore       deck-access blocks, maisonettes, a shop parade, lock-up garages, the tower
//   Canary Quay   brick warehouses (loading doors, hoists, glass penthouses) and mid-rise offices
//   Peninsula     glass and stone towers with setbacks, plant and masts
// Everything is textured from the two KitArt atlases (Bld_Kit_A / Bld_Kit_B), one mesh per atlas per 200 m chunk,
// so the whole city still costs about two draw calls per chunk.
// Choices come from each plot's position (not the city's random sequence), so nothing else in the city shifts.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        /// Set false to get the old plain boxes back (kept as a fallback).
        public static bool UseBuildingKit = true;
        public const string KitMatA = "Bld_Kit_A", KitMatB = "Bld_Kit_B";

        public enum KitType { Terrace, Parade, Pub, Infill, Stucco, Mansion, Georgian, Tower, Office, Warehouse, EstTower, Deck, Maisonette, EstParade, Garages,
            DeptStore, MusicHall, Civic }        // E1: Brixtun's landmark buildings

        public class KitPlan
        {
            public KitType type; public int face; public string road; public double gap; public bool nearJunction, mixed;
            public V2 c, n, r; public double W, D;
            public double eave;          // L3: top of the front wall (scaffolding goes up to here)
        }

        /// L3: the kit's plan for a plot (type, front, size), or null.
        public static KitPlan KitPlanFor(BuildingInfo b) { KitPlan p; return kitPlans.TryGetValue(b, out p) ? p : null; }

        static Dictionary<BuildingInfo, KitPlan> kitPlans = new Dictionary<BuildingInfo, KitPlan>();
        /// How many of each type were built last time (for the log).
        public static Dictionary<string, int> KitCounts = new Dictionary<string, int>();

        /// Dressing asks this before putting shop fronts or a pub on a building: houses get none.
        public static bool KitAllowsShops(BuildingInfo b)
        {
            if (!UseBuildingKit) return true;
            KitPlan p; if (!kitPlans.TryGetValue(b, out p)) return true;
            switch (p.type)
            {
                case KitType.Parade: case KitType.Office: case KitType.Warehouse: case KitType.Tower: case KitType.EstParade: return true;
                case KitType.Stucco: case KitType.Mansion: return p.mixed;
                default: return false;
            }
        }

        /// For logs and tests: "type face nx nz W D gap".
        public static string KitInfo(BuildingInfo b)
        {
            KitPlan p; if (!kitPlans.TryGetValue(b, out p)) return "none";
            System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
            return p.type + " " + p.face + " " + p.n.x.ToString("0.###", ci) + " " + p.n.z.ToString("0.###", ci) + " " + p.W.ToString("0.##", ci) + " " + p.D.ToString("0.##", ci) + " " + p.gap.ToString("0.##", ci) + " " + p.road.Replace(' ', '_');
        }

        // ------------------------------------------------------------------ choosing what goes on each plot
        static readonly HashSet<string> PeckamParadeRoads = new HashSet<string> { "Rye Lane", "Peckam High Street", "Old Kent Lane" };
        static readonly HashSet<string> KnightShopRoads = new HashSet<string> { "Brompton Road" };

        static int KitSeed(BuildingInfo b) { return (int)(KHash((int)Math.Round(b.c.x * 10), (int)Math.Round(b.c.z * 10)) & 0x7FFFFFFF); }
        static uint KHash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263 + 977); h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16); }

        static KitPlan PlanBuilding(BuildingInfo b, Random kr, ref int peckamPubs)
        {
            KitPlan p = new KitPlan();
            V2 f = b.dir.Norm, l = f.Left;
            V2[] normals = { l, l * -1, f, f * -1 };
            double[] halfDepth = { b.dep / 2, b.dep / 2, b.len / 2, b.len / 2 };
            double[] faceLen = { b.len, b.len, b.dep, b.dep };

            // 1. the same rule the shop fronts use (so shops and the building agree on the front)
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
            // 2. otherwise whichever face is nearest any road
            if (bestFace < 0)
            {
                bestGap = double.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    V2 fc = b.c + normals[i] * halfDepth[i];
                    foreach (RoadInfo ri in roads.Values)
                    {
                        if (ri.cls == "bridge") continue;
                        double gap = ri.line.DistanceTo(fc) - ri.w / 2 - Math.Max(ri.pave, ri.verge);
                        if (gap < bestGap && V2.Dot(normals[i], ClosestOn(ri, fc) - fc) > 0) { bestGap = gap; bestFace = i; road = ri; }
                    }
                }
                if (bestFace < 0) { bestFace = 0; bestGap = 99; }
            }
            p.face = bestFace; p.gap = bestGap; p.road = road != null ? road.r.name : "";
            p.n = normals[bestFace]; p.r = p.n.Left; p.c = b.c;
            p.W = faceLen[bestFace]; p.D = bestFace < 2 ? b.dep : b.len;
            V2 face = b.c + p.n * halfDepth[bestFace];
            foreach (JInfo ji in juncs.Values) if (V2.Dist(ji.c, face) < ji.radius + p.W / 2 + 6) p.nearJunction = true;
            bool onRoad = p.gap < 8;
            string cls = road != null ? road.cls : "";
            double x = kr.NextDouble();

            switch (b.district)
            {
                case "D_PECKAM":
                    if (onRoad && (PeckamParadeRoads.Contains(p.road) || cls == "arterial" || (ExpansionMode && BrixtunParadeRoads.Contains(p.road))))
                    {
                        if (p.nearJunction && peckamPubs < 6 && x < 0.3) { p.type = KitType.Pub; peckamPubs++; }
                        else p.type = KitType.Parade;
                    }
                    else if (onRoad && p.nearJunction && peckamPubs < 6 && x < 0.12) { p.type = KitType.Pub; peckamPubs++; }
                    else p.type = (!onRoad && x < 0.25) || x > 0.92 ? KitType.Infill : KitType.Terrace;
                    if (p.W < 10 && p.type == KitType.Terrace) p.type = KitType.Infill;
                    break;
                case "D_KNIGHT":
                    if (b.h >= 45) p.type = KitType.Tower;
                    else
                    {
                        p.mixed = onRoad && (cls == "arterial" || KnightShopRoads.Contains(p.road));
                        if (p.mixed) p.type = x < 0.55 ? KitType.Stucco : KitType.Mansion;
                        else p.type = x < 0.45 ? KitType.Stucco : x < 0.7 ? KitType.Mansion : KitType.Georgian;
                    }
                    break;
                case "D_CANARY":
                    p.type = (onRoad && cls == "arterial" && x < 0.5) || x > 0.82 ? KitType.Office : KitType.Warehouse;
                    break;
                case "D_CANARY_PEN": p.type = KitType.Tower; break;
                case "D_GILMOR":
                    if (b.h >= 40) p.type = KitType.EstTower;
                    else if (onRoad && cls == "street" && x < 0.18) p.type = KitType.EstParade;
                    else if (p.W >= 40 && x > 0.8) p.type = KitType.Garages;
                    else p.type = x < 0.5 ? KitType.Deck : KitType.Maisonette;
                    break;
                default: p.type = KitType.Infill; break;
            }
            return p;
        }

        // ------------------------------------------------------------------ build
        static void BuildKit(GreyboxResult R)
        {
            kitPlans.Clear(); KitCounts.Clear();
            Dictionary<string, MeshData> chunks = new Dictionary<string, MeshData>();
            int pubs = 0;
            foreach (BuildingInfo b in R.buildings)
            {
                Random kr = new Random(KitSeed(b));
                KitPlan p = SpecialPlan(b) ?? PlanBuilding(b, kr, ref pubs);     // E1: an expansion's landmark buildings are planned by hand
                kitPlans[b] = p;
                string ck = "_" + (int)Math.Floor((b.c.x - ChunkOrigin) / ChunkSize) + "_" + (int)Math.Floor((b.c.z - ChunkOrigin) / ChunkSize);
                Kit k = new Kit { A = ChunkMesh(chunks, KitMatA + ck, KitMatA), B = ChunkMesh(chunks, KitMatB + ck, KitMatB), p = p, rnd = kr, h = b.h };
                switch (p.type)
                {
                    case KitType.Terrace: k.Terrace(); break;
                    case KitType.Parade: k.Parade(false); break;
                    case KitType.Pub: k.Parade(true); break;
                    case KitType.Infill: k.Infill(); break;
                    case KitType.Stucco: k.Stucco(); break;
                    case KitType.Mansion: k.Mansion(); break;
                    case KitType.Georgian: k.Georgian(); break;
                    case KitType.Tower: k.Tower(b.district == "D_CANARY_PEN"); break;
                    case KitType.Office: k.Office(); break;
                    case KitType.Warehouse: k.Warehouse(); break;
                    case KitType.EstTower: k.EstTower(); break;
                    case KitType.Deck: k.Deck(); break;
                    case KitType.Maisonette: k.Maisonette(false, double.NaN); break;
                    case KitType.EstParade: k.Maisonette(true, double.NaN); break;
                    case KitType.Garages: k.Garages(); break;
                    case KitType.DeptStore: k.DeptStore(); break;
                    case KitType.MusicHall: k.MusicHall(); break;
                    case KitType.Civic: k.Civic(); break;
                }
                string tn = p.type.ToString();
                int c; KitCounts.TryGetValue(tn, out c); KitCounts[tn] = c + 1;
            }
            List<string> keys = new List<string>(chunks.Keys); keys.Sort();
            int tris = 0;
            foreach (string key in keys) { if (chunks[key].tri.Count == 0) continue; R.meshes.Add(chunks[key]); tris += chunks[key].tri.Count / 3; }
            List<string> tl = new List<string>(); foreach (var kv in KitCounts) tl.Add(kv.Key + " " + kv.Value); tl.Sort();
            R.log.Add("  building kit: " + string.Join(", ", tl.ToArray()) + " (" + tris + " triangles)");
        }

        static MeshData ChunkMesh(Dictionary<string, MeshData> chunks, string key, string mat)
        {
            MeshData m;
            if (!chunks.TryGetValue(key, out m)) { m = new MeshData("Buildings_" + key, "Buildings", mat, true) { navIgnore = false, chunk = false }; chunks[key] = m; }
            return m;
        }

        // ================================================================== the builder for one plot
        partial class Kit
        {
            public MeshData A, B; public KitPlan p; public Random rnd; public double h;
            const double Tex = KitArt.RepeatM;

            double W { get { return p.W; } }
            double D { get { return p.D; } }
            MeshData M(int atlas) { return atlas == 0 ? A : B; }

            /// Local → world: s along the front (to the right seen from the street), d inwards from the front face.
            V2 P(double s, double d) { return p.c + p.r * s + p.n * (D / 2 - d); }
            V2 Front { get { return p.n; } }
            V2 Back { get { return p.n * -1; } }
            V2 Right { get { return p.r; } }
            V2 Left { get { return p.r * -1; } }
            int Pick(int n) { return rnd.Next(n); }
            bool Chance(double c) { return rnd.NextDouble() < c; }

            // ---------------------------------------------------------- faces
            /// A wall quad from a to b with outward normal o. U runs left → right as seen from outside,
            /// from uStart (texture metres) for uLen texture metres (negative = mirrored). V covers t0..t1 of the strip.
            void Face(int atlas, int row, V2 a, V2 b, V2 o, double y0, double y1, double uStart, double uLen, double t0 = 0, double t1 = 1)
            {
                if (V2.Dot(b - a, o.Left) < 0) { V2 t = a; a = b; b = t; }
                double u0 = uStart / Tex, u1 = (uStart + uLen) / Tex;
                double v0 = KitArt.V(row, t0), v1 = KitArt.V(row, t1);
                M(atlas).Quad(new P3(a, y0, u0, v0), new P3(b, y0, u1, v0), new P3(b, y1, u1, v1), new P3(a, y1, u0, v1), o.x, 0, o.z);
            }

            /// A storey of a unit-based facade across a whole wall: whole units, stretched to fit, starting at unit k.
            void Units(int atlas, int row, V2 a, V2 b, V2 o, double y0, double y1, int k, bool mirror = false)
            {
                double L = V2.Dist(a, b), uw = KitArt.UnitW(atlas, row);
                int n = Math.Max(1, (int)Math.Round(L / uw));
                double start = k * uw, len = n * uw;
                if (mirror) { start += len; len = -len; }
                Face(atlas, row, a, b, o, y0, y1, start, len);
            }

            /// A plain wall: the strip repeated every storey so bricks keep their size; U in real metres.
            void Plain(int atlas, int row, V2 a, V2 b, V2 o, double y0, double y1, double uStart = 0)
            {
                double fh = KitArt.FloorH(atlas, row), L = V2.Dist(a, b);
                for (double y = y0; y < y1 - 0.01; y += fh)
                {
                    double top = Math.Min(y1, y + fh);
                    Face(atlas, row, a, b, o, y, top, uStart, L, 0, (top - y) / fh);
                }
            }

            /// A triangle of plain wall (gable ends), V by height.
            void PlainTri(int atlas, int row, V2 a, V2 b, V2 apex, double yBase, double yApex, V2 o)
            {
                double fh = KitArt.FloorH(atlas, row);
                if (V2.Dot(b - a, o.Left) < 0) { V2 t = a; a = b; b = t; }
                double L = V2.Dist(a, b), ua = V2.Dot(apex - a, (b - a).Norm);
                M(atlas).Tri(new P3(a, yBase, 0, KitArt.V(row, 0)), new P3(b, yBase, L / Tex, KitArt.V(row, 0)),
                    new P3(apex, yApex, ua / Tex, KitArt.V(row, Math.Min(1, (yApex - yBase) / fh))), o.x, 0, o.z);
            }

            void Swatch(int atlas, int sw, V2 a, V2 b, V2 c, V2 d, double ya, double yb, double yc, double yd, double nx, double ny, double nz)
            {
                double u, v; KitArt.SwatchUV(sw, out u, out v);
                M(atlas).Quad(new P3(a, ya, u, v), new P3(b, yb, u, v), new P3(c, yc, u, v), new P3(d, yd, u, v), nx, ny, nz);
            }

            /// A box in local coordinates. Sides: a plain strip (row ≥ 0) or a swatch (row < 0, sw); top: swatch.
            void LBox(int atlas, double s0, double s1, double d0, double d1, double y0, double y1, int row, int sw, int topSw, bool top = true)
            {
                V2 a = P(s0, d0), b = P(s1, d0), c = P(s1, d1), d = P(s0, d1);
                V2[] q = { a, b, c, d }; V2[] o = { Front, Right, Back, Left };
                for (int i = 0; i < 4; i++)
                {
                    V2 x0 = q[i], x1 = q[(i + 1) % 4];
                    if (row >= 0) Plain(atlas, row, x0, x1, o[i], y0, y1);
                    else Swatch(atlas, sw, x0, x1, x1, x0, y0, y0, y1, y1, o[i].x, 0, o[i].z);
                }
                if (top) Swatch(atlas, topSw, a, b, c, d, y1, y1, y1, y1, 0, 1, 0);
            }

            void FlatRoof(int atlas, double s0, double s1, double d0, double d1, double y)
            {
                int row = atlas == 0 ? KitArt.A_RoofFlat : KitArt.B_RoofFlat;
                V2 a = P(s0, d0), b = P(s1, d0), c = P(s1, d1), d = P(s0, d1);
                double u0 = s0 / Tex, u1 = s1 / Tex;
                M(atlas).Quad(new P3(a, y, u0, KitArt.V(row, 0)), new P3(b, y, u1, KitArt.V(row, 0)), new P3(c, y, u1, KitArt.V(row, 1)), new P3(d, y, u0, KitArt.V(row, 1)), 0, 1, 0);
            }

            /// A parapet ring round a flat roof: outer face (strip or plain), inner face, coping on top.
            void Parapet(int atlas, double s0, double s1, double d0, double d1, double y, double hgt, int frontRow, int sideRow, int coping)
            {
                double t = 0.25;
                // outer faces
                if (frontRow == KitArt.A_Cornice && atlas == 0) Face(0, frontRow, P(s0, d0), P(s1, d0), Front, y, y + hgt, 0, s1 - s0);
                else Plain(atlas, frontRow, P(s0, d0), P(s1, d0), Front, y, y + hgt);
                Plain(atlas, sideRow, P(s1, d0), P(s1, d1), Right, y, y + hgt);
                Plain(atlas, sideRow, P(s1, d1), P(s0, d1), Back, y, y + hgt);
                Plain(atlas, sideRow, P(s0, d1), P(s0, d0), Left, y, y + hgt);
                // inner faces (swatch, seen from above / far away only)
                Swatch(atlas, coping, P(s0 + t, d0 + t), P(s1 - t, d0 + t), P(s1 - t, d0 + t), P(s0 + t, d0 + t), y, y, y + hgt, y + hgt, -p.n.x, 0, -p.n.z);
                Swatch(atlas, coping, P(s0 + t, d1 - t), P(s1 - t, d1 - t), P(s1 - t, d1 - t), P(s0 + t, d1 - t), y, y, y + hgt, y + hgt, p.n.x, 0, p.n.z);
                Swatch(atlas, coping, P(s0 + t, d0 + t), P(s0 + t, d1 - t), P(s0 + t, d1 - t), P(s0 + t, d0 + t), y, y, y + hgt, y + hgt, p.r.x, 0, p.r.z);
                Swatch(atlas, coping, P(s1 - t, d0 + t), P(s1 - t, d1 - t), P(s1 - t, d1 - t), P(s1 - t, d0 + t), y, y, y + hgt, y + hgt, -p.r.x, 0, -p.r.z);
                // coping
                double yt = y + hgt;
                Swatch(atlas, coping, P(s0, d0), P(s1, d0), P(s1, d0 + t), P(s0, d0 + t), yt, yt, yt, yt, 0, 1, 0);
                Swatch(atlas, coping, P(s0, d1 - t), P(s1, d1 - t), P(s1, d1), P(s0, d1), yt, yt, yt, yt, 0, 1, 0);
                Swatch(atlas, coping, P(s0, d0 + t), P(s0 + t, d0 + t), P(s0 + t, d1 - t), P(s0, d1 - t), yt, yt, yt, yt, 0, 1, 0);
                Swatch(atlas, coping, P(s1 - t, d0 + t), P(s1, d0 + t), P(s1, d1 - t), P(s1 - t, d1 - t), yt, yt, yt, yt, 0, 1, 0);
            }

            /// A pitched roof with its ridge along the front, gables at both ends (plain wall triangles).
            void Pitched(int atlas, double s0, double s1, double d0, double d1, double eave, double rise, int gableRow, double over = 0.3, bool gables = true)
            {
                int slate = KitArt.A_Slate;
                double dm = (d0 + d1) / 2, ridge = eave + rise;
                double slope = Math.Sqrt((dm - d0) * (dm - d0) + rise * rise);
                double tl = Math.Min(1, slope / KitArt.FloorH(0, slate));
                // front slope (from the eave, overhanging a little, to the ridge), back slope
                double dy = rise / (dm - d0) * over;
                Slope(P(s0, d0 - over), P(s1, d0 - over), P(s1, dm), P(s0, dm), eave - dy, ridge, Front, rise, dm - d0, s0, s1, tl);
                Slope(P(s1, d1 + over), P(s0, d1 + over), P(s0, dm), P(s1, dm), eave - dy, ridge, Back, rise, dm - d0, -s1, -s0, tl);
                if (gables)
                {
                    PlainTri(atlas, gableRow, P(s0, d0), P(s0, d1), P(s0, dm), eave, ridge, Left);
                    PlainTri(atlas, gableRow, P(s1, d1), P(s1, d0), P(s1, dm), eave, ridge, Right);
                }
                // ridge tiles
                Swatch(0, KitArt.S_Slate, P(s0, dm - 0.12), P(s1, dm - 0.12), P(s1, dm + 0.12), P(s0, dm + 0.12), ridge + 0.04, ridge + 0.04, ridge + 0.04, ridge + 0.04, 0, 1, 0);
            }

            void Slope(V2 a, V2 b, V2 c, V2 d, double yLow, double yHigh, V2 o, double rise, double run, double sA, double sB, double tl)
            {
                int slate = KitArt.A_Slate;
                double len = Math.Sqrt(rise * rise + run * run);
                double nx = o.x * rise / len, ny = run / len, nz = o.z * rise / len;
                double u0 = sA / Tex, u1 = sB / Tex, v0 = KitArt.V(slate, 0), v1 = KitArt.V(slate, tl);
                A.Quad(new P3(a, yLow, u0, v0), new P3(b, yLow, u1, v0), new P3(c, yHigh, u1, v1), new P3(d, yHigh, u0, v1), nx, ny, nz);
            }

            void Chimney(int atlas, int row, double s, double d, double yBase, double hgt, double wid = 0.9, double dep = 0.65)
            {
                LBox(atlas, s - wid / 2, s + wid / 2, d - dep / 2, d + dep / 2, yBase, yBase + hgt, row, 0, KitArt.S_Bitumen);
                int pots = 2 + Pick(3);
                for (int i = 0; i < pots; i++)
                {
                    double ps = s - wid / 2 + (i + 0.5) * wid / pots;
                    LBox(atlas, ps - 0.09, ps + 0.09, d - 0.09, d + 0.09, yBase + hgt, yBase + hgt + 0.35 + rnd.NextDouble() * 0.2, -1, KitArt.S_Terracotta, KitArt.S_Black);
                }
            }

            /// Front protrusions may only use the space between the front wall and the pavement.
            double Room { get { return Math.Max(0, Math.Min(p.gap - 0.4, 2.0)); } }

            // ---------------------------------------------------------- Peckam: Victorian terrace
            public void Terrace()
            {
                int houses = Math.Max(2, (int)Math.Round(W / 5.333));
                double hw = W / houses;
                int floors = h >= 10.5 ? 3 : 2;
                double gf = 3.3, up = 3.1, eave = gf + up * (floors - 1);
                int pal = rnd.NextDouble() < 0.4 ? 0 : rnd.NextDouble() < 0.8 ? 1 : 2;
                int bayType = Chance(0.15) ? 0 : (floors >= 2 && Chance(0.35)) ? 2 : 1;
                if (Room < 0.8) bayType = 0;
                int plainRow = pal == 0 ? KitArt.A_BrickRed : pal == 1 ? KitArt.A_BrickYel : KitArt.A_Render;
                double rise = Math.Min(3.4, D / 2 * Math.Tan(32 * Math.PI / 180));
                p.eave = eave;

                for (int i = 0; i < houses; i++)
                {
                    double s0 = -W / 2 + i * hw, s1 = s0 + hw;
                    bool mirror = i % 2 == 1;                      // houses come in handed pairs, doors side by side
                    int hp = pal != 2 && Chance(0.18) ? 2 : pal;    // the odd painted house in a brick row
                    int gfRow = hp == 0 ? KitArt.A_TGfRed : hp == 1 ? KitArt.A_TGfYel : KitArt.A_TGfPaint;
                    int upRow = hp == 0 ? KitArt.A_TUpRed : hp == 1 ? KitArt.A_TUpYel : KitArt.A_TUpPaint;
                    double uw = KitArt.UnitW(0, gfRow);
                    // front, storey by storey (brick storeys pick their own unit so lit windows differ;
                    // a painted house keeps one colour top to bottom: upper unit = colour, ground unit = colour − 2)
                    int colour = Pick(6);
                    FaceUnit(0, gfRow, s0, s1, 0, 0, gf, hp == 2 ? (colour + 4) % 6 : Pick(6), mirror);
                    for (int f = 1; f < floors; f++) FaceUnit(0, upRow, s0, s1, 0, gf + up * (f - 1), gf + up * f, hp == 2 ? colour : Pick(6), mirror);
                    // rear: windows on every floor
                    for (int f = 0; f < floors; f++)
                    {
                        double y0 = f == 0 ? 0 : gf + up * (f - 1), y1 = f == 0 ? gf : gf + up * f;
                        Face(0, upRow, P(s1, D), P(s0, D), Back, y0, y1, (hp == 2 ? colour : Pick(6)) * uw, uw);
                    }
                    // the bay and the front step (positions follow the texture: door at 0.45–1.4, bay window at 2.3–4.8 of 5.33)
                    double k = hw / uw;
                    double doorC = mirror ? s1 - 0.925 * k : s0 + 0.925 * k;
                    double bayC = mirror ? s1 - 3.55 * k : s0 + 3.55 * k;
                    if (Room > 0.25) LBox(0, doorC - 0.6, doorC + 0.6, -0.32, 0, 0, 0.16, -1, KitArt.S_Portland, KitArt.S_Portland);
                    int bt = bayType == 2 && Chance(0.15) ? 1 : bayType;
                    if (bt > 0) Bay(bayC, bt == 2 ? gf + up - 0.05 : gf - 0.05, gf);
                    // chimney stacks on the party walls (every other one) and at the ends
                    if (i % 2 == 1 || i == 0) Chimney(0, plainRow, i == 0 ? s0 + 0.5 : s0, D / 2, eave + rise - 0.6, 1.5);
                    if (i == houses - 1) Chimney(0, plainRow, s1 - 0.5, D / 2, eave + rise - 0.6, 1.5);
                }
                // the end walls, roof and gables
                Plain(0, plainRow, P(-W / 2, D), P(-W / 2, 0), Left, 0, eave);
                Plain(0, plainRow, P(W / 2, 0), P(W / 2, D), Right, 0, eave);
                Pitched(0, -W / 2, W / 2, 0, D, eave, rise, plainRow);
                // a rear extension (the classic back addition) on some houses — kept inside the plot
            }

            void FaceUnit(int atlas, int row, double s0, double s1, double d, double y0, double y1, int k, bool mirror)
            {
                double uw = KitArt.UnitW(atlas, row);
                double start = k * uw, len = uw;
                if (mirror) { start += uw; len = -uw; }
                Face(atlas, row, P(s0, d), P(s1, d), Front, y0, y1, start, len);
            }

            /// Canted bay: back width 2.5 m at the wall, front 1.7 m, 0.75 m deep. Sashes on all three faces.
            void Bay(double sc, double top, double gf)
            {
                double dep = Math.Min(0.75, Room - 0.1), wb = 1.25, wf = 0.85;
                if (dep < 0.4) return;
                V2 bl = P(sc - wb, 0), br = P(sc + wb, 0), fl = P(sc - wf, -dep), fr = P(sc + wf, -dep);
                V2 nl = ((fl - bl).Left * -1).Norm; if (V2.Dot(nl, Front) < 0) nl = nl * -1;
                V2 nr = ((br - fr).Left * -1).Norm; if (V2.Dot(nr, Front) < 0) nr = nr * -1;
                int row = KitArt.A_Bay;
                double y = 0;
                int storey = 0;
                while (y < top - 0.05)
                {
                    double y1 = Math.Min(top, storey == 0 ? gf : y + 3.1);
                    double t1 = (y1 - y) / 3.2;
                    int k = Pick(32);
                    Face(0, row, fl, fr, Front, y, y1, k, 2, 0, t1);
                    Face(0, row, bl, fl, nl, y, y1, k + 2, 1, 0, t1);
                    Face(0, row, fr, br, nr, y, y1, k + 3, 1, 0, t1);
                    y = y1; storey++;
                }
                // lead-covered top
                Swatch(0, KitArt.S_Lead, bl, br, fr, fl, top, top, top, top, 0, 1, 0);
            }

            // ---------------------------------------------------------- shop parade / corner pub
            public void Parade(bool pub)
            {
                int ups = h >= 10.5 ? 3 : 2;
                double gf = 4.0, up = 3.2, eave = gf + up * ups;
                int pal = Pick(3);
                int upRow = pal == 0 ? KitArt.A_PUpYel : pal == 1 ? KitArt.A_PUpRed : KitArt.A_PUpPaint;
                int plainRow = pal == 0 ? KitArt.A_BrickYel : pal == 1 ? KitArt.A_BrickRed : KitArt.A_Render;
                int gfRow = pub ? KitArt.A_PubGf : KitArt.A_ShopGf;
                p.eave = eave;
                Units(0, gfRow, P(-W / 2, 0), P(W / 2, 0), Front, 0, gf, Pick(5));
                int k0 = Pick(5);                                   // painted parades keep their colours lined up storey to storey
                for (int f = 0; f < ups; f++) Units(0, upRow, P(-W / 2, 0), P(W / 2, 0), Front, gf + up * f, gf + up * (f + 1), pal == 2 ? k0 : Pick(5));
                // a corner pub wraps round the side street
                bool cornerL = pub && SideFacesRoad(-1), cornerR = pub && SideFacesRoad(1);
                SideWall(0, cornerL ? gfRow : plainRow, -1, 0, gf, cornerL);
                SideWall(0, cornerR ? gfRow : plainRow, 1, 0, gf, cornerR);
                for (int f = 0; f < ups; f++)
                {
                    SideWall(0, cornerL ? upRow : plainRow, -1, gf + up * f, gf + up * (f + 1), cornerL);
                    SideWall(0, cornerR ? upRow : plainRow, 1, gf + up * f, gf + up * (f + 1), cornerR);
                }
                Plain(0, plainRow, P(W / 2, D), P(-W / 2, D), Back, 0, gf);
                for (int f = 0; f < ups; f++) Units(0, upRow, P(W / 2, D), P(-W / 2, D), Back, gf + up * f, gf + up * (f + 1), pal == 2 ? k0 : Pick(5));
                // cornice + parapet in front; behind it either a flat roof or a slate roof
                LBox(0, -W / 2, W / 2, -0.12, 0, gf - 0.05, gf + 0.12, -1, KitArt.S_Cream, KitArt.S_Lead);
                if (Chance(0.45))
                {
                    Parapet(0, -W / 2, W / 2, 0, D, eave, 1.0, KitArt.A_Cornice, plainRow, KitArt.S_Portland);
                    FlatRoof(0, -W / 2, W / 2, 0, D, eave);
                    RoofClutter(0, -W / 2 + 1, W / 2 - 1, 1, D - 1, eave);
                }
                else
                {
                    Face(0, KitArt.A_Cornice, P(-W / 2, 0), P(W / 2, 0), Front, eave, eave + 1.0, 0, W);
                    Swatch(0, KitArt.S_Portland, P(-W / 2, 0), P(W / 2, 0), P(W / 2, 0.3), P(-W / 2, 0.3), eave + 1, eave + 1, eave + 1, eave + 1, 0, 1, 0);
                    Swatch(0, KitArt.S_DarkGrey, P(-W / 2, 0.3), P(W / 2, 0.3), P(W / 2, 0.3), P(-W / 2, 0.3), eave, eave, eave + 1, eave + 1, -p.n.x, 0, -p.n.z);
                    double rise = Math.Min(3.0, D / 2 * Math.Tan(30 * Math.PI / 180));
                    Pitched(0, -W / 2, W / 2, 0.3, D, eave, rise, plainRow, 0.1);
                    int stacks = Math.Max(1, (int)(W / 9));
                    for (int i = 1; i <= stacks; i++) Chimney(0, plainRow, -W / 2 + i * W / (stacks + 1), (0.3 + D) / 2, eave + rise - 0.6, 1.4);
                }
            }

            bool SideFacesRoad(int side)
            {
                V2 fc = P(side * W / 2, D / 2), o = p.r * side;
                foreach (RoadInfo ri in roads.Values)
                {
                    if (ri.cls == "bridge" || ri.pave <= 0) continue;
                    double gap = ri.line.DistanceTo(fc) - ri.w / 2 - ri.pave;
                    if (gap < 8 && V2.Dot(o, ClosestOn(ri, fc) - fc) > 0) return true;
                }
                return false;
            }

            void SideWall(int atlas, int row, int side, double y0, double y1, bool facade)
            {
                V2 a = side < 0 ? P(-W / 2, D) : P(W / 2, 0), b = side < 0 ? P(-W / 2, 0) : P(W / 2, D);
                V2 o = side < 0 ? Left : Right;
                double uw = KitArt.UnitW(atlas, row);
                if (facade && uw < 31) Units(atlas, row, a, b, o, y0, y1, Pick(8));
                else Plain(atlas, row, a, b, o, y0, y1);
            }

            void RoofClutter(int atlas, double s0, double s1, double d0, double d1, double y)
            {
                int n = 1 + Pick(3);
                for (int i = 0; i < n; i++)
                {
                    double w = 0.8 + rnd.NextDouble() * 2.2, dd = 0.8 + rnd.NextDouble() * 1.6, hh = 0.6 + rnd.NextDouble() * 1.6;
                    double s = s0 + w / 2 + rnd.NextDouble() * Math.Max(0.1, s1 - s0 - w), d = d0 + dd / 2 + rnd.NextDouble() * Math.Max(0.1, d1 - d0 - dd);
                    int sw = Chance(0.5) ? KitArt.S_Galv : KitArt.S_LightGrey;
                    LBox(atlas, s - w / 2, s + w / 2, d - dd / 2, d + dd / 2, y, y + hh, -1, sw, KitArt.S_MidGrey);
                }
            }

            // ---------------------------------------------------------- 60s infill (Peckam backstreets)
            public void Infill()
            {
                int floors = 3 + Pick(2);
                double fh = 2.9, gfh = 3.0, eave = gfh + fh * (floors - 1);
                Units(1, KitArt.B_EstGf, P(-W / 2, 0), P(W / 2, 0), Front, 0, gfh, Pick(6));
                Units(1, KitArt.B_EstBrickUp, P(W / 2, D), P(-W / 2, D), Back, 0, gfh, Pick(6));
                for (int f = 1; f < floors; f++)
                {
                    double y0 = gfh + fh * (f - 1), y1 = y0 + fh;
                    Units(1, KitArt.B_EstBrickUp, P(-W / 2, 0), P(W / 2, 0), Front, y0, y1, Pick(6));
                    Units(1, KitArt.B_EstBrickUp, P(W / 2, D), P(-W / 2, D), Back, y0, y1, Pick(6));
                }
                Plain(1, KitArt.B_EstBrick, P(-W / 2, D), P(-W / 2, 0), Left, 0, eave);
                Plain(1, KitArt.B_EstBrick, P(W / 2, 0), P(W / 2, D), Right, 0, eave);
                Parapet(1, -W / 2, W / 2, 0, D, eave, 0.6, KitArt.B_EstBrick, KitArt.B_EstBrick, KitArt.S_Concrete);
                FlatRoof(1, -W / 2, W / 2, 0, D, eave);
                RoofClutter(1, -W / 2 + 1, W / 2 - 1, 1, D - 1, eave);
            }

            // ---------------------------------------------------------- Knightsbury: white stucco terrace
            public void Stucco()
            {
                int houses = Math.Max(2, (int)Math.Round(W / 6.4));
                double hw = W / houses;
                double gf = 3.8, up = 3.6;
                int ups = Math.Max(2, Math.Min(4, (int)Math.Round((h - gf - 3.4) / up)));
                double eave = gf + up * ups;
                bool mansard = Chance(0.6);
                p.eave = eave;
                int gfRow = p.mixed ? KitArt.A_ShopGf : KitArt.A_StuccoGf;
                double uw = KitArt.UnitW(0, KitArt.A_StuccoUp);
                if (p.mixed) Units(0, gfRow, P(-W / 2, 0), P(W / 2, 0), Front, 0, 4.0, Pick(5));
                for (int i = 0; i < houses; i++)
                {
                    double s0 = -W / 2 + i * hw, s1 = s0 + hw;
                    bool mirror = i % 2 == 1;
                    if (!p.mixed) FaceUnit(0, gfRow, s0, s1, 0, 0, gf, Pick(5), mirror);
                    for (int f = 0; f < ups; f++)
                    {
                        double y0 = (p.mixed ? 4.0 : gf) + up * f;
                        FaceUnit(0, KitArt.A_StuccoUp, s0, s1, 0, y0, y0 + up, Pick(5), mirror);
                    }
                    for (int f = 0; f <= ups; f++)
                        Face(0, KitArt.A_GeorgUp, P(s1, D), P(s0, D), Back, f == 0 ? 0 : gf + up * (f - 1), gf + up * f, Pick(6) * KitArt.UnitW(0, KitArt.A_GeorgUp), KitArt.UnitW(0, KitArt.A_GeorgUp));
                    // portico: two columns, an entablature, steps
                    double k = hw / uw;
                    double doorC = mirror ? s1 - 1.16 * k : s0 + 1.16 * k;
                    if (!p.mixed && Room >= 1.4)
                    {
                        double pd = Math.Min(1.35, Room - 0.1);
                        LBox(0, doorC - 0.85, doorC + 0.85, -pd, 0, 0, 0.3, -1, KitArt.S_Portland, KitArt.S_Portland);
                        foreach (int sgn in new[] { -1, 1 })
                            LBox(0, doorC + sgn * 0.75 - 0.15, doorC + sgn * 0.75 + 0.15, -pd + 0.05, -pd + 0.35, 0.3, 3.15, -1, KitArt.S_White, KitArt.S_White, false);
                        LBox(0, doorC - 0.95, doorC + 0.95, -pd - 0.05, 0, 3.15, 3.6, -1, KitArt.S_White, KitArt.S_White);
                    }
                    // stacks on the party walls
                    if (i > 0 && i % 2 == 0) Chimney(0, KitArt.A_Stucco, s0, D * 0.55, mansard ? eave + 3.4 : eave + 1.0, 1.6, 1.0, 0.8);
                }
                // string course between ground and first floor, main cornice
                LBox(0, -W / 2, W / 2, -0.15, 0, (p.mixed ? 4.0 : gf) - 0.12, (p.mixed ? 4.0 : gf) + 0.08, -1, KitArt.S_White, KitArt.S_White);
                LBox(0, -W / 2 - 0.05, W / 2 + 0.05, -0.38, 0, eave - 0.3, eave + 0.05, -1, KitArt.S_White, KitArt.S_Lead);
                double topY = p.mixed ? eave + 0.2 : eave;
                Plain(0, KitArt.A_Stucco, P(-W / 2, D), P(-W / 2, 0), Left, 0, topY);
                Plain(0, KitArt.A_Stucco, P(W / 2, 0), P(W / 2, D), Right, 0, topY);
                if (mansard) Mansard(eave, 3.4, KitArt.A_Stucco);
                else { Parapet(0, -W / 2, W / 2, 0, D, eave, 1.0, KitArt.A_Cornice, KitArt.A_Stucco, KitArt.S_White); FlatRoof(0, -W / 2, W / 2, 0, D, eave); }
            }

            /// A mansard storey: steep slate front and back with dormers, plain ends, flat top.
            void Mansard(double eave, double hgt, int endRow)
            {
                double inset = 1.2, top = eave + hgt;
                double len = Math.Sqrt(inset * inset + hgt * hgt);
                double nx = p.n.x * hgt / len, ny = inset / len, nz = p.n.z * hgt / len;
                int row = KitArt.A_Mansard;
                double uw = KitArt.UnitW(0, row);
                int n = Math.Max(1, (int)Math.Round(W / uw));
                double u0 = Pick(5) * uw / Tex, u1 = u0 + n * uw / Tex;
                A.Quad(new P3(P(-W / 2, 0), eave, u0, KitArt.V(row, 0)), new P3(P(W / 2, 0), eave, u1, KitArt.V(row, 0)),
                       new P3(P(W / 2, inset), top, u1, KitArt.V(row, 1)), new P3(P(-W / 2, inset), top, u0, KitArt.V(row, 1)), nx, ny, nz);
                A.Quad(new P3(P(W / 2, D), eave, u0, KitArt.V(row, 0)), new P3(P(-W / 2, D), eave, u1, KitArt.V(row, 0)),
                       new P3(P(-W / 2, D - inset), top, u1, KitArt.V(row, 1)), new P3(P(W / 2, D - inset), top, u0, KitArt.V(row, 1)), -nx, ny, -nz);
                // ends: trapezoids of plain wall
                foreach (int side in new[] { -1, 1 })
                {
                    V2 o = p.r * side; double s = side * W / 2;
                    double fh = KitArt.FloorH(0, endRow), tt = Math.Min(1, hgt / fh);
                    V2 a = P(s, 0), b = P(s, D), c = P(s, D - inset), d = P(s, inset);
                    double L = D / Tex;
                    A.Quad(new P3(a, eave, 0, KitArt.V(endRow, 0)), new P3(b, eave, L, KitArt.V(endRow, 0)), new P3(c, top, L - inset / Tex, KitArt.V(endRow, tt)), new P3(d, top, inset / Tex, KitArt.V(endRow, tt)), o.x, 0, o.z);
                }
                FlatRoof(0, -W / 2, W / 2, inset, D - inset, top);
                Swatch(0, KitArt.S_Lead, P(-W / 2, inset - 0.1), P(W / 2, inset - 0.1), P(W / 2, inset + 0.15), P(-W / 2, inset + 0.15), top + 0.02, top + 0.02, top + 0.02, top + 0.02, 0, 1, 0);
            }

            // ---------------------------------------------------------- Knightsbury: red-brick mansion block
            public void Mansion()
            {
                double gf = p.mixed ? 4.0 : 3.8, up = 3.4;
                int ups = Math.Max(3, Math.Min(6, (int)Math.Round((h - gf - 1.0) / up)));
                double eave = gf + up * ups;
                p.eave = eave;
                Units(0, p.mixed ? KitArt.A_ShopGf : KitArt.A_MansionGf, P(-W / 2, 0), P(W / 2, 0), Front, 0, gf, Pick(5));
                Units(0, KitArt.A_MansionGf, P(W / 2, D), P(-W / 2, D), Back, 0, gf, Pick(4));
                for (int f = 0; f < ups; f++)
                {
                    double y0 = gf + up * f;
                    Units(0, KitArt.A_MansionUp, P(-W / 2, 0), P(W / 2, 0), Front, y0, y0 + up, Pick(4));
                    Units(0, KitArt.A_MansionUp, P(W / 2, D), P(-W / 2, D), Back, y0, y0 + up, Pick(4));
                    Units(0, KitArt.A_MansionUp, P(-W / 2, D), P(-W / 2, 0), Left, y0, y0 + up, Pick(4));
                    Units(0, KitArt.A_MansionUp, P(W / 2, 0), P(W / 2, D), Right, y0, y0 + up, Pick(4));
                }
                Plain(0, KitArt.A_MansionPlain, P(-W / 2, D), P(-W / 2, 0), Left, 0, gf);
                Plain(0, KitArt.A_MansionPlain, P(W / 2, 0), P(W / 2, D), Right, 0, gf);
                if (p.mixed) LBox(0, -W / 2, W / 2, -0.12, 0, gf - 0.05, gf + 0.12, -1, KitArt.S_Portland, KitArt.S_Lead);
                // parapet, slate roof behind, Dutch gables along the front
                Parapet(0, -W / 2, W / 2, 0, D, eave, 0.9, KitArt.A_MansionPlain, KitArt.A_MansionPlain, KitArt.S_Portland);
                double rise = Math.Min(4.0, (D - 2) / 2 * Math.Tan(35 * Math.PI / 180));
                Pitched(0, -W / 2 + 0.3, W / 2 - 0.3, 1.0, D - 1.0, eave + 0.5, rise, KitArt.A_MansionPlain, 0.0);
                FlatRoof(0, -W / 2, W / 2, 0, D, eave + 0.05);
                int units = Math.Max(1, (int)Math.Round(W / 8.0));
                double gw = W / units;
                for (int i = 0; i < units; i++)
                {
                    if (i % 2 == 1 && units > 2) continue;
                    double sc = -W / 2 + (i + 0.5) * gw;
                    Gable(sc, 1.6, eave + 0.9, 1.4, 1.8);
                }
                int stacks = Math.Max(1, units);
                for (int i = 0; i < stacks; i++) Chimney(0, KitArt.A_MansionPlain, -W / 2 + (i + 0.5) * W / stacks, D * 0.5, eave + 0.5 + rise - 0.8, 1.8, 1.2, 0.8);
            }

            /// A shaped gable on the front parapet: a rectangle, then a triangle, 0.35 m thick, stone coping.
            void Gable(double sc, double halfW, double y, double rect, double tri)
            {
                int row = KitArt.A_MansionPlain;
                V2 a = P(sc - halfW, 0), b = P(sc + halfW, 0), ab = P(sc - halfW, 0.35), bb = P(sc + halfW, 0.35), apex = P(sc, 0), apexB = P(sc, 0.35);
                Plain(0, row, a, b, Front, y, y + rect);
                Plain(0, row, bb, ab, Back, y, y + rect);
                PlainTri(0, row, a, b, apex, y + rect, y + rect + tri, Front);
                PlainTri(0, row, bb, ab, apexB, y + rect, y + rect + tri, Back);
                Plain(0, row, ab, a, Left, y, y + rect); Plain(0, row, b, bb, Right, y, y + rect);
                double yt = y + rect, ya = yt + tri;
                double sl = Math.Sqrt(halfW * halfW + tri * tri);
                Swatch(0, KitArt.S_Portland, a, apex, apexB, ab, yt, ya, ya, yt, -p.r.x * tri / sl, halfW / sl, -p.r.z * tri / sl);
                Swatch(0, KitArt.S_Portland, apex, b, bb, apexB, ya, yt, yt, ya, p.r.x * tri / sl, halfW / sl, p.r.z * tri / sl);
                LBox(0, sc - 0.2, sc + 0.2, -0.05, 0.4, ya - 0.1, ya + 0.35, -1, KitArt.S_Portland, KitArt.S_Portland);     // finial
            }

            // ---------------------------------------------------------- Knightsbury: Georgian brick terrace
            public void Georgian()
            {
                int houses = Math.Max(2, (int)Math.Round(W / 5.333));
                double hw = W / houses, gf = 3.6, up = 3.5;
                int ups = Math.Max(2, Math.Min(4, (int)Math.Round((h - gf - 1.0) / up)));
                double eave = gf + up * ups;
                p.eave = eave;
                for (int i = 0; i < houses; i++)
                {
                    double s0 = -W / 2 + i * hw, s1 = s0 + hw;
                    bool mirror = i % 2 == 1;
                    FaceUnit(0, KitArt.A_GeorgGf, s0, s1, 0, 0, gf, Pick(6), mirror);
                    for (int f = 0; f < ups; f++) FaceUnit(0, KitArt.A_GeorgUp, s0, s1, 0, gf + up * f, gf + up * (f + 1), Pick(6), mirror);
                    for (int f = 0; f <= ups; f++)
                        Face(0, KitArt.A_GeorgUp, P(s1, D), P(s0, D), Back, f == 0 ? 0 : gf + up * (f - 1), f == 0 ? gf : gf + up * f, Pick(6) * 5.333, 5.333);
                    double k = hw / 5.333, doorC = mirror ? s1 - 0.9 * k : s0 + 0.9 * k;
                    if (Room > 0.5) LBox(0, doorC - 0.7, doorC + 0.7, -Math.Min(0.9, Room - 0.1), 0, 0, 0.3, -1, KitArt.S_Portland, KitArt.S_Portland);
                    if (i % 2 == 0) Chimney(0, KitArt.A_GeorgPlain, s0 + (i == 0 ? 0.6 : 0), D * 0.5, eave + 1.0, 1.7, 1.1, 0.8);
                }
                LBox(0, -W / 2, W / 2, -0.12, 0, gf - 0.1, gf + 0.06, -1, KitArt.S_White, KitArt.S_White);
                LBox(0, -W / 2, W / 2, -0.2, 0, eave - 0.25, eave, -1, KitArt.S_White, KitArt.S_White);
                Plain(0, KitArt.A_GeorgPlain, P(-W / 2, D), P(-W / 2, 0), Left, 0, eave);
                Plain(0, KitArt.A_GeorgPlain, P(W / 2, 0), P(W / 2, D), Right, 0, eave);
                Parapet(0, -W / 2, W / 2, 0, D, eave, 1.0, KitArt.A_GeorgPlain, KitArt.A_GeorgPlain, KitArt.S_Portland);
                if (Chance(0.5)) { MansardBehind(eave + 0.05, 2.8); }
                else FlatRoof(0, -W / 2, W / 2, 0, D, eave + 0.05);
            }

            void MansardBehind(double y, double hgt)
            {
                // a small mansard set back behind the parapet (only its slope and top show)
                FlatRoof(0, -W / 2, W / 2, 0, D, y);
                double inset = 1.0, top = y + hgt;
                double len = Math.Sqrt(inset * inset + hgt * hgt);
                int row = KitArt.A_Mansard;
                double uw = KitArt.UnitW(0, row);
                int n = Math.Max(1, (int)Math.Round((W - 1.2) / uw));
                double u0 = Pick(5) * uw / Tex, u1 = u0 + n * uw / Tex;
                double nx = p.n.x * hgt / len, ny = inset / len, nz = p.n.z * hgt / len;
                double sA = -W / 2 + 0.6, sB = W / 2 - 0.6, d0 = 0.6, d1 = D - 0.6;
                A.Quad(new P3(P(sA, d0), y, u0, KitArt.V(row, 0)), new P3(P(sB, d0), y, u1, KitArt.V(row, 0)), new P3(P(sB, d0 + inset), top, u1, KitArt.V(row, 1)), new P3(P(sA, d0 + inset), top, u0, KitArt.V(row, 1)), nx, ny, nz);
                A.Quad(new P3(P(sB, d1), y, u0, KitArt.V(row, 0)), new P3(P(sA, d1), y, u1, KitArt.V(row, 0)), new P3(P(sA, d1 - inset), top, u1, KitArt.V(row, 1)), new P3(P(sB, d1 - inset), top, u0, KitArt.V(row, 1)), -nx, ny, -nz);
                foreach (int side in new[] { -1, 1 })
                {
                    double s = side < 0 ? sA : sB;
                    Swatch(0, KitArt.S_Slate, P(s, d0), P(s, d1), P(s, d1 - inset), P(s, d0 + inset), y, y, top, top, p.r.x * side, 0, p.r.z * side);
                }
                FlatRoof(0, sA, sB, d0 + inset, d1 - inset, top);
            }

            // ---------------------------------------------------------- towers (Circus, Peninsula)
            public void Tower(bool peninsula)
            {
                int[] skins = { KitArt.B_GlassBlue, KitArt.B_GlassGreen, KitArt.B_GlassSilver, KitArt.B_OfficeDark, KitArt.B_OfficeStone };
                int skin = skins[Pick(skins.Length)];
                double lobby = 4.8, fh = 3.8;
                int floors = Math.Max(6, (int)((h - lobby) / fh));
                int tier1 = (int)(floors * (0.72 + rnd.NextDouble() * 0.15));
                double inset1 = Chance(0.5) ? 0.0 : 0.8, inset2 = 2.2 + rnd.NextDouble() * 1.5;
                double e1 = lobby + tier1 * fh, e2 = lobby + floors * fh;
                // lobby all round (glass, lit), canopy over the front door
                Ring(1, KitArt.B_Lobby, 0, 0, 0, lobby);
                LBox(1, -4, 4, -Math.Min(2.5, Math.Max(0.6, Room)), 0, lobby - 0.6, lobby - 0.3, -1, KitArt.S_Steel, KitArt.S_Galv);
                for (int f = 0; f < tier1; f++) Ring(1, skin, inset1, Pick(20), lobby + fh * f, lobby + fh * (f + 1));
                if (inset1 > 0) FlatRing(1, 0, inset1, lobby);
                for (int f = tier1; f < floors; f++) Ring(1, skin, inset2, Pick(20), lobby + fh * f, lobby + fh * (f + 1));
                FlatRing(1, inset1, inset2, e1);
                // plant on the roof, sometimes a mast
                double pi = inset2 + 3.0;
                if (W - 2 * pi > 4 && D - 2 * pi > 4)
                {
                    Ring(1, KitArt.B_Plant, pi, 0, e2, e2 + 4.5);
                    FlatRoof(1, -W / 2 + pi, W / 2 - pi, pi, D - pi, e2 + 4.5);
                }
                FlatRoof(1, -W / 2 + inset2, W / 2 - inset2, inset2, D - inset2, e2);
                Parapet(1, -W / 2 + inset2, W / 2 - inset2, inset2, D - inset2, e2, 1.1, KitArt.B_Concrete, KitArt.B_Concrete, KitArt.S_Steel);
                if (peninsula && Chance(0.35)) LBox(1, -0.3, 0.3, D / 2 - 0.3, D / 2 + 0.3, e2 + 4.5, e2 + 4.5 + 10 + rnd.NextDouble() * 10, -1, KitArt.S_Steel, KitArt.S_Red);
            }

            /// A storey all the way round the plot (inset from every side).
            void Ring(int atlas, int row, double inset, int k, double y0, double y1)
            {
                double s0 = -W / 2 + inset, s1 = W / 2 - inset, d0 = inset, d1 = D - inset;
                Units(atlas, row, P(s0, d0), P(s1, d0), Front, y0, y1, k);
                Units(atlas, row, P(s1, d0), P(s1, d1), Right, y0, y1, k + 3);
                Units(atlas, row, P(s1, d1), P(s0, d1), Back, y0, y1, k + 5);
                Units(atlas, row, P(s0, d1), P(s0, d0), Left, y0, y1, k + 7);
            }

            /// The flat bit of roof between two insets (a setback terrace).
            void FlatRing(int atlas, double i0, double i1, double y)
            {
                if (i1 <= i0 + 0.05) return;
                FlatRoof(atlas, -W / 2 + i0, W / 2 - i0, i0, i1, y);
                FlatRoof(atlas, -W / 2 + i0, W / 2 - i0, D - i1, D - i0, y);
                FlatRoof(atlas, -W / 2 + i0, -W / 2 + i1, i1, D - i1, y);
                FlatRoof(atlas, W / 2 - i1, W / 2 - i0, i1, D - i1, y);
            }

            // ---------------------------------------------------------- Canary Quay
            public void Office()
            {
                int[] skins = { KitArt.B_GlassGreen, KitArt.B_OfficeStone, KitArt.B_GlassSilver, KitArt.B_GlassBlue };
                int skin = skins[Pick(skins.Length)];
                double lobby = 4.8, fh = 3.8;
                int floors = 3 + Pick(3);
                double e = lobby + floors * fh;
                Ring(1, KitArt.B_Lobby, 0, Pick(4), 0, lobby);
                for (int f = 0; f < floors; f++) Ring(1, skin, 0, Pick(20), lobby + fh * f, lobby + fh * (f + 1));
                Parapet(1, -W / 2, W / 2, 0, D, e, 1.0, KitArt.B_Concrete, KitArt.B_Concrete, KitArt.S_Steel);
                FlatRoof(1, -W / 2, W / 2, 0, D, e);
                double pw = Math.Min(10, W * 0.4), pd = Math.Min(7, D * 0.4);
                LBoxRing(1, -pw / 2, pw / 2, D / 2 - pd / 2, D / 2 + pd / 2, e, e + 3.0, KitArt.B_Plant);
            }

            void LBoxRing(int atlas, double s0, double s1, double d0, double d1, double y0, double y1, int row)
            {
                Plain(atlas, row, P(s0, d0), P(s1, d0), Front, y0, y1);
                Plain(atlas, row, P(s1, d0), P(s1, d1), Right, y0, y1);
                Plain(atlas, row, P(s1, d1), P(s0, d1), Back, y0, y1);
                Plain(atlas, row, P(s0, d1), P(s0, d0), Left, y0, y1);
                FlatRoof(atlas, s0, s1, d0, d1, y1);
            }

            public void Warehouse()
            {
                bool yel = Chance(0.6);
                int upRow = yel ? KitArt.B_WhUpYel : KitArt.B_WhUpRed, plain = yel ? KitArt.B_WhYel : KitArt.B_WhRed;
                double gf = 4.2, up = 3.6;
                int ups = 3 + Pick(3);
                double eave = gf + up * ups;
                int k = Pick(3);
                Ring(1, KitArt.B_WhGf, 0, k, 0, gf);
                for (int f = 0; f < ups; f++) Ring(1, upRow, 0, Pick(3), gf + up * f, gf + up * (f + 1));
                Parapet(1, -W / 2, W / 2, 0, D, eave, 0.9, plain, plain, KitArt.S_Portland);
                FlatRoof(1, -W / 2, W / 2, 0, D, eave);
                // hoists over the loading doors on the front (one per 10.67 m unit, at its loading-door bay)
                double uw = KitArt.UnitW(1, upRow);
                int n = Math.Max(1, (int)Math.Round(W / uw));
                double sc = W / (n * uw);
                for (int i = 0; i < n; i++)
                {
                    double s = -W / 2 + (i * uw + 6.667) * sc;
                    if (s > W / 2 - 0.8) continue;
                    LBox(1, s - 0.12, s + 0.12, -Math.Min(1.2, Math.Max(0.4, Room)), 0, eave - 0.9, eave - 0.6, -1, KitArt.S_Black, KitArt.S_Black);
                    LBox(1, s - 0.9, s + 0.9, -0.7, 0, eave - 0.2, eave + 1.3, plain, 0, KitArt.S_Slate);       // the hoist housing ("cathead")
                }
                // some have been converted: a glass penthouse set back on the roof
                if (Chance(0.35) && W > 14 && D > 10)
                {
                    double ins = 2.0;
                    Ring(1, KitArt.B_GlassSilver, ins, Pick(20), eave, eave + 3.8);
                    FlatRoof(1, -W / 2 + ins, W / 2 - ins, ins, D - ins, eave + 3.8);
                }
                else RoofClutter(1, -W / 2 + 1, W / 2 - 1, 1, D - 1, eave);
            }

            // ---------------------------------------------------------- Gilmore Estate
            public void EstTower()
            {
                int row = Chance(0.5) ? KitArt.B_TowerUp : KitArt.B_TowerUpB;
                double gf = 3.0, fh = 2.9;
                int floors = Math.Max(8, (int)((h - gf - 1) / fh));
                double e = gf + floors * fh;
                Ring(1, KitArt.B_EstGf, 0, Pick(6), 0, gf);
                for (int f = 0; f < floors; f++) Ring(1, row, 0, Pick(8), gf + fh * f, gf + fh * (f + 1));
                Parapet(1, -W / 2, W / 2, 0, D, e, 0.9, KitArt.B_Concrete, KitArt.B_Concrete, KitArt.S_Concrete);
                FlatRoof(1, -W / 2, W / 2, 0, D, e);
                LBoxRing(1, -3, 3, D / 2 - 2, D / 2 + 2, e, e + 3.2, KitArt.B_Concrete);                         // lift motor room
                LBox(1, -3, 3, -Math.Min(2.5, Math.Max(0.6, Room)), 0, gf - 0.35, gf - 0.05, -1, KitArt.S_Concrete, KitArt.S_Bitumen);   // entrance canopy
            }

            public void Deck()
            {
                double gf = 3.0, fh = 2.9, deck = 1.6;
                int ups = Math.Max(2, Math.Min(5, (int)Math.Round((h - gf) / fh)));
                double eave = gf + fh * ups;
                double core = 3.6;
                double sA = -W / 2 + core, sB = W / 2 - core;          // the deck runs between the two stair cores
                // ground floor: full footprint
                Units(1, KitArt.B_EstGf, P(sA, 0), P(sB, 0), Front, 0, gf, Pick(6));
                Units(1, KitArt.B_EstBack, P(sB, D), P(sA, D), Back, 0, gf, Pick(6));
                for (int f = 0; f < ups; f++)
                {
                    double y0 = gf + fh * f, y1 = y0 + fh;
                    // the flats' front walls, set back behind the walkway
                    Units(1, KitArt.B_EstDeck, P(sA, deck), P(sB, deck), Front, y0, y1, Pick(6));
                    Units(1, KitArt.B_EstBack, P(sB, D), P(sA, D), Back, y0, y1, Pick(6));
                    // walkway slab (edge, soffit, floor) and its solid parapet
                    Plain(1, KitArt.B_Concrete, P(sA, 0), P(sB, 0), Front, y0 - 0.3, y0 + 1.1);
                    Swatch(1, KitArt.S_PaleConc, P(sA, 0), P(sB, 0), P(sB, deck), P(sA, deck), y0 - 0.3, y0 - 0.3, y0 - 0.3, y0 - 0.3, 0, -1, 0);
                    Swatch(1, KitArt.S_Concrete, P(sA, 0.2), P(sB, 0.2), P(sB, deck), P(sA, deck), y0, y0, y0, y0, 0, 1, 0);
                    Swatch(1, KitArt.S_Concrete, P(sA, 0.2), P(sB, 0.2), P(sB, 0.2), P(sA, 0.2), y0, y0, y0 + 1.1, y0 + 1.1, -p.n.x, 0, -p.n.z);
                    Swatch(1, KitArt.S_PaleConc, P(sA, 0), P(sB, 0), P(sB, 0.2), P(sA, 0.2), y0 + 1.1, y0 + 1.1, y0 + 1.1, y0 + 1.1, 0, 1, 0);
                    // balconies on the back
                    double uw = KitArt.UnitW(1, KitArt.B_EstBack);
                    int n = Math.Max(1, (int)Math.Round((sB - sA) / uw));
                    double sc = (sB - sA) / (n * uw);
                    for (int i = 0; i < n; i++)
                    {
                        if ((i + f) % 3 == 2) continue;
                        double bc = sB - (i * uw + 3.175) * sc;       // the back wall runs right-to-left
                        Balcony(bc, 1.4, y0, true);
                    }
                }
                // the deck's ends (between cores and flats)
                Plain(1, KitArt.B_Concrete, P(sA, deck), P(sA, 0), Left, gf, eave);
                Plain(1, KitArt.B_Concrete, P(sB, 0), P(sB, deck), Right, gf, eave);
                // stair cores at both ends, taller than the block
                foreach (int side in new[] { -1, 1 })
                {
                    double c0 = side < 0 ? -W / 2 : sB, c1 = side < 0 ? sA : W / 2;
                    Units(1, KitArt.B_StairCore, P(c0, 0), P(c1, 0), Front, 0, eave + 2.4, 0);
                    Plain(1, KitArt.B_EstBrick, P(c1, D), P(c0, D), Back, 0, eave);
                    Plain(1, KitArt.B_Concrete, P(c1, D), P(c0, D), Back, eave, eave + 2.4);
                    if (side < 0) { Plain(1, KitArt.B_EstBrick, P(c0, D), P(c0, 0), Left, 0, eave); Plain(1, KitArt.B_Concrete, P(c0, D), P(c0, 0), Left, eave, eave + 2.4); }
                    else { Plain(1, KitArt.B_EstBrick, P(c1, 0), P(c1, D), Right, 0, eave); Plain(1, KitArt.B_Concrete, P(c1, 0), P(c1, D), Right, eave, eave + 2.4); }
                    Plain(1, KitArt.B_Concrete, side < 0 ? P(c1, 0) : P(c0, D), side < 0 ? P(c1, D) : P(c0, 0), side < 0 ? Right : Left, eave, eave + 2.4);
                    FlatRoof(1, c0, c1, 0, D, eave + 2.4);
                }
                Parapet(1, sA, sB, deck, D, eave, 0.8, KitArt.B_Concrete, KitArt.B_Concrete, KitArt.S_Concrete);
                FlatRoof(1, sA, sB, deck, D, eave);
                // the top walkway's roof
                Swatch(1, KitArt.S_PaleConc, P(sA, 0), P(sB, 0), P(sB, deck), P(sA, deck), eave - 0.02, eave - 0.02, eave - 0.02, eave - 0.02, 0, -1, 0);
                Swatch(1, KitArt.S_Bitumen, P(sA, 0), P(sB, 0), P(sB, deck), P(sA, deck), eave + 0.25, eave + 0.25, eave + 0.25, eave + 0.25, 0, 1, 0);
                Plain(1, KitArt.B_Concrete, P(sA, 0), P(sB, 0), Front, eave - 0.02, eave + 0.25);
                RoofClutter(1, sA + 2, sB - 2, deck + 1, D - 1, eave);
            }

            /// A projecting balcony: slab, painted front panel, concrete sides. back = on the rear wall.
            void Balcony(double s, double halfW, double y, bool back)
            {
                double dep = 1.2;
                double d0 = back ? D : -dep, d1 = back ? D + dep : 0;
                if (!back && Room < dep + 0.2) return;
                V2 o = back ? Back : Front;
                double dOut = back ? d1 : d0;
                Face(1, KitArt.B_Balcony, P(s - halfW, dOut), P(s + halfW, dOut), o, y, y + 1.1, Pick(6) * 5.333, 5.333 * (2 * halfW / 5.333));
                Swatch(1, KitArt.S_Concrete, P(s - halfW, d0), P(s + halfW, d0), P(s + halfW, d1), P(s - halfW, d1), y, y, y, y, 0, 1, 0);
                Swatch(1, KitArt.S_PaleConc, P(s - halfW, d0), P(s + halfW, d0), P(s + halfW, d1), P(s - halfW, d1), y - 0.2, y - 0.2, y - 0.2, y - 0.2, 0, -1, 0);
                Swatch(1, KitArt.S_Concrete, P(s - halfW, d0), P(s - halfW, d1), P(s - halfW, d1), P(s - halfW, d0), y - 0.2, y - 0.2, y + 1.1, y + 1.1, -p.r.x, 0, -p.r.z);
                Swatch(1, KitArt.S_Concrete, P(s + halfW, d0), P(s + halfW, d1), P(s + halfW, d1), P(s + halfW, d0), y - 0.2, y - 0.2, y + 1.1, y + 1.1, p.r.x, 0, p.r.z);
                Swatch(1, KitArt.S_Concrete, P(s - halfW, dOut), P(s + halfW, dOut), P(s + halfW, dOut), P(s - halfW, dOut), y - 0.2, y - 0.2, y, y, o.x, 0, o.z);
            }

            public void Maisonette(bool shops, double sOnly)
            {
                double gf = shops ? 4.0 : 3.0, fh = 2.9;
                int ups = Math.Max(2, Math.Min(4, (int)Math.Round((h - gf) / fh)));
                double eave = gf + fh * ups;
                double s0 = double.IsNaN(sOnly) ? -W / 2 : sOnly, s1 = W / 2;
                Units(1, shops ? KitArt.B_EstShop : KitArt.B_EstGf, P(s0, 0), P(s1, 0), Front, 0, gf, Pick(6));
                Units(1, KitArt.B_EstGf, P(s1, D), P(s0, D), Back, 0, gf, Pick(6));
                for (int f = 0; f < ups; f++)
                {
                    double y0 = gf + fh * f, y1 = y0 + fh;
                    Units(1, KitArt.B_EstBrickUp, P(s0, 0), P(s1, 0), Front, y0, y1, Pick(6));
                    Units(1, KitArt.B_EstBrickUp, P(s1, D), P(s0, D), Back, y0, y1, Pick(6));
                    if (f % 2 == 0)
                    {
                        double uw = KitArt.UnitW(1, KitArt.B_EstBrickUp);
                        int n = Math.Max(1, (int)Math.Round((s1 - s0) / uw));
                        double sc = (s1 - s0) / (n * uw);
                        for (int i = 0; i < n; i += 2) Balcony(s1 - (i * uw + 2.67) * sc, 1.0, y0, true);
                    }
                }
                Plain(1, KitArt.B_EstBrick, P(s0, D), P(s0, 0), Left, 0, eave);
                Plain(1, KitArt.B_EstBrick, P(s1, 0), P(s1, D), Right, 0, eave);
                if (shops) LBox(1, s0, s1, -Math.Min(1.8, Math.Max(0.3, Room)), 0, gf - 0.3, gf, -1, KitArt.S_Concrete, KitArt.S_Bitumen);   // a canopy over the shops
                Parapet(1, s0, s1, 0, D, eave, 0.7, KitArt.B_EstBrick, KitArt.B_EstBrick, KitArt.S_Concrete);
                FlatRoof(1, s0, s1, 0, D, eave);
                RoofClutter(1, s0 + 1, s1 - 1, 1, D - 1, eave);
            }

            public void Garages()
            {
                // a row of lock-ups at one end, a low maisonette block on the rest, a gap between them
                double g = Math.Round(W * (0.4 + rnd.NextDouble() * 0.1) / 2.667) * 2.667;
                double gEnd = -W / 2 + g;
                Units(1, KitArt.B_Garages, P(-W / 2, 0), P(gEnd, 0), Front, 0, 2.6, Pick(12));
                Plain(1, KitArt.B_EstBrick, P(gEnd, D), P(-W / 2, D), Back, 0, 2.6);
                Plain(1, KitArt.B_EstBrick, P(-W / 2, D), P(-W / 2, 0), Left, 0, 2.6);
                Plain(1, KitArt.B_EstBrick, P(gEnd, 0), P(gEnd, D), Right, 0, 2.6);
                Swatch(1, KitArt.S_Bitumen, P(-W / 2, 0), P(gEnd, 0), P(gEnd, D), P(-W / 2, D), 2.75, 2.75, 2.55, 2.55, 0, 1, 0);
                Plain(1, KitArt.B_Concrete, P(-W / 2, 0), P(gEnd, 0), Front, 2.6, 2.75);
                Maisonette(false, gEnd + 3.0);
            }
        }
    }
}
