using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// R4a: gangs on the street. Groups of 3 to 5 who hang about on street corners in their own areas at their own times,
/// looking for trouble:
///   Gilmore Estate        Tower Block Kings   teal     18:00–05:00
///   Angel Fields Estate   Angel Fields Firm   purple   18:00–05:00
///   Peckam                Rye Lane Riders     orange   22:00–04:00
///   Brixtun               Electric Youth      yellow   22:00–04:00
/// Never in Knightsbury, Canary Quay or Kingsmere. At most two groups near James at once.
/// Each group: a shared colour (a cap and a tint), one or two with a knife or a machete (drawn when it kicks off), and
/// after midnight one group in four has someone with a gun. Their corners are worked out from the street layout (the
/// junctions inside each area, the pavement on the corner), so no menu is needed. See LundunGangMember for how they act.
/// Gang members come out of the same people budget as pedestrians (LundunPopulation), so the frame rate holds.
/// Added at runtime by LundunPopulation (untick Gangs there to switch them off).
public class LundunGangs : MonoBehaviour
{
    public static LundunGangs Instance { get; private set; }
    /// Gang members out right now (LundunPopulation makes that much less room for ordinary pedestrians).
    public static int ActiveMembers { get; private set; }

    public class Gang
    {
        public string district, name; public Color colour; public float from, to;
        public readonly List<Vector3> spots = new List<Vector3>();
        public bool spotsFound;
    }

    public class Group
    {
        public Gang gang; public Vector3 spot; public int spotIndex;
        public readonly List<LundunGangMember> members = new List<LundunGangMember>();
        public bool fighting, scattering;
        public float nextTrouble, nextTaunt, lastContact;
        public bool HasLiving { get { foreach (LundunGangMember m in members) if (m != null && !m.IsDead) return true; return false; } }
    }

    [SerializeField] private int maxGroups = 2;
    [SerializeField] private float spawnFrom = 60f, spawnTo = 160f, removeBeyond = 220f;
    [Tooltip("After midnight, the chance a group has someone with a gun (knives and machetes are far more common)")]
    [SerializeField, Range(0f, 1f)] private float gunChance = 0.25f;
    [Tooltip("Ignore the time of day (for testing)")]
    [SerializeField] private bool anyTime = false;

    private readonly List<Gang> gangs = new List<Gang>
    {
        new Gang { district = "D_GILMOR", name = "Tower Block Kings", colour = new Color(0.05f, 0.62f, 0.6f), from = 18f, to = 5f },
        new Gang { district = "D_BRIXTUN_EST", name = "Angel Fields Firm", colour = new Color(0.52f, 0.18f, 0.72f), from = 18f, to = 5f },
        new Gang { district = "D_PECKAM", name = "Rye Lane Riders", colour = new Color(0.95f, 0.45f, 0.08f), from = 22f, to = 4f },
        new Gang { district = "D_BRIXTUN", name = "Electric Youth", colour = new Color(0.95f, 0.82f, 0.1f), from = 22f, to = 4f },
    };
    private readonly List<Group> groups = new List<Group>();
    private readonly Dictionary<string, float> spotRestUntil = new Dictionary<string, float>();
    private readonly System.Random rng = new System.Random();
    private float nextTick;
    private Transform holder;
    private static Material bladeSteel, bladeHandle;
    private readonly Dictionary<Gang, Material> capMats = new Dictionary<Gang, Material>();

