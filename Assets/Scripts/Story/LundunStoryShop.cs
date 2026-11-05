using UnityEngine;
using UnityEngine.AI;

/// S3: LAGOS FOODS, Uncle Femi's corner shop on Rye Lane (built as a walk-in shop by Lundun → 13). Added in both modes
/// by LundunGame, because the shutters are what keep it shut:
///   Free Roam   shutters down, always (the shop is part of the story, so Free Roam never opens it)
///   Story       shut until mission 01 (which takes place inside it); while a mission uses it, open; after mission 01,
///               open 7am to 11pm by the game clock with Uncle Femi behind the counter (shut if it's been smashed up)
/// The shutters roll up and down; the door's shutter blocks the doorway (and carves the NavMesh) while it's down.
/// Never shuts on James while he's inside. Missions place people with Spot() (LundunShopLayout's positions).
/// S5: smashed up (mission 08, "lagos:smashed", until mission 10 puts it right): shutters down, broken glass on the
/// pavement and the floor, a cracked window, shelves knocked over, stock everywhere, and no Uncle Femi (he's staying
/// at James's: he waits by the front door). A mission can still open it (to show the damage).
public class LundunStoryShop : MonoBehaviour
{
    public const string Id = "LAGOS_FOODS";
    public const string OpenedBy = "done:m01_one_last_favour";
    public const string SmashedFlag = "lagos:smashed";
    public static LundunStoryShop Instance { get; private set; }
    /// A mission is using the shop: open whatever the time.
    public static bool MissionOpen;

    private LundunPlaces.Place place;
    private double W, D;
    private Vector3 front, outward, along;            // the unit's front middle (on the floor), out to the street, +x
    private Transform doorShutter, windowShutter;
    private Collider doorBlock;
    private NavMeshObstacle carve;
    private float shown = 0f;                          // 0 = shutters down … 1 = rolled up
    private float nextCheck;
    private bool want;
    private static Material shutterMat;

