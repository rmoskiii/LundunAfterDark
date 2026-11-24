using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// G5: the Lundun Underground you walk through (the approved G4 design). One station, built in code the first time
/// it's needed and re-signed for whichever stop you're at. It sits high above that stop (like the pubs' bar room), so
/// the map still shows where you are. Added by LundunTube; nothing in the city changes.
///   street steps (TAKE THE TUBE)  →  ticket hall: the gates open as James walks up  →  walk down the escalators  →
///   the platform: a train pulls in  →  BOARD THE TRAIN, pick a stop  →  a short ride in the dark  →  the same platform
///   at the other end  →  walk up the escalators, through the gates  →  up the steps (WAY OUT) onto the street
/// SKIP is on screen the whole time: straight up to the street at any stop (the old quick ride). The police after you,
/// a mission starting, or James taken away some other way (wasted, busted): out of the station.
/// G9a: the train's doors slide open (a lit carriage behind them) and shut again; people on the platform get on and off:
/// a few step off and walk up the escalators, most of those waiting walk to a door and board, and after it's gone new
/// people come down to wait.
public class LundunTubeStation : MonoBehaviour
{
    public static bool Underground { get; private set; }
    public LundunTube tube;

    // ---- the layout, in metres from the station's origin (ticket hall floor, middle of its front wall)
    const float SetY = 420f;                                  // how high above the stop the station sits
    const float HallH = 4.6f, GateZ = 7.5f;                   // ticket hall: x -10…10, z 0…16 (the way out is at the back)
    const float Drop = 9f, SlopeDeg = 24f;                    // the escalators: down 9 m at 24°
    const float LandZ = -25f;                                 // the bottom landing ends where the platform tunnel starts
    const float TunZ = -28.8f, TunY = -7.6f, TunR = 4.6f;     // the platform tunnel: along x, round
    const float EdgeZ = -29f, TrackY = -10.1f, PlatX = 36f, TrainZ = -30.85f;
    static readonly float[] Lanes = { -5f, -3f, -1f, 1f, 3f, 5f };

    // materials
    const int MFloor = 0, MTile = 1, MNavy = 2, MYellow = 3, MSteel = 4, MBlack = 5, MCeil = 6, MStrip = 7, MRed = 8,
              MWhite = 9, MGlass = 10, MWood = 11, MLamp = 12, MScreen = 13, MGreen = 14, MBallast = 15, MDaylight = 16,
              MPoster0 = 17, MDoorway = 21, MCount = 22;

    GameObject root, train, doorsShut, doorsOpen;              // G9a: the left-sliding and right-sliding leaves
    float doorOpen, doorWant;                                   // G9a: 0 shut … 1 open, and where they're going
    Transform colliders;
    readonly List<TextMeshPro> nameSigns = new List<TextMeshPro>();
    TextMeshPro dotMatrix, linesSign;
    string lineName = "All stations";                        // G6: the line(s) through this station
    class Gate { public Transform a, b; public float ax, bx, open; public Collider block; }
    readonly List<Gate> gates = new List<Gate>();
    readonly List<GameObject> people = new List<GameObject>();

    // G9a: the people on the platform, who walk (their own little paths; no NavMesh down here)
    const byte WWait = 0, WBoard = 1, WOff = 2, WCome = 3;
    class Walker { public GameObject go; public Animator anim; public byte kind; public readonly List<Vector3> path = new List<Vector3>(); public int i; public float speed, startAt, yaw; }
    readonly List<Walker> walkers = new List<Walker>();
    float nextComer;
    int comersLeft;
    Vector3 origin;
    int at = -1;
    bool busy;
    VehicleSystem vehicles;

    enum TrainState { None, Coming, Waiting, Leaving }
    TrainState state;
    float stateAt, waitUntil, nextTrain, nextBoard;
    AudioSource trainAudio, sfx;
    AudioClip rumble, beep, hiss;

    // UI
    GameObject boardGo, skipGo, captionGo;
    TextMeshProUGUI captionText;

    // ================================================================== in and out
    /// Down the steps at station i (LundunTube's TAKE THE TUBE).
    public void Enter(int station)
    {
        if (busy || Underground || tube == null) return;
        StartCoroutine(EnterRoutine(station));
    }

