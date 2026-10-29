using System.Collections.Generic;
using UnityEngine;

/// V4a: crashes leave their mark, a bit more than real life (GTA-style):
///   dents     the bodywork is pushed in where it was hit, deeper and wider the harder the hit, crumpled a little so it
///             doesn't look like clay; repeated hits keep caving it in, up to a limit
///   lamps     a head or tail light near the hit smashes (LundunCarLamps)
///   glass     a hard hit crazes the windows white
///   bits      sparks and shards fly from the hit; a crunch for the cars around James (his own car has CarCrash)
/// Bumping people, the kerb or the road does nothing. The parts that dent and the glass are listed by Lundun → 10.
/// Repair() (VehicleHealth.Repair, or a car reused by the traffic) puts the original shape back.
[DisallowMultipleComponent]
public class LundunCarDamage : MonoBehaviour
{
    [System.Serializable]
    public class Pane { public Renderer renderer; public int slot; }

    [Tooltip("The body parts that dent")]
    [SerializeField] private MeshFilter[] panels = new MeshFilter[0];
    [SerializeField] private Pane[] glass = new Pane[0];
    [SerializeField] private Material smashedGlass;
    [SerializeField] private AudioClip crunch;

    [Header("How much (a bit more than real life)")]
    [Tooltip("Hits slower than this (m/s, into the car) leave no mark")]
    [SerializeField] private float minImpact = 2.5f;
    [Tooltip("How deep a dent goes per m/s of impact, and the most one hit can push in")]
    [SerializeField] private float depthPerSpeed = 0.032f, maxDepth = 0.34f;
    [Tooltip("How far a dent spreads (m), and how far any point can be pushed in total")]
    [SerializeField] private float minRadius = 0.45f, maxRadius = 1.35f, maxTotal = 0.5f;
    [Tooltip("Above this, the windows craze (m/s)")]
    [SerializeField] private float glassAt = 10f;

    class Panel { public MeshFilter mf; public Mesh original, dented; public Vector3[] rest, now; }
    private readonly List<Panel> parts = new List<Panel>();
    private Material[][] restGlass;
    private bool glassBroken;
    private float nextDent;
    private Rigidbody rb;
    private LundunCarLamps lamps;
    private bool hasCarCrash;
    private static int ignoreLayers = -1;
    private Vector3 lastVelocity, lastSpin;      // V4b: just before a hit (to drive on through what it knocked over)

    void FixedUpdate()
    {
        if (rb != null) { lastVelocity = rb.linearVelocity; lastSpin = rb.angularVelocity; }
    }
    private static Transform listener;

    public void Setup(MeshFilter[] dentable, Pane[] windows, Material smashed, AudioClip sound)
    {
        panels = dentable; glass = windows; smashedGlass = smashed; crunch = sound;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        lamps = GetComponent<LundunCarLamps>();
        hasCarCrash = GetComponent<CarCrash>() != null;
        foreach (MeshFilter mf in panels) if (mf != null && mf.sharedMesh != null) parts.Add(new Panel { mf = mf, original = mf.sharedMesh });
        if (ignoreLayers == -1) ignoreLayers = LayerMask.GetMask("Player", "NPC");
    }

    void OnCollisionEnter(Collision c)
    {
        if (c.collider == null || ((1 << c.collider.gameObject.layer) & ignoreLayers) != 0) return;
        // V4b: street furniture: knocked flying (the car drives on, a little slower) or it stops the car
        if (LundunSmash.Instance != null && c.contactCount > 0)
        {
            ContactPoint cp = c.GetContact(0);
            float closing = Mathf.Max(0f, -Vector3.Dot(lastVelocity, cp.normal));
            float keep;
            if (LundunSmash.Instance.Hit(c.collider, cp.point, lastVelocity, closing, out keep))
            {
                rb.linearVelocity = lastVelocity * keep;
                rb.angularVelocity = lastSpin;
                if (keep < 0.92f) Hit(cp.point, cp.normal, closing * (1f - keep) * 2.2f + minImpact);      // a heavy thing still leaves a mark
                else LundunImpactFX.Burst(cp.point, cp.normal, closing * 0.6f, this);
                return;
            }
        }
        if (c.collider.GetComponentInParent<Health>() != null) return;              // people
        int n = c.contactCount;
        if (n == 0) return;
        ContactPoint best = c.GetContact(0);
        for (int i = 1; i < n; i++) { ContactPoint p = c.GetContact(i); if (p.separation < best.separation) best = p; }
        Vector3 normal = best.normal;                                                  // points into this car
        if (Mathf.Abs(normal.y) > 0.75f) return;                                        // the road, a kerb top, landing
        float impact = Mathf.Abs(Vector3.Dot(c.relativeVelocity, normal));
        if (impact < minImpact) return;
        Hit(best.point, normal, impact);
    }

