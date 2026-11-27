using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// G8.2a: the car radio. Three stations (SOUTHSIDE 2-STEP 94.7, BLOCKWORK FM 101.3, LUNDUN TALK 1152 AM) that follow a
/// deterministic clock from UTC, so switching, leaving the car, changing mode or restarting never sends a station back to
/// its start. Only the tuned station makes sound, on ONE station source. LUNDUN TALK's news reads report SETTLED
/// incidents from the G8.0 log. The live police feed (LundunPoliceFeed, on this same object) breaks into any station.
///
/// Lifecycle (exactly one object, both modes, however the game starts):
///   • SubsystemRegistration resets the statics (Enter Play Mode Options: domain reload is off in this project).
///   • BeforeSceneLoad registers ONE sceneLoaded listener (a named method: removed, then added).
///   • A supported city scene (a VehicleSystem among its roots) loading creates the root-level DontDestroyOnLoad
///     "LundunRadio" (AfterSceneLoad also checks the scenes already open), binds it at once, and rebinds it on every
///     later city load (Story ⇄ Free Roam reloads the Lundun scene). A second LundunRadio destroys itself in Awake.
///
/// RadioCanPlay (one shared check for the station, the news and the feed): in a car or van; RADIO not OFF and the tuned
/// station has content; not VehicleSystem.Locked; no WASTED / BUSTED sequence; no CCTV playback; no mission card; no
/// start menu; app in the foreground; native audio session Active. The game pause is separate (AudioListener.pause).
///
/// Placeholders (code-made beds, idents, host lines, news captions) exist only in the Editor and development builds:
/// that code is compiled out of release builds, and a missing commissioned asset never falls back to one.
public class LundunRadio : MonoBehaviour
{
    public static LundunRadio Instance { get; private set; }

    // ------------------------------------------------------------------ settings (DISPLAY tab)
    const string SettingKey = "lad_radio";                 // 0 ON, 1 STREAMER-SAFE, 2 OFF
    const string StationKey = "lad_radio_station";         // last tuned station id, or "OFF" (the pill's OFF)
    public static RadioSetting Setting
    {
        get { return (RadioSetting)Mathf.Clamp(PlayerPrefs.GetInt(SettingKey, 0), 0, 2); }
        set
        {
            PlayerPrefs.SetInt(SettingKey, (int)value); PlayerPrefs.Save();
            if (Instance != null) Instance.OnSettingChanged();
        }
    }

    [Header("Levels")]
    [SerializeField] private float stationLevel = 0.55f;      // under the engine
    [SerializeField] private float missionLevel = 0.6f;       // × while a mission runs, so calls and toasts stay clear
    [SerializeField] private float fadeTime = 0.3f;

    [Header("Read only")]
    [SerializeField] private string tuned;
    [SerializeField] private string state;
    [SerializeField] private string why;

    public static readonly RadioStationDef[] Stations =
    {
        new RadioStationDef("southside", "SOUTHSIDE 2-STEP", "94.7", "Southside", false, new Color(1f, 0.66f, 0.18f), 0.0),
        new RadioStationDef("blockwork", "BLOCKWORK FM", "101.3", "Blockwork", false, new Color(0.62f, 0.42f, 1f), 5003.0),
        new RadioStationDef("talk", "LUNDUN TALK", "1152 AM", "LundunTalk", true, new Color(0.35f, 0.8f, 1f), 9011.0),
    };

    // ------------------------------------------------------------------ static lifecycle
    public static int Subscriptions { get; private set; }     // the number of sceneLoaded registrations (always 1)
    public static int Created { get; private set; }           // radio objects created this run (logged for the tests)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        Instance = null; Subscriptions = 0; Created = 0;
        News = new RadioNewsDesk();
        LundunPoliceFeed.ResetStatics();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        simState = SessionActive; simSeq = 0;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;                      // named method: never subscribed twice
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        Subscriptions = 1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterFirstLoad()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (IsCity(s)) Ensure(s);                                    // idempotent: seen twice makes no difference
        }
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode mode) { if (IsCity(s)) Ensure(s); }

    static void OnSceneUnloaded(Scene s)
    {
        if (Instance != null && Instance.boundScene == s.handle) Instance.Unbind("city unloaded");
    }

    /// A supported city scene: one with a VehicleSystem among its roots (the Prototype and test scenes have none).
    public static bool IsCity(Scene s)
    {
        if (!s.IsValid() || !s.isLoaded) return false;
        foreach (GameObject g in s.GetRootGameObjects())
            if (g != null && g.GetComponentInChildren<VehicleSystem>(true) != null) return true;
        return false;
    }

    static void Ensure(Scene s)
    {
        if (Instance == null)
        {
            GameObject g = new GameObject("LundunRadio");                // root level (no parent)
            g.AddComponent<LundunRadio>();                                // Awake: Instance, DontDestroyOnLoad, the feed
            Created++;
        }
        if (Instance != null) Instance.Bind(s);                          // bound at once, and on every later city load
    }

    // ------------------------------------------------------------------ the native audio session (iOS), 4.1c / 4.1d
    public const int SessionActive = 0, SessionInterrupted = 1, SessionResumeRecommended = 2, SessionDoNotResume = 3;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static int simState = SessionActive, simSeq;                         // development tests only (on an iPhone the native state rules)
#endif
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void LundunAudioSession_Start(string unityObject);
    [DllImport("__Internal")] static extern int LundunAudioSession_State();
    [DllImport("__Internal")] static extern int LundunAudioSession_Sequence();
    [DllImport("__Internal")] static extern void LundunAudioSession_UserResume();
    static int NativeState() { return LundunAudioSession_State(); }
    static int NativeSequence() { return LundunAudioSession_Sequence(); }
    static void NativeStart() { LundunAudioSession_Start("LundunRadio"); }
    static void NativeUserResume() { LundunAudioSession_UserResume(); }
#else
    // Editor and other platforms: always Active, unless a development test simulates the native messages
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static int NativeState() { return simState; }
    static int NativeSequence() { return simSeq; }
    static void NativeUserResume() { if (simState == SessionDoNotResume) { simState = SessionActive; Log("session: player action, reactivated (simulated)"); } }
#else
    static int NativeState() { return SessionActive; }
    static int NativeSequence() { return 0; }
    static void NativeUserResume() { }
#endif
    static void NativeStart() { }
