using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// v50: the first-time controls guide (Story and Free Roam). A small card at the top says what to do; a pulsing ring
/// and a moving fingertip sit on the real button; each step finishes when you've actually done it:
///   walk · look · sprint + jump · get in a car · drive · open the map + set a waypoint · swap weapons + fire
/// Story: runs alongside mission 01 the first time (the mission walks you through most of it anyway).
/// Free Roam: the first time the city loads. SKIP GUIDE on the card; Pause → CONTROLS → GUIDE: SHOW AGAIN brings it back.
/// Never blocks touches (only the SKIP button takes them). Added by LundunHud.
public class LundunTutorial : MonoBehaviour
{
    public static LundunTutorial Instance { get; private set; }
    /// The guide is on screen.
    public static bool Showing { get { return Instance != null && Instance.step >= 0; } }

    const string FreeKey = "Lundun.Guide.Free", StoryKey = "Lundun.Guide.Story";

    class Step
    {
        public string title, text;
        public Func<RectTransform> target;            // the button to ring (null = a spot on screen)
        public Vector2 spot;                          // no button: where (0..1 of the screen)
        public int motion;                            // the fingertip: 0 tap, 1 drag up, 2 drag sideways
        public Action begin;
        public Func<bool> done;
    }

    private readonly List<Step> steps = new List<Step>();
    private int step = -1;
    private float stepAt, doneAt = -1f, startAt = -1f;
    private bool wanted;

    // what each step watches
    private Vector3 lastPos; private float walked, turned, lastYaw, sprintFor, driven;
    private bool jumped, mapOpened, swapped, fired;
    private Vector3 wpWas; private bool wpHad;
    private int kindWas;

    // the overlay
    private GameObject ui;
    private RectTransform card, ring, finger, rootRt;
    private Image ringImg, fingerImg, cardImg, tick;
    private TextMeshProUGUI titleText, bodyText, countText;
    private CanvasGroup group;

