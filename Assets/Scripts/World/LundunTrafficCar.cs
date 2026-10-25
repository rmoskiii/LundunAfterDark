using System.Collections.Generic;
using UnityEngine;

/// A traffic or parked car. Drives a real physics car (CarController) along the road network,
/// on the left, turning at junctions, slowing for corners and braking for whatever's in front.
/// Carjack it (enter while a driver is inside) and the driver gets pulled out: they run, fight or shoot.
/// Ram it hard and a hot-headed driver gets out to have a go.
/// Spawned and recycled by LundunPopulation.
/// R3: each driver has a personality (careful, normal or reckless: LundunDriver) that sets their speed, following gap,
/// how they take junctions and how they watch for people; throttle and brakes are eased in and out.
[RequireComponent(typeof(CarController))]
public class LundunTrafficCar : MonoBehaviour
{
    public enum Mode { Driving, Parked, Abandoned }
    public enum Temper { Flee, Fight, Gun }

    public Mode CurrentMode { get; private set; }
    public bool HasDriver { get; private set; }
    public Temper DriverTemper { get; private set; }

    [Header("Driving")]
    [SerializeField] private float lookAhead = 7f;
    [SerializeField] private float steerAngleForFull = 25f;
    [SerializeField] private float cornerSpeed = 5f;
#pragma warning disable 0414
    [Tooltip("Kept for the prefabs; R3: how far they look ahead now comes from the driver's personality (LundunDriver)")]
    [SerializeField] private float brakeLookBase = 4f;
#pragma warning restore 0414

    [Header("Bus (only used when IsBus)")]
    [SerializeField] private bool isBus = false;
    [SerializeField] private float stopEvery = 260f;            // metres between bus stops
    [SerializeField] private float stopFor = 6f;                // seconds at each stop

    [Header("Ramming")]
    [SerializeField] private float angryImpact = 4f;           // m/s relative speed that sets a driver off

    // the old high street's Kenney tiles are narrower: hug the middle there so we miss the Belisha beacons
    static Rect narrowStreet = new Rect(800f, 154f, 120f, 72f);

    private CarController car;
    private LundunRoadGraph graph;
    private int fromNode = -1, toNode = -1, nextNode = -1;
    private float cruise = 9f, panicUntil;
    private readonly List<int> scratch = new List<int>();
    private System.Random rng;
    private static int seedCounter;
    private List<int> route;            // fixed loop of nodes (buses); null = wander at random
    private int routeIndex;
    private float sinceStop, stopUntil, ignoreStopsUntil;

    public bool IsBus { get { return isBus; } }

    // R3: the driver
    public LundunDriver.Style Style { get; private set; }
    private LundunDriver.Profile profile = LundunDriver.For(LundunDriver.Style.Normal);
    private readonly LundunDriver.Senses senses = new LundunDriver.Senses();
    private bool toJunction;

    void RollStyle()
    {
        float hour = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
        Style = isBus ? LundunDriver.Style.Careful : LundunDriver.Roll(rng, hour);    // bus drivers are always careful
        profile = LundunDriver.For(Style);
        car.ProportionalBrake = true;
    }

    void Awake()
    {
        car = GetComponent<CarController>();
        rng = new System.Random(++seedCounter * 7919 + System.Environment.TickCount);
    }

    void OnEnable()
    {
        VehicleSystem.Entering += OnEntering;
        PlayerCombat.GunshotFired += OnGunshot;
    }

    void OnDisable()
    {
        VehicleSystem.Entering -= OnEntering;
        PlayerCombat.GunshotFired -= OnGunshot;
    }

    // ------------------------------------------------------------------ set-up (called by LundunPopulation)
    public void BeginDriving(LundunRoadGraph g, int from, int to, float speed)
    {
        graph = g; fromNode = from; toNode = to; cruise = speed; route = null;
        PickNext();
        CurrentMode = Mode.Driving;
        HasDriver = true;
        DriverTemper = RollTemper();
        RollStyle();
        toJunction = IsJunction(toNode);
    }

    /// Follow a fixed loop of nodes, starting on the hop route[index] → route[index+1]. Buses stop every stopEvery metres.
    public void BeginRoute(LundunRoadGraph g, List<int> loop, int index, float speed)
    {
        graph = g; route = loop; routeIndex = index; cruise = speed;
        fromNode = route[routeIndex % route.Count];
        toNode = route[(routeIndex + 1) % route.Count];
        nextNode = route[(routeIndex + 2) % route.Count];
        sinceStop = 0f; stopUntil = 0f;
        CurrentMode = Mode.Driving;
        HasDriver = true;
        DriverTemper = RollTemper();
        RollStyle();
        toJunction = IsJunction(toNode);
    }

