using UnityEngine;

/// L3: the parking bays. Built by Lundun → 23 on the LundunParked object.
///  • holds the bay slots kept free for real cars: LundunPopulation parks its (stealable) cars there instead of at
///    random kerbs, so parked cars always sit in proper bays
///  • the static parked cars (baked meshes) are switched off beyond drawDistance to save the iPhone some work
///  • v53: any parked car can be taken. As James walks up to a static parked car it's swapped, in place, for a real one
///    of the same model and paint (LundunPopulation.ParkReal); if he walks off without it, the static one comes back.
///    If he drives it away, the bay refills once he's well away and not looking.
public class LundunParking : MonoBehaviour
{
    public static LundunParking Instance { get; private set; }

    [Tooltip("Bays left free for real, stealable parked cars: x, z, facing (degrees), length")]
    [SerializeField] private Vector4[] slots = new Vector4[0];
    [SerializeField] private Renderer[] blocks = new Renderer[0];
    [Tooltip("Static parked cars further than this from the camera aren't drawn")]
    [SerializeField] private float drawDistance = 170f;
    [SerializeField] private float roadY = 0.02f;

    [Header("v53: taking parked cars")]
    [Tooltip("A static parked car becomes a real one when James (on foot) is this close")]
    [SerializeField] private float realWithin = 13f;
    [Tooltip("At most this many swapped at once")]
    [SerializeField] private int maxReal = 5;

    class Parked { public GameObject go; public Renderer rend; public Collider col; public Vector3 pos; public Quaternion rot; public string model; public GameObject real; public bool hidden; }
    private readonly System.Collections.Generic.List<Parked> statics = new System.Collections.Generic.List<Parked>();
    private VehicleSystem vehicles;
    private float nextSwap;
    private int realNow;

    private float nextCull;

