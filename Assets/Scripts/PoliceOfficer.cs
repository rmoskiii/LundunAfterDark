// W1: some tuning fields below are kept (so nothing in the prefabs/scene is lost) but the wanted level now drives
// them through PoliceTiers. This hides the "assigned but never used" warnings for those.
#pragma warning disable 0414
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// W1: behaviour comes from the wanted level (PoliceTiers): how fast they run, how far and how wide they see,
/// how well they track you. Patrol officers are unarmed (arrest only; Taser from 2★). Only armed response
/// officers (from 3★) carry guns, and they need a moment to react before the first shot.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class PoliceOfficer : MonoBehaviour
{
    private enum State { Chase, Search, ReturnToCar, Dead, Patrol }

    // Every officer on foot (BustedSystem checks these to see who's grabbing you)
    public static readonly List<PoliceOfficer> Active = new List<PoliceOfficer>();

    [Header("Movement")]
    [SerializeField] private float runSpeed = 7f;
    [SerializeField] private float walkSpeed = 2.5f;

    [Header("Senses")]
    [SerializeField] private float sightRange = 35f;
    [SerializeField] private LayerMask sightBlockers = 1;     // Default layer = buildings
    [SerializeField] private float giveUpAfter = 10f;          // seconds without seeing you
    [SerializeField] private float giveUpCarDistance = 25f;    // you drove off: run back to the car

    [Header("Shooting (only at 3+ stars)")]
    [SerializeField] private int starsToShoot = 3;
    [SerializeField] private float shootRange = 18f;
    [SerializeField] private float closeRange = 4f;            // closer than this: go for the arrest instead
    [SerializeField] private float timeBetweenShots = 0.6f;
    [SerializeField] private float damageOnFoot = 8f;
    [SerializeField] private float damageToCar = 3f;
    [SerializeField] private float closeHitChance = 0.6f;
    [SerializeField] private float farHitChance = 0.2f;
    [SerializeField] private AudioClip gunshot;
    [SerializeField] private Material tracerMaterial;

    public bool IsChasing => state == State.Chase;
    public bool IsDown => state == State.Dead;
    /// W2: this officer can see James right now (red sight cone on the minimap).
    public bool SeesPlayer => sawLastFrame && state != State.Dead;
    /// W1b: an everyday foot patrol (no car). Walks a beat; when the chase is over, goes back to it.
    public bool IsPatrolOfficer { get; private set; }
    public bool OnBeat => state == State.Patrol;
    /// Armed response officer (carries a gun). Patrol officers don't.
    public bool Armed { get; private set; }

    private State state = State.Chase;
    private NavMeshAgent agent;
    private Animator animator;
    private Health health;
    private PoliceAI myCar;
    private AudioSource audioSource;
    private LineRenderer tracer;
    private Renderer bodyRenderer;
    private float lastSeenTime;
    private float nextRepath;
    private float nextShot;
    private float lostTimer;
    private float alertedUntil, reactUntil, taserAt, nextTaser;
    private bool sawLastFrame;

    // R1: no more freezing mid-chase
    private bool aiming;                       // the aiming pose is on (so it can always be switched off again)
    private float nextNavFix;                  // off the walkable ground: try to get back on
    private float stuckFor, nextHoldMove;      // not moving although he wants to / can't reach James
    private Vector3 holdSpot, holdEnd;
    private NavMeshPath holdPath;
    private bool holding;
    private string debugNote = "";

    public void Init(PoliceAI car)
    {
        myCar = car;
        Armed = car != null && car.Armed;
        if (Armed) ArmedLook();
    }

    /// W3: armed response officers wear dark tactical kit and carry a visible gun, so you can tell them apart.
    void ArmedLook()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer) continue;
            foreach (Material m in r.materials)
            {
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                Color dark = new Color(c.r * 0.32f, c.g * 0.34f, c.b * 0.4f, c.a);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", dark);
                m.color = dark;
            }
        }
        if (health != null) health.RefreshColour();
        if (animator == null || !animator.isHuman) return;
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        if (Rifle(hand)) return;                                    // G1: a rifle (the carbine James can take off them)
        GameObject gun = LundunAngryDriver.GunModel;                // (the old pistol, if the rifle couldn't be built)
        if (gun == null) return;
        GameObject prop = Instantiate(gun, hand);
        prop.name = "OfficerGun";
        float s = transform.localScale.y; if (s < 0.0001f) s = 1f;
        prop.transform.localScale = gun.transform.localScale / s;
        prop.transform.localPosition = new Vector3(0.05f, 0.08f, 0.02f) / s;
        prop.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);
        foreach (Collider c in prop.GetComponentsInChildren<Collider>()) Destroy(c);
    }

    // ------------------------------------------------------------------ G1 (how each officer goes about it)
    /// Each officer has his own way: RUSHER takes up the chase at once and keeps moving (short bursts); STEADY is the
    /// standard; CAUTIOUS holds back when he first takes up the chase (3–5.5 s: armed, standing off and firing;
    /// unarmed, waiting) before he comes on. From 2★ they run at their own pace too: rushers at the tier's full speed,
    /// the rest a little slower. Never faster than the tier's speed.
    private enum Temper { Rusher, Steady, Cautious }
    private Temper temper;
    private float speedFactor = 1f, holdBackUntil = -1f;
    private bool heldBack;
    private State lastState = State.Patrol;

    void PickTemper()
    {
        float r = Random.value;
        temper = r < 0.35f ? Temper.Rusher : r < 0.75f ? Temper.Steady : Temper.Cautious;
        speedFactor = temper == Temper.Rusher ? 1f : temper == Temper.Steady ? Random.Range(0.88f, 0.96f) : Random.Range(0.8f, 0.88f);
    }

    void Engage(int stars)
    {
        if (temper != Temper.Cautious || stars < 2 || heldBack) { holdBackUntil = -1f; return; }
        heldBack = true;                                                    // once: after that he comes on like the rest
        holdBackUntil = Time.time + Random.Range(3f, 5.5f);
    }

    /// His running speed: the tier's, or a little under it from 2★ (never over).
    float RunSpeed(PoliceTiers.Tier tier, int stars)
    {
        return stars >= 2 ? tier.runSpeed * Mathf.Min(1f, speedFactor) : tier.runSpeed;
    }

    // ------------------------------------------------------------------ G1 (police rifles)
    /// The end of his rifle (null if he hasn't one).
    public Transform RifleMuzzle { get { return rifleMuzzle; } }
    /// For a scene where he only stands and fires (M01's cold open): the armed look and rifle, and the aim pose.
    public void ShowArmedForScene()
    {
        if (rifle == null) ArmedLook();
        LundunAnim.Bool(animator, "Aim", true);
    }
    private GameObject rifle;
    private Transform rifleMuzzle;
    private AudioClip rifleShot;

    /// The carbine model in his right hand: the barrel along his fingers, the top of the gun the way his thumb points,
    /// so it sits right in any pose (carried low when he's walking, levelled when he aims). Worked out from his own
    /// hand bones, so it fits whatever model he is. Returns false if it couldn't be made.
    bool Rifle(Transform hand)
    {
        GameObject g = LundunGunModels.Build(LundunWeapons.Kind.Carbine);
        if (g == null) return false;
        Transform finger = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        Transform thumb = animator.GetBoneTransform(HumanBodyBones.RightThumbProximal);
        Vector3 along = finger != null ? finger.position - hand.position : transform.forward;
        if (along.sqrMagnitude < 0.0001f) along = transform.forward;
        along.Normalize();
        Vector3 up = thumb != null ? thumb.position - hand.position : transform.up;
        up = Vector3.ProjectOnPlane(up, along);
        if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(transform.up, along);
        foreach (Collider c in g.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        g.name = "OfficerRifle";
        g.transform.SetPositionAndRotation(hand.position + along * 0.05f, Quaternion.LookRotation(along, up.normalized));
        g.transform.SetParent(hand, true);
        Vector3 ls = hand.lossyScale;
        g.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(ls.x)), 1f / Mathf.Max(0.0001f, Mathf.Abs(ls.y)), 1f / Mathf.Max(0.0001f, Mathf.Abs(ls.z)));
        rifle = g;
        rifleMuzzle = g.transform.Find("Muzzle");
        rifleShot = LundunGunAudio.Shot(LundunWeapons.Kind.Carbine);
        return true;
    }

    // ------------------------------------------------------------------ W1b: foot patrols
    private PoliceOfficer partner;            // walks beside the leader
    private Vector3 beatTarget;
    private float nextBeat, warnUntil, respondUntil = -99f;
    private Vector3 warnAt;

    /// Start walking a beat (unarmed). Give a partner to walk beside them instead of picking a route.
    public void StartPatrol(PoliceOfficer walkBeside)
    {
        IsPatrolOfficer = true;
        Armed = false;
        myCar = null;
        partner = walkBeside;
        state = State.Patrol;
        nextBeat = 0f;
    }

    /// Heard about a crime nearby: go to where it happened (W1b, by how serious it is).
    public void Respond()
    {
        if (state == State.Dead) return;
        lastSeenTime = Time.time;
        respondUntil = Time.time + 45f;           // time to get there before giving up
        state = State.Search;
    }

    /// A minor offence in front of them, first time: a telling-off instead of an arrest.
    public void Warn(Vector3 suspect)
    {
        if (state == State.Dead) return;
        warnUntil = Time.time + 3f;
        warnAt = suspect;
    }

    void Beat()
    {
        SetAim(false);
        agent.isStopped = false;
        agent.speed = walkSpeed * 0.6f;
        agent.stoppingDistance = 0.5f;

        if (Time.time < warnUntil)
        {
            // stop and face whoever they're telling off
            Stop();
            FaceTowards(warnAt);
            return;
        }

        if (partner != null && !partner.IsDown && partner.OnBeat)
        {
            Vector3 side = partner.transform.position + partner.transform.right * 1.1f - partner.transform.forward * 0.3f;
            Repath(side, 0.5f);
            return;
        }
        if (Time.time >= nextBeat || FlatDistance(beatTarget) < 2f)
        {
            nextBeat = Time.time + 40f;
            if (LundunPatrols.PavementPoint(transform.position, 30f, 90f, out Vector3 p)) beatTarget = p;
            else beatTarget = transform.position + Random.insideUnitSphere * 20f;
            nextRepath = 0f;
        }
        Repath(beatTarget, 1f);
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        health = GetComponent<Health>();
        bodyRenderer = GetComponentInChildren<Renderer>();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 6f;
        audioSource.maxDistance = 100f;
        PickTemper();

        if (tracerMaterial != null)
        {
            tracer = new GameObject("OfficerTracer").AddComponent<LineRenderer>();
            tracer.transform.SetParent(transform, false);
            tracer.material = tracerMaterial;
            tracer.positionCount = 2;
            tracer.startWidth = 0.03f;
            tracer.endWidth = 0.015f;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.enabled = false;
        }

        lastSeenTime = Time.time;
        nextShot = Time.time + 1f;      // a beat to draw their weapon
        nextTaser = Time.time + 1.5f;
    }

    void OnEnable()
    {
        Active.Add(this);
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        PlayerCombat.GunshotFired += OnGunshot;
    }

    void OnDisable()
    {
        Active.Remove(this);
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
        PlayerCombat.GunshotFired -= OnGunshot;
    }

    // a gunshot nearby: they look round (see in every direction) for a moment
    void OnGunshot(Vector3 at)
    {
        if (state == State.Dead) return;
        if ((at - transform.position).sqrMagnitude < PoliceTiers.HearGunfire * PoliceTiers.HearGunfire) alertedUntil = Time.time + 2f;
    }

    void Update()
    {
        if (state == State.Dead) return;

        if (animator != null)
            animator.SetFloat("Speed", agent.velocity.magnitude, 0.1f, Time.deltaTime);

        // R1: off the walkable ground (got out of a car at a kerb, knocked about): put him back on it, don't just stop
        if (!agent.isOnNavMesh) { FixNavMesh(); debugNote = "OFF NAVMESH"; return; }

        // L3: shoved: the stagger moves him, then he carries on
        if (LundunStagger.Busy(gameObject)) { sawLastFrame = false; return; }

        // Hold still while BUSTED / WASTED plays out
        if (VehicleSystem.Locked) { Stop(); return; }

        WantedSystem wanted = WantedSystem.Instance;
        Transform target = wanted.PlayerTarget;
        PoliceTiers.Tier tier = PoliceTiers.For(wanted.Stars);
        bool canSee = wanted.Stars > 0 && CanSee(target, tier);

        if (canSee)
        {
            if (!sawLastFrame) reactUntil = Time.time + tier.reactionTime;     // just spotted you: a beat before shooting
            lastSeenTime = Time.time;
            wanted.ReportSighting(target.position);
        }
        sawLastFrame = canSee;

        // ---------- decide what to do ----------
        bool suspectDroveOff = target.GetComponent<CarController>() != null
                            && FlatDistance(target.position) > giveUpCarDistance;

        bool lost = wanted.Stars == 0 || suspectDroveOff || (Time.time - lastSeenTime > tier.giveUpAfter && Time.time > respondUntil);
        if (IsPatrolOfficer)
        {
            // patrols: chase while they can, then back to the beat (never "back to the car")
            if (canSee && !suspectDroveOff) { state = State.Chase; respondUntil = -99f; }
            else if (state == State.Patrol) { }
            else if (lost) state = State.Patrol;
            else if (state == State.Chase) state = State.Search;
        }
        else if (lost)
            state = State.ReturnToCar;
        else if (canSee)
            state = State.Chase;
        else if (state == State.Chase)
            state = State.Search;

        // R1: out of the chase, nothing carries over: no Taser wind-up, no aiming pose (that left officers gliding
        // round in the aim stance, looking frozen), nothing held
        if (state != State.Chase) { CancelTaser(); SetAim(false); }
        if (state != State.Chase && state != State.Search) holding = false;
        canSeeNow = canSee;
        Watchdog(target, wanted);
        if (state == State.Chase && lastState != State.Chase) Engage(wanted.Stars);     // G1: each time he takes up the chase
        lastState = state;

        // ---------- do it ----------
        switch (state)
        {
            case State.Chase:      Chase(target, wanted.Stars, tier); break;
            case State.Search:     Search(wanted.LastKnownPosition, tier); break;
            case State.ReturnToCar: ReturnToCar(); break;
            case State.Patrol:     Beat(); break;
        }
    }

    void Chase(Transform target, int stars, PoliceTiers.Tier tier)
    {
        float distance = FlatDistance(target.position);
        bool playerAlive = PlayerHealth.Instance != null && !PlayerHealth.Instance.IsDead;

        // unarmed officers (2★ and up): Taser when close enough, James on foot
        if (!Armed && (taserAt > 0f || TaserReady(target, tier, distance)) && playerAlive)
        {
            if (Taser(target)) { debugNote = "TASER"; return; }
            // R1: the shot was called off (out of range, out of sight, into a car): carry on chasing this frame
        }

        // G1: a cautious one holds back first: armed, he stands off and fires; unarmed, he waits for backup a moment
        if (Time.time < holdBackUntil && distance > closeRange && playerAlive)
        {
            bool inRange = Armed && stars >= starsToShoot && distance < shootRange && canSeeNow;
            if (inRange || !Armed)
            {
                SetAim(inRange);
                Stop();
                FaceTowards(target.position);
                if (inRange && Time.time >= nextShot && Time.time >= reactUntil)
                {
                    nextShot = Time.time + tier.timeBetweenShots * Random.Range(0.8f, 1.3f);
                    FireAt(target, tier);
                    LundunAnim.Trigger(animator, "Shoot");
                }
                debugNote = inRange ? "HOLD + FIRE" : "HOLD BACK";
                return;
            }
        }

        bool shooting = Armed && stars >= starsToShoot && distance > closeRange && distance < shootRange && playerAlive && canSeeNow;

        // L2: fire and move: a burst standing still (2–3.5 s), then a second or so moving up, never rooted to one spot
        if (shooting && Time.time < advanceUntil) shooting = false;
        else if (shooting)
        {
            if (standingSince < 0f) { standingSince = Time.time; burstFor = temper == Temper.Rusher ? Random.Range(1f, 1.8f) : Random.Range(2f, 3.5f); }
            if (Time.time - standingSince > burstFor) { advanceUntil = Time.time + (temper == Temper.Rusher ? Random.Range(1.4f, 2.2f) : Random.Range(1f, 1.6f)); standingSince = -1f; shooting = false; }
        }
        else standingSince = -1f;
        if (!shooting && Time.time < advanceUntil) debugNote = "MOVE UP";

        SetAim(shooting);
        if (shooting)
        {
            // Stand, face you, fire
            Stop();
            FaceTowards(target.position);
            if (Time.time >= nextShot && Time.time >= reactUntil)
            {
                nextShot = Time.time + tier.timeBetweenShots * Random.Range(0.8f, 1.3f);
                FireAt(target, tier);
                LundunAnim.Trigger(animator, "Shoot");
            }
            debugNote = "SHOOT";
            return;
        }

        // Run you down for the arrest
        agent.isStopped = false;
        agent.speed = RunSpeed(tier, stars);
        agent.stoppingDistance = 1.2f;
        if (!HoldIfUnreachable(target.position))
        {
            Repath(target.position, tier.repath);
            Unstick(distance > 2.5f);
            if (Time.time >= advanceUntil) debugNote = "CHASE";
        }
        if (distance < 2f) FaceTowards(target.position);
    }

    void Search(Vector3 lastKnown, PoliceTiers.Tier tier)
    {
        agent.isStopped = false;
        agent.speed = RunSpeed(tier, WantedSystem.Instance != null ? WantedSystem.Instance.Stars : 0) * 0.7f;
        agent.stoppingDistance = 1f;
        if (HoldIfUnreachable(lastKnown)) return;
        Repath(lastKnown, tier.repath);
        Unstick(FlatDistance(lastKnown) > 2f);
        debugNote = "SEARCH";
    }

    // ------------------------------------------------------------------ R1: never freeze
    private bool canSeeNow;

    // ------------------------------------------------------------------ L2: the watchdog
    private float standingSince = -1f, burstFor, advanceUntil;
    private Vector3 wdPos;
    private float wdSince, lastReset = -99f, resetShowUntil;
    private int resets;

    /// Chasing or searching, an officer who hasn't moved half a metre in 2.5 s (and isn't standing to shoot, aiming the
    /// Taser, pacing where he can't reach James, or right next to him) is reset: Taser and aim cancelled, a fresh path
    /// to the nearest point he CAN reach, and a step onto clear ground if it happens twice in a row. The overlay shows
    /// "RESET" (and how many so far) when it does.
    void Watchdog(Transform target, WantedSystem wanted)
    {
        Vector3 here = transform.position;
        bool exempt = (state != State.Chase && state != State.Search) || holding || taserAt > 0f
                   || debugNote == "SHOOT" || FlatDistance(target.position) < 2.5f
                   || (state == State.Search && FlatDistance(wanted.LastKnownPosition) < 3f);    // looking round where James was
        Vector3 moved = here - wdPos; moved.y = 0f;
        if (exempt || moved.sqrMagnitude > 0.36f) { wdPos = here; wdSince = Time.time; return; }
        if (Time.time - wdSince < 2.5f) return;

        resets++;
        resetShowUntil = Time.time + 1.5f;
        wdPos = here; wdSince = Time.time;
        CancelTaser();
        aiming = true; SetAim(false);                 // always tell the animator: aim off
        stuckFor = 0f;
        if (!agent.isOnNavMesh) { nextNavFix = 0f; FixNavMesh(); }
        if (!agent.isOnNavMesh) return;
        Vector3 goal = Reachable(state == State.Chase ? target.position : wanted.LastKnownPosition);
        if (Time.time - lastReset < 6f)
        {
            // second time running: a step towards the goal onto clear ground first
            Vector3 to = goal - here; to.y = 0f;
            Vector3 step = here + (to.sqrMagnitude > 0.01f ? to.normalized : transform.forward) * 1.5f + Random.insideUnitSphere * 0.8f;
            if (NavMesh.SamplePosition(step, out NavMeshHit sh, 2f, NavMesh.AllAreas)) agent.Warp(sh.position);
        }
        lastReset = Time.time;
        agent.isStopped = false;
        agent.ResetPath();
        agent.SetDestination(goal);
        nextRepath = Time.time + 0.5f;
    }

    /// The nearest point on the walkable ground to 'p' (James on a roof, in a car on a kerb, over a wall).
    static Vector3 Reachable(Vector3 p)
    {
        if (NavMesh.SamplePosition(p, out NavMeshHit h, 2f, NavMesh.AllAreas)) return h.position;
        if (NavMesh.SamplePosition(p, out h, 6f, NavMesh.AllAreas)) return h.position;
        if (NavMesh.SamplePosition(p, out h, 15f, NavMesh.AllAreas)) return h.position;
        return p;
    }

    /// The aiming pose on or off (only tells the animator when it changes, and always lets it be switched off).
    void SetAim(bool on)
    {
        if (on == aiming) return;
        aiming = on;
        LundunAnim.Bool(animator, "Aim", on);
    }

    void CancelTaser()
    {
        if (taserAt <= 0f) return;
        taserAt = 0f;
        nextTaser = Mathf.Max(nextTaser, Time.time + 1f);       // a moment before he tries again
        SetAim(false);
    }

    /// Off the walkable ground: every half second, look for it within 4 m and put him back on it.
    void FixNavMesh()
    {
        if (!agent.enabled || Time.time < nextNavFix) return;
        nextNavFix = Time.time + 0.5f;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas)) agent.Warp(hit.position);
    }

    /// Where he's going can't be reached (James on a roof, in a shop, over the seam): instead of standing at the end
    /// of the path, he paces about the nearest point he can reach, facing James, and every few seconds checks whether
    /// James can be reached again. True while that's happening.
    bool HoldIfUnreachable(Vector3 goal)
    {
        if (!holding)
        {
            // start holding when his own path to the goal stops short of it and he's nearly at its end
            if (agent.pathPending || !agent.hasPath || agent.pathStatus != NavMeshPathStatus.PathPartial) return false;
            Vector3 end = agent.pathEndPosition;
            if (!FarFrom(goal, end) || FlatDistance(end) > 3f) return false;
            holding = true;
            holdEnd = end;
            nextHoldMove = 0f;
        }
        if (Time.time >= nextHoldMove)
        {
            nextHoldMove = Time.time + Random.Range(2.5f, 4f);
            // can he get to James now?
            if (holdPath == null) holdPath = new NavMeshPath();
            if (NavMesh.CalculatePath(transform.position, goal, NavMesh.AllAreas, holdPath) && holdPath.status == NavMeshPathStatus.PathComplete)
            { holding = false; nextRepath = 0f; return false; }
            Vector3 around = holdEnd + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
            holdSpot = NavMesh.SamplePosition(around, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : holdEnd;
            agent.isStopped = false;
            agent.SetDestination(holdSpot);
        }
        if (FlatDistance(holdSpot) < 1.2f) FaceTowards(goal);
        debugNote = "HOLD";
        return true;
    }

    static bool FarFrom(Vector3 goal, Vector3 end)
    {
        Vector3 gap = goal - end;
        return new Vector2(gap.x, gap.z).magnitude > 2.5f || Mathf.Abs(gap.y) > 1.8f;
    }

    /// Wants to move but hasn't for a second and a half (blocked by a car, a crowd, a bad path): find a new path; if
    /// that doesn't help, step aside onto the nearest clear ground.
    void Unstick(bool shouldMove)
    {
        if (!shouldMove || agent.pathPending || agent.velocity.sqrMagnitude > 0.25f) { stuckFor = 0f; return; }
        stuckFor += Time.deltaTime;
        if (stuckFor > 1.5f && stuckFor - Time.deltaTime <= 1.5f) { agent.ResetPath(); nextRepath = 0f; }
        if (stuckFor > 3f)
        {
            stuckFor = 0f;
            Vector3 side = transform.position + Random.insideUnitSphere * 2.5f;
            if (NavMesh.SamplePosition(side, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) agent.Warp(hit.position);
            nextRepath = 0f;
            debugNote = "UNSTICK";
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static GUIStyle debugStyle;
    void OnGUI()
    {
        if (!PoliceTiers.ShowOfficerStates || state == State.Dead) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
        if (sp.z <= 0f) return;
        if (debugStyle == null) debugStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        string s = state == State.Chase || state == State.Search ? debugNote : state.ToString().ToUpperInvariant();
        if (Time.time < resetShowUntil) s = "RESET " + resets + " · " + s;          // L2: the watchdog just stepped in
        if (aiming) s += " (aim)";
        if (agent != null && agent.enabled && agent.isOnNavMesh && agent.isStopped) s += " (stopped)";
        debugStyle.normal.textColor = s.StartsWith("RESET") ? new Color(1f, 0.45f, 0.3f)
            : s.StartsWith("HOLD") || s.StartsWith("UNSTICK") || s.StartsWith("OFF") ? Color.yellow : Color.white;
        GUI.Label(new Rect(sp.x - 120f, Screen.height - sp.y - 14f, 240f, 28f), s, debugStyle);
    }
#endif

    // ------------------------------------------------------------------ Taser (W1)
    bool TaserReady(Transform target, PoliceTiers.Tier tier, float distance)
    {
        PoliceTiers p = PoliceTiers.Instance;
        if (p == null || !tier.taser || Time.time < nextTaser || !canSeeNow) return false;
        if (target.GetComponent<CarController>() != null) return false;                  // not through a car
        PlayerController pc = target.GetComponent<PlayerController>();
        if (pc == null || !pc.CanBeTased) return false;
        return distance >= p.taserMin && distance <= p.taserMax;
    }

    /// Stop, aim, fire the Taser: a hit stuns James for a moment (plenty of time for an arrest).
    /// R1: false = called off (James got out of range or out of sight, or into a car, during the wind-up): the officer
    /// drops his aim and carries on chasing instead of standing there.
    bool Taser(Transform target)
    {
        PoliceTiers p = PoliceTiers.Instance;
        if (p == null) { CancelTaser(); return false; }
        if (taserAt > 0f)
        {
            bool inCar = target.GetComponent<CarController>() != null;
            if (inCar || FlatDistance(target.position) > p.taserMax + 1.5f || !canSeeNow) { CancelTaser(); return false; }
        }
        Stop();
        FaceTowards(target.position);
        SetAim(true);
        if (taserAt <= 0f) { taserAt = Time.time + p.taserWindup; return true; }
        if (Time.time < taserAt) return true;

        taserAt = 0f;
        nextTaser = Time.time + p.taserCooldown;
        SetAim(false);
        Vector3 from = transform.position + Vector3.up * 1.3f + transform.forward * 0.4f;
        Vector3 aim = PoliceAim.Centre(target);
        float chance = p.taserHitChance - PoliceTiers.TargetSpeed(target) * 0.04f;
        bool inRange = FlatDistance(target.position) <= p.taserMax + 0.5f;
        bool clear = !Physics.Linecast(from, aim, sightBlockers, QueryTriggerInteraction.Ignore);
        PlayerController pc = target.GetComponent<PlayerController>();
        Vector3 end = aim;
        if (inRange && clear && pc != null && Random.value < chance)
        {
            pc.Stun(p.taserStun, p.taserImmunity);
            if (PlayerHealth.Instance != null) PlayerHealth.Instance.TakeDamage(p.taserDamage);
            LundunHaptics.Medium();
            if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast("TASED!");
        }
        else end = aim + Vector3.Cross(Vector3.up, (aim - from).normalized) * Random.Range(-1.2f, 1.2f) + Vector3.down * 0.6f;
        if (tracer != null) StartCoroutine(ShowTracer(from, end, new Color(1f, 0.92f, 0.25f), 0.3f));
        return true;
    }

    void ReturnToCar()
    {
        debugNote = "TO CAR";
        agent.isStopped = false;
        agent.speed = walkSpeed;
        agent.stoppingDistance = 0.5f;

        // Car gone (blown up / removed)? Just wander off and vanish when nobody's looking
        if (myCar == null || !myCar.enabled)
        {
            lostTimer += Time.deltaTime;
            if (lostTimer > 5f && (bodyRenderer == null || !bodyRenderer.isVisible))
                Destroy(gameObject);
            return;
        }

        agent.speed = WantedSystem.Instance.Stars > 0 ? PoliceTiers.For(WantedSystem.Instance.Stars).runSpeed : walkSpeed;
        Repath(myCar.transform.position);
        if (FlatDistance(myCar.transform.position) < 2.8f) myCar.Board(this);
    }

    void FireAt(Transform target, PoliceTiers.Tier tier)
    {
        Vector3 from = transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;
        if (rifleMuzzle != null) from = rifleMuzzle.position;                       // G1: from the end of his rifle
        Vector3 aim = PoliceAim.Centre(target);
        float distance = Vector3.Distance(from, aim);

        // W1: accuracy and damage come from the wanted level; a moving target is harder to hit
        float chance = Mathf.Lerp(tier.hitChanceNear, tier.hitChanceFar, distance / shootRange);
        chance -= PoliceTiers.TargetSpeed(target) * 0.03f;
        chance = Mathf.Clamp(chance, 0.03f, 0.9f);

        // Hit or miss is rolled first; a hit lands unless a building is in the way (see PoliceAim)
        Vector3 end = PoliceAim.Fire(transform, from, target, chance, shootRange, tier.damage, damageToCar);

        if (rifleShot != null)
        {
            // G1: a rifle's crack, and the flash at the muzzle
            audioSource.pitch = Random.Range(0.96f, 1.04f);
            audioSource.PlayOneShot(rifleShot, 0.75f);
            if (rifleMuzzle != null) LundunCombatFX.Muzzle(rifleMuzzle, 0.8f, 1);
        }
        else if (gunshot != null)
        {
            audioSource.pitch = Random.Range(1.05f, 1.2f);     // a bit higher: pistol, not rifle
            audioSource.PlayOneShot(gunshot, 0.7f);
        }
        if (tracer != null) StartCoroutine(ShowTracer(from, end, Color.white, 0.04f));
    }

    IEnumerator ShowTracer(Vector3 from, Vector3 to, Color colour, float time)
    {
        tracer.startColor = tracer.endColor = colour;
        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;
        yield return new WaitForSeconds(time);
        if (tracer != null) tracer.enabled = false;
    }

    /// W1: limited range and field of view; very close, just seen you, or just heard a shot = they notice anyway.
    bool CanSee(Transform target, PoliceTiers.Tier tier)
    {
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Vector3 point = target.position + Vector3.up * 1f;
        bool tracking = Time.time - lastSeenTime < PoliceTiers.KeepTrackFor || Time.time < alertedUntil;
        return PoliceTiers.Sees(eye, transform.forward, point, tier.sightRange, tier.sightCone, tracking, sightBlockers);
    }

    void Repath(Vector3 to, float interval = 0.25f)
    {
        if (Time.time < nextRepath || !agent.isOnNavMesh) return;
        nextRepath = Time.time + interval;
        // L2: a spot off the walkable ground used to leave him with no path at all (standing still): go as near as he can
        if (!agent.SetDestination(to)) agent.SetDestination(Reachable(to));
    }

    void Stop()
    {
        if (agent.isOnNavMesh) agent.isStopped = true;
    }

    void FaceTowards(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            Quaternion.LookRotation(dir), 540f * Time.deltaTime);
    }

    float FlatDistance(Vector3 point)
    {
        Vector3 d = point - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    // ---------- getting hurt ----------

    void OnDamaged(Vector3 hitPoint)
    {
        if (health.IsDead) return;                       // the killing blow is reported in OnDied
        CrimeSystem.Report(CrimeType.AssaultOfficer, hitPoint);
        // you hurt me: I know exactly where you are, wherever you shot from
        lastSeenTime = Time.time;
        alertedUntil = Time.time + 3f;
        WantedSystem w = WantedSystem.Instance;
        if (w != null && w.Stars > 0)
        {
            w.ReportSighting(w.PlayerTarget.position);
            if (state == State.Patrol) Respond();
        }
    }

    void OnDied()
    {
        // G2: armed officers drop their carbine (with some ammo) for James to pick up
        // G1 (rifles): it leaves his hands: the one on the ground is the one he was carrying
        if (Armed)
        {
            Vector3 at = rifle != null ? rifle.transform.position : transform.position;
            if (rifle != null) { Destroy(rifle); rifle = null; rifleMuzzle = null; }
            LundunPickup.Spawn(LundunWeapons.Kind.Carbine, 60, at);
        }
        state = State.Dead;
        CrimeSystem.Report(CrimeType.KillOfficer, transform.position);
        Active.Remove(this);
        if (myCar != null) myCar.OfficerLost(this);

        agent.isStopped = true;
        agent.enabled = false;

        // Same as pedestrians: shot = drop where you stand, hit by a car = keep colliders
        if (!health.DiedFromPhysics)
            foreach (Collider col in GetComponentsInChildren<Collider>())
                col.enabled = false;

        if (animator != null) LundunDeaths.Play(animator, health, transform);      // G2: was always "Die"
    }
}
