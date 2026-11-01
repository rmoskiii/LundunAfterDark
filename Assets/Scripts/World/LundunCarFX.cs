using System.Collections.Generic;
using UnityEngine;

/// V1: what makes a car look and sound alive. Added to every car by CarController at start-up (no menu to run):
///   wheels   the model's wheels (anything named wheel / tyre / tire) spin with the speed and the front ones steer;
///            they're given a pivot at their centre, so it works whatever the model
///   body     leans a little: the nose dips under braking, squats pulling away, rolls in corners (only the body: the
///            "Model" or "Body" part, when it has no colliders in it)
///   engine   a note made in code (no audio file) that rises with the revs and drops through the gears; James's car
///            louder, other cars only when they're near
///   tyres    a squeal while sliding, and skid marks on the road (LundunSkids)
/// Far away or off screen, it does almost nothing.
[DisallowMultipleComponent]
public class LundunCarFX : MonoBehaviour
{
    class Wheel { public Transform pivot; public float radius; public bool front; public float spin; public int skidId = -1; }

    private CarController car;
    private Rigidbody rb;
    private LundunTrafficCar traffic;
    private PoliceAI police;
    private readonly List<Wheel> wheels = new List<Wheel>();
    private Transform body;
    private Quaternion bodyRest;
    private AudioSource engine, squeal;
    private float nextCheck;
    private bool near = true, playerCar;
    private static Transform listener;
    private static int groundMask = -1;

    void Start()
    {
        car = GetComponent<CarController>();
        rb = GetComponent<Rigidbody>();
        traffic = GetComponent<LundunTrafficCar>();
        police = GetComponent<PoliceAI>();
        FindWheels();
        FindBody();
    }

    // ------------------------------------------------------------------ set-up
    void FindWheels()
    {
        // the top-most part named like a wheel (a tyre with its rim inside counts once)
        List<Transform> found = new List<Transform>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            Transform t = r.transform, top = null;
            for (Transform p = t; p != null && p != transform; p = p.parent) if (IsWheelName(p.name)) top = p;
            if (top != null && !found.Contains(top)) found.Add(top);
        }
        if (found.Count == 0) return;
        float midZ = 0f;
        List<Bounds> bs = new List<Bounds>();
        foreach (Transform w in found)
        {
            Bounds b = new Bounds(w.position, Vector3.zero); bool any = false;
            foreach (Renderer r in w.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            bs.Add(b);
            midZ += transform.InverseTransformPoint(b.center).z;
        }
        midZ /= found.Count;
        for (int i = 0; i < found.Count; i++)
        {
            GameObject pivot = new GameObject("WheelPivot_" + found[i].name);
            pivot.transform.SetParent(transform, false);                   // on the car itself, so the body can lean without them
            pivot.transform.SetPositionAndRotation(bs[i].center, transform.rotation);
            found[i].SetParent(pivot.transform, true);
            Vector3 local = transform.InverseTransformPoint(bs[i].center);
            wheels.Add(new Wheel { pivot = pivot.transform, radius = Mathf.Max(0.15f, bs[i].extents.y), front = local.z > midZ + 0.1f });
        }
    }

    static bool IsWheelName(string n)
    {
        string s = n.ToLowerInvariant();
        if (s.Contains("pivot") || s.Contains("steering")) return false;
        return s.Contains("wheel") || s.Contains("tyre") || s.Contains("tire");
    }

    void FindBody()
    {
        foreach (string n in new[] { "Model", "Body" })
        {
            Transform t = transform.Find(n);
            if (t == null || t.GetComponentInChildren<Collider>(true) != null) continue;
            body = t; bodyRest = t.localRotation;
            return;
        }
    }

    // ------------------------------------------------------------------ every frame
    void Update()
    {
        if (car == null || rb == null) return;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            if (listener == null && Camera.main != null) listener = Camera.main.transform;     // the listener rides on the camera
            float d = listener != null ? Vector3.Distance(listener.position, transform.position) : 0f;
            playerCar = car.IsDriven;
            near = playerCar || d < 80f;
            // the engine only runs with someone at the wheel (not parked, abandoned or stolen-and-left cars)
            bool running = playerCar || police != null || (traffic != null && traffic.HasDriver && traffic.CurrentMode == LundunTrafficCar.Mode.Driving);
            Sounds(running && (playerCar || d < 32f));
        }
        if (!near) return;
        float speed = car.Speed;
        float dt = Time.deltaTime;

        foreach (Wheel w in wheels)
        {
            if (w.pivot == null) continue;
            w.spin = Mathf.Repeat(w.spin + speed / w.radius * Mathf.Rad2Deg * dt, 360f);
            float steer = w.front ? car.SteerAngle : 0f;
            w.pivot.rotation = transform.rotation * Quaternion.Euler(0f, steer, 0f) * Quaternion.Euler(w.spin, 0f, 0f);
        }

        if (body != null)
        {
            Vector2 a = car.BodyAccel;
            float pitch = Mathf.Clamp(-a.x * 0.28f, -2.6f, 2.6f);              // brake = nose down, accelerate = squat
            float roll = Mathf.Clamp(a.y * 0.22f, -3.2f, 3.2f);                // leans out of the corner
            body.localRotation = Quaternion.Slerp(body.localRotation, bodyRest * Quaternion.Euler(pitch, 0f, roll), 8f * dt);
        }

