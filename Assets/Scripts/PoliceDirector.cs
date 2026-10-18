// W1: some tuning fields below are kept (so nothing in the prefabs/scene is lost) but the wanted level now drives
// them through PoliceTiers. This hides the "assigned but never used" warnings for those.
#pragma warning disable 0414
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Sends police cars. W1: how many, how quickly and from how far comes from the wanted level (PoliceTiers):
///   • new units appear on a road out of sight, around where the police THINK you are (last sighting / the
///     reported crime), never around where you really are — and never right next to you
///   • from 3★ some of them are armed response units
///   • only from 3★ are cars left far behind recalled and sent in again (low stars: lose them and they stay lost)
/// Prototype scene (no road network): falls back to the old fixed spawn points.
public class PoliceDirector : MonoBehaviour
{
    [SerializeField] private PoliceAI policeCarPrefab;

    [Header("Spawning near you (Lundun)")]
    [SerializeField] private int maxCars = 4;                 // 1★ = 1 car … 4★ = 4 cars
    [SerializeField] private float spawnNearest = 90f;
    [SerializeField] private float spawnFarthest = 160f;
    [SerializeField] private float firstCarDelay = 1.5f;      // after you first get a star
    [SerializeField] private float spawnDelay = 2.5f;         // between further cars
    [SerializeField] private float recallDistance = 300f;     // further than this and unseen = sent in again