#endif
    int lastSeq;

    /// Native → Unity (UnitySendMessage needs a public method with one string): "sequence|event|detail".
    /// Events: began, resume, noresume, activationfailed.
    public void OnAudioSessionEvent(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        string[] p = message.Split('|');
        int seq;
        if (p.Length < 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out seq)) { Log("session: unreadable message \"" + message + "\""); return; }
        if (seq <= lastSeq) { Log("session: message " + seq + " already handled (ignored)"); return; }
        lastSeq = seq;
        string detail = p.Length > 2 ? p[2] : "";
        switch (p[1])
        {
            case "began": Log("session: inactive (" + detail + ") — stopping"); break;              // eligibility drops this frame
            case "resume": Log("session: active (confirmation, " + detail + ") — restart from clock"); break;
            case "noresume": Log("session: should not resume — waiting for player"); break;
            case "activationfailed": Log("session: reactivation failed (" + detail + "), will retry when the app becomes active"); break;
            default: Log("session: unknown event " + p[1]); break;
        }
        Evaluate();                                                       // stop or restart straight away
    }

    // ------------------------------------------------------------------ state
    public static RadioNewsDesk News = new RadioNewsDesk();               // reported incidents, per mode, this session
    int boundScene = -1;
    bool bound;
    int mode;                                                             // 0 Free Roam, 1 Story
    VehicleSystem vehicles;
    LundunPoliceFeed feed;
    RadioCatalog catalog;
    readonly RadioRunningOrder[] orders = new RadioRunningOrder[3];
    int tunedIndex;                                                       // -1 = the pill's OFF
    AudioSource src;
    readonly RadioClipBank bank = new RadioClipBank();

    bool canPlay, lastCanPlay;
    bool foreground = true;
    bool wastedOrBusted, wbLockSeen; float wbSince;
    bool wasInCar;
    bool forceRetune;
    bool wasPaused; float pauseStarted; float lastReal;

    enum St { Idle, Waiting, Playing, Quiet }
    St st = St.Idle;
    long curOrd = long.MinValue; RadioItem curItem; double itemPos;
    long waitOrd; double waitOff; float waitSince; bool waitTuneIn; string waitKase = "";
    long preOrd = long.MinValue;
    int fails;
    float fade, fadeTarget;
    float duck = 1f;
    // news (TALK)
    RadioNewsPlan plan;                                                   // chosen when the slot becomes the next item
    int newsPart = -1; bool filling;

    /// The feed sets this every frame (1 = full level, 0.2 = ducked under a dispatch).
    public float DuckLevel { get { return duck; } set { duck = Mathf.Clamp01(value); } }
    public bool CanPlay { get { return canPlay; } }
    public bool Paused { get { return AudioListener.pause; } }
    public RadioCatalog Catalog { get { return catalog; } }
    public int Mode { get { return mode; } }
    public bool StationOn { get { return tunedIndex >= 0; } }

    // ------------------------------------------------------------------ life
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }   // duplicates never register anything
        Instance = this;
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false; src.loop = false; src.spatialBlend = 0f; src.priority = 0;      // a preference, not a guarantee
        src.volume = 0f; src.dopplerLevel = 0f; src.bypassReverbZones = true;
        feed = GetComponent<LundunPoliceFeed>();
        if (feed == null) feed = gameObject.AddComponent<LundunPoliceFeed>();
        feed.Init(this);

        lastSeq = NativeSequence();                                      // a recreated radio starts from the current state
        NativeStart();                                                   // natively: dispatch_once, later calls only update the name
        lastReal = Time.realtimeSinceStartup;
        BuildUI();
        Log("created (subscriptions " + Subscriptions + ", objects created " + (Created + 1) + ", session state " + NativeState() + ")");
    }

    void OnEnable() { WastedSystem.Happened += OnWastedBusted; BustedSystem.Happened += OnWastedBusted; }
    void OnDisable() { WastedSystem.Happened -= OnWastedBusted; BustedSystem.Happened -= OnWastedBusted; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    // WastedSystem / BustedSystem fire Happened just before they lock the controls: the sequence lasts until that lock has
    // been seen and released (or 5 s if no lock ever comes).
    void OnWastedBusted() { wastedOrBusted = true; wbLockSeen = false; wbSince = Time.realtimeSinceStartup; Evaluate(); }

    void OnApplicationPause(bool paused)
    {
        if (paused) { foreground = false; Evaluate(); }
        else if (!foreground) { foreground = true; Evaluate(); }          // merged: one return, one restart
    }

    void OnApplicationFocus(bool focus) { if (focus && !foreground) { foreground = true; Evaluate(); } }

    /// A city scene loaded: drop every old scene reference, find the new ones, rebuild the running orders.
    public void Bind(Scene s)
    {
        StopStation("rebind", true);
        if (feed != null) feed.LoseEligibility("rebind");
        boundScene = s.handle; bound = true;
        LundunOccupancy.Forget();
        vehicles = null;
        foreach (GameObject g in s.GetRootGameObjects())
        {
            if (g == null) continue;
            vehicles = g.GetComponentInChildren<VehicleSystem>(true);
            if (vehicles != null) break;
        }
        mode = LundunGame.IsStory ? 1 : 0;
        wastedOrBusted = false; wbLockSeen = false; wasInCar = false; lastCanPlay = false; canPlay = false;
        RebuildOrders();
        if (feed != null) feed.Bind();
        Log("bound to scene \"" + s.name + "\" (" + (mode == 1 ? "Story" : "Free Roam") + "), stations " + AvailableList());
    }

    void Unbind(string reason)
    {
        StopStation(reason, true);
        if (feed != null) { feed.LoseEligibility(reason); feed.Unbind(); }
        bound = false; vehicles = null; boundScene = -1; canPlay = false; lastCanPlay = false;
        LundunOccupancy.Forget();
        Log("idle (" + reason + "), no scene references held");
    }

    void OnSettingChanged()
    {
        if (NativeState() == SessionDoNotResume) NativeUserResume();     // a player action
        RebuildOrders();
        forceRetune = true;
        Evaluate();
    }

    void RebuildOrders()
    {
        catalog = RadioCatalog.Load(Setting);
        for (int i = 0; i < Stations.Length; i++) orders[i] = RadioStationBuilder.Build(Stations[i], catalog);
        string saved = PlayerPrefs.GetString(StationKey, Stations[0].id);
        tunedIndex = saved == "OFF" ? -1 : Array.FindIndex(Stations, d => d.id == saved);
        if (tunedIndex >= 0 && !Available(tunedIndex)) tunedIndex = NextAvailable(tunedIndex);
        if (tunedIndex < 0 && saved != "OFF") tunedIndex = NextAvailable(-1);
        tuned = tunedIndex >= 0 ? Stations[tunedIndex].name : "OFF";
    }

    bool Available(int i) { return i >= 0 && i < orders.Length && orders[i] != null && orders[i].Count > 0; }
    public bool AnyStation { get { for (int i = 0; i < orders.Length; i++) if (Available(i)) return true; return false; } }
    int NextAvailable(int from) { for (int k = 1; k <= Stations.Length; k++) { int i = (from + k + Stations.Length) % Stations.Length; if (Available(i)) return i; } return -1; }
    string AvailableList() { var sb = new StringBuilder(); for (int i = 0; i < orders.Length; i++) if (Available(i)) sb.Append(Stations[i].name).Append(orders[i].Placeholder ? " (placeholder)" : "").Append(" v").Append(orders[i].Version).Append("; "); return sb.Length == 0 ? "none" : sb.ToString(); }

    // ------------------------------------------------------------------ the pill: next station, then OFF, then the first
    public void TapPill()
    {
        if (NativeState() == SessionDoNotResume) NativeUserResume();     // a player action clears should-not-resume
        int next = tunedIndex < 0 ? NextAvailable(-1) : NextAvailable(tunedIndex);
        if (tunedIndex >= 0 && (next <= tunedIndex)) next = -1;          // past the last station: OFF
        tunedIndex = next;
        PlayerPrefs.SetString(StationKey, tunedIndex >= 0 ? Stations[tunedIndex].id : "OFF"); PlayerPrefs.Save();
        tuned = tunedIndex >= 0 ? Stations[tunedIndex].name : "OFF";
        StopStation("switch", false);
        forceRetune = true;
        Evaluate();
    }

    // ------------------------------------------------------------------ RadioCanPlay
    void Evaluate()
    {
        RadioEligibility e = new RadioEligibility();
        string carWhy = "";
        CarController car; LundunOccupancy.Seat seat;
        e.inCarOrVan = bound && LundunOccupancy.InCarOrVan(out car, out seat, out carWhy);
        if (e.inCarOrVan && !wasInCar && NativeState() == SessionDoNotResume) NativeUserResume();   // getting in is a player action
        wasInCar = e.inCarOrVan;
        e.radioOn = Setting != RadioSetting.Off && tunedIndex >= 0;
        e.hasContent = tunedIndex >= 0 && Available(tunedIndex);
        e.locked = VehicleSystem.Locked;
        if (wastedOrBusted)
        {
            if (e.locked) wbLockSeen = true;
            else if (wbLockSeen || Time.realtimeSinceStartup - wbSince > 5f) wastedOrBusted = false;   // the sequence ends with its lock
        }
        e.wastedOrBusted = wastedOrBusted;
        e.cctv = LundunCctvFeed.Busy;
        e.missionCard = LundunMissions.CardOpen;
        e.startMenu = LundunGame.ShowStartMenu;
        e.foreground = foreground;
        e.sessionActive = NativeState() == SessionActive;
        string w;
        canPlay = e.Evaluate(out w);
        why = canPlay ? "" : (w == "not in a car or van" && carWhy != "" ? w + " (" + carWhy + ")" : w);

        if (lastCanPlay && !canPlay)
        {
            Log("can't play: " + why);
            StopStation(why, false);
            if (feed != null) feed.LoseEligibility(why);
        }
        else if (canPlay && (!lastCanPlay || forceRetune))
        {
            TuneIn();
            if (feed != null && !lastCanPlay) feed.AudibleAgain();
        }
        forceRetune = false;
        lastCanPlay = canPlay;
    }

    void Update()
    {
        float real = Time.realtimeSinceStartup;
        float gap = real - lastReal; lastReal = real;
        bool paused = AudioListener.pause;
        if (paused && !wasPaused) pauseStarted = real;
        if (!paused && wasPaused)
        {
            float d = real - pauseStarted;
            if (feed != null) feed.Unpaused(d);
            if (d > RadioTransitions.LagTolerance) ResumeAfterGap(d);
        }
        else if (!paused && gap > RadioTransitions.LagTolerance) { if (feed != null) feed.Unpaused(gap); ResumeAfterGap(gap); }   // an Editor pause or a stall
        wasPaused = paused;

        if (bound) Evaluate();
        if (canPlay && !paused) TickStation(Mathf.Min(gap, 0.25f));
        ApplyVolume(paused ? 0f : Time.unscaledDeltaTime);
        UpdateUI();
        state = st.ToString() + (curItem != null ? " · " + curItem.id + " @" + itemPos.ToString("0") + "s" : "");
    }

    // ------------------------------------------------------------------ the station
    RadioRunningOrder Order { get { return tunedIndex >= 0 ? orders[tunedIndex] : null; } }
    double ClockNow(RadioRunningOrder o) { return RadioRunningOrder.Clock(UtcSource != null ? UtcSource() : DateTime.UtcNow, o.Def.offset); }
    /// Test hook only (offline simulations): replaces the UTC clock. Never set by the game.
    public static Func<DateTime> UtcSource;

    void TuneIn()
    {
        StopStation("retune", true);
        RadioRunningOrder o = Order;
        if (o == null || o.Count == 0) { st = St.Quiet; return; }
        fails = 0;
        long ord; double off;
        o.Locate(ClockNow(o), out ord, out off);
        RadioTransitions.Decision d = RadioTransitions.TuneIn(ord, off, o.LengthOf);
        ShowPopUp(o);
        BeginWait(d.ordinal, d.offset, true, "tune-in");
    }

    void BeginWait(long ord, double off, bool tuneIn, string kase)
    {
        RadioRunningOrder o = Order;
        RadioItem it = o.ItemAt(ord);
        waitOrd = ord; waitOff = off; waitTuneIn = tuneIn; waitKase = kase;
        waitSince = Time.realtimeSinceStartup;
        if (it.kind == RadioItemKind.News && plan == null && (!tuneIn || off <= 1.0)) PlanNews(ord);
        bank.Acquire(ord, Loadable(o, it), plan != null && plan.ordinal == ord ? plan.clips : null);
        st = St.Waiting;
    }

    /// What an item needs loaded: its own clips; for a news slot, also its fill (the bed and an ident), so the fill is
    /// loaded and released under the same discipline as everything else (the report's parts come as extras).
    static RadioItem Loadable(RadioRunningOrder o, RadioItem it)
    {
        if (it.kind != RadioItemKind.News) return it;
        List<string> fill = new List<string>();
        if (o.BedPath != null) fill.Add(o.BedPath);
        if (o.IdentPath != null) fill.Add(o.IdentPath);
        RadioItem r = it.WithLength(it.length); r.clips = fill.ToArray();
        return r;
    }

    void TickStation(float dt)
    {
        RadioRunningOrder o = Order;
        if (o == null) return;
        if (st == St.Waiting) TickWaiting(o);
        else if (st == St.Playing) TickPlaying(o, dt);
    }

    void TickWaiting(RadioRunningOrder o)
    {
        RadioItem it = o.ItemAt(waitOrd);
        bool timeout = Time.realtimeSinceStartup - waitSince > RadioClipBank.Timeout;
        int ready = bank.Ready(waitOrd);                                  // 1 loaded, 0 loading, -1 failed
        if (it.kind == RadioItemKind.News && plan != null && plan.ordinal == waitOrd && ready != 1 && (ready < 0 || timeout))
        {
            Log("news: report parts not ready (" + (ready < 0 ? "failed" : "5 s") + "), the slot plays its fill");
            bank.ReleaseExtras(waitOrd); plan = null; ready = bank.Ready(waitOrd);
        }
        if (ready == 1)
        {
            // the clock check happens now, when the item is actually ready to start
            long cOrd; double cOff;
            o.Locate(ClockNow(o), out cOrd, out cOff);
            RadioTransitions.Decision d = waitTuneIn ? RadioTransitions.TuneIn(cOrd, cOff, o.LengthOf)
                                                     : RadioTransitions.Decide(curOrd, cOrd, cOff, o.LengthOf);
            if (d.ordinal == waitOrd || (!waitTuneIn && d.ordinal < waitOrd)) { StartItem(o, waitOrd, d.ordinal == waitOrd ? d.offset : 0.0, waitTuneIn ? "tune-in" : d.kase); return; }
            Log("clock moved on while loading: " + it.id + " → item " + d.ordinal + " (" + d.kase + ")");
            bank.Release(waitOrd);
            BeginWait(d.ordinal, d.offset, waitTuneIn, d.kase);
            return;
        }
        if (ready < 0 || timeout)
        {
            fails++;
            Log("skip: " + it.id + (ready < 0 ? " failed to load" : " not loaded within 5 s") + " (" + fails + " in a row)");
            bank.Release(waitOrd);
            curOrd = waitOrd;                                              // treated as passed
            if (fails >= 3) { st = St.Quiet; Log("3 failures in a row: quiet for this tune-in"); return; }
            long cOrd; double cOff;
            o.Locate(ClockNow(o), out cOrd, out cOff);
            RadioTransitions.Decision d = RadioTransitions.Decide(curOrd, cOrd, cOff, o.LengthOf);
            BeginWait(d.ordinal, d.offset, false, d.kase);
        }
    }

    void StartItem(RadioRunningOrder o, long ord, double off, string kase)
    {
        if (ord != curOrd && curOrd != long.MinValue) bank.Release(curOrd);
        curOrd = ord; curItem = o.ItemAt(ord); itemPos = off; fails = 0; preOrd = long.MinValue;
        newsPart = -1; filling = false;
        src.Stop(); src.loop = false; src.clip = null;
        if (curItem.kind == RadioItemKind.News) StartNews(o, off);
        else
        {
            AudioClip c = curItem.placeholder ? RadioPlaceholderClip(curItem) : bank.Clip(curItem.clips[0]);
            if (c == null) { EndItem(o, "no clip"); return; }
            src.clip = c; src.loop = curItem.placeholder && curItem.loopClip;
            src.time = (float)(src.loop ? off % Math.Max(0.01, c.length) : Math.Min(off, Math.Max(0.0, c.length - 0.05)));
            src.Play();
            ShowItemCaption(curItem);
        }
        fade = 0f; fadeTarget = 1f;
        st = St.Playing;
        Log(o.Def.name + " item " + ord + " '" + curItem.id + "' at ~" + off.ToString("0") + " s (" + kase + ") · " + bank.Describe());
    }

    void TickPlaying(RadioRunningOrder o, float dt)
    {
        if (curItem.kind != RadioItemKind.News && !curItem.placeholder && src.isPlaying) itemPos = src.time;
        else itemPos += dt;
        if (curItem.kind == RadioItemKind.News) TickNews(o);
        double remaining = curItem.length - itemPos;
        if (remaining <= RadioClipBank.Preload && preOrd == long.MinValue)
        {
            preOrd = curOrd + 1;
            RadioItem next = o.ItemAt(preOrd);
            if (next.kind == RadioItemKind.News) PlanNews(preOrd);
            bank.Acquire(preOrd, Loadable(o, next), plan != null && plan.ordinal == preOrd ? plan.clips : null);
        }
        if (remaining <= fadeTime) fadeTarget = 0f;                       // fade to silence…
        bool clipDone = curItem.kind != RadioItemKind.News && !curItem.placeholder && !src.isPlaying && itemPos > 0.5;
        if (remaining <= 0.0 || clipDone) EndItem(o, "end");             // …then swap and fade in (no crossfade)
    }

    void EndItem(RadioRunningOrder o, string reason)
    {
        src.Stop(); src.clip = null;
        HideCaption();
        long finished = curOrd;
        bank.Release(finished);
        long cOrd; double cOff;
        o.Locate(ClockNow(o), out cOrd, out cOff);
        RadioTransitions.Decision d = RadioTransitions.Decide(finished, cOrd, cOff, o.LengthOf);
        if (preOrd != long.MinValue && preOrd != d.ordinal) { bank.Release(preOrd); if (plan != null && plan.ordinal == preOrd) plan = null; }
        preOrd = long.MinValue;
        curItem = null;
        BeginWait(d.ordinal, d.offset, false, d.kase);
    }

    /// Stop the station source (fade-free when switching or stopping; nothing resumes the old position).
    void StopStation(string reason, bool quiet)
    {
        if (src != null) { src.Stop(); src.clip = null; src.loop = false; }
        bank.ReleaseAll();
        plan = null; curItem = null; curOrd = long.MinValue; preOrd = long.MinValue;
        st = St.Idle; fade = 0f; fadeTarget = 0f; newsPart = -1; filling = false;
        HideCaption();
        if (!quiet) Log("station stopped (" + reason + ")");
    }

    void ResumeAfterGap(float seconds)
    {
        RadioRunningOrder o = Order;
        if (o == null || st != St.Playing || curItem == null) return;
        long cOrd; double cOff;
        o.Locate(ClockNow(o), out cOrd, out cOff);
        RadioTransitions.ResumeAction a = RadioTransitions.Resume(curOrd, itemPos, cOrd, cOff);
        Log("resume after a " + seconds.ToString("0") + " s gap: " + a);
        if (a == RadioTransitions.ResumeAction.Carry) return;
        if (a == RadioTransitions.ResumeAction.Seek && curItem.kind != RadioItemKind.News)
        {
            itemPos = cOff;
            if (src.clip != null) src.time = (float)(src.loop ? cOff % Math.Max(0.01, src.clip.length) : Math.Min(cOff, src.clip.length - 0.05));
            fade = 0f; fadeTarget = 1f;
            return;
        }
        src.Stop(); HideCaption();
        long finished = curOrd;
        bank.Release(finished);
        if (preOrd != long.MinValue) { bank.Release(preOrd); preOrd = long.MinValue; }
        plan = null; curItem = null;
        RadioTransitions.Decision d = RadioTransitions.Decide(finished, cOrd, cOff, o.LengthOf);
        BeginWait(d.ordinal, d.offset, false, d.kase);
    }

    void ApplyVolume(float dt)
    {
        if (dt > 0f) fade = Mathf.MoveTowards(fade, fadeTarget, dt / Mathf.Max(0.01f, fadeTime));
        float m = LundunMissions.Running ? missionLevel : 1f;
        if (src != null) src.volume = canPlay ? stationLevel * m * duck * fade : 0f;
    }

    // ------------------------------------------------------------------ LUNDUN TALK news slots
    void PlanNews(long ord)
    {
        plan = null;
        RadioRunningOrder o = Order;
        if (o == null || !o.Def.talk) return;
        if (LundunMissions.Running) { Log("news: mission running, slot " + ord + " will be filled"); return; }
        List<LundunIncident> picks = News.Candidates(LundunIncidents.History, mode, Time.time);
        foreach (LundunIncident r in picks)
        {
            RadioNewsPlan p = RadioNewsPlan.Compose(r, ord, catalog);
            if (p != null) { plan = p; Log("news: slot " + ord + " planned for " + r.id + " (" + r.kind + ", " + r.endDistrict + "); not marked until it plays"); return; }
        }
        if (picks.Count == 0) Log("news: no eligible incident for slot " + ord);
    }

    void StartNews(RadioRunningOrder o, double off)
    {
        bool valid = plan != null && plan.ordinal == curOrd && off <= 1.0 && canPlay && !AudioListener.pause && !LundunMissions.Running
                     && News.IsEligible(plan.incident, mode, Time.time);
        if (plan != null && !valid) Log("news: the planned report is no longer valid (joined late, mission or not eligible): fill only, nothing marked");
        if (valid)
        {
            News.MarkReported(mode, plan.incident.id);                    // marked only when it actually starts playing
            Log("news: reporting " + plan.incident.id + " — " + plan.text);
            newsPart = 0;
            PlayNewsPart();
        }
        else
        {
            if (plan == null) Log("news: empty slot, nothing invented");
            plan = null;
            StartFill(o);
        }
    }

    void PlayNewsPart()
    {
        if (plan.placeholder)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            src.clip = RadioPlaceholders.Sting; src.loop = false; src.time = 0f; src.Play();
            ShowCaption("NEWS (PLACEHOLDER): " + plan.text, 7f);
#endif
            newsPart = 3;                                                 // the sting is the whole placeholder report
            return;
        }
        src.clip = bank.Clip(plan.clips[newsPart]); src.loop = false; src.time = 0f;
        if (src.clip != null) src.Play();
    }

    void TickNews(RadioRunningOrder o)
    {
        if (filling || newsPart < 0) return;
        if (src.isPlaying) return;
        if (newsPart < 2 && plan != null && !plan.placeholder) { newsPart++; PlayNewsPart(); return; }
        newsPart = -1;
        StartFill(o);
    }

    /// The rest of a news slot: the bed (looped), else one ident then the slot ends early, else the slot is skipped.
    void StartFill(RadioRunningOrder o)
    {
        filling = true;
        AudioClip bed = o.BedPath != null ? bank.Clip(o.BedPath) : null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (bed == null && o.Placeholder) bed = RadioPlaceholders.Bed(o.Def.id);
#endif
        if (bed != null) { src.clip = bed; src.loop = true; src.time = 0f; src.Play(); return; }
        AudioClip ident = o.IdentPath != null ? bank.Clip(o.IdentPath) : null;
        if (ident != null && itemPos < curItem.length - ident.length)
        {
            src.clip = ident; src.loop = false; src.time = 0f; src.Play();
            curItem = curItem.WithLength(itemPos + ident.length);         // the slot ends after the ident ("ahead": next item from its start)
            return;
        }
        Log("news slot: no bed or ident, skipped");
        curItem = curItem.WithLength(itemPos);
    }

    AudioClip RadioPlaceholderClip(RadioItem it)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (it.kind == RadioItemKind.Ident) return RadioPlaceholders.Ident;
        return RadioPlaceholders.Bed(Order.Def.id);
