using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// W1b: everyday police. With no stars, pairs of unarmed officers walk a beat on the pavements, and now and then
/// a patrol car drives around with the traffic (no siren). Like everyone else they only exist near James: they
/// appear out of view and are put away when far behind and unseen.
///   • busier near police stations and Tube stations; quieter elsewhere and on foot late at night
///   • they're natural witnesses: commit a crime in front of them and they react at once
///   • when police learn of a crime, patrols nearby respond — from further away the more serious it is
///   • a patrol that joins a chase counts as one of that wanted level's units; afterwards it goes back on the beat
/// Added automatically next to the PoliceDirector. All numbers tunable in the Inspector.
public class LundunPatrols : MonoBehaviour
{
    public static LundunPatrols Instance { get; private set; }

    [Header("Foot patrols (pairs of officers)")]
    [Tooltip("Near a police station or Tube station, by day")]
    [SerializeField] private int busyPatrols = 2;
    [Tooltip("Everywhere else, by day")]
    [SerializeField] private int quietPatrols = 1;
    [Tooltip("One fewer foot patrol between these hours")]
    [SerializeField] private Vector2 nightHours = new Vector2(23f, 6f);
    [SerializeField] private float busyNearPoliceStation = 300f;
    [SerializeField] private float busyNearTube = 200f;
    [SerializeField] private float footSpawnNearest = 60f, footSpawnFarthest = 130f, footDespawnBeyond = 170f;

    [Header("Patrol cars")]
    [SerializeField] private int carsByDay = 1;
    [SerializeField] private int carsAtNight = 2;
    [SerializeField] private float carSpawnNearest = 100f, carSpawnFarthest = 180f, carDespawnBeyond = 260f;

    [Header("Responding to crimes: how far away patrols come from")]
    [SerializeField] private float respondMinor = 0f;          // scuffles: only the officers who saw it
    [SerializeField] private float respondGunfire = 150f;
    [SerializeField] private float respondSerious = 150f;      // assault, hit-and-run
    [SerializeField] private float respondKill = 200f;
    [SerializeField] private float respondOfficer = 300f;      // attacking an officer / police car
    [Tooltip("Patrol cars respond from this much further than officers on foot")]
    [SerializeField] private float carRangeFactor = 1.5f;

    [SerializeField] private float tickEvery = 1f;

    private readonly List<PoliceOfficer[]> foot = new List<PoliceOfficer[]>();
    private readonly List<PoliceAI> cars = new List<PoliceAI>();
    private static readonly System.Random rng = new System.Random();
    private float nextTick;

    void Awake() { Instance = this; }
    void OnEnable() { WantedSystem.CrimeKnown += OnCrimeKnown; }
    void OnDisable() { WantedSystem.CrimeKnown -= OnCrimeKnown; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + tickEvery;
        WantedSystem w = WantedSystem.Instance;
        if (w == null || w.PlayerTarget == null || VehicleSystem.Locked) return;
        PoliceAI carPrefab = PoliceDirector.CarPrefab;
        if (carPrefab == null) return;

        Vector3 player = w.PlayerTarget.position;
        Tidy(player);
        if (w.Stars > 0) return;                       // no new patrols mid-chase (the director handles that)

        bool night = IsNight();
        int wantFoot = Busy(player) ? busyPatrols : quietPatrols;
        if (night) wantFoot = Mathf.Max(0, wantFoot - 1);
        int wantCars = night ? carsAtNight : carsByDay;

        if (foot.Count < wantFoot && carPrefab.OfficerPrefab != null) SpawnFoot(carPrefab.OfficerPrefab, player);
        else if (cars.Count < wantCars) SpawnCar(carPrefab, player);
    }

    // ------------------------------------------------------------------ who's around
    bool IsNight()
    {
        float h = LundunClock.Instance != null ? LundunClock.Instance.Hour : 12f;
        return nightHours.x > nightHours.y ? (h >= nightHours.x || h < nightHours.y) : (h >= nightHours.x && h < nightHours.y);
    }

    bool Busy(Vector3 at)
    {
        LundunPlaces places = LundunPlaces.Instance;
        if (places != null && places.Places != null)
            foreach (LundunPlaces.Place p in places.Places)
                if (p != null && p.kind == "police" && Flat(p.door, at) < busyNearPoliceStation) return true;
        LundunTube tube = LundunTube.Instance;
        if (tube != null && tube.Markers != null)
            foreach (Transform m in tube.Markers)
                if (m != null && Flat(m.position, at) < busyNearTube) return true;
        return false;
    }

    /// Forget the dead and gone; put away patrols far behind James that nobody can see.
    void Tidy(Vector3 player)
    {
        for (int i = foot.Count - 1; i >= 0; i--)
        {
            PoliceOfficer[] g = foot[i];
            bool anyAlive = false, allOnBeat = true, farAndHidden = true;
            foreach (PoliceOfficer o in g)
            {
                if (o == null || o.IsDown) continue;
                anyAlive = true;
                if (!o.OnBeat) allOnBeat = false;
                if (Flat(o.transform.position, player) < footDespawnBeyond || Visible(o.gameObject)) farAndHidden = false;
            }
            if (!anyAlive) { foot.RemoveAt(i); continue; }
            if (allOnBeat && farAndHidden)
            {
                foreach (PoliceOfficer o in g) if (o != null) Destroy(o.gameObject);
                foot.RemoveAt(i);
            }
        }
        for (int i = cars.Count - 1; i >= 0; i--)
        {
            PoliceAI c = cars[i];
            if (c == null || !c.enabled) { cars.RemoveAt(i); continue; }      // wrecked or removed
            if (c.OnPatrol && !c.OfficersOut && Flat(c.transform.position, player) > carDespawnBeyond && !Visible(c.gameObject))
            {
                Destroy(c.gameObject);
                cars.RemoveAt(i);
            }
        }
    }

