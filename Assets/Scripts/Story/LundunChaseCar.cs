using System.Collections.Generic;
using UnityEngine;

/// S4: an enemy crew's car after James in a mission (the Rye Lane Riders in 06). Made with LundunPopulation.NewCar and
/// given this; nobody is seen inside (as with every car), but its gunmen are heard and seen shooting from the windows.
///   driving   along the roads to him (the road graph's route, planned again every second); within 40 m, on his tail
///             and ramming now and then (alongside, then into his back wheel); a look-ahead brakes for things in the
///             way, whiskers steer off walls, and a wedged car backs up and tries again
///   shooting  within 32 m with a clear line: a shot every second or so from the window (the police's hit rules: it
///             hurts his car, or him on foot), with a flash, a tracer and the sound
///   done      wrecked (shot up or smashed: its own VehicleHealth), or lost: James 220 m away, or 140 m away and out of
///             its sight for five seconds. Lost cars go once nobody's looking
public class LundunChaseCar : MonoBehaviour
{
    public static readonly List<LundunChaseCar> Active = new List<LundunChaseCar>();
    /// A passenger's hit on one of these cars (LundunCrew's drive-bys).
    public const float HitDamage = 9f;

    private CarController car;
    private Rigidbody body;
    private VehicleHealth vh;
    private VehicleSystem vehicles;
    private readonly List<Vector3> route = new List<Vector3>();
    private int routeAt;
    private float nextRoute, nextShot, unseenFor, reverseFor, stillFor, ramUntil, damage = 1f;
    private float lastSteer;
    private LineRenderer tracer;
    private float tracerOff;
    private Transform muzzle;
    private static Material tracerMat;
    private static int bodyMask = -1, wallMask = -1;
    private static readonly RaycastHit[] hits = new RaycastHit[8];
    private static readonly LundunDriver.Profile driver = LundunDriver.For(LundunDriver.Style.Reckless);

    public bool Wrecked { get { return vh != null && vh.IsWrecked; } }
    public bool Lost { get; private set; }
    public VehicleHealth Health { get { return vh; } }

    public void Begin(float damageScale)
    {
        damage = damageScale;
        car = GetComponent<CarController>();
        body = GetComponent<Rigidbody>();
        vh = GetComponent<VehicleHealth>();
        vehicles = FindAnyObjectByType<VehicleSystem>();
        nextShot = Time.time + 3f;
        if (!Active.Contains(this)) Active.Add(this);
    }

    void OnDestroy() { Active.Remove(this); }

