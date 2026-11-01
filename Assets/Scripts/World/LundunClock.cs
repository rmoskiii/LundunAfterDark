using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Game clock: night → dawn → day → dusk → night, blending the four lighting presets.
/// Night runs at 30 game minutes per real minute, daytime at 60: from 23:00, the sky starts to glow
/// ~12 minutes in and the sun is up by ~15. A full day takes ~34 real minutes, mostly night.
/// The time is saved, so the next session carries on where you left off (a first-ever session starts at 23:00).
/// BUSTED / WASTED push the clock forward an hour (hidden behind the black screen).
/// Built by Lundun → 7  Build Day-Night Clock.
public class LundunClock : MonoBehaviour
{
    public static LundunClock Instance { get; private set; }

    [SerializeField] private LundunLighting lighting;
    [SerializeField] private LundunLightingPreset night, dawn, day, dusk;

    [Header("Time")]
    [SerializeField] private float startHour = 23f;                 // first ever session
    [SerializeField] private float nightMinutesPerRealMinute = 30f;  // 19:00 – 07:00
    [SerializeField] private float dayMinutesPerRealMinute = 60f;    // 07:00 – 19:00
    [SerializeField] private bool rememberTime = true;              // carry the time over to the next session
    [SerializeField] private float respawnSkipHours = 1f;

    [Header("HUD")]
    [SerializeField] private TextMeshProUGUI clockText;
    [SerializeField] private bool showDevButtons = true;     // +1h and fast-forward, for testing. Switch off for release.
    [SerializeField] private Sprite buttonSprite;

    /// 0..24
    public float Hour { get; private set; }
    public float Speed { get; private set; }

    // When each preset is fully showing. Between two keys the looks blend.
    static readonly float[] KeyHours = { 0f, 4.5f, 5.5f, 6.5f, 8f, 17.5f, 19f, 19.75f, 21f, 24f };
    private LundunLightingPreset[] keyLooks;
    private float nextApply;
    private TextMeshProUGUI speedLabel;
    private static readonly float[] Speeds = { 1f, 12f, 60f };
    private int speedIndex;
    private float nextSave;
    const string SaveKey = "Lundun.ClockHour";

    void Awake()
    {
        Instance = this;
        Hour = Mathf.Repeat(rememberTime ? PlayerPrefs.GetFloat(SaveKey, startHour) : startHour, 24f);
        Speed = 1f;
        keyLooks = new[] { night, night, dawn, dawn, day, day, dusk, dusk, night, night };
        if (lighting != null) lighting.DrivenByClock = true;
    }

    void OnEnable() { VehicleSystem.Respawned += OnRespawn; }
    void OnDisable() { VehicleSystem.Respawned -= OnRespawn; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (showDevButtons && clockText != null) MakeDevButtons();
        ApplyNow();
    }

    void OnRespawn() { AddHours(respawnSkipHours); }

    void Save() { if (rememberTime) { PlayerPrefs.SetFloat(SaveKey, Hour); PlayerPrefs.Save(); } }
    void OnApplicationPause(bool paused) { if (paused) Save(); }
    void OnApplicationQuit() { Save(); }

    public void AddHours(float h)
    {
        Hour = Mathf.Repeat(Hour + h, 24f);
        ApplyNow();
    }

    public void SetHour(float h)
    {
        Hour = Mathf.Repeat(h, 24f);
        ApplyNow();
    }

    void Update()
    {
        // deltaTime stops while the full map is open (time scale 0), so the clock pauses too
        float rate = (Hour >= 7f && Hour < 19f) ? dayMinutesPerRealMinute : nightMinutesPerRealMinute;
        Hour = Mathf.Repeat(Hour + Time.deltaTime * rate * Speed / 3600f, 24f);
        if (Time.unscaledTime >= nextSave) { nextSave = Time.unscaledTime + 10f; Save(); }
        if (Time.unscaledTime >= nextApply || Speed > 1f) ApplyNow();
    }

    void ApplyNow()
    {
        nextApply = Time.unscaledTime + 0.1f;
        if (clockText != null)
        {
            int mins = Mathf.FloorToInt(Hour * 60f) % 1440;
            clockText.text = (mins / 60).ToString("00") + ":" + (mins % 60).ToString("00");
        }
        if (lighting != null && night != null && dawn != null && day != null && dusk != null)
            lighting.Apply(Evaluate(Hour));
    }

    public LightingLook Evaluate(float h)
    {
        int i = 0;
        while (i < KeyHours.Length - 2 && h >= KeyHours[i + 1]) i++;
        float t = Mathf.InverseLerp(KeyHours[i], KeyHours[i + 1], h);
        t = t * t * (3f - 2f * t);                                   // ease in and out
        LightingLook l = LightingLook.Lerp(keyLooks[i].look, keyLooks[i + 1].look, t);

        // the sun moves across the sky in the daytime: rises in the east, sets in the west
        float sunWeight = Smooth(5.5f, 6.5f, h) * (1f - Smooth(18.5f, 19.5f, h));
        if (sunWeight > 0f)
        {
            float p = Mathf.Clamp01((h - 6f) / 12.5f);
            Vector2 sunPath = new Vector2(5f + 55f * Mathf.Sin(p * Mathf.PI), 90f + 180f * p);
            l.sunAngles = new Vector2(Mathf.Lerp(l.sunAngles.x, sunPath.x, sunWeight), Mathf.LerpAngle(l.sunAngles.y, sunPath.y, sunWeight));
        }

        // flats go dark through the early hours
        if (h < 12f) l.windowGlow *= 1f - 0.6f * Smooth(0.5f, 4.5f, h);
        return l;
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // ------------------------------------------------------------------ dev buttons
    void MakeDevButtons()
    {
        RectTransform parent = (RectTransform)clockText.transform.parent;
        RectTransform ct = clockText.rectTransform;
        Button(parent, "+1h", ct.anchoredPosition + new Vector2(110f, 0f), () => AddHours(1f));
        speedLabel = Button(parent, "x1", ct.anchoredPosition + new Vector2(185f, 0f), CycleSpeed);
    }

    void CycleSpeed()
    {
        speedIndex = (speedIndex + 1) % Speeds.Length;
        Speed = Speeds[speedIndex];
        if (speedLabel != null) speedLabel.text = "x" + Speed;
    }

    TextMeshProUGUI Button(RectTransform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject g = new GameObject("Dev_" + label, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(66, 36);
        Image bg = g.AddComponent<Image>();
        bg.sprite = buttonSprite; bg.type = Image.Type.Simple; bg.color = new Color(1f, 1f, 1f, 0.15f);
        Button b = g.AddComponent<Button>(); b.targetGraphic = bg; b.onClick.AddListener(onClick);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(rt, false);
        RectTransform trt = (RectTransform)tg.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        TextMeshProUGUI t = tg.AddComponent<TextMeshProUGUI>();
        t.text = label; t.fontSize = 22; t.alignment = TextAlignmentOptions.Center; t.color = Color.white; t.raycastTarget = false;
        return t;
    }
}