    static readonly string[] Lookers = { "What you looking at?", "Oi! You lost?", "Wrong ends, bruv.", "Keep walking.", "You got a problem?" };
    static readonly string[] Warnings = { "Last chance. Move.", "You deaf? Go on!", "Don't make me ask again." };
    static readonly string[] Attacks = { "Get him!", "Run him!", "Draw it!" };

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; ActiveMembers = 0; }

    void Update()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + 1f;
        LundunNavigation nav = LundunNavigation.Instance;
        PlayerHealth ph = PlayerHealth.Instance;
        if (nav == null || nav.World == null || nav.Graph == null || ph == null) return;
        Vector3 me = ph.Body != null && ph.OnFoot ? ph.Body.position : (FindAnyObjectByType<VehicleSystem>() is VehicleSystem vs ? vs.PlayerTarget.position : Vector3.zero);
        float hour = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;

        // tidy up: gone, all dead, far away and unseen, or their time is over and nobody's watching
        Camera cam = Camera.main;
        int living = 0;
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            Group g = groups[i];
            g.members.RemoveAll(m => m == null);
            bool any = g.HasLiving;
            float d = Flat(g.spot, me);
            bool seen = false;
            foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead && cam != null && LundunView.CanSee(cam, m.transform.position)) seen = true;
            bool over = !anyTime && !InHours(g.gang, hour) && !g.fighting;
            if (!any || ((d > removeBeyond || over || g.scattering) && !seen && d > 40f))
            {
                foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead) Destroy(m.gameObject);
                spotRestUntil[g.gang.name + g.spotIndex] = Time.time + 300f;       // give the corner a rest
                groups.RemoveAt(i);
                continue;
            }
            foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead) living++;
            Think(g, me, ph);
        }
        ActiveMembers = living;

        // new groups: an active gang's corner, near enough but out of sight
        if (groups.Count >= maxGroups) return;
        foreach (Gang gang in gangs)
        {
            if (!anyTime && !InHours(gang, hour)) continue;
            if (!gang.spotsFound) FindSpots(gang, nav);
            for (int s = 0; s < gang.spots.Count; s++)
            {
                Vector3 p = gang.spots[s];
                float d = Flat(p, me);
                if (d < spawnFrom || d > spawnTo || InUse(gang, s)) continue;
                if (spotRestUntil.TryGetValue(gang.name + s, out float rest) && Time.time < rest) continue;
                if (cam != null && LundunView.CanSee(cam, p)) continue;
                Spawn(gang, s, hour);
                return;                                          // one at a time
            }
        }
    }

    bool InUse(Gang gang, int s) { foreach (Group g in groups) if (g.gang == gang && g.spotIndex == s) return true; return false; }

    static bool InHours(Gang g, float h) { return g.from <= g.to ? h >= g.from && h < g.to : h >= g.from || h < g.to; }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    // ------------------------------------------------------------------ the group's mind (once a second)
    void Think(Group g, Vector3 me, PlayerHealth ph)
    {
        if (g.scattering) return;
        // police about: scatter
        foreach (PoliceOfficer o in PoliceOfficer.Active)
            if (o != null && !o.IsDown && Flat(o.transform.position, g.spot) < 30f) { Scatter(g, o.transform.position); return; }
        foreach (PoliceAI c in PoliceAI.Active)
            if (c != null && Flat(c.transform.position, g.spot) < 30f) { Scatter(g, c.transform.position); return; }

        // morale: half of them down and the rest lose their nerve
        int dead = 0, all = g.members.Count;
        foreach (LundunGangMember m in g.members) if (m == null || m.IsDead) dead++;
        if (all > 0 && dead * 2 >= all && dead > 0) { Scatter(g, me); return; }

        if (g.fighting)
        {
            float far = Flat(me, g.spot);
            if (ph.IsDead || far > 45f || Time.time - g.lastContact > 40f) EndFight(g);
            return;
        }

        // now and then, pick on a passer-by
        if (Time.time >= g.nextTrouble)
        {
            g.nextTrouble = Time.time + Random.Range(45f, 90f);
            LundunPopulation pop = LundunPopulation.Instance;
            GameObject victim = pop != null ? pop.NearestPedestrian(g.spot, 10f) : null;
            if (victim != null && Flat(me, g.spot) < 70f)
            {
                int sent = 0;
                foreach (LundunGangMember m in g.members)
                    if (m != null && !m.IsDead && sent < 2 && m.PickOn(victim)) sent++;
            }
        }
    }

    // ------------------------------------------------------------------ from the members
    /// James is close: they watch him and say something (not too often).
    public void Look(Group g)
    {
        if (Time.time < g.nextTaunt) return;
        g.nextTaunt = Time.time + Random.Range(12f, 20f);
        Toast(Lookers[Random.Range(0, Lookers.Length)]);
    }

    public void Warn(Group g)
    {
        if (Time.time < g.nextTaunt - 8f) return;
        g.nextTaunt = Time.time + 10f;
        Toast(Warnings[Random.Range(0, Warnings.Length)]);
    }

    /// It's kicked off (James hit one, stayed in their face, shot at them…): the whole group goes for him.
    public void StartFight(Group g)
    {
        g.lastContact = Time.time;
        if (g.fighting || g.scattering) return;
        g.fighting = true;
        Toast(Attacks[Random.Range(0, Attacks.Length)]);
        LundunHaptics.Medium();
        foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead) m.Fight();
    }

    void EndFight(Group g)
    {
        g.fighting = false;
        foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead) m.BackToCorner();
    }

    public void Scatter(Group g, Vector3 from)
    {
        if (g.scattering) return;
        g.scattering = true; g.fighting = false;
        foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead) m.Scatter(from);
    }

    static void Toast(string s) { if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(s); }

    // ------------------------------------------------------------------ spawning
    void Spawn(Gang gang, int spotIndex, float hour)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return;
        if (holder == null) { holder = new GameObject("Gangs").transform; holder.SetParent(transform, false); }
        Group g = new Group { gang = gang, spot = gang.spots[spotIndex], spotIndex = spotIndex, nextTrouble = Time.time + Random.Range(20f, 50f) };
        int n = 3 + rng.Next(3);
        bool late = hour >= 0f && hour < 5f;
        int gunman = late && rng.NextDouble() < gunChance ? rng.Next(n) : -1;
        int blades = 1 + (rng.NextDouble() < 0.55 ? 1 : 0);                   // one or two with a blade
        for (int i = 0; i < n; i++)
        {
            GameObject go = pop.NewPerson(holder, "Street");
            if (go == null) break;
            float a = i * Mathf.PI * 2f / n + (float)rng.NextDouble() * 0.4f;
            Vector3 p = g.spot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (1.4f + (float)rng.NextDouble() * 0.8f);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) p = hit.position;
            Vector3 face = g.spot - p; face.y = 0f;
            go.transform.SetPositionAndRotation(p, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity);
            go.name = gang.name + "_" + i;
            Dress(go, gang);
            go.SetActive(true);
            LundunGangMember.Weapon w = i == gunman ? LundunGangMember.Weapon.Gun
                                      : blades > 0 ? (rng.NextDouble() < 0.5 ? LundunGangMember.Weapon.Machete : LundunGangMember.Weapon.Knife)
                                      : LundunGangMember.Weapon.None;
            if (w == LundunGangMember.Weapon.Machete || w == LundunGangMember.Weapon.Knife) blades--;
            LundunGangMember m = go.AddComponent<LundunGangMember>();
            m.Begin(this, g, w, p);
            g.members.Add(m);
        }
        if (g.members.Count == 0) return;
        groups.Add(g);
    }

    /// The gang's colour: a cap on the head and a light tint on their clothes.
    void Dress(GameObject go, Gang gang)
    {
        if (!capMats.TryGetValue(gang, out Material cap))
        {
            cap = NewMaterial(gang.colour);
            capMats[gang] = cap;
        }
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is LineRenderer) continue;
            foreach (Material m in r.materials)
            {
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                Color t = Color.Lerp(c, gang.colour, 0.28f); t.a = c.a;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", t);
                m.color = t;
            }
        }
        Health h = go.GetComponent<Health>();
        if (h != null) h.RefreshColour();
        Animator anim = go.GetComponentInChildren<Animator>();
        Transform head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;
        float s = go.transform.localScale.y; if (s < 0.0001f) s = 1f;
        GameObject capGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        capGo.name = "GangCap";
        Object.Destroy(capGo.GetComponent<Collider>());
        capGo.transform.SetParent(head, false);
        capGo.transform.localPosition = new Vector3(0f, 0.13f, 0.01f) / s;
        capGo.transform.localScale = new Vector3(0.2f, 0.07f, 0.23f) / s;
        capGo.GetComponent<Renderer>().sharedMaterial = cap;
        GameObject peak = GameObject.CreatePrimitive(PrimitiveType.Cube);
        peak.name = "GangCapPeak";
        Object.Destroy(peak.GetComponent<Collider>());
        peak.transform.SetParent(capGo.transform, false);
        peak.transform.localPosition = new Vector3(0f, -0.3f, 0.62f);
        peak.transform.localScale = new Vector3(0.9f, 0.25f, 0.6f);
        peak.GetComponent<Renderer>().sharedMaterial = cap;
    }

    /// A knife or machete (built here, no model needed), held in the right hand.
    public GameObject MakeBlade(Transform hand, bool machete, float personScale)
    {
        if (bladeSteel == null) { bladeSteel = NewMaterial(new Color(0.78f, 0.8f, 0.82f)); bladeSteel.SetFloat("_Smoothness", 0.8f); bladeHandle = NewMaterial(new Color(0.08f, 0.07f, 0.06f)); }
        float s = personScale < 0.0001f ? 1f : personScale;
        GameObject root = new GameObject(machete ? "Machete" : "Knife");
        root.transform.SetParent(hand, false);
        root.transform.localPosition = new Vector3(0.04f, 0.07f, 0.02f) / s;
        root.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);
        root.transform.localScale = Vector3.one / s;
        float len = machete ? 0.46f : 0.17f, wid = machete ? 0.055f : 0.022f;
        Part(root.transform, bladeHandle, new Vector3(0f, 0f, -0.02f), new Vector3(0.03f, 0.035f, machete ? 0.13f : 0.1f));
        Part(root.transform, bladeSteel, new Vector3(0f, 0.005f, (machete ? 0.13f : 0.1f) * 0.5f + len * 0.5f - 0.02f), new Vector3(0.006f, wid, len));
        if (machete) Part(root.transform, bladeSteel, new Vector3(0f, 0.016f, 0.13f * 0.5f + len - 0.05f), new Vector3(0.006f, wid * 1.35f, 0.1f));    // the wider tip
        return root;
    }

    static void Part(Transform parent, Material m, Vector3 pos, Vector3 size)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Material NewMaterial(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        Material m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    // ------------------------------------------------------------------ their corners
    /// Street corners in the gang's area: junctions inside its district, the pavement on the corner between two of
    /// the roads, at least 110 m apart, up to four.
    void FindSpots(Gang gang, LundunNavigation nav)
    {
        gang.spotsFound = true;
        LwPoly district = null;
        foreach (LwPoly d in nav.World.districts) if (d.id == gang.district) district = d;
        if (district == null) return;
        LundunRoadGraph g = nav.Graph;
        List<int> nb = new List<int>();
        List<Vector3> found = new List<Vector3>();
        for (int n = 0; n < g.NodeCount; n++)
        {
            g.NextNodes(n, -1, nb);
            if (nb.Count < 3) continue;
            NavPoint np = g.NodePosition(n);
            Vector3 c = new Vector3((float)np.x, 0f, (float)np.z);
            if (!InPoly(c.x, c.z, district.polygon)) continue;
            bool clash = false;
            foreach (Vector3 f in found) if (Flat(f, c) < 110f) clash = true;
            if (clash) continue;
            // between the first two roads out of the junction
            double w1, w2; string k1, k2;
            g.EdgeInfo(n, nb[0], out w1, out k1); g.EdgeInfo(n, nb[1], out w2, out k2);
            NavPoint p1 = g.NodePosition(nb[0]), p2 = g.NodePosition(nb[1]);
            Vector3 d1 = (new Vector3((float)p1.x, 0f, (float)p1.z) - c).normalized, d2 = (new Vector3((float)p2.x, 0f, (float)p2.z) - c).normalized;
            Vector3 mid = (d1 + d2).sqrMagnitude > 0.01f ? (d1 + d2).normalized : new Vector3(-d1.z, 0f, d1.x);
            float r = (float)System.Math.Max(w1, w2) * 0.5f + 4.5f;
            Vector3 corner = c + mid * r * 1.2f;
            const int mask = 1 << 4;                                     // the pavement area (as NPCBrain uses)
            if (!NavMesh.SamplePosition(corner, out NavMeshHit hit, 3f, mask) && !NavMesh.SamplePosition(corner, out hit, 3f, NavMesh.AllAreas)) continue;
            found.Add(hit.position);
            if (found.Count >= 4) break;
        }
        gang.spots.AddRange(found);
    }

    static bool InPoly(float x, float z, LwPoint[] p)
    {
        bool c = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if (((p[i].z > z) != (p[j].z > z)) && (x < (p[j].x - p[i].x) * (z - p[i].z) / (p[j].z - p[i].z) + p[i].x)) c = !c;
        return c;
    }

    /// For testing (in Play mode, ⋮ on this component): a group at the nearest corner of any gang, any time of day.
    [ContextMenu("Spawn a group near me (test)")]
    void SpawnNearMe()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        PlayerHealth ph = PlayerHealth.Instance;
        if (nav == null || ph == null) return;
        Gang best = null; int bestSpot = -1; float bd = float.MaxValue;
        foreach (Gang gang in gangs)
        {
            if (!gang.spotsFound) FindSpots(gang, nav);
            for (int s = 0; s < gang.spots.Count; s++)
            {
                float d = Flat(gang.spots[s], ph.Body.position);
                if (d > 25f && d < bd && !InUse(gang, s)) { bd = d; best = gang; bestSpot = s; }
            }
        }
        if (best == null) { Debug.Log("[Lundun] No gang corner found (no road graph yet?)."); return; }
        Spawn(best, bestSpot, 1f);
        Debug.Log("[Lundun] " + best.name + " out on their corner " + Mathf.RoundToInt(bd) + " m away.");
    }
}
