using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// V3b: London weather. Drifts between Clear, Cloudy, Drizzle, Rain and Heavy showers on the game clock —
/// rain is frequent but not constant, showers are short, and a dry spell often follows.
///   • rain falls around the camera (one particle system that follows it)
///   • roads and pavements get wet when it rains and dry slowly afterwards (shinier and darker)
///   • orange lamp reflections streak across the wet tarmac towards you
///   • overcast = flatter, greyer light, weaker sun and shadows, closer fog
///   • a soft rain sound
/// A dev button by the clock cycles the weather for testing. Added by Lundun → 19.
[DefaultExecutionOrder(150)]
public class LundunWeather : MonoBehaviour
{
    public static LundunWeather Instance { get; private set; }

    public enum Kind { Clear, Cloudy, Drizzle, Rain, Heavy }

    [Serializable]
    public class WetSurface
    {
        public Material material;
        [Range(0, 1)] public float wetSmoothness = 0.8f;
        [Range(0, 1)] [Tooltip("How much darker it gets when soaked")] public float darken = 0.35f;
        [NonSerialized] public float drySmoothness; [NonSerialized] public Color dryColour; [NonSerialized] public bool saved;
    }

    [Header("Pattern (durations in game hours)")]
    [SerializeField] private Vector2 clearHours = new Vector2(2f, 6f);
    [SerializeField] private Vector2 cloudyHours = new Vector2(1.5f, 4f);
    [SerializeField] private Vector2 drizzleHours = new Vector2(0.6f, 2f);
    [SerializeField] private Vector2 rainHours = new Vector2(0.5f, 1.5f);
    [SerializeField] private Vector2 heavyHours = new Vector2(0.3f, 0.8f);
    [Tooltip("Game hours to blend from one weather to the next")]
    [SerializeField] private float blendHours = 0.3f;

    [Header("Wet roads")]
    [SerializeField] private WetSurface[] surfaces = new WetSurface[0];
    [Tooltip("Game hours of steady rain to soak the roads")]
    [SerializeField] private float soakHours = 0.4f;
    [Tooltip("Game hours to dry out after rain (in daylight; nights dry slower)")]
    [SerializeField] private float dryHours = 2.5f;

    [Header("Rain")]
    [SerializeField] private Material rainMaterial;
    [SerializeField] private int maxDrops = 2500;
    [SerializeField] private float heavyDropsPerSecond = 2200f;
    [SerializeField] private float fallSpeed = 17f;
    [SerializeField] private Vector3 rainBox = new Vector3(34f, 34f, 1f);     // emitter faces down: x / y across, z thin
    [Range(0, 1)] [SerializeField] private float rainVolume = 0.5f;

    [Header("Lamp reflections on wet roads")]
    [SerializeField] private int streakCount = 16;
    [SerializeField] private float streakRange = 75f;
    [Range(0, 1)] [SerializeField] private float streakStrength = 0.6f;
    [SerializeField] private Color streakColour = new Color(1f, 0.6f, 0.28f);

    [Header("Testing")]
    [SerializeField] private bool devButton = true;

    public Kind State { get; private set; }
    /// 0..1, how overcast right now (blended).
    public float Cloud { get; private set; }
    /// 0..1, how hard it's raining right now (blended).
    public float Rain { get; private set; }
    /// 0..1, how wet the roads are.
    public float Wetness { get; private set; }

    private float cloudTarget, rainTarget, hoursLeft, lastHour = -1f;
    private ParticleSystem rain;
    private ParticleSystem.EmissionModule rainEmission;
    private AudioSource rainAudio;
    private SpriteRenderer[] streaks;
    private Vector3[] streakBase;
    private int streakUsed;
    private float nextStreakPick;
    private LundunLighting lighting;
    private LundunLampLights lamps;
    private Camera cam;
    private TextMeshProUGUI devLabel;

    // ------------------------------------------------------------------ set-up
    void Awake() { Instance = this; }

    void OnEnable()
    {
        LundunLighting.Modify += ModifyLook;
        LundunAtmosphere.Modify += ModifyGrade;
    }

    void OnDisable()
    {
        LundunLighting.Modify -= ModifyLook;
        LundunAtmosphere.Modify -= ModifyGrade;
        RestoreSurfaces();                         // in the editor, never leave the material assets wet
    }

    void OnApplicationQuit() { RestoreSurfaces(); }
    void OnDestroy() { RestoreSurfaces(); if (Instance == this) Instance = null; }