#else
        return null;                                                      // never reached: release builds have no placeholder items
#endif
    }

    // ------------------------------------------------------------------ the HUD: the pill (and LIVE), the pop-up, captions
    Canvas canvas; RectTransform safe, pill, popUp; TextMeshProUGUI pillText, popName, popFreq, captionText; GameObject liveTag, captionBox;
    float popUntil, captionUntil; Rect lastSafe; Vector2 lastScreen;

    void BuildUI()
    {
        GameObject cg = new GameObject("RadioUI");
        cg.transform.SetParent(transform, false);
        canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 65;                                        // over the HUD, under the incident pop-up (70) and pause (85)
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        safe = NewRect("Safe", cg.transform);
        safe.anchorMin = Vector2.zero; safe.anchorMax = Vector2.one; safe.offsetMin = safe.offsetMax = Vector2.zero;

        // the pill: under the weapon box, top right (the weapon box hides while driving)
        pill = NewRect("RadioPill", safe);
        pill.anchorMin = pill.anchorMax = new Vector2(1f, 1f); pill.pivot = new Vector2(1f, 1f);
        pill.anchoredPosition = new Vector2(-36f, -150f); pill.sizeDelta = new Vector2(330f, 72f);
        Image pi = pill.gameObject.AddComponent<Image>();
        pi.sprite = LundunHudArt.Pill; pi.type = Image.Type.Sliced; pi.pixelsPerUnitMultiplier = 0.85f; pi.color = new Color(0.03f, 0.05f, 0.09f, 0.72f);
        Button b = pill.gameObject.AddComponent<Button>(); b.targetGraphic = pi;
        b.onClick.AddListener(() => { TapPill(); LundunHaptics.Light(); });
        pill.gameObject.AddComponent<LundunPressFX>();
        pillText = NewText(pill, "", 26f, TextAlignmentOptions.MidlineLeft);
        pillText.rectTransform.anchorMin = Vector2.zero; pillText.rectTransform.anchorMax = Vector2.one;
        pillText.rectTransform.offsetMin = new Vector2(24f, 0f); pillText.rectTransform.offsetMax = new Vector2(-96f, 0f);
        RectTransform lt = NewRect("Live", pill);
        lt.anchorMin = lt.anchorMax = new Vector2(1f, 0.5f); lt.pivot = new Vector2(1f, 0.5f);
        lt.anchoredPosition = new Vector2(-16f, 0f); lt.sizeDelta = new Vector2(74f, 34f);
        Image li = lt.gameObject.AddComponent<Image>(); li.sprite = LundunHudArt.Pill; li.type = Image.Type.Sliced; li.pixelsPerUnitMultiplier = 1.6f;
        li.color = new Color(1f, 0.66f, 0.18f); li.raycastTarget = false;
        TextMeshProUGUI ltt = NewText(lt, "LIVE", 20f, TextAlignmentOptions.Center); ltt.color = new Color(0.08f, 0.05f, 0.02f);
        ltt.rectTransform.anchorMin = Vector2.zero; ltt.rectTransform.anchorMax = Vector2.one; ltt.rectTransform.offsetMin = ltt.rectTransform.offsetMax = Vector2.zero;
        liveTag = lt.gameObject; liveTag.SetActive(false);
        pill.gameObject.SetActive(false);

        // the station pop-up: top centre, about 2.5 s on tune-in
        popUp = NewRect("StationPopUp", safe);
        popUp.anchorMin = popUp.anchorMax = new Vector2(0.5f, 1f); popUp.pivot = new Vector2(0.5f, 1f);
        popUp.anchoredPosition = new Vector2(0f, -40f); popUp.sizeDelta = new Vector2(640f, 110f);
        Image pb = popUp.gameObject.AddComponent<Image>(); pb.sprite = LundunHudArt.Panel; pb.type = Image.Type.Sliced; pb.pixelsPerUnitMultiplier = 0.5f;
        pb.color = new Color(0.03f, 0.05f, 0.09f, 0.82f); pb.raycastTarget = false;
        popName = NewText(popUp, "", 40f, TextAlignmentOptions.Center);
        popName.rectTransform.anchorMin = new Vector2(0f, 0.4f); popName.rectTransform.anchorMax = Vector2.one; popName.rectTransform.offsetMin = popName.rectTransform.offsetMax = Vector2.zero;
        popFreq = NewText(popUp, "", 24f, TextAlignmentOptions.Center); popFreq.color = new Color(1f, 1f, 1f, 0.7f);
        popFreq.rectTransform.anchorMin = Vector2.zero; popFreq.rectTransform.anchorMax = new Vector2(1f, 0.45f); popFreq.rectTransform.offsetMin = popFreq.rectTransform.offsetMax = Vector2.zero;
        popUp.gameObject.SetActive(false);

        // development captions (placeholders only): under the pill
        RectTransform cb = NewRect("Caption", safe);
        cb.anchorMin = cb.anchorMax = new Vector2(1f, 1f); cb.pivot = new Vector2(1f, 1f);
        cb.anchoredPosition = new Vector2(-36f, -232f); cb.sizeDelta = new Vector2(560f, 96f);
        Image ci = cb.gameObject.AddComponent<Image>(); ci.sprite = LundunHudArt.Panel; ci.type = Image.Type.Sliced; ci.pixelsPerUnitMultiplier = 0.5f;
        ci.color = new Color(0.03f, 0.05f, 0.09f, 0.82f); ci.raycastTarget = false;
        captionText = NewText(cb, "", 21f, TextAlignmentOptions.MidlineLeft);
        captionText.textWrappingMode = TextWrappingModes.Normal;
        captionText.rectTransform.anchorMin = Vector2.zero; captionText.rectTransform.anchorMax = Vector2.one;
        captionText.rectTransform.offsetMin = new Vector2(18f, 6f); captionText.rectTransform.offsetMax = new Vector2(-18f, -6f);
        captionBox = cb.gameObject; captionBox.SetActive(false);
    }

    void UpdateUI()
    {
        if (pill == null) return;
        Rect sa = Screen.safeArea; Vector2 sz = new Vector2(Screen.width, Screen.height);
        if (sa != lastSafe || sz != lastScreen)
        {
            lastSafe = sa; lastScreen = sz;
            if (sz.x > 0f && sz.y > 0f)
            {
                safe.anchorMin = new Vector2(sa.xMin / sz.x, sa.yMin / sz.y);
                safe.anchorMax = new Vector2(sa.xMax / sz.x, sa.yMax / sz.y);
            }
        }
        CarController car; LundunOccupancy.Seat seat;
        bool inCar = bound && LundunOccupancy.InCarOrVan(out car, out seat);
        bool show = inCar && Setting != RadioSetting.Off && AnyStation && !VehicleSystem.Locked && !LundunCctvFeed.Busy && !LundunGame.ShowStartMenu;
        if (pill.gameObject.activeSelf != show) pill.gameObject.SetActive(show);
        if (show)
        {
            RadioRunningOrder o = Order;
            pillText.text = o != null ? o.Def.name : "RADIO OFF";
            pillText.color = o != null ? o.Def.colour : new Color(1f, 1f, 1f, 0.6f);
            bool live = feed != null && feed.Live && StationOn && Setting != RadioSetting.Off;
            if (liveTag.activeSelf != live) liveTag.SetActive(live);
        }
        bool pop = show && StationOn && Time.unscaledTime < popUntil;
        if (popUp.gameObject.activeSelf != pop) popUp.gameObject.SetActive(pop);
        bool cap = show && captionBox != null && Time.unscaledTime < captionUntil;
        if (captionBox != null && captionBox.activeSelf != cap) captionBox.SetActive(cap);
        if (canvas != null) canvas.enabled = bound;
    }

    void ShowPopUp(RadioRunningOrder o)
    {
        if (popName == null) return;
        popName.text = o.Def.name; popName.color = o.Def.colour;
        popFreq.text = o.Def.freq + (o.Placeholder ? "  ·  PLACEHOLDER" : "");
        popUntil = Time.unscaledTime + 2.5f;
    }

    void ShowItemCaption(RadioItem it)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (it.placeholder && !string.IsNullOrEmpty(it.caption)) ShowCaption(it.caption, 4f);
