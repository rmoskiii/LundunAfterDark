using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// L6: people sleeping rough (LundunPopulation adds this; each person is a LundunHomelessPerson).
///   where     with their back to a wall: under the railway arches (from the line's path), by station entrances, in
///             shop doorways, and on the mattresses dumped in the rough spots. Most in the rough areas (Gilmore and
///             Angel Fields estates, Peckam, Brixtun, the arches, the edges of the city); a few in the centre
///             (Knightsbury, Canary Quay); none in Kingsmere. Spots are kept at least 18 m apart
///   who       when James comes within about 85 m of a spot, it's decided whether someone's there (rough areas and
///             arches often, the edges sometimes, the centre now and then; a little more at night); they appear only
///             where the camera can't see, at most six around him, and are cleared once he's well away
///   how       worn, dirty clothes (their colours dulled), cardboard, a sleeping bag, a couple of bags, a paper cup
///             for those awake; at night most are asleep
///   money     there's no money yet: asking for change is just what they say (a wallet can come with missions)
/// They share the pedestrian budget (like the gangs and the bus queues).
public class LundunHomeless : MonoBehaviour
{
    public static LundunHomeless Instance { get; private set; }
    public static int Active { get; private set; }

    class Spot
    {
        public Vector3 at, outward;
        public float weight;
        public bool mattress, rolled;
        public LundunHomelessPerson who;
    }

    private readonly List<Spot> spots = new List<Spot>();
    private readonly List<LundunHomelessPerson> strays = new List<LundunHomelessPerson>();
    private Transform holder, propRoot;
    private float nextLook;
    private static float nextLine;
    private int wallMask, roadArea = 3;
    private Material[] bagMats;
    private Material cardboard, cupMat, bagBlack, bagBlue;

