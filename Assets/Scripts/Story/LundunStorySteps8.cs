using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// Phase 5 (gate A2): M04's Kingsley in person. A balcony is put on the estate block nearest the courtyard (a slab and
/// a railing on the face of the building, a door at one end), and Kingsley stands up there in the Kings' colours. While
/// he talks the camera looks up at him from behind James; then he turns his back and walks in through the door as his
/// boys come out. Everything it makes goes when the attempt ends.
public class LundunBalcony : LundunStep
{
    readonly Vector3 courtyard;
    readonly Func<LundunLine[]> pick;
    LundunLine[] lines;
    int at; float next;
    enum Phase { Talk, Walk, Done }
    Phase phase;
    float walkFrom;
    GameObject kingsley;
    Animator anim;
    Vector3 standAt, doorAt, facing;
    CameraFollow follow;
    VehicleSystem vs;

    public LundunBalcony(Vector3 courtyardMiddle, Func<LundunLine[]> said) { courtyard = courtyardMiddle; pick = said; objective = ""; }

    public override void Begin(LundunMissionRun run)
    {
        lines = pick() ?? new LundunLine[0];
        at = -1; next = 0f; phase = Phase.Talk;
        LundunMissions.TakeSkip();
        Build(run);
        vs = Object.FindAnyObjectByType<VehicleSystem>();
        if (vs != null) vs.LockControls(true);
        follow = Object.FindAnyObjectByType<CameraFollow>();
        if (follow != null && kingsley != null) follow.enabled = false;
    }

