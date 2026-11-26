using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// G8.0: the incident log. The ONE place in the game that records notable moments: police chases (first star until
/// they end), big crashes, WASTED, BUSTED, carjackings, reported crimes with nobody chasing. It only watches: it never
/// changes what the police, people, missions or saves do. Nothing is written to disk (session only), and Free Roam and
/// Story keep completely separate histories.
///
/// RULE for every later system (replays, share cards, radio, feed, bystanders, wanted 2.0): only this log creates
/// incidents. Read them, attach to them; never keep a competing incident list.
///   • player-facing features (cards, radio reports, feed posts) act only on SETTLED incidents: the Settled event, or
///     Eligible(record). A record from a failed, retried, quit or abandoned mission attempt never settles (VOID), and a
///     record folded into a chase never settles (MERGED).
///   • media capture (G8.1b) may Attach on Closed, even while the record is HELD (its mission attempt not decided yet),
///     so the CCTV window isn't lost; player-facing code reads media only through TryGetSettled. If the record goes VOID
///     or is evicted, every attachment's release action runs.
///   • Pin(id) while you need a record (120 s max); the history is 40 per mode, never evicting open, HELD or pinned
///     records, and a MERGED record leaves only with its chase.
///
/// Sources: the wanted level (read every frame) and notify-only hooks: WantedSystem.LostThem / StarDropped / Raised,
/// CrimeSystem.Reported, CarCrash.Crashed, plus events that already existed (WASTED, BUSTED, CCTV, carjack, train,
/// missions, gunshots, respawn). Known gap (by design, G8a): car thefts nobody sees or reports aren't recorded.
/// Testing: in the Editor each record prints one line when it opens and when it closes; the live list is on this
/// object's Inspector; ⋮ → "Dump incidents".
public class LundunIncidents : MonoBehaviour
{
    public static LundunIncidents Instance { get; private set; }

    [Tooltip("Editor only: a Console line when an incident opens and when it closes (never in builds)")]
    [SerializeField] private bool logToConsole = true;
    [Header("This mode's history (read only, newest first)")]
    [SerializeField] private string mode;
    [SerializeField] private string openChase;
    [SerializeField] private int minorCrashes;
    [SerializeField] private List<LundunIncident> incidents = new List<LundunIncident>();

    static readonly LundunIncidentLog[] logs = { new LundunIncidentLog(0), new LundunIncidentLog(1) };
    int myMode;                                    // fixed for this scene's life (a mode switch reloads the scene)
    LundunIncidentLog Log { get { return logs[myMode]; } }

    WantedSystem wanted;
    VehicleSystem vehicles;
    LundunMissionRun trackedRun;

    // ------------------------------------------------------------------ the API later phases use
    public static event Action<LundunIncident> Opened { add { LundunIncidentLog.Opened += value; } remove { LundunIncidentLog.Opened -= value; } }
    public static event Action<LundunIncident> Updated { add { LundunIncidentLog.Updated += value; } remove { LundunIncidentLog.Updated -= value; } }
    public static event Action<LundunIncident> Closed { add { LundunIncidentLog.Closed += value; } remove { LundunIncidentLog.Closed -= value; } }
    public static event Action<LundunIncident> Settled { add { LundunIncidentLog.Settled += value; } remove { LundunIncidentLog.Settled -= value; } }
    public static event Action<LundunIncident> Voided { add { LundunIncidentLog.Voided += value; } remove { LundunIncidentLog.Voided -= value; } }
    public static event Action<LundunIncident> Evicted { add { LundunIncidentLog.Evicted += value; } remove { LundunIncidentLog.Evicted -= value; } }

    static LundunIncidentLog Current { get { return logs[LundunGame.IsStory ? 1 : 0]; } }
    /// This mode's retained records, newest first.
    public static IReadOnlyList<LundunIncident> History { get { return Current.History; } }
    /// The chase going on now (null if none).
    public static LundunIncident Chase { get { return Current.OpenChase; } }
    public static LundunIncident Get(string id) { return Current.Get(id); }
    public static LundunIncident Latest(LundunIncidentKind kind) { return Current.Latest(kind); }
    /// The one eligibility test: closed and SETTLED.
    public static bool Eligible(LundunIncident r) { return LundunIncidentLog.Eligible(r); }
    public static int MinorCrashes { get { return Current.MinorCrashes; } }
    public static bool Pin(string id) { return Current.Pin(id, Time.unscaledTime); }
    public static void Unpin(string id) { Current.Unpin(id); }
    public static bool Attach(string id, string key, object value, Action release) { return Current.Attach(id, key, value, release); }
    /// Player-facing: only a SETTLED record's media.
    public static bool TryGetSettled(string id, string key, out object value) { return Current.TryGetSettled(id, key, out value); }
    /// Only for the system that attached it (e.g. G8.1b checking its own capture while the record is HELD).
    public static bool TryGetAny(string id, string key, out object value) { return Current.TryGetAny(id, key, out value); }

