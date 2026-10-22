using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// L6b: the Rye Lane shuttle. A three-car train runs the railway: it waits at Rye Lane station with its doors open,
/// sounds its horn, runs south over Old Kent Lane and the Ring and out past the edge of the map, waits, and comes back.
/// Walk up the stairs behind the station passage to the platform; while the doors are open "RIDE THE TRAIN" appears,
/// and James rides the round trip (the camera follows the train). Don't stand on the tracks.
/// Built by Lundun → 25 (the path, the cars and their colliders). Builds its own little UI.
public class LundunTrain : MonoBehaviour
{
    [Header("Filled in by Lundun → 25")]
    [SerializeField] private Vector3[] path = new Vector3[0];          // the platform road at rail-head height, south end → buffers
    [SerializeField] private Transform[] cars = new Transform[0];      // car 0 at the buffer end
    [SerializeField] private float stationFront;                        // where the buffer end of car 0 stops at Rye Lane (m along the path)
    [SerializeField] private float depotFront;                          // and where it stops at the far end
    [SerializeField] private Vector3 platformSpot;                      // where James gets off
    [SerializeField] private float carLength = 19.4f, carGap = 0.6f;
    [SerializeField] private Vector3 entrance;                          // the station's street entrance (for the map and GPS)

    [Header("Running")]
    [SerializeField] private float maxSpeed = 16f;                      // m/s (about 36 mph)
    [SerializeField] private float acceleration = 0.7f;
    [SerializeField] private float braking = 0.9f;
    [SerializeField] private float curveGrip = 1.1f;                    // sideways m/s² allowed on curves
    [SerializeField] private float dwellAtStation = 35f;
    [SerializeField] private float dwellAtEnd = 15f;
    [SerializeField] private float boardDistance = 3.6f;

    enum State { AtStation, Outbound, AtEnd, Inbound }
    private State state = State.AtStation;
    private float[] cum, limit;
    private float s, v, waitUntil;
    private Rigidbody[] bodies;
    private AudioSource rumbleSource, fxSource;
    private AudioClip horn, chime;
    private bool chimed;

    // riding
    private bool riding, leftStation;
    private VehicleSystem vehicles;
    private CameraFollow cam;
    private GameObject playerGo;
    private Transform rideCam;                  // what the camera follows while riding: the middle car, facing the way it's going
    private GameObject promptGo;
    private TextMeshProUGUI promptText;
    private float nextCheck;

    public bool Riding { get { return riding; } }
    public float Speed { get { return v; } }

    public void SetData(Vector3[] p, Transform[] c, float station, float depot, Vector3 spot, Vector3 way)
    {
        path = p; cars = c; stationFront = station; depotFront = depot; platformSpot = spot; entrance = way;
    }

    public const string StationName = "Rye Lane Station";
    /// The station's street entrance, and a marker there for the minimap.
    public Vector3 Entrance { get { return entrance; } }
    public Transform StationMarker { get; private set; }

