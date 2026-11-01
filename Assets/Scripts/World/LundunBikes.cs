using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Lundun Cycles (R9): the docking stations and your hire bike.
/// Walk up to a dock → "HIRE A BIKE". Riding → "GET OFF" ("DOCK BIKE" at a dock). Near the bike you left → "RIDE".
/// One bike at a time; one you've ridden away from is cleared up. Added automatically by LundunDressing.
public class LundunBikes : MonoBehaviour
{
    public static LundunBikes Instance { get; private set; }

    [SerializeField] private float dockReach = 3.2f;
    [SerializeField] private float bikeReach = 2.4f;
    [SerializeField] private float clearAbandonedBeyond = 80f;
    [SerializeField] private float crashDamagePerMs = 2.5f;
    [Tooltip("Bike button, measured from the bottom-right corner (1920x1080 layout)")]
    [SerializeField] private Vector2 buttonPosition = new Vector2(-220f, 690f);

    private Vector3[] dockCentre, dockAlong, dockOut;
    private Transform[] markers;
    private Mesh frameMesh, wheelMesh;
    private Material material;
    private LundunBike bike;
    private Transform james;
    private VehicleSystem vehicles;
    private CameraFollow cam;
    private GameObject buttonGo;
    private TextMeshProUGUI buttonText;
    private enum Offer { None, Hire, Ride, GetOff, Dock }
    private Offer offer;
    private int offerDock = -1;
    private float nextCheck;

    public Transform[] Markers { get { return markers; } }
    public bool Riding { get { return bike != null && bike.Ridden; } }

