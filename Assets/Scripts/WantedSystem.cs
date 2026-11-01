using System.Collections.Generic;
using UnityEngine;

/// W1: crimes only count if someone sees them.
///   • police who see (or hear a gunshot near) a crime: stars straight away, and they know where you are
///   • a pedestrian who sees it (or hears gunfire close by) calls it in after a few seconds; police then head to
///     where the crime happened, not to where you are now. Get away (or stop them calling) and nothing happens.
///   • escaping takes longer the more stars you have (PoliceTiers)
public class WantedSystem : MonoBehaviour
{
    public static WantedSystem Instance { get; private set; }

    [Header("Heat points per crime")]
    [SerializeField] private float gunfirePerShot = 2f;
    [SerializeField] private float assault = 25f;
    [SerializeField] private float hitAndRun = 30f;
    [SerializeField] private float kill = 60f;
    [SerializeField] private float assaultOfficer = 40f;
    [SerializeField] private float killOfficer = 120f;
    [SerializeField] private float destroyPoliceCar = 100f;
    [Tooltip("E5: stealing from a shop, seen — just enough for one star")]
    [SerializeField] private float shoplifting = 22f;
    [Tooltip("E8: each report while trespassing in the royal quarter (one every few seconds you stay inside)")]
    [SerializeField] private float trespass = 22f;
    [Tooltip("E8: trespassing on its own never takes you past this many stars")]
    [SerializeField] private int trespassMaxStars = 2;

    [Header("Heat needed for each star")]
    // 2 kills = 2 stars. Kill a cop on top of that = 3 stars.
    [SerializeField] private float[] heatForStar = { 20f, 100f, 220f, 400f, 700f };

    [Header("Escaping (W1: now set per star in PoliceTiers)")]
    [SerializeField] private float baseEscapeTime = 10f;
    [SerializeField] private float extraTimePerStar = 4f;

    [Header("Witnesses (W1)")]
    [Tooltip("Pedestrians this close who can see a crime will report it")]
    [SerializeField] private float witnessSight = 30f;
    [Tooltip("Pedestrians this close hear gunfire (no need to see it)")]
    [SerializeField] private float witnessHearGunfire = 35f;
    [Tooltip("Seconds a witness takes to call it in")]
    [SerializeField] private Vector2 reportDelay = new Vector2(4f, 6f);

    [SerializeField] private VehicleSystem vehicleSystem;

    private float heat;
    private float timeSinceSeen;
    private float escapeClock;                 // W2: counts towards losing them (faster outside the search area)
    private bool escapeTimerRunning;

    class Report { public Health witness; public float heat; public Vector3 where; public float when, at; public CrimeType worst; }

    /// W1b: police now know about a crime (seen by them, or a call got through) and roughly where.
    /// Everyday patrols nearby respond, the more serious the crime the further away they come from.
    public static event System.Action<CrimeType, Vector3> CrimeKnown;

    [Header("Minor offences (W1b)")]
    [Tooltip("First minor offence (a punch, a scuffle) in front of a patrol gets a warning, not a star. A second one within this many seconds counts")]
    [SerializeField] private float warningMemory = 90f;
    private float lastWarning = -999f;

    /// A scuffle, not a crime the public would phone in.
    public static bool IsMinor(CrimeType t) => t == CrimeType.AssaultPedestrian;

    /// How serious (for which crime wins in a combined phone report).
    static int Severity(CrimeType t)
    {
        switch (t)
        {
            case CrimeType.AssaultPedestrian: return 0;
            case CrimeType.Shoplifting: return 0;
            case CrimeType.Trespass: return 0;
            case CrimeType.Gunfire: return 1;
            case CrimeType.HitAndRun: return 2;
            case CrimeType.KillPedestrian: return 3;
            case CrimeType.DestroyPoliceCar: return 4;
            case CrimeType.AssaultOfficer: return 4;
            default: return 5;               // killing an officer
        }
    }
    private readonly Dictionary<Health, Report> calls = new Dictionary<Health, Report>();
    private readonly List<Health> done = new List<Health>();
    private static readonly Collider[] nearby = new Collider[64];
    private int npcMask = -1, blockMask = -1;
    private float lastSighting = -99f;
    private float lastGunLog = -99f;

