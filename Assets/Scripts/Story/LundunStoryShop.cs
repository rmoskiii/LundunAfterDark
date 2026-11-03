using UnityEngine;
using UnityEngine.AI;

/// S3: LAGOS FOODS, Uncle Femi's corner shop on Rye Lane (built as a walk-in shop by Lundun → 13). Added in both modes
/// by LundunGame, because the shutters are what keep it shut:
///   Free Roam   shutters down, always (the shop is part of the story, so Free Roam never opens it)
///   Story       shut until mission 01 (which takes place inside it); while a mission uses it, open; after mission 01,
///               open 7am to 11pm by the game clock with Uncle Femi behind the counter (shut if it's been smashed up)
/// The shutters roll up and down; the door's shutter blocks the doorway (and carves the NavMesh) while it's down.
/// Never shuts on James while he's inside. Missions place people with Spot() (LundunShopLayout's positions).
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
        if (d != null && d.flags.Contains(SmashedFlag) && !MissionOpen) return false;
        if (MissionOpen) return true;
        if (LundunMissions.StartWhere(Inside)) return true;                  // S4: a mission starts in here
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
            if (femi) LundunCrew.Resident("UNCLE FEMI", SpotPos(LundunShopLayout.Behind(W)), SpotYaw(LundunShopLayout.Behind(W)));
            else LundunCrew.NotResident("UNCLE FEMI");
        }
        float target = want ? 1f : 0f;
        if (!Mathf.Approximately(shown, target))
        {
            shown = Mathf.MoveTowards(shown, target, Time.deltaTime / 1.6f);
            Roll();
        }
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
