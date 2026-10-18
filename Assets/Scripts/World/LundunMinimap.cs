using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Round GTA-style minimap, top-left. Turns with the camera, zooms out with speed,
/// shows police, the safehouse, hospital, police station, missions and your parked car.
/// Built by Lundun → 5  Build Minimap (which also fills in every field below).
public class LundunMinimap : MonoBehaviour, IPointerClickHandler
{
    /// Pass 3: the full map listens to this.
    public static event Action Tapped;

    [Header("Parts (filled in by the menu)")]
    [SerializeField] private RectTransform hudRoot;        // minimap + health bar; moved clear of the notch
    [SerializeField] private RectTransform rotator;
    [SerializeField] private RawImage map;
    [SerializeField] private RectTransform blipLayer;
    [SerializeField] private RectTransform playerArrow;
    [SerializeField] private RectTransform north;
    [SerializeField] private Image border;
    [SerializeField] private Sprite dotSprite;
    [SerializeField] private Transform poiGroup;           // World/POIs
    [SerializeField] private Transform playerCar;

    [Header("Map picture area (world metres)")]
    [SerializeField] private float mapMinX = -150f;
    [SerializeField] private float mapMinZ = -150f;
    [SerializeField] private float mapWidth = 1900f;
    [SerializeField] private float mapHeight = 1500f;

    [Header("Zoom: metres from centre to edge")]
    [SerializeField] private float onFootRadius = 90f;
    [SerializeField] private float drivingRadius = 140f;
    [SerializeField] private float fastRadius = 230f;
    [SerializeField] private float fastSpeed = 30f;          // m/s where the map is fully zoomed out

    [Header("Look")]
    [SerializeField] private Color borderColour = new Color(0.85f, 0.88f, 0.92f, 0.9f);
    [SerializeField] private Color wantedRed = new Color(1f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color wantedBlue = new Color(0.2f, 0.45f, 1f, 1f);

    class Blip
    {
        public Transform target;
        public RectTransform rt; public Image img; public bool clampToEdge; public float alpha = 1f;
    }

    private readonly List<Blip> statics = new List<Blip>();
    private readonly List<Blip> dynamicPool = new List<Blip>();
    private Blip carBlip, waypointBlip;
    private VehicleSystem vehicles;
    private Canvas rootCanvas;
    private Vector2 hudBasePos;
    private float radiusM;
    private float nextSafeArea;

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        Canvas c = GetComponentInParent<Canvas>();
        rootCanvas = c != null ? c.rootCanvas : null;
        if (hudRoot != null) hudBasePos = hudRoot.anchoredPosition;
        radiusM = onFootRadius;

        LundunPlaces places = LundunPlaces.Instance;
        if (places != null)
            foreach (LundunPlaces.Place p in places.Places)
            {
                Transform t = places.Marker(p.id);
                if (t == null) continue;
                if (p.kind == "safehouse") AddStatic(t, new Color(0.25f, 0.9f, 0.45f), "S", 28, true, 1f);
                else if (p.kind == "hospital") AddStatic(t, new Color(0.95f, 0.25f, 0.3f), "H", 26, false, 1f);
                else if (p.kind == "police") AddStatic(t, new Color(0.25f, 0.5f, 1f), "P", 26, false, 1f);
            }
        LundunBikes bikes = LundunBikes.Instance;
        if (bikes != null && bikes.Markers != null)
            foreach (Transform t in bikes.Markers) AddStatic(t, new Color(0f, 0.64f, 0.64f), "B", 18, false, 0.9f);
        LundunTube tube = LundunTube.Instance;
        if (tube != null && tube.Markers != null)
            foreach (Transform t in tube.Markers) AddStatic(t, new Color(0.11f, 0.15f, 0.4f), "U", 24, false, 1f);
        if (poiGroup != null)
            foreach (Transform t in poiGroup)
            {
                string id = t.name;
                if (places != null && (id == "POI_SAFEHOUSE" || id == "POI_HOSPITAL" || id == "POI_POLICE")) continue;
                if (id == "POI_SAFEHOUSE") AddStatic(t, new Color(0.25f, 0.9f, 0.45f), "S", 30, true, 1f);
                else if (id == "POI_HOSPITAL") AddStatic(t, new Color(0.95f, 0.25f, 0.3f), "H", 26, false, 1f);
                else if (id == "POI_POLICE") AddStatic(t, new Color(0.25f, 0.5f, 1f), "P", 26, false, 1f);
                else if (id.Length >= 2 && id[0] == 'M' && char.IsDigit(id[1])) AddStatic(t, new Color(1f, 0.82f, 0.2f), id.Substring(1, 1), 26, true, 0.45f);   // M1_ONFOOT, M2… greyed until missions exist
            }
        waypointBlip = NewBlip(new Color(0.72f, 0.36f, 1f, 1f), "", 22);
        waypointBlip.clampToEdge = true;
        if (playerCar != null)
        {
            carBlip = NewBlip(new Color(1f, 0.85f, 0.25f), "", 14);
            carBlip.target = playerCar;
        }
    }

