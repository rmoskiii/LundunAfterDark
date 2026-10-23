using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// "Life bubble": the one place that decides who exists in the world.
/// Only the area around the player is alive. People appear on pavements just out of view and are
/// put away again once they're far behind you and unseen, so nothing far away costs battery.
/// People are reused (pooled) instead of created/destroyed, so there are no hitches.
/// R2 (traffic) and R4 (more people) plug into the same budget.
public class LundunPopulation : MonoBehaviour
{
    public enum Budget { Low, Medium, High }
    public static LundunPopulation Instance { get; private set; }

    [Header("Budget (iPhone 14 Plus: Medium)")]
    [SerializeField] private Budget budget = Budget.Medium;

    [Header("Pedestrians")]
    [SerializeField] private GameObject[] pedestrianPrefabs;
    [Tooltip("Pistol model for armed drivers (set by Lundun → 12  Build People)")]
    [SerializeField] private GameObject gunModel;
    [Tooltip("Given to any person who turns up without an animator controller (set by Lundun → 12)")]
    [SerializeField] private RuntimeAnimatorController peopleController;
    [Tooltip("How strongly districts prefer their own look (Ped_Suit_* in Knightsbury, Ped_Street_* on the estate / Peckam, Ped_Work_* at the warehouses)")]
    [SerializeField, Range(0f, 1f)] private float districtFlavour = 0.6f;
    [SerializeField] private float spawnNearest = 45f;
    [SerializeField] private float spawnFarthest = 110f;
    [SerializeField] private float despawnBeyond = 140f;
    [SerializeField] private float tickEvery = 0.25f;       // how often the manager thinks (not every frame)
    [SerializeField] private int spawnsPerTick = 2;

    [Header("Shoving: run into people and they stumble")]
    [SerializeField] private float shoveSpeed = 3.5f;          // James must be at least jogging (m/s)
    [SerializeField] private float shoveReach = 1.25f;         // metres, centre to centre
    [Range(0f, 1f)] [SerializeField] private float shoveFightBack = 0.25f;   // squares up to James afterwards
    [Range(0f, 1f)] [SerializeField] private float shoveRunOff = 0.35f;      // runs off; the rest just carry on

    [Header("Traffic and parked cars")]
    [SerializeField] private GameObject[] carPrefabs;          // made by Lundun → 9  Build Traffic
    [SerializeField] private float carSpawnNearest = 70f;
    [SerializeField] private float carSpawnFarthest = 170f;
    [SerializeField] private float carDespawnBeyond = 220f;
    [Tooltip("1 = normal. 0.5 = half as many moving cars, 1.5 = busier")]
    [SerializeField, Range(0f, 2f)] private float trafficDensity = 1f;
    [Tooltip("1 = normal. 0 = no parked cars")]
    [SerializeField, Range(0f, 2f)] private float parkedDensity = 1f;
    [SerializeField] private float movingCarSpacing = 25f;     // a new moving car never appears closer than this to another car
    [SerializeField] private float parkedCarSpacing = 10f;
    [SerializeField] private Color[] paintColours = {
        new Color(0.85f, 0.85f, 0.87f), new Color(0.08f, 0.08f, 0.09f), new Color(0.55f, 0.57f, 0.6f), new Color(0.6f, 0.06f, 0.06f),
        new Color(0.08f, 0.18f, 0.42f), new Color(0.12f, 0.3f, 0.18f), new Color(0.75f, 0.68f, 0.55f), new Color(0.95f, 0.95f, 0.95f) };

    [Header("Buses")]
    [SerializeField] private GameObject busPrefab;              // made by Lundun → 11  Make Double-Decker Bus
    [SerializeField] private float busSpeed = 9f;

    [Header("Safety net: shrink the budget if frame rate drops")]
    [SerializeField] private bool adaptive = true;
    [SerializeField] private float slowFrame = 0.037f;      // ~27 fps
    [SerializeField] private float okFrame = 0.034f;

