using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// G8.3a: STUNT JUMPS. Twelve bright, code-made ramps in the city's open spaces (parks, the hilltops, a garden square,
/// the Memorial Circle island). Hit one fast enough in a car, a van or on a hire bike and the game drops into real slow
/// motion (Time.timeScale, with the physics step scaled to match) with a side-on stunt camera while you're in the air.
///   Free Roam: a clean landing shows "STUNT JUMP 4/12" (the first time), saved in Free Roam's PlayerPrefs; minimap blips.
///   Story (G8.3a follow-up): the same ramps in the same places, so the city is identical in both modes, but nothing is
///   saved or read (the banner shows the distance and height every time) and there are no minimap blips. While a mission
///   is running the ramps are just solid ramps: no slow motion, no stunt camera, no banner (missions own the camera,
///   their cutscenes and their clocks).
///
/// Slow motion never leaks: it starts only when the game runs at normal speed, ends only if the speed is still ours, and a
/// watchdog puts the game back to normal speed and the normal physics step if anything leaves it behind. Pausing mid-air
/// is safe: everything that pauses saves the speed it found and puts it back, and a jump can't land while time is frozen.
///
/// Jumps are not logged in the G8.0 incident log (decided: separate until a share card or the feed needs them).
public class LundunStunts : MonoBehaviour
{
    public static LundunStunts Instance { get; private set; }
    public const float SlowScale = 0.35f;                     // distinct from BUSTED's 0.3
    const string SaveKey = "lad_fr_stunts";                    // Free Roam: a bit per jump (Story never reads or writes it)

    /// The 12 jumps: where the ramp starts (x, z) and which way it faces (degrees: 0 = north/+z, 90 = east/+x). Chosen
    /// from the world plan and the scene's colliders so the run-up, the ramp and the landing are clear.
    public static readonly StuntSite[] Sites =
    {
        new StuntSite("Hydemoor Park east", 262f, 1040f, 270f),
        new StuntSite("Hydemoor Park south", 150f, 960f, 0f),
        new StuntSite("Gilmore Green east", 328f, 236f, 345f),   // moved clear of mission 14's fight and ambush spots
        new StuntSite("Quay Gardens", 1300f, 445f, 15f),
        new StuntSite("Rye Hill summit", 1440f, 160f, 70f),
        new StuntSite("Rye Hill west slope", 1250f, 150f, 270f),
        new StuntSite("Brockmere Park south slope", 600f, -845f, 180f),
        new StuntSite("Brockmere Park west slope", 520f, -790f, 280f),
        new StuntSite("St Jarvis's Park south", 330f, 1200f, 0f),
        new StuntSite("St Jarvis's Park east", 525f, 1500f, 270f),
        new StuntSite("Palace Gardens", 660f, 1855f, 270f),
        new StuntSite("Memorial Circle", 575f, 1634f, 15f),
    };

    [Header("Tuning")]
    [SerializeField] private float carMinSpeed = 15f;          // m/s along the ramp (about 34 mph)
    [SerializeField] private float bikeMinSpeed = 5f;          // a hire bike's top speed is 9 m/s
    [SerializeField] private float bikePop = 3f;               // extra lift for a bike leaving the lip (m/s)
    [SerializeField] private float maxAir = 8f;                // game seconds before giving up
    [SerializeField] private string state;

    readonly List<StuntRamp> ramps = new List<StuntRamp>();
    VehicleSystem vehicles;
    LundunMinimap minimap;
    float baseFixed = 0.02f;
    bool slowOurs;
    float slowWatch;

    // the jump in progress
    enum Phase { Idle, Armed, Air, Settling }
    Phase phase = Phase.Idle;
    int jumpSite = -1;
    CarController jumpCar; LundunBike jumpBike; CharacterController jumpBikeCc;
    float armedAt, airStart, settleUntil, groundedFor; bool bikeLaunched;
    Vector3 lipPoint, landPoint; float topY;
    // the stunt camera
    CameraFollow follow; Transform camT; Vector3 camPos; bool camOurs; float camReleaseAt;
    // the banner
    Canvas canvas; TextMeshProUGUI bannerTitle, bannerSub; float bannerUntil;
    // James's bike (polled)
    LundunBike myBike; float nextBikePoll;

