using System.Collections.Generic;
using UnityEngine;

/// Runs the street dressing built by Lundun → 13  Dress the City:
/// - signs, shop windows and billboards glow brighter as it gets dark (follows the clock's lamp level)
/// - Belisha beacons blink (one shared material, so all of them for the price of one)
/// - far-away dressing blocks are switched off (they're tiny at that distance anyway — saves battery)
/// - knows where the bus shelters are, so buses pull in at them
public class LundunDressing : MonoBehaviour
{
    public static LundunDressing Instance { get; private set; }

    [Header("Filled in by Lundun → 13")]
    [SerializeField] private Vector4[] busStops = new Vector4[0];        // x, z, direction of travel x, z
    [SerializeField] private Material glowMaterial;
    [SerializeField] private Material belishaGlobe;
    [SerializeField] private string[] stationNames = new string[0];
    [SerializeField] private Vector3[] stationPositions = new Vector3[0];
    [SerializeField] private Vector4[] bikeDocks = new Vector4[0];        // R9: x, z, along-the-kerb x, z
    [SerializeField] private Vector4[] bikeDocksOut = new Vector4[0];     //     towards the road x, z
    [SerializeField] private Mesh bikeFrame, bikeWheel;
    [SerializeField] private Material bikeMaterial;

    [Header("Look")]
    [SerializeField] private float dayGlow = 0.35f;
    [SerializeField] private float nightGlow = 1.5f;
    [SerializeField] private float blinkEvery = 0.6f;
    [SerializeField] private Color belishaOn = new Color(1f, 0.54f, 0f) * 4f;

    [Header("Battery")]
    [SerializeField] private float drawDistance = 190f;

    private Material glowRuntime, globeRuntime;
    private readonly List<Renderer> blocks = new List<Renderer>();
    private readonly List<Vector3> blockCentres = new List<Vector3>();
    private LundunLighting lighting;
    private float nextCheck, nextBlink;
    private bool blinkOn;

    public int BusStopCount { get { return busStops.Length; } }
    public string[] StationNames { get { return stationNames; } }
    public Vector3[] StationPositions { get { return stationPositions; } }

    public void SetBikes(Vector4[] docks, Vector4[] docksOut, Mesh frame, Mesh wheel, Material mat)
    {
        bikeDocks = docks; bikeDocksOut = docksOut; bikeFrame = frame; bikeWheel = wheel; bikeMaterial = mat;
    }

    public void SetData(Vector4[] stops, Material glow, Material globe, string[] names, Vector3[] stations)
    {
        busStops = stops; glowMaterial = glow; belishaGlobe = globe; stationNames = names; stationPositions = stations;
    }

    void Awake()
    {
        Instance = this;
        // R8: the Underground as fast travel between these entrances
        if (stationNames.Length > 1)
        {
            LundunTube tube = GetComponent<LundunTube>();
            if (tube == null) tube = gameObject.AddComponent<LundunTube>();
            tube.Init(stationNames, stationPositions);
            // E1: the expansion areas' stations (built by their own menus)
            foreach (LundunExpansionArea area in FindObjectsByType<LundunExpansionArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                for (int i = 0; i < area.StationNames.Length && i < area.StationTops.Length; i++)
                    tube.AddStation(area.StationNames[i], area.StationTops[i], i < area.StationOut.Length ? area.StationOut[i] : Vector3.forward);
        }
        // R9: Lundun Cycles (E3: with the expansion areas' docks too)
        List<Vector4> docks = new List<Vector4>(bikeDocks), docksOut = new List<Vector4>(bikeDocksOut);
        while (docksOut.Count < docks.Count) docksOut.Add(new Vector4(0f, 1f, 0f, 0f));
        foreach (LundunExpansionArea area in FindObjectsByType<LundunExpansionArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            for (int i = 0; i < area.BikeDocks.Length; i++)
            {
                docks.Add(area.BikeDocks[i]);
                docksOut.Add(i < area.BikeDocksOut.Length ? area.BikeDocksOut[i] : new Vector4(0f, 1f, 0f, 0f));
            }
        if (docks.Count > 0 && bikeFrame != null && bikeWheel != null)
        {
            LundunBikes bikes = GetComponent<LundunBikes>();
            if (bikes == null) bikes = gameObject.AddComponent<LundunBikes>();
            bikes.Init(docks.ToArray(), docksOut.ToArray(), bikeFrame, bikeWheel, bikeMaterial);
        }
        // runtime copies, so playing never edits the material files
        if (glowMaterial != null) glowRuntime = new Material(glowMaterial);
        if (belishaGlobe != null) { globeRuntime = new Material(belishaGlobe); globeRuntime.EnableKeyword("_EMISSION"); }
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            Material m = r.sharedMaterial;
            if (glowRuntime != null && m == glowMaterial) r.sharedMaterial = glowRuntime;
            else if (globeRuntime != null && m == belishaGlobe) r.sharedMaterial = globeRuntime;
            blocks.Add(r);
            blockCentres.Add(r.bounds.center);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (glowRuntime != null) Destroy(glowRuntime);
        if (globeRuntime != null) Destroy(globeRuntime);
    }

    void Update()
    {
        if (globeRuntime != null && Time.time >= nextBlink)
        {
            nextBlink = Time.time + blinkEvery;
            blinkOn = !blinkOn;
            globeRuntime.SetColor("_EmissionColor", blinkOn ? belishaOn : Color.black);
        }
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;

        if (glowRuntime != null)
        {
            if (lighting == null) lighting = FindAnyObjectByType<LundunLighting>();
            float level = lighting != null ? lighting.Current.lampLevel : 1f;
            glowRuntime.SetColor("_EmissionColor", Color.white * Mathf.Lerp(dayGlow, nightGlow, level));
        }

        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;
        float far = drawDistance + 75f;          // blocks are 100 m: measure to the centre, allow half a diagonal
        for (int i = 0; i < blocks.Count; i++)
        {
            Vector3 d = blockCentres[i] - c; d.y = 0f;
            bool show = d.sqrMagnitude < far * far;
            if (blocks[i].enabled != show) blocks[i].enabled = show;
        }
    }

    /// Nearest bus shelter ahead on this side of the road (for a bus heading along forward). ahead = metres to go.
    public bool StopAhead(Vector3 pos, Vector3 forward, out float ahead)
    {
        ahead = float.MaxValue;
        forward.y = 0f; forward.Normalize();
        for (int i = 0; i < busStops.Length; i++)
        {
            Vector4 s = busStops[i];
            if (s.z * forward.x + s.w * forward.z < 0.8f) continue;                 // other direction
            float dx = s.x - pos.x, dz = s.y - pos.z;
            float along = dx * forward.x + dz * forward.z;
            float side = Mathf.Abs(dx * forward.z - dz * forward.x);
            if (along < -1f || along > 30f || side > 6f) continue;
            if (along < ahead) ahead = along;
        }
        return ahead < float.MaxValue;
    }
}
