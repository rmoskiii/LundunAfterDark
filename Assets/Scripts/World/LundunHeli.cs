using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// W3: the police helicopter (4★ and up). Built in code from simple shapes (an original design: dark navy with a
/// yellow band), with spinning rotors, a rotor sound and a searchlight.
///   • the searchlight is the threat: it sweeps the search area; if its beam finds James (and nothing is in the
///     way — a roof, a bridge, a tunnel), it locks on and every unit knows where he is
///   • by day it can also spot James from the air nearby; at night only the beam (and very close) finds him
///   • it follows from the air, staying high; hide under cover to break its line of sight
///   • it can be shot down (tough: roughly three carbine magazines); it catches fire, falls and explodes
///   • when the stars clear it flies off. No marksman (yet) — see how the searchlight plays first.
/// Spawned by PoliceDirector. Shows on the minimap with its beam's footprint.
public class LundunHeli : MonoBehaviour
{
    public static readonly List<LundunHeli> Active = new List<LundunHeli>();

    [Header("Flight")]
    [SerializeField] private float altitude = 42f;            // above the ground below it
    [SerializeField] private float maxSpeed = 30f;
    [SerializeField] private float orbitRadius = 26f;          // circling James while it can see him
    [Header("Searchlight")]
    [SerializeField] private float beamFootprint = 7f;         // radius of the bright spot on the ground (m)
    [SerializeField] private float daySpotting = 32f;          // by day it can spot James this far from the point below it
    [SerializeField] private float sweepSpeed = 0.35f;
    [Header("Toughness")]
    [SerializeField] private float health = 600f;

    public bool SeesPlayer { get; private set; }
    /// Where the searchlight lands on the ground (for the minimap).
    public Vector3 BeamPoint { get; private set; }
    public float BeamRadius => beamFootprint;
    public bool Leaving => leaving;

    private Transform rotor, tailRotor, lightPivot, beamCone;
    private Light spot;
    private Material beamMat;
    private AudioSource rotorAudio;
    private VehicleHealth vh;
    private Rigidbody rb;
    private Vector3 velocity, beamAim;
    private float orbitAngle, sweepT, lastSaw = -99f, leaveAt;
    private bool leaving, falling;
    private int blockMask;

    // ------------------------------------------------------------------ spawning
    /// A helicopter coming in from far off, high up, out of sight.
    public static LundunHeli Spawn(Vector3 around, VehicleHealth explosionTemplate)
    {
        Vector2 dir = Random.insideUnitCircle.normalized;
        Vector3 from = around + new Vector3(dir.x, 0f, dir.y) * 260f + Vector3.up * 60f;
        GameObject go = new GameObject("PoliceHelicopter");
        go.SetActive(false);                                   // set it all up before anything wakes
        go.transform.position = from;
        go.transform.rotation = Quaternion.LookRotation(new Vector3(-dir.x, 0f, -dir.y));
        LundunHeli h = go.AddComponent<LundunHeli>();
        h.Build(explosionTemplate);
        go.SetActive(true);
        return h;
    }

