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
/// R4b: turf, grudges and the map:
///   turf     rival gangs whose areas meet (Tower Block Kings / Rye Lane Riders, Angel Fields Firm / Electric Youth):
///            every few minutes, when you're near a group, a group of their rivals may come over to their corner, and
///            the two fight it out (people nearby run). Police scatter both.
///   grudges  hurt one of a gang and the whole gang remembers for the rest of their night: their groups come at you
///            on sight
///   the map  once you've been into a gang's area it's shaded on the full map in their colour, with their name
///            (remembered between sessions); GangAreas lists them for missions to use
public class LundunGangs : MonoBehaviour
{
    public static LundunGangs Instance { get; private set; }
    /// Gang members out right now (LundunPopulation makes that much less room for ordinary pedestrians).
    public static int ActiveMembers { get; private set; }

    public class Gang
    {
        public string district, name, rival; public Color colour; public float from, to;
        public readonly List<Vector3> spots = new List<Vector3>();
        public bool spotsFound;
        public bool grudge;                       // R4b: James hurt one of them tonight
    }

    /// R4b: a gang's area for the map (and, later, missions): its district, name and colour.
    public struct GangArea { public string district, name; public Color colour; }
    public static readonly GangArea[] GangAreas =
    {
        new GangArea { district = "D_GILMOR", name = "TOWER BLOCK KINGS", colour = new Color(0.05f, 0.62f, 0.6f) },
        new GangArea { district = "D_BRIXTUN_EST", name = "ANGEL FIELDS FIRM", colour = new Color(0.52f, 0.18f, 0.72f) },
        new GangArea { district = "D_PECKAM", name = "RYE LANE RIDERS", colour = new Color(0.95f, 0.45f, 0.08f) },
        new GangArea { district = "D_BRIXTUN", name = "ELECTRIC YOUTH", colour = new Color(0.95f, 0.82f, 0.1f) },
    };
    const string SeenKey = "Lundun.GangArea.";
    /// R4b: James has been into this gang's area (so it shows on the map).
    public static bool Discovered(string district)
    {
        try { return PlayerPrefs.GetInt(SeenKey + district, 0) == 1; } catch { return false; }
    }

    public class Group
    {
        public Gang gang; public Vector3 spot; public int spotIndex;
        public readonly List<LundunGangMember> members = new List<LundunGangMember>();
        public bool fighting, scattering;
        public float nextTrouble, nextTaunt, lastContact;
        public int started;                               // R4b: how many there were (for morale)
        public float pinnedUntil;                         // test groups: kept whatever the time or distance until then
        public Group rival;                               // R4b: brawling with this group
        public Group raiding;                             // R4b: on their way to this group's corner
        public bool HasLiving { get { foreach (LundunGangMember m in members) if (m != null && !m.IsDead) return true; return false; } }
        public int Living { get { int n = 0; foreach (LundunGangMember m in members) if (m != null && !m.IsDead) n++; return n; } }
        /// Where the group is now (the middle of the living ones; their corner if none).
        public Vector3 Centre
        {
            get
            {
                Vector3 c = Vector3.zero; int n = 0;
                foreach (LundunGangMember m in members) if (m != null && !m.IsDead) { c += m.transform.position; n++; }
                return n > 0 ? c / n : spot;
            }
        }
    }

    [SerializeField] private int maxGroups = 2;
    [SerializeField] private float spawnFrom = 60f, spawnTo = 160f, removeBeyond = 220f;
    [Tooltip("After midnight, the chance a group has someone with a gun (knives and machetes are far more common)")]
    [SerializeField, Range(0f, 1f)] private float gunChance = 0.25f;
    [Tooltip("Ignore the time of day (for testing)")]
    [SerializeField] private bool anyTime = false;
    [Header("R4b: turf")]
    [Tooltip("Seconds between rival gangs coming over to fight (a random time in this range)")]
    [SerializeField] private Vector2 raidEvery = new Vector2(180f, 360f);
    private float nextRaid = -1f;

