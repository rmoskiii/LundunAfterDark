using System.Collections.Generic;
using UnityEngine;

/// S1: the places a mission keeps for itself while it runs (its set pieces). The city's own people only check this
/// before putting someone new there: corner gangs, rough sleepers, bus queues, cyclists and police patrols don't appear
/// inside. Nothing else about them changes, and the zones are cleared the moment the mission ends, fails or is quit.
public static class LundunMissionZones
{
    struct Zone { public Vector3 at; public float radius; }
    static readonly List<Zone> zones = new List<Zone>();

    public static void Add(Vector3 at, float radius) { zones.Add(new Zone { at = at, radius = radius }); }
    public static void Clear() { zones.Clear(); }
    public static int Count { get { return zones.Count; } }

    /// Is this spot inside a running mission's area?
    public static bool Inside(Vector3 p)
    {
        for (int i = 0; i < zones.Count; i++)
        {
            float dx = p.x - zones[i].at.x, dz = p.z - zones[i].at.z;
            if (dx * dx + dz * dz < zones[i].radius * zones[i].radius) return true;
        }
        return false;
    }
}
