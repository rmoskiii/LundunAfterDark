using UnityEngine;

/// E1: what the game needs to know about an expansion area built by its own menu (Brixtun: Lundun → 28), kept on the
/// area's root so re-running the city's menus never loses it:
///   lamps     its street lamps (the real lamp lights near the player move to these too)
///   stations  its Underground entrances (they join the Tube's fast travel and the maps)
///   glow      its dressing's lit signs (brighter at night, like the city's) and its Belisha beacons (blinking)
///   E3: street life   its bike docks (join Lundun Cycles), its parking bays kept for real cars (join LundunParking) and its
///             static parked cars (switched off far away, like the city's), its police spawn points (join PoliceDirector's)
public class LundunExpansionArea : MonoBehaviour
{
    [SerializeField] private Material glowMaterial, belishaGlobe;
    [SerializeField] private float dayGlow = 0.35f, nightGlow = 1.5f, blinkEvery = 0.6f;
    [SerializeField] private Color belishaOn = new Color(1f, 0.54f, 0f) * 4f;
    [SerializeField] private string areaName = "";
    [SerializeField] private Vector3[] lamps = new Vector3[0];
    [SerializeField] private int[] lampStyles = new int[0];
    [SerializeField] private string[] stationNames = new string[0];
    [SerializeField] private Vector3[] stationTops = new Vector3[0];
    [SerializeField] private Vector3[] stationOut = new Vector3[0];     // the way out of each entrance, onto the street
    [Header("E3: street life")]
    [SerializeField] private Vector4[] bikeDocks = new Vector4[0];      // x, z, along-the-kerb x, z
    [SerializeField] private Vector4[] bikeDocksOut = new Vector4[0];   // towards the road x, z
    [SerializeField] private Vector4[] parkingBays = new Vector4[0];    // bays kept for real cars: x, z, facing (degrees), length
    [SerializeField] private Renderer[] parkedBlocks = new Renderer[0];
    [SerializeField] private Transform[] policeSpawns = new Transform[0];
    [SerializeField] private float parkedDrawDistance = 170f;

    public string AreaName { get { return areaName; } }
    public Vector3[] Lamps { get { return lamps; } }
    public int[] LampStyles { get { return lampStyles; } }
    public string[] StationNames { get { return stationNames; } }
    public Vector3[] StationTops { get { return stationTops; } }
    public Vector3[] StationOut { get { return stationOut; } }
    public Vector4[] BikeDocks { get { return bikeDocks; } }
    public Vector4[] BikeDocksOut { get { return bikeDocksOut; } }
    public Vector4[] ParkingBays { get { return parkingBays; } }
    public Transform[] PoliceSpawns { get { return policeSpawns; } }

    private Material glowRuntime, globeRuntime;
    private LundunLighting lighting;
    private float nextCheck, nextBlink, nextCull;
    private bool blinkOn;

    public void SetLook(Material glow, Material globe) { glowMaterial = glow; belishaGlobe = globe; }

    void Awake()
    {
        // runtime copies, so playing never edits the material files
        if (glowMaterial != null) glowRuntime = new Material(glowMaterial);
        if (belishaGlobe != null) { globeRuntime = new Material(belishaGlobe); globeRuntime.EnableKeyword("_EMISSION"); }
        if (glowRuntime == null && globeRuntime == null) return;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            Material m = r.sharedMaterial;
            if (glowRuntime != null && m == glowMaterial) r.sharedMaterial = glowRuntime;
            else if (globeRuntime != null && m == belishaGlobe) r.sharedMaterial = globeRuntime;
        }
    }

    void OnDestroy()
    {
        if (glowRuntime != null) Destroy(glowRuntime);
        if (globeRuntime != null) Destroy(globeRuntime);
    }

    void Update()
    {
        CullParked();
        if (globeRuntime != null && Time.time >= nextBlink)
        {
            nextBlink = Time.time + blinkEvery;
            blinkOn = !blinkOn;
            globeRuntime.SetColor("_EmissionColor", blinkOn ? belishaOn : Color.black);
        }
        if (glowRuntime == null || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        if (lighting == null) lighting = FindAnyObjectByType<LundunLighting>();
        float level = lighting != null ? lighting.Current.lampLevel : 1f;
        glowRuntime.SetColor("_EmissionColor", Color.white * Mathf.Lerp(dayGlow, nightGlow, level));
    }

    /// E3: the static parked cars further than parkedDrawDistance from the camera aren't drawn.
    void CullParked()
    {
        if (parkedBlocks.Length == 0 || Time.unscaledTime < nextCull) return;
        nextCull = Time.unscaledTime + 0.5f;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;
        float d2 = parkedDrawDistance * parkedDrawDistance;
        foreach (Renderer r in parkedBlocks)
        {
            if (r == null) continue;
            bool on = (r.bounds.ClosestPoint(c) - c).sqrMagnitude < d2;
            if (r.enabled != on) r.enabled = on;
        }
    }

    /// E3: set by the area's menu.
    public void SetStreetLife(Vector4[] docks, Vector4[] docksOut, Vector4[] bays, Renderer[] parked, Transform[] police)
    {
        bikeDocks = docks ?? new Vector4[0]; bikeDocksOut = docksOut ?? new Vector4[0]; parkingBays = bays ?? new Vector4[0];
        parkedBlocks = parked ?? new Renderer[0]; policeSpawns = police ?? new Transform[0];
    }

    public void SetData(string area, Vector3[] lampPositions, int[] styles, string[] names, Vector3[] tops, Vector3[] outDirs)
    {
        areaName = area; lamps = lampPositions; lampStyles = styles; stationNames = names; stationTops = tops; stationOut = outDirs;
    }
}
