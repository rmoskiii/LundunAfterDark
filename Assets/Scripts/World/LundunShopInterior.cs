using System.Collections.Generic;
using UnityEngine;

/// E5: one walk-in store (Basketful, Grocera, ValuStack), built by Lundun → 28.
///   • its inside (shelves, lights, people) is only switched on when you're near or inside: the outside stays cheap
///   • the sliding doors open for anyone walking up to them (you, shoppers, staff), and stay open if their glass is broken
///   • Inside(point) tells other systems whether a point is in the store
/// E6: FLATPAK Home Store has a first floor too:
///   • its detail is split per floor, and only the floor you're on is drawn (both when you're by an escalator well)
///   • the escalators move: stand on one and it carries you (their treads scroll)
///   • two lifts: stand in one and the LIFT button (LundunShopPrompt) takes you to the other floor
public class LundunShopInterior : MonoBehaviour
{
    [Header("Filled in by Lundun → 28")]
    [SerializeField] private string storeName = "";
    [SerializeField] private Vector3 footprintMin, footprintMax;     // the store's outline (x, z)
    [SerializeField] private GameObject detail;                       // everything switched on near the store
    [SerializeField] private Transform[] doorLeftLeaves = new Transform[0], doorRightLeaves = new Transform[0];
    [SerializeField] private Vector3[] doorCentres = new Vector3[0];
    [SerializeField] private float doorHalfWidth = 2.2f;
    [Header("E6: floors, escalators, lifts (empty in a one-floor store)")]
    [SerializeField] private GameObject[] levels = new GameObject[0];  // ground floor, first floor (children of detail)
    [SerializeField] private float upperY;                             // the first floor's height
    [SerializeField] private float insideTop = 8f;
    [SerializeField] private Vector4[] wells = new Vector4[0];          // x, z, radius: near here both floors are drawn
    [SerializeField] private float[] doorHalves = new float[0];         // per door (empty = doorHalfWidth)
    [SerializeField] private float[] doorReach = new float[0];          // per door (empty = openWithin)
    [SerializeField] private Vector3[] escalatorPath = new Vector3[0];  // four points each, the way it moves
    [SerializeField] private float[] escalatorHalf = new float[0];
    [SerializeField] private Renderer[] escalatorTreads = new Renderer[0];
    [SerializeField] private Vector3[] liftAt = new Vector3[0];         // cabin centres (x, 0, z)
    [SerializeField] private float[] liftFloors = new float[0];         // two per lift
    [SerializeField] private string[] liftNames = new string[0];        // two per lift

    [Header("Tuning")]
    [SerializeField] private float showWithin = 90f;                  // metres from the store's outline
    [SerializeField] private float hideBeyond = 110f;
    [SerializeField] private float openWithin = 4.2f;
    [SerializeField] private float doorSpeed = 2.4f;                  // of a full opening per second
    [SerializeField] private float escalatorSpeed = 0.6f;             // m/s

    public static readonly List<LundunShopInterior> All = new List<LundunShopInterior>();
    public string StoreName { get { return storeName; } }
    public bool DetailOn { get { return detail != null && detail.activeSelf; } }
    public int LiftCount { get { return liftAt.Length; } }

    private float nextCheck, nextDoorCheck;
    private float[] open, want;
    private Vector3[] leftClosed, rightClosed;
    private int peopleMask;
    private static readonly Collider[] hits = new Collider[16];
    private Material treadMat;
    private CharacterController playerCC;
    private Transform playerT;

    public void SetData(string store, Vector3 min, Vector3 max, GameObject inside, Transform[] left, Transform[] right, Vector3[] centres, float half)
    {
        storeName = store; footprintMin = min; footprintMax = max; detail = inside;
        doorLeftLeaves = left; doorRightLeaves = right; doorCentres = centres; doorHalfWidth = half;
    }

    /// E6: the floors (each a child of the detail), where the wells are, how high Inside() reaches; each door's size.
    public void SetLevels(GameObject[] floors, float firstFloor, Vector4[] wellsAt, float top, float[] halves, float[] reach)
    {
        levels = floors; upperY = firstFloor; wells = wellsAt; insideTop = top; doorHalves = halves; doorReach = reach;
    }

    public void SetEscalators(Vector3[] path, float[] half, Renderer[] treads) { escalatorPath = path; escalatorHalf = half; escalatorTreads = treads; }

