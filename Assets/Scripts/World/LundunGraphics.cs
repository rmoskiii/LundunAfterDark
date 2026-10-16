using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// Graphics V1: keeps a reliable 30 fps while running the picture as sharp as the phone allows.
/// Reads the real GPU time each frame (Frame Timing Stats); if the GPU gets close to the 30 fps budget it trims the
/// render resolution a step, and when there's plenty of room it brings it back up. Shows the numbers on screen,
/// including whether there'd be room for a 60 fps mode. Added by Lundun → 15.
public class LundunGraphics : MonoBehaviour
{
    public static LundunGraphics Instance { get; private set; }

    [Header("Resolution")]
    [Range(0.5f, 1f)] [SerializeField] private float startScale = 0.9f;
    [Range(0.5f, 1f)] [SerializeField] private float minScale = 0.7f;
    [Range(0.5f, 1f)] [SerializeField] private float maxScale = 1f;
    [SerializeField] private float step = 0.05f;
    [SerializeField] private bool adaptive = true;

    [Header("Budget (30 fps = 33.3 ms a frame)")]
    [Tooltip("GPU busier than this share of the frame = lower the resolution a step")]
    [Range(0.5f, 1f)] [SerializeField] private float lowerAbove = 0.85f;
    [Tooltip("GPU quieter than this share of the frame = raise it a step")]
    [Range(0.2f, 0.9f)] [SerializeField] private float raiseBelow = 0.55f;

    [Header("On-screen numbers")]
    [SerializeField] private bool showStats = true;

    private UniversalRenderPipelineAsset urp;
    private float originalScale = -1f;
    private readonly FrameTiming[] timings = new FrameTiming[1];
    private float gpuMs, cpuMs, frameMs = 33f, nextDecision;
    private int overCount, underCount;
    private bool haveGpu;
    private TextMeshProUGUI label;

    public float RenderScale { get { return urp != null ? urp.renderScale : 1f; } }
    public float GpuMs { get { return gpuMs; } }

    void Awake()
    {
        Instance = this;
        urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null)
        {
            originalScale = urp.renderScale;                  // put back on exit, so Play in the editor never changes the asset
            urp.renderScale = Mathf.Clamp(startScale, minScale, maxScale);
        }
    }

    void Start() { if (showStats) BuildLabel(); }

    void OnDisable() { Restore(); }
    void OnApplicationQuit() { Restore(); }
    void OnDestroy() { Restore(); if (Instance == this) Instance = null; }

    void Restore()
    {
        if (urp != null && originalScale > 0f) urp.renderScale = originalScale;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        frameMs = Mathf.Lerp(frameMs, dt * 1000f, 0.05f);

        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0.0)
        {
            haveGpu = true;
            gpuMs = Mathf.Lerp(gpuMs, (float)timings[0].gpuFrameTime, 0.1f);
            float cpu = (float)System.Math.Max(timings[0].cpuMainThreadFrameTime, timings[0].cpuRenderThreadFrameTime);
            if (cpu > 0f) cpuMs = Mathf.Lerp(cpuMs, cpu, 0.1f);
        }

        if (adaptive && urp != null && Time.unscaledTime >= nextDecision) Decide();
        if (label != null && Time.frameCount % 15 == 0) UpdateLabel();
    }

    /// Once a second: two busy seconds in a row = a step down; five calm seconds in a row = a step up.
    void Decide()
    {
        nextDecision = Time.unscaledTime + 1f;
        float budget = 1000f / Mathf.Max(1, Application.targetFrameRate > 0 ? Application.targetFrameRate : 30);
        bool busy, calm;
        if (haveGpu)
        {
            busy = gpuMs > budget * lowerAbove;
            calm = gpuMs < budget * raiseBelow && frameMs < budget * 1.05f;
        }
        else
        {
            // no GPU timings (some editors / devices): go by the frame rate alone
            busy = frameMs > budget * 1.1f;
            calm = false;
        }
        overCount = busy ? overCount + 1 : 0;
        underCount = calm ? underCount + 1 : 0;
        if (overCount >= 2) { SetScale(urp.renderScale - step); overCount = 0; }
        else if (underCount >= 5) { SetScale(urp.renderScale + step); underCount = 0; }
    }

    void SetScale(float s)
    {
        s = Mathf.Clamp(Mathf.Round(s / step) * step, minScale, maxScale);
        if (!Mathf.Approximately(s, urp.renderScale)) urp.renderScale = s;
    }

    // ------------------------------------------------------------------ on-screen numbers
    void UpdateLabel()
    {
        string res = Mathf.RoundToInt(RenderScale * 100f) + "%";
        if (!haveGpu) { label.text = "res " + res + "   gpu n/a"; return; }
        float worst = Mathf.Max(gpuMs, cpuMs);
        string sixty = worst < 14f ? "<color=#7CFC7C>60fps ok</color>" : worst < 16.6f ? "<color=#FFD24A>60fps tight</color>" : "<color=#FF7A6A>60fps no</color>";
        label.text = "res " + res + "   gpu " + gpuMs.ToString("0.0") + "ms   cpu " + cpuMs.ToString("0.0") + "ms   " + sixty;
    }

    void BuildLabel()
    {
        GameObject cg = new GameObject("GraphicsStats", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas c = cg.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 90;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(cg.transform, false);
        RectTransform rt = (RectTransform)tg.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -40f); rt.sizeDelta = new Vector2(900f, 30f);
        label = tg.AddComponent<TextMeshProUGUI>();
        label.fontSize = 20; label.alignment = TextAlignmentOptions.Center; label.color = new Color(1f, 1f, 1f, 0.85f);
        label.raycastTarget = false; label.richText = true;
    }
}
