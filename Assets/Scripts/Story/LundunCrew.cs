using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// S2: the story's people out in the world (Story mode only; added by LundunStoryUI). Each looks like themselves
/// (LundunCast) and has a name tag when you're close.
///   waiting    a mission's giver stands by its start marker (Kemi at James's door), turning to face him as he comes up
///   with James walks behind him at a sensible distance, jogging or running to keep up (and catching up out of sight
///              if they're left far behind); keeps back while a mission fight is on (they join in from S4)
///   cars       James gets into a car: they head for the passenger doors and get in (front, then the back); he gets out:
///              they get out on the pavement side. Nobody is seen inside a car, the same as James
///   talking    now and then on a drive someone says something; police, flooring it, being shot or punched by James and a
///              car coming at them all get a reaction
///   leaving    they walk off (or in through a door) and are gone once out of sight
/// They can't be killed: a hit makes them flinch, and they step out of the way of cars.
public partial class LundunCrew : MonoBehaviour
{
    public static LundunCrew Instance { get; private set; }
    /// Set while a mission fight is on: the people with James keep well back.
    public static bool HangBack;

    enum State { Waiting, Idle, Following, Boarding, InCar, Leaving }

    class Member
    {
        public string who;
        public GameObject go; public Transform t; public NavMeshAgent agent; public Animator anim; public Health health; public Collider body;
        public State state; public float stateAt, nextPath, colliderBack, lastOuch;
        public Vector3 spot; public float spotYaw;                    // waiting: where they stand
        public Vector3 leaveTo; public bool inside;                   // leaving: where to, and whether it's in through a door
        public CarController car; public Vector3 carFrom; public int seat;
        public TextMeshPro tag; public Transform head; public bool dotted;
        // S4: fighting (LundunCrewCombat)
        public LundunLook look; public float hp, maxHp, nextAttack, downedAt; public bool downed, armed;
        public Health foe; public GameObject gun; public Transform barBack, barFill; public LineRenderer tracer; public float tracerOff;
        public System.Action<float> hurtBy;
        public Collider[] colliders;                                    // James passes through these
        public System.Action<Vector3> hurt;
    }
    struct Spot { public Vector3 at; public float yaw; }

    static readonly Vector2[] Slots = { new Vector2(-1.2f, -1.7f), new Vector2(1.2f, -1.8f), new Vector2(0f, -2.8f), new Vector2(-1.8f, -3.2f) };
    static readonly string[] Seats = { "front", "back left", "back right" };

    private readonly Dictionary<string, Member> members = new Dictionary<string, Member>();
    private readonly Dictionary<string, Spot> waits = new Dictionary<string, Spot>();
    private readonly Dictionary<string, Spot> residents = new Dictionary<string, Spot>();     // S3: Uncle Femi in his shop
    private readonly Dictionary<string, Spot> staged = new Dictionary<string, Spot>();        // S3: placed by a mission
    private readonly List<string> scratch = new List<string>();
    private readonly List<Member> withJames = new List<Member>(), inCarNow = new List<Member>();
    private VehicleSystem vs;
    private LundunMinimap minimap;
    private bool wasDriving;
    private CarController lastCar;
    private float nextChat, nextScan, fastFor, lastPolice, lastFast, nextPass;
    private Transform jamesBody;
    private Collider[] jamesColliders;
    private int lastStars;
    private string lastChatWho;
    private static int vehicleMask = -1;
    private static readonly Collider[] near = new Collider[12];

    void Awake() { Instance = this; nextChat = Time.time + 25f; LundunAngryDriver.PickTarget = PickFor; }
    void OnDestroy() { if (Instance == this) Instance = null; HangBack = false; LundunAngryDriver.PickTarget = null; }
    void OnEnable() { VehicleSystem.Entering += OnEntering; VehicleSystem.Respawned += OnRespawned; }
    void OnDisable() { VehicleSystem.Entering -= OnEntering; VehicleSystem.Respawned -= OnRespawned; }

    // ------------------------------------------------------------------ for missions
    /// 'who' waits at 'at' facing 'yaw' (a mission giver by its start marker). They turn up when nobody's looking.
    public static void Wait(string who, Vector3 at, float yaw)
    {
        LundunCrew c = Instance;
        if (c == null || !LundunCast.Has(who)) return;
        c.waits[who] = new Spot { at = at, yaw = yaw };
        Member m;
        if (c.members.TryGetValue(who, out m) && (m.state == State.Idle || m.state == State.Waiting || (m.state == State.Leaving && !m.inside)))
        { m.spot = at; m.spotYaw = yaw; c.Set(m, State.Waiting); }        // S3: someone just leaving stays for the next one
    }