    void AddStatic(Transform t, Color col, string label, float size, bool clamp, float alpha)
    {
        Blip b = NewBlip(col, label, size);
        b.target = t; b.clampToEdge = clamp; b.alpha = alpha;
        b.img.color = new Color(col.r, col.g, col.b, alpha);
        statics.Add(b);
    }

    Blip NewBlip(Color col, string label, float size)
    {
        GameObject g = new GameObject("Blip", typeof(RectTransform));
        g.transform.SetParent(blipLayer, false);
        Blip b = new Blip { rt = (RectTransform)g.transform };
        b.rt.sizeDelta = new Vector2(size, size);
        b.img = g.AddComponent<Image>();
        b.img.sprite = dotSprite;
        b.img.color = col;
        b.img.raycastTarget = false;
        if (!string.IsNullOrEmpty(label))
        {
            GameObject tg = new GameObject("Label", typeof(RectTransform));
            tg.transform.SetParent(g.transform, false);
            RectTransform trt = (RectTransform)tg.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            TextMeshProUGUI t = tg.AddComponent<TextMeshProUGUI>();
            t.text = label; t.fontSize = size * 0.62f; t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center; t.color = new Color(0.05f, 0.05f, 0.05f);
            t.raycastTarget = false;
        }
        return b;
    }

    void LateUpdate()
    {
        if (vehicles == null || map == null) return;
        Transform player = vehicles.PlayerTarget;
        Vector3 centre = player.position;
        Camera cam = Camera.main;
        float camYaw = cam != null ? cam.transform.eulerAngles.y : player.eulerAngles.y;

        // zoom out with speed
        float speed = vehicles.PlayerSpeed;
        float want = vehicles.IsDriving ? Mathf.Lerp(drivingRadius, fastRadius, Mathf.InverseLerp(8f, fastSpeed, speed)) : onFootRadius;
        radiusM = Mathf.Lerp(radiusM, want, 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime));

        RectTransform self = (RectTransform)transform;
        float radiusPx = self.rect.width * 0.5f;
        float ppm = radiusPx / radiusM;

        // map picture: player's spot sits at the centre, map turns with the camera
        RectTransform mrt = map.rectTransform;
        mrt.sizeDelta = new Vector2(mapWidth * ppm, mapHeight * ppm);
        mrt.pivot = new Vector2((centre.x - mapMinX) / mapWidth, (centre.z - mapMinZ) / mapHeight);
        mrt.anchoredPosition = Vector2.zero;
        rotator.localEulerAngles = new Vector3(0f, 0f, camYaw);

        playerArrow.localEulerAngles = new Vector3(0f, 0f, camYaw - player.eulerAngles.y);
        if (north != null) north.anchoredPosition = Rotate(new Vector2(0f, radiusPx - 16f), camYaw);

        float edge = radiusPx - 14f;
        foreach (Blip b in statics) Place(b, centre, ppm, camYaw, edge);

        LundunNavigation nav = LundunNavigation.Instance;
        if (nav != null && nav.HasWaypoint) PlaceAt(waypointBlip, nav.Waypoint, centre, ppm, camYaw, edge);
        else waypointBlip.rt.gameObject.SetActive(false);