#endif
    }

    /// Development builds and the Editor only (placeholders): a line under the pill.
    public void ShowCaption(string text, float seconds)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (captionText == null) return;
        captionText.text = text; captionUntil = Time.unscaledTime + seconds;
#endif
    }

    void HideCaption() { captionUntil = 0f; }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static TextMeshProUGUI NewText(RectTransform parent, string s, float size, TextAlignmentOptions align)
    {
        RectTransform r = NewRect("Text", parent);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = s; t.fontSize = size; t.color = Color.white; t.fontStyle = FontStyles.Bold; t.characterSpacing = 4f;
        t.alignment = align; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    public static void Log(string s) { if (Debug.isDebugBuild) Debug.Log("[Radio] " + s); }

    // ------------------------------------------------------------------ development tests (Editor / development builds)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// Simulates the native messages, for the eligibility and lifecycle tests without an iPhone.
    public void SimulateSession(string evt)
    {
        if (evt == "began") simState = SessionInterrupted;
        else if (evt == "resume") simState = SessionActive;
        else if (evt == "noresume") simState = SessionDoNotResume;
        simSeq++;
        OnAudioSessionEvent(simSeq + "|" + evt + "|simulated");
    }
    [ContextMenu("Test: interruption began")] void TestBegan() { SimulateSession("began"); }
    [ContextMenu("Test: interruption resume")] void TestResume() { SimulateSession("resume"); }
    [ContextMenu("Test: should not resume")] void TestNoResume() { SimulateSession("noresume"); }
    [ContextMenu("Test: app background")] void TestBackground() { OnApplicationPause(true); }
    [ContextMenu("Test: app foreground")] void TestForeground() { OnApplicationPause(false); }
    [ContextMenu("Dump radio")]
    void Dump()
    {
        Log("subscriptions " + Subscriptions + " · objects created " + Created + " · instance " + (Instance == this) + " · bound " + bound +
            " · canPlay " + canPlay + (canPlay ? "" : " (" + why + ")") + " · session " + NativeState() + " · stations " + AvailableList());
        Log("loaded: " + bank.Describe());
    }
#endif
}