    public override bool Tick(LundunMissionRun run)
    {
        Frame();
        if (phase == Phase.Talk)
        {
            if (Time.time < next && !LundunMissions.TakeSkip()) return false;
            at++;
            if (at >= lines.Length)
            {
                LundunMissions.Say(null, null, 0f);
                phase = Phase.Walk; walkFrom = Time.time;
                return false;
            }
            float secs = LundunMissions.ReadTime(lines[at].text);
            LundunMissions.Say(lines[at].who, lines[at].text, secs);
            next = Time.time + secs + LundunMissions.TakeDelay();
            return false;
        }
        if (phase == Phase.Walk)
        {
            // he turns his back and walks in (2.4 s), then he's gone
            float k = (Time.time - walkFrom) / 2.4f;
            if (kingsley != null)
            {
                Vector3 to = doorAt - standAt; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) kingsley.transform.rotation = Quaternion.Slerp(kingsley.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 8f);
                kingsley.transform.position = Vector3.Lerp(standAt, doorAt, Mathf.Clamp01((k - 0.15f) / 0.85f));
                LundunAnim.Float(anim, "Speed", k < 1f ? 1.5f : 0f, 0.1f);
            }
            if (k >= 1f)
            {
                if (kingsley != null) kingsley.SetActive(false);
                phase = Phase.Done;
                return true;
            }
        }
        return phase == Phase.Done;
    }

    public override void End(LundunMissionRun run)
    {
        LundunMissions.Say(null, null, 0f);
        if (follow != null) follow.enabled = true;
        if (vs != null) vs.LockControls(false);
        if (kingsley != null) kingsley.SetActive(false);
    }

    /// Looking up at him from just behind James's shoulder.
    void Frame()
    {
        Camera cam = Camera.main;
        Transform j = LundunMissionRun.James();
        if (cam == null || j == null || kingsley == null || follow == null || follow.enabled) return;
        Vector3 head = kingsley.transform.position + Vector3.up * 1.7f;
        Vector3 toHim = head - j.position; toHim.y = 0f;
        toHim = toHim.sqrMagnitude > 0.01f ? toHim.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, toHim);
        Vector3 p = j.position - toHim * 3.2f + side * 1.1f + Vector3.up * 1.5f;
        cam.transform.SetPositionAndRotation(Vector3.Lerp(cam.transform.position, p, Time.deltaTime * 3f),
            Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(head - p), Time.deltaTime * 3f));
    }

    // ------------------------------------------------------------------ the balcony and the man on it
    void Build(LundunMissionRun run)
    {
        Vector3 ground = LundunMissionRun.Ground(courtyard);
        Vector3 wall, normal;
        if (!Facade(ground, out wall, out normal))
        {
            // no block close enough: a walkway on two posts at the courtyard's edge instead
            normal = Vector3.forward;
            wall = ground - normal * 16f + Vector3.up * 8f;
        }
        Vector3 along = Vector3.Cross(Vector3.up, normal);
        float floor = ground.y + 6.6f;
        Vector3 slab = new Vector3(wall.x, floor, wall.z) + normal * 0.8f;

        GameObject root = new GameObject("KingsleyBalcony");
        run.Spawn(root);
        Material concrete = Lit(new Color(0.55f, 0.54f, 0.52f));
        Material rail = Lit(new Color(0.12f, 0.12f, 0.13f));
        Material door = Lit(new Color(0.06f, 0.06f, 0.07f));
        Box(root.transform, "Slab", slab - Vector3.up * 0.1f, new Vector3(7f, 0.2f, 1.6f), along, normal, concrete, true);
        Box(root.transform, "Rail", slab + normal * 0.78f + Vector3.up * 0.55f, new Vector3(7f, 0.08f, 0.06f), along, normal, rail, false);
        for (int i = -3; i <= 3; i++)
            Box(root.transform, "Post", slab + normal * 0.78f + along * i * 1.15f + Vector3.up * 0.27f, new Vector3(0.05f, 0.55f, 0.05f), along, normal, rail, false);
        Box(root.transform, "Door", new Vector3(wall.x, floor + 1.05f, wall.z) + normal * 0.04f + along * 2.8f, new Vector3(1f, 2.1f, 0.06f), along, normal, door, false);
        Light lamp = new GameObject("BalconyLight").AddComponent<Light>();
        lamp.transform.SetParent(root.transform, false);
        lamp.transform.position = slab + Vector3.up * 2.6f + normal * 0.3f;
        lamp.type = LightType.Point; lamp.range = 7f; lamp.intensity = 2.4f; lamp.color = new Color(1f, 0.82f, 0.6f); lamp.shadows = LightShadows.None;

        standAt = slab - normal * 0.1f;
        doorAt = new Vector3(wall.x, floor, wall.z) + normal * 0.3f + along * 2.8f;
        facing = normal;
        kingsley = LundunCast.Make("KINGSLEY", null);
        if (kingsley == null) { Debug.LogWarning("[Lundun] Kingsley couldn't be made (no people models?)"); return; }
        NavMeshAgent ag = kingsley.GetComponent<NavMeshAgent>(); if (ag != null) ag.enabled = false;
        Rigidbody rb = kingsley.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
        kingsley.transform.SetPositionAndRotation(standAt, Quaternion.LookRotation(normal));
        kingsley.SetActive(true);
        anim = kingsley.GetComponentInChildren<Animator>();
        if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        run.Spawn(kingsley);
        Debug.Log("[Lundun] Kingsley on his balcony at " + standAt.ToString("F1"));
    }

    /// The nearest building face round the courtyard (8–45 m away), at first-floor height.
    static bool Facade(Vector3 ground, out Vector3 point, out Vector3 normal)
    {
        point = ground; normal = Vector3.forward;
        float best = 999f;
        int mask = LayerMask.GetMask("Default");
        for (int i = 0; i < 24; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
            if (!Physics.Raycast(ground + Vector3.up * 8f, dir, out RaycastHit h, 45f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (h.distance < 8f || Mathf.Abs(h.normal.y) > 0.3f || h.distance >= best) continue;
            best = h.distance; point = h.point;
            normal = new Vector3(h.normal.x, 0f, h.normal.z).normalized;
        }
        return best < 999f;
    }

    static void Box(Transform parent, string name, Vector3 at, Vector3 size, Vector3 along, Vector3 normal, Material m, bool collider)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        if (!collider) Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal));
        g.transform.localScale = new Vector3(size.x, size.y, size.z);
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
    }

    static Material Lit(Color c)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(s != null ? s : Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }
}

