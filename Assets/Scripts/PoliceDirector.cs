using System.Collections.Generic;
using UnityEngine;

public class PoliceDirector : MonoBehaviour
{
    [SerializeField] private PoliceAI policeCarPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private float minSpawnDistance = 30f;
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
        // Pick a random spawn point that's far enough away from the player
        List<Transform> options = new List<Transform>();
        foreach (Transform point in spawnPoints)
            if (Vector3.Distance(point.position, playerPosition) >= minSpawnDistance)
                options.Add(point);

        if (options.Count == 0) return;

        Transform chosen = options[Random.Range(0, options.Count)];
        units.Add(Instantiate(policeCarPrefab, chosen.position, chosen.rotation));
    }
}