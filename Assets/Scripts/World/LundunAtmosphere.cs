using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// V3a: light and atmosphere on top of the day/night clock (LundunLighting / LundunClock stay as they are).
///   • a full colour grade for each time of day (night, dawn, day, dusk), blended on the same hours as the clock:
///     white balance, split toning (shadow / highlight colour), colour tint, vignette, bloom threshold / spread / tint
///   • height fog: thick near the ground, thin up high (drawn by the LundunHeightFog renderer feature)
///   • soft glow halos round the nearest street lamps at night
/// Every value can be tuned in the Inspector while playing. Added by Lundun → 18.
[DefaultExecutionOrder(200)]
public class LundunAtmosphere : MonoBehaviour
{
    public static LundunAtmosphere Instance { get; private set; }

    [Serializable]
    public class Grade
    {
        [Header("Colour")]
        [Range(-100, 100)] public float temperature;
        [Range(-100, 100)] public float tint;
        [ColorUsage(false, true)] public Color colorFilter = Color.white;
        [Tooltip("Colour pushed into the dark parts (grey = none)")] public Color shadows = Color.grey;
        [Tooltip("Colour pushed into the bright parts (grey = none)")] public Color highlights = Color.grey;
        [Range(-100, 100)] public float splitBalance;
        [Range(0, 1)] public float vignette = 0.18f;

        [Header("Glow (bloom)")]
        public float bloomThreshold = 0.95f;
        [Range(0, 1)] public float bloomScatter = 0.6f;
        public Color bloomTint = Color.white;

        [Header("Height fog")]
        public Color fogColor = new Color(0.6f, 0.65f, 0.72f);
        [Tooltip("How thick at ground level")] public float fogDensity = 0.006f;
        [Tooltip("How fast it thins out going up (per metre)")] public float fogFalloff = 0.06f;
        [Tooltip("Ground level for the fog (metres)")] public float fogHeight = 0f;
        [Tooltip("Clear for this many metres in front of the camera")] public float fogStart = 25f;
        [Range(0, 1)] public float fogStrength = 0.8f;
        [Range(0, 1)] public float fogOnSky = 0.5f;

