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
    [SerializeField] private float spawnNearest = 45f;
    [SerializeField] private float spawnFarthest = 110f;
    [SerializeField] private float despawnBeyond = 140f;
    [SerializeField] private float tickEvery = 0.25f;       // how often the manager thinks (not every frame)
    [SerializeField] private int spawnsPerTick = 2;

    [Header("Safety net: shrink the budget if frame rate drops")]
    [SerializeField] private bool adaptive = true;
    [SerializeField] private float slowFrame = 0.037f;      // ~27 fps
    [SerializeField] private float okFrame = 0.034f;

    [Header("Testing")]
    [SerializeField] private bool showStats = true;

    // Budgets: pedestrians, moving cars, parked cars, buses (cars/buses used from R2)
    static readonly int[,] Budgets = { { 10, 6, 12, 1 }, { 18, 10, 20, 2 }, { 26, 14, 28, 2 } };

    public int PedestrianBudget { get { return Budgets[(int)budget, 0]; } }
    public int MovingCarBudget { get { return Budgets[(int)budget, 1]; } }
    public int ParkedCarBudget { get { return Budgets[(int)budget, 2]; } }
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

    void Awake() { Instance = this; Scale = 1f; TimeOfDayFactor = 1f; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        vehicles = FindFirstObjectByType<VehicleSystem>();
        if (showStats) MakeStats();
    }

    void Update()
    {
        // average frame time (unscaled, so the paused map doesn't count)
        float dt = Time.unscaledDeltaTime;
        if (dt > 0f && dt < 0.5f) frameAvg = Mathf.Lerp(frameAvg, dt, 0.05f);
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + tickEvery;
        if (vehicles == null) { vehicles = FindFirstObjectByType<VehicleSystem>(); if (vehicles == null) return; }

        Adapt();
        TimeOfDayFactor = DensityAt(LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f);

        Vector3 player = vehicles.PlayerTarget.position;
        int target = Mathf.RoundToInt(PedestrianBudget * Scale * TimeOfDayFactor);
        TickPedestrians(player, target);

        if (stats != null)
            stats.text = Mathf.RoundToInt(1f / frameAvg) + " fps   people " + activeCount + "/" + target + "   scale " + Scale.ToString("0.00");
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
            Ped p = GetPooled();
            if (p == null) break;
            p.go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            p.go.SetActive(true);        // NavMeshAgent snaps onto the NavMesh here
            p.active = true;
            activeCount++;
        }
    }

    Ped GetPooled()
    {
        foreach (Ped p in peds) if (!p.active && p.go != null) return p;
        if (pedestrianPrefabs == null || pedestrianPrefabs.Length == 0) return null;
        // pool grows up to the High budget, one new person per tick at most
        if (peds.Count >= Budgets[2, 0]) return null;
        GameObject prefab = pedestrianPrefabs[rng.Next(pedestrianPrefabs.Length)];
        if (prefab == null) return null;
        GameObject go = Instantiate(prefab, transform);
        go.SetActive(false);
        go.name = prefab.name + "_" + peds.Count;
        foreach (Animator a in go.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.CullCompletely;   // off-screen = no animation work
        Ped np = new Ped { go = go, health = go.GetComponent<Health>(), renderers = go.GetComponentsInChildren<Renderer>(true) };
        peds.Add(np);
        return np;
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
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None)) if (c.isRootCanvas) { canvas = c; break; }
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
