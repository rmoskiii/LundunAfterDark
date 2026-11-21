using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The Lundun Underground as fast travel (R8). Walk up to an entrance and "TAKE THE TUBE" appears:
/// pick a station, the screen goes dark to the rumble of a train, a few minutes pass on the clock,
/// and James comes up the steps at the other end. Closed while you're wanted.
/// Added automatically by LundunDressing (the stations come from Lundun → 13). Builds its own little UI.
/// G5: TAKE THE TUBE now walks you down into the station (LundunTubeStation: ticket hall, gates, escalators, the
/// platform, the train). SKIP next to it is the old quick ride. A story mission, or a mission following someone
/// (ForcedTo), still gets the quick ride.
public class LundunTube : MonoBehaviour
{
    public static LundunTube Instance { get; private set; }

    [SerializeField] private float useDistance = 4.5f;        // from the top of the steps
    [SerializeField] private float rideSeconds = 3.2f;
    [SerializeField] private float minutesPerKm = 3f;         // clock time the journey takes (plus 3 min waiting)

    private string[] names;
    private Vector3[] tops;                                    // top of each staircase
    private Quaternion[] facing;                               // looking out of the station
    private Transform[] markers;
    private VehicleSystem vehicles;
    private Canvas canvas;
    private GameObject promptGo, panel;
    private TextMeshProUGUI promptText, panelTitle;
    private Button promptButton;
    private Image blackout;
    private AudioSource audioSource;
    private AudioClip rumble;
    private int here = -1;
    private bool travelling;
    private LundunTubeStation station;                       // G5: the walk-through station
    private GameObject skipGo;
    private LundunTubeMap map;                               // G6b: the Tube map (choosing a stop, and the ride)
    private float nextCheck;

    public Transform[] Markers { get { return markers; } }
    public string[] Names { get { return names; } }

    // S5: a mission following someone down the Tube: the button goes straight to their stop (Free Roam never sets it)
    public static string ForcedTo;
    public static string ForcedLabel = "FOLLOW";
    /// S5: James came up at this station (its name), each time he rides.
    public static event System.Action<string> Arrived;
    public bool Travelling { get { return travelling; } }
    // G5: for the walk-through station
    public LundunTubeStation Station { get { return station; } }
    public bool PanelOpen { get { return (panel != null && panel.activeSelf) || (map != null && map.Showing); } }
    public LundunTubeMap Map { get { return map; } }
    public Quaternion Facing(int i) { return facing[i]; }
    /// G6: game minutes from a to b: by the lines (LundunTubeNetwork) when both are on them, else by the distance.
    public int Minutes(int a, int b)
    {
        if (a == b) return 0;
        LundunTubeNetwork.Journey j = Journey(a, b);
        return j != null ? j.Minutes : Mathf.RoundToInt(3f + Vector3.Distance(tops[a], tops[b]) / 1000f * minutesPerKm);
    }
    /// G6: the trip from station a to b on the lines (null if either isn't on them).
    public LundunTubeNetwork.Journey Journey(int a, int b)
    {
        if (names == null || a < 0 || b < 0 || a >= names.Length || b >= names.Length) return null;
        return LundunTubeNetwork.Plan(names[a], names[b]);
    }
    public void Black(float a) { SetBlack(a); }
    public void RaiseArrived(int to) { if (Arrived != null && to >= 0 && to < names.Length) Arrived(names[to]); }
    /// The quick ride (fade, a few minutes, up the steps at `to`); atBlack runs once the screen's dark.
    public void QuickRide(int from, int to, int minutes, System.Action atBlack) { StartCoroutine(Ride(from, to, minutes, atBlack)); }
    /// The station list: chosen(to, minutes), or the quick ride if chosen is null. includeHere: "way out here" too.
    public void Pick(int from, string subtitle, bool includeHere, System.Action<int, int> chosen)
    {
        if (map != null) { promptGo.SetActive(false); if (skipGo != null) skipGo.SetActive(false); map.Choose(from, subtitle, includeHere, chosen); }
        else ShowList(from, subtitle, includeHere, chosen);
    }
    bool CanWalk() { return station != null && !LundunMissions.Running && ForcedTo == null; }