    void Build(VehicleHealth template)
    {
        blockMask = LayerMask.GetMask("Default");
        Material navy = NewLit(new Color(0.08f, 0.1f, 0.18f), 0.6f);
        Material band = NewLit(new Color(0.98f, 0.78f, 0.12f), 0.4f);
        Material glass = NewLit(new Color(0.05f, 0.07f, 0.1f), 0.95f);
        Material dark = NewLit(new Color(0.03f, 0.03f, 0.035f), 0.3f);

        Transform body = Part("Body", PrimitiveType.Capsule, transform, new Vector3(0f, 0f, 0f), new Vector3(1.9f, 1.6f, 2.3f), Quaternion.Euler(90f, 0f, 0f), navy);
        Part("Canopy", PrimitiveType.Sphere, transform, new Vector3(0f, 0.15f, 1.75f), new Vector3(1.6f, 1.3f, 1.6f), Quaternion.identity, glass);
        Part("Band", PrimitiveType.Cube, transform, new Vector3(0f, -0.05f, -0.2f), new Vector3(1.93f, 0.28f, 2.6f), Quaternion.identity, band);
        Part("Boom", PrimitiveType.Cube, transform, new Vector3(0f, 0.25f, -3.6f), new Vector3(0.35f, 0.4f, 3.8f), Quaternion.identity, navy);
        Part("Fin", PrimitiveType.Cube, transform, new Vector3(0f, 0.9f, -5.3f), new Vector3(0.12f, 1.3f, 0.7f), Quaternion.Euler(-15f, 0f, 0f), navy);
        Part("SkidL", PrimitiveType.Cube, transform, new Vector3(-0.85f, -1.15f, 0.2f), new Vector3(0.1f, 0.1f, 3.2f), Quaternion.identity, dark);
        Part("SkidR", PrimitiveType.Cube, transform, new Vector3(0.85f, -1.15f, 0.2f), new Vector3(0.1f, 0.1f, 3.2f), Quaternion.identity, dark);
        Part("Mast", PrimitiveType.Cylinder, transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.18f, 0.25f, 0.18f), Quaternion.identity, dark);

        rotor = new GameObject("Rotor").transform;
        rotor.SetParent(transform, false); rotor.localPosition = new Vector3(0f, 1.3f, 0f);
        Part("BladeA", PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(9.5f, 0.05f, 0.3f), Quaternion.identity, dark);
        Part("BladeB", PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(0.3f, 0.05f, 9.5f), Quaternion.identity, dark);
        tailRotor = new GameObject("TailRotor").transform;
        tailRotor.SetParent(transform, false); tailRotor.localPosition = new Vector3(0.2f, 0.95f, -5.45f);
        Part("TailBlade", PrimitiveType.Cube, tailRotor, Vector3.zero, new Vector3(0.04f, 1.5f, 0.15f), Quaternion.identity, dark);

        // the searchlight under the nose
        lightPivot = new GameObject("Searchlight").transform;
        lightPivot.SetParent(transform, false); lightPivot.localPosition = new Vector3(0f, -0.9f, 1.4f);
        spot = lightPivot.gameObject.AddComponent<Light>();
        spot.type = LightType.Spot; spot.range = 110f; spot.spotAngle = 18f; spot.innerSpotAngle = 10f;
        spot.color = new Color(0.92f, 0.96f, 1f); spot.shadows = LightShadows.None;
        beamCone = BeamMesh(lightPivot);

        // one collider for bullets (and crashing)
        CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
        col.direction = 2; col.radius = 1.1f; col.height = 6f; col.center = new Vector3(0f, 0f, -0.8f);
        rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false; rb.mass = 2500f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // health and the fireball reuse the police car's (copied settings), with much more health
        vh = gameObject.AddComponent<VehicleHealth>();
        string json = template != null ? JsonUtility.ToJson(template) : "{}";
        JsonUtility.FromJsonOverwrite(json, vh);
        JsonUtility.FromJsonOverwrite("{\"maxHealth\":" + health.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                      ",\"crashDamageSpeed\":1000}", vh);

        rotorAudio = gameObject.AddComponent<AudioSource>();
        rotorAudio.clip = LundunGunAudio.Rotor;
        rotorAudio.loop = true; rotorAudio.spatialBlend = 1f; rotorAudio.minDistance = 25f; rotorAudio.maxDistance = 400f;
        rotorAudio.rolloffMode = AudioRolloffMode.Linear; rotorAudio.volume = 0.75f; rotorAudio.dopplerLevel = 0.4f;
        rotorAudio.playOnAwake = true;

        foreach (Transform t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LayerMask.NameToLayer("Vehicle") >= 0 ? LayerMask.NameToLayer("Vehicle") : 0;
        lightPivot.gameObject.layer = 0;
        if (body != null) body.name = "Body";
    }

