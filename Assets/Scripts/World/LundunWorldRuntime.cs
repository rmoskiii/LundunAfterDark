using UnityEngine;
using UnityEngine.AI;

/// Lives on the "LundunRuntime" object in the Lundun scene (added by Lundun → 2  Place Gameplay Objects).
/// - Locks the game to 30 fps (iPhone battery/heat).
/// - Falling into the river = WASTED.
/// - The player's car comes back to its parking spot if it sinks; sunk police cars are removed.
/// - Makes police prefer roads over grass when they path-find.
public class LundunWorldRuntime : MonoBehaviour
{
    [SerializeField] private int targetFps = 30;
    [SerializeField] private float drownY = -1f;             // water surface is at -2
    [SerializeField] private float lostY = -15f;             // anything below this has sunk
    [SerializeField] private Transform playerCar;
    [SerializeField] private Transform carRespawn;
    [SerializeField] private float offRoadCost = 2f;          // NavMesh area 0 (grass, other walkable stuff). Roads are area 3, cost 1.
    [SerializeField] private float pavementCost = 3f;         // NavMesh area 4 (pavements)

    private VehicleSystem vehicles;
    private float nextSweep;

    void Awake()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = targetFps;
        NavMesh.SetAreaCost(0, offRoadCost);   // police and pedestrians prefer roads and pavements
        NavMesh.SetAreaCost(4, pavementCost);  // pavements: police cars only mount them if it really saves time
    }

    void Start()
    {
        vehicles = FindFirstObjectByType<VehicleSystem>();
    }

    void Update()
    {
        PlayerHealth health = PlayerHealth.Instance;
        if (vehicles != null && health != null && !health.IsDead && vehicles.PlayerTarget.position.y < drownY)
            health.Kill();

        if (playerCar != null && playerCar.position.y < lostY && !(vehicles != null && vehicles.IsDriving && vehicles.PlayerTarget == playerCar))
            ReturnCar();

        if (Time.time >= nextSweep)
        {
            nextSweep = Time.time + 1f;
            foreach (PoliceAI cop in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
                if (cop.transform.position.y < lostY) Destroy(cop.gameObject);
        }
    }

    void ReturnCar()
    {
        if (carRespawn == null) return;
        Rigidbody rb = playerCar.GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        playerCar.SetPositionAndRotation(carRespawn.position + Vector3.up * 0.6f, carRespawn.rotation);
        if (rb != null) rb.position = playerCar.position;
    }
}