    [Header("Fallback: fixed spawn points (Prototype scene)")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float minSpawnDistance = 30f;
    [SerializeField] private float maxSpawnDistance = 260f;

    private readonly List<PoliceAI> units = new List<PoliceAI>();
    private static PoliceDirector instance;

    void Awake()
    {
        instance = this;
        // W1b: everyday foot and car patrols live alongside the director
        if (GetComponent<LundunPatrols>() == null) gameObject.AddComponent<LundunPatrols>();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    /// The car prefab police are sent in (W1b patrols use it too).
    public static PoliceAI CarPrefab => instance != null ? instance.policeCarPrefab : null;

    /// W1b: a patrol car joining the chase counts as one of this level's units (so fewer are sent in).
    public static void Adopt(PoliceAI car)
    {
        if (instance == null || car == null || instance.units.Contains(car)) return;
        instance.units.Add(car);
    }
    private float nextSpawnTime;
    private float nextRecall;
    private int lastStars;
    private readonly System.Random rng = new System.Random();

    void Update()
    {
        WantedSystem wanted = WantedSystem.Instance;
        if (wanted == null || VehicleSystem.Locked) return;

        units.RemoveAll(u => u == null);
        int stars = wanted.Stars;
        if (stars == 0) units.RemoveAll(u => u.IsPatrolCar);       // W1b: patrols are back on their beat
        if (stars > 0 && lastStars == 0) nextSpawnTime = Time.time + (PoliceTiers.Instance != null ? PoliceTiers.For(stars).responseTime : firstCarDelay);
        else if (stars > lastStars) OnStarsUp(stars);
        lastStars = stars;

        Vector3 player = wanted.PlayerTarget.position;
        if (stars > 0 && PoliceTiers.For(stars).recall && Time.time >= nextRecall) { nextRecall = Time.time + 2f; Recall(wanted.LastKnownPosition); }

        if (stars == 0) return;
        PoliceTiers.Tier tier = PoliceTiers.For(stars);
        int armed = 0;
        foreach (PoliceAI u in units) if (u.Armed) armed++;
        bool needArmed = armed < tier.armedCars;
        bool needAny = units.Count < tier.cars;
        if ((needArmed || needAny) && Time.time >= nextSpawnTime)
        {
            // full up but short of armed response: the next car sent is armed (the oldest patrol stands down)
            if (!needAny && needArmed) StandDownOnePatrol(player);
            if (Spawn(wanted.LastKnownPosition, player, tier, needArmed)) nextSpawnTime = Time.time + tier.spawnGap;
            else nextSpawnTime = Time.time + 0.5f;     // no good spot this time; try again shortly
        }
    }

    /// W1: called when the stars go up, so the next level's response (e.g. armed units at 3★) is on its way.
    void OnStarsUp(int stars)
    {
        nextSpawnTime = Mathf.Min(nextSpawnTime, Time.time + PoliceTiers.For(stars).responseTime);
    }

    bool Spawn(Vector3 around, Vector3 player, PoliceTiers.Tier tier, bool armed)
    {
        Vector3 pos; Quaternion rot;
        if (!FindRoadSpot(around, player, tier, out pos, out rot) && !FindFixedSpot(player, out pos, out rot)) return false;
        PoliceAI car = Instantiate(policeCarPrefab, pos, rot);
        car.Armed = armed;
        units.Add(car);
        return true;
    }

    /// Make room for an armed unit: the patrol car furthest from you (and out of sight, with nobody out) leaves.
    void StandDownOnePatrol(Vector3 player)
    {
        PoliceAI best = null; float bestD = -1f;
        Camera cam = Camera.main;
        foreach (PoliceAI u in units)
        {
            if (u.Armed || u.OfficersOut || u.CanSeeTarget) continue;
            if (cam != null && CanCameraSee(cam, u.transform.position)) continue;
            float d = Vector3.Distance(u.transform.position, player);
            if (d > bestD) { bestD = d; best = u; }
        }
        if (best == null) return;
        units.Remove(best);
        Destroy(best.gameObject);
    }

    /// A spot on a road around where the police think you are, that the camera can't see and not near you, facing that way.
    bool FindRoadSpot(Vector3 around, Vector3 player, PoliceTiers.Tier tier, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return false;
        Camera cam = Camera.main;
        float near = PoliceTiers.Instance != null ? tier.spawnNearest : spawnNearest;
        float far = PoliceTiers.Instance != null ? tier.spawnFarthest : spawnFarthest;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            NavPoint p, dir; double halfWidth;
            if (!nav.Graph.RandomRoadPoint(new NavPoint(around.x, around.z), near, far, rng, out p, out dir, out halfWidth)) return false;
            Vector3 flat = new Vector3((float)p.x, 0f, (float)p.z);
            Vector3 me = player; me.y = 0f;
            if (Vector3.Distance(flat, me) < near * 0.6f) continue;                                 // never pops up near you
            Vector3 along = new Vector3((float)dir.x, 0f, (float)dir.z);
            Vector3 toward = around; toward.y = 0f;
            if (Vector3.Dot(along, toward - flat) < 0f) along = -along;                              // drive towards the search area
            Vector3 left = new Vector3(-along.z, 0f, along.x);
            Vector3 candidate = new Vector3((float)p.x, 1f, (float)p.z) + left * (float)(halfWidth * 0.5);       // UK: left lane
            if (cam != null && CanCameraSee(cam, candidate)) continue;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
            pos = hit.position + Vector3.up * 0.8f;
            rot = Quaternion.LookRotation(along, Vector3.up);
            return true;
        }
        return false;
    }

    static bool CanCameraSee(Camera cam, Vector3 p)
    {
        Vector3 v = cam.WorldToViewportPoint(p + Vector3.up);
        bool onScreen = v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        if (!onScreen) return false;
        // on screen, but hidden behind a building is fine
        return !Physics.Linecast(cam.transform.position, p + Vector3.up * 1.5f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);
    }

    bool FindFixedSpot(Vector3 player, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        if (spawnPoints == null) return false;
        List<Transform> options = new List<Transform>();
        foreach (Transform point in spawnPoints)
        {
            if (point == null) continue;
            float d = Vector3.Distance(point.position, player);
            if (d >= minSpawnDistance && d <= maxSpawnDistance) options.Add(point);
        }
        if (options.Count == 0) return false;
        options.Sort((a, b) => Vector3.Distance(a.position, player).CompareTo(Vector3.Distance(b.position, player)));
        Transform chosen = options[Random.Range(0, Mathf.Min(3, options.Count))];
        pos = chosen.position; rot = chosen.rotation;
        return true;
    }

    /// 3★ and up: cars stuck far from the search area (and not visible, nobody out on foot) are removed;
    /// Update sends a new one in, around the search area (not around you).
    void Recall(Vector3 searchArea)
    {
        Camera cam = Camera.main;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            PoliceAI u = units[i];
            if (u == null || u.OfficersOut || u.CanSeeTarget) continue;
            if (Vector3.Distance(u.transform.position, searchArea) < recallDistance) continue;
            if (cam != null && CanCameraSee(cam, u.transform.position)) continue;
            units.RemoveAt(i);
            Destroy(u.gameObject);
        }
    }
}