    // ------------------------------------------------------------------ attaching itself (both modes)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterFirstLoad()
    {
        SceneManager.sceneLoaded += (s, m) => Ensure();
        Ensure();
    }

    static void Ensure()
    {
        if (FindAnyObjectByType<VehicleSystem>() == null) return;          // not the city (e.g. the Prototype scene)
        if (FindAnyObjectByType<LundunIncidents>() != null) return;
        new GameObject("LundunIncidents").AddComponent<LundunIncidents>();
    }

    void Awake()
    {
        Instance = this;
        myMode = LundunGame.IsStory ? 1 : 0;
        mode = myMode == 1 ? "Story" : "Free Roam";
        Log.Places = Place;
        Log.Print = s => { if (logToConsole && Application.isEditor) Debug.Log("[Incidents] " + s); };
        incidents = Log.HistoryList;                                  // the Inspector shows the real list
    }

    void Start()
    {
        wanted = WantedSystem.Instance;
        vehicles = FindAnyObjectByType<VehicleSystem>();
    }

    void OnEnable()
    {
        WantedSystem.LostThem += OnLostThem;
        WantedSystem.StarDropped += OnStarDropped;
        WantedSystem.Raised += OnRaised;
        WantedSystem.CrimeKnown += OnCrimeKnown;
        CrimeSystem.Reported += OnReported;
        CarCrash.Crashed += OnCrashed;
        WastedSystem.Happened += OnWasted;
        BustedSystem.Happened += OnBusted;
        LundunCctv.Spotted += OnSpotted;
        LundunCctv.PlateRead += OnPlateRead;
        LundunTrafficCar.Carjacked += OnCarjacked;
        LundunTrain.Departed += OnTrain;
        LundunMissions.Passed += OnPassed;
        PlayerCombat.GunshotFired += OnShot;
        VehicleSystem.Respawned += OnRespawned;
    }

    void OnDisable()
    {
        WantedSystem.LostThem -= OnLostThem;
        WantedSystem.StarDropped -= OnStarDropped;
        WantedSystem.Raised -= OnRaised;
        WantedSystem.CrimeKnown -= OnCrimeKnown;
        CrimeSystem.Reported -= OnReported;
        CarCrash.Crashed -= OnCrashed;
        WastedSystem.Happened -= OnWasted;
        BustedSystem.Happened -= OnBusted;
        LundunCctv.Spotted -= OnSpotted;
        LundunCctv.PlateRead -= OnPlateRead;
        LundunTrafficCar.Carjacked -= OnCarjacked;
        LundunTrain.Departed -= OnTrain;
        LundunMissions.Passed -= OnPassed;
        PlayerCombat.GunshotFired -= OnShot;
        VehicleSystem.Respawned -= OnRespawned;
    }

