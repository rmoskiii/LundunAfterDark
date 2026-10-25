using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Full-screen map (V2b redesign). Opens when you tap the minimap; pauses the game while open.
///   • the map fills the screen: drag to pan, pinch / wheel or the + − buttons to zoom, ◎ to find yourself
///   • district names, and street names once you zoom in
///   • filter chips on the left: safehouses, hospitals, police, Tube, bikes, landmarks (tap to show / hide)
///   • tap a place's icon, or anywhere on a road, to select it: a card at the bottom shows what it is and how far,
///     with SET ROUTE. With a route set, the card shows it with CLEAR ROUTE
///   • PLACES (top right) opens a list of every destination
///   • R4b: gang areas you've been into are shaded in the gang's colour, with their name under the district's
///   • E8: Greater Lundun round the playable city (the river across the middle, the outer boroughs and their names),
///     drawn behind the map picture; zooming right out shows all of it, top to bottom, without scrolling
/// Built by Lundun → 6  Build Full Map + GPS (which fills in the fields below); the new look is made in code.
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

    [Header("E8: Greater Lundun round it (filled in by Lundun → 17)")]
    [SerializeField] private Texture2D backdrop;
    [Tooltip("The backdrop's area: min x, min z, width, height (world metres)")]
    [SerializeField] private Vector4 backdropArea = Vector4.zero;
    [SerializeField] private string[] outerNames = new string[0];
    [SerializeField] private Vector2[] outerAt = new Vector2[0];
    [Tooltip("Zoomed right out, this much room is left round Greater Lundun (fraction of the screen)")]
    [SerializeField] private float fitMargin = 0.02f;

    [Header("Zoom")]
    [SerializeField] private float openMetresTall = 700f;     // how much map fits top-to-bottom when it opens
    [SerializeField] private float closestMetresTall = 120f;
    [Tooltip("Street names appear once less than this many metres fit top-to-bottom")]
    [SerializeField] private float streetNamesBelow = 560f;

    static readonly Color Purple = new Color(0.72f, 0.36f, 1f, 1f);
    static readonly Color Ink = new Color(0.03f, 0.05f, 0.09f, 0.88f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

    class Item { public string kind, name; public Vector3 pos; public RectTransform icon; public bool isPin; }
    class Chip { public string kind; public Image disc; public TextMeshProUGUI label; public bool on = true; }

    private float ppm;                 // UI units per metre
    private float prevTimeScale = 1f;
    private VehicleSystem vehicles;
    private Canvas rootCanvas;
    private RectTransform playerIcon, waypointIcon, carIcon, selectRing, pinIcon;
    private readonly List<Image> policeIcons = new List<Image>();
    private readonly List<Item> items = new List<Item>();
    private readonly List<Chip> chips = new List<Chip>();
    private readonly List<TextMeshProUGUI> streetLabels = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> districtLabels = new List<TextMeshProUGUI>();
    private bool built;

    // overlay parts
    private RectTransform overlay, card, drawer;
    private TextMeshProUGUI titleDistrict, cardTitle, cardSub, cardButtonText;
    private Image cardDisc, cardGlyph, cardButton;
    private TextMeshProUGUI cardLetter;
    private Item selected;
    private readonly Item pin = new Item { kind = "pin", name = "Dropped pin", isPin = true };

    private RectTransform backdropRect;
    private readonly List<KeyValuePair<string, GameObject>> gangAreas = new List<KeyValuePair<string, GameObject>>();   // R4b

    void Awake() { Instance = this; if (window != null) window.SetActive(false); }

    /// Lundun → 17: Greater Lundun's picture, its area, and the outer boroughs' names.
    public void SetBackdrop(Texture2D picture, Vector4 area, string[] names, Vector2[] at)
    {
        backdrop = picture; backdropArea = area;
        outerNames = names ?? new string[0]; outerAt = at ?? new Vector2[0];
    }

    bool HasBackdrop { get { return backdrop != null && backdropArea.z > 1f && backdropArea.w > 1f; } }

    /// What you can pan round and zoom out to: Greater Lundun (or just the map picture without it). min x, min z, w, h.
    Vector4 Bounds { get { return HasBackdrop ? backdropArea : new Vector4(mapMinX, mapMinZ, mapWidth, mapHeight); } }
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
        else ShowGangAreas();                    // R4b: any gang area James has found since
        IsOpen = true;
        window.SetActive(true);
        if (drawer != null) drawer.gameObject.SetActive(false);
        selected = null;
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Canvas.ForceUpdateCanvases();
        FitSafeArea();
        ppm = viewport.rect.height / openMetresTall;
        ApplyZoom();
        CentreOn(vehicles != null ? vehicles.PlayerTarget.position : Vector3.zero);
        Refresh();
    }

    public void Close()
    {
        if (!IsOpen) return;
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
        // E8: zoomed right out, the whole of Greater Lundun fits on screen (before, the map filled the width and you had to
        // scroll for the top and bottom)
        Vector4 bb = Bounds;
        float minPpm = Mathf.Min(viewport.rect.width / bb.z, viewport.rect.height / bb.w) * (1f - fitMargin);
        float maxPpm = viewport.rect.height / closestMetresTall;
        ppm = Mathf.Clamp(ppm * factor, minPpm, maxPpm);
        ApplyZoom();
        content.anchoredPosition = vp - before * ppm;                   // keep that spot under the finger
        ClampPan();
    }

    /// Tap: a place's icon selects it; anywhere else drops a pin there. Either way the card offers SET ROUTE.
    public void Tap(Vector2 screenPoint)
    {
        if (drawer != null && drawer.gameObject.activeSelf) { drawer.gameObject.SetActive(false); return; }
        Vector2 at = ViewportPoint(screenPoint);

        Item best = null; float bestD = 46f;
        foreach (Item it in items)
        {
            if (it.icon == null || !it.icon.gameObject.activeSelf) continue;
            float d = Vector2.Distance(WorldToViewport(it.pos), at);
            if (d < bestD) { bestD = d; best = it; }
        }
        LundunNavigation nav = LundunNavigation.Instance;
        if (best == null && nav != null && nav.HasWaypoint && Vector2.Distance(WorldToViewport(nav.Waypoint), at) < 46f)
        { selected = null; LundunHaptics.Light(); Refresh(); return; }      // tapping the route's end shows the route card
        if (best == null)
        {
            pin.pos = ViewportToWorld(at);
            // E8: the outer boroughs are only scenery: a pin out there goes to the nearest point inside the map
            pin.pos = new Vector3(Mathf.Clamp(pin.pos.x, mapMinX + 30f, mapMinX + mapWidth - 30f), 0f, Mathf.Clamp(pin.pos.z, mapMinZ + 30f, mapMinZ + mapHeight - 30f));
            pin.name = NearestStreet(pin.pos, out _) ?? "Dropped pin";
            best = pin;
        }
        selected = best;
        LundunHaptics.Light();
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
        Vector3 me = vehicles != null ? vehicles.PlayerTarget.position : Vector3.zero;
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

        // police: flashing red / blue
        bool flash = Mathf.Repeat(Time.unscaledTime * 4f, 2f) < 1f;
        int used = 0;
        foreach (PoliceAI car in PoliceAI.Active)
        {
            if (car == null) continue;
            if (used >= policeIcons.Count) policeIcons.Add(Disc(blipLayer, Color.red, 18).GetComponent<Image>());
            Image img = policeIcons[used++];
            img.gameObject.SetActive(true);
            img.color = flash ? new Color(1f, 0.2f, 0.2f) : new Color(0.25f, 0.5f, 1f);
            Anchor(img.rectTransform, car.transform.position);
        }
        for (int i = used; i < policeIcons.Count; i++) policeIcons[i].gameObject.SetActive(false);

        // zoom-dependent labels
        float metresTall = viewport.rect.height / Mathf.Max(0.0001f, ppm);
        bool streets = metresTall < streetNamesBelow;
        foreach (TextMeshProUGUI s in streetLabels) if (s.gameObject.activeSelf != streets) s.gameObject.SetActive(streets);
        float da = Mathf.Lerp(0.16f, 0.42f, Mathf.InverseLerp(250f, 800f, metresTall));
        foreach (TextMeshProUGUI d in districtLabels) d.alpha = da;

        // selection ring and pin
        if (selectRing != null)
        {
            selectRing.gameObject.SetActive(selected != null);
            if (selected != null)
            {
                Anchor(selectRing, selected.pos);
                float s = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
                selectRing.localScale = new Vector3(s, s, 1f);
            }
        }
        if (pinIcon != null)
        {
            bool showPin = selected == pin;
            pinIcon.gameObject.SetActive(showPin);
            if (showPin) Anchor(pinIcon, pin.pos);
        }

        if (titleDistrict != null) titleDistrict.text = DistrictAt(me);
        UpdateCard(me, nav, hasWp);
    }

    void UpdateCard(Vector3 me, LundunNavigation nav, bool hasWp)
    {
        if (card == null) return;
        if (selected != null)
        {
            card.gameObject.SetActive(true);
            cardTitle.text = selected.name;
            cardSub.text = KindName(selected.kind) + "  ·  " + Distance(Flat(selected.pos - me)) + " away";
            SetCardIcon(selected.kind);
            cardButtonText.text = "SET ROUTE";
            cardButton.color = Purple;
        }
        else if (hasWp)
        {
            card.gameObject.SetActive(true);
            cardTitle.text = nav.WaypointName;
            cardSub.text = "Route  ·  " + Distance(nav.RouteMetres) + " by road";
            SetCardIcon("route");
            cardButtonText.text = "CLEAR ROUTE";
            cardButton.color = new Color(1f, 1f, 1f, 0.16f);
        }
        else card.gameObject.SetActive(false);
        if (routeInfo != null) routeInfo.text = hasWp ? "To: " + nav.WaypointName + "\n" + Distance(nav.RouteMetres) : "";
    }

    void CardButton()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null) return;
        if (selected != null)
        {
            nav.SetDestination(selected.pos, selected.name, selected.isPin);
            nav.ShowToast("Route set\n" + selected.name);
            selected = null;
        }
        else if (nav.HasWaypoint) nav.Clear();
        LundunHaptics.Medium();
        Refresh();
    }

    static string Distance(float m) { return m >= 1000f ? (m / 1000f).ToString("0.0") + " km" : Mathf.Max(10, Mathf.RoundToInt(m / 10f) * 10) + " m"; }
    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    static string KindName(string kind)
    {
        switch (kind)
        {
            case "safehouse": return "Safehouse";
            case "hospital": return "Hospital";
            case "police": return "Police station";
            case "tube": return "Lundun Underground";
            case "rail": return "Lundun Rail station";
            case "bike": return "Lundun Cycles dock";
            case "landmark": return "Landmark";
            case "mission": return "Job";
            case "pin": return "Map pin";
            default: return LundunMapRegistry.CardName(kind);
        }
    }

    // ------------------------------------------------------------------ contents (made once, the first time it opens)
    void BuildContents()
    {
        built = true;
        LundunNavigation nav = LundunNavigation.Instance;
        LundunWorldData w = nav != null ? nav.World : null;

        // the map fills the screen; the old side panel becomes the PLACES drawer
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        Image winBg = window.GetComponent<Image>();
        if (winBg != null) winBg.color = new Color(0.03f, 0.04f, 0.05f, 1f);
        Transform oldCross = window.transform.Find("CloseButton");
        if (oldCross != null) oldCross.gameObject.SetActive(false);

        // E8: Greater Lundun behind the map picture, and the outer boroughs' names
        if (HasBackdrop)
        {
            backdropRect = Rect("GreaterLundun", content);
            backdropRect.SetAsFirstSibling();
            backdropRect.anchorMin = backdropRect.anchorMax = backdropRect.pivot = new Vector2(0.5f, 0.5f);
            RawImage ri = backdropRect.gameObject.AddComponent<RawImage>();
            ri.texture = backdrop;
            ri.raycastTarget = false;
            for (int i = 0; i < outerNames.Length && i < outerAt.Length; i++)
            {
                TextMeshProUGUI t = Label(labelLayer, outerNames[i], new Vector3(outerAt[i].x, 0f, outerAt[i].y), 34, new Color(1f, 1f, 1f, 0.3f));
                t.characterSpacing = 18f;
                districtLabels.Add(t);
            }
        }

        // districts and streets
        Dictionary<string, TextMeshProUGUI> districtById = new Dictionary<string, TextMeshProUGUI>();
        if (w != null)
        {
            foreach (LwPoly d in w.districts)
            {
                if (d.id == "D_CANARY_PEN") continue;
                float cx = 0, cz = 0;
                foreach (LwPoint p in d.polygon) { cx += p.x; cz += p.z; }
                cx /= d.polygon.Length; cz /= d.polygon.Length;
                TextMeshProUGUI t = Label(labelLayer, d.name, new Vector3(cx, 0, cz), 40, new Color(1f, 1f, 1f, 0.4f));
                t.characterSpacing = 18f;
                districtLabels.Add(t);
                districtById[d.id] = t;
            }
            GangAreas(w, districtById);
            foreach (LwRoad r in w.roads)
            {
                if (r.roadClass == "service" || r.points == null || r.points.Length < 2 || string.IsNullOrEmpty(r.name)) continue;
                int best = 0; float bestLen = 0f;
                for (int i = 0; i + 1 < r.points.Length; i++)
                {
                    float len = Vector2.Distance(new Vector2(r.points[i].x, r.points[i].z), new Vector2(r.points[i + 1].x, r.points[i + 1].z));
                    if (len > bestLen) { bestLen = len; best = i; }
                }
                if (bestLen < 60f) continue;
                Vector2 a = new Vector2(r.points[best].x, r.points[best].z), b = new Vector2(r.points[best + 1].x, r.points[best + 1].z);
                Vector2 mid = (a + b) * 0.5f;
                float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                if (ang > 90f) ang -= 180f; else if (ang < -90f) ang += 180f;     // never upside down
                TextMeshProUGUI t = Label(labelLayer, r.name.ToUpperInvariant(), new Vector3(mid.x, 0, mid.y), 17, new Color(0.08f, 0.1f, 0.12f, 0.95f));
                t.characterSpacing = 4f;
                t.rectTransform.localEulerAngles = new Vector3(0f, 0f, ang);
                t.gameObject.SetActive(false);
                streetLabels.Add(t);
            }
        }

        // places
        if (nav != null)
        {
            foreach (string kind in KindOrder(new[] { "landmark", "mission", "rail", "tube", "police", "hospital", "safehouse" }, nav))
                foreach (LundunNavigation.Destination d in nav.Destinations)
                    if (d.kind == kind) AddItem(d.kind, d.name, d.position, d.label);
        }
        LundunBikes bikes = LundunBikes.Instance;
        if (bikes != null && bikes.Markers != null)
            foreach (Transform t in bikes.Markers) if (t != null) AddItem("bike", "Lundun Cycles", t.position);

        // live icons (on top of the places)
        if (playerCar != null)
        {
            carIcon = Disc(blipLayer, new Color(1f, 0.72f, 0.2f), 30);
            Glyph(carIcon, LundunHudArt.Car, new Color(0.08f, 0.06f, 0.02f), 0.7f);
        }
        selectRing = Img(blipLayer, LundunHudArt.Ring, Color.white, 64);
        pinIcon = Img(blipLayer, LundunHudArt.Pin, Purple, 52);
        pinIcon.pivot = new Vector2(0.5f, 0.06f);
        waypointIcon = Disc(blipLayer, Purple, 34);
        Glyph(waypointIcon, LundunHudArt.Pin, Color.white, 0.62f);
        playerIcon = Img(blipLayer, arrowSprite != null ? arrowSprite : LundunHudArt.Jump, Color.white, 40);

        BuildOverlay();
        BuildDrawer();
    }

    /// E0: the built-in kinds in their usual order, then any other kinds the expansions registered.
    /// R4b: each gang's turf, shaded in its colour (over the map picture, under the route), its name under the
    /// district's. Shown once James has been into it (LundunGangs.Discovered), checked each time the map opens.
    void GangAreas(LundunWorldData w, Dictionary<string, TextMeshProUGUI> districtById)
    {
        RectTransform layer = Rect("GangAreas", content);
        Stretch(layer);
        Transform img = content.Find("MapImage");
        layer.SetSiblingIndex(img != null ? img.GetSiblingIndex() + 1 : (labelLayer != null ? labelLayer.GetSiblingIndex() : 0));
        foreach (LundunGangs.GangArea a in LundunGangs.GangAreas)
        {
            LwPoly poly = null;
            foreach (LwPoly d in w.districts) if (d.id == a.district) poly = d;
            if (poly == null || poly.polygon == null || poly.polygon.Length < 3) continue;
            RectTransform r = Rect("Turf_" + a.district, layer);
            Stretch(r);
            LundunMapShade shade = r.gameObject.AddComponent<LundunMapShade>();
            Color fill = a.colour; fill.a = 0.16f;
            Color edge = a.colour; edge.a = 0.55f;
            shade.Set(poly.polygon, mapMinX, mapMinZ, mapWidth, mapHeight, fill, edge);
            GameObject both = r.gameObject;
            if (districtById.TryGetValue(a.district, out TextMeshProUGUI dl))
            {
                Color c = a.colour; c.a = 0.9f;
                TextMeshProUGUI g = Text(dl.rectTransform, a.name, 24, c);
                g.name = "GangName";
                g.textWrappingMode = TextWrappingModes.NoWrap;
                g.characterSpacing = 10f;
                RectTransform gr = g.rectTransform;
                gr.anchorMin = gr.anchorMax = new Vector2(0.5f, 0.5f);
                gr.sizeDelta = new Vector2(600f, 40f);
                gr.anchoredPosition = new Vector2(0f, -36f);
                gangAreas.Add(new KeyValuePair<string, GameObject>(a.district, g.gameObject));
            }
            gangAreas.Add(new KeyValuePair<string, GameObject>(a.district, both));
        }
        ShowGangAreas();
    }

    void ShowGangAreas()
    {
        foreach (KeyValuePair<string, GameObject> kv in gangAreas)
            if (kv.Value != null) kv.Value.SetActive(LundunGangs.Discovered(kv.Key));
    }

    static List<string> KindOrder(string[] builtIn, LundunNavigation nav)
    {
        List<string> r = new List<string>(builtIn);
        if (nav != null)
            foreach (LundunNavigation.Destination d in nav.Destinations)
                if (!r.Contains(d.kind) && LundunMapRegistry.Known(d.kind)) r.Add(d.kind);
        return r;
    }

    static string LetterFor(string kind)
    {
        return kind == "safehouse" ? "S" : kind == "hospital" ? "H" : kind == "police" ? "P" : LundunMapRegistry.Letter(kind);
    }

    void AddItem(string kind, string name, Vector3 pos, bool label = false)
    {
        Color col; string letter = null; Sprite glyph = null; float size = 34;
        switch (kind)
        {
            case "safehouse": col = new Color(0.25f, 0.9f, 0.45f); letter = "S"; break;
            case "hospital": col = new Color(0.95f, 0.27f, 0.32f); letter = "H"; break;
            case "police": col = new Color(0.27f, 0.52f, 1f); letter = "P"; break;
            case "tube": col = new Color(0.16f, 0.24f, 0.62f); glyph = LundunHudArt.Tube; size = 34; break;
            case "bike": col = new Color(0.1f, 0.68f, 0.64f); glyph = LundunHudArt.Bike; size = 26; break;
            case "mission": col = new Color(1f, 0.82f, 0.2f, 0.75f); letter = name.Length > 0 ? name.Substring(0, 1) : "?"; break;
            case "rail": col = KindColour("rail"); letter = "R"; size = 34; break;
            default:
                if (LundunMapRegistry.Known(kind)) { col = LundunMapRegistry.Colour(kind); letter = LundunMapRegistry.Letter(kind); size = 34; }
                else { col = new Color(0.92f, 0.92f, 0.92f, 0.9f); size = 14; }
                break;
        }
        RectTransform ic = Disc(blipLayer, col, size);
        if (letter != null)
        {
            TextMeshProUGUI t = Text(ic, letter, size * 0.66f, new Color(0.04f, 0.05f, 0.06f));
            Stretch(t.rectTransform);
        }
        else if (glyph != null) Glyph(ic, glyph, Color.white, 0.68f);
        Anchor(ic, pos);
        if (kind == "landmark" || label)
        {
            TextMeshProUGUI l = Label(labelLayer, name, pos, 20, new Color(1f, 1f, 1f, 0.75f));
            l.fontStyle = FontStyles.Italic;
            l.rectTransform.anchoredPosition = new Vector2(0f, -22f);
        }
        items.Add(new Item { kind = kind, name = name, pos = pos, icon = ic });
    }

    // ------------------------------------------------------------------ overlay: title, chips, zoom, card
    void BuildOverlay()
    {
        overlay = Rect("Overlay", window.transform);
        Stretch(overlay);

        // soft dark fade along the top, so the title reads over any part of the map
        RectTransform scrim = Img(overlay, LundunHudArt.FadeDown, new Color(0.02f, 0.03f, 0.05f, 0.9f), 10f);
        scrim.anchorMin = new Vector2(0f, 1f); scrim.anchorMax = new Vector2(1f, 1f); scrim.pivot = new Vector2(0.5f, 1f);
        scrim.offsetMin = new Vector2(-200f, -230f); scrim.offsetMax = new Vector2(200f, 200f);
        scrim.GetComponent<Image>().preserveAspect = false;

        // title + the district you're in
        TextMeshProUGUI title = Text(overlay, "LUNDUN", 50, Color.white);
        title.characterSpacing = 14f; title.alignment = TextAlignmentOptions.TopLeft;
        Corner(title.rectTransform, new Vector2(0, 1), new Vector2(40, -30), new Vector2(500, 60));
        titleDistrict = Text(overlay, "", 26, Dim);
        titleDistrict.characterSpacing = 6f; titleDistrict.alignment = TextAlignmentOptions.TopLeft;
        Corner(titleDistrict.rectTransform, new Vector2(0, 1), new Vector2(42, -88), new Vector2(500, 36));

        // close + places, top right
        RoundButton(overlay, LundunHudArt.Close, new Vector2(1, 1), new Vector2(-36, -30), 92, Close);
        RectTransform places = PillButton(overlay, LundunHudArt.List, "PLACES", new Vector2(1, 1), new Vector2(-148, -42), ToggleDrawer);
        places.pivot = new Vector2(1, 1);

        // filters, down the left
        float y = -150f;
        foreach (string kind in KindOrder(new[] { "safehouse", "hospital", "police", "tube", "rail", "bike", "landmark" }, LundunNavigation.Instance))
        {
            bool any = false;
            foreach (Item it in items) if (it.kind == kind) { any = true; break; }
            if (!any) continue;
            MakeChip(kind, y);
            y -= 60f;
        }

        // zoom and find-me, bottom right
        RoundButton(overlay, LundunHudArt.Plus, new Vector2(1, 0), new Vector2(-40, 330), 84, () => ZoomCentre(1.45f));
        RoundButton(overlay, LundunHudArt.Minus, new Vector2(1, 0), new Vector2(-40, 232), 84, () => ZoomCentre(1f / 1.45f));
        RoundButton(overlay, LundunHudArt.Locate, new Vector2(1, 0), new Vector2(-40, 134), 84, () => { if (vehicles != null) CentreOn(vehicles.PlayerTarget.position); });

        // the card, bottom centre
        card = Rect("Card", overlay);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f); card.pivot = new Vector2(0.5f, 0f);
        card.anchoredPosition = new Vector2(0f, 36f); card.sizeDelta = new Vector2(860f, 140f);
        Image cbg = card.gameObject.AddComponent<Image>();
        cbg.sprite = LundunHudArt.Panel; cbg.type = Image.Type.Sliced; cbg.pixelsPerUnitMultiplier = 0.5f; cbg.color = Ink;
        RectTransform cd = Disc(card, Color.white, 96);
        cd.anchorMin = cd.anchorMax = new Vector2(0f, 0.5f); cd.anchoredPosition = new Vector2(22f + 48f, 0f);
        cardDisc = cd.GetComponent<Image>();
        RectTransform cg = Img(cd, LundunHudArt.Pin, Color.white, 58);
        cardGlyph = cg.GetComponent<Image>();
        cardLetter = Text(cd, "", 60, new Color(0.04f, 0.05f, 0.06f)); Stretch(cardLetter.rectTransform);
        cardTitle = Text(card, "", 36, Color.white);
        cardTitle.alignment = TextAlignmentOptions.BottomLeft; cardTitle.textWrappingMode = TextWrappingModes.NoWrap; cardTitle.overflowMode = TextOverflowModes.Ellipsis;
        Corner(cardTitle.rectTransform, new Vector2(0, 0.5f), new Vector2(140f, 2f), new Vector2(400f, 46f)); cardTitle.rectTransform.pivot = new Vector2(0, 0);
        cardSub = Text(card, "", 24, Dim);
        cardSub.alignment = TextAlignmentOptions.TopLeft; cardSub.textWrappingMode = TextWrappingModes.NoWrap; cardSub.overflowMode = TextOverflowModes.Ellipsis;
        Corner(cardSub.rectTransform, new Vector2(0, 0.5f), new Vector2(140f, -2f), new Vector2(400f, 34f)); cardSub.rectTransform.pivot = new Vector2(0, 1);
        RectTransform btn = Rect("Action", card);
        btn.anchorMin = btn.anchorMax = new Vector2(1f, 0.5f); btn.pivot = new Vector2(1f, 0.5f);
        btn.anchoredPosition = new Vector2(-26f, 0f); btn.sizeDelta = new Vector2(270f, 84f);
        cardButton = btn.gameObject.AddComponent<Image>();
        cardButton.sprite = LundunHudArt.Pill; cardButton.type = Image.Type.Sliced; cardButton.pixelsPerUnitMultiplier = 0.76f;
        Button b = btn.gameObject.AddComponent<Button>(); b.targetGraphic = cardButton; b.onClick.AddListener(CardButton);
        btn.gameObject.AddComponent<LundunPressFX>();
        cardButtonText = Text(btn, "SET ROUTE", 30, Color.white); Stretch(cardButtonText.rectTransform);
        cardButtonText.characterSpacing = 4f;
        card.gameObject.SetActive(false);
    }

    void MakeChip(string kind, float y)
    {
        Chip c = new Chip { kind = kind };
        RectTransform r = Rect("Chip_" + kind, overlay);
        r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(36f, y); r.sizeDelta = new Vector2(224f, 52f);
        Image bg = r.gameObject.AddComponent<Image>();
        bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 1.23f; bg.color = Ink;
        Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
        b.onClick.AddListener(() => ToggleChip(c));
        r.gameObject.AddComponent<LundunPressFX>();
        Color col = KindColour(kind);
        RectTransform d = Disc(r, col, 38);
        d.anchorMin = d.anchorMax = new Vector2(0, 0.5f); d.anchoredPosition = new Vector2(7f + 19f, 0f);
        c.disc = d.GetComponent<Image>();
        string letter = LetterFor(kind);
        if (letter != null) { TextMeshProUGUI t = Text(d, letter, 25, new Color(0.04f, 0.05f, 0.06f)); Stretch(t.rectTransform); }
        else if (kind == "tube") Glyph(d, LundunHudArt.Tube, Color.white, 0.68f);
        else if (kind == "bike") Glyph(d, LundunHudArt.Bike, Color.white, 0.68f);
        else Glyph(d, LundunHudArt.Star, new Color(0.04f, 0.05f, 0.06f), 0.6f);
        c.label = Text(r, ChipName(kind), 21, Color.white);
        c.label.alignment = TextAlignmentOptions.MidlineLeft; c.label.characterSpacing = 3f;
        Stretch(c.label.rectTransform); c.label.rectTransform.offsetMin = new Vector2(56f, 0f);
        chips.Add(c);
    }

    void ToggleChip(Chip c)
    {
        c.on = !c.on;
        c.label.alpha = c.on ? 1f : 0.4f;
        Color dc = c.disc.color; dc.a = c.on ? 1f : 0.3f; c.disc.color = dc;
        foreach (Item it in items) if (it.kind == c.kind && it.icon != null) it.icon.gameObject.SetActive(c.on);
        if (selected != null && selected.kind == c.kind && !c.on) selected = null;
        LundunHaptics.Light();
    }

    static string ChipName(string kind)
    {
        switch (kind)
        {
            case "safehouse": return "SAFEHOUSES";
            case "hospital": return "HOSPITALS";
            case "police": return "POLICE";
            case "tube": return "UNDERGROUND";
            case "bike": return "BIKE DOCKS";
            case "rail": return "RAIL";
            default: return LundunMapRegistry.ChipName(kind);
        }
    }

    static Color KindColour(string kind)
    {
        switch (kind)
        {
            case "safehouse": return new Color(0.25f, 0.9f, 0.45f);
            case "hospital": return new Color(0.95f, 0.27f, 0.32f);
            case "police": return new Color(0.27f, 0.52f, 1f);
            case "tube": return new Color(0.16f, 0.24f, 0.62f);
            case "bike": return new Color(0.1f, 0.68f, 0.64f);
            case "rail": return new Color(0.17f, 0.55f, 0.38f);
            case "mission": return new Color(1f, 0.82f, 0.2f);
            case "route": case "pin": return Purple;
            default: return LundunMapRegistry.Colour(kind);
        }
    }

    void SetCardIcon(string kind)
    {
        cardDisc.color = KindColour(kind);
        string letter = LetterFor(kind);
        cardLetter.text = letter ?? "";
        cardGlyph.gameObject.SetActive(letter == null);
        if (letter == null)
        {
            cardGlyph.sprite = kind == "tube" ? LundunHudArt.Tube : kind == "bike" ? LundunHudArt.Bike : kind == "landmark" ? LundunHudArt.Star : LundunHudArt.Pin;
            cardGlyph.color = kind == "landmark" ? new Color(0.04f, 0.05f, 0.06f) : Color.white;
        }
    }

    // ------------------------------------------------------------------ PLACES drawer (the old side panel, restyled)
    void BuildDrawer()
    {
        if (listContainer == null) return;
        drawer = (RectTransform)listContainer.parent;
        drawer.anchorMin = new Vector2(1, 0); drawer.anchorMax = new Vector2(1, 1); drawer.pivot = new Vector2(1, 0.5f);
        drawer.sizeDelta = new Vector2(560f, 0f); drawer.anchoredPosition = Vector2.zero;
        Image bg = drawer.GetComponent<Image>();
        if (bg != null) bg.color = new Color(0.03f, 0.045f, 0.08f, 0.97f);
        foreach (TextMeshProUGUI t in drawer.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (t.transform.parent == drawer) { if (t == routeInfo) t.gameObject.SetActive(false); else { t.text = "PLACES"; t.characterSpacing = 12f; } }
        for (int i = listContainer.childCount - 1; i >= 0; i--) Destroy(listContainer.GetChild(i).gameObject);
        listContainer.offsetMax = new Vector2(listContainer.offsetMax.x, -110f);

        foreach (string kind in KindOrder(new[] { "safehouse", "hospital", "police", "tube", "rail", "landmark", "mission" }, LundunNavigation.Instance))
            foreach (Item it in items)
                if (it.kind == kind)
                {
                    Item pick = it;
                    ListButton(it.name, kind, () =>
                    {
                        drawer.gameObject.SetActive(false);
                        selected = pick;
                        if (pick.icon != null && !pick.icon.gameObject.activeSelf) pick.icon.gameObject.SetActive(true);
                        CentreOn(pick.pos);
                        Refresh();
                    });
                }
        RoundButton(drawer, LundunHudArt.Close, new Vector2(1, 1), new Vector2(-22f, -22f), 70, ToggleDrawer);
        drawer.SetAsLastSibling();
        drawer.gameObject.SetActive(false);
    }

    void ToggleDrawer()
    {
        if (drawer == null) return;
        drawer.SetAsLastSibling();
        drawer.gameObject.SetActive(!drawer.gameObject.activeSelf);
        LundunHaptics.Light();
    }

    void ListButton(string text, string kind, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform r = Rect("Place", listContainer);
        r.sizeDelta = new Vector2(0, 66);
        LayoutElement le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 66;
        Image bg = r.gameObject.AddComponent<Image>();
        bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 0.97f; bg.color = new Color(1f, 1f, 1f, 0.07f);
        Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = bg; b.onClick.AddListener(onClick);
        RectTransform d = Disc(r, KindColour(kind), 44);
        d.anchorMin = d.anchorMax = new Vector2(0, 0.5f); d.anchoredPosition = new Vector2(11f + 22f, 0f);
        string letter = kind == "mission" ? text.Substring(0, 1) : LetterFor(kind);
        if (letter != null) { TextMeshProUGUI t = Text(d, letter, 29, new Color(0.04f, 0.05f, 0.06f)); Stretch(t.rectTransform); }
        else Glyph(d, kind == "tube" ? LundunHudArt.Tube : LundunHudArt.Star, kind == "tube" ? Color.white : new Color(0.04f, 0.05f, 0.06f), 0.66f);
        TextMeshProUGUI l = Text(r, text, 27, Color.white);
        l.alignment = TextAlignmentOptions.MidlineLeft; l.textWrappingMode = TextWrappingModes.NoWrap; l.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(l.rectTransform); l.rectTransform.offsetMin = new Vector2(70f, 0f); l.rectTransform.offsetMax = new Vector2(-16f, 0f);
    }

    // ------------------------------------------------------------------ iPhone safe area for the overlay
    void FitSafeArea()
    {
        if (overlay == null || Screen.width <= 0) return;
        float k = Scale();
        Rect s = Screen.safeArea;
        float left = s.xMin / k, right = (Screen.width - s.xMax) / k, bottom = s.yMin / k, top = (Screen.height - s.yMax) / k;
        overlay.offsetMin = new Vector2(left, bottom);
        overlay.offsetMax = new Vector2(-right, -top);
        if (drawer != null) { drawer.offsetMin = new Vector2(drawer.offsetMin.x, bottom); }
    }

    // ------------------------------------------------------------------ helpers
    void ZoomCentre(float factor)
    {
        Vector3 wc = viewport.TransformPoint(viewport.rect.center);
        ZoomAt(RectTransformUtility.WorldToScreenPoint(null, wc), factor);
        LundunHaptics.Light();
    }

    string DistrictAt(Vector3 p)
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null) return "";
        foreach (LwPoly d in nav.World.districts)
            if (d.id != "D_CANARY_PEN" && Inside(p.x, p.z, d.polygon)) return d.name;
        foreach (LwPoly d in nav.World.districts)
            if (Inside(p.x, p.z, d.polygon)) return d.name;
        return "";
    }

    static bool Inside(float x, float z, LwPoint[] poly)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].z > z) != (poly[j].z > z) && x < (poly[j].x - poly[i].x) * (z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x) c = !c;
        return c;
    }

    /// Name of the street nearest to a point (within 40 m), for dropped pins.
    string NearestStreet(Vector3 p, out float dist)
    {
        dist = float.MaxValue;
        string best = null;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null) return null;
        Vector2 q = new Vector2(p.x, p.z);
        foreach (LwRoad r in nav.World.roads)
        {
            if (r.points == null) continue;
            for (int i = 0; i + 1 < r.points.Length; i++)
            {
                Vector2 a = new Vector2(r.points[i].x, r.points[i].z), b = new Vector2(r.points[i + 1].x, r.points[i + 1].z);
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                float d = Vector2.Distance(q, a + ab * t);
                if (d < dist) { dist = d; best = r.name; }
            }
        }
        return dist < 40f ? best : null;
    }

    RectTransform RoundButton(RectTransform parent, Sprite icon, Vector2 anchor, Vector2 pos, float size, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform r = Disc(parent, Ink, size);
        r.anchorMin = r.anchorMax = anchor; r.pivot = anchor; r.anchoredPosition = pos;
        Image img = r.GetComponent<Image>(); img.raycastTarget = true;
        Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(onClick);
        r.gameObject.AddComponent<LundunPressFX>();
        RectTransform rim = Img(r, LundunHudArt.Ring, new Color(1f, 1f, 1f, 0.3f), size); Stretch(rim);
        Glyph(r, icon, Color.white, 0.46f);
        return r;
    }

    RectTransform PillButton(RectTransform parent, Sprite icon, string text, Vector2 anchor, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform r = Rect("Pill_" + text, parent);
        r.anchorMin = r.anchorMax = anchor; r.anchoredPosition = pos; r.sizeDelta = new Vector2(230f, 68f);
        Image bg = r.gameObject.AddComponent<Image>();
        bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 0.94f; bg.color = Ink;
        Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = bg; b.onClick.AddListener(onClick);
        r.gameObject.AddComponent<LundunPressFX>();
        RectTransform ic = Img(r, icon, Color.white, 36);
        ic.anchorMin = ic.anchorMax = new Vector2(0, 0.5f); ic.anchoredPosition = new Vector2(44f, 0f);
        TextMeshProUGUI t = Text(r, text, 26, Color.white);
        t.characterSpacing = 6f; t.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(t.rectTransform); t.rectTransform.offsetMin = new Vector2(78f, 0f);
        return r;
    }

    RectTransform Disc(RectTransform parent, Color col, float size) { return Img(parent, LundunHudArt.Disc, col, size); }

    RectTransform Img(RectTransform parent, Sprite sprite, Color col, float size)
    {
        RectTransform rt = Rect("Icon", parent);
        rt.sizeDelta = new Vector2(size, size);
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = col; img.raycastTarget = false; img.preserveAspect = true;
        return rt;
    }

    void Glyph(RectTransform disc, Sprite sprite, Color col, float scale)
    {
        RectTransform g = Img(disc, sprite, col, disc.sizeDelta.x * scale);
        g.anchorMin = g.anchorMax = new Vector2(0.5f, 0.5f); g.anchoredPosition = Vector2.zero;
    }

    TextMeshProUGUI Label(RectTransform parent, string text, Vector3 world, float fontSize, Color col)
    {
        TextMeshProUGUI t = Text(parent, text, fontSize, col);
        t.rectTransform.sizeDelta = new Vector2(600, 60);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        Anchor(t.rectTransform, world);
        return t;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static void Corner(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = anchor; r.anchoredPosition = pos; r.sizeDelta = size;
    }

    static TextMeshProUGUI Text(RectTransform parent, string text, float size, Color col)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.color = col;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    // ------------------------------------------------------------------ maths
    float Scale() { return rootCanvas != null && rootCanvas.scaleFactor > 0 ? rootCanvas.scaleFactor : 1f; }

    void ApplyZoom()
    {
        content.sizeDelta = new Vector2(mapWidth * ppm, mapHeight * ppm);
        if (backdropRect != null)
        {
            // E8: Greater Lundun's picture, sized and placed round the map picture (content's middle is the map's middle)
            Vector4 a = backdropArea;
            backdropRect.sizeDelta = new Vector2(a.z * ppm, a.w * ppm);
            backdropRect.anchoredPosition = new Vector2(a.x + a.z * 0.5f - (mapMinX + mapWidth * 0.5f), a.y + a.w * 0.5f - (mapMinZ + mapHeight * 0.5f)) * ppm;
        }
    }

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

    /// Keep the screen over Greater Lundun (E8; before, over the map picture): if it's smaller than the screen it stays
    /// in the middle.
    void ClampPan()
    {
        Vector4 bb = Bounds;
        Vector2 half = new Vector2(bb.z, bb.w) * ppm * 0.5f, vh = viewport.rect.size * 0.5f;
        // where the bounds' middle is, from the map's middle, in UI units
        Vector2 off = new Vector2(bb.x + bb.z * 0.5f - (mapMinX + mapWidth * 0.5f), bb.y + bb.w * 0.5f - (mapMinZ + mapHeight * 0.5f)) * ppm;
        Vector2 lim = new Vector2(Mathf.Max(0f, half.x - vh.x), Mathf.Max(0f, half.y - vh.y));
        Vector2 p = content.anchoredPosition;
        content.anchoredPosition = new Vector2(Mathf.Clamp(p.x, -off.x - lim.x, -off.x + lim.x), Mathf.Clamp(p.y, -off.y - lim.y, -off.y + lim.y));
    }
}
