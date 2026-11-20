using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// Phase 5 (gate A1): M01's cold open. About 30 seconds of the ending before the beginning: black, "THREE WEEKS LATER",
/// then Kingsmere Palace at midnight: police lights at the gates, sirens, Hollis's helicopter on the roof, the camera
/// craning up to it while James and Hollis are heard. Then "THREE WEEKS EARLIER" and back to James's front door.
///   the HUD and controls are hidden throughout; the camera's flown by hand (CameraFollow is paused)
///   SKIP, top right, after a second and a half
///   everything it puts at the palace is gone afterwards, and the clock goes back to what it was
public class LundunColdOpen : LundunStep
{
    const float Shots = 21f;                                   // the palace part (the cards add ~5 s)

    enum Phase { Later, Palace, Earlier, Done }
    Phase phase;
    float t, startedAt;
    bool skip;
    float hourWas = -1f;
    GameObject ui, lettersGo;
    TextMeshProUGUI letters, sub;
    Image black;
    readonly List<Canvas> hidden = new List<Canvas>();
    readonly List<GameObject> props = new List<GameObject>();
    CameraFollow follow;
    Camera cam;
    Vector3 gate, palace, roof;
    int said;

    public LundunColdOpen() { objective = ""; }

    public override void Begin(LundunMissionRun run)
    {
        phase = Phase.Later; t = 0f; startedAt = Time.time; skip = false; said = 0;
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        if (vs != null) vs.LockControls(true);
        follow = Object.FindAnyObjectByType<CameraFollow>();
        cam = Camera.main;
        LundunClock c = LundunClock.Instance;
        if (c != null) { hourWas = c.Hour; c.SetHour(23.9f); }

        gate = LundunMissionRun.Ground(new Vector3(616f, 0f, 1700f));
        palace = LundunPalaceWays.PalaceCentre; palace.y = gate.y;
        roof = Roof(new Vector3(625f, 0f, 1790f));
        Stage();
        BuildUI();
        Show("THREE WEEKS LATER", "KINGSMERE PALACE  ·  23:58");
    }

    public override bool Tick(LundunMissionRun run)
    {
        t += Time.deltaTime;
        if (skip && phase != Phase.Done) { Finish(); return true; }
        switch (phase)
        {
            case Phase.Later:
                if (t > 2.4f) { phase = Phase.Palace; t = 0f; if (follow != null) follow.enabled = false; letters.text = ""; sub.text = ""; }
                black.color = new Color(0f, 0f, 0f, 1f);
                break;
            case Phase.Palace:
                black.color = new Color(0f, 0f, 0f, Mathf.Clamp01(1f - t / 0.8f) + Mathf.Clamp01((t - (Shots - 1f)) / 1f));
                Fly(t);
                Lines(t);
                if (t > Shots) { phase = Phase.Earlier; t = 0f; Show("THREE WEEKS EARLIER", "RYE LANE  ·  20:40"); Restore(); }
                break;
            case Phase.Earlier:
                black.color = new Color(0f, 0f, 0f, t < 2.2f ? 1f : Mathf.Clamp01(1f - (t - 2.2f) / 0.8f));
                if (t > 2.2f) { letters.text = ""; sub.text = ""; }
                if (t > 3f) { phase = Phase.Done; return true; }
                break;
            case Phase.Done:
                return true;
        }
        return false;
    }

    public override void End(LundunMissionRun run) { Finish(); }