    void OnEnable() { Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    void Start()
    {
        if (rotorAudio != null && !rotorAudio.isPlaying) rotorAudio.Play();
        beamAim = transform.position + transform.forward * 30f + Vector3.down * altitude;
    }

    /// Stars cleared (or dropped below 4): fly off and disappear.
    public void Leave()
    {
        if (leaving) return;
        leaving = true;
        leaveAt = Time.time;
    }

    // ------------------------------------------------------------------ flying
    void Update()
    {
        if (rotor != null) rotor.Rotate(0f, 1300f * Time.deltaTime, 0f, Space.Self);
        if (tailRotor != null) tailRotor.Rotate(2400f * Time.deltaTime, 0f, 0f, Space.Self);

        if (vh != null && vh.IsBurning && !falling) StartFalling();
        if (falling) return;

        WantedSystem w = WantedSystem.Instance;
        if (w == null) return;
        if (w.Stars < 4) Leave();
        Transform player = w.PlayerTarget;

        Vector3 goal;
        if (leaving)
        {
            Vector3 away = transform.position - (player != null ? player.position : transform.position); away.y = 0f;
            if (away.sqrMagnitude < 1f) away = transform.forward;
            goal = transform.position + away.normalized * 80f + Vector3.up * 5f;
            if (Time.time - leaveAt > 20f && !Visible()) { Destroy(gameObject); return; }
        }
        else
        {
            bool tracking = Time.time - lastSaw < 3f && player != null;
            Vector3 centre = tracking ? player.position : w.LastKnownPosition;
            float radius = tracking ? orbitRadius : Mathf.Max(orbitRadius, w.SearchRadius * 0.55f);
            orbitAngle += (tracking ? 0.32f : 0.18f) * Time.deltaTime;
            goal = centre + new Vector3(Mathf.Cos(orbitAngle), 0f, Mathf.Sin(orbitAngle)) * radius;
            goal.y = GroundY(goal) + altitude;
        }

        // smooth flight: accelerate towards the goal, tilt into the motion
        Vector3 want = Vector3.ClampMagnitude((goal - transform.position) * 0.6f, maxSpeed);
        velocity = Vector3.MoveTowards(velocity, want, 9f * Time.deltaTime);
        transform.position += velocity * Time.deltaTime;
        Vector3 flat = velocity; flat.y = 0f;
        Vector3 look = flat.sqrMagnitude > 4f ? flat : (player != null ? player.position - transform.position : transform.forward);
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f)
        {
            Quaternion yaw = Quaternion.LookRotation(look.normalized);
            Vector3 local = Quaternion.Inverse(yaw) * velocity;
            Quaternion tilt = Quaternion.Euler(Mathf.Clamp(local.z * 0.5f, -14f, 14f), 0f, Mathf.Clamp(-local.x * 0.6f, -18f, 18f));
            transform.rotation = Quaternion.Slerp(transform.rotation, yaw * tilt, 1.8f * Time.deltaTime);
        }

        Searchlight(w, player);
    }

    void Searchlight(WantedSystem w, Transform player)
    {
        bool night = IsNight();
        spot.intensity = night ? 9f : 2.5f;
        if (beamMat != null) beamMat.SetColor("_BaseColor", new Color(0.85f, 0.92f, 1f, night ? 0.09f : 0.025f));

        // aim: locked on James while it can see him, otherwise sweeping the search area
        Vector3 target;
        if (!leaving && player != null && Time.time - lastSaw < 1.5f) target = player.position;
        else
        {
            sweepT += sweepSpeed * Time.deltaTime;
            Vector3 c = w.LastKnownPosition;
            float r = Mathf.Max(8f, w.SearchRadius * 0.7f);
            target = c + new Vector3(Mathf.Sin(sweepT * 1.3f), 0f, Mathf.Sin(sweepT * 0.9f + 1.1f)) * r;
            target.y = GroundY(target);
        }
        beamAim = Vector3.Lerp(beamAim, target, 2.5f * Time.deltaTime);
        Vector3 dir = beamAim - lightPivot.position;
        if (dir.sqrMagnitude > 0.01f) lightPivot.rotation = Quaternion.LookRotation(dir.normalized);
        if (beamCone != null) { float len = Mathf.Min(dir.magnitude, 100f); beamCone.localScale = new Vector3(len, len, len); }

        // where the beam lands (stops at the first roof it hits)
        BeamPoint = Physics.Raycast(lightPivot.position, dir.normalized, out RaycastHit hit, 160f, blockMask, QueryTriggerInteraction.Ignore) ? hit.point : beamAim;

        // spotting
        SeesPlayer = false;
        if (leaving || player == null || w.Stars < 4) return;
        Vector3 p = player.position + Vector3.up;
        Vector3 toP = p - BeamPoint; toP.y = 0f;
        Vector3 below = p - transform.position; below.y = 0f;
        bool inBeam = toP.magnitude < beamFootprint + 1f;
        bool inView = !night && below.magnitude < daySpotting;
        bool veryClose = below.magnitude < 10f;
        if ((inBeam || inView || veryClose) && !Physics.Linecast(lightPivot.position, p, blockMask, QueryTriggerInteraction.Ignore))
        {
            SeesPlayer = true;
            lastSaw = Time.time;
            w.ReportSighting(player.position);
        }
    }

