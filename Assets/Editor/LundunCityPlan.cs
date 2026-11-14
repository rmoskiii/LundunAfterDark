// V2a: the city plan. Pure C# (no UnityEngine), so it can be tested outside Unity.
// The city used to be rows of buildings with holes in them, on a lawn. This makes it read as one built-up place:
//   street walls   every road is walked again, both sides; wherever the frontage has a hole (an empty lot, a plot that
//                  didn't fit, land that was in no district at all) new buildings are fitted in, on one straight
//                  building line, sized to the hole, so streets are built up end to end
//   the edges      the land between the districts and the Ring, and the far side of the Ring (east and west), gets
//                  the same treatment in the style of the nearest district, so the city doesn't stop at a field
//   left open      the river side of the embankments (the view), parks, the railway and its arches, hospitals,
//                  police stations, safehouses, the Wheel, and every spot a mission uses
// Nothing that was already there moves: every existing plot keeps its position, footprint and order (the new plots
// are added after them, with their own random numbers), so missions, shops, homes and the maps still line up.
// What stands on each plot (high-street parade, terrace, corner shop…) is decided in LundunBuildingKit.PlanBuilding
// from the street's role (StreetRole below).
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        /// Set false to build the city as it was before V2a (holes and all).
        public static bool UseCityPlan = true;
        /// How many plots the plan added last time (for the log and tests).
        public static int CityPlanAdded;
        /// The plots the plan added (so the building kit can tell them from the originals).
        static readonly HashSet<BuildingInfo> planned = new HashSet<BuildingInfo>();
        public static bool IsPlanned(BuildingInfo b) { return planned.Contains(b); }
        /// Is this point inside (or within 'grow' of) one of the plan's buildings?
        static bool InPlanned(V2 q, double grow)
        {
            foreach (BuildingInfo b in planned)
            {
                V2 d = q - b.c; V2 f = b.dir.Norm;
                if (Math.Abs(V2.Dot(d, f)) < b.len / 2 + grow && Math.Abs(V2.Dot(d, f.Left)) < b.dep / 2 + grow) return true;
            }
            return false;
        }

        // Mission spots that aren't on a road (from the story scripts): nothing is built within reach of them.
        static readonly double[] PlanKeepClear =
        {
            174, 1040, 265, 240, 330, 380, 380, 562, 560, 945, 690, 860, 777, 396, 804, 339, 880, 538, 989, 104, 990, 562,
            1180, 812, 1180, 815, 1220, 560, 1260, 780, 1287, 477,
            // Brixtun
            40, -45, 428, -56, 451, -73, 484, -66, 519, -66, 1106, -96, 1108, -176, 1142, -135, 1177, -96, 1110, -600, 1110, -560, 1272, -560, 1420, -350, 1492, -560,
            // Kingsmere
            452, 1740, 560, 1790, 560, 1822, 560, 1850, 600, 1888, 616, 1700, 625, 1790, 655, 1790, 662, 1822, 672, 1830,
        };

        // ------------------------------------------------------------------ what a street is for
        public enum StreetRole { HighStreet, Local, Residential, Estate, Works, Grand, Ring }

        // The high streets: shops end to end, flats above. (Rye Lane, Peckam High Street and Old Kent Lane always were.)
        static readonly HashSet<string> HighStreets = new HashSet<string>
        {
            "Rye Lane", "Peckam High Street", "Old Kent Lane", "Oxbridge Street", "Regent Row", "Brompton Road", "Commercial Way",
            "Gilmore Road", "Westferry Road",
            "Brixtun Road", "Coldharbor Lane", "Acre Lane", "Electric Parade", "Atlantic Road", "Ferndale Way",
            "Admiral's Walk", "Pall Row",
        };
        // Local streets: a parade of shops at each junction, houses in between.
        static readonly HashSet<string> LocalStreets = new HashSet<string>
        {
            "Bellenden Road", "Choumert Grove", "Thurlow Street", "Mayfield Street", "Sloane Lane", "Dock Street", "Wharf Road",
            "Tower Plaza", "Quay Loop", "North Embankment", "South Embankment",
            "Stockwell Road", "Loughborough Road", "Denmore Hill", "Somerleyton Road", "Herne Lane", "Saltoun Road", "Tulse Lane",
            "Regency Street", "Spur Road",
        };

        public static StreetRole RoleOf(string roadName, string cls)
        {
            if (cls == "ring") return StreetRole.Ring;
            if (HighStreets.Contains(roadName)) return StreetRole.HighStreet;
            if (LocalStreets.Contains(roadName)) return StreetRole.Local;
            if (cls == "arterial") return StreetRole.HighStreet;
            if (cls == "service") return StreetRole.Works;
            return StreetRole.Residential;
        }

        // ------------------------------------------------------------------ filling the frontage
        class PlanRule { public double lenMin, lenMax, dep, depMin, hMin, hMax; }

        static PlanRule PlanRuleFor(string district, StreetRole role, Random pr)
        {
            switch (district)
            {
                case "D_PECKAM":
                    return role == StreetRole.HighStreet ? new PlanRule { lenMin = 8, lenMax = 40, dep = 12, depMin = 8, hMin = 9.5, hMax = 12 }
                                                          : new PlanRule { lenMin = 8, lenMax = 36, dep = 11, depMin = 8, hMin = 9, hMax = 11 };
                case "D_KNIGHT": return new PlanRule { lenMin = 10, lenMax = 40, dep = 20, depMin = 12, hMin = 20, hMax = 26 };
                case "D_GILMOR": return new PlanRule { lenMin = 14, lenMax = 56, dep = 12, depMin = 10, hMin = 12, hMax = 15 };
                case "D_CANARY": return new PlanRule { lenMin = 14, lenMax = 42, dep = 18, depMin = 12, hMin = 12, hMax = 18 };
                case "D_CANARY_PEN": return new PlanRule { lenMin = 16, lenMax = 30, dep = 20, depMin = 14, hMin = 16, hMax = 24 };
            }
            return new PlanRule { lenMin = 8, lenMax = 36, dep = 11, depMin = 8, hMin = 9, hMax = 11 };
        }

        /// The nearest district to a point that is in none (the edges take on their neighbour's look).
        static string NearestDistrict(V2 q, Dictionary<string, List<V2>> dpoly)
        {
            string best = null; double bd = double.MaxValue;
            foreach (var kv in dpoly)
            {
                if (kv.Key == "D_CANARY_PEN") continue;                 // the towers stay on the peninsula
                double d = EdgeDist(q, kv.Value);
                if (d < bd) { bd = d; best = kv.Key; }
            }
            return best;
        }

        /// Called by BuildBuildings once the original plots are placed (and the hospital / police / safehouse plots
        /// cleared): fits new buildings into every hole in the frontage. Uses only its own random numbers.
        static void CityPlanInfill(GreyboxResult R, Dictionary<string, List<V2>> dpoly)
        {
            planned.Clear(); CityPlanAdded = 0;
            if (!UseCityPlan) return;
            Random pr = new Random(Brix ? 2251 : Kingsmere ? 2252 : 2250);

            // keep clear: hospitals / police / safehouses (their own buildings stand there), the railway and the room
            // in front of its arches, the Wheel, mission spots
            for (int i = 0; i + 1 < PlanKeepClear.Length; i += 2)
            {
                V2 k = new V2(PlanKeepClear[i], PlanKeepClear[i + 1]);
                MarkSegment(k, k, 16);
            }
            if (Kingsmere) foreach (double[] z in KingsmereRestricted) MarkRect(z[0] - 6, z[2] + 6, z[1] - 6, z[3] + 6);   // the palace, its forecourt, the barracks
            if (!ExpansionMode)
            {
                foreach (PlaceSite s in PlanPlaces())
                {
                    if (!s.ok) continue;
                    for (double a = -s.len / 2 - 3; a <= s.len / 2 + 3; a += 1.5)
                        for (double b = -2; b <= s.depth + 3; b += 1.5) { int ci = CellIndex(s.Corner(a, b)); if (ci >= 0) blocked[ci] = true; }
                }
                if (railPlan != null)
                    for (int i = 0; i + 1 < railPlan.line.p.Count; i++)
                    {
                        double s = (railPlan.line.ch[i] + railPlan.line.ch[i + 1]) / 2;
                        double r = (RailKind(s) == 'E' ? RailHW + RailDeck * BankSlope + 4 : RailStationHW + 14);
                        MarkSegment(railPlan.line.p[i], railPlan.line.p[i + 1], r);
                    }
                MarkSegment(WheelSite, WheelSite, 46);
            }

            // the land the edges may be built on: inside the Ring north and south (the expansions start beyond it),
            // out to the edge of the grid east and west. The expansions only fill inside their own districts.
            List<V2> edgeLand = new List<V2> { new V2(3, 44), new V2(1597, 44), new V2(1597, 1156), new V2(3, 1156) };

            List<string> ids = new List<string>(roads.Keys); ids.Sort(StringComparer.Ordinal);
            // high streets first, so their walls are the most complete; then the rest
            ids.Sort((a, b) =>
            {
                int ra = (int)RoleOf(roads[a].r.name, roads[a].cls), rb = (int)RoleOf(roads[b].r.name, roads[b].cls);
                return ra != rb ? ra.CompareTo(rb) : string.CompareOrdinal(a, b);
            });
            foreach (string id in ids)
            {
                RoadInfo ri = roads[id];
                if (ri.cls == "bridge") continue;
                StreetRole role = RoleOf(ri.r.name, ri.cls);
                double L = ri.line.Length;
                for (int s = 1; s >= -1; s -= 2)
                {
                    double ch = 3;
                    while (ch < L - 3)
                    {
                        V2 dir; V2 p = ri.line.At(ch, out dir);
                        V2 probe = p + dir.Left * (s * (ri.w / 2 + 20));
                        string did = DistrictAt(probe, dpoly);
                        List<V2> land;
                        if (did != null) land = dpoly[did];
                        else
                        {
                            if (ExpansionMode || !InPoly(probe, edgeLand)) { ch += 6; continue; }
                            did = NearestDistrict(probe, dpoly); land = edgeLand;
                            if (did == null) { ch += 6; continue; }
                        }
                        // the embankments keep their river side open
                        if (river != null && river.DistanceTo(probe) < WallOuter + 34) { ch += 6; continue; }
                        PlanRule rule = PlanRuleFor(did, role, pr);
                        double front = ri.w / 2 + Math.Max(ri.pave, ri.verge) + 2.6;       // one straight building line
                        bool placed = false;
                        // the longest building that fits here, down to the shortest; the full depth, or shallower
                        for (double len = Math.Min(rule.lenMax, L - 3 - ch); len >= rule.lenMin - 0.01 && !placed; len -= 2)
                        {
                            V2 d2; V2 mid = ri.line.At(ch + len / 2, out d2);
                            V2 e0, e1; V2 a0 = ri.line.At(ch, out e0), a1 = ri.line.At(ch + len, out e1);
                            if (V2.Dot(e0, e1) < 0.985) continue;                        // not round a bend
                            for (double dep = rule.dep; dep >= rule.depMin - 0.01 && !placed; dep -= 2)
                            {
                                V2 c = mid + d2.Left * (s * (front + dep / 2));
                                double h = rule.hMin + pr.NextDouble() * (rule.hMax - rule.hMin);
                                if (!TryPlace(R, did, land, c, d2, len, dep, h, 1.0)) continue;
                                planned.Add(R.buildings[R.buildings.Count - 1]);
                                CityPlanAdded++; placed = true;
                                ch += len + 1.0 + (pr.NextDouble() < 0.14 ? 3.0 : 0);      // now and then an alley through to the back
                            }
                        }
                        if (!placed) ch += 2;
                    }
                }
            }
            R.log.Add("  city plan: " + CityPlanAdded + " new buildings fitted into the gaps (streets built up end to end)");
        }
    }
}