    void Awake()
    {
        GameObject mk = new GameObject("RyeLaneStation");
        mk.transform.SetParent(transform, false);
        mk.transform.position = entrance;
        StationMarker = mk.transform;
        int n = path.Length;
        cum = new float[n];
        for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        // speed limit from how sharply the line turns over ±10 m
        limit = new float[n];
        for (int i = 0; i < n; i++)
        {
            int a = Mathf.Max(0, i - 5), b = Mathf.Min(n - 1, i + 5);
            float lim = maxSpeed;
            if (b - a >= 2)
            {
                Vector3 d0 = path[i] - path[a], d1 = path[b] - path[i]; d0.y = d1.y = 0f;
                float ang = Vector3.Angle(d0, d1) * Mathf.Deg2Rad, len = cum[b] - cum[a];
                if (ang > 0.001f) lim = Mathf.Clamp(Mathf.Sqrt(curveGrip * len / ang), 6f, maxSpeed);
            }
            limit[i] = lim;
        }
        s = stationFront;
        waitUntil = Time.time + 8f;
        bodies = new Rigidbody[cars.Length];
        for (int k = 0; k < cars.Length; k++)
        {
            if (cars[k] == null) continue;
            Rigidbody rb = cars[k].GetComponent<Rigidbody>();
            if (rb == null) rb = cars[k].gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            bodies[k] = rb;
        }
        PlaceCars(true);
    }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        cam = FindAnyObjectByType<CameraFollow>();
        if (cars.Length > 1 && cars[1] != null)
        {
            rumbleSource = cars[1].gameObject.AddComponent<AudioSource>();
            rumbleSource.clip = MakeRumble(4f); rumbleSource.loop = true; rumbleSource.spatialBlend = 1f;
            rumbleSource.minDistance = 10f; rumbleSource.maxDistance = 140f; rumbleSource.rolloffMode = AudioRolloffMode.Linear;
            rumbleSource.volume = 0f; rumbleSource.Play();
            fxSource = cars[1].gameObject.AddComponent<AudioSource>();
            fxSource.spatialBlend = 0.8f; fxSource.minDistance = 15f; fxSource.maxDistance = 220f; fxSource.rolloffMode = AudioRolloffMode.Linear;
        }
        horn = MakeHorn(); chime = MakeChime();
        rideCam = new GameObject("TrainRideCamera").transform; rideCam.SetParent(transform, false);
        BuildUI();
    }

    // ------------------------------------------------------------------ running
    void FixedUpdate()
    {
        if (path.Length < 2) return;
        float dt = Time.fixedDeltaTime;
        switch (state)
        {
            case State.AtStation:
                v = 0f;
                if (!chimed && Time.time > waitUntil - 4f) { chimed = true; Play(chime, 0.7f); }
                if (Time.time >= waitUntil) { state = State.Outbound; leftStation = true; Play(horn, 1f); }
                break;
            case State.AtEnd:
                v = 0f;
                if (Time.time >= waitUntil) state = State.Inbound;
                break;
            case State.Outbound:
                if (Drive(depotFront, -1, dt)) { state = State.AtEnd; waitUntil = Time.time + dwellAtEnd; }
                break;
            case State.Inbound:
                if (Drive(stationFront, 1, dt))
                {
                    state = State.AtStation; waitUntil = Time.time + dwellAtStation; chimed = false;
                    if (riding && leftStation) Invoke(nameof(GetOff), 1.5f);
                }
                break;
        }
        PlaceCars(false);
        if (rumbleSource != null)
        {
            float k = Mathf.Clamp01(v / maxSpeed);
            rumbleSource.volume = Mathf.Lerp(rumbleSource.volume, k * 0.9f + (v > 0.1f ? 0.08f : 0f), 4f * dt);
            rumbleSource.pitch = 0.75f + 0.45f * k;
        }
        if (v > 1.5f && !riding) CheckTrack();
    }

    /// Moves towards target (direction dir = ±1); true when stopped there.
    bool Drive(float target, int dir, float dt)
    {
        float d = (target - s) * dir;
        if (d <= 0.05f && v < 0.3f) { s = target; v = 0f; return true; }
        float want = Mathf.Min(maxSpeed, Mathf.Sqrt(2f * braking * Mathf.Max(0f, d - 0.2f)));
        // slow for curves ahead of the leading end (and under the whole train)
        float lead = dir > 0 ? s : s - TrainLength;
        for (float x = 0f; x <= 160f; x += 10f)
        {
            float lim = LimitAt(lead + dir * x);
            want = Mathf.Min(want, Mathf.Sqrt(lim * lim + 2f * braking * x));
        }
        want = Mathf.Min(want, Mathf.Min(LimitAt(s), LimitAt(s - TrainLength)));
        v = want > v ? Mathf.Min(want, v + acceleration * dt) : Mathf.Max(want, v - braking * 1.6f * dt);
        if (d < 1f) v = Mathf.Max(v, 0.25f);
        s += dir * Mathf.Min(v * dt, d);
        return false;
    }

    float TrainLength { get { return cars.Length * carLength + (cars.Length - 1) * carGap; } }

    float LimitAt(float at)
    {
        int i = Index(at);
        return limit.Length > 0 ? limit[i] : maxSpeed;
    }

    int Index(float at)
    {
        int lo = 0, hi = cum.Length - 1;
        if (at <= 0f) return 0;
        if (at >= cum[hi]) return hi;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (cum[m] <= at) lo = m; else hi = m; }
        return lo;
    }

    Vector3 PointAt(float at)
    {
        int i = Index(at);
        if (i >= cum.Length - 1) { Vector3 d = path[path.Length - 1] - path[path.Length - 2]; return path[path.Length - 1] + d.normalized * (at - cum[cum.Length - 1]); }
        if (at < 0f) return path[0] + (path[0] - path[1]).normalized * -at;
        float t = (at - cum[i]) / Mathf.Max(0.001f, cum[i + 1] - cum[i]);
        return Vector3.Lerp(path[i], path[i + 1], t);
    }

    void PlaceCars(bool snap)
    {
        for (int k = 0; k < cars.Length; k++)
        {
            if (cars[k] == null) continue;
            float front = s - k * (carLength + carGap), rear = front - carLength;
            Vector3 pf = PointAt(front - 3f), pr = PointAt(rear + 3f);
            Vector3 pos = (pf + pr) * 0.5f;
            Quaternion rot = Quaternion.LookRotation(pf - pr, Vector3.up);
            if (snap || bodies == null || bodies[k] == null) cars[k].SetPositionAndRotation(pos, rot);
            else { bodies[k].MovePosition(pos); bodies[k].MoveRotation(rot); }
        }
        if (cars.Length > 1 && cars[1] != null && rideCam != null)
        {
            Vector3 fwd = PointAt(s - carLength - carGap - 3f) - PointAt(s - 2f * carLength - carGap + 3f);
            if (state == State.Outbound || state == State.AtEnd) fwd = -fwd;
            rideCam.SetPositionAndRotation(PointAt(s - 1.5f * carLength - carGap) + Vector3.up * 1.6f, Quaternion.LookRotation(fwd, Vector3.up));
        }
        if (riding && playerGo != null) playerGo.transform.position = rideCam.position;
    }

    /// Standing in front of a moving train is a bad idea.
    void CheckTrack()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot) return;
        int dir = state == State.Inbound ? 1 : -1;
        float lead = dir > 0 ? s : s - TrainLength;
        Vector3 p = ph.Body.position;
        for (float x = -1f; x <= 2.5f; x += 0.5f)
        {
            Vector3 q = PointAt(lead + dir * x);
            Vector3 d = p - q;
            if (Mathf.Abs(d.y) < 2.4f && new Vector2(d.x, d.z).magnitude < 1.7f) { ph.Kill(); return; }
        }
    }

    // ------------------------------------------------------------------ riding
    void Update()
    {
        if (Time.time < nextCheck || promptGo == null) return;
        nextCheck = Time.time + 0.2f;
        bool show = false; string text = "";
        if (riding)
        {
            show = state == State.AtStation && !leftStation;
            text = "GET OFF";
        }
        else if (state == State.AtStation && Time.time < waitUntil - 4f && NearTrain())
        {
            bool wanted = WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
            show = true;
            text = wanted ? "DOORS CLOSED\n<size=70%>Police incident</size>" : "RIDE THE TRAIN\n<size=70%>Rye Lane line, there and back</size>";
            promptGo.GetComponent<Button>().interactable = !wanted;
        }
        if (show) { promptText.text = text; if (riding) promptGo.GetComponent<Button>().interactable = true; }
        promptGo.SetActive(show);
    }

    bool NearTrain()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot || vehicles == null || vehicles.IsDriving || VehicleSystem.Locked) return false;
        if (LundunBikes.Instance != null && LundunBikes.Instance.Riding) return false;
        Vector3 p = ph.Body.position;
        if (p.y < path[path.Length - 1].y + 0.3f) return false;           // up on the platform
        for (float x = 0f; x <= TrainLength; x += 2f)
        {
            Vector3 d = p - PointAt(s - x); d.y = 0f;
            if (d.magnitude < boardDistance) return true;
        }
        return false;
    }

    void OnPrompt()
    {
        if (riding) GetOff();
        else Board();
    }

    void Board()
    {
        if (riding || PlayerHealth.Instance == null || vehicles == null || cars.Length < 2) return;
        riding = true; leftStation = false;
        playerGo = PlayerHealth.Instance.Body.gameObject;
        vehicles.LockControls(true);
        playerGo.SetActive(false);
        if (cam != null) cam.SetTarget(rideCam, true);
        promptGo.SetActive(false);
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast("Rye Lane line\nRound trip, about 3 minutes");
    }

    void GetOff()
    {
        if (!riding) return;
        riding = false;
        CancelInvoke(nameof(GetOff));
        if (playerGo != null)
        {
            Vector3 along = (PointAt(stationFront) - PointAt(stationFront - 10f)).normalized;
            playerGo.transform.SetPositionAndRotation(platformSpot + Vector3.up * 0.1f, Quaternion.LookRotation(-along, Vector3.up));
            playerGo.SetActive(true);
            if (cam != null) cam.SetTarget(playerGo.transform, false);
        }
        if (vehicles != null) vehicles.LockControls(false);
        nextCheck = Time.time + 2f;
    }

    // ------------------------------------------------------------------ UI (built in code)
    void BuildUI()
    {
        GameObject cg = new GameObject("TrainUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 61;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        promptGo = new GameObject("TrainPrompt", typeof(RectTransform));
        promptGo.transform.SetParent(cg.transform, false);
        Image img = promptGo.AddComponent<Image>(); img.color = new Color(0.12f, 0.16f, 0.29f, 0.92f);
        Button b = promptGo.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(OnPrompt);
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

    // ------------------------------------------------------------------ sounds (made in code)
    void Play(AudioClip c, float vol) { if (fxSource != null && c != null) fxSource.PlayOneShot(c, vol); }

    static AudioClip MakeRumble(float seconds)
    {
        int rate = 22050, n = (int)(seconds * rate);
        float[] d = new float[n];
        System.Random r = new System.Random(11);
        float brown = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            brown = Mathf.Clamp(brown + ((float)r.NextDouble() * 2f - 1f) * 0.04f, -1f, 1f) * 0.995f;
            float hum = Mathf.Sin(t * 2f * Mathf.PI * 48f) * 0.5f + Mathf.Sin(t * 2f * Mathf.PI * 96f) * 0.2f;
            float beat = t % 1.0f, clack = 0f;                     // bogies over the rail joints, loops cleanly every second
            if (beat < 0.03f) clack = Mathf.Exp(-beat * 150f);
            else if (beat > 0.16f && beat < 0.19f) clack = Mathf.Exp(-(beat - 0.16f) * 150f) * 0.8f;
            clack *= ((float)r.NextDouble() * 2f - 1f);
            d[i] = brown * 0.5f + hum * 0.12f + clack * 0.45f;
        }
        AudioClip clip = AudioClip.Create("TrainRumble", n, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }

    static AudioClip MakeHorn()
    {
        int rate = 22050, n = (int)(1.3f * rate);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate, f = t < 0.55f ? 370f : 311f;      // two tones, high then low
            float env = Mathf.Clamp01(t / 0.04f) * Mathf.Clamp01((1.3f - t) / 0.1f) * (t > 0.55f && t < 0.6f ? 0.3f : 1f);
            float w = 0f;
            for (int h = 1; h <= 5; h++) w += Mathf.Sin(t * 2f * Mathf.PI * f * h) / h;
            d[i] = env * Mathf.Clamp(w * 0.6f, -0.8f, 0.8f) * 0.5f;
        }
        AudioClip clip = AudioClip.Create("TrainHorn", n, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }

    static AudioClip MakeChime()
    {
        int rate = 22050, n = (int)(1.5f * rate);
        float[] d = new float[n];
        float[] notes = { 784f, 659f, 523f };
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate; int k = Mathf.Min(2, (int)(t / 0.45f)); float lt = t - k * 0.45f;
            d[i] = Mathf.Sin(t * 2f * Mathf.PI * notes[k]) * Mathf.Exp(-lt * 5f) * 0.35f;
        }
        AudioClip clip = AudioClip.Create("TrainDoors", n, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
