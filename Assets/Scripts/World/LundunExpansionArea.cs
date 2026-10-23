using UnityEngine;

/// E1: what the game needs to know about an expansion area built by its own menu (Brixtun: Lundun → 28), kept on the
/// area's root so re-running the city's menus never loses it:
///   lamps     its street lamps (the real lamp lights near the player move to these too)
///   stations  its Underground entrances (they join the Tube's fast travel and the maps)
///   glow      its dressing's lit signs (brighter at night, like the city's) and its Belisha beacons (blinking)
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

    public string AreaName { get { return areaName; } }
    public Vector3[] Lamps { get { return lamps; } }
    public int[] LampStyles { get { return lampStyles; } }
    public string[] StationNames { get { return stationNames; } }
    public Vector3[] StationTops { get { return stationTops; } }
    public Vector3[] StationOut { get { return stationOut; } }

    private Material glowRuntime, globeRuntime;
    private LundunLighting lighting;
    private float nextCheck, nextBlink;
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

    public void SetData(string area, Vector3[] lampPositions, int[] styles, string[] names, Vector3[] tops, Vector3[] outDirs)
    {
        areaName = area; lamps = lampPositions; lampStyles = styles; stationNames = names; stationTops = tops; stationOut = outDirs;
    }
}
