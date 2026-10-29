using System.Collections.Generic;
using UnityEngine;

/// V4b: street furniture a car can knock flying (GTA-style), or that stops it:
///   light    bins, wheelie bins, cones, barriers, signs, trolleys, cycle stands, benches, railings, fly-tipping:
///            a car ploughs through, barely slowed
///   medium   bollards, lamp posts, Belisha beacons, post boxes, cabinets, planters, fences: they stop a slow car;
///            faster, they snap off and fly, and the car loses some speed
///   heavy    phone boxes, bus shelters, estate bin stores: they stop you unless you're really going (over ~30 mph)
/// What breaks disappears from the city's merged meshes and a loose copy tumbles away with real physics (and fades
/// after a while); a knocked-down lamp's light and glow go out. A few minutes later, when you're not looking, it's
/// back. Only cars break things: James and people on foot walk past railings as before, and are stopped by what
/// already stopped them. The list of things is made by Lundun → 30; lamp posts come from the lamp lights at start.
[DefaultExecutionOrder(500)]
public class LundunSmash : MonoBehaviour
{
    public enum Kind : byte
    {
        Bin, WheelieBin, Cone, Barrier, Sign, Trolley, CycleStand, Bench, Railing, Sofa, Fridge, Mattress, Tyres,
        Bollard, Lamp, Belisha, PostBox, Cabinet, Planter, Fence,
        PhoneBox, BusShelter, EstateBins
    }

    [System.Serializable]
    public struct Prop
    {
        public Kind kind;
        [Tooltip("The middle of its foot on the ground")] public Vector3 at;
        public float yaw;
        [Tooltip("Half its width (x), its height (y), half its length (z)")] public Vector3 size;
    }

    [SerializeField] private Prop[] props = new Prop[0];
    [Tooltip("The merged meshes the furniture is in (Lundun → 30 lists them and takes them out of static batching)")]
    [SerializeField] private Renderer[] targets = new Renderer[0];
    [Tooltip("Seconds before something knocked over is put back (when you're not near)")]
    [SerializeField] private float respawnAfter = 180f;
    [Tooltip("How long the loose pieces stay before they fade away")]
    [SerializeField] private float debrisLife = 30f;
    [SerializeField] private int maxDebris = 30;

    public static LundunSmash Instance { get; private set; }

    struct Feel { public float breakAt, keep, mass; public bool solid; public Feel(float b, float k, float m, bool s) { breakAt = b; keep = k; mass = m; solid = s; } }
    // breaks above (m/s into it), car speed kept, the piece's mass (kg), blocks people too (had a collider before)
    static Feel For(Kind k)
    {
        switch (k)
        {
            case Kind.Cone: return new Feel(1.2f, 0.99f, 4f, false);
            case Kind.Bin: return new Feel(2.5f, 0.95f, 14f, false);
            case Kind.WheelieBin: return new Feel(2.5f, 0.95f, 16f, false);
            case Kind.Barrier: return new Feel(2f, 0.97f, 9f, false);
            case Kind.Sign: return new Feel(2f, 0.97f, 10f, false);
            case Kind.Trolley: return new Feel(2f, 0.97f, 18f, false);
            case Kind.Tyres: return new Feel(2f, 0.95f, 25f, false);
            case Kind.Mattress: return new Feel(2f, 0.97f, 15f, false);
            case Kind.CycleStand: return new Feel(3f, 0.93f, 22f, false);
            case Kind.Bench: return new Feel(3.5f, 0.9f, 45f, false);
            case Kind.Railing: return new Feel(4f, 0.9f, 28f, false);
            case Kind.Sofa: case Kind.Fridge: return new Feel(3f, 0.9f, 45f, true);
            case Kind.Fence: return new Feel(4.5f, 0.86f, 28f, true);
            case Kind.Bollard: return new Feel(7f, 0.7f, 70f, false);
            case Kind.Lamp: return new Feel(6f, 0.74f, 160f, false);
            case Kind.Belisha: return new Feel(5.5f, 0.78f, 60f, false);
            case Kind.PostBox: return new Feel(9f, 0.65f, 140f, false);
            case Kind.Cabinet: return new Feel(6f, 0.8f, 70f, true);
            case Kind.Planter: return new Feel(7f, 0.74f, 130f, true);
            case Kind.PhoneBox: return new Feel(13f, 0.55f, 320f, true);
            case Kind.BusShelter: return new Feel(12f, 0.6f, 260f, true);
            default: return new Feel(9f, 0.7f, 160f, true);       // estate bin stores
        }
    }