    void OnDestroy()
    {
        // the scene's going (mode switch, new story, quitting): anything open is abandoned, a running attempt is void
        try { Log.Abandon(Context()); } catch (Exception e) { Debug.LogException(e); }
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ every frame (after everything else has run)
    void LateUpdate()
    {
        try
        {
            if (wanted == null) wanted = WantedSystem.Instance;
            LundunMissionRun run = LundunGame.IsStory ? LundunMissions.CurrentRun : null;
            bool runChanged = run != trackedRun;
            trackedRun = run;
            Log.Frame(Context(), wanted != null ? wanted.Stars : 0, run, run != null && run.failed != null,
                      run != null && run.mission != null ? run.mission.Id : null, runChanged);
            openChase = Log.OpenChase != null ? Log.OpenChase.id : "";
            minorCrashes = Log.MinorCrashes;
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    // ------------------------------------------------------------------ the hooks (each wrapped: the log can never get in the way)
    void OnLostThem() { Safe(() => Log.LostThem(Time.frameCount)); }
    void OnStarDropped(int left) { Safe(() => Log.StarDropped(Time.frameCount, left)); }
    void OnRaised(int stars) { Safe(() => Log.Raised(Time.frameCount)); }
    void OnTrain() { Safe(() => Log.Train(Time.frameCount)); }
    void OnCrimeKnown(CrimeType t, Vector3 at) { Safe(() => Log.PoliceTold(Context())); }
    void OnReported(CrimeType t, Vector3 at) { Safe(() => Log.Crime(Context(), t, at)); }
    void OnCrashed(CarController car, float impact, Vector3 at) { Safe(() => Log.Crash(Context(), impact, at)); }
    void OnWasted() { Safe(() => Log.Wasted(Context())); }
    void OnBusted() { Safe(() => Log.Busted(Context())); }
    void OnCarjacked(CarController car) { Safe(() => Log.Carjack(Context(), car != null ? Clean(car.name) : "")); }
    void OnPlateRead(int cam) { Safe(() => Log.PlateRead(Context())); }
    void OnShot(Vector3 at) { Safe(() => Log.Shot(Context())); }
    void OnPassed(string id, int medal) { Safe(() => Log.Passed(Context(), id)); }
    void OnRespawned() { Safe(() => Log.Respawned(Context())); }
    void OnSpotted(int cam)
    {
        Safe(() =>
        {
            bool identified = false;
            try { identified = LundunCctv.Instance != null && LundunCctv.Instance.Identifies(cam); } catch (Exception) { }
            Log.Cctv(Context(), identified);
        });
    }

    static void Safe(Action a) { try { a(); } catch (Exception e) { Debug.LogException(e); } }

    LundunIncidentLog.Now Context()
    {
        LundunIncidentLog.Now n = new LundunIncidentLog.Now();
        n.frame = Time.frameCount;
        n.time = Time.time;
        n.unscaled = Time.unscaledTime;
        n.hour = LundunClock.Instance != null ? LundunClock.Instance.Hour : -1f;
        n.real = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        if (vehicles != null && vehicles.PlayerTarget != null) n.at = vehicles.PlayerTarget.position;
        n.vehicle = vehicles != null && vehicles.IsDriving && vehicles.CurrentCar != null ? Clean(vehicles.CurrentCar.name) : "";
        return n;
    }

    static string Clean(string n) { return string.IsNullOrEmpty(n) ? "" : n.Replace("(Clone)", "").Trim(); }

    // ------------------------------------------------------------------ places: the district and the nearest named road
    // (a small private copy of the full map's lookup, so nothing else changes; base city and expansions)
    void Place(Vector3 p, out string district, out string street)
    {
        district = "Lundun"; street = "";
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null) return;
        LundunWorldData w = nav.World;
        string d = District(w.districts, p, true) ?? (w.expansion != null ? District(w.expansion.districts, p, true) : null)
                   ?? District(w.districts, p, false) ?? (w.expansion != null ? District(w.expansion.districts, p, false) : null);
        if (!string.IsNullOrEmpty(d)) district = d;
        float best = 40f; string s = null;
        Nearest(w.roads, p, ref best, ref s);
        if (w.expansion != null) Nearest(w.expansion.roads, p, ref best, ref s);
        street = s ?? "";
    }

    static string District(LwPoly[] polys, Vector3 p, bool skipPen)
    {
        if (polys == null) return null;
        foreach (LwPoly d in polys)
        {
            if (d == null || d.polygon == null || (skipPen && d.id == "D_CANARY_PEN")) continue;
            if (Inside(p.x, p.z, d.polygon)) return d.name;
        }
        return null;
    }

    static bool Inside(float x, float z, LwPoint[] poly)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].z > z) != (poly[j].z > z) && x < (poly[j].x - poly[i].x) * (z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x) c = !c;
        return c;
    }

    static void Nearest(LwRoad[] roads, Vector3 p, ref float best, ref string name)
    {
        if (roads == null) return;
        Vector2 q = new Vector2(p.x, p.z);
        foreach (LwRoad r in roads)
        {
            if (r == null || r.points == null || string.IsNullOrEmpty(r.name)) continue;
            for (int i = 0; i + 1 < r.points.Length; i++)
            {
                Vector2 a = new Vector2(r.points[i].x, r.points[i].z), b = new Vector2(r.points[i + 1].x, r.points[i + 1].z);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                float d = Vector2.Distance(q, a + ab * t);
                if (d < best) { best = d; name = r.name; }
            }
        }
    }

    [ContextMenu("Dump incidents")]
    void Dump()
    {
        Debug.Log("[Incidents] " + mode + ": " + Log.HistoryList.Count + " kept, " + Log.MinorCrashes + " minor crashes");
        foreach (LundunIncident r in Log.HistoryList) Debug.Log("[Incidents]   " + r.Line());
    }
}

// ====================================================================== the record
public enum LundunIncidentKind { Chase, Crash, Wasted, Busted, Carjack, Street }
public enum LundunIncidentEnd { Open, Escaped, EscapedByTrain, Resprayed, Wasted, Busted, Mission, Cleared, Abandoned, Ended }
/// Pending: no mission attempt involved, not closed yet. Held: its attempt isn't decided. Settled: safe for
/// player-facing features. Void: failed / retried / quit / abandoned attempt. Merged: folded into a chase.
public enum LundunIncidentStatus { Pending, Held, Settled, Void, Merged }