    // ------------------------------------------------------------------ lifecycle (the city scene, both modes)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { SceneManager.sceneLoaded -= OnSceneLoaded; Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterFirstLoad() { for (int i = 0; i < SceneManager.sceneCount; i++) Ensure(SceneManager.GetSceneAt(i)); }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) { Ensure(s); }

    static void Ensure(Scene s)
    {
        if (!LundunRadio.IsCity(s)) return;                                  // the city, in Free Roam and Story alike
        if (Instance != null && Instance.gameObject.scene == s) return;      // already here
        GameObject g = new GameObject("LundunStunts");
        if (g.scene != s) SceneManager.MoveGameObjectToScene(g, s);           // it goes with the city when the city reloads
        g.AddComponent<LundunStunts>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene) { Destroy(gameObject); return; }
        Instance = this;
        baseFixed = Time.fixedDeltaTime > 0.0001f && !Mathf.Approximately(Time.timeScale, SlowScale) ? Time.fixedDeltaTime : 0.02f;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            vehicles = root.GetComponentInChildren<VehicleSystem>(true);
            if (vehicles != null) break;
        }
        BuildRamps();
        BuildBanner();
        Log(LundunGame.IsStory ? "12 stunt jumps placed (Story: nothing saved, no minimap blips)" : "12 stunt jumps placed (Free Roam), " + Done + " already done");
    }

    void OnDestroy()
    {
        RestoreTime("destroyed");
        ReleaseCamera();
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ progress (Free Roam's PlayerPrefs)
    static int Mask { get { return PlayerPrefs.GetInt(SaveKey, 0); } }
    public static bool IsDone(int i) { return (Mask & (1 << i)) != 0; }
    public static int Done { get { int n = 0, m = Mask; for (int i = 0; i < Sites.Length; i++) if ((m & (1 << i)) != 0) n++; return n; } }
    static void MarkDone(int i) { PlayerPrefs.SetInt(SaveKey, Mask | (1 << i)); PlayerPrefs.Save(); }

    // ------------------------------------------------------------------ the ramps
    void BuildRamps()
    {
        StuntRampKit kit = StuntRampKit.Get();
        for (int i = 0; i < Sites.Length; i++)
        {
            StuntRamp r = StuntRamp.Place(Sites[i], i, kit, transform);
            ramps.Add(r);
            string blocked = r.CheckClearance();
            if (blocked != null) Log("jump " + (i + 1) + " (" + Sites[i].name + "): something in the way: " + blocked + " (please check in the playtest)");
        }
    }

    void TrackOnMap()
    {
        if (minimap != null || LundunGame.IsStory) return;                      // Story: no blips (nothing to complete)
        minimap = FindAnyObjectByType<LundunMinimap>();
        if (minimap == null) return;
        for (int i = 0; i < ramps.Count; i++) Blip(i);
    }

    void Blip(int i)
    {
        if (minimap == null || ramps[i] == null) return;
        minimap.Track(ramps[i].transform, IsDone(i) ? new Color(0.35f, 0.85f, 0.45f) : new Color(1f, 0.55f, 0.1f), "", 14f, false);
    }

    // ------------------------------------------------------------------ every frame
    void Update()
    {
        TrackOnMap();
        Watchdog();
        if (Time.timeScale == 0f) return;                                        // paused: nothing moves, nothing lands
        switch (phase)
        {
            case Phase.Idle: LookForRun(); break;
            case Phase.Armed: TickArmed(); break;
            case Phase.Air: TickAir(); break;
            case Phase.Settling: TickSettle(); break;
        }
        state = phase + (jumpSite >= 0 ? " · jump " + (jumpSite + 1) : "") + (slowOurs ? " · slow" : "");
    }

    void LateUpdate() { UpdateCamera(); UpdateBanner(); }

    /// What James is riding that can do a stunt: a car or van (never a bus or police car), or his hire bike.
    bool Vehicle(out CarController car, out LundunBike bike)
    {
        car = null; bike = null;
        if (vehicles == null || VehicleSystem.Locked) return false;
        CarController c = vehicles.CurrentCar;
        string why;
        if (c != null && LundunOccupancy.IsCarOrVan(c, out why)) { car = c; return true; }
        if (LundunBikes.Instance != null && LundunBikes.Instance.Riding)
        {
            if (myBike == null || !myBike.Ridden || Time.unscaledTime >= nextBikePoll)
            {
                nextBikePoll = Time.unscaledTime + 0.5f; myBike = null;
                foreach (LundunBike b in FindObjectsByType<LundunBike>(FindObjectsSortMode.None))
                    if (b.Ridden && b.Rider != null && b.Rider.GetComponent<PlayerController>() != null) { myBike = b; break; }
            }
            if (myBike != null && !myBike.Crashed && !myBike.Wrecked) { bike = myBike; return true; }
        }
        return false;
    }

    Vector3 VehiclePos(CarController car, LundunBike bike) { return car != null ? car.transform.position : bike.transform.position; }
    Vector3 VehicleVel(CarController car, LundunBike bike)
    {
        if (car != null) { Rigidbody rb = car.GetComponent<Rigidbody>(); return rb != null ? rb.linearVelocity : Vector3.zero; }
        return bike.transform.forward * bike.Speed;
    }
    bool Airborne(CarController car, LundunBike bike, CharacterController bcc)
    {
        if (car != null) return car.GroundedWheels == 0;
        return bcc != null && !bcc.isGrounded;
    }

    /// A mission is running (Story): the ramps are just ramps (no slow motion, camera or banner).
    static bool MissionOn { get { return LundunMissions.Running; } }

    void LookForRun()
    {
        CarController car; LundunBike bike;
        if (MissionOn || !Vehicle(out car, out bike)) return;
        Vector3 p = VehiclePos(car, bike);
        for (int i = 0; i < ramps.Count; i++)
        {
            StuntRamp r = ramps[i];
            if (r == null) continue;
            float along, side, above;
            r.Local(p, out along, out side, out above);
            if (along < -1f || along > StuntRampKit.Length || Mathf.Abs(side) > StuntRampKit.Width / 2f + 0.6f || above > 2.5f || above < -1f) continue;
            float forward = Vector3.Dot(VehicleVel(car, bike), r.Heading);
            float min = car != null ? carMinSpeed : bikeMinSpeed;
            if (forward < min) continue;
            phase = Phase.Armed; jumpSite = i; jumpCar = car; jumpBike = bike; jumpBikeCc = bike != null ? bike.GetComponent<CharacterController>() : null;
            armedAt = Time.time;
            return;
        }
    }

    void TickArmed()
    {
        CarController car; LundunBike bike;
        if (MissionOn || !Vehicle(out car, out bike) || car != jumpCar || bike != jumpBike) { Reset(); return; }
        StuntRamp r = ramps[jumpSite];
        float along, side, above;
        r.Local(VehiclePos(car, bike), out along, out side, out above);
        if (Mathf.Abs(side) > StuntRampKit.Width / 2f + 1.5f || Time.time - armedAt > 4f) { Reset(); return; }
        if (bike != null && !bikeLaunched && along >= StuntRampKit.Length - 0.6f)
        {
            bikeLaunched = true;
            bike.Launch(Mathf.Max(0f, Vector3.Dot(VehicleVel(car, bike), r.Heading)) * StuntRampKit.Rise + bikePop);   // a bike keeps its lift off the lip
        }
        if (along > StuntRampKit.Length - 0.2f && Airborne(car, bike, jumpBikeCc))
        {
            phase = Phase.Air; airStart = Time.time; lipPoint = r.Lip; topY = VehiclePos(car, bike).y; groundedFor = 0f;
            BeginSlow();
            TakeCamera(r, car != null ? car.transform : bike.transform);
            Log("jump " + (jumpSite + 1) + " (" + Sites[jumpSite].name + "): airborne at " + Vector3.Dot(VehicleVel(car, bike), r.Heading).ToString("0.0") + " m/s");
        }
        else if (along > StuntRampKit.Length + 4f) Reset();                          // rolled off without lift
    }

    void TickAir()
    {
        CarController car; LundunBike bike;
        bool same = Vehicle(out car, out bike) && car == jumpCar && bike == jumpBike;
        if (!same) { Abort("left the vehicle"); return; }
        if (MissionOn) { Abort("a mission started"); return; }
        Vector3 p = VehiclePos(car, bike);
        topY = Mathf.Max(topY, p.y);
        if (slowOurs && !Mathf.Approximately(Time.timeScale, SlowScale)) { Abort("the game speed was changed elsewhere"); return; }
        if (Time.time - airStart > maxAir) { Abort("too long in the air"); return; }
        if (!Airborne(car, bike, jumpBikeCc)) groundedFor += Time.deltaTime; else groundedFor = 0f;
        if (groundedFor >= 0.12f)
        {
            landPoint = p; phase = Phase.Settling; settleUntil = Time.time + 0.45f;
            EndSlow("landed");
        }
    }

    void TickSettle()
    {
        CarController car; LundunBike bike;
        if (MissionOn) { Abort("a mission started"); return; }
        bool same = Vehicle(out car, out bike) && car == jumpCar && bike == jumpBike;
        if (!same) { Finish(false, "crashed"); return; }
        bool upright = car != null ? car.transform.up.y > 0.6f : !bike.Crashed;
        if (!upright) { Finish(false, "crashed"); return; }
        if (Time.time >= settleUntil) Finish(true, "clean");
    }

    void Finish(bool clean, string why)
    {
        int i = jumpSite;
        EndSlow(why);
        camReleaseAt = Time.unscaledTime + (clean ? 0.6f : 0.9f);
        Vector3 d = landPoint - lipPoint; d.y = 0f;
        float dist = clean ? d.magnitude : 0f, high = Mathf.Max(0f, topY - lipPoint.y);
        if (clean)
        {
            bool first = !LundunGame.IsStory && !IsDone(i);                    // Story: never saved, never counted
            if (first) { MarkDone(i); Blip(i); }
            ShowBanner("STUNT JUMP", first ? Done + " / " + Sites.Length : Mathf.RoundToInt(dist) + " m · " + high.ToString("0.0") + " m high", new Color(1f, 0.78f, 0.2f));
            Log("jump " + (i + 1) + ": clean, " + dist.ToString("0.0") + " m, " + high.ToString("0.0") + " m high" + (first ? " — " + Done + "/" + Sites.Length : LundunGame.IsStory ? " (Story: not saved)" : " (already done)"));
        }
        else
        {
            ShowBanner("STUNT JUMP FAILED", "", new Color(1f, 0.32f, 0.28f));
            Log("jump " + (i + 1) + ": failed (" + why + ")");
        }
        Reset();
    }

    void Reset() { phase = Phase.Idle; jumpSite = -1; jumpCar = null; jumpBike = null; jumpBikeCc = null; bikeLaunched = false; }

    /// Not a stunt after all (left the vehicle, something else changed the game speed, stuck in the air): no banner.
    void Abort(string why)
    {
        Log("jump " + (jumpSite + 1) + ": abandoned (" + why + ")");
        EndSlow(why);
        camReleaseAt = Time.unscaledTime;
        Reset();
    }

    // ------------------------------------------------------------------ real slow motion, never leaking
    void BeginSlow()
    {
        if (!Mathf.Approximately(Time.timeScale, 1f)) return;                    // only from normal speed
        Time.timeScale = SlowScale;
        Time.fixedDeltaTime = baseFixed * SlowScale;                               // smooth physics in slow motion
        slowOurs = true;
    }

    void EndSlow(string why)
    {
        if (!slowOurs) return;
        slowOurs = false;
        if (Mathf.Approximately(Time.timeScale, SlowScale)) Time.timeScale = 1f;   // only if it's still ours
        Time.fixedDeltaTime = baseFixed;
    }

    void RestoreTime(string why)
    {
        if (slowOurs || Mathf.Approximately(Time.timeScale, SlowScale)) { if (Mathf.Approximately(Time.timeScale, SlowScale)) Time.timeScale = 1f; }
        slowOurs = false;
        if (!Mathf.Approximately(Time.fixedDeltaTime, baseFixed)) Time.fixedDeltaTime = baseFixed;
    }

    /// If no jump is in the air, the game must not stay at our slow speed or our physics step (half a second's grace).
    void Watchdog()
    {
        bool leak = !slowOurs && (Mathf.Approximately(Time.timeScale, SlowScale) || (!Mathf.Approximately(Time.fixedDeltaTime, baseFixed) && Time.timeScale > 0f));
        if (!leak) { slowWatch = 0f; return; }
        slowWatch += Time.unscaledDeltaTime;
        if (slowWatch < 0.5f) return;
        slowWatch = 0f;
        Log("watchdog: slow motion left behind, back to normal speed");
        RestoreTime("watchdog");
    }

    // ------------------------------------------------------------------ the stunt camera (side-on, looking at the vehicle)
    void TakeCamera(StuntRamp r, Transform target)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        follow = cam.GetComponent<CameraFollow>();
        if (follow == null || !follow.enabled) return;                              // something else has the camera
        camT = cam.transform;
        Vector3 mid = r.Lip + r.Heading * 12f;
        Vector3 right = Vector3.Cross(Vector3.up, r.Heading).normalized;
        Vector3 best = Vector3.zero; bool found = false;
        foreach (float sideSign in new[] { 1f, -1f })
        {
            Vector3 c = mid + right * (14f * sideSign) + Vector3.up * 4f;
            c.y = Mathf.Max(c.y, LundunGround.Height(c.x, c.z) + 2.5f);
            if (!Physics.Linecast(r.Lip + Vector3.up * 2f, c, ~LayerMask.GetMask("Vehicle", "Player"), QueryTriggerInteraction.Ignore)) { best = c; found = true; break; }
        }
        if (!found) best = r.Start - r.Heading * 6f + Vector3.up * 9f;               // high behind, if both sides are blocked
        follow.enabled = false; camOurs = true; camPos = best; camReleaseAt = float.MaxValue;
        camT.position = camPos;
        camT.rotation = Quaternion.LookRotation(target.position - camPos);
        camTarget = target;
    }
    Transform camTarget;

    void UpdateCamera()
    {
        if (!camOurs || camT == null) return;
        if (camTarget != null)
        {
            Quaternion want = Quaternion.LookRotation((camTarget.position + Vector3.up * 0.8f) - camT.position);
            camT.rotation = Quaternion.Slerp(camT.rotation, want, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        }
        if (Time.unscaledTime >= camReleaseAt || VehicleSystem.Locked) ReleaseCamera();
    }

    void ReleaseCamera()
    {
        if (!camOurs) return;
        camOurs = false; camTarget = null;
        if (follow != null) follow.enabled = true;
    }

    // ------------------------------------------------------------------ the banner
    void BuildBanner()
    {
        GameObject cg = new GameObject("StuntBanner");
        cg.transform.SetParent(transform, false);
        canvas = cg.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 64;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        bannerTitle = Text(cg.transform, 84f, new Vector2(0f, 200f));
        bannerSub = Text(cg.transform, 42f, new Vector2(0f, 120f));
        canvas.enabled = false;
    }

    static TextMeshProUGUI Text(Transform parent, float size, Vector2 at)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(1600f, 120f); rt.anchoredPosition = at;
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.fontSize = size; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center; t.characterSpacing = 6f;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        t.outlineWidth = 0.18f; t.outlineColor = new Color32(0, 0, 0, 220);
        return t;
    }

    void ShowBanner(string title, string sub, Color c)
    {
        if (bannerTitle == null) return;
        bannerTitle.text = title; bannerTitle.color = c;
        bannerSub.text = sub; bannerSub.color = Color.white;
        bannerUntil = Time.unscaledTime + 2.6f;
        canvas.enabled = true;
    }

    void UpdateBanner()
    {
        if (canvas == null || !canvas.enabled) return;
        float left = bannerUntil - Time.unscaledTime;
        if (left <= 0f) { canvas.enabled = false; return; }
        float a = Mathf.Clamp01(left / 0.5f);
        bannerTitle.alpha = a; bannerSub.alpha = a;
    }

    static void Log(string s) { if (Debug.isDebugBuild) Debug.Log("[Stunts] " + s); }

    // ------------------------------------------------------------------ development tests (Editor / development builds)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    int testNext;
    /// Puts the car you're driving 45 m before the next ramp, facing it, at 25 m/s (bike: 9 m/s, 30 m before).
    [ContextMenu("Test: launch at the next jump")]
    void TestNext()
    {
        CarController car; LundunBike bike;
        if (!Vehicle(out car, out bike)) { Log("test: get in a car or on a hire bike first"); return; }
        StuntRamp r = ramps[testNext % ramps.Count]; testNext++;
        float back = car != null ? 45f : 30f;
        Vector3 at = r.Start - r.Heading * back; at.y = LundunGround.Height(at.x, at.z) + 0.6f;
        Quaternion rot = Quaternion.LookRotation(r.Heading);
        if (car != null)
        {
            Rigidbody rb = car.GetComponent<Rigidbody>();
            rb.position = at; rb.rotation = rot; car.transform.SetPositionAndRotation(at, rot);
            rb.linearVelocity = r.Heading * 25f; rb.angularVelocity = Vector3.zero;
        }
        else { CharacterController cc = bike.GetComponent<CharacterController>(); cc.enabled = false; bike.transform.SetPositionAndRotation(at, rot); cc.enabled = true; }
        Log("test: at jump " + ((testNext - 1) % ramps.Count + 1) + " (" + r.Site.name + ")");
    }
    [ContextMenu("Test: forget stunt progress")] void TestForget() { if (LundunGame.IsStory) { Log("Story: nothing is saved"); return; } PlayerPrefs.DeleteKey(SaveKey); for (int i = 0; i < ramps.Count; i++) Blip(i); Log("progress cleared"); }
#endif
}