    class State
    {
        public Prop prop;
        public List<Collider> colliders = new List<Collider>();
        public bool broken; public float brokenAt;
        public List<Saved> saved = new List<Saved>();
        public int lampIndex = -1;
        public Vector3 pole;                   // lamps: where the column stands (the head hangs over the road)
        public Transform loose;                // a hand-placed lamp post that falls over itself
        public Vector3 loosePos; public Quaternion looseRot;
    }
    class Saved { public Target t; public int[] idx; public Vector3[] was; }
    class Target
    {
        public Renderer r; public MeshFilter mf; public Mesh original, copy;
        public Vector3[] v; public Vector3[] n; public Vector2[] uv; public int[][] tris; public bool[] hidden;
        public Matrix4x4 toWorld, toLocal; public bool pool;
    }

    private readonly List<State> states = new List<State>();
    private readonly Dictionary<Collider, State> byCollider = new Dictionary<Collider, State>();
    private readonly List<Target> meshes = new List<Target>();
    private readonly LinkedList<GameObject> debris = new LinkedList<GameObject>();
    private int smashLayer = -1, defaultMask;
    private float nextRespawnCheck;
    private bool ready;
    private Transform cam;

    public void SetData(Prop[] list, Renderer[] meshesIn) { props = list; targets = meshesIn; }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; foreach (Target t in meshes) if (t.copy != null) Destroy(t.copy); }

    void Start()
    {
        smashLayer = LayerMask.NameToLayer("Smash");
        if (smashLayer < 0) smashLayer = 0;
        else
        {
            int vehicle = LayerMask.NameToLayer("Vehicle");
            for (int l = 0; l < 32; l++) Physics.IgnoreLayerCollision(smashLayer, l, l != vehicle);   // only cars meet them
        }
        defaultMask = LayerMask.GetMask("Default");
        foreach (Renderer r in targets) AddTarget(r);
        Transform root = new GameObject("SmashColliders").transform;
        root.SetParent(transform, false);
        foreach (Prop p in props) Add(p, root);
        AddLamps(root);
        ready = true;
    }

    void AddTarget(Renderer r)
    {
        if (r == null) return;
        MeshFilter mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || r.isPartOfStaticBatch || !mf.sharedMesh.isReadable) return;
        Material m = r.sharedMaterial;
        meshes.Add(new Target { r = r, mf = mf, original = mf.sharedMesh, pool = m != null && m.name.StartsWith("LightPool") });
    }

    // ------------------------------------------------------------------ colliders
    void Add(Prop p, Transform root)
    {
        State s = new State { prop = p };
        Feel f = For(p.kind);
        Quaternion rot = Quaternion.Euler(0f, p.yaw, 0f);
        Vector3 centre = p.at + Vector3.up * (p.size.y * 0.5f);
        // the colliders already there (planters, cabinets, fly-tips, phone boxes, shelters, bin stores) become this
        // thing's; long fence runs and roadworks sites are switched off (each panel / barrier gets its own)
        foreach (Collider c in Physics.OverlapBox(centre, new Vector3(p.size.x, p.size.y * 0.5f, p.size.z) * 0.9f, rot, defaultMask, QueryTriggerInteraction.Ignore))
        {
            if (c.attachedRigidbody != null || c.transform.parent == null || c.transform.parent.name != "Colliders") continue;
            string n = c.name;
            if (n == "Fence" || n == "Works") { c.enabled = false; continue; }
            if ((n == "Planter" || n == "Cabinet" || n == "FlyTip" || n == "PhoneBox" || n == "BusShelter" || n == "EstateBins") && !byCollider.ContainsKey(c))
            { s.colliders.Add(c); byCollider[c] = s; }
        }
        if (s.colliders.Count == 0)
        {
            GameObject g = new GameObject(p.kind.ToString());
            g.transform.SetParent(root, false);
            g.transform.SetPositionAndRotation(centre, rot);
            g.layer = f.solid ? 0 : smashLayer;
            BoxCollider bc = g.AddComponent<BoxCollider>();
            bc.size = new Vector3(Mathf.Max(0.12f, p.size.x * 2f), Mathf.Max(0.3f, p.size.y), Mathf.Max(0.12f, p.size.z * 2f));
            s.colliders.Add(bc); byCollider[bc] = s;
        }
        states.Add(s);
    }

    /// Lamp posts: one for every lamp light (the built ones, the expansions', the hand-placed high-street ones).
    void AddLamps(Transform root)
    {
        LundunLampLights lights = FindAnyObjectByType<LundunLampLights>();
        if (lights == null) return;
        // where the columns are: the lamp meshes' feet near each head
        List<Vector3> feet = new List<Vector3>();
        foreach (Target t in meshes)
        {
            Material m = t.r.sharedMaterial;
            if (m == null || !m.name.StartsWith("LampPole")) continue;
            Vector3[] v = t.original.vertices; Matrix4x4 w = t.r.transform.localToWorldMatrix;
            foreach (Vector3 lv in v) { Vector3 p = w.MultiplyPoint3x4(lv); feet.Add(p); }
        }
        Dictionary<long, List<Vector3>> grid = new Dictionary<long, List<Vector3>>();
        foreach (Vector3 p in feet) { long k = Key(p, 3f); List<Vector3> l; if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<Vector3>(); l.Add(p); }
        Dictionary<Vector3, Transform> posts = new Dictionary<Vector3, Transform>();
        foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            for (Transform t = l.transform; t != null; t = t.parent) if (t.name.StartsWith("LampPost")) { posts[l.transform.position] = t; break; }

        IReadOnlyList<Vector3> heads = lights.Positions;
        for (int i = 0; i < heads.Count; i++)
        {
            Vector3 h = heads[i];
            // the column: the low points of the lamp meshes within 1.6 m of the head (the arm reaches out over the road)
            Vector3 sum = Vector3.zero; int n = 0; float low = float.MaxValue;
            for (int gx = -1; gx <= 1; gx++)
                for (int gz = -1; gz <= 1; gz++)
                {
                    List<Vector3> l;
                    if (!grid.TryGetValue(Key(h + new Vector3(gx * 3f, 0f, gz * 3f), 3f), out l)) continue;
                    foreach (Vector3 p in l)
                    {
                        Vector3 d = p - h; d.y = 0f;
                        if (d.sqrMagnitude > 1.6f * 1.6f || p.y > h.y - 2.5f) continue;
                        sum += p; n++; low = Mathf.Min(low, p.y);
                    }
                }
            Transform post = null;
            foreach (var kv in posts) if ((kv.Key - h).sqrMagnitude < 0.25f) { post = kv.Value; break; }
            Vector3 pole = n > 0 ? new Vector3(sum.x / n, low, sum.z / n) : (post != null ? post.position : h - Vector3.up * 5.75f);
            if (n == 0 && post == null) continue;                                  // no column found (under a railway bridge)
            Prop p2 = new Prop { kind = Kind.Lamp, at = pole, yaw = 0f, size = new Vector3(0.16f, Mathf.Max(3f, h.y - pole.y + 0.3f), 0.16f) };
            int before = states.Count;
            Add(p2, root);
            State s = states[before];
            s.lampIndex = i; s.pole = pole;
            if (post != null)
            {
                s.loose = post; s.loosePos = post.position; s.looseRot = post.rotation;
                foreach (Collider c in post.GetComponentsInChildren<Collider>()) if (!byCollider.ContainsKey(c)) { byCollider[c] = s; s.colliders.Add(c); }
            }
        }
    }

    static long Key(Vector3 p, float cell) { return ((long)Mathf.FloorToInt(p.x / cell)) * 1000003L + Mathf.FloorToInt(p.z / cell); }

    // ------------------------------------------------------------------ hits
    /// A car (rb, moving at 'velocity' just before) hit this collider at 'point', closing at 'closing' m/s. True if it
    /// broke something (then the car keeps most of its speed: set back to what this returns in 'keep').
    public bool Hit(Collider c, Vector3 point, Vector3 velocity, float closing, out float keep)
    {
        keep = 1f;
        State s;
        if (!ready || c == null || !byCollider.TryGetValue(c, out s) || s.broken) return false;
        Feel f = For(s.prop.kind);
        if (closing < f.breakAt) return false;
        keep = f.keep;
        Break(s, point, velocity, f);
        return true;
    }

    /// Is this a piece of furniture that breaks when hit this hard (whether or not it's already gone)? 'keep' = how
    /// much speed a car keeps going through it.
    public bool WouldBreak(Collider c, float speed, out float keep)
    {
        keep = 1f;
        State s;
        if (c == null || !byCollider.TryGetValue(c, out s)) return false;
        Feel f = For(s.prop.kind);
        keep = f.keep;
        return s.broken || speed >= f.breakAt;
    }

    void Break(State s, Vector3 point, Vector3 velocity, Feel f)
    {
        s.broken = true; s.brokenAt = Time.time;
        foreach (Collider c in s.colliders) if (c != null) c.enabled = false;
        if (s.lampIndex >= 0)
        {
            LundunLampLights lights = FindAnyObjectByType<LundunLampLights>();
            if (lights != null) lights.Knock(s.lampIndex, true);
        }
        GameObject piece;
        if (s.loose != null) piece = Loosen(s);
        else piece = Cut(s);
        if (piece == null) return;
        Rigidbody rb = piece.GetComponent<Rigidbody>();
        if (rb == null) rb = piece.AddComponent<Rigidbody>();
        rb.mass = f.mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        float light = Mathf.Clamp01(1f - f.mass / 200f);
        Vector3 v = velocity * Random.Range(0.55f, 0.95f) * Mathf.Lerp(0.7f, 1.1f, light);
        v += Vector3.up * Random.Range(1.5f, 4f) * Mathf.Lerp(0.5f, 1.2f, light);
        rb.linearVelocity = v;
        Vector3 dir = velocity; dir.y = 0f;
        Vector3 topple = dir.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, dir.normalized) : Random.onUnitSphere;
        rb.angularVelocity = topple * Random.Range(2f, 5f) + Random.insideUnitSphere * Random.Range(1f, 4f) * light;
        debris.AddLast(piece);
        while (debris.Count > maxDebris) { GameObject old = debris.First.Value; debris.RemoveFirst(); Kill(old); }
        StartCoroutine(Fade(piece));
    }

    /// The thing's triangles come out of the merged meshes (squashed to nothing) and go into a loose piece.
    GameObject Cut(State s)
    {
        Prop p = s.prop;
        Quaternion rot = Quaternion.Euler(0f, p.yaw, 0f), inv = Quaternion.Inverse(rot);
        bool lamp = p.kind == Kind.Lamp;
        Vector3 head = lamp && s.lampIndex >= 0 ? HeadOf(s.lampIndex) : p.at;
        float reach = lamp ? 1.7f : Mathf.Max(p.size.x, p.size.z) + 0.1f;
        Bounds want = new Bounds(p.at + Vector3.up * (p.size.y * 0.5f), new Vector3(reach * 2f, p.size.y + 0.3f, reach * 2f));
        if (lamp) want.Encapsulate(new Bounds(head, Vector3.one * 3.4f));
        Bounds poolArea = new Bounds(lamp ? head : p.at, new Vector3(18f, 2f, 18f));

        List<Vector3> pv = new List<Vector3>(); List<Vector3> pn = new List<Vector3>(); List<Vector2> pu = new List<Vector2>();
        List<List<int>> subs = new List<List<int>>(); List<Material> mats = new List<Material>();
        Dictionary<long, int> remap = new Dictionary<long, int>();

        foreach (Target t in meshes)
        {
            if (t.r == null) continue;
            Bounds tb = t.r.bounds;
            bool poolHit = lamp && t.pool && tb.Intersects(poolArea);
            if (!poolHit && (t.pool || !tb.Intersects(want))) continue;
            Prepare(t);
            List<int> mine = new List<int>();
            for (int sub = 0; sub < t.tris.Length; sub++)
            {
                int[] tri = t.tris[sub];
                List<int> outTris = null;
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                    if (t.hidden[a] || t.hidden[b] || t.hidden[c]) continue;
                    Vector3 w = t.toWorld.MultiplyPoint3x4((t.v[a] + t.v[b] + t.v[c]) / 3f);
                    bool inside;
                    if (poolHit) { Vector3 d = w - head; d.y = 0f; inside = d.sqrMagnitude < 8f * 8f; }
                    else if (lamp)
                    {
                        Vector3 d1 = w - head; d1.y = 0f; Vector3 d2 = w - s.pole; d2.y = 0f;
                        inside = (d1.sqrMagnitude < 1.7f * 1.7f || d2.sqrMagnitude < 0.45f * 0.45f) && w.y > s.pole.y - 0.05f && w.y < head.y + 0.6f;
                    }
                    else
                    {
                        Vector3 l = inv * (w - p.at);
                        inside = Mathf.Abs(l.x) <= p.size.x + 0.05f && Mathf.Abs(l.z) <= p.size.z + 0.05f && l.y >= -0.05f && l.y <= p.size.y + 0.1f;
                    }
                    if (!inside) continue;
                    mine.Add(a); mine.Add(b); mine.Add(c);
                    if (poolHit) continue;                                          // the glow just goes out
                    if (outTris == null) { outTris = new List<int>(); subs.Add(outTris); Material[] rm = t.r.sharedMaterials; mats.Add(rm[Mathf.Min(sub, rm.Length - 1)]); }
                    foreach (int vi in new[] { a, b, c })
                    {
                        long key = ((long)t.GetHashCode() << 32) ^ vi;
                        int ni;
                        if (!remap.TryGetValue(key, out ni))
                        {
                            ni = pv.Count; remap[key] = ni;
                            pv.Add(inv * (t.toWorld.MultiplyPoint3x4(t.v[vi]) - p.at));
                            pn.Add(inv * t.toWorld.MultiplyVector(t.n != null && vi < t.n.Length ? t.n[vi] : Vector3.up).normalized);
                            pu.Add(t.uv != null && vi < t.uv.Length ? t.uv[vi] : Vector2.zero);
                        }
                        outTris.Add(ni);
                    }
                }
            }
            if (mine.Count == 0) continue;
            // squash them out of the merged mesh
            HashSet<int> uniq = new HashSet<int>(mine);
            int[] idx = new int[uniq.Count]; uniq.CopyTo(idx);
            Vector3[] was = new Vector3[idx.Length];
            Vector3 gone = t.toLocal.MultiplyPoint3x4(new Vector3(p.at.x, -50f, p.at.z));
            for (int i = 0; i < idx.Length; i++) { was[i] = t.v[idx[i]]; t.v[idx[i]] = gone; t.hidden[idx[i]] = true; }
            t.copy.vertices = t.v;
            s.saved.Add(new Saved { t = t, idx = idx, was = was });
        }
        if (pv.Count == 0) return null;

        Mesh m = new Mesh { name = "Smashed_" + p.kind };
        if (pv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(pv); m.SetNormals(pn); m.SetUVs(0, pu);
        m.subMeshCount = subs.Count;
        for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
        m.RecalculateBounds();
        GameObject g = new GameObject("Smashed_" + p.kind);
        g.transform.SetPositionAndRotation(p.at, rot);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats.ToArray();
        BoxCollider bc = g.AddComponent<BoxCollider>();
        Bounds mb = m.bounds;
        bc.center = mb.center; bc.size = Vector3.Max(mb.size, new Vector3(0.1f, 0.1f, 0.1f));
        return g;
    }

    /// A hand-placed lamp post (a prefab of its own) simply comes loose.
    GameObject Loosen(State s)
    {
        Transform t = s.loose;
        if (t == null) return null;
        foreach (Collider c in t.GetComponentsInChildren<Collider>()) c.enabled = true;
        return t.gameObject;
    }

    Vector3 HeadOf(int lampIndex)
    {
        LundunLampLights lights = FindAnyObjectByType<LundunLampLights>();
        return lights != null ? lights.HomeOf(lampIndex) : Vector3.zero;
    }

    void Prepare(Target t)
    {
        if (t.copy != null) return;
        t.copy = Instantiate(t.original);
        t.copy.name = t.original.name + "_smash";
        t.copy.MarkDynamic();
        t.mf.sharedMesh = t.copy;
        t.v = t.copy.vertices; t.n = t.copy.normals; t.uv = t.copy.uv;
        t.tris = new int[t.copy.subMeshCount][];
        for (int i = 0; i < t.tris.Length; i++) t.tris[i] = t.copy.GetTriangles(i);
        t.hidden = new bool[t.v.Length];
        t.toWorld = t.r.transform.localToWorldMatrix; t.toLocal = t.r.transform.worldToLocalMatrix;
    }

    System.Collections.IEnumerator Fade(GameObject g)
    {
        yield return new WaitForSeconds(debrisLife);
        if (g == null) yield break;
        // sink into the ground, then go
        Rigidbody rb = g.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; }
        foreach (Collider c in g.GetComponentsInChildren<Collider>()) c.enabled = false;
        float t = 0f;
        while (t < 2f && g != null) { g.transform.position += Vector3.down * Time.deltaTime * 0.6f; t += Time.deltaTime; yield return null; }
        debris.Remove(g);
        Kill(g);
    }

    void Kill(GameObject g)
    {
        if (g == null) return;
        foreach (State s in states)
            if (s.loose != null && s.loose.gameObject == g) { Park(s); return; }            // hand-placed posts are kept, hidden until put back
        MeshFilter mf = g.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
        Destroy(g);
    }

    void Park(State s)
    {
        Rigidbody rb = s.loose.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
        s.loose.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ putting things back
    void Update()
    {
        if (!ready || Time.time < nextRespawnCheck) return;
        nextRespawnCheck = Time.time + 2f;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        Vector3 c = cam != null ? cam.position : Vector3.zero;
        foreach (State s in states)
        {
            if (!s.broken || Time.time - s.brokenAt < respawnAfter) continue;
            Vector3 d = s.prop.at - c; d.y = 0f;
            if (d.sqrMagnitude < 70f * 70f) continue;                                   // not while you're near
            Restore(s);
        }
    }

    void Restore(State s)
    {
        HashSet<Target> touched = new HashSet<Target>();
        foreach (Saved sv in s.saved)
        {
            for (int i = 0; i < sv.idx.Length; i++) { sv.t.v[sv.idx[i]] = sv.was[i]; sv.t.hidden[sv.idx[i]] = false; }
            touched.Add(sv.t);
        }
        foreach (Target t in touched) t.copy.vertices = t.v;
        s.saved.Clear();
        if (s.loose != null)
        {
            Rigidbody rb = s.loose.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);
            s.loose.gameObject.SetActive(true);
            s.loose.SetPositionAndRotation(s.loosePos, s.looseRot);
        }
        foreach (Collider col in s.colliders) if (col != null) col.enabled = true;
        if (s.lampIndex >= 0)
        {
            LundunLampLights lights = FindAnyObjectByType<LundunLampLights>();
            if (lights != null) lights.Knock(s.lampIndex, false);
        }
        s.broken = false;
    }
}
