using System.Collections.Generic;

/// V2c: what each street is for, for the things that happen on it at run time (how many people are about, which lane
/// traffic keeps to). The same lists the city plan builds from (Editor/LundunCityPlan.cs): keep the two in step.
public static class LundunStreets
{
    /// Shops end to end: the busiest pavements.
    static readonly HashSet<string> High = new HashSet<string>
    {
        "Rye Lane", "Peckam High Street", "Old Kent Lane", "Oxbridge Street", "Regent Row", "Brompton Road", "Commercial Way",
        "Gilmore Road", "Westferry Road",
        "Brixtun Road", "Coldharbor Lane", "Acre Lane", "Electric Parade", "Atlantic Road", "Ferndale Way",
        "Admiral's Walk", "Pall Row",
    };
    /// A parade of shops at each junction, houses in between.
    static readonly HashSet<string> Local = new HashSet<string>
    {
        "Bellenden Road", "Choumert Grove", "Thurlow Street", "Mayfield Street", "Sloane Lane", "Dock Street", "Wharf Road",
        "Tower Plaza", "Quay Loop", "North Embankment", "South Embankment",
        "Stockwell Road", "Loughborough Road", "Denmore Hill", "Somerleyton Road", "Herne Lane", "Saltoun Road", "Tulse Lane",
        "Regency Street", "Spur Road",
    };
    /// Arterials with a red bus lane painted along the kerb (Editor/LundunStreetDetail.cs: BusLaneRoads).
    static readonly HashSet<string> BusLane = new HashSet<string> { "Old Kent Lane", "Rye Lane", "Oxbridge Street", "Commercial Way", "Brixtun Road", "Coldharbor Lane" };

    public static bool IsHighStreet(string road, string cls) { return road != null && (High.Contains(road) || (cls == "arterial" && !Local.Contains(road))); }
    public static bool HasBusLane(string road, string cls) { return cls == "arterial" && road != null && BusLane.Contains(road); }

    /// How busy this street's pavements are, against an ordinary residential street (1). People are put down on
    /// streets in proportion to length × this, so the same number of people gather where the shops are.
    public static double Footfall(string road, string cls)
    {
        if (cls == "service") return 0.3;
        if (cls == "ring" || cls == "bridge") return 0.5;
        if (IsHighStreet(road, cls)) return 4.0;
        if (road != null && Local.Contains(road)) return 2.0;
        return 1.0;
    }
}
