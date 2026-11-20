using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// Phase 5 (gate A1): M01's cold open. About 30 seconds of the ending before the beginning: black, "THREE WEEKS LATER",
/// then Kingsmere Palace at midnight, and all of it going off: a ragged cordon of a dozen police cars across the gates,
/// every light bar strobing red and blue over the stone, sirens, armed officers behind the cars firing up at the roof,
/// a car burning (then going up), floodlights on the front, the police helicopter circling with its searchlight on the
/// roof, and Hollis's helicopter up there with its rotors turning. The camera runs low along the cordon (handheld),
/// cranes up the front to the roof, then pulls out wide over the lot while James and Hollis are heard. Then "THREE
/// WEEKS EARLIER" and back to James's front door.
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
    readonly List<Light> strobes = new List<Light>();
    readonly List<Transform> shooters = new List<Transform>();
    readonly List<Transform> spinning = new List<Transform>();
    Transform copter, copterBeam;
    VehicleHealth burner;
    float nextShot, copterAngle;
    bool burnt;
    CameraFollow follow;
    Camera cam;
    Vector3 gate, palace, roof;
    int said;

    public LundunColdOpen() { objective = ""; }

    public override void Begin(LundunMissionRun run)
    {
        phase = Phase.Later; t = 0f; startedAt = Time.time; skip = false; said = 0; burnt = false; nextShot = 0f; copterAngle = 0f;
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
                Chaos(t);
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

    /// The camera's path: low and handheld along the cordon, then craning up the front to the roof, then out wide.
    void Fly(float s)
    {
        if (cam == null) return;
        Vector3 toPalace = palace - gate; toPalace.y = 0f; toPalace.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toPalace);
        Vector3 p, look;
        if (s < 7f)
        {
            // low and handheld along the outside of the cordon, the lights in your face, the palace beyond
            float k = Ease(s / 7f);
            p = gate - toPalace * 15f + side * Mathf.Lerp(-16f, 10f, k) + Vector3.up * 1.5f;
            look = gate + toPalace * 20f + side * Mathf.Lerp(-6f, 4f, k) + Vector3.up * Mathf.Lerp(4f, 12f, k);
            p += Handheld(s, 0.12f);
        }
        else if (s < 14f)
        {
            // over the cordon and up the front to the roof
            float k = Ease((s - 7f) / 7f);
            Vector3 a = gate - toPalace * 15f + side * 10f + Vector3.up * 1.5f;
            Vector3 b = roof - toPalace * 16f - side * 10f + Vector3.up * 5f;
            p = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 12f;
            look = Vector3.Lerp(gate + toPalace * 20f + Vector3.up * 8f, roof + Vector3.up * 1.5f, k);
            p += Handheld(s, 0.05f);
        }
        else
        {
            // out wide: the roof, the helicopters, the cordon and its lights below
            float k = Ease((s - 14f) / (Shots - 14f));
            Vector3 round = Quaternion.AngleAxis(Mathf.Lerp(-40f, 30f, k), Vector3.up) * (-toPalace);
            p = roof + round * Mathf.Lerp(18f, 60f, k) + Vector3.up * Mathf.Lerp(6f, 30f, k);
            look = Vector3.Lerp(roof + Vector3.up * 1.5f, (roof + gate) * 0.5f, k * 0.6f);
        }
        cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(look - p, Vector3.up));
    }

    static Vector3 Handheld(float s, float amount)
    {
        return new Vector3(Mathf.PerlinNoise(s * 0.9f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, s * 1.1f) - 0.5f, Mathf.PerlinNoise(s * 0.8f, 1.9f) - 0.5f) * amount * 2f;
    }

    /// Every frame of the palace part: the strobes, the rotors, the police helicopter's circle and beam, the shots,
    /// and the car going up.
    void Chaos(float s)
    {
        for (int i = 0; i < strobes.Count; i++)
        {
            Light l = strobes[i]; if (l == null) continue;
            bool red = Mathf.Repeat(Time.time * 3.2f + i * 0.37f, 1f) < 0.5f;
            l.color = red ? new Color(1f, 0.08f, 0.06f) : new Color(0.1f, 0.25f, 1f);
            l.intensity = Mathf.Repeat(Time.time * 9f + i * 0.61f, 1f) < 0.55f ? 9f : 1.5f;
        }
        foreach (Transform r in spinning) if (r != null) r.Rotate(0f, 1300f * Time.deltaTime, 0f, Space.Self);
        if (copter != null)
        {
            copterAngle += 0.22f * Time.deltaTime;
            Vector3 c = palace + new Vector3(Mathf.Cos(copterAngle), 0f, Mathf.Sin(copterAngle)) * 48f;
            c.y = roof.y + 32f;
            Vector3 vel = new Vector3(-Mathf.Sin(copterAngle), 0f, Mathf.Cos(copterAngle));
            copter.SetPositionAndRotation(c, Quaternion.LookRotation(vel) * Quaternion.Euler(8f, 0f, -12f));
            if (copterBeam != null)
            {
                Vector3 onRoof = roof + new Vector3(Mathf.Sin(Time.time * 0.7f) * 6f, 0f, Mathf.Cos(Time.time * 0.5f) * 5f);
                copterBeam.rotation = Quaternion.LookRotation(onRoof - copterBeam.position);
            }
        }
        if (Time.time >= nextShot && shooters.Count > 0)
        {
            nextShot = Time.time + UnityEngine.Random.Range(0.12f, 0.5f);
            Transform sh = shooters[UnityEngine.Random.Range(0, shooters.Count)];
            if (sh != null)
            {
                LundunCombatFX.Muzzle(sh, 0.9f, 1);
                AudioClip clip = LundunGunAudio.Shot(UnityEngine.Random.value < 0.6f ? LundunWeapons.Kind.Carbine : LundunWeapons.Kind.Pistol);
                if (clip != null) AudioSource.PlayClipAtPoint(clip, sh.position, 0.55f);
            }
        }
        if (!burnt && burner != null && s > 1.2f)
        {
            burnt = true;
            burner.TakeDamage(burner.Max * 5f);                                  // it burns, then it goes up
        }
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
        Vector3 toPalace = palace - gate; toPalace.y = 0f; toPalace.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, toPalace);
        int cars = 0, officers = 0;

        // Hollis's helicopter on the roof, rotors turning
        LundunFlyHeli hollis = LundunFlyHeli.Spawn(roof, 90f);
        if (hollis != null) { props.Add(hollis.gameObject); Transform r = Find(hollis.transform, "Rotor"); if (r != null) spinning.Add(r); }

        // the cordon: a dozen cars in a ragged double line across the approach (on the ground; no navmesh needed)
        PoliceAI prefab = PoliceDirector.CarPrefab;
        int sirens = 0;
        for (int i = 0; i < 12; i++)
        {
            float lateral = -17f + i * 3.1f + UnityEngine.Random.Range(-0.8f, 0.8f);
            float back = (i % 2 == 0 ? 4f : 9f) + UnityEngine.Random.Range(-1f, 1f);
            Vector3 at = LundunMissionRun.Ground(gate - toPalace * back + side * lateral);
            Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, (i % 2 == 0 ? 1f : -1f) * UnityEngine.Random.Range(35f, 80f), 0f) * toPalace);
            GameObject car = null;
            if (prefab != null)
            {
                PoliceAI ai = Object.Instantiate(prefab, at + Vector3.up * 0.8f, rot);
                ai.enabled = false;                                            // parked for the shot: no driving, no officers
                car = ai.gameObject;
                foreach (BlinkingLight bl in car.GetComponentsInChildren<BlinkingLight>(true)) { bl.CancelInvoke(); bl.InvokeRepeating("Toggle", UnityEngine.Random.Range(0f, 0.6f), 0.6f); }
                foreach (AudioSource a in car.GetComponentsInChildren<AudioSource>(true))
                    if (sirens < 3 && a.clip != null && a.clip.name.ToLowerInvariant().Contains("siren"))
                    { a.loop = true; a.spatialBlend = 0.6f; a.pitch = 0.94f + sirens * 0.06f; a.volume = 0.8f; a.Play(); sirens++; }
            }
            else
            {
                LundunPopulation pop = LundunPopulation.Instance;
                car = pop != null ? pop.NewCar(null, at + Vector3.up * 0.6f, rot, i) : null;
                if (car != null) car.SetActive(true);
            }
            if (car == null) continue;
            Still(car);
            props.Add(car);
            cars++;
            // the light bar's glow on everything round it
            Light l = new GameObject("Strobe").AddComponent<Light>();
            l.transform.SetParent(car.transform, false); l.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            l.type = LightType.Point; l.range = 14f; l.shadows = LightShadows.None;
            strobes.Add(l);
        }

        // armed officers between the cars, guns up at the roof (they don't move; their shots are flashes and noise)
        PoliceOfficer officerPrefab = prefab != null ? prefab.OfficerPrefab : null;
        for (int i = 0; i < 7; i++)
        {
            Vector3 at = LundunMissionRun.Ground(gate - toPalace * 1.5f + side * (-13f + i * 4.3f));
            Quaternion face = Quaternion.LookRotation(toPalace);
            GameObject o;
            if (officerPrefab != null)
            {
                PoliceOfficer po = Object.Instantiate(officerPrefab, at, face);
                po.enabled = false;
                o = po.gameObject;
            }
            else
            {
                LundunPopulation pop = LundunPopulation.Instance;
                o = pop != null ? pop.NewPerson(null) : null;
                if (o == null) continue;
                o.transform.SetPositionAndRotation(at, face);
                o.SetActive(true);
            }
            Still(o);
            props.Add(o);
            officers++;
            Transform muzzle = new GameObject("Muzzle").transform;              // where his flashes come from
            muzzle.SetParent(o.transform, false);
            muzzle.localPosition = new Vector3(0.25f, 1.45f, 0.7f);
            muzzle.localRotation = Quaternion.Euler(-35f, 0f, 0f);             // up at the roof
            shooters.Add(muzzle);
        }

        // a car on fire against the gates (it goes up a few seconds in)
        LundunPopulation pp = LundunPopulation.Instance;
        if (pp != null)
        {
            GameObject wreck = pp.NewCar(null, LundunMissionRun.Ground(gate - toPalace * 6f + side * 19f) + Vector3.up * 0.6f, Quaternion.LookRotation(Quaternion.Euler(0f, 110f, 0f) * toPalace), 3);
            if (wreck != null)
            {
                wreck.SetActive(true);
                Still(wreck);
                burner = wreck.GetComponent<VehicleHealth>();
                props.Add(wreck);
            }
        }

        // floodlights up the front of the palace
        for (int i = -1; i <= 1; i += 2)
        {
            Light f = new GameObject("Floodlight").AddComponent<Light>();
            f.transform.position = LundunMissionRun.Ground(gate - toPalace * 3f + side * (i * 11f)) + Vector3.up * 3f;
            f.transform.rotation = Quaternion.LookRotation(palace + Vector3.up * 14f - f.transform.position);
            f.type = LightType.Spot; f.range = 90f; f.spotAngle = 55f; f.intensity = 30f; f.color = new Color(1f, 0.96f, 0.88f);
            f.shadows = LightShadows.None;
            props.Add(f.gameObject);
        }

        // the police helicopter, circling, its searchlight on the roof
        VehicleHealth template = prefab != null ? prefab.GetComponent<VehicleHealth>() : null;
        LundunHeli ph = LundunHeli.Spawn(roof, template);
        if (ph != null)
        {
            ph.enabled = false;                                                // flown by this, not by the police
            copter = ph.transform;
            Transform r = Find(copter, "Rotor"); if (r != null) spinning.Add(r);
            copterBeam = Find(copter, "Searchlight");
            props.Add(ph.gameObject);
        }
        Debug.Log("[Lundun] Cold open staged: " + cars + " police cars, " + officers + " officers, " + strobes.Count + " strobes, police helicopter " + (ph != null) + ", burning car " + (burner != null));
    }

    /// Nothing in the shot moves by itself: no physics, no walking.
    static void Still(GameObject g)
    {
        foreach (Rigidbody rb in g.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        foreach (NavMeshAgent ag in g.GetComponentsInChildren<NavMeshAgent>(true)) ag.enabled = false;
        foreach (NPCBrain b in g.GetComponentsInChildren<NPCBrain>(true)) b.enabled = false;
        foreach (CarController c in g.GetComponentsInChildren<CarController>(true)) c.enabled = false;
    }

    static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
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
        props.Clear(); strobes.Clear(); shooters.Clear(); spinning.Clear();
        copter = null; copterBeam = null; burner = null;
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