    IEnumerator EnterRoutine(int station)
    {
        busy = true;
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        if (vehicles != null) vehicles.LockControls(true);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f) { tube.Black(t); yield return null; }
        tube.Black(1f);
        if (root == null) { Build(); BuildUI(); }
        MoveTo(station);
        root.SetActive(true);
        Physics.SyncTransforms();
        Fill(false);
        ResetTrain();
        nextTrain = Time.time;                       // the first train comes once James is halfway down
        Place(Local(-5f, 0.05f, 10.6f), 180f);       // at the foot of the steps from the street, facing the gates
        Underground = true;
        yield return new WaitForSecondsRealtime(0.2f);
        if (vehicles != null) vehicles.LockControls(false);
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.35f) { tube.Black(t); yield return null; }
        tube.Black(0f);
        busy = false;
    }

    /// Everything put away (called with the screen black, or when James has been taken away some other way).
    void Close()
    {
        Underground = false;
        busy = false;
        ClearPeople();
        ResetTrain();
        if (root != null) root.SetActive(false);
        if (boardGo != null) boardGo.SetActive(false);
        if (skipGo != null) skipGo.SetActive(false);
        if (captionGo != null) captionGo.SetActive(false);
    }

    /// Up to the street at stop `to` (the way out, SKIP, or thrown out).
    void Leave(int to, string toast)
    {
        if (busy) return;
        busy = true;
        if (boardGo != null) boardGo.SetActive(false);
        if (skipGo != null) skipGo.SetActive(false);
        if (!string.IsNullOrEmpty(toast)) LundunMissions.Toast(toast);
        tube.QuickRide(at, to, tube.Minutes(at, to), Close);
    }

    void OnDestroy() { if (Underground) Underground = false; }

    // ================================================================== every frame
    void Update()
    {
        if (!Underground || root == null) return;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.Body == null) return;
        Vector3 l = ph.Body.position - origin;
        if (!busy)
        {
            if (l.sqrMagnitude > 95f * 95f) { Close(); return; }                       // wasted, busted, teleported
            if (WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0) { Leave(at, "The police are at the station"); return; }
            if (LundunMissions.Running) { Leave(at, null); return; }
            if (l.z > 11.5f && l.x > -8f && l.x < -2f && l.y > -0.5f && l.y < 4f) { Leave(at, null); return; }   // up the steps: WAY OUT
        }
        Gates(l);
        TrainTick(l);
        DoorTick();
        Walk();
        bool panel = tube.PanelOpen;
        boardGo.SetActive(!busy && !panel && state == TrainState.Waiting && doorWant > 0.5f && Time.time >= nextBoard && l.y < -8f && l.z < -26.4f && l.z > -29.5f);
        skipGo.SetActive(!busy && !panel);
    }

    // ------------------------------------------------------------------ the gates: open as James walks up (either side)
    void Gates(Vector3 l)
    {
        foreach (Gate g in gates)
        {
            float lane = g.ax + 0.31f;                    // the lane's middle
            bool near = Mathf.Abs(l.x - lane) < 1.0f && Mathf.Abs(l.z - GateZ) < 2.3f && Mathf.Abs(l.y) < 1.5f;
            if (near && g.open < 0.02f && sfx != null && beep != null) sfx.PlayOneShot(beep, 0.6f);
            g.open = Mathf.MoveTowards(g.open, near ? 1f : 0f, Time.deltaTime * 3.5f);
            g.a.localPosition = new Vector3(g.ax - g.open * 0.62f, g.a.localPosition.y, g.a.localPosition.z);
            g.b.localPosition = new Vector3(g.bx + g.open * 0.62f, g.b.localPosition.y, g.b.localPosition.z);
            bool shut = g.open < 0.5f;
            if (g.block.enabled != shut) g.block.enabled = shut;
        }
    }

    // ------------------------------------------------------------------ the train
    void ResetTrain()
    {
        state = TrainState.None;
        if (train != null) { train.SetActive(false); train.transform.localPosition = new Vector3(120f, 0f, 0f); }
        Doors(false, true);
        if (trainAudio != null) trainAudio.Stop();
    }

    // G9a: the leaves slide apart (0.65 m each way) over about a second, and back
    const float DoorSlide = 0.65f, DoorTime = 1.1f;
    void Doors(bool open) { Doors(open, false); }
    void Doors(bool open, bool instant)
    {
        doorWant = open ? 1f : 0f;
        if (instant) { doorOpen = doorWant; SetLeaves(); }
    }

    void SetLeaves()
    {
        float k = Mathf.SmoothStep(0f, 1f, doorOpen);
        if (doorsShut != null) doorsShut.transform.localPosition = new Vector3(-DoorSlide * k, 0f, 0f);
        if (doorsOpen != null) doorsOpen.transform.localPosition = new Vector3(DoorSlide * k, 0f, 0f);
    }

    void DoorTick()
    {
        if (Mathf.Approximately(doorOpen, doorWant)) return;
        doorOpen = Mathf.MoveTowards(doorOpen, doorWant, Time.deltaTime / DoorTime);
        SetLeaves();
    }

    void TrainTick(Vector3 l)
    {
        const float Come = 7f, Go = 6f, Far = 115f;
        float now = Time.time;
        switch (state)
        {
            case TrainState.None:
                if (l.y < -4f && now >= nextTrain)
                {
                    state = TrainState.Coming; stateAt = now;
                    train.SetActive(true); Doors(false);
                    train.transform.localPosition = new Vector3(Far, 0f, 0f);
                    if (rumble != null) { trainAudio.clip = rumble; trainAudio.Play(); }
                }
                break;
            case TrainState.Coming:
            {
                float k = Mathf.Clamp01((now - stateAt) / Come);
                train.transform.localPosition = new Vector3(Far * (1f - k) * (1f - k), 0f, 0f);
                if (k >= 1f)
                {
                    state = TrainState.Waiting; waitUntil = now + 25f;
                    Doors(true);
                    if (hiss != null) sfx.PlayOneShot(hiss, 0.7f);
                    DoorsOpened(Random.Range(1, 5));                // G9a: off, then on
                }
                break;
            }
            case TrainState.Waiting:
                if (now >= waitUntil - DoorTime - 0.3f && doorWant > 0.5f)
                {
                    Doors(false);                                   // G9a: shut before it moves
                    if (hiss != null) sfx.PlayOneShot(hiss, 0.7f);
                    MissedIt();
                }
                if (now >= waitUntil)
                {
                    state = TrainState.Leaving; stateAt = now;
                    if (doorWant > 0.5f && hiss != null) sfx.PlayOneShot(hiss, 0.7f);    // G9a: (they usually shut already)
                    Doors(false, true);
                    if (rumble != null) { trainAudio.clip = rumble; trainAudio.Play(); }
                }
                break;
            case TrainState.Leaving:
            {
                float k = Mathf.Clamp01((now - stateAt) / Go);
                train.transform.localPosition = new Vector3(-Far * k * k, 0f, 0f);
                if (k >= 1f) { state = TrainState.None; train.SetActive(false); nextTrain = now + 18f; Restock(); }
                break;
            }
        }
        if (dotMatrix != null && Time.frameCount % 15 == 0) dotMatrix.text = DotMatrix(now);
    }

    string DotMatrix(float now)
    {
        string first;
        switch (state)
        {
            case TrainState.Coming: first = "arr"; break;
            case TrainState.Waiting: first = "doors open"; break;
            case TrainState.Leaving: first = "departing"; break;
            default: first = Mathf.Max(1, Mathf.CeilToInt((nextTrain - now + 7f) / 60f)) + " min"; break;
        }
        return "<align=left>1  " + lineName + "<line-height=0>\n<align=right>" + first + "<line-height=1em>\n" +
               "<align=left>2  " + lineName + "<line-height=0>\n<align=right>" + (state == TrainState.None ? "5 min" : "4 min") + "<line-height=1em>\n" +
               "<align=center>STAND BEHIND THE YELLOW LINE";
    }

    // ------------------------------------------------------------------ BOARD, SKIP, and the ride
    void OnBoard()
    {
        if (busy || state != TrainState.Waiting) return;
        boardGo.SetActive(false);
        tube.Pick(at, "from " + tube.Names[at] + " · where to?", false, (to, minutes) => StartCoroutine(RideTo(to, minutes)));
    }

    void OnSkip()
    {
        if (busy) return;
        tube.Pick(at, "SKIP · straight up to the street", true, (to, minutes) => Leave(to, null));
    }

    IEnumerator RideTo(int to, int minutes)
    {
        if (busy) yield break;
        busy = true;
        boardGo.SetActive(false); skipGo.SetActive(false);
        if (vehicles != null) vehicles.LockControls(true);
        if (hiss != null) sfx.PlayOneShot(hiss, 0.7f);
        // G6b: the ride plays on the Tube map: the train along the route, the pause at a change, ARRIVING
        LundunTubeNetwork.Journey jn = tube.Journey(at, to);
        LundunTubeMap map = tube.Map;
        float total = jn != null && jn.Hops > 0 ? jn.RideSeconds : 3.5f;
        if (map != null && jn != null && jn.Hops > 0) map.ShowRide(jn);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f) { tube.Black(t); if (map != null) map.SetAlpha(t); yield return null; }
        tube.Black(1f); if (map != null) map.SetAlpha(1f);
        if (trainAudio != null) trainAudio.Stop();
        AudioClip ride = LundunTube.MakeRumble(total + 0.4f);
        if (sfx != null && ride != null) sfx.PlayOneShot(ride, 0.9f);
        if (LundunClock.Instance != null) LundunClock.Instance.AddHours(minutes / 60f);
        ClearPeople();
        MoveTo(to);
        Physics.SyncTransforms();
        Fill(true);
        // the train's in, doors open; it pulls out a few seconds after James steps off
        train.SetActive(true);
        train.transform.localPosition = Vector3.zero;
        Doors(true, true);
        state = TrainState.Waiting; waitUntil = Time.time + total + 9f; nextBoard = Time.time + total + 4.5f;   // pulls out once he's off (G9a: and the others are on)
        DoorsOpened(Random.Range(1, 4), total - 0.6f);      // G9a: others get off with him, once the screen's back
        Place(Local(Mathf.Round(Random.Range(-2f, 2f)) * 4.5f, -Drop + 0.05f, -27.6f), 0f);    // off the train, facing the way out
        if (map != null && map.Showing) yield return map.Animate(jn, Mathf.Max(1f, total - 0.7f));
        else
        {
            captionText.text = "<size=60%>NEXT STOP</size>\n" + tube.Names[to];
            captionGo.SetActive(true);
            yield return new WaitForSecondsRealtime(total - 0.7f);
            captionGo.SetActive(false);
        }
        if (sfx != null) sfx.Stop();
        if (vehicles != null) vehicles.LockControls(false);
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.35f) { tube.Black(t); if (map != null) map.SetAlpha(t); yield return null; }
        tube.Black(0f);
        if (map != null) map.Hide();
        LundunMissions.Toast(tube.Names[to] + "\n" + minutes + " min on the Tube");
        busy = false;
    }

    // ------------------------------------------------------------------ where it is, who's there
    void MoveTo(int station)
    {
        at = station;
        Vector3 top = tube.Top(station);
        origin = new Vector3(top.x, SetY, top.z);
        root.transform.position = origin;
        string n = tube.Names[station];
        foreach (TextMeshPro t in nameSigns) if (t != null) t.text = n;
        // G6: the line(s) through here, on the landing's sign and the next-train board
        string lines = LundunTubeNetwork.LinesText(n);
        lineName = lines.Length > 0 ? lines.Replace(" and ", " & ") : "All stations";
        if (linesSign != null) linesSign.text = lines.Length > 0 ? lines : "Trains: All stations";
    }

    Vector3 Local(float x, float y, float z) { return origin + new Vector3(x, y, z); }

    /// A few people about: in the ticket hall, and waiting on the platform (they stand where they're put).
    void Fill(bool arriving)
    {
        ClearPeople();
        Vector4[] spots =
        {
            new Vector4(6.5f, 0f, 12.2f, 200f), new Vector4(8.6f, 0f, 11.3f, 90f), new Vector4(-7.0f, 0f, 3.4f, 40f), new Vector4(2.8f, 0f, 1.8f, 180f),
            new Vector4(-10f, -Drop, -27.5f, 180f), new Vector4(-9.2f, -Drop, -26.0f, 200f), new Vector4(12f, -Drop, -27.3f, 170f),
            new Vector4(21f, -Drop, -25.9f, 150f), new Vector4(-22f, -Drop, -27.0f, 185f), new Vector4(29f, -Drop, -26.8f, 175f),
        };
        int n = Random.Range(5, 8);
        List<int> order = new List<int>(); for (int i = 0; i < spots.Length; i++) order.Add(i);
        for (int i = 0; i < order.Count; i++) { int k = Random.Range(i, order.Count); int tmp = order[i]; order[i] = order[k]; order[k] = tmp; }
        for (int i = 0; i < n; i++)
        {
            Vector4 s = spots[order[i]];
            GameObject g = Person(Local(s.x, s.y, s.z), s.w + Random.Range(-20f, 20f));
            if (g != null && s.y < -1f) walkers.Add(new Walker { go = g, anim = g.GetComponentInChildren<Animator>(), kind = WWait, yaw = s.w });   // G9a
        }
        comersLeft = 0;
    }

    GameObject Person(Vector3 pos, float yaw)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return null;
        GameObject g = pop.NewPerson(null);
        if (g == null) return null;
        NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;          // no wandering, no NavMesh
        NavMeshAgent a = g.GetComponent<NavMeshAgent>(); if (a != null) a.enabled = false;
        g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        g.SetActive(true);
        foreach (Animator an in g.GetComponentsInChildren<Animator>(true)) an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        people.Add(g);
        return g;
    }

    void ClearPeople()
    {
        foreach (GameObject g in people) if (g != null) Destroy(g);
        people.Clear();
        walkers.Clear();
    }

    // ------------------------------------------------------------------ G9a: getting on and off
    // The doors, in the station's metres (the train stopped at x = 0): two per car, on the platform side.
    static float DoorX(int d) { return -24.75f + (d / 2) * 16.5f + (d % 2 == 0 ? -4.5f : 4.5f); }
    const int DoorCount = 8;
    const float DoorZ = TrainZ + 1.3f, FrontZ = -28.3f, AisleZ = -26.0f;
    static int NearestDoor(float x)
    {
        int best = 0; float bd = float.MaxValue;
        for (int d = 0; d < DoorCount; d++) { float dd = Mathf.Abs(DoorX(d) - x); if (dd < bd) { bd = dd; best = d; } }
        return best;
    }

    // up an escalator lane (or down it): along its slope, from the bottom (s = 0) to near the top
    Vector3 EscalatorPoint(float laneX, float s)
    {
        float th = SlopeDeg * Mathf.Deg2Rad, run = Run();
        return Local(laneX, -Drop + 0.05f + s * Mathf.Sin(th), -run + s * Mathf.Cos(th));
    }
    float EscalatorLength() { float run = Run(); return Mathf.Sqrt(run * run + Drop * Drop); }

    void AddWayUp(Walker w, float fromX)
    {
        float lane = Random.value < 0.5f ? -2.6f : 2.6f;
        w.path.Add(Local(Mathf.Clamp(fromX, -3f, 3f), -Drop + 0.05f, AisleZ + Random.Range(-0.4f, 0.4f)));
        w.path.Add(Local(lane, -Drop + 0.05f, -Run() - 1.2f));
        w.path.Add(EscalatorPoint(lane, 0f));
        w.path.Add(EscalatorPoint(lane, EscalatorLength() - 1.5f));
    }

    /// The doors are open: 'off' people step out and head up; then most of those waiting walk to a door and get on.
    void DoorsOpened(int off, float delay = 0f)
    {
        float t = Time.time + delay + DoorTime * 0.8f;
        for (int i = 0; i < off; i++)
        {
            int d = Random.Range(0, DoorCount);
            float x = DoorX(d) + Random.Range(-0.3f, 0.3f);
            GameObject g = Person(Local(x, -Drop + 0.05f, DoorZ - 0.6f), 0f);
            if (g == null) break;
            g.SetActive(false);                                         // until it's their turn to step out
            Walker w = new Walker { go = g, anim = g.GetComponentInChildren<Animator>(), kind = WOff, speed = Random.Range(1.3f, 1.6f), startAt = t };
            w.path.Add(Local(x, -Drop + 0.05f, FrontZ + 0.6f));
            AddWayUp(w, x);
            walkers.Add(w);
            t += Random.Range(0.5f, 0.9f);
        }
        // those waiting: all but now and then one (waiting for a different line)
        float on = t + 0.6f;
        foreach (Walker w in walkers)
        {
            if (w.kind != WWait || w.go == null || Random.value < 0.2f) continue;
            Vector3 l = w.go.transform.position - origin;
            int d = NearestDoor(l.x);
            float x = DoorX(d) + Random.Range(-0.25f, 0.25f);
            w.kind = WBoard; w.speed = Random.Range(1.2f, 1.5f); w.startAt = on + Mathf.Abs(l.x - DoorX(d)) * 0.05f + Random.Range(0f, 0.8f);
            w.path.Clear(); w.i = 0;
            w.path.Add(Local(x, -Drop + 0.05f, FrontZ));
            w.path.Add(Local(x, -Drop + 0.05f, DoorZ - 0.5f));
        }
    }

    /// The doors are shutting: anyone not at a door yet waits for the next one.
    void MissedIt()
    {
        foreach (Walker w in walkers)
        {
            if (w.kind != WBoard || w.go == null) continue;
            Vector3 l = w.go.transform.position - origin;
            if (l.z < FrontZ - 0.3f) continue;                          // already stepping in: let them
            w.kind = WWait; w.path.Clear(); w.i = 0;
        }
    }

    /// The train's gone: a few come down the escalators to wait (up to six on the platform).
    void Restock()
    {
        int waiting = 0;
        foreach (Walker w in walkers) if (w.kind == WWait || w.kind == WCome) waiting++;
        comersLeft = Mathf.Max(0, Mathf.Min(Random.Range(1, 4), 6 - waiting));
        nextComer = Time.time + Random.Range(1f, 4f);
    }

    void Comer()
    {
        float lane = Random.value < 0.5f ? -2.6f : 2.6f;
        Vector3 from = EscalatorPoint(lane, EscalatorLength() - 1.5f);
        GameObject g = Person(from, 180f);
        if (g == null) { comersLeft = 0; return; }
        Walker w = new Walker { go = g, anim = g.GetComponentInChildren<Animator>(), kind = WCome, speed = Random.Range(1.2f, 1.5f), startAt = Time.time, yaw = 180f + Random.Range(-20f, 20f) };
        w.path.Add(EscalatorPoint(lane, 0f));
        w.path.Add(Local(lane, -Drop + 0.05f, -Run() - 1.2f));
        float x = Random.Range(-30f, 30f);
        w.path.Add(Local(Mathf.Clamp(x, -3f, 3f), -Drop + 0.05f, AisleZ));
        w.path.Add(Local(x, -Drop + 0.05f, Random.Range(-27.6f, -25.9f)));
        walkers.Add(w);
    }

    void Walk()
    {
        if (comersLeft > 0 && Time.time >= nextComer && state != TrainState.Waiting)
        {
            comersLeft--; nextComer = Time.time + Random.Range(3f, 7f);
            Comer();
        }
        for (int n = walkers.Count - 1; n >= 0; n--)
        {
            Walker w = walkers[n];
            if (w.go == null) { walkers.RemoveAt(n); continue; }
            if (w.path.Count == 0 || w.i >= w.path.Count || Time.time < w.startAt)
            {
                if (w.go.activeInHierarchy) LundunAnim.Float(w.anim, "Speed", 0f, 0.1f);
                continue;
            }
            if (!w.go.activeSelf) w.go.SetActive(true);
            Vector3 pos = w.go.transform.position, to = w.path[w.i];
            Vector3 d = to - pos;
            float step = w.speed * Time.deltaTime;
            if (d.magnitude <= step)
            {
                w.go.transform.position = to;
                w.i++;
                if (w.i >= w.path.Count) Arrived(w, n);
                continue;
            }
            w.go.transform.position = pos + d.normalized * step;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            if (flat.sqrMagnitude > 0.0001f)
                w.go.transform.rotation = Quaternion.RotateTowards(w.go.transform.rotation, Quaternion.LookRotation(flat), 360f * Time.deltaTime);
            LundunAnim.Float(w.anim, "Speed", w.speed, 0.1f);
        }
    }

    void Arrived(Walker w, int n)
    {
        if (w.kind == WBoard || w.kind == WOff)
        {
            // on the train, or up the escalator and away
            people.Remove(w.go);
            Destroy(w.go);
            walkers.RemoveAt(n);
            return;
        }
        // come down to wait: stand facing the track
        w.kind = WWait; w.path.Clear(); w.i = 0;
        w.go.transform.rotation = Quaternion.Euler(0f, w.yaw, 0f);
        LundunAnim.Float(w.anim, "Speed", 0f);
    }

    void Place(Vector3 pos, float yaw)
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.Body == null) return;
        CharacterController cc = ph.Body.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        ph.Body.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        if (cc != null) cc.enabled = true;
        CameraFollow cf = FindAnyObjectByType<CameraFollow>();
        if (cf != null) cf.Nudge(Mathf.DeltaAngle(cf.Yaw, yaw), 0f);        // look the way he's facing
    }

    // ================================================================== building it (once)
    LundunMeshKit hall, esc, plat, trainKit, shutKit, openKit;
    Material[] mats;

    void Build()
    {
        root = new GameObject("LundunTubeStation");
        root.transform.SetParent(transform, false);
        colliders = new GameObject("Colliders").transform;
        colliders.SetParent(root.transform, false);
        mats = Materials();
        hall = new LundunMeshKit(MCount); esc = new LundunMeshKit(MCount); plat = new LundunMeshKit(MCount);
        trainKit = new LundunMeshKit(MCount); shutKit = new LundunMeshKit(MCount); openKit = new LundunMeshKit(MCount);

        BuildHall();
        BuildEscalators();
        BuildPlatform();
        BuildTrain();

        MakeRenderer(root.transform, hall, "Hall");
        MakeRenderer(root.transform, esc, "Escalators");
        MakeRenderer(root.transform, plat, "Platform");
        train = new GameObject("Train");
        train.transform.SetParent(root.transform, false);
        MakeRenderer(train.transform, trainKit, "TrainBody");
        doorsShut = MakeRenderer(train.transform, shutKit, "DoorsLeft");          // G9a: both always on, sliding
        doorsOpen = MakeRenderer(train.transform, openKit, "DoorsRight");
        trainAudio = train.AddComponent<AudioSource>();
        trainAudio.spatialBlend = 1f; trainAudio.minDistance = 6f; trainAudio.maxDistance = 90f; trainAudio.volume = 0.9f;
        trainAudio.playOnAwake = false;
        sfx = root.AddComponent<AudioSource>();
        sfx.spatialBlend = 0f; sfx.playOnAwake = false;
        rumble = LundunTube.MakeRumble(7.5f);
        beep = Tone(1046f, 0.12f, 0.35f);
        hiss = Noise(0.7f, 0.25f);

        // light: a few warm-white lamps in each part (and everything glows a little, so it's never pitch dark)
        foreach (Vector3 p in new[] { new Vector3(-5f, 3.9f, 12f), new Vector3(5f, 3.9f, 12f), new Vector3(-5f, 3.9f, 3.5f), new Vector3(5f, 3.9f, 3.5f) }) Lamp(p, 11f, 2.2f);
        float run = Run();
        foreach (float s in new[] { 0.2f, 0.5f, 0.8f }) Lamp(new Vector3(0f, -Drop * (1f - s) + 3.2f, -run * (1f - s)), 10f, 2f);
        Lamp(new Vector3(0f, -Drop + 3.6f, -22.6f), 9f, 2f);
        for (float x = -30f; x <= 30f; x += 12f) Lamp(new Vector3(x, -4.3f, -27.4f), 13f, 2.4f);

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
        ResetTrain();
        root.SetActive(false);
    }

    static float Run() { return Drop / Mathf.Tan(SlopeDeg * Mathf.Deg2Rad); }

    GameObject MakeRenderer(Transform parent, LundunMeshKit k, string name)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = k.Build("Tube_" + name);
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return g;
    }

    static Material[] Materials()
    {
        Color cream = new Color(0.88f, 0.84f, 0.72f), navy = new Color(0.106f, 0.153f, 0.4f), yellow = new Color(1f, 0.78f, 0.17f);
        Material[] m = new Material[MCount];
        m[MFloor] = LundunMeshKit.Mat("TubeFloor", new Color(0.42f, 0.4f, 0.37f), 0.35f, new Color(0.42f, 0.4f, 0.37f) * 0.12f);
        m[MTile] = LundunMeshKit.Mat("TubeTile", cream, 0.7f, cream * 0.2f);
        m[MNavy] = LundunMeshKit.Mat("TubeNavy", navy, 0.4f, navy * 0.35f);
        m[MYellow] = LundunMeshKit.Mat("TubeYellow", yellow, 0.4f, yellow * 0.3f);
        m[MSteel] = LundunMeshKit.Mat("TubeSteel", new Color(0.62f, 0.65f, 0.68f), 0.75f, new Color(0.05f, 0.05f, 0.06f));
        m[MBlack] = LundunMeshKit.Mat("TubeBlack", new Color(0.04f, 0.04f, 0.045f), 0.3f, Color.black);
        m[MCeil] = LundunMeshKit.Mat("TubeCeiling", new Color(0.82f, 0.8f, 0.74f), 0.1f, new Color(0.82f, 0.8f, 0.74f) * 0.15f);
        m[MStrip] = LundunMeshKit.Mat("TubeLightStrip", Color.white, 0.5f, new Color(1f, 0.96f, 0.88f) * 2.2f);
        m[MRed] = LundunMeshKit.Mat("TubeRed", new Color(0.8f, 0.11f, 0.12f), 0.55f, new Color(0.8f, 0.11f, 0.12f) * 0.15f);
        m[MWhite] = LundunMeshKit.Mat("TubeWhite", new Color(0.9f, 0.9f, 0.91f), 0.6f, new Color(0.9f, 0.9f, 0.91f) * 0.12f);
        m[MGlass] = LundunMeshKit.Mat("TubeWindow", new Color(0.25f, 0.24f, 0.2f), 0.9f, new Color(0.75f, 0.68f, 0.5f) * 0.55f);
        m[MWood] = LundunMeshKit.Mat("TubeBench", new Color(0.33f, 0.2f, 0.12f), 0.4f, Color.black);
        m[MLamp] = LundunMeshKit.Mat("TubeLamp", new Color(1f, 0.95f, 0.85f), 0.5f, new Color(1f, 0.9f, 0.7f) * 2.5f);
        m[MScreen] = LundunMeshKit.Mat("TubeScreen", new Color(0.1f, 0.4f, 0.95f), 0.8f, new Color(0.1f, 0.4f, 0.95f) * 1.4f);
        m[MGreen] = LundunMeshKit.Mat("TubeGo", new Color(0.2f, 1f, 0.4f), 0.5f, new Color(0.2f, 1f, 0.35f) * 2f);
        m[MBallast] = LundunMeshKit.Mat("TubeTrack", new Color(0.2f, 0.17f, 0.15f), 0.1f, Color.black);
        m[MDaylight] = LundunMeshKit.Mat("TubeDaylight", Color.white, 0.2f, new Color(1f, 0.98f, 0.92f) * 1.6f);
        Color[] posters = { new Color(0.91f, 0.31f, 0.22f), new Color(0.16f, 0.62f, 0.56f), new Color(0.42f, 0.3f, 0.58f), new Color(0.96f, 0.64f, 0.38f) };
        for (int i = 0; i < 4; i++) m[MPoster0 + i] = LundunMeshKit.Mat("TubePoster" + i, posters[i], 0.6f, posters[i] * 0.35f);
        m[MDoorway] = LundunMeshKit.Mat("TubeDoorway", new Color(0.86f, 0.83f, 0.72f), 0.2f, new Color(1f, 0.95f, 0.8f) * 0.9f);   // G9a: a lit carriage
        return m;
    }

    void Solid(Vector3 c, Vector3 size) { Solid(c, size, Quaternion.identity); }
    void Solid(Vector3 c, Vector3 size, Quaternion rot)
    {
        GameObject g = new GameObject("Solid");
        g.transform.SetParent(colliders, false);
        g.transform.localPosition = c; g.transform.localRotation = rot;
        g.AddComponent<BoxCollider>().size = size;
    }

    void Lamp(Vector3 p, float range, float intensity)
    {
        GameObject g = new GameObject("TubeLight");
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = p;
        Light li = g.AddComponent<Light>();
        li.type = LightType.Point; li.range = range; li.intensity = intensity; li.color = new Color(1f, 0.95f, 0.86f);
        li.shadows = LightShadows.None;
    }

    // ------------------------------------------------------------------ ticket hall (floor y 0, x -10…10, z 0…16)
    void BuildHall()
    {
        LundunMeshKit k = hall;
        k.Box(MFloor, new Vector3(0f, -0.1f, 8f), new Vector3(20.4f, 0.2f, 16.4f));
        Solid(new Vector3(0f, -0.1f, 8f), new Vector3(20.4f, 0.2f, 16.4f));
        k.Box(MCeil, new Vector3(0f, HallH + 0.1f, 8f), new Vector3(20.4f, 0.2f, 16.4f));
        Solid(new Vector3(0f, HallH + 0.1f, 8f), new Vector3(20.4f, 0.2f, 16.4f));
        for (float z = 1.5f; z < 15f; z += 3f) foreach (float x in new[] { -6f, 0f, 6f }) k.Box(MStrip, new Vector3(x, HallH - 0.03f, z), new Vector3(3.2f, 0.05f, 0.25f));
        // side walls, with the navy band
        foreach (int s in new[] { -1, 1 })
        {
            TiledWall(k, new Vector3(s * 10.1f, HallH / 2f, 8f), new Vector3(0.2f, HallH, 16.4f), new Vector3(-s, 0f, 0f));
        }
        // back wall: the way out (steps up to the street) at x -8…-2
        TiledWall(k, new Vector3(-9f, HallH / 2f, 16.1f), new Vector3(2f, HallH, 0.2f), Vector3.back);
        TiledWall(k, new Vector3(4f, HallH / 2f, 16.1f), new Vector3(12f, HallH, 0.2f), Vector3.back);
        // front wall: the escalators go down through the middle (x -4.3…4.3)
        TiledWall(k, new Vector3(-7.15f, HallH / 2f, -0.1f), new Vector3(5.7f, HallH, 0.2f), Vector3.forward);
        TiledWall(k, new Vector3(7.15f, HallH / 2f, -0.1f), new Vector3(5.7f, HallH, 0.2f), Vector3.forward);

        // the gate line: 7 cabinets, 6 lanes, fences either side
        for (int i = 0; i < 7; i++)
        {
            float x = -6f + i * 2f;
            k.Box(MSteel, new Vector3(x, 0.525f, GateZ), new Vector3(0.3f, 1.05f, 1.6f));
            k.Box(MBlack, new Vector3(x, 1.07f, GateZ), new Vector3(0.32f, 0.04f, 1.62f));
            k.Box(MYellow, new Vector3(x, 1.1f, GateZ + 0.5f), new Vector3(0.24f, 0.02f, 0.24f));
            k.Box(MYellow, new Vector3(x, 1.1f, GateZ - 0.5f), new Vector3(0.24f, 0.02f, 0.24f));
            k.Box(MGreen, new Vector3(x, 0.95f, GateZ + 0.81f), new Vector3(0.2f, 0.05f, 0.01f));
            k.Box(MGreen, new Vector3(x, 0.95f, GateZ - 0.81f), new Vector3(0.2f, 0.05f, 0.01f));
            Solid(new Vector3(x, 0.6f, GateZ), new Vector3(0.32f, 1.2f, 1.6f));
        }
        foreach (int s in new[] { -1, 1 })
        {
            k.Box(MSteel, new Vector3(s * 8.075f, 0.55f, GateZ), new Vector3(3.85f, 1.1f, 0.06f));
            k.Box(MBlack, new Vector3(s * 8.075f, 1.12f, GateZ), new Vector3(3.85f, 0.05f, 0.1f));
            Solid(new Vector3(s * 8.075f, 0.9f, GateZ), new Vector3(3.85f, 1.8f, 0.2f));
        }
        Material paddle = mats[MYellow];
        for (int i = 0; i < 6; i++)
        {
            float lane = Lanes[i];
            Gate g = new Gate();
            g.ax = lane - 0.31f; g.bx = lane + 0.31f;
            g.a = Paddle(new Vector3(g.ax, 0.62f, GateZ), paddle);
            g.b = Paddle(new Vector3(g.bx, 0.62f, GateZ), paddle);
            GameObject blk = new GameObject("GateShut");
            blk.transform.SetParent(colliders, false);
            blk.transform.localPosition = new Vector3(lane, 0.8f, GateZ);
            BoxCollider bc = blk.AddComponent<BoxCollider>(); bc.size = new Vector3(1.7f, 1.6f, 0.2f);
            g.block = bc;
            g.ax = g.a.localPosition.x; g.bx = g.b.localPosition.x;
            gates.Add(g);
        }

        // ticket machines (the street side), the staff booth, the line map (the trains side)
        foreach (float z in new[] { 10.5f, 12f, 13.5f })
        {
            k.Box(MNavy, new Vector3(9.65f, 0.95f, z), new Vector3(0.7f, 1.9f, 1.1f));
            k.Box(MScreen, new Vector3(9.29f, 1.35f, z), new Vector3(0.02f, 0.45f, 0.6f));
            Solid(new Vector3(9.65f, 0.95f, z), new Vector3(0.7f, 1.9f, 1.1f));
        }
        Words(root.transform, "TICKETS", new Vector3(9.27f, 1.95f, 12f), Quaternion.LookRotation(Vector3.right), new Vector2(3.2f, 0.3f), 2.4f, Color.white, TextAlignmentOptions.Center);
        k.Box(MWhite, new Vector3(-8.6f, 1.2f, 3f), new Vector3(2.4f, 2.4f, 1.8f));
        k.Box(MGlass, new Vector3(-7.38f, 1.6f, 3f), new Vector3(0.04f, 0.9f, 1.5f));
        k.Box(MNavy, new Vector3(-8.6f, 2.5f, 3f), new Vector3(2.45f, 0.2f, 1.85f));
        Solid(new Vector3(-8.6f, 1.2f, 3f), new Vector3(2.4f, 2.4f, 1.8f));
        Words(root.transform, "HELP", new Vector3(-7.33f, 2.5f, 3f), Quaternion.LookRotation(Vector3.left), new Vector2(1.6f, 0.18f), 1.6f, Color.white, TextAlignmentOptions.Center);
        k.Box(MWhite, new Vector3(9.97f, 2.2f, 3.5f), new Vector3(0.04f, 1.7f, 2.8f));
        k.Box(MYellow, new Vector3(9.94f, 2.45f, 3.5f), new Vector3(0.02f, 0.07f, 2.3f));
        k.Box(MRed, new Vector3(9.94f, 2.05f, 3.4f), new Vector3(0.02f, 0.07f, 2.0f));
        k.Box(MScreen, new Vector3(9.94f, 2.2f, 3.6f), new Vector3(0.02f, 1.1f, 0.07f));
        Words(root.transform, "LUNDUN UNDERGROUND", new Vector3(9.93f, 2.88f, 3.5f), Quaternion.LookRotation(Vector3.right), new Vector2(2.6f, 0.2f), 1.6f, new Color(0.106f, 0.153f, 0.4f), TextAlignmentOptions.Center);

        // signs: over the gates (both ways), over the way out, and the station's name over the escalators
        k.Box(MBlack, new Vector3(-1.5f, 4.15f, GateZ), new Vector3(0.04f, 0.9f, 0.04f));
        k.Box(MBlack, new Vector3(1.5f, 4.15f, GateZ), new Vector3(0.04f, 0.9f, 0.04f));
        k.Box(MNavy, new Vector3(0f, 3.55f, GateZ), new Vector3(5.4f, 0.6f, 0.08f));
        Words(root.transform, "To all trains", new Vector3(0f, 3.55f, GateZ + 0.05f), Quaternion.LookRotation(Vector3.back), new Vector2(4.8f, 0.42f), 3.2f, Color.white, TextAlignmentOptions.Center);
        Words(root.transform, "Way out", new Vector3(0f, 3.55f, GateZ - 0.05f), Quaternion.LookRotation(Vector3.forward), new Vector2(4.8f, 0.42f), 3.2f, Color.white, TextAlignmentOptions.Center);
        k.Box(MNavy, new Vector3(-5f, 3.95f, 15.95f), new Vector3(4.2f, 0.55f, 0.06f));
        Words(root.transform, "WAY OUT", new Vector3(-5f, 3.95f, 15.9f), Quaternion.LookRotation(Vector3.forward), new Vector2(3.8f, 0.4f), 3f, Color.white, TextAlignmentOptions.Center);
        Board(k, new Vector3(0f, 3.85f, 0.07f), Vector3.forward, 6.6f, 0.9f);

        // the steps up to the street (they go on up out of sight, to daylight); walking up them is the way out
        const float rise = 0.18f, tread = 0.26f;
        for (int i = 0; i < 34; i++)
        {
            float top = (i + 1) * rise, z0 = 10.8f + i * tread;
            k.Box(i % 2 == 0 ? MFloor : MSteel, new Vector3(-5f, top - 0.6f, z0 + tread / 2f), new Vector3(6f, 1.2f, tread));
        }
        float stairsLen = 34 * tread, stairsUp = 34 * rise;
        float ang = Mathf.Atan2(stairsUp, stairsLen) * Mathf.Rad2Deg;
        Quaternion sr = Quaternion.Euler(-ang, 0f, 0f);
        Vector3 sMid = new Vector3(-5f, stairsUp / 2f, 10.8f + stairsLen / 2f);
        Solid(sMid - (sr * Vector3.up) * 0.12f, new Vector3(6f, 0.2f, Mathf.Sqrt(stairsLen * stairsLen + stairsUp * stairsUp) + 0.4f), sr);
        foreach (int s in new[] { -1, 1 })
        {
            float x = -5f + s * 3.05f;
            k.Box(MSteel, sMid + new Vector3(s * 3.0f, 0.95f, 0f), new Vector3(0.06f, 0.06f, stairsLen * 1.1f), sr);   // handrails
            k.Box(MTile, new Vector3(x + s * 0.05f, 5f, 10.8f + stairsLen / 2f + 2f), new Vector3(0.12f, 10f, stairsLen));   // the stairwell's sides
        }
        k.Box(MTile, new Vector3(-5f, 8.5f, 19.75f), new Vector3(6.4f, 10f, 0.2f));
        k.Box(MDaylight, new Vector3(-5f, 7.6f, 19.6f), new Vector3(5.6f, 3.2f, 0.05f));        // daylight at the top
        k.Box(MCeil, new Vector3(-5f, 9.6f, 18f), new Vector3(6.4f, 0.2f, 4f));
    }

    Transform Paddle(Vector3 at, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        g.name = "GatePaddle";
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = at;
        g.transform.localScale = new Vector3(0.6f, 0.55f, 0.05f);
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        return g.transform;
    }

    /// A tiled wall with the navy band at shoulder height on its inner face.
    static void TiledWall(LundunMeshKit k, Vector3 c, Vector3 size, Vector3 inward)
    {
        k.Box(MTile, c, size);
        bool alongX = Mathf.Abs(inward.z) > 0.5f;
        Vector3 band = alongX ? new Vector3(size.x, 0.2f, 0.04f) : new Vector3(0.04f, 0.2f, size.z);
        Vector3 face = c + inward * (Mathf.Abs(Vector3.Dot(size, new Vector3(Mathf.Abs(inward.x), 0f, Mathf.Abs(inward.z)))) / 2f + 0.02f);
        k.Box(MNavy, new Vector3(face.x, 2.45f + (c.y - size.y / 2f), face.z), band);
    }

    /// A station name board facing `facing` (±z): navy, yellow lines, our diamond, the name (re-signed each stop).
    void Board(LundunMeshKit k, Vector3 c, Vector3 facing, float w, float h)
    {
        Vector3 left = Vector3.Cross(Vector3.up, facing);           // the viewer's left
        k.Box(MNavy, c, new Vector3(w, h, 0.08f));
        k.Box(MYellow, c + facing * 0.045f + Vector3.up * (h / 2f - h * 0.13f), new Vector3(w - 0.1f, h * 0.06f, 0.01f));
        k.Box(MYellow, c + facing * 0.045f - Vector3.up * (h / 2f - h * 0.13f), new Vector3(w - 0.1f, h * 0.06f, 0.01f));
        Vector3 lc = c + left * (w / 2f - h * 0.55f) + facing * 0.05f;
        k.Box(MWhite, lc, new Vector3(h * 0.78f, h * 0.78f, 0.01f));
        k.Box(MNavy, lc + facing * 0.006f, new Vector3(h * 0.7f, h * 0.7f, 0.01f));
        k.Box(MYellow, lc + facing * 0.012f, new Vector3(h * 0.4f, h * 0.4f, 0.01f), Quaternion.Euler(0f, 0f, 45f));
        Words(root.transform, "U", lc + facing * 0.02f, Quaternion.LookRotation(-facing), new Vector2(h * 0.3f, h * 0.3f), h * 3f, new Color(0.106f, 0.153f, 0.4f), TextAlignmentOptions.Center);
        TextMeshPro t = Words(root.transform, "", c + facing * 0.06f - left * (h * 0.5f), Quaternion.LookRotation(-facing),
                              new Vector2(w - h * 1.6f, h * 0.55f), h * 6f, Color.white, TextAlignmentOptions.Left);
        nameSigns.Add(t);
    }

    static TextMeshPro Words(Transform parent, string text, Vector3 localPos, Quaternion rot, Vector2 box, float size, Color colour, TextAlignmentOptions align)
    {
        GameObject g = new GameObject("Words", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = rot;
        ((RectTransform)g.transform).sizeDelta = box;
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = text; t.color = colour; t.fontStyle = FontStyles.Bold;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = size; t.fontSize = size;
        t.richText = true;
        MeshRenderer r = g.GetComponent<MeshRenderer>();
        if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        return t;
    }

    // ------------------------------------------------------------------ the escalators: three, down 9 m (walk them)
    void BuildEscalators()
    {
        LundunMeshKit k = esc;
        float run = Run(), len = Mathf.Sqrt(run * run + Drop * Drop), th = SlopeDeg * Mathf.Deg2Rad;
        Quaternion rot = Quaternion.Euler(-SlopeDeg, 0f, 0f);          // forward rises towards the hall (+z)
        Vector3 nrm = rot * Vector3.up;
        Vector3 mid = new Vector3(0f, -Drop / 2f, -run / 2f);
        System.Func<float, Vector3> along = s => new Vector3(0f, -Drop + s * Mathf.Sin(th), -run + s * Mathf.Cos(th));   // s from the bottom

        Solid(mid - nrm * 0.1f, new Vector3(8.4f, 0.2f, len + 0.6f), rot);                     // what you walk on
        k.Box(MSteel, mid - nrm * 0.05f, new Vector3(8.7f, 0.1f, len + 0.2f), rot);              // the deck under it all
        foreach (float x in new[] { -2.6f, 0f, 2.6f })
        {
            k.Box(MBlack, mid + nrm * 0.02f + new Vector3(x, 0f, 0f), new Vector3(1.3f, 0.04f, len), rot);
            for (float s = 0.3f; s < len; s += 0.42f) k.Box(MSteel, along(s) + nrm * 0.045f + new Vector3(x, 0f, 0f), new Vector3(1.3f, 0.012f, 0.04f), rot);
            foreach (int e in new[] { -1, 1 }) k.Box(MYellow, mid + nrm * 0.046f + new Vector3(x + e * 0.6f, 0f, 0f), new Vector3(0.04f, 0.012f, len), rot);
        }
        foreach (int s in new[] { -1, 1 })
        {
            // the two dividers, with their lamps
            Vector3 d = new Vector3(s * 1.3f, 0f, 0f);
            k.Box(MSteel, mid + nrm * 0.5f + d, new Vector3(0.5f, 1f, len), rot);
            k.Box(MBlack, mid + nrm * 1.02f + d, new Vector3(0.56f, 0.06f, len), rot);
            Solid(mid + nrm * 0.6f + d, new Vector3(0.5f, 1.2f, len), rot);
            for (float a = 2f; a < len - 1f; a += 3f)
            {
                Vector3 b = along(a) + d;
                float deck = b.y + 1.05f / Mathf.Cos(th);
                k.Box(MBlack, new Vector3(b.x, deck + 0.3f, b.z), new Vector3(0.07f, 0.6f, 0.07f));
                k.Cyl(MLamp, new Vector3(b.x, deck + 0.75f, b.z), 0.09f, 0.32f);
            }
            // the outer balustrades and handrails
            Vector3 o = new Vector3(s * 3.95f, 0f, 0f);
            k.Box(MSteel, mid + nrm * 0.5f + o, new Vector3(0.12f, 1f, len), rot);
            k.Box(MBlack, mid + nrm * 1.03f + o, new Vector3(0.16f, 0.07f, len), rot);
            Solid(mid + nrm * 0.6f + o, new Vector3(0.12f, 1.2f, len), rot);
            // the shaft's walls (upright), a navy band along the slope, posters
            k.Box(MTile, new Vector3(s * 4.4f, (HallH - Drop) / 2f, -run / 2f), new Vector3(0.2f, HallH + Drop + 0.4f, run + 0.4f));
            Solid(new Vector3(s * 4.4f, (HallH - Drop) / 2f, -run / 2f), new Vector3(0.2f, HallH + Drop + 0.4f, run + 0.4f));
            k.Box(MNavy, mid + Vector3.up * 2.4f + new Vector3(s * 4.28f, 0f, 0f), new Vector3(0.04f, 0.18f, len), rot);
            int p = 0;
            for (float a = 3f; a < len - 2.5f; a += 3.4f, p++)
            {
                Vector3 b = along(a);
                Vector3 pc = new Vector3(s * 4.27f, b.y + 1.75f, b.z);
                int m = MPoster0 + (p + (s > 0 ? 2 : 0)) % 4;
                k.Box(m, pc, new Vector3(0.04f, 1.4f, 1.0f));
                k.Box(MWhite, pc + new Vector3(0f, 0.66f, 0f), new Vector3(0.045f, 0.08f, 1.0f));
                string[] words = { "LUNDUN ZOO", "RIVERBOAT", "GALLERY LATE", "HOT PIES", "THE WHEEL", "MIND HOW YOU GO" };
                Words(root.transform, words[(p * 2 + (s > 0 ? 1 : 0)) % words.Length], pc + new Vector3(-s * 0.03f, -0.35f, 0f),
                      Quaternion.LookRotation(new Vector3(s, 0f, 0f)), new Vector2(0.9f, 0.2f), 1.4f, Color.white, TextAlignmentOptions.Center);
            }
        }
        // the roof: 4.6 m over the steps all the way, from the hall's ceiling to the landing's
        k.Box(MCeil, mid + Vector3.up * HallH + nrm * 0.1f, new Vector3(8.8f, 0.2f, len + 0.3f), rot);
        k.Box(MStrip, mid + Vector3.up * HallH - nrm * 0.03f, new Vector3(0.3f, 0.04f, len - 1f), rot);
        Words(root.transform, "Stand on the right", new Vector3(0f, 3.2f, -1.6f), Quaternion.LookRotation(Vector3.back), new Vector2(2.6f, 0.3f), 2.2f, Color.white, TextAlignmentOptions.Center);
        k.Box(MNavy, new Vector3(0f, 3.2f, -1.55f), new Vector3(2.9f, 0.42f, 0.06f));

        // the bottom landing, through to the platform
        // floor z -19.9…-23.9 (the platform's floor takes over), walls to -24.5 (the tunnel's curve), roof to -25.6
        float z0 = -run + 0.3f, lz = (-run + LandZ) / 2f;
        k.Box(MFloor, new Vector3(0f, -Drop - 0.1f, (z0 - 23.9f) / 2f), new Vector3(8.8f, 0.2f, z0 + 23.9f));
        Solid(new Vector3(0f, -Drop - 0.1f, (z0 - 23.9f) / 2f), new Vector3(8.8f, 0.2f, z0 + 23.9f));
        k.Box(MCeil, new Vector3(0f, -Drop + HallH + 0.1f, (z0 - 25.6f) / 2f), new Vector3(8.8f, 0.2f, z0 + 25.6f));
        foreach (int s in new[] { -1, 1 })
        {
            TiledWall(k, new Vector3(s * 4.4f, -Drop + HallH / 2f, (z0 - 24.5f) / 2f), new Vector3(0.2f, HallH, z0 + 24.5f), new Vector3(-s, 0f, 0f));
            Solid(new Vector3(s * 4.4f, -Drop + HallH / 2f, (z0 - 24.5f) / 2f), new Vector3(0.2f, HallH, z0 + 24.5f));
        }
        foreach (int s in new[] { -1, 1 })                                                     // close the corners where the way through meets the tunnel's curve
        {
            k.Box(MTile, new Vector3(s * 4.4f, -5.3f, -25.05f), new Vector3(0.2f, 2.2f, 1.3f));
            Solid(new Vector3(s * 4.4f, -5.3f, -25.05f), new Vector3(0.2f, 2.2f, 1.3f));
        }
        k.Box(MNavy, new Vector3(0f, -Drop + 3.6f, lz), new Vector3(4.6f, 0.55f, 0.08f));
        linesSign = Words(root.transform, "Trains: All stations", new Vector3(0f, -Drop + 3.6f, lz + 0.05f), Quaternion.LookRotation(Vector3.back), new Vector2(4.2f, 0.38f), 2.8f, Color.white, TextAlignmentOptions.Center);
        Words(root.transform, "Way out", new Vector3(0f, -Drop + 3.6f, lz - 0.05f), Quaternion.LookRotation(Vector3.forward), new Vector2(4.2f, 0.38f), 2.8f, Color.white, TextAlignmentOptions.Center);
    }

    // ------------------------------------------------------------------ the platform: a round tiled tunnel along x
    void BuildPlatform()
    {
        LundunMeshKit k = plat;
        float floorY = -Drop;
        // the tunnel's curve, from the platform's back wall over the top to the far side of the track
        float a0 = Mathf.Asin((floorY - TunY) / TunR) * Mathf.Rad2Deg;                       // about -18°
        float a1 = 180f - Mathf.Asin((TrackY - TunY) / TunR) * Mathf.Rad2Deg;               // about 213°
        const int N = 26;
        float[][] segs = { new[] { -PlatX, -4.4f }, new[] { -4.4f, 4.4f }, new[] { 4.4f, PlatX } };
        for (int si = 0; si < segs.Length; si++)
        {
            float x0 = segs[si][0], x1 = segs[si][1];
            for (int i = 0; i < N; i++)
            {
                float pa = Mathf.Lerp(a0, a1, i / (float)N), pb = Mathf.Lerp(a0, a1, (i + 1) / (float)N), pm = (pa + pb) / 2f;
                if (si == 1 && pm < 46f) continue;                                             // the way through from the escalators
                float chord = 2f * TunR * Mathf.Sin((pb - pa) * 0.5f * Mathf.Deg2Rad);
                float r = pm * Mathf.Deg2Rad;
                Vector3 c = new Vector3((x0 + x1) / 2f, TunY + (TunR + 0.1f) * Mathf.Sin(r), TunZ + (TunR + 0.1f) * Mathf.Cos(r));
                k.Box(MTile, c, new Vector3(x1 - x0, 0.2f, chord * 1.06f), Quaternion.Euler(-(pm + 90f), 0f, 0f));
            }
        }
        // the ends: dark tunnel mouths
        foreach (int s in new[] { -1, 1 })
        {
            k.Box(MBlack, new Vector3(s * (PlatX + 0.1f), TunY, TunZ), new Vector3(0.2f, 2f * TunR + 1.5f, 2f * TunR + 1.5f));
            Solid(new Vector3(s * (PlatX + 0.1f), TunY, TunZ), new Vector3(0.2f, 2f * TunR + 1.5f, 2f * TunR + 1.5f));
        }
        // floor, edge, yellow line, the drop to the track, the track
        k.Box(MFloor, new Vector3(0f, floorY - 0.1f, -26.4f), new Vector3(2f * PlatX, 0.2f, 5.2f));
        Solid(new Vector3(0f, floorY - 0.1f, -26.4f), new Vector3(2f * PlatX, 0.2f, 5.2f));
        k.Box(MWhite, new Vector3(0f, floorY + 0.005f, EdgeZ + 0.25f), new Vector3(2f * PlatX, 0.02f, 0.5f));
        k.Box(MYellow, new Vector3(0f, floorY + 0.012f, EdgeZ + 0.7f), new Vector3(2f * PlatX, 0.02f, 0.14f));
        k.Box(MBlack, new Vector3(0f, (floorY + TrackY) / 2f, EdgeZ + 0.05f), new Vector3(2f * PlatX, floorY - TrackY, 0.1f));
        k.Box(MBallast, new Vector3(0f, TrackY - 0.1f, (EdgeZ - 32.7f) / 2f), new Vector3(2f * PlatX, 0.2f, 3.8f));
        foreach (float rz in new[] { TrainZ - 0.72f, TrainZ + 0.72f }) k.Box(MSteel, new Vector3(0f, TrackY + 0.07f, rz), new Vector3(2f * PlatX, 0.12f, 0.07f));
        Solid(new Vector3(0f, floorY + 1.2f, EdgeZ - 0.05f), new Vector3(2f * PlatX, 2.4f, 0.1f));            // nobody goes on the track
        foreach (int s in new[] { -1, 1 }) Solid(new Vector3(s * (PlatX + 4.4f) / 2f, floorY + 2f, -24.25f), new Vector3(PlatX - 4.4f, 4f, 0.1f));
        // MIND THE GAP
        foreach (float x in new[] { -24f, -8f, 8f, 24f })
            Words(root.transform, "MIND THE GAP", new Vector3(x, floorY + 0.03f, EdgeZ + 0.45f), Quaternion.Euler(90f, 180f, 0f), new Vector2(3f, 0.32f), 2.4f, Color.white, TextAlignmentOptions.Center);
        // name boards across the track and on the back wall; posters between; benches
        foreach (float x in new[] { -24f, -8f, 8f, 24f }) Board(k, new Vector3(x, -7.0f, -33.2f), Vector3.forward, 4.6f, 0.75f);
        foreach (float x in new[] { -20f, 20f }) Board(k, new Vector3(x, -7.0f, -24.4f), Vector3.back, 4.2f, 0.7f);
        int pi = 0;
        foreach (float x in new[] { -16f, 0f, 16f }) k.Box(MPoster0 + (pi++ % 4), new Vector3(x, -7.4f, -33.22f), new Vector3(1.6f, 1.2f, 0.04f));
        foreach (float x in new[] { -28f, -12f, 12f, 28f }) k.Box(MPoster0 + (pi++ % 4), new Vector3(x, -7.4f, -24.38f), new Vector3(1.6f, 1.2f, 0.04f));
        foreach (float x in new[] { -26f, -8f, 8f, 26f })
        {
            k.Box(MWood, new Vector3(x, floorY + 0.45f, -24.95f), new Vector3(2.2f, 0.08f, 0.5f));
            k.Box(MWood, new Vector3(x, floorY + 0.75f, -24.68f), new Vector3(2.2f, 0.5f, 0.06f));
            foreach (int e in new[] { -1, 1 }) k.Box(MBlack, new Vector3(x + e * 0.95f, floorY + 0.22f, -24.95f), new Vector3(0.06f, 0.44f, 0.45f));
            Solid(new Vector3(x, floorY + 0.45f, -24.85f), new Vector3(2.2f, 0.9f, 0.7f));
        }
        // lights along the roof
        k.Box(MStrip, new Vector3(0f, -3.35f, -27.6f), new Vector3(2f * PlatX - 2f, 0.05f, 0.3f));
        k.Box(MStrip, new Vector3(0f, -3.75f, -31.0f), new Vector3(2f * PlatX - 2f, 0.05f, 0.2f));
        // the next-train board, hanging over the platform, facing the way in
        foreach (int e in new[] { -1, 1 }) k.Box(MBlack, new Vector3(e * 1.2f, -4.6f, -27.2f), new Vector3(0.04f, 1.8f, 0.04f));
        k.Box(MBlack, new Vector3(0f, -5.9f, -27.2f), new Vector3(3.1f, 0.85f, 0.12f));
        dotMatrix = Words(root.transform, "", new Vector3(0f, -5.9f, -27.13f), Quaternion.LookRotation(Vector3.back), new Vector2(2.9f, 0.72f), 1.6f, new Color(1f, 0.69f, 0f), TextAlignmentOptions.Left);
        dotMatrix.textWrappingMode = TextWrappingModes.Normal;
        dotMatrix.fontStyle = FontStyles.Normal;
    }

    // ------------------------------------------------------------------ the train: four cars, white with red doors
    void BuildTrain()
    {
        float floorY = -Drop, side = TrainZ + 1.3f;                     // its platform side (z)
        for (int c = 0; c < 4; c++)
        {
            float cx = -24.75f + c * 16.5f;
            trainKit.Box(MWhite, new Vector3(cx, floorY + 1.4f, TrainZ), new Vector3(16f, 2.8f, 2.6f));
            trainKit.Box(MNavy, new Vector3(cx, floorY - 0.45f, TrainZ), new Vector3(16f, 0.9f, 2.5f));
            trainKit.Box(MWhite, new Vector3(cx, floorY + 2.9f, TrainZ), new Vector3(16f, 0.2f, 2.0f));
            foreach (int e in new[] { -1, 1 })
                trainKit.Box(MWhite, new Vector3(cx, floorY + 2.72f, TrainZ + e * 1.15f), new Vector3(16f, 0.12f, 0.6f), Quaternion.Euler(e * 30f, 0f, 0f));
            foreach (float zs in new[] { side + 0.005f, TrainZ - 1.305f })
            {
                float zf = zs;
                foreach (float[] w in new[] { new[] { -7.6f, -5.3f }, new[] { -3.7f, 3.7f }, new[] { 5.3f, 7.6f } })      // between the doors
                {
                    trainKit.Box(MGlass, new Vector3(cx + (w[0] + w[1]) / 2f, floorY + 1.6f, zf), new Vector3(w[1] - w[0], 0.8f, 0.02f));
                    trainKit.Box(MRed, new Vector3(cx + (w[0] + w[1]) / 2f, floorY + 0.55f, zf), new Vector3(w[1] - w[0], 0.12f, 0.02f));
                }
            }
            foreach (float dx in new[] { -4.5f, 4.5f })
            {
                float x = cx + dx;
                trainKit.Box(MDoorway, new Vector3(x, floorY + 1.0f, side + 0.006f), new Vector3(1.3f, 1.95f, 0.015f));
                // G9a: the lit carriage behind the doors: a yellow grab pole, the floor, a line of seats either side
                trainKit.Box(MYellow, new Vector3(x, floorY + 1.0f, side + 0.012f), new Vector3(0.05f, 1.95f, 0.006f));
                trainKit.Box(MBlack, new Vector3(x, floorY + 0.06f, side + 0.012f), new Vector3(1.3f, 0.12f, 0.006f));
                foreach (int e in new[] { -1, 1 })
                    trainKit.Box(MNavy, new Vector3(x + e * 0.5f, floorY + 0.55f, side + 0.012f), new Vector3(0.3f, 0.45f, 0.006f));
                foreach (int e in new[] { -1, 1 })
                {
                    LundunMeshKit leaf = e < 0 ? shutKit : openKit;              // G9a: left leaves slide left, right leaves right
                    leaf.Box(MRed, new Vector3(x + e * 0.33f, floorY + 1.0f, side + 0.03f), new Vector3(0.64f, 1.95f, 0.04f));
                    leaf.Box(MGlass, new Vector3(x + e * 0.33f, floorY + 1.45f, side + 0.052f), new Vector3(0.4f, 0.55f, 0.01f));
                }
                // the far side's doors stay shut
                foreach (int e in new[] { -1, 1 }) trainKit.Box(MRed, new Vector3(x + e * 0.33f, floorY + 1.0f, TrainZ - 1.33f), new Vector3(0.64f, 1.95f, 0.04f));
            }
        }
        // the cab ends
        foreach (int e in new[] { -1, 1 })
        {
            float ex = e * (24.75f + 8.1f);
            trainKit.Box(MWhite, new Vector3(ex, floorY + 1.2f, TrainZ), new Vector3(0.3f, 2.5f, 2.5f));
            trainKit.Box(MBlack, new Vector3(ex + e * 0.16f, floorY + 1.7f, TrainZ), new Vector3(0.02f, 0.8f, 2.0f));
            trainKit.Box(MRed, new Vector3(ex + e * 0.16f, floorY + 0.5f, TrainZ), new Vector3(0.02f, 0.4f, 2.4f));
        }
    }

    // ================================================================== UI
    void BuildUI()
    {
        GameObject cg = new GameObject("TubeStationUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 59;                                     // under the Tube's list and its blackout
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        boardGo = MakeButton(cg.transform, "BOARD THE TRAIN\n<size=60%>pick your stop</size>", new Color(0.11f, 0.15f, 0.4f, 0.94f), new Vector2(0f, 230f), new Vector2(460f, 120f), 38f, OnBoard);
        skipGo = MakeButton(cg.transform, "SKIP  <size=70%>up to the street</size>", new Color(0.2f, 0.2f, 0.24f, 0.92f), new Vector2(0f, 130f), new Vector2(360f, 84f), 30f, OnSkip);
        boardGo.SetActive(false); skipGo.SetActive(false);

        // the stop's name, over the dark during the ride
        captionGo = new GameObject("TubeCaption", typeof(RectTransform));
        captionGo.transform.SetParent(cg.transform, false);
        Canvas over = captionGo.AddComponent<Canvas>();
        over.overrideSorting = true; over.sortingOrder = 66;
        RectTransform crt = (RectTransform)captionGo.transform;
        crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = crt.offsetMax = Vector2.zero;
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(captionGo.transform, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        captionText = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) captionText.font = LundunHud.Instance.FontAsset;
        captionText.fontSize = 64; captionText.alignment = TextAlignmentOptions.Center; captionText.color = new Color(1f, 0.78f, 0.17f);
        captionText.fontStyle = FontStyles.Bold; captionText.raycastTarget = false; captionText.richText = true;
        captionGo.SetActive(false);
    }

    static GameObject MakeButton(Transform parent, string text, Color col, Vector2 pos, Vector2 size, float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        GameObject g = new GameObject("Button", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        Image img = g.AddComponent<Image>(); img.color = col;
        if (LundunHudArt.Pill != null) { img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.8f; }
        Button b = g.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(onClick);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(g.transform, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        TextMeshProUGUI t = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = fontSize; t.alignment = TextAlignmentOptions.Center; t.color = Color.white;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false; t.richText = true;
        return g;
    }

    // ================================================================== sounds (made in code)
    static AudioClip Tone(float hz, float seconds, float vol)
    {
        int rate = 22050, n = (int)(seconds * rate);
        float[] s = new float[n];
        for (int i = 0; i < n; i++) { float t = i / (float)rate; s[i] = Mathf.Sin(t * hz * 2f * Mathf.PI) * vol * Mathf.Clamp01((seconds - t) / 0.03f) * Mathf.Clamp01(t / 0.005f); }
        AudioClip c = AudioClip.Create("TubeBeep", n, 1, rate, false); c.SetData(s, 0); return c;
    }

    static AudioClip Noise(float seconds, float vol)
    {
        int rate = 22050, n = (int)(seconds * rate);
        float[] s = new float[n];
        System.Random r = new System.Random(9);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            lp = Mathf.Lerp(lp, (float)r.NextDouble() * 2f - 1f, 0.35f);
            s[i] = lp * vol * Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((seconds - t) / 0.4f);
        }
        AudioClip c = AudioClip.Create("TubeDoors", n, 1, rate, false); c.SetData(s, 0); return c;
    }
}
