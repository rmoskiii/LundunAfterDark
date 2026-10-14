using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Sends police cars. One car per star (up to maxCars).
/// Lundun: cars appear on a road 90–160 m away, out of the camera's view, so they arrive within seconds.
/// Cars left far behind and out of sight are recalled and sent in again closer.
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
        if (stars > 0 && lastStars == 0) nextSpawnTime = Time.time + firstCarDelay;
        lastStars = stars;

        Vector3 player = wanted.PlayerTarget.position;
        if (Time.time >= nextRecall) { nextRecall = Time.time + 2f; Recall(player); }

        int want = stars == 0 ? 0 : Mathf.Min(stars, maxCars);
        if (units.Count < want && Time.time >= nextSpawnTime)
        {
            if (Spawn(player)) nextSpawnTime = Time.time + spawnDelay;
            else nextSpawnTime = Time.time + 0.5f;     // no good spot this time; try again shortly
        }
    }

    bool Spawn(Vector3 player)
    {
        Vector3 pos; Quaternion rot;
        if (!FindRoadSpot(player, out pos, out rot) && !FindFixedSpot(player, out pos, out rot)) return false;
        units.Add(Instantiate(policeCarPrefab, pos, rot));
        return true;
    }

    /// A spot on a road near the player that the camera can't see, facing roughly towards the player.
    bool FindRoadSpot(Vector3 player, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return false;
        Camera cam = Camera.main;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            NavPoint p, dir; double halfWidth;
            if (!nav.Graph.RandomRoadPoint(new NavPoint(player.x, player.z), spawnNearest, spawnFarthest, rng, out p, out dir, out halfWidth)) return false;
            Vector3 along = new Vector3((float)dir.x, 0f, (float)dir.z);
            if (Vector3.Dot(along, player - new Vector3((float)p.x, 0f, (float)p.z)) < 0f) along = -along;   // drive towards you
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

    /// Cars stuck far away (and not visible, and with nobody out on foot) are removed; Update sends a new one closer.
    void Recall(Vector3 player)
    {
        Camera cam = Camera.main;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            PoliceAI u = units[i];
            if (u == null || u.OfficersOut) continue;
            if (Vector3.Distance(u.transform.position, player) < recallDistance) continue;
            if (cam != null && CanCameraSee(cam, u.transform.position)) continue;
            units.RemoveAt(i);
            Destroy(u.gameObject);
        }
    }
}
