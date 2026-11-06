using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// S8: a helicopter James can fly (missions only for now: side mission 15, and the finale later). Built in code like
/// the police one (an original design: charcoal with an orange stripe), on the ground until James gets in.
/// Arcade hover controls, its own on-screen controls while flying:
///   left stick      fly forwards / backwards / sideways (relative to where it's facing)
///   drag the right  turn it (left / right) and tilt the view (up / down)
///   UP / DOWN       climb and descend; let go and it holds its height
/// The rotors take a moment to spin up. There's a ceiling. Hitting things hurts it (its health on the bar at the top);
/// wrecked = the mission fails. It sits on the ground when it's down (skids), and only lets James out when landed (and
/// when the mission allows it). While he flies, James rides along inside (hidden): the police chase where he is.
public class LundunFlyHeli : MonoBehaviour
{
    static readonly List<LundunFlyHeli> all = new List<LundunFlyHeli>();
    /// The one James is in (null if none).
    public static LundunFlyHeli Flying { get; private set; }

    const float MaxSpeed = 30f, Accel = 11f, Climb = 8f, Sink = 7f, CeilingAboveGround = 110f, MaxHealth = 500f, TurnRate = 0.16f;

    public bool Occupied { get; private set; }
    public bool Landed { get; private set; }
    public bool Ready { get { return spin >= 0.95f; } }
    public float HealthFraction { get { return vh != null && vh.Max > 0f ? Mathf.Clamp01(vh.Current / vh.Max) : 1f; } }
    public bool Wrecked { get { return vh != null && vh.IsWrecked; } }
    public float HeightAboveGround { get; private set; }
    /// The mission says whether James can get out (false until he's landed where he should).
    public bool CanLeave = false;
    /// Set by a mission: shown under the controls (null = the built-in tips).
    public string Hint;

    private Transform model, rotor, tailRotor;
    private Rigidbody rb;
    private VehicleHealth vh;
    private AudioSource rotorAudio;
    private Vector3 velocity;
    private float yaw, spin, airborneAt = -1f;
    private int groundMask;

    // the controls
    private GameObject ui;
    private LundunFlyStick stick;
    private HoldButton up, down;
    private Image hpFill;
    private TextMeshProUGUI altText, hintText, upLabel;
    private GameObject getOut;

    // ------------------------------------------------------------------ making one
    /// A helicopter standing on the ground at 'at', facing 'yawDeg'.
    public static LundunFlyHeli Spawn(Vector3 at, float yawDeg)
    {
        GameObject go = new GameObject("Helicopter");
        go.SetActive(false);
        go.transform.SetPositionAndRotation(at + Vector3.up * 1.2f, Quaternion.Euler(0f, yawDeg, 0f));
        LundunFlyHeli h = go.AddComponent<LundunFlyHeli>();
        h.Build();
        h.yaw = yawDeg;
        go.SetActive(true);
        return h;
    }

