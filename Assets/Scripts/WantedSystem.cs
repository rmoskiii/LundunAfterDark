using UnityEngine;

public class WantedSystem : MonoBehaviour
{
    public static WantedSystem Instance { get; private set; }

    [Header("Heat points per crime")]
    [SerializeField] private float gunfirePerShot = 2f;
    [SerializeField] private float assault = 25f;
    [SerializeField] private float hitAndRun = 30f;
    [SerializeField] private float kill = 60f;
    [SerializeField] private float assaultOfficer = 40f;
    [SerializeField] private float killOfficer = 120f;
    [SerializeField] private float destroyPoliceCar = 100f;

    [Header("Heat needed for each star")]
    // 2 kills = 2 stars. Kill a cop on top of that = 3 stars.
    [SerializeField] private float[] heatForStar = { 20f, 100f, 220f, 400f, 700f };

    [Header("Escaping")]
    [SerializeField] private float baseEscapeTime = 10f;
    [SerializeField] private float extraTimePerStar = 4f;

    [SerializeField] private VehicleSystem vehicleSystem;

    private float heat;
    private float timeSinceSeen;
    private bool escapeTimerRunning;

    public int Stars { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }
    public Transform PlayerTarget => vehicleSystem.PlayerTarget;
    public bool IsSearching => Stars > 0 && escapeTimerRunning && timeSinceSeen > 1f;
    private float EscapeTime => baseEscapeTime + extraTimePerStar * (Stars - 1);

    void Awake()
    {
        Instance = this;
    }

    public void AddCrime(CrimeType type, Vector3 position)
    {
        heat += type switch
        {
            CrimeType.Gunfire => gunfirePerShot,
            CrimeType.AssaultPedestrian => assault,
            CrimeType.HitAndRun => hitAndRun,
            CrimeType.KillPedestrian => kill,
            CrimeType.AssaultOfficer => assaultOfficer,
            CrimeType.KillOfficer => killOfficer,
            CrimeType.DestroyPoliceCar => destroyPoliceCar,
            _ => 0f
        };

        LastKnownPosition = PlayerTarget.position;
        timeSinceSeen = 0f;
        UpdateStars();
    }

    // Police call this every frame they can see the player
    public void ReportSighting(Vector3 position)
    {
        LastKnownPosition = position;
        timeSinceSeen = 0f;
        escapeTimerRunning = true;
    }

    // Police call this when they reach the last known position and find nothing
    public void BeginSearch()
    {
        escapeTimerRunning = true;
    }

    void Update()
    {
        if (Stars == 0 || !escapeTimerRunning) return;

        timeSinceSeen += Time.deltaTime;
        if (timeSinceSeen >= EscapeTime) ClearWanted();
    }

    void UpdateStars()
    {
        int stars = 0;
        foreach (float threshold in heatForStar)
            if (heat >= threshold) stars++;

        // Stars only go up while you're wanted. They clear all at once when you escape.
        Stars = Mathf.Max(Stars, stars);
    }

    public void ClearWanted()
    {
        heat = 0f;
        Stars = 0;
        escapeTimerRunning = false;
    }

    // TESTING: in Play mode, click the ⋮ on this component and choose "Add a star"
    [ContextMenu("Add a star")]
    void AddStarForTesting()
    {
        if (Stars >= heatForStar.Length) return;
        heat = Mathf.Max(heat, heatForStar[Stars]);
        LastKnownPosition = PlayerTarget.position;
        timeSinceSeen = 0f;
        UpdateStars();
    }
}
