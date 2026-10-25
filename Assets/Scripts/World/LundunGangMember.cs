using UnityEngine;
using UnityEngine.AI;

/// R4a: one gang member (LundunGangs puts them on their corner). Their ordinary pedestrian brain is switched off while
/// they're in the gang; this does the thinking:
///   hanging about   stood in a loose ring on the corner, shifting about, turning to each other
///   watching James  he comes within 14 m on foot: they turn and watch; closer, one of them says something
///   squaring up     he stops right by them, or bumps one: a warning, then if he's still there it kicks off
///   fighting        he hits or shoots one (or stays in their face): the whole group goes for him. Knives and
///                   machetes (drawn when it kicks off) cut hard, the rest punch; after midnight one in four groups
///                   has someone with a gun, who keeps his distance and shoots. Machete hits knock James back.
///   trouble         now and then a couple of them pick on a passer-by: a shove and a punch, and the victim runs
///   scattering      police near, or half of them down: they run for it (and are tidied away out of sight)
///   R4b: marching   on their way to a rival gang's corner (walking, then running the last stretch)
///   R4b: brawling   fighting the rival group: each goes for the nearest of the other lot; blades and fists hurt,
///                   people die. Hurt by James instead and their gang holds a grudge (LundunGangs.Hurt)
public class LundunGangMember : MonoBehaviour
{
    public enum Weapon { None, Knife, Machete, Gun }
    enum State { Hang, PickOn, Fight, Scatter, March, Brawl }

    public bool IsDead { get { return health == null || health.IsDead; } }
    public Weapon Carries { get; private set; }

    const float WalkSpeed = 1.4f, RunSpeed = 5.8f;
    const float ShootMin = 6f, ShootMax = 14f, ShotDamage = 6f, ShotToCar = 3f;

    private LundunGangs gangs;
    private LundunGangs.Group group;
    private State state;
    private NavMeshAgent agent;
    private Animator animator;
    private Health health;
    private NPCBrain brain;
    private VehicleSystem vehicles;
    private PlayerController playerMove;
    private Vector3 home;
    private float nextShuffle, closeFor, nextHit, windupUntil, nextPath, pickOnUntil;
    private int swings;
    private bool swinging;
    private GameObject prop;
    private GameObject victim;
    private LundunGangs.Group foes;              // R4b: the rival group in a brawl
    private LundunGangMember foe;
    private float nextFoe;
    /// R4b: when a rival last hit this one (so the hit isn't blamed on James).
    public float RivalHitAt { get; set; } = -99f;

    public void Begin(LundunGangs owner, LundunGangs.Group g, Weapon carries, Vector3 standAt)
    {
        gangs = owner; group = g; Carries = carries; home = standAt;
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        health = GetComponent<Health>();
        brain = GetComponent<NPCBrain>();
        vehicles = FindAnyObjectByType<VehicleSystem>();
        if (brain != null) brain.enabled = false;              // the gang does their thinking now
        if (health != null) health.Damaged += OnHurt;
        PlayerCombat.GunshotFired += OnGunshot;
        state = State.Hang;
        nextShuffle = Time.time + Random.Range(4f, 9f);
        if (agent != null) { agent.speed = WalkSpeed; agent.stoppingDistance = 0.3f; }
    }

    void OnDestroy()
    {
        if (health != null) health.Damaged -= OnHurt;
        PlayerCombat.GunshotFired -= OnGunshot;
    }

    void OnHurt(Vector3 at)
    {
        if (group == null) return;
        bool byRival = Time.time - RivalHitAt < 0.25f;
        if (!byRival) gangs.Hurt(group, false);                    // hit one and the whole group comes for you (and the gang remembers)
    }

    void OnGunshot(Vector3 from)
    {
        if (IsDead || group == null || state == State.Scatter || state == State.Brawl) return;
        float d = Flat(from, transform.position);
        if (d > 30f) return;
        // someone with a gun or a blade close by stands and fights; otherwise the group runs for it
        bool armed = false;
        foreach (LundunGangMember m in group.members) if (m != null && !m.IsDead && m.Carries != Weapon.None) armed = true;
        if (armed && d < 18f) gangs.StartFight(group); else gangs.Scatter(group, from);
    }

    // ------------------------------------------------------------------ orders from the group
    public void Fight()
    {
        if (IsDead) return;
        state = State.Fight;
        victim = null;
        Draw();
        if (agent != null) { agent.speed = RunSpeed; agent.stoppingDistance = 0.8f; agent.isStopped = false; }
        nextHit = Time.time + Random.Range(0.3f, 0.9f);
    }