    /// A hit at this point (world), pushing in along 'into' (world), this hard (m/s).
    public void Hit(Vector3 point, Vector3 into, float impact)
    {
        if (Time.time < nextDent) return;
        nextDent = Time.time + 0.08f;
        float over = impact - minImpact;
        float depth = Mathf.Min(maxDepth, over * depthPerSpeed + 0.03f);
        float radius = Mathf.Clamp(minRadius + over * 0.045f, minRadius, maxRadius);
        foreach (Panel p in parts) Dent(p, point, into, depth, radius);

        if (lamps != null && impact > 4.5f) lamps.BreakNear(transform.InverseTransformPoint(point), 0.55f + over * 0.03f);
        if (!glassBroken && impact > glassAt) SmashGlass();
        LundunImpactFX.Burst(point, into, impact, this);
        if (!hasCarCrash && crunch != null && impact > 4f)
        {
            if (listener == null && Camera.main != null) listener = Camera.main.transform;
            if (listener == null || (listener.position - point).sqrMagnitude < 45f * 45f)
                AudioSource.PlayClipAtPoint(crunch, point, Mathf.Clamp01(impact / 20f));
        }
    }

    void Dent(Panel p, Vector3 point, Vector3 into, float depth, float radius)
    {
        if (p.mf == null) return;
        Transform t = p.mf.transform;
        Vector3 lp = t.InverseTransformPoint(point);
        Vector3 ld = t.InverseTransformDirection(into).normalized;
        // quick reject on the part's own box
        Bounds b = p.original.bounds; b.Expand(radius * 2f / Mathf.Max(0.001f, t.lossyScale.x));
        if (!b.Contains(lp)) return;
        if (p.dented == null)
        {
            if (!p.original.isReadable) return;                                     // (Lundun → 10 makes them readable)
            p.dented = Instantiate(p.original);
            p.dented.name = p.original.name + "_dented";
            p.dented.MarkDynamic();
            p.rest = p.original.vertices;
            p.now = (Vector3[])p.rest.Clone();
            p.mf.sharedMesh = p.dented;
        }
        // distances measured in the world, so a scaled part dents the same
        float s = Mathf.Max(0.001f, (t.lossyScale.x + t.lossyScale.y + t.lossyScale.z) / 3f);
        float r = radius / s, d = depth / s, cap = maxTotal / s;
        bool any = false;
        for (int i = 0; i < p.now.Length; i++)
        {
            Vector3 v = p.now[i];
            float dist = (v - lp).magnitude;
            if (dist >= r) continue;
            float f = 1f - dist / r; f = f * f * (3f - 2f * f);
            // crumple: the same jitter for the same spot, so seams stay closed
            Vector3 o = p.rest[i];
            float h = Mathf.Sin(o.x * 12.9898f + o.y * 78.233f + o.z * 37.719f) * 43758.5453f; h -= Mathf.Floor(h);
            Vector3 push = ld * (d * f * (0.75f + 0.5f * h));
            Vector3 moved = v + push;
            Vector3 total = moved - o;
            if (total.magnitude > cap) moved = o + total.normalized * cap;
            p.now[i] = moved;
            any = true;
        }
        if (!any) return;
        p.dented.vertices = p.now;
        p.dented.RecalculateNormals();
        p.dented.RecalculateBounds();
    }

    void SmashGlass()
    {
        if (smashedGlass == null || glass.Length == 0) return;
        glassBroken = true;
        restGlass = new Material[glass.Length][];
        for (int i = 0; i < glass.Length; i++)
        {
            Pane g = glass[i];
            if (g.renderer == null) continue;
            Material[] m = g.renderer.sharedMaterials;
            restGlass[i] = (Material[])m.Clone();
            if (g.slot >= 0 && g.slot < m.Length) { m[g.slot] = smashedGlass; g.renderer.sharedMaterials = m; }
        }
    }

