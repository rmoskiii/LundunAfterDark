using UnityEngine;

/// R3: how AI drivers drive. Shared by traffic (LundunTrafficCar) and police patrol cars (PoliceAI, on patrol).
///   Personalities, rolled when a car is spawned:
///     Careful  (about 65%)  a little under the limit, a 2-second gap, slows into every junction and gives way to
///                           anyone already there or arriving first, stops for people in the road or stepping out,
///                           brakes early and gently
///     Normal   (about 25%)  at the limit, a 1.5-second gap, rolls through quiet junctions (gives way to cars already
///                           in them), stops for people
///     Reckless (about 10%, more after midnight)  up to a third over the limit, sits right behind you, brakes late and
///                           hard, straight through junctions, only stops for people right in front, honks
///   Everyone: throttle and brakes eased in (CarController.ProportionalBrake), looks further ahead the faster they go,
///   follows a moving car at its speed, sounds the horn when stuck behind something.
public static class LundunDriver
{
    public enum Style { Careful, Normal, Reckless }

    public class Profile
    {
        public Style style;
        public float speed;            // × the road's speed
        public float headway;          // seconds behind the car in front
        public float minGap;           // metres to stop short of things
        public float brake;            // comfortable braking (m/s²)
        public float accel;            // most throttle they use (0-1)
        public float junctionSpeed;    // m/s through a junction (0 = don't slow)
        public float junctionFrom;     // start slowing this far before it (m)
        public int giveWay;            // 0 = never, 1 = to cars already in the junction, 2 = and to cars arriving first
        public bool watchPeople;       // brake for people about to step out, not only those in the road
        public float hornAfter;        // seconds stuck before they honk
    }

    static readonly Profile careful = new Profile { style = Style.Careful, speed = 0.92f, headway = 2.0f, minGap = 5f, brake = 3.5f, accel = 0.55f, junctionSpeed = 4.5f, junctionFrom = 24f, giveWay = 2, watchPeople = true, hornAfter = 5f };
    static readonly Profile normal = new Profile { style = Style.Normal, speed = 1.0f, headway = 1.4f, minGap = 3.5f, brake = 5f, accel = 0.8f, junctionSpeed = 7f, junctionFrom = 16f, giveWay = 1, watchPeople = true, hornAfter = 3f };
    static readonly Profile reckless = new Profile { style = Style.Reckless, speed = 1.3f, headway = 0.6f, minGap = 2f, brake = 7.5f, accel = 1f, junctionSpeed = 0f, junctionFrom = 0f, giveWay = 0, watchPeople = false, hornAfter = 1.2f };

    public static Profile For(Style s) { return s == Style.Careful ? careful : s == Style.Reckless ? reckless : normal; }

    /// About 65% careful, 25% normal, 10% reckless; after midnight (00:00–05:00) 18% reckless, 57% careful.
    public static Style Roll(System.Random rng, float hour)
    {
        bool late = hour >= 0f && hour < 5f;
        double r = rng.NextDouble();
        double rk = late ? 0.18 : 0.10, nm = 0.25;
        if (r < rk) return Style.Reckless;
        if (r < rk + nm) return Style.Normal;
        return Style.Careful;
    }

    /// What one car has noticed (kept between frames, so the costlier looks run a few times a second, not every frame).
    public class Senses
    {
        public float nextPeople, peopleLimit = float.MaxValue;
        public float nextJunction, waitedAtJunction, ignoreYieldUntil;
        public bool yielding;
        public float stuckFor, nextHorn;
        public AudioSource horn;
    }

    static int carMask = -1, peopleMask = -1;
    static readonly Collider[] near = new Collider[24];

