using System;
using System.Collections.Generic;
using UnityEngine;

/// V4a: a car's lamps. Lundun → 10 finds them on each model (the head, tail, reversing and indicator parts of the lamp
/// texture, or lamp shapes it adds where a model has none) and lists them here; this drives them:
///   headlights   dipped glow in the day, full at night, with a pool of light on the road ahead (James's car: a real
///                light too)
///   tail lights  glow at night; brake lights bright whenever the car brakes
///   reversing    white while reversing
///   indicators   amber, blinking: traffic and buses signal at junctions (LundunTrafficCar), James's car on tight turns
///   broken       a lamp hit in a crash goes dark for good (LundunCarDamage), until the car is repaired
/// Parked and abandoned cars are dark. Far away, nothing changes (it's checked every few frames).
[DisallowMultipleComponent]
public class LundunCarLamps : MonoBehaviour
{
    public enum Kind { Head, Tail, Reverse, IndicatorLeft, IndicatorRight }

    [Serializable]
    public class Lamp
    {
        public Kind kind;
        public Renderer renderer;
        public int slot;
        [Tooltip("Where it is on the car (car space), for crash damage")]
        public Vector3 local;
        [NonSerialized] public Material mat;
        [NonSerialized] public bool broken;
        [NonSerialized] public float shown = -1f;
    }

    [SerializeField] private Lamp[] lamps = new Lamp[0];
    [Tooltip("The road under the car (car space y), the front of the car (z) and the headlights' height (y)")]
    [SerializeField] private float groundY, noseZ = 2.2f, headY = 0.7f;
    [SerializeField] private float width = 1.8f;
    [Tooltip("The soft pool of light on the road in front at night (made by Lundun → 10)")]
    [SerializeField] private Material poolMaterial;

    static readonly Color HeadCol = new Color(1f, 0.95f, 0.85f), TailCol = new Color(1f, 0.06f, 0.03f),
                          RevCol = new Color(1f, 1f, 1f), IndCol = new Color(1f, 0.5f, 0.04f);

    private CarController car;
    private LundunTrafficCar traffic;
    private PoliceAI police;
    private VehicleHealth health;
    private GameObject pool;
    private Light beam;
    private int indicate;
    private float indicateUntil, jamesTurnSince = -1f, nextFar;
    private bool near = true;
    private static Transform cam;

    public void Setup(Lamp[] list, float ground, float nose, float headHeight, float carWidth, Material poolMat)
    {
        lamps = list; groundY = ground; noseZ = nose; headY = headHeight; width = carWidth; poolMaterial = poolMat;
    }

    /// Signal a turn: −1 left, +1 right, 0 off. Keeps blinking for 'hold' seconds after the last call.
    public void Indicate(int dir, float hold = 1.5f)
    {
        indicate = dir;
        indicateUntil = dir != 0 ? Time.time + hold : 0f;
    }

    /// A crash at this point (car space): lamps within 'radius' break.
    public void BreakNear(Vector3 local, float radius)
    {
        foreach (Lamp l in lamps)
        {
            if (l.broken || (l.local - local).sqrMagnitude > radius * radius) continue;
            l.broken = true;
            if (l.mat != null)
            {
                l.mat.SetColor("_EmissionColor", Color.black);
                l.mat.SetColor("_BaseColor", l.mat.GetColor("_BaseColor") * 0.25f);
            }
            l.shown = 0f;
        }
    }

    /// Good as new (VehicleHealth.Repair, or a car reused by the traffic).
    public void Repair()
    {
        foreach (Lamp l in lamps) { l.broken = false; l.shown = -1f; }
        Bind();
    }

    void Awake()
    {
        car = GetComponent<CarController>();
        traffic = GetComponent<LundunTrafficCar>();
        police = GetComponent<PoliceAI>();
        health = GetComponent<VehicleHealth>();
    }

    void OnEnable() { Bind(); }

    void OnDisable()
    {
        // switched off (a wreck): everything dark
        foreach (Lamp l in lamps) if (l.mat != null) l.mat.SetColor("_EmissionColor", Color.black);
        if (pool != null) pool.SetActive(false);
        if (beam != null) beam.enabled = false;
    }

    /// Each lamp gets its own copy of its material (so one car's brake lights don't light every car's), put in its
    /// renderer. Done again after anything swapped the materials (the wreck's burnt look makes copies of its own).
    void Bind()
    {
        Dictionary<Renderer, Material[]> sets = new Dictionary<Renderer, Material[]>();
        foreach (Lamp l in lamps)
        {
            if (l.renderer == null) continue;
            Material[] mats;
            if (!sets.TryGetValue(l.renderer, out mats)) { mats = l.renderer.sharedMaterials; sets[l.renderer] = mats; }
            if (l.slot < 0 || l.slot >= mats.Length || mats[l.slot] == null) continue;
            if (l.mat == null || mats[l.slot] != l.mat)
            {
                l.mat = new Material(mats[l.slot]) { name = mats[l.slot].name + "_lamp" };
                l.mat.EnableKeyword("_EMISSION");
                mats[l.slot] = l.mat;
            }
            l.shown = -1f;
        }
        foreach (var kv in sets) kv.Key.sharedMaterials = kv.Value;
    }