// ====================================================================== plain logic (no scene objects)

public enum RadioSetting { On = 0, StreamerSafe = 1, Off = 2 }
public enum RadioItemKind { Track, Ident, Host, News, Bed }

public sealed class RadioStationDef
{
    public readonly string id, name, freq, folder; public readonly bool talk; public readonly Color colour; public readonly double offset;
    public RadioStationDef(string id, string name, string freq, string folder, bool talk, Color colour, double offset)
    { this.id = id; this.name = name; this.freq = freq; this.folder = folder; this.talk = talk; this.colour = colour; this.offset = offset; }
}

public sealed class RadioItem
{
    public RadioItemKind kind; public string id, hash; public double length; public string[] clips; public bool placeholder, loopClip; public string caption;
    public RadioItem WithLength(double len) { RadioItem r = (RadioItem)MemberwiseClone(); r.length = Math.Max(0.0, len); return r; }
}

/// Deterministic 64-bit FNV-1a (never string.GetHashCode, which differs between runs).
public static class RadioHash
{
    public static ulong Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        if (s != null) foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }
    public static string Hex(ulong h) { return h.ToString("x16"); }
}

/// A small deterministic random sequence (xorshift64*), identical in the Editor and on iOS.
public sealed class RadioRandom
{
    ulong s;
    public RadioRandom(ulong seed) { s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }
    public ulong Next() { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 2685821657736338717UL; }
    public int Range(int n) { return n <= 1 ? 0 : (int)(Next() % (ulong)n); }
    public double Value() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }
    public void Shuffle<T>(IList<T> list) { for (int i = list.Count - 1; i > 0; i--) { int j = Range(i + 1); T t = list[i]; list[i] = list[j]; list[j] = t; } }
}

/// Resources/Radio/RadioManifest.txt, written by the Editor (LundunRadioImport): one line per clip,
/// "resource path<TAB>length in seconds<TAB>asset dependency hash", sorted by path, no timestamps.
public sealed class RadioManifest
{
    public struct Entry { public string path; public double length; public string hash; }
    public const string ResourcePath = "Radio/RadioManifest";
    public const string Header = "# Lundun After Dark radio manifest v1 (generated by the Editor: do not edit)";
    public readonly List<Entry> Entries = new List<Entry>();

    public static string Format(IEnumerable<Entry> entries)
    {
        List<Entry> list = new List<Entry>(entries);
        list.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
        StringBuilder sb = new StringBuilder(Header).Append('\n');
        foreach (Entry e in list)
            sb.Append(e.path).Append('\t').Append(e.length.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t').Append(e.hash).Append('\n');
        return sb.ToString();
    }

    public static RadioManifest Parse(string text)
    {
        RadioManifest m = new RadioManifest();
        if (string.IsNullOrEmpty(text)) return m;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            string[] p = line.Split('\t');
            double len;
            if (p.Length < 3 || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out len)) continue;
            m.Entries.Add(new Entry { path = p[0], length = len, hash = p[2] });
        }
        return m;
    }
}