/// Phase 5 (gate A2): M05's Kings rolling up. Cars come along the road into Tower Plaza and stop hard by the kerb,
/// and the men in them get out with guns (Men: hand them to a LundunFight). Where they stopped is Stop.
public class LundunRollUp : LundunStep
{
    readonly Vector3 near; readonly int cars, gunmen;
    public readonly List<Health> Men = new List<Health>();
    readonly List<Transform> moving = new List<Transform>();
    readonly List<Vector3> from = new List<Vector3>(), to = new List<Vector3>();
    float started;
    public Vector3 Stop { get; private set; }

    public LundunRollUp(Vector3 roadNear, int howMany, int men) { near = roadNear; cars = howMany; gunmen = men; objective = ""; Stop = roadNear; }

    public override void Begin(LundunMissionRun run)
    {
        moving.Clear(); from.Clear(); to.Clear();
        started = Time.time;
        Vector3 pos; Quaternion rot;
        if (!LundunStoryPlaces.Kerb(near, out pos, out rot)) { pos = LundunMissionRun.Ground(near); rot = Quaternion.identity; }
        Stop = pos;
        Vector3 fwd = rot * Vector3.forward;
        LundunPopulation pop = LundunPopulation.Instance;
        for (int i = 0; i < cars && pop != null; i++)
        {
            Vector3 end = pos - fwd * (i * 7.5f);
            Vector3 start = end - fwd * 55f;
            GameObject c = pop.NewCar(null, start, rot * Quaternion.Euler(0f, i == 0 ? 0f : -8f, 0f), 11 + i * 3);
            if (c == null) continue;
            foreach (Rigidbody rb in c.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            foreach (CarController cc in c.GetComponentsInChildren<CarController>(true)) cc.enabled = false;
            run.Spawn(c);
            moving.Add(c.transform); from.Add(c.transform.position); to.Add(c.transform.position + (end - start));
        }
        LundunMissions.Toast("Kingsley's boys!");
    }

    public override bool Tick(LundunMissionRun run)
    {
        float k = Mathf.Clamp01((Time.time - started) / 2.6f);
        float e = 1f - (1f - k) * (1f - k);                                     // fast in, braking hard
        for (int i = 0; i < moving.Count; i++) if (moving[i] != null) moving[i].position = Vector3.Lerp(from[i], to[i], e);
        if (k < 1f) return false;
        GetOut(run);
        return true;
    }

    /// Out of the cars on the pavement side, facing James.
    void GetOut(LundunMissionRun run)
    {
        Men.Clear();
        LundunPopulation pop = LundunPopulation.Instance;
        Transform j = LundunMissionRun.James();
        int n = gunmen + run.ExtraEnemies;
        for (int i = 0; i < n && pop != null; i++)
        {
            Transform car = moving.Count > 0 ? moving[i % moving.Count] : null;
            Vector3 p = car != null ? car.position - car.right * 2f + car.forward * (i / Mathf.Max(1, moving.Count) * 1.6f - 0.8f) : Stop + new Vector3(i * 1.5f, 0f, 0f);
            if (!NavMesh.SamplePosition(p + Vector3.up, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
            GameObject g = pop.NewPerson(null, "Street");
            if (g == null) continue;
            NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
            Vector3 face = j != null ? j.position - hit.position : Vector3.forward; face.y = 0f;
            g.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward));
            g.SetActive(true);
            run.Spawn(g);
            Health h = g.GetComponent<Health>();
            if (h != null) Men.Add(h);
        }
    }
}

/// Phase 5 (gate A2): after M05, about 15 seconds of what's coming (not a paywall: everything's unlocked by playing).
/// Letterboxed, at night: a car chase round Knightsbury Circus, a torch down the aisles of Hollis's yard, the palace
/// lit up, a helicopter on its roof. Then the next chapter's name. SKIP after a second and a half. The HUD's hidden
/// and the camera's flown by hand; everything it puts out is gone afterwards and the clock goes back.
public class LundunChapterPreview : LundunStep
{
    const float Shot = 3.4f;
    static readonly string[] Lines = { "THE RIDERS WANT A WORD", "HOLLIS KEEPS A BOOK", "THE PALACE KEEPS SECRETS", "AND THERE'S ONE WAY OUT" };
    readonly string next;
    int shot = -1;
    float t, startedAt, hourWas = -1f;
    bool skip, done;
    GameObject ui;
    Image black;
    TextMeshProUGUI caption, title, small;
    CameraFollow follow;
    Camera cam;
    readonly List<Canvas> hidden = new List<Canvas>();
    readonly List<GameObject> props = new List<GameObject>();
    readonly List<Transform> spin = new List<Transform>();
    Transform lead, chaser;
    Vector3 chaseStart, chaseDir;
    Vector3 roof;

