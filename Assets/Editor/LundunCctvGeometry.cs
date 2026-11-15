// V2d: CCTV. London is one of the most watched cities in the world, so the cameras are part of how the streets look:
//   junction domes   a tall grey pole on a corner of every junction with traffic lights (two on the Ring), and on half the
//                    high-street crossings; every junction in Kingsmere; a yellow "CCTV" plate on the pole
//   shop cameras     a bracket camera above the fascia, at one end of the front, looking down the pavement: nearly every
//                    bank, bookies, pharmacy, pawnbroker, money transfer, off licence and phone shop; then by street
//                    (high streets most, back streets least) and by area (Kingsmere, Knightsbury, the Quay most)
//   estates          cameras on the corners of the blocks at walkway height, a council plate by the door; about a third
//                    of them sprayed over or smashed hanging off their bracket
//   offices          the Quay's towers and offices, and Kingsmere's grand buildings, at both front corners
//   stations, A&E,   a pole camera watching the door
//   police
//   at night         a red LED on each working camera and a faint infrared glow round the lens (the glow material)
// Every camera (where, which way, how wide, how far, working or not) goes in DressingResult.cctv; Lundun → 30 saves
// them in the scene (LundunCctv), for V3, when they become witnesses.
// Runs last in Dress, with its own random numbers, so nothing placed before it moves. Pure C# (no UnityEngine).
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    /// One camera. kind: dome (360°, on a pole), bullet (on a wall / bracket), estate (bullet on an estate block).
    public class CctvInfo
    {
        public string kind;
        public double x, y, z, yaw, pitch, fov, range;
        public bool broken;
    }

    /// A shop or pub front (recorded by Shop / Pub so the cameras can go above them; no random numbers).
    public class ShopFrontInfo { public double x, z, nx, nz, w; public string name; public bool shut; }

    public static partial class Generator
    {
        static Random crng;
        static readonly List<V2> shopCams = new List<V2>();

        // shops that nearly always have a camera on them
        static readonly string[] CctvShops = { "BANK", "BET", "SLOTS", "PHARMACY", "PAWN", "CASH", "MONEY", "OFF LICENCE", "FOOD & WINE",
                                               "PHONE", "FONE", "JEWEL", "WINES", "GOLD", "NEWS" };

        static void DressCctv()
        {
            crng = new Random(4747);
            shopCams.Clear();
            int before = D.cctv.Count;
            CctvJunctions();
            CctvShopFronts();
            CctvBuildings();
            CctvDoors();
            int n = D.cctv.Count - before, bad = 0;
            foreach (CctvInfo c in D.cctv) if (c.broken) bad++;
            D.Count("CCTV cameras", n);
            D.Count("CCTV cameras (vandalised)", bad);
        }

        static double AreaWatch(string dist)
        {
            switch (dist)
            {
                case "D_KINGSMERE": return 1.6;
                case "D_CANARY": case "D_CANARY_PEN": return 1.4;
                case "D_KNIGHT": return 1.3;
                case "D_BRIXTUN_EST": return 1.1;
            }
            return 1.0;
        }

        // ------------------------------------------------------------------ junctions
        static void CctvJunctions()
        {
            List<string> jids = new List<string>(juncs.Keys); jids.Sort(StringComparer.Ordinal);
            foreach (string jid in jids)
            {
                JInfo ji = juncs[jid];
                if (ji.j == null || ji.legRoad.Count < 3) continue;
                bool big = false, service = false, high = false;
                foreach (RoadInfo ri in ji.legRoad)
                {
                    if (ri.cls == "arterial" || ri.cls == "ring") big = true;
                    if (ri.cls == "service") service = true;
                    if (RoleOf(ri.r.name, ri.cls) == StreetRole.HighStreet) high = true;
                }
                bool lights = big && !service && !jid.StartsWith("KJ") && !(jid.StartsWith("XJ") && ji.j.arms < 4);   // the same junctions as LundunTrafficLights
                bool want = lights || (Kingsmere && !service) || (high && crng.NextDouble() < 0.5);
                if (!want) continue;
                bool ring = false; foreach (RoadInfo ri in ji.legRoad) if (ri.cls == "ring") ring = true;
                CornerPoles(ji, ring ? 2 : 1);
            }
        }

        /// Up to 'count' poles on the corners of a junction (on the pavement, clear of both roads), the second one
        /// on the far side from the first.
        static void CornerPoles(JInfo ji, int count)
        {
            int k = ji.legDir.Count;
            int[] order = new int[k];
            double[] ang = new double[k];
            for (int i = 0; i < k; i++) { order[i] = i; ang[i] = Math.Atan2(ji.legDir[i].z, ji.legDir[i].x); }
            Array.Sort(order, (a, b) => ang[a].CompareTo(ang[b]));
            int start = crng.Next(k), placed = 0;
            V2 first = new V2(0, 0);
            for (int step = 0; step < k && placed < count; step++)
            {
                int a = order[(start + step) % k], b = order[(start + step + 1) % k];
                RoadInfo ra = ji.legRoad[a], rb = ji.legRoad[b];
                if (ra.pave <= 0 || rb.pave <= 0) continue;
                double between = ang[b] - ang[a]; if (between <= 0) between += Math.PI * 2;
                if (between > Math.PI * 0.85 || between < 0.35) continue;                      // nearly straight on, or a sliver
                V2 bis = (ji.legDir[a] + ji.legDir[b]).Norm;
                double need = Math.Max(ra.w / 2 + 0.7, rb.w / 2 + 0.7);
                V2 p = ji.c + bis * (need / Math.Sin(between / 2));
                if (placed > 0 && V2.Dist(p, first) < 12) continue;
                if (!PoleSpot(p, ji)) continue;
                PoleCam(p, (ji.c - p).Norm, PaveY, true, "dome");
                if (placed == 0) first = p;
                placed++;
            }
        }

        /// On a pavement: near a road (but not on any), not in a building, the old high street, the river or anything placed.
        static bool PoleSpot(V2 p, JInfo ji)
        {
            if (InBox(p) || !Free(p, 0.6) || InBuilding(p, 0.6)) return false;
            if (river != null && river.DistanceTo(p) < WallOuter + 2) return false;
            bool onPave = false;
            foreach (RoadInfo o in roads.Values)
            {
                double d = o.line.DistanceTo(p);
                if (d < o.w / 2 + 0.4) return false;
                if (o.pave > 0 && d < o.w / 2 + o.pave) onPave = true;
            }
            return onPave;
        }

        static bool InBuilding(V2 q, double grow)
        {
            foreach (BuildingInfo b in blds)
            {
                V2 d = q - b.c; V2 f = b.dir.Norm;
                if (Math.Abs(V2.Dot(d, f)) < b.len / 2 + grow && Math.Abs(V2.Dot(d, f.Left)) < b.dep / 2 + grow) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ shop fronts
        static void CctvShopFronts()
        {
            foreach (ShopFrontInfo s in D.shopFronts)
            {
                V2 c = new V2(s.x, s.z), n = new V2(s.nx, s.nz), t = n.Left;
                string dist = District(c);
                double chance;
                string name = s.name.ToUpperInvariant();
                bool key = false; foreach (string w in CctvShops) if (name.Contains(w)) key = true;
                if (key) chance = 0.85;
                else if (name.StartsWith("PUB:")) chance = 0.45;
                else
                {
                    StreetRole role = StreetRole.Residential;
                    double best = 40;
                    foreach (RoadInfo ri in roads.Values)
                    {
                        if (ri.pave <= 0 || ri.cls == "bridge") continue;
                        double d = ri.line.DistanceTo(c);
                        if (d < best) { best = d; role = RoleOf(ri.r.name, ri.cls); }
                    }
                    chance = role == StreetRole.HighStreet ? 0.32 : role == StreetRole.Local ? 0.2 : 0.1;
                }
                chance = Math.Min(0.95, chance * AreaWatch(dist));
                double roll = crng.NextDouble(), endRoll = crng.NextDouble();       // always two numbers per shop (stable)
                if (roll > chance) continue;
                bool near = false; foreach (V2 o in shopCams) if (V2.Dist(o, c) < 10) near = true;
                if (near && !key) continue;
                int side = endRoll < 0.5 ? -1 : 1;
                double hw = s.w / 2 - 0.3;
                V2 wall = c + t * (side * hw);
                V2 look = (t * -side * 0.8 + n * 0.6).Norm;                          // back along the front, out over the pavement
                WallCam(wall, n, look, 4.25, false, "bullet");
                shopCams.Add(c);
            }
        }

        // ------------------------------------------------------------------ estates, towers, grand buildings
        static void CctvBuildings()
        {
            foreach (BuildingInfo b in blds)
            {
                KitPlan p = KitPlanFor(b);
                if (p == null) continue;
                string dist = b.district;
                bool estate = p.type == KitType.Deck || p.type == KitType.Maisonette || p.type == KitType.EstTower || p.type == KitType.EstParade;
                bool office = p.type == KitType.Tower || p.type == KitType.Office;
                bool grand = dist == "D_KINGSMERE" && (p.type == KitType.Civic || p.type == KitType.Mansion || p.type == KitType.Stucco || p.type == KitType.DeptStore);
                double roll = crng.NextDouble(), broke = crng.NextDouble(), how = crng.NextDouble(), signRoll = crng.NextDouble();
                if (estate)
                {
                    if (roll > 0.62) continue;
                    double y = p.type == KitType.EstTower ? 3.6 : 3.2;
                    bool tower = p.type == KitType.EstTower;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        if (tower && s > 0) break;                                          // a tower: one, by the door
                        V2 wall = tower ? PP(p, 1.6, 0) : PP(p, s * (p.W / 2 - 0.5), 0);
                        V2 look = tower ? (p.n * 0.7 - p.r * 0.7).Norm : (p.r * -s * 0.75 + p.n * 0.66).Norm;
                        bool bad = (s < 0 ? broke : 1 - broke) < 0.34;
                        WallCam(wall, p.n, look, y, bad, "estate", how);
                    }
                    if (signRoll < 0.7) CctvPlate(PP(p, -0.9, 0) + p.n * 0.03, p.n, p.r, 2.05, "cctv_council", signRoll < 0.25);
                }
                else if (office || grand)
                {
                    double chance = dist == "D_CANARY_PEN" || dist == "D_CANARY" ? 0.8 : grand ? 0.7 : 0.35;
                    if (roll > chance) continue;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        V2 wall = PP(p, s * (p.W / 2 - 0.4), 0);
                        WallCam(wall, p.n, (p.r * -s * 0.7 + p.n * 0.7).Norm, 4.2, false, "bullet");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ stations, hospitals, police stations
        static void CctvDoors()
        {
            foreach (StationInfo st in D.stations) DoorPole(new V2(st.x, st.z), st.yaw);
            foreach (PlaceInfo pl in D.places)
                if (pl.kind == "hospital" || pl.kind == "police") DoorPole(new V2(pl.doorX, pl.doorZ), pl.doorYaw);
        }

        static void DoorPole(V2 door, double yaw)
        {
            V2 f = new V2(Math.Sin(yaw * Math.PI / 180), Math.Cos(yaw * Math.PI / 180));
            foreach (double side in new[] { 3.0, -3.0, 4.5, -4.5 })
            {
                V2 p = door + f * 2.0 + f.Left * side;
                if (InBox(p) || !Free(p, 0.6) || InBuilding(p, 0.5)) continue;
                bool onRoad = false; foreach (RoadInfo o in roads.Values) if (o.line.DistanceTo(p) < o.w / 2 + 0.4) onRoad = true;
                if (onRoad) continue;
                PoleCam(p, (door - p).Norm, PaveY, true, "dome");
                return;
            }
        }

        // ------------------------------------------------------------------ the cameras themselves
        /// A dome camera on an arm at the top of a 6.6 m grey pole; the plate on the pole faces the pavement.
        static void PoleCam(V2 p, V2 look, double y0, bool plate, string kind)
        {
            V2 l = look.Left;
            Cyl(mSolid, p, 0.09, y0, y0 + 6.6, 6, S("grey"));
            SBox(mSolid, p + look * 0.45, look, 0.45, 0.045, y0 + 6.42, y0 + 6.52, S("grey"), true);       // the arm
            V2 tip = p + look * 0.85;
            SBox(mSolid, tip, look, 0.05, 0.04, y0 + 6.35, y0 + 6.42, S("grey"));                          // the drop
            Cyl(mSolid, tip, 0.17, y0 + 6.22, y0 + 6.35, 8, S("white"));                                   // the housing
            Cyl(mSolid, tip, 0.13, y0 + 6.04, y0 + 6.22, 8, S("black"), false);                            // the smoked dome
            SBox(mSolid, tip, look, 0.09, 0.09, y0 + 6.0, y0 + 6.04, S("black"), true);
            Led(tip + look * 0.172, look, y0 + 6.29);
            Led(tip - look * 0.172, look * -1, y0 + 6.29);
            if (plate)
            {
                // a pole-wrap clamp and the plate, both faces (the pole is round: it's read from either side)
                SBox(mSolid, p, look, 0.1, 0.1, y0 + 2.55, y0 + 2.62, S("charcoal"));
                V2 pc = p - look * 0.11;
                VQuad(mSolid, pc + l * 0.16, pc - l * 0.16, y0 + 2.2, y0 + 2.6, A["cctv_sign"], look * -1);
                VQuad(mSolid, pc - l * 0.16 + look * 0.005, pc + l * 0.16 + look * 0.005, y0 + 2.2, y0 + 2.6, A["cctv_sign"], look);
            }
            Collider("CctvPole", p, look, 0.2, 0.2, y0, y0 + 6.6);
            Claim(p, 0.5);
            Record(kind, tip, y0 + 6.1, Yaw(look), -15, 360, 42, false);
            D.Count("CCTV poles");
        }

        /// A bracket camera on a wall: n = out of the wall, look = where it points. A broken one is either sprayed over
        /// (how < 0.5) or smashed and hanging off its bracket.
        static void WallCam(V2 wall, V2 n, V2 look, double y, bool broken, string kind, double how = 0)
        {
            V2 nl = n.Left;
            SBox(mSolid, wall + n * 0.03, nl, 0.07, 0.03, y - 0.02, y + 0.26, S("charcoal"));               // wall plate
            SBox(mSolid, wall + n * 0.2, n, 0.17, 0.025, y + 0.15, y + 0.2, S("charcoal"), true);           // bracket arm
            V2 hc = wall + n * 0.38;
            if (broken && how >= 0.5)
            {
                // smashed: the housing hangs nose-down on its cable, no lens, no light
                SBox(mSolid, hc, n, 0.012, 0.012, y - 0.1, y + 0.15, S("black"));
                SBox(mSolid, hc + n * 0.02, n, 0.06, 0.05, y - 0.5, y - 0.12, S("white"), true);
                SBox(mSolid, hc + n * 0.02, n, 0.045, 0.04, y - 0.53, y - 0.5, S("charcoal"), true);
                Record(kind, hc, y - 0.3, Yaw(look), -90, 0, 0, true);
                return;
            }
            V2 l = look.Left;
            SBox(mSolid, hc, look, 0.19, 0.065, y - 0.03, y + 0.11, S("white"), true);                       // housing
            SBox(mSolid, hc + look * 0.03, look, 0.23, 0.085, y + 0.11, y + 0.13, S("white"), true);        // sun hood
            V2 face = hc + look * 0.19;
            if (broken)
            {
                // sprayed over: a blob of paint over the front, a run of it down the side
                string paint = new[] { "orange", "cycleteal", "postred", "safegreen", "black" }[(int)(how * 10) % 5];
                VQuad(mSolid, face + look * 0.006 - l * 0.075, face + look * 0.006 + l * 0.075, y - 0.04, y + 0.12, S(paint), look);
                SBox(mSolid, hc + look * 0.1 + l * 0.066, look, 0.07, 0.004, y - 0.03, y + 0.11, S(paint));
                Record(kind, hc, y + 0.04, Yaw(look), -20, 0, 0, true);
                return;
            }
            VQuad(mGlow, face + look * 0.003 - l * 0.055, face + look * 0.003 + l * 0.055, y - 0.015, y + 0.095, S("darkred"), look);   // infrared ring
            VQuad(mSolid, face + look * 0.008 - l * 0.032, face + look * 0.008 + l * 0.032, y + 0.008, y + 0.072, S("black"), look);   // lens
            Led(face + look * 0.009 + l * 0.045, look, y + 0.085);
            Record(kind, hc, y + 0.04, Yaw(look), -22, kind == "estate" ? 80 : 70, kind == "estate" ? 22 : 28, false);
        }

        /// The little red light: on the glow material, so it shows at night.
        static void Led(V2 c, V2 n, double y)
        {
            V2 l = n.Left;
            VQuad(mGlow, c - l * 0.025, c + l * 0.025, y - 0.025, y + 0.025, S("postred"), n);
        }

        /// A yellow CCTV plate flat on a wall (n out of it, r along it); now and then tagged over.
        static void CctvPlate(V2 c, V2 n, V2 r, double y, string sign, bool tagged)
        {
            VQuad(mSolid, c - r * 0.2, c + r * 0.2, y, y + 0.5, A[sign], n);
            if (tagged) VQuad(mCut, c + n * 0.01 - r * 0.32, c + n * 0.01 + r * 0.32, y - 0.1, y + 0.4, A["tag_" + crng.Next(AreaTags.Length)], n);
        }

        static void Record(string kind, V2 at, double y, double yaw, double pitch, double fov, double range, bool broken)
        {
            D.cctv.Add(new CctvInfo { kind = kind, x = at.x, y = y, z = at.z, yaw = yaw, pitch = pitch, fov = fov, range = range, broken = broken });
        }
    }

    /// The two CCTV plates, painted into the unused ends of the dressing atlas's shelves (our own wording and pictogram).
    public static class CctvArt
    {
        public static void Paint(DressAtlas a)
        {
            Plate(a, "cctv_sign", "CCTV", "IN OPERATION", null);
            Plate(a, "cctv_council", "CCTV", "24 HOUR RECORDING", "LUNDUN COUNCIL");
        }

        static void Plate(DressAtlas a, string name, string big, string line, string foot)
        {
            int w = 128, h = 160, x, y;
            if (!a.AllocTail(name, w, h, out x, out y)) return;
            Col yellow = Col.Hex("ffd21f"), ink = Col.Hex("141414");
            a.Fill(x, y, w, h, yellow);
            a.Frame(x + 5, y + 5, w - 10, h - 10, 5, ink);
            // the camera: a body on a bracket, from a wall on the right
            a.Fill(x + 98, y + 92, 7, 46, ink);                                  // the wall
            a.Seg(x + 98, y + 112, x + 80, y + 112, 3.5, ink);                    // the bracket
            a.Seg(x + 78, y + 118, x + 38, y + 104, 11, ink);                     // the body, nose down
            a.Seg(x + 34, y + 102, x + 26, y + 99, 6, ink);                       // the lens
            a.Seg(x + 76, y + 130, x + 40, y + 117, 2.5, ink);                    // the hood
            a.Text(big, x + 14, y + 54, w - 28, 30, ink, 0.55, DressAtlas.Align.Centre);
            a.Text(line, x + 12, foot == null ? y + 22 : y + 34, w - 24, 18, ink, 0.5, DressAtlas.Align.Centre);
            if (foot != null) a.Text(foot, x + 12, y + 14, w - 24, 14, ink, 0.45, DressAtlas.Align.Centre);
        }
    }
}
