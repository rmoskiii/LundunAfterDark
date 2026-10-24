using System.Collections.Generic;
using UnityEngine;

/// E5: one walk-in store (Basketful, Grocera, ValuStack), built by Lundun → 28.
///   • its inside (shelves, lights, people) is only switched on when you're near or inside: the outside stays cheap
///   • the sliding doors open for anyone walking up to them (you, shoppers, staff), and stay open if their glass is broken
///   • Inside(point) tells other systems whether a point is in the store
public class LundunShopInterior : MonoBehaviour
{
    [Header("Filled in by Lundun → 28")]
    [SerializeField] private string storeName = "";
    [SerializeField] private Vector3 footprintMin, footprintMax;     // the store's outline (x, z)
    [SerializeField] private GameObject detail;                       // everything switched on near the store
    [SerializeField] private Transform[] doorLeftLeaves = new Transform[0], doorRightLeaves = new Transform[0];
    [SerializeField] private Vector3[] doorCentres = new Vector3[0];
    [SerializeField] private float doorHalfWidth = 2.2f;

    [Header("Tuning")]
    [SerializeField] private float showWithin = 90f;                  // metres from the store's outline
    [SerializeField] private float hideBeyond = 110f;
    [SerializeField] private float openWithin = 4.2f;
    [SerializeField] private float doorSpeed = 2.4f;                  // of a full opening per second

    public static readonly List<LundunShopInterior> All = new List<LundunShopInterior>();
    public string StoreName { get { return storeName; } }
    public bool DetailOn { get { return detail != null && detail.activeSelf; } }

    private float nextCheck, nextDoorCheck;
    private float[] open, want;
    private Vector3[] leftClosed, rightClosed;
    private int peopleMask;
    private static readonly Collider[] hits = new Collider[16];

    public void SetData(string store, Vector3 min, Vector3 max, GameObject inside, Transform[] left, Transform[] right, Vector3[] centres, float half)
    {
        storeName = store; footprintMin = min; footprintMax = max; detail = inside;
        doorLeftLeaves = left; doorRightLeaves = right; doorCentres = centres; doorHalfWidth = half;
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        int n = doorCentres.Length;
        open = new float[n]; want = new float[n];
        leftClosed = new Vector3[n]; rightClosed = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            if (i < doorLeftLeaves.Length && doorLeftLeaves[i] != null) { leftClosed[i] = doorLeftLeaves[i].localPosition; }
            if (i < doorRightLeaves.Length && doorRightLeaves[i] != null) { rightClosed[i] = doorRightLeaves[i].localPosition; }
        }
        peopleMask = LayerMask.GetMask("Player", "NPC", "Vehicle");
        if (detail != null) detail.SetActive(Distance(Viewer()) < showWithin);
    }

    /// Is this point inside the store?
    public bool Inside(Vector3 p)
    {
        return p.x > footprintMin.x && p.x < footprintMax.x && p.z > footprintMin.z && p.z < footprintMax.z && p.y < 8f;
    }

    /// The store a point is in (null if none).
    public static LundunShopInterior At(Vector3 p)
    {
        foreach (LundunShopInterior s in All) if (s.Inside(p)) return s;
        return null;
    }

    Vector3 Viewer()
    {
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.Body != null && PlayerHealth.Instance.Body.gameObject.activeInHierarchy) return PlayerHealth.Instance.Body.position;
        if (Camera.main != null) return Camera.main.transform.position;
        return new Vector3(1e9f, 0f, 1e9f);
    }

    /// Metres from p to the store's outline (0 inside).
    float Distance(Vector3 p)
    {
        float dx = Mathf.Max(footprintMin.x - p.x, 0f, p.x - footprintMax.x), dz = Mathf.Max(footprintMin.z - p.z, 0f, p.z - footprintMax.z);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    void Update()
    {
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.4f;
            float d = Mathf.Min(Distance(Viewer()), Camera.main != null ? Distance(Camera.main.transform.position) : 1e9f);
            if (detail != null)
            {
                if (!detail.activeSelf && d < showWithin) detail.SetActive(true);
                else if (detail.activeSelf && d > hideBeyond) detail.SetActive(false);
            }
        }
        Doors();
    }

    // ------------------------------------------------------------------ the sliding doors
    void Doors()
    {
        if (open == null) return;
        if (Time.time >= nextDoorCheck)
        {
            nextDoorCheck = Time.time + 0.2f;
            for (int i = 0; i < doorCentres.Length; i++)
                want[i] = Physics.OverlapSphereNonAlloc(doorCentres[i] + Vector3.up, openWithin, hits, peopleMask, QueryTriggerInteraction.Collide) > 0 ? 1f : 0f;
        }
        float step = doorSpeed * Time.deltaTime;
        for (int i = 0; i < doorCentres.Length; i++)
        {
            float was = open[i];
            open[i] = Mathf.MoveTowards(open[i], want[i], step);
            if (Mathf.Approximately(was, open[i]) && Time.frameCount > 2) continue;
            float slide = Mathf.SmoothStep(0f, 1f, open[i]) * doorHalfWidth * 0.95f;
            if (i < doorLeftLeaves.Length && doorLeftLeaves[i] != null) doorLeftLeaves[i].localPosition = leftClosed[i] + Vector3.left * slide;
            if (i < doorRightLeaves.Length && doorRightLeaves[i] != null) doorRightLeaves[i].localPosition = rightClosed[i] + Vector3.right * slide;
        }
    }
}
