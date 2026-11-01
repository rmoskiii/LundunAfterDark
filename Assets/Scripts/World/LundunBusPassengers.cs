using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// L5: people at the bus stops (LundunPopulation adds this).
///   waiting      the stops near James (up to four, within about 110 m) each have a few people waiting at the kerb by
///                the shelter, looking up the road for the bus: 2–4 in the day, 1–3 in the evening, at most one late at
///                night (times the phone's performance scale). Newcomers walk up along the pavement now and then;
///                when James is far off they're put away
///   the bus      when a bus pulls in at one of those stops, the queue walks to its front door one after another and
///                gets on (they disappear inside), and 0–4 people step off the middle door and walk away along the
///                pavement (fewer at night); the bus waits until everyone's on (at most 12 s), then indicates and pulls
///                out (LundunTrafficCar)
///   people       they're ordinary pedestrians (the same looks, and they count towards the same budget): they can be
///                bumped (L3), and anyone hurt nearby or a gunshot within 30 m and the queue scatters; anyone who
///                squares up to James or runs off stops being a passenger
/// The ones getting off are the town's pooled pedestrians (LundunPopulation.SpawnPersonAt), so they carry on as normal.
public class LundunBusPassengers : MonoBehaviour
{
    public static LundunBusPassengers Instance { get; private set; }
    /// People waiting or boarding right now (they share the pedestrian budget, like gang members).
    public static int Active { get; private set; }

    const byte ToSpot = 0, Waiting = 1, ToBus = 2;

    class Person
    {
        public GameObject go;
        public NavMeshAgent agent;
        public NPCBrain brain;
        public Health health;
        public Animator anim;
        public Stop stop;
        public Vector3 spot;
        public byte state;
        public float goAt, nextLook;
        public System.Action<Vector3> hurt;
    }

    class Stop
    {
        public int index;
        public Vector3 point, travel, left, kerb;
        public bool active, filling;
        public int want;
        public float nextArrival, nextWant;
        public readonly List<Person> queue = new List<Person>();
    }

    class Job
    {
        public LundunTrafficCar bus;
        public Stop stop;
        public float arrived, deadline, nextOff;
        public int toGetOff;
        public readonly List<Person> boarding = new List<Person>();
    }

