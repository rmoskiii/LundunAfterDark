using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// G8a: stealing a parked car, GTA-style. Any parked car can already be taken (v53 swaps the static one for a real one
/// as James walks up); now most of them are locked:
///   • the button says BREAK IN. James goes to the driver's door (on the right: UK cars) and works the lock for about
///     two seconds: a few metal clicks, then the window goes in (glass everywhere) or the lock pops
///   • then the alarm may go off: the horn wails and the hazards flash for 20 seconds, even as he drives away
///   • and someone may call it in (one star), only if somebody actually sees or hears it, or a camera or the police
///     do (WantedSystem decides, as for shoplifting). By day that's likely, especially with the alarm going; at night,
///     on empty streets, it's rare
///   • about one car in four is left unlocked (more at night): STEAL, and James just gets in, as before
/// Traffic with a driver is a carjack as before; story mission cars, police cars and James's own car are never locked.
/// During a story mission nobody calls it in (so a quick car never fails a mission), but the alarm still goes.
/// VehicleSystem adds this by itself; nothing to set up.
public class LundunCarTheft : MonoBehaviour
{
    public static LundunCarTheft Instance { get; private set; }

    const float BreakSeconds = 2.0f, AlarmSeconds = 20f;

    bool busy;
    GameObject ui;
    RectTransform fill;
    TextMeshProUGUI label;

    // ------------------------------------------------------------------ the rules
    /// Rolled each time a car parks: is it locked? About 1 in 5 left open by day, 1 in 3 at night.
    public static bool RollLocked()
    {
        float open = Mathf.Lerp(0.2f, 0.35f, LundunCarLamps.Night());
        return Random.value >= open;
    }

    /// Would James have to break into this car?
    public static bool IsLocked(CarController car)
    {
        if (car == null) return false;
        LundunTrafficCar t = car.GetComponent<LundunTrafficCar>();
        return t != null && t.CurrentMode == LundunTrafficCar.Mode.Parked && t.Locked;
    }

    void Awake()
    {
        Instance = this;
        VehicleSystem.BeforeEnter = Allow;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (VehicleSystem.BeforeEnter == (System.Func<CarController, bool>)Allow) VehicleSystem.BeforeEnter = null;
        VehicleSystem.BreakingIn = false;
    }

    /// VehicleSystem asks before James gets in: open cars yes; a locked one: not yet, break in first.
    bool Allow(CarController car)
    {
        if (!IsLocked(car)) return true;
        if (!busy) StartCoroutine(BreakIn(car));
        return false;
    }

    // ------------------------------------------------------------------ breaking in
    IEnumerator BreakIn(CarController car)
    {
        busy = true;
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        PlayerHealth ph = PlayerHealth.Instance;
        Transform body = ph != null ? ph.Body : null;
        if (vs == null || body == null) { busy = false; yield break; }
        VehicleSystem.BreakingIn = true;
        PlayerController pc = body.GetComponent<PlayerController>();
        PlayerCombat combat = body.GetComponent<PlayerCombat>();
        bool pcWas = pc != null && pc.enabled, combatWas = combat != null && combat.enabled;
        if (pc != null) pc.enabled = false;
        if (combat != null) combat.enabled = false;

        // to the driver's door (the right-hand side), facing the car
        Transform ct = car.transform;
        LundunCarShape shape = car.GetComponent<LundunCarShape>();
        float half = shape != null ? shape.Width * 0.5f : 0.95f;
        Vector3 door = ct.position + ct.right * (half + 0.55f) + ct.forward * 0.3f;
        door.y = GroundAt(door, body.position.y);
        Put(body, door, Quaternion.LookRotation(Flat(-ct.right), Vector3.up));
        Vector3 window = ct.position + ct.right * half + ct.forward * 0.3f + Vector3.up * 1.0f;

        ShowBar(true, "BREAKING IN");
        bool ok = true, clicked1 = false, clicked2 = false, clicked3 = false;
        for (float t = 0f; t < BreakSeconds; t += Time.deltaTime)
        {
            SetBar(t / BreakSeconds);
            if (!clicked1 && t > 0.25f) { clicked1 = true; AudioSource.PlayClipAtPoint(Click(), window, 0.7f); }
            if (!clicked2 && t > 0.85f) { clicked2 = true; AudioSource.PlayClipAtPoint(Click(), window, 0.7f); }
            if (!clicked3 && t > 1.4f) { clicked3 = true; AudioSource.PlayClipAtPoint(Click(), window, 0.75f); }
            // given up on: James hurt badly, BUSTED / WASTED, the car moved off or was taken
            if (ph.IsDead || VehicleSystem.Locked || car == null || !car.isActiveAndEnabled || car.IsDriven
                || !IsLocked(car) || (car.transform.position - door).sqrMagnitude > 6f * 6f) { ok = false; break; }
            yield return null;
        }
        ShowBar(false, "");
        if (pc != null) pc.enabled = pcWas;
        if (combat != null) combat.enabled = combatWas;
        VehicleSystem.BreakingIn = false;
        busy = false;
        if (!ok || car == null) yield break;

        // in: the window went, or the lock popped
        if (Random.value < 0.5f) { AudioSource.PlayClipAtPoint(Glass(), window, 1f); Shards(window, ct.right); }
        else AudioSource.PlayClipAtPoint(Clunk(), window, 0.9f);
        LundunTrafficCar traffic = car.GetComponent<LundunTrafficCar>();
        if (traffic != null) traffic.Locked = false;

        float night = LundunCarLamps.Night();
        bool alarm = Random.value < Mathf.Lerp(0.6f, 0.35f, night);
        if (alarm) StartCoroutine(Alarm(car));
        bool storyJob = LundunGame.IsStory && LundunMissions.Running;
        float callChance = alarm ? Mathf.Lerp(1f, 0.5f, night) : Mathf.Lerp(0.7f, 0.15f, night);
        if (!storyJob && Random.value < callChance) CrimeSystem.Report(CrimeType.CarTheft, door);

        vs.EnterThis(car);
    }