/// STREAMER-SAFE: Resources/Radio/StreamerSafe.txt lists exact relative resource paths (case-sensitive, no extension).
/// Fails closed: no list, nothing plays; a LIB_ file never plays even when listed.
public static class RadioStreamerSafe
{
    public const string ResourcePath = "Radio/StreamerSafe";
    public static HashSet<string> Parse(string text)
    {
        if (text == null) return null;
        HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            set.Add(line);
        }
        return set;
    }
    public static bool IsLibrary(string path) { int s = path.LastIndexOf('/'); return path.Substring(s + 1).StartsWith("LIB_", StringComparison.Ordinal); }
    public static bool Allows(HashSet<string> list, string path) { return list != null && list.Contains(path) && !IsLibrary(path); }
}

/// The clips this setting may play, by folder.
public sealed class RadioCatalog
{
    public readonly RadioSetting setting;
    readonly Dictionary<string, List<RadioManifest.Entry>> byFolder = new Dictionary<string, List<RadioManifest.Entry>>(StringComparer.Ordinal);
    public readonly string allowedFingerprint;

    public RadioCatalog(RadioManifest m, RadioSetting setting, HashSet<string> streamerSafe)
    {
        this.setting = setting;
        StringBuilder fp = new StringBuilder(setting.ToString());
        foreach (RadioManifest.Entry e in m.Entries)
        {
            if (setting == RadioSetting.StreamerSafe && !RadioStreamerSafe.Allows(streamerSafe, e.path)) continue;
            int s = e.path.LastIndexOf('/');
            string folder = s < 0 ? "" : e.path.Substring(0, s);
            List<RadioManifest.Entry> l;
            if (!byFolder.TryGetValue(folder, out l)) byFolder[folder] = l = new List<RadioManifest.Entry>();
            l.Add(e);
        }
        allowedFingerprint = fp.ToString();
    }

    public static RadioCatalog Load(RadioSetting setting)
    {
        TextAsset mt = Resources.Load<TextAsset>(RadioManifest.ResourcePath);
        TextAsset st = Resources.Load<TextAsset>(RadioStreamerSafe.ResourcePath);
        return new RadioCatalog(RadioManifest.Parse(mt != null ? mt.text : null), setting, RadioStreamerSafe.Parse(st != null ? st.text : null));
    }

    /// Entries directly in "Radio/<folder>", optionally only those whose file name starts with prefix. Sorted by path.
    public List<RadioManifest.Entry> In(string folder, string prefix = null)
    {
        List<RadioManifest.Entry> l, r = new List<RadioManifest.Entry>();
        if (!byFolder.TryGetValue("Radio/" + folder, out l)) return r;
        foreach (RadioManifest.Entry e in l)
            if (prefix == null || e.path.Substring(e.path.LastIndexOf('/') + 1).StartsWith(prefix, StringComparison.Ordinal)) r.Add(e);
        return r;
    }

    public static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder();
        foreach (char c in s.ToLowerInvariant()) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    /// The clip in "Radio/<folder>" whose (normalised) file name is this place's name, or null.
    public string Named(string folder, string name)
    {
        string n = Norm(name);
        if (n.Length == 0) return null;
        foreach (RadioManifest.Entry e in In(folder))
            if (Norm(e.path.Substring(e.path.LastIndexOf('/') + 1)) == n) return e.path;
        return null;
    }
}

/// A station's fixed, endless running order. Cut into cycles; each cycle is the station's items shuffled with a seed made
/// from the station, its running-order version and the cycle number. Position = (UTC seconds + station offset) mod
/// the cycle length. Built once per session per setting; never reordered while it's in use.
public sealed class RadioRunningOrder
{
    public const double NewsEvery = 180.0, NewsSlot = 45.0;
    public static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public readonly RadioStationDef Def;
    public readonly string Version;
    public readonly bool Placeholder;
    public readonly string BedPath, IdentPath;                            // TALK: the fill for news slots
    readonly List<RadioItem> tracks, idents, hosts;
    readonly RadioItem news;
    public int Count { get; private set; }
    public double CycleLength { get; private set; }
    long cachedCycle = long.MinValue; List<RadioItem> cached;

    public RadioRunningOrder(RadioStationDef def, string version, List<RadioItem> tracks, List<RadioItem> idents, List<RadioItem> hosts,
                             RadioItem news, bool placeholder, string bedPath, string identPath)
    {
        Def = def; Version = version; this.tracks = tracks ?? new List<RadioItem>(); this.idents = idents ?? new List<RadioItem>();
        this.hosts = hosts ?? new List<RadioItem>(); this.news = news; Placeholder = placeholder; BedPath = bedPath; IdentPath = identPath;
        List<RadioItem> c0 = Cycle(0);
        Count = c0.Count;
        double len = 0; foreach (RadioItem it in c0) len += it.length;
        CycleLength = len;
    }

    public static double Clock(DateTime utcNow, double stationOffset) { return (utcNow - Epoch).TotalSeconds + stationOffset; }

    List<RadioItem> Cycle(long c)
    {
        if (c == cachedCycle) return cached;
        RadioRandom rnd = new RadioRandom(RadioHash.Fnv(Def.id + "|" + Version + "|" + c.ToString(CultureInfo.InvariantCulture)));
        List<RadioItem> list = new List<RadioItem>();
        if (!Def.talk)
        {
            List<RadioItem> t = new List<RadioItem>(tracks), id = new List<RadioItem>(idents);
            rnd.Shuffle(t); rnd.Shuffle(id);
            int k = 0;
            for (int i = 0; i < t.Count; i++)
            {
                list.Add(t[i]);
                if (i % 2 == 1 && id.Count > 0) list.Add(id[k++ % id.Count]);          // an ident after every second track
            }
        }
        else
        {
            List<RadioItem> h = new List<RadioItem>(hosts), id = new List<RadioItem>(idents);
            rnd.Shuffle(h); rnd.Shuffle(id);
            double since = 0; int k = 0;
            for (int i = 0; i < h.Count; i++)
            {
                list.Add(h[i]); since += h[i].length;
                if (news != null && since >= NewsEvery) { list.Add(news); since = 0; }
                else if (i % 3 == 2 && id.Count > 0) list.Add(id[k++ % id.Count]);
            }
            if (news != null && list.IndexOf(news) < 0 && h.Count > 0) list.Add(news);   // at least one slot per cycle
        }
        cachedCycle = c; cached = list;
        return list;
    }

    /// The item playing at station time t, as an ordinal (cycle × items per cycle + index) and an offset into it.
    public void Locate(double t, out long ordinal, out double offset)
    {
        ordinal = 0; offset = 0;
        if (Count == 0 || CycleLength <= 0) return;
        long c = (long)Math.Floor(t / CycleLength);
        double within = t - c * CycleLength;
        List<RadioItem> list = Cycle(c);
        for (int i = 0; i < list.Count; i++)
        {
            if (within < list[i].length || i == list.Count - 1) { ordinal = c * Count + i; offset = Math.Min(within, list[i].length); return; }
            within -= list[i].length;
        }
    }

    public RadioItem ItemAt(long ordinal)
    {
        long c = ordinal >= 0 ? ordinal / Count : -((-ordinal + Count - 1) / Count);
        int i = (int)(ordinal - c * Count);
        return Cycle(c)[i];
    }

    public double LengthOf(long ordinal) { return ItemAt(ordinal).length; }
}

/// Builds each station's running order from the catalogue (commissioned items, resolved one by one) and, in the Editor
/// and development builds only, code-made placeholders for whatever hasn't been commissioned.
public static class RadioStationBuilder
{
    public const double PlaceholderTrack = 75, PlaceholderIdent = 5, PlaceholderHost = 60;