    public static readonly string[] Asks = { "\"Spare some change, mate?\"", "\"Got any spare change, boss?\"", "\"Spare a quid for a cup of tea?\"",
                                             "\"Any change, please? God bless.\"", "\"Spare some change for a hostel, mate?\"" };
    public static readonly string[] Mutters = { "\"Cheers anyway...\"", "\"Yeah. Have a good one.\"", "\"Nice one. Thanks for nothing.\"", "\"...Tight git.\"" };
    public static readonly string[] Woken = { "\"Oi! I was asleep!\"", "\"Do you mind? Trying to sleep here.\"" };
    public static readonly string[] Bumps = { "\"Watch where you're walking!\"", "\"Oi, mind my stuff!\"" };

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; Active = 0; }
    void OnEnable() { PlayerCombat.GunshotFired += OnGunshot; }
    void OnDisable() { PlayerCombat.GunshotFired -= OnGunshot; }

    void Start()
    {
        holder = new GameObject("RoughSleepers").transform; holder.SetParent(transform, false);
        propRoot = new GameObject("RoughSleepersThings").transform; propRoot.SetParent(transform, false);
        wallMask = LayerMask.GetMask("Default");
        MakeLook();
        FindSpots();
        Debug.Log("[Lundun] L6: " + spots.Count + " places people sleep rough");
    }

    // ------------------------------------------------------------------ talking
    /// A line on screen, unless someone's just said something (gap = seconds before the next line may come). True if said.
    public static bool Say(string[] lines, float gap)
    {
        if (Time.time < nextLine || LundunNavigation.Instance == null) return false;
        nextLine = Time.time + gap;
        LundunNavigation.Instance.ShowToast(lines[Random.Range(0, lines.Length)]);
        return true;
    }

    public static Vector3 JamesAt()
    {
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        if (vs != null && vs.PlayerTarget != null) return vs.PlayerTarget.position;
        PlayerHealth ph = PlayerHealth.Instance;
        return ph != null && ph.Body != null ? ph.Body.position : Vector3.zero;
    }

    /// Someone got up and ran (LundunHomelessPerson.Trouble): an ordinary pedestrian now, tidied away out of sight.
    public static void Released(LundunHomelessPerson p)
    {
        if (Instance == null || p == null) return;
        foreach (Spot s in Instance.spots) if (s.who == p) s.who = null;
        if (!Instance.strays.Contains(p)) Instance.strays.Add(p);
    }

    void OnGunshot(Vector3 from)
    {
        foreach (Spot s in spots)
            if (s.who != null && (s.who.transform.position - from).sqrMagnitude < 25f * 25f) s.who.Trouble(from);
    }

    // ------------------------------------------------------------------ where
    void FindSpots()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        LundunWorldData world = nav != null ? nav.World : null;
        List<Spot> found = new List<Spot>();

        // 1. the mattresses dumped in the rough spots (lie on them)
        LundunSmash smash = LundunSmash.Instance;
        if (smash != null)
            foreach (Vector3 m in smash.Where(LundunSmash.Kind.Mattress))
            {
                Spot s = WallSpot(m, 2.5f);
                if (s == null) continue;
                s.mattress = true; s.at = Ground(m, s.at.y); s.weight = 0.7f;
                found.Add(s);
            }

        // 2. under the railway arches (where the line runs high above the street)
        LundunTrain train = FindAnyObjectByType<LundunTrain>();
        if (train != null && train.Path != null)
        {
            Vector3[] path = train.Path;
            for (int i = 0; i + 1 < path.Length; i++)
            {
                Vector3 a = path[i], b = path[i + 1];
                float len = Vector3.Distance(a, b);
                for (float d = 0f; d < len; d += 12f)
                {
                    Vector3 p = Vector3.Lerp(a, b, d / Mathf.Max(0.01f, len));
                    Vector3 t = b - a; t.y = 0f; if (t.sqrMagnitude < 0.01f) continue; t.Normalize();
                    if (!Physics.Raycast(p + Vector3.down * 2f, Vector3.down, out RaycastHit g, 40f, wallMask, QueryTriggerInteraction.Ignore)) continue;
                    if (p.y - g.point.y < 4f) continue;                                    // not up on the viaduct here
                    Vector3 n = Vector3.Cross(Vector3.up, t);
                    foreach (float side in new[] { 1f, -1f })
                    {
                        Vector3 from = new Vector3(p.x, g.point.y + 1.2f, p.z) + n * side * 12f;
                        if (!Physics.Raycast(from, -n * side, out RaycastHit w, 11.5f, wallMask, QueryTriggerInteraction.Ignore) || w.distance < 1f) continue;
                        Vector3 normal = w.normal; normal.y = 0f;
                        if (normal.sqrMagnitude < 0.5f) continue;
                        normal.Normalize();
                        Vector3 at = w.point + normal * 0.5f;
                        if (!OnFoot(at, out Vector3 q)) continue;
                        found.Add(new Spot { at = q, outward = normal, weight = 0.6f });
                    }
                }
            }
        }

        // 3. by the stations, 4. in shop doorways
        if (nav != null && nav.Destinations != null)
            foreach (LundunNavigation.Destination d in nav.Destinations)
            {
                bool station = d.kind == "tube" || d.kind == "station" || d.kind == "rail";
                bool shop = d.kind == "shop";
                if (!station && !shop) continue;
                Vector3 off = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * (station ? 4f : 2f);
                Spot s = WallSpot(d.position + off, 3f);
                if (s == null) continue;
                s.weight = station ? 0.45f : 0.3f;
                found.Add(s);
            }

        // by area; none in Kingsmere; at least 18 m apart (the arches and mattresses were listed first, so they win)
        foreach (Spot s in found)
        {
            string area = Area(world, s.at);
            if (area == "KINGSMERE") continue;
            bool rough = area == "GILMORE ESTATE" || area == "ANGEL FIELDS ESTATE" || area == "PECKAM" || area == "BRIXTUN";
            bool centre = area != null && (area.StartsWith("KNIGHTSBURY") || area.StartsWith("CANARY QUAY"));
            s.weight *= rough ? 1f : centre ? 0.3f : 0.6f;                               // outside every district: the edges
            bool clash = false;
            foreach (Spot o in spots) if ((o.at - s.at).sqrMagnitude < 18f * 18f) { clash = true; break; }
            if (!clash) spots.Add(s);
        }
    }

    /// A spot with its back to the nearest wall within 'reach' of near (on the walkable ground, not in the road).
    Spot WallSpot(Vector3 near, float reach)
    {
        if (!OnFoot(near, out Vector3 q)) return null;
        RaycastHit best = new RaycastHit(); float bd = float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (Physics.Raycast(q + Vector3.up * 0.8f, dir, out RaycastHit h, reach, wallMask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(h.normal.y) < 0.3f && h.distance < bd) { bd = h.distance; best = h; }
        }
        if (bd == float.MaxValue) return null;
        Vector3 normal = best.normal; normal.y = 0f; normal.Normalize();
        if (!OnFoot(best.point + normal * 0.45f, out Vector3 at)) return null;
        return new Spot { at = at, outward = normal };
    }

    bool OnFoot(Vector3 p, out Vector3 at)
    {
        at = p;
        if (!NavMesh.SamplePosition(p + Vector3.up * 0.5f, out NavMeshHit hit, 1.6f, NavMesh.AllAreas)) return false;
        if ((hit.mask & (1 << roadArea)) != 0) return false;                       // not in the road
        at = hit.position;
        return true;
    }

    static Vector3 Ground(Vector3 p, float fallback)
    {
        return NavMesh.SamplePosition(p + Vector3.up * 0.5f, out NavMeshHit hit, 1.6f, NavMesh.AllAreas) ? hit.position : new Vector3(p.x, fallback, p.z);
    }

    static string Area(LundunWorldData w, Vector3 p)
    {
        if (w == null || w.districts == null) return null;
        foreach (LwPoly d in w.districts)
            if (d != null && d.polygon != null && Inside(d.polygon, p.x, p.z)) return d.name;
        return null;
    }

    static bool Inside(LwPoint[] poly, float x, float z)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].z > z) != (poly[j].z > z) && x < (poly[j].x - poly[i].x) * (z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x) c = !c;
        return c;
    }

    // ------------------------------------------------------------------ who's there
    void Update()
    {
        if (Time.time < nextLook || spots.Count == 0) return;
        nextLook = Time.time + 1f;
        Vector3 james = JamesAt();
        Camera cam = Camera.main;
        float h = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
        bool night = h >= 22f || h < 7f;
        int present = 0;
        foreach (Spot s in spots) if (s.who != null) present++;

        foreach (Spot s in spots)
        {
            float d = Flat(s.at, james);
            if (s.who != null && s.who.Gone) s.who = null;
            if (d > 130f)
            {
                s.rolled = false;
                if (s.who != null && (cam == null || !LundunView.CanSee(cam, s.at))) { Remove(s.who); s.who = null; present--; }
                continue;
            }
            if (d > 85f || s.rolled || s.who != null) continue;
            if (present >= 6) continue;
            if (cam != null && LundunView.CanSee(cam, s.at)) continue;               // never appear in view: try again later
            s.rolled = true;
            if (Random.value > s.weight * (night ? 1.15f : 0.85f)) continue;
            s.who = Make(s, night);
            if (s.who != null) present++;
        }

        // the ones who ran off: gone once well away and out of sight
        for (int i = strays.Count - 1; i >= 0; i--)
        {
            LundunHomelessPerson p = strays[i];
            if (p == null) { strays.RemoveAt(i); continue; }
            if (Flat(p.transform.position, james) > 80f && (cam == null || !LundunView.CanSee(cam, p.transform.position))) { Remove(p); strays.RemoveAt(i); }
        }
        Active = present;
    }

    LundunHomelessPerson Make(Spot s, bool night)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return null;
        GameObject go = pop.NewPerson(holder, Random.value < 0.6f ? "Street" : "Casual");
        if (go == null) return null;
        NPCBrain brain = go.GetComponent<NPCBrain>(); if (brain != null) brain.enabled = false;
        NavMeshAgent agent = go.GetComponent<NavMeshAgent>(); if (agent != null) agent.enabled = false;
        go.transform.SetPositionAndRotation(s.at, Quaternion.LookRotation(s.outward));
        go.SetActive(true);
        bool asleep = Random.value < (s.mattress ? 0.8f : night ? 0.65f : 0.25f);
        Transform cup;
        Transform things = Things(s, asleep, out cup);
        LundunHomelessPerson p = go.AddComponent<LundunHomelessPerson>();
        p.Setup(s.at + Vector3.up * (s.mattress ? 0.18f : 0f), s.outward, asleep, Random.value < 0.8f, things, cup);     // on top of a mattress
        return p;
    }

    void Remove(LundunHomelessPerson p)
    {
        if (p == null) return;
        if (p.Things != null) Destroy(p.Things.gameObject);
        Destroy(p.gameObject);
    }

    // ------------------------------------------------------------------ their things
    /// Cardboard on the ground, the sleeping bag (along the wall for a sleeper, out in front for someone sitting),
    /// one or two bags by the wall, and a paper cup in front (shown while they're sitting up).
    Transform Things(Spot s, bool asleep, out Transform cup)
    {
        Vector3 o = s.outward, along = Vector3.Cross(Vector3.up, o), p = s.at;
        Transform root = new GameObject("RoughSleeperThings").transform;
        root.SetParent(propRoot, false);
        root.position = p;
        Material bag = bagMats[Random.Range(0, bagMats.Length)];
        if (!s.mattress)
            Piece(root, PrimitiveType.Cube, p + o * 0.5f + Vector3.up * 0.01f, Quaternion.LookRotation(o), new Vector3(2.1f, 0.02f, 1.0f), cardboard);
        float bed = s.mattress ? 0.18f : 0f;
        // the bag: lying, along the wall up to the shoulders; sitting, out in front over the legs
        if (asleep)
            Piece(root, PrimitiveType.Capsule, p - along * 0.2f + o * 0.05f + Vector3.up * (0.2f + bed), Quaternion.LookRotation(Vector3.up, along), new Vector3(0.7f, 0.8f, 0.42f), bag);
        else
            Piece(root, PrimitiveType.Capsule, p + o * 0.5f + Vector3.up * (0.2f + bed), Quaternion.LookRotation(Vector3.up, o), new Vector3(0.62f, 0.6f, 0.4f), bag);
        if (asleep) Piece(root, PrimitiveType.Cube, p + along * 0.95f + Vector3.up * (0.08f + bed), Quaternion.LookRotation(o), new Vector3(0.5f, 0.16f, 0.35f), bag);   // a rolled-up pillow
        // bags by the wall
        int n = Random.Range(1, 3);
        for (int i = 0; i < n; i++)
        {
            float side = i == 0 ? (asleep ? -1.25f : -0.8f) : 0.85f;
            Piece(root, PrimitiveType.Cube, p + along * side + o * 0.05f + Vector3.up * 0.16f, Quaternion.LookRotation(o) * Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f),
                  new Vector3(0.42f, 0.32f, 0.28f), Random.value < 0.5f ? bagBlack : bagBlue);
        }
        cup = Piece(root, PrimitiveType.Cylinder, p + o * 1.3f + along * 0.3f + Vector3.up * 0.055f, Quaternion.identity, new Vector3(0.085f, 0.055f, 0.085f), cupMat);
        return root;
    }

    static Transform Piece(Transform root, PrimitiveType type, Vector3 at, Quaternion rot, Vector3 scale, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        g.name = type.ToString();
        g.transform.SetParent(root, true);
        g.transform.SetPositionAndRotation(at, rot);
        g.transform.localScale = scale;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    void MakeLook()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        bagMats = new[] { Mat(lit, new Color(0.12f, 0.16f, 0.28f)), Mat(lit, new Color(0.22f, 0.25f, 0.14f)), Mat(lit, new Color(0.32f, 0.1f, 0.1f)),
                          Mat(lit, new Color(0.25f, 0.25f, 0.26f)), Mat(lit, new Color(0.35f, 0.24f, 0.12f)) };
        cardboard = Mat(lit, new Color(0.55f, 0.42f, 0.27f));
        cupMat = Mat(lit, new Color(0.9f, 0.88f, 0.82f));
        bagBlack = Mat(lit, new Color(0.04f, 0.04f, 0.045f));
        bagBlue = Mat(lit, new Color(0.12f, 0.22f, 0.5f));
    }

    static Material Mat(Shader s, Color c)
    {
        Material m = new Material(s) { name = "Rough_" + c.ToString() };
        m.SetColor("_BaseColor", c); m.color = c;
        m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
}