    public void BeginParked()
    {
        graph = null;
        CurrentMode = Mode.Parked;
        HasDriver = false;
        car.ProportionalBrake = false;
        car.SetTouchInput(0f, 0f);
    }

    Temper RollTemper()
    {
        float hour = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
        float gun = (hour < 5f || hour > 22f) ? 0.12f : 0.08f;   // more armed nutters after midnight
        double r = rng.NextDouble();
        if (r < gun) return Temper.Gun;
        if (r < gun + 0.32f) return Temper.Fight;
        return Temper.Flee;
    }

    /// R3: a reckless driver is more likely to get out and have a go after a crash.
    Temper CrashTemper()
    {
        if (Style != LundunDriver.Style.Reckless || DriverTemper != Temper.Flee) return DriverTemper;
        return rng.NextDouble() < 0.5 ? Temper.Fight : Temper.Flee;
    }

    /// R3: a node where roads meet (three or more ways), not just a bend.
    bool IsJunction(int node)
    {
        if (graph == null || node < 0) return false;
        graph.NextNodes(node, -1, scratch);
        return scratch.Count >= 3;
    }

    // ------------------------------------------------------------------ driving
    void Update()
    {
        if (car.IsDriven) { CurrentMode = Mode.Abandoned; return; }      // James has it now
        if (CurrentMode != Mode.Driving || graph == null) return;

        NavPoint a = graph.NodePosition(fromNode), b = graph.NodePosition(toNode);
        Vector3 A = new Vector3((float)a.x, 0f, (float)a.z), B = new Vector3((float)b.x, 0f, (float)b.z);
        Vector3 dir = B - A; float segLen = dir.magnitude; if (segLen < 0.1f) { Advance(); return; }
        dir /= segLen;
        double width; string cls;
        graph.EdgeInfo(fromNode, toNode, out width, out cls);

        // where are we along this stretch of road?
        Vector3 pos = transform.position; pos.y = 0f;
        float s = Vector3.Dot(pos - A, dir);
        if (segLen - s < lookAhead * 0.6f) { Advance(); return; }

        // aim at a point a little way ahead, in the left lane (spills onto the next road near the end)
        float aimS = s + lookAhead;
        Vector3 target;
        if (aimS <= segLen) target = LanePoint(A, dir, aimS, width);
        else
        {
            NavPoint c = graph.NodePosition(nextNode);
            Vector3 C = new Vector3((float)c.x, 0f, (float)c.z), ndir = (C - B).normalized;
            double nw; string ncls; graph.EdgeInfo(toNode, nextNode, out nw, out ncls);
            target = LanePoint(B, ndir, aimS - segLen, nw);
        }

        Vector3 local = transform.InverseTransformPoint(new Vector3(target.x, transform.position.y, target.z));
        float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float steer = Mathf.Clamp(angle / steerAngleForFull, -1f, 1f);

        // how fast: road speed (by the driver's personality), slower into sharp turns and junctions, stop for things in front
        float want = cruise * (isBus ? 1f : profile.speed) * (Time.time < panicUntil ? 1.4f : 1f);
        float toEnd = segLen - s;
        float speed = car.Speed;
        if (toEnd < 25f && nextNode >= 0)
        {
            NavPoint c = graph.NodePosition(nextNode);
            Vector3 ndir = (new Vector3((float)c.x, 0f, (float)c.z) - B).normalized;
            float turn = Vector3.Angle(dir, ndir);
            float corner = Style == LundunDriver.Style.Reckless ? cornerSpeed * 1.4f : cornerSpeed;
            if (turn > 30f) want = Mathf.Min(want, Mathf.Lerp(cruise, corner, Mathf.InverseLerp(30f, 90f, turn)));
        }
        // R3: junctions: ease down into them, and give way (careful / normal drivers)
        if (toJunction && Time.time >= panicUntil)
        {
            if (profile.junctionSpeed > 0f && toEnd < profile.junctionFrom)
                want = Mathf.Min(want, profile.junctionSpeed + Mathf.Sqrt(2f * profile.brake * Mathf.Max(0f, toEnd - 6f)));
            if (toEnd < 30f && LundunDriver.GiveWay(transform, speed, B, toEnd, profile, senses))
            {
                float stopLine = (float)(width * 0.5) + 4f;                // short of the crossing road
                want = Mathf.Min(want, Mathf.Sqrt(2f * profile.brake * Mathf.Max(0f, toEnd - stopLine)));
            }
        }
        if (isBus)
        {
            LundunDressing dressing = LundunDressing.Instance;
            if (Time.time < stopUntil) want = 0f;
            else if (dressing != null && dressing.BusStopCount > 0)
            {
                // pull in at the real shelters (Lundun → 13): ease off, stop alongside, wait, move on
                float ahead;
                if (Time.time > ignoreStopsUntil && dressing.StopAhead(transform.position, transform.forward, out ahead))
                {
                    want = Mathf.Min(want, Mathf.Max(1.2f, ahead * 0.5f));
                    if (ahead < 1.5f) { stopUntil = Time.time + stopFor; ignoreStopsUntil = stopUntil + 8f; want = 0f; }
                }
            }
            else
            {
                sinceStop += Mathf.Max(0f, speed) * Time.deltaTime;
                if (sinceStop > stopEvery && toEnd > 30f && s > 20f) { stopUntil = Time.time + stopFor; sinceStop = 0f; want = 0f; }
            }
        }
        // R3: what's ahead (cars at a safe gap, people in the road or stepping out), eased throttle and brakes, the horn
        float laneHalf = Mathf.Max(1.4f, (float)(width * 0.25));
        bool emergency, blocked;
        want = Mathf.Min(want, LundunDriver.Ahead(transform, speed, profile, laneHalf, senses, out emergency, out blocked));
        float throttle = LundunDriver.Pedal(speed, want, profile, emergency);
        LundunDriver.Horn(transform, speed, blocked, profile, senses);
        car.SetTouchInput(throttle, steer);
    }

