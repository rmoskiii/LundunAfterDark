using UnityEngine;
using UnityEngine.AI;

/// A pedestrian reeling from something (shoved by James, shot, punched): stops walking, plays the reaction
/// and lets the animation itself move them (stumbles, spins), then puts them back on the pavement and
/// hands control back to whoever had it (their normal brain, or an angry driver).
/// Added for the length of the reaction, then removes itself.
public class LundunStagger : MonoBehaviour
{
    /// R2: optional extras for a reaction (left out = exactly as before).
    public class Options
    {
        /// Start the clip this many seconds in (skips a slow start), instead of firing its trigger.
        public float startAt;
        /// Turn to faceYaw at this many degrees a second instead of snapping (0 = snap).
        public float turnRate;
        /// Pushed along this velocity (m/s), easing to a stop over slideTime; stops at walls and cars.
        public Vector3 slide;
        public float slideTime;
        /// Ended before the clip has finished: blend back into walking (or aiming) instead of finishing it in place.
        public bool blendOut;
    }
    static int slideMask = -1;
    private Options opts;
    private float wantYaw = float.NaN, slideLeft;
    private NavMeshAgent agent;
    private Animator animator;
    private Health health;
    private NPCBrain brain;
    private bool brainWasOn, rootMotion, ended;
    private float until;
    private System.Action after;

    /// True while this person is mid-reaction (other scripts should leave them alone).
    public static bool Busy(GameObject g) { return g != null && g.GetComponent<LundunStagger>() != null; }

    /// Play a reaction. seconds = how long they're out of action; moveWithAnimation = let the clip carry them.
    /// faceYaw: turn them to this heading first (NaN = leave as is). then: called when they recover (not if they die).
    public static LundunStagger Play(GameObject person, string trigger, float seconds, bool moveWithAnimation,
                                     float faceYaw = float.NaN, System.Action then = null)
    {
        return Play(person, trigger, seconds, moveWithAnimation, faceYaw, then, null);
    }

    /// R2: the same, with Options (start partway in, turn smoothly, a push, blend out).
    public static LundunStagger Play(GameObject person, string trigger, float seconds, bool moveWithAnimation,
                                     float faceYaw, System.Action then, Options options)
    {
        if (person == null || !person.activeInHierarchy || Busy(person)) return null;
        Health h = person.GetComponent<Health>();
        if (h != null && h.IsDead) return null;
        NavMeshAgent a = person.GetComponent<NavMeshAgent>();
        if (a == null || !a.enabled || !a.isOnNavMesh) return null;

        LundunStagger s = person.AddComponent<LundunStagger>();
        s.opts = options;
        s.Begin(trigger, seconds, moveWithAnimation, faceYaw, then);
        return s;
    }

    void Begin(string trigger, float seconds, bool moveWithAnimation, float faceYaw, System.Action then)
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        health = GetComponent<Health>();
        brain = GetComponent<NPCBrain>();
        after = then;
        until = Time.time + seconds;
        rootMotion = moveWithAnimation && animator != null && animator.isHuman;

        brainWasOn = brain != null && brain.enabled;
        if (brain != null) brain.enabled = false;
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.updatePosition = false;          // the animation moves them now
        agent.updateRotation = false;
        if (!float.IsNaN(faceYaw))
        {
            if (opts != null && opts.turnRate > 0f) wantYaw = faceYaw;            // R2: turn into it, don't snap
            else transform.rotation = Quaternion.Euler(0f, faceYaw, 0f);
        }
        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            animator.applyRootMotion = rootMotion;
        }
        int state;
        if (opts != null && opts.startAt > 0f && animator != null && animator.isActiveAndEnabled && FindState(animator, trigger, out state))
            animator.CrossFadeInFixedTime(state, 0.06f, 0, opts.startAt);      // R2: straight into the reaction
        else
            LundunAnim.Trigger(animator, trigger);
        if (opts != null && opts.slideTime > 0f) slideLeft = opts.slideTime;
        if (health != null) health.Died += OnDied;
    }

    void Update()
    {
        if (!float.IsNaN(wantYaw))
        {
            Quaternion want = Quaternion.Euler(0f, wantYaw, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, opts.turnRate * Time.deltaTime);
            if (Quaternion.Angle(transform.rotation, want) < 0.5f) wantYaw = float.NaN;
        }
        if (slideLeft > 0f) Slide();
        if (Time.time >= until) End(true);
    }

    /// R2: a state on the base layer, by its name (people's controllers from Lundun → 12 have Shoved, Locomotion, AimPistol).
    static bool FindState(Animator a, string name, out int hash)
    {
        hash = Animator.StringToHash("Base Layer." + name);
        if (a.HasState(0, hash)) return true;
        hash = Animator.StringToHash(name);
        return a.HasState(0, hash);
    }

    /// R2: the push, easing out; stops at walls and cars rather than going through them.
    void Slide()
    {
        float t = Mathf.Clamp01(slideLeft / opts.slideTime);
        slideLeft -= Time.deltaTime;
        Vector3 step = opts.slide * (t * t) * Time.deltaTime;
        step.y = 0f;
        float len = step.magnitude;
        if (len < 0.0001f) return;
        if (slideMask == -1) slideMask = LayerMask.GetMask("Default", "Vehicle");
        if (Physics.SphereCast(transform.position + Vector3.up * 0.9f, 0.25f, step / len, out RaycastHit hit, len + 0.1f, slideMask, QueryTriggerInteraction.Ignore))
        { slideLeft = 0f; return; }
        transform.position += step;
    }

    void OnDied() { End(false); }
    void OnDisable() { End(false); }          // put away by the population manager

    void End(bool recovered)
    {
        if (ended) return;
        ended = true;
        if (health != null) health.Died -= OnDied;
        if (animator != null) animator.applyRootMotion = false;
        bool dead = health != null && health.IsDead;
        // R2: cut short (the stumble's long tail): blend back to walking, or to aiming if they have a gun up
        if (opts != null && opts.blendOut && recovered && !dead && animator != null && animator.isActiveAndEnabled)
        {
            bool aim = false;
            foreach (AnimatorControllerParameter p in animator.parameters) if (p.name == "Aim") { aim = animator.GetBool("Aim"); break; }
            int back;
            if (FindState(animator, aim ? "AimPistol" : "Locomotion", out back)) animator.CrossFadeInFixedTime(back, 0.25f, 0);
        }

        if (agent != null && agent.enabled)
        {
            // back onto the walkable ground nearest to where the stumble left them
            if (!dead && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            agent.updatePosition = true;
            agent.updateRotation = true;
            if (agent.isOnNavMesh) agent.isStopped = false;
        }
        // (R4a: gang members keep their brain off: LundunGangMember does their thinking)
        if (!dead && brain != null && GetComponent<LundunGangMember>() == null && (brainWasOn || GetComponent<LundunAngryDriver>() == null)) brain.enabled = true;
        if (recovered && !dead && after != null) after();
        Destroy(this);
    }
}
