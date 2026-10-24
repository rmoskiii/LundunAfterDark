using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// E3: Brixtun market's people. While you're near, traders stand behind their stalls (turning to the shoppers now and
/// then) and a crowd of shoppers (the city's pedestrian looks) browses: they walk the aisle and the pavements, stop at a
/// stall, look, move on, and now and then leave by the station passage or a street corner while others arrive.
/// They walk set paths (built by Lundun → 28, see Generator.DressMarket), not the NavMesh, so they're cheap. If a gun
/// goes off nearby or someone gets hurt, everyone close by turns back into an ordinary pedestrian and runs (their
/// NavMeshAgent and NPCBrain take over); once things have been quiet a while, the market takes them back.
/// How many: about 1.2 × the pedestrian budget (times performance scale and time of day), plus up to 14 traders.
public class LundunMarketCrowd : MonoBehaviour
{
    [Header("Filled in by Lundun → 28 (world space)")]
    [SerializeField] private Vector3[] nodes = new Vector3[0];
    [SerializeField] private int[] edgeA = new int[0], edgeB = new int[0];
    [SerializeField] private Vector3[] spots = new Vector3[0];
    [SerializeField] private Vector3[] spotLook = new Vector3[0];
    [SerializeField] private int[] spotNode = new int[0];
    [SerializeField] private int[] gates = new int[0];
    [SerializeField] private Vector3[] traderAt = new Vector3[0];
    [SerializeField] private Vector3[] traderFace = new Vector3[0];

    [Header("How many")]
    [Tooltip("Shoppers = this × the pedestrian budget (Low 10, Medium 18, High 26), then scaled for performance and time of day")]
    [SerializeField] private float perBudget = 1.2f;
    [SerializeField] private int fewest = 8;
    [SerializeField] private int maxTraders = 14;
    [Header("When")]
    [SerializeField] private float startWithin = 200f;
    [SerializeField] private float stopBeyond = 250f;
    [Header("Walking")]
    [SerializeField] private Vector2 walkSpeed = new Vector2(1.0f, 1.4f);
    [SerializeField] private Vector2 lingerTime = new Vector2(4f, 18f);
    [SerializeField, Range(0f, 1f)] private float leaveChance = 0.2f;
    [Header("Trouble")]
    [SerializeField] private float hearing = 40f;
    [SerializeField] private float calmAfter = 45f;

    enum State { Off, Walking, Lingering, Trading, Released }
    enum Then { Linger, Leave }

    class Person
    {
        public GameObject go;
        public Animator anim;
        public NavMeshAgent agent;
        public NPCBrain brain;
        public Health health;
        public bool rootMotion, trader;
        public State state;
        public readonly List<Vector3> path = new List<Vector3>();
        public int next, spot = -1, stall = -1;
        public Then then;
        public float speed, timer, since, lastSpeed;
        public Vector3 pos;
    }

    private readonly List<Person> people = new List<Person>();
    private bool[] spotTaken = new bool[0];
    private List<int>[] adj;
    private Transform holder, live;
    private Vector3 centre;
    private bool running;
    private float nextCheck, lastTrouble = -999f;
    private int wanted;
    private readonly System.Random rng = new System.Random(7373);

    /// E5: a store's crowd is smaller and only runs close to it.
    public void SetTuning(float start, float stop, float budget, int least, int traders)
    {
        startWithin = start; stopBeyond = stop; perBudget = budget; fewest = least; maxTraders = traders;
    }

    public void SetData(Vector3[] n, int[] ea, int[] eb, Vector3[] s, Vector3[] look, int[] sn, int[] g, Vector3[] tAt, Vector3[] tFace)
    {
        nodes = n; edgeA = ea; edgeB = eb; spots = s; spotLook = look; spotNode = sn; gates = g; traderAt = tAt; traderFace = tFace;
    }

    void Start()
    {
        holder = new GameObject("MarketCrowdPool").transform; holder.SetParent(transform, false); holder.gameObject.SetActive(false);
        live = new GameObject("MarketCrowd").transform; live.SetParent(transform, false);
        adj = new List<int>[nodes.Length];
        for (int i = 0; i < nodes.Length; i++) adj[i] = new List<int>();
        for (int i = 0; i < edgeA.Length && i < edgeB.Length; i++)
        {
            if (edgeA[i] < 0 || edgeB[i] < 0 || edgeA[i] >= nodes.Length || edgeB[i] >= nodes.Length) continue;
            adj[edgeA[i]].Add(edgeB[i]); adj[edgeB[i]].Add(edgeA[i]);
        }
        spotTaken = new bool[spots.Length];
        centre = Vector3.zero;
        foreach (Vector3 s in spots) centre += s;
        if (spots.Length > 0) centre /= spots.Length;
        PlayerCombat.GunshotFired += OnGunshot;
    }

