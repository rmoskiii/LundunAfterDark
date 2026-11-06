using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// S1: runs the story's missions (Story mode only; added by LundunStoryUI).
///   start markers  a yellow glow where each available mission starts, with its giver's letter on the minimap
///                  (K Kemi, M Marcus, T Tayo, F Uncle Femi, L Leah). Walk (or pull up) into one, not wanted, and the
///                  mission card opens: title, giver, a line about it, the pay, the medals, EASY / NORMAL / HARD, START
///   during it      the objective line at the top, what people say at the bottom, a yellow marker (and the GPS route) for
///                  where to go, the city's own gangs, rough sleepers, bus queues, cyclists and patrols kept off its set
///                  pieces (LundunMissionZones). The pause menu's switch button becomes QUIT MISSION
///   failing        WASTED, BUSTED or the mission's own reason: MISSION FAILED, why, RETRY (from the last checkpoint the
///                  difficulty allows; free) or QUIT
///   passing        MISSION PASSED: the pay (× difficulty), the medal, CONTINUE; saved at once
public class LundunMissions : MonoBehaviour
{
    public static LundunMissions Instance { get; private set; }
    public static bool Running { get { return Instance != null && Instance.run != null; } }
    /// Guidance: the step on now (null between missions).
    public static LundunStep CurrentStep { get { return Instance != null && Instance.run != null && Instance.run.failed == null ? Instance.run.Current : null; } }
    private static bool skip;
    /// Guidance: how long a line stays up: an easy reading pace, at least 4 seconds.
    public static float ReadTime(string text)
    {
        int words = string.IsNullOrEmpty(text) ? 1 : text.Split(' ').Length;
        return Mathf.Clamp(1.4f + words * 0.36f, 4f, 10f);
    }
    /// Guidance: the subtitle was tapped (once).
    public static bool TakeSkip() { bool s = skip; skip = false; return s; }

    static readonly Color Ink = new Color(0.05f, 0.07f, 0.12f, 0.94f);
    static readonly Color Accent = new Color(1f, 0.66f, 0.18f);
    static readonly Color Dim = new Color(0.62f, 0.66f, 0.74f);
    static readonly Color MarkerYellow = new Color(1f, 0.8f, 0.15f);

    // who's who: their letter and colour
    static readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>
    {
        { "JAMES", Color.white }, { "MARCUS", new Color(0.95f, 0.35f, 0.3f) }, { "TAYO", new Color(0.4f, 0.9f, 0.45f) },
        { "KEMI", new Color(0.78f, 0.5f, 1f) }, { "UNCLE FEMI", new Color(1f, 0.6f, 0.2f) }, { "LEAH", new Color(0.35f, 0.85f, 0.9f) },
        { "HOLLIS", new Color(0.75f, 0.78f, 0.82f) },
        { "KINGSLEY", new Color(0.85f, 0.2f, 0.25f) }, { "TOWER BLOCK KING", new Color(0.8f, 0.35f, 0.3f) }, { "RYE LANE RIDER", new Color(0.95f, 0.5f, 0.2f) },
        { "ELECTRIC YOUTH", new Color(0.95f, 0.82f, 0.1f) },                                                    // S5
    };

    private LundunMissionRun run;
    private int checkpointAt;
    private Vector3 checkpointPos; private Quaternion checkpointRot;
    private CarController checkpointCar; private bool checkpointInCar; private Vector3 carPos; private Quaternion carRot;
    private LundunDifficulty difficulty = LundunDifficulty.Normal;
    private readonly Dictionary<LundunMission, Transform> starts = new Dictionary<LundunMission, Transform>();
    private LundunMission declined;                    // NOT NOW: the card stays shut until you walk away
    private Transform objectiveMarker;
    private bool routeSet;
    private readonly List<GameObject> leftovers = new List<GameObject>();     // people a passed mission leaves in the street

    // UI
    private GameObject canvasGo, card, failPanel, passPanel;
    private TextMeshProUGUI objectiveText, subWho, subText, failWhy, passMoney, passMedal;
    private RectTransform objectiveBox, subBox;
    private float subUntil;
    private readonly List<Image> diffButtons = new List<Image>();
    private bool failShown;
    private float timeScaleBefore = 1f;
    private static Material markerMat, parcelMat;

    void Awake() { Instance = this; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        LundunMissionZones.Clear();
    }

    void OnEnable() { WastedSystem.Happened += OnWasted; BustedSystem.Happened += OnBusted; }
    void OnDisable() { WastedSystem.Happened -= OnWasted; BustedSystem.Happened -= OnBusted; }

    IEnumerator Start()
    {
        yield return null;                                       // the HUD and minimap first
        yield return null;
        BuildUI();
        LundunTrust.Recalc(false);                               // S5: trust from the choices made (and the defaults)
        foreach (LundunMission m in LundunMissionBook.All) MakeStart(m);
    }

    // ------------------------------------------------------------------ for steps and other systems
    /// A line of dialogue at the bottom of the screen (who = null hides it).
    public static void Say(string who, string text, float seconds)
    {
        LundunMissions m = Instance;
        if (m == null || m.subBox == null) return;
        if (who == null) { m.subBox.gameObject.SetActive(false); return; }
        Color c; if (!Colours.TryGetValue(who, out c)) c = Accent;
        m.subWho.text = who; m.subWho.color = c;
        m.subText.text = text;
        m.subBox.gameObject.SetActive(true);
        m.subUntil = Time.time + seconds + 0.3f;
    }