    Vector3 LanePoint(Vector3 start, Vector3 dir, float along, double width)
    {
        Vector3 centre = start + dir * along;
        Vector3 left = new Vector3(-dir.z, 0f, dir.x);
        // UK: keep left (buses hug the kerb; clear of the bays on 10 m streets; the old high street is narrower)
        float offset = LundunDriver.LaneOffset(width, narrowStreet.Contains(new Vector2(centre.x, centre.z)), isBus);
        return centre + left * offset;
    }

    void Advance()
    {
        fromNode = toNode; toNode = nextNode;
        PickNext();
        toJunction = IsJunction(toNode);
        senses.waitedAtJunction = 0f;
    }

    void PickNext()
    {
        if (route != null)
        {
            routeIndex = (routeIndex + 1) % route.Count;
            if (route[routeIndex % route.Count] != fromNode) routeIndex = route.IndexOf(fromNode) >= 0 ? route.IndexOf(fromNode) : routeIndex;
            nextNode = route[(routeIndex + 2) % route.Count];
            return;
        }
        graph.NextNodes(toNode, fromNode, scratch);
        nextNode = scratch.Count > 0 ? scratch[rng.Next(scratch.Count)] : fromNode;
    }

    // ------------------------------------------------------------------ drivers
    void OnEntering(CarController c)
    {
        if (c != car || !HasDriver) return;
        // carjack: driver pulled out of the driver's side (UK: right), reacts by temper
        Transform player = FindAnyObjectByType<VehicleSystem>() != null ? FindAnyObjectByType<VehicleSystem>().PlayerTarget : null;
        EjectDriver(DriverTemper, player);
        CurrentMode = Mode.Abandoned;
    }

    void OnCollisionEnter(Collision c)
    {
        if (!HasDriver || CurrentMode != Mode.Driving) return;
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        if (vs == null || vs.CurrentCar == null) return;
        if (!c.transform.IsChildOf(vs.CurrentCar.transform)) return;          // only James's car counts
        if (c.relativeVelocity.magnitude < angryImpact) return;
        Temper temper = CrashTemper();                                         // R3: reckless drivers are hot-headed
        if (temper == Temper.Flee) { panicUntil = Time.time + 6f; return; }   // honks and speeds off
        CurrentMode = Mode.Abandoned;
        car.ProportionalBrake = false;
        car.SetTouchInput(-1f, 0f);
        EjectDriver(temper, vs.PlayerTarget);
    }

    void OnGunshot(Vector3 from)
    {
        if (CurrentMode != Mode.Driving) return;
        if ((from - transform.position).sqrMagnitude < 40f * 40f) panicUntil = Time.time + 8f;   // floor it
    }

    void EjectDriver(Temper temper, Transform player)
    {
        HasDriver = false;
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return;
        Vector3 door = transform.position + transform.right * 1.6f;
        GameObject driver = pop.SpawnPersonAt(door, transform.eulerAngles.y + 90f);
        if (driver == null) return;
        if (temper == Temper.Flee || player == null)
        {
            NPCBrain brain = driver.GetComponent<NPCBrain>();
            if (brain != null && player != null) brain.Panic(player.position);
            return;
        }
        LundunAngryDriver angry = driver.AddComponent<LundunAngryDriver>();
        angry.Begin(temper == Temper.Gun);
    }
}