    public int Stars { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }
    public Transform PlayerTarget => vehicleSystem.PlayerTarget;
    public bool IsSearching => Stars > 0 && escapeTimerRunning && timeSinceSeen > 1f;

    // ------------------------------------------------------------------ W2: evasion feedback
    [Header("Search area (W2)")]
    [Tooltip("Radius of the search area round where they last saw you, per star (1..5)")]
    [SerializeField] private float[] searchRadius = { 60f, 80f, 110f, 140f, 180f };
    [Tooltip("Losing them goes this much faster once you're outside the search area")]
    [SerializeField] private float outsideSearchArea = 1.6f;

    /// How big the search area is right now (metres).
    public float SearchRadius => Stars <= 0 || searchRadius.Length == 0 ? 0f : searchRadius[Mathf.Clamp(Stars, 1, searchRadius.Length) - 1];
    /// James is inside the area police are searching.
    public bool InSearchArea
    {
        get
        {
            if (Stars == 0 || PlayerTarget == null) return false;
            Vector3 d = PlayerTarget.position - LastKnownPosition; d.y = 0f;
            return d.magnitude < SearchRadius;
        }
    }
    /// 0 = just seen … 1 = lost them (for the escape bar).
    public float EscapeProgress => Stars > 0 && escapeTimerRunning ? Mathf.Clamp01(escapeClock / Mathf.Max(0.1f, EscapeTime)) : 0f;
    /// Police have started looking (they've seen you, or reached the reported spot).
    public bool Hunting => Stars > 0 && escapeTimerRunning;
    /// Seconds left to lose them at the current rate.
    public float EscapeTimeLeft => Mathf.Max(0f, EscapeTime - escapeClock);
    private float EscapeTime => PoliceTiers.Instance != null ? PoliceTiers.For(Stars).escapeTime : baseEscapeTime + extraTimePerStar * (Stars - 1);
    /// Police have seen you in the last moment.
    public bool Seen => Stars > 0 && Time.time - lastSighting < 1f;
    /// A witness is on the phone right now.
    public bool BeingReported => calls.Count > 0;

    void Awake()
    {
        Instance = this;
        if (GetComponent<PoliceTiers>() == null) gameObject.AddComponent<PoliceTiers>();
    }

    /// Every crime comes through here (CrimeSystem.Report). Only counts if police or a pedestrian witnessed it.
    public void AddCrime(CrimeType type, Vector3 position)
    {
        float amount = HeatFor(type);
        if (amount <= 0f) return;
        bool gunfire = type == CrimeType.Gunfire;

        // E8: the royal quarter's guards and cameras always see a trespasser — no witness needed. On its own it
        // tops out at trespassMaxStars (stay inside and the second star comes); anything worse still adds as normal.
        if (type == CrimeType.Trespass)
        {
            if (heatForStar.Length > 0)
            {
                float cap = heatForStar[Mathf.Clamp(trespassMaxStars, 1, heatForStar.Length) - 1];
                amount = Mathf.Min(amount, Mathf.Max(0f, cap - heat));
                if (heat < heatForStar[0]) amount = Mathf.Max(amount, heatForStar[0] - heat);   // first report = one star
            }
            Vector3 seenAt = PlayerTarget != null ? PlayerTarget.position : position;
            if (amount > 0f) Apply(amount, seenAt, true);
            else { LastKnownPosition = seenAt; lastSighting = Time.time; timeSinceSeen = 0f; escapeClock = 0f; }
            Log(type, "seen by the royal guard");
            CrimeKnown?.Invoke(type, seenAt);
            return;
        }

        // police saw it (or are already watching you): straight away
        PoliceOfficer officer;
        bool policeSaw = PoliceWitness(position, gunfire, out officer);
        if (policeSaw || Seen)
        {
            // shots fired in front of the police = wanted straight away (not after ten shots)
            if (gunfire && heatForStar.Length > 0) amount = Mathf.Max(amount, heatForStar[0] - heat);
            // W1b: a first scuffle in front of a patrol (not already wanted) is a telling-off
            if (IsMinor(type) && Stars == 0 && Time.time - lastWarning > warningMemory)
            {
                lastWarning = Time.time;
                if (officer != null) officer.Warn(PlayerTarget.position);
                Toast("Officer: \"Oi! Pack it in!\"");
                return;
            }
            Apply(amount, PlayerTarget.position, true);
            Log(type, "seen/heard by police");
            CrimeKnown?.Invoke(type, PlayerTarget.position);
            return;
        }

        // a pedestrian saw or heard it: they'll call it in (not for a scuffle)
        if (IsMinor(type) && Stars == 0) return;
        Health w = CivilianWitness(position, gunfire);
        if (w == null) { Log(type, "no witnesses"); return; }   // nobody saw: no stars
        if (!calls.TryGetValue(w, out Report r))
        {
            r = new Report { witness = w, at = Time.time + Random.Range(reportDelay.x, reportDelay.y), worst = type };
            calls[w] = r;
            if (calls.Count == 1 && Stars == 0) Toast("Someone's calling the police…");
        }
        r.heat += amount;
        r.where = position;
        r.when = Time.time;
        if (Severity(type) > Severity(r.worst)) r.worst = type;
    }