    public void Init(Vector4[] docks, Vector4[] docksOut, Mesh frame, Mesh wheel, Material mat)
    {
        frameMesh = frame; wheelMesh = wheel; material = mat;
        int n = docks.Length;
        dockCentre = new Vector3[n]; dockAlong = new Vector3[n]; dockOut = new Vector3[n]; markers = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            dockCentre[i] = new Vector3(docks[i].x, 0.15f, docks[i].y);
            dockAlong[i] = new Vector3(docks[i].z, 0f, docks[i].w);
            dockOut[i] = i < docksOut.Length ? new Vector3(docksOut[i].x, 0f, docksOut[i].y) : Vector3.forward;
            GameObject m = new GameObject("BikeDock_" + i);
            m.transform.SetParent(transform, false);
            m.transform.position = dockCentre[i];
            markers[i] = m.transform;
        }
    }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }
    void OnEnable() { VehicleSystem.Entering += OnEnteringCar; VehicleSystem.Respawned += OnRespawned; }
    void OnDisable() { VehicleSystem.Entering -= OnEnteringCar; VehicleSystem.Respawned -= OnRespawned; }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        cam = FindAnyObjectByType<CameraFollow>();
        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) james = pc.transform;
        BuildButton();
    }

    void Update()
    {
        // cutscenes (BUSTED, WASTED, the Tube) and dying: off the bike first
        if (Riding && (VehicleSystem.Locked || (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead))) GetOff(false);

        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;
        offer = Offer.None; offerDock = -1;
        if (james == null || frameMesh == null || dockCentre == null) { Show(); return; }

        Vector3 p = Riding ? bike.transform.position : james.position;
        offerDock = NearestDock(p);
        if (Riding) offer = offerDock >= 0 ? Offer.Dock : Offer.GetOff;
        else if (!VehicleSystem.Locked && vehicles != null && !vehicles.IsDriving && PlayerHealth.Instance != null && !PlayerHealth.Instance.IsDead)
        {
            if (bike != null && Flat(bike.transform.position - p) < bikeReach) offer = Offer.Ride;
            else if (offerDock >= 0) offer = Offer.Hire;
        }

        // tidy up a bike you've left behind
        if (bike != null && !bike.Ridden && Flat(bike.transform.position - p) > clearAbandonedBeyond) { Destroy(bike.gameObject); bike = null; }
        Show();
    }

    int NearestDock(Vector3 p)
    {
        for (int i = 0; i < dockCentre.Length; i++)
        {
            Vector3 d = p - dockCentre[i]; d.y = 0f;
            float along = Vector3.Dot(d, dockAlong[i]);
            float across = Vector3.Dot(d, dockOut[i]);
            if (Mathf.Abs(along) < 5f && across > -1f && across < dockReach) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ actions
    void OnButton()
    {
        switch (offer)
        {
            case Offer.Hire: Hire(offerDock); break;
            case Offer.Ride: Ride(); break;
            case Offer.GetOff: GetOff(false); break;
            case Offer.Dock: GetOff(true); break;
        }
        nextCheck = 0f;
    }

    void Hire(int dock)
    {
        if (dock < 0) return;
        if (bike != null) Destroy(bike.gameObject);
        GameObject g = new GameObject("LundunCycle");
        Vector3 at = dockCentre[dock] + dockOut[dock] * 1.6f;
        Vector3 face = Vector3.Dot(james.forward, dockAlong[dock]) >= 0 ? dockAlong[dock] : -dockAlong[dock];
        g.transform.SetPositionAndRotation(at, Quaternion.LookRotation(face));
        g.AddComponent<CharacterController>();
        bike = g.AddComponent<LundunBike>();
        bike.Build(frameMesh, wheelMesh, material);
        Ride();
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast("Lundun Cycles\nJoystick up to pedal");
    }

    void Ride()
    {
        if (bike == null) return;
        if (bike.Crashed) bike.transform.rotation = Quaternion.Euler(0f, bike.transform.eulerAngles.y, 0f);
        bike.Mount(james);
        if (cam != null) cam.SetTarget(bike.transform, true);
    }

    void GetOff(bool dockIt)
    {
        if (bike == null) return;
        Transform j = bike.Dismount(false);
        if (cam != null && j != null) cam.SetTarget(j, false);
        if (dockIt)
        {
            Destroy(bike.gameObject); bike = null;
            if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast("Bike docked");
        }
    }

    /// Knocked off by a car or a wall.
    public void ThrownOff(LundunBike b, float impact)
    {
        Transform j = b.Dismount(true);
        if (cam != null && j != null) cam.SetTarget(j, false);
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.TakeDamage(impact * crashDamagePerMs);
        LundunPlayerReact.Punched();
    }

    void OnEnteringCar(CarController c) { if (Riding) GetOff(false); }
    void OnRespawned() { if (Riding) GetOff(false); if (bike != null) { Destroy(bike.gameObject); bike = null; } }

    // ------------------------------------------------------------------ UI
    void Show()
    {
        if (buttonGo == null) return;
        buttonGo.SetActive(offer != Offer.None);
        if (offer == Offer.Hire) buttonText.text = "HIRE";
        else if (offer == Offer.Ride) buttonText.text = "RIDE";
        else if (offer == Offer.GetOff) buttonText.text = "GET OFF";
        else if (offer == Offer.Dock) buttonText.text = "DOCK";
    }

    void BuildButton()
    {
        GameObject cg = new GameObject("BikeUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas c = cg.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 59;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        buttonGo = new GameObject("BikeButton", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        RectTransform rt = (RectTransform)buttonGo.transform;
        // right-hand side, above the GET IN / jump buttons, so it never covers James on the bike
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = buttonPosition; rt.sizeDelta = new Vector2(260f, 100f);
        Image img = buttonGo.AddComponent<Image>(); img.color = new Color(0f, 0.55f, 0.55f, 0.92f);
        Button b = buttonGo.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(OnButton);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(buttonGo.transform, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        buttonText = tg.AddComponent<TextMeshProUGUI>();
        buttonText.fontSize = 34; buttonText.alignment = TextAlignmentOptions.Center; buttonText.color = Color.white;
        buttonText.fontStyle = FontStyles.Bold; buttonText.raycastTarget = false; buttonText.richText = true;
        buttonGo.SetActive(false);
    }

    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
}
