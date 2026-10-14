using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// GPS: holds the current waypoint, keeps a route to it along the roads (re-planned every half second,
/// so a wrong turn just re-routes), shows the waypoint beam in the world and says "Arrived".
/// Missions can call LundunNavigation.Instance.SetDestination(position, "name") later.
public class LundunNavigation : MonoBehaviour
{
    public static LundunNavigation Instance { get; private set; }

    public class Destination { public string id, name, kind; public Vector3 position; }

    [SerializeField] private TextAsset worldJson;
    [SerializeField] private Transform waypointBeam;        // tall purple beam at the waypoint
    [SerializeField] private TextMeshProUGUI toast;          // "Arrived" message
    [SerializeField] private float arriveDistance = 18f;
    [SerializeField] private float replanEvery = 0.5f;

    public bool HasWaypoint { get; private set; }
    public Vector3 Waypoint { get; private set; }
    public string WaypointName { get; private set; }
    public float RouteMetres { get; private set; }
    public List<Vector3> Route { get; private set; }
    /// Goes up by one every time the route changes (the route lines watch this).
    public int RouteVersion { get; private set; }
    public List<Destination> Destinations { get; private set; }
    public LundunWorldData World { get; private set; }
    public LundunRoadGraph Graph { get { return graph; } }

    private LundunRoadGraph graph;
    private VehicleSystem vehicles;
    private float nextPlan, toastUntil;

    void Awake()
    {
        Instance = this;
        Route = new List<Vector3>();
        Destinations = new List<Destination>();
        if (worldJson != null)
        {
            World = JsonUtility.FromJson<LundunWorldData>(worldJson.text);
            graph = new LundunRoadGraph(World);
            foreach (LwPoi p in World.pois)
                Destinations.Add(new Destination { id = p.id, name = p.name, kind = p.kind, position = new Vector3(p.x, 0f, p.z) });
        }
        else Debug.LogWarning("[Lundun] LundunNavigation has no world JSON — GPS off.");
        if (waypointBeam != null) waypointBeam.gameObject.SetActive(false);
        if (toast != null) toast.gameObject.SetActive(false);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() { vehicles = FindAnyObjectByType<VehicleSystem>(); }

    /// Sets the waypoint. snapToRoad = move it onto the nearest road (used for taps on the map).
    public void SetDestination(Vector3 position, string name, bool snapToRoad = false)
    {
        if (graph == null) return;
        if (snapToRoad)
        {
            int e; double t, d;
            NavPoint p = graph.NearestOnRoad(new NavPoint(position.x, position.z), out e, out t, out d);
            if (d < 80) position = new Vector3((float)p.x, 0f, (float)p.z);
        }
        HasWaypoint = true;
        Waypoint = new Vector3(position.x, 0f, position.z);
        WaypointName = string.IsNullOrEmpty(name) ? "Waypoint" : name;
        if (waypointBeam != null)
        {
            waypointBeam.position = new Vector3(Waypoint.x, waypointBeam.localScale.y, Waypoint.z);
            waypointBeam.gameObject.SetActive(true);
        }
        Plan();
    }

    public void Clear()
    {
        HasWaypoint = false;
        Route.Clear();
        RouteMetres = 0f;
        RouteVersion++;
        if (waypointBeam != null) waypointBeam.gameObject.SetActive(false);
    }

    void Update()
    {
        if (toast != null && toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);
        if (!HasWaypoint || vehicles == null) return;

        Vector3 me = vehicles.PlayerTarget.position;
        Vector2 flat = new Vector2(me.x - Waypoint.x, me.z - Waypoint.z);
        if (flat.magnitude < arriveDistance)
        {
            ShowToast("Arrived: " + WaypointName);
            Clear();
            return;
        }
        if (Time.unscaledTime >= nextPlan) Plan();
    }

    void Plan()
    {
        nextPlan = Time.unscaledTime + replanEvery;
        if (graph == null) return;
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        if (vehicles == null) return;
        Vector3 me = vehicles.PlayerTarget.position;
        double metres;
        List<NavPoint> pts = graph.Route(new NavPoint(me.x, me.z), new NavPoint(Waypoint.x, Waypoint.z), out metres);
        Route.Clear();
        foreach (NavPoint p in pts) Route.Add(new Vector3((float)p.x, 0f, (float)p.z));
        RouteMetres = (float)metres;
        RouteVersion++;
    }

    public void ShowToast(string message)
    {
        if (toast == null) return;
        toast.text = message;
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 2.5f;
    }
}
