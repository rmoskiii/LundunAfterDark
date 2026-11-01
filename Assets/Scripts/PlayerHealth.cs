using System;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }
    public event Action Died;

    [SerializeField] private PlayerController playerController;   // drag James in

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float regenDelay = 5f;        // seconds without a hit before healing starts
    [SerializeField] private float regenPerSecond = 20f;

    [Header("UI")]
    [SerializeField] private Image healthBar;              // Image Type: Filled
    [SerializeField] private Image damageFlash;            // the red vignette

    [Header("Health bar feel")]
    [Tooltip("How much thicker the bar swells when you're hit")]
    [SerializeField] private float hitSwell = 2.2f;
    [SerializeField] private float swellTime = 0.45f;
    [Tooltip("The chunk you just lost stays visible this long, then drains away")]
    [SerializeField] private float chunkHold = 0.6f;
    [SerializeField] private float chunkDrainPerSecond = 0.5f;
    [SerializeField] private Color healthyColour = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color hurtColour = new Color(1f, 0.72f, 0.1f);
    [SerializeField] private Color dangerColour = new Color(0.9f, 0.12f, 0.1f);
    [Tooltip("While driving, the bar shows the car's health in this colour")]
    [SerializeField] private Color carColour = new Color(1f, 0.55f, 0.1f);

    [Header("Screen pulse")]
    [Tooltip("Below this much health the screen starts pulsing red like a heartbeat")]
    [Range(0f, 1f)] [SerializeField] private float heartbeatBelow = 0.5f;
    [SerializeField] private float heartbeatStrength = 0.45f;
    [Tooltip("Camera jolt per point of damage")]
    [SerializeField] private float shakePerDamage = 0.008f;

    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public Transform Body => playerController.transform;
    public bool OnFoot => playerController.gameObject.activeInHierarchy;

    private float lastHitTime = -999f;
    private float flash;

    // bar effects
    private RectTransform barRt, chunkRt, trackRt;
    private Image chunk;
    private float baseHeight, shown = 1f, chunkFill = 1f, chunkUntil, swell, whiten;
    private float beatPhase;
    private bool showingCar;
    private VehicleHealth watchedCar;
    private float watchedCarHp;
    private VehicleSystem vehicles;
    private CameraFollow cam;

    void Awake()
    {
        Instance = this;
        Current = maxHealth;
    }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        cam = FindAnyObjectByType<CameraFollow>();
        BuildBarExtras();
    }

    void Update()
    {
        // CoD-style: stay out of trouble for a few seconds and you heal
        if (!IsDead && Current < maxHealth && Time.time > lastHitTime + regenDelay)
            Current = Mathf.Min(maxHealth, Current + regenPerSecond * Time.deltaTime);

        float dt = Time.unscaledDeltaTime;
        float frac = BarFraction(out bool car);

        // the bar: drops at once, the lost chunk lingers then drains; heals smoothly
        if (car != showingCar) { shown = chunkFill = frac; showingCar = car; }
        if (frac < shown) shown = frac; else shown = Mathf.MoveTowards(shown, frac, 0.6f * dt);
        if (chunkFill < shown) chunkFill = shown;
        else if (Time.unscaledTime > chunkUntil) chunkFill = Mathf.MoveTowards(chunkFill, shown, chunkDrainPerSecond * dt);
        swell = Mathf.MoveTowards(swell, 0f, dt / Mathf.Max(0.05f, swellTime));
        whiten = Mathf.MoveTowards(whiten, 0f, dt * 6f);

        if (healthBar != null)
        {
            healthBar.fillAmount = shown;
            Color c = car ? carColour : HealthColour(shown);
            healthBar.color = Color.Lerp(c, Color.white, whiten);
            float h = baseHeight * (1f + (hitSwell - 1f) * EaseOut(swell));
            SetHeight(barRt, h); SetHeight(chunkRt, h); SetHeight(trackRt, h);
        }
        if (chunk != null) chunk.fillAmount = chunkFill;

        // the screen: a red flash on each hit, and a heartbeat once you're badly hurt
        flash = Mathf.MoveTowards(flash, 0f, dt * 1.5f);
        float pulse = 0f, beat = 0f;
        float danger = car || IsDead ? 0f : Mathf.InverseLerp(heartbeatBelow, 0.05f, Current / maxHealth);
        if (danger > 0f)
        {
            beatPhase += dt * Mathf.Lerp(1.1f, 2.4f, danger);             // beats per second
            float p = Mathf.Repeat(beatPhase, 1f);
            beat = Mathf.Exp(-p * 14f) + (p > 0.22f ? 0.6f * Mathf.Exp(-(p - 0.22f) * 14f) : 0f);   // lub-dub
            pulse = 0.06f + heartbeatStrength * Mathf.Lerp(0.35f, 1f, danger) * beat;
        }
        if (damageFlash != null)
        {
            Color c = damageFlash.color;
            c.a = Mathf.Clamp01(Mathf.Max(flash, pulse));
            damageFlash.color = c;
            damageFlash.rectTransform.localScale = Vector3.one * (1f + 0.04f * beat * danger + 0.03f * flash);
        }
    }

    public void TakeDamage(float amount)
    {
        // Locked controls (BUSTED / WASTED cutscenes, the Tube) = can't be hurt. (Riding a bike you can.)
        if (IsDead || amount <= 0f || VehicleSystem.Locked) return;

        Current -= amount;
        lastHitTime = Time.time;
        flash = Mathf.Max(flash, Mathf.Clamp(0.25f + amount * 0.02f, 0.3f, 0.7f));
        Hit(amount, maxHealth);

        if (Current <= 0f) Kill();
    }

    public void Kill()
    {
        if (IsDead) return;
        Current = 0f;
        IsDead = true;
        Died?.Invoke();
    }

    public void ResetHealth()
    {
        Current = maxHealth;
        IsDead = false;
        flash = 0f;
        shown = chunkFill = 1f;
        swell = whiten = 0f;
    }

    // ------------------------------------------------------------------ bar helpers

    /// On foot: James's health. Driving: the car's (that's what the police are shooting).
    float BarFraction(out bool car)
    {
        car = false;
        VehicleHealth vh = null;
        if (vehicles != null && vehicles.IsDriving && vehicles.CurrentCar != null) vh = vehicles.CurrentCar.GetComponent<VehicleHealth>();
        if (vh != watchedCar) { watchedCar = vh; watchedCarHp = vh != null ? vh.Current : 0f; }
        if (vh != null && vh.Max > 0f)
        {
            car = true;
            if (vh.Current < watchedCarHp - 0.01f) Hit(watchedCarHp - vh.Current, vh.Max);
            watchedCarHp = vh.Current;
            return Mathf.Clamp01(vh.Current / vh.Max);
        }
        return Mathf.Clamp01(Current / maxHealth);
    }

    /// The bar swells and flashes, the lost chunk is held, and the camera jolts: bigger hits, bigger reaction.
    void Hit(float amount, float max)
    {
        float size = Mathf.Clamp01(amount / (max * 0.25f));
        swell = Mathf.Max(swell, Mathf.Lerp(0.5f, 1f, size));
        whiten = 1f;
        chunkUntil = Time.unscaledTime + chunkHold;
        if (size > 0.5f) LundunHaptics.Heavy(); else LundunHaptics.Medium();
        if (cam != null) cam.AddShake(Mathf.Min(0.2f, amount * shakePerDamage));
    }

    Color HealthColour(float f)
    {
        return f > 0.5f ? Color.Lerp(hurtColour, healthyColour, (f - 0.5f) * 2f) : Color.Lerp(dangerColour, hurtColour, f * 2f);
    }

    static float EaseOut(float t) { return 1f - (1f - t) * (1f - t); }

    static void SetHeight(RectTransform rt, float h)
    {
        if (rt == null) return;
        Vector2 s = rt.sizeDelta;
        if (Mathf.Abs(s.y - h) > 0.01f) rt.sizeDelta = new Vector2(s.x, h);
    }

    /// Adds, behind the bar, a dark track and the pale "chunk you just lost". Centred, so the bar swells both ways.
    void BuildBarExtras()
    {
        if (healthBar == null) return;
        barRt = healthBar.rectTransform;
        Vector2 pv = barRt.pivot;
        if (!Mathf.Approximately(pv.y, 0.5f))
        {
            Vector2 ap = barRt.anchoredPosition;
            ap.y += (0.5f - pv.y) * barRt.rect.height;
            barRt.pivot = new Vector2(pv.x, 0.5f);
            barRt.anchoredPosition = ap;
        }
        baseHeight = barRt.sizeDelta.y > 0.5f ? barRt.sizeDelta.y : 14f;

        Image track = MakeCopy("HealthBarTrack", new Color(0f, 0f, 0f, 0.5f));
        track.type = Image.Type.Simple;
        trackRt = track.rectTransform;
        chunk = MakeCopy("HealthBarChunk", new Color(1f, 0.92f, 0.85f, 0.9f));
        chunkRt = chunk.rectTransform;
        int at = barRt.GetSiblingIndex();
        trackRt.SetSiblingIndex(at);
        chunkRt.SetSiblingIndex(at + 1);
        barRt.SetSiblingIndex(at + 2);
    }

    Image MakeCopy(string name, Color colour)
    {
        GameObject g = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rt = (RectTransform)g.transform;
        rt.SetParent(barRt.parent, false);
        rt.anchorMin = barRt.anchorMin; rt.anchorMax = barRt.anchorMax; rt.pivot = barRt.pivot;
        rt.anchoredPosition = barRt.anchoredPosition; rt.sizeDelta = barRt.sizeDelta;
        rt.localScale = barRt.localScale; rt.localRotation = barRt.localRotation;
        Image img = g.GetComponent<Image>();
        img.sprite = healthBar.sprite;
        img.type = healthBar.type; img.fillMethod = healthBar.fillMethod; img.fillOrigin = healthBar.fillOrigin;
        img.fillAmount = 1f;
        img.color = colour;
        img.raycastTarget = false;
        return img;
    }
}