    private readonly List<Stop> stops = new List<Stop>();
    private readonly List<Person> spare = new List<Person>();
    private readonly List<GameObject> strays = new List<GameObject>();            // released: removed once nobody sees them
    private readonly List<Job> jobs = new List<Job>();
    private readonly Dictionary<LundunTrafficCar, float> handled = new Dictionary<LundunTrafficCar, float>();
    private readonly System.Random rng = new System.Random();
    private Transform holder;
    private float nextLook;
    private int pavementMask = -1;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; Active = 0; }
    void OnEnable() { PlayerCombat.GunshotFired += OnGunshot; }
    void OnDisable() { PlayerCombat.GunshotFired -= OnGunshot; }

    void Start()
    {
        holder = new GameObject("BusPassengers").transform;
        holder.SetParent(transform, false);
        pavementMask = 1 << 4;                                   // the greybox's pavement area (NPCBrain's pavementArea)
        LundunDressing d = LundunDressing.Instance;
        if (d == null) return;
        for (int i = 0; i < d.BusStopCount; i++)
        {
            Vector4 b = d.BusStop(i);
            Vector3 t = new Vector3(b.z, 0f, b.w).normalized;
            Vector3 l = new Vector3(-t.z, 0f, t.x);                 // UK: the kerb is on the bus's left
            Vector3 p = new Vector3(b.x, 0f, b.y);
            stops.Add(new Stop { index = i, point = p, travel = t, left = l, kerb = p + l * 1.6f });
        }
    }

    // ------------------------------------------------------------------ every frame
    void Update()
    {
        if (stops.Count == 0) return;
        Vector3 james = James();
        if (Time.time >= nextLook) { nextLook = Time.time + 1f; ChooseStops(james); Tidy(); }

        int count = 0;
        foreach (Stop s in stops)
        {
            if (!s.active) continue;
            Fill(s);
            for (int i = s.queue.Count - 1; i >= 0; i--)
            {
                Person p = s.queue[i];
                if (!Look(p)) { s.queue.Remove(p); continue; }        // (Release may have taken them off already)
                count++;
            }
        }
        for (int j = jobs.Count - 1; j >= 0; j--) if (!Run(jobs[j])) jobs.RemoveAt(j);
        foreach (Job jb in jobs) count += jb.boarding.Count;
        Active = count;
        WatchBuses();
    }

    static Vector3 James()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        if (vs != null && vs.PlayerTarget != null) return vs.PlayerTarget.position;
        return ph != null && ph.Body != null ? ph.Body.position : Vector3.zero;
    }

    /// The (up to four) nearest stops within 110 m are busy; a stop more than 150 m away is cleared.
    void ChooseStops(Vector3 james)
    {
        List<Stop> near = new List<Stop>();
        foreach (Stop s in stops)
        {
            float d = Flat(s.point, james);
            if (d < 110f) near.Add(s);
            else if (s.active && d > 150f) Clear(s);
        }
        near.Sort((a, b) => Flat(a.point, james).CompareTo(Flat(b.point, james)));
        for (int i = 0; i < near.Count; i++)
        {
            Stop s = near[i];
            if (i >= 4) { if (s.active && Flat(s.point, james) > 130f) Clear(s); continue; }
            if (!s.active) { s.active = true; s.filling = true; s.nextArrival = 0f; s.nextWant = 0f; }
        }
    }

    /// How many should be waiting: more in the day, fewer at night.
    int WantAt()
    {
        float h = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
        int lo, hi;
        if (h >= 7f && h < 19f) { lo = 2; hi = 4; }
        else if (h >= 6f && h < 23f) { lo = 1; hi = 3; }
        else { lo = 0; hi = 1; }
        float scale = LundunPopulation.Instance != null ? LundunPopulation.Instance.Scale : 1f;
        return Mathf.RoundToInt(rng.Next(lo, hi + 1) * scale);
    }

    /// Top the queue up: at first straight onto the spots (if nobody can see them), then newcomers now and then, walking up.
    void Fill(Stop s)
    {
        if (Time.time >= s.nextWant) { s.nextWant = Time.time + 40f; s.want = WantAt(); }
        if (s.queue.Count >= s.want) { s.filling = false; return; }
        if (Time.time < s.nextArrival) return;
        // just come into range: everyone's there already; after that, newcomers now and then
        bool first = s.filling;
        s.nextArrival = Time.time + (first ? 0.3f : Random.Range(15f, 35f));
        Vector3 spot = Spot(s, s.queue.Count);
        Camera cam = Camera.main;
        Vector3 from = spot;
        if (!first || (cam != null && LundunView.CanSee(cam, spot)))
        {
            // walk up from along the pavement, out of sight
            float along = (rng.NextDouble() < 0.5 ? -1f : 1f) * Random.Range(22f, 34f);
            from = s.kerb + s.travel * along + s.left * 1.4f;
            if (cam != null && LundunView.CanSee(cam, from)) { s.nextArrival = Time.time + 3f; return; }
        }
        Person p = Make(from);
        if (p == null) { s.nextArrival = Time.time + 5f; return; }
        p.stop = s; p.spot = spot;
        p.state = ToSpot;
        if (Flat(from, spot) < 0.5f) { p.state = Waiting; Face(p, -s.travel, 1000f); p.agent.isStopped = true; }
        else { p.agent.isStopped = false; p.agent.speed = Random.Range(1.2f, 1.6f); p.agent.SetDestination(spot); }
        s.queue.Add(p);
    }

    /// Where the n-th person waits: at the kerb by the shelter, spread along it, not quite in a line.
    Vector3 Spot(Stop s, int n)
    {
        float along = -1.5f + (n % 5) * 1.1f + Random.Range(-0.25f, 0.25f);
        float fromKerb = 0.9f + Random.Range(0f, 0.8f);
        Vector3 p = s.kerb + s.travel * along + s.left * fromKerb;
        if (NavMesh.SamplePosition(p + Vector3.up, out NavMeshHit hit, 1.5f, pavementMask)) return hit.position;
        if (NavMesh.SamplePosition(p + Vector3.up, out hit, 1.5f, NavMesh.AllAreas)) return hit.position;
        return p;
    }

    /// A waiting person, each frame. False = no longer a passenger (gone, dead, scared off, squaring up to James).
    bool Look(Person p)
    {
        if (p.go == null) return false;
        if (p.health != null && p.health.IsDead) { Release(p, false, Vector3.zero); return false; }
        if (p.brain != null && p.brain.enabled || p.go.GetComponent<LundunAngryDriver>() != null) { Release(p, false, Vector3.zero); return false; }
        if (!p.go.activeInHierarchy || LundunStagger.Busy(p.go)) return true;
        if (!p.agent.enabled || !p.agent.isOnNavMesh) return true;
        LundunAnim.Float(p.anim, "Speed", p.agent.velocity.magnitude, 0.1f);
        if (p.state == ToSpot)
        {
            if (!p.agent.pathPending && Flat(p.go.transform.position, p.spot) < 0.45f)
            {
                p.state = Waiting;
                p.agent.isStopped = true;
            }
            else if (p.agent.isStopped || !p.agent.hasPath) { p.agent.isStopped = false; p.agent.SetDestination(p.spot); }
        }
        else if (p.state == Waiting)
        {
            // knocked off the spot (a bump): back to it
            if (Flat(p.go.transform.position, p.spot) > 1.4f) { p.state = ToSpot; p.agent.isStopped = false; p.agent.SetDestination(p.spot); return true; }
            // looking up the road for the bus, and now and then about
            if (Time.time >= p.nextLook) p.nextLook = Time.time + Random.Range(3f, 8f);
            float look = Mathf.Repeat(p.nextLook * 13.7f, 1f);
            Vector3 dir = look < 0.6f ? -p.stop.travel : look < 0.8f ? p.stop.left : -p.stop.left;
            Face(p, dir, 120f);
        }
        return true;
    }

    static void Face(Person p, Vector3 dir, float rate)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        p.go.transform.rotation = Quaternion.RotateTowards(p.go.transform.rotation, Quaternion.LookRotation(dir), rate * Time.deltaTime);
    }

    // ------------------------------------------------------------------ the buses
    void WatchBuses()
    {
        foreach (LundunTrafficCar bus in LundunTrafficCar.Buses)
        {
            if (bus == null || !bus.AtStop) continue;
            float seen;
            if (handled.TryGetValue(bus, out seen) && seen == bus.StoppedAt) continue;
            handled[bus] = bus.StoppedAt;
            Stop s = StopFor(bus);
            if (s == null) continue;                       // a stop nobody's near: the bus just waits its usual time
            Job jb = new Job { bus = bus, stop = s, arrived = Time.time, deadline = Time.time + 12f, nextOff = Time.time + 0.8f };
            float h = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
            jb.toGetOff = h >= 7f && h < 22f ? rng.Next(0, 5) : rng.Next(0, 3);
            // the queue gets on in the order they're standing, a moment apart
            float at = Time.time + 0.4f;
            List<Person> line = new List<Person>(s.queue);
            Vector3 door = Door(bus, true);
            line.Sort((a, b) => Flat(a.go.transform.position, door).CompareTo(Flat(b.go.transform.position, door)));
            foreach (Person p in line)
            {
                if (p.go == null || !p.go.activeInHierarchy) continue;
                p.state = ToBus; p.goAt = at; at += 0.7f;
                jb.boarding.Add(p);
                s.queue.Remove(p);
            }
            bus.HoldAtStop(jb.deadline);
            jobs.Add(jb);
        }
    }

    Stop StopFor(LundunTrafficCar bus)
    {
        Vector3 f = bus.transform.forward; f.y = 0f; f.Normalize();
        foreach (Stop s in stops)
            if (s.active && Flat(s.point, bus.transform.position) < 9f && Vector3.Dot(s.travel, f) > 0.7f) return s;
        return null;
    }

    /// The front door (getting on) or the middle door (getting off), on the kerb side, just outside the bus.
    static Vector3 Door(LundunTrafficCar bus, bool front)
    {
        LundunCarShape shape = bus.GetComponent<LundunCarShape>();
        float nose = shape != null ? shape.Nose.z : 5.2f, width = shape != null ? shape.Width : 2.55f;
        Vector3 local = new Vector3(-width * 0.5f - 0.45f, 0f, front ? nose - 1.4f : nose - 5.6f);
        Vector3 w = bus.transform.TransformPoint(local);
        if (NavMesh.SamplePosition(w, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) return hit.position;
        return w;
    }

    /// One bus at one stop: people on, people off, then away. False when it's done.
    bool Run(Job jb)
    {
        bool busGone = jb.bus == null || !jb.bus.isActiveAndEnabled || jb.bus.CurrentMode != LundunTrafficCar.Mode.Driving || !jb.bus.HasDriver;
        if (busGone)
        {
            // hijacked or gone: whoever didn't get on goes back to waiting
            foreach (Person p in jb.boarding) BackToQueue(p, jb.stop);
            jb.boarding.Clear();
            return false;
        }
        Vector3 front = Door(jb.bus, true);
        for (int i = jb.boarding.Count - 1; i >= 0; i--)
        {
            Person p = jb.boarding[i];
            if (p.go == null) { jb.boarding.RemoveAt(i); continue; }
            if ((p.health != null && p.health.IsDead) || (p.brain != null && p.brain.enabled) || p.go.GetComponent<LundunAngryDriver>() != null)
            { Release(p, false, Vector3.zero); jb.boarding.RemoveAt(i); continue; }
            if (!p.go.activeInHierarchy || LundunStagger.Busy(p.go) || !p.agent.enabled || !p.agent.isOnNavMesh) continue;
            LundunAnim.Float(p.anim, "Speed", p.agent.velocity.magnitude, 0.1f);
            if (Time.time < p.goAt) continue;
            p.agent.isStopped = false;
            p.agent.speed = 1.7f;
            if (!p.agent.hasPath || Flat(p.agent.destination, front) > 0.6f) p.agent.SetDestination(front);
            if (Flat(p.go.transform.position, front) < 0.9f) { Put(p); jb.boarding.RemoveAt(i); }     // on board
        }
        // people getting off, one at a time, walking away along the pavement
        if (jb.toGetOff > 0 && Time.time >= jb.nextOff)
        {
            jb.toGetOff--;
            jb.nextOff = Time.time + Random.Range(0.8f, 1.2f);
            GetOff(jb);
        }
        bool done = jb.boarding.Count == 0 && jb.toGetOff == 0 && Time.time > jb.arrived + 3f;
        if (done || Time.time > jb.deadline)
        {
            foreach (Person p in jb.boarding) BackToQueue(p, jb.stop);            // missed it: wait for the next one
            jb.boarding.Clear();
            jb.bus.HoldAtStop(Time.time + 0.6f);
            return false;
        }
        return true;
    }

    void GetOff(Job jb)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return;
        Vector3 door = Door(jb.bus, false);
        GameObject g = pop.SpawnPersonAt(door, Quaternion.LookRotation(jb.stop.left).eulerAngles.y);
        if (g == null) return;
        // off along the pavement, one way or the other
        NavMeshAgent a = g.GetComponent<NavMeshAgent>();
        if (a == null || !a.enabled || !a.isOnNavMesh) return;
        float along = (rng.NextDouble() < 0.5 ? -1f : 1f) * Random.Range(18f, 32f);
        Vector3 to = jb.stop.kerb + jb.stop.travel * along + jb.stop.left * Random.Range(1f, 2.2f);
        if (NavMesh.SamplePosition(to + Vector3.up, out NavMeshHit hit, 3f, pavementMask) || NavMesh.SamplePosition(to + Vector3.up, out hit, 3f, NavMesh.AllAreas))
            a.SetDestination(hit.position);
    }

    void BackToQueue(Person p, Stop s)
    {
        if (p.go == null || (p.health != null && p.health.IsDead)) return;
        if (!s.active) { Put(p); return; }
        p.stop = s; p.spot = Spot(s, s.queue.Count);
        p.state = ToSpot;
        if (p.agent.enabled && p.agent.isOnNavMesh) { p.agent.isStopped = false; p.agent.SetDestination(p.spot); }
        s.queue.Add(p);
    }

    // ------------------------------------------------------------------ trouble
    void OnGunshot(Vector3 from)
    {
        foreach (Stop s in stops)
        {
            if (!s.active || Flat(s.point, from) > 30f) continue;
            foreach (Person p in s.queue.ToArray()) Release(p, true, from);
            s.queue.Clear();
            s.nextArrival = Time.time + 60f;                           // nobody new for a while
        }
        foreach (Job jb in jobs)
        {
            if (Flat(jb.stop.point, from) > 30f) continue;
            foreach (Person p in jb.boarding) Release(p, true, from);
            jb.boarding.Clear();
        }
    }

    void Hurt(Person p, Vector3 at)
    {
        // anyone at the stop hurt: everyone there runs
        Vector3 from = James();
        Stop s = p.stop;
        if (s == null) { Release(p, true, from); return; }
        foreach (Person q in s.queue.ToArray()) Release(q, true, from);
        s.queue.Clear();
        s.nextArrival = Time.time + 60f;
        foreach (Job jb in jobs) if (jb.boarding.Remove(p)) Release(p, true, from);
    }

    /// No longer a passenger: an ordinary pedestrian from now on (running if 'panic'); removed once out of sight.
    void Release(Person p, bool panic, Vector3 from)
    {
        if (p.go == null) return;
        if (p.health != null && p.hurt != null) { p.health.Damaged -= p.hurt; p.hurt = null; }
        if (p.stop != null) p.stop.queue.Remove(p);
        p.stop = null;
        if (p.health == null || !p.health.IsDead)
        {
            if (p.agent.enabled && p.agent.isOnNavMesh) p.agent.isStopped = false;
            if (p.brain != null && !p.brain.enabled && p.go.GetComponent<LundunAngryDriver>() == null && !LundunStagger.Busy(p.go)) p.brain.enabled = true;
            if (panic && p.brain != null && p.brain.enabled) p.brain.Panic(from);
        }
        strays.Add(p.go);
    }

    /// Strays (released, or dead) are removed once they're well away and out of sight.
    void Tidy()
    {
        Camera cam = Camera.main;
        Vector3 c = cam != null ? cam.transform.position : Vector3.zero;
        for (int i = strays.Count - 1; i >= 0; i--)
        {
            GameObject g = strays[i];
            if (g == null) { strays.RemoveAt(i); continue; }
            if (Flat(g.transform.position, c) > 80f && (cam == null || !LundunView.CanSee(cam, g.transform.position))) { Destroy(g); strays.RemoveAt(i); }
        }
    }

    // ------------------------------------------------------------------ people
    Person Make(Vector3 at)
    {
        Person p;
        if (spare.Count > 0) { p = spare[spare.Count - 1]; spare.RemoveAt(spare.Count - 1); }
        else
        {
            LundunPopulation pop = LundunPopulation.Instance;
            GameObject go = pop != null ? pop.NewPerson(holder) : null;
            if (go == null) return null;
            p = new Person { go = go, agent = go.GetComponent<NavMeshAgent>(), brain = go.GetComponent<NPCBrain>(), health = go.GetComponent<Health>(), anim = go.GetComponentInChildren<Animator>(true) };
            if (p.agent == null) { Destroy(go); return null; }
        }
        if (p.brain != null) p.brain.enabled = false;           // we steer them while they're passengers
        if (!NavMesh.SamplePosition(at + Vector3.up, out NavMeshHit hit, 3f, NavMesh.AllAreas)) { spare.Add(p); return null; }
        p.go.transform.SetPositionAndRotation(hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        p.go.SetActive(true);
        if (!p.agent.enabled) p.agent.enabled = true;
        p.agent.Warp(hit.position);
        p.agent.stoppingDistance = 0.1f;
        if (p.health != null) { Person me = p; p.hurt = at2 => Hurt(me, at2); p.health.Damaged += p.hurt; }
        return p;
    }

    /// Away (got on the bus, or the stop's been cleared): hidden, kept for next time.
    void Put(Person p)
    {
        if (p.go == null) return;
        if (p.health != null && p.hurt != null) { p.health.Damaged -= p.hurt; p.hurt = null; }
        if (p.stop != null) p.stop.queue.Remove(p);
        p.stop = null;
        if (p.health != null && p.health.IsDead) { strays.Add(p.go); return; }
        p.go.SetActive(false);
        spare.Add(p);
    }

    void Clear(Stop s)
    {
        s.active = false;
        Camera cam = Camera.main;
        foreach (Person p in s.queue.ToArray())
        {
            if (p.go != null && cam != null && LundunView.CanSee(cam, p.go.transform.position)) { Release(p, false, Vector3.zero); continue; }
            Put(p);
        }
        s.queue.Clear();
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
}
