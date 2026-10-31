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

    /// V3: the renderer that shows whether the car is on screen: the real body ("Shell", from Lundun → 10), else the first
    /// one switched on (the old boxes are switched off on the new cars, and a switched-off renderer is never "visible").
    Renderer VisibleBody()
    {
        Renderer first = null;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "Shell") return r;
            if (first == null && r.enabled && r.GetComponent<BlinkingLight>() == null) first = r;
        }
        return first != null ? first : GetComponentInChildren<Renderer>();
    }

    /// Armed response cars: a yellow roof marker, black side bands and a push bar, so you can tell them apart.
    void ApplyArmedLook()
    {
        LundunCarShape shape = GetComponent<LundunCarShape>();
        if (shape != null) { ApplyArmedLookV3(shape); return; }
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

    /// V3: on the real-car body: the yellow roof marker behind the light bar and a black push bar on the nose (the side
    /// bands of the old boxes don't fit a curved body, and the Battenburg livery already covers the sides).
    void ApplyArmedLookV3(LundunCarShape shape)
    {
        Renderer br = VisibleBody();
        if (br == null || br.sharedMaterial == null) return;
        Material yellow = new Material(br.sharedMaterial) { name = "ARV_Yellow" };
        yellow.SetTexture("_BaseMap", null); yellow.SetTexture("_MetallicGlossMap", null); yellow.DisableKeyword("_METALLICSPECGLOSSMAP");
        yellow.SetColor("_BaseColor", new Color(1f, 0.8f, 0.08f)); yellow.color = new Color(1f, 0.8f, 0.08f);
        Material black = new Material(yellow) { name = "ARV_Black" };
        black.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f)); black.color = new Color(0.04f, 0.04f, 0.05f);
        Vector3 roof = shape.Roof;
        Plate("ARV_Roof", roof + new Vector3(0f, 0.012f, -Mathf.Min(0.45f, shape.RoofLength * 0.3f)), new Vector3(shape.RoofWidth * 0.7f, 0.02f, Mathf.Min(0.6f, shape.RoofLength * 0.35f)), yellow);
        Vector3 nose = shape.Nose;
        Plate("ARV_PushBar", nose + new Vector3(0f, -shape.Height * 0.05f, 0.06f), new Vector3(shape.Width * 0.55f, shape.Height * 0.2f, 0.08f), black);
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
        roadArrived = false; route.Clear();    // L2: plan a new route to the call
        Lights(true);
    }

    void ResumePatrol()
    {
        state = State.Patrolling;
        nextPatrolRoute = 0f;
        Lights(false);
        pFrom = -1;                      // R3: find the road again
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
    private float heldTimer;                 // R3: waiting behind something in a chase
    private static int chaseMask = -1;
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
        car.ProportionalBrake = true;            // R3: eased braking (patrols; chases still brake flat out)
        body = GetComponent<Rigidbody>();        // L2
        path = new NavMeshPath();
        bodyRenderer = VisibleBody();
        destination = WantedSystem.Instance.LastKnownPosition;
        if (IsPatrolCar) { destination = transform.position; beat = transform.position; beatSet = true; StartCoroutine(LightsOffNextFrame()); }
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
                // L2: or as close as the road goes (the spot may be on a pavement or in a yard)
                if (FlatDistance(destination) < arriveDistance || roadArrived)
                {
                    roadArrived = false;
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
                // Reached the spot? Pick somewhere else nearby to look (L2: on a road, so the car can get there)
                if (FlatDistance(destination) < arriveDistance || roadArrived)
                {
                    roadArrived = false;
                    destination = LundunPatrols.RoadPoint(wanted.LastKnownPosition, 0f, searchRadius * 2f, out Vector3 rp)
                        ? rp : RandomNavPoint(wanted.LastKnownPosition, searchRadius);
                }
                break;

            case State.Returning:
                // L2: at the end of the road it was heading for: carry on to another one further out
                if (roadArrived)
                {
                    roadArrived = false;
                    if (LundunPatrols.RoadPoint(transform.position, 120f, 260f, out Vector3 away)) destination = away;
                }
                returnTimer += Time.deltaTime;
                // Quietly remove the car once it's been leaving a while and is off screen
                if (returnTimer > 6f && (bodyRenderer == null || !bodyRenderer.isVisible))
                    Destroy(gameObject);
                break;
        }

        // R3: on patrol they drive the roads like careful drivers (in lane, at the limit, braking for traffic and people);
        // off the roads (after a chase), the old way gets them back onto one
        if (state == State.Patrolling && PatrolOnRoads()) return;
        // L2: responding, chasing, searching and leaving: on the roads, looking ahead (the old way only off the roads)
        if (state != State.Patrolling && DriveWithControl(wanted)) return;
        driveNote = "OFF ROAD";
        Drive();
    }

    // ------------------------------------------------------------------ L2: driving with control
    // Responding, searching and leaving, they follow the roads lane by lane (the NavMesh is made for people: following
    // it took them over kerbs and between bollards). They look ahead the whole time: brake for anything in their way,
    // slow for cars crossing in front and for bends, go round a car stopped in the lane, and steer off walls, parked
    // cars and street furniture. Lights and siren mean faster and no giving way, not that they stop looking.
    // Close behind James they drive at him, but contact is only on purpose: from 3★ a PIT (alongside, then turning
    // into his back wheel), never head-on; below that, or just after a PIT, they sit on his tail at a gap.
    static readonly LundunDriver.Profile sirenDriver = new LundunDriver.Profile
    {
        style = LundunDriver.Style.Normal, speed = 1.3f, headway = 0.8f, minGap = 3f, brake = 8f, accel = 1f,
        junctionSpeed = 0f, junctionFrom = 0f, giveWay = 0, watchPeople = true, hornAfter = 1.5f
    };
    static readonly LundunDriver.Profile calmDriver = LundunDriver.For(LundunDriver.Style.Normal);
    static int whiskerMask = -1, bodyMask = -1;
    static readonly RaycastHit[] castHits = new RaycastHit[12];
    private readonly List<Vector3> route = new List<Vector3>();
    private Rigidbody body;
    private int routeAt, pitSide;
    private Vector3 routeGoal;
    private bool roadArrived;
    private float nextRoute, blockedFor, overtakeUntil, pitUntilAgain, crossCheckAt, crossLimit = float.MaxValue;
    private float stillFor, firstCarReset, lastSteer;
    private int carResets;
    private string driveNote = "";

    bool DriveWithControl(WantedSystem wanted)
    {
        if (whiskerMask == -1) { whiskerMask = LayerMask.GetMask("Default", "Smash"); bodyMask = LayerMask.GetMask("Vehicle", "NPC", "Player"); }
        if (reverseTimer > 0f)
        {
            reverseTimer -= Time.deltaTime;
            car.SetTouchInput(-1f, -lastSteer);
            driveNote = "BACK UP";
            return true;
        }
        Transform james = wanted.PlayerTarget;
        if (state == State.Pursuing && james != null && FlatDistance(james.position) < 40f) { ChaseClose(james); return true; }
        LundunNavigation nav = LundunNavigation.Instance;
        LundunRoadGraph g = nav != null ? nav.Graph : null;
        if (g == null) return false;
        return FollowRoute(g, destination, state != State.Returning);
    }

    /// Along the roads to 'goal' (the route is planned again every second, or as soon as the goal moves). False if
    /// there's no route or the car is well off the road (then the old NavMesh way gets it back).
    bool FollowRoute(LundunRoadGraph g, Vector3 goal, bool sirens)
    {
        Vector3 pos = transform.position; pos.y = 0f; goal.y = 0f;
        if (Time.time >= nextRoute || (goal - routeGoal).sqrMagnitude > 144f)
        {
            nextRoute = Time.time + 1f;
            if ((goal - routeGoal).sqrMagnitude > 144f) roadArrived = false;
            routeGoal = goal;
            route.Clear(); routeAt = 0;
            g.NearestOnRoad(new NavPoint(pos.x, pos.z), out int edge, out double t, out double off);
            if (edge >= 0 && off <= 14.0)
            {
                foreach (NavPoint p in g.Route(new NavPoint(pos.x, pos.z), new NavPoint(goal.x, goal.z), out double metres))
                    route.Add(new Vector3((float)p.x, 0f, (float)p.z));
                // the last bit off the road (onto a pavement, into a yard) is for people on foot: the car stops at the road
                if (route.Count >= 3 && (route[route.Count - 1] - route[route.Count - 2]).magnitude > 5f) route.RemoveAt(route.Count - 1);
            }
        }
        if (route.Count < 2) return false;

        // where along the route we are
        while (routeAt < route.Count - 2)
        {
            Vector3 a = route[routeAt], d = route[routeAt + 1] - a;
            float len = d.magnitude;
            if (len < 0.5f || Vector3.Dot(pos - a, d / len) > len - 2.5f) routeAt++; else break;
        }
        Vector3 A = route[routeAt], seg = route[routeAt + 1] - A;
        float segLen = seg.magnitude;
        Vector3 dir = segLen > 0.01f ? seg / segLen : transform.forward;
        float s = Mathf.Clamp(Vector3.Dot(pos - A, dir), 0f, segLen);
        if (Mathf.Abs(Vector3.Dot(pos - A, new Vector3(-dir.z, 0f, dir.x))) > 16f) { route.Clear(); nextRoute = Time.time + 0.5f; return false; }

        // aim a little ahead, on the left (UK); out to the right while going round something stopped in the lane
        float speed = car.Speed;
        float laneOff = Time.time < overtakeUntil ? -1.8f : sirens ? 1.3f : 1.8f;
        float remain = s + 6f + Mathf.Max(0f, speed) * 0.45f;
        Vector3 aim = route[route.Count - 1];
        for (int i = routeAt; i < route.Count - 1; i++)
        {
            Vector3 a = route[i], d = route[i + 1] - a;
            float l = d.magnitude;
            if (remain <= l || i == route.Count - 2)
            {
                Vector3 u = l > 0.01f ? d / l : dir;
                aim = a + u * Mathf.Min(remain, l) + (i > 0 ? new Vector3(-u.z, 0f, u.x) * laneOff : Vector3.zero);
                break;
            }
            remain -= l;
        }

        // speed: quick with the siren, slower into bends, stopping at the end
        PoliceTiers.Tier tier = PoliceTiers.For(WantedSystem.Instance.Stars);
        float want = !sirens ? 11f : state == State.Searching ? 11f : 20f * tier.carThrottle;
        float toNode = segLen - s;
        for (int k = routeAt + 1; k < route.Count - 1; k++)
        {
            if (toNode < 45f)
            {
                float ang = Vector3.Angle(route[k] - route[k - 1], route[k + 1] - route[k]);
                if (ang > 25f)
                {
                    float through = Mathf.Lerp(sirens ? 12f : 9f, sirens ? 6.5f : 4.5f, Mathf.InverseLerp(25f, 100f, ang));
                    want = Mathf.Min(want, through + Mathf.Sqrt(2f * 6f * Mathf.Max(0f, toNode - 4f)));
                }
            }
            toNode += (route[k + 1] - route[k]).magnitude;
        }
        want = Mathf.Min(want, Mathf.Sqrt(2f * 6f * Mathf.Max(0f, toNode - 4f)));       // toNode is now the distance to the end
        if (toNode < 7f) roadArrived = true;

        Go(want, SteerTo(aim), sirens ? sirenDriver : calmDriver, null, sirens ? "ROAD (siren)" : "ROAD");
        return true;
    }

    /// Within 40 m of James: on foot, up to him (they pull over and get out); in a car, on his tail, or a PIT from 3★.
    void ChaseClose(Transform james)
    {
        Vector3 jp = james.position; jp.y = 0f;
        Vector3 me = transform.position; me.y = 0f;
        float dist = (jp - me).magnitude;
        PoliceTiers.Tier tier = PoliceTiers.For(WantedSystem.Instance.Stars);
        CarController jc = james.GetComponent<CarController>();
        if (jc == null)
        {
            Go(Mathf.Min(16f * tier.carThrottle, Mathf.Sqrt(2f * 6f * Mathf.Max(0f, dist - 6f)) + 1f), SteerTo(jp), sirenDriver, null, "TO JAMES (on foot)");
            return;
        }

        Rigidbody jrb = jc.GetComponent<Rigidbody>();
        Vector3 jv = jrb != null ? jrb.linearVelocity : Vector3.zero; jv.y = 0f;
        Vector3 jf = james.forward; jf.y = 0f;
        jf = jf.sqrMagnitude > 0.001f ? jf.normalized : Vector3.forward;
        Vector3 jr = new Vector3(jf.z, 0f, -jf.x);
        float jSpeed = Mathf.Max(0f, Vector3.Dot(jv, jf));
        Vector3 rel = me - jp;
        float along = Vector3.Dot(rel, jf), lat = Vector3.Dot(rel, jr);
        float facing = Vector3.Dot(transform.forward, jf);

        // PIT: same way as James, not in front of him
        if (tier.ram && Time.time >= pitUntilAgain && facing > 0.3f && along < 1.5f)
        {
            if (Mathf.Abs(lat) > 0.6f) pitSide = lat > 0f ? 1 : -1; else if (pitSide == 0) pitSide = 1;
            if (along < -4f || Mathf.Abs(lat) > 4.5f)
                Go(jSpeed + 7f, SteerTo(jp - jf * 2.2f + jr * (pitSide * 2.4f) + jv * 0.5f), sirenDriver, james, "PIT: coming alongside");
            else
                Go(jSpeed + 4f, SteerTo(jp - jf * 1.4f + jv * 0.35f), sirenDriver, james, "PIT: turning in");
            return;
        }

        // coming at each other: pull over to the left and slow, never into him head-on
        if (facing < -0.3f && Vector3.Dot(jp - me, transform.forward) > 0f && dist < 30f)
        {
            Go(Mathf.Min(6f, Mathf.Max(0f, car.Speed)), SteerTo(me + transform.forward * 10f - transform.right * 3.5f), sirenDriver, null, "AVOID HEAD-ON");
            return;
        }

        // on his tail, about 10 m back (the look-ahead brakes for his car like any other)
        Vector3 lead = jp + jv * Mathf.Clamp(dist / 15f, 0.2f, 1.2f);
        Go(Mathf.Clamp(jSpeed + (dist - 10f) * 0.7f, 0f, 24f), SteerTo(lead), sirenDriver, null, Time.time < pitUntilAgain ? "FOLLOW (after PIT)" : "FOLLOW");
    }

    float SteerTo(Vector3 p)
    {
        Vector3 local = transform.InverseTransformPoint(new Vector3(p.x, transform.position.y, p.z));
        return Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg / 25f, -1f, 1f);
    }

    /// Drive towards 'want' m/s, steering 'steer', with the look-ahead: anything in the way (except 'ignore', the car
    /// being PITted), cars about to cross in front, and walls, parked cars and street furniture to steer off.
    void Go(float want, float steer, LundunDriver.Profile p, Transform ignore, string note)
    {
        float speed = car.Speed;
        bool overtaking = Time.time < overtakeUntil;

        // anything in the way, looking where we're turning (further the faster we go)
        bool emergency = false, blocked = false, carInWay = false;
        float look = Mathf.Min(45f, 6f + Mathf.Max(0f, speed) * 1.6f);
        Vector3 castDir = Quaternion.Euler(0f, steer * 18f, 0f) * transform.forward;
        Vector3 front = transform.position + transform.forward * 2.4f + Vector3.up * 0.6f;
        int n = Physics.SphereCastNonAlloc(front, 0.85f, castDir, castHits, look, bodyMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue; RaycastHit hit = new RaycastHit();
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = castHits[i];
            if (h.collider == null || h.transform.IsChildOf(transform)) continue;
            if (ignore != null && h.transform.IsChildOf(ignore)) continue;
            Health hp = h.collider.GetComponentInParent<Health>();
            if (hp != null && hp.IsDead) continue;
            if (h.distance < nearest) { nearest = h.distance; hit = h; }
        }
        if (nearest < float.MaxValue)
        {
            Rigidbody orb = hit.collider.attachedRigidbody;
            float other = orb != null ? Mathf.Max(0f, Vector3.Dot(orb.linearVelocity, castDir)) : 0f;
            float limit = Mathf.Sqrt(2f * p.brake * Mathf.Max(0f, nearest - p.minGap)) + other;
            want = Mathf.Min(want, limit);
            blocked = limit < 1f;
            carInWay = hit.collider.GetComponentInParent<CarController>() != null;
            if (nearest < 1.5f && speed > 1f) emergency = true;
        }

        // cars about to cross in front (time to the closest approach): slow so they pass first
        if (Time.time >= crossCheckAt)
        {
            crossCheckAt = Time.time + 0.2f;
            crossLimit = float.MaxValue;
            Vector3 myV = body != null ? body.linearVelocity : transform.forward * speed; myV.y = 0f;
            foreach (CarController c in CarController.All)
            {
                if (c == null || c == car || (ignore != null && c.transform == ignore)) continue;
                Vector3 rp = c.transform.position - transform.position; rp.y = 0f;
                if (rp.sqrMagnitude > 35f * 35f || Vector3.Dot(rp, transform.forward) < 1f) continue;
                Rigidbody crb = c.GetComponent<Rigidbody>();
                Vector3 ov = crb != null ? crb.linearVelocity : Vector3.zero; ov.y = 0f;
                if (ov.sqrMagnitude < 0.8f || Vector3.Angle(ov, transform.forward) < 35f) continue;   // parked, or going our way
                Vector3 rv = ov - myV;
                float vv = rv.sqrMagnitude;
                if (vv < 0.01f) continue;
                float t = -Vector3.Dot(rp, rv) / vv;
                if (t <= 0f || t > 2.5f || (rp + rv * t).magnitude > 3.5f) continue;
                crossLimit = Mathf.Min(crossLimit, Mathf.Max(0f, Mathf.Max(0f, speed) * (t / 2.5f) - 1f));
            }
        }
        want = Mathf.Min(want, crossLimit);

        // walls, parked cars, bollards, railings: steer off them, and don't drive into one straight ahead
        float wl = 3.5f + Mathf.Max(0f, speed) * 0.35f;
        Vector3 o = transform.position + Vector3.up * 0.7f + transform.forward * 2f, side = transform.right * 0.8f;
        if (Physics.Raycast(o - side, Quaternion.Euler(0f, -22f, 0f) * transform.forward, out RaycastHit hl, wl, whiskerMask, QueryTriggerInteraction.Ignore))
            steer += 0.6f * (1f - hl.distance / wl);
        if (Physics.Raycast(o + side, Quaternion.Euler(0f, 22f, 0f) * transform.forward, out RaycastHit hr, wl, whiskerMask, QueryTriggerInteraction.Ignore))
            steer -= 0.6f * (1f - hr.distance / wl);
        if (Physics.Raycast(o, transform.forward, out RaycastHit hc, wl + 2f, whiskerMask, QueryTriggerInteraction.Ignore))
            want = Mathf.Min(want, Mathf.Max(3f, Mathf.Sqrt(2f * 8f * Mathf.Max(0f, hc.distance - 1.5f))));
        steer = Mathf.Clamp(steer, -1f, 1f);

        // a car stopped in the lane: wait a moment, then go round it on the right
        if (blocked && carInWay && Mathf.Abs(speed) < 1.5f && ignore == null && state != State.Returning) blockedFor += Time.deltaTime; else blockedFor = 0f;
        if (blockedFor > 1.2f) { blockedFor = 0f; overtakeUntil = Time.time + 3.5f; }
        if (overtaking) want = Mathf.Min(want, 6f);

        // the watchdog: wants to move but hasn't for a couple of seconds (wedged on something): back up and try again;
        // three times in a row off screen, it's put back on the road
        // (also nose to nose with a car that isn't moving: the look-ahead says stop, but waiting there gets nowhere)
        bool wedged = carInWay && nearest < 1f && Mathf.Abs(speed) < 0.5f;
        if ((want > 1.5f || wedged) && Mathf.Abs(speed) < 0.8f && !emergency) stillFor += Time.deltaTime; else stillFor = 0f;
        if (stillFor > 2.2f)
        {
            stillFor = 0f;
            reverseTimer = 1.2f;
            lastSteer = steer;
            if (Time.time - firstCarReset > 15f) { firstCarReset = Time.time; carResets = 0; }
            if (++carResets >= 3 && (bodyRenderer == null || !bodyRenderer.isVisible)) { carResets = 0; reverseTimer = 0f; BackOnRoad(); }
            return;
        }

        lastSteer = steer;
        driveNote = overtaking ? note + " / going round" : note;
        car.SetTouchInput(LundunDriver.Pedal(speed, want, p, emergency), steer);
    }

    /// Stuck for good and nobody's looking: put the car on the nearest road, in the left lane, facing along it.
    void BackOnRoad()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return;
        Vector3 pos = transform.position;
        NavPoint q = nav.Graph.NearestOnRoad(new NavPoint(pos.x, pos.z), out int edge, out double t, out double off);
        if (edge < 0) return;
        nav.Graph.EdgeEnds(edge, out int a, out int b);
        NavPoint pa = nav.Graph.NodePosition(a), pb = nav.Graph.NodePosition(b);
        Vector3 along = new Vector3((float)(pb.x - pa.x), 0f, (float)(pb.z - pa.z)).normalized;
        Vector3 toGoal = destination - pos; toGoal.y = 0f;
        if (Vector3.Dot(along, toGoal) < 0f) along = -along;
        Vector3 spot = new Vector3((float)q.x, pos.y + 0.4f, (float)q.z) + new Vector3(-along.z, 0f, along.x) * 1.8f;
        transform.SetPositionAndRotation(spot, Quaternion.LookRotation(along, Vector3.up));
        if (body != null) { body.position = spot; body.rotation = transform.rotation; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        route.Clear(); nextRoute = 0f;
        driveNote = "BACK ON ROAD";
    }

    void OnCollisionEnter(Collision c)
    {
        // made contact with James's car: that was the PIT; drop back for a few seconds before trying again
        WantedSystem w = WantedSystem.Instance;
        if (w == null || w.PlayerTarget == null || c.collider == null) return;
        if (c.collider.transform.IsChildOf(w.PlayerTarget)) pitUntilAgain = Time.time + 5f;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static GUIStyle carDebugStyle;
    void OnGUI()
    {
        // PoliceTiers → Show Officer States: what each car is doing, above it
        if (!PoliceTiers.ShowOfficerStates) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.6f);
        if (sp.z <= 0f) return;
        if (carDebugStyle == null) carDebugStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        string s = state.ToString().ToUpperInvariant() + (state == State.Patrolling || state == State.Parked ? "" : ": " + driveNote);
        carDebugStyle.normal.textColor = s.Contains("BACK") || s.Contains("OFF ROAD") ? Color.yellow : s.Contains("PIT") ? new Color(1f, 0.5f, 0.3f) : new Color(0.6f, 0.85f, 1f);
        GUI.Label(new Rect(sp.x - 160f, Screen.height - sp.y - 14f, 320f, 28f), s, carDebugStyle);
    }
