using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// S7: chicken shops (both modes; added by LundunGame). Bright takeaway fronts dotted along the streets, found at
/// runtime like the pubs (a flat stretch of building facing the road, away from corners, shops and story places).
/// Walk up to the door: BUY CHICKEN, £20 in Story ("on the house" in Free Roam) and you're back to full health.
public class LundunChicken : MonoBehaviour
{
    public static LundunChicken Instance { get; private set; }
    public const long Price = 20;
    const int MaxShops = 26;
    const float Width = 6.4f, Proud = 0.14f;

    static readonly string[][] Brands =
    {
        new[] { "PECKAM FRIED CHICKEN", "c8102e", "ffd200" }, new[] { "CLUCK ROYALE", "ffcc00", "c8102e" },
        new[] { "TASTY BIRD CHICKEN", "1d4fb8", "ffffff" }, new[] { "CHICKEN SHACK", "e31d1a", "ffffff" },
    };

    public class Shop { public int brand; public Vector3 at, outward, along; public Quaternion rot; public GameObject go; public Vector3 Door { get { return at + along * 2.2f + outward * 0.9f; } } }
    private readonly List<Shop> shops = new List<Shop>();

    private GameObject buttonGo;
    private TextMeshProUGUI label;
    private Image back;
    private Shop offer;
    private float nextCheck;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    IEnumerator Start()
    {
        BuildUI();
        for (int i = 0; i < 600; i++)
        {
            LundunNavigation nav = LundunNavigation.Instance;
            if (nav != null && nav.World != null && nav.Graph != null) break;
            yield return null;
        }
        yield return new WaitForSeconds(0.5f);                   // after the pubs
        yield return Find();
        Debug.Log("[Lundun] Chicken shops: " + shops.Count);
    }