    public int Find(string stationName) { if (names != null) for (int i = 0; i < names.Length; i++) if (names[i] == stationName) return i; return -1; }
    /// The top of a station's steps, and the way out onto the street.
    public Vector3 Top(int i) { return tops[i]; }
    public Vector3 Out(int i) { return facing[i] * Vector3.forward; }

    public void Init(string[] stationNames, Vector3[] openEnds)
    {
        names = stationNames;
        int n = names.Length;
        tops = new Vector3[n]; facing = new Quaternion[n]; markers = new Transform[n];
        // each entrance's box collider sits over the stairs: "out" is from its middle to the top of the steps
        Transform cols = transform.Find("Colliders");
        for (int i = 0; i < n; i++)
        {
            tops[i] = openEnds[i];
            Vector3 outDir = Vector3.forward; float best = float.MaxValue;
            if (cols != null)
                foreach (Transform c in cols)
                {
                    if (c.name != "Underground") continue;
                    Vector3 d = openEnds[i] - c.position; d.y = 0f;
                    if (d.magnitude < best) { best = d.magnitude; outDir = d.normalized; }
                }
            facing[i] = Quaternion.LookRotation(outDir);
            GameObject m = new GameObject("Tube_" + names[i]);
            m.transform.SetParent(transform, false);
            m.transform.position = tops[i];
            markers[i] = m.transform;
        }
    }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; ForcedTo = null; }

    /// E1: another station (an expansion area's), after Init. outDir = the way out onto the street.
    public void AddStation(string stationName, Vector3 top, Vector3 outDir)
    {
        if (names == null) return;
        int n = names.Length;
        System.Array.Resize(ref names, n + 1); System.Array.Resize(ref tops, n + 1);
        System.Array.Resize(ref facing, n + 1); System.Array.Resize(ref markers, n + 1);
        names[n] = stationName; tops[n] = top;
        outDir.y = 0f;
        facing[n] = Quaternion.LookRotation(outDir.sqrMagnitude > 0.01f ? outDir.normalized : Vector3.forward);
        GameObject m = new GameObject("Tube_" + stationName);
        m.transform.SetParent(transform, false);
        m.transform.position = top;
        markers[n] = m.transform;
    }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        BuildUI();
        station = gameObject.AddComponent<LundunTubeStation>();     // G5
        station.tube = this;
        map = gameObject.AddComponent<LundunTubeMap>();            // G6b
        map.tube = this;
        rumble = MakeRumble(rideSeconds + 0.6f);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0.8f;
    }

    void Update()
    {
        if (names == null) return;
        if (LundunTubeStation.Underground)                        // G5: down in the station: it runs its own buttons
        {
            here = -1;
            if (promptGo.activeSelf) promptGo.SetActive(false);
            if (skipGo != null && skipGo.activeSelf) skipGo.SetActive(false);
            return;
        }
        if (travelling || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;
        here = -1;
        PlayerHealth ph = PlayerHealth.Instance;
        bool riding = LundunBikes.Instance != null && LundunBikes.Instance.Riding;
        if (ph != null && !ph.IsDead && ph.OnFoot && !riding && vehicles != null && !vehicles.IsDriving)
        {
            Vector3 p = ph.Body.position;
            for (int i = 0; i < tops.Length; i++)
            {
                Vector3 d = tops[i] - p; d.y = 0f;
                if (d.magnitude < useDistance) { here = i; break; }
            }
        }
        bool show = here >= 0;
        if (!show && panel.activeSelf) panel.SetActive(false);
        if (!show && map != null && map.Choosing) map.Hide();          // walked away from the steps
        promptGo.SetActive(show && !PanelOpen);
        if (skipGo != null) skipGo.SetActive(false);
        if (show)
        {
            bool wanted = WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
            int forced = ForcedTo != null ? Find(ForcedTo) : -1;
            if (forced >= 0 && forced != here)
            {
                promptText.text = ForcedLabel + "\n<size=70%>to " + names[forced] + "</size>";
                promptButton.interactable = true;
                return;
            }
            bool walk = CanWalk();
            promptText.text = wanted ? "STATION CLOSED\n<size=70%>Police incident</size>" : "TAKE THE TUBE\n<size=70%>" + names[here] + (walk ? " · walk down" : "") + "</size>";
            promptButton.interactable = !wanted;
            if (skipGo != null) skipGo.SetActive(walk && !wanted && !PanelOpen);    // G5: the old quick ride
        }
    }

    // ------------------------------------------------------------------ choosing a station
    void OpenPanel()
    {
        if (here < 0) return;
        int forced = ForcedTo != null ? Find(ForcedTo) : -1;
        if (forced >= 0 && forced != here)
        {
            int minutes = Minutes(here, forced);
            StartCoroutine(Ride(here, forced, minutes));
            return;
        }
        bool wanted = WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
        if (CanWalk() && !wanted)                                  // G5: down the steps into the station
        {
            promptGo.SetActive(false);
            if (skipGo != null) skipGo.SetActive(false);
            station.Enter(here);
            return;
        }
        int fromHere = here;
        Pick(fromHere, "from " + LundunTubeNetwork.Title(names[fromHere]) + " · where to?", false, (to, minutes) => StartCoroutine(Ride(fromHere, to, minutes)));
    }

    /// G5: SKIP at the top of the steps: the old quick ride.
    void OpenQuick()
    {
        if (here < 0) return;
        int fromHere = here;
        Pick(fromHere, "from " + LundunTubeNetwork.Title(names[fromHere]) + " · quick ride: where to?", false, (to, minutes) => StartCoroutine(Ride(fromHere, to, minutes)));
    }

    void ShowList(int from, string subtitle, bool includeHere, System.Action<int, int> chosen)
    {
        panel.SetActive(true);
        promptGo.SetActive(false);
        if (skipGo != null) skipGo.SetActive(false);
        panelTitle.text = "LUNDUN UNDERGROUND\n<size=60%>" + subtitle + "</size>";
        Transform list = panel.transform.Find("List");
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
        // G6: nearest first (by the lines), each with its time and any change; two columns once there are lots
        List<int> order = new List<int>();
        for (int i = 0; i < names.Length; i++) if (i != from || includeHere) order.Add(i);
        order.Sort((x, y) => Minutes(from, x) != Minutes(from, y) ? Minutes(from, x).CompareTo(Minutes(from, y)) : string.CompareOrdinal(names[x], names[y]));
        bool two = order.Count > 6;
        foreach (int i in order)
        {
            int to = i;
            int minutes = Minutes(from, to);
            string label;
            if (to == from) label = names[i] + "\n<size=60%><color=#ffc72c>way out here</color></size>";
            else
            {
                LundunTubeNetwork.Journey j = Journey(from, to);
                string how = j != null && j.changes.Count > 0 ? " · change at " + LundunTubeNetwork.Title(j.changes[0]) + (j.changes.Count > 1 ? " +1" : "") : "";
                label = names[i] + "\n<size=60%><color=#ffc72c>" + minutes + " min</color>" + how + "</size>";
            }
            Button b = MakeButton(list, label, new Color(0.11f, 0.15f, 0.4f, 0.95f), () =>
            {
                panel.SetActive(false);
                if (chosen != null) chosen(to, minutes);
                else StartCoroutine(Ride(from, to, minutes));
            });
            b.GetComponentInChildren<TextMeshProUGUI>().fontSize = two ? 28 : 34;
        }
        MakeButton(list, "Cancel", new Color(0.25f, 0.25f, 0.25f, 0.9f), () => panel.SetActive(false));
        int rows = two ? (order.Count + 2) / 2 : order.Count + 1;
        GridLayoutGroup grid = list.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = two ? 2 : 1;
            grid.cellSize = new Vector2(two ? 400f : 680f, two ? 82f : 92f);
            grid.spacing = new Vector2(16f, two ? 14f : 16f);
        }
        ((RectTransform)panel.transform).sizeDelta = new Vector2(two ? 900f : 760f, Mathf.Max(640f, 230f + rows * (two ? 96f : 108f)));   // room for every stop
    }

    IEnumerator Ride(int from, int to, int minutes, System.Action atBlack = null)
    {
        travelling = true;
        panel.SetActive(false);
        promptGo.SetActive(false);
        if (skipGo != null) skipGo.SetActive(false);
        vehicles.LockControls(true);
        // G6b: a ride on the lines plays on the Tube map (not a mission's follow-the-target ride, which stays quick)
        LundunTubeNetwork.Journey jn = ForcedTo == null && from != to ? Journey(from, to) : null;
        bool onMap = jn != null && jn.Hops > 0 && map != null;
        if (onMap)
        {
            float total = jn.RideSeconds;
            AudioClip clip = MakeRumble(total + 0.4f);
            if (clip != null) audioSource.PlayOneShot(clip);
            map.ShowRide(jn);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f) { SetBlack(t); map.SetAlpha(t); yield return null; }
            SetBlack(1f); map.SetAlpha(1f);
            if (atBlack != null) { try { atBlack(); } catch (System.Exception e) { Debug.LogException(e); } }
            PlaceAtTop(to);
            if (LundunClock.Instance != null && minutes > 0) LundunClock.Instance.AddHours(minutes / 60f);
            yield return map.Animate(jn, Mathf.Max(1f, total - 0.7f));
            audioSource.Stop();
            vehicles.LockControls(false);
            if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(names[to] + "\n" + minutes + " min on the Tube");
            for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.35f) { SetBlack(t); map.SetAlpha(t); yield return null; }
            SetBlack(0f); map.Hide();
            travelling = false;
            nextCheck = Time.time + 1.5f;
            if (Arrived != null) Arrived(names[to]);
            yield break;
        }
        if (rumble != null && to != from) audioSource.PlayOneShot(rumble);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.6f) { SetBlack(t); yield return null; }
        SetBlack(1f);
        if (atBlack != null) { try { atBlack(); } catch (System.Exception e) { Debug.LogException(e); } }   // G5: out of the station

        PlaceAtTop(to);
        if (LundunClock.Instance != null && minutes > 0) LundunClock.Instance.AddHours(minutes / 60f);

        yield return new WaitForSecondsRealtime(to == from ? 0.3f : Mathf.Max(0.5f, rideSeconds - 1.2f));
        vehicles.LockControls(false);
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(minutes > 0 ? names[to] + "\n" + minutes + " min on the Tube" : names[to]);
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.6f) { SetBlack(t); yield return null; }
        SetBlack(0f);
        travelling = false;
        nextCheck = Time.time + 1.5f;      // don't pop the button straight back up
        if (Arrived != null) Arrived(names[to]);
    }

    /// James at the top of station `to`'s steps, a couple of metres out, facing the street.
    void PlaceAtTop(int to)
    {
        Transform player = PlayerHealth.Instance.Body;
        Vector3 outDir = facing[to] * Vector3.forward;
        Vector3 pos = LundunPlaces.Ground(tops[to] + outDir * 2.5f) + Vector3.up * LundunPlaces.FeetOffset(player);
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(pos, facing[to]);
        if (cc != null) cc.enabled = true;
    }

    void SetBlack(float a)
    {
        Color c = blackout.color; c.a = a; blackout.color = c;
        blackout.raycastTarget = a > 0.01f;
    }

    // ------------------------------------------------------------------ UI (built in code, no setup needed)
    void BuildUI()
    {
        GameObject cg = new GameObject("TubeUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        // prompt button, middle of the screen near the bottom (between the thumb controls)
        promptGo = MakeButton(cg.transform, "TAKE THE TUBE", new Color(0.11f, 0.15f, 0.4f, 0.92f), OpenPanel).gameObject;
        RectTransform prt = (RectTransform)promptGo.transform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
        prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 230f);
        prt.sizeDelta = new Vector2(420f, 120f);
        promptButton = promptGo.GetComponent<Button>();
        promptText = promptGo.GetComponentInChildren<TextMeshProUGUI>();
        promptGo.SetActive(false);

        // G5: SKIP, under TAKE THE TUBE: the old quick ride
        skipGo = MakeButton(cg.transform, "SKIP  <size=70%>quick ride</size>", new Color(0.2f, 0.2f, 0.24f, 0.92f), OpenQuick).gameObject;
        RectTransform skt = (RectTransform)skipGo.transform;
        skt.anchorMin = skt.anchorMax = new Vector2(0.5f, 0f);
        skt.pivot = new Vector2(0.5f, 0f);
        skt.anchoredPosition = new Vector2(0f, 130f);
        skt.sizeDelta = new Vector2(300f, 84f);
        skipGo.GetComponentInChildren<TextMeshProUGUI>().fontSize = 30;
        skipGo.SetActive(false);

        // station list
        panel = new GameObject("TubePanel", typeof(RectTransform));
        panel.transform.SetParent(cg.transform, false);
        RectTransform pr = (RectTransform)panel.transform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(760f, 640f);
        Image bg = panel.AddComponent<Image>(); bg.color = new Color(0.05f, 0.07f, 0.16f, 0.94f);
        GameObject stripe = new GameObject("Stripe", typeof(RectTransform)); stripe.transform.SetParent(panel.transform, false);
        RectTransform srt = (RectTransform)stripe.transform; srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f);
        srt.pivot = new Vector2(0.5f, 1f); srt.sizeDelta = new Vector2(0f, 12f); srt.anchoredPosition = new Vector2(0f, -150f);
        stripe.AddComponent<Image>().color = new Color(1f, 0.78f, 0.17f);
        panelTitle = Label(panel.transform, "LUNDUN UNDERGROUND", 44, new Vector2(0f, -20f), new Vector2(700f, 120f));
        GameObject list = new GameObject("List", typeof(RectTransform));
        list.transform.SetParent(panel.transform, false);
        RectTransform lrt = (RectTransform)list.transform;
        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = new Vector2(40f, 30f); lrt.offsetMax = new Vector2(-40f, -180f);
        GridLayoutGroup v = list.AddComponent<GridLayoutGroup>();        // G6: one column, or two once there are lots of stops
        v.spacing = new Vector2(16f, 16f); v.cellSize = new Vector2(680f, 92f); v.childAlignment = TextAnchor.UpperCenter;
        v.constraint = GridLayoutGroup.Constraint.FixedColumnCount; v.constraintCount = 1;
        panel.SetActive(false);

        // blackout for the journey
        GameObject bo = new GameObject("TubeBlackout", typeof(RectTransform));
        bo.transform.SetParent(cg.transform, false);
        RectTransform brt = (RectTransform)bo.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
        blackout = bo.AddComponent<Image>(); blackout.color = new Color(0f, 0f, 0f, 0f); blackout.raycastTarget = false;
    }

    Button MakeButton(Transform parent, string text, Color col, UnityEngine.Events.UnityAction onClick)
    {
        GameObject g = new GameObject("Button", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        Image img = g.AddComponent<Image>(); img.color = col;
        Button b = g.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(onClick);
        LayoutElement le = g.AddComponent<LayoutElement>(); le.preferredHeight = 92f;
        TextMeshProUGUI t = Label(g.transform, text, 38, Vector2.zero, Vector2.zero);
        RectTransform trt = (RectTransform)t.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        return b;
    }

    TextMeshProUGUI Label(Transform parent, string text, float size, Vector2 pos, Vector2 box)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = box;
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.color = Color.white;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false; t.richText = true;
        return t;
    }

    // ------------------------------------------------------------------ the sound of a train (made in code)
    public static AudioClip MakeRumble(float seconds)
    {
        int rate = 22050, n = (int)(seconds * rate);
        float[] s = new float[n];
        System.Random r = new System.Random(5);
        float brown = 0f, hum = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Clamp01(t / 0.8f) * Mathf.Clamp01((seconds - t) / 0.8f);
            brown = Mathf.Clamp(brown + ((float)r.NextDouble() * 2f - 1f) * 0.04f, -1f, 1f) * 0.995f;
            hum = Mathf.Sin(t * 2f * Mathf.PI * (55f + 8f * Mathf.Sin(t * 0.7f)));
            // clack-clack over the rail joints
            float beat = t % 0.9f, clack = 0f;
            if (beat < 0.03f) clack = Mathf.Exp(-beat * 160f);
            else if (beat > 0.18f && beat < 0.21f) clack = Mathf.Exp(-(beat - 0.18f) * 160f) * 0.8f;
            clack *= ((float)r.NextDouble() * 2f - 1f);
            s[i] = env * (brown * 0.55f + hum * 0.12f + clack * 0.5f);
        }
        AudioClip clip = AudioClip.Create("TubeRumble", n, 1, rate, false);
        clip.SetData(s, 0);
        return clip;
    }
}
