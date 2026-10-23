using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// E7b: the crowd at the Lundun Wheel. While you're near, a few dozen people (the city's pedestrian looks) make the
/// riverside plaza busy: they stroll between spots at the river wall and in front of the Wheel, stop and look, join
/// the queue on the platform, step into a capsule as it passes (and ride it: the capsule shows them inside), and
/// come back out of other capsules, down the exit steps and onto the plaza again.
/// They walk set paths (built by Lundun → 27, see Generator.WheelCrowd*), not the NavMesh, so they're cheap. If a gun
/// goes off nearby or someone gets hurt, everyone close by turns back into an ordinary pedestrian and runs (their
/// NavMeshAgent and NPCBrain take over); once you've gone, or things have been quiet a while, the crowd takes them back.
/// How many: about 1.4 × the pedestrian budget, times the population's performance scale and time of day.
public class LundunWheelCrowd : MonoBehaviour
{
    [Header("Filled in by Lundun → 27 (all in the Wheel's space)")]
    [SerializeField] private Vector3[] nodes = new Vector3[0];
    [SerializeField] private int[] edgeA = new int[0], edgeB = new int[0];
    [SerializeField] private Vector3[] spots = new Vector3[0];
    [SerializeField] private Vector3[] spotLook = new Vector3[0];
    [SerializeField] private int[] spotNode = new int[0];
    [SerializeField] private Vector3[] queuePath = new Vector3[0];
    [SerializeField] private Vector3[] exitPath = new Vector3[0];
    [SerializeField] private int queueNode, exitNode;
    [SerializeField] private int[] arriveNodes = new int[0];
    [SerializeField] private float doorZ = -1.5f, edgeZ = -3f;

    [Header("How many")]
    [Tooltip("Crowd size = this × the pedestrian budget (Low 10, Medium 18, High 26), then scaled for performance and time of day")]
    [SerializeField] private float perBudget = 1.4f;
    [SerializeField] private int fewest = 8;
    [SerializeField] private int longestQueue = 16;
    [Header("When")]
    [SerializeField] private float startWithin = 230f;
    [SerializeField] private float stopBeyond = 290f;
    [Header("Walking")]
    [SerializeField] private Vector2 walkSpeed = new Vector2(1.15f, 1.55f);
    [SerializeField] private Vector2 lingerTime = new Vector2(6f, 26f);
    [SerializeField] private float queueGap = 0.75f;
    [SerializeField, Range(0f, 1f)] private float joinQueueChance = 0.45f;
    [Header("Trouble")]
    [SerializeField] private float hearing = 40f;
    [SerializeField] private float calmAfter = 45f;

    enum State { Off, Riding, Walking, Lingering, Queuing, Boarding, Leaving, Released }
    enum Then { Linger, Queue }

    class Person
    {
        public GameObject go;
        public Animator anim;
        public NavMeshAgent agent;
        public NPCBrain brain;
        public Health health;
        public bool rootMotion;
        public State state;
        public readonly List<Vector3> path = new List<Vector3>();   // Wheel space
        public int next;
        public Then then;
        public int spot = -1;
        public float speed, timer, queueS, since;
        public int capsule = -1;
        public Vector3 pos;            // Wheel space
        public float lastSpeed;
    }

    private readonly List<Person> people = new List<Person>();
    private readonly List<Person> queue = new List<Person>();
    private bool[] spotTaken = new bool[0];
    private List<int>[] adj;
    private float queueLength;
    private float[] queueAt = new float[0];
    private float[] lastDelta = new float[0];
    private LundunWheel wheel;
    private Transform holder, live;
    private bool running;
    private float nextCheck, lastTrouble = -999f;
    private int wanted;
    private readonly System.Random rng = new System.Random(5150);

    const float UnloadAt = -6.5f, BoardAt = -2.2f;      // degrees from the bottom: capsules climb past the platform along +x

    public void SetData(Vector3[] n, int[] ea, int[] eb, Vector3[] s, Vector3[] look, int[] sn, Vector3[] q, Vector3[] x, int qNode, int xNode, int[] arrive, float door, float edge)
    {
        nodes = n; edgeA = ea; edgeB = eb; spots = s; spotLook = look; spotNode = sn; queuePath = q; exitPath = x;
        queueNode = qNode; exitNode = xNode; arriveNodes = arrive; doorZ = door; edgeZ = edge;
    }