    void OnDestroy() { PlayerCombat.GunshotFired -= OnGunshot; }

    // ------------------------------------------------------------------ on and off
    void Update()
    {
        if (nodes.Length == 0 || spots.Length == 0) return;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            float d = PlayerDistance();
            if (!running && d < startWithin) StartCrowd();
            else if (running && d > stopBeyond) StopCrowd();
            if (running) Housekeeping();
        }
        if (!running) return;
        float dt = Time.deltaTime;
        for (int i = 0; i < people.Count; i++) Step(people[i], dt);
    }

    Vector3 Player()
    {
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.Body != null && PlayerHealth.Instance.Body.gameObject.activeInHierarchy) return PlayerHealth.Instance.Body.position;
        if (Camera.main != null) return Camera.main.transform.position;
        return new Vector3(1e9f, 0f, 1e9f);
    }

    float PlayerDistance() { Vector3 d = Player() - centre; d.y = 0f; return d.magnitude; }

    int Wanted()
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return fewest;
        float busy = Mathf.Clamp01(0.4f + pop.TimeOfDayFactor);
        return Mathf.Max(fewest, Mathf.RoundToInt(pop.PedestrianBudget * perBudget * pop.Scale * busy));
    }

    void StartCrowd()
    {
        running = true;
        for (int i = 0; i < spotTaken.Length; i++) spotTaken[i] = false;
        // traders: the stalls nearest you get someone behind them
        List<int> stalls = new List<int>();
        for (int i = 0; i < traderAt.Length; i++) stalls.Add(i);
        Vector3 me = Player();
        stalls.Sort((a, b) => (traderAt[a] - me).sqrMagnitude.CompareTo((traderAt[b] - me).sqrMagnitude));
        int nTraders = Mathf.Min(maxTraders, stalls.Count);
        wanted = Wanted();
        int total = nTraders + wanted;
        while (people.Count < total) { Person p = Make(); if (p == null) break; people.Add(p); }
        int k = 0;
        for (int i = 0; i < people.Count; i++)
        {
            Person p = people[i];
            if (p.go == null) continue;
            if (k < nTraders) { MakeTrader(p, stalls[k]); k++; continue; }
            p.trader = false; p.stall = -1;
            if (i >= total) { Hide(p, State.Off); continue; }
            int s = FreeSpot(-1);
            if (s < 0) { Hide(p, State.Off); continue; }
            spotTaken[s] = true; p.spot = s;
            if (rng.NextDouble() < 0.6) { Show(p, spots[s]); Linger(p, spotLook[s]); }
            else
            {
                int from = rng.Next(nodes.Length);
                Show(p, nodes[from]);
                Walk(p, Route(from, spotNode[s]), Then.Linger); p.path.Add(spots[s]);
            }
        }
    }

    void MakeTrader(Person p, int stall)
    {
        p.trader = true; p.stall = stall;
        Show(p, traderAt[stall]);
        Vector3 f = stall < traderFace.Length ? traderFace[stall] : Vector3.forward; f.y = 0f;
        if (f.sqrMagnitude > 0.01f) p.go.transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
        p.state = State.Trading; p.timer = Mathf.Lerp(3f, 10f, (float)rng.NextDouble());
    }

    void StopCrowd()
    {
        running = false;
        for (int i = people.Count - 1; i >= 0; i--)
        {
            Person p = people[i];
            if (p.go == null || (p.health != null && p.health.IsDead)) { people.RemoveAt(i); continue; }
            if (p.state == State.Released) Recapture(p);
            p.trader = false; p.stall = -1;
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
            case State.Trading:
                p.timer -= dt; Animate(p, 0f, dt);
                if (p.timer <= 0f)
                {
                    // a glance along the stall, then back to the customers
                    p.timer = Mathf.Lerp(3f, 10f, (float)rng.NextDouble());
                    Vector3 f = p.stall < traderFace.Length ? traderFace[p.stall] : Vector3.forward; f.y = 0f;
                    if (f.sqrMagnitude > 0.01f)
                        p.go.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, (float)(rng.NextDouble() * 70.0 - 35.0), 0f) * f.normalized, Vector3.up);
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
        if (p.then == Then.Leave) { Hide(p, State.Off); return; }      // gone into the station, or round the corner
        Linger(p, p.spot >= 0 ? spotLook[p.spot] : p.pos + Vector3.forward);
    }

    void Linger(Person p, Vector3 lookAt)
    {
        p.state = State.Lingering;
        p.timer = Mathf.Lerp(lingerTime.x, lingerTime.y, (float)rng.NextDouble());
        Vector3 f = lookAt - p.pos; f.y = 0f;
        if (f.sqrMagnitude > 0.01f) p.go.transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
    }

    /// Done looking: another stall, or off home.
    void Decide(Person p)
    {
        int from = p.spot >= 0 && p.spot < spotNode.Length ? spotNode[p.spot] : Nearest(p.pos);
        if (p.spot >= 0) spotTaken[p.spot] = false;
        p.spot = -1;
        p.path.Clear(); p.next = 0; p.state = State.Walking;
        if (gates.Length > 0 && rng.NextDouble() < leaveChance)
        {
            int g = gates[rng.Next(gates.Length)];
            p.path.AddRange(Route(from, g)); p.then = Then.Leave;
            return;
        }
        int s = FreeSpot(from);
        if (s < 0) { Linger(p, p.pos + Vector3.forward); return; }
        spotTaken[s] = true; p.spot = s;
        p.path.Add(nodes[from]);
        p.path.AddRange(Route(from, spotNode[s])); p.path.Add(spots[s]); p.then = Then.Linger;
    }

    // ------------------------------------------------------------------ keeping it going
    void Housekeeping()
    {
        int shoppers = 0;
        Person spare = null;
        for (int i = people.Count - 1; i >= 0; i--)
        {
            Person p = people[i];
            if (p.go == null) { RemovePerson(i); continue; }
            if (p.state == State.Released)
            {
                if (p.health != null && p.health.IsDead) continue;
                if (Time.time - lastTrouble > calmAfter && Time.time - p.since > calmAfter && !Seen(p.go.transform.position))
                {
                    Recapture(p);
                    if (p.trader && p.stall >= 0) MakeTrader(p, p.stall); else Hide(p, State.Off);
                }
                continue;
            }
            if (p.state == State.Off) { if (spare == null) spare = p; continue; }
            if (!p.trader) shoppers++;
        }
        int want = Wanted();
        if (Time.time - lastTrouble < calmAfter) return;
        // someone new arrives (out of the station, round a corner) where you can't see them pop up
        if (shoppers < want && gates.Length > 0)
        {
            if (spare == null && people.Count < want + maxTraders) { spare = Make(); if (spare != null) people.Add(spare); }
            if (spare == null) return;
            int g = gates[rng.Next(gates.Length)];
            if (Seen(nodes[g] + Vector3.up)) return;
            spare.trader = false; spare.stall = -1;
            Show(spare, nodes[g]);
            spare.spot = -1; spare.state = State.Lingering; spare.timer = 0f;     // decides where to go next frame
        }
    }

    void RemovePerson(int i)
    {
        Person p = people[i];
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

    /// Everyone close by stops being part of the market and runs like any other pedestrian.
    void Scatter(Vector3 from, float range)
    {
        lastTrouble = Time.time;
        foreach (Person p in people)
        {
            if (p.go == null || !p.go.activeSelf || p.state == State.Released || p.state == State.Off) continue;
            if ((p.go.transform.position - from).sqrMagnitude <= range * range) Release(p, from);
        }
    }

    void Release(Person p, Vector3 from)
    {
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
    void Show(Person p, Vector3 at)
    {
        p.pos = at;
        p.go.transform.position = at;
        if (!p.go.activeSelf) p.go.SetActive(true);
        p.lastSpeed = 0f;
    }

    void Hide(Person p, State s)
    {
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
        p.go.transform.position = to;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            p.go.transform.rotation = Quaternion.Slerp(p.go.transform.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), dt * 8f);
        Animate(p, moved / Mathf.Max(dt, 1e-4f), dt);
    }

    static void Animate(Person p, float speed, float dt)
    {
        p.lastSpeed = Mathf.Lerp(p.lastSpeed, speed, Mathf.Clamp01(dt * 10f));
        LundunAnim.Float(p.anim, "Speed", p.lastSpeed, 0.1f);
    }

    /// A free spot, preferring ones near node `near` (any node if −1), but not always the nearest.
    int FreeSpot(int near)
    {
        int best = -1; float bestScore = float.MaxValue;
        for (int i = 0; i < spots.Length; i++)
        {
            if (spotTaken[i]) continue;
            float score = (float)rng.NextDouble() * 50f;
            if (near >= 0) score += Vector3.Distance(nodes[near], spots[i]) * 0.6f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    int Nearest(Vector3 at)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < nodes.Length; i++) { float d = (nodes[i] - at).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
        return best;
    }

    /// The nodes from a to b along the walkways (shortest), as points; includes both ends.
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