    /// Good as new.
    public void Repair()
    {
        foreach (Panel p in parts)
        {
            if (p.mf != null) p.mf.sharedMesh = p.original;
            if (p.dented != null) Destroy(p.dented);
            p.dented = null; p.rest = null; p.now = null;
        }
        if (glassBroken && restGlass != null)
            for (int i = 0; i < glass.Length; i++)
                if (glass[i].renderer != null && restGlass[i] != null)
                {
                    Material[] m = glass[i].renderer.sharedMaterials;
                    if (glass[i].slot < m.Length) { m[glass[i].slot] = restGlass[i][glass[i].slot]; glass[i].renderer.sharedMaterials = m; }
                }
        glassBroken = false;
        if (lamps != null) lamps.Repair();
    }

    void OnDestroy()
    {
        foreach (Panel p in parts) if (p.dented != null) Destroy(p.dented);
    }
}

/// V4a: sparks and flying bits from a crash (one shared set of particles, moved to each hit).
public static class LundunImpactFX
{
    static ParticleSystem sparks, shards;

    public static void Burst(Vector3 point, Vector3 into, float impact, LundunCarDamage from)
    {
        if (impact < 3f) return;
        if (sparks == null) Make(from);
        if (sparks == null) return;
        Quaternion out_ = Quaternion.LookRotation(-into + Vector3.up * 0.4f);
        sparks.transform.SetPositionAndRotation(point, out_);
        shards.transform.SetPositionAndRotation(point, out_);
        sparks.Emit(Mathf.Clamp(Mathf.RoundToInt(impact * 2.5f), 6, 60));
        if (impact > 6f) shards.Emit(Mathf.Clamp(Mathf.RoundToInt((impact - 4f) * 1.2f), 3, 26));
    }

    static void Make(LundunCarDamage from)
    {
        VehicleHealth vh = from.GetComponent<VehicleHealth>();
        Material fire = vh != null ? vh.FireMaterial : null, smoke = vh != null ? vh.SmokeMaterial : null;
        if (fire == null) return;
        GameObject root = new GameObject("LundunImpactFX");

        // sparks: hot streaks that fall and die fast
        sparks = new GameObject("Sparks").AddComponent<ParticleSystem>();
        sparks.transform.SetParent(root.transform, false);
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = sparks.main;
        m.playOnAwake = false; m.loop = false; m.maxParticles = 300; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
        m.gravityModifier = 1.6f;
        var em = sparks.emission; em.enabled = false;
        var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 0.05f;
        var pr = sparks.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.04f; pr.lengthScale = 1.5f;
        pr.sharedMaterial = fire;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // shards: little bits of trim, plastic and glass that tumble and bounce
        shards = new GameObject("Shards").AddComponent<ParticleSystem>();
        shards.transform.SetParent(root.transform, false);
        shards.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m2 = shards.main;
        m2.playOnAwake = false; m2.loop = false; m2.maxParticles = 200; m2.simulationSpace = ParticleSystemSimulationSpace.World;
        m2.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        m2.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        m2.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        m2.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m2.startColor = new ParticleSystem.MinMaxGradient(new Color(0.12f, 0.12f, 0.13f), new Color(0.75f, 0.78f, 0.8f));
        m2.gravityModifier = 1.2f;
        var em2 = shards.emission; em2.enabled = false;
        var sh2 = shards.shape; sh2.shapeType = ParticleSystemShapeType.Cone; sh2.angle = 65f; sh2.radius = 0.1f;
        var col = shards.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.dampen = 0.5f; col.bounce = 0.3f;
        col.quality = ParticleSystemCollisionQuality.Low; col.collidesWith = LayerMask.GetMask("Default");
        var rot = shards.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
        var pr2 = shards.GetComponent<ParticleSystemRenderer>();
        pr2.renderMode = ParticleSystemRenderMode.Billboard;
        pr2.sharedMaterial = smoke != null ? smoke : fire;
        pr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
