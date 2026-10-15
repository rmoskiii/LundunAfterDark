// Pure C#: R7 places — hospitals, police stations and safehouses. Each one is planned on a road frontage
// (any city buildings standing on the plot are cleared — nothing else moves), drawn with the same cheap
// atlas meshes as the street dressing, and given a door (where James appears) and, for safehouses,
// a driveway (where the Kestrel waits).
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class PlaceInfo
    {
        public string id, kind, name;
        public double doorX, doorZ, doorYaw;
        public bool hasCar; public double carX, carZ, carYaw;
    }

    public class PlaceSite
    {
        public string id, kind, name, roadId, style;
        public double anchorX, anchorZ, len, depth, height;
        // solved:
        public bool ok; public double ch; public int side; public V2 front, f, n;     // front = back-of-pavement point at the plot centre
        public double lat0;                                                           // lateral distance from road centre to the plot's front
        public V2 Corner(double along, double inward) { return front + f * along + n * inward; }
        public bool Contains(V2 q, double grow)
        {
            V2 d = q - front; double a = V2.Dot(d, f), b = V2.Dot(d, n);
            return Math.Abs(a) <= len / 2 + grow && b >= -grow && b <= depth + grow;
        }
    }

    public static partial class Generator
    {
        static List<PlaceSite> placeSites;

        static List<PlaceSite> PlaceSpecs()
        {
            return new List<PlaceSite>
            {
                new PlaceSite { id = "HOSP_NORTH", kind = "hospital", name = "St Lundun's Hospital", roadId = "R05", anchorX = 240, anchorZ = 760, len = 46, depth = 30, height = 14 },
                new PlaceSite { id = "HOSP_SOUTH", kind = "hospital", name = "Gilmore General Hospital", roadId = "R23", anchorX = 470, anchorZ = 385, len = 46, depth = 30, height = 14 },
                new PlaceSite { id = "POLICE_PECKAM", kind = "police", name = "Peckam Police Station", roadId = "R19", anchorX = 745, anchorZ = 250, len = 30, depth = 22, height = 10 },
                new PlaceSite { id = "POLICE_KNIGHT", kind = "police", name = "Knightsbury Police Station", roadId = "R03", anchorX = 700, anchorZ = 895, len = 30, depth = 22, height = 10 },
                new PlaceSite { id = "SAFE_KNIGHT", kind = "safehouse", name = "Knightsbury Mews Safehouse", roadId = "R08", anchorX = 450, anchorZ = 1045, len = 17, depth = 16, height = 8, style = "stucco" },
                new PlaceSite { id = "SAFE_CANARY", kind = "safehouse", name = "Canary Quay Safehouse", roadId = "R12", anchorX = 1365, anchorZ = 760, len = 17, depth = 16, height = 13, style = "modern" },
                new PlaceSite { id = "SAFE_GILMORE", kind = "safehouse", name = "Gilmore Estate Safehouse", roadId = "R25", anchorX = 380, anchorZ = 160, len = 17, depth = 16, height = 8, style = "brick" },
            };
        }

        /// Works out exactly where each plot goes (needs roads; no randomness, so it's the same every run).
        static List<PlaceSite> PlanPlaces()
        {
            if (placeSites != null && placeSitesFor == W) return placeSites;
            List<PlaceSite> list = PlaceSpecs();
            foreach (PlaceSite s in list)
            {
                RoadInfo ri;
                if (!roads.TryGetValue(s.roadId, out ri)) continue;
                V2 anchor = new V2(s.anchorX, s.anchorZ);
                // nearest chainage to the anchor, and which side it's on
                double bestCh = 0, bestD = double.MaxValue;
                for (double c = 0; c <= ri.line.Length; c += 1)
                {
                    V2 d0; V2 q = ri.line.At(c, out d0);
                    double d = V2.Dist(q, anchor);
                    if (d < bestD) { bestD = d; bestCh = c; }
                }
                V2 dd; V2 qa = ri.line.At(bestCh, out dd);
                int side = V2.Dot(anchor - qa, dd.Left) >= 0 ? 1 : -1;
                double lat0 = ri.w / 2 + Math.Max(ri.pave, ri.verge);
                List<double[]> ok = Stretches(ri, side, 6, s.len);
                // slide along the road (nearest first) until the plot is clear of junctions, other roads and the river
                for (int k = 0; k < 160 && !s.ok; k++)
                {
                    double c = bestCh + (k % 2 == 0 ? 1 : -1) * (k / 2) * 1.5;
                    if (!Inside(c - s.len / 2, ok, 0) || !Inside(c + s.len / 2, ok, 0)) continue;
                    V2 f; V2 cp = ri.line.At(c, out f);
                    V2 n = f.Left * side;
                    V2 front = cp + n * lat0;
                    PlaceSite t = s; t.front = front; t.f = f; t.n = n;
                    bool clear = true;
                    for (double a = -s.len / 2; a <= s.len / 2 && clear; a += 2)
                        for (double b = 0; b <= s.depth && clear; b += 2)
                        {
                            V2 q = t.Corner(a, b);
                            if (InBox(q) || NearOtherRoad(q, ri, Math.Max(3.0, 0)) || river.DistanceTo(q) < WallOuter + 2) clear = false;
                            foreach (RoadInfo o in roads.Values)
                                if (o != ri && o.line.DistanceTo(q) < o.w / 2 + Math.Max(o.pave, o.verge) + 1) clear = false;
                            foreach (LwPoly park in W.parks) if (InPoly(q, Pts(park.polygon))) clear = false;
                        }
                    if (!clear) continue;
                    s.ok = true; s.ch = c; s.side = side; s.lat0 = lat0;
                }
            }
            placeSites = list; placeSitesFor = W;
            return list;
        }
        static LundunWorldData placeSitesFor;

        /// Called by BuildBuildings after placing: buildings standing on a plot are taken away (nothing else changes).
        static int ClearPlots(GreyboxResult R)
        {
            List<PlaceSite> sites = PlanPlaces();
            return R.buildings.RemoveAll(b =>
            {
                V2 f = b.dir.Norm, l = f.Left;
                for (double a = -b.len / 2; a <= b.len / 2; a += 2)
                    for (double c = -b.dep / 2; c <= b.dep / 2; c += 2)
                        foreach (PlaceSite s in sites)
                            if (s.ok && s.Contains(b.c + f * a + l * c, 2.0)) return true;
                return false;
            });
        }

        // ================================================================== drawing (from Dress)
        static void DressPlaces()
        {
            foreach (PlaceSite s in PlanPlaces())
            {
                if (!s.ok) { D.log.Add("  !! couldn't fit " + s.name + " — skipped"); continue; }
                // claim the plot so trees / benches / phone boxes keep off it
                for (double a = -s.len / 2; a <= s.len / 2; a += 3) for (double b = 0; b <= s.depth; b += 3) Claim(s.Corner(a, b), 2.2);
                for (double a = -s.len / 2; a <= s.len / 2; a += 3) Claim(s.front + s.n * -1.5 + s.f * a, 1.4);   // and the pavement in front
                if (s.kind == "hospital") Hospital(s);
                else if (s.kind == "police") PoliceStation(s);
                else Safehouse(s);
                D.Count(s.kind == "hospital" ? "hospitals" : s.kind == "police" ? "police stations" : "safehouses");
            }
            // the existing Peckam safehouse (in the old high street) — door and Kestrel bay from the spawn points
            LwSpawn start = null, car = null;
            foreach (LwSpawn sp in W.spawns) { if (sp.id == "SP_PLAYER_START") start = sp; if (sp.id == "SP_KESTREL") car = sp; }
            if (start != null)
                D.places.Add(new PlaceInfo { id = "SAFE_PECKAM", kind = "safehouse", name = "Peckam Safehouse", doorX = start.x, doorZ = start.z, doorYaw = start.yaw,
                                             hasCar = car != null, carX = car != null ? car.x : 0, carZ = car != null ? car.z : 0, carYaw = car != null ? car.yaw : 0 });
        }

        static void AddPlace(PlaceSite s, V2 door, V2 facing, bool hasCar, V2 car, V2 carFacing)
        {
            D.places.Add(new PlaceInfo { id = s.id, kind = s.kind, name = s.name, doorX = door.x, doorZ = door.z, doorYaw = Yaw(facing),
                                         hasCar = hasCar, carX = car.x, carZ = car.z, carYaw = Yaw(carFacing) });
        }

        /// Building block with a lit window grid on the front (facing the road).
        static void Block(PlaceSite s, double along, double inward, double w, double d, double h, string wall, string windows, double winFrom, double winTo, double doorGap)
        {
            V2 c = s.Corner(along, inward + d / 2);
            SBox(mSolid, c, s.f, w / 2, d / 2, 0, h, S(wall));
            SBox(mSolid, c, s.f, w / 2 + 0.15, d / 2 + 0.15, h, h + 0.4, S("charcoal"));                      // parapet / roof edge
            V2 face = s.Corner(along, inward - 0.05);
            if (windows != null)
            {
                if (doorGap <= 0) VQuad(mGlow, face - s.f * (w / 2 - 0.6), face + s.f * (w / 2 - 0.6), winFrom, winTo, A[windows], s.n * -1);
                else
                {
                    VQuad(mGlow, face - s.f * (w / 2 - 0.6), face - s.f * (doorGap / 2), winFrom, winTo, A[windows], s.n * -1);
                    VQuad(mGlow, face + s.f * (doorGap / 2), face + s.f * (w / 2 - 0.6), winFrom, winTo, A[windows], s.n * -1);
                }
            }
            Collider("PlaceBuilding", c, s.f, d, w, 0, h);
        }

        static void SignBoard(V2 at, V2 n, V2 t, double halfW, double y0, double y1, string tile)
        {
            SBox(mSolid, at - n * 0.08, t, halfW + 0.1, 0.08, y0 - 0.1, y1 + 0.1, S("charcoal"));
            VQuad(mGlow, at + n * 0.01 - t * halfW, at + n * 0.01 + t * halfW, y0, y1, A[tile], n);
        }

        // ------------------------------------------------------------------ hospital
        static void Hospital(PlaceSite s)
        {
            V2 toRoad = s.n * -1;
            HQuad(mSolid, s.Corner(0, s.depth / 2), s.n, s.depth / 2, s.len / 2, 0.03, S("slab"));
            double bIn = 12, bW = s.len - 6, bD = s.depth - bIn - 1;
            Block(s, 0, bIn, bW, bD, s.height, "hospwall", "officewin", 4.2, s.height - 1.0, 0);
            // blue floor bands
            for (double y = 4.0; y < s.height - 1; y += 3.4) SBox(mSolid, s.Corner(0, bIn - 0.1), s.f, bW / 2, 0.12, y - 0.15, y + 0.1, S("nhsblue"));
            // entrance: canopy on posts, glass doors, A&E sign
            V2 ent = s.Corner(-bW / 4, bIn);
            SBox(mSolid, ent - s.n * 2.2, s.f, 6.5, 2.4, 3.3, 3.6, S("white"));
            foreach (int k in new[] { -1, 1 }) SBox(mSolid, ent - s.n * 4.2 + s.f * (k * 6.0), s.f, 0.12, 0.12, 0, 3.3, S("white"));
            VQuad(mSolid, ent - s.n * 0.06 - s.f * 2.5, ent - s.n * 0.06 + s.f * 2.5, 0, 2.9, S("glass"), toRoad);
            SignBoard(ent - s.n * 4.62, toRoad, s.f, 3.2, 3.65, 4.45, "sign_AE");
            SignBoard(s.Corner(0, bIn - 0.2), toRoad, s.f, 11, s.height - 0.2 + 0.6, s.height + 1.8, "sign_" + s.id);
            // ambulance bay
            for (int i = 0; i < 2; i++) Ambulance(s.Corner(bW / 4 + (i - 0.5) * 5.0, 6.5), s.n);
            // blue H road sign at the kerb
            V2 hp = s.front - s.n * 0.6 + s.f * (s.len / 2 - 1);
            Cyl(mSolid, hp, 0.06, PaveY, PaveY + 2.6, 6, S("grey"));
            SBox(mSolid, hp, s.f, 0.45, 0.03, PaveY + 2.0, PaveY + 2.9, S("charcoal"));
            VQuad(mGlow, hp - s.n * 0.04 - s.f * 0.42, hp - s.n * 0.04 + s.f * 0.42, PaveY + 2.03, PaveY + 2.87, A["sign_H"], toRoad);
            VQuad(mGlow, hp + s.n * 0.04 + s.f * 0.42, hp + s.n * 0.04 - s.f * 0.42, PaveY + 2.03, PaveY + 2.87, A["sign_H"], s.n);
            V2 door = s.Corner(-bW / 4, bIn - 5.5);
            AddPlace(s, door, toRoad, false, door, toRoad);
        }

        static void Ambulance(V2 c, V2 nose)
        {
            V2 f = nose.Norm, l = f.Left;
            SBox(mSolid, c - f * 0.6, f, 2.0, 1.15, 0.45, 2.9, S("ambyellow"));                    // box body
            SBox(mSolid, c + f * 2.0, f, 0.75, 1.1, 0.45, 2.1, S("ambyellow"));                    // cab
            SBox(mSolid, c + f * 2.55, f, 0.22, 1.05, 1.3, 2.0, S("glass"));                       // windscreen
            foreach (int s in new[] { -1, 1 })
                VQuad(mSolid, c - f * 2.55 + l * (s * 1.16), c + f * 1.35 + l * (s * 1.16), 0.9, 1.5, A["battenburg"], l * s);
            SBox(mGlow, c + f * 1.35, l, 0.9, 0.12, 2.9, 3.05, S("bluelight"));                    // light bar
            foreach (int a in new[] { -1, 1 }) foreach (int s in new[] { -1, 1 })
                    Cyl(mSolid, c + f * (a > 0 ? 2.0 : -1.6) + l * (s * 1.05), 0.38, 0.0, 0.5, 8, S("black"));
            Collider("Ambulance", c, f, 6.0, 2.4, 0, 2.9);
        }

        // ------------------------------------------------------------------ police station
        static void PoliceStation(PlaceSite s)
        {
            V2 toRoad = s.n * -1;
            HQuad(mSolid, s.Corner(0, s.depth / 2), s.n, s.depth / 2, s.len / 2, 0.03, S("slab"));
            double bIn = 8, bW = s.len - 4, bD = s.depth - bIn - 0.5;
            Block(s, 0, bIn, bW, bD, s.height, "brick", "officewin", 1.4, s.height - 1.2, 4.5);
            V2 face = s.Corner(0, bIn - 0.06);
            VQuad(mSolid, face - s.f * 1.4, face + s.f * 1.4, 0, 2.8, S("glass"), toRoad);            // doors
            SBox(mSolid, s.Corner(0, bIn - 1.0), s.f, 2.2, 1.0, 0, 0.3, S("concrete"));              // step
            SignBoard(s.Corner(0, bIn - 0.1), toRoad, s.f, 2.0, 3.0, 3.7, "sign_POLICE");
            SignBoard(s.Corner(0, bIn - 0.1), toRoad, s.f, 8.5, s.height + 0.5, s.height + 1.5, "sign_" + s.id);
            // the blue lamp, on a bracket over the door
            V2 lamp = s.Corner(2.6, bIn - 0.8);
            SBox(mSolid, s.Corner(2.6, bIn - 0.4), s.n, 0.4, 0.04, 3.95, 4.02, S("black"));
            SBox(mGlow, lamp, s.f, 0.28, 0.28, 3.6, 4.2, S("bluelamp"));
            SBox(mSolid, lamp, s.f, 0.32, 0.32, 4.2, 4.32, S("black"));
            // parked patrol cars on the forecourt (your PoliceCar prefab, merged like the bus shelters)
            for (int i = 0; i < 2; i++)
            {
                V2 p = s.Corner(-s.len / 2 + 4 + i * 5.5, 3.5);
                Prefab("PoliceCar", p, 0, s.n);
                Collider("ParkedPoliceCar", p, s.n, 4.6, 2.0, 0, 1.6);
            }
            AddPlace(s, s.Corner(0, bIn - 3.5), toRoad, false, s.front, toRoad);
        }

        // ------------------------------------------------------------------ safehouse with driveway
        static void Safehouse(PlaceSite s)
        {
            V2 toRoad = s.n * -1;
            string wall = s.style == "stucco" ? "stucco" : s.style == "modern" ? "modernwall" : "brick";
            string doorCol = s.style == "stucco" ? "navy" : s.style == "modern" ? "charcoal" : "postred";
            double houseW = 9.5, houseIn = 3.5, houseD = 9;
            double houseAlong = -s.len / 2 + houseW / 2 + 0.5;
            Block(s, houseAlong, houseIn, houseW, houseD, s.height, wall, "homewin", 1.0, s.height - 0.8, 2.2);
            // front door, porch light, step, path
            V2 df = s.Corner(houseAlong, houseIn - 0.06);
            VQuad(mSolid, df - s.f * 0.55, df + s.f * 0.55, 0, 2.3, S(doorCol), toRoad);
            SBox(mGlow, s.Corner(houseAlong + 0.95, houseIn - 0.15), s.f, 0.1, 0.1, 2.35, 2.6, S("safegreen"));   // green lamp = safehouse
            SBox(mSolid, s.Corner(houseAlong, houseIn - 0.6), s.f, 0.8, 0.6, 0, 0.18, S("concrete"));
            HQuad(mSolid, s.Corner(houseAlong, houseIn / 2), s.n, houseIn / 2, 0.6, 0.03, S("slab"));
            // front garden + low wall with a gate gap
            HQuad(mSolid, s.Corner(houseAlong, houseIn / 2), s.n, houseIn / 2, houseW / 2, 0.015, S("lawn"));
            double wallFrom = -s.len / 2, gate0 = houseAlong - 0.8, gate1 = houseAlong + 0.8, drive0 = houseAlong + houseW / 2 + 0.6;
            SBox(mSolid, s.Corner((wallFrom + gate0) / 2, 0.15), s.f, (gate0 - wallFrom) / 2, 0.12, 0, 0.75, S(wall));
            SBox(mSolid, s.Corner((gate1 + drive0) / 2, 0.15), s.f, (drive0 - gate1) / 2, 0.12, 0, 0.75, S(wall));
            // driveway beside the house: paved strip, the Kestrel parks nose-in
            double driveC = (drive0 + s.len / 2) / 2, driveHalf = (s.len / 2 - drive0) / 2;
            HQuad(mSolid, s.Corner(driveC, s.depth / 2), s.n, s.depth / 2, driveHalf, 0.035, S("drive"));
            HQuad(mSolid, s.front - s.n * 0.8 + s.f * driveC, s.n, 0.8, driveHalf, PaveY + 0.004, S("drive"));   // dropped kerb look
            SBox(mSolid, s.Corner(s.len / 2 + 0.1, s.depth / 2), s.n, s.depth / 2, 0.12, 0, 1.6, S(wall));     // side wall
            SBox(mSolid, s.Corner(driveC, s.depth + 0.1), s.f, driveHalf + 0.2, 0.12, 0, 1.6, S(wall));       // back wall
            V2 door = s.Corner(houseAlong, 1.4);
            V2 car = s.Corner(driveC, 6.5);
            AddPlace(s, door, toRoad, true, car, s.n);
        }
    }
}