    /// Nobody waits by the start markers any more (a mission has started): givers it doesn't take along wander off.
    public static void ClearWaits()
    {
        LundunCrew c = Instance;
        if (c == null) return;
        c.waits.Clear();
        foreach (Member m in c.members.Values) if (m.state == State.Waiting) c.Set(m, State.Idle);
    }

    /// S3: 'who' is usually found at 'at' (Uncle Femi behind his counter). They turn up there when nobody's looking.
    public static void Resident(string who, Vector3 at, float yaw)
    {
        LundunCrew c = Instance;
        if (c == null || !LundunCast.Has(who)) return;
        Spot old;
        if (c.residents.TryGetValue(who, out old) && (old.at - at).sqrMagnitude < 0.01f) return;
        c.residents[who] = new Spot { at = at, yaw = yaw };
        Member m;
        if (c.members.TryGetValue(who, out m) && (m.state == State.Idle || m.state == State.Waiting) && !c.staged.ContainsKey(who)) { m.spot = at; m.spotYaw = yaw; c.Set(m, State.Waiting); }
    }

    /// S3: not there any more (the shop's shut): they go once nobody's looking.
    public static void NotResident(string who)
    {
        LundunCrew c = Instance;
        if (c == null || !c.residents.Remove(who)) return;
        Member m;
        if (c.members.TryGetValue(who, out m) && m.state == State.Waiting && !c.staged.ContainsKey(who) && !c.waits.ContainsKey(who)) c.Dismiss(m);
    }

    /// S3: a mission puts 'who' at 'at' facing yaw (the family in the shop). There at once if they weren't about;
    /// someone already about (with James, or waiting) walks there.
    public static void Stage(string who, Vector3 at, float yaw)
    {
        LundunCrew c = Instance;
        if (c == null || !LundunCast.Has(who)) return;
        c.staged[who] = new Spot { at = at, yaw = yaw };
        Member m;
        if (!c.members.TryGetValue(who, out m)) { m = c.Spawn(who, at, yaw); if (m == null) return; }
        if (m.state == State.InCar) c.GetOut(m);
        m.spot = at; m.spotYaw = yaw;
        c.Set(m, State.Waiting);
    }

    /// S3: everyone a mission placed goes (once nobody's looking).
    public static void ClearStaged()
    {
        LundunCrew c = Instance;
        if (c == null) return;
        c.scratch.Clear();
        foreach (string w in c.staged.Keys) c.scratch.Add(w);
        c.staged.Clear();
        foreach (string w in c.scratch)
        {
            Member m;
            if (!c.members.TryGetValue(w, out m) || m.state != State.Waiting) continue;
            Spot r;
            if (c.residents.TryGetValue(w, out r)) { m.spot = r.at; m.spotYaw = r.yaw; continue; }
            c.Dismiss(m);
        }
    }

    /// Gone the moment nobody's looking (they stay where they are till then).
    void Dismiss(Member m)
    {
        m.inside = false; m.leaveTo = m.t.position;
        Set(m, State.Leaving);
    }

    /// 'who' comes with James (the one waiting nearby if there is one, otherwise they turn up just behind him).
    public static void Join(string who)
    {
        LundunCrew c = Instance;
        if (c == null || !LundunCast.Has(who)) return;
        Member m;
        if (!c.members.TryGetValue(who, out m))
        {
            Transform j = c.James();
            if (j == null) return;
            Vector3 at = c.Behind(j, 5f);
            m = c.Spawn(who, at, j.eulerAngles.y);
            if (m == null) return;
        }
        c.staged.Remove(who);
        if (c.Driving() && c.vs.CurrentCar != null) c.Board(m, c.vs.CurrentCar, true);
        else c.Set(m, State.Following);
    }

    /// 'who' goes: to 'into' and in (a door) if given, otherwise off down the street. Gone once out of sight.
    public static void Leave(string who, Vector3? into = null)
    {
        LundunCrew c = Instance;
        Member m;
        if (c == null || !c.members.TryGetValue(who, out m)) return;
        c.SendOff(m, into);
    }