    // ------------------------------------------------------------------ spawning
    void SpawnFoot(PoliceOfficer prefab, Vector3 player)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (!PavementPoint(player, footSpawnNearest, footSpawnFarthest, out Vector3 p)) return;
            if (OnCamera(p)) continue;
            Quaternion facing = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            PoliceOfficer lead = Instantiate(prefab, p, facing);
            lead.StartPatrol(null);
            PoliceOfficer second = null;
            if (NavMesh.SamplePosition(p + facing * Vector3.right * 1.1f, out NavMeshHit h, 2f, NavMesh.AllAreas))
            {
                second = Instantiate(prefab, h.position, facing);
                second.StartPatrol(lead);
            }
            foot.Add(second != null ? new[] { lead, second } : new[] { lead });
            return;
        }
    }

    void SpawnCar(PoliceAI prefab, Vector3 player)
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            NavPoint p, dir; double halfWidth;
            if (!nav.Graph.RandomRoadPoint(new NavPoint(player.x, player.z), carSpawnNearest, carSpawnFarthest, rng, out p, out dir, out halfWidth)) return;
            Vector3 along = new Vector3((float)dir.x, 0f, (float)dir.z);
            if (Random.value < 0.5f) along = -along;
            Vector3 left = new Vector3(-along.z, 0f, along.x);
            Vector3 spot = new Vector3((float)p.x, 1f, (float)p.z) + left * (float)(halfWidth * 0.5);      // UK: left lane
            if (OnCamera(spot)) continue;
            if (Physics.CheckSphere(spot + Vector3.up, 6f, LayerMask.GetMask("Vehicle", "Player"), QueryTriggerInteraction.Ignore)) continue;
            if (!NavMesh.SamplePosition(spot, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
            PoliceAI car = Instantiate(prefab, hit.position + Vector3.up * 0.8f, Quaternion.LookRotation(along, Vector3.up));
            car.Armed = false;
            car.StartPatrol();
            cars.Add(car);
            return;
        }
    }

    // ------------------------------------------------------------------ responding (by how serious)
    void OnCrimeKnown(CrimeType type, Vector3 where)
    {
        float r = Radius(type);
        if (r <= 0f) return;
        foreach (PoliceOfficer[] g in foot)
            foreach (PoliceOfficer o in g)
                if (o != null && !o.IsDown && o.OnBeat && Flat(o.transform.position, where) < r) o.Respond();
        float rc = r * carRangeFactor;
        foreach (PoliceAI c in cars)
            if (c != null && c.OnPatrol && Flat(c.transform.position, where) < rc)
            {
                c.Engage();
                PoliceDirector.Adopt(c);
            }
    }

    float Radius(CrimeType type)
    {
        switch (type)
        {
            case CrimeType.AssaultPedestrian: return respondMinor;
            case CrimeType.Shoplifting: return respondMinor;          // E5
            case CrimeType.Gunfire: return respondGunfire;
            case CrimeType.HitAndRun: return respondSerious;
            case CrimeType.KillPedestrian: return respondKill;
            default: return respondOfficer;          // officer assaulted / killed, police car destroyed
        }
    }

    // ------------------------------------------------------------------ helpers (also used by officers / cars on patrol)
    /// A point on a pavement between near and far metres from centre.
    public static bool PavementPoint(Vector3 centre, float near, float far, out Vector3 point)
    {
        point = centre;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return false;
        NavPoint p, dir; double halfWidth, pave;
        if (!nav.Graph.RandomRoadPoint(new NavPoint(centre.x, centre.z), near, far, rng, true, out p, out dir, out halfWidth, out pave)) return false;
        Vector3 along = new Vector3((float)dir.x, 0f, (float)dir.z);
        Vector3 side = new Vector3(-along.z, 0f, along.x) * (Random.value < 0.5f ? 1f : -1f);
        Vector3 spot = new Vector3((float)p.x, 0.5f, (float)p.z) + side * (float)(halfWidth + pave * 0.5);
        if (!NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return false;
        point = hit.position;
        return true;
    }

    /// A point on a road (left lane) between near and far metres from centre.
    public static bool RoadPoint(Vector3 centre, float near, float far, out Vector3 point)
    {
        point = centre;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return false;
        NavPoint p, dir; double halfWidth;
        if (!nav.Graph.RandomRoadPoint(new NavPoint(centre.x, centre.z), near, far, rng, out p, out dir, out halfWidth)) return false;
        point = new Vector3((float)p.x, centre.y, (float)p.z);
        return true;
    }

    static bool OnCamera(Vector3 p)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        Vector3 v = cam.WorldToViewportPoint(p + Vector3.up);
        bool onScreen = v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        if (!onScreen) return false;
        return !Physics.Linecast(cam.transform.position, p + Vector3.up * 1.5f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);
    }

    static bool Visible(GameObject g)
    {
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>())
            if (r.isVisible) return true;
        return false;
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
}