    /// The fastest they should go given what's ahead: other cars and things (a cast down the lane, further the faster
    /// they go; a moving car is followed at a safe gap) and people (in the road, or stepping out for those who watch).
    /// emergency = brake as hard as possible now; blocked = it's something in front holding them up.
    public static float Ahead(Transform me, float speed, Profile p, float laneHalf, Senses s, out bool emergency, out bool blocked)
    {
        if (carMask == -1) { carMask = LayerMask.GetMask("Vehicle", "Player", "NPC"); peopleMask = LayerMask.GetMask("NPC", "Player"); }
        emergency = false; blocked = false;
        float limit = float.MaxValue;
        Vector3 fwd = me.forward;
        Vector3 front = me.position + fwd * 2.8f + Vector3.up * 0.6f;
        float look = Mathf.Min(55f, 6f + Mathf.Max(0f, speed) * (p.headway + 1.4f));
        if (Physics.SphereCast(front, 0.9f, fwd, out RaycastHit hit, look, carMask, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(me))
        {
            float d = hit.distance;
            float other = 0f;
            CarController oc = hit.collider.GetComponentInParent<CarController>();
            if (oc != null) other = Mathf.Max(0f, Vector3.Dot(oc.GetComponent<Rigidbody>() != null ? oc.GetComponent<Rigidbody>().linearVelocity : Vector3.zero, fwd));
            float room = d - p.minGap;
            // never faster than they could stop in (with the car in front still moving away), and drop back to a
            // time gap behind a moving car
            float stopIn = Mathf.Sqrt(2f * p.brake * Mathf.Max(0f, room)) + other;
            float gapFor = other + (d - (p.minGap + Mathf.Max(0f, speed) * p.headway)) * 0.6f;
            limit = Mathf.Min(limit, Mathf.Max(0f, Mathf.Min(stopIn, Mathf.Max(gapFor, room > 0f ? 0.5f : 0f))));
            blocked = limit < 1f;
            if (d < p.minGap * 0.5f && speed > 1f) emergency = true;
        }
        // people (a few times a second)
        if (Time.time >= s.nextPeople)
        {
            s.nextPeople = Time.time + 0.15f;
            s.peopleLimit = float.MaxValue;
            float len = Mathf.Min(32f, 6f + Mathf.Max(0f, speed) * 1.6f);
            float side = laneHalf + (p.watchPeople ? 1.6f : 0.3f);
            Vector3 centre = me.position + fwd * (2.5f + len * 0.5f) + Vector3.up;
            int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(side, 1.2f, len * 0.5f), near, me.rotation, peopleMask, QueryTriggerInteraction.Ignore);
            Vector3 right = me.right;
            for (int i = 0; i < n; i++)
            {
                Collider c = near[i];
                if (c == null || c.transform.IsChildOf(me)) continue;
                Health h = c.GetComponentInParent<Health>();
                if (h != null && h.IsDead) continue;
                Vector3 rel = c.transform.position - (me.position + fwd * 2.5f);
                float along = Vector3.Dot(rel, fwd), lat = Vector3.Dot(rel, right);
                if (along < 0f) continue;
                bool inPath = Mathf.Abs(lat) < laneHalf + 0.4f;
                bool steppingOut = false;
                if (!inPath && p.watchPeople)
                {
                    Vector3 v = Velocity(c);
                    float towards = -Mathf.Sign(lat) * Vector3.Dot(v, right);           // moving towards the lane
                    steppingOut = towards > 0.4f && Mathf.Abs(lat) - towards * 1.5f < laneHalf + 0.4f;
                }
                if (!inPath && !steppingOut) continue;
                float stopShort = p.style == Style.Reckless ? 1.5f : 3.5f;
                s.peopleLimit = Mathf.Min(s.peopleLimit, Mathf.Sqrt(2f * p.brake * Mathf.Max(0f, along - stopShort)));
            }
        }
        if (s.peopleLimit < limit) { limit = s.peopleLimit; blocked |= limit < 1f; }
        return limit;
    }

    static Vector3 Velocity(Collider c)
    {
        UnityEngine.AI.NavMeshAgent a = c.GetComponentInParent<UnityEngine.AI.NavMeshAgent>();
        if (a != null && a.enabled) return a.velocity;
        CharacterController cc = c.GetComponentInParent<CharacterController>();
        return cc != null ? cc.velocity : Vector3.zero;
    }