    void Start()
    {
        lighting = FindAnyObjectByType<LundunLighting>();
        lamps = FindAnyObjectByType<LundunLampLights>();
        foreach (WetSurface s in surfaces)
            if (s.material != null && !s.saved)
            {
                s.drySmoothness = s.material.HasProperty("_Smoothness") ? s.material.GetFloat("_Smoothness") : 0.2f;
                s.dryColour = s.material.HasProperty("_BaseColor") ? s.material.GetColor("_BaseColor") : Color.white;
                s.saved = true;
            }
        BuildRain();
        BuildStreaks();
        BuildAudio();
        // first weather: usually dry to start a session
        float r = UnityEngine.Random.value;
        Set(r < 0.45f ? Kind.Clear : r < 0.8f ? Kind.Cloudy : Kind.Drizzle, true);
        if (devButton) Invoke(nameof(BuildDevButton), 0.3f);
    }

    // ------------------------------------------------------------------ the weather pattern
    void Update()
    {
        float hour = LundunClock.Instance != null ? LundunClock.Instance.Hour : Time.time / 60f % 24f;
        float dh = lastHour < 0f ? 0f : Mathf.Repeat(hour - lastHour, 24f);
        if (dh > 6f) dh = 0f;                                  // a jump (respawn skip etc.): no huge step
        lastHour = hour;

        hoursLeft -= dh;
        if (hoursLeft <= 0f) Set(Next(State), false);

        float k = blendHours > 0f ? Mathf.Clamp01(dh / blendHours) : 1f;
        Cloud = Mathf.MoveTowards(Cloud, cloudTarget, k);
        Rain = Mathf.MoveTowards(Rain, rainTarget, k);

        // wet quickly in the rain, dry slowly after (faster under a sunny day sky)
        bool day = hour > 7f && hour < 19f;
        if (Rain > 0.05f) Wetness = Mathf.MoveTowards(Wetness, 1f, dh / Mathf.Max(0.01f, soakHours) * Mathf.Lerp(0.4f, 1.5f, Rain));
        else Wetness = Mathf.MoveTowards(Wetness, 0f, dh / Mathf.Max(0.01f, dryHours) * (day ? Mathf.Lerp(1.4f, 0.7f, Cloud) : 0.5f));

        ApplySurfaces();
        UpdateRain();
        UpdateStreaks();
        if (devLabel != null) devLabel.text = State.ToString().ToUpperInvariant();
    }

    void Set(Kind k, bool instant)
    {
        State = k;
        switch (k)
        {
            case Kind.Clear: cloudTarget = 0.1f; rainTarget = 0f; hoursLeft = Range(clearHours); break;
            case Kind.Cloudy: cloudTarget = 0.7f; rainTarget = 0f; hoursLeft = Range(cloudyHours); break;
            case Kind.Drizzle: cloudTarget = 0.85f; rainTarget = 0.25f; hoursLeft = Range(drizzleHours); break;
            case Kind.Rain: cloudTarget = 0.95f; rainTarget = 0.6f; hoursLeft = Range(rainHours); break;
            default: cloudTarget = 1f; rainTarget = 1f; hoursLeft = Range(heavyHours); break;
        }
        if (instant) { Cloud = cloudTarget; Rain = rainTarget; Wetness = rainTarget > 0f ? 0.7f : 0f; }
    }

    static float Range(Vector2 r) { return UnityEngine.Random.Range(r.x, r.y); }

    /// London: clouds roll in, showers come and go, sunny spells after rain.
    static Kind Next(Kind k)
    {
        float r = UnityEngine.Random.value;
        switch (k)
        {
            case Kind.Clear: return r < 0.72f ? Kind.Cloudy : r < 0.88f ? Kind.Drizzle : Kind.Rain;          // the odd surprise shower
            case Kind.Cloudy: return r < 0.35f ? Kind.Clear : r < 0.7f ? Kind.Drizzle : r < 0.9f ? Kind.Rain : Kind.Heavy;
            case Kind.Drizzle: return r < 0.45f ? Kind.Cloudy : r < 0.8f ? Kind.Rain : Kind.Clear;
            case Kind.Rain: return r < 0.4f ? Kind.Drizzle : r < 0.7f ? Kind.Cloudy : r < 0.88f ? Kind.Heavy : Kind.Clear;
            default: return r < 0.5f ? Kind.Rain : r < 0.7f ? Kind.Drizzle : Kind.Cloudy;
        }
    }

