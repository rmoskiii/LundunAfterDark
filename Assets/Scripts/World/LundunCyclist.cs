using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// Cyclists: someone riding a Lundun Cycles hire bike through town (on the bike's GameObject; LundunCyclists sends
/// them out). They ride it the way James does (the same bike, the same pedalling and leaning), on the left by the kerb,
/// along the roads: turning at junctions, slowing for corners, stopping at red lights (L4) and braking for whatever's
/// in front of them.
/// Hit by a car: the bike's knocked away on its own (a loose bike flying and tumbling down the road) and the rider's
/// thrown clear the other way. Hard enough to kill (as with anyone hit by a car) and the body's launched; otherwise
/// they tumble, lie there a moment, get up and run off (now and then, if it was James, they square up to him).
/// Shot off the bike, they fall and the bike goes over.
[RequireComponent(typeof(LundunBike))]
public class LundunCyclist : MonoBehaviour
{
    private LundunBike bike;
    private Transform rider;
    private Health health;
    private NPCBrain brain;
    private NavMeshAgent agent;
    private Animator anim;
    private LundunRoadGraph graph;
    private int fromNode, toNode, nextNode;
    private float cruise, stillFor, crashedAt;
    private Rigidbody riderBody;
    private Vector3 hitFrom;
    private bool byJames;
    private System.Action<Vector3> hurt;
    private readonly System.Collections.Generic.List<int> scratch = new System.Collections.Generic.List<int>();
    private static int bodyMask = -1;
    private static readonly RaycastHit[] castHits = new RaycastHit[8];

    public bool Crashed { get; private set; }
    /// Not going anywhere for a while (wedged): LundunCyclists clears it when nobody's looking.
    public bool Stuck { get { return stillFor > 5f; } }
    /// The person (on the bike, lying in the road, or up and about after a crash).
    public Transform Person { get { return rider; } }
    public bool RiderUp { get; private set; }

    // S5: a story rider (Tayo getting away): along a set route of road nodes, through red lights; Arrived at its end
    private List<int> route;
    private bool fleeing;
    public bool Arrived { get; private set; }
    /// S5: ride this route (road nodes, from the one after the start) instead of turning at random. flee = through reds.
    public void Follow(List<int> nodes, bool flee) { route = nodes; fleeing = flee; Arrived = false; PickNext(); }

    public void Begin(LundunRoadGraph g, int from, int to, GameObject person, float speed)
    {
        bike = GetComponent<LundunBike>();
        graph = g; fromNode = from; toNode = to; cruise = speed;
        PickNext();
        rider = person.transform;
        health = person.GetComponent<Health>();
        brain = person.GetComponent<NPCBrain>();
        agent = person.GetComponent<NavMeshAgent>();
        anim = person.GetComponentInChildren<Animator>(true);
        if (brain != null) brain.enabled = false;
        if (agent != null) agent.enabled = false;
        if (anim != null) { anim.applyRootMotion = false; LundunAnim.Float(anim, "Speed", 0f); }
        bike.Mount(rider);
        bike.OnCrash = impact => Crash(NearestCar(), impact);
        if (health != null) { hurt = h => Hurt(); health.Damaged += hurt; }
    }

    void OnDestroy()
    {
        if (health != null && hurt != null) health.Damaged -= hurt;
    }

    // ------------------------------------------------------------------ riding
    void Update()
    {
        if (Crashed) { AfterCrash(); return; }
        if (bike == null || bike.Rider == null || graph == null) return;
        if (bodyMask == -1) bodyMask = LayerMask.GetMask("Vehicle", "NPC", "Player");
        if (Arrived) { bike.SetInput(new Vector2(0f, bike.Speed > 0.3f ? -1f : 0f)); return; }      // S5: the end of the route

        NavPoint a = graph.NodePosition(fromNode), b = graph.NodePosition(toNode);
        Vector3 A = new Vector3((float)a.x, 0f, (float)a.z), B = new Vector3((float)b.x, 0f, (float)b.z);
        Vector3 dir = B - A; float segLen = dir.magnitude;
        if (segLen < 0.1f) { Advance(); return; }
        dir /= segLen;
        Vector3 pos = transform.position; pos.y = 0f;
        float s = Vector3.Dot(pos - A, dir);
        if (segLen - s < 3.5f) { Advance(); return; }
        double width; string cls;
        graph.EdgeInfo(fromNode, toNode, out width, out cls);

        // a few metres ahead, by the kerb on the left
        float aimS = s + 5f;
        Vector3 target;
        if (aimS <= segLen || nextNode < 0) target = A + dir * aimS + new Vector3(-dir.z, 0f, dir.x) * Kerb(width);
        else
        {
            NavPoint c = graph.NodePosition(nextNode);
            Vector3 C = new Vector3((float)c.x, 0f, (float)c.z), nd = (C - B).normalized;
            double nw; string ncls; graph.EdgeInfo(toNode, nextNode, out nw, out ncls);
            target = B + nd * (aimS - segLen) + new Vector3(-nd.z, 0f, nd.x) * Kerb(nw);
        }
        Vector3 to = target - pos;
        float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);
        float steer = Mathf.Clamp(angle / 25f, -1f, 1f);