[Serializable]
public class LundunIncident
{
    public string id;
    public int mode;                               // 0 Free Roam, 1 Story
    public LundunIncidentKind kind;
    public LundunIncidentEnd end;
    public LundunIncidentStatus status;
    public bool closed;
    public int version;

    public float startTime, endTime;               // game seconds (pause doesn't count)
    public float startHour = -1f;                  // the game clock
    public string startReal, endReal;              // the real date and time (feed timestamps)
    public Vector3 startPos, endPos;
    public string startDistrict, startStreet, endDistrict, endStreet;
    public int startStars, peakStars;

    public int crimes, kills, officersKilled, policeCars, thefts, shots, carjacks, crashes;
    public float maxImpact;
    public int policeTold, cctvSightings, plateReads;
    public bool onCctv, identified;
    public string vehicle;                         // "" on foot

    public string missionId;                       // "" outside a mission
    public int attempt;                            // 0 outside a mission
    public bool scripted;                          // a mission raised the wanted level itself (WantedSystem.Raised)
    public string causeId, mergedInto;

    [NonSerialized] public float lastActivity, lastCollision, pinnedUntil;
    [NonSerialized] internal Dictionary<string, KeyValuePair<object, Action>> media;

    public float Duration { get { return Mathf.Max(0f, (closed ? endTime : lastActivity) - startTime); } }

    public string Line()
    {
        string stars = peakStars > 0 ? " " + new string('★', peakStars) : "";
        string where = startDistrict + (string.IsNullOrEmpty(startStreet) ? "" : " · " + startStreet);
        int secs = Mathf.RoundToInt(Duration);
        string s = id + " " + kind.ToString().ToUpperInvariant() + stars + " " + where + " · " + (secs / 60) + ":" + (secs % 60).ToString("00")
                   + " · " + (closed ? end.ToString().ToUpperInvariant() : "OPEN") + " · " + status.ToString().ToUpperInvariant();
        if (kills > 0) s += " · " + kills + " killed";
        if (policeCars > 0) s += " · " + policeCars + " police car" + (policeCars > 1 ? "s" : "") + " destroyed";
        if (crashes > 0) s += " · " + crashes + " crash" + (crashes > 1 ? "es" : "") + " (max " + maxImpact.ToString("0") + " m/s)";
        if (carjacks > 0) s += " · " + carjacks + " carjack" + (carjacks > 1 ? "s" : "");
        if (onCctv) s += " · on CCTV" + (identified ? " (identified)" : " (face not clear)");
        if (plateReads > 0) s += " · " + plateReads + " plate read" + (plateReads > 1 ? "s" : "");
        if (!string.IsNullOrEmpty(missionId)) s += " · " + missionId + " #" + attempt + (scripted ? " scripted" : "");
        if (!string.IsNullOrEmpty(causeId)) s += " · cause " + causeId;
        if (!string.IsNullOrEmpty(mergedInto)) s += " · merged into " + mergedInto;
        return s;
    }
}

// ====================================================================== the log itself (plain C#, one per mode)
/// Everything the incident log decides, kept apart from Unity's frame loop so it can be driven (and tested) with
/// explicit inputs. LundunIncidents feeds it; nothing else should.
public sealed class LundunIncidentLog
{
    public const int Keep = 40;                    // records per mode (a soft cap: open / HELD / pinned stay)
    public const float BigCrash = 12f;             // m/s: a CRASH record from this impact up
    public const float SameCrash = 1.5f;           // s: collisions this close together are one crash
    public const float CrashQuiet = 10f;           // s: a CRASH / CARJACK record closes after this long with nothing new
    public const float StreetQuiet = 20f;          // s: a STREET record closes after this long with nothing new
    public const float PinFor = 120f;              // s: a pin lasts at most this long (real time)
    public const float UpdateEvery = 0.5f;         // s: Updated at most twice a second
    public const float AfterArrest = 3f;           // s: a second WASTED / BUSTED inside this is the same one

    public struct Now { public int frame; public float time, unscaled, hour; public string real; public Vector3 at; public string vehicle; }
    public delegate void PlaceLookup(Vector3 p, out string district, out string street);

    public static event Action<LundunIncident> Opened, Updated, Closed, Settled, Voided, Evicted;

    public PlaceLookup Places;
    public Action<string> Print;