    void Start()
    {
        wheel = GetComponent<LundunWheel>();
        holder = new GameObject("WheelCrowdPool").transform; holder.SetParent(transform, false); holder.gameObject.SetActive(false);
        live = new GameObject("WheelCrowd").transform; live.SetParent(transform, false);
        adj = new List<int>[nodes.Length];
        for (int i = 0; i < nodes.Length; i++) adj[i] = new List<int>();
        for (int i = 0; i < edgeA.Length && i < edgeB.Length; i++)
        {
            if (edgeA[i] < 0 || edgeB[i] < 0 || edgeA[i] >= nodes.Length || edgeB[i] >= nodes.Length) continue;
            adj[edgeA[i]].Add(edgeB[i]); adj[edgeB[i]].Add(edgeA[i]);
        }
        spotTaken = new bool[spots.Length];
        queueAt = new float[queuePath.Length];
        for (int i = 1; i < queuePath.Length; i++) queueAt[i] = queueAt[i - 1] + Vector3.Distance(queuePath[i - 1], queuePath[i]);
        queueLength = queuePath.Length > 0 ? queueAt[queuePath.Length - 1] : 0f;
        longestQueue = Mathf.Min(longestQueue, Mathf.Max(1, Mathf.FloorToInt(queueLength / queueGap)));
        PlayerCombat.GunshotFired += OnGunshot;
    }

    void OnDestroy() { PlayerCombat.GunshotFired -= OnGunshot; }