    [Header("Testing")]
    [SerializeField] private bool showStats = true;

    // Budgets: pedestrians, moving cars, parked cars, buses (cars/buses used from R2)
    static readonly int[,] Budgets = { { 10, 3, 5, 1 }, { 18, 6, 9, 2 }, { 26, 9, 14, 2 } };

    public int PedestrianBudget { get { return Budgets[(int)budget, 0]; } }
    public int MovingCarBudget { get { return Mathf.RoundToInt(Budgets[(int)budget, 1] * trafficDensity); } }
    public int ParkedCarBudget { get { return Mathf.RoundToInt(Budgets[(int)budget, 2] * parkedDensity); } }
    public int BusBudget { get { return Budgets[(int)budget, 3]; } }
    /// 0.4–1: shrinks when the phone struggles. Other spawners multiply their budgets by this too.
    public float Scale { get; private set; }
    /// Busier at rush hour, quiet at 3am.
    public float TimeOfDayFactor { get; private set; }

    class Ped { public GameObject go; public Health health; public Renderer[] renderers; public bool active; }

    private readonly List<Ped> peds = new List<Ped>();
    private VehicleSystem vehicles;
    private readonly System.Random rng = new System.Random();
    private float nextTick, frameAvg = 0.033f, slowFor, fastFor;
    private TextMeshProUGUI stats;
    private int activeCount;

    class Car { public GameObject go; public LundunTrafficCar ai; public CarController cc; public Rigidbody rb; public VehicleHealth health; public Renderer[] renderers; public bool active; public bool parked; public bool bus; }
    private readonly List<Car> cars = new List<Car>();
    private int movingCount, parkedCount, busCount, nextRoute;
    private List<List<int>> busRoutes;
    // bus loops (three stops each, no U-turns): Peckam → Knightsbury Circus → Mayfield St, and Gilmore → Warehouses → Quay Bridge
    private static readonly float[][] RouteStops = {
        new float[] { 905, 190, 560, 920, 820, 1030 },
        new float[] { 430, 300, 1260, 780, 1230, 230 } };
    private static readonly string[] DriveClasses = { "ring", "arterial", "street", "service" };
    private static readonly string[] ParkClasses = { "street", "arterial" };
    private readonly Dictionary<Material, Material[]> paintCache = new Dictionary<Material, Material[]>();

    void Awake() { Instance = this; Scale = 1f; TimeOfDayFactor = 1f; if (gunModel != null) LundunAngryDriver.GunModel = gunModel; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        if (showStats) MakeStats();
    }

    void Update()
    {
        // average frame time (unscaled, so the paused map doesn't count)
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f && dt < 0.5f) frameAvg = Mathf.Lerp(frameAvg, dt, 0.05f);
        CheckShoves();
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + tickEvery;
        if (vehicles == null) { vehicles = FindAnyObjectByType<VehicleSystem>(); if (vehicles == null) return; }

        Adapt();
        TimeOfDayFactor = DensityAt(LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f);

        Vector3 player = vehicles.PlayerTarget.position;
        int target = Mathf.RoundToInt(PedestrianBudget * Scale * TimeOfDayFactor);
        TickPedestrians(player, target);
        int movingTarget = Mathf.RoundToInt(MovingCarBudget * Scale * TimeOfDayFactor);
        int parkedTarget = Mathf.RoundToInt(ParkedCarBudget * Scale);
        TickCars(player, movingTarget, parkedTarget);
        int busTarget = Scale < 0.5f ? 0 : Mathf.RoundToInt(BusBudget * Mathf.Min(1f, Scale));
        TickBuses(player, busTarget);

