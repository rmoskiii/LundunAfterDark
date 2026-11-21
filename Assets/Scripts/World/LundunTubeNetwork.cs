using System.Collections.Generic;
using UnityEngine;

/// G6: the Lundun Underground's lines, in one place. Everything that needs to know how to get from one station to
/// another asks here: the station list and the ride (LundunTube, LundunTubeStation) now, and (G6b–d) the boarding
/// map, the full map's lines and the GPS. So they always agree.
///   Crown line   (purple)  Kingsmere Palace · Knightsbury Circus · River Bridge · Peckam High St · Brixtun · Brockmere Park
///   Quay line    (teal)    St Jarvis's Park · Hydemoor Park · Knightsbury Circus · Quay Cross · Canary Quay
///   Market line  (orange)  Angel Fields · Gilmore Green · Peckam High St · Peckam Rye · Ferndale Circus
/// Change at Knightsbury Circus (Crown/Quay) or Peckam High St (Crown/Market).
/// A journey: the fewest minutes (2 a stop, 3 to change, 2 waiting for the train), and on a tie the fewest changes.
/// The ride itself (real time) is short: 1–2 stops about 5–6 s, 3–4 about 8–10 s, 5–6 about 10–12 s.
public static class LundunTubeNetwork
{
    public class Line
    {
        public string name;
        public Color colour;
        public string[] stops;
        public Line(string name, Color colour, params string[] stops) { this.name = name; this.colour = colour; this.stops = stops; }
        public string Hex { get { return ColorUtility.ToHtmlStringRGB(colour); } }
    }

    public static readonly Line[] Lines =
    {
        new Line("Crown", new Color(0.59f, 0.27f, 0.75f), "KINGSMERE PALACE", "KNIGHTSBURY CIRCUS", "RIVER BRIDGE", "PECKAM HIGH ST", "BRIXTUN", "BROCKMERE PARK"),
        new Line("Quay", new Color(0.08f, 0.63f, 0.63f), "ST JARVIS'S PARK", "HYDEMOOR PARK", "KNIGHTSBURY CIRCUS", "QUAY CROSS", "CANARY QUAY"),
        new Line("Market", new Color(0.92f, 0.49f, 0.14f), "ANGEL FIELDS", "GILMORE GREEN", "PECKAM HIGH ST", "PECKAM RYE", "FERNDALE CIRCUS"),
    };

    public const int MinutesPerStop = 2, MinutesToChange = 3, MinutesWaiting = 2;

    /// One trip: the stations in order (from … to), the line of each hop, where you change.
    public class Journey
    {
        public string from, to;
        public readonly List<string> stops = new List<string>();
        public readonly List<int> hopLine = new List<int>();          // hopLine[i]: the line from stops[i] to stops[i + 1]
        public readonly List<string> changes = new List<string>();
        public int Hops { get { return hopLine.Count; } }
        public int Minutes { get { return Hops == 0 ? 0 : MinutesWaiting + Hops * MinutesPerStop + changes.Count * MinutesToChange; } }
        /// Real seconds for the ride (G6b's animated map uses this).
        public float RideSeconds { get { return LundunTubeNetwork.RideSeconds(Hops, changes.Count); } }
        public int FirstLine { get { return hopLine.Count > 0 ? hopLine[0] : -1; } }
        /// "Crown line" or "Crown line, change at Knightsbury Circus for the Quay line"
        public string Summary
        {
            get
            {
                if (Hops == 0) return "";
                string s = Lines[hopLine[0]].name + " line";
                for (int i = 1, c = 0; i < hopLine.Count; i++)
                    if (hopLine[i] != hopLine[i - 1]) s += ", change at " + Title(changes[c++]) + " for the " + Lines[hopLine[i]].name + " line";
                return s;
            }
        }
    }

    static Dictionary<string, List<int>> linesAt;

    public static bool Known(string station) { Index(); return station != null && linesAt.ContainsKey(station); }

    /// The lines through a station (indexes into Lines), in order; empty if it's not on the network.
    public static List<int> LinesAt(string station)
    {
        Index();
        List<int> l;
        return station != null && linesAt.TryGetValue(station, out l) ? l : new List<int>();
    }

    public static bool IsInterchange(string station) { return LinesAt(station).Count > 1; }

    /// "Crown and Quay lines"
    public static string LinesText(string station)
    {
        List<int> l = LinesAt(station);
        if (l.Count == 0) return "";
        if (l.Count == 1) return Lines[l[0]].name + " line";
        string s = "";
        for (int i = 0; i < l.Count; i++) s += (i == 0 ? "" : i == l.Count - 1 ? " and " : ", ") + Lines[l[i]].name;
        return s + " lines";
    }