    void Update()
    {
        if (car == null || lamps.Length == 0) return;
        if (Time.time >= nextFar)
        {
            nextFar = Time.time + 0.4f;
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            near = car.IsDriven || cam == null || (cam.position - transform.position).sqrMagnitude < 140f * 140f;
        }
        if (!near) return;

        bool running = car.IsDriven || police != null || (traffic != null && traffic.HasDriver && traffic.CurrentMode == LundunTrafficCar.Mode.Driving);
        if (health != null && health.IsWrecked) running = false;
        float night = Night();

        // James's car: indicate on a tight turn at town speeds
        if (car.IsDriven)
        {
            float lockShare = Mathf.Abs(car.SteerAngle) / 20f;
            if (lockShare > 0.8f && Mathf.Abs(car.Speed) < 11f && Mathf.Abs(car.Speed) > 0.5f)
            {
                if (jamesTurnSince < 0f) jamesTurnSince = Time.time;
                if (Time.time - jamesTurnSince > 0.35f) Indicate(car.SteerAngle < 0f ? -1 : 1, 0.8f);
            }
            else jamesTurnSince = -1f;
        }
        if (Time.time > indicateUntil) indicate = 0;
        bool blink = (Time.time * 1.6f) % 1f < 0.5f;
        bool reversing = car.Speed < -0.4f && car.InputNow.y < -0.05f;

        foreach (Lamp l in lamps)
        {
            if (l.mat == null || l.broken) continue;
            float k; Color c;
            switch (l.kind)
            {
                case Kind.Head: c = HeadCol; k = running ? Mathf.Lerp(0.5f, 2.6f, night) : 0f; break;
                case Kind.Tail: c = TailCol; k = running ? (car.Braking ? 3.2f : Mathf.Lerp(0f, 1.1f, night)) : 0f; break;
                case Kind.Reverse: c = RevCol; k = running && reversing ? 2.2f : 0f; break;
                case Kind.IndicatorLeft: c = IndCol; k = running && indicate < 0 && blink ? 3f : 0f; break;
                default: c = IndCol; k = running && indicate > 0 && blink ? 3f : 0f; break;
            }
            if (Mathf.Abs(k - l.shown) < 0.02f) continue;
            l.shown = k;
            l.mat.SetColor("_EmissionColor", c * k);
        }

        // the light on the road: a soft pool in front at night, and a real light for James's car
        bool headsOn = running && night > 0.5f && HasWorkingHead();
        if (headsOn && pool == null && poolMaterial != null) MakePool();
        if (pool != null && pool.activeSelf != headsOn) pool.SetActive(headsOn);
        bool beamOn = headsOn && car.IsDriven;
        if (beamOn && beam == null) MakeBeam();
        if (beam != null && beam.enabled != beamOn) beam.enabled = beamOn;
    }

    bool HasWorkingHead()
    {
        foreach (Lamp l in lamps) if (l.kind == Kind.Head && !l.broken) return true;
        return false;
    }

    /// 0 in the day, 1 at night, fading at dusk and dawn (the same hours the city's lamps use).
    public static float Night()
    {
        float h = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
        if (h >= 19.5f || h < 6.5f) return 1f;
        if (h >= 18.5f) return h - 18.5f;
        if (h < 7.5f) return 7.5f - h;
        return 0f;
    }

    void MakePool()
    {
        pool = new GameObject("HeadlightPool");
        pool.transform.SetParent(transform, false);
        pool.transform.localPosition = new Vector3(0f, groundY + 0.03f, noseZ + 5.5f);
        pool.layer = gameObject.layer;
        Mesh m = new Mesh { name = "HeadlightPool" };
        float w = Mathf.Max(3.2f, width * 2.2f), len = 10f;
        m.vertices = new[] { new Vector3(-w * 0.32f, 0f, -len / 2f), new Vector3(w * 0.32f, 0f, -len / 2f), new Vector3(w / 2f, 0f, len / 2f), new Vector3(-w / 2f, 0f, len / 2f) };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateNormals(); m.RecalculateBounds();
        pool.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer r = pool.AddComponent<MeshRenderer>();
        r.sharedMaterial = poolMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    void MakeBeam()
    {
        GameObject g = new GameObject("Headlights");
        g.transform.SetParent(transform, false);
        g.transform.localPosition = new Vector3(0f, headY, noseZ - 0.1f);
        g.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
        beam = g.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.color = HeadCol;
        beam.range = 32f;
        beam.spotAngle = 70f;
        beam.intensity = 6f;
        beam.shadows = LightShadows.None;
        beam.renderMode = LightRenderMode.ForcePixel;
    }
}