// ====================================================================== the ramps

public struct StuntSite
{
    public string name; public float x, z, heading;
    public StuntSite(string name, float x, float z, float heading) { this.name = name; this.x = x; this.z = z; this.heading = heading; }
}

/// One ramp: a wedge 10 m long, 2 m high at the lip (an 11° climb that a hire bike can ride up), 5.5 m wide, in
/// yellow-and-black chevrons that glow at night, orange sides, hazard-striped back, a flag post either side of the lip
/// and a tall orange beacon, sat on the ground (tilted with a hillside).
public class StuntRamp : MonoBehaviour
{
    public StuntSite Site { get; private set; }
    public int Index { get; private set; }
    public Vector3 Heading { get; private set; }
    public Vector3 Start { get { return transform.position; } }
    public Vector3 Lip { get { return transform.TransformPoint(new Vector3(0f, StuntRampKit.Height, StuntRampKit.Length)); } }

    public static StuntRamp Place(StuntSite s, int index, StuntRampKit kit, Transform parent)
    {
        GameObject g = new GameObject("StuntJump_" + (index + 1));
        g.transform.SetParent(parent, false);
        Vector3 f = Quaternion.Euler(0f, s.heading, 0f) * Vector3.forward, right = Vector3.Cross(Vector3.up, f);
        Vector3 start = new Vector3(s.x, 0f, s.z), lip = start + f * StuntRampKit.Length;
        float h0 = Ground(start), h1 = Ground(lip);
        float hl = Ground(start + f * (StuntRampKit.Length / 2f) - right * (StuntRampKit.Width / 2f));
        float hr = Ground(start + f * (StuntRampKit.Length / 2f) + right * (StuntRampKit.Width / 2f));
        float pitch = Mathf.Atan2(h1 - h0, StuntRampKit.Length) * Mathf.Rad2Deg;          // with the hillside
        float roll = Mathf.Atan2(hr - hl, StuntRampKit.Width) * Mathf.Rad2Deg;
        g.transform.SetPositionAndRotation(new Vector3(s.x, Mathf.Min(h0, (hl + hr) / 2f) - 0.04f, s.z), Quaternion.Euler(-pitch, s.heading, -roll));
        StuntRamp r = g.AddComponent<StuntRamp>();
        r.Site = s; r.Index = index; r.Heading = f;
        kit.Dress(g);
        return r;
    }

