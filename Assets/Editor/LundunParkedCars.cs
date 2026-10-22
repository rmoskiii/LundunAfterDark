// L3: where the parked cars go (pure C#, testable outside Unity). Every marked parking bay from L2 is cut into car-sized
// slots. Most slots get a static parked car (baked into big meshes by Lundun → 23, cheap to draw); one in four is left
// for LundunPopulation, which parks real, stealable cars there as you move about. Slots near roadworks are left empty.
// The mix follows the district: vans and hatchbacks in Peckam and Gilmore, big SUVs and sports saloons in Knightsbury.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public class ParkSlot
    {
        public double x, z, yaw;       // centre on the road, facing (degrees, Unity yaw)
        public double length;          // metres of kerb it takes
        public bool forPopulation;     // left empty for a real (stealable) car
        public string model;           // static cars: which vehicle (Veh_<model> prefab)
        public int colour;             // static cars: index into ParkColours (vans mostly white)
        public string district;
    }

    public static partial class Generator
    {
        public static readonly string[] ParkColourNames = { "white", "black", "silver", "grey", "blue", "red", "green", "orange" };
        public static readonly double[][] ParkColours = {
            new[] { 0.92, 0.92, 0.9 }, new[] { 0.05, 0.05, 0.06 }, new[] { 0.62, 0.64, 0.66 }, new[] { 0.28, 0.29, 0.31 },
            new[] { 0.1, 0.18, 0.38 }, new[] { 0.55, 0.06, 0.07 }, new[] { 0.12, 0.25, 0.16 }, new[] { 0.8, 0.4, 0.08 } };
        static readonly double[] CarColourWeights = { 22, 18, 16, 14, 10, 8, 3, 2 };
        static readonly double[] VanColourWeights = { 80, 4, 12, 4, 0, 0, 0, 0 };

        /// Vehicle mix per district: model name → weight. Lengths match LundunVehicleBuilder.TargetLength.
        static readonly Dictionary<string, double[]> ParkMix = new Dictionary<string, double[]>();
        static readonly string[] ParkModels = { "hatchback-sports", "sedan", "suv", "suv-luxury", "sedan-sports", "van", "delivery" };
        static double[] Mix(string district)
        {
            switch (district)
            {
                case "D_KNIGHT": return new double[] { 12, 20, 20, 25, 18, 5, 0 };
                case "D_GILMOR": return new double[] { 28, 28, 12, 0, 6, 20, 6 };
                case "D_CANARY": case "D_CANARY_PEN": return new double[] { 18, 26, 18, 12, 14, 9, 3 };
                default: return new double[] { 25, 25, 12, 4, 6, 22, 6 };
            }
        }
        public static double ModelLength(string m) { return m == "van" || m == "delivery" ? 5.2 : m.StartsWith("suv") ? 4.8 : 4.5; }

        static int Pick(Random r, double[] w)
        {
            double t = 0; foreach (double x in w) t += x;
            double v = r.NextDouble() * t;
            for (int i = 0; i < w.Length; i++) { v -= w[i]; if (v <= 0) return i; }
            return w.Length - 1;
        }

        public static List<ParkSlot> ParkingSlots(StreetResult sr)
        {
            List<ParkSlot> slots = new List<ParkSlot>();
            Random r = new Random(4041);
            if (sDpoly == null) { sDpoly = new Dictionary<string, List<V2>>(); foreach (LwPoly d in W.districts) sDpoly[d.id] = Pts(d.polygon); sDpolyWorld = W; }
            foreach (ParkingBay b in sr.bays)
            {
                RoadInfo ri; if (!roads.TryGetValue(b.road, out ri)) continue;
                double ch = b.ch0 + 0.3 + r.NextDouble() * 0.6;
                int n = 0;
                while (true)
                {
                    V2 dir0; V2 probe = ri.line.At(ch + 2.5, out dir0);
                    string dist = DistrictAt(probe, sDpoly);
                    string model = ParkModels[Pick(r, Mix(dist))];
                    double len = ModelLength(model);
                    if (ch + len > b.ch1 - 0.2) break;
                    double mid = ch + len / 2;
                    V2 dir; V2 c = ri.line.At(mid, out dir);
                    c = c + dir.Left * (b.side * (ri.w / 2 - 0.95));
                    bool works = false;
                    foreach (double[] z in worksZones) if (V2.Dist(new V2(z[0], z[1]), c) < z[2]) works = true;
                    if (!works)
                    {
                        // UK: park facing the way the traffic on your side goes (the odd one the other way)
                        V2 face = b.side > 0 ? dir : dir * -1;
                        if (r.NextDouble() < 0.08) face = face * -1;
                        double yaw = Math.Atan2(face.x, face.z) * 180 / Math.PI;
                        bool pop = n % 4 == 2;
                        bool empty = !pop && r.NextDouble() < 0.12;          // gaps, as on any real street
                        if (!empty)
                            slots.Add(new ParkSlot
                            {
                                x = c.x, z = c.z, yaw = yaw < 0 ? yaw + 360 : yaw, length = len, forPopulation = pop, model = model, district = dist,
                                colour = Pick(r, model == "van" || model == "delivery" ? VanColourWeights : CarColourWeights)
                            });
                    }
                    n++;
                    ch += len + 0.5 + r.NextDouble() * 0.9;
                }
            }
            return slots;
        }
    }
}