        if (carBlip != null)
        {
            bool show = !(vehicles.IsDriving && player == playerCar);
            carBlip.rt.gameObject.SetActive(show);
            if (show) Place(carBlip, centre, ppm, camYaw, edge);
        }

        // police: flashing red/blue cars, small blue dots for officers on foot
        // W1b: everyday patrols (not chasing) show as plain blue, and only when they're close
        bool flash = Mathf.Repeat(Time.unscaledTime * 4f, 2f) < 1f;
        int used = 0;
        const float patrolShowWithin = 90f;
        foreach (PoliceAI car in PoliceAI.Active)
        {
            if (car == null) continue;
            if (car.OnPatrol)
            {
                if ((car.transform.position - centre).sqrMagnitude < patrolShowWithin * patrolShowWithin)
                    PlaceDynamic(ref used, car.transform.position, wantedBlue, 14f, centre, ppm, camYaw, edge);
            }
            else PlaceDynamic(ref used, car.transform.position, flash ? wantedRed : wantedBlue, 16f, centre, ppm, camYaw, edge);
        }
        foreach (PoliceOfficer o in PoliceOfficer.Active)
        {
            if (o == null) continue;
            if (o.OnBeat && (o.transform.position - centre).sqrMagnitude > patrolShowWithin * patrolShowWithin) continue;
            PlaceDynamic(ref used, o.transform.position, wantedBlue, 9f, centre, ppm, camYaw, edge);
        }
        for (int i = used; i < dynamicPool.Count; i++) dynamicPool[i].rt.gameObject.SetActive(false);

        // border flashes while wanted
        WantedSystem w = WantedSystem.Instance;
        if (border != null) border.color = (w != null && w.Stars > 0) ? (flash ? wantedRed : wantedBlue) : borderColour;

        if (Time.unscaledTime >= nextSafeArea) { nextSafeArea = Time.unscaledTime + 1f; FitSafeArea(); }
    }

    void Place(Blip b, Vector3 centre, float ppm, float camYaw, float edge)
    {
        if (b.target == null) { b.rt.gameObject.SetActive(false); return; }
        PlaceAt(b, b.target.position, centre, ppm, camYaw, edge);
    }

    void PlaceAt(Blip b, Vector3 p, Vector3 centre, float ppm, float camYaw, float edge)
    {
        Vector2 off = Rotate(new Vector2(p.x - centre.x, p.z - centre.z) * ppm, camYaw);
        bool outside = off.magnitude > edge;
        if (outside && !b.clampToEdge) { b.rt.gameObject.SetActive(false); return; }
        b.rt.gameObject.SetActive(true);
        b.rt.anchoredPosition = outside ? off.normalized * edge : off;
        b.rt.localScale = outside ? Vector3.one * 0.8f : Vector3.one;
    }

    void PlaceDynamic(ref int used, Vector3 pos, Color col, float size, Vector3 centre, float ppm, float camYaw, float edge)
    {
        Vector2 off = Rotate(new Vector2(pos.x - centre.x, pos.z - centre.z) * ppm, camYaw);
        if (off.magnitude > edge) return;
        if (used >= dynamicPool.Count) dynamicPool.Add(NewBlip(col, "", size));
        Blip b = dynamicPool[used++];
        b.rt.gameObject.SetActive(true);
        b.rt.sizeDelta = new Vector2(size, size);
        b.img.color = col;
        b.rt.anchoredPosition = off;
    }

    /// Rotates a map offset (x = east, y = north) into minimap space for a camera facing camYaw.
    static Vector2 Rotate(Vector2 v, float camYaw)
    {
        float a = camYaw * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    /// Keeps the minimap clear of the iPhone's notch / rounded corners.
    void FitSafeArea()
    {
        if (hudRoot == null || rootCanvas == null) return;
        Rect safe = Screen.safeArea;
        float scale = rootCanvas.scaleFactor > 0 ? rootCanvas.scaleFactor : 1f;
        float left = safe.xMin / scale, top = (Screen.height - safe.yMax) / scale;
        hudRoot.anchoredPosition = hudBasePos + new Vector2(left, -top);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (Tapped != null) Tapped();
    }
}