    /// The ground's height here (the hills included), found by a short ray from just above it.
    public static float Ground(Vector3 p)
    {
        float guess = LundunGround.Height(p.x, p.z);
        RaycastHit hit;
        if (Physics.Raycast(new Vector3(p.x, guess + 4f, p.z), Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody == null) return hit.point.y;
        return guess;
    }

    /// A point in the ramp's terms: how far along it (0 = the start, Length = the lip), how far to the side, and how
    /// high above the ramp's surface.
    public void Local(Vector3 world, out float along, out float side, out float above)
    {
        Vector3 l = transform.InverseTransformPoint(world);
        along = l.z; side = l.x;
        float surface = Mathf.Clamp01(l.z / StuntRampKit.Length) * StuntRampKit.Height;
        above = l.y - surface;
    }

    /// Run-up, ramp and landing: anything solid in the way (not the ground, not cars or people)? Null when clear.
    public string CheckClearance()
    {
        Vector3 f = Heading, right = Vector3.Cross(Vector3.up, f);
        for (float a = -25f; a <= StuntRampKit.Length + 40f; a += 2.5f)
        {
            if (a >= -1f && a <= StuntRampKit.Length + 1f) continue;                  // the ramp itself
            Vector3 c = Start + f * a; c.y = Ground(c) + 1.8f;
            float half = a < 0f ? 3f : 5f;
            Collider[] hits = Physics.OverlapBox(c, new Vector3(half, 1.2f, 1.25f), Quaternion.LookRotation(f), ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider h in hits)
            {
                if (h == null || h.attachedRigidbody != null || h.transform.IsChildOf(transform.parent)) continue;
                return h.name + " (" + (a < 0f ? "run-up" : "landing") + ", " + a.ToString("0") + " m)";
            }
        }
        return null;
    }
}

/// The shared mesh and materials for every ramp (made once, in code).
public class StuntRampKit
{
    public const float Length = 10f, Height = 2f, Width = 5.5f;
    public static float Rise { get { return Height / Length; } }                         // the climb, as a fraction (11.3°)
    public static float AngleDeg { get { return Mathf.Atan2(Height, Length) * Mathf.Rad2Deg; } }