    IEnumerator Alarm(CarController car)
    {
        AudioSource src = car.gameObject.AddComponent<AudioSource>();
        src.clip = Wail(); src.loop = true; src.spatialBlend = 1f; src.volume = 0.85f;
        src.minDistance = 6f; src.maxDistance = 90f; src.rolloffMode = AudioRolloffMode.Linear; src.dopplerLevel = 0f;
        src.Play();
        LundunCarLamps lamps = car.GetComponent<LundunCarLamps>();
        if (lamps != null) lamps.Hazards(AlarmSeconds);
        float until = Time.time + AlarmSeconds;
        while (Time.time < until && car != null && car.gameObject.activeInHierarchy) yield return null;
        if (src != null) Destroy(src);
        if (lamps != null) lamps.Hazards(0f);
    }

    // ------------------------------------------------------------------ helpers
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    static float GroundAt(Vector3 p, float fallback)
    {
        int mask = ~LayerMask.GetMask("Vehicle", "Player", "NPC", "Ignore Raycast");
        if (Physics.Raycast(new Vector3(p.x, fallback + 2f, p.z), Vector3.down, out RaycastHit hit, 5f, mask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return fallback;
    }

    static void Put(Transform who, Vector3 pos, Quaternion rot)
    {
        CharacterController cc = who.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        who.SetPositionAndRotation(pos, rot);
        if (cc != null) cc.enabled = true;
    }

    static void Shards(Vector3 at, Vector3 outward)
    {
        for (int i = 0; i < 12; i++)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "WindowShard";
            Destroy(g.GetComponent<Collider>());                     // they only need to fall (nothing to trip on)
            g.transform.position = at + new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(-0.25f, 0.2f), Random.Range(-0.35f, 0.35f));
            g.transform.rotation = Random.rotation;
            g.transform.localScale = new Vector3(Random.Range(0.03f, 0.09f), Random.Range(0.03f, 0.08f), 0.008f);
            Renderer r = g.GetComponent<Renderer>();
            if (r != null)
            {
                if (shardMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
                    shardMat = new Material(sh != null ? sh : Shader.Find("Standard"));
                    shardMat.color = new Color(0.75f, 0.88f, 0.9f, 1f);
                    if (shardMat.HasProperty("_Smoothness")) shardMat.SetFloat("_Smoothness", 0.95f);
                }
                r.sharedMaterial = shardMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Rigidbody rb = g.AddComponent<Rigidbody>();
            rb.mass = 0.02f;
            rb.linearVelocity = outward * Random.Range(0.6f, 1.8f) + Vector3.up * Random.Range(0.2f, 1.2f);
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            Destroy(g, 1.2f);
        }
    }
    static Material shardMat;

    // ------------------------------------------------------------------ the progress bar
    void ShowBar(bool on, string text)
    {
        if (ui == null) BuildUI();
        ui.SetActive(on);
        if (on) { label.text = text; SetBar(0f); }
    }

    void SetBar(float k) { if (fill != null) fill.anchorMax = new Vector2(Mathf.Clamp01(k), 1f); }

    void BuildUI()
    {
        ui = new GameObject("CarTheftUI", typeof(RectTransform));
        ui.transform.SetParent(transform, false);
        Canvas c = ui.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 57;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        GameObject panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(ui.transform, false);
        RectTransform prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f); prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 230f); prt.sizeDelta = new Vector2(420f, 78f);
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.07f, 0.82f);
        if (LundunHudArt.Pill != null) { bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 1.2f; }
        bg.raycastTarget = false;

        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(panel.transform, false);
        RectTransform trt = (RectTransform)tg.transform;
        trt.anchorMin = new Vector2(0f, 0.42f); trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        label = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) label.font = LundunHud.Instance.FontAsset;
        label.fontSize = 26; label.fontStyle = FontStyles.Bold; label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white; label.raycastTarget = false;

        GameObject track = new GameObject("Track", typeof(RectTransform));
        track.transform.SetParent(panel.transform, false);
        RectTransform krt = (RectTransform)track.transform;
        krt.anchorMin = new Vector2(0.08f, 0.16f); krt.anchorMax = new Vector2(0.92f, 0.34f); krt.offsetMin = krt.offsetMax = Vector2.zero;
        Image ki = track.AddComponent<Image>(); ki.color = new Color(1f, 1f, 1f, 0.15f); ki.raycastTarget = false;

        GameObject fg = new GameObject("Fill", typeof(RectTransform));
        fg.transform.SetParent(track.transform, false);
        fill = (RectTransform)fg.transform;
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.offsetMin = fill.offsetMax = Vector2.zero;
        Image fi = fg.AddComponent<Image>(); fi.color = new Color(0.95f, 0.75f, 0.2f, 1f); fi.raycastTarget = false;
        ui.SetActive(false);
    }

    // ------------------------------------------------------------------ sounds (made here: no audio files)
    const int Rate = 22050;
    static AudioClip click, clunk, glass, wail;

    static AudioClip Click()
    {
        if (click != null) return click;
        int n = (int)(Rate * 0.09f); float[] d = new float[n];
        System.Random r = new System.Random(3);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate, env = Mathf.Exp(-t * 70f);
            d[i] = env * (0.55f * Mathf.Sin(2f * Mathf.PI * 3100f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 5200f * t) + 0.25f * (float)(r.NextDouble() * 2 - 1));
        }
        click = AudioClip.Create("Theft_Click", n, 1, Rate, false); click.SetData(d, 0);
        return click;
    }

    static AudioClip Clunk()
    {
        if (clunk != null) return clunk;
        int n = (int)(Rate * 0.22f); float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate, env = Mathf.Exp(-t * 28f);
            d[i] = env * (0.7f * Mathf.Sin(2f * Mathf.PI * 140f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 410f * t));
        }
        clunk = AudioClip.Create("Theft_Clunk", n, 1, Rate, false); clunk.SetData(d, 0);
        return clunk;
    }

    static AudioClip Glass()
    {
        if (glass != null) return glass;
        int n = (int)(Rate * 0.9f); float[] d = new float[n];
        System.Random r = new System.Random(11);
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float crash = Mathf.Exp(-t * 9f) * (float)(r.NextDouble() * 2 - 1) * 0.8f;               // the smash
            float tinkle = 0f;
            for (int k = 0; k < 5; k++)                                                           // bits landing
            {
                float s = 0.12f + k * 0.13f, dt = t - s;
                if (dt > 0f) tinkle += Mathf.Exp(-dt * 45f) * Mathf.Sin(2f * Mathf.PI * (3500f + k * 900f) * dt) * 0.35f;
            }
            d[i] = Mathf.Clamp(crash + tinkle, -1f, 1f);
        }
        glass = AudioClip.Create("Theft_Glass", n, 1, Rate, false); glass.SetData(d, 0);
        return glass;
    }

    /// The car alarm: a horn-ish two-tone that wails up and down (loops cleanly every 2 s).
    static AudioClip Wail()
    {
        if (wail != null) return wail;
        int n = Rate * 2; float[] d = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float f = t < 1f ? Mathf.Lerp(650f, 1250f, t) : ((int)(t * 8f) % 2 == 0 ? 1250f : 850f);   // a sweep, then a warble
            phase += 2.0 * System.Math.PI * f / Rate;
            float s = (float)System.Math.Sin(phase);
            d[i] = Mathf.Clamp(s * 2.2f, -1f, 1f) * 0.5f;                                         // squared off, like a horn
        }
        wail = AudioClip.Create("Theft_Alarm", n, 1, Rate, false); wail.SetData(d, 0);
        return wail;
    }
}
