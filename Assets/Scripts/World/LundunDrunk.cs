using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// How drunk James is (pints from the pub: LundunPubs), and what that does. Each pint stacks; it wears off, about a
/// pint every 45 seconds. The drunker he is:
///   • the view sways and rolls, blurs at the edges, colours split (double vision), the screen darkens at the corners
///   • the phone buzzes now and then (harder the drunker he is)
///   • on foot he staggers (drifts side to side as he walks)
///   • driving, the car wanders; swerve near the police and they'll have you (a wanted star)
/// Wasted or busted: sober again. A small TIPSY / DRUNK / HAMMERED tag at the top while it lasts.
[DefaultExecutionOrder(500)]
public class LundunDrunk : MonoBehaviour
{
    public static LundunDrunk Instance { get; private set; }
    /// Pints in him right now (goes down as it wears off).
    public static float Pints { get { return Instance != null ? Instance.pints : 0f; } }
    /// 0 sober … 1 as drunk as it gets.
    public static float Level { get { return Instance != null ? Mathf.Clamp01(Instance.pints / 6f) : 0f; } }

    const float WearOffSeconds = 45f;           // per pint
    const float MostPints = 9f;

    private float pints, shown, nextBuzz, nextPoliceCheck, seed;
    private Volume volume;
    private ChromaticAberration chroma;
    private LensDistortion lens;
    private Vignette vignette;
    private DepthOfField blur;
    private Quaternion savedRot;
    private Camera swayed;
    private TextMeshProUGUI tag;
    private GameObject tagGo;
    private VehicleSystem vehicles;

