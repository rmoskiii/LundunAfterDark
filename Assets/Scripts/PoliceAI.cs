// W1: some tuning fields below are kept (so nothing in the prefabs/scene is lost) but the wanted level now drives
// them through PoliceTiers. This hides the "assigned but never used" warnings for those.
#pragma warning disable 0414
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// W1: sight, speed, ramming and crew size come from the wanted level (PoliceTiers).
/// Armed response cars (sent from 3★) carry armed officers; patrol cars don't.
[RequireComponent(typeof(CarController))]
public class PoliceAI : MonoBehaviour
{
    private enum State { Responding, Pursuing, Searching, Parked, Returning, Patrolling }

    // Every police car currently in the game (so other systems can find them)
    public static readonly List<PoliceAI> Active = new List<PoliceAI>();
    public bool CanSeeTarget { get; private set; }
    public bool OfficersOut => officers.Count > 0;
    /// Armed response unit (set by PoliceDirector when it's sent).
    public bool Armed { get; set; }
    /// Heading off (wanted level cleared).
    public bool IsLeaving => state == State.Returning;
    /// W1b: an everyday patrol car (part of the traffic, no siren). Goes back to patrolling after a chase.
    public bool IsPatrolCar { get; private set; }
    public bool OnPatrol => state == State.Patrolling;
    public PoliceOfficer OfficerPrefab => officerPrefab;
    private float nextPatrolRoute;

    // ------------------------------------------------------------------ W3: roadblocks and the armed-response look
    private float roadblockCrewWithin = -1f;
    /// Parked across a road waiting for James (crew still inside).
    public bool HoldingRoadblock => roadblockCrewWithin > 0f && state == State.Parked && !officersDeployedThisStop;

    /// Park here as a roadblock: the crew gets out once James is within this distance (call right after Instantiate).
    public void StartRoadblock(float crewOutWithin)
    {
        roadblockCrewWithin = crewOutWithin;
        state = State.Parked;
        officersDeployedThisStop = false;
    }

