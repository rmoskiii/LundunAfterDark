using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Full-screen map. Opens when you tap the minimap; pauses the game while open.
/// Drag to pan, pinch (or mouse wheel) to zoom, tap a road to drop a waypoint (tap it again to remove),
/// or pick a place from the list on the right. Built by Lundun → 6  Build Full Map + GPS.
public class LundunFullMap : MonoBehaviour
{
    public static LundunFullMap Instance { get; private set; }
    public bool IsOpen { get; private set; }

    [Header("Parts (filled in by the menu)")]
    [SerializeField] private GameObject window;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RectTransform content;
    [SerializeField] private RectTransform labelLayer;
    [SerializeField] private RectTransform blipLayer;
    [SerializeField] private RectTransform listContainer;
    [SerializeField] private TextMeshProUGUI routeInfo;
    [SerializeField] private Sprite dotSprite;
    [SerializeField] private Sprite arrowSprite;
    [SerializeField] private Transform playerCar;

    [Header("Map picture area (world metres)")]
    [SerializeField] private float mapMinX = -150f;
    [SerializeField] private float mapMinZ = -150f;
    [SerializeField] private float mapWidth = 1900f;
    [SerializeField] private float mapHeight = 1500f;

    [Header("Zoom")]
    [SerializeField] private float openMetresTall = 700f;     // how much map fits top-to-bottom when it opens
    [SerializeField] private float closestMetresTall = 120f;

    static readonly Color Purple = new Color(0.72f, 0.36f, 1f, 1f);

    private float ppm;                 // UI units per metre
    private float prevTimeScale = 1f;
    private VehicleSystem vehicles;
    private Canvas rootCanvas;
    private RectTransform playerIcon, waypointIcon, carIcon;
    private readonly List<Image> policeIcons = new List<Image>();
    private bool built;

    void Awake() { Instance = this; if (window != null) window.SetActive(false); }
    void OnEnable() { LundunMinimap.Tapped += Open; }
    void OnDisable() { LundunMinimap.Tapped -= Open; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        Canvas c = GetComponentInParent<Canvas>();
        rootCanvas = c != null ? c.rootCanvas : null;
    }

    // ------------------------------------------------------------------ open / close
    public void Open()
    {
        if (IsOpen) return;
        if (VehicleSystem.Locked || (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead))
        { Debug.Log("[Lundun] Map not opened: controls are locked (BUSTED / WASTED)."); return; }
        if (window == null || viewport == null || content == null)
        { Debug.LogWarning("[Lundun] Full map parts missing — run Lundun → 6  Build Full Map + GPS again."); return; }
        if (!built) BuildContents();
        IsOpen = true;
        window.SetActive(true);
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Canvas.ForceUpdateCanvases();
        ppm = viewport.rect.height / openMetresTall;
        ApplyZoom();
        CentreOn(vehicles != null ? vehicles.PlayerTarget.position : Vector3.zero);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
        Debug.Log("[Lundun] Map closed");
        IsOpen = false;
        window.SetActive(false);
        Time.timeScale = prevTimeScale;
        AudioListener.pause = false;
    }

    // ------------------------------------------------------------------ input (called by LundunMapInput)
    public void Pan(Vector2 screenDelta)
    {
        content.anchoredPosition += screenDelta / Scale();
        ClampPan();
    }

    public void ZoomAt(Vector2 screenPoint, float factor)
    {
        Vector2 vp = ViewportPoint(screenPoint);
        Vector2 before = (vp - content.anchoredPosition) / ppm;          // metres from the map centre under the finger
        float minPpm = Mathf.Max(viewport.rect.width / mapWidth, viewport.rect.height / mapHeight);
        float maxPpm = viewport.rect.height / closestMetresTall;
        ppm = Mathf.Clamp(ppm * factor, minPpm, maxPpm);
        ApplyZoom();
        content.anchoredPosition = vp - before * ppm;                   // keep that spot under the finger
        ClampPan();
    }