    void Awake() { Instance = this; seed = Random.value * 100f; }
    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += Before;
        RenderPipelineManager.endCameraRendering += After;
    }
    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Before;
        RenderPipelineManager.endCameraRendering -= After;
    }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() { BuildVolume(); BuildTag(); }

    /// One more pint.
    public static void Drink()
    {
        if (Instance == null) return;
        Instance.pints = Mathf.Min(MostPints, Instance.pints + 1f);
        LundunHaptics.Heavy();
    }

    public static void Sober() { if (Instance != null) Instance.pints = 0f; }

    void Update()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph != null && ph.IsDead) pints = 0f;
        if (pints > 0f) pints = Mathf.Max(0f, pints - Time.deltaTime / WearOffSeconds);
        shown = Mathf.MoveTowards(shown, Level, Time.deltaTime * 0.4f);       // it creeps up on you
        float k = shown;
        float t = Time.time + seed;

        if (volume != null)
        {
            volume.weight = k;
            volume.enabled = k > 0.001f;
            if (k > 0.001f)
            {
                chroma.intensity.Override(Mathf.Clamp01(0.35f + 0.65f * k + Mathf.Sin(t * 1.7f) * 0.15f));
                lens.intensity.Override(Mathf.Sin(t * 0.8f) * 0.35f * k);
                lens.scale.Override(1f + 0.04f * k);
                vignette.intensity.Override(0.25f + 0.25f * k);
            }
        }

        // buzz: the drunker, the more often and the harder
        if (k > 0.25f && Time.unscaledTime >= nextBuzz)
        {
            nextBuzz = Time.unscaledTime + Random.Range(0.5f, 1.6f) / Mathf.Max(0.3f, k);
            if (k > 0.65f) LundunHaptics.Heavy(); else LundunHaptics.Medium();
        }

        if (ph != null && !ph.IsDead && k > 0.05f)
        {
            if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
            VehicleSystem vs = vehicles;
            if (vs != null && vs.IsDriving) Driving(vs, k, t);
            else if (ph.OnFoot) Stagger(ph, k, t);
        }

        if (tagGo != null)
        {
            bool on = pints > 0.05f;
            if (tagGo.activeSelf != on) tagGo.SetActive(on);
            if (on) tag.text = (Level < 0.3f ? "TIPSY" : Level < 0.7f ? "DRUNK" : "HAMMERED") + "  <size=70%>" + Mathf.CeilToInt(pints) + (Mathf.CeilToInt(pints) == 1 ? " PINT" : " PINTS") + "</size>";
        }
    }

    /// On foot: he drifts side to side as he walks (and lurches now and then).
    void Stagger(PlayerHealth ph, float k, float t)
    {
        CharacterController cc = ph.Body.GetComponent<CharacterController>();
        if (cc == null || !cc.enabled || VehicleSystem.Locked) return;
        Vector3 vel = cc.velocity; vel.y = 0f;
        if (vel.magnitude < 0.6f) return;
        Vector3 side = Vector3.Cross(Vector3.up, vel.normalized);
        float drift = (Mathf.Sin(t * 1.4f) + 0.5f * Mathf.Sin(t * 3.7f)) * 1.1f * k;
        cc.Move(side * drift * Time.deltaTime);
    }

    /// Driving: the car wanders; the police notice if they're close.
    void Driving(VehicleSystem vs, float k, float t)
    {
        CarController car = vs.CurrentCar;
        Rigidbody rb = car != null ? car.GetComponent<Rigidbody>() : null;
        if (rb == null) return;
        float wander = (Mathf.Sin(t * 0.9f) + 0.6f * Mathf.Sin(t * 2.3f)) * k;
        rb.AddTorque(Vector3.up * wander * rb.mass * 0.6f, ForceMode.Force);
        if (k < 0.35f || Time.time < nextPoliceCheck) return;
        nextPoliceCheck = Time.time + 4f;
        if (rb.linearVelocity.magnitude < 5f || Random.value > 0.4f) return;
        WantedSystem w = WantedSystem.Instance;
        if (w == null || w.Stars > 0) return;
        Vector3 p = rb.position;
        bool near = false;
        foreach (PoliceOfficer o in PoliceOfficer.Active) if (o != null && (o.transform.position - p).sqrMagnitude < 45f * 45f) { near = true; break; }
        if (!near)
            foreach (PoliceAI c in FindObjectsByType<PoliceAI>(FindObjectsSortMode.None))
                if (c != null && (c.transform.position - p).sqrMagnitude < 45f * 45f) { near = true; break; }
        if (!near) return;
        w.RaiseTo(1, p, true);
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast("The police saw you swerving: drink-driving");
    }

    // ------------------------------------------------------------------ the swaying view (only while it's drawn)
    void Before(ScriptableRenderContext ctx, Camera cam)
    {
        if (shown < 0.01f || cam != Camera.main) return;
        float k = shown, t = Time.time + seed;
        float roll = (Mathf.Sin(t * 0.7f) * 6f + Mathf.Sin(t * 1.9f) * 2f) * k;
        float pitch = Mathf.Sin(t * 0.55f) * 2.5f * k;
        float yaw = Mathf.Sin(t * 0.43f + 1f) * 3f * k;
        savedRot = cam.transform.rotation;
        swayed = cam;
        cam.transform.rotation = savedRot * Quaternion.Euler(pitch, yaw, roll);
    }

    void After(ScriptableRenderContext ctx, Camera cam)
    {
        if (swayed != cam) return;
        cam.transform.rotation = savedRot;
        swayed = null;
    }

    // ------------------------------------------------------------------ setup
    void BuildVolume()
    {
        GameObject g = new GameObject("DrunkVolume");
        g.transform.SetParent(transform, false);
        volume = g.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        volume.weight = 0f;
        VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
        chroma = p.Add<ChromaticAberration>(true);
        lens = p.Add<LensDistortion>(true);
        vignette = p.Add<Vignette>(true);
        vignette.color.Override(new Color(0.15f, 0.05f, 0.02f));
        vignette.smoothness.Override(0.8f);
        blur = p.Add<DepthOfField>(true);
        blur.mode.Override(DepthOfFieldMode.Gaussian);
        blur.gaussianStart.Override(3f);
        blur.gaussianEnd.Override(14f);
        blur.gaussianMaxRadius.Override(1.2f);
        volume.profile = p;
        volume.enabled = false;
    }

    void BuildTag()
    {
        GameObject cg = new GameObject("DrunkUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 54;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        tagGo = new GameObject("DrunkTag", typeof(RectTransform));
        tagGo.transform.SetParent(cg.transform, false);
        RectTransform r = (RectTransform)tagGo.transform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f); r.pivot = new Vector2(0.5f, 1f);
        r.anchoredPosition = new Vector2(0f, -150f); r.sizeDelta = new Vector2(520f, 56f);
        Image bg = tagGo.AddComponent<Image>(); bg.color = new Color(0.4f, 0.22f, 0.02f, 0.75f); bg.raycastTarget = false;
        if (LundunHudArt.Pill != null) { bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 1.4f; }
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(tagGo.transform, false);
        RectTransform tr = (RectTransform)tg.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        tag = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) tag.font = LundunHud.Instance.FontAsset;
        tag.fontSize = 30; tag.alignment = TextAlignmentOptions.Center; tag.color = new Color(1f, 0.85f, 0.45f);
        tag.fontStyle = FontStyles.Bold; tag.raycastTarget = false; tag.characterSpacing = 6f; tag.richText = true;
        tagGo.SetActive(false);
    }
}