    readonly int mode;
    readonly List<LundunIncident> history = new List<LundunIncident>();      // newest first
    int nextId;
    public int MinorCrashes { get; private set; }
    public LundunIncident OpenChase { get; private set; }
    LundunIncident street, crash, carjack;          // open standalone records (at most one of each)

    // mission attempts
    enum AttemptState { Running, Passed, Void }
    readonly Dictionary<int, AttemptState> attempts = new Dictionary<int, AttemptState>();
    static int attemptSerial;                      // shared by both modes: attempt numbers never repeat
    int attemptNow;                                // 0 = no attempt running
    string attemptMission;
    object attemptRun;

    // what happened this frame
    int lostThemFrame = -1, droppedFrame = -1, droppedTo = -1, raisedFrame = -1, trainFrame = -1;
    bool holdUntilClear;                           // after WASTED / BUSTED: no new chase until the stars are cleared
    float lastArrest = -99f, lastMinor = -99f, lastUpdateSent = -99f;
    readonly HashSet<LundunIncident> dirty = new HashSet<LundunIncident>();

    public LundunIncidentLog(int mode) { this.mode = mode; }

    public IReadOnlyList<LundunIncident> History { get { return history; } }
    public List<LundunIncident> HistoryList { get { return history; } }

    public LundunIncident Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (LundunIncident r in history) if (r.id == id) return r;
        return null;
    }

    public LundunIncident Latest(LundunIncidentKind kind)
    {
        foreach (LundunIncident r in history) if (r.kind == kind) return r;
        return null;
    }

    public static bool Eligible(LundunIncident r) { return r != null && r.closed && r.status == LundunIncidentStatus.Settled; }

    // ------------------------------------------------------------------ pins and attachments
    public bool Pin(string id, float unscaledNow)
    {
        LundunIncident r = Get(id);
        if (r == null || r.status == LundunIncidentStatus.Void || r.status == LundunIncidentStatus.Merged) return false;
        r.pinnedUntil = unscaledNow + PinFor;
        return true;
    }

    public void Unpin(string id) { LundunIncident r = Get(id); if (r != null) r.pinnedUntil = 0f; }

    public bool Attach(string id, string key, object value, Action release)
    {
        LundunIncident r = Get(id);
        if (r == null || !r.closed || r.status == LundunIncidentStatus.Void || r.status == LundunIncidentStatus.Merged)
        {
            Run(release);                                              // nothing to keep it on: hand it straight back
            return false;
        }
        if (r.media == null) r.media = new Dictionary<string, KeyValuePair<object, Action>>();
        KeyValuePair<object, Action> old;
        if (r.media.TryGetValue(key, out old)) Run(old.Value);
        r.media[key] = new KeyValuePair<object, Action>(value, release);
        return true;
    }

    public bool TryGetSettled(string id, string key, out object value)
    {
        value = null;
        LundunIncident r = Get(id);
        if (!Eligible(r) || r.media == null) return false;
        KeyValuePair<object, Action> m;
        if (!r.media.TryGetValue(key, out m)) return false;
        value = m.Key; return true;
    }

    public bool TryGetAny(string id, string key, out object value)
    {
        value = null;
        LundunIncident r = Get(id);
        if (r == null || r.media == null) return false;
        KeyValuePair<object, Action> m;
        if (!r.media.TryGetValue(key, out m)) return false;
        value = m.Key; return true;
    }

    static void Release(LundunIncident r)
    {
        if (r.media == null) return;
        foreach (KeyValuePair<object, Action> m in r.media.Values) Run(m.Value);
        r.media.Clear();
    }

    static void Run(Action a) { if (a == null) return; try { a(); } catch (Exception e) { Debug.LogException(e); } }
    static void Fire(Action<LundunIncident> e, LundunIncident r) { if (e == null) return; try { e(r); } catch (Exception x) { Debug.LogException(x); } }

    // ------------------------------------------------------------------ notify-only hooks
    public void LostThem(int frame) { lostThemFrame = frame; }
    public void StarDropped(int frame, int left) { droppedFrame = frame; droppedTo = left; }
    public void Raised(int frame) { raisedFrame = frame; if (OpenChase != null) { OpenChase.scripted = true; Touch(OpenChase); } }
    public void Train(int frame) { trainFrame = frame; }

    public void Crime(Now n, CrimeType t, Vector3 at)
    {
        LundunIncident r = OpenChase ?? Standalone(n, ref street, LundunIncidentKind.Street);
        r.crimes++;
        switch (t)
        {
            case CrimeType.KillPedestrian: r.kills++; break;
            case CrimeType.KillOfficer: r.kills++; r.officersKilled++; break;
            case CrimeType.DestroyPoliceCar: r.policeCars++; break;
            case CrimeType.CarTheft: r.thefts++; break;
        }
        Touch(r, n);
    }

    public void Crash(Now n, float impact, Vector3 at)
    {
        bool sameCrash = n.time - lastMinor < SameCrash;
        lastMinor = n.time;
        if (impact < BigCrash)
        {
            // a smaller knock: the chase's counter, else an open STREET / CRASH record's, else the session tally
            LundunIncident host = OpenChase ?? crash ?? street;
            if (host == null) { if (!sameCrash) MinorCrashes++; return; }
            if (!sameCrash) host.crashes++;
            Touch(host, n);
            return;
        }
        if (OpenChase != null)
        {
            if (!sameCrash || OpenChase.lastCollision < n.time - SameCrash) OpenChase.crashes++;
            OpenChase.maxImpact = Mathf.Max(OpenChase.maxImpact, impact);
            OpenChase.lastCollision = n.time;
            Touch(OpenChase, n);
            return;
        }
        bool isNew = crash == null;
        LundunIncident c = Standalone(n, ref crash, LundunIncidentKind.Crash);
        if (isNew || n.time - c.lastCollision >= SameCrash) c.crashes++;
        c.maxImpact = Mathf.Max(c.maxImpact, impact);
        c.lastCollision = n.time;
        Touch(c, n);
    }

    public void Carjack(Now n, string car)
    {
        LundunIncident r = OpenChase ?? Standalone(n, ref carjack, LundunIncidentKind.Carjack);
        r.carjacks++;
        if (!string.IsNullOrEmpty(car)) r.vehicle = car;
        Touch(r, n);
    }

    public void Cctv(Now n, bool identified)
    {
        LundunIncident r = OpenChase ?? Recent();
        if (r == null) return;
        r.onCctv = true; r.cctvSightings++;
        if (identified) r.identified = true;
        Touch(r, n);
    }

    public void PlateRead(Now n) { LundunIncident r = OpenChase ?? Recent(); if (r == null) return; r.plateReads++; r.onCctv = true; Touch(r, n); }
    public void PoliceTold(Now n) { LundunIncident r = OpenChase ?? Recent(); if (r == null) return; r.policeTold++; Touch(r, n); }
    public void Shot(Now n) { LundunIncident r = OpenChase ?? street; if (r == null) return; r.shots++; Touch(r, n); }

    public void Wasted(Now n) { Arrest(n, LundunIncidentKind.Wasted, LundunIncidentEnd.Wasted); }
    public void Busted(Now n) { Arrest(n, LundunIncidentKind.Busted, LundunIncidentEnd.Busted); }

    void Arrest(Now n, LundunIncidentKind kind, LundunIncidentEnd end)
    {
        if (n.time - lastArrest < AfterArrest) return;                 // the same death / arrest
        lastArrest = n.time;
        holdUntilClear = true;                                         // its sequence clears the stars a few seconds later
        if (OpenChase != null) { CloseRecord(OpenChase, n, end); OpenChase = null; return; }
        LundunIncident r = New(n, kind);                               // e.g. died in a crash with no stars
        r.peakStars = r.startStars = 0;
        Absorb(r, ref crash, n); Absorb(r, ref carjack, n); Absorb(r, ref street, n);   // the crash that killed him: one record
        CloseRecord(r, n, end);
    }

    public void Respawned(Now n)
    {
        // a clean break: nothing carries over from before the hospital / the station
        CloseStandalone(n, ref street); CloseStandalone(n, ref crash); CloseStandalone(n, ref carjack);
    }

    public void Passed(Now n, string missionId)
    {
        if (attemptNow == 0 || attemptMission != missionId) return;
        attempts[attemptNow] = AttemptState.Passed;
        foreach (LundunIncident r in history.ToArray())
            if (r.attempt == attemptNow && r.closed && r.status == LundunIncidentStatus.Held) Settle(r);
    }

    // ------------------------------------------------------------------ every frame
    public void Frame(Now n, int stars, object run, bool runFailed, string missionId, bool runChanged)
    {
        // mission attempts: a new run object is a new attempt; one that ends without Passed (or fails) is void
        if (run != attemptRun)
        {
            if (attemptNow != 0 && attempts[attemptNow] == AttemptState.Running) VoidAttempt(attemptNow);
            attemptRun = run;
            if (run != null) { attemptNow = ++attemptSerial; attemptMission = missionId; attempts[attemptNow] = AttemptState.Running; }
            else { attemptNow = 0; attemptMission = null; }
        }
        if (attemptNow != 0 && runFailed && attempts[attemptNow] == AttemptState.Running) VoidAttempt(attemptNow);

        // the chase: the wanted level read every frame (nothing written back)
        if (stars <= 0) holdUntilClear = false;
        if (OpenChase == null && stars > 0 && !holdUntilClear)
        {
            LundunIncident c = New(n, LundunIncidentKind.Chase);
            c.startStars = c.peakStars = stars;
            c.scripted = raisedFrame == n.frame && attemptNow != 0;
            OpenChase = c;
            Absorb(c, ref crash, n); Absorb(c, ref carjack, n); Absorb(c, ref street, n);
        }
        else if (OpenChase != null && stars > OpenChase.peakStars)
        {
            OpenChase.peakStars = stars;
            if (raisedFrame == n.frame && attemptNow != 0) OpenChase.scripted = true;
            Touch(OpenChase, n);
        }
        else if (OpenChase != null && stars <= 0)
        {
            LundunIncidentEnd end;
            if (lostThemFrame == n.frame) end = LundunIncidentEnd.Escaped;
            else if (droppedFrame == n.frame && droppedTo <= 0) end = LundunIncidentEnd.Resprayed;
            else if (trainFrame == n.frame) end = LundunIncidentEnd.EscapedByTrain;
            else if (runChanged || (attemptNow == 0 && OpenChase.attempt != 0 && attempts.ContainsKey(OpenChase.attempt)
                                     && attempts[OpenChase.attempt] != AttemptState.Running)) end = LundunIncidentEnd.Mission;
            else end = LundunIncidentEnd.Cleared;
            CloseRecord(OpenChase, n, end);
            OpenChase = null;
        }

        // standalone records close after their quiet time
        if (crash != null && n.time - crash.lastActivity >= CrashQuiet) CloseStandalone(n, ref crash);
        if (carjack != null && n.time - carjack.lastActivity >= CrashQuiet) CloseStandalone(n, ref carjack);
        if (street != null && n.time - street.lastActivity >= StreetQuiet) CloseStandalone(n, ref street);

        // Updated, at most twice a second
        if (dirty.Count > 0 && n.unscaled - lastUpdateSent >= UpdateEvery)
        {
            lastUpdateSent = n.unscaled;
            foreach (LundunIncident r in new List<LundunIncident>(dirty)) if (!r.closed) Fire(Updated, r);
            dirty.Clear();
        }

        // expired pins, then the cap
        foreach (LundunIncident r in history) if (r.pinnedUntil > 0f && n.unscaled >= r.pinnedUntil) r.pinnedUntil = 0f;
        Trim(n);
    }

    /// The scene's going: anything open is ABANDONED and a running attempt is VOID.
    public void Abandon(Now n)
    {
        if (attemptNow != 0 && attempts[attemptNow] == AttemptState.Running) VoidAttempt(attemptNow);
        if (OpenChase != null) { CloseRecord(OpenChase, n, LundunIncidentEnd.Abandoned); OpenChase = null; }
        if (street != null) { CloseRecord(street, n, LundunIncidentEnd.Abandoned); street = null; }
        if (crash != null) { CloseRecord(crash, n, LundunIncidentEnd.Abandoned); crash = null; }
        if (carjack != null) { CloseRecord(carjack, n, LundunIncidentEnd.Abandoned); carjack = null; }
        attemptNow = 0; attemptRun = null; attemptMission = null;
        holdUntilClear = false;
        dirty.Clear();
    }

    // ------------------------------------------------------------------ inner workings
    LundunIncident New(Now n, LundunIncidentKind kind)
    {
        LundunIncident r = new LundunIncident();
        r.id = (mode == 1 ? "S" : "F") + (++nextId);
        r.mode = mode; r.kind = kind; r.end = LundunIncidentEnd.Open;
        r.startTime = r.lastActivity = n.time; r.startHour = n.hour; r.startReal = n.real;
        r.startPos = n.at;
        string d = "Lundun", s = "";
        if (Places != null) Places(n.at, out d, out s);
        r.startDistrict = d; r.startStreet = s;
        r.vehicle = n.vehicle ?? "";
        r.lastCollision = -99f;
        r.missionId = attemptNow != 0 ? attemptMission ?? "" : "";
        r.attempt = attemptNow;
        r.status = attemptNow != 0 ? LundunIncidentStatus.Held : LundunIncidentStatus.Pending;
        history.Insert(0, r);
        if (Print != null) Print("opened  " + r.Line());
        Fire(Opened, r);
        return r;
    }

    LundunIncident Standalone(Now n, ref LundunIncident slot, LundunIncidentKind kind)
    {
        if (slot == null) slot = New(n, kind);
        return slot;
    }

    /// The most recent open record (for CCTV / police-told facts outside a chase).
    LundunIncident Recent()
    {
        LundunIncident best = null;
        foreach (LundunIncident r in new[] { street, crash, carjack })
            if (r != null && (best == null || r.lastActivity > best.lastActivity)) best = r;
        return best;
    }

    void Touch(LundunIncident r) { r.version++; dirty.Add(r); }
    void Touch(LundunIncident r, Now n) { r.lastActivity = n.time; Touch(r); }

    /// A standalone record still open when a chase starts becomes the chase's cause (MERGED, never reported itself).
    void Absorb(LundunIncident chase, ref LundunIncident slot, Now n)
    {
        LundunIncident r = slot;
        if (r == null) return;
        slot = null;
        chase.crimes += r.crimes; chase.kills += r.kills; chase.officersKilled += r.officersKilled; chase.policeCars += r.policeCars;
        chase.thefts += r.thefts; chase.shots += r.shots; chase.carjacks += r.carjacks; chase.crashes += r.crashes;
        chase.maxImpact = Mathf.Max(chase.maxImpact, r.maxImpact);
        chase.policeTold += r.policeTold; chase.cctvSightings += r.cctvSightings; chase.plateReads += r.plateReads;
        chase.onCctv |= r.onCctv; chase.identified |= r.identified;
        if (string.IsNullOrEmpty(chase.vehicle)) chase.vehicle = r.vehicle;
        if (string.IsNullOrEmpty(chase.causeId)) chase.causeId = r.id;     // the first cause (crash, carjack, street)
        r.mergedInto = chase.id;
        r.status = LundunIncidentStatus.Merged;
        Release(r);
        CloseRecord(r, n, LundunIncidentEnd.Ended);
        Touch(chase);
    }

    void CloseStandalone(Now n, ref LundunIncident slot)
    {
        if (slot == null) return;
        CloseRecord(slot, n, LundunIncidentEnd.Ended);
        slot = null;
    }

    void CloseRecord(LundunIncident r, Now n, LundunIncidentEnd end)
    {
        if (r.closed) return;                                          // Closed fires exactly once
        r.closed = true; r.end = end;
        r.endTime = Mathf.Max(r.lastActivity, n.time); r.endReal = n.real; r.endPos = n.at;
        string d = "Lundun", s = "";
        if (Places != null) Places(n.at, out d, out s);
        r.endDistrict = d; r.endStreet = s;
        if (!string.IsNullOrEmpty(n.vehicle)) r.vehicle = n.vehicle;
        r.version++;
        dirty.Remove(r);
        if (Print != null) Print("closed  " + r.Line());
        Fire(Closed, r);
        if (r.status == LundunIncidentStatus.Pending) Settle(r);
        else if (r.status == LundunIncidentStatus.Held && attempts.ContainsKey(r.attempt) && attempts[r.attempt] == AttemptState.Passed) Settle(r);
    }

    void Settle(LundunIncident r)
    {
        if (r.status == LundunIncidentStatus.Settled || r.status == LundunIncidentStatus.Void || r.status == LundunIncidentStatus.Merged) return;
        r.status = LundunIncidentStatus.Settled;
        r.version++;
        if (Print != null) Print("settled " + r.id);
        Fire(Settled, r);
    }

    void VoidAttempt(int attempt)
    {
        attempts[attempt] = AttemptState.Void;
        foreach (LundunIncident r in history.ToArray())
        {
            if (r.attempt != attempt || r.status == LundunIncidentStatus.Void || r.status == LundunIncidentStatus.Merged) continue;
            r.status = LundunIncidentStatus.Void;
            r.version++;
            Release(r);
            if (Print != null) Print("void    " + r.id + " (mission attempt not passed)");
            Fire(Voided, r);
        }
    }

    bool Evictable(LundunIncident r, Now n)
    {
        if (!r.closed || r.pinnedUntil > n.unscaled) return false;
        if (r.status == LundunIncidentStatus.Held || r.status == LundunIncidentStatus.Pending) return false;
        if (r.status == LundunIncidentStatus.Merged) return false;     // leaves with its chase
        return true;
    }

    void Trim(Now n)
    {
        while (history.Count > Keep)
        {
            LundunIncident victim = null;
            for (int i = history.Count - 1; i >= 0; i--) if (Evictable(history[i], n)) { victim = history[i]; break; }
            if (victim == null) return;                                // all open / HELD / pinned: grow for now
            Evict(victim);
            foreach (LundunIncident m in history.ToArray()) if (m.mergedInto == victim.id) Evict(m);   // its causes go with it
        }
    }

    void Evict(LundunIncident r)
    {
        Release(r);
        Fire(Evicted, r);
        history.Remove(r);
    }
}