    void Build()
    {
        groundMask = LayerMask.GetMask("Default");
        Material body = Lit(new Color(0.11f, 0.115f, 0.125f), 0.65f);
        Material stripe = Lit(new Color(1f, 0.45f, 0.1f), 0.4f);
        Material glass = Lit(new Color(0.06f, 0.09f, 0.12f), 0.95f);
        Material dark = Lit(new Color(0.03f, 0.03f, 0.035f), 0.3f);

        model = new GameObject("Model").transform;
        model.SetParent(transform, false);
        Part("Body", PrimitiveType.Capsule, model, Vector3.zero, new Vector3(1.9f, 1.6f, 2.3f), Quaternion.Euler(90f, 0f, 0f), body);
        Part("Canopy", PrimitiveType.Sphere, model, new Vector3(0f, 0.15f, 1.75f), new Vector3(1.6f, 1.3f, 1.6f), Quaternion.identity, glass);
        Part("Stripe", PrimitiveType.Cube, model, new Vector3(0f, -0.2f, -0.2f), new Vector3(1.93f, 0.18f, 2.6f), Quaternion.identity, stripe);
        Part("Boom", PrimitiveType.Cube, model, new Vector3(0f, 0.25f, -3.6f), new Vector3(0.35f, 0.4f, 3.8f), Quaternion.identity, body);
        Part("BoomStripe", PrimitiveType.Cube, model, new Vector3(0f, 0.25f, -3.6f), new Vector3(0.37f, 0.1f, 3.6f), Quaternion.identity, stripe);
        Part("Fin", PrimitiveType.Cube, model, new Vector3(0f, 0.9f, -5.3f), new Vector3(0.12f, 1.3f, 0.7f), Quaternion.Euler(-15f, 0f, 0f), body);
        Part("Tailplane", PrimitiveType.Cube, model, new Vector3(0f, 0.3f, -4.9f), new Vector3(1.4f, 0.06f, 0.4f), Quaternion.identity, body);
        Part("SkidL", PrimitiveType.Cube, model, new Vector3(-0.85f, -1.15f, 0.2f), new Vector3(0.1f, 0.1f, 3.2f), Quaternion.identity, dark);
        Part("SkidR", PrimitiveType.Cube, model, new Vector3(0.85f, -1.15f, 0.2f), new Vector3(0.1f, 0.1f, 3.2f), Quaternion.identity, dark);
        foreach (float x in new[] { -0.85f, 0.85f }) foreach (float z in new[] { -0.6f, 1.0f })
            Part("Strut", PrimitiveType.Cube, model, new Vector3(x * 0.85f, -0.85f, z), new Vector3(0.07f, 0.6f, 0.07f), Quaternion.Euler(0f, 0f, x > 0 ? -18f : 18f), dark);
        Part("Mast", PrimitiveType.Cylinder, model, new Vector3(0f, 1.05f, 0f), new Vector3(0.18f, 0.25f, 0.18f), Quaternion.identity, dark);
        rotor = new GameObject("Rotor").transform;
        rotor.SetParent(model, false); rotor.localPosition = new Vector3(0f, 1.3f, 0f);
        Part("BladeA", PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(9.5f, 0.05f, 0.3f), Quaternion.identity, dark);
        Part("BladeB", PrimitiveType.Cube, rotor, Vector3.zero, new Vector3(0.3f, 0.05f, 9.5f), Quaternion.identity, dark);
        tailRotor = new GameObject("TailRotor").transform;
        tailRotor.SetParent(model, false); tailRotor.localPosition = new Vector3(0.2f, 0.95f, -5.45f);
        Part("TailBlade", PrimitiveType.Cube, tailRotor, Vector3.zero, new Vector3(0.04f, 1.5f, 0.15f), Quaternion.identity, dark);
        // landing lights under the nose
        GameObject lg = new GameObject("NoseLight");
        lg.transform.SetParent(model, false); lg.transform.localPosition = new Vector3(0f, -0.8f, 1.6f);
        lg.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
        Light l = lg.AddComponent<Light>();
        l.type = LightType.Spot; l.range = 60f; l.spotAngle = 40f; l.intensity = 3f; l.color = new Color(1f, 0.95f, 0.85f); l.shadows = LightShadows.None;

        // solid: the body, the tail, the rotor's sweep (so the blades can't go through walls)
        CapsuleCollider cap = gameObject.AddComponent<CapsuleCollider>();
        cap.direction = 2; cap.radius = 1.1f; cap.height = 6.6f; cap.center = new Vector3(0f, -0.05f, -1.1f);
        BoxCollider sweep = gameObject.AddComponent<BoxCollider>();
        sweep.center = new Vector3(0f, 1.3f, 0f); sweep.size = new Vector3(9f, 0.3f, 9f);
        rb = gameObject.AddComponent<Rigidbody>();
        rb.useGravity = false; rb.mass = 2200f; rb.linearDamping = 0f; rb.angularDamping = 5f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // health and the fireball: a car's settings, more health, and no crash damage of its own (we do that here)
        vh = gameObject.AddComponent<VehicleHealth>();
        VehicleHealth template = FindAnyObjectByType<VehicleHealth>();
        if (template != null && template != vh) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(template), vh);
        JsonUtility.FromJsonOverwrite("{\"maxHealth\":" + MaxHealth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"crashDamageSpeed\":1000}", vh);

        rotorAudio = gameObject.AddComponent<AudioSource>();
        rotorAudio.clip = LundunGunAudio.Rotor;
        rotorAudio.loop = true; rotorAudio.spatialBlend = 1f; rotorAudio.minDistance = 20f; rotorAudio.maxDistance = 400f;
        rotorAudio.rolloffMode = AudioRolloffMode.Linear; rotorAudio.volume = 0f; rotorAudio.dopplerLevel = 0.3f;
        rotorAudio.playOnAwake = false;

