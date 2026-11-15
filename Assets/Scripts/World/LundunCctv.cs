using System.Collections.Generic;
using UnityEngine;

/// V2d: every CCTV camera in the city, Brixtun and Kingsmere (saved by Lundun → 30 from the dressing: where, which way,
/// how wide, how far, and whether it's been sprayed over or smashed).
/// V2e: the control room. The cameras watch:
///   witnesses    a crime a working camera can see (the crime, or James doing it) is phoned through by the CCTV
///                operator a few seconds later (WantedSystem), and nobody can stop that call
///   on camera    while wanted, a camera that can see James keeps the police's search area on him and his escape runs
///                at a third of the speed; somewhere no camera covers (back streets, the Common) it runs faster
///   shoot them   a shot that hits a camera knocks it off its bracket (LundunSmash) and it's blind until it's put back
///                (about three minutes, once you're well away)
///   plates       a car taken in view of a camera, a carjacking (the driver phones it in) or any police car is reported
///                stolen; drive it past a junction dome and the plate is read: one star, police head for it. Leave
///                the car, get it resprayed, or keep away from cameras for three minutes (not wanted) and it's dropped
///   minimap      while wanted, the cameras' views show on the minimap (LundunMinimap)
/// V2f: missions too. The cameras watch during missions as well as Free Roam, except the ones listed in
/// QuietMissions (the first mission: a new player learns the police before the cameras); a mission's own script can
/// turn them on or off for itself (WatchThisMission). Cars a mission gives James, and his own car, are never "stolen":
/// only the city's cars (traffic, parked) and police cars can be reported.
/// The feed: when a camera catches James (and at WASTED / BUSTED) the picture cuts to it (LundunCctvFeed).
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

    [Header("V2e: watching")]
    [Tooltip("While on camera, losing the police runs at this speed")]
    [SerializeField] private float onCameraEscape = 0.35f;
    [Tooltip("With no working camera this close, losing the police runs at this speed")]
    [SerializeField] private float blindEscape = 1.3f;
    [SerializeField] private float blindWithin = 45f;
    [Tooltip("Seconds a camera stays dead if there's no smashable piece to put back")]
    [SerializeField] private float repairAfter = 180f;
    [Tooltip("A reported car is dropped after this long with no plate read (while not wanted)")]
    [SerializeField] private float plateForgetAfter = 180f;
    [Tooltip("How close a junction dome has to be to read a plate")]
    [SerializeField] private float plateReadRange = 32f;

    /// Layer 3 hooks: a camera picked James up (index); a camera read a stolen plate (index).
    public static event System.Action<int> Spotted;
    public static event System.Action<int> PlateRead;

    public int Count { get { return cams.Length; } }
    public Cam this[int i] { get { return cams[i]; } }

    /// The cameras count: there are some, and (in a mission) that mission lets them.
    public static bool Live { get { return Instance != null && Instance.cams.Length > 0 && MissionAllows; } }

    /// Missions the cameras keep out of (by id), unless the mission says otherwise.
    static readonly HashSet<string> QuietMissions = new HashSet<string> { "m01_one_last_favour" };
    static LundunMissionRun overrideRun;
    static bool overrideOn;

    /// A mission's script: cameras on (true) or off (false) for the rest of this attempt.
    public static void WatchThisMission(bool on)
    {
        overrideRun = LundunMissions.CurrentRun;
        overrideOn = on;
    }

    static bool MissionAllows
    {
        get
        {
            LundunMissionRun run = LundunMissions.CurrentRun;
            if (run == null) return true;                                    // Free Roam (or between missions)
            if (overrideRun == run) return overrideOn;
            return run.mission == null || !QuietMissions.Contains(run.mission.Id);
        }
    }

    /// The first camera that can see James right now (-1 if none).
    public int FirstWatching { get { return watching.Count > 0 ? watching[0] : -1; } }
    /// How fast losing the police runs (WantedSystem): slower on camera, faster where none can see.
    public static float EscapeFactor
    {
        get
        {
            if (!Live) return 1f;
            return Instance.OnCamera ? Instance.onCameraEscape : Instance.Blind ? Instance.blindEscape : 1f;
        }
    }

    /// A working camera can see James right now (checked a few times a second).
    public bool OnCamera { get; private set; }
    /// No working camera anywhere near.
    public bool Blind { get; private set; }

    // a 50 m grid of who's where, built on first use
    const float Cell = 50f;
    private Dictionary<long, List<int>> grid;
    private int blockMask = -1;
    private float[] downAt;                 // V2e: shot out (Time.time), or -1
    private int[] smashIndex;               // its piece in LundunSmash (-2 = not looked for yet, -1 = none)
    private readonly List<int> watching = new List<int>();
    private readonly List<long> visited = new List<long>();
    private int[] stamp; private int stampId;
    private float nextLook, nextRepair, lastOnCameraToast = -99f;
    private VehicleSystem vehicles;

    class Hot { public bool reported; public float reportAt = -1f, flaggedAt, lastRead = -99f; }
    private readonly Dictionary<CarController, Hot> stolen = new Dictionary<CarController, Hot>();
    private readonly List<CarController> gone = new List<CarController>();

    void Awake()
    {
        Instance = this;
        if (GetComponent<LundunCctvFeed>() == null) gameObject.AddComponent<LundunCctvFeed>();   // V2f: the feed cut and the replays
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
    void OnEnable() { VehicleSystem.Entering += OnEntering; LundunTrafficCar.Carjacked += OnCarjacked; }
    void OnDisable() { VehicleSystem.Entering -= OnEntering; LundunTrafficCar.Carjacked -= OnCarjacked; }

    /// Lundun → 30.
    public void SetData(Cam[] c) { cams = c ?? new Cam[0]; grid = null; }

    static long Key(int x, int z) { return (long)x * 100000L + z; }
    static long KeyAt(Vector3 p) { return Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell)); }

    void BuildGrid()
    {
        grid = new Dictionary<long, List<int>>();
        downAt = new float[cams.Length];
        smashIndex = new int[cams.Length];
        for (int i = 0; i < cams.Length; i++)
        {
            downAt[i] = -1f; smashIndex[i] = -2;
            if (cams[i].broken) continue;
            int r = Mathf.Max(1, Mathf.CeilToInt(cams[i].range / Cell));
            int cx = Mathf.FloorToInt(cams[i].at.x / Cell), cz = Mathf.FloorToInt(cams[i].at.z / Cell);
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++)
                {
                    List<int> l;
                    if (!grid.TryGetValue(Key(x, z), out l)) grid[Key(x, z)] = l = new List<int>();
                    l.Add(i);
                }
        }
        blockMask = LayerMask.GetMask("Default");
        if (blockMask == 0) blockMask = 1;
    }

    List<int> CamsAround(Vector3 p)
    {
        if (grid == null) BuildGrid();
        List<int> l;
        return grid.TryGetValue(KeyAt(p), out l) ? l : null;
    }

    /// Working: not sprayed or smashed in the dressing, and not shot out.
    public bool Working(int i) { return !cams[i].broken && (downAt == null || downAt[i] < 0f); }
    /// This camera can see James right now.
    public bool IsWatching(int i) { return watching.Contains(i); }

    /// Can camera i see this point? (in range, in its view, nothing solid in between)
    bool SeesFrom(int i, Vector3 p)
    {
        Cam c = cams[i];
        Vector3 d = p - c.at;
        float dist = d.magnitude;
        if (dist > c.range || dist < 0.5f) return false;
        if (c.fov < 359f)
        {
            Vector3 fwd = Quaternion.Euler(-c.pitch, c.yaw, 0f) * Vector3.forward;
            if (Vector3.Angle(fwd, d) > c.fov * 0.5f + 10f) return false;      // a little slack: the view is wider down low
        }
        Vector3 from = c.at + d / dist * 0.5f;
        return !Physics.Linecast(from, p, blockMask, QueryTriggerInteraction.Ignore);
    }

    /// Can a working camera see this point (someone's chest, or a car)? Which one, if so.
    public bool Sees(Vector3 p, out int which)
    {
        which = -1;
        List<int> near = CamsAround(p);
        if (near == null) return false;
        foreach (int i in near)
            if (Working(i) && SeesFrom(i, p)) { which = i; return true; }
        return false;
    }

    /// WantedSystem: did a camera see this crime (where it happened, or James doing it)?
    public bool SeesCrime(Vector3 crime, out int which)
    {
        if (Sees(crime + Vector3.up, out which)) return true;
        return Sees(PlayerPoint(), out which);
    }

    /// The cameras within r of a point (working ones), for the minimap.
    public void Near(Vector3 c, float r, List<int> into)
    {
        into.Clear();
        if (grid == null) BuildGrid();
        if (stamp == null || stamp.Length != cams.Length) stamp = new int[cams.Length];
        stampId++;
        int cx = Mathf.FloorToInt(c.x / Cell), cz = Mathf.FloorToInt(c.z / Cell), n = Mathf.CeilToInt(r / Cell);
        for (int x = cx - n; x <= cx + n; x++)
            for (int z = cz - n; z <= cz + n; z++)
            {
                List<int> l;
                if (!grid.TryGetValue(Key(x, z), out l)) continue;
                foreach (int i in l)
                {
                    if (!Working(i) || stamp[i] == stampId) continue;
                    stamp[i] = stampId;
                    Vector3 d = cams[i].at - c; d.y = 0f;
                    if (d.sqrMagnitude < r * r) into.Add(i);
                }
            }
    }

    Vector3 PlayerPoint()
    {
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        if (vehicles == null || vehicles.PlayerTarget == null) return Vector3.zero;
        return vehicles.PlayerTarget.position + Vector3.up * (vehicles.IsDriving ? 1.0f : 1.4f);
    }

    // ------------------------------------------------------------------ shooting them out
    /// PlayerCombat: the nearest working camera a shot from 'from' along 'dir' would hit within 'max' (index, or -1),
    /// and how far along.
    public int Along(Vector3 from, Vector3 dir, float max, out float t)
    {
        t = max;
        if (cams.Length == 0) return -1;
        if (grid == null) BuildGrid();
        int best = -1;
        visited.Clear();
        for (float s = 0f; s <= max + Cell * 0.5f; s += Cell * 0.5f)
        {
            long k = KeyAt(from + dir * Mathf.Min(s, max));
            if (visited.Contains(k)) continue;
            visited.Add(k);
            List<int> l;
            if (!grid.TryGetValue(k, out l)) continue;
            foreach (int i in l)
            {
                if (!Working(i)) continue;
                // the ray against a ball round the housing (a little generous: it's a small thing up high)
                Vector3 oc = cams[i].at - from;
                float along = Vector3.Dot(oc, dir);
                if (along <= 0f || along >= t) continue;
                float miss2 = oc.sqrMagnitude - along * along;
                if (miss2 > 0.45f * 0.45f) continue;
                best = i; t = along;
            }
        }
        return best;
    }

    /// A shot hit camera i: off its bracket, blind until it's put back.
    public void Shot(int i, Vector3 dir)
    {
        if (i < 0 || i >= cams.Length || !Working(i)) return;
        if (grid == null) BuildGrid();
        downAt[i] = Time.time;
        watching.Remove(i);
        LundunSmash sm = LundunSmash.Instance;
        if (sm != null)
        {
            if (smashIndex[i] == -2) smashIndex[i] = sm.IndexOf(LundunSmash.Kind.Cctv, cams[i].at, 1.2f);
            if (smashIndex[i] >= 0) sm.BreakIndex(smashIndex[i], dir * 5f);
        }
        LundunHaptics.Light();
        if (Live) Toast("Camera down");
    }

    void Repairs()
    {
        if (downAt == null) return;
        LundunSmash sm = LundunSmash.Instance;
        Vector3 me = PlayerPoint();
        for (int i = 0; i < downAt.Length; i++)
        {
            if (downAt[i] < 0f) continue;
            bool back;
            if (sm != null && smashIndex[i] >= 0) back = !sm.IsBroken(smashIndex[i]);       // LundunSmash put the piece back
            else
            {
                Vector3 d = cams[i].at - me; d.y = 0f;
                back = Time.time - downAt[i] > repairAfter && d.sqrMagnitude > 70f * 70f;
            }
            if (back) downAt[i] = -1f;
        }
    }

    // ------------------------------------------------------------------ watching James
    void Update()
    {
        if (Time.time >= nextRepair) { nextRepair = Time.time + 2f; Repairs(); }
        if (Time.time < nextLook) return;
        nextLook = Time.time + 0.3f;
        watching.Clear();
        bool was = OnCamera;
        if (!Live) { OnCamera = false; Blind = false; return; }

        Vector3 me = PlayerPoint();
        List<int> near = CamsAround(me);
        float nearest = float.MaxValue;
        if (near != null)
            foreach (int i in near)
            {
                if (!Working(i)) continue;
                Vector3 d = cams[i].at - me; d.y = 0f;
                nearest = Mathf.Min(nearest, d.magnitude);
                if (SeesFrom(i, me)) watching.Add(i);
            }
        OnCamera = watching.Count > 0;
        Blind = nearest > blindWithin;

        WantedSystem w = WantedSystem.Instance;
        if (OnCamera && !was) Spotted?.Invoke(watching[0]);
        if (w != null && w.Stars > 0 && OnCamera)
        {
            w.CameraSighting(me);
            if (!was && Time.time - lastOnCameraToast > 8f) { lastOnCameraToast = Time.time; Toast("ON CCTV"); }
        }
        Plates(w);
    }

    // ------------------------------------------------------------------ stolen cars
    void OnEntering(CarController car)
    {
        if (car == null || !Live || !Stealable(car)) return;
        Hot h;
        if (!stolen.TryGetValue(car, out h)) stolen[car] = h = new Hot();
        if (h.reported) return;                                               // back in a car that's already reported
        if (car.GetComponent<PoliceAI>() != null) { h.reported = true; h.flaggedAt = Time.time; return; }   // a police car: they know
        int seen;
        if (Sees(PlayerPoint(), out seen) && (h.reportAt < 0f || h.reportAt > Time.time + 15f)) h.reportAt = Time.time + 15f;   // taken on camera
    }

    void OnCarjacked(CarController car)
    {
        if (car == null || !Live || !Stealable(car)) return;
        Hot h;
        if (!stolen.TryGetValue(car, out h)) stolen[car] = h = new Hot();
        if (!h.reported && (h.reportAt < 0f || h.reportAt > Time.time + 30f)) h.reportAt = Time.time + 30f;   // the driver phones it in
    }

    /// Only the city's cars (traffic, parked) and police cars can be reported stolen: not James's own car, and not a
    /// car a mission gave him.
    static bool Stealable(CarController car)
    {
        if (car.GetComponent<PoliceAI>() != null) return true;
        if (car.GetComponent<LundunTrafficCar>() == null) return false;
        LundunMissionRun run = LundunMissions.CurrentRun;
        return run == null || !run.spawned.Contains(car.gameObject);
    }

    /// ARCHES MOTORS: a respray means the plate's no longer the one they're looking for.
    public static void Resprayed(CarController car)
    {
        if (Instance != null && car != null) Instance.stolen.Remove(car);
    }

    /// The car James is in has been reported stolen.
    public bool InHotCar
    {
        get
        {
            Hot h;
            return vehicles != null && vehicles.CurrentCar != null && stolen.TryGetValue(vehicles.CurrentCar, out h) && h.reported;
        }
    }

    void Plates(WantedSystem w)
    {
        gone.Clear();
        foreach (KeyValuePair<CarController, Hot> kv in stolen)
        {
            if (kv.Key == null) { gone.Add(kv.Key); continue; }
            Hot h = kv.Value;
            if (!h.reported && h.reportAt > 0f && Time.time >= h.reportAt) { h.reported = true; h.flaggedAt = Time.time; }
            // not wanted, and no camera's read it in a while: they've stopped looking for it
            if (h.reported && (w == null || w.Stars == 0) && Time.time - Mathf.Max(h.lastRead, h.flaggedAt) > plateForgetAfter) gone.Add(kv.Key);
        }
        foreach (CarController c in gone) stolen.Remove(c);

        CarController car = vehicles != null ? vehicles.CurrentCar : null;
        Hot mine;
        if (car == null || !stolen.TryGetValue(car, out mine) || !mine.reported || w == null) return;
        foreach (int i in watching)
        {
            if (cams[i].kind != Kind.Dome) continue;
            if ((cams[i].at - car.transform.position).sqrMagnitude > plateReadRange * plateReadRange) continue;
            if (Time.time - mine.lastRead < 8f) return;
            mine.lastRead = Time.time;
            PlateRead?.Invoke(i);
            if (w.Stars == 0)
            {
                w.RaiseTo(1, car.transform.position, false);
                Toast("PLATE READ\nStolen car flagged");
                LundunHaptics.Medium();
            }
            return;
        }
    }

    static void Toast(string message)
    {
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(message);
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
