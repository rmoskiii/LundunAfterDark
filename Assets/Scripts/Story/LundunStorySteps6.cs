using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// S6: a guard on patrol at night (mission 09): walks a loop of points with a torch (a real spot light and a faint
/// beam you can see). Not on the NavMesh while patrolling (it walks the yard's aisles straight); Wake() hands it back
/// to the NavMesh for a fight. Suspicious: stops and turns to look, a "?" over its head.
public class LundunGuard : MonoBehaviour
{
    public Vector3[] route;
    public float speed = 1.35f;
    public const float Range = 12f, HalfAngle = 25f;

    public bool Suspicious { get; set; }
    public Vector3 Eye { get { return transform.position + Vector3.up * 1.55f; } }

    private int next;
    private Animator anim;
    private NavMeshAgent agent;
    private TextMeshPro mark;
    private Transform torch;
    private static Material beamMat;
    private static Mesh beamMesh;

    void Start()
    {
        anim = GetComponentInChildren<Animator>();
        agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        BuildTorch();
        GameObject g = new GameObject("Huh", typeof(RectTransform));
        g.transform.SetParent(transform, false);
        g.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        ((RectTransform)g.transform).sizeDelta = new Vector2(1f, 1f);
        mark = g.AddComponent<TextMeshPro>();
        mark.text = "?"; mark.fontSize = 6f; mark.color = new Color(1f, 0.8f, 0.2f); mark.alignment = TextAlignmentOptions.Center; mark.fontStyle = FontStyles.Bold;
        g.AddComponent<LundunFaceCamera>();
        g.SetActive(false);
    }

    void Update()
    {
        if (mark != null && mark.gameObject.activeSelf != Suspicious) mark.gameObject.SetActive(Suspicious);
        if (route == null || route.Length == 0) return;
        Transform j = LundunMissionRun.James();
        if (Suspicious && j != null)
        {
            Vector3 d = j.position - transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 140f * Time.deltaTime);
            LundunAnim.Float(anim, "Speed", 0f, 0.15f);
            return;
        }
        Vector3 to = route[next];
        Vector3 flat = to - transform.position; flat.y = 0f;
        if (flat.magnitude < 0.4f) { next = (next + 1) % route.Length; return; }
        Quaternion want = Quaternion.LookRotation(flat);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 180f * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, want) < 40f)
        {
            Vector3 p = transform.position + flat.normalized * speed * Time.deltaTime;
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out hit, 5f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            transform.position = p;
            LundunAnim.Float(anim, "Speed", speed, 0.15f);
        }
        else LundunAnim.Float(anim, "Speed", 0f, 0.15f);
    }

    /// Can this guard see James (in its torch's cone, nothing in between)? How strongly (0–1, nearer = more).
    public float Sees(Vector3 james)
    {
        Vector3 chest = james + Vector3.up * 1.2f;
        Vector3 v = chest - Eye;
        float d = v.magnitude;
        if (d > Range) return 0f;
        if (d > 2.2f && Vector3.Angle(transform.forward, new Vector3(v.x, 0f, v.z)) > HalfAngle) return 0f;
        if (Physics.Linecast(Eye, chest, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore)) return 0f;
        return Mathf.Lerp(1f, 0.35f, d / Range);
    }

    /// The patrol's over (they've seen him): back on the NavMesh, no more walking the loop.
    public void Wake()
    {
        Suspicious = false;
        route = null;
        if (agent != null && NavMesh.SamplePosition(transform.position, out NavMeshHit h, 3f, NavMesh.AllAreas)) { agent.enabled = true; agent.Warp(h.position); }
        if (mark != null) mark.gameObject.SetActive(false);
        enabled = false;
    }

    void BuildTorch()
    {
        GameObject t = new GameObject("Torch");
        t.transform.SetParent(transform, false);
        t.transform.localPosition = new Vector3(0.25f, 1.35f, 0.35f);
        t.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
        torch = t.transform;
        Light l = t.AddComponent<Light>();
        l.type = LightType.Spot; l.range = Range + 4f; l.spotAngle = HalfAngle * 2f + 6f; l.intensity = 7f;
        l.color = new Color(1f, 0.95f, 0.82f); l.shadows = LightShadows.None;
        GameObject beam = new GameObject("Beam");
        beam.transform.SetParent(t.transform, false);
        beam.AddComponent<MeshFilter>().sharedMesh = BeamMesh();
        MeshRenderer r = beam.AddComponent<MeshRenderer>();
        r.sharedMaterial = BeamMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    }

    static Mesh BeamMesh()
    {
        if (beamMesh != null) return beamMesh;
        const int seg = 16;
        float len = Range, rad = Range * Mathf.Tan(HalfAngle * Mathf.Deg2Rad);
        List<Vector3> v = new List<Vector3> { Vector3.zero };
        List<Color> c = new List<Color> { new Color(1f, 1f, 1f, 1f) };
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            v.Add(new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, len));
            c.Add(new Color(1f, 1f, 1f, 0f));
        }
        List<int> tris = new List<int>();
        for (int i = 0; i < seg; i++) { tris.Add(0); tris.Add(i + 1); tris.Add(i + 2); }
        beamMesh = new Mesh { name = "TorchBeam" };
        beamMesh.SetVertices(v); beamMesh.SetColors(c); beamMesh.SetTriangles(tris, 0);
        beamMesh.RecalculateBounds();
        return beamMesh;
    }

    /// Faint, see-through, fades towards the end (vertex alpha), both sides drawn.
    static Material BeamMaterial()
    {
        if (beamMat != null) return beamMat;
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Sprites/Default");
        beamMat = new Material(s) { name = "TorchBeam" };
        Color col = new Color(1f, 0.95f, 0.75f, 0.13f);
        if (beamMat.HasProperty("_BaseColor")) beamMat.SetColor("_BaseColor", col);
        beamMat.color = col;
        if (beamMat.HasProperty("_Surface"))
        {
            beamMat.SetFloat("_Surface", 1f); beamMat.SetFloat("_Blend", 2f);              // additive
            beamMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            beamMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            beamMat.SetFloat("_ZWrite", 0f);
            beamMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            beamMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        if (beamMat.HasProperty("_Cull")) beamMat.SetFloat("_Cull", 0f);
        return beamMat;
    }
}