    /// Everyone with James goes (a mission's over). now = gone at once (a failed mission: the screen's about to change).
    public static void LeaveAll(bool now)
    {
        LundunCrew c = Instance;
        if (c == null) return;
        c.scratch.Clear();
        foreach (Member m in c.members.Values)
            if (m.state == State.Following || m.state == State.Boarding || m.state == State.InCar || m.state == State.Idle) c.scratch.Add(m.who);
        foreach (string w in c.scratch)
        {
            Member m = c.members[w];
            if (now) c.Despawn(m); else c.SendOff(m, null);
        }
    }

    public static bool IsWithJames(string who)
    {
        LundunCrew c = Instance; Member m;
        return c != null && c.members.TryGetValue(who, out m) && (m.state == State.Following || m.state == State.Boarding || m.state == State.InCar);
    }

    // ------------------------------------------------------------------ the people
    Member Spawn(string who, Vector3 at, float yaw)
    {
        GameObject go = LundunCast.Make(who, transform);
        if (go == null) return null;
        Vector3 p = LundunMissionRun.Ground(at);
        go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
        go.SetActive(true);
        Member m = new Member { who = who, go = go, t = go.transform, agent = go.GetComponent<NavMeshAgent>(), anim = go.GetComponentInChildren<Animator>(), health = go.GetComponent<Health>(), body = go.GetComponent<Collider>() };
        if (m.agent != null)
        {
            if (m.agent.isOnNavMesh) m.agent.Warp(p);
            m.agent.acceleration = 14f; m.agent.angularSpeed = 600f; m.agent.stoppingDistance = 0.25f; m.agent.autoBraking = true;
            m.agent.avoidancePriority = 30;
        }
        if (m.anim != null && m.anim.isHuman && m.anim.avatar != null) m.head = m.anim.GetBoneTransform(HumanBodyBones.Head);
        if (m.health != null) { Member mm = m; m.hurt = h => Hurt(mm); m.health.Damaged += m.hurt; m.hurtBy = a => HurtBy(mm, a); m.health.Hurt += m.hurtBy; }
        m.look = LundunCast.Look(who); m.maxHp = m.look != null ? m.look.health : 100f; m.hp = m.maxHp;
        m.tag = NameTag(who);
        members[who] = m;
        return m;
    }

    void Despawn(Member m)
    {
        Dot(m, false);
        CombatGone(m);
        if (m.health != null && m.hurt != null) m.health.Damaged -= m.hurt;
        if (m.health != null && m.hurtBy != null) m.health.Hurt -= m.hurtBy;
        if (m.go != null)
        {
            foreach (Renderer r in m.go.GetComponentsInChildren<Renderer>(true)) foreach (Material mat in r.materials) if (mat != null) Destroy(mat);
            Destroy(m.go);
        }
        if (m.tag != null) Destroy(m.tag.gameObject);
        members.Remove(m.who);
    }

    void Set(Member m, State s)
    {
        m.state = s; m.stateAt = Time.time; m.nextPath = 0f;
        if (s != State.InCar && m.go != null && !m.go.activeSelf) { m.go.SetActive(true); nextPass = 0f; }
        Dot(m, s == State.Following || s == State.Boarding);
    }

    /// With James: a dot in their colour on the minimap (none while they're in the car with him, or anywhere else).
    void Dot(Member m, bool on)
    {
        if (m.dotted == on || m.t == null) return;
        if (minimap == null) minimap = FindAnyObjectByType<LundunMinimap>();
        if (minimap == null) return;
        if (on) minimap.Track(m.t, LundunMissions.ColourOf(m.who), "", 13f, true); else minimap.Untrack(m.t);
        m.dotted = on;
    }

    void SendOff(Member m, Vector3? into)
    {
        if (m.state == State.InCar) GetOut(m);
        m.inside = into.HasValue;
        if (into.HasValue) m.leaveTo = into.Value;
        else
        {
            Vector3 away = m.t.position - (James() != null ? James().position : m.t.position); away.y = 0f;
            if (away.sqrMagnitude < 0.5f) away = -m.t.forward;
            m.leaveTo = m.t.position + away.normalized * 30f;
        }
        Set(m, State.Leaving);
    }

