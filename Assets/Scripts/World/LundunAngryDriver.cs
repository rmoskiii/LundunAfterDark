using UnityEngine;
using UnityEngine.AI;

/// Added to a pedestrian for a short time: a carjacked or rammed driver having a go at James.
/// Fighter: runs at you and punches (and kicks your car if you're sat in it).
/// Gunman: keeps 6–14 m away and shoots (same hit rules as the police).
/// Calms down after ~20 s, if you get 40 m away, or if they die — then they're a normal pedestrian again.
/// (Proper punch / shoot animations arrive with the new characters in R4.)
public class LundunAngryDriver : MonoBehaviour
{
    private bool armed;
    private float until, nextHit;
    private NavMeshAgent agent;
    private NPCBrain brain;
    private Health health;
    private Animator animator;
    private VehicleSystem vehicles;
    private LineRenderer tracer;
    private float tracerOff;

    const float RunSpeed = 6f, PunchRange = 1.7f, PunchDamage = 7f, CarKickDamage = 3f;
    const float ShootMin = 6f, ShootMax = 14f, ShotDamage = 6f, ShotToCar = 3f;

    public void Begin(bool withGun)
    {
        armed = withGun;
        until = Time.time + 20f;
        nextHit = Time.time + (armed ? 1.2f : 0.6f);
        agent = GetComponent<NavMeshAgent>();
        brain = GetComponent<NPCBrain>();
        health = GetComponent<Health>();
        animator = GetComponentInChildren<Animator>();
        vehicles = FindAnyObjectByType<VehicleSystem>();
        if (brain != null) brain.enabled = false;          // we're in charge for now
        if (agent != null) { agent.speed = RunSpeed; agent.isStopped = false; }
    }

    void OnDisable() { CalmDown(); }       // put away by the population manager

    void Update()
    {
        if (vehicles == null || agent == null || !agent.isOnNavMesh) { CalmDown(); return; }
        if ((health != null && health.IsDead) || Time.time > until) { CalmDown(); return; }
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead) { CalmDown(); return; }

        Transform target = vehicles.PlayerTarget;
        Vector3 to = target.position - transform.position; to.y = 0f;
        float d = to.magnitude;
        if (d > 40f) { CalmDown(); return; }

        if (armed)
        {
            // keep a shooting distance and fire
            if (d > ShootMax) { agent.isStopped = false; agent.SetDestination(target.position); }
            else if (d < ShootMin) { agent.isStopped = false; agent.SetDestination(transform.position - to.normalized * 4f); }
            else { agent.isStopped = true; Face(to); }
            if (d <= ShootMax && Time.time >= nextHit)
            {
                nextHit = Time.time + Random.Range(0.8f, 1.3f);
                Vector3 from = transform.position + Vector3.up * 1.4f + transform.forward * 0.4f;
                float chance = Mathf.Lerp(0.6f, 0.3f, d / ShootMax);
                Vector3 end = PoliceAim.Fire(transform, from, target, chance, ShootMax * 1.5f, ShotDamage, ShotToCar);
                ShowTracer(from, end);
            }
        }
        else
        {
            agent.isStopped = false;
            agent.SetDestination(target.position);
            float reach = vehicles.IsDriving ? 3f : PunchRange;
            if (d <= reach && Time.time >= nextHit)
            {
                nextHit = Time.time + 1.1f;
                Face(to);
                if (vehicles.IsDriving)
                {
                    VehicleHealth vh = target.GetComponent<VehicleHealth>();
                    if (vh != null) vh.TakeDamage(CarKickDamage);
                }
                else if (PlayerHealth.Instance != null) PlayerHealth.Instance.TakeDamage(PunchDamage);
            }
            // you drove off fast: give up
            if (vehicles.IsDriving && vehicles.PlayerSpeed > 8f && d > 12f) { CalmDown(); return; }
        }

        if (animator != null) animator.SetFloat("Speed", agent.velocity.magnitude, 0.1f, Time.deltaTime);
        if (tracer != null && tracer.enabled && Time.time > tracerOff) tracer.enabled = false;
    }

    void Face(Vector3 to)
    {
        if (to.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 10f * Time.deltaTime);
    }

    void ShowTracer(Vector3 from, Vector3 to)
    {
        if (tracer == null)
        {
            GameObject g = new GameObject("DriverTracer");
            g.transform.SetParent(transform, false);
            tracer = g.AddComponent<LineRenderer>();
            tracer.positionCount = 2;
            tracer.startWidth = 0.03f; tracer.endWidth = 0.015f;
            tracer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            tracer.material.SetColor("_BaseColor", new Color(1f, 0.85f, 0.5f));
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        tracer.SetPosition(0, from); tracer.SetPosition(1, to);
        tracer.enabled = true;
        tracerOff = Time.time + 0.05f;
    }

    void CalmDown()
    {
        if (tracer != null) Destroy(tracer.gameObject);
        if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
        if (brain != null && (health == null || !health.IsDead)) brain.enabled = true;
        Destroy(this);
    }
}
