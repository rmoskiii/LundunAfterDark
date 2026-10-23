// L6: the railway over Rye Lane.
// A double-track line comes in on a grassy embankment from the south edge of the map, through the gap between Gilmore and
// Peckam (over the Ring and Old Kent Lane on steel girder bridges), curves east onto a brick viaduct along the river side
// of Peckam, crosses Rye Lane on a graffitied plate-girder bridge, and ends at Rye Lane station: a side platform with a
// canopy, a train waiting at the buffers. The arches underneath are lock-ups, a car repair bay, a café and the station
// entrance. Built from the building kit's atlases (new strips in atlas B), so it costs two materials.
// No random numbers are shared with anything else, and everything it adds comes after the rest of the city.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        /// Set false to build the city without the railway.
        public static bool UseRailway = true;

        // heights (m above the ground)
        public const double RailDeck = 6.3, RailBallast = 6.45, RailSleeper = 6.6, RailHead = 6.75, RailParapet = 7.7;
        const double RailHW = 5.2, RailStationHW = 7.2, RailTrack = 1.9, RailGauge = 0.7175;
        const double ArchSpring = 2.6, ArchRad = 3.2, ArchBand = 3.85, ArchRecess = 0.5;
        const double BankSlope = 1.5, AbutLen = 3.0, WallRun = 20.0, ViaductFoot = 4.0;
        const double GirderY0 = 5.5, GirderY1 = 8.1, DeckSoffit = 5.6, BeamY = 5.42;
        const double PlatformTop = 7.45, PlatformEdge = 3.45, CanopyY = 10.4;

        public class RailSpanInfo { public string road; public double s0, s1; public bool expansion; }

        // E1: the route through Brixtun (see MakeRailPlan)
        const double BrixWestX = 120, BrixWestZ0 = -160, BrixR1 = 80, BrixLineZ = -80, BrixR2 = 60;
        const double BrixViaductZ0 = -380, BrixViaductZ1 = 2;     // the viaduct runs from here on the west side to here on the way north

        static bool InBrixViaduct(double s) { return railPlan.sBrix0 >= 0 && s >= railPlan.sBrix0 && s <= railPlan.sBrix1; }
        public class RailPlan
        {
            public Line line;
            public double length, sViaduct, sStation0;
            public double sBrix0 = -1, sBrix1 = -1;       // E1: the Brixtun viaduct (−1 = none)
            public List<RailSpanInfo> spans = new List<RailSpanInfo>();
            public RailSpanInfo rye;
        }

        static RailPlan railPlan;
        static readonly List<double[]> railLamps = new List<double[]>();   // street lamps the railway stands over: x, z, style

        /// The line as built last time (null without the railway).
        public static RailPlan Railway { get { return railPlan; } }

        // ================================================================== the route
        static RailPlan MakeRailPlan()
        {
            List<V2> p = new List<V2>();
            // E0: the line starts beyond the south edge of the map, wherever that is (−300 for the original city)
            double zStart = Math.Min(-300, Math.Floor((W.worldBounds.wallMinZ - 100) / 10) * 10);
            // E1: its final route through Brixtun: up from the south edge west of the town, a curve east onto a viaduct
            // across Stockwell Road, Brixtun Road and Atlantic Road (where Brixtun station will be), a curve north,
            // and up through the gap between Gilmore and Peckam as before
            List<V2> south = new List<V2>();
            for (double z = zStart; z < BrixWestZ0; z += 10) south.Add(new V2(BrixWestX, z));
            for (int i = 0; i <= 16; i++) { double a = Math.PI - Math.PI / 2 * i / 16; south.Add(new V2(BrixWestX + BrixR1 + BrixR1 * Math.Cos(a), BrixWestZ0 + BrixR1 * Math.Sin(a))); }
            for (double sx = BrixWestX + BrixR1 + 10; sx < 615 - BrixR2 - 0.01; sx += 10) south.Add(new V2(sx, BrixLineZ));
            for (int i = 0; i <= 14; i++) { double a = -Math.PI / 2 + Math.PI / 2 * i / 14; south.Add(new V2(615 - BrixR2 + BrixR2 * Math.Cos(a), BrixLineZ + BrixR2 + BrixR2 * Math.Sin(a))); }
            for (double z = BrixLineZ + BrixR2 + 10; z < 150; z += 10) south.Add(new V2(615, z));               // up through the gap
            p.AddRange(south);
            for (double z = 150; z < 250; z += 4) { double t = (z - 150) / 100; p.Add(new V2(615 + 10 * (1 - Math.Cos(Math.PI * t)), z)); }
            for (double z = 250; z < 466; z += 8) p.Add(new V2(635, z));
            V2 d1 = new V2(1, -0.148).Norm;                                                                   // then east, along the river side
            double cx = 690, cz = 466, rad = 55, th1 = Math.Atan2(d1.x, -d1.z);
            int nArc = (int)Math.Ceiling((Math.PI - th1) * rad / 2.0);
            for (int i = 0; i <= nArc; i++) { double th = Math.PI - (Math.PI - th1) * i / nArc; p.Add(new V2(cx + rad * Math.Cos(th), cz + rad * Math.Sin(th))); }
            double x = p[p.Count - 1].x, zz = p[p.Count - 1].z;
            while (x < 875)
            {
                double slope = x < 806 ? -0.148 : x < 824 ? -0.148 + (-0.31 + 0.148) * (x - 806) / 18 : -0.31;
                x += 2; zz += slope * 2; p.Add(new V2(x, zz));
            }
            RailPlan rp = new RailPlan { line = new Line(p) };
            rp.length = rp.line.Length;
            double lat; rp.sViaduct = RailProject(rp.line, new V2(635, 400), out lat);

            List<string> ids = new List<string>(roads.Keys); ids.Sort();
            // E1: the expansions' roads it crosses get bridges too
            List<RoadInfo> crossed = new List<RoadInfo>();
            foreach (string id in ids)
            {
                string nm = roads[id].r.name;
                if (nm == "Lundun Ring South" || nm == "Old Kent Lane" || nm == "Rye Lane") crossed.Add(roads[id]);
            }
            if (W.expansion != null && W.expansion.roads != null)
                foreach (LwRoad xr in W.expansion.roads)
                    crossed.Add(new RoadInfo { r = xr, line = new Line(Pts(xr.points)), w = xr.width, pave = ClassPave(xr.roadClass), cls = xr.roadClass });
            rp.sBrix0 = RailProject(rp.line, new V2(BrixWestX, BrixViaductZ0), out lat);
            rp.sBrix1 = RailProject(rp.line, new V2(615, BrixViaductZ1), out lat);
            foreach (RoadInfo ri in crossed)
            {
                string nm = ri.r.name;
                for (int i = 0; i + 1 < rp.line.p.Count; i++)
                    for (int j = 0; j + 1 < ri.line.p.Count; j++)
                    {
                        V2 a = rp.line.p[i], b = rp.line.p[i + 1], c = ri.line.p[j], d = ri.line.p[j + 1];
                        V2 r = b - a, q = d - c;
                        double den = r.x * q.z - r.z * q.x;
                        if (Math.Abs(den) < 1e-9) continue;
                        double t = ((c.x - a.x) * q.z - (c.z - a.z) * q.x) / den, u = ((c.x - a.x) * r.z - (c.z - a.z) * r.x) / den;
                        if (t < 0 || t > 1 || u < 0 || u > 1) continue;
                        double sc = rp.line.ch[i] + t * V2.Dist(a, b);
                        double sin = Math.Abs(den) / (r.Len * q.Len);
                        double half = (ri.w / 2 + Math.Max(ri.pave, ri.verge) + 1.2) / Math.Max(0.3, sin);
                        bool dup = false;
                        foreach (RailSpanInfo o in rp.spans) if (o.road == nm && Math.Abs((o.s0 + o.s1) / 2 - sc) < 2) dup = true;
                        if (dup) continue;                                                  // the line passes exactly through a vertex
                        RailSpanInfo sp = new RailSpanInfo { road = nm, s0 = sc - half, s1 = sc + half, expansion = !roads.ContainsKey(ri.r.id) };
                        rp.spans.Add(sp);
                        if (nm == "Rye Lane") rp.rye = sp;
                    }
            }
            rp.spans.Sort((m, n) => m.s0.CompareTo(n.s0));
            rp.sStation0 = rp.rye != null ? rp.rye.s1 + AbutLen : rp.length - 60;
            return rp;
        }

        /// Chainage of the nearest point on the line, and the signed distance from it (positive = left of the direction of travel).
        static double RailProject(Line line, V2 q, out double lateral)
        {
            double best = double.MaxValue, bs = 0; lateral = 0;
            for (int i = 0; i + 1 < line.p.Count; i++)
            {
                V2 a = line.p[i], b = line.p[i + 1], ab = b - a;
                double l2 = V2.Dot(ab, ab), t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, V2.Dot(q - a, ab) / l2));
                V2 h = a + ab * t; double d = V2.Dist(q, h);
                if (d < best) { best = d; bs = line.ch[i] + t * Math.Sqrt(l2); lateral = V2.Dot(q - h, ab.Norm.Left); }
            }
            return bs;
        }

        static bool InRailSpan(double s, double pad)
        {
            foreach (RailSpanInfo sp in railPlan.spans) if (s > sp.s0 - pad && s < sp.s1 + pad) return true;
            return false;
        }

        /// Half width of the structure on one side (+1 left, -1 right): the station is wider on the right.
        static double RailHalf(double s, int side) { return side < 0 && s > railPlan.sStation0 - 0.01 ? RailStationHW : RailHW; }

        /// 'E' embankment, 'W' walled approach, 'A' bridge abutment, 'S' bridge span, 'V' viaduct
        static char RailKind(double s)
        {
            foreach (RailSpanInfo sp in railPlan.spans)
            {
                if (s >= sp.s0 && s <= sp.s1) return 'S';
                if ((s >= sp.s0 - AbutLen && s < sp.s0) || (s > sp.s1 && s <= sp.s1 + AbutLen)) return 'A';
            }
            if (s >= railPlan.sViaduct || InBrixViaduct(s)) return 'V';
            foreach (RailSpanInfo sp in railPlan.spans)
                if ((s >= sp.s0 - AbutLen - WallRun && s < sp.s0) || (s > sp.s1 && s <= sp.s1 + AbutLen + WallRun)) return 'W';
            return 'E';
        }

        /// True if q is inside the railway's footprint (its banks, walls or piers, or under a bridge).
        public static bool RailHides(V2 q)
        {
            if (railPlan == null) return false;
            double lat, s = RailProject(railPlan.line, q, out lat);
            V2 n; V2 h = RP(s, out n);
            if (V2.Dist(q, h) > Math.Abs(lat) + 0.5) return false;           // beyond either end of the line
            char k = RailKind(s);
            double foot = k == 'E' ? RailHW + RailDeck * BankSlope : RailHalf(s, lat > 0 ? 1 : -1);
            return Math.Abs(lat) < foot + 0.8;
        }

        static V2 RP(double s, out V2 n) { V2 d; V2 p = railPlan.line.At(Math.Max(0, Math.Min(railPlan.length, s)), out d); n = d.Left; return p; }
        static V2 RDir(double s) { V2 d; railPlan.line.At(Math.Max(0, Math.Min(railPlan.length, s)), out d); return d; }
        /// A point on the line, offset sideways (positive = left).
        static V2 RAt(double s, double lat) { V2 n; V2 p = RP(s, out n); return p + n * lat; }

        // ================================================================== building it
        static MeshData rA, rB, rBank, rTrack, rStation, rRamp, rLights;

        static void BuildRailway(GreyboxResult R)
        {
            if (railPlan == null) return;
            Random rr = new Random(6161);
            rA = new MeshData("Railway_A", "Railway", KitMatA, true) { navArea = 1 };
            rB = new MeshData("Railway_B", "Railway", KitMatB, true) { navArea = 1 };
            rBank = new MeshData("Railway_Bank", "Railway", "RailBank", true) { navArea = 1 };
            rTrack = new MeshData("Railway_Track", "Railway", KitMatA, false) { navIgnore = true };
            rStation = new MeshData("Railway_Station", "Railway", KitMatB, false) { navIgnore = true };
            rRamp = new MeshData("Railway_Walkways", "Railway", "ColliderOnly", true) { navArea = 1 };       // invisible: smooth stairs, platform edge
            rLights = new MeshData("Railway_StationLights", "Railway", LampHeadMats[3], false) { navIgnore = true };
            RailPlan rp = railPlan;

            // 1. the embankment, walled approaches and abutments, up to the viaduct
            List<double> cuts = new List<double> { 0, rp.sViaduct };
            foreach (RailSpanInfo sp in rp.spans)
                foreach (double c in new[] { sp.s0 - AbutLen - WallRun, sp.s0 - AbutLen, sp.s0, sp.s1, sp.s1 + AbutLen, sp.s1 + AbutLen + WallRun })
                    if (c > 0 && c < rp.length) cuts.Add(c);
            cuts.Add(rp.sViaduct + ViaductFoot);
            if (rp.sBrix0 >= 0) { cuts.Add(rp.sBrix0); cuts.Add(rp.sBrix0 + ViaductFoot); cuts.Add(rp.sBrix1 - ViaductFoot); cuts.Add(rp.sBrix1); }
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double a = cuts[i], b = cuts[i + 1];
                if (b - a < 0.01) continue;
                char k = RailKind((a + b) / 2);
                if (a >= rp.sViaduct + ViaductFoot - 0.01 && k == 'V') continue;      // arches: below
                if (k == 'V' && rp.sBrix0 >= 0 && a >= rp.sBrix0 + ViaductFoot - 0.01 && b <= rp.sBrix1 - ViaductFoot + 0.01) continue;
                if (k == 'E') Embankment(a, b, i > 0 && RailKind(a - 0.5) != 'E', b < rp.length && RailKind(b + 0.5) != 'E');
                else if (k == 'W' || k == 'A' || k == 'V') Solid(a, b, k == 'A' ? SpanFacing(a, b) : 0);
                else if (k == 'S') { }
            }

            // 2. the arches: from the end of the bank to the Rye Lane bridge, then the station to the buffers
            int bay = 0;
            if (rp.rye != null)
            {
                Arches(rp.sViaduct + ViaductFoot, rp.rye.s0 - AbutLen, rr, ref bay, false);
                Arches(rp.rye.s1 + AbutLen, rp.length, rr, ref bay, true);
                // the step where the station widens
                V2 n; V2 c = RP(rp.sStation0, out n);
                BrickWall(rA, c - n * RailHW, c - n * RailStationHW, RDir(rp.sStation0) * -1, -0.05, RailParapet);
            }
            else Arches(rp.sViaduct + ViaductFoot, rp.length, rr, ref bay, false);
            EndWall(rp.length);
            // E1: Brixtun's arches, between its bridges (their own random numbers, after the rest)
            if (rp.sBrix0 >= 0)
            {
                Random rb = new Random(6262);
                double from = rp.sBrix0 + ViaductFoot;
                List<RailSpanInfo> inside = new List<RailSpanInfo>();
                foreach (RailSpanInfo sp in rp.spans) if (sp.s1 > from && sp.s0 < rp.sBrix1 - ViaductFoot) inside.Add(sp);
                foreach (RailSpanInfo sp in inside)
                {
                    if (sp.s0 - AbutLen - from > 2) Arches(from, sp.s0 - AbutLen, rb, ref bay, false);
                    from = sp.s1 + AbutLen;
                }
                if (rp.sBrix1 - ViaductFoot - from > 2) Arches(from, rp.sBrix1 - ViaductFoot, rb, ref bay, false);
            }

            // 3. the bridges
            int bi = 0;
            foreach (RailSpanInfo sp in rp.spans) GirderBridge(sp, bi++);

            // 4. track, station, train
            Track();
            Station();

            // 5. lights hung under the bridges where the street lamps would have been
            Dictionary<int, MeshData> fit = new Dictionary<int, MeshData>();
            foreach (double[] l in railLamps)
            {
                V2 q = new V2(l[0], l[1]); double lat, s = RailProject(rp.line, q, out lat);
                if (!InRailSpan(s, 0) || Math.Abs(lat) > RailHW - 0.6) continue;
                int st = (int)l[2];
                MeshData m;
                if (!fit.TryGetValue(st, out m)) { m = new MeshData("Railway_Lights_" + st, "Railway", LampHeadMats[st], false) { navIgnore = true }; fit[st] = m; }
                RBox(m, -1, q, RDir(s), 0.35, 0.18, BeamY - 0.14, BeamY, true);
            }

            foreach (MeshData m in new[] { rA, rB, rBank, rTrack, rStation, rRamp, rLights }) R.meshes.Add(m);
            List<int> fk = new List<int>(fit.Keys); fk.Sort();
            foreach (int k in fk) R.meshes.Add(fit[k]);
            R.log.Add(string.Format("railway: {0:0} m of line, {1} arches, {2} bridges, Rye Lane station", rp.length, bay, rp.spans.Count));
        }

        /// +1 if the abutment between a and b faces a span ahead of it, -1 if behind, 0 if neither.
        static int SpanFacing(double a, double b)
        {
            foreach (RailSpanInfo sp in railPlan.spans)
            {
                if (Math.Abs(b - sp.s0) < 0.01) return 1;
                if (Math.Abs(a - sp.s1) < 0.01) return -1;
            }
            return 0;
        }

        // ------------------------------------------------------------------ embankment
        static void Embankment(double a, double b, bool closeStart, bool closeEnd)
        {
            double toe = RailHW + RailDeck * BankSlope;
            int n = Math.Max(1, (int)Math.Ceiling((b - a) / 4));
            for (int i = 0; i < n; i++)
            {
                double s0 = a + (b - a) * i / n, s1 = a + (b - a) * (i + 1) / n;
                foreach (int side in new[] { 1, -1 })
                {
                    V2 c0 = RAt(s0, side * RailHW), c1 = RAt(s1, side * RailHW), t0 = RAt(s0, side * toe), t1 = RAt(s1, side * toe);
                    V2 o; RP((s0 + s1) / 2, out o); o = o * side;
                    rBank.Quad(new P3(c0, RailDeck, s0 * 0.25, 1.6), new P3(c1, RailDeck, s1 * 0.25, 1.6), new P3(t1, -0.05, s1 * 0.25, 0), new P3(t0, -0.05, s0 * 0.25, 0),
                               o.x * RailDeck, toe - RailHW, o.z * RailDeck);
                }
                Deck(s0, s1, RailHW, RailHW);
            }
            foreach (int side in new[] { 1, -1 })
            {
                if (closeStart) BankEnd(a, side, -1);
                if (closeEnd) BankEnd(b, side, 1);
            }
        }

        /// Closes the side of the bank where it meets a wall: a triangle facing along the line.
        static void BankEnd(double s, int side, int facing)
        {
            double toe = RailHW + RailDeck * BankSlope;
            V2 d = RDir(s) * facing;
            rBank.Tri(new P3(RAt(s, side * RailHW), RailDeck, 0, 1.6), new P3(RAt(s, side * RailHW), -0.05, 0, 0), new P3(RAt(s, side * toe), -0.05, 2.4, 0), d.x, 0, d.z);
        }

        // ------------------------------------------------------------------ solid brick sections (walled approaches, abutments, the viaduct's end on the bank)
        static void Solid(double a, double b, int spanFacing)
        {
            int n = Math.Max(1, (int)Math.Ceiling((b - a) / 8));
            for (int i = 0; i < n; i++)
            {
                double s0 = a + (b - a) * i / n, s1 = a + (b - a) * (i + 1) / n;
                foreach (int side in new[] { 1, -1 })
                {
                    double hw = RailHalf((s0 + s1) / 2, side);
                    V2 o; RP((s0 + s1) / 2, out o); o = o * side;
                    BrickWall(rA, RAt(s0, side * hw), RAt(s1, side * hw), o, -0.05, RailParapet);
                    Parapet(s0, s1, side, hw);
                }
                Deck(s0, s1, RailHalf(s0, 1), RailHalf(s0, -1));
            }
            if (spanFacing != 0)
            {
                double s = spanFacing > 0 ? b : a;
                V2 d = RDir(s) * spanFacing;
                BrickWall(rA, RAt(s, RailHW), RAt(s, -RailHW), d, -0.05, DeckSoffit);
                // above the deck, the bit of wall outside the girders
                BrickWall(rA, RAt(s, RailHW), RAt(s, 4.95), d, DeckSoffit, RailParapet);
                BrickWall(rA, RAt(s, -4.95), RAt(s, -RailHW), d, DeckSoffit, RailParapet);
            }
        }

        static void Parapet(double s0, double s1, int side, double hw)
        {
            V2 o; RP((s0 + s1) / 2, out o); o = o * side;
            V2 i0 = RAt(s0, side * (hw - 0.35)), i1 = RAt(s1, side * (hw - 0.35)), e0 = RAt(s0, side * hw), e1 = RAt(s1, side * hw);
            SwQuad(rA, KitArt.S_YelBrick, new P3(i0, RailDeck, 0, 0), new P3(i1, RailDeck, 0, 0), new P3(i1, RailParapet, 0, 0), new P3(i0, RailParapet, 0, 0), -o.x, 0, -o.z);
            SwQuad(rA, KitArt.S_Portland, new P3(i0, RailParapet, 0, 0), new P3(i1, RailParapet, 0, 0), new P3(e1, RailParapet, 0, 0), new P3(e0, RailParapet, 0, 0), 0, 1, 0);
        }

        static void Deck(double s0, double s1, double hl, double hr)
        {
            SwQuad(rA, KitArt.S_DarkGrey, new P3(RAt(s0, hl), RailDeck, 0, 0), new P3(RAt(s1, hl), RailDeck, 0, 0), new P3(RAt(s1, -hr), RailDeck, 0, 0), new P3(RAt(s0, -hr), RailDeck, 0, 0), 0, 1, 0);
        }

        static void EndWall(double s)
        {
            V2 d = RDir(s);
            BrickWall(rA, RAt(s, RailHalf(s, 1)), RAt(s, -RailHalf(s, -1)), d, -0.05, RailParapet);
            SwQuad(rA, KitArt.S_Portland, new P3(RAt(s, RailHalf(s, 1)), RailParapet, 0, 0), new P3(RAt(s, -RailHalf(s, -1)), RailParapet, 0, 0),
                   new P3(RAt(s - 0.35, -RailHalf(s, -1)), RailParapet, 0, 0), new P3(RAt(s - 0.35, RailHalf(s, 1)), RailParapet, 0, 0), 0, 1, 0);
            SwQuad(rA, KitArt.S_YelBrick, new P3(RAt(s - 0.35, RailHalf(s, 1)), RailDeck, 0, 0), new P3(RAt(s - 0.35, -RailHalf(s, -1)), RailDeck, 0, 0),
                   new P3(RAt(s - 0.35, -RailHalf(s, -1)), RailParapet, 0, 0), new P3(RAt(s - 0.35, RailHalf(s, 1)), RailParapet, 0, 0), -d.x, 0, -d.z);
        }

        // ------------------------------------------------------------------ the arches
        static void Arches(double a, double b, Random rr, ref int count, bool station)
        {
            int n = Math.Max(1, (int)Math.Round((b - a) / 8.0));
            for (int i = 0; i < n; i++)
            {
                double s0 = a + (b - a) * i / n, s1 = a + (b - a) * (i + 1) / n;
                bool passage = station && i == 0;          // the station's way in: open right through the viaduct
                foreach (int side in new[] { 1, -1 })
                {
                    double hw = RailHalf((s0 + s1) / 2, side);
                    V2 o; RP((s0 + s1) / 2, out o); o = o * side;
                    // left = the river / road side, right = the backs of the Peckam houses
                    double k = rr.NextDouble();
                    int unit = side > 0 ? (k < 0.42 ? 0 : k < 0.7 ? 1 : 2) : (k < 0.72 ? 0 : 1);
                    ArchBay(RAt(s0, side * hw), RAt(s1, side * hw), o, unit, passage);
                    Parapet(s0, s1, side, hw);
                }
                Deck(s0, s1, RailHalf(s0 + 0.1, 1), RailHalf(s0 + 0.1, -1));
                if (passage) Passage(s0, s1);
                count++;
            }
        }

        /// One bay of the viaduct's side: piers, the arch, the spandrels up to the parapet, and the arch's infill set back
        /// in the opening. pa → pb along the line; o points out of the wall. Texture: the arch strips, unit = what's built in.
        static void ArchBay(V2 pa, V2 pb, V2 o, int unit, bool open = false)
        {
            bool mirror = V2.Dot(pb - pa, o.Left) < 0;
            const int N = 16;
            double[] ax = new double[N + 1], ay = new double[N + 1];
            for (int i = 0; i <= N; i++) { double th = Math.PI - Math.PI * i / N; ax[i] = 4 + ArchRad * Math.Cos(th); ay[i] = ArchSpring + ArchRad * Math.Sin(th); }
            double x0 = 4 - ArchRad, x1 = 4 + ArchRad, top = RailParapet;
            // face
            ArchPoly(pa, pb, o, unit, mirror, 0, Rect(0, x0, 0, ArchSpring));
            ArchPoly(pa, pb, o, unit, mirror, 0, Rect(x1, 8, 0, ArchSpring));
            ArchPoly(pa, pb, o, unit, mirror, 0, Rect(0, x0, ArchSpring, top));
            ArchPoly(pa, pb, o, unit, mirror, 0, Rect(x1, 8, ArchSpring, top));
            for (int i = 0; i < N; i++)
                ArchPoly(pa, pb, o, unit, mirror, 0, new List<double[]> { new[] { ax[i], ay[i] }, new[] { ax[i + 1], ay[i + 1] }, new[] { ax[i + 1], top }, new[] { ax[i], top } });
            if (open) return;                                                                  // a passage: Passage() does the inside
            // infill, set back
            ArchPoly(pa, pb, o, unit, mirror, ArchRecess, Rect(x0, x1, 0, ArchSpring));
            for (int i = 0; i < N; i++)
                ArchPoly(pa, pb, o, unit, mirror, ArchRecess, new List<double[]> { new[] { 4.0, ArchSpring }, new[] { ax[i], ay[i] }, new[] { ax[i + 1], ay[i + 1] } });
            // reveals: the pier sides and the underside of the arch
            V2 along = (pb - pa).Norm, back = o * -ArchRecess;
            Func<double, V2> Wp = t => pa + (pb - pa) * (t / 8.0);
            SwQuad(rB, KitArt.S_BrownBrick, new P3(Wp(x0), 0, 0, 0), new P3(Wp(x0) + back, 0, 0, 0), new P3(Wp(x0) + back, ArchSpring, 0, 0), new P3(Wp(x0), ArchSpring, 0, 0), along.x, 0, along.z);
            SwQuad(rB, KitArt.S_BrownBrick, new P3(Wp(x1), 0, 0, 0), new P3(Wp(x1) + back, 0, 0, 0), new P3(Wp(x1) + back, ArchSpring, 0, 0), new P3(Wp(x1), ArchSpring, 0, 0), -along.x, 0, -along.z);
            for (int i = 0; i < N; i++)
            {
                double mx = (ax[i] + ax[i + 1]) / 2 - 4, my = (ay[i] + ay[i + 1]) / 2 - ArchSpring, l = Math.Sqrt(mx * mx + my * my);
                V2 nh = along * (-mx / l);
                SwQuad(rB, KitArt.S_BrownBrick, new P3(Wp(ax[i]), ay[i], 0, 0), new P3(Wp(ax[i + 1]), ay[i + 1], 0, 0), new P3(Wp(ax[i + 1]) + back, ay[i + 1], 0, 0), new P3(Wp(ax[i]) + back, ay[i], 0, 0),
                       nh.x, -my / l, nh.z);
            }
        }

        static List<double[]> Rect(double x0, double x1, double y0, double y1)
        { return new List<double[]> { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x1, y1 }, new[] { x0, y1 } }; }

        /// A convex polygon in bay coordinates (x 0–8 along, y up), cut at the strip boundary and mapped to the arch strips.
        static void ArchPoly(V2 pa, V2 pb, V2 o, int unit, bool mirror, double setBack, List<double[]> poly)
        {
            for (int band = 0; band < 2; band++)
            {
                double y0 = band == 0 ? -1 : ArchBand, y1 = band == 0 ? ArchBand : 99;
                List<double[]> c = ClipY(ClipY(poly, y0, true), y1, false);
                if (c.Count < 3) continue;
                int row = band == 0 ? KitArt.B_RailArchLo : KitArt.B_RailArchHi;
                double off = band == 0 ? 0 : ArchBand;
                Func<double[], P3> P = q =>
                {
                    double tx = mirror ? 8 - q[0] : q[0];
                    V2 w = pa + (pb - pa) * (q[0] / 8.0) - o * setBack;
                    return new P3(w, q[1], (unit * 8 + tx) / KitArt.RepeatM, KitArt.V(row, (Math.Max(0, q[1]) - off) / ArchBand));
                };
                P3 p0 = P(c[0]);
                for (int i = 1; i + 1 < c.Count; i++) rB.Tri(p0, P(c[i]), P(c[i + 1]), o.x, 0, o.z);
            }
        }

        /// Sutherland–Hodgman against a horizontal line: keep y ≥ lim (above) or y ≤ lim.
        static List<double[]> ClipY(List<double[]> poly, double lim, bool above)
        {
            List<double[]> r = new List<double[]>();
            for (int i = 0; i < poly.Count; i++)
            {
                double[] a = poly[i], b = poly[(i + 1) % poly.Count];
                bool ain = above ? a[1] >= lim : a[1] <= lim, bin = above ? b[1] >= lim : b[1] <= lim;
                if (ain) r.Add(a);
                if (ain != bin) { double t = (lim - a[1]) / (b[1] - a[1]); r.Add(new[] { a[0] + (b[0] - a[0]) * t, lim }); }
            }
            return r;
        }

        // ------------------------------------------------------------------ the bridges
        static void GirderBridge(RailSpanInfo sp, int index)
        {
            double a = sp.s0 - 0.8, b = sp.s1 + 0.8;
            V2 d = RDir((sp.s0 + sp.s1) / 2);
            double uStart = index * 11.0;
            foreach (int side in new[] { 1, -1 })
            {
                V2 o; RP((a + b) / 2, out o); o = o * side;
                V2 oa = RAt(a, side * 5.0), ob = RAt(b, side * 5.0), ia = RAt(a, side * 4.5), ib = RAt(b, side * 4.5);
                FaceUV(rB, KitArt.B_Girder, oa, ob, o, GirderY0, GirderY1, uStart, V2.Dist(oa, ob), 0, 1, false);
                SwQuad(rB, KitArt.S_Green, new P3(ia, GirderY0, 0, 0), new P3(ib, GirderY0, 0, 0), new P3(ib, GirderY1, 0, 0), new P3(ia, GirderY1, 0, 0), -o.x, 0, -o.z);
                SwQuad(rB, KitArt.S_Green, new P3(ia, GirderY1, 0, 0), new P3(ib, GirderY1, 0, 0), new P3(ob, GirderY1, 0, 0), new P3(oa, GirderY1, 0, 0), 0, 1, 0);
                SwQuad(rB, KitArt.S_Green, new P3(ia, GirderY0, 0, 0), new P3(ib, GirderY0, 0, 0), new P3(ob, GirderY0, 0, 0), new P3(oa, GirderY0, 0, 0), 0, -1, 0);
                SwQuad(rB, KitArt.S_Green, new P3(ia, GirderY0, 0, 0), new P3(oa, GirderY0, 0, 0), new P3(oa, GirderY1, 0, 0), new P3(ia, GirderY1, 0, 0), -d.x, 0, -d.z);
                SwQuad(rB, KitArt.S_Green, new P3(ib, GirderY0, 0, 0), new P3(ob, GirderY0, 0, 0), new P3(ob, GirderY1, 0, 0), new P3(ib, GirderY1, 0, 0), d.x, 0, d.z);
                // top flange lip
                RBoxAlong(rB, KitArt.S_Green, a, b, side * 4.75, 0.38, GirderY1, GirderY1 + 0.06);
            }
            // the deck between the girders, and cross girders under it
            SwQuad(rB, KitArt.S_DarkGrey, new P3(RAt(a, 4.5), DeckSoffit, 0, 0), new P3(RAt(b, 4.5), DeckSoffit, 0, 0), new P3(RAt(b, -4.5), DeckSoffit, 0, 0), new P3(RAt(a, -4.5), DeckSoffit, 0, 0), 0, -1, 0);
            Deck(a, b, 4.5, 4.5);
            for (double s = sp.s0 + 0.6; s < sp.s1 - 0.3; s += 1.8)
                RBox(rB, KitArt.S_Green, RAt(s, 0), RDirLeft(s), 4.5, 0.12, BeamY, DeckSoffit, true);
        }

        static V2 RDirLeft(double s) { return RDir(s).Left; }

        // ------------------------------------------------------------------ track
        static void Track()
        {
            RailPlan rp = railPlan;
            double end = rp.length - 3.0;
            // ballast
            for (double s = 0; s < end; s += 4)
            {
                double s1 = Math.Min(end + 0.5, s + 4);
                double hr = s >= rp.sStation0 ? PlatformEdge : 3.9;
                SwQuad(rTrack, KitArt.S_MidGrey, new P3(RAt(s, 3.9), RailBallast, 0, 0), new P3(RAt(s1, 3.9), RailBallast, 0, 0), new P3(RAt(s1, -hr), RailBallast, 0, 0), new P3(RAt(s, -hr), RailBallast, 0, 0), 0, 1, 0);
                V2 o; RP(s, out o);
                SwQuad(rTrack, KitArt.S_MidGrey, new P3(RAt(s, 3.9), RailDeck, 0, 0), new P3(RAt(s1, 3.9), RailDeck, 0, 0), new P3(RAt(s1, 3.9), RailBallast, 0, 0), new P3(RAt(s, 3.9), RailBallast, 0, 0), o.x, 0, o.z);
                if (hr > 3.8)
                    SwQuad(rTrack, KitArt.S_MidGrey, new P3(RAt(s, -3.9), RailDeck, 0, 0), new P3(RAt(s1, -3.9), RailDeck, 0, 0), new P3(RAt(s1, -3.9), RailBallast, 0, 0), new P3(RAt(s, -3.9), RailBallast, 0, 0), -o.x, 0, -o.z);
            }
            // sleepers
            for (double s = 0.5; s < end; s += 1.2)
                foreach (double t in new[] { RailTrack, -RailTrack })
                    RBox(rTrack, KitArt.S_Concrete, RAt(s, t), RDirLeft(s), 1.3, 0.13, RailBallast, RailSleeper, false);
            // rails
            for (double s = 0; s < end; s += 4)
            {
                double s1 = Math.Min(end, s + 4);
                foreach (double t in new[] { RailTrack, -RailTrack })
                    foreach (double g in new[] { RailGauge, -RailGauge })
                    {
                        double c = t + g;
                        V2 a0 = RAt(s, c - 0.035), a1 = RAt(s1, c - 0.035), b0 = RAt(s, c + 0.035), b1 = RAt(s1, c + 0.035);
                        V2 o; RP(s, out o);
                        SwQuad(rTrack, KitArt.S_Galv, new P3(a0, RailHead, 0, 0), new P3(a1, RailHead, 0, 0), new P3(b1, RailHead, 0, 0), new P3(b0, RailHead, 0, 0), 0, 1, 0);
                        SwQuad(rTrack, KitArt.S_Rust, new P3(b0, RailSleeper, 0, 0), new P3(b1, RailSleeper, 0, 0), new P3(b1, RailHead, 0, 0), new P3(b0, RailHead, 0, 0), o.x, 0, o.z);
                        SwQuad(rTrack, KitArt.S_Rust, new P3(a0, RailSleeper, 0, 0), new P3(a1, RailSleeper, 0, 0), new P3(a1, RailHead, 0, 0), new P3(a0, RailHead, 0, 0), -o.x, 0, -o.z);
                    }
            }
            // buffer stops
            foreach (double t in new[] { RailTrack, -RailTrack })
            {
                RBox(rTrack, KitArt.S_Red, RAt(end, t), RDirLeft(end), 1.0, 0.18, RailHead + 0.15, RailHead + 0.75, true);
                RBox(rTrack, KitArt.S_DarkGrey, RAt(end + 0.8, t), RDirLeft(end), 0.8, 0.6, RailBallast, RailHead + 0.6, false);
            }
        }

        // ------------------------------------------------------------------ the station
        static void Station()
        {
            RailPlan rp = railPlan;
            if (rp.rye == null) return;
            double p0 = rp.sStation0 + 1, p1 = rp.length - 1;
            double inner = RailStationHW - 0.35;
            // platform
            for (double s = p0; s < p1; s += 4)
            {
                double s1 = Math.Min(p1, s + 4);
                V2 o; RP(s, out o);
                SwQuad(rA, KitArt.S_PaleConc, new P3(RAt(s, -PlatformEdge - 0.4), PlatformTop, 0, 0), new P3(RAt(s1, -PlatformEdge - 0.4), PlatformTop, 0, 0), new P3(RAt(s1, -inner), PlatformTop, 0, 0), new P3(RAt(s, -inner), PlatformTop, 0, 0), 0, 1, 0);
                SwQuad(rA, KitArt.S_White, new P3(RAt(s, -PlatformEdge), PlatformTop + 0.01, 0, 0), new P3(RAt(s1, -PlatformEdge), PlatformTop + 0.01, 0, 0), new P3(RAt(s1, -PlatformEdge - 0.4), PlatformTop + 0.01, 0, 0), new P3(RAt(s, -PlatformEdge - 0.4), PlatformTop + 0.01, 0, 0), 0, 1, 0);
                SwQuad(rA, KitArt.S_Yellow, new P3(RAt(s, -PlatformEdge - 0.75), PlatformTop + 0.012, 0, 0), new P3(RAt(s1, -PlatformEdge - 0.75), PlatformTop + 0.012, 0, 0), new P3(RAt(s1, -PlatformEdge - 0.85), PlatformTop + 0.012, 0, 0), new P3(RAt(s, -PlatformEdge - 0.85), PlatformTop + 0.012, 0, 0), 0, 1, 0);
                SwQuad(rA, KitArt.S_DarkGrey, new P3(RAt(s, -PlatformEdge), RailBallast, 0, 0), new P3(RAt(s1, -PlatformEdge), RailBallast, 0, 0), new P3(RAt(s1, -PlatformEdge), PlatformTop, 0, 0), new P3(RAt(s, -PlatformEdge), PlatformTop, 0, 0), o.x, 0, o.z);
            }
            V2 d0 = RDir(p0), d1 = RDir(p1);
            SwQuad(rA, KitArt.S_DarkGrey, new P3(RAt(p0, -PlatformEdge), RailBallast, 0, 0), new P3(RAt(p0, -inner), RailBallast, 0, 0), new P3(RAt(p0, -inner), PlatformTop, 0, 0), new P3(RAt(p0, -PlatformEdge), PlatformTop, 0, 0), -d0.x, 0, -d0.z);
            SwQuad(rA, KitArt.S_DarkGrey, new P3(RAt(p1, -PlatformEdge), RailBallast, 0, 0), new P3(RAt(p1, -inner), RailBallast, 0, 0), new P3(RAt(p1, -inner), PlatformTop, 0, 0), new P3(RAt(p1, -PlatformEdge), PlatformTop, 0, 0), d1.x, 0, d1.z);

            // canopy: green columns, a roof, a timber valance, strip lights
            double c0 = p0 + 8, c1 = p1 - 3;
            MeshData lights = rLights;
            for (double s = c0; s <= c1 + 0.01; s += 6)
            {
                RBox(rStation, KitArt.S_Green, RAt(s, -5.4), RDir(s), 0.1, 0.1, PlatformTop, CanopyY, false);
                RBox(lights, -1, RAt(s, -4.9), RDir(s), 0.7, 0.06, CanopyY - 0.12, CanopyY - 0.05, true);
            }
            for (double s = c0 - 1.5; s < c1 + 1.5; s += 4)
            {
                double s1 = Math.Min(c1 + 1.5, s + 4);
                V2 o; RP(s, out o);
                SwQuad(rStation, KitArt.S_LightGrey, new P3(RAt(s, -3.25), CanopyY + 0.2, 0, 0), new P3(RAt(s1, -3.25), CanopyY + 0.2, 0, 0), new P3(RAt(s1, -7.0), CanopyY + 0.2, 0, 0), new P3(RAt(s, -7.0), CanopyY + 0.2, 0, 0), 0, 1, 0);
                SwQuad(rStation, KitArt.S_White, new P3(RAt(s, -3.25), CanopyY, 0, 0), new P3(RAt(s1, -3.25), CanopyY, 0, 0), new P3(RAt(s1, -7.0), CanopyY, 0, 0), new P3(RAt(s, -7.0), CanopyY, 0, 0), 0, -1, 0);
                FaceUV(rStation, KitArt.B_Station, RAt(s, -3.25), RAt(s1, -3.25), o, CanopyY - 0.75, CanopyY + 0.3, 2 * 6.4, s1 - s, 0, 1, false, 6.4);
                SwQuad(rStation, KitArt.S_Green, new P3(RAt(s, -7.0), CanopyY - 0.3, 0, 0), new P3(RAt(s1, -7.0), CanopyY - 0.3, 0, 0), new P3(RAt(s1, -7.0), CanopyY + 0.2, 0, 0), new P3(RAt(s, -7.0), CanopyY + 0.2, 0, 0), -o.x, 0, -o.z);
            }
            // name boards facing the track
            int nb = 0;
            for (double s = p0 + 4; s < p1 - 2; s += 14, nb++)
            {
                V2 o; RP(s, out o);
                int unit = nb == 0 ? 1 : nb == 2 ? 4 : 0;
                V2 a = RAt(s - 1.2, -6.5), b = RAt(s + 1.2, -6.5);
                RBox(rStation, KitArt.S_Navy, RAt(s - 1.15, -6.55), RDir(s), 0.04, 0.04, PlatformTop, 9.2, false);
                RBox(rStation, KitArt.S_Navy, RAt(s + 1.15, -6.55), RDir(s), 0.04, 0.04, PlatformTop, 9.2, false);
                FaceUV(rStation, KitArt.B_Station, a, b, o, 8.75, 9.2, unit * 6.4, 6.4, 0, 1, false);
                SwQuad(rStation, KitArt.S_Navy, new P3(RAt(s - 1.2, -6.56), 8.75, 0, 0), new P3(RAt(s + 1.2, -6.56), 8.75, 0, 0), new P3(RAt(s + 1.2, -6.56), 9.2, 0, 0), new P3(RAt(s - 1.2, -6.56), 9.2, 0, 0), -o.x, 0, -o.z);
            }
            StationStairs();
        }

        // ------------------------------------------------------------------ the way in: through an arch, then up the stairs
        /// The first station bay is open right through: a whitewashed passage under the tracks with lights in the vault,
        /// RYE LANE over the road side, PLATFORM 1 over the back.
        static void Passage(double s0, double s1)
        {
            double hr = RailHalf(s0 + 0.1, -1);
            V2 nA = RAt(s0, RailHW), nB = RAt(s1, RailHW), sA = RAt(s0, -hr), sB = RAt(s1, -hr);
            Func<double, V2> Wn = t => nA + (nB - nA) * (t / 8.0), Ws = t => sA + (sB - sA) * (t / 8.0);
            double x0 = 4 - ArchRad, x1 = 4 + ArchRad;
            V2 along = (nB - nA).Norm, on; RP((s0 + s1) / 2, out on);
            BrickWall(rA, Wn(x0), Ws(x0), along, -0.05, ArchSpring);
            BrickWall(rA, Wn(x1), Ws(x1), along * -1, -0.05, ArchSpring);
            const int N = 16;
            for (int i = 0; i < N; i++)
            {
                double t0 = Math.PI - Math.PI * i / N, t1 = Math.PI - Math.PI * (i + 1) / N;
                double ax0 = 4 + ArchRad * Math.Cos(t0), ay0 = ArchSpring + ArchRad * Math.Sin(t0), ax1 = 4 + ArchRad * Math.Cos(t1), ay1 = ArchSpring + ArchRad * Math.Sin(t1);
                double mx = (Math.Cos(t0) + Math.Cos(t1)) / 2, my = (Math.Sin(t0) + Math.Sin(t1)) / 2;
                V2 nh = along * -mx;
                SwQuad(rA, KitArt.S_White, new P3(Wn(ax0), ay0, 0, 0), new P3(Wn(ax1), ay1, 0, 0), new P3(Ws(ax1), ay1, 0, 0), new P3(Ws(ax0), ay0, 0, 0), nh.x, -my, nh.z);
            }
            SwQuad(rA, KitArt.S_PaleConc, new P3(Wn(x0), 0.03, 0, 0), new P3(Wn(x1), 0.03, 0, 0), new P3(Ws(x1), 0.03, 0, 0), new P3(Ws(x0), 0.03, 0, 0), 0, 1, 0);
            for (int k = 0; k < 3; k++)
            {
                V2 c = (Wn(4) + (Ws(4) - Wn(4)) * ((k + 0.5) / 3.0));
                RBox(rLights, -1, c, along, 0.6, 0.08, ArchSpring + ArchRad - 0.18, ArchSpring + ArchRad - 0.08, true);
            }
            // name boards hung in the arch mouths
            foreach (int side in new[] { 1, -1 })
            {
                Func<double, V2> Wf = side > 0 ? Wn : Ws;
                V2 o = on * side;
                V2 a = Wf(1.25) - o * 0.15, b = Wf(6.75) - o * 0.15;
                FaceUV(rStation, KitArt.B_Station, a, b, o, 2.9, 3.95, (side > 0 ? 0 : 4) * 6.4, 6.4, 0, 1, false);
                SwQuad(rStation, KitArt.S_Navy, new P3(a - o * 0.03, 2.9, 0, 0), new P3(b - o * 0.03, 2.9, 0, 0), new P3(b - o * 0.03, 3.95, 0, 0), new P3(a - o * 0.03, 3.95, 0, 0), -o.x, 0, -o.z);
            }
        }

        /// Up from the back of the passage to the platform: two flights of 22 steps (17.5 cm by 28 cm) with a landing,
        /// built against the viaduct in brick, to a landing that steps down onto the platform over the parapet.
        /// The steps are for looks; a smooth invisible ramp under them is what you walk on.
        public static double StairFoot { get { return railPlan == null ? 0 : railPlan.sStation0 + 21.0; } }
        const double StepRise = 0.175, StepGo = 0.28, StairIn = RailStationHW, StairOut = RailStationHW + 2.25, StairWall = 0.25;
        const int StepsPerFlight = 22;

        static void StationStairs()
        {
            double f0 = StairFoot, run = StepsPerFlight * StepGo, h1 = StepsPerFlight * StepRise;     // 6.16 m, 3.85 m
            double l0 = f0 + run, l1 = l0 + 1.6, f2 = l1 + run, t1 = f2 + 2.6, top = RailParapet;
            double rail = 1.1;
            // steps
            for (int fl = 0; fl < 2; fl++)
                for (int k = 1; k <= StepsPerFlight; k++)
                {
                    double sa = (fl == 0 ? f0 : l1) + (k - 1) * StepGo, y = (fl == 0 ? 0 : h1) + k * StepRise;
                    RBox(rStation, KitArt.S_PaleConc, RAt(sa + StepGo / 2, -(StairIn + StairOut) / 2), RDirLeft(sa), (StairOut - StairIn) / 2, StepGo / 2 + 0.005, y - StepRise - 0.01, y, false);
                }
            // landings
            FlatSlab(rA, l0, l1, StairIn, StairOut, h1);
            FlatSlab(rA, f2, t1, RailStationHW - 0.35, StairOut, top);
            // the outer wall, its top following the stairs (a handrail height above the nosings)
            double[] ss = { f0, l0, l1, f2, t1 }, yy = { rail, h1 + rail, h1 + rail, top + rail, top + rail };
            for (int i = 0; i + 1 < ss.Length; i++)
            {
                foreach (double lat in new[] { StairOut, StairOut + StairWall })
                {
                    V2 o; RP((ss[i] + ss[i + 1]) / 2, out o); o = o * (lat > StairOut ? -1 : 1);
                    BrickPoly(rA, RAt(ss[i], -lat), RAt(ss[i + 1], -lat), o, yy[i], yy[i + 1]);
                }
                SwQuad(rA, KitArt.S_Portland, new P3(RAt(ss[i], -StairOut), yy[i], 0, 0), new P3(RAt(ss[i + 1], -StairOut), yy[i + 1], 0, 0),
                       new P3(RAt(ss[i + 1], -StairOut - StairWall), yy[i + 1], 0, 0), new P3(RAt(ss[i], -StairOut - StairWall), yy[i], 0, 0), 0, 1, 0);
            }
            // ends: a low cheek at the foot, the full-height wall at the top
            V2 d0 = RDir(f0) * -1, d1 = RDir(t1);
            BrickWall(rA, RAt(f0, -StairOut), RAt(f0, -StairOut - StairWall), d0, -0.05, rail);
            BrickWall(rA, RAt(t1, -RailStationHW), RAt(t1, -StairOut - StairWall), d1, -0.05, top + rail);
            // what you actually walk on: two smooth ramps and the landings
            Ramp(f0, l0, 0, h1, StairIn - 0.05, StairOut);
            Ramp(l0, l1, h1, h1, StairIn - 0.05, StairOut);
            Ramp(l1, f2, h1, top, StairIn - 0.05, StairOut);
            Ramp(f2, t1, top, top, RailStationHW - 0.35, StairOut);
            // a light over each landing
            RBox(rLights, -1, RAt((l0 + l1) / 2, -StairIn - 0.12), RDir(l0), 0.3, 0.06, h1 + 2.3, h1 + 2.4, true);
            RBox(rLights, -1, RAt((f2 + t1) / 2, -RailStationHW + 0.1), RDir(f2), 0.3, 0.06, top + 2.3, top + 2.4, true);
            // the back of the platform: an invisible wall behind the railing, open where the stairs come up
            double p0 = railPlan.sStation0 + 1, p1 = railPlan.length - 1;
            foreach (double[] iv in new[] { new[] { p0, f2 - 0.1 }, new[] { t1 + 0.1, p1 } })
                for (double s = iv[0]; s < iv[1] - 0.01; s += 4)
                    RBoxAlong(rRamp, -1, s, Math.Min(iv[1], s + 4), -(RailStationHW - 0.18), 0.12, PlatformTop, PlatformTop + 1.5);
        }

        /// Opening in the station railing for the stairs (Dress leaves this gap).
        static bool StairGap(double s) { double f2 = StairFoot + 2 * StepsPerFlight * StepGo + 1.6; return s > f2 - 0.2 && s < f2 + 2.7; }

        static void FlatSlab(MeshData m, double a, double b, double latIn, double latOut, double y)
        {
            SwQuad(m, KitArt.S_PaleConc, new P3(RAt(a, -latIn), y, 0, 0), new P3(RAt(b, -latIn), y, 0, 0), new P3(RAt(b, -latOut), y, 0, 0), new P3(RAt(a, -latOut), y, 0, 0), 0, 1, 0);
        }

        /// A closed invisible wedge from a to b (heights ya → yb) between two laterals on the right of the line.
        static void Ramp(double a, double b, double ya, double yb, double latIn, double latOut)
        {
            V2 ia = RAt(a, -latIn), ib = RAt(b, -latIn), oa = RAt(a, -latOut), ob = RAt(b, -latOut);
            V2 d = RDir(a), n = d.Left;
            double rise = yb - ya, len = Math.Max(0.01, b - a);
            rRamp.Quad(new P3(ia, ya), new P3(ib, yb), new P3(ob, yb), new P3(oa, ya), -d.x * rise / len, 1, -d.z * rise / len);
            rRamp.Quad(new P3(ia, -0.1), new P3(ib, -0.1), new P3(ib, yb), new P3(ia, ya), n.x, 0, n.z);
            rRamp.Quad(new P3(oa, -0.1), new P3(ob, -0.1), new P3(ob, yb), new P3(oa, ya), -n.x, 0, -n.z);
            rRamp.Quad(new P3(ia, -0.1), new P3(oa, -0.1), new P3(oa, ya), new P3(ia, ya), -d.x, 0, -d.z);
            rRamp.Quad(new P3(ib, -0.1), new P3(ob, -0.1), new P3(ob, yb), new P3(ib, yb), d.x, 0, d.z);
        }

        /// Yellow-stock brick from the ground up to a sloping top (ya at a, yb at b), cut into storeys like BrickWall.
        static void BrickPoly(MeshData m, V2 a, V2 b, V2 o, double ya, double yb)
        {
            int row = KitArt.A_BrickYel;
            double fh = KitArt.FloorH(0, row), L = V2.Dist(a, b), u0 = Math.Abs(a.x * 0.37 + a.z * 0.61) % 16;
            bool mirror = V2.Dot(b - a, o.Left) < 0;
            List<double[]> poly = new List<double[]> { new[] { 0.0, -0.05 }, new[] { L, -0.05 }, new[] { L, yb }, new[] { 0.0, ya } };
            double ymax = Math.Max(ya, yb);
            for (double y0 = -0.05; y0 < ymax - 0.01; y0 += fh)
            {
                List<double[]> c = ClipY(ClipY(poly, y0, true), y0 + fh, false);
                if (c.Count < 3) continue;
                double band = y0;
                Func<double[], P3> P = q => new P3(a + (b - a) * (q[0] / L), q[1], (u0 + (mirror ? L - q[0] : q[0])) / KitArt.RepeatM, KitArt.V(row, (q[1] - band) / fh));
                P3 p0 = P(c[0]);
                for (int i = 1; i + 1 < c.Count; i++) m.Tri(p0, P(c[i]), P(c[i + 1]), o.x, 0, o.z);
            }
        }

        // ------------------------------------------------------------------ the train (one car, in its own space: +z forward, y 0 = rail head)
        public const float TrainCarLength = 19.4f, TrainCarGap = 0.6f;

        /// A car's body (building kit atlas B) and its lights. cabFront / cabRear: a driving cab at that end.
        public static MeshData[] TrainCarMeshes(bool cabFront, bool cabRear)
        {
            MeshData body = new MeshData("TrainCar", "Train", KitMatB, false), lights = new MeshData("TrainLights", "Train", LampHeadMats[3], false);
            const double hl = 9.7, hw = 1.4, y0 = 0.3, y1 = 3.2, uw = 10.666667;
            V2 f = new V2(0, 1);
            foreach (int side in new[] { 1, -1 })
            {
                V2 o = new V2(side, 0), a = new V2(side * hw, -hl), m = new V2(side * hw, 0), b = new V2(side * hw, hl);
                foreach (int half in new[] { -1, 1 })
                {
                    V2 inner = m, outer = half > 0 ? b : a;
                    bool cab = half > 0 ? cabFront : cabRear;
                    bool outerIsLeft = V2.Dot(outer - inner, o.Left) < 0;
                    FaceUV(body, KitArt.B_Train, inner, outer, o, y0, y1, (cab ? 2 : half > 0 ? 1 : 0) * uw, uw, 0, 1, cab && !outerIsLeft, uw);
                }
            }
            foreach (int end in new[] { 1, -1 })
            {
                bool cab = end > 0 ? cabFront : cabRear;
                V2 l = new V2(-hw, end * hl), r = new V2(hw, end * hl), d = f * end;
                if (cab)
                {
                    SwQuad(body, KitArt.S_Yellow, new P3(l, y0, 0, 0), new P3(r, y0, 0, 0), new P3(r, 1.85, 0, 0), new P3(l, 1.85, 0, 0), d.x, 0, d.z);
                    SwQuad(body, KitArt.S_Glass, new P3(l, 1.85, 0, 0), new P3(r, 1.85, 0, 0), new P3(r, 2.85, 0, 0), new P3(l, 2.85, 0, 0), d.x, 0, d.z);
                    SwQuad(body, KitArt.S_Yellow, new P3(l, 2.85, 0, 0), new P3(r, 2.85, 0, 0), new P3(r, y1, 0, 0), new P3(l, y1, 0, 0), d.x, 0, d.z);
                    foreach (int s in new[] { -1, 1 }) RBox(lights, -1, new V2(s * 0.85, end * (hl + 0.02)), new V2(1, 0), 0.16, 0.03, 0.85, 1.0, true);
                }
                else
                {
                    SwQuad(body, KitArt.S_LightGrey, new P3(l, y0, 0, 0), new P3(r, y0, 0, 0), new P3(r, y1, 0, 0), new P3(l, y1, 0, 0), d.x, 0, d.z);
                    RBox(body, KitArt.S_Black, new V2(0, end * (hl + 0.15)), new V2(1, 0), 0.6, 0.15, 0.6, 2.9, false);      // gangway
                }
            }
            SwQuad(body, KitArt.S_LightGrey, new P3(new V2(-hw, -hl), y1, 0, 0), new P3(new V2(-hw, hl), y1, 0, 0), new P3(new V2(hw, hl), y1, 0, 0), new P3(new V2(hw, -hl), y1, 0, 0), 0, 1, 0);
            SwQuad(body, KitArt.S_DarkGrey, new P3(new V2(-hw, -hl), y0, 0, 0), new P3(new V2(-hw, hl), y0, 0, 0), new P3(new V2(hw, hl), y0, 0, 0), new P3(new V2(hw, -hl), y0, 0, 0), 0, -1, 0);
            foreach (double z in new[] { -6.7, 6.7 }) RBox(body, KitArt.S_Black, new V2(0, z), f, 1.3, 1.05, 0.0, y0, false);
            return new[] { body, lights };
        }

        /// The station's street entrance: in front of the passage arch on the road side (for the map and GPS).
        public static double[] RailStationEntrance()
        {
            if (railPlan == null || railPlan.rye == null) return null;
            double L = railPlan.length - railPlan.sStation0, bay = L / Math.Max(1, (int)Math.Round(L / 8.0));
            V2 p = RAt(railPlan.sStation0 + bay / 2, RailHW + 2.0);
            return new[] { p.x, 0.0, p.z };
        }

        /// A point on the railway: s metres along it, lat metres to the left (right if negative). For the map.
        public static V2 RailPoint(double s, double lat) { return RAt(s, lat); }
        public static double RailStationStart { get { return railPlan == null ? 0 : railPlan.sStation0; } }

        /// The train's track: the platform road, every 2 m from the south end to the buffers, at rail-head height.
        public static List<double[]> TrainPath(out double stationFront, out double depotFront, out double[] platformSpot)
        {
            List<double[]> pts = new List<double[]>();
            stationFront = depotFront = 0; platformSpot = null;
            if (railPlan == null) return pts;
            double end = railPlan.length - 3.0;
            for (double s = 0; s < end; s += 2) { V2 p = RAt(s, -RailTrack); pts.Add(new[] { p.x, RailHead, p.z }); }
            V2 e = RAt(end, -RailTrack); pts.Add(new[] { e.x, RailHead, e.z });
            stationFront = end - 1.0;
            double lat;                                          // out beyond the edge of the map, behind the hedge
            depotFront = RailProject(railPlan.line, new V2(railPlan.line.p[0].x, W.worldBounds.wallMinZ - 30), out lat);
            V2 ps = RAt(stationFront - 30, -(PlatformEdge + 1.6));
            platformSpot = new[] { ps.x, PlatformTop + 0.05, ps.z };
            return pts;
        }

        // ------------------------------------------------------------------ dressing (run last in Dress): fences, graffiti, the station railing
        static void DressRailway()
        {
            if (railPlan == null) return;
            RailPlan rp = railPlan;
            Random rr = new Random(9090), rx = new Random(9191);       // E1: the expansions' bridges get their own numbers
            double toe = RailHW + RailDeck * BankSlope + 0.8;
            UVRect link = A["chainlink"], rail = A["railing"], post = S("grey");
            // chain-link along the foot of the bank
            for (double s = 2; s + 2.5 < rp.sViaduct; s += 2.5)
            {
                if (RailKind(s) != 'E' || RailKind(s + 2.5) != 'E') continue;
                foreach (int side in new[] { 1, -1 })
                {
                    V2 a = RAt(s, side * toe), b = RAt(s + 2.5, side * toe), o; RP(s + 1.25, out o); o = o * side;
                    VQuad(mCut, a, b, 0, 2.0, link, o);
                    VQuad(mCut, b, a, 0, 2.0, link, o * -1);
                    Cyl(mSolid, a, 0.035, 0, 2.05, 5, post);
                    D.Count("railway fence panels");
                }
            }
            // graffiti where drivers see it: the bridge abutments, and the walls of the approaches
            Random cityRr = rr;
            foreach (RailSpanInfo sp in rp.spans)
            {
                rr = sp.expansion ? rx : cityRr;
                foreach (int facing in new[] { 1, -1 })
                {
                    double s = facing > 0 ? sp.s0 : sp.s1;
                    V2 d = RDir(s) * facing;
                    int n = sp == rp.rye ? 2 : 1 + rr.Next(2);
                    for (int k = 0; k < n; k++)
                    {
                        double c = n == 2 ? (k == 0 ? -2.5 : 2.5) : (rr.NextDouble() - 0.5) * 3, w = 2.0 + rr.NextDouble() * 0.3, y0 = 0.3 + rr.NextDouble() * 0.3;
                        VQuad(mCut, RAt(s, c - w) + d * 0.04, RAt(s, c + w) + d * 0.04, y0, y0 + w * 2 / 2.33, A["tag_" + rr.Next(Tags.Length)], d);
                        D.Count("railway graffiti");
                    }
                    double w0 = facing > 0 ? sp.s0 - AbutLen - WallRun : sp.s1 + AbutLen, w1 = w0 + WallRun;
                    if (w1 > rp.sViaduct) continue;
                    foreach (int side in new[] { 1, -1 })
                    {
                        if (rr.NextDouble() < 0.3) continue;
                        double sm = w0 + 3 + rr.NextDouble() * (WallRun - 6), w = 1.8 + rr.NextDouble() * 0.5, y0 = 0.3 + rr.NextDouble() * 0.4;
                        V2 o; RP(sm, out o); o = o * side;
                        VQuad(mCut, RAt(sm - w, side * (RailHW + 0.04)), RAt(sm + w, side * (RailHW + 0.04)), y0, y0 + w * 2 / 2.33, A["tag_" + rr.Next(Tags.Length)], o);
                        D.Count("railway graffiti");
                    }
                }
            }
            // the station's back railing, on top of the parapet
            for (double s = rp.sStation0 + 0.5; s + 2 < rp.length - 0.3; s += 2)
            {
                if (StairGap(s) || StairGap(s + 2)) continue;
                V2 a = RAt(s, -(RailStationHW - 0.18)), b = RAt(s + 2, -(RailStationHW - 0.18)), o; RP(s + 1, out o);
                VQuad(mCut, a, b, RailParapet, RailParapet + 1.1, rail, o);
                VQuad(mCut, b, a, RailParapet, RailParapet + 1.1, rail, o * -1);
            }
        }

        // ------------------------------------------------------------------ helpers (kit atlas UVs)
        static void SwQuad(MeshData m, int sw, P3 a, P3 b, P3 c, P3 d, double nx, double ny, double nz)
        {
            double u, v; KitArt.SwatchUV(sw, out u, out v);
            a.u = b.u = c.u = d.u = u; a.v = b.v = c.v = d.v = v;
            m.Quad(a, b, c, d, nx, ny, nz);
        }

        /// A box of one swatch (sw < 0: plain UVs, for the lamp materials). f = long axis.
        static void RBox(MeshData m, int sw, V2 c, V2 f, double hl, double hw, double y0, double y1, bool bottom)
        {
            double u = 0.5, v = 0.5; if (sw >= 0) KitArt.SwatchUV(sw, out u, out v);
            f = f.Norm; V2 l = f.Left;
            V2[] q = { c - f * hl - l * hw, c + f * hl - l * hw, c + f * hl + l * hw, c - f * hl + l * hw };
            for (int i = 0; i < 4; i++)
            {
                V2 a = q[i], b = q[(i + 1) % 4], n = ((a + b) * 0.5 - c).Norm;
                m.Quad(new P3(a, y0, u, v), new P3(b, y0, u, v), new P3(b, y1, u, v), new P3(a, y1, u, v), n.x, 0, n.z);
            }
            m.Quad(new P3(q[0], y1, u, v), new P3(q[1], y1, u, v), new P3(q[2], y1, u, v), new P3(q[3], y1, u, v), 0, 1, 0);
            if (bottom) m.Quad(new P3(q[0], y0, u, v), new P3(q[1], y0, u, v), new P3(q[2], y0, u, v), new P3(q[3], y0, u, v), 0, -1, 0);
        }

        /// A thin box following the line from s a to b at lateral lat.
        static void RBoxAlong(MeshData m, int sw, double a, double b, double lat, double w, double y0, double y1)
        {
            V2 c = (RAt(a, lat) + RAt(b, lat)) * 0.5;
            RBox(m, sw, c, RAt(b, lat) - RAt(a, lat), V2.Dist(RAt(a, lat), RAt(b, lat)) / 2, w / 2, y0, y1, true);
        }

        /// A wall quad textured from a kit strip: U runs left → right as seen from outside (o), from uStart for uLen texture
        /// metres (mirror flips it); V covers t0..t1 of the strip. unitW > 0 keeps uStart inside one unit's art.
        static void FaceUV(MeshData m, int row, V2 a, V2 b, V2 o, double y0, double y1, double uStart, double uLen, double t0, double t1, bool mirror, double unitW = 0)
        {
            if (V2.Dot(b - a, o.Left) < 0) { V2 t = a; a = b; b = t; }
            double ua = uStart, ub = uStart + (unitW > 0 ? unitW : uLen);
            if (mirror) { double t = ua; ua = ub; ub = t; }
            double u0 = ua / KitArt.RepeatM, u1 = ub / KitArt.RepeatM, v0 = KitArt.V(row, t0), v1 = KitArt.V(row, t1);
            m.Quad(new P3(a, y0, u0, v0), new P3(b, y0, u1, v0), new P3(b, y1, u1, v1), new P3(a, y1, u0, v1), o.x, 0, o.z);
        }

        /// Plain yellow-stock brick from a to b, the strip repeated each storey so the bricks keep their size.
        static void BrickWall(MeshData m, V2 a, V2 b, V2 o, double y0, double y1)
        {
            int row = KitArt.A_BrickYel;
            double fh = KitArt.FloorH(0, row), L = V2.Dist(a, b), u0 = Math.Abs(a.x * 0.37 + a.z * 0.61) % 16;
            for (double y = y0; y < y1 - 0.01; y += fh)
            {
                double top = Math.Min(y1, y + fh);
                FaceUV(m, row, a, b, o, y, top, u0, L, 0, (top - y) / fh, false);
            }
        }
    }
}
