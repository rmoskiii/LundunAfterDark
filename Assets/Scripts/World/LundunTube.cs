using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// The Lundun Underground as fast travel (R8). Walk up to an entrance and "TAKE THE TUBE" appears:
/// pick a station, the screen goes dark to the rumble of a train, a few minutes pass on the clock,
/// and James comes up the steps at the other end. Closed while you're wanted.
/// Added automatically by LundunDressing (the stations come from Lundun → 13). Builds its own little UI.
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
    private float nextCheck;

    public Transform[] Markers { get { return markers; } }
    public string[] Names { get { return names; } }

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
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        BuildUI();
        rumble = MakeRumble(rideSeconds + 0.6f);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0.8f;
    }

    void Update()
    {
        if (names == null || travelling || Time.time < nextCheck) return;
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
        promptGo.SetActive(show && !panel.activeSelf);
        if (show)
        {
            bool wanted = WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
            promptText.text = wanted ? "STATION CLOSED\n<size=70%>Police incident</size>" : "TAKE THE TUBE\n<size=70%>" + names[here] + "</size>";
            promptButton.interactable = !wanted;
        }
    }

    // ------------------------------------------------------------------ choosing a station
    void OpenPanel()
    {
        if (here < 0) return;
        panel.SetActive(true);
        promptGo.SetActive(false);
        panelTitle.text = "LUNDUN UNDERGROUND\n<size=60%>from " + names[here] + "</size>";
        Transform list = panel.transform.Find("List");
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
        for (int i = 0; i < names.Length; i++)
        {
            if (i == here) continue;
            int to = i;
            float km = Vector3.Distance(tops[here], tops[to]) / 1000f;
            int minutes = Mathf.RoundToInt(3f + km * minutesPerKm);
            MakeButton(list, names[i] + "   <size=70%><color=#ffc72c>" + minutes + " min</color></size>", new Color(0.11f, 0.15f, 0.4f, 0.95f), () => StartCoroutine(Ride(here, to, minutes)));
        }
        MakeButton(list, "Cancel", new Color(0.25f, 0.25f, 0.25f, 0.9f), () => panel.SetActive(false));
    }

    IEnumerator Ride(int from, int to, int minutes)
    {
        travelling = true;
        panel.SetActive(false);
        promptGo.SetActive(false);
        vehicles.LockControls(true);
        if (rumble != null) audioSource.PlayOneShot(rumble);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.6f) { SetBlack(t); yield return null; }
        SetBlack(1f);

        // move James to the top of the other station's steps, a couple of metres out
        Transform player = PlayerHealth.Instance.Body;
        Vector3 outDir = facing[to] * Vector3.forward;
        Vector3 pos = LundunPlaces.Ground(tops[to] + outDir * 2.5f) + Vector3.up * LundunPlaces.FeetOffset(player);
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.SetPositionAndRotation(pos, facing[to]);
        if (cc != null) cc.enabled = true;
        if (LundunClock.Instance != null) LundunClock.Instance.AddHours(minutes / 60f);

        yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, rideSeconds - 1.2f));
        vehicles.LockControls(false);
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(names[to] + "\n" + minutes + " min on the Tube");
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.6f) { SetBlack(t); yield return null; }
        SetBlack(0f);
        travelling = false;
        nextCheck = Time.time + 1.5f;      // don't pop the button straight back up
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
        VerticalLayoutGroup v = list.AddComponent<VerticalLayoutGroup>();
        v.spacing = 16f; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false;
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
    static AudioClip MakeRumble(float seconds)
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