        if (stats != null)
            stats.text = Mathf.RoundToInt(1f / frameAvg) + " fps   people " + activeCount + "/" + target
                       + "   traffic " + movingCount + "/" + movingTarget + "   parked " + parkedCount + "/" + parkedTarget
                       + "   buses " + busCount + "/" + busTarget
                       + "   scale " + Scale.ToString("0.00");
    }

    // ------------------------------------------------------------------ shoving
    private CharacterController playerBody;
    private float nextShove;
    const float ShoveClip = 4.3f;               // Anim_Shove_reaction (stumble and spin round)
    const float ShoveClipYaw = 12f;             // the stumble drifts ~12° right of straight ahead

    /// James running into a pedestrian on foot knocks them stumbling (every frame, but only ~20 people to check).
    void CheckShoves()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot || Time.time < nextShove) return;
        if (playerBody == null || playerBody.transform != ph.Body) playerBody = ph.Body.GetComponent<CharacterController>();
        if (playerBody == null) return;
        Vector3 v = playerBody.velocity; v.y = 0f;
        if (v.magnitude < shoveSpeed) return;
        Vector3 me = ph.Body.position, dir = v.normalized;
        foreach (Ped p in peds)
        {
            if (!p.active || p.go == null || (p.health != null && p.health.IsDead)) continue;
            Vector3 to = p.go.transform.position - me; to.y = 0f;
            if (to.sqrMagnitude > shoveReach * shoveReach || Vector3.Dot(to.normalized, dir) < 0.3f) continue;
            if (LundunStagger.Busy(p.go)) continue;
            nextShove = Time.time + 0.4f;
            GameObject who = p.go;
            Vector3 pushedFrom = me;
            bool fighting = who.GetComponent<LundunAngryDriver>() != null;
            double roll = rng.NextDouble();
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - ShoveClipYaw;      // stumble the way James pushed them
            LundunStagger.Play(who, "Shoved", ShoveClip, true, yaw, () =>
            {
                if (fighting || who == null) return;                                   // already having a go at James
                if (roll < shoveFightBack) who.AddComponent<LundunAngryDriver>().Begin(false);
                else if (roll < shoveFightBack + shoveRunOff)
                {
                    NPCBrain b = who.GetComponent<NPCBrain>();
                    if (b != null) b.Panic(pushedFrom);
                }
            });
            return;
        }
    }

    // ------------------------------------------------------------------ pedestrians
    void TickPedestrians(Vector3 player, int target)
    {
        Camera cam = Camera.main;
        activeCount = 0;
        for (int i = peds.Count - 1; i >= 0; i--)
        {
            Ped p = peds[i];
            if (p.go == null) { peds.RemoveAt(i); continue; }
            if (!p.active) continue;
            float d = Flat(p.go.transform.position, player);
            bool dead = p.health != null && p.health.IsDead;
            bool seen = Seen(p, cam);
            // dead bodies: remove once nobody's looking; the living: put away when far and unseen
            if (dead && !seen && d > 40f) { Destroy(p.go); peds.RemoveAt(i); continue; }
            if (!dead && d > despawnBeyond && !seen) { p.go.SetActive(false); p.active = false; continue; }
            if (!dead) activeCount++;
        }
        // too many (budget shrank): put away the farthest unseen ones
        while (activeCount > target)
        {
            Ped far = null; float fd = 0f;
            foreach (Ped p in peds)
                if (p.active && (p.health == null || !p.health.IsDead) && !Seen(p, cam))
                {
                    float d = Flat(p.go.transform.position, player);
                    if (d > fd) { fd = d; far = p; }
                }
            if (far == null || fd < 30f) break;
            far.go.SetActive(false); far.active = false; activeCount--;
        }

        for (int n = 0; n < spawnsPerTick && activeCount < target; n++)
        {
            Vector3 pos;
            if (!FindPavementSpot(player, cam, out pos)) break;
            Ped p = GetPooled(pos);
            if (p == null) break;
            p.go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            p.go.SetActive(true);        // NavMeshAgent snaps onto the NavMesh here
            p.active = true;
            activeCount++;
        }
    }

    Ped GetPooled(Vector3 at)
    {
        string look = rng.NextDouble() < districtFlavour ? LookFor(at) : null;
        if (look != null) foreach (Ped p in peds) if (!p.active && p.go != null && p.go.name.Contains(look)) return p;
        foreach (Ped p in peds) if (!p.active && p.go != null) return p;
        if (pedestrianPrefabs == null || pedestrianPrefabs.Length == 0) return null;
        // pool grows up to the High budget
        if (peds.Count >= Budgets[2, 0]) return null;
        GameObject prefab = null;
        if (look != null)
        {
            List<GameObject> match = new List<GameObject>();
            foreach (GameObject g in pedestrianPrefabs) if (g != null && g.name.Contains(look)) match.Add(g);
            if (match.Count > 0) prefab = match[rng.Next(match.Count)];
        }
        if (prefab == null) prefab = pedestrianPrefabs[rng.Next(pedestrianPrefabs.Length)];
        if (prefab == null) return null;
        GameObject go = Instantiate(prefab, transform);
        go.SetActive(false);
        LundunPeopleSizer.Normalize(go);                       // safety net: nobody at the wrong size
        go.name = prefab.name + "_" + peds.Count;
        foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
        {
            a.cullingMode = AnimatorCullingMode.CullCompletely;   // off-screen = no animation work
            if (a.runtimeAnimatorController == null && peopleController != null) a.runtimeAnimatorController = peopleController;
        }
        Ped np = new Ped { go = go, health = go.GetComponent<Health>(), renderers = go.GetComponentsInChildren<Renderer>(true) };
        peds.Add(np);
        return np;
    }

    /// E7b: a new person of their own for another system to look after (the Lundun Wheel's crowd), made under parent,
    /// switched off. Not part of this pool or its budget: the caller owns it.
    public GameObject NewPerson(Transform parent)
    {
        if (pedestrianPrefabs == null || pedestrianPrefabs.Length == 0) return null;
        GameObject prefab = pedestrianPrefabs[rng.Next(pedestrianPrefabs.Length)];
        if (prefab == null) return null;
        GameObject go = Instantiate(prefab, parent);
        go.SetActive(false);
        LundunPeopleSizer.Normalize(go);
        foreach (Animator a in go.GetComponentsInChildren<Animator>(true))
        {
            a.cullingMode = AnimatorCullingMode.CullCompletely;
            if (a.runtimeAnimatorController == null && peopleController != null) a.runtimeAnimatorController = peopleController;
        }
        return go;
    }

    /// Puts a person at a specific spot right now (e.g. a driver pulled out of a car). Null if none free.
    public GameObject SpawnPersonAt(Vector3 near, float yaw)
    {
        if (!UnityEngine.AI.NavMesh.SamplePosition(near, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) return null;
        Ped p = GetPooled(hit.position);
        if (p == null)
        {
            // pool full: borrow the farthest unseen person
            Camera cam = Camera.main; float fd = -1f;
            foreach (Ped q in peds)
                if (q.active && (q.health == null || !q.health.IsDead) && !Seen(q, cam))
                {
                    float d = Flat(q.go.transform.position, near);
                    if (d > fd) { fd = d; p = q; }
                }
            if (p == null) return null;
            p.go.SetActive(false);
        }
        p.go.transform.SetPositionAndRotation(hit.position, Quaternion.Euler(0f, yaw, 0f));
        p.go.SetActive(true);
        p.active = true;
        return p.go;
    }

    // ------------------------------------------------------------------ cars
    void TickCars(Vector3 player, int movingTarget, int parkedTarget)
    {
        if (carPrefabs == null || carPrefabs.Length == 0) return;
        Camera cam = Camera.main;
        movingCount = 0; parkedCount = 0;
        for (int i = cars.Count - 1; i >= 0; i--)
        {
            Car c = cars[i];
            if (c.go == null) { cars.RemoveAt(i); continue; }
            if (!c.active) continue;
            if (c.cc.IsDriven) continue;                                      // James's ride: never touch it
            float d = Flat(c.go.transform.position, player);
            bool seen = SeenCar(c, cam);
            bool damaged = c.health != null && (c.health.IsWrecked || c.health.IsBurning || c.health.Current < 60f);
            if (d > carDespawnBeyond + (c.bus ? 40f : 0f) && !seen)
            {
                if (damaged || c.go.transform.position.y < -5f) { Destroy(c.go); cars.RemoveAt(i); continue; }
                c.go.SetActive(false); c.active = false; continue;
            }
            if (c.health != null && c.health.IsWrecked) continue;            // wrecks don't count
            if (c.bus) continue;                                              // counted in TickBuses
            // too many standing cars (e.g. lots you've stolen and dumped): tidy the far, unseen ones away
            if (c.ai.CurrentMode != LundunTrafficCar.Mode.Driving && parkedCount >= parkedTarget && d > 60f && !seen)
            { if (damaged) { Destroy(c.go); cars.RemoveAt(i); } else { c.go.SetActive(false); c.active = false; } continue; }
            if (c.ai.CurrentMode == LundunTrafficCar.Mode.Driving) movingCount++; else parkedCount++;
        }

        int spawned = 0;
        while (spawned < 2 && movingCount < movingTarget && SpawnCar(player, cam, false)) { movingCount++; spawned++; }
        while (spawned < 2 && parkedCount < parkedTarget && SpawnCar(player, cam, true)) { parkedCount++; spawned++; }
    }

    bool SpawnCar(Vector3 player, Camera cam, bool parked)
    {
        // L3: parked cars go in the marked bays when the city has them (Lundun → 23)
        if (parked && LundunParking.Instance != null && LundunParking.Instance.SlotCount > 0) return SpawnInBay(player, cam);
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return false;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            NavPoint p, dir; double halfWidth, pave, along; int edge; string cls;
            if (!nav.Graph.RandomRoadSpot(new NavPoint(player.x, player.z), parked ? spawnNearest : carSpawnNearest, carSpawnFarthest, rng, false,
                                          parked ? ParkClasses : DriveClasses, out p, out dir, out halfWidth, out pave, out edge, out along, out cls)) return false;
            int a, b; nav.Graph.EdgeEnds(edge, out a, out b);
            NavPoint na = nav.Graph.NodePosition(a), nb = nav.Graph.NodePosition(b);
            if (NavPoint.Dist(p, na) < 18 || NavPoint.Dist(p, nb) < 18) continue;          // not in a junction
            if (InOldStreet(p)) continue;

            bool forward = rng.NextDouble() < 0.5;                                          // which way it faces
            Vector3 along3 = new Vector3((float)dir.x, 0f, (float)dir.z) * (forward ? 1f : -1f);
            Vector3 left = new Vector3(-along3.z, 0f, along3.x);
            float lateral = parked ? (float)halfWidth - 1.2f : (float)(halfWidth * 0.5);
            Vector3 pos = new Vector3((float)p.x, 0f, (float)p.z) + left * lateral;
            if (cam != null && LundunView.CanSee(cam, pos)) continue;
            if (Physics.CheckSphere(pos + Vector3.up, parked ? parkedCarSpacing : movingCarSpacing, LayerMask.GetMask("Vehicle", "Player"), QueryTriggerInteraction.Ignore)) continue;

            Car c = GetPooledCar();
            if (c == null) return false;
            Quaternion rot = Quaternion.LookRotation(along3, Vector3.up);
            Vector3 at = pos + Vector3.up * 1.0f;
            c.go.transform.SetPositionAndRotation(at, rot);
            c.go.SetActive(true);
            c.rb.position = at; c.rb.rotation = rot;
            c.rb.linearVelocity = Vector3.zero; c.rb.angularVelocity = Vector3.zero;
            c.cc.SetTouchInput(0f, 0f);
            c.active = true;
            if (parked) c.ai.BeginParked();
            else
            {
                float speed = cls == "ring" ? 16f : cls == "arterial" ? 12f : cls == "service" ? 6f : 9f;
                c.ai.BeginDriving(nav.Graph, forward ? a : b, forward ? b : a, speed * Random.Range(0.85f, 1.1f));
                c.rb.linearVelocity = along3 * speed * 0.6f;                               // already moving
            }
            return true;
        }
        return false;
    }

    /// L3: a real (stealable) parked car in a free bay.
    bool SpawnInBay(Vector3 player, Camera cam)
    {
        Vector3 pos; Quaternion rot;
        if (!LundunParking.Instance.TryGetSlot(player, spawnNearest, carSpawnFarthest, rng, cam, out pos, out rot)) return false;
        Car c = GetPooledCar();
        if (c == null) return false;
        Vector3 at = pos + Vector3.up * 1.0f;
        c.go.transform.SetPositionAndRotation(at, rot);
        c.go.SetActive(true);
        c.rb.position = at; c.rb.rotation = rot;
        c.rb.linearVelocity = Vector3.zero; c.rb.angularVelocity = Vector3.zero;
        c.cc.SetTouchInput(0f, 0f);
        c.active = true;
        c.ai.BeginParked();
        return true;
    }

    // ------------------------------------------------------------------ buses
    void TickBuses(Vector3 player, int target)
    {
        busCount = 0;
        if (busPrefab == null) return;
        foreach (Car c in cars) if (c.bus && c.active && c.go != null && !(c.health != null && c.health.IsWrecked)) busCount++;
        if (busCount >= target) return;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return;
        if (busRoutes == null)
        {
            busRoutes = new List<List<int>>();
            foreach (float[] r in RouteStops)
            {
                NavPoint[] stops = new NavPoint[r.Length / 2];
                for (int i = 0; i < stops.Length; i++) stops[i] = new NavPoint(r[i * 2], r[i * 2 + 1]);
                List<int> loop = nav.Graph.LoopRoute(stops);
                if (loop.Count >= 3) busRoutes.Add(loop);
            }
        }
        if (busRoutes.Count == 0) return;

        Camera cam = Camera.main;
        List<int> route = busRoutes[nextRoute++ % busRoutes.Count];
        // a hop on this route 90–220 m from the player, out of sight
        int start = rng.Next(route.Count);
        for (int k = 0; k < route.Count; k++)
        {
            int i = (start + k) % route.Count;
            NavPoint a = nav.Graph.NodePosition(route[i]), b = nav.Graph.NodePosition(route[(i + 1) % route.Count]);
            double len = NavPoint.Dist(a, b);
            if (len < 40) continue;
            NavPoint p = new NavPoint(a.x + (b.x - a.x) * 20 / len, a.z + (b.z - a.z) * 20 / len);   // 20 m past the junction
            float d = Vector2.Distance(new Vector2((float)p.x, (float)p.z), new Vector2(player.x, player.z));
            if (d < 90f || d > 220f) continue;
            double w; string cls; nav.Graph.EdgeInfo(route[i], route[(i + 1) % route.Count], out w, out cls);
            Vector3 along = new Vector3((float)(b.x - a.x), 0f, (float)(b.z - a.z)).normalized;
            Vector3 left = new Vector3(-along.z, 0f, along.x);
            Vector3 pos = new Vector3((float)p.x, 0f, (float)p.z) + left * Mathf.Max((float)(w * 0.25), (float)(w * 0.5) - 1.8f);
            if (cam != null && LundunView.CanSee(cam, pos)) continue;
            if (Physics.CheckSphere(pos + Vector3.up * 1.5f, 6f, LayerMask.GetMask("Vehicle", "Player"), QueryTriggerInteraction.Ignore)) continue;

            Car c = GetPooledBus();
            if (c == null) return;
            Quaternion rot = Quaternion.LookRotation(along, Vector3.up);
            Vector3 at = pos + Vector3.up * 1.2f;
            c.go.transform.SetPositionAndRotation(at, rot);
            c.go.SetActive(true);
            c.rb.position = at; c.rb.rotation = rot;
            c.rb.linearVelocity = along * busSpeed * 0.5f; c.rb.angularVelocity = Vector3.zero;
            c.cc.SetTouchInput(0f, 0f);
            c.active = true;
            c.ai.BeginRoute(nav.Graph, route, i, busSpeed);
            return;
        }
    }

    Car GetPooledBus()
    {
        foreach (Car c in cars) if (c.bus && !c.active && c.go != null) return c;
        if (busPrefab == null) return null;
        GameObject go = Instantiate(busPrefab, transform);
        go.SetActive(false);
        go.name = busPrefab.name + "_" + cars.Count;
        LundunTrafficCar ai = go.GetComponent<LundunTrafficCar>();
        if (ai == null) ai = go.AddComponent<LundunTrafficCar>();
        Car nc = new Car { go = go, ai = ai, cc = go.GetComponent<CarController>(), rb = go.GetComponent<Rigidbody>(), bus = true,
                           health = go.GetComponent<VehicleHealth>(), renderers = go.GetComponentsInChildren<Renderer>(true) };
        cars.Add(nc);
        return nc;
    }

    Car GetPooledCar()
    {
        foreach (Car c in cars) if (!c.active && !c.bus && c.go != null) return c;
        int carsMade = 0; foreach (Car c in cars) if (!c.bus) carsMade++;
        if (carsMade >= Budgets[2, 1] + Budgets[2, 2]) return null;
        GameObject prefab = carPrefabs[rng.Next(carPrefabs.Length)];
        if (prefab == null) return null;
        GameObject go = Instantiate(prefab, transform);
        go.SetActive(false);
        go.name = prefab.name + "_" + cars.Count;
        int ci = rng.Next(paintColours.Length);
        Repaint(go, paintColours[ci], ci);
        LundunTrafficCar ai = go.GetComponent<LundunTrafficCar>();
        if (ai == null) ai = go.AddComponent<LundunTrafficCar>();
        Car nc = new Car { go = go, ai = ai, cc = go.GetComponent<CarController>(), rb = go.GetComponent<Rigidbody>(),
                           health = go.GetComponent<VehicleHealth>(), renderers = go.GetComponentsInChildren<Renderer>(true) };
        cars.Add(nc);
        return nc;
    }

    /// Swaps paint materials (names containing "Paint" or "Roof") for a shared coloured copy — cheap to draw.
    void Repaint(GameObject go, Color colour, int colourIndex)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || !(m.name.Contains("Paint") || m.name.Contains("Roof"))) continue;
                Material[] set;
                if (!paintCache.TryGetValue(m, out set)) { set = new Material[paintColours.Length]; paintCache[m] = set; }
                if (set[colourIndex] == null)
                {
                    set[colourIndex] = new Material(m);
                    set[colourIndex].name = m.name + "_" + colourIndex;
                    set[colourIndex].SetColor("_BaseColor", colour);
                }
                mats[i] = set[colourIndex];
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    bool InOldStreet(NavPoint p)
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null || nav.World.existingStreet == null) return false;
        LwExistingStreet e = nav.World.existingStreet;
        return Mathf.Abs((float)p.x - e.centreX) < e.sizeX / 2f + 12f && Mathf.Abs((float)p.z - e.centreZ) < e.sizeZ / 2f + 12f;
    }

    static bool SeenCar(Car c, Camera cam)
    {
        foreach (Renderer r in c.renderers) if (r != null && r.isVisible) return true;
        return false;
    }

    /// Which look suits this spot: "Suit" in Knightsbury, "Street" on the estate / in Peckam, "Work" at the warehouses.
    string LookFor(Vector3 at)
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null || nav.World.districts == null) return null;
        foreach (LwPoly d in nav.World.districts)
        {
            if (!InPoly(at.x, at.z, d.polygon)) continue;
            switch (d.id)
            {
                case "D_KNIGHT": return "Suit";
                case "D_GILMOR": case "D_PECKAM": return "Street";
                case "D_CANARY": return "Work";
                default: return null;
            }
        }
        return null;
    }

    static bool InPoly(float x, float z, LwPoint[] p)
    {
        bool c = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if (((p[i].z > z) != (p[j].z > z)) && (x < (p[j].x - p[i].x) * (z - p[i].z) / (p[j].z - p[i].z) + p[i].x)) c = !c;
        return c;
    }

    bool FindPavementSpot(Vector3 player, Camera cam, out Vector3 pos)
    {
        pos = Vector3.zero;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return false;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            NavPoint p, dir; double halfWidth, pave;
            if (!nav.Graph.RandomRoadPoint(new NavPoint(player.x, player.z), spawnNearest, spawnFarthest, rng, true, out p, out dir, out halfWidth, out pave)) return false;
            float side = rng.NextDouble() < 0.5 ? -1f : 1f;
            Vector3 left = new Vector3(-(float)dir.z, 0f, (float)dir.x);
            Vector3 c = new Vector3((float)p.x, 0.5f, (float)p.z) + left * side * (float)(halfWidth + pave * 0.5);
            if (cam != null && LundunView.CanSee(cam, c)) continue;
            if (!UnityEngine.AI.NavMesh.SamplePosition(c, out UnityEngine.AI.NavMeshHit hit, 2f, 1 << 4)) continue;   // pavement area only
            pos = hit.position;
            return true;
        }
        return false;
    }

    static bool Seen(Ped p, Camera cam)
    {
        foreach (Renderer r in p.renderers) if (r != null && r.isVisible) return true;
        return false;
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    // ------------------------------------------------------------------ budget helpers
    /// How busy the streets are at a given hour (0..1).
    public static float DensityAt(float h)
    {
        float[] hours = { 0f, 3f, 5f, 7f, 9f, 12f, 17f, 19f, 22f, 24f };
        float[] dens = { 0.55f, 0.35f, 0.35f, 0.6f, 0.9f, 0.8f, 1f, 1f, 0.75f, 0.55f };
        for (int i = 0; i + 1 < hours.Length; i++)
            if (h <= hours[i + 1]) return Mathf.Lerp(dens[i], dens[i + 1], Mathf.InverseLerp(hours[i], hours[i + 1], h));
        return dens[0];
    }

    void Adapt()
    {
        if (!adaptive || Time.timeScale == 0f) return;
        if (frameAvg > slowFrame) { slowFor += tickEvery; fastFor = 0f; } else if (frameAvg < okFrame) { fastFor += tickEvery; slowFor = 0f; }
        if (slowFor >= 3f) { Scale = Mathf.Max(0.4f, Scale * 0.85f); slowFor = 0f; }
        if (fastFor >= 10f) { Scale = Mathf.Min(1f, Scale * 1.05f); fastFor = 0f; }
    }

    void MakeStats()
    {
        Canvas canvas = null;
        foreach (Canvas c in FindObjectsByType<Canvas>()) if (c.isRootCanvas) { canvas = c; break; }
        if (canvas == null) return;
        GameObject g = new GameObject("PopulationStats", typeof(RectTransform));
        g.transform.SetParent(canvas.transform, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -8f); rt.sizeDelta = new Vector2(700f, 34f);
        stats = g.AddComponent<TextMeshProUGUI>();
        stats.fontSize = 22; stats.alignment = TextAlignmentOptions.Center;
        stats.color = new Color(1f, 1f, 1f, 0.75f); stats.raycastTarget = false;
    }
}

/// Shared "can the camera see this spot?" test: on screen and not hidden behind a building.
public static class LundunView
{
    static int mask = -1;
    public static bool CanSee(Camera cam, Vector3 p)
    {
        Vector3 v = cam.WorldToViewportPoint(p + Vector3.up);
        if (!(v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f)) return false;
        if (mask == -1) mask = LayerMask.GetMask("Default");
        return !Physics.Linecast(cam.transform.position, p + Vector3.up * 1.5f, mask, QueryTriggerInteraction.Ignore);
    }
}
