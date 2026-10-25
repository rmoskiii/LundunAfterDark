using UnityEngine;

/// W1: how the police behave at each wanted level. Higher stars change HOW they hunt you, not just how many come.
///   1★  slow response, short sight, easy to lose round a corner (unarmed patrol)
///   2★  more pressure and a Taser up close, still fairly easy to shake off (unarmed patrols)
///   3★  first genuinely dangerous level: armed response arrives, quicker and more coordinated
///   4★  armed units in numbers, sharper eyes, better aim (helicopter + roadblocks come in W3)
///   5★  sustained pursuit, the most accurate shooters
/// Police never magically reappear closer at 1–2★: new units come from far out, towards where they THINK you are.
/// Lives on the WantedSystem object (added automatically). Every number can be tuned live in the Inspector.
public class PoliceTiers : MonoBehaviour
{
    [System.Serializable]
    public class Tier
    {
        [Header("Response")]
        public int cars = 1;
        [Tooltip("How many of those cars are armed response (their officers carry guns)")]
        public int armedCars = 0;
        [Tooltip("Seconds before the first unit for this level is sent")]
        public float responseTime = 5f;
        [Tooltip("Seconds between further units")]
        public float spawnGap = 8f;
        public float spawnNearest = 110f, spawnFarthest = 160f;
        [Tooltip("Cars left far behind can be sent in again closer to where you were last seen (never at low stars)")]
        public bool recall = false;

        [Header("Officers on foot")]
        public float runSpeed = 5.2f;                 // James runs 7.5
        public float sightRange = 25f;
        [Tooltip("Field of view (degrees). Outside it they don't see you, unless you're very close or they just saw you")]
        public float sightCone = 110f;
        public float repath = 0.6f;                   // how often they re-plan the route to you (slower = poorer tracking)
        [Tooltip("Seconds without seeing you before they give up and go back to the car")]
        public float giveUpAfter = 6f;
        public int officersPerCar = 1;
        public bool taser = false;

        [Header("Armed officers (armed response only)")]
        public float hitChanceNear = 0f, hitChanceFar = 0f;
        public float damage = 0f;
        [Tooltip("Seconds after spotting you before the first shot")]
        public float reactionTime = 1.2f;
        public float timeBetweenShots = 0.8f;

        [Header("Cars")]
        public float carSightRange = 30f;
        public float carSightCone = 120f;
        [Range(0.3f, 1f)] public float carThrottle = 0.7f;
        public bool ram = false;

        [Header("Escaping")]
        [Tooltip("Seconds fully out of sight to lose them")]
        public float escapeTime = 8f;
    }

    public static PoliceTiers Instance { get; private set; }

    [Tooltip("Within this distance police notice you even from behind (they hear you)")]
    public float nearSense = 6f;
    [Tooltip("Once they've seen you, they keep track for this long even if you leave their field of view")]
    public float keepTrackFor = 1f;
    [Tooltip("Hearing a gunshot this close makes them look round (360°) for a moment")]
    public float hearGunfire = 40f;

    [Header("Taser (unarmed officers, 2★ and up)")]
    public float taserMin = 2.5f, taserMax = 7f;
    public float taserWindup = 0.6f, taserCooldown = 5f, taserHitChance = 0.55f;
    public float taserStun = 1.6f, taserDamage = 5f;
    [Tooltip("After being tased, James can't be tased again for this long (no stun-lock)")]
    public float taserImmunity = 3.5f;

    [Header("Debug (R1)")]
    [Tooltip("Editor / development builds: each officer's state (CHASE, SEARCH, TASER, HOLD, UNSTICK…) above his head")]
    public bool showOfficerStates = false;
    public static bool ShowOfficerStates => Instance != null && Instance.showOfficerStates;

    [Header("Wanted levels 1 to 5")]
    public Tier[] tiers = Defaults();

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    static Tier[] fallback;

    /// The tier for a star count (1..5). 0 stars gets the 1★ tier.
    public static Tier For(int stars)
    {
        Tier[] t = Instance != null && Instance.tiers != null && Instance.tiers.Length > 0 ? Instance.tiers : (fallback != null ? fallback : (fallback = Defaults()));
        return t[Mathf.Clamp(stars, 1, t.Length) - 1];
    }

