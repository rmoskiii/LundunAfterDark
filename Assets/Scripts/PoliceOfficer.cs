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

    public void Init(PoliceAI car)
    {
        myCar = car;
        Armed = car != null && car.Armed;
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
        LundunAnim.Bool(animator, "Aim", false);
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

        if (!agent.isOnNavMesh) return;

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

        if (state != State.Chase) taserAt = 0f;

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
            Taser(target);
            return;
        }

        bool shooting = Armed && stars >= starsToShoot && distance > closeRange && distance < shootRange && playerAlive;

        LundunAnim.Bool(animator, "Aim", shooting);
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
            return;
        }

        // Run you down for the arrest
        agent.isStopped = false;
        agent.speed = tier.runSpeed;
        agent.stoppingDistance = 1.2f;
        Repath(target.position, tier.repath);
        if (distance < 2f) FaceTowards(target.position);
    }

    void Search(Vector3 lastKnown, PoliceTiers.Tier tier)
    {
        agent.isStopped = false;
        agent.speed = tier.runSpeed * 0.7f;
        agent.stoppingDistance = 1f;
        Repath(lastKnown, tier.repath);
    }

    // ------------------------------------------------------------------ Taser (W1)
    bool TaserReady(Transform target, PoliceTiers.Tier tier, float distance)
    {
        PoliceTiers p = PoliceTiers.Instance;
        if (p == null || !tier.taser || Time.time < nextTaser) return false;
        if (target.GetComponent<CarController>() != null) return false;                  // not through a car
        PlayerController pc = target.GetComponent<PlayerController>();
        if (pc == null || !pc.CanBeTased) return false;
        return distance >= p.taserMin && distance <= p.taserMax;
    }

    /// Stop, aim, fire the Taser: a hit stuns James for a moment (plenty of time for an arrest).
    void Taser(Transform target)
    {
        PoliceTiers p = PoliceTiers.Instance;
        Stop();
        FaceTowards(target.position);
        LundunAnim.Bool(animator, "Aim", true);
        if (taserAt <= 0f) { taserAt = Time.time + p.taserWindup; return; }
        if (Time.time < taserAt) return;

        taserAt = 0f;
        nextTaser = Time.time + p.taserCooldown;
        LundunAnim.Bool(animator, "Aim", false);
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
    }

    void ReturnToCar()
    {
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
        Vector3 aim = PoliceAim.Centre(target);
        float distance = Vector3.Distance(from, aim);

        // W1: accuracy and damage come from the wanted level; a moving target is harder to hit
        float chance = Mathf.Lerp(tier.hitChanceNear, tier.hitChanceFar, distance / shootRange);
        chance -= PoliceTiers.TargetSpeed(target) * 0.03f;
        chance = Mathf.Clamp(chance, 0.03f, 0.9f);

        // Hit or miss is rolled first; a hit lands unless a building is in the way (see PoliceAim)
        Vector3 end = PoliceAim.Fire(transform, from, target, chance, shootRange, tier.damage, damageToCar);

        if (gunshot != null)
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
        agent.SetDestination(to);
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
        if (Armed) LundunPickup.Spawn(LundunWeapons.Kind.Carbine, 60, transform.position);
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

        if (animator != null) animator.SetTrigger("Die");
    }
}