    /// S4: is any start marker on show at a place 'test' accepts (LAGOS FOODS opens for a mission that starts inside it)?
    public static bool StartWhere(System.Func<Vector3, bool> test)
    {
        if (Instance == null || Running) return false;
        foreach (var kv in Instance.starts) if (kv.Value != null && kv.Value.gameObject.activeSelf && test(kv.Value.position)) return true;
        return false;
    }

    /// S2: someone's colour (their name on screen and their name tag).
    public static Color ColourOf(string who) { Color c; return who != null && Colours.TryGetValue(who, out c) ? c : Accent; }
    /// S2: a line is on screen right now (the crew don't talk over it).
    public static bool Talking { get { return Instance != null && Instance.subBox != null && Instance.subBox.gameObject.activeSelf; } }

    /// S4: fail the mission now for 'why' (Marcus went down and nobody got to him). Applied on the next frame.
    public static void FailNow(string why) { if (Instance != null && Instance.run != null && Instance.pendingFail == null) Instance.pendingFail = why; }
    private string pendingFail;

    public static void Toast(string s) { if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(s); }

    /// Enemies show as red dots on the minimap while they're a threat.
    public static void Mark(List<Health> people, bool on) { foreach (Health h in people) Mark(h, on); }
    public static void Mark(Health h, bool on)
    {
        LundunMinimap mm = FindAnyObjectByType<LundunMinimap>();
        if (mm == null || h == null) return;
        if (on) mm.Track(h.transform, new Color(1f, 0.25f, 0.2f), "", 12f, false); else mm.Untrack(h.transform);
    }

    public static Material ParcelMaterial
    {
        get
        {
            if (parcelMat == null)
            {
                parcelMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MissionParcel" };
                Color c = new Color(0.62f, 0.45f, 0.25f);
                parcelMat.SetColor("_BaseColor", c); parcelMat.color = c;
                parcelMat.EnableKeyword("_EMISSION"); parcelMat.SetColor("_EmissionColor", new Color(0.5f, 0.38f, 0.1f));
            }
            return parcelMat;
        }
    }

    // ------------------------------------------------------------------ start markers and the card
    void MakeStart(LundunMission m)
    {
        if (!m.Available(LundunStory.Data) && !replaying.Contains(m.Id)) return;
        Transform t = Glow("Start_" + m.Id, LundunMissionRun.Ground(m.StartAt), 1.3f, 2.4f);
        starts[m] = t;
        LundunMinimap mm = FindAnyObjectByType<LundunMinimap>();
        Color c; if (!Colours.TryGetValue(m.Giver, out c)) c = MarkerYellow;
        if (mm != null) mm.Track(t, c, m.Giver.Substring(0, 1) == "U" ? "F" : m.Giver.Substring(0, 1), 26f, false);
        GiverWaits(m, t);
    }