        Engine(speed);
        Skids(speed);
    }

    // ------------------------------------------------------------------ sound
    void Sounds(bool on)
    {
        if (on && engine == null)
        {
            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = LundunCarSounds.Engine; engine.loop = true; engine.spatialBlend = 1f;
            engine.minDistance = 4f; engine.maxDistance = 45f; engine.dopplerLevel = 0f; engine.volume = 0f;
            engine.time = Random.Range(0f, engine.clip.length * 0.9f);
            engine.Play();
            squeal = gameObject.AddComponent<AudioSource>();
            squeal.clip = LundunCarSounds.Squeal; squeal.loop = true; squeal.spatialBlend = 1f;
            squeal.minDistance = 4f; squeal.maxDistance = 50f; squeal.dopplerLevel = 0f; squeal.volume = 0f;
            squeal.Play();
        }
        if (engine != null && engine.enabled != on) { engine.enabled = on; squeal.enabled = on; if (on) { engine.Play(); squeal.Play(); } }
    }

    // gears: the speed (m/s) each one tops out at, as a share of the car's top speed
    static readonly float[] Gears = { 0.16f, 0.3f, 0.46f, 0.64f, 0.82f, 1.05f };

    void Engine(float speed)
    {
        if (engine == null || !engine.enabled) return;
        float top = Mathf.Max(10f, car.TopSpeed);
        float v = Mathf.Abs(speed) / top;
        float lo = 0f, rev = 0f;
        foreach (float g in Gears)
        {
            if (v <= g) { rev = Mathf.InverseLerp(lo, g, v); break; }
            lo = g; rev = 1f;
        }
        float rpm = Mathf.Lerp(0.18f, 1f, rev);
        if (Mathf.Abs(speed) < 0.5f) rpm = 0.18f + car.ThrottleNow * 0.35f;              // idling (blipping)
        float pitch = Mathf.Lerp(0.55f, 1.9f, rpm);
        engine.pitch = Mathf.Lerp(engine.pitch, pitch, 10f * Time.deltaTime);
        float vol = (playerCar ? 0.32f : 0.16f) * (0.55f + 0.45f * car.ThrottleNow) * (0.8f + 0.2f * rpm);
        engine.volume = Mathf.Lerp(engine.volume, vol, 6f * Time.deltaTime);
        float sq = car.Slip > 0.15f && Mathf.Abs(speed) > 3f ? Mathf.Clamp01(car.Slip) * (playerCar ? 0.5f : 0.3f) : 0f;
        squeal.volume = Mathf.Lerp(squeal.volume, sq, 12f * Time.deltaTime);
        squeal.pitch = 0.9f + 0.2f * Mathf.PerlinNoise(Time.time * 2f, 0.3f);
    }

    // ------------------------------------------------------------------ skid marks
    void Skids(float speed)
    {
        bool skidding = car.Slip > 0.3f && Mathf.Abs(speed) > 3f;
        if (groundMask == -1) groundMask = LayerMask.GetMask("Default");
        foreach (Wheel w in wheels)
        {
            if (w.pivot == null) continue;
            bool marks = skidding && (!w.front || car.Slip > 0.65f || car.Braking);
            if (!marks) { if (w.skidId >= 0) LundunSkids.End(w.skidId); w.skidId = -1; continue; }
            Vector3 c = w.pivot.position;
            if (!Physics.Raycast(c, Vector3.down, out RaycastHit hit, w.radius + 0.6f, groundMask, QueryTriggerInteraction.Ignore))
            { if (w.skidId >= 0) LundunSkids.End(w.skidId); w.skidId = -1; continue; }
            w.skidId = LundunSkids.Mark(w.skidId, hit.point, hit.normal, Mathf.Clamp01(car.Slip));
        }
    }
}

/// V1: car sounds made in code once (no audio files): an engine note to loop and pitch, and a tyre squeal.
public static class LundunCarSounds
{
    static AudioClip engine, squeal;
    const int Rate = 22050;

    public static AudioClip Engine
    {
        get
        {
            if (engine != null) return engine;
            // one second of a four-cylinder-ish note at 55 Hz: firing pulses with a few harmonics and a little rumble
            int n = Rate; float[] d = new float[n];
            System.Random r = new System.Random(5);
            float noise = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 55f;
                float s = 0.55f * Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2f * t + 0.6f)
                        + 0.18f * Mathf.Sin(2f * Mathf.PI * f * 3f * t + 1.1f) + 0.1f * Mathf.Sin(2f * Mathf.PI * f * 4f * t + 2.3f);
                float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * f * 2f * t)), 6f);
                noise = noise * 0.92f + ((float)r.NextDouble() * 2f - 1f) * 0.08f;
                d[i] = Mathf.Clamp((s * 0.55f + pulse * 0.35f + noise * 0.6f) * 0.6f, -1f, 1f);
            }
            engine = AudioClip.Create("Lundun_Engine", n, 1, Rate, false);
            engine.SetData(d, 0);
            return engine;
        }
    }

    public static AudioClip Squeal
    {
        get
        {
            if (squeal != null) return squeal;
            // a wavering high squeal with a bit of hiss
            int n = Rate; float[] d = new float[n];
            System.Random r = new System.Random(9);
            double ph = 0;                                   // the wobble averages out over the second, so it loops cleanly
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                double f = 1450.0 + 90.0 * System.Math.Sin(2 * System.Math.PI * 3 * t) + 40.0 * System.Math.Sin(2 * System.Math.PI * 7 * t);
                ph += 2 * System.Math.PI * f / Rate;
                float s = (float)(System.Math.Sin(ph) * 0.5 + System.Math.Sin(ph * 1.5) * 0.2);
                d[i] = (s + ((float)r.NextDouble() * 2f - 1f) * 0.25f) * 0.45f;
            }
            squeal = AudioClip.Create("Lundun_Squeal", n, 1, Rate, false);
            squeal.SetData(d, 0);
            return squeal;
        }
    }
}