    public LundunChapterPreview(string nextChapter) { next = nextChapter; objective = ""; }

    public override void Begin(LundunMissionRun run)
    {
        shot = -1; t = 0f; skip = false; done = false; startedAt = Time.time;
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        if (vs != null) vs.LockControls(true);
        follow = Object.FindAnyObjectByType<CameraFollow>();
        if (follow != null) follow.enabled = false;
        cam = Camera.main;
        LundunClock c = LundunClock.Instance;
        if (c != null) { hourWas = c.Hour; c.SetHour(23.4f); }
        roof = Roof(new Vector3(625f, 0f, 1790f));
        BuildUI();
        NextShot();
    }

    public override bool Tick(LundunMissionRun run)
    {
        if (done) return true;
        if (skip) { Finish(); return true; }
        t += Time.deltaTime;
        if (shot < 4 && t > Shot) NextShot();
        else if (shot == 4 && t > 2.2f) { Finish(); return true; }
        Fly();
        // a dip to black between shots
        float edge = shot < 4 ? Mathf.Max(Mathf.Clamp01(1f - t / 0.35f), Mathf.Clamp01((t - (Shot - 0.3f)) / 0.3f)) : 1f;
        if (black != null) black.color = new Color(0f, 0f, 0f, edge);
        foreach (Transform r in spin) if (r != null) r.Rotate(0f, 1300f * Time.deltaTime, 0f, Space.Self);
        return false;
    }

    public override void End(LundunMissionRun run) { Finish(); }

    void NextShot()
    {
        Clear();
        shot++; t = 0f;
        if (caption != null) caption.text = shot < 4 ? Lines[shot] : "";
        if (shot == 4)
        {
            if (title != null) title.text = "LUNDUN AFTER DARK";
            if (small != null) small.text = "NEXT  ·  " + next;
            return;
        }
        if (shot == 0) StageChase();
        else if (shot == 1) StageYard();
        else if (shot == 2) StagePalace();
        else if (shot == 3) StageHeli();
    }

