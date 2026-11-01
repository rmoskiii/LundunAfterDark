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
    /// James's own car (the Kestrel).
    public Transform Kestrel { get { return kestrel; } }

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
        pos = Ground(p.door) + Vector3.up * PlayerFeet();
        rot = Quaternion.Euler(0f, p.doorYaw, 0f);
        return true;
    }

    /// Where to wake up after WASTED ("hospital") or BUSTED ("police"): the nearest one of that kind to where it happened.
    /// Never fails: falls back to the old spawn marker (inspector link, then the SP_ object by name), then to where it happened.
    /// Returns the place's name for the on-screen message (null if it had to fall back).
    public static string RespawnSpot(string kind, string markerName, Transform marker, Vector3 from, out Vector3 pos, out Quaternion rot)
    {
        Place p = Instance != null ? Instance.NearestOf(kind, from) : null;
        if (p != null)
        {
            pos = Ground(p.door) + Vector3.up * PlayerFeet();
            rot = Quaternion.Euler(0f, p.doorYaw, 0f);
            return p.name;
        }
        if (marker == null)
        {
            GameObject g = GameObject.Find(markerName);
            if (g != null) marker = g.transform;
        }
        if (marker != null) { pos = Ground(marker.position) + Vector3.up * PlayerFeet(); rot = Quaternion.Euler(0f, marker.eulerAngles.y, 0f); return null; }
        Debug.LogWarning("[Lundun] No " + kind + " or " + markerName + " found: respawning where it happened.");
        pos = Ground(new Vector3(from.x, Mathf.Max(from.y, 0f), from.z)) + Vector3.up * PlayerFeet();
        rot = Quaternion.identity;
        return null;
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
        player.SetPositionAndRotation(Ground(pos) + Vector3.up * FeetOffset(player), Quaternion.Euler(0f, yaw, 0f));
        if (cc != null) cc.enabled = true;
    }

    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    /// How far James's root sits above his feet. His capsule is centred on the root (about 1 m up),
    /// so anything that puts him "on the ground" adds this, or he ends up half under the road.
    public static float FeetOffset(Transform person)
    {
        CharacterController cc = person != null ? person.GetComponent<CharacterController>() : null;
        if (cc == null) return 0f;
        return Mathf.Max(0f, (cc.height * 0.5f - cc.center.y) * person.lossyScale.y);
    }

    static float PlayerFeet() { return PlayerHealth.Instance != null ? FeetOffset(PlayerHealth.Instance.Body) : 0f; }

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