/// S6: sneak about (mission 09). A meter fills while a guard's torch is on James (faster close up; a guard right
/// beside him or hearing him run nearby counts too) and drains when it isn't. Full: they've seen him (alarm). Done
/// when goal() is true, or on the alarm.
public class LundunSneak : LundunStep
{
    readonly Func<List<LundunGuard>> guards;
    readonly Func<bool> goal;
    readonly Action alarm;
    float meter;
    GameObject ui;
    Image fill;
    TextMeshProUGUI label;

    public LundunSneak(Func<List<LundunGuard>> who, Func<bool> done, Action onAlarm, string objectiveText)
    {
        guards = who; goal = done; alarm = onAlarm; objective = objectiveText;
    }

    public override void Begin(LundunMissionRun run) { meter = 0f; Build(); }

    public override bool Tick(LundunMissionRun run)
    {
        if (goal()) return true;
        Transform j = LundunMissionRun.James();
        List<LundunGuard> gs = guards();
        float rate = 0f;
        if (j != null && gs != null)
        {
            CharacterController cc = j.GetComponent<CharacterController>();
            float speed = cc != null ? new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude : 0f;
            bool driving = LundunMissionRun.Driving();
            foreach (LundunGuard g in gs)
            {
                if (g == null || !g.enabled) continue;
                float s = g.Sees(j.position);
                float d = LundunMissionRun.FlatDistance(g.transform.position, j.position);
                if (speed > 4.2f && d < 7f) s = Mathf.Max(s, 0.45f);                  // heard him running
                if (driving && d < 25f) s = Mathf.Max(s, 0.8f);                       // a car in the yard
                g.Suspicious = s > 0f && meter > 0.25f;
                rate = Mathf.Max(rate, s * 1.15f);
            }
        }
        float scale = run.difficulty == LundunDifficulty.Easy ? 0.75f : run.difficulty == LundunDifficulty.Hard ? 1.3f : 1f;
        meter = rate > 0f ? meter + rate * scale * Time.deltaTime : Mathf.Max(0f, meter - 0.22f * Time.deltaTime);
        Show();
        if (meter >= 1f)
        {
            if (gs != null) foreach (LundunGuard g in gs) if (g != null) g.Suspicious = false;
            LundunHaptics.Heavy();
            if (alarm != null) alarm();
            return true;
        }
        return false;
    }