    // ------------------------------------------------------------------ the four shots
    void StageChase()
    {
        Vector3 circus = new Vector3(560f, 0f, 945f);
        Vector3 pos; Quaternion rot;
        if (!LundunStoryPlaces.Kerb(circus, out pos, out rot)) { pos = LundunMissionRun.Ground(circus); rot = Quaternion.identity; }
        chaseDir = rot * Vector3.forward;
        chaseStart = pos - chaseDir * 30f;
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return;
        GameObject a = pop.NewCar(null, chaseStart, rot, 2);
        GameObject b = pop.NewCar(null, chaseStart - chaseDir * 9f, rot, 7);
        foreach (GameObject g in new[] { a, b })
        {
            if (g == null) continue;
            foreach (Rigidbody rb in g.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            foreach (CarController cc in g.GetComponentsInChildren<CarController>(true)) cc.enabled = false;
            props.Add(g);
        }
        lead = a != null ? a.transform : null; chaser = b != null ? b.transform : null;
    }

    void StageYard()
    {
        foreach (Vector3 fp in new[] { new Vector3(1106f, 0f, -96f), new Vector3(1177f, 0f, -96f) })
        {
            Light l = new GameObject("PreviewFlood").AddComponent<Light>();
            l.transform.position = LundunMissionRun.Ground(fp) + Vector3.up * 7.8f;
            l.transform.rotation = Quaternion.LookRotation((new Vector3(1141.5f, 0f, -86f) - fp).normalized * 0.4f + Vector3.down);
            l.type = LightType.Spot; l.range = 26f; l.spotAngle = 75f; l.intensity = 4f; l.color = new Color(0.85f, 0.9f, 1f); l.shadows = LightShadows.None;
            props.Add(l.gameObject);
        }
        LundunPopulation pop = LundunPopulation.Instance;
        GameObject guard = pop != null ? pop.NewPerson(null, "Street") : null;
        if (guard == null) return;
        NPCBrain b = guard.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
        NavMeshAgent ag = guard.GetComponent<NavMeshAgent>(); if (ag != null) ag.enabled = false;
        Vector3 g0 = LundunMissionRun.Ground(new Vector3(1141.5f, 0f, -128f));
        guard.transform.SetPositionAndRotation(g0, Quaternion.LookRotation(Vector3.back));
        guard.SetActive(true);
        Light torch = new GameObject("Torch").AddComponent<Light>();
        torch.transform.SetParent(guard.transform, false);
        torch.transform.localPosition = new Vector3(0.3f, 1.3f, 0.4f);
        torch.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
        torch.type = LightType.Spot; torch.range = 18f; torch.spotAngle = 32f; torch.intensity = 6f; torch.color = new Color(1f, 0.95f, 0.8f); torch.shadows = LightShadows.None;
        props.Add(guard);
    }

    void StagePalace()
    {
        for (int i = -1; i <= 1; i += 2)
        {
            Light f = new GameObject("PreviewFloodlight").AddComponent<Light>();
            f.transform.position = LundunMissionRun.Ground(new Vector3(560f + i * 40f, 0f, 1712f)) + Vector3.up * 3f;
            f.transform.rotation = Quaternion.LookRotation(new Vector3(560f + i * 20f, roof.y - 6f, 1765f) - f.transform.position);
            f.type = LightType.Spot; f.range = 110f; f.spotAngle = 55f; f.intensity = 26f; f.color = new Color(1f, 0.95f, 0.85f); f.shadows = LightShadows.None;
            props.Add(f.gameObject);
        }
    }

    void StageHeli()
    {
        LundunFlyHeli h = LundunFlyHeli.Spawn(roof, 90f);
        if (h == null) return;
        props.Add(h.gameObject);
        foreach (Transform t2 in h.GetComponentsInChildren<Transform>(true)) if (t2.name == "Rotor") spin.Add(t2);
    }

    void Fly()
    {
        if (cam == null) return;
        float k = Mathf.Clamp01(t / Shot);
        Vector3 p, look;
        switch (shot)
        {
            case 0:
                {
                    float d = t * 21f;
                    if (lead != null) lead.position = chaseStart + chaseDir * d;
                    if (chaser != null) chaser.position = chaseStart + chaseDir * (d - 9f + Mathf.Sin(t * 2f) * 1.5f);
                    Vector3 c = chaseStart + chaseDir * d;
                    Vector3 right = Vector3.Cross(Vector3.up, chaseDir);
                    p = c - chaseDir * Mathf.Lerp(4f, 12f, k) + right * 3.4f + Vector3.up * 1.1f;
                    look = c - chaseDir * 4f + Vector3.up * 0.8f;
                    break;
                }
            case 1:
                p = LundunMissionRun.Ground(new Vector3(1141.5f, 0f, Mathf.Lerp(-174f, -150f, k))) + Vector3.up * 1.6f;
                look = LundunMissionRun.Ground(new Vector3(1141.5f, 0f, -110f)) + Vector3.up * 1.6f;
                break;
            case 2:
                p = LundunMissionRun.Ground(new Vector3(560f, 0f, Mathf.Lerp(1640f, 1690f, k))) + Vector3.up * Mathf.Lerp(3f, 9f, k);
                look = new Vector3(560f, roof.y - 4f, 1790f);
                break;
            case 3:
                {
                    Vector3 round = Quaternion.AngleAxis(Mathf.Lerp(-70f, 10f, k), Vector3.up) * Vector3.back;
                    p = roof + round * 22f + Vector3.up * Mathf.Lerp(4f, 9f, k);
                    look = roof + Vector3.up * 1.5f;
                    break;
                }
            default:
                return;
        }
        cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(look - p, Vector3.up));
    }

