using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class PoliceOfficer : MonoBehaviour
{
    private enum State { Chase, Search, ReturnToCar, Dead }

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

    public void Init(PoliceAI car) => myCar = car;

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
    }

    void OnEnable()
    {
        Active.Add(this);
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    void OnDisable()
    {
        Active.Remove(this);
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
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
        bool canSee = wanted.Stars > 0 && CanSee(target);

        if (canSee)
        {
            lastSeenTime = Time.time;
            wanted.ReportSighting(target.position);
        }

        // ---------- decide what to do ----------
        bool suspectDroveOff = target.GetComponent<CarController>() != null
                            && FlatDistance(target.position) > giveUpCarDistance;

        if (wanted.Stars == 0 || suspectDroveOff || Time.time - lastSeenTime > giveUpAfter)
            state = State.ReturnToCar;
        else if (canSee)
            state = State.Chase;
        else if (state == State.Chase)
            state = State.Search;

        // ---------- do it ----------
        switch (state)
        {
            case State.Chase:      Chase(target, wanted.Stars); break;
            case State.Search:     Search(wanted.LastKnownPosition); break;
            case State.ReturnToCar: ReturnToCar(); break;
        }
    }

    void Chase(Transform target, int stars)
    {
        float distance = FlatDistance(target.position);
        bool shooting = stars >= starsToShoot && distance > closeRange && distance < shootRange
                     && PlayerHealth.Instance != null && !PlayerHealth.Instance.IsDead;

        if (shooting)
        {
            // Stand, face you, fire
            Stop();
            FaceTowards(target.position);
            if (Time.time >= nextShot)
            {
                nextShot = Time.time + timeBetweenShots * Random.Range(0.8f, 1.3f);
                FireAt(target);
            }
            return;
        }

        // Run you down for the arrest
        agent.isStopped = false;
        agent.speed = runSpeed;
        agent.stoppingDistance = 1.2f;
        Repath(target.position);
        if (distance < 2f) FaceTowards(target.position);
    }

    void Search(Vector3 lastKnown)
    {
        agent.isStopped = false;
        agent.speed = runSpeed * 0.7f;
        agent.stoppingDistance = 1f;
        Repath(lastKnown);
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

        agent.speed = WantedSystem.Instance.Stars > 0 ? runSpeed : walkSpeed;
        Repath(myCar.transform.position);
        if (FlatDistance(myCar.transform.position) < 2.8f) myCar.Board(this);
    }

    void FireAt(Transform target)
    {
        Vector3 from = transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;
        Vector3 aim = target.position + Vector3.up * 1.1f;
        float distance = Vector3.Distance(from, aim);

        float chance = Mathf.Lerp(closeHitChance, farHitChance, distance / shootRange);
        chance = Mathf.Clamp(chance, 0.05f, 0.9f);

        // Misses go past you, so you see the tracer
        if (Random.value > chance)
        {
            Vector3 side = Vector3.Cross(Vector3.up, (aim - from).normalized);
            aim += side * Random.Range(1f, 2f) * (Random.value < 0.5f ? -1f : 1f)
                 + Vector3.up * Random.Range(-0.4f, 0.8f);
        }

        Vector3 dir = (aim - from).normalized;
        Vector3 end = from + dir * shootRange;

        if (Physics.Raycast(from, dir, out RaycastHit hit, shootRange * 1.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            if (hit.transform == target || hit.transform.IsChildOf(target))
            {
                VehicleHealth playerCar = target.GetComponent<VehicleHealth>();
                if (playerCar != null) playerCar.TakeDamage(damageToCar);
                else PlayerHealth.Instance.TakeDamage(damageOnFoot);
            }
        }

        if (gunshot != null)
        {
            audioSource.pitch = Random.Range(1.05f, 1.2f);     // a bit higher: pistol, not rifle
            audioSource.PlayOneShot(gunshot, 0.7f);
        }
        if (tracer != null) StartCoroutine(ShowTracer(from, end));
    }

    IEnumerator ShowTracer(Vector3 from, Vector3 to)
    {
        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;
        yield return new WaitForSeconds(0.04f);
        if (tracer != null) tracer.enabled = false;
    }

    bool CanSee(Transform target)
    {
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Vector3 point = target.position + Vector3.up * 1f;
        if ((point - eye).sqrMagnitude > sightRange * sightRange) return false;
        return !Physics.Linecast(eye, point, sightBlockers, QueryTriggerInteraction.Ignore);
    }

    void Repath(Vector3 to)
    {
        if (Time.time < nextRepath || !agent.isOnNavMesh) return;
        nextRepath = Time.time + 0.25f;
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
        lastSeenTime = Time.time;                        // you shot me: I know where you are
    }

    void OnDied()
    {
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
