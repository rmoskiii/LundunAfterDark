using UnityEngine;
using UnityEngine.AI;

/// L6: one person sleeping rough (put there by LundunHomeless). Back to a wall, on cardboard, in a sleeping bag:
///   asleep        lying along the wall in the bag, only the head showing
///   sitting up    sitting in the bag (it covers them to the waist), a paper cup in front: when James walks past
///                 within about 3 m they ask for change ("Spare some change, mate?"); now and then they get up and
///                 follow him a couple of steps, then go back and sit down. Ignore them and they mutter as he goes
///   bumped        stepped on or walked into: a sleeper wakes up and sits up; someone sitting tells James to watch it
///   trouble       a gunshot nearby or getting hurt: up and away, an ordinary pedestrian from then on (their things
///                 are left behind)
/// There are no sitting or lying animations yet, so the idle one is used: lying, the bag covers the body; sitting,
/// they're set lower, legs under the pavement, and the bag covers the hips.
public class LundunHomelessPerson : MonoBehaviour
{
    enum State { Sitting, Lying, GettingUp, Following, GoingBack, SittingDown, Gone }

    private State state;
    private Vector3 spot, outward, along;
    private bool hasCup;
    private NavMeshAgent agent;
    private NPCBrain brain;
    private Health health;
    private Animator anim;
    private Transform things;                 // cardboard, bag, cup, bags (stay put if they run off)
    private Transform cup;
    private float moveFrom, moveUntil, followUntil, nextAsk, nextBumpLine;
    private Vector3 moveA, moveB;
    private bool asked, muttered;
    private System.Action<Vector3> hurt;

    const float SitDrop = 0.62f;

    /// On the ground (sitting or lying, or getting up / down): James's bumps are handled here, not by the usual stagger.
    public bool OnTheGround { get { return state == State.Sitting || state == State.Lying || state == State.GettingUp || state == State.SittingDown; } }
    public bool Gone { get { return state == State.Gone; } }
    public Transform Things { get { return things; } }

    public void Setup(Vector3 at, Vector3 facing, bool asleep, bool cupOut, Transform props, Transform cupProp)
    {
        agent = GetComponent<NavMeshAgent>();
        brain = GetComponent<NPCBrain>();
        health = GetComponent<Health>();
        anim = GetComponentInChildren<Animator>(true);
        spot = at; outward = facing; outward.y = 0f; outward.Normalize();
        along = Vector3.Cross(Vector3.up, outward);
        hasCup = cupOut; things = props; cup = cupProp;
        if (brain != null) brain.enabled = false;
        if (agent != null) agent.enabled = false;
        if (anim != null) { anim.applyRootMotion = false; LundunAnim.Float(anim, "Speed", 0f); }
        if (health != null) { hurt = h => Trouble(LundunHomeless.JamesAt()); health.Damaged += hurt; }
        Grubby();
        if (asleep) Lie(); else Sit();
        nextAsk = Time.time + Random.Range(0f, 3f);
    }

    void OnDestroy()
    {
        if (health != null && hurt != null) health.Damaged -= hurt;
        // the grubby copies of their clothes
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (!(r is ParticleSystemRenderer) && !(r is LineRenderer))
                foreach (Material m in r.sharedMaterials) if (m != null && m.name.EndsWith("_rough")) Destroy(m);
    }