    public static float NearSense => Instance != null ? Instance.nearSense : 6f;
    public static float KeepTrackFor => Instance != null ? Instance.keepTrackFor : 1f;
    public static float HearGunfire => Instance != null ? Instance.hearGunfire : 40f;

    public static Tier[] Defaults()
    {
        return new[]
        {
            new Tier { cars = 1, armedCars = 0, responseTime = 5f, spawnGap = 8f, spawnNearest = 110f, spawnFarthest = 160f, recall = false,
                       runSpeed = 5.2f, sightRange = 25f, sightCone = 110f, repath = 0.6f, giveUpAfter = 6f, officersPerCar = 1, taser = false,
                       carSightRange = 30f, carSightCone = 120f, carThrottle = 0.7f, ram = false, escapeTime = 8f },
            new Tier { cars = 2, armedCars = 0, responseTime = 4f, spawnGap = 6f, spawnNearest = 100f, spawnFarthest = 150f, recall = false,
                       runSpeed = 5.8f, sightRange = 30f, sightCone = 120f, repath = 0.45f, giveUpAfter = 8f, officersPerCar = 2, taser = true,
                       carSightRange = 35f, carSightCone = 130f, carThrottle = 0.8f, ram = false, escapeTime = 12f },
            new Tier { cars = 3, armedCars = 1, responseTime = 3f, spawnGap = 4f, spawnNearest = 90f, spawnFarthest = 140f, recall = true,
                       runSpeed = 6.4f, sightRange = 35f, sightCone = 140f, repath = 0.35f, giveUpAfter = 11f, officersPerCar = 2, taser = true,
                       hitChanceNear = 0.35f, hitChanceFar = 0.1f, damage = 5f, reactionTime = 1.2f, timeBetweenShots = 0.8f,
                       carSightRange = 42f, carSightCone = 140f, carThrottle = 0.9f, ram = true, escapeTime = 18f },
            new Tier { cars = 4, armedCars = 2, responseTime = 2.5f, spawnGap = 3f, spawnNearest = 80f, spawnFarthest = 130f, recall = true,
                       runSpeed = 7f, sightRange = 45f, sightCone = 160f, repath = 0.3f, giveUpAfter = 15f, officersPerCar = 2, taser = true,
                       hitChanceNear = 0.6f, hitChanceFar = 0.25f, damage = 7f, reactionTime = 0.9f, timeBetweenShots = 0.6f,
                       carSightRange = 50f, carSightCone = 160f, carThrottle = 1f, ram = true, escapeTime = 30f },
            new Tier { cars = 5, armedCars = 3, responseTime = 2f, spawnGap = 2.5f, spawnNearest = 70f, spawnFarthest = 120f, recall = true,
                       runSpeed = 7.3f, sightRange = 55f, sightCone = 180f, repath = 0.25f, giveUpAfter = 20f, officersPerCar = 2, taser = true,
                       hitChanceNear = 0.75f, hitChanceFar = 0.35f, damage = 8f, reactionTime = 0.6f, timeBetweenShots = 0.5f,
                       carSightRange = 60f, carSightCone = 180f, carThrottle = 1f, ram = true, escapeTime = 45f },
        };
    }

    /// Can someone at eye, facing forward, see point? Shared by officers and cars.
    public static bool Sees(Vector3 eye, Vector3 forward, Vector3 point, float range, float cone, bool tracking, int blockers)
    {
        Vector3 to = point - eye;
        float d = to.magnitude;
        if (d > range) return false;
        bool near = d <= NearSense;
        if (!near && !tracking && cone < 359f)
        {
            Vector3 f = forward; f.y = 0f; Vector3 t = to; t.y = 0f;
            if (f.sqrMagnitude > 0.001f && t.sqrMagnitude > 0.001f && Vector3.Angle(f, t) > cone * 0.5f) return false;
        }
        return !Physics.Linecast(eye, point, blockers, QueryTriggerInteraction.Ignore);
    }

    /// How fast the target is moving (m/s): moving targets are harder to hit.
    public static float TargetSpeed(Transform target)
    {
        if (target == null) return 0f;
        CarController car = target.GetComponent<CarController>();
        if (car != null) return Mathf.Abs(car.Speed);
        CharacterController cc = target.GetComponent<CharacterController>();
        if (cc == null) return 0f;
        Vector3 v = cc.velocity; v.y = 0f;
        return v.magnitude;
    }
}
