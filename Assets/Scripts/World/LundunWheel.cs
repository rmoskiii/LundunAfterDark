using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// E7: the Lundun Wheel. Turns slowly (one turn every few minutes), its 32 capsules staying level. Up close it's the
/// detailed model; from further away than the fog it swaps to a simple version on the Landmark layer, which the camera
/// draws much further than everything else, so the Wheel stands on the skyline from across the city. At night the rim
/// and the capsules light up.
/// Ride it: walk up the steps onto the platform and "RIDE THE WHEEL" appears. James gets into the capsule at the
/// bottom and goes once round (the camera follows the capsule, looking out over the river); he gets off on the
/// platform at the end. Closed while you're wanted.
/// Built by Lundun → 27.
public class LundunWheel : MonoBehaviour
{
    [Header("Filled in by Lundun → 27")]
    [SerializeField] private Transform rotor;
    [SerializeField] private Transform farRotor;
    [SerializeField] private Transform[] capsules = new Transform[0];
    [SerializeField] private GameObject nearRoot, farRoot;
    [SerializeField] private Renderer[] lightRenderers = new Renderer[0];     // rim and capsule lights (near and far)
    [SerializeField] private Renderer[] farBodyRenderers = new Renderer[0];
    [SerializeField] private float hubHeight = 66.9f;
    [SerializeField] private float capsuleRadius = 64f;
    [SerializeField] private Vector3 boardSpot, exitSpot;                      // in the Wheel's space

    [Header("Turning")]
    [SerializeField] private float secondsPerTurn = 180f;
    [Header("Seeing it from afar")]
    [Tooltip("Swap to the simple version beyond this distance (m)")]
    [SerializeField] private float farFrom = 420f;
    [Tooltip("How far the camera draws landmarks (m)")]
    [SerializeField] private float landmarkDrawDistance = 2200f;
    [Header("Lights")]
    [SerializeField] private Color glow = new Color(0.82f, 0.9f, 1f);
    [SerializeField] private float glowStrength = 2.6f;
    [SerializeField] private Color dayTint = new Color(0.92f, 0.93f, 0.95f), nightTint = new Color(0.16f, 0.18f, 0.24f);
    [Header("Riding")]
    [SerializeField] private float boardDistance = 7f;

    private float angle;                 // degrees the rotor has turned
    private bool far;
    private float nextCheck, nextLight;
    private Material[] lightMats, farMats;
    private LundunLighting lighting;

    // riding
    private bool riding;
    private int rideCapsule;
    private float rideTurned;
    private Transform rideCam;
    private GameObject playerGo;
    private VehicleSystem vehicles;
    private CameraFollow cam;
    private GameObject promptGo;
    private TextMeshProUGUI promptText;

    public bool Riding { get { return riding; } }