    /// Worn, dirty clothes: their colours darker, browner and duller.
    void Grubby()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is LineRenderer) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                Material m = new Material(mats[i]) { name = mats[i].name + "_rough" };
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                float grey = (c.r + c.g + c.b) / 3f;
                Color dull = Color.Lerp(c, new Color(grey, grey, grey, c.a), 0.45f);
                dull = new Color(dull.r * 0.62f, dull.g * 0.57f, dull.b * 0.5f, c.a);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", dull);
                m.color = dull;
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }
        if (health != null) health.RefreshColour();
    }

    void Sit()
    {
        state = State.Sitting;
        transform.SetPositionAndRotation(spot - Vector3.up * SitDrop + outward * 0.05f, Quaternion.LookRotation(outward));
        if (cup != null) cup.gameObject.SetActive(hasCup);
    }

    void Lie()
    {
        state = State.Lying;
        // on their back along the wall, head towards 'along', inside the bag
        transform.SetPositionAndRotation(spot - along * 0.9f + Vector3.up * 0.12f, Quaternion.LookRotation(Vector3.up, along));
        if (cup != null) cup.gameObject.SetActive(false);
    }

    void Update()
    {
        if (state == State.Gone) return;
        if (health != null && health.IsDead) { state = State.Gone; return; }
        Vector3 james = LundunHomeless.JamesAt();
        bool onFoot = PlayerHealth.Instance != null && PlayerHealth.Instance.OnFoot && !PlayerHealth.Instance.IsDead;
        float d = Flat(james, spot);

        switch (state)
        {
            case State.Sitting:
                if (onFoot && d < 3.2f && Time.time >= nextAsk)
                {
                    nextAsk = Time.time + Random.Range(25f, 40f);
                    if (LundunHomeless.Say(LundunHomeless.Asks, 5f))
                    {
                        asked = true; muttered = false;
                        if (Random.value < 0.35f) GetUp();                  // and a couple of steps after him
                    }
                }
                else if (asked && !muttered && (d > 7f || !onFoot))
                {
                    muttered = true; asked = false;
                    if (Random.value < 0.6f) LundunHomeless.Say(LundunHomeless.Mutters, 3f);
                }
                // looking up at him as he passes
                if (onFoot && d < 6f)
                {
                    Vector3 to = james - transform.position; to.y = 0f;
                    if (Vector3.Dot(to.normalized, outward) > 0.1f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 90f * Time.deltaTime);
                }
                break;

            case State.GettingUp:
            case State.SittingDown:
                float t = Mathf.InverseLerp(moveFrom, moveUntil, Time.time);
                transform.position = Vector3.Lerp(moveA, moveB, Mathf.SmoothStep(0f, 1f, t));
                if (t >= 1f)
                {
                    if (state == State.SittingDown) Sit();
                    else StartWalking(james);
                }
                break;

            case State.Following:
                LundunAnim.Float(anim, "Speed", agent.enabled ? agent.velocity.magnitude : 0f, 0.1f);
                if (agent.enabled && agent.isOnNavMesh && Time.time < followUntil && onFoot && d < 9f)
                {
                    Vector3 to = james - transform.position; to.y = 0f;
                    if (to.magnitude > 1.8f) agent.SetDestination(james - to.normalized * 1.5f);
                }
                else GoBack();
                break;

            case State.GoingBack:
                LundunAnim.Float(anim, "Speed", agent.enabled ? agent.velocity.magnitude : 0f, 0.1f);
                if (!agent.enabled || !agent.isOnNavMesh || (!agent.pathPending && Flat(transform.position, spot) < 0.45f) || Time.time > followUntil + 8f)
                    SitDown();
                break;
        }
    }

    // ------------------------------------------------------------------ up and down
    void GetUp()
    {
        state = State.GettingUp;
        moveA = transform.position; moveB = spot + outward * 0.15f;
        moveFrom = Time.time; moveUntil = Time.time + 0.45f;
        transform.rotation = Quaternion.LookRotation(outward);
    }

    void StartWalking(Vector3 james)
    {
        if (agent == null || !NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) { SitDown(); return; }
        agent.enabled = true;
        agent.Warp(hit.position);
        agent.isStopped = false;
        agent.speed = 1.1f;
        agent.stoppingDistance = 0.3f;
        followUntil = Time.time + Random.Range(2f, 3.2f);
        state = State.Following;
    }

    void GoBack()
    {
        state = State.GoingBack;
        if (agent.enabled && agent.isOnNavMesh) { agent.isStopped = false; agent.SetDestination(spot); }
    }

    void SitDown()
    {
        LundunAnim.Float(anim, "Speed", 0f);
        if (agent != null && agent.enabled) agent.enabled = false;
        state = State.SittingDown;
        transform.rotation = Quaternion.LookRotation(outward);
        moveA = transform.position; moveB = spot - Vector3.up * SitDrop + outward * 0.05f;
        moveFrom = Time.time; moveUntil = Time.time + 0.45f;
    }

    // ------------------------------------------------------------------ bumps and trouble
    /// James walked into them, or onto them (LundunPopulation, L3).
    public void Bumped()
    {
        if (state == State.Lying)
        {
            Sit();
            LundunHomeless.Say(LundunHomeless.Woken, 2f);
            nextAsk = Time.time + 4f;
            return;
        }
        if (Time.time < nextBumpLine) return;
        nextBumpLine = Time.time + 6f;
        LundunHomeless.Say(LundunHomeless.Bumps, 2f);
    }

    /// A gunshot close by, or they're hurt: up and away, an ordinary pedestrian from now on (their things stay).
    public void Trouble(Vector3 from)
    {
        if (state == State.Gone) return;
        bool wasLying = state == State.Lying;
        state = State.Gone;
        if (health != null && hurt != null) { health.Damaged -= hurt; hurt = null; }
        // stand up where they are first (they may be lying, or sat low)
        Vector3 standAt = wasLying ? spot : transform.position;
        transform.rotation = Quaternion.LookRotation(outward);
        if (NavMesh.SamplePosition(new Vector3(standAt.x, spot.y + 0.5f, standAt.z), out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            if (agent != null) { agent.enabled = true; agent.Warp(hit.position); agent.isStopped = false; }
        }
        else transform.position = new Vector3(standAt.x, spot.y, standAt.z);
        if (cup != null) cup.gameObject.SetActive(hasCup);
        if (brain != null && agent != null && agent.enabled && agent.isOnNavMesh && (health == null || !health.IsDead))
        {
            brain.enabled = true;
            brain.Panic(from);
        }
        LundunHomeless.Released(this);
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
}