    public static RadioRunningOrder Build(RadioStationDef def, RadioCatalog cat)
    {
        List<RadioItem> tracks = Items(cat, def.folder + "/Tracks", RadioItemKind.Track);
        List<RadioItem> idents = Items(cat, def.folder + "/Idents", RadioItemKind.Ident);
        List<RadioItem> hosts = def.talk ? Items(cat, def.folder + "/Host", RadioItemKind.Host) : new List<RadioItem>();
        List<RadioManifest.Entry> beds = def.talk ? cat.In(def.folder + "/Beds") : new List<RadioManifest.Entry>();
        bool newsRecorded = def.talk && cat.In(def.folder + "/News", "TPL_").Count > 0 && cat.In(def.folder + "/News", "SIGN_").Count > 0 && cat.In(def.folder + "/Places").Count > 0;
        bool placeholder = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // development only: stand-ins for anything not commissioned yet (never in a release build, never in STREAMER-SAFE,
        // which plays only approved files)
        bool ph = cat.setting == RadioSetting.On;
        if (ph && !def.talk && tracks.Count == 0) { for (int i = 0; i < 5; i++) tracks.Add(Ph(RadioItemKind.Track, def.id + "_placeholder_" + (i + 1), PlaceholderTrack, true, def.name + " · PLACEHOLDER TRACK " + (i + 1))); placeholder = true; }
        if (ph && idents.Count == 0) { for (int i = 0; i < 3; i++) idents.Add(Ph(RadioItemKind.Ident, def.id + "_ident_placeholder_" + (i + 1), PlaceholderIdent, false, def.name + " · PLACEHOLDER IDENT")); placeholder = true; }
        if (ph && def.talk && hosts.Count == 0) { for (int i = 0; i < 4; i++) hosts.Add(Ph(RadioItemKind.Host, "talk_host_placeholder_" + (i + 1), PlaceholderHost, true, "LUNDUN TALK · HOST SEGMENT (PLACEHOLDER)")); placeholder = true; }
        if (ph && def.talk && !newsRecorded) placeholder = true;
        bool newsPossible = def.talk && (newsRecorded || ph);               // recorded, or placeholder captions (ON only)
#else
        bool newsPossible = newsRecorded;                                   // release: only with commissioned news
#endif
        // music: a station with no tracks is hidden; TALK: needs host segments
        if ((!def.talk && tracks.Count == 0) || (def.talk && hosts.Count == 0))
            return new RadioRunningOrder(def, "empty", new List<RadioItem>(), null, null, null, false, null, null);
        RadioItem news = newsPossible ? new RadioItem { kind = RadioItemKind.News, id = "news_slot", length = RadioRunningOrder.NewsSlot, clips = new string[0] } : null;
        StringBuilder v = new StringBuilder(cat.setting.ToString());
        foreach (List<RadioItem> l in new[] { tracks, idents, hosts })
            foreach (RadioItem it in l) v.Append('|').Append(it.id).Append(':').Append(it.placeholder ? "ph" : it.hash);
        foreach (RadioManifest.Entry e in cat.In(def.folder + "/News")) v.Append('|').Append(e.path).Append(':').Append(e.hash);
        foreach (RadioManifest.Entry e in cat.In(def.folder + "/Places")) v.Append('|').Append(e.path).Append(':').Append(e.hash);
        foreach (RadioManifest.Entry e in beds) v.Append('|').Append(e.path).Append(':').Append(e.hash);
        string version = RadioHash.Hex(RadioHash.Fnv(v.ToString())).Substring(0, 8);
        return new RadioRunningOrder(def, version, tracks, idents, hosts, news, placeholder,
                                     beds.Count > 0 ? beds[0].path : null, idents.Count > 0 && !idents[0].placeholder ? idents[0].clips[0] : null);
    }

    static List<RadioItem> Items(RadioCatalog cat, string folder, RadioItemKind kind)
    {
        List<RadioItem> l = new List<RadioItem>();
        foreach (RadioManifest.Entry e in cat.In(folder))
            if (e.length > 0.5) l.Add(new RadioItem { kind = kind, id = e.path.Substring(e.path.LastIndexOf('/') + 1), length = e.length, clips = new[] { e.path }, hash = e.hash });
        return l;
    }

    static RadioItem Ph(RadioItemKind k, string id, double len, bool loop, string caption)
    {
        return new RadioItem { kind = k, id = id, length = len, clips = new string[0], placeholder = true, loopClip = loop, caption = caption };
    }
}

/// The exact transition rules (proposal 4.1b). Ordinals are positions in one running-order version's endless sequence.
public static class RadioTransitions
{
    public const double LagTolerance = 10.0, EndMargin = 3.0;
    public struct Decision { public long ordinal; public double offset; public string kase; }
    public enum ResumeAction { Carry, Seek, Move }

    /// Tune-in: the clock's item at its offset, but never a sliver within 3 s of an end.
    public static Decision TuneIn(long clockOrd, double clockOff, Func<long, double> lengthOf)
    {
        Decision d = new Decision { ordinal = clockOrd, offset = clockOff, kase = "tune-in" };
        return Margin(d, lengthOf);
    }

    /// At a transition: current = the item just finished.
    public static Decision Decide(long current, long clockOrd, double clockOff, Func<long, double> lengthOf)
    {
        long next = current + 1;
        Decision d;
        if (clockOrd == next) d = new Decision { ordinal = next, offset = clockOff <= LagTolerance ? 0.0 : clockOff, kase = "next" };
        else if (clockOrd == current) d = new Decision { ordinal = next, offset = 0.0, kase = "ahead" };
        else if (clockOrd > next) d = new Decision { ordinal = clockOrd, offset = clockOff, kase = "beyond" };
        else d = new Decision { ordinal = next, offset = 0.0, kase = "behind" };
        return Margin(d, lengthOf);
    }

    static Decision Margin(Decision d, Func<long, double> lengthOf)
    {
        int guard = 0;
        while (d.offset > 0 && d.offset > lengthOf(d.ordinal) - EndMargin && guard++ < 64) { d.ordinal++; d.offset = 0; d.kase += "+end"; }
        return d;
    }

    /// After a pause or stall of more than 10 s, with an item playing at position.
    public static ResumeAction Resume(long current, double position, long clockOrd, double clockOff)
    {
        if (clockOrd == current) return clockOff <= position + LagTolerance ? ResumeAction.Carry : ResumeAction.Seek;
        if (clockOrd > current) return ResumeAction.Move;
        return ResumeAction.Carry;                                           // the clock is behind: never seek backwards
    }
}

/// LUNDUN TALK's news desk: which SETTLED incidents may be reported. Reported ids are kept in memory per mode
/// (once per session per mode); a missed slot never marks anything.
public sealed class RadioNewsDesk
{
    public const float Window = 900f;                                      // 15 minutes of game time from the incident's close
    readonly HashSet<string>[] reported = { new HashSet<string>(), new HashSet<string>() };

    public static bool Reportable(LundunIncidentKind k)
    { return k == LundunIncidentKind.Chase || k == LundunIncidentKind.Crash || k == LundunIncidentKind.Wasted || k == LundunIncidentKind.Busted; }

    public bool IsEligible(LundunIncident r, int mode, float now)
    {
        return r != null && LundunIncidentLog.Eligible(r) && r.mode == mode && Reportable(r.kind)
               && now - r.endTime <= Window && now >= r.endTime && !reported[Mathf.Clamp(mode, 0, 1)].Contains(r.id);
    }

    /// Eligible incidents, newest first.
    public List<LundunIncident> Candidates(IEnumerable<LundunIncident> history, int mode, float now)
    {
        List<LundunIncident> l = new List<LundunIncident>();
        if (history != null) foreach (LundunIncident r in history) if (IsEligible(r, mode, now)) l.Add(r);
        l.Sort((a, b) => b.endTime.CompareTo(a.endTime));
        return l;
    }

    public void MarkReported(int mode, string id) { reported[Mathf.Clamp(mode, 0, 1)].Add(id); }
    public bool Reported(int mode, string id) { return reported[Mathf.Clamp(mode, 0, 1)].Contains(id); }
}

/// One planned news report: a template for the incident's kind, the place, and a sign-off (recorded), or, in the Editor
/// and development builds only, a placeholder caption. Never invents an incident or a detail.
public sealed class RadioNewsPlan
{
    public LundunIncident incident; public long ordinal; public string[] clips; public string text; public bool placeholder;

    static string Kind(LundunIncidentKind k)
    {
        switch (k) { case LundunIncidentKind.Chase: return "CHASE"; case LundunIncidentKind.Crash: return "CRASH"; case LundunIncidentKind.Wasted: return "WASTED"; default: return "BUSTED"; }
    }

    public static string Text(LundunIncident r)
    {
        string where = string.IsNullOrEmpty(r.endDistrict) ? "Lundun" : r.endDistrict;
        switch (r.kind)
        {
            case LundunIncidentKind.Chase: return "Police are appealing for witnesses after a chase through " + where + ".";
            case LundunIncidentKind.Crash: return "Reports of a serious collision in " + where + (string.IsNullOrEmpty(r.endStreet) ? "" : ", on " + r.endStreet) + ".";
            case LundunIncidentKind.Wasted: return "Emergency services were called to an incident in " + where + ".";
            default: return "A man has been arrested in " + where + " following a police pursuit.";
        }
    }

