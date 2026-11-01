using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Cyclists (LundunPopulation adds this): people riding Lundun Cycles hire bikes around James (each one a
/// LundunCyclist), so the docks get used.
///   when      busiest at the rush hours (7–9:30 and 16:30–19), steady through the day, a couple in the evening, the odd
///             one late, none from midnight to 6 (times the phone's performance scale)
///   where     on the roads within about 160 m of James: some set off from a docking station near him, the rest are
///             already riding along a road; they only appear where the camera can't see, and are put away once
///             they're well away (or a crashed one's out of sight)
/// They count towards the pedestrian budget (they're people on bikes).
public class LundunCyclists : MonoBehaviour
{
    public static LundunCyclists Instance { get; private set; }
    public static int Active { get; private set; }

    private readonly List<LundunCyclist> riders = new List<LundunCyclist>();
    private readonly System.Random rng = new System.Random();
    private Transform holder;
    private float nextLook;
    private Mesh frame, wheel;
    private Material mat;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; Active = 0; }

    void Start()
    {
        holder = new GameObject("Cyclists").transform;
        holder.SetParent(transform, false);
    }

    int Wanted()
    {
        float h = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
        int n;
        if ((h >= 7f && h < 9.5f) || (h >= 16.5f && h < 19f)) n = 4;          // rush hour
        else if (h >= 9.5f && h < 16.5f) n = 3;
        else if (h >= 19f && h < 22f) n = 2;
        else if (h >= 6f || h >= 22f) n = 1;
        else n = 0;                                                         // midnight to 6
        float scale = LundunPopulation.Instance != null ? LundunPopulation.Instance.Scale : 1f;
        return Mathf.RoundToInt(n * scale);
    }

    void Update()
    {
        if (Time.time < nextLook) return;
        nextLook = Time.time + 1f;
        LundunBikes bikes = LundunBikes.Instance;
        LundunNavigation nav = LundunNavigation.Instance;
        if (bikes == null || nav == null || nav.Graph == null) return;
        if (frame == null && !bikes.Parts(out frame, out wheel, out mat)) return;

        Vector3 james = LundunHomeless.JamesAt();
        Camera cam = Camera.main;
        int riding = 0;
        for (int i = riders.Count - 1; i >= 0; i--)
        {
            LundunCyclist c = riders[i];
            if (c == null) { riders.RemoveAt(i); continue; }
            Vector3 at = c.transform.position;
            bool seen = cam != null && (LundunView.CanSee(cam, at) || (c.Person != null && LundunView.CanSee(cam, c.Person.position)));
            float d = Flat(at, james);
            bool done = (d > 200f || (c.Crashed && d > 60f) || (c.Stuck && d > 25f)) && !seen;
            if (done) { Remove(c); riders.RemoveAt(i); continue; }
            if (!c.Crashed) riding++;
        }
        Active = riding;
        if (riding < Wanted()) Spawn(nav.Graph, bikes, james, cam);
    }

    void Remove(LundunCyclist c)
    {
        // the rider goes with the bike (on it, lying there, or up and about as an ordinary pedestrian: they were ours)
        if (c.Person != null) Destroy(c.Person.gameObject);
        Destroy(c.gameObject);
    }

    /// A cyclist out of sight: setting off from a dock near James now and then, otherwise riding along a road.
    void Spawn(LundunRoadGraph g, LundunBikes bikes, Vector3 james, Camera cam)
    {
        Vector3 pos = Vector3.zero, facing = Vector3.forward;
        int from = -1, to = -1;
        bool ok = false;
        if (rng.NextDouble() < 0.4 && bikes.DockCount > 0)
        {
            for (int tries = 0; tries < 6 && !ok; tries++)
            {
                int i = rng.Next(bikes.DockCount);
                Vector3 d = bikes.DockAt(i);
                float dist = Flat(d, james);
                if (dist < 45f || dist > 160f || (cam != null && LundunView.CanSee(cam, d))) continue;
                NavPoint on = g.NearestOnRoad(new NavPoint(d.x, d.z), out int e, out double t, out double off);
                if (e < 0 || off > 25.0) continue;
                g.EdgeEnds(e, out from, out to);
                if (rng.NextDouble() < 0.5) { int x = from; from = to; to = x; }
                pos = d;
                Vector3 road = new Vector3((float)on.x, d.y, (float)on.z);
                facing = road - d; facing.y = 0f;
                if (facing.sqrMagnitude < 0.01f) facing = Dir(g, from, to);
                ok = true;
            }
        }
        if (!ok)
        {
            NavPoint p, dir; double half;
            if (!g.RandomRoadPoint(new NavPoint(james.x, james.z), 80, 160, rng, out p, out dir, out half)) return;
            g.NearestOnRoad(p, out int e, out double t, out double off);
            if (e < 0) return;
            g.EdgeEnds(e, out from, out to);
            if (rng.NextDouble() < 0.5) { int x = from; from = to; to = x; }
            double w; string cls; g.EdgeInfo(from, to, out w, out cls);
            if (cls == "service") return;
            facing = Dir(g, from, to);
            Vector3 left = new Vector3(-facing.z, 0f, facing.x);
            pos = new Vector3((float)p.x, 0f, (float)p.z) + left * Mathf.Max(1.2f, (float)(w * 0.5) - 1.1f);
            if (cam != null && LundunView.CanSee(cam, pos)) return;
        }
        if (!NavMesh.SamplePosition(pos + Vector3.up, out NavMeshHit hit, 3f, NavMesh.AllAreas)) return;
        pos = hit.position;
        if (Physics.CheckSphere(pos + Vector3.up * 0.8f, 2.5f, LayerMask.GetMask("Vehicle", "Player"), QueryTriggerInteraction.Ignore)) return;

        LundunPopulation pop = LundunPopulation.Instance;
        GameObject person = pop != null ? pop.NewPerson(holder, rng.NextDouble() < 0.5 ? "Casual" : null) : null;
        if (person == null) return;
        NPCBrain brain = person.GetComponent<NPCBrain>(); if (brain != null) brain.enabled = false;
        NavMeshAgent agent = person.GetComponent<NavMeshAgent>(); if (agent != null) agent.enabled = false;
        person.SetActive(true);

        GameObject g2 = new GameObject("Cyclist");
        g2.transform.SetParent(holder, false);
        g2.transform.SetPositionAndRotation(pos + Vector3.up * 0.05f, Quaternion.LookRotation(facing));
        g2.AddComponent<CharacterController>();
        LundunBike bike = g2.AddComponent<LundunBike>();
        bike.Build(frame, wheel, mat);
        int npc = LayerMask.NameToLayer("NPC");
        if (npc >= 0) g2.layer = npc;                                  // traffic still brakes for them (cars look for people too)
        LundunCyclist c = g2.AddComponent<LundunCyclist>();
        c.Begin(g, from, to, person, (float)(rng.NextDouble() * 2.0 + 4.5));
        riders.Add(c);
    }

    static Vector3 Dir(LundunRoadGraph g, int from, int to)
    {
        NavPoint a = g.NodePosition(from), b = g.NodePosition(to);
        Vector3 d = new Vector3((float)(b.x - a.x), 0f, (float)(b.z - a.z));
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
}
