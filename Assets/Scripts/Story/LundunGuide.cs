using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Guidance (Story only): you always know what to do next and where it is.
///   during a mission   each new objective flashes large in the middle of the screen (about 4 seconds), then sits
///                      pinned in a box under the minimap with how far it is ("Get to FADEZ BARBERS · 230 m") until
///                      it's done; tap the box to see it large again. Wherever you have to go there's the purple
///                      route (minimap and map), and a pointer: over the place when it's on screen, at the edge of the
///                      screen (pointing) when it isn't. Moving targets (a car to get into, someone to catch) are
///                      followed: the route is re-planned as they move.
///   between missions   the next mission is always guided: the route to its start, the pointer with who's waiting and
///                      how far, and the NEXT bar under the minimap ("NEXT: Easy Money · Tayo at FADEZ BARBERS ·
///                      420 m"). A few seconds after a mission ends, that mission's giver rings with where to come.
/// Free Roam has none of this (added by LundunStoryUI in Story only).
public class LundunGuide : MonoBehaviour
{
    public static LundunGuide Instance { get; private set; }

    // where each mission's giver is, and what they say when they ring (by mission id)
    static readonly System.Collections.Generic.Dictionary<string, string[]> Calls = new System.Collections.Generic.Dictionary<string, string[]>
    {
        { "m01_one_last_favour",   new[] { "your front door", "(phone) James, I'm outside yours. Uncle wants everybody at the shop." } },
        { "m02_easy_money",        new[] { "FADEZ BARBERS, Rye Lane", "(phone) Bro! Come to Fadez on Rye Lane. I've got a way to fix your money." } },
        { "m03_no_way_home",       new[] { "the lock-up by Brixtun station", "(phone) James... I'm at the lock-up by Brixtun station. Come quick." } },
        { "m04_estate_business",   new[] { "your front door", "(phone) I'm outside yours. Kingsley wants a word. Don't keep him waiting." } },
        { "m05_uncles_rules",      new[] { "LAGOS FOODS, Rye Lane", "(phone) James. Come to the shop. We need to talk." } },
        { "m06_passenger_problems",new[] { "Knightsbury Circus", "(phone) Finished on the door. Pick me up at Knightsbury Circus?" } },
        { "m07_home_turf",         new[] { "your front door", "(phone) I'm at yours. Kemi's been ringing. Get here." } },
        { "m08_blood_is_thicker",  new[] { "Knightsbury Circus", "(phone) I've found Tayo. Knightsbury Circus. Come now." } },
        { "m09_paper_trail",       new[] { "your front door", "(phone) Come home, James. I have a job for you. A quiet one." } },
        { "m10_open_for_business", new[] { "your front door", "(phone) Come home. Today the shop opens again." } },
        { "m13_kemis_deliveries",  new[] { "the bike dock off Rye Lane", "(phone) James! I've got parcels and no rider. Bike dock off Rye Lane?" } },
        { "m14_prove_yourself",    new[] { "Gilmore Green", "(phone) Gilmore Green. Come and show me what you've got." } },
        { "m15_warehouse_9",       new[] { "Warehouse Lane, Canary Quay", "(phone) Warehouse Lane, tonight. I've got a job. A big one. Trust me." } },
    };

    public static string Where(LundunMission m)
    {
        string[] c; return m != null && Calls.TryGetValue(m.Id, out c) ? c[0] : "";
    }