    // ------------------------------------------------------------------ where they go
    IEnumerator Find()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        List<LwRoad> roads = new List<LwRoad>();
        if (nav.World.roads != null) roads.AddRange(nav.World.roads);
        if (nav.World.expansion != null && nav.World.expansion.roads != null) roads.AddRange(nav.World.expansion.roads);
        int mask = LayerMask.GetMask("Default");
        int n = 0;
        foreach (LwRoad r in roads)
        {
            if (shops.Count >= MaxShops) break;
            if (r.points == null || r.points.Length < 2) continue;
            int h = Hash(r.id);
            float run = 40f + (h % 60);                          // the first candidate along this road
            float walked = 0f;
            for (int i = 0; i < r.points.Length - 1 && shops.Count < MaxShops; i++)
            {
                Vector3 a = new Vector3(r.points[i].x, 0f, r.points[i].z), b = new Vector3(r.points[i + 1].x, 0f, r.points[i + 1].z);
                float len = Vector3.Distance(a, b);
                if (len < 1f) continue;
                Vector3 dir = (b - a) / len;
                while (run < walked + len)
                {
                    Vector3 p = a + dir * (run - walked);
                    run += 140f + (h % 50);
                    if (NearJunction(r, run - 140f - (h % 50))) continue;
                    float side = ((h + (int)run) & 1) == 0 ? 1f : -1f;
                    Shop s;
                    if (TryFront(p, dir, side, r.width, mask, h, out s) || TryFront(p, dir, -side, r.width, mask, h, out s)) shops.Add(s);
                }
                walked += len;
            }
            if (++n % 6 == 0) yield return null;
        }
    }

    static bool NearJunction(LwRoad r, float chainage)
    {
        if (r.junctionChainage == null) return false;
        foreach (float c in r.junctionChainage) if (Mathf.Abs(c - chainage) < 24f) return true;
        return false;
    }

    bool TryFront(Vector3 p, Vector3 dir, float side, float roadWidth, int mask, int h, out Shop shop)
    {
        shop = null;
        Vector3 n = new Vector3(-dir.z, 0f, dir.x) * side;       // from the road towards the buildings
        Vector3 g = LundunPlaces.Ground(p);
        float y = g.y + 1.6f;
        RaycastHit hit;
        Vector3 from = p + n * (roadWidth * 0.5f); from.y = y;
        if (!Physics.Raycast(from, n, out hit, 12f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (!IsBuilding(hit.collider)) return false;
        Vector3 face = hit.normal; face.y = 0f;
        if (face.sqrMagnitude < 0.8f || Vector3.Dot(face.normalized, -n) < 0.95f) return false;
        Vector3 Z = face.normalized, X = Vector3.Cross(Vector3.up, Z);
        foreach (float u in new[] { -Width * 0.5f + 0.2f, 0f, Width * 0.5f - 0.2f })
        {
            Vector3 o = hit.point + X * u + Z * 1.5f; o.y = y;
            RaycastHit fh;
            if (!Physics.Raycast(o, -Z, out fh, 2.6f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (!IsBuilding(fh.collider) || Vector3.Dot(fh.normal, Z) < 0.95f || Mathf.Abs(fh.distance - 1.5f) > 0.12f) return false;
        }
        Vector3 at = hit.point; at.y = LundunPlaces.Ground(hit.point + Z * 1.2f).y;
        // a clear pavement in front of the door
        Collider[] cs = Physics.OverlapBox(at + Z * 1.4f + Vector3.up * 1.2f, new Vector3(Width * 0.5f - 0.4f, 0.8f, 0.9f), Quaternion.LookRotation(Z), mask, QueryTriggerInteraction.Ignore);
        foreach (Collider c in cs) if (c.attachedRigidbody == null) return false;
        if (Reserved(at)) return false;
        foreach (Shop o in shops) if ((o.at - at).sqrMagnitude < 150f * 150f) return false;
        shop = new Shop { brand = (h / 7 + shops.Count) % Brands.Length, at = at, outward = Z, along = X, rot = Quaternion.LookRotation(Z) };
        return true;
    }

    static bool IsBuilding(Collider c)
    {
        if (c == null || c.attachedRigidbody != null) return false;
        Bounds b = c.bounds;
        return b.size.y > 4f && (b.size.x > 5f || b.size.z > 5f);
    }

    static bool Reserved(Vector3 p)
    {
        if (LundunShopInterior.At(p) != null) return true;
        foreach (LundunShopInterior s in LundunShopInterior.All)
        {
            if (s == null) continue;
            Vector3 mn = s.Min, mx = s.Max;
            if (p.x > mn.x - 20f && p.x < mx.x + 20f && p.z > mn.z - 20f && p.z < mx.z + 20f) return true;
        }
        LundunPlaces places = LundunPlaces.Instance;
        if (places != null && places.Places != null)
            foreach (LundunPlaces.Place pl in places.Places) if (pl != null && (pl.door - p).sqrMagnitude < 25f * 25f) return true;
        LundunStoryShop shop = LundunStoryShop.Instance;
        if (shop != null && shop.Ready && (shop.DoorOutside - p).sqrMagnitude < 30f * 30f) return true;
        LundunPubs pubs = LundunPubs.Instance;
        if (pubs != null) foreach (LundunPubs.Pub pb in pubs.All) if ((pb.at - p).sqrMagnitude < 30f * 30f || (pb.corner - p).sqrMagnitude < 30f * 30f) return true;
        LundunTrain t = LundunTrain.Instance;
        if (t != null && (t.Entrance - p).sqrMagnitude < 30f * 30f) return true;
        return false;
    }

    static int Hash(string s)
    {
        unchecked { int h = 17; if (s != null) foreach (char ch in s) h = h * 31 + ch; return h & 0x7fffffff; }
    }

    // ------------------------------------------------------------------ every frame
    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;
        Camera cam = Camera.main;
        if (cam != null)
            foreach (Shop s in shops)
            {
                bool want = (s.at - cam.transform.position).sqrMagnitude < 200f * 200f;
                if (want && s.go == null) Build(s);
                if (s.go != null && s.go.activeSelf != want) s.go.SetActive(want);
            }
        Prompt();
    }

    void Prompt()
    {
        offer = null;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph != null && !ph.IsDead && ph.OnFoot && !VehicleSystem.Locked && ph.Body != null)
        {
            Vector3 j = ph.Body.position;
            foreach (Shop s in shops)
            {
                if (LundunMissionRun.FlatDistance(s.Door, j) < 1.8f && Mathf.Abs(s.Door.y - j.y) < 2f) { offer = s; break; }
            }
        }
        if (offer == null) { buttonGo.SetActive(false); return; }
        bool story = LundunGame.IsStory;
        bool full = ph.Current >= ph.Max - 0.5f;
        bool skint = story && !LundunWallet.CanAfford(Price);
        string name = Brands[offer.brand][0];
        if (full) { label.text = "NOT HUNGRY\n<size=60%>" + name + " · you're on full health</size>"; back.color = new Color(0.2f, 0.2f, 0.22f, 0.92f); }
        else if (skint) { label.text = "SKINT\n<size=60%>" + name + " · £" + Price + "</size>"; back.color = new Color(0.2f, 0.2f, 0.22f, 0.92f); }
        else { label.text = "BUY CHICKEN\n<size=60%>" + (story ? "£" + Price : "on the house") + " · full health</size>"; back.color = new Color(0.62f, 0.12f, 0.06f, 0.92f); }
        buttonGo.SetActive(true);
    }

    void OnPress()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (offer == null || ph == null) return;
        if (ph.Current >= ph.Max - 0.5f) { LundunHaptics.Light(); return; }
        if (LundunGame.IsStory)
        {
            if (!LundunWallet.CanAfford(Price)) { LundunHaptics.Light(); Toast("You can't afford chicken"); return; }
            LundunWallet.Pay(Price, "Chicken & chips");
        }
        ph.Heal();
        LundunHaptics.Medium();
        string[] lines = { "Wings and chips. Back to full health.", "Two-piece meal. Lovely.", "Boneless bucket. You feel brand new.", "Spicy wings. Worth it." };
        Toast(lines[Random.Range(0, lines.Length)]);
        buttonGo.SetActive(false);
        nextCheck = Time.time + 0.8f;
    }

    static void Toast(string s) { if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(s); }

    // ------------------------------------------------------------------ the shopfront (a skin just proud of the building)
    // Local frame: +Z out to the street, +X along; the door at x = +2.2.
    // materials: 0 fascia (brand colour), 1 frame (dark), 2 window glow, 3 door glass, 4 stallriser, 5 sign glow
    void Build(Shop s)
    {
        string[] brand = Brands[s.brand];
        Color main = Hex(brand[1]), ink = Hex(brand[2]);
        LundunMeshKit k = new LundunMeshKit(6);
        float W = Width, z = Proud;
        k.Box(1, new Vector3(-W / 2f + 0.15f, 1.9f, z / 2f), new Vector3(0.3f, 3.8f, z));               // pilasters
        k.Box(1, new Vector3(W / 2f - 0.15f, 1.9f, z / 2f), new Vector3(0.3f, 3.8f, z));
        k.Box(0, new Vector3(0f, 3.35f, z / 2f + 0.08f), new Vector3(W, 0.9f, z + 0.16f));               // the fascia
        k.Box(5, new Vector3(0f, 3.35f, z + 0.17f), new Vector3(W - 0.5f, 0.68f, 0.02f));
        k.Box(4, new Vector3(-0.85f, 0.3f, z / 2f), new Vector3(4.0f, 0.6f, z));                         // stallriser, window
        k.Box(2, new Vector3(-0.85f, 1.75f, z / 2f - 0.02f), new Vector3(3.9f, 2.3f, z - 0.04f));
        k.Box(1, new Vector3(-0.85f, 2.92f, z / 2f + 0.02f), new Vector3(4.0f, 0.06f, z + 0.04f));
        k.Box(1, new Vector3(-0.85f, 0.62f, z / 2f + 0.03f), new Vector3(4.0f, 0.06f, z + 0.06f));
        k.Box(1, new Vector3(-0.85f, 1.75f, z / 2f + 0.02f), new Vector3(0.06f, 2.3f, z + 0.04f));
        k.Box(1, new Vector3(1.6f, 1.9f, z / 2f), new Vector3(0.15f, 3.8f, z));                          // between window and door
        k.Box(3, new Vector3(2.2f, 1.1f, z / 2f - 0.03f), new Vector3(1.0f, 2.2f, z - 0.06f));            // the door
        k.Box(1, new Vector3(2.2f, 2.25f, z / 2f), new Vector3(1.1f, 0.1f, z));
        k.Box(1, new Vector3(2.2f, 2.6f, z / 2f), new Vector3(1.1f, 0.6f, z));
        k.Box(0, new Vector3(2.75f, 3.4f, 0.55f), new Vector3(0.08f, 0.7f, 0.9f));                       // a projecting sign
        GameObject g = new GameObject("Chicken_" + brand[0]);
        g.transform.SetParent(transform, false);
        g.transform.SetPositionAndRotation(s.at, s.rot);
        g.AddComponent<MeshFilter>().sharedMesh = k.Build("ChickenShop");
        g.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            Mat("ChickenFascia" + s.brand, main, 0.4f, main * 0.25f),
            Mat("ChickenFrame", new Color(0.08f, 0.08f, 0.09f), 0.5f, Color.black),
            Mat("ChickenWindow", new Color(1f, 0.92f, 0.78f), 0.8f, new Color(1f, 0.85f, 0.6f) * 1.1f),
            Mat("ChickenDoor", new Color(0.55f, 0.5f, 0.42f), 0.9f, new Color(0.8f, 0.65f, 0.45f) * 0.5f),
            Mat("ChickenTiles", new Color(0.85f, 0.85f, 0.82f), 0.6f, Color.black),
            Mat("ChickenSign" + s.brand, main, 0.4f, main * 1.4f),
        };
        Words(g.transform, brand[0], new Vector3(0f, 3.37f, z + 0.19f), 180f, new Vector2(W - 0.7f, 0.56f), 3f, ink);
        Words(g.transform, "OPEN LATE · HALAL · WINGS · BURGERS", new Vector3(-0.85f, 2.7f, z + 0.01f), 180f, new Vector2(3.6f, 0.2f), 1.2f, Hex(brand[1]));
        Words(g.transform, "£" + Price + " MEAL DEAL", new Vector3(2.75f, 3.4f, 0.55f), -90f, new Vector2(0.85f, 0.5f), 1.4f, ink);
        Words(g.transform, "£" + Price + " MEAL DEAL", new Vector3(2.75f, 3.4f, 0.55f), 90f, new Vector2(0.85f, 0.5f), 1.4f, ink);
        GameObject lg = new GameObject("ChickenLight");
        lg.transform.SetParent(g.transform, false);
        lg.transform.localPosition = new Vector3(0f, 2.6f, 1.6f);
        Light li = lg.AddComponent<Light>();
        li.type = LightType.Point; li.range = 7f; li.intensity = 1.3f; li.shadows = LightShadows.None;
        li.color = Color.Lerp(main, new Color(1f, 0.9f, 0.7f), 0.6f);
        s.go = g;
    }

    private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Material Mat(string name, Color c, float smooth, Color glow)
    {
        Material m;
        if (!mats.TryGetValue(name, out m) || m == null) { m = LundunMeshKit.Mat(name, c, smooth, glow); mats[name] = m; }
        return m;
    }

    static Color Hex(string h) { Color c; return ColorUtility.TryParseHtmlString("#" + h, out c) ? c : Color.white; }

    static void Words(Transform parent, string text, Vector3 at, float yaw, Vector2 box, float size, Color colour)
    {
        GameObject g = new GameObject("Words", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        ((RectTransform)g.transform).sizeDelta = box;
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = text; t.color = colour; t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = size; t.fontSize = size;
        MeshRenderer r = g.GetComponent<MeshRenderer>();
        if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    // ------------------------------------------------------------------ UI
    void BuildUI()
    {
        GameObject cg = new GameObject("ChickenUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 62;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        buttonGo = new GameObject("ChickenPrompt", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        back = buttonGo.AddComponent<Image>();
        if (LundunHudArt.Pill != null) { back.sprite = LundunHudArt.Pill; back.type = Image.Type.Sliced; back.pixelsPerUnitMultiplier = 0.8f; }
        Button b = buttonGo.AddComponent<Button>(); b.targetGraphic = back; b.onClick.AddListener(OnPress);
        buttonGo.AddComponent<LundunPressFX>();
        RectTransform rt = (RectTransform)buttonGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 230f); rt.sizeDelta = new Vector2(520f, 120f);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(buttonGo.transform, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        label = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) label.font = LundunHud.Instance.FontAsset;
        label.fontSize = 38; label.alignment = TextAlignmentOptions.Center; label.color = Color.white;
        label.fontStyle = FontStyles.Bold; label.raycastTarget = false; label.richText = true;
        buttonGo.SetActive(false);
    }
}