        // how fast: their pace, slower round corners, stopping at the lights and for whatever's in front
        float speed = bike.Speed;
        float toEnd = segLen - s;
        float want = cruise;
        if (toEnd < 15f && nextNode >= 0)
        {
            NavPoint c = graph.NodePosition(nextNode);
            float turn = Vector3.Angle(dir, new Vector3((float)c.x, 0f, (float)c.z) - B);
            if (turn > 30f) want = Mathf.Min(want, 3.5f);
        }
        LundunTrafficLights lights = fleeing ? null : LundunTrafficLights.Instance;
        if (route != null && toNode == route[route.Count - 1] && toEnd < 6f) { want = 0f; Arrived = true; }
        if (lights != null)
        {
            float line = lights.StopAhead(fromNode, toNode, nextNode, toEnd, Mathf.Max(0f, speed), 3f, false, false);
            if (line >= 0f) want = Mathf.Min(want, Mathf.Sqrt(2f * 3f * Mathf.Max(0f, line - 1.2f)));
        }
        Vector3 fwd = transform.forward;
        int n = Physics.SphereCastNonAlloc(transform.position + Vector3.up * 0.8f + fwd * 0.9f, 0.35f, fwd, castHits, 3f + Mathf.Max(0f, speed) * 1.3f, bodyMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue; float otherSpeed = 0f;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = castHits[i];
            if (h.collider == null || h.transform.IsChildOf(transform)) continue;
            if (h.distance < nearest)
            {
                nearest = h.distance;
                Rigidbody orb = h.collider.attachedRigidbody;
                otherSpeed = orb != null ? Mathf.Max(0f, Vector3.Dot(orb.linearVelocity, fwd)) : 0f;
            }
        }
        if (nearest < float.MaxValue) want = Mathf.Min(want, Mathf.Sqrt(2f * 4f * Mathf.Max(0f, nearest - 1.5f)) + otherSpeed);

        // pedals: cruise at 'want' (the bike's top speed is 9 m/s), brake if too fast, freewheel to a stop
        float pedal;
        if (want < 0.3f) pedal = speed > 0.3f ? -1f : 0f;
        else if (speed > want + 0.6f) pedal = -0.5f;
        else pedal = Mathf.Clamp(want / 9f, 0.2f, 1f);
        bike.SetInput(new Vector2(steer, pedal));