    static Vector3 Roof(Vector3 p)
    {
        if (Physics.Raycast(new Vector3(p.x, 90f, p.z), Vector3.down, out RaycastHit h, 120f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore) && h.point.y > 12f)
            return h.point;
        return new Vector3(p.x, LundunPlaces.Ground(p).y + LundunPalaceWays.PalaceTop, p.z);
    }

    // ------------------------------------------------------------------ the frame round it
    void BuildUI()
    {
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.isRootCanvas && c.enabled && c.renderMode == RenderMode.ScreenSpaceOverlay && c.GetComponentInParent<LundunMissions>() == null) { c.enabled = false; hidden.Add(c); }
        ui = new GameObject("ChapterPreview", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 69;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)ui.transform;
        for (int i = 0; i < 2; i++)
        {
            Image bar = LundunUIBits.Box(root, "Bar", Vector2.zero, Vector2.zero, Color.black);
            RectTransform br = bar.rectTransform;
            br.anchorMin = new Vector2(0f, i == 0 ? 0f : 0.88f); br.anchorMax = new Vector2(1f, i == 0 ? 0.12f : 1f);
            br.offsetMin = br.offsetMax = Vector2.zero;
            bar.raycastTarget = false;
        }
        black = LundunUIBits.Box(root, "Black", Vector2.zero, Vector2.zero, Color.black);
        RectTransform bk = black.rectTransform; bk.anchorMin = Vector2.zero; bk.anchorMax = Vector2.one; bk.offsetMin = bk.offsetMax = Vector2.zero;
        black.raycastTarget = false;
        TextMeshProUGUI tag = LundunUIBits.Label(root, "COMING UP", 26f, Vector2.zero, new Vector2(500f, 50f));
        RectTransform tr = tag.rectTransform; tr.anchorMin = tr.anchorMax = new Vector2(0f, 1f); tr.pivot = new Vector2(0f, 1f); tr.anchoredPosition = new Vector2(60f, -40f);
        tag.alignment = TextAlignmentOptions.TopLeft; tag.characterSpacing = 18f; tag.color = new Color(1f, 0.66f, 0.18f);
        caption = LundunUIBits.Label(root, "", 44f, new Vector2(0f, -400f), new Vector2(1700f, 70f));
        caption.characterSpacing = 16f;
        title = LundunUIBits.Label(root, "", 84f, new Vector2(0f, 30f), new Vector2(1700f, 120f));
        title.characterSpacing = 24f;
        small = LundunUIBits.Label(root, "", 30f, new Vector2(0f, -50f), new Vector2(1400f, 50f));
        small.characterSpacing = 14f; small.color = new Color(1f, 0.66f, 0.18f);
        Image sk = LundunUIBits.Box(root, "Skip", Vector2.zero, new Vector2(220f, 76f), new Color(1f, 1f, 1f, 0.12f));
        RectTransform skr = sk.rectTransform; skr.anchorMin = skr.anchorMax = new Vector2(1f, 1f); skr.pivot = new Vector2(1f, 1f); skr.anchoredPosition = new Vector2(-40f, -40f);
        if (LundunHudArt.Pill != null) { sk.sprite = LundunHudArt.Pill; sk.type = Image.Type.Sliced; sk.pixelsPerUnitMultiplier = 0.8f; }
        Button b = sk.gameObject.AddComponent<Button>(); b.targetGraphic = sk;
        b.onClick.AddListener(() => { if (Time.time - startedAt > 1.5f) skip = true; });
        TextMeshProUGUI st = LundunUIBits.Label(skr, "SKIP  ›", 30f, Vector2.zero, new Vector2(220f, 76f));
        st.color = new Color(1f, 1f, 1f, 0.8f);
    }