    public void SetData(Transform rot, Transform farRot, Transform[] caps, GameObject near, GameObject farObj, Renderer[] lights, Renderer[] farBodies,
                        float hub, float capR, Vector3 board, Vector3 exit)
    {
        rotor = rot; farRotor = farRot; capsules = caps; nearRoot = near; farRoot = farObj; lightRenderers = lights; farBodyRenderers = farBodies;
        hubHeight = hub; capsuleRadius = capR; boardSpot = board; exitSpot = exit;
    }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        cam = FindAnyObjectByType<CameraFollow>();
        lighting = FindAnyObjectByType<LundunLighting>();
        // our own copies of the light and far materials, so the time of day can change them
        lightMats = new Material[lightRenderers.Length];
        for (int i = 0; i < lightRenderers.Length; i++) if (lightRenderers[i] != null) lightMats[i] = lightRenderers[i].material;
        farMats = new Material[farBodyRenderers.Length];
        for (int i = 0; i < farBodyRenderers.Length; i++) if (farBodyRenderers[i] != null) farMats[i] = farBodyRenderers[i].material;
        rideCam = new GameObject("WheelRideCamera").transform; rideCam.SetParent(transform, false);
        SeeFar();
        SetFar(false);
        BuildUI();
    }

    /// The camera keeps its usual distance for everything except the Landmark layer, which it draws much further.
    void SeeFar()
    {
        Camera c = Camera.main;
        int layer = LayerMask.NameToLayer("Landmark");
        if (c == null || layer < 0) return;
        float baseFar = c.farClipPlane;
        float[] d = c.layerCullDistances;
        bool set = false; for (int i = 0; i < 32; i++) if (d[i] > 0f) set = true;
        if (!set)
        {
            for (int i = 0; i < 32; i++) d[i] = baseFar;
            d[layer] = 0f;                            // 0 = the far clip plane
            c.farClipPlane = Mathf.Max(baseFar, landmarkDrawDistance);
            c.layerCullDistances = d;
        }
    }

    void SetFar(bool f)
    {
        far = f;
        if (nearRoot != null) nearRoot.SetActive(!f || riding);
        if (farRoot != null) farRoot.SetActive(f && !riding);
    }

    void Update()
    {
        angle = (angle + 360f / Mathf.Max(10f, secondsPerTurn) * Time.deltaTime) % 360f;
        Quaternion spin = Quaternion.Euler(0f, 0f, angle);
        if (rotor != null && !far) rotor.localRotation = spin;
        if (farRotor != null && far) farRotor.localRotation = spin;
        if (!far || riding) PlaceCapsules();

        if (riding)
        {
            rideTurned += 360f / Mathf.Max(10f, secondsPerTurn) * Time.deltaTime;
            Transform cp = capsules[rideCapsule];
            rideCam.SetPositionAndRotation(cp.position + Vector3.up * 0.3f, Quaternion.LookRotation(transform.forward, Vector3.up));
            if (playerGo != null) playerGo.transform.position = cp.position;
            if (rideTurned >= 359f) GetOff();
        }

        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.25f;
            Camera c = Camera.main;
            if (c != null)
            {
                Vector3 d = c.transform.position - (transform.position + Vector3.up * hubHeight * 0.5f); d.y = 0f;
                float dist = d.magnitude;
                if (!far && dist > farFrom + 15f) SetFar(true);
                else if (far && dist < farFrom - 15f) SetFar(false);
            }
            UpdatePrompt();
        }
        if (Time.time >= nextLight) { nextLight = Time.time + 0.5f; UpdateLights(); }
    }

    /// Capsule k sits at angle + k × 360/32 round the rim (0 = the bottom), always level.
    void PlaceCapsules()
    {
        int n = capsules.Length;
        for (int k = 0; k < n; k++)
        {
            if (capsules[k] == null) continue;
            float a = (CapsuleAngle(k)) * Mathf.Deg2Rad;
            capsules[k].localPosition = new Vector3(Mathf.Sin(a) * capsuleRadius, hubHeight - Mathf.Cos(a) * capsuleRadius, 0f);
            capsules[k].localRotation = Quaternion.identity;
        }
    }

    float CapsuleAngle(int k) { return angle + k * 360f / Mathf.Max(1, capsules.Length); }

    void UpdateLights()
    {
        if (lighting == null) lighting = FindAnyObjectByType<LundunLighting>();
        float level = lighting != null ? lighting.Current.lampLevel : 1f;
        Color e = glow * (glowStrength * level);
        foreach (Material m in lightMats) if (m != null) m.SetColor("_EmissionColor", e);
        Color tint = Color.Lerp(dayTint, nightTint, level);
        foreach (Material m in farMats) if (m != null) m.SetColor("_Color", tint);
    }

    // ------------------------------------------------------------------ riding
    void UpdatePrompt()
    {
        if (promptGo == null) return;
        if (riding) { promptGo.SetActive(false); return; }
        PlayerHealth ph = PlayerHealth.Instance;
        bool near = false;
        if (ph != null && !ph.IsDead && ph.OnFoot && vehicles != null && !vehicles.IsDriving && !VehicleSystem.Locked &&
            !(LundunBikes.Instance != null && LundunBikes.Instance.Riding))
        {
            Vector3 p = transform.InverseTransformPoint(ph.Body.position);
            Vector3 d = p - boardSpot; float dy = d.y; d.y = 0f;
            near = d.magnitude < boardDistance && dy > -0.6f;
        }
        promptGo.SetActive(near);
        if (near)
        {
            bool wanted = WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
            promptText.text = wanted ? "WHEEL CLOSED\n<size=70%>Police incident</size>" : "RIDE THE WHEEL\n<size=70%>Once round, " + Mathf.RoundToInt(secondsPerTurn / 60f) + " minutes</size>";
            promptGo.GetComponent<Button>().interactable = !wanted;
        }
    }

    void Board()
    {
        if (riding || PlayerHealth.Instance == null || vehicles == null || capsules.Length == 0) return;
        // the capsule nearest the bottom
        int best = 0; float bestA = 999f;
        for (int k = 0; k < capsules.Length; k++)
        {
            float a = Mathf.Abs(Mathf.DeltaAngle(CapsuleAngle(k), 0f));
            if (a < bestA) { bestA = a; best = k; }
        }
        riding = true; rideCapsule = best; rideTurned = 0f;
        SetFar(far);
        playerGo = PlayerHealth.Instance.Body.gameObject;
        vehicles.LockControls(true);
        playerGo.SetActive(false);
        if (cam != null) cam.SetTarget(rideCam, true);
        promptGo.SetActive(false);
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast("The Lundun Wheel\nOnce round, " + Mathf.RoundToInt(secondsPerTurn / 60f) + " minutes");
    }

    void GetOff()
    {
        if (!riding) return;
        riding = false;
        if (playerGo != null)
        {
            Vector3 p = transform.TransformPoint(exitSpot) + Vector3.up * 0.1f;
            playerGo.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-transform.forward, Vector3.up));
            playerGo.SetActive(true);
            if (cam != null) cam.SetTarget(playerGo.transform, false);
        }
        if (vehicles != null) vehicles.LockControls(false);
        SetFar(far);
        nextCheck = Time.time + 2f;
    }

    void BuildUI()
    {
        GameObject cg = new GameObject("WheelUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 61;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        promptGo = new GameObject("WheelPrompt", typeof(RectTransform));
        promptGo.transform.SetParent(cg.transform, false);
        Image img = promptGo.AddComponent<Image>(); img.color = new Color(0.12f, 0.37f, 0.42f, 0.92f);
        Button b = promptGo.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(Board);
        RectTransform rt = (RectTransform)promptGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 230f); rt.sizeDelta = new Vector2(460f, 120f);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(promptGo.transform, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        promptText = tg.AddComponent<TextMeshProUGUI>();
        promptText.fontSize = 38; promptText.alignment = TextAlignmentOptions.Center; promptText.color = Color.white;
        promptText.fontStyle = FontStyles.Bold; promptText.raycastTarget = false; promptText.richText = true;
        promptGo.SetActive(false);
    }
}