    // ------------------------------------------------------------------ every frame
    void Update()
    {
        if (vs == null) vs = FindAnyObjectByType<VehicleSystem>();
        Transform j = James();
        if (vs == null || j == null) return;
        if (vehicleMask == -1) vehicleMask = LayerMask.GetMask("Vehicle");

        bool driving = Driving();
        if (wasDriving && !driving) OnGotOut(lastCar);
        wasDriving = driving;
        if (driving) lastCar = vs.CurrentCar;

        // givers turn up at their spots when nobody's looking
        foreach (var kv in waits)
        {
            if (members.ContainsKey(kv.Key)) continue;
            float d = LundunMissionRun.FlatDistance(j.position, kv.Value.at);
            if (d > 90f) continue;
            if (d < 35f && Seen(kv.Value.at)) continue;
            Member m = Spawn(kv.Key, kv.Value.at, kv.Value.yaw);
            if (m != null) { m.spot = kv.Value.at; m.spotYaw = kv.Value.yaw; Set(m, State.Waiting); }
        }

        foreach (var kv in residents)
        {
            if (members.ContainsKey(kv.Key)) continue;
            float d = LundunMissionRun.FlatDistance(j.position, kv.Value.at);
            if (d > 90f || (d < 35f && Seen(kv.Value.at))) continue;
            Member m = Spawn(kv.Key, kv.Value.at, kv.Value.yaw);
            if (m != null) { m.spot = kv.Value.at; m.spotYaw = kv.Value.yaw; Set(m, State.Waiting); }
        }

        scratch.Clear();
        foreach (string w in members.Keys) scratch.Add(w);
        bool scan = Time.time >= nextScan;
        if (scan) nextScan = Time.time + 0.2f;
        foreach (string w in scratch)
        {
            Member m;
            if (!members.TryGetValue(w, out m)) continue;
            if (m.go == null) { members.Remove(w); if (m.tag != null) Destroy(m.tag.gameObject); continue; }
            Tick(m, j, driving, scan);
        }
        Chatter(driving);
        CombatUpdate(j);                                                    // S4: foes, the revive button
        if (Time.time >= nextPass) { nextPass = Time.time + 0.5f; PassThrough(j); }
    }

    /// James walks straight through the story's people (they never block a doorway or a path). Pedestrians, police
    /// and everyone else are as solid as ever. Unity forgets an "ignore" whenever a collider is switched off (getting in
    /// and out of cars does that), so it's set again twice a second.
    void PassThrough(Transform j)
    {
        if (j == null) return;
        if (jamesBody != j) { jamesBody = j; jamesColliders = j.GetComponentsInChildren<Collider>(true); }
        if (jamesColliders == null) return;
        foreach (Member m in members.Values)
        {
            if (m.go == null || !m.go.activeInHierarchy) continue;
            if (m.colliders == null) m.colliders = m.go.GetComponentsInChildren<Collider>(true);
            foreach (Collider a in jamesColliders)
                foreach (Collider b in m.colliders)
                    if (a != null && b != null && a.enabled && b.enabled && a.gameObject.activeInHierarchy) Physics.IgnoreCollision(a, b, true);
        }
    }

    void Tick(Member m, Transform j, bool driving, bool scan)
    {
        if (m.colliderBack > 0f && Time.time >= m.colliderBack) { m.colliderBack = 0f; if (m.body != null) m.body.enabled = true; nextPass = 0f; }
        bool busy = LundunStagger.Busy(m.go);
        switch (m.state)
        {
            case State.Waiting:
                if (!waits.ContainsKey(m.who) && !residents.ContainsKey(m.who) && !staged.ContainsKey(m.who)) { Set(m, State.Idle); break; }
                if (LundunMissionRun.FlatDistance(j.position, m.t.position) > 140f && !Seen(m.t.position)) { Despawn(m); return; }
                if (!busy) { if (LundunMissionRun.FlatDistance(m.t.position, m.spot) > 1.2f) Walk(m, m.spot, 1.6f); else Stand(m, j, 12f, m.spotYaw); }
                break;
            case State.Idle:
                if (!busy) Stand(m, j, 12f, m.t.eulerAngles.y);
                if (Time.time - m.stateAt > 2f) SendOff(m, null);       // nobody asked them along
                break;
            case State.Following:
                if (m.downed) { DownedTick(m, j); break; }                      // S4: hurt badly: waiting for James
                if (driving && vs.CurrentCar != null) { Board(m, vs.CurrentCar, false); break; }
                if (!driving && !j.gameObject.activeInHierarchy)                 // S3: on the train (or the Tube) with him
                {
                    if (LundunMissionRun.FlatDistance(m.t.position, j.position) < 40f) { m.car = null; m.seat = 0; IntoCar(m); }
                    break;
                }
                if (!busy) Follow(m, j);
                break;
            case State.Boarding:
                Boarding(m);
                break;
            case State.InCar:
                if (m.car != null && driving && vs.CurrentCar == m.car) DriveBy(m);   // S4: shooting from the window
                if (m.car == null) { if (!driving && j.gameObject.activeInHierarchy) GetOut(m); break; }   // S3: off the train with him
                if (vs.CurrentCar != m.car) GetOut(m);   // the car's gone (wrecked, or James somehow left it)
                break;
            case State.Leaving:
                Leaving(m, j);
                break;
        }
        if (!m.go.activeSelf) { if (m.tag != null) m.tag.gameObject.SetActive(false); return; }
        if (scan && !busy && m.state != State.Boarding) Dodge(m, driving);
        if (m.agent != null && m.agent.enabled) LundunAnim.Float(m.anim, "Speed", busy ? 0f : m.agent.velocity.magnitude, 0.1f);
    }

