using System.Collections.Generic;
using UnityEngine;

/// A traffic or parked car. Drives a real physics car (CarController) along the road network,
/// on the left, turning at junctions, slowing for corners and braking for whatever's in front.
/// Carjack it (enter while a driver is inside) and the driver gets pulled out: they run, fight or shoot.
/// Ram it hard and a hot-headed driver gets out to have a go.
/// Spawned and recycled by LundunPopulation.
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
    [SerializeField] private float brakeLookBase = 4f;        // metres checked ahead, plus speed × 1.2

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
    private static int obstacleMask = -1;
    private System.Random rng;
    private static int seedCounter;
    private List<int> route;            // fixed loop of nodes (buses); null = wander at random
    private int routeIndex;
    private float sinceStop, stopUntil, ignoreStopsUntil;

    public bool IsBus { get { return isBus; } }

    void Awake()
    {
        car = GetComponent<CarController>();
        if (obstacleMask == -1) obstacleMask = LayerMask.GetMask("Vehicle", "Player", "NPC");
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
    }

    public void BeginParked()
    {
        graph = null;
        CurrentMode = Mode.Parked;
        HasDriver = false;
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

        // how fast: road speed, slower into sharp turns, stop for things in front
        float want = cruise * (Time.time < panicUntil ? 1.4f : 1f);
        float toEnd = segLen - s;
        if (toEnd < 25f && nextNode >= 0)
        {
            NavPoint c = graph.NodePosition(nextNode);
            Vector3 ndir = (new Vector3((float)c.x, 0f, (float)c.z) - B).normalized;
            float turn = Vector3.Angle(dir, ndir);
            if (turn > 30f) want = Mathf.Min(want, Mathf.Lerp(cruise, cornerSpeed, Mathf.InverseLerp(30f, 90f, turn)));
        }
        float speed = car.Speed;
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
        float look = brakeLookBase + Mathf.Max(0f, speed) * 1.2f;
        Vector3 front = transform.position + transform.forward * 2.8f + Vector3.up * 0.6f;
        if (Physics.SphereCast(front, 0.9f, transform.forward, out RaycastHit hit, look, obstacleMask, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(transform))
            want = Mathf.Min(want, Mathf.Max(0f, hit.distance - 2f) * 0.8f);

        float throttle = speed < want - 0.5f ? Mathf.Clamp01((want - speed) / 3f) : (speed > want + 1f ? -1f : 0.15f);
        if (want < 0.3f && speed < 0.5f) throttle = 0f;       // wait, don't creep
        car.SetTouchInput(throttle, steer);
    }

    Vector3 LanePoint(Vector3 start, Vector3 dir, float along, double width)
    {
        Vector3 centre = start + dir * along;
        Vector3 left = new Vector3(-dir.z, 0f, dir.x);
        bool baysStreet = width > 9.5 && width < 10.5;      // L3: 10 m streets have parked cars along the kerb: keep clear of them
        float offset = narrowStreet.Contains(new Vector2(centre.x, centre.z)) ? 1.6f
                     : baysStreet ? (float)(width * 0.25) - 0.35f
                     : isBus ? Mathf.Max((float)(width * 0.25), (float)(width * 0.5) - 1.8f)   // buses hug the kerb
                     : (float)(width * 0.25);                                                     // UK: keep left
        return centre + left * offset;
    }

    void Advance()
    {
        fromNode = toNode; toNode = nextNode;
        PickNext();
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
        if (DriverTemper == Temper.Flee) { panicUntil = Time.time + 6f; return; }   // honks and speeds off
        CurrentMode = Mode.Abandoned;
        car.SetTouchInput(-1f, 0f);
        EjectDriver(DriverTemper, vs.PlayerTarget);
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