    /// S2: the mission's giver stands just beside its start marker, facing the way it faces (out from the door).
    static void GiverWaits(LundunMission m, Transform marker)
    {
        if (marker == null || !LundunCast.Has(m.Giver)) return;
        // S8: one person can't wait in two places: a story mission's giver waits there, not at their side job
        if (!m.Story && Instance != null)
            foreach (var kv in Instance.starts) if (kv.Key != m && kv.Key.Story && kv.Key.Giver == m.Giver && kv.Value != null) return;
        Vector3 f = m.GiverFacing; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, f);
        LundunCrew.Wait(m.Giver, marker.position + side * 1.6f + f * 0.4f, Quaternion.LookRotation(f).eulerAngles.y);
    }

    void ShowStarts(bool on)
    {
        foreach (var kv in starts) if (kv.Value != null) kv.Value.gameObject.SetActive(on);
        if (!on) LundunCrew.ClearWaits();                                       // S2: givers not taken along wander off
        else foreach (var kv in starts) GiverWaits(kv.Key, kv.Value);
    }

    void Update()
    {
        if (subBox != null && subBox.gameObject.activeSelf && Time.time > subUntil) subBox.gameObject.SetActive(false);
        for (int i = leftovers.Count - 1; i >= 0; i--)
        {
            GameObject g = leftovers[i];
            Camera cam = Camera.main;
            if (g == null) { leftovers.RemoveAt(i); continue; }
            if (cam != null && !LundunView.CanSee(cam, g.transform.position) && (g.transform.position - cam.transform.position).sqrMagnitude > 60f * 60f)
            { Destroy(g); leftovers.RemoveAt(i); }
        }

        if (run == null) { LundunCrew.HangBack = false; pendingFail = null; WatchStarts(); return; }
        if (pendingFail != null && run.failed == null) { run.Fail(pendingFail); pendingFail = null; EndAttempt(); return; }
        if (run.failed != null) { if (!failShown && !VehicleSystem.Locked && !Dead()) ShowFail(); return; }

        WantedSystem w = WantedSystem.Instance;
        if (w != null && w.Stars > 0) run.wanted = true;

        LundunStep s = run.Current;
        if (s == null) return;
        LundunCrew.HangBack = s is LundunFight || (s is LundunIf && ((LundunIf)s).Inner is LundunFight);     // S5: a fight that might not happen
        bool done = s.Tick(run);
        if (run.failed != null) { EndAttempt(); return; }
        Objective(s.objective);
        if (done) Next();
    }

    static bool Dead() { return PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead; }

    void WatchStarts()
    {
        if (card != null && card.activeSelf) return;
        Transform j = LundunMissionRun.James();
        if (j == null || VehicleSystem.Locked || Dead()) return;
        WantedSystem w = WantedSystem.Instance;
        bool wanted = w != null && w.Stars > 0;
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        foreach (var kv in starts)
        {
            if (kv.Value == null) continue;
            float d = LundunMissionRun.FlatDistance(j.position, kv.Value.position);
            if (declined == kv.Key) { if (d > 7f) declined = null; continue; }
            if (d > 3f || wanted) continue;
            if (vs != null && vs.IsDriving && vs.PlayerSpeed > 2.5f) continue;     // pull up and stop
            OpenCard(kv.Key);
            return;
        }
    }

    void OpenCard(LundunMission m)
    {
        BuildCard(m);
        timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        card.SetActive(true);
        MarkDifficulty();
    }

    void CloseCard() { if (card != null) Destroy(card); card = null; Time.timeScale = timeScaleBefore; }

    // ------------------------------------------------------------------ running
    void Begin(LundunMission m)
    {
        CloseCard();
        LundunHaptics.Light();
        run = new LundunMissionRun(m, difficulty);
        m.Build(run);
        checkpointAt = 0;
        SaveCheckpointPose();
        StartAt(0);
        LundunStory.Data.currentMission = m.Id;
        LundunStory.MarkDirty();
    }

    /// Start (or restart) the attempt at step 'from'.
    void StartAt(int from)
    {
        run.startedAt = Time.time;
        LundunMissionZones.Clear();
        foreach (var z in run.zones) LundunMissionZones.Add(z.Key, z.Value);
        ShowStarts(false);
        objectiveBox.gameObject.SetActive(true);
        // S2: who should be with James by this point (starting again from a checkpoint after they joined)
        for (int i = 0; i < from && i < run.steps.Count; i++)
        {
            LundunSetup setup = run.steps[i] as LundunSetup;                    // S3: every set-up step (people, the shop, cars)
            if (setup != null) setup.Apply(run);
        }
        run.index = from - 1;
        Next();
    }

    void Next()
    {
        LundunStep old = run.Current;
        if (old != null) old.End(run);
        run.index++;
        LundunStep s = run.Current;
        if (s == null) { Pass(); return; }
        if (run.index > 0 && run.UsesCheckpoint(s.checkpoint)) { checkpointAt = run.index; SaveCheckpointPose(); }
        s.Begin(run);
        PointAt(s);
        Objective(s.objective);
        if (LundunGuide.Instance != null && !string.IsNullOrEmpty(s.objective)) LundunGuide.Instance.Announce(s.objective);   // guidance: big, then pinned
    }

    /// S8: a step moved its marker (the next drop): show it again.
    public static void Repoint()
    {
        if (Instance == null || Instance.run == null || Instance.run.Current == null) return;
        Instance.PointAt(Instance.run.Current);
    }

    /// The marker and GPS route for the step (none if it has none).
    void PointAt(LundunStep s)
    {
        if (objectiveMarker != null) { LundunMinimap mm = FindAnyObjectByType<LundunMinimap>(); if (mm != null) mm.Untrack(objectiveMarker); Destroy(objectiveMarker.gameObject); objectiveMarker = null; }
        LundunNavigation nav = LundunNavigation.Instance;
        if (routeSet && nav != null) { nav.Clear(); routeSet = false; }
        if (!s.hasMarker) return;
        objectiveMarker = Glow("Objective", s.marker, Mathf.Max(0.9f, s.markerRadius * 0.45f), 3f);
        LundunMinimap map = FindAnyObjectByType<LundunMinimap>();
        if (map != null) map.Track(objectiveMarker, MarkerYellow, "", 22f, true);
        bool far = LundunMissionRun.James() != null && LundunMissionRun.FlatDistance(LundunMissionRun.James().position, s.marker) > 40f;
        if (s.air && nav != null) { nav.SetStraight(s.marker, s.objective); routeSet = true; }                   // S8: flying
        else if ((s.route || far) && nav != null) { nav.SetDestination(s.marker, s.objective); routeSet = true; }   // guidance: a route whenever it's a walk
    }

    void SaveCheckpointPose()
    {
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        PlayerHealth ph = PlayerHealth.Instance;
        if (vs == null || ph == null || ph.Body == null) return;
        checkpointCar = vs.CurrentCar;
        checkpointInCar = checkpointCar != null;
        if (checkpointInCar)
        {
            carPos = checkpointCar.transform.position; carRot = checkpointCar.transform.rotation;
            checkpointPos = carPos + checkpointCar.transform.right * 1.9f;          // beside the driver's door
            checkpointRot = Quaternion.Euler(0f, carRot.eulerAngles.y, 0f);
        }
        else { checkpointPos = ph.Body.position; checkpointRot = Quaternion.Euler(0f, ph.Body.eulerAngles.y, 0f); }
    }

    /// Back to the checkpoint: police gone, health full, James where he was (and his car beside him, if he had one).
    void ToCheckpoint()
    {
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        PlayerHealth ph = PlayerHealth.Instance;
        try { if (WantedSystem.Instance != null) WantedSystem.Instance.ClearWanted(); } catch (System.Exception e) { Debug.LogException(e); }
        try { PoliceAI.ClearAll(); } catch (System.Exception e) { Debug.LogException(e); }
        if (vs != null && vs.IsDriving) vs.ExitCar();
        // he was driving: the same car if it's still in one piece, otherwise his own (the Kestrel); on foot: no car
        CarController car = null;
        if (checkpointInCar)
        {
            car = checkpointCar;
            VehicleHealth cvh = car != null ? car.GetComponent<VehicleHealth>() : null;
            if (car == null || (cvh != null && cvh.IsWrecked))
            {
                Transform k = LundunPlaces.Instance != null ? LundunPlaces.Instance.Kestrel : null;
                car = k != null ? k.GetComponent<CarController>() : null;
                VehicleHealth kvh = car != null ? car.GetComponent<VehicleHealth>() : null;
                if (kvh != null && kvh.IsWrecked) car = null;                          // both gone: on foot it is
            }
        }
        if (car != null)
        {
            Rigidbody rb = car.GetComponent<Rigidbody>();
            Vector3 at = carPos + Vector3.up * 0.4f;
            car.transform.SetPositionAndRotation(at, carRot);
            if (rb != null) { rb.position = at; rb.rotation = carRot; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            VehicleHealth vh = car.GetComponent<VehicleHealth>();
            if (vh != null && !vh.IsWrecked) vh.Repair();
        }
        if (ph != null && ph.Body != null)
        {
            CharacterController cc = ph.Body.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            ph.Body.SetPositionAndRotation(checkpointPos + Vector3.up * 0.1f, checkpointRot);
            if (cc != null) cc.enabled = true;
            ph.ResetHealth();
        }
    }

    void Objective(string s)
    {
        if (objectiveText == null) return;
        if (LundunGuide.Instance != null)                                       // guidance: the pinned box under the minimap
        {
            LundunGuide.Instance.Pin(s);
            if (objectiveBox.gameObject.activeSelf) objectiveBox.gameObject.SetActive(false);
            return;
        }
        if (objectiveText.text != s) objectiveText.text = s;
        objectiveBox.gameObject.SetActive(!string.IsNullOrEmpty(s));
    }

    // ------------------------------------------------------------------ failing, retrying, quitting, passing
    void OnWasted() { if (run != null) { run.Fail("You were wasted"); EndAttempt(); } }
    void OnBusted() { if (run != null) { run.Fail("You were busted"); EndAttempt(); } }

    /// Stop this attempt: clear what it spawned and its markers (the fail screen comes once any WASTED / BUSTED
    /// screen has finished).
    void EndAttempt()
    {
        LundunStep s = run.Current;
        if (s != null) s.End(run);
        Clean(false);
        failShown = false;
    }

    void Clean(bool keepVisiblePeople)
    {
        Say(null, null, 0f);
        LundunCrew.HangBack = false;
        if (keepVisiblePeople) foreach (string who in run.crew) LundunCrew.Leave(who);       // passed / quit: they walk off
        else LundunCrew.LeaveAll(true);                                                    // failed: gone (retry brings them back)
        run.crew.Clear();
        // S3: whatever the mission switched on
        LundunCrew.ClearStaged();
        LundunCrew.DisarmAll();                                                 // S4: guns given for the mission
        LundunChaseCar.Clear();                                                 // S4: enemy cars
        LundunStoryShop.MissionOpen = false;
        LundunTube.ForcedTo = null;                                             // S5: following someone down the Tube
        LundunRoadblocks.MissionFromStars = 0;
        LundunRobbery.MissionStore = null; LundunRobbery.MissionCash = 0;
        LundunArches.MissionInside = false;                                     // S8: Warehouse 9's floor
        LundunFlyHeli.LetGoAll();                                               // S8: the helicopter (James back on his feet)
        if (LundunTrain.Instance != null) LundunTrain.Instance.StopHolding();
        LundunMissionZones.Clear();
        if (objectiveMarker != null) { LundunMinimap mm = FindAnyObjectByType<LundunMinimap>(); if (mm != null) mm.Untrack(objectiveMarker); Destroy(objectiveMarker.gameObject); objectiveMarker = null; }
        if (routeSet && LundunNavigation.Instance != null) { LundunNavigation.Instance.Clear(); routeSet = false; }
        if (objectiveBox != null) objectiveBox.gameObject.SetActive(false);
        LundunMinimap map = FindAnyObjectByType<LundunMinimap>();
        Camera cam = Camera.main;
        foreach (GameObject g in run.spawned)
        {
            if (g == null) continue;
            if (map != null) map.Untrack(g.transform);
            Health h = g.GetComponent<Health>();
            bool person = h != null;
            // people someone can see stay (and go once out of sight); everything else goes now
            if (keepVisiblePeople && person && cam != null && LundunView.CanSee(cam, g.transform.position))
            {
                LundunAngryDriver ad = g.GetComponent<LundunAngryDriver>(); if (ad != null) Destroy(ad);
                leftovers.Add(g);
            }
            else Destroy(g);
        }
        run.spawned.Clear();
    }

    /// S3: a car the mission made, now it's done with: James keeps it if he's in it; one in view goes once nobody's
    /// looking; otherwise it goes now.
    public static void LetGo(GameObject car)
    {
        if (car == null) return;
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        if (vs != null && vs.CurrentCar != null && vs.CurrentCar.gameObject == car) return;
        Camera cam = Camera.main;
        if (Instance != null && cam != null && LundunView.CanSee(cam, car.transform.position)) { Instance.leftovers.Add(car); return; }
        Destroy(car);
    }

    /// S5: missions played again (from the pause menu's CREW page): their start markers come back until passed.
    private static readonly HashSet<string> replaying = new HashSet<string>();
    public static bool Replaying(string id) { return replaying.Contains(id); }

    /// S5: play a passed mission again (its start marker comes back, its giver waits there). The choice in it is made
    /// again, and trust follows the new choice. False if a mission's on or it hasn't been passed.
    public static bool Replay(string id)
    {
        if (Instance == null || Running || !LundunMissionBook.Done(LundunStory.Data, id)) return false;
        replaying.Add(id);
        Instance.RefreshStarts();
        return true;
    }

    /// S3: start markers again, for whatever's available now (after a mission's passed).
    void RefreshStarts()
    {
        LundunMinimap mm = FindAnyObjectByType<LundunMinimap>();
        foreach (var kv in starts) if (kv.Value != null) { if (mm != null) mm.Untrack(kv.Value); Destroy(kv.Value.gameObject); }
        starts.Clear();
        foreach (LundunMission m in LundunMissionBook.All) MakeStart(m);
    }

    void ShowFail()
    {
        failShown = true;
        timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        failWhy.text = run.failed;
        failPanel.SetActive(true);
    }

    void Retry()
    {
        failPanel.SetActive(false);
        Time.timeScale = timeScaleBefore;
        LundunMission m = run.mission;
        int from = checkpointAt;
        run = new LundunMissionRun(m, difficulty);
        m.Build(run);
        ToCheckpoint();
        StartAt(from);
    }

    /// QUIT from the fail screen or the pause menu: back to free roam (the mission can be started again).
    public void Quit()
    {
        if (run == null) return;
        if (run.failed == null) { LundunStep s = run.Current; if (s != null) s.End(run); Clean(true); }
        if (failPanel != null) failPanel.SetActive(false);
        if (Time.timeScale == 0f) Time.timeScale = timeScaleBefore;
        run.mission.Tidy();                                                    // S3: its cars
        run = null;
        LundunStory.Data.currentMission = "";
        LundunStory.MarkDirty();
        ShowStarts(true);
        Toast("Mission abandoned");
    }

    /// S8: a mission's been passed (its id, the medal): daily challenges listen.
    public static event System.Action<string, int> Passed;

    void Pass()
    {
        LundunMission m = run.mission;
        int medal = Mathf.Clamp(m.Medal(run), 0, 3);
        if (run.difficulty == LundunDifficulty.Easy) medal = Mathf.Min(medal, 2);   // gold needs Normal or Hard
        try { Passed?.Invoke(m.Id, medal); } catch (System.Exception e) { Debug.LogException(e); }
        long pay = (long)Mathf.Round(m.Reward * run.RewardScale);
        int secs = Mathf.Max(1, Mathf.RoundToInt(run.Elapsed));
        Clean(true);
        LundunStoryData d = LundunStory.Data;
        string doneFlag = "done:" + m.Id;
        bool first = !d.flags.Contains(doneFlag);
        if (first) { d.flags.Add(doneFlag); if (m.Story) d.missionsDone++; }
        string medalKey = "medal:" + m.Id + ":";
        int best = 0;
        foreach (string s in d.medals) if (s.StartsWith(medalKey)) { int v; if (int.TryParse(s.Substring(medalKey.Length), out v)) best = Mathf.Max(best, v); }
        if (medal > best) { d.medals.RemoveAll(s => s.StartsWith(medalKey)); d.medals.Add(medalKey + medal); }
        // S6: the best time too (kept with the medals: "time:<id>:<seconds>")
        int bestTime = BestTime(m.Id);
        bool newBest = !first && (medal > best || (bestTime > 0 && secs < bestTime));
        if (bestTime <= 0 || secs < bestTime) { string tk = "time:" + m.Id + ":"; d.medals.RemoveAll(s => s.StartsWith(tk)); d.medals.Add(tk + secs); }
        d.currentMission = "";
        replaying.Remove(m.Id);                                                // S5
        LundunTrust.Recalc(false);
        LundunWallet.Earn(pay, "Mission passed");
        LundunStory.Write();
        m.Tidy();                                                              // S3: its cars
        run = null;

        passMoney.text = "+" + LundunWallet.Format(pay);
        passMedal.text = (medal == 3 ? "GOLD" : medal == 2 ? "SILVER" : medal == 1 ? "BRONZE" : "") + "   " + Clock(secs) + (newBest ? "   NEW BEST!" : "");
        passMedal.color = medal == 3 ? new Color(1f, 0.82f, 0.25f) : medal == 2 ? new Color(0.8f, 0.84f, 0.9f) : new Color(0.85f, 0.55f, 0.3f);
        timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        ShowNextButtons();
        passPanel.SetActive(true);
        LundunHaptics.Medium();
    }

    // ------------------------------------------------------------------ S6: medals and times
    /// The best medal won on a mission (0 none, 1 bronze, 2 silver, 3 gold).
    public static int BestMedal(string id)
    {
        LundunStoryData d = LundunStory.Data;
        if (d == null || d.medals == null) return 0;
        string key = "medal:" + id + ":"; int best = 0;
        foreach (string s in d.medals) if (s.StartsWith(key)) { int v; if (int.TryParse(s.Substring(key.Length), out v)) best = Mathf.Max(best, v); }
        return best;
    }

    /// The best time on a mission in seconds (0: none yet).
    public static int BestTime(string id)
    {
        LundunStoryData d = LundunStory.Data;
        if (d == null || d.medals == null) return 0;
        string key = "time:" + id + ":"; int best = 0;
        foreach (string s in d.medals) if (s.StartsWith(key)) { int v; if (int.TryParse(s.Substring(key.Length), out v) && (best == 0 || v < best)) best = v; }
        return best;
    }

    public static string Clock(int secs) { return (secs / 60) + ":" + (secs % 60).ToString("00"); }

    // ------------------------------------------------------------------ fix: on to the next mission
    private RectTransform nextButton, roamButton, fade;
    private TextMeshProUGUI roamLabel;
    private Image fadeImage;

    /// The next mission in the story's order that can be played now (null: none left).
    public static LundunMission NextUp()
    {
        LundunStoryData d = LundunStory.Data;
        if (d == null) return null;
        foreach (LundunMission m in LundunMissionBook.All)
        {
            if (!m.Available(d)) continue;
            if (!m.Story && LundunMissionBook.Done(d, m.Id)) continue;          // S8: a side job you've done is there to repeat, not "next"
            return m;
        }
        return null;
    }

    void ShowNextButtons()
    {
        bool any = NextUp() != null;
        nextButton.gameObject.SetActive(any);
        roamButton.anchoredPosition = any ? new Vector2(220f, -150f) : new Vector2(0f, -150f);
        if (roamLabel != null) roamLabel.text = any ? "FREE ROAM" : "CONTINUE";
    }

    void ClosePass(bool next)
    {
        passPanel.SetActive(false);
        Time.timeScale = timeScaleBefore;
        RefreshStarts();                                                       // S3: the next mission's marker
        LundunMission n = NextUp();
        if (n == null) return;
        if (next) { StartCoroutine(GoTo(n)); return; }
        // free roam: the GPS route to it, and a reminder of who's waiting where
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav != null) nav.SetDestination(LundunMissionRun.Ground(n.StartAt), n.Title);
        Toast("NEXT: " + n.Title.ToUpperInvariant() + "\n" + LundunTrust.Pretty(n.Giver) + " is waiting. Follow the purple route");
    }

    /// Fade out, put James beside the next mission's start (out of any car), fade in: its card opens.
    System.Collections.IEnumerator GoTo(LundunMission m)
    {
        fade.gameObject.SetActive(true);
        fadeImage.raycastTarget = true;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.45f) { fadeImage.color = new Color(0f, 0f, 0f, t); yield return null; }
        fadeImage.color = Color.black;
        try { if (WantedSystem.Instance != null) WantedSystem.Instance.ClearWanted(); } catch (System.Exception e) { Debug.LogException(e); }
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        if (vs != null && vs.IsDriving) vs.ExitCar();
        yield return null;
        Vector3 start = LundunMissionRun.Ground(m.StartAt);
        Vector3 f = m.GiverFacing; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        Vector3 stand = start + f * 2.2f;                                       // just in front, facing the marker and the giver
        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(stand + Vector3.up, out hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) stand = hit.position;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph != null && ph.Body != null)
        {
            CharacterController cc = ph.Body.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            ph.Body.SetPositionAndRotation(stand + Vector3.up * 0.1f, Quaternion.LookRotation(-f));
            if (cc != null) cc.enabled = true;
        }
        declined = null;
        LundunCrew.ShowUpNow();                                                 // the giver's there when the screen comes back
        float until = Time.unscaledTime + 0.6f;
        while (Time.unscaledTime < until) yield return null;
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.45f) { fadeImage.color = new Color(0f, 0f, 0f, t); yield return null; }
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeImage.raycastTarget = false;
        fade.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ the glow (start markers and objectives)
    Transform Glow(string name, Vector3 at, float radius, float height)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = "Mission" + name;
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        g.transform.position = at + Vector3.up * (height * 0.5f);
        g.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = MarkerMaterial();
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        g.transform.SetParent(transform, true);
        return g.transform;
    }

    static Material MarkerMaterial()
    {
        if (markerMat != null) return markerMat;
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        markerMat = new Material(s) { name = "MissionGlow" };
        markerMat.SetFloat("_Surface", 1f); markerMat.SetFloat("_Blend", 0f); markerMat.SetFloat("_ZWrite", 0f);
        markerMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); markerMat.SetFloat("_DstBlend", (float)BlendMode.One);   // additive glow
        markerMat.SetFloat("_Cull", (float)CullMode.Off);
        markerMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        markerMat.SetOverrideTag("RenderType", "Transparent");
        markerMat.renderQueue = (int)RenderQueue.Transparent;
        Color c = new Color(1f, 0.75f, 0.1f, 0.35f);
        markerMat.SetColor("_BaseColor", c); markerMat.color = c;
        return markerMat;
    }

    // ------------------------------------------------------------------ UI
    void BuildUI()
    {
        canvasGo = new GameObject("LundunMissionUI", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        Canvas cv = canvasGo.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 70;
        CanvasScaler sc = canvasGo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)canvasGo.transform;

        // the objective line, top centre under the pause button
        objectiveBox = Pill(root, new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(980f, 64f), new Color(0f, 0f, 0f, 0.55f));
        objectiveText = Label(objectiveBox, "", 32f, Color.white); Stretch(objectiveText.rectTransform);
        objectiveBox.gameObject.SetActive(false);

        // dialogue, bottom centre above the controls
        subBox = Pill(root, new Vector2(0.5f, 0f), new Vector2(0f, 250f), new Vector2(1100f, 110f), new Color(0f, 0f, 0f, 0.6f));
        subWho = Label(subBox, "", 26f, Accent); subWho.characterSpacing = 8f;
        RectTransform wr = subWho.rectTransform; wr.anchorMin = new Vector2(0f, 0.58f); wr.anchorMax = Vector2.one; wr.offsetMin = wr.offsetMax = Vector2.zero;
        subText = Label(subBox, "", 30f, Color.white); subText.textWrappingMode = TextWrappingModes.Normal;
        RectTransform tr = subText.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = new Vector2(1f, 0.62f); tr.offsetMin = new Vector2(30f, 6f); tr.offsetMax = new Vector2(-30f, 0f);
        // guidance: tap the subtitle for the next line ("tap to skip" in its corner)
        Image subImg = subBox.GetComponent<Image>(); subImg.raycastTarget = true;
        Button subBtn = subBox.gameObject.AddComponent<Button>(); subBtn.targetGraphic = subImg; subBtn.transition = Selectable.Transition.None;
        subBtn.onClick.AddListener(() => { skip = true; subUntil = 0f; });
        TextMeshProUGUI hint = Label(subBox, "TAP ›", 18f, new Color(1f, 1f, 1f, 0.45f));
        RectTransform hr = hint.rectTransform; hr.anchorMin = hr.anchorMax = new Vector2(1f, 0f); hr.pivot = new Vector2(1f, 0f);
        hr.anchoredPosition = new Vector2(-22f, 8f); hr.sizeDelta = new Vector2(120f, 26f);
        subBox.gameObject.SetActive(false);

        // failed
        failPanel = Overlay(root, "MISSION FAILED", new Color(1f, 0.35f, 0.3f), out failWhy);
        Button((RectTransform)failPanel.transform.GetChild(0), "RETRY", new Vector2(-210f, -150f), new Vector2(380f, 96f), Accent, Retry);
        Button((RectTransform)failPanel.transform.GetChild(0), "QUIT", new Vector2(210f, -150f), new Vector2(380f, 96f), new Color(1f, 1f, 1f, 0.12f), Quit);
        failPanel.SetActive(false);

        // passed
        TextMeshProUGUI passNote;
        passPanel = Overlay(root, "MISSION PASSED", Accent, out passNote);
        passMoney = passNote; passMoney.fontSize = 56f; passMoney.color = new Color(0.35f, 0.95f, 0.45f);
        passMedal = Label((RectTransform)passPanel.transform.GetChild(0), "", 34f, Color.white);
        Place(passMedal.rectTransform, new Vector2(0f, -50f), new Vector2(1100f, 50f));
        passMedal.characterSpacing = 12f;
        // fix: what's next. NEXT MISSION takes James straight to the next one's start (in order); FREE ROAM leaves him
        // where he is with the GPS route to it. With nothing left: just CONTINUE.
        nextButton = Button((RectTransform)passPanel.transform.GetChild(0), "NEXT MISSION", new Vector2(-220f, -150f), new Vector2(400f, 96f), Accent, () => ClosePass(true));
        roamButton = Button((RectTransform)passPanel.transform.GetChild(0), "FREE ROAM", new Vector2(220f, -150f), new Vector2(400f, 96f), new Color(1f, 1f, 1f, 0.12f), () => ClosePass(false));
        roamLabel = roamButton.GetComponentInChildren<TextMeshProUGUI>();
        passPanel.SetActive(false);

        fade = Rect("Fade", root); Stretch(fade);
        fadeImage = fade.gameObject.AddComponent<Image>(); fadeImage.color = new Color(0f, 0f, 0f, 0f); fadeImage.raycastTarget = false;
        fade.gameObject.SetActive(false);
    }

    GameObject Overlay(RectTransform root, string title, Color titleColour, out TextMeshProUGUI note)
    {
        RectTransform o = Rect("Overlay_" + title, root); Stretch(o);
        o.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        RectTransform panel = Rect("Panel", o);
        Place(panel, Vector2.zero, new Vector2(1000f, 460f));
        Image pi = panel.gameObject.AddComponent<Image>();
        pi.sprite = LundunHudArt.Panel; pi.type = Image.Type.Sliced; pi.pixelsPerUnitMultiplier = 0.35f; pi.color = Ink;
        TextMeshProUGUI t = Label(panel, title, 64f, titleColour); t.characterSpacing = 14f;
        Place(t.rectTransform, new Vector2(0f, 140f), new Vector2(950f, 80f));
        note = Label(panel, "", 32f, Dim); note.textWrappingMode = TextWrappingModes.Normal;
        Place(note.rectTransform, new Vector2(0f, 40f), new Vector2(900f, 90f));
        return o.gameObject;
    }

    void BuildCard(LundunMission m)
    {
        if (card != null) Destroy(card);
        RectTransform root = (RectTransform)canvasGo.transform;
        RectTransform o = Rect("MissionCard", root); Stretch(o);
        card = o.gameObject;
        o.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
        RectTransform panel = Rect("Panel", o);
        Place(panel, Vector2.zero, new Vector2(1100f, 700f));
        Image pi = panel.gameObject.AddComponent<Image>();
        pi.sprite = LundunHudArt.Panel; pi.type = Image.Type.Sliced; pi.pixelsPerUnitMultiplier = 0.35f; pi.color = Ink;
        Color gc; if (!Colours.TryGetValue(m.Giver, out gc)) gc = Accent;
        TextMeshProUGUI giver = Label(panel, m.Giver, 28f, gc); giver.characterSpacing = 10f;
        Place(giver.rectTransform, new Vector2(0f, 290f), new Vector2(1000f, 40f));
        TextMeshProUGUI title = Label(panel, m.Title, 58f, Color.white);
        Place(title.rectTransform, new Vector2(0f, 230f), new Vector2(1040f, 76f));
        TextMeshProUGUI teaser = Label(panel, m.Teaser, 28f, Dim); teaser.textWrappingMode = TextWrappingModes.Normal;
        Place(teaser.rectTransform, new Vector2(0f, 150f), new Vector2(980f, 90f));
        TextMeshProUGUI pay = Label(panel, "PAYS " + LundunWallet.Format(m.Reward) + (string.IsNullOrEmpty(m.MedalNote) ? "" : "   ·   GOLD: " + m.MedalNote), 26f, new Color(0.35f, 0.95f, 0.45f));
        Place(pay.rectTransform, new Vector2(0f, 82f), new Vector2(1040f, 40f));
        TextMeshProUGUI dl = Label(panel, "DIFFICULTY", 24f, Dim); dl.characterSpacing = 8f;
        Place(dl.rectTransform, new Vector2(0f, 26f), new Vector2(600f, 36f));
        diffButtons.Clear();
        string[] names = { "EASY", "NORMAL", "HARD" };
        string[] notes = { "pays ×1", "pays ×1.25", "pays ×1.5" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            RectTransform b = Button(panel, names[i], new Vector2(-330f + i * 330f, -50f), new Vector2(300f, 84f), new Color(1f, 1f, 1f, 0.1f), () => { difficulty = (LundunDifficulty)idx; MarkDifficulty(); });
            diffButtons.Add(b.GetComponent<Image>());
            TextMeshProUGUI n = Label(panel, notes[i], 20f, Dim);
            Place(n.rectTransform, new Vector2(-330f + i * 330f, -108f), new Vector2(300f, 30f));
        }
        LundunMission chosen = m;
        Button(panel, "START", new Vector2(-220f, -230f), new Vector2(400f, 100f), Accent, () => Begin(chosen));
        Button(panel, "NOT NOW", new Vector2(220f, -230f), new Vector2(400f, 100f), new Color(1f, 1f, 1f, 0.12f), () => { declined = chosen; CloseCard(); });
        card.SetActive(false);
    }

    void MarkDifficulty()
    {
        for (int i = 0; i < diffButtons.Count; i++)
        {
            bool on = i == (int)difficulty;
            diffButtons[i].color = on ? Accent : new Color(1f, 1f, 1f, 0.1f);
            TextMeshProUGUI t = diffButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.color = on ? new Color(0.08f, 0.05f, 0.02f) : Color.white;
        }
    }

    RectTransform Button(RectTransform parent, string text, Vector2 at, Vector2 size, Color colour, System.Action click)
    {
        RectTransform r = Rect("Button_" + text, parent);
        Place(r, at, size);
        Image img = r.gameObject.AddComponent<Image>();
        img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.6f; img.color = colour;
        Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(() => { LundunHaptics.Light(); click(); });
        r.gameObject.AddComponent<LundunPressFX>();
        TextMeshProUGUI t = Label(r, text, 36f, colour.a > 0.5f ? new Color(0.08f, 0.05f, 0.02f) : Color.white);
        t.characterSpacing = 8f; Stretch(t.rectTransform);
        return r;
    }

    static RectTransform Pill(RectTransform parent, Vector2 anchor, Vector2 at, Vector2 size, Color c)
    {
        RectTransform r = Rect("Box", parent);
        r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(0.5f, anchor.y); r.anchoredPosition = at; r.sizeDelta = size;
        Image i = r.gameObject.AddComponent<Image>(); i.sprite = LundunHudArt.Pill; i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 0.8f; i.color = c; i.raycastTarget = false;
        return r;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static void Place(RectTransform r, Vector2 at, Vector2 size)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = at; r.sizeDelta = size;
    }

    static TextMeshProUGUI Label(RectTransform parent, string text, float size, Color col)
    {
        RectTransform r = Rect("Text", parent);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.color = col; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
