using UnityEngine;

/// L3: the parking bays. Built by Lundun → 23 on the LundunParked object.
///  • holds the bay slots kept free for real cars: LundunPopulation parks its (stealable) cars there instead of at
///    random kerbs, so parked cars always sit in proper bays
///  • the static parked cars (baked meshes) are switched off beyond drawDistance to save the iPhone some work
public class LundunParking : MonoBehaviour
{
    public static LundunParking Instance { get; private set; }

    [Tooltip("Bays left free for real, stealable parked cars: x, z, facing (degrees), length")]
    [SerializeField] private Vector4[] slots = new Vector4[0];
    [SerializeField] private Renderer[] blocks = new Renderer[0];
    [Tooltip("Static parked cars further than this from the camera aren't drawn")]
    [SerializeField] private float drawDistance = 170f;
    [SerializeField] private float roadY = 0.02f;

    private float nextCull;

    void Awake()
    {
        Instance = this;
        // E3: the expansion areas' bays too (their static parked cars stay with them)
        System.Collections.Generic.List<Vector4> all = new System.Collections.Generic.List<Vector4>(slots);
        foreach (LundunExpansionArea area in FindObjectsByType<LundunExpansionArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            all.AddRange(area.ParkingBays);
        slots = all.ToArray();
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void SetData(Vector4[] s, Renderer[] b) { slots = s; blocks = b; }
    public int SlotCount { get { return slots.Length; } }

    /// A free bay between near and far metres from the player, out of sight. Returns false if there isn't one.
    public bool TryGetSlot(Vector3 player, float near, float far, System.Random rng, Camera cam, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        if (slots.Length == 0) return false;
        int mask = LayerMask.GetMask("Vehicle", "Player");
        int start = rng.Next(slots.Length);
        for (int k = 0; k < slots.Length; k++)
        {
            Vector4 s = slots[(start + k) % slots.Length];
            Vector3 p = new Vector3(s.x, roadY, s.y);
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(player.x, player.z));
            if (d < near || d > far) continue;
            if (cam != null && LundunView.CanSee(cam, p)) continue;
            if (Physics.CheckSphere(p + Vector3.up, Mathf.Max(1.5f, s.w * 0.5f), mask, QueryTriggerInteraction.Ignore)) continue;
            pos = p; rot = Quaternion.Euler(0f, s.z, 0f);
            return true;
        }
        return false;
    }

    void Update()
    {
        if (Time.unscaledTime < nextCull || blocks.Length == 0) return;
        nextCull = Time.unscaledTime + 0.5f;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;
        float d2 = drawDistance * drawDistance;
        foreach (Renderer r in blocks)
        {
            if (r == null) continue;
            Vector3 q = r.bounds.ClosestPoint(c);
            bool on = (q - c).sqrMagnitude < d2;
            if (r.enabled != on) r.enabled = on;
        }
    }
}
