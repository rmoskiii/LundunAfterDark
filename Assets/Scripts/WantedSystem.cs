using UnityEngine;

public class WantedSystem : MonoBehaviour
{
    public static WantedSystem Instance { get; private set; }

    [Header("Heat points per crime")]
    [SerializeField] private float gunfirePerShot = 2f;
    [SerializeField] private float assault = 25f;
    [SerializeField] private float hitAndRun = 30f;
    [SerializeField] private float kill = 60f;

    [Header("Heat needed for each star")]
    [SerializeField] private float[] starThresholds = { 20f, 120f, 300f, 600f, 1000f };

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
        foreach (float threshold in starThresholds)
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
}