    /// Approaching a junction (node): should they hold back for someone? Careful drivers give way to anyone in it or
    /// getting there first; normal drivers to cars already in it; reckless drivers to nobody. Never waits more than 4 s
    /// (so two careful drivers can't wait for each other for ever).
    public static bool GiveWay(Transform me, float speed, Vector3 node, float toNode, Profile p, Senses s)
    {
        if (p.giveWay == 0 || Time.time < s.ignoreYieldUntil) { s.yielding = false; return false; }
        if (Time.time < s.nextJunction) return s.yielding;
        s.nextJunction = Time.time + 0.2f;
        bool yield = false;
        float myEta = toNode / Mathf.Max(1f, speed);
        Vector3 fwd = me.forward;
        foreach (CarController c in CarController.All)
        {
            if (c == null || c.transform == me) continue;
            Vector3 to = node - c.transform.position; to.y = 0f;
            float d = to.magnitude;
            if (d > 30f) continue;
            Rigidbody rb = c.GetComponent<Rigidbody>();
            Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero; v.y = 0f;
            if (v.sqrMagnitude < 0.8f) continue;                                         // parked / stopped
            if (Vector3.Angle(v, fwd) < 35f) continue;                                    // going our way (in front or behind)
            if (d < 9f) { yield = true; break; }                                          // in the junction
            if (p.giveWay >= 2 && Vector3.Dot(v, to) > 0f && d / Mathf.Max(1f, v.magnitude) < myEta + 0.5f) { yield = true; break; }   // gets there first
        }
        if (yield) s.waitedAtJunction += 0.2f; else s.waitedAtJunction = 0f;
        if (s.waitedAtJunction > 4f) { s.ignoreYieldUntil = Time.time + 3f; s.waitedAtJunction = 0f; yield = false; }
        s.yielding = yield;
        return yield;
    }

    /// Throttle (+) or brake (−) to reach 'want' smoothly; 0 when stopped and waiting (so it never creeps or reverses).
    public static float Pedal(float speed, float want, Profile p, bool emergency)
    {
        if (emergency) return speed > 0.6f ? -1f : 0f;
        if (want < 0.3f && speed < 0.6f) return 0f;                                       // wait, don't creep
        if (speed > want + 0.3f)
        {
            if (speed < 0.6f) return 0f;                                                  // a brake press this slow would reverse
            float decel = Mathf.Clamp((speed - want) * 1.6f, p.brake * 0.4f, 20f);
            return -Mathf.Clamp(decel / 20f, 0.08f, 1f);                                  // CarController's brakes: 20 m/s² flat out
        }
        if (speed < want - 0.3f) return Mathf.Clamp((want - speed) / 3f, 0.12f, 1f) * p.accel;
        return 0.15f;                                                                      // hold the speed
    }

    /// A blast on the horn when they've been held up a while (and, for reckless drivers, sooner).
    public static void Horn(Transform me, float speed, bool blocked, Profile p, Senses s)
    {
        if (blocked && speed < 1.5f) s.stuckFor += Time.deltaTime; else s.stuckFor = 0f;
        if (s.stuckFor < p.hornAfter || Time.time < s.nextHorn) return;
        s.nextHorn = Time.time + Random.Range(5f, 9f);
        s.stuckFor = 0f;
        if (s.horn == null)
        {
            s.horn = me.gameObject.AddComponent<AudioSource>();
            s.horn.playOnAwake = false; s.horn.spatialBlend = 1f; s.horn.minDistance = 6f; s.horn.maxDistance = 70f;
            s.horn.dopplerLevel = 0f;
        }
        s.horn.pitch = Random.Range(0.92f, 1.08f);
        s.horn.PlayOneShot(HornClip(), p.style == Style.Reckless ? 0.85f : 0.6f);
    }

    static AudioClip hornClip;
    /// A short two-tone car horn, made once (no audio file needed).
    static AudioClip HornClip()
    {
        if (hornClip != null) return hornClip;
        const int rate = 22050; int n = (int)(rate * 0.42f);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Clamp01(t / 0.015f) * Mathf.Clamp01((0.42f - t) / 0.05f);
            float a = Mathf.Sin(2f * Mathf.PI * 415f * t), b = Mathf.Sin(2f * Mathf.PI * 523f * t);
            float sq = Mathf.Clamp((a + b) * 1.6f, -1f, 1f);                               // a bit of bite
            d[i] = sq * 0.45f * env;
        }
        hornClip = AudioClip.Create("CarHorn", n, 1, rate, false);
        hornClip.SetData(d, 0);
        return hornClip;
    }

    /// Where in the road a car keeps (UK: the left lane), the same rules traffic has always used.
    public static float LaneOffset(double width, bool narrowHighStreet, bool bus)
    {
        bool baysStreet = width > 9.5 && width < 10.5;      // L3: 10 m streets have parked cars along the kerb: keep clear of them
        return narrowHighStreet ? 1.6f
             : baysStreet ? (float)(width * 0.25) - 0.35f
             : bus ? Mathf.Max((float)(width * 0.25), (float)(width * 0.5) - 1.8f)
             : (float)(width * 0.25);
    }
}