    /// The camera's path: low at the gates looking up through the police lights, then craning up the front to the roof
    /// and round the helicopter, then out wide over the palace.
    void Fly(float s)
    {
        if (cam == null) return;
        Vector3 toPalace = palace - gate; toPalace.y = 0f; toPalace.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toPalace);
        Vector3 p, look;
        if (s < 7f)
        {
            float k = Ease(s / 7f);
            p = gate - toPalace * Mathf.Lerp(26f, 12f, k) + side * 6f + Vector3.up * Mathf.Lerp(1.6f, 3f, k);
            look = Vector3.Lerp(gate + Vector3.up * 2f, palace + Vector3.up * 18f, k);
        }
        else if (s < 14f)
        {
            float k = Ease((s - 7f) / 7f);
            Vector3 a = gate - toPalace * 12f + side * 6f + Vector3.up * 3f;
            Vector3 b = roof - toPalace * 16f - side * 10f + Vector3.up * 5f;
            p = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 10f;
            look = Vector3.Lerp(palace + Vector3.up * 18f, roof + Vector3.up * 1.5f, k);
        }
        else
        {
            float k = Ease((s - 14f) / (Shots - 14f));
            float ang = Mathf.Lerp(-40f, 30f, k) * Mathf.Deg2Rad;
            Vector3 round = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * (-toPalace);
            p = roof + round * Mathf.Lerp(16f, 46f, k) + Vector3.up * Mathf.Lerp(5f, 24f, k);
            look = roof + Vector3.up * 1.5f;
        }
        cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(look - p, Vector3.up));
    }

    void Lines(float s)
    {
        if (said == 0 && s > 2.5f) { said++; LundunMissions.Say("JAMES", "Three weeks ago I was two payments behind on a car.", 4f); }
        if (said == 1 && s > 8f) { said++; LundunMissions.Say("JAMES", "Now every police car in Lundun is outside a palace. And I'm on the roof.", 4.5f); }
        if (said == 2 && s > 14.5f) { said++; LundunMissions.Say("HOLLIS", "You've no idea who else is in that book.", 4f); }
    }

    static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

    static Vector3 Roof(Vector3 p)
    {
        if (Physics.Raycast(new Vector3(p.x, 90f, p.z), Vector3.down, out RaycastHit h, 120f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore) && h.point.y > 12f)
            return h.point;
        return new Vector3(p.x, LundunPlaces.Ground(p).y + LundunPalaceWays.PalaceTop, p.z);
    }

    // ------------------------------------------------------------------ the palace dressed for the night
    void Stage()
    {
        LundunFlyHeli heli = LundunFlyHeli.Spawn(roof, 90f);
        if (heli != null) props.Add(heli.gameObject);
        PoliceAI prefab = PoliceDirector.CarPrefab;
        if (prefab == null) return;
        Vector3 toPalace = palace - gate; toPalace.y = 0f; toPalace.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toPalace);
        Vector3[] spots = { gate - toPalace * 4f - side * 5f, gate - toPalace * 7f + side * 4f, gate - toPalace * 13f - side * 1.5f, gate - toPalace * 9f + side * 11f };
        float[] turn = { 35f, -50f, 80f, -20f };
        bool loud = true;
        for (int i = 0; i < spots.Length; i++)
        {
            if (!NavMesh.SamplePosition(spots[i] + Vector3.up, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;
            PoliceAI car = Object.Instantiate(prefab, hit.position + Vector3.up * 0.8f, Quaternion.LookRotation(Quaternion.Euler(0f, turn[i], 0f) * toPalace));
            car.enabled = false;                                          // parked for the shot: no driving, no officers
            NavMeshAgent ag = car.GetComponent<NavMeshAgent>(); if (ag != null) ag.enabled = false;
            Rigidbody rb = car.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
            foreach (BlinkingLight b in car.GetComponentsInChildren<BlinkingLight>(true)) { b.CancelInvoke(); b.InvokeRepeating("Toggle", UnityEngine.Random.Range(0f, 0.6f), 0.6f); }
            foreach (AudioSource a in car.GetComponentsInChildren<AudioSource>(true))
                if (loud && a.clip != null && a.clip.name.ToLowerInvariant().Contains("siren")) { a.loop = true; a.spatialBlend = 0.2f; a.Play(); loud = false; }   // one siren is plenty
            props.Add(car.gameObject);
        }
    }

    // ------------------------------------------------------------------ the black, the words, SKIP
    void BuildUI()
    {
        // everything else on screen goes for the length of it, except the mission's own (the subtitles), drawn on top
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (c.isRootCanvas && c.enabled && c.renderMode == RenderMode.ScreenSpaceOverlay && c.GetComponentInParent<LundunMissions>() == null) { c.enabled = false; hidden.Add(c); }

        ui = new GameObject("ColdOpen", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 69;   // just under the subtitles
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)ui.transform;

        // letterbox bars, then the black that fades
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

        letters = LundunUIBits.Label(root, "", 84f, new Vector2(0f, 30f), new Vector2(1600f, 120f));
        letters.characterSpacing = 24f;
        sub = LundunUIBits.Label(root, "", 30f, new Vector2(0f, -50f), new Vector2(1400f, 50f));
        sub.characterSpacing = 14f; sub.color = new Color(1f, 0.66f, 0.18f);

        Image sk = LundunUIBits.Box(root, "Skip", new Vector2(0f, 0f), new Vector2(220f, 76f), new Color(1f, 1f, 1f, 0.12f));
        RectTransform skr = sk.rectTransform; skr.anchorMin = skr.anchorMax = new Vector2(1f, 1f); skr.pivot = new Vector2(1f, 1f); skr.anchoredPosition = new Vector2(-40f, -40f);
        if (LundunHudArt.Pill != null) { sk.sprite = LundunHudArt.Pill; sk.type = Image.Type.Sliced; sk.pixelsPerUnitMultiplier = 0.8f; }
        Button b = sk.gameObject.AddComponent<Button>(); b.targetGraphic = sk;
        b.onClick.AddListener(() => { if (Time.time - startedAt > 1.5f) skip = true; });
        TextMeshProUGUI st = LundunUIBits.Label(skr, "SKIP  ›", 30f, Vector2.zero, new Vector2(220f, 76f));
        st.color = new Color(1f, 1f, 1f, 0.8f);
    }

    void Show(string big, string small)
    {
        if (letters != null) letters.text = big;
        if (sub != null) sub.text = small;
    }

    /// The palace props go, the camera and the clock come back (the cards can still be showing).
    void Restore()
    {
        foreach (GameObject g in props) if (g != null) Object.Destroy(g);
        props.Clear();
        if (follow != null) follow.enabled = true;
        LundunClock c = LundunClock.Instance;
        if (c != null && hourWas >= 0f) { c.SetHour(hourWas); hourWas = -1f; }
    }

    void Finish()
    {
        if (phase == Phase.Done && ui == null) return;
        phase = Phase.Done;
        Restore();
        if (ui != null) Object.Destroy(ui);
        ui = null;
        foreach (Canvas c in hidden) if (c != null) c.enabled = true;
        hidden.Clear();
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        if (vs != null) vs.LockControls(false);
    }
}

