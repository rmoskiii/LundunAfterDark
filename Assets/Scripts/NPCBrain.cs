using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Health))]
public class NPCBrain : MonoBehaviour
{
    private enum State { Wander, Flee, Dead }

    [Header("Wandering")]
    [SerializeField] private float walkSpeed = 1.5f;
    [SerializeField] private float wanderRadius = 15f;
    [SerializeField] private Vector2 idleTimeRange = new Vector2(1f, 4f);

    [Header("Panic")]
    [SerializeField] private float runSpeed = 5.5f;
    [SerializeField] private float hearingRange = 25f;
    [SerializeField] private float fleeDistance = 20f;
    [SerializeField] private float panicDuration = 8f;

    private NavMeshAgent agent;
    private Animator animator;
    private Health health;

    private State state = State.Wander;
    private float idleTimer;
    private float panicTimer;
    private Vector3 threatPosition;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        health = GetComponent<Health>();
        idleTimer = Random.Range(idleTimeRange.x, idleTimeRange.y);
    }

    // Start listening when switched on, stop when switched off (like initState / dispose)
    void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        PlayerCombat.GunshotFired += OnGunshot;
    }

    void OnDisable()
    {
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
        PlayerCombat.GunshotFired -= OnGunshot;
    }

    void Update()
    {
        if (state == State.Dead) return;

        if (state == State.Wander) Wander();
        else Flee();

        if (animator != null)
        {
            animator.SetFloat("Speed", agent.velocity.magnitude, 0.1f, Time.deltaTime);
        }
    }

    void Wander()
    {
        agent.speed = walkSpeed;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f)
        {
            idleTimer -= Time.deltaTime;
            if (idleTimer <= 0f)
            {
                agent.SetDestination(RandomPointNear(transform.position, wanderRadius));
                idleTimer = Random.Range(idleTimeRange.x, idleTimeRange.y);
            }
        }
    }

    void Flee()
    {
        panicTimer -= Time.deltaTime;

        if (panicTimer <= 0f)
        {
            state = State.Wander;
            return;
        }

        // Reached the escape point but still scared? Keep running.
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
        {
            RunFrom(threatPosition);
        }
    }

    void StartPanic(Vector3 from)
    {
        threatPosition = from;
        state = State.Flee;
        panicTimer = panicDuration;
        agent.speed = runSpeed;
        RunFrom(from);
    }

    void RunFrom(Vector3 from)
    {
        Vector3 away = transform.position - from;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
        away.y = 0f;

        Vector3 target = transform.position + away.normalized * fleeDistance;
        agent.SetDestination(RandomPointNear(target, 5f));
    }

    Vector3 RandomPointNear(Vector3 centre, float radius)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 candidate = centre + Random.insideUnitSphere * radius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }
        return transform.position;
    }

    void OnGunshot(Vector3 shooterPosition)
    {
        if (state == State.Dead) return;

        if (Vector3.Distance(shooterPosition, transform.position) <= hearingRange)
        {
            StartPanic(shooterPosition);
        }
    }

    void OnDamaged(Vector3 hitPoint)
    {
        if (state == State.Dead) return;
        StartPanic(threatPosition);
    }

    void OnDied()
    {
        state = State.Dead;

        agent.isStopped = true;
        agent.enabled = false;

        // Let bullets pass through the body from now on
        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        if (animator != null) animator.SetTrigger("Die");
    }
}