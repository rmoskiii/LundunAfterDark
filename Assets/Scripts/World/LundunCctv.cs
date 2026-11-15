using System.Collections.Generic;
using UnityEngine;

/// V2d: every CCTV camera in the city, Brixtun and Kingsmere (saved by Lundun → 30 from the dressing: where, which way,
/// how wide, how far, and whether it's been sprayed over or smashed). They're part of the street dressing (static
/// meshes); this is the list of them.
/// V3 makes them witnesses: a crime inside a working camera's view (and in line of sight) is reported, and the wanted
/// level drops slower while you're on camera. Sees() below is what that will ask; nothing calls it yet.
public class LundunCctv : MonoBehaviour
{
    public static LundunCctv Instance { get; private set; }

    public enum Kind : byte { Dome, Bullet, Estate }

    [System.Serializable]
    public struct Cam
    {
        public Vector3 at;          // the lens
        public float yaw, pitch;    // where it points (degrees; pitch < 0 = down)
        public float fov, range;    // how wide (360 = a dome) and how far (metres)
        public Kind kind;
        public bool broken;         // sprayed over or smashed: sees nothing
    }

    [SerializeField] private Cam[] cams = new Cam[0];

    public int Count { get { return cams.Length; } }
    public Cam this[int i] { get { return cams[i]; } }

    // a 50 m grid of who's where, built on first use
    const float Cell = 50f;
    private Dictionary<long, List<int>> grid;
    private int blockMask = -1;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// Lundun → 30.
    public void SetData(Cam[] c) { cams = c ?? new Cam[0]; grid = null; }

    static long Key(int x, int z) { return (long)x * 100000L + z; }

    void BuildGrid()
    {
        grid = new Dictionary<long, List<int>>();
        for (int i = 0; i < cams.Length; i++)
        {
            if (cams[i].broken) continue;
            int r = Mathf.CeilToInt(cams[i].range / Cell);
            int cx = Mathf.FloorToInt(cams[i].at.x / Cell), cz = Mathf.FloorToInt(cams[i].at.z / Cell);
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++)
                {
                    List<int> l;
                    if (!grid.TryGetValue(Key(x, z), out l)) grid[Key(x, z)] = l = new List<int>();
                    l.Add(i);
                }
        }
        blockMask = LayerMask.GetMask("Default", "Buildings", "World");
        if (blockMask == 0) blockMask = 1;
    }

    /// Can a working camera see this point (someone's chest, or a car)? Which one, if so.
    public bool Sees(Vector3 p, out int which)
    {
        which = -1;
        if (cams.Length == 0) return false;
        if (grid == null) BuildGrid();
        List<int> near;
        if (!grid.TryGetValue(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell)), out near)) return false;
        foreach (int i in near)
        {
            Cam c = cams[i];
            Vector3 d = p - c.at;
            float dist = d.magnitude;
            if (dist > c.range || dist < 0.5f) continue;
            if (c.fov < 359f)
            {
                Vector3 fwd = Quaternion.Euler(-c.pitch, c.yaw, 0f) * Vector3.forward;
                if (Vector3.Angle(fwd, d) > c.fov * 0.5f + 10f) continue;      // a little slack: the view is wider down low
            }
            Vector3 from = c.at + d / dist * 0.4f;
            if (Physics.Linecast(from, p, blockMask, QueryTriggerInteraction.Ignore)) continue;
            which = i;
            return true;
        }
        return false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        foreach (Cam c in cams)
        {
            Gizmos.color = c.broken ? Color.grey : c.kind == Kind.Dome ? Color.cyan : Color.red;
            Gizmos.DrawSphere(c.at, 0.35f);
            if (c.broken) continue;
            Vector3 fwd = Quaternion.Euler(-c.pitch, c.yaw, 0f) * Vector3.forward;
            Gizmos.DrawLine(c.at, c.at + fwd * Mathf.Min(c.range, 12f));
        }
    }
#endif
}