    void Clear()
    {
        foreach (GameObject g in props) if (g != null) Object.Destroy(g);
        props.Clear(); spin.Clear();
        lead = null; chaser = null;
    }

    void Finish()
    {
        if (done) return;
        done = true;
        Clear();
        if (ui != null) Object.Destroy(ui);
        ui = null;
        foreach (Canvas c in hidden) if (c != null) c.enabled = true;
        hidden.Clear();
        if (follow != null) follow.enabled = true;
        LundunClock c2 = LundunClock.Instance;
        if (c2 != null && hourWas >= 0f) { c2.SetHour(hourWas); hourWas = -1f; }
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        if (vs != null) vs.LockControls(false);
    }
}

/// Phase 5 (gate A2): Hollis holds his phone up to James (M05). What's on it depends on what the cameras got:
///   Basketful    the still the till camera kept in M02 (if it wasn't shot out)
///   the station  else the still Brixtun Station's camera kept in M03 (if it saw him go in)
///   live         else (both dealt with, or nothing on file) a camera on Tower Plaza, live, pointed at James right now
/// Which one is decided by Pick() (call it for the lines before, so they match), and played by this step.
public class LundunHollisPhone : LundunStep
{
    public enum Footage { Basketful, Station, Live }
    readonly Func<Footage> which;
    readonly Func<Vector3> hollisAt;
    bool done;

    public LundunHollisPhone(Func<Footage> footage, Func<Vector3> hollis) { which = footage; hollisAt = hollis; objective = ""; }

    public static Footage Pick()
    {
        if (LundunCctvFeed.HasStill(M02EasyMoney.Evidence)) return Footage.Basketful;
        if (LundunCctvFeed.HasStill(M03NoWayHome.Evidence)) return Footage.Station;
        return Footage.Live;
    }

    public override void Begin(LundunMissionRun run)
    {
        done = false;
        LundunCctvFeed f = LundunCctvFeed.Instance;
        if (f == null) { done = true; return; }
        f.StartCoroutine(Play());
    }

    System.Collections.IEnumerator Play()
    {
        Footage w = which();
        if (w == Footage.Basketful)
            yield return LundunCctvFeed.Playback(M02EasyMoney.Evidence, "BASKETFUL  ·  CAM 02  ·  TILLS", "HOLLIS'S PHONE  ·  BASKETFUL, THE NIGHT OF THE ROBBERY", 6f, "SUBJECT 01  ·  ID MATCH", true);
        else if (w == Footage.Station)
            yield return LundunCctvFeed.Playback(M03NoWayHome.Evidence, "BRIXTUN STATION  ·  CAM 01  ·  ENTRANCE", "HOLLIS'S PHONE  ·  BRIXTUN STATION, THE SAME NIGHT", 6f, "SUBJECT 01  ·  ID MATCH", true);
        else
        {
            // a camera up a lamp post on the plaza, behind Hollis, looking at James
            Transform j = LundunMissionRun.James();
            Vector3 jp = j != null ? j.position : hollisAt();
            Vector3 h = hollisAt();
            Vector3 away = h - jp; away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            Vector3 lens = jp + away * 11f + Vector3.Cross(Vector3.up, away) * 4f + Vector3.up * 6.5f;
            yield return LundunCctvFeed.Scripted(lens, () => { Transform jj = LundunMissionRun.James(); return (jj != null ? jj.position : jp) + Vector3.up * 1.1f; },
                "CANARY QUAY  ·  CAM 0412  ·  TOWER PLAZA", "HOLLIS'S PHONE  ·  LIVE", 6f, LundunCctvFeed.Mark.Subject,
                () => { Transform jj = LundunMissionRun.James(); return (jj != null ? jj.position : jp) + Vector3.up * 1f; }, "SUBJECT 01", null, true);
        }
        done = true;
    }

    public override bool Tick(LundunMissionRun run) { return done; }
}