    // ------------------------------------------------------------------ on and off
    void Update()
    {
        if (wheel == null || nodes.Length == 0 || queuePath.Length < 2) return;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            float d = PlayerDistance();
            if (!running && d < startWithin && wheel.Near) StartCrowd();
            else if (running && (d > stopBeyond || !wheel.Near)) StopCrowd();
            if (running) Housekeeping();
        }
        if (!running) return;
        Capsules();
        float dt = Time.deltaTime;
        for (int i = 0; i < people.Count; i++) Step(people[i], dt);
    }

    float PlayerDistance()
    {
        Vector3 p;
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.Body != null && PlayerHealth.Instance.Body.gameObject.activeInHierarchy) p = PlayerHealth.Instance.Body.position;
        else if (Camera.main != null) p = Camera.main.transform.position;
        else return 1e9f;
        Vector3 d = p - transform.position; d.y = 0f;
        return d.magnitude;
    }

    int Wanted()
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return fewest;
        float busy = Mathf.Clamp01(0.45f + pop.TimeOfDayFactor);       // the Wheel stays busy into the evening
        return Mathf.Max(fewest, Mathf.RoundToInt(pop.PedestrianBudget * perBudget * pop.Scale * busy));
    }

    void StartCrowd()
    {
        running = true;
        wheel.CrowdControlled = true;
        lastDelta = new float[wheel.CapsuleCount];
        for (int k = 0; k < lastDelta.Length; k++) lastDelta[k] = wheel.CapsuleDelta(k);
        wanted = Wanted();
        while (people.Count < wanted) { Person p = Make(); if (p == null) break; people.Add(p); }
        for (int i = 0; i < spotTaken.Length; i++) spotTaken[i] = false;
        queue.Clear();
        // a starting mix: some queuing, some riding, most out on the plaza
        int nQueue = Mathf.Min(longestQueue - 2, Mathf.RoundToInt(people.Count * 0.3f));
        int nRide = Mathf.RoundToInt(people.Count * 0.2f);
        for (int i = 0; i < people.Count && i < wanted; i++)
        {
            Person p = people[i];
            if (i < nQueue) { Show(p, QueuePoint(queueLength - i * queueGap)); p.state = State.Queuing; p.queueS = queueLength - i * queueGap; queue.Add(p); }
            else if (i < nQueue + nRide) Hide(p, State.Riding);
            else if (rng.NextDouble() < 0.6)
            {
                int s = FreeSpot(-1);
                if (s < 0) { Hide(p, State.Riding); continue; }
                Show(p, spots[s]); spotTaken[s] = true; p.spot = s; Linger(p, Look(s));
            }
            else
            {
                int s = FreeSpot(-1), from = rng.Next(nodes.Length);
                if (s < 0) { Hide(p, State.Riding); continue; }
                Show(p, nodes[from]); spotTaken[s] = true; p.spot = s;
                Walk(p, Route(from, spotNode[s]), Then.Linger); p.path.Add(spots[s]);
            }
        }
        for (int i = wanted; i < people.Count; i++) Hide(people[i], State.Off);
    }

    void StopCrowd()
    {
        running = false;
        wheel.CrowdControlled = false;
        queue.Clear();
        for (int i = people.Count - 1; i >= 0; i--)
        {
            Person p = people[i];
            if (p.go == null || (p.health != null && p.health.IsDead)) { people.RemoveAt(i); continue; }
            if (p.state == State.Released) Recapture(p);
            Hide(p, State.Off);
        }
    }

    Person Make()
    {
        if (LundunPopulation.Instance == null) return null;
        GameObject go = LundunPopulation.Instance.NewPerson(holder);
        if (go == null) return null;
        Person p = new Person { go = go };
        p.agent = go.GetComponent<NavMeshAgent>();
        p.brain = go.GetComponent<NPCBrain>();
        p.health = go.GetComponent<Health>();
        p.anim = go.GetComponentInChildren<Animator>(true);
        if (p.agent != null) p.agent.enabled = false;
        if (p.brain != null) p.brain.enabled = false;
        if (p.anim != null) { p.rootMotion = p.anim.applyRootMotion; p.anim.applyRootMotion = false; }
        go.transform.SetParent(live, false);
        p.speed = Mathf.Lerp(walkSpeed.x, walkSpeed.y, (float)rng.NextDouble());
        if (p.health != null) { Person me = p; p.health.Damaged += hit => OnHurt(me, hit); }
        p.state = State.Off;
        return p;
    }

    // ------------------------------------------------------------------ the capsules: getting off, getting on
    void Capsules()
    {
        int n = wheel.CapsuleCount;
        if (lastDelta.Length != n) { lastDelta = new float[n]; for (int k = 0; k < n; k++) lastDelta[k] = wheel.CapsuleDelta(k); }
        for (int k = 0; k < n; k++)
        {
            float d = wheel.CapsuleDelta(k), was = lastDelta[k];
            lastDelta[k] = d;
            if (k == wheel.RideCapsule || d - was > 20f || d < was) continue;
            if (was < UnloadAt && d >= UnloadAt) Unload(k);
            if (was < BoardAt && d >= BoardAt) Load(k);
        }
    }

    /// Everyone in capsule k gets off (as many riders as there are people riding: they come out of the door in turn).
    void Unload(int k)
    {
        int count = wheel.Riders(k), out_ = 0;
        wheel.SetRiders(k, 0);
        for (int i = 0; i < people.Count && out_ < count; i++)
        {
            Person p = people[i];
            if (p.state != State.Riding || p.go == null) continue;
            p.state = State.Leaving; p.capsule = k; p.timer = 0.15f + out_ * 0.55f;
            out_++;
        }
    }

    /// The front of the queue (a family, a couple, or one) walks to capsule k's door.
    void Load(int k)
    {
        if (queue.Count == 0 || WantedLevel() > 0) return;
        double r = rng.NextDouble();
        int group = r < 0.25 ? 1 : r < 0.6 ? 2 : r < 0.85 ? 3 : 4;
        group = Mathf.Min(group, Mathf.Min(queue.Count, 8 - wheel.Riders(k)));
        for (int i = 0; i < group; i++)
        {
            Person p = queue[0]; queue.RemoveAt(0);
            p.state = State.Boarding; p.capsule = k; p.timer = i * 0.25f;
        }
    }

    static int WantedLevel() { return WantedSystem.Instance != null ? WantedSystem.Instance.Stars : 0; }

    // ------------------------------------------------------------------ each person
    void Step(Person p, float dt)
    {
        if (p.go == null) return;
        switch (p.state)
        {
            case State.Walking: Walking(p, dt); break;
            case State.Lingering:
                p.timer -= dt; Animate(p, 0f, dt);
                if (p.timer <= 0f) Decide(p);
                break;
            case State.Queuing: Queuing(p, dt); break;
            case State.Boarding: Boarding(p, dt); break;
            case State.Leaving:
                p.timer -= dt;
                if (p.timer <= 0f)
                {
                    // out of the door, onto the platform's edge, along to the exit
                    Vector3 door = Local(wheel.CapsuleDoor(p.capsule, doorZ));
                    Show(p, door);
                    p.path.Clear();
                    p.path.Add(new Vector3(door.x, exitPath[0].y, edgeZ));
                    p.path.AddRange(exitPath);
                    p.next = 0;
                    p.state = State.Walking;
                    if (p.spot >= 0) { spotTaken[p.spot] = false; p.spot = -1; }
                    int s = FreeSpot(exitNode);
                    if (s >= 0)
                    {
                        spotTaken[s] = true; p.spot = s;
                        p.path.AddRange(Route(exitNode, spotNode[s])); p.path.Add(spots[s]); p.then = Then.Linger;
                    }
                    else { p.path.AddRange(Route(exitNode, queueNode)); p.path.Add(queuePath[0]); p.then = Then.Queue; }
                }
                break;
        }
    }

    void Walking(Person p, float dt)
    {
        if (p.next >= p.path.Count) { Arrive(p); return; }
        Vector3 target = p.path[p.next];
        Vector3 to = target - p.pos;
        float d = to.magnitude, step = p.speed * dt;
        if (d <= step) { Move(p, target, to, dt); p.next++; if (p.next >= p.path.Count) Arrive(p); return; }
        Move(p, p.pos + to * (step / d), to, dt);
    }

    void Arrive(Person p)
    {
        if (p.then == Then.Queue)
        {
            if (queue.Count >= longestQueue) { Decide(p); return; }
            p.state = State.Queuing; p.queueS = 0f; queue.Add(p);
            return;
        }
        Linger(p, p.spot >= 0 ? Look(p.spot) : p.pos + Vector3.forward);
    }

    void Linger(Person p, Vector3 lookAt)
    {
        p.state = State.Lingering;
        p.timer = Mathf.Lerp(lingerTime.x, lingerTime.y, (float)rng.NextDouble());
        Vector3 f = lookAt - p.pos; f.y = 0f;
        if (f.sqrMagnitude > 0.01f) p.go.transform.rotation = transform.rotation * Quaternion.LookRotation(f.normalized, Vector3.up);
    }

    /// Done looking: queue for the Wheel, or wander to another spot.
    void Decide(Person p)
    {
        // from a spot, back the way they came (that bit of path is known to be clear); otherwise the nearest corner
        int from = p.spot >= 0 && p.spot < spotNode.Length ? spotNode[p.spot] : Nearest(p.pos);
        if (p.spot >= 0) spotTaken[p.spot] = false;
        p.spot = -1;
        p.path.Clear(); p.next = 0; p.state = State.Walking;
        if (queue.Count < longestQueue - 1 && rng.NextDouble() < joinQueueChance && WantedLevel() == 0)
        {
            p.path.AddRange(Route(from, queueNode)); p.path.Add(queuePath[0]); p.then = Then.Queue;
            return;
        }
        int s = FreeSpot(from);
        if (s < 0) { Linger(p, p.pos + Vector3.forward); return; }
        spotTaken[s] = true; p.spot = s;
        p.path.AddRange(Route(from, spotNode[s])); p.path.Add(spots[s]); p.then = Then.Linger;
    }

    void Queuing(Person p, float dt)
    {
        int slot = queue.IndexOf(p);
        if (slot < 0) { Decide(p); return; }
        float want = Mathf.Max(0f, queueLength - slot * queueGap);
        float was = p.queueS;
        p.queueS = Mathf.MoveTowards(p.queueS, want, p.speed * dt);
        Vector3 at = QueuePoint(p.queueS);
        Vector3 ahead = QueuePoint(Mathf.Min(queueLength, p.queueS + 0.4f)) - at;
        if (p.queueS >= queueLength - 0.01f) ahead = Vector3.forward;            // the head faces the capsules
        p.pos = at;
        p.go.transform.position = transform.TransformPoint(at);
        ahead.y = 0f;
        if (ahead.sqrMagnitude > 0.0001f) p.go.transform.rotation = Quaternion.Slerp(p.go.transform.rotation, transform.rotation * Quaternion.LookRotation(ahead.normalized), dt * 6f);
        Animate(p, Mathf.Abs(p.queueS - was) / Mathf.Max(dt, 1e-4f), dt);
    }

    void Boarding(Person p, float dt)
    {
        if (p.timer > 0f) { p.timer -= dt; Animate(p, 0f, dt); return; }
        Vector3 door = Local(wheel.CapsuleDoor(p.capsule, doorZ));
        Vector3 to = door - p.pos; to.y = 0f;
        float d = to.magnitude, step = Mathf.Max(p.speed, 3.2f) * dt;      // a quick step to catch the moving door
        if (d <= step + 0.5f || Mathf.Abs(wheel.CapsuleDelta(p.capsule)) > 12f)
        {
            wheel.SetRiders(p.capsule, wheel.Riders(p.capsule) + 1);
            Hide(p, State.Riding);
            return;
        }
        Vector3 np = p.pos + to * (step / d); np.y = exitPath.Length > 0 ? exitPath[0].y : p.pos.y;
        Move(p, np, to, dt);
    }

    // ------------------------------------------------------------------ keeping it going
    void Housekeeping()
    {
        // people killed (and gone) are replaced next time; released ones come back once things have calmed down
        int hidden = 0, controlled = 0;
        for (int i = people.Count - 1; i >= 0; i--)
        {
            Person p = people[i];
            if (p.go == null) { RemovePerson(i); continue; }
            if (p.state == State.Released)
            {
                if (p.health != null && p.health.IsDead) continue;
                if (Time.time - lastTrouble > calmAfter && Time.time - p.since > calmAfter && !Seen(p.go.transform.position)) { Recapture(p); Hide(p, State.Riding); }
                continue;
            }
            controlled++;
            if (p.state == State.Riding) hidden++;
        }
        int want = Wanted();
        if (people.Count < want && Time.time - lastTrouble > calmAfter) { Person np = Make(); if (np != null) { people.Add(np); Hide(np, State.Riding); } }
        // too many riding (nobody getting off fast enough): someone walks in from the end of the plaza
        if (hidden > Mathf.Max(4, controlled * 0.35f) && arriveNodes.Length > 0)
        {
            int at = arriveNodes[rng.Next(arriveNodes.Length)];
            Vector3 w = transform.TransformPoint(nodes[at]);
            if (!Seen(w))
                foreach (Person p in people)
                    if (p.state == State.Riding && p.go != null)
                    {
                        Show(p, nodes[at]);
                        p.state = State.Lingering; p.timer = 0f;     // decides where to go next frame
                        break;
                    }
        }
    }

    void RemovePerson(int i)
    {
        Person p = people[i];
        queue.Remove(p);
        if (p.spot >= 0 && p.spot < spotTaken.Length) spotTaken[p.spot] = false;
        people.RemoveAt(i);
    }

    bool Seen(Vector3 world)
    {
        Camera c = Camera.main;
        return c != null && LundunView.CanSee(c, world);
    }

    // ------------------------------------------------------------------ trouble
    void OnGunshot(Vector3 from)
    {
        if (!running) return;
        Scatter(from, hearing);
    }

    void OnHurt(Person p, Vector3 hit)
    {
        if (!running || p.state == State.Released) return;
        Vector3 from = PlayerHealth.Instance != null && PlayerHealth.Instance.Body != null ? PlayerHealth.Instance.Body.position : hit;
        Release(p, from);
        Scatter(from, hearing * 0.6f);
    }

    /// Everyone close by stops being part of the crowd and runs like any other pedestrian.
    void Scatter(Vector3 from, float range)
    {
        lastTrouble = Time.time;
        foreach (Person p in people)
        {
            if (p.go == null || !p.go.activeSelf || p.state == State.Released || p.state == State.Riding || p.state == State.Off || p.state == State.Leaving) continue;
            if ((p.go.transform.position - from).sqrMagnitude <= range * range) Release(p, from);
        }
    }

    void Release(Person p, Vector3 from)
    {
        queue.Remove(p);
        if (p.spot >= 0 && p.spot < spotTaken.Length) { spotTaken[p.spot] = false; p.spot = -1; }
        p.state = State.Released; p.since = Time.time;
        if (p.anim != null) p.anim.applyRootMotion = p.rootMotion;
        if (p.agent == null || p.brain == null) return;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(p.go.transform.position, out hit, 2.5f, NavMesh.AllAreas)) return;
        p.agent.enabled = true;
        p.agent.Warp(hit.position);
        p.brain.enabled = true;
        p.brain.Panic(from);
    }

    void Recapture(Person p)
    {
        if (p.brain != null) p.brain.enabled = false;
        if (p.agent != null) p.agent.enabled = false;
        if (p.anim != null) p.anim.applyRootMotion = false;
    }

    // ------------------------------------------------------------------ helpers
    void Show(Person p, Vector3 local)
    {
        p.pos = local;
        p.go.transform.SetPositionAndRotation(transform.TransformPoint(local), transform.rotation);
        if (!p.go.activeSelf) p.go.SetActive(true);
        p.lastSpeed = 0f;
    }

    void Hide(Person p, State s)
    {
        queue.Remove(p);
        if (p.spot >= 0 && p.spot < spotTaken.Length) { spotTaken[p.spot] = false; p.spot = -1; }
        p.state = s;
        if (p.go != null && p.go.activeSelf) p.go.SetActive(false);
    }

    void Walk(Person p, List<Vector3> path, Then then)
    {
        p.path.Clear(); p.path.AddRange(path); p.next = 0; p.then = then; p.state = State.Walking;
    }

    void Move(Person p, Vector3 to, Vector3 dir, float dt)
    {
        float moved = (to - p.pos).magnitude;
        p.pos = to;
        p.go.transform.position = transform.TransformPoint(to);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            p.go.transform.rotation = Quaternion.Slerp(p.go.transform.rotation, transform.rotation * Quaternion.LookRotation(dir.normalized, Vector3.up), dt * 8f);
        Animate(p, moved / Mathf.Max(dt, 1e-4f), dt);
    }

    static void Animate(Person p, float speed, float dt)
    {
        p.lastSpeed = Mathf.Lerp(p.lastSpeed, speed, Mathf.Clamp01(dt * 10f));
        LundunAnim.Float(p.anim, "Speed", p.lastSpeed, 0.1f);
    }

    Vector3 Local(Vector3 world) { return transform.InverseTransformPoint(world); }
    Vector3 Look(int s) { return spotLook[s]; }

    Vector3 QueuePoint(float s)
    {
        if (queuePath.Length == 0) return Vector3.zero;
        for (int i = 1; i < queuePath.Length; i++)
            if (s <= queueAt[i]) { float seg = queueAt[i] - queueAt[i - 1]; return Vector3.Lerp(queuePath[i - 1], queuePath[i], seg > 0f ? (s - queueAt[i - 1]) / seg : 0f); }
        return queuePath[queuePath.Length - 1];
    }

    /// A free spot, preferring ones near node `near` (any node if −1), but not always the nearest.
    int FreeSpot(int near)
    {
        int best = -1; float bestScore = float.MaxValue;
        for (int i = 0; i < spots.Length; i++)
        {
            if (spotTaken[i]) continue;
            float score = (float)rng.NextDouble() * 60f;
            if (near >= 0) score += Vector3.Distance(nodes[near], spots[i]) * 0.5f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    int Nearest(Vector3 local)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < nodes.Length; i++) { float d = (nodes[i] - local).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
        return best;
    }

    /// The nodes from a to b along the path network (shortest), as points; includes both ends.
    List<Vector3> Route(int a, int b)
    {
        List<Vector3> pts = new List<Vector3>();
        int n = nodes.Length;
        if (a < 0 || b < 0 || a >= n || b >= n) return pts;
        float[] dist = new float[n]; int[] prev = new int[n]; bool[] done = new bool[n];
        for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
        dist[a] = 0f;
        for (int it = 0; it < n; it++)
        {
            int u = -1; float ud = float.MaxValue;
            for (int i = 0; i < n; i++) if (!done[i] && dist[i] < ud) { ud = dist[i]; u = i; }
            if (u < 0 || u == b) break;
            done[u] = true;
            foreach (int v in adj[u])
            {
                float nd = dist[u] + Vector3.Distance(nodes[u], nodes[v]);
                if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
            }
        }
        if (prev[b] < 0 && a != b) { pts.Add(nodes[b]); return pts; }
        for (int v = b; v >= 0; v = prev[v]) { pts.Insert(0, nodes[v]); if (v == a) break; }
        return pts;
    }
}