    /// For testing (and later, missions): change the weather now.
    public void Force(Kind k) { Set(k, false); Cloud = Mathf.Lerp(Cloud, cloudTarget, 0.5f); }

    // ------------------------------------------------------------------ overcast light and grade
    void ModifyLook(ref LightingLook l)
    {
        float c = Cloud, r = Rain;
        l.sunIntensity *= Mathf.Lerp(1f, 0.35f, c);
        l.shadowStrength *= Mathf.Lerp(1f, 0.3f, c);
        Color daySky = new Color(0.55f, 0.58f, 0.62f), nightSky = new Color(0.08f, 0.08f, 0.1f);
        float bright = Mathf.Clamp01((l.skyHorizon.r + l.skyHorizon.g + l.skyHorizon.b) / 2.4f);
        Color grey = Color.Lerp(nightSky, daySky, bright);
        l.skyTop = Color.Lerp(l.skyTop, grey * 0.85f, c * 0.8f);
        l.skyHorizon = Color.Lerp(l.skyHorizon, grey, c * 0.75f);
        Color flat = (l.ambientSky + l.ambientHorizon) * 0.5f;
        l.ambientSky = Color.Lerp(l.ambientSky, flat * 0.9f, c * 0.6f);
        l.ambientHorizon = Color.Lerp(l.ambientHorizon, flat * 0.9f, c * 0.6f);
        float fogPull = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(c * 0.6f + r * 0.6f));
        l.fogStart *= fogPull; l.fogEnd *= fogPull;
        l.postExposure -= 0.25f * c;
        l.saturation -= 14f * c;
        l.contrast -= 2f * c;
        l.bloom *= 1f + 0.3f * Wetness;
    }

    void ModifyGrade(LundunAtmosphere.Grade g)
    {
        float c = Cloud, r = Rain;
        g.colorFilter = Color.Lerp(g.colorFilter, g.colorFilter * new Color(0.88f, 0.9f, 0.94f), c);
        g.temperature -= 7f * c;
        float lum = g.fogColor.r * 0.3f + g.fogColor.g * 0.59f + g.fogColor.b * 0.11f;
        g.fogColor = Color.Lerp(g.fogColor, new Color(lum * 0.95f, lum, lum * 1.06f), c * 0.7f);
        g.fogDensity *= 1f + 1.4f * c + 1.6f * r;
        g.fogStart *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(c * 0.5f + r * 0.7f));
        g.fogFalloff *= Mathf.Lerp(1f, 0.7f, r);               // rain haze reaches higher
        g.vignette += 0.06f * r;
        g.bloomScatter = Mathf.Min(0.9f, g.bloomScatter + 0.12f * Wetness);
    }

    // ------------------------------------------------------------------ wet roads
    void ApplySurfaces()
    {
        float w = Wetness * Wetness * (3f - 2f * Wetness);      // eases in and out
        foreach (WetSurface s in surfaces)
        {
            if (s.material == null || !s.saved) continue;
            if (s.material.HasProperty("_Smoothness")) s.material.SetFloat("_Smoothness", Mathf.Lerp(s.drySmoothness, Mathf.Max(s.drySmoothness, s.wetSmoothness), w));
            if (s.material.HasProperty("_BaseColor"))
            {
                Color c = s.dryColour * (1f - s.darken * w); c.a = s.dryColour.a;
                s.material.SetColor("_BaseColor", c);
            }
        }
    }

    void RestoreSurfaces()
    {
        foreach (WetSurface s in surfaces)
        {
            if (s == null || s.material == null || !s.saved) continue;
            if (s.material.HasProperty("_Smoothness")) s.material.SetFloat("_Smoothness", s.drySmoothness);
            if (s.material.HasProperty("_BaseColor")) s.material.SetColor("_BaseColor", s.dryColour);
        }
    }

    // ------------------------------------------------------------------ rain
    void BuildRain()
    {
        if (rainMaterial == null) { Debug.LogWarning("[Lundun] Weather: no rain material — run Lundun → 19 again."); return; }
        GameObject g = new GameObject("Rain");
        g.transform.SetParent(transform, false);
        g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);         // the box emits along its +Z: straight down
        rain = g.AddComponent<ParticleSystem>();
        rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule m = rain.main;
        m.loop = true;
        m.playOnAwake = false;
        m.duration = 1f;
        m.startLifetime = 1.15f;
        m.startSpeed = new ParticleSystem.MinMaxCurve(fallSpeed * 0.9f, fallSpeed * 1.1f);
        m.startSize3D = false;
        m.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.03f);
        m.startColor = new Color(0.78f, 0.84f, 0.92f, 0.38f);
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles = maxDrops;
        m.gravityModifier = 0f;

        ParticleSystem.VelocityOverLifetimeModule v = rain.velocityOverLifetime;
        v.enabled = true;
        v.space = ParticleSystemSimulationSpace.World;
        v.x = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
        v.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        v.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);

        ParticleSystem.ShapeModule s = rain.shape;
        s.enabled = true;
        s.shapeType = ParticleSystemShapeType.Box;
        s.scale = rainBox;

        rainEmission = rain.emission;
        rainEmission.enabled = true;
        rainEmission.rateOverTime = 0f;

        ParticleSystemRenderer pr = g.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Stretch;
        pr.velocityScale = 0.035f;
        pr.lengthScale = 1f;
        pr.sharedMaterial = rainMaterial;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        rain.Play();
    }

    void UpdateRain()
    {
        if (cam == null) cam = Camera.main;
        if (rain != null && cam != null)
        {
            Transform ct = cam.transform;
            Vector3 fwd = ct.forward; fwd.y = 0f;
            rain.transform.position = ct.position + fwd.normalized * 9f + Vector3.up * 13f;
            rainEmission.rateOverTime = heavyDropsPerSecond * Rain * Rain;
        }
        if (rainAudio != null)
        {
            float want = rainVolume * Mathf.Clamp01(Rain * 1.3f);
            rainAudio.volume = Mathf.MoveTowards(rainAudio.volume, want, Time.unscaledDeltaTime * 0.3f);
            if (rainAudio.volume > 0.001f && !rainAudio.isPlaying) rainAudio.Play();
            else if (rainAudio.volume <= 0.001f && rainAudio.isPlaying) rainAudio.Stop();
        }
    }

    void BuildAudio()
    {
        rainAudio = gameObject.AddComponent<AudioSource>();
        rainAudio.clip = MakeRainClip(3f);
        rainAudio.loop = true;
        rainAudio.spatialBlend = 0f;
        rainAudio.volume = 0f;
        rainAudio.playOnAwake = false;
    }

    /// Soft rain hiss, made in code: filtered noise with a few louder drops.
    static AudioClip MakeRainClip(float seconds)
    {
        const int rate = 22050;
        int n = (int)(rate * seconds);
        float[] d = new float[n];
        System.Random rnd = new System.Random(7);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rnd.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * 0.35f;                       // take the harsh top off
            lp2 += (lp - lp2) * 0.08f;                        // and a little body
            float hiss = (lp - lp2) * 0.6f + lp2 * 0.5f;
            if (rnd.NextDouble() < 0.0009) hiss += (float)(rnd.NextDouble() - 0.5) * 0.9f;   // a nearby drop
            d[i] = hiss;
        }
        // fade the loop's ends together so it doesn't click
        int f = rate / 10;
        for (int i = 0; i < f; i++) { float t = i / (float)f; float a = d[i], b = d[n - f + i]; d[i] = a * t + b * (1f - t); d[n - f + i] = b; }
        AudioClip c = AudioClip.Create("LundunRain", n - f, 1, rate, false);
        float[] o = new float[n - f]; Array.Copy(d, o, n - f);
        c.SetData(o, 0);
        return c;
    }

    // ------------------------------------------------------------------ lamp reflections on the wet road
    void BuildStreaks()
    {
        Sprite sp = StreakSprite();
        streaks = new SpriteRenderer[Mathf.Max(1, streakCount)];
        streakBase = new Vector3[streaks.Length];
        GameObject root = new GameObject("WetReflections");
        root.transform.SetParent(transform, false);
        for (int i = 0; i < streaks.Length; i++)
        {
            GameObject g = new GameObject("Streak_" + i);
            g.transform.SetParent(root.transform, false);
            SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = sp;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.enabled = false;
            streaks[i] = sr;
        }
    }

    void UpdateStreaks()
    {
        if (streaks == null) return;
        if (cam == null) cam = Camera.main;
        float level = lighting != null ? lighting.Current.lampLevel : 0f;
        float a = Wetness * level * streakStrength;
        if (cam == null || lamps == null || a <= 0.01f)
        {
            foreach (SpriteRenderer s in streaks) if (s.enabled) s.enabled = false;
            return;
        }
        Vector3 cp = cam.transform.position;
        if (Time.unscaledTime >= nextStreakPick) { nextStreakPick = Time.unscaledTime + 0.3f; PickStreakLamps(cp); }
        for (int i = 0; i < streaks.Length; i++)
        {
            SpriteRenderer s = streaks[i];
            if (i >= streakUsed) { if (s.enabled) s.enabled = false; continue; }
            Vector3 b = streakBase[i];
            Vector3 to = cp - b; to.y = 0f;
            float dist = to.magnitude;
            if (dist < 3f) { s.enabled = false; continue; }
            Vector3 dir = to / dist;
            float len = Mathf.Clamp(dist * 0.55f, 4f, 15f);
            float wid = 0.7f + dist * 0.012f;
            s.enabled = true;
            s.transform.SetPositionAndRotation(b + dir * (len * 0.5f) + Vector3.up * 0.04f, Quaternion.LookRotation(Vector3.down, dir));
            s.transform.localScale = new Vector3(wid, len, 1f);
            float fade = 1f - Mathf.InverseLerp(streakRange * 0.7f, streakRange, dist);
            Color c = streakColour; c.a = a * fade;
            s.color = c;
        }
    }

    /// The lamps in front of the camera, nearest first, with the ground under each one.
    void PickStreakLamps(Vector3 cp)
    {
        streakUsed = 0;
        IReadOnlyList<Vector3> all = lamps.Positions;
        if (all == null) return;
        Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
        List<KeyValuePair<float, Vector3>> found = new List<KeyValuePair<float, Vector3>>();
        float r2 = streakRange * streakRange;
        for (int i = 0; i < all.Count; i++)
        {
            Vector3 d = all[i] - cp; d.y = 0f;
            float d2 = d.sqrMagnitude;
            if (d2 > r2 || Vector3.Dot(d, fwd) < -4f) continue;
            found.Add(new KeyValuePair<float, Vector3>(d2, all[i]));
        }
        found.Sort((x, y) => x.Key.CompareTo(y.Key));
        int mask = ~LayerMask.GetMask("Player", "NPC", "Vehicle", "UI");
        for (int i = 0; i < found.Count && streakUsed < streaks.Length; i++)
        {
            Vector3 head = found[i].Value;
            float y = 0.02f;
            if (Physics.Raycast(head + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 15f, mask, QueryTriggerInteraction.Ignore)) y = hit.point.y;
            streakBase[streakUsed++] = new Vector3(head.x, y, head.z);
        }
    }

    /// Long soft streak: brightest towards the lamp end, fading towards you, soft at the sides.
    static Sprite StreakSprite()
    {
        const int w = 32, h = 128;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;          // v: 0 = lamp end, 1 = towards you
                float side = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u)), 2.2f);
                float along = Mathf.SmoothStep(0f, 1f, v / 0.12f) * Mathf.Pow(1f - v, 1.4f);
                float shimmer = 0.85f + 0.15f * Mathf.Sin(v * 70f + u * 3f);      // a little broken up, like rippled water
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(side * along * shimmer * 1.25f) * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), h);   // 1 unit long at scale 1
    }

    // ------------------------------------------------------------------ dev button (by the clock)
    void BuildDevButton()
    {
        GameObject clockGo = GameObject.Find("Clock");
        if (clockGo == null) return;
        RectTransform ct = (RectTransform)clockGo.transform;
        GameObject g = new GameObject("Dev_Weather", typeof(RectTransform));
        g.transform.SetParent(ct.parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = ct.anchoredPosition + new Vector2(260f, 0f); rt.sizeDelta = new Vector2(110, 36);
        Image bg = g.AddComponent<Image>();
        bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 2.6f; bg.color = new Color(0f, 0f, 0f, 0.45f);
        Button b = g.AddComponent<Button>(); b.targetGraphic = bg;
        b.onClick.AddListener(() => Force((Kind)(((int)State + 1) % 5)));
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(rt, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        devLabel = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) devLabel.font = LundunHud.Instance.FontAsset;
        devLabel.fontSize = 20; devLabel.alignment = TextAlignmentOptions.Center; devLabel.color = Color.white; devLabel.raycastTarget = false;
        devLabel.text = State.ToString().ToUpperInvariant();
    }
}
