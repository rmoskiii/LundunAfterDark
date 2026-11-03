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
                // James's home (the story starts here): a modern three-storey house on Choumert Grove, just off Peckam High Street.
                // Same id as the old high-street spot it replaces, so a home saved there still finds it.
                new PlaceSite { id = "SAFE_PECKAM", kind = "safehouse", name = "Peckam Safehouse", roadId = "R22", anchorX = 990, anchorZ = 120, len = 17, depth = 16, height = 10.5, style = "villa" },
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
                else if (s.style == "villa") Villa(s);
                else Safehouse(s);
                D.Count(s.kind == "hospital" ? "hospitals" : s.kind == "police" ? "police stations" : "safehouses");
            }
            DressLagos();                           // S3: LAGOS FOODS, the walk-in shop on Rye Lane
            // Peckam Safehouse: a real house on Choumert Grove now (it used to be a door on the old high street, from the
            // spawn points). Only if its plot couldn't be fitted does the old spot stand in, so there's always a home.
            bool peckam = false;
            foreach (PlaceInfo p in D.places) if (p.id == "SAFE_PECKAM") peckam = true;
            LwSpawn start = null, car = null;
            foreach (LwSpawn sp in W.spawns) { if (sp.id == "SP_PLAYER_START") start = sp; if (sp.id == "SP_KESTREL") car = sp; }
            if (!peckam && start != null)
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

        // ------------------------------------------------------------------ James's home: a modern three-storey house
        // Same plot, door and driveway as the brick safehouse (so the spawn and the Kestrel's spot don't move).
        // From the road: a timber-slat column up the middle with the front door at its foot; on the left a grey
        // frame with a glass balcony and a white box with a perforated screen on top; on the right a grey frame
        // with a glass balcony on each floor and white fins over the roof; a roof terrace with a pergola and planters;
        // slatted timber front fence on a grey plinth, white pillars with lamps; the driveway beside it as before.
        static void Villa(PlaceSite s)
        {
            V2 toRoad = s.n * -1;
            const double hw = 4.75, bF = 3.5, bB = 12.5, FH = 3.2, roof = 9.6;
            double ac = -s.len / 2 + hw + 0.5;                       // house centre = front door (same spot as before)
            double a0 = ac - hw, a1 = ac + hw, c0 = ac - 0.95, c1 = ac + 0.95;

            // body, roof deck, parapets
            VBox(s, a0, a1, bF, bB, 0, roof, "villawhite");
            HQuad(mSolid, s.Corner(ac, (bF + bB) / 2), s.n, (bB - bF) / 2 - 0.2, hw - 0.2, roof + 0.02, A["roofdeck"]);
            VBox(s, a0, a1, bB - 0.2, bB, roof, roof + 0.9, "villawhite");
            VBox(s, a0, a0 + 0.2, bF, bB, roof, roof + 0.9, "villawhite");
            VBox(s, a1 - 0.2, a1, bF, bB, roof, roof + 0.9, "villawhite");
            VBox(s, a0, a1, bF - 0.15, bF, 2.95, 3.3, "villawhite");                         // ledge over the ground floor

            // the timber column, front door at its foot under a white canopy
            VBox(s, c0, c1, bF - 0.35, bF + 0.1, 0, roof + 1.6, "villawood");
            int slatRuns = (int)Math.Round((roof + 1.6 - 2.55) / 1.2);
            for (int k = 0; k < slatRuns; k++)          // the slat picture repeated up the column, ~20 cm a slat
            {
                double run = (roof + 1.6 - 2.55) / slatRuns;
                Pane(s, c0, c1, bF - 0.36, 2.55 + k * run, 2.55 + (k + 1) * run, "villaslats", false);
            }
            Pane(s, ac - 0.55, ac + 0.55, bF - 0.37, 0.15, 2.45, "villadoor", false);
            Pane(s, ac + 0.6, ac + 0.85, bF - 0.37, 0.15, 2.45, "villawin_lit", true);       // glass strip beside the door
            Pane(s, c0 + 0.1, ac - 0.6, bF - 0.37, 0.15, 2.45, "villawood", false);
            VBox(s, ac - 1.4, ac + 1.4, bF - 1.55, bF - 0.35, 2.7, 2.85, "villawhite");         // canopy
            SBox(mGlow, s.Corner(ac, bF - 0.95), s.f, 0.18, 0.18, 2.66, 2.7, A["warmlamp"]);    // downlight
            VBox(s, ac - 0.9, ac + 0.9, bF - 1.15, bF - 0.35, 0, 0.15, "concrete");             // step
            SBox(mGlow, s.Corner(c1 + 0.3, bF - 0.12), s.f, 0.1, 0.1, 2.2, 2.5, A["safegreen"]);  // green lamp = safehouse

            // ---- left side: big ground-floor window; grey frame above; glass balcony; white box with the perforated screen
            double l0 = a0, l1 = c0;
            Window(s, l0 + 0.5, l1 - 0.4, bF, 0.6, 2.7, true);
            VBox(s, l0, l1, bF - 0.7, bF, 3.05, 3.3, "villagrey");
            VBox(s, l0, l0 + 0.3, bF - 0.7, bF, 3.05, roof + 0.9, "villagrey");
            VBox(s, l1 - 0.3, l1, bF - 0.7, bF, 3.05, roof + 0.9, "villagrey");
            VBox(s, l0, l1, bF - 0.7, bF, roof, roof + 0.9, "villagrey");
            VBox(s, l0 + 0.3, l1 - 0.3, bF - 0.7, bF, 6.25, 6.4, "villagrey");
            Window(s, l0 + 0.5, l1 - 0.5, bF, FH + 0.25, 2 * FH - 0.3, false);
            Balcony(s, l0 + 0.3, l1 - 0.3, bF - 1.75, bF - 0.7, FH + 0.15);
            // top floor: a white box standing proud of the frame, screen on the left, window on the right
            VBox(s, l0 + 0.3, l1 - 0.3, bF - 1.05, bF - 0.7, 2 * FH, 2 * FH + 0.2, "villawhite");
            VBox(s, l0 + 0.3, l1 - 0.3, bF - 1.05, bF - 0.7, roof - 0.25, roof, "villawhite");
            VBox(s, l0 + 0.3, l0 + 0.5, bF - 1.05, bF - 0.7, 2 * FH, roof, "villawhite");
            VBox(s, l1 - 0.5, l1 - 0.3, bF - 1.05, bF - 0.7, 2 * FH, roof, "villawhite");
            double scr = l0 + 0.5 + 1.45;
            VBox(s, scr - 0.06, scr + 0.06, bF - 1.05, bF - 0.7, 2 * FH, roof, "villawhite");
            Pane(s, l0 + 0.5, scr - 0.06, bF - 0.9, 2 * FH + 0.2, roof - 0.25, "villaperf", false);
            Pane(s, scr + 0.1, l1 - 0.55, bF - 0.02, 2 * FH + 0.4, roof - 0.45, "villawin_lit", true);

            // ---- right side: full-height glass on the ground floor; grey frame with a balcony on each floor; fins
            double r0 = c1, r1 = a1;
            Window(s, r0 + 0.35, r1 - 0.35, bF, 0.15, 2.75, true);
            VBox(s, r0, r1, bF - 0.6, bF, 3.05, 3.3, "villagrey");
            VBox(s, r0, r0 + 0.3, bF - 0.6, bF, 3.05, roof + 0.3, "villagrey");
            VBox(s, r1 - 0.3, r1, bF - 0.6, bF, 3.05, roof + 0.3, "villagrey");
            VBox(s, r0, r1, bF - 0.6, bF, roof, roof + 0.3, "villagrey");
            for (int fl = 1; fl <= 2; fl++)
            {
                double y = fl * FH;
                Window(s, r0 + 0.5, r1 - 0.5, bF, y + 0.35, y + 2.75, fl == 2);
                VBox(s, r0 + 0.3, r1 - 0.3, bF - 0.85, bF - 0.6, y + 2.9, y + 3.05 - (fl == 2 ? 0.4 : 0), "villawhite");   // white lintel
                Balcony(s, r0 + 0.3, r1 - 0.3, bF - 1.6, bF - 0.6, y + 0.1);
            }
            VBox(s, r0 + 0.3, r1 - 0.3, bF + 0.02, bF + 0.12, roof + 0.3, roof + 1.1, "balglass");    // roof terrace glass
            Pane(s, r0 + 0.3, r1 - 0.3, bF + 0.015, roof + 0.3, roof + 1.1, "villaglass", false);
            for (int k = 0; k < 4; k++)
            {
                double a = r1 - 0.2 - k * 0.32;
                VBox(s, a - 0.035, a + 0.035, bF - 0.6, bF + 0.5, roof + 0.3, roof + 2.7, "villawhite");
            }

            // ---- roof terrace: pergola over the left / middle, planters along the front, stair housing at the back
            double p0 = a0 + 0.45, p1 = c1 + 0.2, q0 = bF + 0.9, q1 = bF + 5.2, top = roof + 2.6;
            foreach (double pa in new[] { p0, p1 }) foreach (double pb in new[] { q0, q1 })
                    VBox(s, pa - 0.07, pa + 0.07, pb - 0.07, pb + 0.07, roof, top, "villagrey");
            VBox(s, p0 - 0.1, p1 + 0.1, q0 - 0.08, q0 + 0.08, top - 0.22, top, "villagrey");
            VBox(s, p0 - 0.1, p1 + 0.1, q1 - 0.08, q1 + 0.08, top - 0.22, top, "villagrey");
            for (double a = p0 + 0.15; a <= p1 - 0.1; a += 0.38) VBox(s, a - 0.045, a + 0.045, q0 - 0.3, q1 + 0.3, top, top + 0.12, "villawood");
            VBox(s, a0 + 0.3, c0 - 0.1, bF + 0.25, bF + 0.75, roof, roof + 0.55, "planter");
            VBox(s, a0 + 0.35, c0 - 0.15, bF + 0.3, bF + 0.7, roof + 0.55, roof + 0.8, "leaf1");
            VBox(s, a1 - 3.2, a1 - 0.25, bB - 3.4, bB - 0.25, roof, roof + 2.7, "villagrey");
            VBox(s, a1 - 3.3, a1 - 0.15, bB - 3.5, bB - 0.15, roof + 2.7, roof + 2.85, "villawhite");
            Pane(s, a1 - 2.2, a1 - 1.2, bB - 3.42, roof + 0.05, roof + 2.2, "villawin_dark", false);

            // ---- sides and back (seen from the drive and the street corner)
            for (int fl = 0; fl < 3; fl++)
            {
                double y = fl * FH;
                SidePane(s, a1 + 0.02, bF + 2.2, bF + 5.6, y + 0.7, y + 2.6, fl != 1 ? "villawin_lit" : "villawin_dark", fl != 1, 1);
                VBox(s, a1, a1 + 0.12, bF + 2.1, bF + 5.7, y + 0.6, y + 0.7, "villagrey");
                SidePane(s, a0 - 0.02, bF + 3.0, bF + 5.0, y + 0.8, y + 2.6, fl == 1 ? "villawin_lit" : "villawin_dark", fl == 1, -1);
                foreach (double wa in new[] { ac - 2.6, ac + 2.6 })
                    VQuad(fl == 0 ? mGlow : mSolid, s.Corner(wa - 1.1, bB + 0.02), s.Corner(wa + 1.1, bB + 0.02), y + 0.7, y + 2.6,
                          A[fl == 0 ? "villawin_lit" : "villawin_dark"], s.n);
            }

            // ---- front garden: lawn, path, planter under the left window
            HQuad(mSolid, s.Corner(ac, bF / 2), s.n, bF / 2, hw, 0.015, S("lawn"));
            HQuad(mSolid, s.Corner(ac, bF / 2), s.n, bF / 2, 0.7, 0.03, S("slab"));
            VBox(s, l0 + 0.5, l1 - 0.4, bF - 1.6, bF - 1.0, 0, 0.5, "planter");
            VBox(s, l0 + 0.55, l1 - 0.45, bF - 1.55, bF - 1.05, 0.5, 0.75, "leaf2");
            VBox(s, r0 + 0.5, r1 - 0.3, bF - 1.5, bF - 0.95, 0, 0.45, "planter");
            VBox(s, r0 + 0.55, r1 - 0.35, bF - 1.45, bF - 1.0, 0.45, 0.7, "leaf3");

            // ---- front fence: grey plinth, timber slats, white pillars (lamps on the gate and drive pillars)
            double wallFrom = -s.len / 2, gate0 = ac - 0.8, gate1 = ac + 0.8, drive0 = a1 + 0.6;
            foreach (double[] seg in new[] { new[] { wallFrom + 0.2, gate0 }, new[] { gate1, drive0 } })
            {
                double x0 = seg[0], x1 = seg[1];
                VBox(s, x0, x1, 0.03, 0.33, 0, 0.45, "villagrey");
                VBox(s, x0, x1, 0.1, 0.26, 0.45, 1.35, "villawood");
                Pane(s, x0, x1, 0.09, 0.45, 1.35, "villaslats", false);
                Collider("SafehouseFence", s.Corner((x0 + x1) / 2, 0.18), s.f, x1 - x0, 0.4, 0, 1.5);
            }
            foreach (double pa in new[] { wallFrom + 0.2, gate0, gate1, drive0 })
            {
                VBox(s, pa - 0.2, pa + 0.2, -0.02, 0.38, 0, 1.55, "villawhite");
                if (pa != wallFrom + 0.2) SBox(mGlow, s.Corner(pa, 0.18), s.f, 0.12, 0.12, 1.55, 1.68, A["warmlamp"]);
            }
            VBox(s, wallFrom, wallFrom + 0.25, 0.03, bF, 0, 0.45, "villagrey");                  // low wall up the left side

            // ---- driveway beside the house (as before): paved strip, dropped kerb, rendered side and back walls
            double driveC = (drive0 + s.len / 2) / 2, driveHalf = (s.len / 2 - drive0) / 2;
            HQuad(mSolid, s.Corner(driveC, s.depth / 2), s.n, s.depth / 2, driveHalf, 0.035, S("drive"));
            HQuad(mSolid, s.front - s.n * 0.8 + s.f * driveC, s.n, 0.8, driveHalf, PaveY + 0.004, S("drive"));
            SBox(mSolid, s.Corner(s.len / 2 + 0.1, s.depth / 2), s.n, s.depth / 2, 0.12, 0, 1.6, S("villawhite"));
            SBox(mSolid, s.Corner(s.len / 2 + 0.1, s.depth / 2), s.n, s.depth / 2, 0.16, 1.6, 1.7, S("villagrey"));
            SBox(mSolid, s.Corner(driveC, s.depth + 0.1), s.f, driveHalf + 0.2, 0.12, 0, 1.6, S("villawhite"));
            SBox(mSolid, s.Corner(driveC, s.depth + 0.1), s.f, driveHalf + 0.2, 0.16, 1.6, 1.7, S("villagrey"));
            SBox(mGlow, s.Corner(s.len / 2 - 0.05, bF + 2.0), s.f, 0.06, 0.12, 1.2, 1.4, A["warmlamp"]);   // wall lights along the drive
            SBox(mGlow, s.Corner(s.len / 2 - 0.05, bF + 8.0), s.f, 0.06, 0.12, 1.2, 1.4, A["warmlamp"]);
            Collider("SafehouseWall", s.Corner(s.len / 2 + 0.1, s.depth / 2), s.n, s.depth, 0.3, 0, 1.7);
            Collider("SafehouseWall", s.Corner(driveC, s.depth + 0.1), s.f, driveHalf * 2 + 0.4, 0.3, 0, 1.7);

            // the house itself
            Collider("PlaceBuilding", s.Corner(ac, (bF + bB) / 2), s.f, 2 * hw, bB - bF, 0, roof + 0.9);
            Collider("PlaceBuilding", s.Corner(ac, bF - 0.15), s.f, 1.9, 0.5, 0, roof + 1.6);

            AddPlace(s, s.Corner(ac, 1.4), toRoad, true, s.Corner(driveC, 6.5), s.n);
        }

        /// Box between along a0..a1 and inward b0..b1 (plot coordinates), one colour.
        static void VBox(PlaceSite s, double a0, double a1, double b0, double b1, double y0, double y1, string sw)
        {
            SBox(mSolid, s.Corner((a0 + a1) / 2, (b0 + b1) / 2), s.f, Math.Abs(a1 - a0) / 2, Math.Abs(b1 - b0) / 2, y0, y1, A[sw]);
        }

        /// Picture on the front (facing the road) at depth b.
        static void Pane(PlaceSite s, double a0, double a1, double b, double y0, double y1, string tile, bool glow)
        {
            VQuad(glow ? mGlow : mSolid, s.Corner(a0, b), s.Corner(a1, b), y0, y1, A[tile], s.n * -1);
        }

        /// Picture on a side wall at along a, facing +f (dir 1) or -f (dir -1).
        static void SidePane(PlaceSite s, double a, double b0, double b1, double y0, double y1, string tile, bool glow, int dir)
        {
            VQuad(glow ? mGlow : mSolid, s.Corner(a, b0), s.Corner(a, b1), y0, y1, A[tile], s.f * dir);
        }

        /// Window in the front wall (face at b): glazing, slim dark frame, white sill.
        static void Window(PlaceSite s, double a0, double a1, double b, double y0, double y1, bool lit)
        {
            Pane(s, a0, a1, b - 0.03, y0, y1, lit ? "villawin_lit" : "villawin_dark", lit);
            VBox(s, a0 - 0.08, a1 + 0.08, b - 0.1, b, y1, y1 + 0.08, "villagrey");
            VBox(s, a0 - 0.08, a0, b - 0.1, b, y0, y1, "villagrey");
            VBox(s, a1, a1 + 0.08, b - 0.1, b, y0, y1, "villagrey");
            VBox(s, a0 - 0.12, a1 + 0.12, b - 0.18, b, y0 - 0.08, y0, "villawhite");
        }

        /// White slab with frameless glass on three sides (floor at y).
        static void Balcony(PlaceSite s, double a0, double a1, double bOut, double bIn, double y)
        {
            VBox(s, a0, a1, bOut, bIn, y, y + 0.18, "villawhite");
            VBox(s, a0, a1, bOut, bOut + 0.06, y + 0.18, y + 1.15, "balglass");
            VBox(s, a0, a0 + 0.06, bOut, bIn, y + 0.18, y + 1.15, "balglass");
            VBox(s, a1 - 0.06, a1, bOut, bIn, y + 0.18, y + 1.15, "balglass");
            Pane(s, a0, a1, bOut - 0.005, y + 0.18, y + 1.15, "villaglass", false);          // light glass with reflections
            SidePane(s, a0 - 0.005, bOut, bIn, y + 0.18, y + 1.15, "villaglass", false, -1);
            SidePane(s, a1 + 0.005, bOut, bIn, y + 0.18, y + 1.15, "villaglass", false, 1);
        }

        /// The house's colours and pictures, added at the end of the city's atlas so nothing already there moves.
        static void VillaAtlas(DressAtlas a)
        {
            a.Swatch("villawhite", Col.Hex("eeeeea"));
            a.Swatch("villagrey", Col.Hex("54585d"));
            a.Swatch("villawood", Col.Hex("8f5a32"));
            a.Swatch("balglass", Col.Hex("bcd3d8"));
            a.Swatch("roofdeck", Col.Hex("7a766f"));
            a.Swatch("planter", Col.Hex("3a3d40"));
            a.Swatch("warmlamp", Col.Hex("ffd08a"));
            a.Swatch("villadoor", Col.Hex("4a2e1a"));
            Random r = new Random(4417);
            int x, y;            // small tiles: the atlas is nearly full, and they're repeated / seen at street distance
            // horizontal timber slats with dark gaps (6 slats; repeated up the column)
            if (a.Alloc("villaslats", 32, 60, out x, out y))
            {
                a.Fill(x, y, 32, 60, Col.Hex("2a1a10"));
                for (int k = 0; k < 6; k++)
                {
                    Col c = Col.Hex("a36a3c").Lerp(Col.Hex("7b4a28"), r.NextDouble());
                    a.FillV(x, y + k * 10 + 2, 32, 8, c.Scale(0.85), c.Scale(1.08));
                }
            }
            // white perforated screen: clusters of round holes of different sizes
            if (a.Alloc("villaperf", 32, 60, out x, out y))
            {
                a.Fill(x, y, 32, 60, Col.Hex("f1f0ec"));
                for (int j = 0; j < 6; j++)
                    for (int i = 0; i < 3; i++)
                    {
                        double cx = x + 6 + i * 10 + (r.NextDouble() - 0.5) * 4, cy = y + 6 + j * 10 + (r.NextDouble() - 0.5) * 4;
                        a.Disc(cx, cy, 1.6 + r.NextDouble() * 2.2, Col.Hex("26292c"));
                    }
                a.Frame(x, y, 32, 60, 2, Col.Hex("f1f0ec"));
            }
            // lit window: warm room behind, curtains at the sides, ceiling light, slim frame and mullion
            if (a.Alloc("villawin_lit", 48, 60, out x, out y))
            {
                a.FillV(x, y, 48, 60, Col.Hex("9c6532"), Col.Hex("d8ad6c"));
                a.Disc(x + 24, y + 52, 7, Col.Hex("f5dca8", 150));
                a.FillV(x + 2, y + 2, 6, 56, Col.Hex("6e4626"), Col.Hex("8a5a33"));
                a.FillV(x + 40, y + 2, 6, 56, Col.Hex("6e4626"), Col.Hex("8a5a33"));
                a.Fill(x, y + 2, 48, 10, Col.Hex("5a3a22", 140));
                a.Frame(x, y, 48, 60, 2, Col.Hex("2c2f32"));
                a.Fill(x + 23, y, 2, 60, Col.Hex("2c2f32"));
            }
            // balcony glass: pale, with diagonal reflections and a darker foot
            if (a.Alloc("villaglass", 32, 60, out x, out y))
            {
                a.FillV(x, y, 32, 60, Col.Hex("9fbcc3"), Col.Hex("d6e6e9"));
                a.Seg(x + 2, y + 8, x + 16, y + 56, 2.5, new Col(255, 255, 255, 90));
                a.Seg(x + 14, y + 4, x + 30, y + 50, 1.5, new Col(255, 255, 255, 70));
                a.Fill(x, y, 32, 4, Col.Hex("7d979e"));
            }
            // unlit window: dark glass with a sky sheen
            if (a.Alloc("villawin_dark", 48, 60, out x, out y))
            {
                a.FillV(x, y, 48, 60, Col.Hex("141c24"), Col.Hex("3a5163"));
                a.Seg(x + 4, y + 14, x + 26, y + 56, 3.5, new Col(255, 255, 255, 34));
                a.Seg(x + 26, y + 10, x + 45, y + 45, 2, new Col(255, 255, 255, 26));
                a.Frame(x, y, 48, 60, 2, Col.Hex("2c2f32"));
                a.Fill(x + 23, y, 2, 60, Col.Hex("2c2f32"));
            }
        }
    }
}