    public static RadioNewsPlan Compose(LundunIncident r, long ordinal, RadioCatalog cat)
    {
        List<RadioManifest.Entry> tpl = cat.In("LundunTalk/News", "TPL_" + Kind(r.kind) + "_");
        List<RadioManifest.Entry> sign = cat.In("LundunTalk/News", "SIGN_");
        string place = cat.Named("LundunTalk/Places", r.endStreet) ?? cat.Named("LundunTalk/Places", r.endDistrict);
        if (tpl.Count > 0 && sign.Count > 0 && place != null)
        {
            RadioRandom rnd = new RadioRandom(RadioHash.Fnv(r.id));
            return new RadioNewsPlan { incident = r, ordinal = ordinal, clips = new[] { tpl[rnd.Range(tpl.Count)].path, place, sign[rnd.Range(sign.Count)].path }, text = Text(r) };
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool anyRecorded = cat.In("LundunTalk/News").Count > 0 && cat.In("LundunTalk/Places").Count > 0;
        if (!anyRecorded && cat.setting == RadioSetting.On) return new RadioNewsPlan { incident = r, ordinal = ordinal, clips = new string[0], text = Text(r), placeholder = true };
#endif
        return null;                                                          // a missing part: this report can't be composed
    }
}

/// The ONE shared eligibility check (RadioCanPlay). The game pause is not part of it (AudioListener.pause).
public struct RadioEligibility
{
    public bool inCarOrVan, radioOn, hasContent, locked, wastedOrBusted, cctv, missionCard, startMenu, foreground, sessionActive;

    public bool Evaluate(out string why)
    {
        why = "";
        if (!inCarOrVan) { why = "not in a car or van"; return false; }
        if (!radioOn) { why = "radio off"; return false; }
        if (!hasContent) { why = "no playable content"; return false; }
        if (locked) { why = "vehicle locked"; return false; }
        if (wastedOrBusted) { why = "wasted or busted"; return false; }
        if (cctv) { why = "cctv playback"; return false; }
        if (missionCard) { why = "mission card"; return false; }
        if (startMenu) { why = "start menu"; return false; }
        if (!foreground) { why = "app in the background"; return false; }
        if (!sessionActive) { why = "audio session not active"; return false; }
        return true;
    }
}

/// Loads only what's needed (LoadAudioData), waits for loadState Loaded, releases with UnloadAudioData. Reference-counted
/// by path, so a clip shared by two loaded items isn't released early. Keyed by the item's ordinal.
public sealed class RadioClipBank
{
    public const float Timeout = 5f, Preload = 10f;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
    readonly Dictionary<string, int> refs = new Dictionary<string, int>(StringComparer.Ordinal);
    readonly Dictionary<long, List<string>> held = new Dictionary<long, List<string>>();
    readonly Dictionary<long, int> extrasFrom = new Dictionary<long, int>();

    public AudioClip Clip(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        AudioClip c;
        if (!clips.TryGetValue(path, out c) || c == null) { c = Resources.Load<AudioClip>(path); clips[path] = c; }
        return c;
    }

    public void Acquire(long ord, RadioItem it, string[] extras)
    {
        if (held.ContainsKey(ord)) return;
        List<string> l = new List<string>();
        if (it.clips != null) l.AddRange(it.clips);
        extrasFrom[ord] = l.Count;
        if (extras != null) l.AddRange(extras);
        foreach (string p in l)
        {
            int n; refs.TryGetValue(p, out n); refs[p] = n + 1;
            AudioClip c = Clip(p);
            if (c != null && n == 0 && c.loadState != AudioDataLoadState.Loaded && c.loadState != AudioDataLoadState.Loading) c.LoadAudioData();
        }
        held[ord] = l;
    }

    /// 1 all loaded, 0 still loading, -1 a clip failed or is missing.
    public int Ready(long ord)
    {
        List<string> l;
        if (!held.TryGetValue(ord, out l)) return 1;
        int r = 1;
        foreach (string p in l)
        {
            AudioClip c = Clip(p);
            if (c == null || c.loadState == AudioDataLoadState.Failed) return -1;
            if (c.loadState != AudioDataLoadState.Loaded) r = 0;
        }
        return r;
    }

    public void Release(long ord)
    {
        List<string> l;
        if (!held.TryGetValue(ord, out l)) return;
        held.Remove(ord); extrasFrom.Remove(ord);
        foreach (string p in l) Drop(p);
    }

    /// Releases a news report's parts but keeps the slot's own clips.
    public void ReleaseExtras(long ord)
    {
        List<string> l; int from;
        if (!held.TryGetValue(ord, out l) || !extrasFrom.TryGetValue(ord, out from)) return;
        for (int i = l.Count - 1; i >= from; i--) { Drop(l[i]); l.RemoveAt(i); }
    }

    void Drop(string p)
    {
        int n; refs.TryGetValue(p, out n);
        if (n <= 1)
        {
            refs.Remove(p);
            AudioClip c; if (clips.TryGetValue(p, out c) && c != null && c.loadState == AudioDataLoadState.Loaded) c.UnloadAudioData();
        }
        else refs[p] = n - 1;
    }

    public void ReleaseAll() { foreach (long k in new List<long>(held.Keys)) Release(k); }

    public string Describe()
    {
        StringBuilder sb = new StringBuilder("loaded ");
        int n = 0; long bytes = 0;
        foreach (KeyValuePair<string, int> kv in refs)
        {
            AudioClip c; clips.TryGetValue(kv.Key, out c);
            n++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (c != null) bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(c);
#endif
        }
        sb.Append(n).Append(" clip(s), ").Append((bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(" MB compressed clip data");
        return sb.ToString();
    }

    public int LoadedCount { get { return refs.Count; } }
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// Development only: code-made stand-ins (beds per station style, an ident sweep, a news sting). Compiled out of release
/// builds, never stored as files.
public static class RadioPlaceholders
{
    const int Rate = 22050;
    static readonly Dictionary<string, AudioClip> beds = new Dictionary<string, AudioClip>();
    static AudioClip ident, sting;

    public static AudioClip Bed(string station)
    {
        AudioClip c;
        if (beds.TryGetValue(station, out c) && c != null) return c;
        float bpm = station == "southside" ? 132f : station == "blockwork" ? 140f : 90f;
        float beat = 60f / bpm; int bars = 2; int n = Mathf.RoundToInt(beat * 4 * bars * Rate);
        float[] d = new float[n];
        System.Random r = new System.Random(7);
        int[] bass = station == "blockwork" ? new[] { 0, 0, 3, -2 } : new[] { 0, 5, 3, 7 };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate; float bt = t / beat; int b = (int)bt; float fb = bt - b;
            float s = 0f;
            if (station == "talk")
            {
                float f = 110f * Mathf.Pow(2f, (b / 4 % 2 == 0 ? 0 : 3) / 12f);
                s = 0.06f * (Mathf.Sin(2 * Mathf.PI * f * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * f * 1.5f * t));
            }
            else
            {
                bool kick = station == "blockwork" ? (b % 4 == 0 || b % 8 == 6) : (b % 4 == 0 || b % 4 == 2 && fb > 0.5f);
                if (kick && fb < 0.25f) s += 0.5f * Mathf.Sin(2 * Mathf.PI * (60f + 90f * (0.25f - fb)) * t) * (1f - fb * 4f);
                float hf = (bt * 2f) % 1f;
                if (hf < 0.08f) s += 0.08f * (float)(r.NextDouble() * 2 - 1) * (1f - hf / 0.08f);
                if (b % 4 == 2 && fb < 0.12f && station == "southside") s += 0.2f * (float)(r.NextDouble() * 2 - 1) * (1f - fb / 0.12f);
                float bf = 55f * Mathf.Pow(2f, bass[(b / 2) % 4] / 12f);
                s += 0.14f * Mathf.Sin(2 * Mathf.PI * bf * t) * (fb < 0.7f ? 1f : 0.3f);
            }
            d[i] = Mathf.Clamp(s, -1f, 1f);
        }
        c = AudioClip.Create("radio_placeholder_" + station, n, 1, Rate, false);
        c.SetData(d, 0);
        beds[station] = c;
        return c;
    }

    public static AudioClip Ident
    {
        get
        {
            if (ident != null) return ident;
            int n = Rate * 2; float[] d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)Rate; float f = 400f + 900f * Mathf.Clamp01(t / 0.6f); d[i] = t < 1.2f ? 0.18f * Mathf.Sin(2 * Mathf.PI * f * t) * (1f - t / 1.2f) : 0f; }
            ident = AudioClip.Create("radio_placeholder_ident", n, 1, Rate, false); ident.SetData(d, 0);
            return ident;
        }
    }

    public static AudioClip Sting
    {
        get
        {
            if (sting != null) return sting;
            int n = (int)(Rate * 1.4f); float[] d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)Rate; float f = t < 0.35f ? 660f : 880f; d[i] = 0.16f * Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Clamp01(1.4f - t); }
            sting = AudioClip.Create("radio_placeholder_sting", n, 1, Rate, false); sting.SetData(d, 0);
            return sting;
        }
    }
}
#endif
