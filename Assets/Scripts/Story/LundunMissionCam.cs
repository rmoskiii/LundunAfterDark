using System;
using System.Collections.Generic;
using UnityEngine;

/// Phase 5 (gate B): a camera a mission puts up for itself (Hollis's yard, the palace gallery, the warehouse...), on top
/// of the city's own CCTV. Made with LundunMissionCam.Create(run, ...): it belongs to that attempt and goes when the
/// attempt ends.
///   sweeps      turns slowly from side to side (a pause at each end), or stays still
///   its view    shown on the ground as a pale cone (amber; red while it has James), so you can time a dash past it
///   seeing      the same idea as a guard's torch: in its cone, in range, nothing in between; stronger close up.
///               On its own it fills a short meter and calls Spotted (identified = it had a close, clear look); in a
///               stealth step (LundunSneak) it feeds the sneak meter instead, like a guard
///   shoot it    a hit knocks the head off its bracket: blind for good. Down(quiet) says whether it was the suppressed
///               pistol (quiet) or any other gun (loud: that gunfire is reported as usual by PlayerCombat)
///   power cut   a fuse box can switch it off for a while (SetPower) and back on again
public class LundunMissionCam : MonoBehaviour
{
    public string id = "";
    public float range = 14f;                 // metres from the lens
    public float halfAngle = 26f;             // degrees either side of where it points
    public float pitch = -28f;                // looking down
    public float sweepHalf = 40f;             // degrees either side of its middle (0 = still)
    public float sweepSeconds = 8f;           // one full side-to-side-and-back
    /// Judges for itself (its own meter → Spotted). Off when a LundunSneak step is doing the judging.
    public bool judgeSelf = true;
    /// Phase 5 (A2): it watches the road too: James driving past is seen (quicker, a car's a big thing to spot). Off by
    /// default: most cameras only care about people on foot. M04's estate cameras on the way in.
    public bool seesCars;
    /// Phase 5 (D): its cone on the ground can be hidden (M12: a camera you didn't spot from the Wheel in 11 is one you
    /// don't know the sweep of). It still sees.
    bool coneHidden;
    public void ShowCone(bool on) { coneHidden = !on; Colour(); }

    public bool Powered { get; private set; } = true;
    public bool Destroyed { get; private set; }
    public bool Working { get { return Powered && !Destroyed; } }
    /// How close it is to having seen James (0–1), when judging for itself.
    public float Meter { get; private set; }
    public bool HasSpotted { get; private set; }

    /// It saw James (identified: a close, clear look; otherwise just a figure it couldn't make out).
    public event Action<LundunMissionCam, bool> Spotted;
    /// It was shot out (quiet: by the suppressed pistol).
    public event Action<LundunMissionCam, bool> Down;

    public static readonly List<LundunMissionCam> All = new List<LundunMissionCam>();

    private float baseYaw, t0;
    private Transform head, cone;
    private Renderer led, coneR;
    private Material coneMat;
    private float lookingAt;                  // how strongly it can see James right now (0–1)
    private static Material bodyMat, lensMat, ledMat;

    // ------------------------------------------------------------------ making one
    /// A camera on a bracket at 'lens' (its height is where it hangs), looking along 'yaw' (degrees), for this attempt.
    public static LundunMissionCam Create(LundunMissionRun run, string id, Vector3 lens, float yaw, float sweepHalfDeg = 40f,
                                          float rangeM = 14f, float halfAngleDeg = 26f, float pitchDeg = -28f)
    {
        GameObject g = new GameObject("MissionCam_" + id);
        g.transform.position = lens;
        LundunMissionCam c = g.AddComponent<LundunMissionCam>();
        c.id = id; c.baseYaw = yaw; c.sweepHalf = sweepHalfDeg; c.range = rangeM; c.halfAngle = halfAngleDeg; c.pitch = pitchDeg;
        c.Build();
        if (run != null) run.Spawn(g);
        return c;
    }

    /// A camera on its own pole (a car park, an estate courtyard): the pole from the ground up to 'lensHeight'.
    public static LundunMissionCam CreateOnPole(LundunMissionRun run, string id, Vector3 ground, float lensHeight, float yaw, float sweepHalfDeg = 40f,
                                                float rangeM = 14f, float halfAngleDeg = 26f, float pitchDeg = -28f)
    {
        LundunMissionCam c = Create(run, id, ground + Vector3.up * lensHeight, yaw, sweepHalfDeg, rangeM, halfAngleDeg, pitchDeg);
        GameObject pole = Part(PrimitiveType.Cylinder, "Pole", c.transform, new Vector3(0f, -lensHeight * 0.5f, -0.38f), new Vector3(0.12f, lensHeight * 0.5f, 0.12f), bodyMat, true);
        pole.transform.localRotation = Quaternion.identity;
        return c;
    }

