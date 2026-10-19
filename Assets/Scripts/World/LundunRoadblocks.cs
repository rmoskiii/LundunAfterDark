using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// W3: roadblocks (4★ and up, while James is driving). Two police cars park across the road ahead of where he's
/// heading, in a V, and wait; their crews (armed at these levels) only get out once he's close. Drive round them,
/// turn off before you reach them, or ram through. Once the crews are back in, the cars join the chase.
/// Old roadblocks left far behind (and out of sight) are cleared away. Added automatically next to the PoliceDirector.
public class LundunRoadblocks : MonoBehaviour
{
    [SerializeField] private int fromStars = 4;
    [SerializeField] private float aheadNearest = 110f, aheadFarthest = 170f;
    [Tooltip("Only while James is driving at least this fast (m/s)")]
    [SerializeField] private float minSpeed = 8f;
    [SerializeField] private float cooldown = 25f;
    [SerializeField] private int maxAlive = 2;
    [SerializeField] private float crewOutWithin = 60f;
    [SerializeField] private float clearBeyond = 220f;

    class Block { public PoliceAI a, b; public Vector3 at; }
    private readonly List<Block> blocks = new List<Block>();
    private VehicleSystem vehicles;
    private float next;
    private static readonly System.Random rng = new System.Random();

    void Start() { vehicles = FindAnyObjectByType<VehicleSystem>(); }

    void Update()
    {
        WantedSystem w = WantedSystem.Instance;
        if (w == null || vehicles == null) return;
        Vector3 player = w.PlayerTarget.position;
        Tidy(player, w.Stars);
        if (w.Stars < fromStars || !vehicles.IsDriving || VehicleSystem.Locked) return;
        if (Time.time < next || blocks.Count >= maxAlive) return;
        if (vehicles.PlayerSpeed < minSpeed) return;
        next = Time.time + 2f;                                     // try again soon if there's no good spot
        if (TryPlace(player)) next = Time.time + cooldown;
    }

    bool TryPlace(Vector3 player)
    {
        LundunNavigation nav = LundunNavigation.Instance;
        PoliceAI prefab = PoliceDirector.CarPrefab;
        if (nav == null || nav.Graph == null || prefab == null) return false;
        Rigidbody body = vehicles.CurrentCar != null ? vehicles.CurrentCar.GetComponent<Rigidbody>() : null;
        Vector3 heading = body != null ? body.linearVelocity : vehicles.PlayerTarget.forward;
        heading.y = 0f;
        if (heading.sqrMagnitude < 1f) return false;
        heading.Normalize();

        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 guess = player + heading * Random.Range(aheadNearest, aheadFarthest)
                          + new Vector3(-heading.z, 0f, heading.x) * Random.Range(-15f, 15f);
            NavPoint p = nav.Graph.NearestOnRoad(new NavPoint(guess.x, guess.z), out int edge, out double t, out double dist);
            if (edge < 0 || dist > 25.0) continue;
            nav.Graph.EdgeEnds(edge, out int na, out int nb);
            NavPoint pa = nav.Graph.NodePosition(na), pb = nav.Graph.NodePosition(nb);
            nav.Graph.EdgeInfo(na, nb, out double width, out string cls);
            Vector3 along = new Vector3((float)(pb.x - pa.x), 0f, (float)(pb.z - pa.z));
            if (along.sqrMagnitude < 1f) continue;
            along.Normalize();
            Vector3 centre = new Vector3((float)p.x, player.y, (float)p.z);
            if (Vector3.Distance(centre, player) < aheadNearest * 0.8f) continue;
            if (Physics.CheckSphere(centre + Vector3.up, 7f, LayerMask.GetMask("Vehicle", "Player"), QueryTriggerInteraction.Ignore)) continue;

            Vector3 right = new Vector3(along.z, 0f, -along.x);
            float half = Mathf.Clamp((float)width * 0.5f, 3f, 9f);
            PoliceAI a = Park(prefab, centre + right * half * 0.42f, Quaternion.LookRotation(Quaternion.Euler(0f, 68f, 0f) * along));
            PoliceAI b = Park(prefab, centre - right * half * 0.42f, Quaternion.LookRotation(Quaternion.Euler(0f, -68f, 0f) * along));
            if (a == null && b == null) continue;
            blocks.Add(new Block { a = a, b = b, at = centre });
            return true;
        }
        return false;
    }

    PoliceAI Park(PoliceAI prefab, Vector3 at, Quaternion rot)
    {
        if (!NavMesh.SamplePosition(at, out NavMeshHit hit, 4f, NavMesh.AllAreas)) return null;
        PoliceAI car = Instantiate(prefab, hit.position + Vector3.up * 0.8f, rot);
        car.Armed = WantedSystem.Instance != null && WantedSystem.Instance.Stars >= 3;
        car.StartRoadblock(crewOutWithin);
        return car;
    }

    void Tidy(Vector3 player, int stars)
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            Block b = blocks[i];
            bool aGone = b.a == null || !b.a.HoldingRoadblock, bGone = b.b == null || !b.b.HoldingRoadblock;
            bool far = Vector3.Distance(b.at, player) > clearBeyond;
            if (far)
            {
                if (b.a != null && b.a.HoldingRoadblock && !Seen(b.a.gameObject)) { Destroy(b.a.gameObject); b.a = null; }
                if (b.b != null && b.b.HoldingRoadblock && !Seen(b.b.gameObject)) { Destroy(b.b.gameObject); b.b = null; }
            }
            if ((b.a == null || aGone) && (b.b == null || bGone)) blocks.RemoveAt(i);
        }
    }

    static bool Seen(GameObject g)
    {
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>()) if (r.isVisible) return true;
        return false;
    }
}
