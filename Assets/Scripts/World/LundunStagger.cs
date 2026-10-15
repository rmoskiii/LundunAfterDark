using UnityEngine;
using UnityEngine.AI;

/// A pedestrian reeling from something (shoved by James, shot, punched): stops walking, plays the reaction
/// and lets the animation itself move them (stumbles, spins), then puts them back on the pavement and
/// hands control back to whoever had it (their normal brain, or an angry driver).
/// Added for the length of the reaction, then removes itself.
public class LundunStagger : MonoBehaviour
{
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
        if (person == null || !person.activeInHierarchy || Busy(person)) return null;
        Health h = person.GetComponent<Health>();
        if (h != null && h.IsDead) return null;
        NavMeshAgent a = person.GetComponent<NavMeshAgent>();
        if (a == null || !a.enabled || !a.isOnNavMesh) return null;

        LundunStagger s = person.AddComponent<LundunStagger>();
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
        if (!float.IsNaN(faceYaw)) transform.rotation = Quaternion.Euler(0f, faceYaw, 0f);
        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            animator.applyRootMotion = rootMotion;
        }
        LundunAnim.Trigger(animator, trigger);
        if (health != null) health.Died += OnDied;
    }

    void Update()
    {
        if (Time.time >= until) End(true);
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

        if (agent != null && agent.enabled)
        {
            // back onto the walkable ground nearest to where the stumble left them
            if (!dead && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            agent.updatePosition = true;
            agent.updateRotation = true;
            if (agent.isOnNavMesh) agent.isStopped = false;
        }
        if (!dead && brain != null && (brainWasOn || GetComponent<LundunAngryDriver>() == null)) brain.enabled = true;
        if (recovered && !dead && after != null) after();
        Destroy(this);
    }
}