    void Awake()
    {
        Instance = this;
        // E3: the expansion areas' bays too (their static parked cars stay with them)
        System.Collections.Generic.List<Vector4> all = new System.Collections.Generic.List<Vector4>(slots);
        foreach (LundunExpansionArea area in FindObjectsByType<LundunExpansionArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            all.AddRange(area.ParkingBays);
        slots = all.ToArray();
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void SetData(Vector4[] s, Renderer[] b) { slots = s; blocks = b; }
    public int SlotCount { get { return slots.Length; } }

    /// A free bay between near and far metres from the player, out of sight. Returns false if there isn't one.
    public bool TryGetSlot(Vector3 player, float near, float far, System.Random rng, Camera cam, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        if (slots.Length == 0) return false;
        int mask = LayerMask.GetMask("Vehicle", "Player");
        int start = rng.Next(slots.Length);
        for (int k = 0; k < slots.Length; k++)
        {
            Vector4 s = slots[(start + k) % slots.Length];
            Vector3 p = new Vector3(s.x, roadY + LundunGround.Height(s.x, s.y), s.y);   // G7b: bays on the hills
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(player.x, player.z));
            if (d < near || d > far) continue;
            if (cam != null && LundunView.CanSee(cam, p)) continue;
            if (Physics.CheckSphere(p + Vector3.up, Mathf.Max(1.5f, s.w * 0.5f), mask, QueryTriggerInteraction.Ignore)) continue;
            pos = p; rot = Quaternion.Euler(0f, s.z, 0f);
            return true;
        }
        return false;
    }

    void Start() { FindStatics(); }

    /// Every static parked car that is its own object (the detailed car models are; Lundun → 23 / 28 / 29 put them
    /// under "Cars", with a matching box under "Colliders"), in the city and the expansion areas.
    void FindStatics()
    {
        statics.Clear();
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name != "Cars" || t.parent == null) continue;
            Transform cols = t.parent.Find("Colliders");
            if (cols == null) continue;
            System.Collections.Generic.Dictionary<long, Collider> byPlace = new System.Collections.Generic.Dictionary<long, Collider>();
            foreach (Collider c in cols.GetComponentsInChildren<Collider>(true)) byPlace[Key(c.transform.position)] = c;
            foreach (Transform car in t)
            {
                Renderer r = car.GetComponent<Renderer>();
                int cut = car.name.LastIndexOf('_');
                if (r == null || cut <= 0) continue;
                Collider col; byPlace.TryGetValue(Key(car.position), out col);
                statics.Add(new Parked { go = car.gameObject, rend = r, col = col, pos = car.position, rot = car.rotation, model = car.name.Substring(0, cut) });
            }
        }
    }

    static long Key(Vector3 p) { return ((long)Mathf.RoundToInt(p.x * 4f) << 24) ^ (long)(Mathf.RoundToInt(p.z * 4f) + 8000000); }

    void Swap()
    {
        nextSwap = Time.unscaledTime + 0.2f;
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null || statics.Count == 0) return;
        if (vehicles == null) { vehicles = FindAnyObjectByType<VehicleSystem>(); if (vehicles == null) return; }
        Vector3 me = vehicles.PlayerTarget != null ? vehicles.PlayerTarget.position : Vector3.zero;
        Camera cam = Camera.main;
        bool onFoot = !vehicles.IsDriving;
        int vehicleMask = LayerMask.GetMask("Vehicle");

        Parked nearest = null; float nd = realWithin * realWithin;
        realNow = 0;
        foreach (Parked p in statics)
        {
            float d2 = (p.pos.x - me.x) * (p.pos.x - me.x) + (p.pos.z - me.z) * (p.pos.z - me.z);
            if (!p.hidden)
            {
                if (onFoot && d2 < nd && Mathf.Abs(p.pos.y - me.y) < 6f && p.go.activeInHierarchy) { nd = d2; nearest = p; }   // (not from a room above the street)
                continue;
            }
            // a swapped bay: the real car still standing there untouched, taken away, or tidied up by the traffic
            bool there = p.real != null && pop.StillParkedAt(p.real, p.pos);
            if (there)
            {
                realNow++;
                if (d2 > 30f * 30f && (cam == null || !LundunView.CanSee(cam, p.pos + Vector3.up)))
                { pop.TakeBack(p.real); Show(p); realNow--; }
                continue;
            }
            if (p.real != null && pop.IsOut(p.real) && (p.real.transform.position - p.pos).sqrMagnitude < 9f) { realNow++; continue; }   // nudged, burning, being got into: leave it
            // gone: the bay fills again once James is well away, isn't looking, and nothing is standing in it
            if (d2 > 110f * 110f && (cam == null || !LundunView.CanSee(cam, p.pos + Vector3.up))
                && !Physics.CheckSphere(p.pos + Vector3.up, 1.6f, vehicleMask, QueryTriggerInteraction.Ignore)) Show(p);
        }
        if (nearest == null || realNow >= maxReal) return;
        // the static car's box goes first, so the real one isn't pushed out of it
        if (nearest.col != null) nearest.col.enabled = false;
        GameObject real = pop.ParkReal(nearest.model, nearest.pos, nearest.rot, nearest.rend);
        if (real == null) { if (nearest.col != null) nearest.col.enabled = true; return; }
        nearest.real = real; nearest.hidden = true;
        nearest.go.SetActive(false);
    }

    static void Show(Parked p)
    {
        p.hidden = false; p.real = null;
        if (p.go != null) p.go.SetActive(true);
        if (p.col != null) p.col.enabled = true;
    }

    void Update()
    {
        if (Time.unscaledTime >= nextSwap) Swap();
        if (Time.unscaledTime < nextCull || blocks.Length == 0) return;
        nextCull = Time.unscaledTime + 0.5f;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;
        float d2 = drawDistance * drawDistance;
        foreach (Renderer r in blocks)
        {
            if (r == null) continue;
            Vector3 q = r.bounds.ClosestPoint(c);
            bool on = (q - c).sqrMagnitude < d2;
            if (r.enabled != on) r.enabled = on;
        }
    }
}