        int vehicleLayer = LayerMask.NameToLayer("Vehicle");
        foreach (Transform t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = vehicleLayer >= 0 ? vehicleLayer : 0;
    }

    void OnEnable() { all.Add(this); }
    void OnDisable()
    {
        all.Remove(this);
        if (Wrecked && rb != null) { rb.useGravity = true; rb.constraints = RigidbodyConstraints.None; }    // down it comes
        if (Occupied && gameObject.scene.isLoaded) Release();                   // wrecked (its scripts switched off), or taken away
    }

    // ------------------------------------------------------------------ in and out
    /// James climbs in: he rides along hidden, the camera follows from behind and above, the flying controls appear.
    public void Board()
    {
        if (Occupied || Wrecked) return;
        PlayerHealth ph = PlayerHealth.Instance;
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        if (ph == null || ph.Body == null) return;
        if (vs != null && vs.IsDriving) vs.ExitCar();
        Occupied = true;
        Flying = this;
        ph.Body.gameObject.SetActive(false);
        ph.Body.position = transform.position;
        if (vs != null) vs.FlightControls(true);
        CameraFollow cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null) cam.SetTarget(transform, true);
        CameraFollow.HeliMode = true;
        if (ui == null) BuildUI();
        ui.SetActive(true);
        if (!rotorAudio.isPlaying) rotorAudio.Play();
        LundunHaptics.Medium();
    }

    /// James gets out beside it (on the ground below, if it's in the air).
    public void Release()
    {
        if (!Occupied) return;
        Occupied = false;
        if (Flying == this) Flying = null;
        PlayerHealth ph = PlayerHealth.Instance;
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        CameraFollow.HeliMode = false;
        if (ui != null) ui.SetActive(false);
        if (ph != null && ph.Body != null)
        {
            Vector3 side = transform.position + transform.right * 3.2f;
            Vector3 at = side;
            if (Physics.Raycast(side + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 400f, groundMask, QueryTriggerInteraction.Ignore)) at = hit.point + Vector3.up * 0.1f;
            CharacterController cc = ph.Body.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            ph.Body.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            ph.Body.gameObject.SetActive(true);
            if (cc != null) cc.enabled = true;
            CameraFollow cam = FindAnyObjectByType<CameraFollow>();
            if (cam != null) cam.SetTarget(ph.Body, false);
        }
        if (vs != null) vs.FlightControls(false);
    }

    /// A mission's over (passed, failed, quit): James is out of any helicopter.
    public static void LetGoAll()
    {
        for (int i = all.Count - 1; i >= 0; i--) if (all[i] != null && all[i].Occupied) all[i].Release();
    }

    // ------------------------------------------------------------------ flying
    void Update()
    {
        // rotors: spin up when someone's in, wind down when not
        spin = Mathf.MoveTowards(spin, Occupied && !Wrecked ? 1f : 0f, Time.deltaTime / (Occupied ? 2.5f : 6f));
        if (rotor != null) rotor.Rotate(0f, 1300f * spin * Time.deltaTime, 0f, Space.Self);
        if (tailRotor != null) tailRotor.Rotate(2400f * spin * Time.deltaTime, 0f, 0f, Space.Self);
        if (rotorAudio != null)
        {
            rotorAudio.volume = 0.85f * spin;
            rotorAudio.pitch = Mathf.Lerp(0.6f, 1f, spin);
            if (spin <= 0.01f && rotorAudio.isPlaying) rotorAudio.Stop();
        }

        // how high above the ground, and whether it's sitting on it
        float h = 999f;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit g, 400f, groundMask, QueryTriggerInteraction.Ignore)) h = g.distance;
        HeightAboveGround = Mathf.Max(0f, h - 1.2f);
        Landed = HeightAboveGround < 0.35f && Mathf.Abs(rb.linearVelocity.y) < 1f;

        // tilt the body into the motion (just the look; the body itself stays level)
        Vector3 local = Quaternion.Inverse(transform.rotation) * velocity;
        Quaternion tilt = Quaternion.Euler(Mathf.Clamp(local.z * 0.45f, -14f, 14f), 0f, Mathf.Clamp(-local.x * 0.55f, -16f, 16f));
        if (model != null) model.localRotation = Quaternion.Slerp(model.localRotation, Landed ? Quaternion.identity : tilt, 3f * Time.deltaTime);

        if (!Occupied) return;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph != null && ph.Body != null) ph.Body.position = transform.position;          // the police chase him here
        UpdateUI();
    }

    void FixedUpdate()
    {
        if (Wrecked) { rb.useGravity = true; rb.constraints = RigidbodyConstraints.None; return; }
        velocity = rb.linearVelocity;
        Vector3 want = Vector3.zero;
        if (Occupied && Ready)
        {
            Vector2 s = stick != null ? stick.Value : Vector2.zero;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            want = (fwd * s.y + right * s.x) * MaxSpeed;
            float v = 0f;
            if (up != null && up.IsHeld) v = Climb;
            else if (down != null && down.IsHeld) v = -Sink * Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(2f, 12f, HeightAboveGround));   // gentle near the ground
            if (HeightAboveGround > CeilingAboveGround && v > 0f) v = 0f;
            if (Landed && v <= 0f) want = Vector3.zero;                         // sitting on its skids
            want.y = v;
            if (!Landed && airborneAt < 0f) airborneAt = Time.time;
        }
        else if (!Landed) want = new Vector3(0f, -4f, 0f);                     // nobody flying it: it settles down
        Vector3 h0 = new Vector3(velocity.x, 0f, velocity.z), h1 = new Vector3(want.x, 0f, want.z);
        h0 = Vector3.MoveTowards(h0, h1, Accel * Time.fixedDeltaTime);
        float vy = Mathf.MoveTowards(velocity.y, want.y, 12f * Time.fixedDeltaTime);
        velocity = new Vector3(h0.x, vy, h0.z);
        rb.linearVelocity = velocity;
        rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
    }

    /// Dragging the right of the screen: turn (degrees of yaw) and tilt the view.
    public void Turn(Vector2 drag)
    {
        if (!Occupied || !Ready) return;
        float k = TurnRate * LundunSettings.LookSpeed;
        yaw += drag.x * k;
        CameraFollow cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null) cam.Nudge(0f, -drag.y * k * 0.6f);
    }

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        if (speed < 7f || vh == null) return;
        vh.TakeDamage((speed - 7f) * 18f);
        CameraFollow cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null && Occupied) cam.AddShake(Mathf.Min(0.4f, speed * 0.02f));
        if (Occupied) LundunHaptics.Heavy();
    }

    // ------------------------------------------------------------------ the controls on screen
    void UpdateUI()
    {
        if (ui == null) return;
        float hp = HealthFraction;
        hpFill.fillAmount = hp;
        hpFill.color = hp > 0.5f ? new Color(0.35f, 0.85f, 0.4f) : hp > 0.25f ? new Color(1f, 0.72f, 0.1f) : new Color(0.95f, 0.15f, 0.1f);
        altText.text = "ALT " + Mathf.RoundToInt(HeightAboveGround) + " m   ·   " + Mathf.RoundToInt(new Vector3(velocity.x, 0f, velocity.z).magnitude * 3.6f) + " km/h";
        upLabel.text = Ready ? "UP" : "…";
        string tip = Hint;
        if (string.IsNullOrEmpty(tip))
        {
            if (!Ready) tip = "Rotors spinning up…";
            else if (Landed && airborneAt < 0f) tip = "Hold UP to lift off";
            else if (airborneAt > 0f && Time.time - airborneAt < 9f) tip = "Left stick to fly  ·  drag the right of the screen to turn  ·  DOWN to descend";
            else if (!Landed && HeightAboveGround < 6f) tip = "Gently does it: DOWN to land";
            else tip = "";
        }
        hintText.text = tip;
        bool canOut = CanLeave && Landed;
        if (getOut.activeSelf != canOut) getOut.SetActive(canOut);
    }

    void BuildUI()
    {
        ui = new GameObject("HeliControls", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 60;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)ui.transform;

        // the turning pad: the right of the screen, behind the buttons
        RectTransform pad = Rect("TurnPad", root);
        pad.anchorMin = new Vector2(0.45f, 0f); pad.anchorMax = new Vector2(1f, 0.82f); pad.offsetMin = pad.offsetMax = Vector2.zero;
        Image pi = pad.gameObject.AddComponent<Image>(); pi.color = new Color(0f, 0f, 0f, 0.001f);
        pad.gameObject.AddComponent<LundunTurnPad>().heli = this;

        // the stick, bottom left
        RectTransform baseR = Rect("FlyStick", root);
        baseR.anchorMin = baseR.anchorMax = new Vector2(0f, 0f); baseR.pivot = new Vector2(0.5f, 0.5f);
        baseR.anchoredPosition = new Vector2(250f, 250f); baseR.sizeDelta = new Vector2(300f, 300f);
        Image bi = baseR.gameObject.AddComponent<Image>(); bi.sprite = LundunHudArt.Disc; bi.color = new Color(1f, 1f, 1f, 0.12f);
        RectTransform knob = Rect("Knob", baseR);
        knob.sizeDelta = new Vector2(130f, 130f);
        Image ki = knob.gameObject.AddComponent<Image>(); ki.sprite = LundunHudArt.Disc; ki.color = new Color(1f, 1f, 1f, 0.55f); ki.raycastTarget = false;
        stick = baseR.gameObject.AddComponent<LundunFlyStick>();
        stick.knob = knob; stick.range = 110f;

        // UP and DOWN, bottom right
        up = Button(root, "UP", new Vector2(-170f, 330f), new Color(0.2f, 0.6f, 0.3f, 0.85f), out upLabel);
        TextMeshProUGUI dl;
        down = Button(root, "DOWN", new Vector2(-170f, 150f), new Color(0.6f, 0.25f, 0.15f, 0.85f), out dl);

        // GET OUT (when landed and allowed), above DOWN's left
        RectTransform go = Rect("GetOut", root);
        go.anchorMin = go.anchorMax = new Vector2(1f, 0f); go.pivot = new Vector2(0.5f, 0.5f);
        go.anchoredPosition = new Vector2(-400f, 150f); go.sizeDelta = new Vector2(220f, 100f);
        Image gi = go.gameObject.AddComponent<Image>(); Pill(gi); gi.color = new Color(0.12f, 0.16f, 0.29f, 0.92f);
        UnityEngine.UI.Button gb = go.gameObject.AddComponent<UnityEngine.UI.Button>(); gb.targetGraphic = gi; gb.onClick.AddListener(() => { if (Landed && CanLeave) Release(); });
        Label(go, "GET OUT", 32f);
        getOut = go.gameObject;
        getOut.SetActive(false);

        // health and height, top middle
        RectTransform top = Rect("Status", root);
        top.anchorMin = top.anchorMax = new Vector2(0.5f, 1f); top.pivot = new Vector2(0.5f, 1f);
        top.anchoredPosition = new Vector2(0f, -150f); top.sizeDelta = new Vector2(520f, 70f);
        Image tb = top.gameObject.AddComponent<Image>(); Pill(tb); tb.color = new Color(0f, 0f, 0f, 0.55f);
        RectTransform track = Rect("Track", top);
        track.anchorMin = track.anchorMax = new Vector2(0.5f, 1f); track.pivot = new Vector2(0.5f, 1f);
        track.anchoredPosition = new Vector2(0f, -12f); track.sizeDelta = new Vector2(460f, 14f);
        track.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        RectTransform fr = Rect("Fill", track); fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero;
        hpFill = fr.gameObject.AddComponent<Image>();
        hpFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        hpFill.type = Image.Type.Filled; hpFill.fillMethod = Image.FillMethod.Horizontal; hpFill.raycastTarget = false;
        altText = Label(top, "", 24f);
        RectTransform ar = altText.rectTransform; ar.offsetMin = new Vector2(0f, 0f); ar.offsetMax = new Vector2(0f, -26f);

        // tips, low in the middle
        RectTransform hr = Rect("Hint", root);
        hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0f); hr.pivot = new Vector2(0.5f, 0f);
        hr.anchoredPosition = new Vector2(0f, 60f); hr.sizeDelta = new Vector2(1000f, 60f);
        hintText = Label(hr, "", 28f);
        hintText.textWrappingMode = TextWrappingModes.Normal;
        hintText.outlineWidth = 0.18f; hintText.outlineColor = new Color32(0, 0, 0, 220);
        ui.SetActive(false);
    }

    HoldButton Button(RectTransform root, string text, Vector2 at, Color c, out TextMeshProUGUI label)
    {
        RectTransform r = Rect(text, root);
        r.anchorMin = r.anchorMax = new Vector2(1f, 0f); r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = at; r.sizeDelta = new Vector2(190f, 150f);
        Image i = r.gameObject.AddComponent<Image>(); Pill(i); i.color = c;
        label = Label(r, text, 38f);
        r.gameObject.AddComponent<LundunPressFX>();
        return r.gameObject.AddComponent<HoldButton>();
    }

    static void Pill(Image i)
    {
        if (LundunHudArt.Pill == null) return;
        i.sprite = LundunHudArt.Pill; i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 0.8f;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static TextMeshProUGUI Label(RectTransform parent, string text, float size)
    {
        RectTransform r = Rect("Text", parent);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false; t.richText = true;
        return t;
    }

    // ------------------------------------------------------------------ building bits
    static Material Lit(Color c, float smooth)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(s != null ? s : Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
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

    /// A helipad on the ground: a dark disc, a white H and a ring of lights (colour: yellow to take off from, green to
    /// land on). Returns the root (a mission spawns it).
    public static GameObject Pad(Vector3 at, float radius, Color ring)
    {
        GameObject root = new GameObject("Helipad");
        root.transform.position = at;
        LundunMeshKit k = new LundunMeshKit(3);
        k.Cyl(0, new Vector3(0f, 0.02f, 0f), radius, 0.04f, Quaternion.identity, 32);
        k.Box(1, new Vector3(-1.4f, 0.05f, 0f), new Vector3(0.6f, 0.02f, 4f));
        k.Box(1, new Vector3(1.4f, 0.05f, 0f), new Vector3(0.6f, 0.02f, 4f));
        k.Box(1, new Vector3(0f, 0.05f, 0f), new Vector3(2.2f, 0.02f, 0.6f));
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16f;
            k.Box(2, new Vector3(Mathf.Cos(a) * (radius - 0.3f), 0.08f, Mathf.Sin(a) * (radius - 0.3f)), new Vector3(0.3f, 0.1f, 0.3f));
        }
        root.AddComponent<MeshFilter>().sharedMesh = k.Build("Helipad");
        MeshRenderer mr = root.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[]
        {
            LundunMeshKit.Mat("PadSurface", new Color(0.12f, 0.13f, 0.14f), 0.2f, Color.black),
            LundunMeshKit.Mat("PadH", new Color(0.95f, 0.95f, 0.92f), 0.2f, new Color(0.4f, 0.4f, 0.38f)),
            LundunMeshKit.Mat("PadLight" + ColorUtility.ToHtmlStringRGB(ring), ring, 0.5f, ring * 3f),
        };
        mr.shadowCastingMode = ShadowCastingMode.Off;
        GameObject lg = new GameObject("PadLight");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, 3f, 0f);
        Light l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = radius * 2.5f; l.intensity = 1.5f; l.color = ring; l.shadows = LightShadows.None;
        return root;
    }
}