/// Phase 5 (gate A1): M01's parcel snatch. One of the Kings grabs the parcel and legs it across the estate: sprint
/// after him and tackle him (reach him). He goes down hard, the parcel spills, and the fight's on right there (him and
/// the mate who was coming after you). Fails if he gets clean away.
public class LundunSnatch : LundunStep
{
    readonly Func<Vector3> from;
    public LundunRunner Runner { get; private set; }
    public Vector3 DroppedAt { get; private set; }
    public readonly List<Health> Fighters = new List<Health>();
    GameObject parcel;
    float farFor;

    public override Transform Follow { get { return Runner != null ? Runner.transform : null; } }

    public LundunSnatch(Func<Vector3> startAt, string objectiveText) { from = startAt; objective = objectiveText; }

    public override void Begin(LundunMissionRun run)
    {
        farFor = 0f; Fighters.Clear();
        LundunPopulation pop = LundunPopulation.Instance;
        Vector3 at = LundunMissionRun.Ground(from());
        if (pop == null || !NavMesh.SamplePosition(at + Vector3.up, out NavMeshHit hit, 4f, NavMesh.AllAreas)) { DroppedAt = at; return; }
        GameObject g = pop.NewPerson(null, "Street");
        if (g == null) { DroppedAt = at; return; }
        NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
        g.transform.SetPositionAndRotation(hit.position, Quaternion.identity);
        g.SetActive(true);
        run.Spawn(g);
        Runner = g.AddComponent<LundunRunner>();
        Runner.speed = 5.9f;
        Runner.Flee();
        // the parcel under his arm
        parcel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        parcel.name = "Mission_parcel";
        Object.Destroy(parcel.GetComponent<Collider>());
        parcel.transform.SetParent(g.transform, false);
        parcel.transform.localPosition = new Vector3(0.28f, 1.05f, 0.15f);
        parcel.transform.localScale = new Vector3(0.38f, 0.26f, 0.3f);
        Renderer r = parcel.GetComponent<Renderer>(); if (r != null) r.sharedMaterial = LundunMissions.ParcelMaterial;
        Health h = g.GetComponent<Health>(); if (h != null) Fighters.Add(h);
        LundunMissions.Mark(Fighters, true);
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        if (mm != null) mm.Track(g.transform, new Color(1f, 0.35f, 0.3f), "", 16f, true);
    }