    void Apply(float amount, Vector3 knownPosition, bool sighted)
    {
        heat += amount;
        LastKnownPosition = knownPosition;
        if (sighted) lastSighting = Time.time;
        timeSinceSeen = 0f; escapeClock = 0f;
        // the escape clock only starts once police have seen you and lost you, or reached the spot and found
        // nobody (as before W1) — otherwise the stars could run out before the first car even arrived
        UpdateStars();
    }

    float HeatFor(CrimeType type)
    {
        return type switch
        {
            CrimeType.Gunfire => gunfirePerShot,
            CrimeType.AssaultPedestrian => assault,
            CrimeType.HitAndRun => hitAndRun,
            CrimeType.KillPedestrian => kill,
            CrimeType.AssaultOfficer => assaultOfficer,
            CrimeType.KillOfficer => killOfficer,
            CrimeType.DestroyPoliceCar => destroyPoliceCar,
            CrimeType.Shoplifting => shoplifting,
            CrimeType.Trespass => trespass,
            _ => 0f
        };
    }

    // ------------------------------------------------------------------ witnesses
    bool PoliceWitness(Vector3 crime, bool gunfire, out PoliceOfficer officer)
    {
        officer = null;
        int stars = Mathf.Max(1, Stars);
        PoliceTiers.Tier t = PoliceTiers.For(stars);
        Vector3 at = crime + Vector3.up;
        foreach (PoliceOfficer o in PoliceOfficer.Active)
        {
            if (o == null || o.IsDown) continue;
            Vector3 eye = o.transform.position + Vector3.up * 1.6f;
            float d = Vector3.Distance(eye, at);
            if ((gunfire && d < PoliceTiers.HearGunfire) || PoliceTiers.Sees(eye, o.transform.forward, at, t.sightRange, t.sightCone, false, BlockMask))
            {
                officer = o;
                return true;
            }
        }
        foreach (PoliceAI car in PoliceAI.Active)
        {
            if (car == null || !car.enabled || car.IsLeaving) continue;
            Vector3 eye = car.transform.position + Vector3.up * 1.5f;
            float d = Vector3.Distance(eye, at);
            if (gunfire && d < PoliceTiers.HearGunfire * 0.75f) return true;
            if (PoliceTiers.Sees(eye, car.transform.forward, at, t.carSightRange, t.carSightCone, false, BlockMask)) return true;
        }
        return false;
    }