    PlayerController player; VehicleSystem vehicles; CameraFollow cam;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; PlayerCombat.GunshotFired -= OnShot; }

    void Start()
    {
        PlayerCombat.GunshotFired += OnShot;
        string key = LundunGame.IsStory ? StoryKey : FreeKey;
        bool fresh = PlayerPrefs.GetInt(key, 0) == 0;
        if (LundunGame.IsStory && LundunStory.Data != null && LundunStory.Data.missionsDone > 0) fresh = false;   // already playing
        if (fresh) { wanted = true; startAt = Time.unscaledTime + 3f; }
    }

    void OnShot(Vector3 at) { fired = true; }

    /// Pause → CONTROLS → GUIDE: SHOW AGAIN.
    public static void Restart()
    {
        if (Instance != null) Instance.Stop(false);
        PlayerPrefs.DeleteKey(FreeKey); PlayerPrefs.DeleteKey(StoryKey); PlayerPrefs.Save();
        if (Instance == null) return;
        Instance.wanted = true; Instance.startAt = Time.unscaledTime + 0.6f;
    }

    // ------------------------------------------------------------------ the steps
    void BuildSteps()
    {
        steps.Clear();
        steps.Add(new Step
        {
            title = "WALK", text = "Put your left thumb on the stick and push it the way you want to go.",
            target = () => Find("JoystickBase"), motion = 1,
            begin = () => { walked = 0f; lastPos = Feet(); },
            done = () => { Vector3 p = Feet(); Vector3 d = p - lastPos; d.y = 0f; if (d.magnitude < 3f) walked += d.magnitude; lastPos = p; return walked > 5f; }
        });
        steps.Add(new Step
        {
            title = "LOOK AROUND", text = "Drag your right thumb anywhere on the right of the screen to turn and look.",
            spot = new Vector2(0.72f, 0.5f), motion = 2,
            begin = () => { turned = 0f; lastYaw = cam != null ? cam.Yaw : 0f; },
            done = () => { if (cam == null) return true; turned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, cam.Yaw)); lastYaw = cam.Yaw; return turned > 120f; }
        });
        steps.Add(new Step
        {
            title = "RUN AND JUMP", text = "Push the stick all the way to sprint. Tap JUMP to jump (over a wall, a bin, a car bonnet).",
            target = () => Find("JumpButton"), motion = 0,
            begin = () => { jumped = false; sprintFor = 0f; },
            done = () =>
            {
                if (player == null) return true;
                if (player.Airborne) jumped = true;
                if (player.IsSprinting) sprintFor += Time.deltaTime;
                return jumped && sprintFor > 0.8f;
            }
        });
        steps.Add(new Step
        {
            title = "GET IN A CAR", text = "Walk up to a car (the driver's door). When the CAR button shows, tap it.",
            target = () => Find("EnterCarButton"), motion = 0,
            done = () => vehicles == null || vehicles.IsDriving
        });
        steps.Add(new Step
        {
            title = "DRIVE", text = "GAS to go, BRAKE to slow down and reverse. Steer with your left thumb. Drive a little way.",
            target = () => Find("GasButton"), motion = 0,
            begin = () => { driven = 0f; lastPos = Feet(); },
            done = () =>
            {
                if (vehicles == null) return true;
                Vector3 p = Feet(); Vector3 d = p - lastPos; d.y = 0f;
                if (vehicles.IsDriving && d.magnitude < 10f) driven += d.magnitude;
                lastPos = p;
                return driven > 60f;
            }
        });
        steps.Add(new Step
        {
            title = "THE MAP", text = "Tap the minimap to open the city map. Tap anywhere on it to set a waypoint: the GPS shows the way.",
            target = () => { LundunMinimap m = FindAnyObjectByType<LundunMinimap>(); return m != null ? m.transform as RectTransform : null; }, motion = 0,
            begin = () =>
            {
                mapOpened = false;
                LundunNavigation n = LundunNavigation.Instance;
                wpHad = n != null && n.HasWaypoint; wpWas = n != null ? n.Waypoint : Vector3.zero;
            },
            done = () =>
            {
                LundunFullMap map = LundunFullMap.Instance;
                LundunNavigation n = LundunNavigation.Instance;
                if (map == null || n == null) return true;
                if (map.IsOpen) mapOpened = true;
                bool newPoint = n.HasWaypoint && (!wpHad || (n.Waypoint - wpWas).sqrMagnitude > 1f);
                return mapOpened && newPoint;
            }
        });
        steps.Add(new Step
        {
            title = "WEAPONS", text = "On foot, tap the weapon panel to swap (fists, pistol…), then hold FIRE to shoot. AIM tightens the view.",
            target = () =>
            {
                if (vehicles != null && vehicles.IsDriving) return Find("ExitButton");
                return swapped ? Find("FireButton") : Find("WeaponPanel");
            },
            motion = 0,
            begin = () => { swapped = false; fired = false; kindWas = Kind(); },
            done = () =>
            {
                if (LundunWeapons.Instance == null) return true;
                if (Kind() != kindWas) swapped = true;
                return swapped && fired;
            }
        });
    }

    int Kind() { LundunWeapons w = LundunWeapons.Instance; return w != null ? (int)w.Current.kind : 0; }
    Vector3 Feet() { return vehicles != null && vehicles.PlayerTarget != null ? vehicles.PlayerTarget.position : player != null ? player.transform.position : Vector3.zero; }

    static RectTransform Find(string name)
    {
        GameObject g = GameObject.Find(name);           // active ones only: a hidden button isn't ringed
        return g != null ? g.transform as RectTransform : null;
    }

    // ------------------------------------------------------------------ running
    void Update()
    {
        if (wanted && step < 0 && Time.unscaledTime >= startAt) TryStart();
        if (step < 0) return;

        bool hide = (LundunSettings.Instance != null && LundunSettings.Instance.IsOpen) || (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead);
        group.alpha = Mathf.MoveTowards(group.alpha, hide ? 0f : 1f, Time.unscaledDeltaTime * 5f);
        group.blocksRaycasts = !hide;

        Step s = steps[step];
        if (doneAt < 0f)
        {
            bool ok = false;
            try { ok = s.done(); } catch (Exception e) { Debug.LogException(e); ok = true; }
            if (ok) { doneAt = Time.unscaledTime; LundunHaptics.Light(); }
        }
        else if (Time.unscaledTime - doneAt > 0.9f) { Next(); return; }

        // the card: a tick once done
        tick.gameObject.SetActive(doneAt >= 0f);
        cardImg.color = doneAt >= 0f ? new Color(0.1f, 0.32f, 0.16f, 0.94f) : new Color(0.04f, 0.06f, 0.11f, 0.9f);

        // the ring and the fingertip
        RectTransform t = null;
        try { t = s.target != null ? s.target() : null; } catch (Exception) { t = null; }
        Vector2 at; float size; bool show = doneAt < 0f;
        if (t != null && ScreenRect(t, out at, out size)) { }
        else if (s.target == null) { at = Local(new Vector2(s.spot.x * Screen.width, s.spot.y * Screen.height)); size = 220f; }
        else { show = false; at = Vector2.zero; size = 0f; }
        ring.gameObject.SetActive(show); finger.gameObject.SetActive(show);
        if (!show) return;
        float k = Time.unscaledTime - stepAt;
        float pulse = 1f + 0.12f * Mathf.Sin(k * 6f);
        ring.anchoredPosition = at; ring.sizeDelta = Vector2.one * (size + 30f) * pulse;
        ringImg.color = new Color(1f, 0.82f, 0.25f, 0.65f + 0.3f * Mathf.Sin(k * 6f));
        float c = (k % 1.4f) / 1.4f;
        Vector2 off = Vector2.zero; float press = 1f;
        if (s.motion == 0) { press = c < 0.3f ? 1f - c : c < 0.5f ? 0.7f : 1f; off = new Vector2(0f, -10f) * (1f - press); }
        else if (s.motion == 1) off = new Vector2(0f, Mathf.Lerp(-10f, 90f, Ease(Mathf.Clamp01(c * 1.4f))));
        else off = new Vector2(Mathf.Lerp(-150f, 150f, Ease(Mathf.Clamp01(c * 1.3f))), 0f);
        finger.anchoredPosition = at + off;
        finger.localScale = Vector3.one * press;
        fingerImg.color = new Color(1f, 1f, 1f, c > 0.85f && s.motion != 0 ? (1f - c) / 0.15f * 0.85f : 0.85f);
    }

    void TryStart()
    {
        player = FindAnyObjectByType<PlayerController>();
        vehicles = FindAnyObjectByType<VehicleSystem>();
        cam = FindAnyObjectByType<CameraFollow>();
        if (player == null || LundunHud.Instance == null) { startAt = Time.unscaledTime + 1f; return; }
        if (Time.timeScale == 0f || (LundunSettings.Instance != null && LundunSettings.Instance.IsOpen)) { startAt = Time.unscaledTime + 0.5f; return; }
        wanted = false;
        BuildSteps();
        if (ui == null) BuildUI();
        ui.SetActive(true);
        group.alpha = 0f;
        // already in a car: skip ahead to driving
        step = -1;
        Go(vehicles != null && vehicles.IsDriving ? 4 : 0);
    }

    void Go(int i)
    {
        step = i; stepAt = Time.unscaledTime; doneAt = -1f;
        Step s = steps[i];
        if (s.begin != null) { try { s.begin(); } catch (Exception e) { Debug.LogException(e); } }
        titleText.text = s.title;
        bodyText.text = s.text;
        countText.text = (i + 1) + " / " + steps.Count;
    }

    void Next()
    {
        int n = step + 1;
        if (n < steps.Count) { Go(n); return; }
        Stop(true);
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav != null) nav.ShowToast("YOU'RE SET\nPause → CONTROLS → GUIDE shows this again");
    }

    void Stop(bool finished)
    {
        if (step >= 0 || finished)
        {
            PlayerPrefs.SetInt(LundunGame.IsStory ? StoryKey : FreeKey, 1);
            PlayerPrefs.Save();
        }
        step = -1;
        if (ui != null) ui.SetActive(false);
    }

    void Skip()
    {
        LundunHaptics.Light();
        Stop(true);
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav != null) nav.ShowToast("Guide skipped. Pause → CONTROLS → GUIDE to see it again");
    }

    static float Ease(float t) { return t * t * (3f - 2f * t); }

    /// Where a HUD button is on our canvas (its centre) and how big.
    bool ScreenRect(RectTransform t, out Vector2 at, out float size)
    {
        at = Vector2.zero; size = 0f;
        Canvas c = t.GetComponentInParent<Canvas>();
        if (c == null) return false;
        Canvas root = c.rootCanvas;
        Camera uiCam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        Vector3[] w = new Vector3[4];
        t.GetWorldCorners(w);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(uiCam, w[0]), b = RectTransformUtility.WorldToScreenPoint(uiCam, w[2]);
        Vector2 la = Local(a), lb = Local(b);
        at = (la + lb) * 0.5f;
        size = Mathf.Max(Mathf.Abs(lb.x - la.x), Mathf.Abs(lb.y - la.y));
        return size > 1f;
    }

    Vector2 Local(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, screen, null, out Vector2 p);
        return p;
    }

    // ------------------------------------------------------------------ the overlay
    void BuildUI()
    {
        ui = new GameObject("ControlsGuide", typeof(RectTransform));
        ui.transform.SetParent(transform, false);
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 75;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        group = ui.AddComponent<CanvasGroup>();
        rootRt = (RectTransform)ui.transform;

        // ring + fingertip (centre-anchored, so Local() points line up)
        ring = Rect("Ring", rootRt, new Vector2(0.5f, 0.5f));
        ringImg = ring.gameObject.AddComponent<Image>(); ringImg.sprite = LundunHudArt.RingThick; ringImg.preserveAspect = true; ringImg.raycastTarget = false;
        finger = Rect("Fingertip", rootRt, new Vector2(0.5f, 0.5f)); finger.sizeDelta = new Vector2(64f, 64f);
        fingerImg = finger.gameObject.AddComponent<Image>(); fingerImg.sprite = LundunHudArt.Disc; fingerImg.preserveAspect = true; fingerImg.raycastTarget = false;
        RectTransform halo = Rect("Halo", finger, new Vector2(0.5f, 0.5f)); halo.sizeDelta = new Vector2(96f, 96f);
        Image hi = halo.gameObject.AddComponent<Image>(); hi.sprite = LundunHudArt.Ring; hi.color = new Color(1f, 1f, 1f, 0.6f); hi.raycastTarget = false;

        // the card, top centre (under the clock / wanted stars row)
        card = Rect("Card", rootRt, new Vector2(0.5f, 1f));
        card.pivot = new Vector2(0.5f, 1f); card.anchoredPosition = new Vector2(0f, -120f); card.sizeDelta = new Vector2(760f, 170f);
        cardImg = card.gameObject.AddComponent<Image>(); cardImg.sprite = LundunHudArt.Panel; cardImg.type = Image.Type.Sliced; cardImg.pixelsPerUnitMultiplier = 0.4f;
        cardImg.raycastTarget = false;
        titleText = Text(card, "", 34f, new Color(1f, 0.82f, 0.25f), new Vector2(0f, 1f), new Vector2(30f, -18f), new Vector2(480f, 44f), TextAlignmentOptions.MidlineLeft);
        titleText.characterSpacing = 8f;
        countText = Text(card, "", 22f, new Color(1f, 1f, 1f, 0.55f), new Vector2(1f, 1f), new Vector2(-30f, -24f), new Vector2(120f, 30f), TextAlignmentOptions.MidlineRight);
        bodyText = Text(card, "", 25f, Color.white, new Vector2(0f, 1f), new Vector2(30f, -66f), new Vector2(560f, 92f), TextAlignmentOptions.TopLeft);
        bodyText.fontStyle = FontStyles.Normal; bodyText.textWrappingMode = TextWrappingModes.Normal; bodyText.overflowMode = TextOverflowModes.Overflow;
        RectTransform tk = Rect("Tick", card, new Vector2(1f, 0.5f)); tk.pivot = new Vector2(1f, 0.5f);
        tk.anchoredPosition = new Vector2(-40f, -14f); tk.sizeDelta = new Vector2(70f, 70f);
        tick = tk.gameObject.AddComponent<Image>(); tick.sprite = LundunHudArt.Disc; tick.color = new Color(0.4f, 0.95f, 0.5f); tick.raycastTarget = false;
        TextMeshProUGUI tt = Text(tk, "OK", 26f, new Color(0.04f, 0.15f, 0.06f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, 70f), TextAlignmentOptions.Center);
        tt.raycastTarget = false;

        // SKIP GUIDE (the only thing here that takes a touch)
        RectTransform sk = Rect("Skip", card, new Vector2(1f, 0f)); sk.pivot = new Vector2(1f, 1f);
        sk.anchoredPosition = new Vector2(-10f, -8f); sk.sizeDelta = new Vector2(190f, 52f);
        Image si = sk.gameObject.AddComponent<Image>(); si.sprite = LundunHudArt.Pill; si.type = Image.Type.Sliced; si.pixelsPerUnitMultiplier = 1.4f; si.color = new Color(0.04f, 0.06f, 0.11f, 0.85f);
        Button sb = sk.gameObject.AddComponent<Button>(); sb.targetGraphic = si; sb.onClick.AddListener(Skip);
        sk.gameObject.AddComponent<LundunPressFX>();
        Text(sk, "SKIP GUIDE", 20f, new Color(1f, 1f, 1f, 0.85f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 52f), TextAlignmentOptions.Center).characterSpacing = 4f;
        ui.SetActive(false);
    }

    static RectTransform Rect(string name, RectTransform parent, Vector2 anchor)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        RectTransform r = (RectTransform)g.transform;
        r.SetParent(parent, false);
        r.anchorMin = r.anchorMax = anchor;
        return r;
    }

    static TextMeshProUGUI Text(RectTransform parent, string s, float size, Color c, Vector2 anchor, Vector2 pos, Vector2 box, TextAlignmentOptions align)
    {
        RectTransform r = Rect("Text", parent, anchor);
        r.pivot = new Vector2(anchor.x == 0f ? 0f : anchor.x == 1f ? 1f : 0.5f, anchor.y == 1f ? 1f : anchor.y == 0f ? 0f : 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = box;
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = s; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }
}