    private readonly List<Gang> gangs = new List<Gang>
    {
        new Gang { district = "D_GILMOR", name = "Tower Block Kings", rival = "Rye Lane Riders", colour = new Color(0.05f, 0.62f, 0.6f), from = 18f, to = 5f },
        new Gang { district = "D_BRIXTUN_EST", name = "Angel Fields Firm", rival = "Electric Youth", colour = new Color(0.52f, 0.18f, 0.72f), from = 18f, to = 5f },
        new Gang { district = "D_PECKAM", name = "Rye Lane Riders", rival = "Tower Block Kings", colour = new Color(0.95f, 0.45f, 0.08f), from = 22f, to = 4f },
        new Gang { district = "D_BRIXTUN", name = "Electric Youth", rival = "Angel Fields Firm", colour = new Color(0.95f, 0.82f, 0.1f), from = 22f, to = 4f },
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
    static readonly string[] Spotted = { "That's him!", "It's him, from before!", "Oi! Remember us?" };

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
        Discover(nav, me);
        foreach (Gang gang in gangs) if (gang.grudge && !anyTime && !InHours(gang, hour)) gang.grudge = false;   // a new night
        // tidy up: gone, all dead, far away and unseen, or their time is over and nobody's watching
        Camera cam = Camera.main;
        int living = 0;
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            Group g = groups[i];
            g.members.RemoveAll(m => m == null);
            bool any = g.HasLiving;
            float d = Flat(g.Centre, me);
            bool seen = false;
            foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead && cam != null && LundunView.CanSee(cam, m.transform.position)) seen = true;
            bool over = !anyTime && !InHours(g.gang, hour) && !g.fighting;
            bool pinned = Time.time < g.pinnedUntil && d < 400f;           // a test group: leave it be
            if (!any || (!pinned && (d > removeBeyond || over || g.scattering) && !seen && d > 40f))
            {
                foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead) Destroy(m.gameObject);
                if (g.spotIndex >= 0) spotRestUntil[g.gang.name + g.spotIndex] = Time.time + 300f;       // give the corner a rest
                foreach (Group o in groups) { if (o.rival == g) o.rival = null; if (o.raiding == g) o.raiding = null; }
                groups.RemoveAt(i);
                continue;
            }
            foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead) living++;
            Think(g, me, ph);
        }
        ActiveMembers = living;

        Raids(me, hour, cam);

        // new groups: an active gang's corner, near enough but out of sight (a raiding group doesn't count)
        int onCorners = 0; foreach (Group g in groups) if (g.spotIndex >= 0) onCorners++;
        if (onCorners >= maxGroups) return;
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
        Vector3 at = g.Centre;
        // police about: scatter
        foreach (PoliceOfficer o in PoliceOfficer.Active)
            if (o != null && !o.IsDown && Flat(o.transform.position, at) < 30f) { Scatter(g, o.transform.position); return; }
        foreach (PoliceAI c in PoliceAI.Active)
            if (c != null && Flat(c.transform.position, at) < 30f) { Scatter(g, c.transform.position); return; }

        // morale: half of them down and the rest lose their nerve
        int living = g.Living;
        if (g.started > 0 && (g.started - living) * 2 >= g.started && living < g.started) { Scatter(g, g.rival != null ? g.rival.Centre : me); return; }

        // R4b: on their way to a rival's corner: close enough, and it kicks off
        if (g.raiding != null)
        {
            if (g.raiding.scattering || !g.raiding.HasLiving) { g.raiding = null; Scatter(g, at); return; }
            float gap = Flat(at, g.raiding.Centre);
            foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead) m.March(g.raiding.Centre, gap < 45f);
            if (gap < 18f) Brawl(g, g.raiding);
            return;
        }
        // R4b: brawling: over when the other lot have gone (or run)
        if (g.rival != null)
        {
            if (g.rival.scattering || !g.rival.HasLiving)
            {
                g.rival = null;
                if (g.spotIndex < 0) { Scatter(g, at); return; }                  // raiders head off home
                foreach (LundunGangMember m in g.members) if (m != null && !m.IsDead && !g.fighting) m.BackToCorner();
            }
            return;
        }
        // R4b: a gang with a grudge comes at James on sight
        if (g.gang.grudge && !g.fighting && !ph.IsDead)
        {
            float d = Flat(me, at);
            bool onFoot = ph.OnFoot;
            VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
            bool stoppedCar = !onFoot && vs != null && vs.PlayerSpeed < 2f;
            if ((onFoot && d < 22f && CanSeeJames(at, me)) || (stoppedCar && d < 9f))
            {
                Toast(Spotted[Random.Range(0, Spotted.Length)]);
                StartFight(g);
                return;
            }
        }

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

    static bool CanSeeJames(Vector3 from, Vector3 james)
    {
        return !Physics.Linecast(from + Vector3.up * 1.6f, james + Vector3.up * 1.2f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);
    }

    // ------------------------------------------------------------------ R4b: turf
    /// Every few minutes: if James is near a group whose rivals are out tonight, a group of the rivals turns up
    /// (out of sight, 90-120 m away) and comes for their corner.
    void Raids(Vector3 me, float hour, Camera cam)
    {
        if (nextRaid < 0f) nextRaid = Time.time + Random.Range(raidEvery.x, raidEvery.y) * 0.5f;
        if (Time.time < nextRaid) return;
        foreach (Group g in groups) if (g.raiding != null) return;              // one at a time
        foreach (Group g in groups)
        {
            if (g.spotIndex < 0 || g.fighting || g.scattering || g.rival != null) continue;
            if (Flat(g.Centre, me) > 150f) continue;
            Gang rival = null;
            foreach (Gang k in gangs) if (k.name == g.gang.rival) rival = k;
            if (rival == null || (!anyTime && !InHours(rival, hour))) continue;
            // somewhere out of sight to come from
            for (int tries = 0; tries < 10; tries++)
            {
                Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                Vector3 p = g.spot + dir * Random.Range(90f, 120f);
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;
                if (cam != null && LundunView.CanSee(cam, hit.position)) continue;
                if (Flat(hit.position, me) < 45f) continue;
                Group raid = SpawnAt(rival, hit.position, -1, hour);
                if (raid == null) break;
                raid.raiding = g;
                nextRaid = Time.time + Random.Range(raidEvery.x, raidEvery.y);
                return;
            }
        }
        nextRaid = Time.time + 20f;                                            // nobody suitable: look again soon
    }

    void Brawl(Group a, Group b)
    {
        a.raiding = null;
        a.rival = b; b.rival = a;
        if (b.fighting) b.fighting = false;
        foreach (LundunGangMember m in a.members) if (m != null && !m.IsDead) m.Brawl(b);
        foreach (LundunGangMember m in b.members) if (m != null && !m.IsDead) m.Brawl(a);
        Vector3 at = (a.Centre + b.Centre) * 0.5f;
        if (LundunPopulation.Instance != null) LundunPopulation.Instance.Scare(at, 35f);    // everyone near runs
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph != null && ph.Body != null && Flat(ph.Body.position, at) < 70f)
            Toast("It's kicking off: " + b.gang.name + " v " + a.gang.name + "!");
    }

    /// A member was hurt. By a rival (in a brawl): nothing more to do. By James: the group goes for him, and the
    /// whole gang remembers.
    public void Hurt(Group g, bool byRival)
    {
        if (byRival) return;
        if (!g.gang.grudge)
        {
            g.gang.grudge = true;
            Toast("The " + g.gang.name + " won't forget that.");
        }
        StartFight(g);
    }

    /// R4b: James has walked into a gang's area for the first time: it goes on the map.
    void Discover(LundunNavigation nav, Vector3 me)
    {
        foreach (Gang gang in gangs)
        {
            if (Discovered(gang.district)) continue;
            foreach (LwPoly d in nav.World.districts)
            {
                if (d.id != gang.district || !InPoly(me.x, me.z, d.polygon)) continue;
                try { PlayerPrefs.SetInt(SeenKey + gang.district, 1); PlayerPrefs.Save(); } catch { }
                Toast(gang.name.ToUpperInvariant() + " ENDS: on your map now");
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
        if (g.raiding != null) g.raiding = null;            // James first
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
    void Spawn(Gang gang, int spotIndex, float hour) { SpawnAt(gang, gang.spots[spotIndex], spotIndex, hour); }

    /// A group of the gang at 'at' (their corner, or for a raid, wherever they set off from: spotIndex −1).
    Group SpawnAt(Gang gang, Vector3 at, int spotIndex, float hour)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return null;
        if (holder == null) { holder = new GameObject("Gangs").transform; holder.SetParent(transform, false); }
        Group g = new Group { gang = gang, spot = at, spotIndex = spotIndex, nextTrouble = Time.time + Random.Range(20f, 50f) };
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
        if (g.members.Count == 0) return null;
        g.started = g.members.Count;
        groups.Add(g);
        return g;
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

    /// Where James is (on foot, or his car).
    static Vector3 JamesAt()
    {
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        if (vs != null && vs.PlayerTarget != null) return vs.PlayerTarget.position;
        PlayerHealth ph = PlayerHealth.Instance;
        return ph != null && ph.Body != null ? ph.Body.position : Vector3.zero;
    }

    /// For testing (in Play mode, ⋮ on this component): a group right where you can see it, any time of day: on the
    /// nearest gang corner if there's one within 120 m, otherwise on the pavement about 20 m in front of the camera.
    /// Test groups stay out for 10 minutes whatever the time (unless you go 400 m away).
    [ContextMenu("Spawn a group near me (test)")]
    void SpawnNearMe() { SpawnTestGroup(); }

    Group SpawnTestGroup()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null || nav.Graph == null) { Debug.LogWarning("[Lundun] Gangs: the road map isn't loaded yet (LundunNavigation)."); return null; }
        Vector3 me = JamesAt();
        // the nearest gang corner within 120 m, if any
        Gang best = null; int bestSpot = -1; float bd = 120f;
        foreach (Gang gang in gangs)
        {
            if (!gang.spotsFound) FindSpots(gang, nav);
            for (int s = 0; s < gang.spots.Count; s++)
            {
                float d = Flat(gang.spots[s], me);
                if (d > 12f && d < bd && !InUse(gang, s)) { bd = d; best = gang; bestSpot = s; }
            }
        }
        Group g;
        if (best != null) g = SpawnAt(best, best.spots[bestSpot], bestSpot, 1f);
        else
        {
            // no corner near: put the gang whose area is nearest on the pavement in front of the camera
            Camera cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 p = me + fwd.normalized * 20f;
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 10f, 1 << 4) && !NavMesh.SamplePosition(p, out hit, 10f, NavMesh.AllAreas))
            { Debug.LogWarning("[Lundun] Gangs: nowhere to stand in front of you (no NavMesh?)."); return null; }
            best = NearestGang(nav, me);
            g = SpawnAt(best, hit.position, -1, 1f);
        }
        if (g == null) { Debug.LogWarning("[Lundun] Gangs: couldn't make anyone (no pedestrian prefabs on LundunPopulation?)."); return null; }
        g.pinnedUntil = Time.time + 600f;
        string where = Mathf.RoundToInt(Flat(g.spot, me)) + " m away";
        Toast("TEST: " + g.gang.name + " " + where);
        Debug.Log("[Lundun] Test gang: " + g.members.Count + " " + g.gang.name + " " + where + " (kept 10 minutes).");
        return g;
    }

    Gang NearestGang(LundunNavigation nav, Vector3 me)
    {
        Gang best = gangs[0]; float bd = float.MaxValue;
        foreach (Gang gang in gangs)
            foreach (LwPoly d in nav.World.districts)
            {
                if (d.id != gang.district) continue;
                Vector3 c = Vector3.zero; foreach (LwPoint q in d.polygon) c += new Vector3(q.x, 0f, q.z);
                c /= d.polygon.Length;
                float dd = Flat(c, me);
                if (dd < bd) { bd = dd; best = gang; }
            }
        return best;
    }

    /// For testing: a group near you (made if there isn't one), and their rivals turning up 40 m off and running at
    /// them, so the fight starts within a few seconds where you can watch it.
    [ContextMenu("Start a turf fight near me (test)")]
    void TurfNearMe()
    {
        Vector3 me = JamesAt();
        Group target = null; float bd = 150f;
        foreach (Group g in groups) if (!g.scattering && g.raiding == null && g.rival == null && Flat(g.Centre, me) < bd) { bd = Flat(g.Centre, me); target = g; }
        if (target == null) target = SpawnTestGroup();
        if (target == null) return;
        target.pinnedUntil = Mathf.Max(target.pinnedUntil, Time.time + 600f);
        Gang rival = null;
        foreach (Gang k in gangs) if (k.name == target.gang.rival) rival = k;
        if (rival == null) return;
        Vector3 from = Vector3.zero; bool found = false;
        for (int tries = 0; tries < 16 && !found; tries++)
        {
            Vector3 p = target.Centre + Quaternion.Euler(0f, tries * 22.5f, 0f) * Vector3.forward * 40f;
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas) && Flat(hit.position, me) > 20f) { from = hit.position; found = true; }
        }
        if (!found) { Debug.LogWarning("[Lundun] Gangs: nowhere for the rivals to come from."); return; }
        Group raid = SpawnAt(rival, from, -1, 1f);
        if (raid == null) return;
        raid.pinnedUntil = Time.time + 600f;
        raid.raiding = target;
        foreach (LundunGangMember m in raid.members) if (m != null) m.March(target.Centre, true);
        Toast("TEST: " + rival.name + " coming for the " + target.gang.name);
        Debug.Log("[Lundun] Test turf fight: " + raid.members.Count + " " + rival.name + " running at the " + target.gang.name + ".");
    }
}