    /// The real time a ride takes on screen: short, so the Tube's quick to use again and again.
    public static float RideSeconds(int hops, int changes)
    {
        if (hops <= 0) return 0f;
        float[] byHops = { 0f, 5f, 6f, 8f, 9.5f, 10.5f, 11.5f };
        float s = hops < byHops.Length ? byHops[hops] : 12f;
        return Mathf.Min(12f, s + 0.6f * changes);                       // the pause at each change, still within the range
    }

    /// The best way from one station to another (null if either isn't on the network or there's no way).
    public static Journey Plan(string from, string to)
    {
        Index();
        if (from == null || to == null || !linesAt.ContainsKey(from) || !linesAt.ContainsKey(to)) return null;
        Journey j = new Journey { from = from, to = to };
        if (from == to) { j.stops.Add(from); return j; }

        // states: (station, line you're on); cost = minutes * 100 + changes (so a tie goes to fewer changes)
        Dictionary<string, int> best = new Dictionary<string, int>();
        Dictionary<string, string> prev = new Dictionary<string, string>();
        List<KeyValuePair<int, string>> open = new List<KeyValuePair<int, string>>();
        foreach (int li in linesAt[from]) { string k = Key(from, li); best[k] = 0; open.Add(new KeyValuePair<int, string>(0, k)); }
        string goal = null;
        while (open.Count > 0)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++) if (open[i].Key < open[bi].Key) bi = i;
            KeyValuePair<int, string> cur = open[bi]; open.RemoveAt(bi);
            int cost; if (best.TryGetValue(cur.Value, out cost) && cost < cur.Key) continue;
            string st; int line; Split(cur.Value, out st, out line);
            if (st == to) { goal = cur.Value; break; }
            // along the line, either way
            string[] stops = Lines[line].stops;
            int at = System.Array.IndexOf(stops, st);
            foreach (int n in new[] { at - 1, at + 1 })
            {
                if (n < 0 || n >= stops.Length) continue;
                Relax(best, prev, open, cur.Value, Key(stops[n], line), cur.Key + MinutesPerStop * 100);
            }
            // change line here
            foreach (int other in linesAt[st])
                if (other != line) Relax(best, prev, open, cur.Value, Key(st, other), cur.Key + MinutesToChange * 100 + 1);
        }
        if (goal == null) return null;

        // walk it back
        List<string> path = new List<string>();
        for (string k = goal; k != null; k = prev.ContainsKey(k) ? prev[k] : null) path.Add(k);
        path.Reverse();
        string lastSt = null; int lastLine = -1;
        foreach (string k in path)
        {
            string st; int line; Split(k, out st, out line);
            if (lastSt == null) { j.stops.Add(st); }
            else if (st == lastSt) { if (j.hopLine.Count > 0) j.changes.Add(st); }        // changed line here
            else { j.stops.Add(st); j.hopLine.Add(line); }
            lastSt = st;
        }
        // a change before the first hop (starting on the "wrong" line) isn't a change
        while (j.changes.Count > 0 && j.changes[0] == from) j.changes.RemoveAt(0);
        return j;
    }

    static void Relax(Dictionary<string, int> best, Dictionary<string, string> prev, List<KeyValuePair<int, string>> open, string from, string to, int cost)
    {
        int old;
        if (best.TryGetValue(to, out old) && old <= cost) return;
        best[to] = cost; prev[to] = from;
        open.Add(new KeyValuePair<int, string>(cost, to));
    }

    static string Key(string station, int line) { return line + "|" + station; }
    static void Split(string k, out string station, out int line) { int b = k.IndexOf('|'); line = int.Parse(k.Substring(0, b)); station = k.Substring(b + 1); }

    static void Index()
    {
        if (linesAt != null) return;
        linesAt = new Dictionary<string, List<int>>();
        for (int li = 0; li < Lines.Length; li++)
            foreach (string s in Lines[li].stops)
            {
                List<int> l;
                if (!linesAt.TryGetValue(s, out l)) linesAt[s] = l = new List<int>();
                if (!l.Contains(li)) l.Add(li);
            }
    }

    /// "KNIGHTSBURY CIRCUS" → "Knightsbury Circus" ("ST JARVIS'S PARK" → "St Jarvis's Park")
    public static string Title(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        bool start = true;
        foreach (char c in s) { b.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c)); start = c == ' '; }
        return b.ToString();
    }
}