    public void Tap(Vector2 screenPoint)
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null) return;
        Vector3 w = ViewportToWorld(ViewportPoint(screenPoint));
        if (nav.HasWaypoint)
        {
            Vector2 wpOnScreen = WorldToViewport(nav.Waypoint);
            if (Vector2.Distance(wpOnScreen, ViewportPoint(screenPoint)) < 40f) { nav.Clear(); Refresh(); return; }
        }
        nav.SetDestination(w, "Waypoint", true);
        Refresh();
    }

    // ------------------------------------------------------------------ per frame while open
    void Update()
    {
        if (!IsOpen) return;
        UnityEngine.InputSystem.Keyboard kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
        Refresh();
    }

    void Refresh()
    {
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        if (vehicles != null && playerIcon != null)
        {
            Transform p = vehicles.PlayerTarget;
            Anchor(playerIcon, p.position);
            playerIcon.localEulerAngles = new Vector3(0, 0, -p.eulerAngles.y);
            if (carIcon != null && playerCar != null)
            {
                bool show = !(vehicles.IsDriving && p == playerCar);
                carIcon.gameObject.SetActive(show);
                if (show) Anchor(carIcon, playerCar.position);
            }
        }

        LundunNavigation nav = LundunNavigation.Instance;
        bool hasWp = nav != null && nav.HasWaypoint;
        if (waypointIcon != null)
        {
            waypointIcon.gameObject.SetActive(hasWp);
            if (hasWp) Anchor(waypointIcon, nav.Waypoint);
        }
        if (routeInfo != null)
            routeInfo.text = hasWp ? "To: " + nav.WaypointName + "\n" + Distance(nav.RouteMetres) : "Tap a road to set a waypoint,\nor pick a place:";

        int used = 0;
        foreach (PoliceAI car in PoliceAI.Active)
        {
            if (car == null) continue;
            if (used >= policeIcons.Count) policeIcons.Add(Icon(blipLayer, new Color(1f, 0.2f, 0.2f), 16, "").GetComponent<Image>());
            Image img = policeIcons[used++];
            img.gameObject.SetActive(true);
            Anchor(img.rectTransform, car.transform.position);
        }
        for (int i = used; i < policeIcons.Count; i++) policeIcons[i].gameObject.SetActive(false);
    }

    static string Distance(float m) { return m >= 1000f ? (m / 1000f).ToString("0.0") + " km" : Mathf.RoundToInt(m / 10f) * 10 + " m"; }

    // ------------------------------------------------------------------ contents (made once, the first time it opens)
    void BuildContents()
    {
        built = true;
        LundunNavigation nav = LundunNavigation.Instance;
        LundunWorldData w = nav != null ? nav.World : null;

        if (w != null)
            foreach (LwPoly d in w.districts)
            {
                if (d.id == "D_CANARY_PEN") continue;
                float cx = 0, cz = 0;
                foreach (LwPoint p in d.polygon) { cx += p.x; cz += p.z; }
                cx /= d.polygon.Length; cz /= d.polygon.Length;
                Label(labelLayer, d.name, new Vector3(cx, 0, cz), 30, new Color(1f, 1f, 1f, 0.28f), FontStyles.Bold);
            }

        if (nav != null)
        {
            List<LundunNavigation.Destination> order = new List<LundunNavigation.Destination>();
            foreach (string kind in new[] { "safehouse", "hospital", "police", "tube", "landmark", "mission" })
                foreach (LundunNavigation.Destination d in nav.Destinations) if (d.kind == kind) order.Add(d);

            foreach (LundunNavigation.Destination d in order)
            {
                Color col; string letter; float size = 30;
                switch (d.kind)
                {
                    case "safehouse": col = new Color(0.25f, 0.9f, 0.45f); letter = "S"; break;
                    case "hospital": col = new Color(0.95f, 0.25f, 0.3f); letter = "H"; break;
                    case "police": col = new Color(0.25f, 0.5f, 1f); letter = "P"; break;
                    case "tube": col = new Color(0.16f, 0.22f, 0.6f); letter = "U"; size = 26; break;
                    case "mission": col = new Color(1f, 0.82f, 0.2f, 0.6f); letter = d.id.Substring(1, 1); break;
                    default: col = new Color(0.9f, 0.9f, 0.9f, 0.8f); letter = ""; size = 14; break;
                }
                RectTransform ic = Icon(blipLayer, col, size, letter);
                Anchor(ic, d.position);
                if (d.kind == "landmark") Label(labelLayer, d.name, d.position + new Vector3(0, 0, 0), 20, new Color(1f, 1f, 1f, 0.7f), FontStyles.Italic, new Vector2(0, -22));

                LundunNavigation.Destination dest = d;
                ListButton(d.name, col, () => { LundunNavigation.Instance.SetDestination(dest.position, dest.name); Close(); });
            }
        }
        ListButton("Clear route", new Color(0.6f, 0.6f, 0.6f), () => { if (LundunNavigation.Instance != null) LundunNavigation.Instance.Clear(); Refresh(); });
        ListButton("Centre on me", new Color(0.6f, 0.6f, 0.6f), () => { if (vehicles != null) CentreOn(vehicles.PlayerTarget.position); });
        ListButton("CLOSE MAP", new Color(1f, 1f, 1f), Close).color = new Color(1f, 1f, 1f, 0.22f);
        CloseCross();

        if (playerCar != null) carIcon = Icon(blipLayer, new Color(1f, 0.85f, 0.25f), 16, "");
        waypointIcon = Icon(blipLayer, Purple, 30, "");
        playerIcon = Icon(blipLayer, Color.white, 34, "");
        playerIcon.GetComponent<Image>().sprite = arrowSprite;
    }

    RectTransform Icon(RectTransform parent, Color col, float size, string letter)
    {
        GameObject g = new GameObject("Icon", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.sizeDelta = new Vector2(size, size);
        Image img = g.AddComponent<Image>();
        img.sprite = dotSprite; img.color = col; img.raycastTarget = false;
        if (!string.IsNullOrEmpty(letter))
        {
            TextMeshProUGUI t = Text(rt, letter, size * 0.62f, new Color(0.05f, 0.05f, 0.05f), FontStyles.Bold);
            Stretch(t.rectTransform);
        }
        return rt;
    }

    void Label(RectTransform parent, string text, Vector3 world, float fontSize, Color col, FontStyles style, Vector2 offset = default(Vector2))
    {
        TextMeshProUGUI t = Text(parent, text, fontSize, col, style);
        t.rectTransform.sizeDelta = new Vector2(500, 60);
        Anchor(t.rectTransform, world);
        t.rectTransform.anchoredPosition = offset;
    }

    /// Big round ✕ in the top-right corner of the map area.
    void CloseCross()
    {
        GameObject g = new GameObject("CloseButton", typeof(RectTransform));
        g.transform.SetParent(window.transform, false);
        RectTransform rt = (RectTransform)g.transform;
        RectTransform vp = viewport;
        rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(vp.offsetMax.x - 24f, -24f);   // just left of the list panel
        rt.sizeDelta = new Vector2(96, 96);
        Image bg = g.AddComponent<Image>(); bg.sprite = dotSprite; bg.color = new Color(0.08f, 0.09f, 0.1f, 0.92f);
        Button b = g.AddComponent<Button>(); b.targetGraphic = bg; b.onClick.AddListener(Close);
        TextMeshProUGUI t = Text(rt, "X", 46, Color.white, FontStyles.Bold);
        Stretch(t.rectTransform);
    }

    Image ListButton(string text, Color accent, UnityEngine.Events.UnityAction onClick)
    {
        GameObject g = new GameObject("Button", typeof(RectTransform));
        g.transform.SetParent(listContainer, false);
        ((RectTransform)g.transform).sizeDelta = new Vector2(0, 62);
        LayoutElement le = g.AddComponent<LayoutElement>(); le.preferredHeight = 62;
        Image bg = g.AddComponent<Image>(); bg.color = new Color(1f, 1f, 1f, 0.08f);
        Button b = g.AddComponent<Button>(); b.targetGraphic = bg; b.onClick.AddListener(onClick);

        GameObject bar = new GameObject("Accent", typeof(RectTransform));
        bar.transform.SetParent(g.transform, false);
        RectTransform br = (RectTransform)bar.transform;
        br.anchorMin = new Vector2(0, 0); br.anchorMax = new Vector2(0, 1); br.pivot = new Vector2(0, 0.5f); br.sizeDelta = new Vector2(8, 0);
        Image bi = bar.AddComponent<Image>(); bi.color = new Color(accent.r, accent.g, accent.b, 1f); bi.raycastTarget = false;

        TextMeshProUGUI t = Text((RectTransform)g.transform, text, 26, Color.white, FontStyles.Normal);
        Stretch(t.rectTransform);
        t.rectTransform.offsetMin = new Vector2(24, 0);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return bg;
    }

    static TextMeshProUGUI Text(RectTransform parent, string text, float size, Color col, FontStyles style)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = col; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    // ------------------------------------------------------------------ maths
    float Scale() { return rootCanvas != null && rootCanvas.scaleFactor > 0 ? rootCanvas.scaleFactor : 1f; }

    void ApplyZoom() { content.sizeDelta = new Vector2(mapWidth * ppm, mapHeight * ppm); }

    /// Icons sit at a fixed spot on the map by anchoring to it, so they follow pan and zoom for free.
    void Anchor(RectTransform rt, Vector3 world)
    {
        Vector2 uv = new Vector2((world.x - mapMinX) / mapWidth, (world.z - mapMinZ) / mapHeight);
        rt.anchorMin = rt.anchorMax = uv;
        rt.anchoredPosition = Vector2.zero;
    }

    Vector2 ViewportPoint(Vector2 screen)
    {
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, null, out local);
        return local - viewport.rect.center;            // relative to the viewport's middle, like content.anchoredPosition
    }

    Vector3 ViewportToWorld(Vector2 vp)
    {
        Vector2 m = (vp - content.anchoredPosition) / ppm;                    // metres from map centre
        return new Vector3(mapMinX + mapWidth * 0.5f + m.x, 0f, mapMinZ + mapHeight * 0.5f + m.y);
    }

    Vector2 WorldToViewport(Vector3 w)
    {
        return content.anchoredPosition + new Vector2(w.x - (mapMinX + mapWidth * 0.5f), w.z - (mapMinZ + mapHeight * 0.5f)) * ppm;
    }

    void CentreOn(Vector3 w)
    {
        content.anchoredPosition = -new Vector2(w.x - (mapMinX + mapWidth * 0.5f), w.z - (mapMinZ + mapHeight * 0.5f)) * ppm;
        ClampPan();
    }

    void ClampPan()
    {
        Vector2 half = content.sizeDelta * 0.5f, vh = viewport.rect.size * 0.5f;
        Vector2 lim = new Vector2(Mathf.Max(0f, half.x - vh.x), Mathf.Max(0f, half.y - vh.y));
        Vector2 p = content.anchoredPosition;
        content.anchoredPosition = new Vector2(Mathf.Clamp(p.x, -lim.x, lim.x), Mathf.Clamp(p.y, -lim.y, lim.y));
    }
}