        public static Grade Lerp(Grade a, Grade b, float t)
        {
            return new Grade
            {
                temperature = Mathf.Lerp(a.temperature, b.temperature, t), tint = Mathf.Lerp(a.tint, b.tint, t),
                colorFilter = Color.Lerp(a.colorFilter, b.colorFilter, t),
                shadows = Color.Lerp(a.shadows, b.shadows, t), highlights = Color.Lerp(a.highlights, b.highlights, t),
                splitBalance = Mathf.Lerp(a.splitBalance, b.splitBalance, t), vignette = Mathf.Lerp(a.vignette, b.vignette, t),
                bloomThreshold = Mathf.Lerp(a.bloomThreshold, b.bloomThreshold, t), bloomScatter = Mathf.Lerp(a.bloomScatter, b.bloomScatter, t),
                bloomTint = Color.Lerp(a.bloomTint, b.bloomTint, t),
                fogColor = Color.Lerp(a.fogColor, b.fogColor, t), fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t),
                fogFalloff = Mathf.Lerp(a.fogFalloff, b.fogFalloff, t), fogHeight = Mathf.Lerp(a.fogHeight, b.fogHeight, t),
                fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t), fogStrength = Mathf.Lerp(a.fogStrength, b.fogStrength, t),
                fogOnSky = Mathf.Lerp(a.fogOnSky, b.fogOnSky, t)
            };
        }

        public Grade Copy() { return Lerp(this, this, 0f); }
    }

    [Header("Grades (blended on the clock's hours)")]
    [SerializeField] private Grade night = new Grade
    {   // London after dark: teal shadows, sodium-orange highlights, a brown-purple city haze
        temperature = 4f, tint = 2f, colorFilter = new Color(0.97f, 0.97f, 1f),
        shadows = new Color(0.40f, 0.50f, 0.57f), highlights = new Color(0.64f, 0.50f, 0.35f), splitBalance = -15f, vignette = 0.3f,
        bloomThreshold = 0.75f, bloomScatter = 0.72f, bloomTint = new Color(1f, 0.82f, 0.62f),
        fogColor = new Color(0.17f, 0.13f, 0.14f), fogDensity = 0.008f, fogFalloff = 0.05f, fogStart = 15f, fogStrength = 0.85f, fogOnSky = 0.35f
    };
    [SerializeField] private Grade dawn = new Grade
    {   // early mist, soft pink-gold
        temperature = 6f, tint = 6f, colorFilter = new Color(1f, 0.97f, 0.96f),
        shadows = new Color(0.49f, 0.48f, 0.58f), highlights = new Color(0.60f, 0.53f, 0.47f), vignette = 0.22f,
        bloomThreshold = 0.88f, bloomScatter = 0.68f, bloomTint = new Color(1f, 0.88f, 0.8f),
        fogColor = new Color(0.74f, 0.63f, 0.62f), fogDensity = 0.014f, fogFalloff = 0.08f, fogStart = 20f, fogStrength = 0.85f, fogOnSky = 0.45f
    };
    [SerializeField] private Grade day = new Grade
    {   // clean, slightly cool London daylight
        temperature = -4f, tint = 0f, colorFilter = Color.white,
        shadows = new Color(0.47f, 0.50f, 0.56f), highlights = new Color(0.55f, 0.53f, 0.49f), vignette = 0.16f,
        bloomThreshold = 1f, bloomScatter = 0.6f, bloomTint = Color.white,
        fogColor = new Color(0.68f, 0.73f, 0.80f), fogDensity = 0.004f, fogFalloff = 0.05f, fogStart = 35f, fogStrength = 0.7f, fogOnSky = 0.5f
    };
    [SerializeField] private Grade dusk = new Grade
    {   // golden hour into purple
        temperature = 14f, tint = 5f, colorFilter = new Color(1f, 0.95f, 0.92f),
        shadows = new Color(0.52f, 0.46f, 0.60f), highlights = new Color(0.64f, 0.50f, 0.39f), splitBalance = -5f, vignette = 0.24f,
        bloomThreshold = 0.82f, bloomScatter = 0.7f, bloomTint = new Color(1f, 0.84f, 0.72f),
        fogColor = new Color(0.62f, 0.45f, 0.47f), fogDensity = 0.007f, fogFalloff = 0.05f, fogStart = 25f, fogStrength = 0.8f, fogOnSky = 0.45f
    };

    [Header("Filled in by Lundun → 18")]
    [SerializeField] private Volume volume;
    [SerializeField] private ScriptableRendererFeature[] fogFeatures = new ScriptableRendererFeature[0];
    [SerializeField] private Material fogMaterial;

    [Header("Lamp halos")]
    [SerializeField] private bool lampHalos = true;
    [SerializeField] private int haloCount = 24;
    [SerializeField] private float haloSize = 2.8f;
    [SerializeField] private float haloRange = 120f;
    [Range(0, 1)] [SerializeField] private float haloStrength = 0.55f;
    [SerializeField] private Color haloColour = new Color(1f, 0.74f, 0.42f);

    [Header("Testing")]
    [Tooltip("-1 = follow the clock. 0..24 = hold this hour's grade")]
    [SerializeField] private float previewHour = -1f;

    /// The grade being shown this frame (after weather, from V3b).
    public Grade Current { get; private set; }
    /// V3b weather hooks this to change the grade (overcast, rain…). Called every frame with the time-of-day grade.
    public static event Action<Grade> Modify;

    static readonly float[] KeyHours = { 0f, 4.5f, 5.5f, 6.5f, 8f, 17.5f, 19f, 19.75f, 21f, 24f };
    private Grade[] keys;
    private VolumeProfile profile;
    private WhiteBalance wb; private SplitToning split; private ColorAdjustments ca; private Vignette vig; private Bloom bloom;
    private bool fogOk;
    private LundunLighting lighting;
    private LundunLampLights lamps;
    private Camera cam;
    private SpriteRenderer[] halos;
    private float nextHaloPick;

    static readonly int FogColorId = Shader.PropertyToID("_LundunFogColor");
    static readonly int FogParamsId = Shader.PropertyToID("_LundunFogParams");
    static readonly int FogSkyId = Shader.PropertyToID("_LundunFogSky");

    void Awake()
    {
        Instance = this;
        keys = new[] { night, night, dawn, dawn, day, day, dusk, dusk, night, night };
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Shader.SetGlobalVector(FogColorId, Vector4.zero);
    }

    void Start()
    {
        lighting = FindAnyObjectByType<LundunLighting>();
        lamps = FindAnyObjectByType<LundunLampLights>();
        if (volume != null)
        {
            profile = volume.profile;                         // play-mode copy: the asset itself is never changed
            wb = Get<WhiteBalance>(); split = Get<SplitToning>(); ca = Get<ColorAdjustments>(); vig = Get<Vignette>(); bloom = Get<Bloom>();
        }
        // height fog only if the shader runs on this device
        fogOk = fogMaterial != null && fogMaterial.shader != null && fogMaterial.shader.isSupported;
        foreach (ScriptableRendererFeature f in fogFeatures) if (f != null) f.SetActive(fogOk);
        if (!fogOk) Shader.SetGlobalVector(FogColorId, Vector4.zero);
        if (lampHalos) BuildHalos();
    }

    T Get<T>() where T : VolumeComponent
    {
        if (profile == null) return null;
        if (!profile.TryGet(out T c)) c = profile.Add<T>(true);
        return c;
    }

    void LateUpdate()
    {
        float h = previewHour >= 0f ? previewHour : (LundunClock.Instance != null ? LundunClock.Instance.Hour : 23f);
        Grade g = Evaluate(h);
        if (Modify != null) Modify(g);
        Current = g;
        ApplyGrade(g);
        ApplyFog(g);
        if (halos != null) UpdateHalos();
    }

    public Grade Evaluate(float h)
    {
        int i = 0;
        while (i < KeyHours.Length - 2 && h >= KeyHours[i + 1]) i++;
        float t = Mathf.InverseLerp(KeyHours[i], KeyHours[i + 1], h);
        t = t * t * (3f - 2f * t);
        return Grade.Lerp(keys[i], keys[i + 1], t);
    }

    void ApplyGrade(Grade g)
    {
        if (wb != null) { wb.temperature.Override(g.temperature); wb.tint.Override(g.tint); }
        if (split != null) { split.shadows.Override(g.shadows); split.highlights.Override(g.highlights); split.balance.Override(g.splitBalance); }
        if (ca != null) ca.colorFilter.Override(g.colorFilter);                 // exposure / contrast / saturation stay with LundunLighting
        if (vig != null) { vig.intensity.Override(g.vignette); vig.smoothness.Override(0.35f); }
        if (bloom != null) { bloom.threshold.Override(g.bloomThreshold); bloom.scatter.Override(g.bloomScatter); bloom.tint.Override(g.bloomTint); }
    }

    void ApplyFog(Grade g)
    {
        if (!fogOk) return;
        Color c = g.fogColor;
        Shader.SetGlobalVector(FogColorId, new Vector4(c.r, c.g, c.b, g.fogStrength));
        Shader.SetGlobalVector(FogParamsId, new Vector4(g.fogDensity, g.fogFalloff, g.fogHeight, g.fogStart));
        Shader.SetGlobalVector(FogSkyId, new Vector4(g.fogOnSky, 380f, 0f, 0f));
    }

    // ------------------------------------------------------------------ lamp halos
    void BuildHalos()
    {
        Sprite s = HaloSprite();
        halos = new SpriteRenderer[Mathf.Max(1, haloCount)];
        GameObject root = new GameObject("LampHalos");
        root.transform.SetParent(transform, false);
        for (int i = 0; i < halos.Length; i++)
        {
            GameObject g = new GameObject("Halo_" + i);
            g.transform.SetParent(root.transform, false);
            SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.enabled = false;
            halos[i] = sr;
        }
    }

    readonly List<Vector3> nearest = new List<Vector3>();

    void UpdateHalos()
    {
        if (cam == null) cam = Camera.main;
        float level = lighting != null ? lighting.Current.lampLevel : 0f;
        if (cam == null || lamps == null || level <= 0.02f)
        {
            foreach (SpriteRenderer h in halos) if (h.enabled) h.enabled = false;
            return;
        }
        Vector3 cp = cam.transform.position;
        if (Time.unscaledTime >= nextHaloPick)
        {
            nextHaloPick = Time.unscaledTime + 0.25f;
            PickNearest(cp);
        }
        Quaternion face = cam.transform.rotation;
        for (int i = 0; i < halos.Length; i++)
        {
            SpriteRenderer h = halos[i];
            if (i >= nearest.Count) { if (h.enabled) h.enabled = false; continue; }
            Vector3 p = nearest[i];
            float d = Vector3.Distance(p, cp);
            float fade = 1f - Mathf.InverseLerp(haloRange * 0.7f, haloRange, d);
            h.enabled = fade > 0.01f;
            if (!h.enabled) continue;
            h.transform.SetPositionAndRotation(p, face);
            float size = haloSize * Mathf.Lerp(1f, 1.6f, Mathf.InverseLerp(20f, haloRange, d));   // far ones stay visible
            h.transform.localScale = new Vector3(size, size, 1f);
            Color c = haloColour; c.a = haloStrength * level * fade;
            h.color = c;
        }
    }

    void PickNearest(Vector3 cp)
    {
        nearest.Clear();
        IReadOnlyList<Vector3> all = lamps.Positions;
        if (all == null) return;
        float r2 = haloRange * haloRange;
        // keep the N closest (simple insertion; a few hundred lamps at 4 Hz is cheap)
        List<float> dist = new List<float>();
        for (int i = 0; i < all.Count; i++)
        {
            float d2 = (all[i] - cp).sqrMagnitude;
            if (d2 > r2) continue;
            int at = dist.Count;
            while (at > 0 && dist[at - 1] > d2) at--;
            if (at >= halos.Length) continue;
            dist.Insert(at, d2); nearest.Insert(at, all[i]);
            if (dist.Count > halos.Length) { dist.RemoveAt(dist.Count - 1); nearest.RemoveAt(nearest.Count - 1); }
        }
    }

    static Sprite HaloSprite()
    {
        const int n = 64;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (0.35f + 0.65f * a);                       // bright core, long soft tail
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px); t.Apply(false, true);
        return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);   // 1 unit wide at scale 1
    }
}