    public bool Ready { get { return place != null; } }
    public bool IsOpen { get { return shown > 0.95f; } }
    /// Outside the door, on the pavement (also where its mission marker goes).
    public Vector3 DoorOutside { get { return place != null ? LundunMissionRun.Ground(place.door) : Vector3.zero; } }
    public float OutYaw { get { return place != null ? place.doorYaw : 0f; } }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; MissionOpen = false; }

    void Start()
    {
        LundunPlaces places = LundunPlaces.Instance;
        place = places != null ? places.Get(Id) : null;
        if (place == null) { enabled = false; return; }              // the city hasn't been re-dressed (Lundun → 13) yet
        W = place.car.x; D = place.car.z;
        outward = Quaternion.Euler(0f, place.doorYaw, 0f) * Vector3.forward;
        along = new Vector3(outward.z, 0f, -outward.x) * -1f;        // V2.Left of the outward normal, as the generator has it
        float dx = (float)LundunShopLayout.DoorX(W);
        front = place.door - outward * 1.2f - along * dx;
        front.y = (float)LundunShopLayout.FloorY;
        BuildShutters();
        want = WantOpen();
        shown = want ? 1f : 0f;
        Roll();
    }

    // ------------------------------------------------------------------ where things are
    /// A point in the shop (LundunShopLayout's frame: x along the front, z in from it).
    public Vector3 Point(double x, double z) { return front + along * (float)x - outward * (float)z; }
    /// The way someone faces: rel degrees from facing into the shop (+ = towards +x).
    public float Yaw(double rel) { return Quaternion.LookRotation(-outward).eulerAngles.y + (float)rel; }
    public Vector3 SpotPos(double[] s) { return Point(s[0], s[1]); }
    public float SpotYaw(double[] s) { return Yaw(s[2]); }
    public double Width { get { return W; } }
    public Vector3 DoorInside { get { return SpotPos(LundunShopLayout.Inside(W)); } }

    /// Is p inside the shop?
    public bool Inside(Vector3 p)
    {
        Vector3 d = p - front;
        float x = Vector3.Dot(d, along), z = -Vector3.Dot(d, outward);
        return Mathf.Abs(x) < W / 2f && z > 0.05f && z < D;
    }

    // ------------------------------------------------------------------ open or shut
    bool WantOpen()
    {
        if (!LundunGame.IsStory) return false;
        LundunStoryData d = LundunStory.Data;
        if (MissionOpen) return true;
        if (LundunMissions.StartWhere(Inside)) return true;                  // S4: a mission starts in here (S5: a replay too)
        if (Smashed) return false;
        if (d == null || !d.flags.Contains(OpenedBy)) return false;
        float h = LundunClock.Instance != null ? LundunClock.Instance.Hour : 12f;
        return h >= 7f && h < 23f;
    }

    void Update()
    {
        if (place == null) return;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            bool w = WantOpen();
            PlayerHealth ph = PlayerHealth.Instance;
            if (!w && ph != null && ph.Body != null && Inside(ph.Body.position)) w = true;     // never shut him in
            want = w;
            // the hangout: Uncle Femi behind his counter while it's open and no mission is on
            bool femi = want && LundunGame.IsStory && !MissionOpen && !LundunMissions.Running;
            bool smashed = Smashed;
            if (smashed != debrisShown) ShowDebris(smashed);
            if (femi && !smashed) LundunCrew.Resident("UNCLE FEMI", SpotPos(LundunShopLayout.Behind(W)), SpotYaw(LundunShopLayout.Behind(W)), true);   // there, not "when nobody's looking" (fix: he never turned up)
            else if (smashed && LundunGame.IsStory && !LundunMissions.Running && !LundunMissions.StartWhere(Inside))   // (not while a replay waits in here)
            {
                // S5: staying at James's while the shop's smashed: by the front door, out of the doorway
                float hy = LundunStoryPlaces.HomeYaw;
                Vector3 f = Quaternion.Euler(0f, hy, 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, f);
                LundunCrew.Resident("UNCLE FEMI", LundunMissionRun.Ground(LundunStoryPlaces.Home + f * 2.2f - side * 2.2f), hy);
            }
            else LundunCrew.NotResident("UNCLE FEMI");
        }
        float target = want ? 1f : 0f;
        if (!Mathf.Approximately(shown, target))
        {
            shown = Mathf.MoveTowards(shown, target, Time.deltaTime / 1.6f);
            Roll();
        }
    }

    // ------------------------------------------------------------------ S5: smashed up
    public static bool Smashed { get { LundunStoryData d = LundunGame.IsStory ? LundunStory.Data : null; return d != null && d.flags.Contains(SmashedFlag); } }
    private GameObject debris;
    private bool debrisShown;
    private static Material glassMat, crackMat, shelfMat;
    private static readonly Color[] Stock = { new Color(0.85f, 0.2f, 0.15f), new Color(0.95f, 0.75f, 0.2f), new Color(0.3f, 0.6f, 0.25f), new Color(0.9f, 0.9f, 0.85f), new Color(0.55f, 0.35f, 0.2f) };

    /// Mission 08 smashes it (and mission 10 puts it right): the mess appears or goes.
    public void Refresh() { nextCheck = 0f; }

    void ShowDebris(bool on)
    {
        debrisShown = on;
        if (!on) { if (debris != null) Destroy(debris); debris = null; return; }
        if (debris != null) return;
        debris = new GameObject("LagosSmashed");
        debris.transform.SetParent(transform, false);
        System.Random r = new System.Random(808);
        float y = (float)LundunShopLayout.FloorY;
        Quaternion inward = Quaternion.LookRotation(-outward);
        // the window: cracked from a brick (a crazed sheet just inside the glass)
        double wx0 = LundunShopLayout.WindowX0(W), wx1 = LundunShopLayout.WindowX1(W);
        Vector3 wc = Point((wx0 + wx1) / 2, 0.06); wc.y = (float)((LundunShopLayout.WindowY0 + LundunShopLayout.WindowY1) / 2);
        Piece(PrimitiveType.Quad, CrackMaterial(), wc, Quaternion.LookRotation(outward), new Vector3((float)(wx1 - wx0), (float)(LundunShopLayout.WindowY1 - LundunShopLayout.WindowY0), 1f));
        // broken glass: on the pavement outside the window and the door, and just inside
        for (int i = 0; i < 46; i++)
        {
            bool outside = i < 28;
            double x = wx0 + r.NextDouble() * (LundunShopLayout.DoorX(W) + 0.6 - wx0);
            double z = outside ? -0.3 - r.NextDouble() * 1.8 : 0.2 + r.NextDouble() * 2.2;
            Vector3 p = Point(x, z); p.y = (outside ? 0.16f : y) + 0.012f;
            float sz = 0.05f + (float)r.NextDouble() * 0.14f;
            Piece(PrimitiveType.Quad, GlassMaterial(), p, Quaternion.Euler(90f, (float)r.NextDouble() * 360f, 0f), new Vector3(sz, sz * (0.4f + (float)r.NextDouble()), 1f));
        }
        // shelves knocked over in the aisle, stock all over the floor
        for (int k = 0; k < 2; k++)
        {
            double x = -W / 2 + 1.0 + k * 1.6, z = 4.2 + k * 1.1;
            Vector3 p = Point(x, z); p.y = y + 0.22f;
            Quaternion q = inward * Quaternion.Euler(0f, 8f + k * 20f, 84f - k * 10f);
            Piece(PrimitiveType.Cube, ShelfMaterial(), p, q, new Vector3(1.9f, 0.42f, 0.5f));
        }
        for (int i = 0; i < 34; i++)
        {
            double x = -W / 2 + 0.5 + r.NextDouble() * (W - 2.6), z = 1.6 + r.NextDouble() * 5.2;
            Vector3 p = Point(x, z); p.y = y + 0.07f;
            float a = 0.1f + (float)r.NextDouble() * 0.12f;
            GameObject g = Piece(PrimitiveType.Cube, null, p, Quaternion.Euler((float)r.NextDouble() * 40f, (float)r.NextDouble() * 360f, (float)r.NextDouble() * 40f), new Vector3(a, a * 1.3f, a * 0.8f));
            Renderer rr = g.GetComponent<Renderer>();
            Color c = Stock[r.Next(Stock.Length)];
            rr.material = PlainMaterial(c);
        }
    }

    GameObject Piece(PrimitiveType t, Material m, Vector3 at, Quaternion rot, Vector3 size)
    {
        GameObject g = GameObject.CreatePrimitive(t);
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);           // mess underfoot, nothing to trip on
        g.transform.SetParent(debris.transform, false);
        g.transform.SetPositionAndRotation(at, rot);
        g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (m != null) r.sharedMaterial = m;
        return g;
    }

    static Material PlainMaterial(Color c)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(s != null ? s : Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    static Material GlassMaterial()
    {
        if (glassMat != null) return glassMat;
        glassMat = PlainMaterial(new Color(0.78f, 0.88f, 0.92f));
        glassMat.name = "LagosBrokenGlass";
        if (glassMat.HasProperty("_Smoothness")) glassMat.SetFloat("_Smoothness", 0.95f);
        if (glassMat.HasProperty("_Metallic")) glassMat.SetFloat("_Metallic", 0.4f);
        return glassMat;
    }

    static Material ShelfMaterial()
    {
        if (shelfMat != null) return shelfMat;
        shelfMat = PlainMaterial(new Color(0.62f, 0.64f, 0.66f));
        shelfMat.name = "LagosKnockedShelf";
        return shelfMat;
    }

    /// A crazed pane: white cracks spreading from where it was hit, on clear (drawn once, here).
    static Material CrackMaterial()
    {
        if (crackMat != null) return crackMat;
        const int N = 256;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        Color clear = new Color(1f, 1f, 1f, 0f);
        Color[] px = new Color[N * N];
        for (int i = 0; i < px.Length; i++) px[i] = clear;
        System.Random r = new System.Random(1308);
        Vector2 hit = new Vector2(N * 0.38f, N * 0.46f);
        for (int k = 0; k < 15; k++)
        {
            float a = (float)(k * Mathf.PI * 2f / 15f + r.NextDouble() * 0.3);
            Vector2 p = hit;
            float len = 50f + (float)r.NextDouble() * 120f;
            for (float d = 0; d < len; d += 0.5f)
            {
                a += (float)(r.NextDouble() - 0.5) * 0.06f;
                p += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f;
                int x = (int)p.x, y = (int)p.y;
                if (x < 0 || y < 0 || x >= N || y >= N) break;
                px[y * N + x] = new Color(1f, 1f, 1f, 0.85f);
            }
        }
        for (int ring = 1; ring <= 3; ring++)                                    // the rings round the hole
            for (int k = 0; k < 220; k++)
            {
                float a = k / 220f * Mathf.PI * 2f, rad = ring * 13f + (float)r.NextDouble() * 3f;
                int x = (int)(hit.x + Mathf.Cos(a) * rad), y = (int)(hit.y + Mathf.Sin(a) * rad);
                if (x >= 0 && y >= 0 && x < N && y < N && r.NextDouble() < 0.7) px[y * N + x] = new Color(1f, 1f, 1f, 0.7f);
            }
        for (int y = -5; y <= 5; y++) for (int x = -5; x <= 5; x++)              // the hole
            if (x * x + y * y <= 25) px[((int)hit.y + y) * N + (int)hit.x + x] = new Color(0.05f, 0.05f, 0.06f, 0.9f);
        t.SetPixels(px);
        t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
        t.Apply(false, false);
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        crackMat = new Material(s != null ? s : Shader.Find("Unlit/Transparent")) { name = "LagosCrackedWindow" };
        if (crackMat.HasProperty("_BaseMap")) crackMat.SetTexture("_BaseMap", t);
        crackMat.mainTexture = t;
        if (crackMat.HasProperty("_Surface"))
        {
            crackMat.SetFloat("_Surface", 1f); crackMat.SetFloat("_Blend", 0f);
            crackMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            crackMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            crackMat.SetFloat("_ZWrite", 0f);
            crackMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            crackMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        if (crackMat.HasProperty("_Cull")) crackMat.SetFloat("_Cull", 0f);
        return crackMat;
    }

    // ------------------------------------------------------------------ the shutters
    void BuildShutters()
    {
        double dh = LundunShopLayout.DoorHalf, dx = LundunShopLayout.DoorX(W);
        double wx0 = LundunShopLayout.WindowX0(W), wx1 = LundunShopLayout.WindowX1(W);
        doorShutter = Shutter("DoorShutter", dx, (float)(dh * 2 + 0.1), (float)LundunShopLayout.DoorTop);
        windowShutter = Shutter("WindowShutter", (wx0 + wx1) / 2, (float)(wx1 - wx0 + 0.1), 2.95f);
        doorBlock = doorShutter.GetComponent<Collider>();
        carve = doorShutter.gameObject.AddComponent<NavMeshObstacle>();
        carve.carving = true;
        Collider wc = windowShutter.GetComponent<Collider>();
        if (wc != null) Destroy(wc);                                   // the window has its own collider
    }

    Transform Shutter(string name, double x, float width, float top)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(transform, false);
        g.transform.rotation = Quaternion.LookRotation(outward);
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = ShutterMaterial();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Vector3 at = Point(x, -0.22f);
        g.transform.position = new Vector3(at.x, 0f, at.z);
        g.transform.localScale = new Vector3(width, top, 0.06f);
        g.AddComponent<LundunShutterTop>().top = top;
        return g.transform;
    }

    /// Down = covering the opening from the pavement to the top; up = rolled into the box under the fascia.
    void Roll()
    {
        foreach (Transform s in new[] { doorShutter, windowShutter })
        {
            if (s == null) continue;
            float top = s.GetComponent<LundunShutterTop>().top;
            float h = Mathf.Max(0.05f, top * (1f - shown));
            Vector3 p = s.position; p.y = top - h / 2f;
            s.position = p;
            Vector3 sc = s.localScale; sc.y = h; s.localScale = sc;
        }
        bool down = shown < 0.6f;
        if (doorBlock != null) doorBlock.enabled = down;
        if (carve != null) carve.enabled = down;
    }

    static Material ShutterMaterial()
    {
        if (shutterMat != null) return shutterMat;
        Texture2D t = new Texture2D(4, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
        {
            float v = (y % 4 == 0) ? 0.3f : (y % 4 == 1 ? 0.55f : 0.47f);
            for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(v, v * 1.02f, v * 1.05f, 1f));
        }
        t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Bilinear;
        t.Apply(false, false);
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        shutterMat = new Material(s != null ? s : Shader.Find("Standard")) { name = "LagosShutter" };
        if (shutterMat.HasProperty("_BaseMap")) { shutterMat.SetTexture("_BaseMap", t); shutterMat.SetTextureScale("_BaseMap", new Vector2(1f, 12f)); }
        shutterMat.mainTexture = t;
        shutterMat.mainTextureScale = new Vector2(1f, 12f);
        if (shutterMat.HasProperty("_Smoothness")) shutterMat.SetFloat("_Smoothness", 0.35f);
        if (shutterMat.HasProperty("_Metallic")) shutterMat.SetFloat("_Metallic", 0.6f);
        return shutterMat;
    }
}

/// S3: how tall a shutter is when it's all the way down.
public class LundunShutterTop : MonoBehaviour { public float top; }