    /// Armed response cars: a yellow roof marker, black side bands and a push bar, so you can tell them apart.
    void ApplyArmedLook()
    {
        Transform body = transform.Find("Body"), cabin = transform.Find("Cabin");
        Renderer br = body != null ? body.GetComponent<Renderer>() : null;
        if (br == null || br.sharedMaterial == null) return;
        Material yellow = new Material(br.sharedMaterial) { name = "ARV_Yellow" };
        yellow.SetTexture("_BaseMap", null); yellow.SetColor("_BaseColor", new Color(1f, 0.8f, 0.08f)); yellow.color = new Color(1f, 0.8f, 0.08f);
        Material black = new Material(br.sharedMaterial) { name = "ARV_Black" };
        black.SetTexture("_BaseMap", null); black.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f)); black.color = new Color(0.04f, 0.04f, 0.05f);
        Vector3 bp = body.localPosition, bs = body.localScale;
        if (cabin != null)
        {
            Vector3 cp = cabin.localPosition, cs = cabin.localScale;
            Plate("ARV_Roof", new Vector3(cp.x, cp.y + cs.y * 0.5f + 0.025f, cp.z - cs.z * 0.18f), new Vector3(cs.x * 0.62f, 0.03f, cs.z * 0.42f), yellow);
        }
        float y = bp.y + bs.y * 0.3f;
        Plate("ARV_BandL", new Vector3(bp.x - bs.x * 0.5f - 0.012f, y, bp.z), new Vector3(0.02f, bs.y * 0.16f, bs.z * 0.94f), black);
        Plate("ARV_BandR", new Vector3(bp.x + bs.x * 0.5f + 0.012f, y, bp.z), new Vector3(0.02f, bs.y * 0.16f, bs.z * 0.94f), black);
        Plate("ARV_PushBar", new Vector3(bp.x, bp.y - bs.y * 0.15f, bp.z + bs.z * 0.5f + 0.06f), new Vector3(bs.x * 0.7f, bs.y * 0.35f, 0.08f), black);
    }

    void Plate(string name, Vector3 localPos, Vector3 localScale, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Collider c = g.GetComponent<Collider>(); if (c != null) DestroyImmediate(c);
        g.name = name;
        g.transform.SetParent(transform, false);
        g.transform.localPosition = localPos; g.transform.localScale = localScale; g.transform.localRotation = Quaternion.identity;
        Renderer r = g.GetComponent<Renderer>(); r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        g.layer = gameObject.layer;
    }

    /// Make this car an everyday patrol (call right after Instantiate).
    public void StartPatrol()
    {
        IsPatrolCar = true;
        state = State.Patrolling;
        if (siren != null) siren.Stop();
        nextPatrolRoute = 0f;
    }

    /// Called to a crime / joins the chase: lights and siren on.
    public void Engage()
    {
        if (state != State.Patrolling) return;
        state = State.Responding;
        Lights(true);
    }

    void ResumePatrol()
    {
        state = State.Patrolling;
        nextPatrolRoute = 0f;
        Lights(false);
    }

    /// Beacons and siren on (responding) or off (just patrolling).
    void Lights(bool on)
    {
        if (siren != null) { if (on && !siren.isPlaying) siren.Play(); else if (!on) siren.Stop(); }
        foreach (BlinkingLight b in GetComponentsInChildren<BlinkingLight>(true))
        {
            b.CancelInvoke();
            Renderer r = b.GetComponent<Renderer>();
            if (on) b.InvokeRepeating("Toggle", Random.Range(0f, 0.6f), 0.6f);
            else if (r != null) r.material.SetColor("_EmissionColor", Color.black);
        }
    }
    private float lastSaw = -99f;

    [Header("Senses")]
    [SerializeField] private float sightRange = 45f;
    [SerializeField] private LayerMask sightBlockers = 1;      // Default layer = buildings and props

    [Header("Driving")]
    [SerializeField] private float repathInterval = 0.4f;
    [SerializeField] private float arriveDistance = 7f;
    [SerializeField] private float searchRadius = 25f;
    [SerializeField] private float steerSharpness = 35f;      // lower = twitchier steering

    [Header("Officers")]
    [SerializeField] private PoliceOfficer officerPrefab;
    [SerializeField] private float deployDistance = 15f;      // pull over when this close to you
    [SerializeField] private float maxSuspectCarSpeed = 4f;   // only stop if your car is this slow (or you're on foot)
    [SerializeField] private float redeployCooldown = 6f;

    [SerializeField] private AudioSource siren;

    private State state = State.Responding;
    private CarController car;
    private NavMeshPath path;
    private Vector3 destination;
    private Vector3 steerTarget;
    private float nextRepath;
    private float stuckTimer;
    private float reverseTimer;
    private float returnTimer;
    private float nextDeployTime;
    private bool officersDeployedThisStop;
    private Renderer bodyRenderer;
    private readonly List<PoliceOfficer> officers = new List<PoliceOfficer>();

    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    void Start()
    {
        car = GetComponent<CarController>();
        car.IsDriven = false;                    // the player isn't driving this one
        path = new NavMeshPath();
        bodyRenderer = GetComponentInChildren<Renderer>();
        destination = WantedSystem.Instance.LastKnownPosition;
        if (IsPatrolCar) { destination = transform.position; StartCoroutine(LightsOffNextFrame()); }
        if (Armed) ApplyArmedLook();                                    // W3
    }

    // the beacons start blinking in their own Start: switch them off just after
    System.Collections.IEnumerator LightsOffNextFrame()
    {
        yield return null;
        if (state == State.Patrolling) Lights(false);
    }

    void Update()
    {
        WantedSystem wanted = WantedSystem.Instance;
        officers.RemoveAll(o => o == null);

        if (wanted.Stars == 0 && state != State.Returning && state != State.Parked && state != State.Patrolling)
            StartReturning();

        // W1b: only look for James while he's wanted (witnessing crimes is handled by WantedSystem)
        bool canSee = state != State.Returning && wanted.Stars > 0 && CanSeePlayer(wanted);
        CanSeeTarget = canSee;
        if (canSee) { lastSaw = Time.time; wanted.ReportSighting(wanted.PlayerTarget.position); }

        // ---------- Parked: officers are out on foot ----------
        if (state == State.Parked)
        {
            Brake();

            // Wanted level dropped before anyone got out? Just drive off.
            if (wanted.Stars == 0 && !officersDeployedThisStop) { StartReturning(); return; }

            // Once stopped, let the officers out (just once per stop) — a roadblock crew waits until James is close (W3)
            bool crewWaits = roadblockCrewWithin > 0f && FlatDistance(wanted.PlayerTarget.position) > roadblockCrewWithin;
            if (!officersDeployedThisStop && Mathf.Abs(car.Speed) < 1f && !crewWaits) DeployOfficers();

            // Everyone back in (or dead)? Carry on.
            if (officersDeployedThisStop && officers.Count == 0)
            {
                nextDeployTime = Time.time + redeployCooldown;
                if (wanted.Stars == 0) StartReturning();
                else state = canSee ? State.Pursuing : State.Searching;
            }
            return;
        }

        if (canSee && state != State.Returning)
        {
            if (state == State.Patrolling) { Lights(true); PoliceDirector.Adopt(this); }   // spotted him: joins the chase
            state = State.Pursuing;
        }

        switch (state)
        {
            case State.Patrolling:
                // drive a loop of the area at a steady pace, picking somewhere new now and then
                if (Time.time >= nextPatrolRoute || FlatDistance(destination) < arriveDistance * 2f)
                {
                    nextPatrolRoute = Time.time + 45f;
                    if (LundunPatrols.RoadPoint(transform.position, 80f, 220f, out Vector3 p)) destination = p;
                }
                break;

            case State.Responding:
                destination = wanted.LastKnownPosition;
                if (FlatDistance(destination) < arriveDistance)
                {
                    wanted.BeginSearch();
                    state = State.Searching;
                }
                break;

            case State.Pursuing:
                destination = wanted.PlayerTarget.position;
                if (!canSee)
                {
                    state = State.Searching;
                    destination = wanted.LastKnownPosition;
                }
                else if (ShouldPullOver(wanted))
                {
                    state = State.Parked;
                    officersDeployedThisStop = false;
                    return;
                }
                break;

            case State.Searching:
                // Reached the spot? Pick somewhere else nearby to look
                if (FlatDistance(destination) < arriveDistance)
                    destination = RandomNavPoint(wanted.LastKnownPosition, searchRadius);
                break;

            case State.Returning:
                returnTimer += Time.deltaTime;
                // Quietly remove the car once it's been leaving a while and is off screen
                if (returnTimer > 6f && (bodyRenderer == null || !bodyRenderer.isVisible))
                    Destroy(gameObject);
                break;
        }

        Drive();
    }

    void StartReturning()
    {
        if (IsPatrolCar) { ResumePatrol(); return; }      // W1b: patrols go back to their beat
        state = State.Returning;
        returnTimer = 0f;
        destination = transform.position + transform.forward * 80f;
        if (siren != null) siren.Stop();
    }

    bool ShouldPullOver(WantedSystem wanted)
    {
        if (officerPrefab == null || Time.time < nextDeployTime) return false;
        if (FlatDistance(wanted.PlayerTarget.position) > deployDistance) return false;

        // On foot: always. In a car: only if it's (nearly) stopped.
        CarController suspectCar = wanted.PlayerTarget.GetComponent<CarController>();
        return suspectCar == null || Mathf.Abs(suspectCar.Speed) < maxSuspectCarSpeed;
    }

    void DeployOfficers()
    {
        officersDeployedThisStop = true;
        int count = Mathf.Clamp(PoliceTiers.For(WantedSystem.Instance.Stars).officersPerCar, 1, 2);

        for (int i = 0; i < count; i++)
        {
            // Out of each side of the car
            float side = i == 0 ? 1f : -1f;
            Vector3 door = transform.position + transform.right * side * 1.8f;
            if (!NavMesh.SamplePosition(door, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;

            PoliceOfficer officer = Instantiate(officerPrefab, hit.position, transform.rotation);
            officer.Init(this);
            officers.Add(officer);
        }
    }

    // Officers call this when they reach the car to get back in
    public void Board(PoliceOfficer officer)
    {
        officers.Remove(officer);
        Destroy(officer.gameObject);
    }

    // Officers call this when they die
    public void OfficerLost(PoliceOfficer officer) => officers.Remove(officer);

    // BUSTED / WASTED call this while the screen is black: every cop vanishes
    public static void ClearAll()
    {
        foreach (PoliceOfficer officer in PoliceOfficer.Active.ToArray())
            if (officer != null) Destroy(officer.gameObject);

        foreach (PoliceAI police in FindObjectsByType<PoliceAI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Destroy(police.gameObject);
    }

    bool CanSeePlayer(WantedSystem wanted)
    {
        Transform target = wanted.PlayerTarget;
        if (target == null) return false;

        Vector3 eye = transform.position + Vector3.up * 1.5f;
        Vector3 targetPoint = target.position + Vector3.up * 1f;

        // W1: limited range and field of view (out of the windscreen and side windows); buildings break line of sight
        PoliceTiers.Tier tier = PoliceTiers.For(wanted.Stars);
        bool tracking = Time.time - lastSaw < PoliceTiers.KeepTrackFor;
        return PoliceTiers.Sees(eye, transform.forward, targetPoint, tier.carSightRange, tier.carSightCone, tracking, sightBlockers);
    }

    void Brake()
    {
        // Press "brake" only while still rolling forwards, so it never starts reversing
        float throttle = car.Speed > 0.5f ? -1f : 0f;
        car.SetTouchInput(throttle, 0f);
    }

    void Drive()
    {
        // Ask the NavMesh for a route every so often, then aim at the next corner
        if (Time.time >= nextRepath)
        {
            nextRepath = Time.time + repathInterval;
            steerTarget = destination;

            if (NavMesh.CalculatePath(Snap(transform.position), Snap(destination), NavMesh.AllAreas, path))
            {
                foreach (Vector3 corner in path.corners)
                {
                    if (FlatDistance(corner) > 5f) { steerTarget = corner; break; }
                }
            }
        }

        Vector3 toTarget = steerTarget - transform.position;
        toTarget.y = 0f;
        float angle = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);
        float steer = Mathf.Clamp(angle / steerSharpness, -1f, 1f);

        PoliceTiers.Tier tier = PoliceTiers.For(WantedSystem.Instance.Stars);
        float throttle = Mathf.Abs(angle) > 70f ? 0.4f : 1f;           // slow down for sharp turns
        if (state != State.Returning) throttle = Mathf.Min(throttle, tier.carThrottle);   // W1: low stars drive less hard
        if (state == State.Patrolling) throttle = Mathf.Min(throttle, 0.5f);               // W1b: just driving around

        // W1: from 3 stars, if you're in a car, they don't ease off: they ram you
        bool ram = tier.ram && WantedSystem.Instance.Stars > 0 && state != State.Patrolling
                && WantedSystem.Instance.PlayerTarget.GetComponent<CarController>() != null;
        if (state == State.Pursuing && FlatDistance(destination) < 8f && !ram) throttle = 0.35f;

        if (state == State.Searching) throttle = Mathf.Min(throttle, 0.6f);

        // Stuck against something? Reverse for a moment, steering the other way
        if (reverseTimer > 0f)
        {
            reverseTimer -= Time.deltaTime;
            car.SetTouchInput(-1f, -steer);
            return;
        }

        if (throttle > 0.3f && Mathf.Abs(car.Speed) < 1f) stuckTimer += Time.deltaTime;
        else stuckTimer = 0f;

        if (stuckTimer > 1.5f)
        {
            stuckTimer = 0f;
            reverseTimer = 1.2f;
        }

        car.SetTouchInput(throttle, steer);
    }

    float FlatDistance(Vector3 point)
    {
        Vector3 d = point - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    Vector3 Snap(Vector3 point)
    {
        return NavMesh.SamplePosition(point, out NavMeshHit hit, 6f, NavMesh.AllAreas) ? hit.position : point;
    }

    Vector3 RandomNavPoint(Vector3 centre, float radius)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 candidate = centre + Random.insideUnitSphere * radius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                return hit.position;
        }
        return centre;
    }
}