#endif

    // ------------------------------------------------------------------ R3: patrolling on the roads
    private int pFrom = -1, pTo = -1, pNext = -1;
    private Vector3 beat;
    private bool beatSet;
    private float nextRoadTry;
    private readonly List<int> pScratch = new List<int>();
    private readonly LundunDriver.Senses pSenses = new LundunDriver.Senses();
    private static readonly LundunDriver.Profile patrolDriver = LundunDriver.For(LundunDriver.Style.Careful);

    /// Follows the road network lane by lane (like traffic), turning at random at junctions but heading back towards
    /// its beat once more than 260 m from it. False if there's no road graph or the car is well off the road.
    bool PatrolOnRoads()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        LundunRoadGraph g = nav != null ? nav.Graph : null;
        if (g == null) return false;
        Vector3 pos = transform.position; pos.y = 0f;
        if (pFrom < 0 || pTo < 0)
        {
            if (Time.time < nextRoadTry) return false;                                  // looked just now: drive on to the road
            nextRoadTry = Time.time + 0.5f;
            g.NearestOnRoad(new NavPoint(pos.x, pos.z), out int edge, out double t, out double off);
            if (edge < 0 || off > 12.0) return false;                                  // not on a road: the old way first
            g.EdgeEnds(edge, out int a, out int b);
            NavPoint pa = g.NodePosition(a), pb = g.NodePosition(b);
            bool towardsB = Vector3.Dot(transform.forward, new Vector3((float)(pb.x - pa.x), 0f, (float)(pb.z - pa.z))) >= 0f;
            pFrom = towardsB ? a : b; pTo = towardsB ? b : a;
            if (!beatSet) { beat = pos; beatSet = true; }
            PickPatrolNext(g);
        }
        NavPoint na = g.NodePosition(pFrom), nb = g.NodePosition(pTo);
        Vector3 A = new Vector3((float)na.x, 0f, (float)na.z), B = new Vector3((float)nb.x, 0f, (float)nb.z);
        Vector3 dir = B - A; float segLen = dir.magnitude;
        if (segLen < 0.1f) { AdvancePatrol(g); return true; }
        dir /= segLen;
        g.EdgeInfo(pFrom, pTo, out double width, out string cls);
        float s = Vector3.Dot(pos - A, dir);
        Vector3 left = new Vector3(-dir.z, 0f, dir.x);
        float lateral = Vector3.Dot(pos - A, left);
        if (Mathf.Abs(lateral) > 14f) { pFrom = -1; return false; }                   // pushed well off the road
        const float lookAhead = 7f;
        if (segLen - s < lookAhead * 0.6f) { AdvancePatrol(g); return true; }

        // aim a little ahead in the left lane (onto the next road near the end)
        float aimS = s + lookAhead;
        Vector3 target;
        if (aimS <= segLen) target = A + dir * aimS + left * LundunDriver.LaneOffset(width, false, false);
        else
        {
            NavPoint nc = g.NodePosition(pNext);
            Vector3 C = new Vector3((float)nc.x, 0f, (float)nc.z), ndir = (C - B).normalized;
            g.EdgeInfo(pTo, pNext, out double nw, out string ncls);
            target = B + ndir * (aimS - segLen) + new Vector3(-ndir.z, 0f, ndir.x) * LundunDriver.LaneOffset(nw, false, false);
        }
        Vector3 local = transform.InverseTransformPoint(new Vector3(target.x, transform.position.y, target.z));
        float steer = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg / 25f, -1f, 1f);

        // speed: the road's, slower into turns and junctions, giving way, what's ahead
        float speed = car.Speed;
        float want = (cls == "ring" ? 14f : cls == "arterial" ? 11f : cls == "service" ? 5.5f : 8.5f) * patrolDriver.speed;
        float toEnd = segLen - s;
        NavPoint nn = g.NodePosition(pNext);
        float turn = Vector3.Angle(dir, (new Vector3((float)nn.x, 0f, (float)nn.z) - B).normalized);
        if (toEnd < 25f && turn > 30f) want = Mathf.Min(want, Mathf.Lerp(want, 5f, Mathf.InverseLerp(30f, 90f, turn)));
        g.NextNodes(pTo, -1, pScratch);
        if (pScratch.Count >= 3)
        {
            if (toEnd < patrolDriver.junctionFrom)
                want = Mathf.Min(want, patrolDriver.junctionSpeed + Mathf.Sqrt(2f * patrolDriver.brake * Mathf.Max(0f, toEnd - 6f)));
            if (toEnd < 30f && LundunDriver.GiveWay(transform, speed, B, toEnd, patrolDriver, pSenses))
                want = Mathf.Min(want, Mathf.Sqrt(2f * patrolDriver.brake * Mathf.Max(0f, toEnd - ((float)(width * 0.5) + 4f))));
        }
        bool emergency, blocked;
        want = Mathf.Min(want, LundunDriver.Ahead(transform, speed, patrolDriver, Mathf.Max(1.4f, (float)(width * 0.25)), pSenses, out emergency, out blocked));
        car.SetTouchInput(LundunDriver.Pedal(speed, want, patrolDriver, emergency), steer);
        return true;
    }

    void AdvancePatrol(LundunRoadGraph g)
    {
        pFrom = pTo; pTo = pNext;
        PickPatrolNext(g);
        pSenses.waitedAtJunction = 0f;
    }

    void PickPatrolNext(LundunRoadGraph g)
    {
        g.NextNodes(pTo, pFrom, pScratch);
        if (pScratch.Count == 0) { pNext = pFrom; return; }
        Vector3 here = transform.position; here.y = 0f;
        if ((here - beat).sqrMagnitude > 260f * 260f && pScratch.Count > 1)
        {
            // too far from the beat: take the turn that heads back towards it
            int best = pScratch[0]; float bd = float.MaxValue;
            foreach (int o in pScratch)
            {
                NavPoint q = g.NodePosition(o);
                float d = (new Vector3((float)q.x, 0f, (float)q.z) - beat).sqrMagnitude;
                if (d < bd) { bd = d; best = o; }
            }
            pNext = best;
        }
        else pNext = pScratch[Random.Range(0, pScratch.Count)];
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

        // R3: brake for cars and people in the way (not when ramming James, and not for James on foot: they pull over
        // for him); held up a while, they back up and go round as before
        bool held = false;
        if (!ram && state != State.Returning)
        {
            if (chaseMask == -1) chaseMask = LayerMask.GetMask("Vehicle", "NPC");
            float speedNow = Mathf.Max(0f, car.Speed);
            float look = 5f + speedNow * 1.1f;
            Vector3 front = transform.position + transform.forward * 2.8f + Vector3.up * 0.6f;
            if (Physics.SphereCast(front, 0.8f, transform.forward, out RaycastHit hit, look, chaseMask, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(transform))
            {
                bool jamesCar = WantedSystem.Instance.PlayerTarget != null && hit.transform.IsChildOf(WantedSystem.Instance.PlayerTarget);
                if (!jamesCar || state != State.Pursuing)
                {
                    float safe = Mathf.Sqrt(2f * 9f * Mathf.Max(0f, hit.distance - 3f));
                    if (speedNow > safe + 0.5f) { throttle = hit.distance < 4f ? -1f : -Mathf.Clamp01((speedNow - safe) / 6f); held = true; }
                    else throttle = Mathf.Min(throttle, Mathf.Clamp01(safe / 8f));
                    if (speedNow < 1f && hit.distance < 6f) held = true;
                }
            }
        }
        if (held && Mathf.Abs(car.Speed) < 1f) heldTimer += Time.deltaTime; else heldTimer = 0f;
        if (heldTimer > 2.5f) { heldTimer = 0f; reverseTimer = 1.2f; }

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
