using UnityEngine;
using UnityEngine.AI;

/// Added to a pedestrian for a short time: a carjacked or rammed driver having a go at James.
/// Fighter: runs at you and punches (and kicks your car if you're sat in it).
/// Gunman: keeps 6–14 m away and shoots (same hit rules as the police).
/// Calms down after ~20 s, if you get 40 m away, or if they die — then they're a normal pedestrian again.
/// Fighters throw two jabs then a big one that rocks James back. Gunmen face you the whole time and back off
/// with the pistol walk if you get too close. Shove one mid-fight and they stumble before carrying on.
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
    private GameObject gunProp;
    private int swings;
    private float recoil;
    private Quaternion gunRest;

    /// Pistol model held by armed drivers (set by Lundun → 12  Build People).
    public static GameObject GunModel;

    const float RunSpeed = 6f, PunchRange = 1.7f, PunchDamage = 7f, BigPunchDamage = 12f, CarKickDamage = 3f;
    const float ShootMin = 6f, ShootMax = 14f, ShotDamage = 6f, ShotToCar = 3f;

    private float giveUpBeyond = 40f, damageScale = 1f;

    /// S4: a mission's enemy: it may go for the people with James as well as James himself.
    public bool Foe { get; set; }
    /// S4: who a Foe goes for instead of James (null = James). Set by the crew (Story only).
    public static System.Func<LundunAngryDriver, Transform> PickTarget;
    private Transform ally;
    private float nextPick;
    public Transform Going { get { return ally; } }

    public void Begin(bool withGun) { Begin(withGun, 20f, 40f, 1f); }

    /// S1: a mission's enemy: keeps at it for 'seconds', gives up only beyond 'range' metres, hits 'damage' × as hard.
    public void Begin(bool withGun, float seconds, float range, float damage)
    {
        giveUpBeyond = range; damageScale = damage;
        armed = withGun;
        until = Time.time + seconds;
        nextHit = Time.time + (armed ? 1.2f : 0.6f);
        agent = GetComponent<NavMeshAgent>();
        brain = GetComponent<NPCBrain>();
        health = GetComponent<Health>();
        animator = GetComponentInChildren<Animator>();
        vehicles = FindAnyObjectByType<VehicleSystem>();
        if (brain != null) brain.enabled = false;          // we're in charge for now
        if (armed) { GiveGun(); LundunAnim.Bool(animator, "Aim", true); if (agent != null) agent.updateRotation = false; }
        if (agent != null) { agent.speed = RunSpeed; agent.isStopped = false; }
    }

    void OnDisable() { CalmDown(); }       // put away by the population manager

    void Update()
    {
        if (vehicles == null || agent == null || !agent.isOnNavMesh) { CalmDown(); return; }
        if ((health != null && health.IsDead) || Time.time > until) { CalmDown(); return; }
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead) { CalmDown(); return; }

        Transform target = vehicles.PlayerTarget;
        if (Foe && PickTarget != null && Time.time >= nextPick) { nextPick = Time.time + 1.5f; ally = PickTarget(this); }
        if (ally != null && !ally.gameObject.activeInHierarchy) ally = null;
        if (ally != null) target = ally;                                    // S4: someone with James
        Vector3 to = target.position - transform.position; to.y = 0f;
        float d = to.magnitude;
        if (d > giveUpBeyond) { CalmDown(); return; }
        if (LundunStagger.Busy(gameObject)) return;       // shoved / hit: wait till they're back on their feet

        if (armed)
        {
            // keep a shooting distance and fire
            // keep facing James: walk at him, stand, or back away (the pistol walk is a backwards walk)
            agent.updateRotation = false;
            if (d > ShootMax) { agent.isStopped = false; agent.SetDestination(target.position); }
            else if (d < ShootMin) { agent.isStopped = false; agent.speed = 1.6f; agent.SetDestination(transform.position - to.normalized * 4f); }
            else { agent.isStopped = true; }
            if (d >= ShootMin) agent.speed = RunSpeed;
            Face(to);
            Vector3 v = transform.InverseTransformDirection(agent.velocity);
            LundunAnim.Float(animator, "AimMove", v.z, 0.1f);
            if (d <= ShootMax && Time.time >= nextHit)
            {
                nextHit = Time.time + Random.Range(0.8f, 1.3f);
                Vector3 from = transform.position + Vector3.up * 1.4f + transform.forward * 0.4f;
                float chance = Mathf.Lerp(0.6f, 0.3f, d / ShootMax);
                Vector3 end = ally != null ? ShootAlly(from, chance) : PoliceAim.Fire(transform, from, target, chance, ShootMax * 1.5f, ShotDamage * damageScale, ShotToCar);
                ShowTracer(gunProp != null ? gunProp.transform.position : from, end);
                recoil = 1f;                               // kick the gun up (no shooting clip needed)
            }
        }
        else
        {
            agent.isStopped = false;
            agent.SetDestination(target.position);
            float reach = vehicles.IsDriving && ally == null ? 3f : PunchRange;
            if (d <= reach && Time.time >= nextHit)
            {
                nextHit = Time.time + 1.1f;
                Face(to);
                bool big = ++swings % 3 == 0;               // two jabs, then a big one
                LundunAnim.Trigger(animator, "Punch");
                if (ally != null)                                           // S4: hitting someone with James
                {
                    Health ah = ally.GetComponent<Health>();
                    if (ah != null) ah.TakeDamage((big ? BigPunchDamage : PunchDamage) * damageScale, ally.position + Vector3.up * 1.4f, to.normalized * 2f);
                }
                else if (vehicles.IsDriving)
                {
                    VehicleHealth vh = target.GetComponent<VehicleHealth>();
                    if (vh != null) vh.TakeDamage(CarKickDamage * damageScale);
                }
                else if (PlayerHealth.Instance != null)
                {
                    PlayerHealth.Instance.TakeDamage((big ? BigPunchDamage : PunchDamage) * damageScale);
                    if (big) LundunPlayerReact.Punched();    // James reels from it
                }
            }
            // you drove off fast: give up
            if (ally == null && vehicles.IsDriving && vehicles.PlayerSpeed > 8f && d > 12f && giveUpBeyond <= 40f) { CalmDown(); return; }
        }

        if (animator != null) animator.SetFloat("Speed", agent.velocity.magnitude, 0.1f, Time.deltaTime);
        if (tracer != null && tracer.enabled && Time.time > tracerOff) tracer.enabled = false;
        if (gunProp != null)
        {
            recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 8f);
            gunProp.transform.localRotation = gunRest * Quaternion.Euler(-25f * recoil, 0f, 0f);
        }
    }

    /// S4: a shot at someone with James: hits now and then, otherwise flies past.
    Vector3 ShootAlly(Vector3 from, float chance)
    {
        Vector3 aim = ally.position + Vector3.up * 1.3f;
        if (Random.value < chance && !Physics.Linecast(from, aim, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
        {
            Health ah = ally.GetComponent<Health>();
            if (ah != null) ah.TakeDamage(ShotDamage * 1.6f * damageScale, aim, (aim - from).normalized * 2f, true);
            return aim;
        }
        Vector3 side = Vector3.Cross(Vector3.up, (aim - from).normalized);
        return aim + side * Random.Range(1f, 2f) * (Random.value < 0.5f ? -1f : 1f);
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

    void GiveGun()
    {
        if (GunModel == null || animator == null || !animator.isHuman) return;
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        gunProp = Instantiate(GunModel, hand);
        gunProp.name = "DriverGun";
        // the gun stays its real size even on a person whose prefab is scaled (Lundun → Fix People Sizes)
        // (undoes only the person's own scale, so everyone at normal size is exactly as before)
        float s = transform.localScale.y;
        if (s < 0.0001f) s = 1f;
        gunProp.transform.localScale = GunModel.transform.localScale / s;
        gunProp.transform.localPosition = new Vector3(0.05f, 0.08f, 0.02f) / s;
        gunProp.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);
        gunRest = gunProp.transform.localRotation;
        foreach (Collider c in gunProp.GetComponentsInChildren<Collider>()) Destroy(c);
    }

    void CalmDown()
    {
        if (gunProp != null) Destroy(gunProp);
        LundunAnim.Bool(animator, "Aim", false);
        LundunAnim.Float(animator, "AimMove", 0f);
        if (tracer != null) Destroy(tracer.gameObject);
        if (agent != null) { agent.updateRotation = true; if (agent.isOnNavMesh) agent.isStopped = false; }
        if (brain != null && (health == null || !health.IsDead) && !LundunStagger.Busy(gameObject)) brain.enabled = true;
        Destroy(this);
    }
}