    void StartFalling()
    {
        falling = true;
        SeesPlayer = false;
        if (spot != null) spot.enabled = false;
        if (beamCone != null) beamCone.gameObject.SetActive(false);
        CrimeSystem.Report(CrimeType.DestroyPoliceCar, transform.position);      // bringing down a police helicopter
        rb.isKinematic = false; rb.useGravity = true;
        rb.linearVelocity = velocity;
        rb.AddTorque(new Vector3(Random.Range(-0.5f, 0.5f), 3f, Random.Range(-0.5f, 0.5f)), ForceMode.VelocityChange);
        Destroy(gameObject, 30f);
    }

    // ------------------------------------------------------------------ helpers
    float GroundY(Vector3 at)
    {
        return Physics.Raycast(new Vector3(at.x, 300f, at.z), Vector3.down, out RaycastHit h, 600f, blockMask, QueryTriggerInteraction.Ignore) ? h.point.y : 0f;
    }

    static bool IsNight()
    {
        float h = LundunClock.Instance != null ? LundunClock.Instance.Hour : 12f;
        return h >= 19.5f || h < 6.5f;
    }

    bool Visible()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) if (r.isVisible) return true;
        return false;
    }

    static Material NewLit(Color c, float smooth)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(s != null ? s : Shader.Find("Standard"));
        m.SetColor("_BaseColor", c); m.color = c;
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    static Transform Part(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Collider c = g.GetComponent<Collider>(); if (c != null) DestroyImmediate(c);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localRotation = rot; g.transform.localScale = scale;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.On;
        return g.transform;
    }

    /// A soft see-through cone along +Z (length 1, scaled to reach the ground): the visible beam at night.
    Transform BeamMesh(Transform parent)
    {
        const int sides = 16;
        float halfAngle = 9f * Mathf.Deg2Rad;
        Mesh m = new Mesh { name = "SearchlightBeam" };
        Vector3[] v = new Vector3[sides + 1];
        int[] tri = new int[sides * 3];
        v[0] = Vector3.zero;
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            v[i + 1] = new Vector3(Mathf.Cos(a) * Mathf.Tan(halfAngle), Mathf.Sin(a) * Mathf.Tan(halfAngle), 1f);
            tri[i * 3] = 0; tri[i * 3 + 1] = 1 + (i + 1) % sides; tri[i * 3 + 2] = 1 + i;
        }
        m.vertices = v; m.triangles = tri; m.RecalculateBounds();
        GameObject g = new GameObject("Beam");
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) { Destroy(g); return null; }
        beamMat = new Material(s) { name = "SearchlightBeam" };
        beamMat.SetFloat("_Surface", 1f); beamMat.SetFloat("_Blend", 0f); beamMat.SetFloat("_ZWrite", 0f);
        beamMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); beamMat.SetFloat("_DstBlend", (float)BlendMode.One);   // additive glow
        beamMat.SetFloat("_Cull", (float)CullMode.Off);
        beamMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        beamMat.SetOverrideTag("RenderType", "Transparent");
        beamMat.renderQueue = (int)RenderQueue.Transparent;
        mr.sharedMaterial = beamMat;
        return g.transform;
    }
}