/// S8: the helicopter's stick (bottom left): where your thumb is on it, -1..1 each way. Lets go when the finger does.
public class LundunFlyStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform knob;
    public float range = 110f;
    public Vector2 Value { get; private set; }
    private int pointer = int.MinValue;

    public void OnPointerDown(PointerEventData e) { pointer = e.pointerId; Move(e); }
    public void OnDrag(PointerEventData e) { if (e.pointerId == pointer) Move(e); }
    public void OnPointerUp(PointerEventData e) { if (e.pointerId != pointer) return; pointer = int.MinValue; Value = Vector2.zero; if (knob != null) knob.anchoredPosition = Vector2.zero; }
    void OnDisable() { pointer = int.MinValue; Value = Vector2.zero; if (knob != null) knob.anchoredPosition = Vector2.zero; }

    void Move(PointerEventData e)
    {
        RectTransform r = (RectTransform)transform;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(r, e.position, e.pressEventCamera, out local)) return;
        Vector2 v = Vector2.ClampMagnitude(local, range);
        if (knob != null) knob.anchoredPosition = v;
        Vector2 n = v / range;
        Value = n.magnitude < 0.12f ? Vector2.zero : n;
    }
}

/// S8: drag on the right of the screen to turn the helicopter (and tilt the view).
public class LundunTurnPad : MonoBehaviour, IDragHandler
{
    public LundunFlyHeli heli;
    public void OnDrag(PointerEventData e) { if (heli != null) heli.Turn(e.delta); }
}