    /// Behind James (their own place in the group), at walking, jogging or running pace to keep up.
    void Follow(Member m, Transform j)
    {
        if (Combat(m, j)) return;                                           // S4: a fight on: in it
        int slot = SlotOf(m);
        Vector3 fwd = j.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector2 o = Slots[slot % Slots.Length];
        Vector3 want = HangBack ? j.position - fwd * 8f + right * (o.x * 2.5f) : j.position + right * o.x + fwd * o.y;
        float toJames = LundunMissionRun.FlatDistance(m.t.position, j.position);
        // left far behind (James ran off, rode off, went somewhere they couldn't follow): catch up out of sight
        if (toJames > 60f && !Seen(m.t.position))
        {
            Vector3 b = Behind(j, 7f);
            if (!Seen(b) || toJames > 150f) { Place(m, b, j.eulerAngles.y); return; }
        }
        float toSpot = LundunMissionRun.FlatDistance(m.t.position, want);
        float jamesSpeed = vs != null ? vs.PlayerSpeed : 0f;
        if (toSpot < 0.9f || (toJames < 2.4f && jamesSpeed < 0.5f) || (HangBack && toJames < 9f && toSpot < 3f))
        {
            Stand(m, j, 30f, m.t.eulerAngles.y);
            return;
        }
        float speed = toJames > 25f ? 7f : toJames > 9f ? 5.6f : toJames > 5f ? Mathf.Max(3.2f, jamesSpeed) : Mathf.Max(1.6f, jamesSpeed);
        Walk(m, want, speed);
    }

    int SlotOf(Member m)
    {
        int i = 0;
        foreach (Member o in members.Values)
        {
            if (o == m) return i;
            if (o.state == State.Following || o.state == State.Boarding) i++;
        }
        return i;
    }