        if (want > 1f && speed < 0.3f) stillFor += Time.deltaTime; else stillFor = 0f;
    }

    /// How far left of the middle they ride: by the kerb, clear of it.
    static float Kerb(double width) { return Mathf.Max(1.2f, (float)(width * 0.5) - 1.1f); }

    void Advance()
    {
        if (route != null && toNode == route[route.Count - 1]) { Arrived = true; return; }
        fromNode = toNode; toNode = nextNode;
        PickNext();
    }

    void PickNext()
    {
        if (route != null)
        {
            int i = route.IndexOf(toNode);
            if (i >= 0 && i + 1 < route.Count) { nextNode = route[i + 1]; return; }
            if (i == route.Count - 1) { nextNode = -1; return; }
        }
        graph.NextNodes(toNode, fromNode, scratch);
        // not down the service lanes if there's a choice
        for (int i = scratch.Count - 1; i >= 0 && scratch.Count > 1; i--)
        {
            double w; string cls;
            graph.EdgeInfo(toNode, scratch[i], out w, out cls);
            if (cls == "service") scratch.RemoveAt(i);
        }
        nextNode = scratch.Count > 0 ? scratch[Random.Range(0, scratch.Count)] : fromNode;
    }

    // ------------------------------------------------------------------ crashing
    Rigidbody NearestCar()
    {
        Collider[] near = Physics.OverlapSphere(transform.position + Vector3.up * 0.7f, 4f, LayerMask.GetMask("Vehicle"), QueryTriggerInteraction.Ignore);
        Rigidbody best = null; float bd = float.MaxValue;
        if (near != null)
            foreach (Collider c in near)
            {
                Rigidbody rb = c.attachedRigidbody;
                if (rb == null) continue;
                float d = (rb.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = rb; }
            }
        return best;
    }

    /// Hurt while riding (a car's bumper, a bullet): off they come.
    void Hurt()
    {
        if (!Crashed) { Crash(NearestCar(), 0f); return; }
        // hit again while tumbling and it's fatal: their own death throw takes over (Health adds its own physics)
        if (health != null && health.IsDead && riderBody != null)
        {
            DestroyImmediate(riderBody);
            riderBody = null;
            OntoTheGround();
        }
    }

    void Crash(Rigidbody car, float impact)
    {
        if (Crashed) return;
        Crashed = true;
        crashedAt = Time.time;
        Vector3 push = car != null ? car.linearVelocity : Vector3.zero; push.y = 0f;
        hitFrom = car != null ? car.position : transform.position - transform.forward;
        CarController cc = car != null ? car.GetComponent<CarController>() : null;
        byJames = cc != null && cc.IsDriven;
        Transform r = bike.ThrowRider();
        bike.Wreck(push);                                             // the bike flies off on its own
        if (r == null) return;
        r.rotation = Quaternion.Euler(0f, r.eulerAngles.y, 0f);
        if (health != null && health.IsDead) { OntoTheGround(); return; }    // a fatal hit: the usual death throw (Health)

        // thrown clear: a tumbling body for a moment, away from the bike (the other side of the car's path)
        riderBody = r.gameObject.AddComponent<Rigidbody>();
        if (riderBody == null) return;
        riderBody.mass = 70f;
        riderBody.interpolation = RigidbodyInterpolation.Interpolate;
        float hard = push.magnitude;
        Vector3 away = Vector3.Cross(Vector3.up, push.sqrMagnitude > 0.01f ? push.normalized : transform.forward) * (Random.value < 0.5f ? -1f : 1f);
        riderBody.linearVelocity = push * 0.8f + Vector3.up * Mathf.Lerp(1.2f, 4.5f, Mathf.Clamp01(hard / 10f)) + away * Mathf.Clamp(hard * 0.25f, 0.5f, 2.5f);
        riderBody.angularVelocity = Random.insideUnitSphere * Mathf.Lerp(2f, 7f, Mathf.Clamp01(hard / 10f));
    }

    /// Put the person on the walkable ground under them (so the usual death works: NPCBrain needs their NavMeshAgent).
    void OntoTheGround()
    {
        if (rider == null || agent == null) return;
        if (NavMesh.SamplePosition(rider.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            agent.enabled = true;
            agent.Warp(hit.position);
        }
    }

    void AfterCrash()
    {
        if (RiderUp || rider == null || (health != null && health.IsDead)) return;
        if (riderBody == null) return;
        bool settled = Time.time - crashedAt > 1.4f && riderBody.linearVelocity.magnitude < 0.6f;
        if (settled || Time.time - crashedAt > 4f) GetUp();
    }

    /// Back on their feet after a tumble: an ordinary pedestrian again, running off (or having a go at James).
    void GetUp()
    {
        RiderUp = true;
        Vector3 p = rider.position;
        if (riderBody != null) { Destroy(riderBody); riderBody = null; }
        Vector3 f = rider.forward; f.y = 0f;
        rider.rotation = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward);
        if (health != null && hurt != null) { health.Damaged -= hurt; hurt = null; }
        if (agent == null || !NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return;
        rider.position = hit.position;
        agent.enabled = true;
        agent.Warp(hit.position);
        agent.isStopped = false;
        GameObject who = rider.gameObject;
        Vector3 from = hitFrom;
        bool angry = byJames && Random.value < 0.2f;
        if (brain != null) brain.enabled = true;
        System.Action then = () =>
        {
            if (who == null) return;
            if (angry && who.GetComponent<LundunAngryDriver>() == null) who.AddComponent<LundunAngryDriver>().Begin(false);
            else { NPCBrain b = who.GetComponent<NPCBrain>(); if (b != null) b.Panic(from); }
        };
        if (LundunStagger.Play(who, "Hit", 0.7f, true, float.NaN, then) == null) then();
    }
}
