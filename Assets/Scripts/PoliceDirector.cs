using System.Collections.Generic;
using UnityEngine;

public class PoliceDirector : MonoBehaviour
{
    [SerializeField] private PoliceAI policeCarPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float minSpawnDistance = 30f;   // never pop in right next to you
    [SerializeField] private float maxSpawnDistance = 260f;  // big map: don't send a car from the other side of town
    [SerializeField] private int maxUnits = 1;            // V0.03: one car. Raise later for more stars.
    [SerializeField] private float spawnDelay = 3f;

    private readonly List<PoliceAI> units = new List<PoliceAI>();
    private float nextSpawnTime;

    void Update()
    {
        WantedSystem wanted = WantedSystem.Instance;
        if (wanted == null) return;

        units.RemoveAll(u => u == null);                  // forget cars that have been removed

        int wantedUnits = wanted.Stars == 0 ? 0 : Mathf.Min(wanted.Stars, maxUnits);

        if (units.Count < wantedUnits && Time.time >= nextSpawnTime)
        {
            Spawn(wanted.PlayerTarget.position);
            nextSpawnTime = Time.time + spawnDelay;
        }
    }

    void Spawn(Vector3 playerPosition)
    {
        // Big map: pick one of the 3 closest spawn points that are at least minSpawnDistance away
        List<Transform> options = new List<Transform>();
        foreach (Transform point in spawnPoints)
        {
            if (point == null) continue;
            float d = Vector3.Distance(point.position, playerPosition);
            if (d >= minSpawnDistance && d <= maxSpawnDistance) options.Add(point);
        }
        if (options.Count == 0)
            foreach (Transform point in spawnPoints)
                if (point != null && Vector3.Distance(point.position, playerPosition) >= minSpawnDistance) options.Add(point);

        if (options.Count == 0) return;

        options.Sort((a, b) => Vector3.Distance(a.position, playerPosition).CompareTo(Vector3.Distance(b.position, playerPosition)));
        int pickFrom = Mathf.Min(3, options.Count);
        Transform chosen = options[Random.Range(0, pickFrom)];
        units.Add(Instantiate(policeCarPrefab, chosen.position, chosen.rotation));
    }
}