    void Walk(Member m, Vector3 to, float speed)
    {
        if (m.agent == null || !m.agent.enabled) return;
        if (!m.agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(m.t.position, out NavMeshHit h, 4f, NavMesh.AllAreas)) m.agent.Warp(h.position);
            else return;
        }
        m.agent.isStopped = false;
        m.agent.speed = Mathf.MoveTowards(m.agent.speed, speed, 12f * Time.deltaTime);
        if (Time.time >= m.nextPath)
        {
            m.nextPath = Time.time + 0.3f;
            if (NavMesh.SamplePosition(to, out NavMeshHit hit, 3f, NavMesh.AllAreas)) m.agent.SetDestination(hit.position);
        }
    }

    /// Stopped: face James if he's within 'faceWithin' metres, otherwise turn to yaw.
    void Stand(Member m, Transform j, float faceWithin, float yaw)
    {
        if (m.agent != null && m.agent.enabled && m.agent.isOnNavMesh && m.agent.hasPath) { m.agent.ResetPath(); m.agent.velocity = Vector3.zero; }
        Vector3 to = j.position - m.t.position; to.y = 0f;
        Quaternion want = to.sqrMagnitude < faceWithin * faceWithin && to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to) : Quaternion.Euler(0f, yaw, 0f);
        m.t.rotation = Quaternion.RotateTowards(m.t.rotation, want, 240f * Time.deltaTime);
    }

    void Place(Member m, Vector3 at, float yaw)
    {
        Vector3 p = LundunMissionRun.Ground(at);
        if (m.agent != null && m.agent.enabled) { if (m.agent.isOnNavMesh) m.agent.ResetPath(); m.agent.Warp(p); }
        m.t.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
    }

    void Leaving(Member m, Transform j)
    {
        float d = LundunMissionRun.FlatDistance(m.t.position, m.leaveTo);
        bool seen = Seen(m.t.position);
        if ((m.inside && d < 1.3f) || (!seen && (d < 3f || Time.time - m.stateAt > 4f)) || Time.time - m.stateAt > 40f) { Despawn(m); return; }
        if (!LundunStagger.Busy(m.go)) Walk(m, m.leaveTo, m.inside ? 1.6f : 1.8f);
    }

    // ------------------------------------------------------------------ cars
    void OnEntering(CarController car)
    {
        foreach (Member m in members.Values)
            if (m.state == State.Following) Board(m, car, false);
    }

    /// Head for a passenger door (front first, then the back). instant = already in (joined while James was driving).
    void Board(Member m, CarController car, bool instant)
    {
        int seat = 0;
        foreach (Member o in members.Values) if (o != m && (o.state == State.Boarding || o.state == State.InCar) && o.car == car) seat++;
        if (seat >= Seats.Length) { Set(m, State.Following); return; }        // no room: they'll catch up
        m.car = car; m.seat = seat; m.carFrom = car.transform.position;
        Set(m, State.Boarding);
        if (instant) IntoCar(m);
    }

    Vector3 Door(Member m)
    {
        Transform c = m.car.transform;
        float side = m.seat == 2 ? 1f : -1f;                       // UK cars: passengers get in on the left
        float along = m.seat == 0 ? 0.2f : -0.9f;
        return c.position + c.right * (1.35f * side) + c.forward * along;
    }

    void Boarding(Member m)
    {
        if (m.car == null || vs.CurrentCar != m.car) { Set(m, State.Following); return; }
        Vector3 door = Door(m);
        float d = LundunMissionRun.FlatDistance(m.t.position, door);
        bool carMoved = LundunMissionRun.FlatDistance(m.car.transform.position, m.carFrom) > 5f;
        if (d < 1.3f || carMoved || Time.time - m.stateAt > 6f) { IntoCar(m); return; }
        if (!LundunStagger.Busy(m.go)) Walk(m, door, d > 6f ? 5.6f : 3.2f);
    }

    void IntoCar(Member m)
    {
        if (m.agent != null && m.agent.enabled && m.agent.isOnNavMesh) m.agent.ResetPath();
        m.state = State.InCar; m.stateAt = Time.time;
        Dot(m, false);
        m.go.SetActive(false);
    }

    void GetOut(Member m)
    {
        Transform c = m.car != null ? m.car.transform : (lastCar != null ? lastCar.transform : null);
        Vector3 at; float yaw;
        if (c != null)
        {
            float side = m.seat == 2 ? 1f : -1f;
            at = c.position + c.right * (1.9f * side) + c.forward * (m.seat == 0 ? 0.2f : -1.1f);
            yaw = c.eulerAngles.y;
        }
        else { Transform j = James(); at = j != null ? Behind(j, 3f) : m.t.position; yaw = j != null ? j.eulerAngles.y : 0f; }
        m.car = null;
        m.t.SetPositionAndRotation(LundunMissionRun.Ground(at), Quaternion.Euler(0f, yaw, 0f));    // there before they're switched back on
        Set(m, State.Following);
        Place(m, at, yaw);
    }

    void OnGotOut(CarController car)
    {
        nextPass = 0f;                                                      // James's collider is back: let him through again
        foreach (Member m in members.Values)
            if (m.state == State.InCar && (m.car == car || m.car == null)) GetOut(m);
            else if (m.state == State.Boarding) Set(m, State.Following);
    }

    void OnRespawned()
    {
        Transform j = James();
        if (j == null) return;
        foreach (Member m in members.Values)
            if (m.state == State.Following || m.state == State.Boarding || m.state == State.InCar)
            {
                m.car = null;
                Set(m, State.Following);
                Place(m, Behind(j, 3f + SlotOf(m)), j.eulerAngles.y);
            }
    }

    /// A car coming at them: out of its way (and a word if it's James driving).
    void Dodge(Member m, bool driving)
    {
        int n = Physics.OverlapSphereNonAlloc(m.t.position + Vector3.up, 9f, near, vehicleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Rigidbody rb = near[i].attachedRigidbody;
            if (rb == null) continue;
            Vector3 v = rb.linearVelocity; v.y = 0f;
            float speed = v.magnitude;
            if (speed < 4f) continue;
            Vector3 to = m.t.position - rb.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > 2.5f + speed * 0.45f || Vector3.Dot(v / speed, to / Mathf.Max(0.01f, dist)) < 0.6f) continue;
            // step to the side of its path, and let it through
            Vector3 side = Vector3.Cross(Vector3.up, v / speed);
            if (Vector3.Dot(side, to) < 0f) side = -side;
            if (m.agent != null && m.agent.enabled && m.agent.isOnNavMesh) m.agent.Move(side * 1.3f);
            if (m.body != null) { m.body.enabled = false; m.colliderBack = Time.time + 1.2f; }
            CarController cc = rb.GetComponent<CarController>();
            if (cc != null && cc.IsDriven && Time.time - m.lastOuch > 5f) { m.lastOuch = Time.time; LundunMissions.Say(m.who, Pick(Swerve), 2.2f); }
            return;
        }
    }

    void Hurt(Member m)
    {
        if (m.go == null || !m.go.activeInHierarchy || m.downed) return;
        if (InMission && foes.Count > 0)                                    // S4: in a fight: a flinch now and then, no words for James
        {
            if (Random.value < 0.35f) LundunStagger.Play(m.go, "Hit", 0.45f, false);
            return;
        }
        LundunStagger.Play(m.go, "Hit", 0.6f, false);
        if (Time.time - m.lastOuch > 3f) { m.lastOuch = Time.time; LundunMissions.Say(m.who, Pick(Ouch), 2.2f); }
    }

    // ------------------------------------------------------------------ talk
    void Chatter(bool driving)
    {
        List<Member> with = withJames; with.Clear();
        foreach (Member m in members.Values) if (m.state == State.Following || m.state == State.InCar || m.state == State.Boarding) with.Add(m);
        WantedSystem w = WantedSystem.Instance;
        int stars = w != null ? w.Stars : 0;
        if (with.Count == 0) { lastStars = stars; fastFor = 0f; return; }

        if (stars > lastStars && Time.time - lastPolice > 20f)
        {
            lastPolice = Time.time;
            Member m = with[Random.Range(0, with.Count)];
            LundunMissions.Say(m.who, Pick(Police), 2.6f);
            nextChat = Mathf.Max(nextChat, Time.time + 20f);
        }
        lastStars = stars;

        float speed = driving && vs.CurrentCar != null ? Mathf.Abs(vs.CurrentCar.Speed) : 0f;
        fastFor = speed > 31f ? fastFor + Time.deltaTime : 0f;                 // about 70 mph
        if (fastFor > 3f && Time.time - lastFast > 45f)
        {
            lastFast = Time.time; fastFor = 0f;
            Member m = with[Random.Range(0, with.Count)];
            if (m.state == State.InCar) LundunMissions.Say(m.who, Pick(TooFast), 2.4f);
        }

        if (!driving || Time.time < nextChat || LundunMissions.Talking || stars > 0) return;
        nextChat = Time.time + Random.Range(40f, 80f);
        List<Member> inCar = inCarNow; inCar.Clear();
        foreach (Member x in with) if (x.state == State.InCar) inCar.Add(x);
        if (inCar.Count == 0) return;
        Member who = inCar[Random.Range(0, inCar.Count)];
        if (inCar.Count > 1 && who.who == lastChatWho) who = inCar.Find(x => x.who != lastChatWho);
        string[] pool;
        if (!Banter.TryGetValue(who.who, out pool)) return;
        lastChatWho = who.who;
        LundunMissions.Say(who.who, Pick(pool), 4f);
    }

    static readonly Dictionary<string, int> lastPick = new Dictionary<string, int>();
    static string Pick(string[] pool)
    {
        if (pool.Length == 1) return pool[0];
        string key = pool[0];
        int last; lastPick.TryGetValue(key, out last);
        int i = Random.Range(0, pool.Length - 1);
        if (i >= last) i++;                                                    // never the same line twice running
        lastPick[key] = i;
        return pool[i];
    }

    static readonly string[] Ouch = { "Oi! It's me!", "James! What was that for?", "Are you mad? Watch it!", "Alright, alright! Calm down!" };
    static readonly string[] Swerve = { "Watch the road!", "Oi! You nearly hit me!", "James! Brakes!" };
    static readonly string[] Police = { "Feds! Lose them!", "Police, James. Police!", "Oh, no no no. That's the feds.", "Blue lights behind us. Go, go!" };
    static readonly string[] TooFast = { "Slow down!", "James! You'll kill us both!", "Easy, easy! This isn't a race.", "We'll get there alive or not at all, yeah?" };

    static readonly Dictionary<string, string[]> Banter = new Dictionary<string, string[]>
    {
        { "KEMI", new[] {
            "Uncle Femi's going to ask why we took so long.",
            "Your music is still terrible, by the way.",
            "Left here's quicker. Trust me.",
            "When did you last clean this car?",
            "Leah asked me where you were tonight. I said I don't know.",
            "Don't tell Marcus I was out this late." } },
        { "MARCUS", new[] {
            "You drive like Dad.",
            "Don't tell Mum about tonight.",
            "Mind the bus lane. I'm not paying that fine.",
            "You eaten? There's jollof at home.",
            "Keep your head down this week. I mean it." } },
        { "TAYO", new[] {
            "Bro, this is the best night we've had in months.",
            "Put the aux on. Please.",
            "Remember when they kicked us out of the Peckam Plex?",
            "If we see the Kings, we keep driving. Yeah?",
            "I'm starving. Chicken shop after?" } },
        { "UNCLE FEMI", new[] {
            "In Lagos, we would call this driving slowly.",
            "Indicate! The indicator is there for a reason.",
            "The shop does not open itself, James.",
            "I have yams to deliver tomorrow. Remind me.",
            "You young people. Always in a hurry." } },
        { "LEAH", new[] {
            "I'm on a twelve-hour shift tomorrow. Gently, please.",
            "You're quiet tonight. What's going on?",
            "I spend all day patching up people who drive like this.",
            "Text me when you're home safe, yeah?",
            "My mum keeps asking when you're coming for Sunday dinner." } },
    };

    // ------------------------------------------------------------------ name tags
    TextMeshPro NameTag(string who)
    {
        GameObject g = new GameObject("NameTag_" + who);
        g.transform.SetParent(transform, false);
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = Pretty(who);
        t.fontSize = 2.4f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = LundunMissions.ColourOf(who);
        t.rectTransform.sizeDelta = new Vector2(4f, 0.6f);
        g.SetActive(false);
        return t;
    }

    static string Pretty(string who)
    {
        string[] w = who.ToLowerInvariant().Split(' ');
        for (int i = 0; i < w.Length; i++) if (w[i].Length > 0) w[i] = char.ToUpperInvariant(w[i][0]) + w[i].Substring(1);
        return string.Join(" ", w);
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Bars(cam);                                                          // S4: health bars
        foreach (Member m in members.Values)
        {
            if (m.tag == null) continue;
            bool show = m.go != null && m.go.activeInHierarchy && (m.t.position - cam.transform.position).sqrMagnitude < 14f * 14f && m.state != State.Leaving;
            if (m.tag.gameObject.activeSelf != show) m.tag.gameObject.SetActive(show);
            if (!show) continue;
            Vector3 top = m.head != null ? m.head.position + Vector3.up * 0.42f : m.t.position + Vector3.up * 2.1f;
            m.tag.transform.position = top;
            m.tag.transform.rotation = Quaternion.LookRotation(top - cam.transform.position);
        }
    }

    // ------------------------------------------------------------------ helpers
    Transform James() { PlayerHealth ph = PlayerHealth.Instance; return ph != null ? ph.Body : null; }
    bool Driving() { return vs != null && vs.IsDriving; }

    Vector3 Behind(Transform j, float metres)
    {
        Vector3 f = j.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        Vector3 p = (vs != null && vs.IsDriving && vs.CurrentCar != null ? vs.CurrentCar.transform.position : j.position) - f * metres;
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas)) return hit.position;
        return p;
    }

    static bool Seen(Vector3 p) { Camera cam = Camera.main; return cam != null && LundunView.CanSee(cam, p); }
}

/// S2: a mission step: someone comes with James (they're with him from here on, getting in and out of cars with him).
public class LundunJoin : LundunSetup
{
    public readonly string who;
    public LundunJoin(string who) { this.who = who; }
    public override void Apply(LundunMissionRun run)
    {
        LundunCrew.Join(who);
        if (!run.crew.Contains(who)) run.crew.Add(who);
    }
}

/// S2: a mission step: someone goes (in through 'into' if given, a door, or off down the street).
public class LundunLeave : LundunSetup
{
    public readonly string who;
    readonly System.Func<Vector3> intoNow;
    public LundunLeave(string who) { this.who = who; }
    public LundunLeave(string who, System.Func<Vector3> door) { this.who = who; intoNow = door; }
    public override void Apply(LundunMissionRun run)
    {
        if (intoNow != null) LundunCrew.Leave(who, intoNow()); else LundunCrew.Leave(who);
        run.crew.Remove(who);
    }
}
