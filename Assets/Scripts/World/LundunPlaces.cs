using System;
using UnityEngine;

/// Hospitals, police stations and safehouses (built by Lundun → 13  Dress the City).
/// - WASTED → nearest hospital; BUSTED → nearest police station (WastedSystem / BustedSystem ask here)
/// - walk up to a safehouse door and it becomes "home": the game starts there next time
/// - the Kestrel comes back to home's driveway if it's wrecked, sunk or left far away
public class LundunPlaces : MonoBehaviour
{
    [Serializable]
    public class Place
    {
        public string id, kind, name;
        public Vector3 door; public float doorYaw;
        public bool hasCar; public Vector3 car; public float carYaw;
    }

    public static LundunPlaces Instance { get; private set; }

    [SerializeField] private Place[] places = new Place[0];
    [SerializeField] private Transform kestrel;
    [SerializeField] private float homeRadius = 5f;          // this close to a safehouse door = it's home now
    [SerializeField] private float carLostBeyond = 250f;     // after WASTED / BUSTED, a car further away than this comes home
    [SerializeField] private float wreckTowAfter = 200f;     // a wreck you've walked this far from is towed home

    const string PrefKey = "Lundun.Safehouse";
    private Place home;
    private float nextCheck;

    public Place[] Places { get { return places; } }
    public Place Home { get { return home; } }

    public void SetData(Place[] list, Transform car) { places = list; kestrel = car; }

    /// Map marker for a place (a child object named after it), used by the minimap.
    public Transform Marker(string id) { return transform.Find("Place_" + id); }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }
    void OnEnable() { VehicleSystem.Respawned += OnRespawned; }
    void OnDisable() { VehicleSystem.Respawned -= OnRespawned; }

    void Start()
    {
        string saved = PlayerPrefs.GetString(PrefKey, "");
        home = Find(saved);
        Transform player = Player();
        if (home != null && player != null)
        {
            // start the session at the safehouse you used last
            Teleport(player, home.door, home.doorYaw);
            ParkKestrel(home);
        }
        else if (player != null) home = NearestOf("safehouse", player.position);
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot) return;
        Vector3 p = ph.Body.position;

        foreach (Place s in places)
        {
            if (s.kind != "safehouse" || s == home) continue;
            Vector3 d = s.door - p; d.y = 0f;
            if (d.sqrMagnitude > homeRadius * homeRadius) continue;
            home = s;
            PlayerPrefs.SetString(PrefKey, s.id); PlayerPrefs.Save();
            if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(s.name + "\nYou'll start here next time");
        }

        // the Kestrel blown up and abandoned: towed home once you're well away
        VehicleHealth vh = kestrel != null ? kestrel.GetComponent<VehicleHealth>() : null;
        if (vh != null && vh.IsWrecked && home != null)
        {
            Vector3 d = kestrel.position - p; d.y = 0f;
            if (d.sqrMagnitude > wreckTowAfter * wreckTowAfter) ParkKestrel(home);
        }
    }

    void OnRespawned()
    {
        if (kestrel == null) return;
        Transform player = Player();
        if (home == null && player != null) home = NearestOf("safehouse", player.position);
        VehicleHealth vh = kestrel.GetComponent<VehicleHealth>();
        bool wrecked = vh != null && vh.IsWrecked;
        bool sunk = kestrel.position.y < -1f;
        bool far = player != null && Flat(kestrel.position - player.position) > carLostBeyond;
        if (wrecked || sunk || far) ParkKestrel(home);
    }

    /// Nearest place of a kind ("hospital", "police", "safehouse"). Leaves pos / rot alone if there are none.
    public static bool Pick(string kind, Vector3 from, ref Vector3 pos, ref Quaternion rot)
    {
        if (Instance == null) return false;
        Place p = Instance.NearestOf(kind, from);
        if (p == null) return false;
        pos = Ground(p.door);
        rot = Quaternion.Euler(0f, p.doorYaw, 0f);
        return true;
    }

    /// Put the Kestrel back on home's driveway, repaired. Returns false if there's nowhere to put it.
    public bool ParkKestrelHome() { return ParkKestrel(home); }

    bool ParkKestrel(Place s)
    {
        if (kestrel == null || s == null || !s.hasCar) return false;
        CarController cc = kestrel.GetComponent<CarController>();
        if (cc != null && cc.IsDriven) return false;
        VehicleHealth vh = kestrel.GetComponent<VehicleHealth>();
        if (vh != null) vh.Repair();
        Rigidbody rb = kestrel.GetComponent<Rigidbody>();
        Quaternion rot = Quaternion.Euler(0f, s.carYaw, 0f);
        Vector3 pos = Ground(s.car) + Vector3.up * 0.5f;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = pos; rb.rotation = rot; }
        kestrel.SetPositionAndRotation(pos, rot);
        return true;
    }

    Place NearestOf(string kind, Vector3 from)
    {
        Place best = null; float bd = float.MaxValue;
        foreach (Place p in places)
        {
            if (p.kind != kind) continue;
            float d = Flat(p.door - from);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    Place Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (Place p in places) if (p.id == id) return p;
        return null;
    }

    static Transform Player()
    {
        PlayerController pc = FindAnyObjectByType<PlayerController>();
        return pc != null ? pc.transform : null;
    }

    static void Teleport(Transform player, Vector3 pos, float yaw)
    {
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(Ground(pos), Quaternion.Euler(0f, yaw, 0f));
        if (cc != null) cc.enabled = true;
    }

    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    /// The real ground height here (pavements, the old high street's raised kerbs…), plus a hair.
    public static Vector3 Ground(Vector3 at)
    {
        int mask = ~LayerMask.GetMask("Player", "NPC", "Vehicle", "UI");
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(at.x, at.y + 4f, at.z), Vector3.down, 12f, mask, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (RaycastHit h in hits) if (h.point.y > best && h.point.y < at.y + 1.5f) best = h.point.y;
        return new Vector3(at.x, best > float.MinValue ? best + 0.05f : at.y + 0.1f, at.z);
    }
}