    public override void End(LundunMissionRun run)
    {
        if (ui != null) Object.Destroy(ui);
        List<LundunGuard> gs = guards();
        if (gs != null) foreach (LundunGuard g in gs) if (g != null) g.Suspicious = false;
    }

    void Show()
    {
        if (ui == null) return;
        bool on = meter > 0.01f;
        if (ui.activeSelf != on) ui.SetActive(on);
        if (!on) return;
        fill.fillAmount = Mathf.Clamp01(meter);
        fill.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.95f, 0.15f, 0.1f), meter);
        label.text = meter < 0.5f ? "SOMEONE'S LOOKING…" : "THEY'VE NEARLY SEEN YOU!";
    }

    void Build()
    {
        ui = new GameObject("SneakMeter", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 58;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        RectTransform root = (RectTransform)ui.transform;
        Image back = LundunUIBits.Box(root, "Back", Vector2.zero, new Vector2(460f, 22f), new Color(0f, 0f, 0f, 0.6f));
        RectTransform br = back.rectTransform; br.anchorMin = br.anchorMax = new Vector2(0.5f, 1f); br.anchoredPosition = new Vector2(0f, -230f);
        fill = LundunUIBits.Box(br, "Fill", Vector2.zero, new Vector2(452f, 14f), Color.yellow);
        fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
        fill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        label = LundunUIBits.Label(br, "", 26f, new Vector2(0f, 34f), new Vector2(700f, 40f));
        ui.SetActive(false);
    }
}

/// S6: get the van somewhere and stop while it's loaded (or unloaded): done after 'seconds' parked there.
public class LundunVanTo : LundunStep
{
    readonly Func<GameObject> van; readonly Func<Vector3> where; readonly float seconds; readonly string loading, lost, text;
    float held;
    public LundunVanTo(Func<GameObject> vehicle, Func<Vector3> place, float stopSeconds, string objectiveText, string whileLoading, string ifWrecked)
    {
        van = vehicle; where = place; seconds = stopSeconds; objective = text = objectiveText; loading = whileLoading; lost = ifWrecked;
    }

    public override void Begin(LundunMissionRun run)
    {
        held = 0f;
        hasMarker = true; marker = LundunMissionRun.Ground(where()); markerRadius = 9f; route = true;
    }

    public override bool Tick(LundunMissionRun run)
    {
        GameObject v = van();
        if (!LundunStoryPlaces.Fine(v)) { run.Fail(lost); return false; }
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        bool inVan = vs != null && vs.IsDriving && vs.CurrentCar != null && vs.CurrentCar.gameObject == v;
        Rigidbody rb = v.GetComponent<Rigidbody>();
        bool here = LundunMissionRun.FlatDistance(v.transform.position, marker) < markerRadius;
        if (!inVan) { objective = "Get back in the van"; held = 0f; return false; }
        if (here && (rb == null || rb.linearVelocity.magnitude < 1.5f))
        {
            held += Time.deltaTime;
            objective = loading + "  " + new string('■', Mathf.Clamp(Mathf.CeilToInt(held / seconds * 5f), 0, 5)) + new string('□', Mathf.Clamp(5 - Mathf.CeilToInt(held / seconds * 5f), 0, 5));
            return held >= seconds;
        }
        held = 0f;
        objective = here ? "Stop the van here" : text;
        return false;
    }
}
