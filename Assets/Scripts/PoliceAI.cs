using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CarController))]
public class PoliceAI : MonoBehaviour
{
    private enum State { Responding, Pursuing, Searching, Returning }

    [Header("Senses")]
    [SerializeField] private float sightRange = 45f;
    [SerializeField] private LayerMask sightBlockers = 1;      // Default layer = buildings and props

    [Header("Driving")]
    [SerializeField] private float repathInterval = 0.4f;
    [SerializeField] private float arriveDistance = 7f;
    [SerializeField] private float searchRadius = 25f;
    [SerializeField] private float steerSharpness = 35f;      // lower = twitchier steering

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
    private Renderer bodyRenderer;

    void Start()
    {
        car = GetComponent<CarController>();
        car.IsDriven = false;                    // the player isn't driving this one
        path = new NavMeshPath();
        bodyRenderer = GetComponentInChildren<Renderer>();
        destination = WantedSystem.Instance.LastKnownPosition;
    }

    void Update()
    {
        WantedSystem wanted = WantedSystem.Instance;

        if (wanted.Stars == 0 && state != State.Returning)
        {
            state = State.Returning;
            destination = transform.position + transform.forward * 80f;
            if (siren != null) siren.Stop();
        }

        bool canSee = state != State.Returning && CanSeePlayer(wanted);
        if (canSee)
        {
            wanted.ReportSighting(wanted.PlayerTarget.position);
            state = State.Pursuing;
        }

        switch (state)
        {
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

    bool CanSeePlayer(WantedSystem wanted)
    {
        Transform target = wanted.PlayerTarget;
        if (target == null) return false;

        Vector3 eye = transform.position + Vector3.up * 1.5f;
        Vector3 targetPoint = target.position + Vector3.up * 1f;
        if ((targetPoint - eye).sqrMagnitude > sightRange * sightRange) return false;

        // Anything on the blocker layers in between (a building) breaks line of sight
        return !Physics.Linecast(eye, targetPoint, sightBlockers, QueryTriggerInteraction.Ignore);
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

        float throttle = Mathf.Abs(angle) > 70f ? 0.4f : 1f;           // slow down for sharp turns
        if (state == State.Pursuing && FlatDistance(destination) < 8f) throttle = 0.35f;
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