    /// All of them, gone (a mission's over): those in view go once nobody's looking.
    public static void Clear()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            LundunChaseCar c = Active[i];
            if (c == null) { Active.RemoveAt(i); continue; }
            c.enabled = false;
            LundunMissions.LetGo(c.gameObject);
        }
        Active.Clear();
    }

    void Update()
    {
        if (car == null || vehicles == null) return;
        if (tracer != null && tracer.enabled && Time.time > tracerOff) tracer.enabled = false;
        if (Wrecked || Lost) { car.SetTouchInput(0f, 0f); return; }
        Transform james = vehicles.PlayerTarget;
        if (james == null) return;
        Vector3 me = transform.position, jp = james.position;
        float dist = Flat(jp - me);

        // lost him?
        bool sees = dist < 140f && !Physics.Linecast(me + Vector3.up * 1.3f, jp + Vector3.up, Walls(), QueryTriggerInteraction.Ignore);
        unseenFor = sees ? 0f : unseenFor + Time.deltaTime;
        if (dist > 220f || (dist > 140f && unseenFor > 5f)) { Lost = true; LundunMissions.Toast("Lost one of them"); return; }

        Drive(james, dist);
        if (dist < 32f && sees && Time.time >= nextShot) Shoot(james);
    }

    // ------------------------------------------------------------------ driving
    void Drive(Transform james, float dist)
    {
        if (reverseFor > 0f) { reverseFor -= Time.deltaTime; car.SetTouchInput(-1f, -lastSteer); return; }
        float speed = car.Speed;
        Vector3 jp = james.position;
        CarController jc = james.GetComponent<CarController>();
        Rigidbody jrb = jc != null ? jc.GetComponent<Rigidbody>() : null;
        Vector3 jv = jrb != null ? jrb.linearVelocity : Vector3.zero; jv.y = 0f;
        float want; Vector3 aim; bool ramming = false;
        if (dist < 40f)
        {
            // on his tail; now and then alongside and into his back wheel
            Vector3 jf = james.forward; jf.y = 0f; jf = jf.sqrMagnitude > 0.01f ? jf.normalized : transform.forward;
            float jSpeed = Mathf.Max(0f, Vector3.Dot(jv, jf));
            if (jc != null && Time.time < ramUntil) { aim = jp - jf * 1.2f + jv * 0.3f; want = jSpeed + 5f; ramming = true; }
            else
            {
                aim = jp + jv * Mathf.Clamp(dist / 15f, 0.2f, 1.2f);
                want = Mathf.Clamp(jSpeed + (dist - 9f) * 0.8f, 0f, 26f);
                if (jc != null && dist < 14f && Random.value < Time.deltaTime * 0.25f) ramUntil = Time.time + 2.5f;
            }
            if (jc == null) want = Mathf.Min(want, Mathf.Sqrt(2f * 6f * Mathf.Max(0f, dist - 7f)) + 1f);   // on foot: pull up near him
        }
        else
        {
            aim = RoutePoint(jp, out want);
        }
        float steer = SteerTo(aim);
        Go(want, steer, ramming ? james : null);
    }

    Vector3 RoutePoint(Vector3 goal, out float want)
    {
        want = 22f;
        Vector3 pos = transform.position; pos.y = 0f; goal.y = 0f;
        LundunNavigation nav = LundunNavigation.Instance;
        LundunRoadGraph g = nav != null ? nav.Graph : null;
        if (g == null) return goal;
        if (Time.time >= nextRoute)
        {
            nextRoute = Time.time + 1f;
            route.Clear(); routeAt = 0;
            double metres;
            foreach (NavPoint p in g.Route(new NavPoint(pos.x, pos.z), new NavPoint(goal.x, goal.z), out metres)) route.Add(new Vector3((float)p.x, 0f, (float)p.z));
        }
        if (route.Count < 2) return goal;
        while (routeAt < route.Count - 2)
        {
            Vector3 a = route[routeAt], d = route[routeAt + 1] - a;
            float len = d.magnitude;
            if (len < 0.5f || Vector3.Dot(pos - a, d / len) > len - 2.5f) routeAt++; else break;
        }
        Vector3 A = route[routeAt], seg = route[routeAt + 1] - A;
        float segLen = seg.magnitude;
        Vector3 dir = segLen > 0.01f ? seg / segLen : transform.forward;
        float s = Mathf.Clamp(Vector3.Dot(pos - A, dir), 0f, segLen);
        float remain = s + 7f + Mathf.Max(0f, car.Speed) * 0.45f;
        Vector3 aim = route[route.Count - 1];
        for (int i = routeAt; i < route.Count - 1; i++)
        {
            Vector3 a = route[i], d = route[i + 1] - a;
            float l = d.magnitude;
            if (remain <= l || i == route.Count - 2)
            {
                Vector3 u = l > 0.01f ? d / l : dir;
                aim = a + u * Mathf.Min(remain, l) + new Vector3(-u.z, 0f, u.x) * 1.4f;
                break;
            }
            remain -= l;
        }
        // slower into sharp bends
        float toNode = segLen - s;
        if (routeAt + 2 < route.Count && toNode < 40f)
        {
            float ang = Vector3.Angle(route[routeAt + 1] - route[routeAt], route[routeAt + 2] - route[routeAt + 1]);
            if (ang > 25f) want = Mathf.Min(want, Mathf.Lerp(13f, 7f, Mathf.InverseLerp(25f, 100f, ang)) + Mathf.Sqrt(2f * 6f * Mathf.Max(0f, toNode - 4f)));
        }
        return aim;
    }

    float SteerTo(Vector3 p)
    {
        Vector3 local = transform.InverseTransformPoint(new Vector3(p.x, transform.position.y, p.z));
        return Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg / 25f, -1f, 1f);
    }

    /// The look-ahead (brake for what's in the way, except the car being rammed), whiskers off walls, and backing up
    /// when wedged.
    void Go(float want, float steer, Transform ignore)
    {
        if (bodyMask == -1) bodyMask = LayerMask.GetMask("Vehicle", "NPC", "Player");
        float speed = car.Speed;
        Vector3 castDir = Quaternion.Euler(0f, steer * 18f, 0f) * transform.forward;
        float look = Mathf.Min(40f, 6f + Mathf.Max(0f, speed) * 1.4f);
        int n = Physics.SphereCastNonAlloc(transform.position + transform.forward * 2.4f + Vector3.up * 0.6f, 0.8f, castDir, hits, look, bodyMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue; Rigidbody other = null;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider == null || h.transform.IsChildOf(transform) || (ignore != null && h.transform.IsChildOf(ignore))) continue;
            if (h.distance < nearest) { nearest = h.distance; other = h.collider.attachedRigidbody; }
        }
        bool emergency = false;
        if (nearest < float.MaxValue)
        {
            float o = other != null ? Mathf.Max(0f, Vector3.Dot(other.linearVelocity, castDir)) : 0f;
            want = Mathf.Min(want, Mathf.Sqrt(2f * driver.brake * Mathf.Max(0f, nearest - 2.5f)) + o);
            if (nearest < 1.5f && speed > 1f) emergency = true;
        }
        float wl = 3.5f + Mathf.Max(0f, speed) * 0.35f;
        Vector3 o0 = transform.position + Vector3.up * 0.7f + transform.forward * 2f, side = transform.right * 0.8f;
        if (Physics.Raycast(o0 - side, Quaternion.Euler(0f, -22f, 0f) * transform.forward, out RaycastHit hl, wl, Walls(), QueryTriggerInteraction.Ignore)) steer += 0.6f * (1f - hl.distance / wl);
        if (Physics.Raycast(o0 + side, Quaternion.Euler(0f, 22f, 0f) * transform.forward, out RaycastHit hr, wl, Walls(), QueryTriggerInteraction.Ignore)) steer -= 0.6f * (1f - hr.distance / wl);
        steer = Mathf.Clamp(steer, -1f, 1f);
        if (want > 1.5f && Mathf.Abs(speed) < 0.8f && !emergency) stillFor += Time.deltaTime; else stillFor = 0f;
        if (stillFor > 2f) { stillFor = 0f; reverseFor = 1.3f; lastSteer = steer; return; }
        lastSteer = steer;
        car.SetTouchInput(LundunDriver.Pedal(speed, want, driver, emergency), steer);
    }

    // ------------------------------------------------------------------ shooting
    void Shoot(Transform james)
    {
        nextShot = Time.time + Random.Range(0.8f, 1.4f);
        bool left = Vector3.Dot(james.position - transform.position, transform.right) < 0f;
        Vector3 from = transform.position + Vector3.up * 1.3f + transform.right * (left ? -1f : 1f) + transform.forward * 0.2f;
        float d = Flat(james.position - transform.position);
        Vector3 end = PoliceAim.Fire(transform, from, james, Mathf.Lerp(0.45f, 0.2f, d / 32f), 50f, 6f * damage, 4f * damage);
        if (tracer == null)
        {
            GameObject g = new GameObject("ChaseTracer");
            g.transform.SetParent(transform, false);
            tracer = g.AddComponent<LineRenderer>();
            tracer.positionCount = 2; tracer.startWidth = 0.03f; tracer.endWidth = 0.015f;
            if (tracerMat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit");
                tracerMat = new Material(s != null ? s : Shader.Find("Unlit/Color"));
                if (tracerMat.HasProperty("_BaseColor")) tracerMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.5f));
            }
            tracer.sharedMaterial = tracerMat;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            muzzle = new GameObject("ChaseMuzzle").transform; muzzle.SetParent(transform, false);
        }
        tracer.SetPosition(0, from); tracer.SetPosition(1, end);
        tracer.enabled = true; tracerOff = Time.time + 0.06f;
        muzzle.position = from; muzzle.rotation = Quaternion.LookRotation(end - from);
        LundunCombatFX.Muzzle(muzzle, 0.8f, 1);
        AudioClip shot = LundunGunAudio.Shot(LundunWeapons.Kind.Pistol);
        if (shot != null) AudioSource.PlayClipAtPoint(shot, from, 0.8f);
    }

    static int Walls() { if (wallMask == -1) wallMask = LayerMask.GetMask("Default", "Smash"); return wallMask; }
    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
}