    Health CivilianWitness(Vector3 crime, bool gunfire)
    {
        if (npcMask == -1) npcMask = LayerMask.GetMask("NPC");
        float r = Mathf.Max(witnessSight, witnessHearGunfire);
        int n = Physics.OverlapSphereNonAlloc(crime, r, nearby, npcMask, QueryTriggerInteraction.Ignore);
        Vector3 at = crime + Vector3.up;
        Health best = null; float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Health h = nearby[i].GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.GetComponent<NPCBrain>() == null) continue;
            if (calls.ContainsKey(h)) return h;                  // already on the phone: add to their report
            Vector3 eye = h.transform.position + Vector3.up * 1.6f;
            float d = Vector3.Distance(eye, at);
            bool ok = (gunfire && d < witnessHearGunfire)
                      || (d < witnessSight && !Physics.Linecast(eye, at, BlockMask, QueryTriggerInteraction.Ignore));
            if (ok && d < bestD) { bestD = d; best = h; }
        }
        return best;
    }

    void ProcessCalls()
    {
        if (calls.Count == 0) return;
        done.Clear();
        foreach (KeyValuePair<Health, Report> kv in calls)
        {
            Report r = kv.Value;
            if (r.witness == null || r.witness.IsDead) { done.Add(kv.Key); continue; }    // they never got through
            if (Time.time < r.at) continue;
            done.Add(kv.Key);
            int before = Stars;
            // police go to where it happened (or where they last saw you, if that's newer)
            Vector3 known = Stars > 0 && timeSinceSeen < Time.time - r.when ? LastKnownPosition : r.where;
            Apply(r.heat, known, false);
            Log(r.worst, "phoned in by a witness");
            CrimeKnown?.Invoke(r.worst, known);
            if (before == 0 && Stars > 0) Toast("A witness called the police");
        }
        foreach (Health h in done) calls.Remove(h);
    }

    int BlockMask { get { if (blockMask == -1) blockMask = LayerMask.GetMask("Default"); return blockMask; } }

    /// Editor only: every crime decision in the Console, so we can see exactly why the police did or didn't come.
    void Log(CrimeType type, string what)
    {
#if UNITY_EDITOR
        if (type == CrimeType.Gunfire && Time.unscaledTime - lastGunLog < 1f) return;      // one line a second while firing
        if (type == CrimeType.Gunfire) lastGunLog = Time.unscaledTime;
        Debug.Log("[Lundun] Crime: " + type + " — " + what + ". Heat " + heat.ToString("0") + ", " + Stars + "★");
#endif
    }

    static void Toast(string message)
    {
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(message);
    }

    // Police call this every frame they can see the player
    public void ReportSighting(Vector3 position)
    {
        // W2: they'd lost you and now they've got you again
        if (Stars > 0 && escapeTimerRunning && Time.time - lastSighting > 2f)
        {
            Toast("SPOTTED!");
            LundunHaptics.Medium();
        }
        lastSighting = Time.time;
        LastKnownPosition = position;
        timeSinceSeen = 0f; escapeClock = 0f;
        escapeTimerRunning = true;
    }

    // Police call this when they reach the last known position and find nothing
    public void BeginSearch()
    {
        escapeTimerRunning = true;
    }

    void Update()
    {
        ProcessCalls();
        if (Stars == 0 || !escapeTimerRunning) return;

        timeSinceSeen += Time.deltaTime;
        escapeClock += Time.deltaTime * (InSearchArea ? 1f : outsideSearchArea);
        if (escapeClock >= EscapeTime)
        {
            ClearWanted();
            Toast("LOST THEM");
            LundunHaptics.Medium();
        }
    }

    void UpdateStars()
    {
        int stars = 0;
        foreach (float threshold in heatForStar)
            if (heat >= threshold) stars++;

        // Stars only go up while you're wanted. They clear all at once when you escape.
        Stars = Mathf.Max(Stars, stars);
    }

    public void ClearWanted()
    {
        heat = 0f;
        Stars = 0;
        escapeTimerRunning = false;
        calls.Clear();
    }

    // TESTING: in Play mode, click the ⋮ on this component and choose "Add a star"
    [ContextMenu("Add a star")]
    void AddStarForTesting()
    {
        if (Stars >= heatForStar.Length) return;
        heat = Mathf.Max(heat, heatForStar[Stars]);
        LastKnownPosition = PlayerTarget.position;
        timeSinceSeen = 0f; escapeClock = 0f;
        UpdateStars();
        CrimeKnown?.Invoke(CrimeType.KillPedestrian, LastKnownPosition);
    }

    // TESTING: jump straight to a level
    [ContextMenu("Set 3 stars")] void Three() { SetStarsForTesting(3); }
    [ContextMenu("Set 5 stars")] void Five() { SetStarsForTesting(5); }
    void SetStarsForTesting(int n)
    {
        heat = Mathf.Max(heat, heatForStar[Mathf.Clamp(n, 1, heatForStar.Length) - 1]);
        LastKnownPosition = PlayerTarget.position;
        timeSinceSeen = 0f; escapeClock = 0f;
        escapeTimerRunning = false;
        UpdateStars();
        Debug.Log("[Lundun] Wanted level set to " + Stars + " star(s) for testing");
        CrimeKnown?.Invoke(n >= 3 ? CrimeType.KillOfficer : CrimeType.KillPedestrian, LastKnownPosition);
    }
}