    public override bool Tick(LundunMissionRun run)
    {
        if (Runner == null) return true;                                   // nobody to chase (no crowd): straight to the fight
        Transform j = LundunMissionRun.James();
        if (Runner.Caught || (j != null && LundunMissionRun.FlatDistance(Runner.transform.position, j.position) < 1.9f)) { Tackle(run); return true; }
        if (j != null && LundunMissionRun.FlatDistance(Runner.transform.position, j.position) > 90f) farFor += Time.deltaTime; else farFor = 0f;
        if (farFor > 8f) run.Fail("He got away with the parcel");
        return false;
    }

    void Tackle(LundunMissionRun run)
    {
        Runner.Stand();
        Health h = Runner.GetComponent<Health>();
        Transform j = LundunMissionRun.James();
        Vector3 push = j != null ? Runner.transform.position - j.position : Runner.transform.forward; push.y = 0f;
        if (h != null) h.TakeDamage(h.Max * 0.3f, Runner.transform.position + Vector3.up, push.normalized * 4f);
        if (parcel != null)
        {
            parcel.transform.SetParent(null, true);
            DroppedAt = LundunMissionRun.Ground(Runner.transform.position + push.normalized * 1.2f);
            Object.Destroy(parcel);
        }
        else DroppedAt = LundunMissionRun.Ground(Runner.transform.position);
        LundunHaptics.Heavy();
        CameraFollow cf = Object.FindAnyObjectByType<CameraFollow>(); if (cf != null) cf.AddShake(0.6f);
        LundunMissions.Toast("TACKLED\nThe parcel's on the floor");
        // his mate, who'd been coming after you
        LundunPopulation pop = LundunPopulation.Instance;
        Vector3 behind = DroppedAt + (j != null ? (j.position - DroppedAt).normalized * 9f : Vector3.forward * 9f);
        if (pop != null && NavMesh.SamplePosition(behind + Vector3.up, out NavMeshHit hit, 6f, NavMesh.AllAreas))
        {
            GameObject g = pop.NewPerson(null, "Street");
            if (g != null)
            {
                NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
                g.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(DroppedAt - hit.position));
                g.SetActive(true);
                run.Spawn(g);
                Health mh = g.GetComponent<Health>(); if (mh != null) Fighters.Add(mh);
            }
        }
        Object.Destroy(Runner);                                            // he fights now, not runs
        Runner = null;
    }

    public override void End(LundunMissionRun run)
    {
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        foreach (Health h in Fighters) if (h != null && mm != null) mm.Untrack(h.transform);
    }
}

/// Phase 5 (gate A1): the cash bag. After a robbery James carries a holdall on his back till the money's put away.
public static class LundunCashBag
{
    static GameObject bag;
    static Material mat;

    public static bool Carrying { get { return bag != null; } }

    public static void Carry(bool on)
    {
        if (!on) { if (bag != null) Object.Destroy(bag); bag = null; return; }
        if (bag != null) return;
        PlayerController pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (pc == null) return;
        Animator a = pc.GetComponentInChildren<Animator>(true);
        Transform chest = a != null && a.isHuman ? (a.GetBoneTransform(HumanBodyBones.Chest) ?? a.GetBoneTransform(HumanBodyBones.Spine)) : null;
        Transform onto = chest != null ? chest : pc.transform;
        bag = new GameObject("CashBag");
        bag.transform.SetParent(onto, false);
        // on his back: behind the chest bone (the model's back is its -forward)
        Vector3 back = -onto.InverseTransformDirection(pc.transform.forward);
        bag.transform.localPosition = chest != null ? back * 0.2f : new Vector3(0f, 1.25f, -0.24f);
        bag.transform.localRotation = Quaternion.Inverse(onto.rotation) * pc.transform.rotation;
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "CashBag" };
            Color c = new Color(0.08f, 0.09f, 0.1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            mat.color = c;
        }
        Piece(PrimitiveType.Capsule, new Vector3(0f, 0f, 0f), new Vector3(0.3f, 0.24f, 0.22f), Quaternion.Euler(0f, 0f, 90f));
        Piece(PrimitiveType.Cube, new Vector3(0f, 0.13f, 0.04f), new Vector3(0.26f, 0.025f, 0.04f), Quaternion.identity);   // the strap
    }

    static void Piece(PrimitiveType t, Vector3 at, Vector3 size, Quaternion rot)
    {
        GameObject g = GameObject.CreatePrimitive(t);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(bag.transform, false);
        g.transform.localPosition = at; g.transform.localRotation = rot; g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }
}