    static StuntRampKit shared;
    public Mesh wedge;
    public Material top, sides, back, pole, flag, beacon;

    public static StuntRampKit Get() { if (shared == null || shared.wedge == null) shared = new StuntRampKit(); return shared; }

    StuntRampKit()
    {
        wedge = WedgeMesh();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) lit = Shader.Find("Standard");
        top = Mat(lit, Color.white, Chevrons(), new Color(0.55f, 0.42f, 0.05f), new Vector2(1f, 3f));
        sides = Mat(lit, new Color(1f, 0.45f, 0.05f), null, new Color(0.25f, 0.09f, 0f), Vector2.one);
        back = Mat(lit, Color.white, Hazard(), new Color(0.35f, 0.27f, 0.03f), new Vector2(2f, 1f));
        pole = Mat(lit, new Color(0.95f, 0.95f, 0.95f), null, Color.black, Vector2.one);
        flag = Mat(lit, new Color(1f, 0.35f, 0.05f), null, new Color(0.6f, 0.18f, 0f), Vector2.one);
        beacon = Mat(lit, new Color(1f, 0.55f, 0.1f), null, new Color(2.2f, 1.1f, 0.15f), Vector2.one);
    }

    /// The wedge: rises from 0 at the start (z = 0) to Height at the lip (z = Length); a vertical back. Three submeshes:
    /// the slope (chevrons), the two sides, and the back.
    public static Mesh WedgeMesh()
    {
        float w = Width / 2f, L = Length, H = Height;
        Vector3[] v =
        {
            // slope (0-3)
            new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(w, H, L), new Vector3(-w, H, L),
            // left side (4-6), right side (7-9)
            new Vector3(-w, 0f, 0f), new Vector3(-w, H, L), new Vector3(-w, 0f, L),
            new Vector3(w, 0f, 0f), new Vector3(w, 0f, L), new Vector3(w, H, L),
            // back (10-13)
            new Vector3(-w, 0f, L), new Vector3(-w, H, L), new Vector3(w, H, L), new Vector3(w, 0f, L),
            // bottom (14-17)
            new Vector3(-w, 0f, 0f), new Vector3(-w, 0f, L), new Vector3(w, 0f, L), new Vector3(w, 0f, 0f),
        };
        Vector2[] uv =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
        };
        Mesh m = new Mesh { name = "StuntRampWedge" };
        m.vertices = v; m.uv = uv;
        m.subMeshCount = 3;
        m.SetTriangles(new[] { 0, 3, 2, 0, 2, 1 }, 0);                                 // slope (faces up and back towards the run-up)
        m.SetTriangles(new[] { 4, 6, 5, 7, 9, 8, 14, 17, 16, 14, 16, 15 }, 1);        // sides and bottom
        m.SetTriangles(new[] { 10, 12, 11, 10, 13, 12 }, 2);                           // back (faces away from the run-up)
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    public void Dress(GameObject g)
    {
        g.AddComponent<MeshFilter>().sharedMesh = wedge;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { top, sides, back };
        MeshCollider mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = wedge;
        // flag posts either side of the lip, and a tall beacon by the start so you can see it from down the road
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 at = new Vector3(s * (Width / 2f + 0.5f), 0f, Length - 0.2f);
            Box(g.transform, "FlagPost", at + Vector3.up * 2.4f, new Vector3(0.12f, 4.8f, 0.12f), pole);
            Box(g.transform, "Flag", at + new Vector3(s * 0.45f, 4.3f, 0f), new Vector3(0.9f, 0.6f, 0.04f), flag);
        }
        Box(g.transform, "Beacon", new Vector3(-(Width / 2f + 1.2f), 3.5f, -0.5f), new Vector3(0.18f, 7f, 0.18f), pole);
        Box(g.transform, "BeaconLight", new Vector3(-(Width / 2f + 1.2f), 7.2f, -0.5f), new Vector3(0.55f, 0.55f, 0.55f), beacon);
    }

    static void Box(Transform parent, string name, Vector3 at, Vector3 size, Material m)
    {
        GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = name;
        Collider c = b.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c);   // decoration only: nothing to hit
        b.transform.SetParent(parent, false);
        b.transform.localPosition = at; b.transform.localScale = size;
        b.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    static Material Mat(Shader s, Color c, Texture2D tex, Color emission, Vector2 tiling)
    {
        Material m = new Material(s) { name = "StuntRamp" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (tex != null)
        {
            foreach (string p in new[] { "_BaseMap", "_MainTex" })
                if (m.HasProperty(p)) { m.SetTexture(p, tex); m.SetTextureScale(p, tiling); }
        }
        m.SetFloat("_Smoothness", 0.25f);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            if (tex != null && m.HasProperty("_EmissionMap")) { m.SetTexture("_EmissionMap", tex); m.SetTextureScale("_EmissionMap", tiling); }
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    /// Yellow with black chevrons pointing up the ramp.
    public static Texture2D Chevrons()
    {
        const int n = 64;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "StuntChevrons", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        Color32 y = new Color32(255, 204, 0, 255), k = new Color32(20, 20, 20, 255);
        Color32[] px = new Color32[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n, v = j / (float)n;
                float c = v - Mathf.Abs(u - 0.5f) * 0.9f;                                  // a "^" shape
                float band = c - Mathf.Floor(c * 2f) / 2f;
                px[j * n + i] = band < 0.2f ? k : y;
            }
        t.SetPixels32(px); t.Apply(true);
        return t;
    }

    /// Diagonal yellow and black hazard stripes.
    public static Texture2D Hazard()
    {
        const int n = 64;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "StuntHazard", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        Color32 y = new Color32(255, 204, 0, 255), k = new Color32(20, 20, 20, 255);
        Color32[] px = new Color32[n * n];
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) px[j * n + i] = ((i + j) / 16) % 2 == 0 ? y : k;
        t.SetPixels32(px); t.Apply(true);
        return t;
    }
}