    public void BackToCorner()
    {
        if (IsDead) return;
        state = State.Hang;
        Holster();
        if (agent != null) { agent.speed = WalkSpeed; agent.stoppingDistance = 0.3f; agent.isStopped = false; if (agent.isOnNavMesh) agent.SetDestination(home); }
        closeFor = 0f;
    }

    public void Scatter(Vector3 from)
    {
        if (IsDead) return;
        state = State.Scatter;
        Holster();
        if (agent == null || !agent.isOnNavMesh) return;
        agent.speed = RunSpeed; agent.stoppingDistance = 0.5f; agent.isStopped = false;
        Vector3 away = transform.position - from; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = transform.forward;
        Vector3 to = transform.position + Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized * Random.Range(35f, 50f);
        if (NavMesh.SamplePosition(to, out NavMeshHit hit, 8f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
    }

    /// R4b: walking (then running) to a rival gang's corner.
    public void March(Vector3 to, bool run)
    {
        if (IsDead || state == State.Fight || state == State.Brawl || state == State.Scatter) return;
        state = State.March;
        if (agent == null || !agent.isOnNavMesh) return;
        agent.speed = run ? RunSpeed * 0.8f : 1.7f; agent.stoppingDistance = 1f; agent.isStopped = false;
        if (Time.time >= nextPath) { nextPath = Time.time + 0.5f; agent.SetDestination(to + Random.insideUnitSphere * 2f); }
        if (run) Draw();
    }

    /// R4b: fight the rival group.
    public void Brawl(LundunGangs.Group rivals)
    {
        if (IsDead || state == State.Fight) return;                 // already going for James
        foes = rivals; foe = null; nextFoe = 0f;
        state = State.Brawl;
        Draw();
        if (agent != null) { agent.speed = RunSpeed; agent.stoppingDistance = 0.8f; agent.isStopped = false; }
        nextHit = Time.time + Random.Range(0.2f, 0.8f);
    }

    /// A couple of them go for a passer-by. False if this one can't (busy, armed with a gun, already at it).
    public bool PickOn(GameObject who)
    {
        if (IsDead || state != State.Hang || who == null || Carries == Weapon.Gun) return false;
        victim = who; state = State.PickOn; pickOnUntil = Time.time + 12f;
        if (agent != null) { agent.speed = 3.2f; agent.stoppingDistance = 1.1f; agent.isStopped = false; }
        return true;
    }

    // ------------------------------------------------------------------ every frame
    void Update()
    {
        if (IsDead || agent == null) return;
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit back, 4f, NavMesh.AllAreas)) agent.Warp(back.position);
            return;
        }
        if (LundunStagger.Busy(gameObject)) return;                      // knocked about: wait till they're steady
        if (animator != null) animator.SetFloat("Speed", agent.velocity.magnitude, 0.1f, Time.deltaTime);
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        Transform james = vehicles != null ? vehicles.PlayerTarget : null;
        switch (state)
        {
            case State.Hang: Hang(james); break;
            case State.PickOn: Bully(); break;
            case State.Fight: FightJames(james); break;
            case State.Scatter: break;
            case State.March: break;                                         // steered by the group (LundunGangs)
            case State.Brawl: BrawlRivals(); break;
        }
    }

    void Hang(Transform james)
    {
        // back to their place in the ring, and now and then a shuffle to a new one
        if (Time.time >= nextShuffle)
        {
            nextShuffle = Time.time + Random.Range(5f, 10f);
            Vector3 c = group.spot, off = home - c; off.y = 0f;
            off = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * off;
            Vector3 p = c + off.normalized * Mathf.Clamp(off.magnitude + Random.Range(-0.3f, 0.3f), 1.2f, 2.4f);
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) home = hit.position;
        }
        if (Flat(home, transform.position) > 0.5f) { agent.isStopped = false; if (Time.time >= nextPath) { nextPath = Time.time + 0.5f; agent.SetDestination(home); } }
        else agent.isStopped = true;

        bool onFoot = PlayerHealth.Instance != null && PlayerHealth.Instance.OnFoot && !PlayerHealth.Instance.IsDead;
        float d = james != null ? Flat(james.position, transform.position) : 999f;
        if (onFoot && d < 14f)
        {
            Face(james.position - transform.position, 6f);                   // they watch him
            if (d < 7f) gangs.Look(group);
            // stood right by them (or bumped one): a warning, then it kicks off
            if (d < 3.5f) closeFor += Time.deltaTime; else closeFor = Mathf.Max(0f, closeFor - Time.deltaTime);
            if (playerMove == null && PlayerHealth.Instance != null) playerMove = PlayerHealth.Instance.Body.GetComponent<PlayerController>();
            if (d < 1.05f && playerMove != null && playerMove.MoveIntent.sqrMagnitude > 1f) closeFor += 2.6f;     // a bump
            if (closeFor > 2.5f) gangs.Warn(group);
            if (closeFor > 5.5f) gangs.StartFight(group);
        }
        else
        {
            closeFor = 0f;
            if (agent.isStopped) Face(group.spot - transform.position, 3f);   // turned in to each other
        }
    }

    void Bully()
    {
        if (victim == null || !victim.activeInHierarchy || Time.time > pickOnUntil) { BackToCorner(); return; }
        Health vh = victim.GetComponent<Health>();
        if (vh != null && vh.IsDead) { BackToCorner(); return; }
        float d = Flat(victim.transform.position, transform.position);
        if (Time.time >= nextPath) { nextPath = Time.time + 0.3f; agent.isStopped = false; agent.SetDestination(victim.transform.position); }
        if (d > 1.6f) return;
        Face(victim.transform.position - transform.position, 12f);
        LundunAnim.Trigger(animator, "Punch");
        Vector3 me = transform.position;
        Vector3 back = me - victim.transform.position; back.y = 0f;
        float yaw = back.sqrMagnitude > 0.01f ? Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg : float.NaN;
        GameObject v = victim;
        LundunStagger.Play(v, "Punched", 1.0f, true, yaw, () =>
        {
            if (v == null) return;
            NPCBrain b = v.GetComponent<NPCBrain>();
            if (b != null && b.enabled) b.Panic(me);                    // and off they run
        });
        BackToCorner();
    }

    void FightJames(Transform james)
    {
        if (james == null) return;
        Vector3 to = james.position - transform.position; to.y = 0f;
        float d = to.magnitude;
        if (d < 15f) group.lastContact = Time.time;
        bool inCar = vehicles != null && vehicles.IsDriving;
        // a car coming at them fast: get out of the way instead
        if (inCar && vehicles.PlayerSpeed > 8f && d < 14f)
        {
            Vector3 side = Vector3.Cross(Vector3.up, james.forward).normalized;
            if (Vector3.Dot(side, -to) < 0f) side = -side;
            agent.isStopped = false; agent.SetDestination(transform.position + side * 4f);
            return;
        }

        if (Carries == Weapon.Gun)
        {
            agent.updateRotation = false;
            if (d > ShootMax) { agent.isStopped = false; if (Time.time >= nextPath) { nextPath = Time.time + 0.3f; agent.SetDestination(james.position); } }
            else if (d < ShootMin) { agent.isStopped = false; agent.SetDestination(transform.position - to.normalized * 4f); }
            else agent.isStopped = true;
            Face(to, 10f);
            Vector3 v = transform.InverseTransformDirection(agent.velocity);
            LundunAnim.Float(animator, "AimMove", v.z, 0.1f);
            if (d <= ShootMax && Time.time >= nextHit)
            {
                nextHit = Time.time + Random.Range(0.9f, 1.4f);
                Vector3 from = transform.position + Vector3.up * 1.4f + transform.forward * 0.4f;
                PoliceAim.Fire(transform, from, james, Mathf.Lerp(0.55f, 0.25f, d / ShootMax), ShootMax * 1.5f, ShotDamage, ShotToCar);
            }
            return;
        }

        // knives, machetes and fists: get in close and swing (a short wind-up you can see coming, and dodge)
        float reach = inCar ? 3f : Carries == Weapon.Machete ? 2.0f : 1.7f;
        if (swinging)
        {
            agent.isStopped = true;
            Face(to, 14f);
            if (Time.time < windupUntil) return;
            swinging = false;
            if (d <= reach + 0.4f) Land(inCar, james);
            return;
        }
        agent.isStopped = false;
        if (Time.time >= nextPath) { nextPath = Time.time + 0.25f; agent.SetDestination(james.position); }
        if (d <= reach && Time.time >= nextHit)
        {
            swinging = true;
            windupUntil = Time.time + (Carries == Weapon.Machete ? 0.38f : 0.28f);
            nextHit = Time.time + (Carries == Weapon.Machete ? 1.4f : Carries == Weapon.Knife ? 0.95f : 1.1f);
            LundunAnim.Trigger(animator, "Punch");
        }
    }

    // ------------------------------------------------------------------ R4b: brawling
    void BrawlRivals()
    {
        if (foes == null) { BackToCorner(); return; }
        if (foe == null || foe.IsDead || Time.time >= nextFoe)
        {
            nextFoe = Time.time + 1f;
            foe = null; float bd = float.MaxValue;
            foreach (LundunGangMember m in foes.members)
            {
                if (m == null || m.IsDead) continue;
                float dd = Flat(m.transform.position, transform.position);
                if (dd < bd) { bd = dd; foe = m; }
            }
            if (foe == null) return;                                         // the group sorts out what's next
        }
        Vector3 to = foe.transform.position - transform.position; to.y = 0f;
        float d = to.magnitude;

        if (Carries == Weapon.Gun)
        {
            agent.updateRotation = false;
            if (d > ShootMax) { agent.isStopped = false; if (Time.time >= nextPath) { nextPath = Time.time + 0.3f; agent.SetDestination(foe.transform.position); } }
            else agent.isStopped = true;
            Face(to, 10f);
            if (d <= ShootMax && Time.time >= nextHit)
            {
                nextHit = Time.time + Random.Range(1.0f, 1.5f);
                if (Random.value < Mathf.Lerp(0.55f, 0.25f, d / ShootMax)) HitFoe(foe, 20f, true);
                if (LundunPopulation.Instance != null) LundunPopulation.Instance.Scare(transform.position, 30f);   // people nearby run
            }
            return;
        }

        float reach = Carries == Weapon.Machete ? 2.0f : 1.7f;
        if (swinging)
        {
            agent.isStopped = true;
            Face(to, 14f);
            if (Time.time < windupUntil) return;
            swinging = false;
            if (d <= reach + 0.4f) HitFoe(foe, Carries == Weapon.Machete ? 30f : Carries == Weapon.Knife ? 22f : 12f, false);
            return;
        }
        agent.isStopped = false;
        if (Time.time >= nextPath) { nextPath = Time.time + 0.25f; agent.SetDestination(foe.transform.position); }
        if (d <= reach && Time.time >= nextHit)
        {
            swinging = true;
            windupUntil = Time.time + (Carries == Weapon.Machete ? 0.38f : 0.28f);
            nextHit = Time.time + (Carries == Weapon.Machete ? 1.5f : Carries == Weapon.Knife ? 1.05f : 1.2f);
            LundunAnim.Trigger(animator, "Punch");
        }
    }

    void HitFoe(LundunGangMember who, float damage, bool gunfire)
    {
        if (who == null || who.IsDead) return;
        who.RivalHitAt = Time.time;
        Health h = who.GetComponent<Health>();
        if (h == null) return;
        Vector3 dir = who.transform.position - transform.position; dir.y = 0f;
        h.TakeDamage(damage, who.transform.position + Vector3.up * 1.3f, dir.normalized * (gunfire ? 6f : 2f), gunfire);
    }

    void Land(bool inCar, Transform james)
    {
        if (inCar)
        {
            VehicleHealth vh = james.GetComponent<VehicleHealth>();
            if (vh != null) vh.TakeDamage(Carries == Weapon.Machete ? 5f : 3f);
            return;
        }
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead) return;
        bool big = ++swings % 3 == 0;
        float dmg = Carries == Weapon.Machete ? 16f : Carries == Weapon.Knife ? 11f : (big ? 12f : 7f);
        ph.TakeDamage(dmg);
        if (Carries == Weapon.Machete || big) LundunPlayerReact.Punched();        // rocks him back
        else LundunHaptics.Light();
    }

    // ------------------------------------------------------------------ weapons
    void Draw()
    {
        if (prop != null || animator == null || !animator.isHuman) return;
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        float s = transform.localScale.y;
        if (Carries == Weapon.Knife || Carries == Weapon.Machete) prop = gangs.MakeBlade(hand, Carries == Weapon.Machete, s);
        else if (Carries == Weapon.Gun && LundunAngryDriver.GunModel != null)
        {
            prop = Instantiate(LundunAngryDriver.GunModel, hand);
            prop.name = "GangGun";
            if (s < 0.0001f) s = 1f;
            prop.transform.localScale = LundunAngryDriver.GunModel.transform.localScale / s;
            prop.transform.localPosition = new Vector3(0.05f, 0.08f, 0.02f) / s;
            prop.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);
            foreach (Collider c in prop.GetComponentsInChildren<Collider>()) Destroy(c);
            LundunAnim.Bool(animator, "Aim", true);
        }
    }

    void Holster()
    {
        if (prop != null) Destroy(prop);
        prop = null;
        swinging = false;
        LundunAnim.Bool(animator, "Aim", false);
        LundunAnim.Float(animator, "AimMove", 0f);
        if (agent != null) agent.updateRotation = true;
    }

    void Face(Vector3 dir, float rate)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), rate * Time.deltaTime);
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
}