    public void SetLifts(Vector3[] at, float[] floors, string[] names) { liftAt = at; liftFloors = floors; liftNames = names; }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        int n = doorCentres.Length;
        open = new float[n]; want = new float[n];
        leftClosed = new Vector3[n]; rightClosed = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            if (i < doorLeftLeaves.Length && doorLeftLeaves[i] != null) { leftClosed[i] = doorLeftLeaves[i].localPosition; }
            if (i < doorRightLeaves.Length && doorRightLeaves[i] != null) { rightClosed[i] = doorRightLeaves[i].localPosition; }
        }
        peopleMask = LayerMask.GetMask("Player", "NPC", "Vehicle");
        if (detail != null) detail.SetActive(Distance(Viewer()) < showWithin);
        // one shared copy of the treads' material, scrolled in Update (so the asset isn't touched)
        if (escalatorTreads.Length > 0 && escalatorTreads[0] != null)
        {
            treadMat = new Material(escalatorTreads[0].sharedMaterial);
            foreach (Renderer r in escalatorTreads) if (r != null) r.sharedMaterial = treadMat;
        }
        Floors(true);
        if (liftAt.Length > 0) LundunShopPrompt.Ensure();
    }

    void OnDestroy() { if (treadMat != null) Destroy(treadMat); }

    /// Is this point inside the store?
    public bool Inside(Vector3 p)
    {
        return p.x > footprintMin.x && p.x < footprintMax.x && p.z > footprintMin.z && p.z < footprintMax.z && p.y < insideTop;
    }

    bool InFootprint(Vector3 p) { return p.x > footprintMin.x && p.x < footprintMax.x && p.z > footprintMin.z && p.z < footprintMax.z; }

    /// The store a point is in (null if none).
    public static LundunShopInterior At(Vector3 p)
    {
        foreach (LundunShopInterior s in All) if (s.Inside(p)) return s;
        return null;
    }

    Vector3 Viewer()
    {
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.Body != null && PlayerHealth.Instance.Body.gameObject.activeInHierarchy) return PlayerHealth.Instance.Body.position;
        if (Camera.main != null) return Camera.main.transform.position;
        return new Vector3(1e9f, 0f, 1e9f);
    }

    /// Metres from p to the store's outline (0 inside).
    float Distance(Vector3 p)
    {
        float dx = Mathf.Max(footprintMin.x - p.x, 0f, p.x - footprintMax.x), dz = Mathf.Max(footprintMin.z - p.z, 0f, p.z - footprintMax.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    void Update()
    {
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.4f;
            float d = Mathf.Min(Distance(Viewer()), Camera.main != null ? Distance(Camera.main.transform.position) : 1e9f);
            if (detail != null)
            {
                if (!detail.activeSelf && d < showWithin) detail.SetActive(true);
                else if (detail.activeSelf && d > hideBeyond) detail.SetActive(false);
            }
            Floors(false);
        }
        Doors();
        if (escalatorHalf.Length > 0 && DetailOn) Escalators();
    }

    // ------------------------------------------------------------------ E6: which floor is drawn
    void Floors(bool force)
    {
        if (levels.Length < 2 || detail == null || !detail.activeSelf) return;
        Vector3 p = Viewer();
        bool show0 = true, show1 = false;
        if (InFootprint(p))
        {
            bool nearWell = false;
            foreach (Vector4 w in wells) { float dx = p.x - w.x, dz = p.z - w.y; if (dx * dx + dz * dz < w.z * w.z) { nearWell = true; break; } }
            show1 = nearWell || p.y > upperY - 4f;
            show0 = nearWell || p.y < upperY - 1f;
        }
        if (levels[0] != null && (force || levels[0].activeSelf != show0)) levels[0].SetActive(show0);
        if (levels[1] != null && (force || levels[1].activeSelf != show1)) levels[1].SetActive(show1);
    }

    // ------------------------------------------------------------------ E6: the escalators carry you
    void Escalators()
    {
        if (treadMat != null) treadMat.mainTextureOffset = new Vector2(0f, -Mathf.Repeat(Time.time * escalatorSpeed / 0.4f, 1f));
        if (PlayerHealth.Instance == null || PlayerHealth.Instance.Body == null || !PlayerHealth.Instance.OnFoot) return;
        Transform t = PlayerHealth.Instance.Body;
        if (t != playerT) { playerT = t; playerCC = t.GetComponent<CharacterController>(); }
        if (playerCC == null || !playerCC.enabled) return;
        Vector3 p = t.position;
        if (!InFootprint(p)) return;
        float feet = p.y - LundunPlaces.FeetOffset(t);
        for (int k = 0; k < escalatorHalf.Length && k * 4 + 3 < escalatorPath.Length; k++)
        {
            Vector3 dir;
            if (!OnEscalator(k, p.x, p.z, feet, out dir)) continue;
            playerCC.Move(dir * escalatorSpeed * Time.deltaTime);
            return;
        }
    }

    /// Is a foot at (x, feet, z) on escalator k? If so, which way it moves there.
    bool OnEscalator(int k, float x, float z, float feet, out Vector3 dir)
    {
        dir = Vector3.zero;
        Vector3 a = escalatorPath[k * 4], e = escalatorPath[k * 4 + 3];
        Vector2 axis = new Vector2(e.x - a.x, e.z - a.z);
        float len = axis.magnitude;
        if (len < 0.1f) return false;
        axis /= len;
        Vector2 rel = new Vector2(x - a.x, z - a.z);
        float along = Vector2.Dot(rel, axis), side = Mathf.Abs(rel.x * axis.y - rel.y * axis.x);
        if (along < 0f || along > len || side > escalatorHalf[k] + 0.15f) return false;
        // which of the three parts (landing, slope, landing), and the tread's height there
        float s = 0f;
        for (int i = 0; i < 3; i++)
        {
            Vector3 p0 = escalatorPath[k * 4 + i], p1 = escalatorPath[k * 4 + i + 1];
            float seg = new Vector2(p1.x - p0.x, p1.z - p0.z).magnitude;
            if (along <= s + seg || i == 2)
            {
                float f = seg < 0.01f ? 0f : Mathf.Clamp01((along - s) / seg);
                float y = Mathf.Lerp(p0.y, p1.y, f);
                if (Mathf.Abs(feet - y) > 0.6f) return false;
                dir = (p1 - p0).normalized;
                return true;
            }
            s += seg;
        }
        return false;
    }

    // ------------------------------------------------------------------ E6: the lifts
    /// The lift (and which of its stops) someone standing at p is in, if any.
    public bool LiftAt(Vector3 p, out int lift, out int stop)
    {
        lift = -1; stop = -1;
        for (int i = 0; i < liftAt.Length; i++)
        {
            if (Mathf.Abs(p.x - liftAt[i].x) > 1.15f || Mathf.Abs(p.z - liftAt[i].z) > 1.15f) continue;
            float feet = p.y;
            for (int s = 0; s < 2 && i * 2 + s < liftFloors.Length; s++)
                if (Mathf.Abs(feet - liftFloors[i * 2 + s]) < 1.6f) { lift = i; stop = s; return true; }
        }
        return false;
    }

    /// Where the lift goes from this stop (for the button).
    public string LiftLabel(int lift, int stop)
    {
        int to = 1 - stop;
        string up = to > stop ? "LIFT UP" : "LIFT DOWN";
        string where = lift * 2 + to < liftNames.Length ? liftNames[lift * 2 + to] : "";
        return up + "\n<size=70%>" + where + "</size>";
    }

    /// Takes the player to the lift's other stop (the doors there open as they arrive).
    public void Ride(int lift, int stop)
    {
        if (PlayerHealth.Instance == null || PlayerHealth.Instance.Body == null) return;
        Transform t = PlayerHealth.Instance.Body;
        int to = 1 - stop;
        if (lift < 0 || lift * 2 + to >= liftFloors.Length) return;
        float dy = liftFloors[lift * 2 + to] - liftFloors[lift * 2 + stop];
        CharacterController cc = t.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        t.position += new Vector3(0f, dy, 0f);
        if (cc != null) cc.enabled = true;
        Floors(true);
        LundunHaptics.Light();
        if (LundunNavigation.Instance != null && lift * 2 + to < liftNames.Length) LundunNavigation.Instance.ShowToast(liftNames[lift * 2 + to]);
    }

    // ------------------------------------------------------------------ the sliding doors
    void Doors()
    {
        if (open == null) return;
        if (Time.time >= nextDoorCheck)
        {
            nextDoorCheck = Time.time + 0.2f;
            for (int i = 0; i < doorCentres.Length; i++)
            {
                float reach = i < doorReach.Length ? doorReach[i] : openWithin;
                want[i] = Physics.OverlapSphereNonAlloc(doorCentres[i] + Vector3.up, reach, hits, peopleMask, QueryTriggerInteraction.Collide) > 0 ? 1f : 0f;
            }
        }
        float step = doorSpeed * Time.deltaTime;
        for (int i = 0; i < doorCentres.Length; i++)
        {
            float was = open[i];
            open[i] = Mathf.MoveTowards(open[i], want[i], step);
            if (Mathf.Approximately(was, open[i]) && Time.frameCount > 2) continue;
            float half = i < doorHalves.Length ? doorHalves[i] : doorHalfWidth;
            float slide = Mathf.SmoothStep(0f, 1f, open[i]) * half * 0.95f;
            if (i < doorLeftLeaves.Length && doorLeftLeaves[i] != null) doorLeftLeaves[i].localPosition = leftClosed[i] + Vector3.left * slide;
            if (i < doorRightLeaves.Length && doorRightLeaves[i] != null) doorRightLeaves[i].localPosition = rightClosed[i] + Vector3.right * slide;
        }
    }
}
