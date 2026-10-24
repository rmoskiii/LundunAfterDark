// E1: the map expansions, built by the same machinery as the city.
// The city is always built first, exactly as before (its own random numbers, nothing moved). An expansion area
// (Brixtun first) is then built as a little world of its own: its roads, junctions, lamps, squares, parks and
// buildings, with its own seed, by the same generator functions the city uses. Its streets borrow a city district's
// character (Brixtun's town centre is built like Peckam, its estates like Gilmore), with Brixtun's own shops,
// postcodes, tags and landmark buildings on top.
// Where an expansion road meets a city road (Brixtun Road on the Ring, say), the city builds the junction: only the
// Ring's markings and lamps right at the join change, and its junction plate grows to take the new road.
// Pure C# (no UnityEngine), so it can be tested outside Unity.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        /// True while the generator is working on an expansion area (set by BuildExpansion, cleared by Build).
        public static bool ExpansionMode { get; private set; }
        /// The expansion area as the generator sees it (its roads, the city roads it joins, flavoured districts).
        public static LundunWorldData ExpansionWorld { get; private set; }

        const int ExpSeed = 1948;
        const double ExpGridX0 = -200, ExpGridZ0 = -1000, ExpGridW = 1400, ExpGridH = 1060;
        const double ExpEmptyLots = 0.15;          // Brixtun keeps more gaps between its buildings (empty lots, yards)

        /// Which city district each expansion district is built like (its plots, lamps, paving, dressing, grit).
        static readonly Dictionary<string, string> Flavours = new Dictionary<string, string>
        {
            { "D_BRIXTUN", "D_PECKAM" }, { "D_BRIXTUN_EST", "D_GILMOR" } };
        static string Flavour(string id) { string f; return id != null && Flavours.TryGetValue(id, out f) ? f : "D_PECKAM"; }

        /// Brixtun's high streets: shop parades all along them.
        static readonly HashSet<string> BrixtunParadeRoads = new HashSet<string> { "Brixtun Road", "Coldharbor Lane", "Acre Lane", "Electric Parade", "Atlantic Road", "Stockwell Road" };

        // ================================================================== joins (the city's side)
        class JoinInfo { public string id; public V2 c; public bool cityJunction; public double radius; public List<V2> dirs = new List<V2>(); public List<double> half = new List<double>(); }
        static readonly List<JoinInfo> joins = new List<JoinInfo>();
        static readonly Dictionary<string, List<double[]>> joinCuts = new Dictionary<string, List<double[]>>();
        static readonly List<double[]> NoCuts = new List<double[]>();
        /// Where the city's lamps would have stood at the joins (the dressing still treats those spots as taken).
        static readonly List<V2> joinGhostLamps = new List<V2>();

        /// The bits of a city road's markings and lamps an expansion junction takes over (none without expansions).
        static List<double[]> JoinCuts(RoadInfo ri) { List<double[]> c; return joinCuts.TryGetValue(ri.r.id, out c) ? c : NoCuts; }

        /// Works out every junction where an expansion road meets a city road: its legs, its size, and the stretch
        /// of each city road it covers. Only the junctions themselves are touched.
        static void PrepareJoins()
        {
            joins.Clear(); joinCuts.Clear();
            if (ExpansionMode || W.expansion == null || W.expansion.junctions == null || W.expansion.roads == null) return;
            Dictionary<string, LwRoad> xr = new Dictionary<string, LwRoad>();
            foreach (LwRoad r in W.expansion.roads) xr[r.id] = r;
            foreach (LwJunction xj in W.expansion.junctions)
            {
                bool onCity = juncs.ContainsKey(xj.id);
                bool touches = onCity;
                foreach (string r in xj.roads) if (roads.ContainsKey(r)) touches = true;
                if (!touches) continue;
                JoinInfo J = new JoinInfo { id = xj.id, c = new V2(xj.x, xj.z), cityJunction = onCity };
                if (onCity)
                {
                    JInfo ji = juncs[xj.id];
                    for (int k = 0; k < ji.legDir.Count; k++) { J.dirs.Add(ji.legDir[k]); J.half.Add(ji.legRoad[k].w / 2 + Math.Max(ji.legRoad[k].pave, 0)); }
                }
                foreach (LwLeg leg in xj.legs)
                {
                    RoadInfo cri; Line line; double w, pave, ch;
                    if (roads.TryGetValue(leg.road, out cri)) { line = cri.line; w = cri.w; pave = cri.pave; ch = ProjectChainage(line, J.c); }
                    else
                    {
                        LwRoad r; if (!xr.TryGetValue(leg.road, out r)) continue;
                        line = new Line(Pts(r.points)); w = r.width; pave = ClassPave(r.roadClass);
                        ch = ProjectChainage(line, J.c);
                    }
                    double ahead = leg.toward == "end" ? Math.Min(ch + 6, line.Length) : Math.Max(ch - 6, 0);
                    V2 dd; V2 q = line.At(ahead, out dd);
                    J.dirs.Add((q - J.c).Norm); J.half.Add(w / 2 + Math.Max(pave, 0));
                }
                double rad = 0; foreach (double h in J.half) rad = Math.Max(rad, h);
                J.radius = rad + 0.5;
                // the city roads through it lose their markings (and lamps) across the junction
                List<string> cityRoads = new List<string>();
                if (onCity) foreach (string r in juncs[xj.id].j.roads) cityRoads.Add(r);
                foreach (string r in xj.roads) if (roads.ContainsKey(r) && !cityRoads.Contains(r)) cityRoads.Add(r);
                foreach (string rid in cityRoads)
                {
                    RoadInfo ri; if (!roads.TryGetValue(rid, out ri)) continue;
                    double c = onCity && Array.IndexOf(ri.r.junctions, xj.id) >= 0 ? ChainageAt(ri, xj.id) : ProjectChainage(ri.line, J.c);
                    List<double[]> list; if (!joinCuts.TryGetValue(rid, out list)) { list = new List<double[]>(); joinCuts[rid] = list; }
                    list.Add(new[] { c - J.radius, c + J.radius });
                }
                joins.Add(J);
            }
        }

        static double ClassPave(string cls)
        {
            foreach (LwRoadClass c in W.roadClasses) if (c.name == cls) return c.pavement;
            if (W.expansion != null && W.expansion.roadClasses != null) foreach (LwRoadClass c in W.expansion.roadClasses) if (c.name == cls) return c.pavement;
            return 0;
        }

        /// Distance along a line to the point nearest q.
        static double ProjectChainage(Line line, V2 q)
        {
            double best = double.MaxValue, bs = 0;
            for (int i = 0; i + 1 < line.p.Count; i++)
            {
                V2 a = line.p[i], b = line.p[i + 1], ab = b - a;
                double l2 = V2.Dot(ab, ab), t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
                double d = V2.Dist(q, a + ab * t);
                if (d < best) { best = d; bs = line.ch[i] + t * Math.Sqrt(l2); }
            }
            return bs;
        }

        /// The plate for a city junction an expansion road joins (true if it was one).
        static bool JoinPlate(MeshData m, string id, double y)
        {
            if (ExpansionMode) return false;
            foreach (JoinInfo J in joins) if (J.cityJunction && J.id == id) { Plate(m, J, y); return true; }
            return false;
        }

        /// Plates for the new junctions on city roads.
        static void NewJoinPlates(MeshData m, double y)
        {
            if (ExpansionMode) return;
            foreach (JoinInfo J in joins) if (!J.cityJunction) Plate(m, J, y);
        }

        static void Plate(MeshData m, JoinInfo J, double y)
        {
            List<V2> pts = new List<V2>();
            for (int k = 0; k < J.dirs.Count; k++)
            {
                V2 d = J.dirs[k], l = d.Left;
                V2 end = J.c + d * J.radius;
                pts.Add(end + l * J.half[k]); pts.Add(end - l * J.half[k]);
                pts.Add(J.c + l * J.half[k]); pts.Add(J.c - l * J.half[k]);
            }
            List<P3> poly = new List<P3>();
            foreach (V2 q in Hull(pts)) poly.Add(new P3(q, y));
            m.FanUp(poly);
        }

        /// In an expansion: does this junction take in a city road? (The city builds those.)
        static bool TouchesCity(JInfo ji)
        {
            foreach (RoadInfo ri in ji.legRoad) if (!roads.ContainsKey(ri.r.id)) return true;
            return false;
        }

        /// A road for a junction leg: the area's own, or (in an expansion) the city road it joins.
        static RoadInfo LegRoad(string id)
        {
            RoadInfo ri;
            if (roads.TryGetValue(id, out ri)) return ri;
            if (ExpansionMode && contextRoads != null && contextRoads.TryGetValue(id, out ri)) return ri;
            return roads[id];                       // (throws, as it always did)
        }

        static double LegChainage(RoadInfo ri, LwJunction j)
        {
            if (ExpansionMode && !roads.ContainsKey(ri.r.id)) return ProjectChainage(ri.line, new V2(j.x, j.z));
            return ChainageAt(ri, j.id);
        }

        // ================================================================== building an expansion area
        static Dictionary<string, RoadInfo> contextRoads;
        static Dictionary<string, JInfo> cityJuncs;

        /// The city first (for the railway and the roads it joins), then the expansion area. Afterwards the generator is
        /// left set up for the area, so Dress(ExpansionWorld, result) and Streets(ExpansionWorld, …) dress it next.
        public static GreyboxResult BuildExpansion(LundunWorldData world)
        {
            Build(world);
            GreyboxResult R = new GreyboxResult();
            LundunExpansionData x = world.expansion;
            if (x == null || x.roads == null || x.roads.Length == 0) return R;
            contextRoads = roads; cityJuncs = juncs;
            LundunWorldData xw = AreaWorld(world);
            ExpansionWorld = xw;
            ExpansionMode = true;
            W = xw; rng = new Random(ExpSeed);
            Prepare();
            BuildRoads(R);
            BuildJunctions(R);
            BuildLamps(R);
            BuildParks(R);
            BuildBuildings(R);
            BuildRailLights(R);
            BuildMarkers(R);                // E3: police spawn points along its roads (no random numbers)
            BuildRetailPark(R);             // E4: Ferndale Retail Park (its own random numbers, last)
            List<MeshData> split = new List<MeshData>();
            foreach (MeshData m in R.meshes) { if (m.chunk) split.AddRange(Chunk(m)); else split.Add(m); }
            R.meshes = split;
            int v = 0; foreach (MeshData m in R.meshes) v += m.VertexCount;
            R.log.Add(string.Format("Brixtun: {0} roads, {1} meshes, {2} vertices, {3} buildings, {4} lamps", roads.Count, R.meshes.Count, v, R.buildings.Count, R.lamps.Count));
            return R;
        }

        /// The expansion area as a world of its own: its roads and places, the city roads its junctions take in,
        /// and its districts under the ids of the city districts they're built like.
        static LundunWorldData AreaWorld(LundunWorldData city)
        {
            LundunExpansionData x = city.expansion;
            List<LwRoadClass> classes = new List<LwRoadClass>(city.roadClasses);
            if (x.roadClasses != null) classes.AddRange(x.roadClasses);
            List<LwJunction> js = new List<LwJunction>();
            foreach (LwJunction j in x.junctions)
            {
                JInfo cj;
                if (!cityJuncs.TryGetValue(j.id, out cj)) { js.Add(j); continue; }
                List<string> rs = new List<string>(cj.j.roads); foreach (string r in j.roads) if (!rs.Contains(r)) rs.Add(r);
                List<LwLeg> legs = new List<LwLeg>(cj.j.legs); legs.AddRange(j.legs);
                js.Add(new LwJunction { id = j.id, x = j.x, z = j.z, arms = legs.Count, roads = rs.ToArray(), legs = legs.ToArray() });
            }
            List<LwPoly> ds = new List<LwPoly>();
            if (x.districts != null) foreach (LwPoly d in x.districts) ds.Add(new LwPoly { id = Flavour(d.id), name = d.name, polygon = d.polygon });
            return new LundunWorldData
            {
                schemaVersion = city.schemaVersion, heights = city.heights, worldBounds = city.worldBounds, camera = city.camera,
                roadClasses = classes.ToArray(), river = city.river, roads = x.roads, bridges = new LwBridge[0], junctions = js.ToArray(),
                districts = ds.ToArray(), parks = x.parks ?? new LwPoly[0], pois = x.pois ?? new LwPoi[0], spawns = new LwSpawn[0],
                existingStreet = new LwExistingStreet { centreX = -1e6f, centreZ = -1e6f, sizeX = 0, sizeZ = 0 },
                expansion = x
            };
        }

        // ------------------------------------------------------------------ squares (paved, not parks)
        static bool IsSquare(LwPoly park) { return ExpansionMode && park.name != null && (park.name.EndsWith("Square") || park.name.EndsWith("Market")); }
        /// E3: a market square (paved like a town square; its stalls come from DressMarket instead of trees and benches).
        static bool IsMarket(LwPoly park) { return ExpansionMode && park.name != null && park.name.EndsWith("Market"); }

        /// A town square's paving: 3 m squares of pale stone with a darker border, at pavement height, walkable.
        static void SquarePaving(GreyboxResult R, List<V2> poly)
        {
            MeshData m = new MeshData("SquarePaving", "Parks", KitMatB, true) { navArea = PavementArea };
            double y = W.heights.pavementTop, minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (V2 q in poly) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
            double u, v;
            for (double gx = minX; gx < maxX - 0.01; gx += 3)
                for (double gz = minZ; gz < maxZ - 0.01; gz += 3)
                {
                    double x1 = Math.Min(maxX, gx + 3), z1 = Math.Min(maxZ, gz + 3);
                    V2 mid = new V2((gx + x1) / 2, (gz + z1) / 2);
                    if (!InPoly(mid, poly)) continue;
                    bool edge = gx - minX < 2.9 || maxX - x1 < 0.1 || gz - minZ < 2.9 || maxZ - z1 < 0.1;
                    int ix = (int)Math.Round((gx - minX) / 3), iz = (int)Math.Round((gz - minZ) / 3);
                    int sw = edge ? KitArt.S_SootStone : ((ix + iz) & 1) == 0 ? KitArt.S_PaleConc : KitArt.S_Portland;
                    KitArt.SwatchUV(sw, out u, out v);
                    m.Quad(new P3(gx, y, gz, u, v), new P3(x1, y, gz, u, v), new P3(x1, y, z1, u, v), new P3(gx, y, z1, u, v), 0, 1, 0);
                }
            // the kerb round it
            KitArt.SwatchUV(KitArt.S_SootStone, out u, out v);
            for (int i = 0; i < poly.Count; i++)
            {
                V2 a = poly[i], b = poly[(i + 1) % poly.Count];
                V2 o = (b - a).Norm.Left; if (InPoly(Lerp(a, b, 0.5) + o * 0.5, poly)) o = o * -1;
                m.Quad(new P3(a, -0.02, u, v), new P3(b, -0.02, u, v), new P3(b, y, u, v), new P3(a, y, u, v), o.x, 0, o.z);
            }
            R.meshes.Add(m);
        }

        // ------------------------------------------------------------------ the expansion's building grid
        /// Keeps the railway, the city's roads and the station's room clear of buildings, and places Brixtun's landmark
        /// buildings before the streets fill up.
        static void ExpansionBlocks(GreyboxResult R)
        {
            specialPlans.Clear();
            if (railPlan != null)
            {
                RailPlan rp = railPlan;
                for (int i = 0; i + 1 < rp.line.p.Count; i++)
                {
                    double s = (rp.line.ch[i] + rp.line.ch[i + 1]) / 2;
                    double r = (RailKind(s) == 'E' ? RailHW + RailDeck * BankSlope : RailStationHW) + 2.5;
                    if (InBrixStation(s)) r = 13.5;                        // E2: room for Brixtun station's stairs and the way round to them
                    MarkSegment(rp.line.p[i], rp.line.p[i + 1], r);
                }
            }
            if (contextRoads != null)
                foreach (RoadInfo ri in contextRoads.Values)
                {
                    double r = ri.w / 2 + Math.Max(ri.pave, ri.verge) + 1.5;
                    for (int i = 0; i + 1 < ri.line.p.Count; i++) MarkSegment(ri.line.p[i], ri.line.p[i + 1], r);
                }
            Dictionary<string, List<V2>> dpoly = new Dictionary<string, List<V2>>();
            foreach (LwPoly d in W.districts) dpoly[d.id] = Pts(d.polygon);
            foreach (Special sp in Specials)
            {
                string did = DistrictAt(sp.c, dpoly);
                if (did == null) continue;
                int before = R.buildings.Count;
                if (!TryPlace(R, did, dpoly[did], sp.c, sp.dir, sp.len, sp.dep, sp.h, 3)) { R.log.Add("  (no room for " + sp.name + ")"); continue; }
                specialPlans[R.buildings[before]] = sp;
            }
        }

        /// Brixtun's landmark buildings: where, how big, which way they face, and what they are.
        class Special { public string name; public KitType type; public V2 c, dir, face; public double len, dep, h; public int sign; }
        static readonly Special[] Specials =
        {
            new Special { name = "Ashdown's", type = KitType.DeptStore, c = new V2(397.0, -274.5), dir = new V2(0, 1), face = new V2(1, 0), len = 34, dep = 24, h = 16, sign = 0 },
            new Special { name = "Kemble & Hart", type = KitType.DeptStore, c = new V2(452.0, -353), dir = new V2(0, 1), face = new V2(-1, 0), len = 42, dep = 24, h = 16, sign = 1 },
            new Special { name = "The Regal Hall", type = KitType.MusicHall, c = new V2(286.0, -172), dir = new V2(0, 1), face = new V2(-1, 0), len = 40, dep = 34, h = 17, sign = 2 },
            new Special { name = "Brixtun Library", type = KitType.Civic, c = new V2(513.0, -264), dir = new V2(0, 1), face = new V2(-1, 0), len = 44, dep = 18, h = 13, sign = 3 },
        };
        static readonly Dictionary<BuildingInfo, Special> specialPlans = new Dictionary<BuildingInfo, Special>();

        /// For the kit: a landmark building's plan (type and front), or null.
        static KitPlan SpecialPlan(BuildingInfo b)
        {
            Special sp;
            if (!ExpansionMode || !specialPlans.TryGetValue(b, out sp)) return null;
            V2 f = b.dir.Norm, l = f.Left;
            V2[] normals = { l, l * -1, f, f * -1 };
            double[] faceLen = { b.len, b.len, b.dep, b.dep };
            int best = 0; double bd = -2;
            for (int i = 0; i < 4; i++) { double d = V2.Dot(normals[i], sp.face.Norm); if (d > bd) { bd = d; best = i; } }
            KitPlan p = new KitPlan { type = sp.type, face = best, n = normals[best], c = b.c, W = faceLen[best], D = best < 2 ? b.dep : b.len, road = sp.name, gap = 2.2 };
            p.r = p.n.Left;
            p.mixed = sp.sign >= 0;
            p.eave = b.h;
            specialSign[p] = sp.sign;
            return p;
        }
        static readonly Dictionary<KitPlan, int> specialSign = new Dictionary<KitPlan, int>();

        // ------------------------------------------------------------------ E3: the expansion's places for the map registry
        /// Where an expansion's landmark buildings, squares, market and station are (their street entrances), after
        /// BuildExpansion. Sorted by id.
        public class MapPlace { public string id, name, kind; public double x, z; public bool label; }

        public static List<MapPlace> ExpansionPlaces()
        {
            List<MapPlace> list = new List<MapPlace>();
            foreach (KeyValuePair<BuildingInfo, Special> kv in specialPlans)
            {
                BuildingInfo b = kv.Key; Special sp = kv.Value;
                V2 f = sp.face.Norm;
                double ext = Math.Abs(V2.Dot(b.dir.Norm, f)) > 0.7 ? b.len / 2 : b.dep / 2;
                V2 p = b.c + f * (ext + 3);
                string kind = sp.type == KitType.DeptStore ? "shop" : sp.type == KitType.MusicHall ? "venue" : "landmark";
                list.Add(new MapPlace { id = "BRX_" + Slug(sp.name), name = sp.name, kind = kind, x = p.x, z = p.z, label = false });
            }
            foreach (LwPoly park in W.parks)
            {
                if (!IsSquare(park)) continue;
                List<V2> poly = Pts(park.polygon);
                double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                foreach (V2 q in poly) { minX = Math.Min(minX, q.x); maxX = Math.Max(maxX, q.x); minZ = Math.Min(minZ, q.z); maxZ = Math.Max(maxZ, q.z); }
                if (IsMarket(park)) list.Add(new MapPlace { id = "MARKET_" + Slug(park.name), name = park.name, kind = "market", x = minX + 1.5, z = (minZ + maxZ) / 2, label = true });
                else list.Add(new MapPlace { id = "LANDMARK_" + Slug(park.name), name = park.name, kind = "landmark", x = (minX + maxX) / 2, z = (minZ + maxZ) / 2, label = true });
            }
            // E4: the retail park's stores (their doors), and the park itself
            if (retailMap.Count > 0)
            {
                foreach (Store s in Stores)
                {
                    V2 p = s.Door + s.Out * 12;
                    bool home = s.id == "FLATPAK";        // E6: the home store has its own icon, and its name on the map
                    list.Add(new MapPlace { id = "SHOP_" + s.id, name = s.name, kind = home ? "homestore" : "shop", x = p.x, z = p.z, label = home });
                }
                list.Add(new MapPlace { id = "LANDMARK_FERNDALE_RETAIL_PARK", name = RetailParkName, kind = "landmark", x = Roundabout.x + 60, z = Roundabout.z + 70, label = true });
            }
            double[] st = BrixtunStationEntrance();
            if (st != null) list.Add(new MapPlace { id = "RAIL_BRIXTUN", name = "Brixtun Station", kind = "rail", x = st[0], z = st[2], label = false });
            list.Sort((m, n) => string.CompareOrdinal(m.id, n.id));
            return list;
        }

        static string Slug(string name)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in name.ToUpperInvariant()) { if (char.IsLetterOrDigit(ch)) sb.Append(ch); else if (ch == ' ' && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_'); }
            return sb.ToString().Trim('_');
        }

        // ------------------------------------------------------------------ lights under the expansion's rail bridges
        static void BuildRailLights(GreyboxResult R)
        {
            if (railPlan == null || railLamps.Count == 0) return;
            Dictionary<int, MeshData> fit = new Dictionary<int, MeshData>();
            foreach (double[] l in railLamps)
            {
                V2 q = new V2(l[0], l[1]); double lat, s = RailProject(railPlan.line, q, out lat);
                if (!InRailSpan(s, 0) || Math.Abs(lat) > RailHW - 0.6) continue;
                int st = (int)l[2];
                MeshData m;
                if (!fit.TryGetValue(st, out m)) { m = new MeshData("Railway_Lights_" + st, "Lamps", LampHeadMats[st], false) { navIgnore = true }; fit[st] = m; }
                RBox(m, -1, q, RDir(s), 0.35, 0.18, BeamY - 0.14, BeamY, true);
            }
            List<int> fk = new List<int>(fit.Keys); fk.Sort();
            foreach (int k in fk) R.meshes.Add(fit[k]);
        }

        // ================================================================== Brixtun's landmark buildings (the kit)
        partial class Kit
        {
            int SignUnit { get { int u; return specialSign.TryGetValue(p, out u) ? u : 4; } }

            /// A name board standing proud of the front, from s0 to s1, between y0 and y1 (one unit of the sign strip).
            void NameBoard(double s0, double s1, double d, double y0, double y1)
            {
                LBox(1, s0 - 0.1, s1 + 0.1, d - 0.08, d + 0.02, y0 - 0.1, y1 + 0.1, -1, KitArt.S_DarkGrey, KitArt.S_DarkGrey);
                Face(0, KitArt.A_BrixtunSigns, P(s0, d - 0.09), P(s1, d - 0.09), Front, y0, y1, SignUnit * 6.4, 6.4);
            }

            /// A 1930s department store: shop windows all along the ground floor under a canopy, three storeys of
            /// Portland stone with tall windows, a stepped stone tower bay over the doors with fins, the name on the parapet.
            public void DeptStore()
            {
                double gf = 4.4, fh = 3.8;
                int ups = Math.Max(2, Math.Min(4, (int)Math.Round((h - gf - 1.0) / fh)));
                double eave = gf + ups * fh;
                p.eave = eave;
                Units(0, KitArt.A_ShopGf, P(-W / 2, 0), P(W / 2, 0), Front, 0, gf, Pick(5));
                for (int f = 0; f < ups; f++)
                {
                    double y0 = gf + fh * f, y1 = y0 + fh;
                    Units(1, KitArt.B_OfficeStone, P(-W / 2, 0), P(W / 2, 0), Front, y0, y1, Pick(20));
                    Units(1, KitArt.B_OfficeStone, P(W / 2, 0), P(W / 2, D), Right, y0, y1, Pick(20));
                    Units(1, KitArt.B_OfficeStone, P(-W / 2, D), P(-W / 2, 0), Left, y0, y1, Pick(20));
                }
                Plain(1, KitArt.B_Concrete, P(W / 2, 0), P(W / 2, D), Right, 0, gf);
                Plain(1, KitArt.B_Concrete, P(-W / 2, D), P(-W / 2, 0), Left, 0, gf);
                Plain(1, KitArt.B_Concrete, P(W / 2, D), P(-W / 2, D), Back, 0, eave);
                // the canopy over the shop windows
                double cd = Math.Min(2.2, Math.Max(0.6, Room));
                LBox(1, -W / 2, W / 2, -cd, 0, gf - 0.55, gf - 0.2, -1, KitArt.S_DarkGrey, KitArt.S_Lead);
                // stone bands between the floors
                for (int f = 1; f <= ups; f++) LBox(1, -W / 2, W / 2, -0.15, 0, gf + fh * f - 0.12, gf + fh * f + 0.08, -1, KitArt.S_Portland, KitArt.S_Portland);
                // the tower bay over the doors: projecting stone, vertical fins, stepped top
                double bw = Math.Min(9, W * 0.28), proj = Math.Min(0.7, Math.Max(0.3, Room - 0.2));
                LBox(1, -bw / 2, bw / 2, -proj, 0, gf - 0.2, eave + 3.2, -1, KitArt.S_Portland, KitArt.S_Portland);
                LBox(1, -bw / 2 + 1.2, bw / 2 - 1.2, -proj, 0, eave + 3.2, eave + 4.6, -1, KitArt.S_Portland, KitArt.S_Portland);
                for (int i = 0; i < 4; i++)
                {
                    double s = -bw / 2 + 1.5 + i * (bw - 3.0) / 3;
                    LBox(1, s - 0.14, s + 0.14, -proj - 0.35, -proj, gf + 0.3, eave + 2.9, -1, KitArt.S_Cream, KitArt.S_Cream);
                }
                LBox(1, -0.05, 0.05, D / 2 - 0.05, D / 2 + 0.05, eave + 4.6, eave + 10.5, -1, KitArt.S_White, KitArt.S_White);   // flagpole
                // parapet with the name on it, flat roof
                Parapet(1, -W / 2, W / 2, 0, D, eave, 1.8, KitArt.B_Concrete, KitArt.B_Concrete, KitArt.S_Portland);
                FlatRoof(1, -W / 2, W / 2, 0, D, eave);
                double nw = Math.Min(15, (W - bw) / 2 - 1.5);
                if (nw > 4)
                {
                    NameBoard(-W / 2 + 1.0, -W / 2 + 1.0 + nw, -0.02, eave + 0.3, eave + 1.5);
                    NameBoard(W / 2 - 1.0 - nw, W / 2 - 1.0, -0.02, eave + 0.3, eave + 1.5);
                }
                RoofClutter(1, -W / 2 + 2, W / 2 - 2, 3, D - 2, eave);
            }

            /// An art-deco music hall: a tall cream front with fins and a stepped top, glazed doors under a deep canopy,
            /// the name in big letters over it, and the brick auditorium behind.
            public void MusicHall()
            {
                double eave = Math.Max(14, h), lobby = 4.8;
                p.eave = eave;
                double side = Math.Min(5, W * 0.15);
                // the front: stone flanks, glazed doors, a tall window over them
                Plain(1, KitArt.B_Concrete, P(-W / 2, 0), P(-W / 2 + side, 0), Front, 0, eave);
                Plain(1, KitArt.B_Concrete, P(W / 2 - side, 0), P(W / 2, 0), Front, 0, eave);
                Units(1, KitArt.B_Lobby, P(-W / 2 + side, 0), P(W / 2 - side, 0), Front, 0, lobby, Pick(4));
                Plain(1, KitArt.B_Concrete, P(-W / 2 + side, 0), P(W / 2 - side, 0), Front, lobby, eave);
                Units(1, KitArt.B_GlassSilver, P(-W / 2 + side + 2, -0.03), P(W / 2 - side - 2, -0.03), Front, 8.2, eave - 1.2, Pick(20));
                // fins up the front, and the stepped top
                int fins = Math.Max(4, (int)((W - 2 * side) / 2.6));
                for (int i = 0; i <= fins; i++)
                {
                    double s = -W / 2 + side + i * (W - 2 * side) / fins;
                    LBox(1, s - 0.18, s + 0.18, -0.5, 0, 8.2, eave + (i == fins / 2 ? 4.2 : 1.6), -1, KitArt.S_Cream, KitArt.S_Cream);
                }
                LBox(1, -W / 2, -W / 2 + side, -0.3, 0, eave, eave + 1.2, -1, KitArt.S_Cream, KitArt.S_Cream);
                LBox(1, W / 2 - side, W / 2, -0.3, 0, eave, eave + 1.2, -1, KitArt.S_Cream, KitArt.S_Cream);
                LBox(1, -W / 2 + side, W / 2 - side, -0.25, 0.4, eave, eave + 2.4, -1, KitArt.S_Cream, KitArt.S_Cream);
                // the canopy, and the name over it
                double cd = Math.Min(3.0, Math.Max(0.8, Room));
                LBox(1, -W / 2 + side, W / 2 - side, -cd, 0, lobby - 0.2, lobby + 0.35, -1, KitArt.S_Black, KitArt.S_Lead);
                LBox(1, -W / 2 + side, W / 2 - side, -cd - 0.05, -cd + 0.05, lobby - 0.2, lobby + 0.35, -1, KitArt.S_Yellow, KitArt.S_Yellow);
                double nw = Math.Min(16, W - 2 * side - 2);
                NameBoard(-nw / 2, nw / 2, -0.55, lobby + 0.7, lobby + 3.2);
                // the auditorium: brick, a slate-grey roof
                int brick = KitArt.A_BrickYel;
                Plain(0, brick, P(W / 2, 0), P(W / 2, D), Right, 0, eave);
                Plain(0, brick, P(-W / 2, D), P(-W / 2, 0), Left, 0, eave);
                Plain(0, brick, P(W / 2, D), P(-W / 2, D), Back, 0, eave);
                FlatRoof(1, -W / 2, W / 2, 0, D, eave);
                LBox(1, -W / 2 + 3, W / 2 - 3, 6, D - 3, eave, eave + 3.5, -1, KitArt.S_Slate, KitArt.S_Slate);     // the fly tower over the stage
            }

            /// A civic library on the square: Portland stone, a colonnade in front of tall glazed doors, the name on
            /// the entablature.
            public void Civic()
            {
                double gf = 5.0, fh = 4.2, eave = gf + 2 * fh;
                p.eave = eave;
                Units(1, KitArt.B_Lobby, P(-W / 2 + 3, 0), P(W / 2 - 3, 0), Front, 0, gf, Pick(4));
                Plain(1, KitArt.B_Concrete, P(-W / 2, 0), P(-W / 2 + 3, 0), Front, 0, gf);
                Plain(1, KitArt.B_Concrete, P(W / 2 - 3, 0), P(W / 2, 0), Front, 0, gf);
                for (int f = 0; f < 2; f++)
                {
                    double y0 = gf + fh * f, y1 = y0 + fh;
                    Units(1, KitArt.B_OfficeStone, P(-W / 2, 0), P(W / 2, 0), Front, y0, y1, Pick(20));
                    Units(1, KitArt.B_OfficeStone, P(W / 2, 0), P(W / 2, D), Right, y0, y1, Pick(20));
                    Units(1, KitArt.B_OfficeStone, P(-W / 2, D), P(-W / 2, 0), Left, y0, y1, Pick(20));
                    Units(1, KitArt.B_OfficeStone, P(W / 2, D), P(-W / 2, D), Back, y0, y1, Pick(20));
                }
                Plain(1, KitArt.B_Concrete, P(W / 2, 0), P(W / 2, D), Right, 0, gf);
                Plain(1, KitArt.B_Concrete, P(-W / 2, D), P(-W / 2, 0), Left, 0, gf);
                Plain(1, KitArt.B_Concrete, P(W / 2, D), P(-W / 2, D), Back, 0, gf);
                // the colonnade: steps, columns, the entablature with the name
                double cd = Math.Min(2.0, Math.Max(0.9, Room));
                LBox(1, -W / 2 + 2, W / 2 - 2, -cd - 0.6, 0, 0, 0.3, -1, KitArt.S_Portland, KitArt.S_Portland);
                int cols = Math.Max(4, (int)((W - 6) / 3.2));
                for (int i = 0; i <= cols; i++)
                {
                    double s = -W / 2 + 3 + i * (W - 6) / cols;
                    LBox(1, s - 0.35, s + 0.35, -cd, -cd + 0.7, 0.3, gf + fh - 0.9, -1, KitArt.S_Portland, KitArt.S_Portland);
                }
                LBox(1, -W / 2 + 2, W / 2 - 2, -cd - 0.2, 0, gf + fh - 0.9, gf + fh + 0.2, -1, KitArt.S_Portland, KitArt.S_Portland);
                double nw = Math.Min(14, W - 8);
                NameBoard(-nw / 2, nw / 2, -cd - 0.21, gf + fh - 0.8, gf + fh + 0.1);
                Parapet(1, -W / 2, W / 2, 0, D, eave, 1.0, KitArt.B_Concrete, KitArt.B_Concrete, KitArt.S_Portland);
                FlatRoof(1, -W / 2, W / 2, 0, D, eave);
                LBox(1, -2.5, 2.5, D / 2 - 2.5, D / 2 + 2.5, eave, eave + 2.6, -1, KitArt.S_Verdigris, KitArt.S_Verdigris);     // a little green roof lantern
            }
        }

        // ================================================================== dressing: Brixtun's own flavour
        static readonly string[][] BrixtunShops = {
            new[] { "ELECTRIC JERK CENTRE", "0b7a3b", "ffd200" }, new[] { "WINDWARD CARIBBEAN", "f2c200", "0b5c2e" },
            new[] { "BRIXTUN CUTS", "111111", "f2f2f2" }, new[] { "RECORD ROOTS", "8e1f1f", "ffe6a8" },
            new[] { "ATLANTIC FISH & MEAT", "0d4f8b", "ffffff" }, new[] { "YAM & PLANTAIN", "2b6a2b", "fff1b8" },
            new[] { "COLDHARBOR PHONES", "00a3e0", "ffffff" }, new[] { "QUEEN B HAIR & WIGS", "d8338a", "ffffff" },
            new[] { "ACRE LANE BAKERY", "f1e3c6", "6b3a1e" }, new[] { "AFRO-CARIB GROCERS", "e8a317", "1a1a1a" },
            new[] { "SOUND SYSTEM HIFI", "1a1a1a", "39ff88" }, new[] { "PATTY KING", "ffcc00", "b01c1c" },
            new[] { "BRIX MONEY TRANSFER", "ffe600", "1a1a1a" }, new[] { "MARKET CAFE", "6a1b1b", "ffffff" },
            new[] { "LUCKY STAR BETS", "123b8c", "ffcc00" }, new[] { "PARADE PHARMACY", "009a5a", "ffffff" } };
        static readonly string[] BrixtunTags = { "SW2", "BRIX", "ELEC", "WNDRD", "REGAL", "ACRE", "LDN", "RIDDIM", "SKANK", "ZION" };
        static readonly Dictionary<string, string> BrixtunStations = new Dictionary<string, string> { { "Windward Square", "BRIXTUN" } };

        /// The Underground stations to build in this area (park name → board).
        static Dictionary<string, string> StationTable { get { return ExpansionMode ? BrixtunStations : StationNames; } }
        static string[][] AreaShops(string[][] cityList) { return ExpansionMode && cityList == PeckamShops ? BrixtunShops : cityList; }
        static string[] AreaTags { get { return ExpansionMode ? BrixtunTags : Tags; } }
    }
}