    /// Phase 5 (M01): one the estate got to first: the head knocked down on its bracket, sprayed over, blind for good
    /// (no cone, no light, never sees anyone). Set dressing.
    public void Vandalise(Color paint)
    {
        Destroyed = true;
        Meter = 0f; lookingAt = 0f;
        if (head != null)
        {
            head.localRotation = Quaternion.Euler(64f, 18f, -24f);               // hanging off its bracket
            Material spray = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Spray" };
            Tint(spray, paint);
            GameObject tag = Part(PrimitiveType.Sphere, "Spray", head, new Vector3(0f, 0.01f, 0.17f), new Vector3(0.2f, 0.17f, 0.06f), spray, false);
            tag.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            Part(PrimitiveType.Cube, "Drip", head, new Vector3(0.03f, -0.1f, 0.18f), new Vector3(0.025f, 0.12f, 0.02f), spray, false);
        }
        Colour();
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); t0 = Time.time; }
    void OnDisable() { All.Remove(this); }

    /// Where it's pointing right now.
    public Vector3 Forward { get { return head != null ? head.forward : transform.forward; } }
    public Vector3 Lens { get { return head != null ? head.position + head.forward * 0.22f : transform.position; } }

    // ------------------------------------------------------------------ seeing
    /// Can it see James (feet at 'james')? How strongly: 0 (no) to 1 (close).
    public float Sees(Vector3 james)
    {
        if (!Working) return 0f;
        Vector3 chest = james + Vector3.up * 1.2f;
        Vector3 v = chest - Lens;
        float d = v.magnitude;
        if (d > range || d < 0.3f) return 0f;
        if (Vector3.Angle(Forward, v) > halfAngle) return 0f;
        if (Physics.Linecast(Lens + v / d * 0.3f, chest, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore)) return 0f;
        return Mathf.Lerp(1f, 0.4f, d / range);
    }

    /// A close, clear look (identified) rather than a figure at the edge of its view.
    public bool Identifies(Vector3 james) { return Sees(james) > 0f && Vector3.Distance(james + Vector3.up * 1.2f, Lens) < range * 0.6f; }

    void Update()
    {
        // the sweep: side to side, easing at each end
        if (head != null && Working && sweepHalf > 0.5f)
        {
            float k = Mathf.Sin((Time.time - t0) * Mathf.PI * 2f / Mathf.Max(2f, sweepSeconds));
            k = Mathf.Sign(k) * Mathf.Pow(Mathf.Abs(k), 0.6f);       // lingers at the ends
            head.rotation = Quaternion.Euler(-pitch, baseYaw + k * sweepHalf, 0f);
        }
        if (cone != null && head != null) cone.rotation = Quaternion.Euler(0f, head.eulerAngles.y, 0f);

        Transform j = LundunMissionRun.James();
        bool driving = LundunMissionRun.Driving();
        lookingAt = j != null && (seesCars || !driving) ? Sees(j.position) : 0f;
        if (judgeSelf && Working && !HasSpotted)
        {
            float rate = driving ? 4.5f : 1.6f;
            Meter = lookingAt > 0f ? Meter + lookingAt * rate * Time.deltaTime : Mathf.Max(0f, Meter - 0.5f * Time.deltaTime);
            if (Meter >= 1f)
            {
                HasSpotted = true; Meter = 1f;
                bool clear = j != null && Identifies(j.position);
                LundunHaptics.Medium();
                try { Spotted?.Invoke(this, clear); } catch (Exception e) { Debug.LogException(e); }
            }
        }
        Colour();
    }

    /// Forget it ever saw him (a mission's retry of a section).
    public void ResetSpotted() { HasSpotted = false; Meter = 0f; }

    void Colour()
    {
        if (coneMat == null) return;
        bool on = Working;
        if (coneR.enabled != (on && !coneHidden)) coneR.enabled = on && !coneHidden;
        if (led != null && led.enabled != on) led.enabled = on;
        float alert = Mathf.Max(lookingAt > 0f ? 0.6f : 0f, judgeSelf ? Meter : 0f);
        Color c = Color.Lerp(new Color(1f, 0.78f, 0.25f, 0.16f), new Color(1f, 0.12f, 0.08f, 0.32f), alert);
        if (coneMat.HasProperty("_BaseColor")) coneMat.SetColor("_BaseColor", c);
        coneMat.color = c;
    }

    // ------------------------------------------------------------------ fuse boxes and bullets
    /// A fuse box: off (no view, no light) or back on.
    public void SetPower(bool on)
    {
        Powered = on;
        if (!on) { Meter = 0f; lookingAt = 0f; }
        Colour();
    }

    /// PlayerCombat: a shot hit it.
    public void Shot(Vector3 dir, bool quiet)
    {
        if (Destroyed) return;
        Destroyed = true;
        Meter = 0f; lookingAt = 0f;
        if (head != null)
        {
            head.SetParent(null, true);
            Rigidbody rb = head.gameObject.AddComponent<Rigidbody>();
            rb.mass = 2f;
            rb.AddForce(dir.normalized * 3f + Vector3.up, ForceMode.Impulse);
            rb.AddTorque(UnityEngine.Random.insideUnitSphere * 2f, ForceMode.Impulse);
            Destroy(head.gameObject, 12f);
        }
        Colour();
        LundunHaptics.Light();
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(quiet ? "Camera down, quietly" : "Camera down");
        try { Down?.Invoke(this, quiet); } catch (Exception e) { Debug.LogException(e); }
    }

    // ------------------------------------------------------------------ the prop and its cone
    void Build()
    {
        Mats();
        // bracket from the wall, then the head
        GameObject arm = Part(PrimitiveType.Cube, "Bracket", transform, new Vector3(0f, 0.05f, -0.22f), new Vector3(0.06f, 0.06f, 0.3f), bodyMat, false);
        arm.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        transform.rotation = Quaternion.Euler(0f, baseYaw, 0f);
        GameObject h = new GameObject("Head");
        h.transform.SetParent(transform, false);
        h.transform.rotation = Quaternion.Euler(-pitch, baseYaw, 0f);
        head = h.transform;
        GameObject box = Part(PrimitiveType.Cube, "Housing", head, new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.14f, 0.36f), bodyMat, true);
        Part(PrimitiveType.Cube, "Hood", head, new Vector3(0f, 0.085f, 0.04f), new Vector3(0.19f, 0.025f, 0.42f), bodyMat, false);
        GameObject lensG = Part(PrimitiveType.Cylinder, "Lens", head, new Vector3(0f, 0f, 0.185f), new Vector3(0.09f, 0.012f, 0.09f), lensMat, false);
        lensG.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject l = Part(PrimitiveType.Sphere, "Led", head, new Vector3(0.055f, 0.045f, 0.185f), new Vector3(0.022f, 0.022f, 0.022f), ledMat, false);
        led = l.GetComponent<Renderer>();
        box.layer = 0;

        // the cone: a fan on the ground under it, as far as it can see
        float groundY = transform.position.y - 4f;
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.down * 0.4f, Vector3.down, out hit, 30f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore)) groundY = hit.point.y;
        float hgt = Mathf.Max(0.5f, transform.position.y - groundY);
        float reach = Mathf.Sqrt(Mathf.Max(1f, range * range - hgt * hgt));
        GameObject cg = new GameObject("Cone");
        cg.transform.SetParent(transform, false);
        cg.transform.position = new Vector3(transform.position.x, groundY + 0.06f, transform.position.z);
        cone = cg.transform;
        cg.AddComponent<MeshFilter>().sharedMesh = Fan(Mathf.Min(reach * 0.15f, 1.2f), reach, halfAngle);
        coneR = cg.AddComponent<MeshRenderer>();
        coneMat = ConeMaterial();
        coneR.sharedMaterial = coneMat;
        coneR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; coneR.receiveShadows = false;
        Colour();
    }

    static GameObject Part(PrimitiveType t, string name, Transform parent, Vector3 at, Vector3 size, Material m, bool keepCollider)
    {
        GameObject g = GameObject.CreatePrimitive(t);
        g.name = name;
        if (!keepCollider) { Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c); }
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at; g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    /// A flat fan (inner to outer radius, ±half degrees) with soft edges (vertex alpha).
    static Mesh Fan(float inner, float outer, float half)
    {
        const int seg = 18;
        List<Vector3> v = new List<Vector3>(); List<Color> c = new List<Color>(); List<int> tri = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = Mathf.Lerp(-half, half, i / (float)seg) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            float edge = (i == 0 || i == seg) ? 0.35f : 1f;
            v.Add(dir * inner); c.Add(new Color(1f, 1f, 1f, 0.9f * edge));
            v.Add(dir * outer); c.Add(new Color(1f, 1f, 1f, 0.15f * edge));
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2;
            tri.Add(a); tri.Add(a + 1); tri.Add(a + 3);
            tri.Add(a); tri.Add(a + 3); tri.Add(a + 2);
        }
        Mesh m = new Mesh { name = "CamCone" };
        m.SetVertices(v); m.SetColors(c); m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        return m;
    }

    static Material ConeMaterial()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Sprites/Default");
        Material m = new Material(s) { name = "CamCone" };
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);                       // alpha blend
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        return m;
    }

    static void Mats()
    {
        if (bodyMat != null) return;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        bodyMat = new Material(lit) { name = "MissionCamBody" }; Tint(bodyMat, new Color(0.78f, 0.79f, 0.8f));
        lensMat = new Material(lit) { name = "MissionCamLens" }; Tint(lensMat, new Color(0.03f, 0.03f, 0.04f));
        if (lensMat.HasProperty("_Smoothness")) lensMat.SetFloat("_Smoothness", 0.9f);
        ledMat = new Material(unlit != null ? unlit : lit) { name = "MissionCamLed" }; Tint(ledMat, new Color(1f, 0.1f, 0.08f));
    }

    static void Tint(Material m, Color c) { if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); m.color = c; }
}
