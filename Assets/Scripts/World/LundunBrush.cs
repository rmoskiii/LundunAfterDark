using UnityEngine;
using UnityEngine.AI;

/// R2: James walked into someone: they step aside, out of his way, and look round at him for a moment, then carry on
/// as they were (their brain and their route are left alone; this only nudges them and turns their head-and-body).
/// Added by LundunPopulation for under a second, then removes itself.
public class LundunBrush : MonoBehaviour
{
    const float StepTime = 0.35f, LookTime = 0.9f, StepDistance = 0.45f;

    private NavMeshAgent agent;
    private Transform james;
    private Vector3 side;
    private float started;
    private bool turnedOff, done;

    public static void Play(GameObject person, Vector3 jamesAt, Vector3 jamesHeading)
    {
        if (person == null || !person.activeInHierarchy || LundunStagger.Busy(person) || person.GetComponent<LundunBrush>() != null) return;
        NavMeshAgent a = person.GetComponent<NavMeshAgent>();
        if (a == null || !a.enabled || !a.isOnNavMesh) return;
        LundunBrush b = person.AddComponent<LundunBrush>();
        b.agent = a;
        // step out of James's line: to whichever side of it they already are
        Vector3 off = person.transform.position - jamesAt; off.y = 0f;
        Vector3 left = new Vector3(-jamesHeading.z, 0f, jamesHeading.x);
        b.side = (Vector3.Dot(off, left) >= 0f ? left : -left).normalized;
        PlayerHealth ph = PlayerHealth.Instance;
        b.james = ph != null ? ph.Body : null;
        b.started = Time.time;
    }

    void Update()
    {
        float t = Time.time - started;
        Health h = GetComponent<Health>();
        if (agent == null || !agent.enabled || !agent.isOnNavMesh || (h != null && h.IsDead) || LundunStagger.Busy(gameObject)) { Finish(); return; }
        if (t < StepTime)
        {
            float k = 1f - t / StepTime;                                        // eases out
            agent.Move(side * (StepDistance * 2f / StepTime) * k * Time.deltaTime);
        }
        if (t < LookTime && james != null)
        {
            if (!turnedOff) { agent.updateRotation = false; turnedOff = true; }
            Vector3 d = james.position - transform.position; d.y = 0f;
            // a look over the shoulder: no more than 70° off the way they're walking (no walking backwards)
            Vector3 travel = agent.desiredVelocity; travel.y = 0f;
            if (travel.sqrMagnitude > 0.04f && d.sqrMagnitude > 0.01f && Vector3.Angle(travel, d) > 70f)
                d = Vector3.RotateTowards(travel.normalized, d.normalized, 70f * Mathf.Deg2Rad, 0f);
            if (d.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 400f * Time.deltaTime);
        }
        if (t >= LookTime) Finish();
    }

    void OnDisable() { Finish(); }

    void Finish()
    {
        if (done) return;
        done = true;
        if (turnedOff && agent != null) agent.updateRotation = true;
        turnedOff = false;
        Destroy(this);
    }
}