    private RectTransform canvasRect, pin, big, pointer, arrow;
    private TextMeshProUGUI pinText, bigText, pointerText;
    private Image pinBack, bigBack;
    private CanvasGroup bigGroup;
    private string objective = "";
    private float bigUntil = -1f, bigStarted;
    // route upkeep
    private Vector3 lastSet;
    private bool guideRouted;
    private string lastNext;
    private LundunStep lastStep;
    private float nextRoute;
    // the giver's call
    private bool wasRunning;
    private int doneBefore = -1;
    private float callAt = -1f;
    private LundunMission calling;
    private LundunMinimap minimap;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        Build();
        LundunStoryData d = LundunStory.Data;
        doneBefore = d != null ? d.missionsDone : 0;
        if (d != null && d.missionsDone > 0) { callAt = Time.time + 5f; calling = LundunMissions.NextUp(); }   // carrying on a story
    }

    // ------------------------------------------------------------------ what LundunMissions tells it
    /// The objective now (pinned under the minimap while there's a mission).
    public void Pin(string s) { objective = s ?? ""; }

    /// A new objective: large in the middle for a few seconds first.
    public void Announce(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        bigText.text = s;
        bigStarted = Time.unscaledTime;
        bigUntil = Time.unscaledTime + 4f;
        big.gameObject.SetActive(true);
    }

    // ------------------------------------------------------------------ every frame
    void LateUpdate()
    {
        LundunStep step = LundunMissions.CurrentStep;
        bool running = LundunMissions.Running;
        Transform j = LundunMissionRun.James();
        LundunMission next = running ? null : LundunMissions.NextUp();

        // where we're heading
        bool has = false; Vector3 target = Vector3.zero; bool moving = false;
        string label = "";
        if (running && step != null)
        {
            Transform f = step.Follow;
            if (f != null) { has = true; target = f.position; moving = true; }
            else if (step.hasMarker) { has = true; target = step.marker; }
        }
        else if (next != null && !VehicleSystem.Locked)
        {
            has = true; target = LundunMissionRun.Ground(next.StartAt);
            label = LundunTrust.Pretty(next.Giver).ToUpperInvariant();
        }
        float dist = has && j != null ? LundunMissionRun.FlatDistance(j.position, target) : 0f;

        Routes(running, step, next, has, target, moving, dist);
        Calling(running, next);

        // the pinned box: the objective (with the distance), or what's next
        string pinned = "";
        if (running) pinned = objective + (has && dist > 8f ? "   <color=#C9A2FF>· " + Metres(dist) + "</color>" : "");
        else if (next != null)
        {
            string where = Where(next);
            pinned = "<color=#FFA82E>NEXT</color>  " + next.Title + "\n<size=78%>" + LundunTrust.Pretty(next.Giver) + (where.Length > 0 ? " at " + where : " is waiting")
                   + (has ? "   <color=#C9A2FF>· " + Metres(dist) + "</color>" : "") + "</size>";
        }
        bool showPin = pinned.Length > 0 && Time.timeScale > 0f;
        if (pin.gameObject.activeSelf != showPin) pin.gameObject.SetActive(showPin);
        if (showPin)
        {
            if (pinText.text != pinned) pinText.text = pinned;
            PlaceUnderMinimap();
        }

        // the big objective, fading in and out
        if (big.gameObject.activeSelf)
        {
            float t = Time.unscaledTime;
            if (t > bigUntil || Time.timeScale == 0f) big.gameObject.SetActive(false);
            else bigGroup.alpha = Mathf.Min(Mathf.Clamp01((t - bigStarted) / 0.25f), Mathf.Clamp01((bigUntil - t) / 0.45f));
        }

        // the pointer: over the place, or at the edge of the screen pointing to it
        Point(has && dist > 10f && Time.timeScale > 0f, target, dist, label);
    }

    /// The purple route: to the next mission between missions; re-planned after moving targets during one.
    void Routes(bool running, LundunStep step, LundunMission next, bool has, Vector3 target, bool moving, float dist)
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null) return;
        if (step != lastStep)                                                   // a new step: our old route goes
        {
            if (guideRouted && nav.HasWaypoint && (nav.Waypoint - Flat(lastSet)).sqrMagnitude < 4f && !(step != null && step.hasMarker && (Flat(step.marker) - Flat(lastSet)).sqrMagnitude < 4f)) nav.Clear();
            guideRouted = false;
            lastStep = step;
            nextRoute = Time.time + 0.5f;
        }
        if (running)
        {
            if (moving && has && dist > 30f && Time.time >= nextRoute)
            {
                nextRoute = Time.time + 2.5f;
                if (!nav.HasWaypoint || (Flat(target) - Flat(lastSet)).sqrMagnitude > 12f * 12f) { nav.SetDestination(target, step.objective); lastSet = target; guideRouted = true; }
            }
            lastNext = null;
            return;
        }
        string id = next != null ? next.Id : null;
        if (id != lastNext)
        {
            lastNext = id;
            if (next != null && has) { nav.SetDestination(target, "Next: " + next.Title); lastSet = target; guideRouted = true; }
            nextRoute = Time.time + 5f;
            return;
        }
        if (next != null && has && Time.time >= nextRoute)
        {
            nextRoute = Time.time + 5f;
            if (!nav.HasWaypoint && dist > 80f) { nav.SetDestination(target, "Next: " + next.Title); lastSet = target; guideRouted = true; }
        }
    }

    /// A few seconds after a mission's done (or the story's loaded): the next giver rings.
    void Calling(bool running, LundunMission next)
    {
        LundunStoryData d = LundunStory.Data;
        int done = d != null ? d.missionsDone : 0;
        if (wasRunning && !running && done > doneBefore) { callAt = Time.time + 6f; calling = next; }
        wasRunning = running;
        doneBefore = done;
        if (callAt < 0f || Time.time < callAt || running || Time.timeScale == 0f || LundunMissions.Talking) return;
        callAt = -1f;
        LundunMission m = calling ?? next;
        calling = null;
        if (m == null || next == null || m.Id != next.Id) return;
        string[] c;
        string line = Calls.TryGetValue(m.Id, out c) ? c[1] : "(phone) Come and find me. I'm waiting.";
        LundunMissions.Say(m.Giver, line, LundunMissions.ReadTime(line) + 1f);
        LundunHaptics.Medium();
        Announce("NEXT: " + m.Title);
        bigUntil = Time.unscaledTime + 4f;
    }

    // ------------------------------------------------------------------ the pointer
    void Point(bool show, Vector3 target, float dist, string label)
    {
        Camera cam = Camera.main;
        if (!show || cam == null) { if (pointer.gameObject.activeSelf) pointer.gameObject.SetActive(false); return; }
        if (!pointer.gameObject.activeSelf) pointer.gameObject.SetActive(true);
        Vector3 sp = cam.WorldToScreenPoint(target + Vector3.up * 3f);
        bool behind = sp.z < 0f;
        if (behind) sp = new Vector3(Screen.width - sp.x, Screen.height - sp.y, 0f);
        Rect safe = Screen.safeArea;
        float mx = 90f * Screen.width / 1920f, my = 120f * Screen.height / 1080f;
        bool onScreen = !behind && sp.x > safe.xMin + mx && sp.x < safe.xMax - mx && sp.y > safe.yMin + my && sp.y < safe.yMax - my;
        Vector2 c = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Vector2 at;
        float angle;
        if (onScreen) { at = new Vector2(sp.x, sp.y); angle = 180f; }                    // pointing down at it
        else
        {
            Vector2 d = new Vector2(sp.x, sp.y) - c;
            if (d.sqrMagnitude < 1f) d = Vector2.up;
            float hx = (safe.width / 2f) - mx, hy = (safe.height / 2f) - my;
            float k = Mathf.Min(hx / Mathf.Max(0.001f, Mathf.Abs(d.x)), hy / Mathf.Max(0.001f, Mathf.Abs(d.y)));
            at = c + d * k;
            angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
        }
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, at, null, out local);
        pointer.anchoredPosition = local;
        arrow.localRotation = Quaternion.Euler(0f, 0f, angle);
        // the label sits on the inside of the arrow
        Vector2 inward = onScreen ? Vector2.up : -(Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector3.up);
        pointerText.rectTransform.anchoredPosition = inward * 58f;
        string text = (label.Length > 0 ? label + "\n" : "") + Metres(dist);
        if (pointerText.text != text) pointerText.text = text;
    }

    static string Metres(float m) { return m < 1000f ? Mathf.RoundToInt(m / 5f) * 5 + " m" : (m / 1000f).ToString("0.0") + " km"; }
    static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0f, v.z); }

    /// The pinned box sits just under the minimap (and its health bar), wherever the safe area put it.
    void PlaceUnderMinimap()
    {
        if (minimap == null) minimap = FindAnyObjectByType<LundunMinimap>();
        LundunMinimap mm = minimap;
        RectTransform hud = mm != null ? mm.HudRoot : null;
        Vector2 local;
        if (hud != null)
        {
            Vector3[] corners = new Vector3[4];
            hud.GetWorldCorners(corners);
            Canvas c = hud.GetComponentInParent<Canvas>();
            Camera ccam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(ccam, corners[0]);              // bottom left
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
            pin.anchoredPosition = local + new Vector2(0f, -14f);
        }
        else
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, new Vector2(Screen.safeArea.xMin + 30f, Screen.height * 0.55f), null, out local);
            pin.anchoredPosition = local;
        }
    }

    // ------------------------------------------------------------------ building it
    void Build()
    {
        GameObject cg = new GameObject("GuideUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas cv = cg.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 56;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        canvasRect = (RectTransform)cg.transform;

        // pinned (tap: large again)
        pin = Rect("Pinned", canvasRect);
        pin.anchorMin = pin.anchorMax = new Vector2(0.5f, 0.5f); pin.pivot = new Vector2(0f, 1f); pin.sizeDelta = new Vector2(560f, 104f);
        pinBack = pin.gameObject.AddComponent<Image>(); Pill(pinBack, new Color(0f, 0f, 0f, 0.62f));
        Button pb = pin.gameObject.AddComponent<Button>(); pb.targetGraphic = pinBack; pb.transition = Selectable.Transition.None;
        pb.onClick.AddListener(() => { if (LundunMissions.Running) Announce(objective); });
        pinText = Label(pin, 26f, TextAlignmentOptions.Left);
        RectTransform pt = pinText.rectTransform; pt.anchorMin = Vector2.zero; pt.anchorMax = Vector2.one; pt.offsetMin = new Vector2(22f, 8f); pt.offsetMax = new Vector2(-18f, -8f);
        pinText.textWrappingMode = TextWrappingModes.Normal;
        pin.gameObject.SetActive(false);

        // big
        big = Rect("Big", canvasRect);
        big.anchorMin = big.anchorMax = new Vector2(0.5f, 0.66f); big.pivot = new Vector2(0.5f, 0.5f); big.sizeDelta = new Vector2(1300f, 130f);
        bigBack = big.gameObject.AddComponent<Image>(); Pill(bigBack, new Color(0f, 0f, 0f, 0.7f)); bigBack.raycastTarget = false;
        bigGroup = big.gameObject.AddComponent<CanvasGroup>(); bigGroup.blocksRaycasts = false; bigGroup.interactable = false;
        bigText = Label(big, 46f, TextAlignmentOptions.Center);
        RectTransform bt = bigText.rectTransform; bt.anchorMin = Vector2.zero; bt.anchorMax = Vector2.one; bt.offsetMin = new Vector2(30f, 6f); bt.offsetMax = new Vector2(-30f, -6f);
        bigText.textWrappingMode = TextWrappingModes.Normal;
        big.gameObject.SetActive(false);

        // pointer: an arrow and a label
        pointer = Rect("Pointer", canvasRect);
        pointer.anchorMin = pointer.anchorMax = new Vector2(0.5f, 0.5f); pointer.sizeDelta = new Vector2(10f, 10f);
        arrow = Rect("Arrow", pointer);
        arrow.sizeDelta = new Vector2(64f, 64f);
        Image ai = arrow.gameObject.AddComponent<Image>(); ai.sprite = Triangle(); ai.color = new Color(0.78f, 0.55f, 1f); ai.raycastTarget = false;
        pointerText = Label(pointer, 24f, TextAlignmentOptions.Center);
        pointerText.rectTransform.sizeDelta = new Vector2(260f, 64f);
        pointerText.outlineWidth = 0.25f; pointerText.outlineColor = new Color32(0, 0, 0, 200);
        pointer.gameObject.SetActive(false);
    }

    static RectTransform Rect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static void Pill(Image i, Color c)
    {
        if (LundunHudArt.Pill != null) { i.sprite = LundunHudArt.Pill; i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 0.8f; }
        i.color = c;
    }

    static TextMeshProUGUI Label(RectTransform parent, float size, TextAlignmentOptions align)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.fontSize = size; t.alignment = align; t.color = Color.white; t.fontStyle = FontStyles.Bold; t.richText = true; t.raycastTarget = false;
        return t;
    }

    static Sprite triangle;
    /// An arrowhead (pointing up), drawn here.
    static Sprite Triangle()
    {
        if (triangle != null) return triangle;
        const int N = 64;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        Color[] px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (y + 0.5f) / N;              // tip at the top middle
                float half = (1f - v) * 0.5f;
                float edge = half - Mathf.Abs(u - 0.5f);
                bool notch = v < 0.32f && Mathf.Abs(u - 0.5f) < (0.32f - v) * 0.9f;
                float a = Mathf.Clamp01(edge * N * 0.8f) * (v > 0.04f && !notch ? 1f : 0f);
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        t.SetPixels(px); t.Apply(false, false);
        triangle = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        return triangle;
    }
}
