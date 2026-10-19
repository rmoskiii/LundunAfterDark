using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// V5: the pause menu and settings. A pause button sits at the top centre; the game also pauses itself when you
/// leave the app. Settings are remembered between sessions:
///   • GRAPHICS  Battery / Balanced / Best — resolution range, anti-aliasing, shadow distance, rain density
///   • FRAME RATE 30 / 60 — 60 is smoother but hotter; resolution drops to hold it (the phone's own numbers are shown)
///   • UI SIZE   Small / Normal / Large
///   • HAPTICS   On / Off      • PERFORMANCE numbers  Hide / Show
/// A3: a CONTROLS tab:
///   • LOOK SPEED  how fast dragging turns the camera      • AIM SPEED  look speed while aimed in (AIM button)
///   • AIM ASSIST  Off / Light / Standard / Strong          • FIRE  Manual / Auto (shoots by itself when the crosshair turns red)
/// Added automatically by LundunHud. In the editor, Play never changes the project's render settings.
[DefaultExecutionOrder(300)]
public class LundunSettings : MonoBehaviour
{
    public static LundunSettings Instance { get; private set; }
    public bool IsOpen { get; private set; }

    public enum Quality { Battery, Balanced, Best }

    const string QKey = "Lundun.Quality", FpsKey = "Lundun.Fps60", UiKey = "Lundun.UiSize", HapticsKey = "Lundun.Haptics";
    const string LookKey = "Lundun.LookSpeed", AimKey = "Lundun.AimSpeed", AssistKey = "Lundun.AimAssist", AutoKey = "Lundun.AutoFire";
    const string LoadoutKey = "Lundun.TestLoadout";

    // ------------------------------------------------------------------ A3: control settings (read by the camera, aim and combat)
    const float LookMin = 0.4f, LookMax = 2f, LookStep = 0.1f, AimMin = 0.3f, AimMax = 1f, AimStep = 0.05f;
    static bool controlsLoaded;
    static float lookSpeed = 1f, aimSpeed = 0.65f;
    static int assistLevel = 2;
    static bool autoFire;
    static bool testLoadout = true;

    static void LoadControls()
    {
        if (controlsLoaded) return;
        controlsLoaded = true;
        lookSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(LookKey, 1f), LookMin, LookMax);
        aimSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(AimKey, 0.65f), AimMin, AimMax);
        assistLevel = Mathf.Clamp(PlayerPrefs.GetInt(AssistKey, 2), 0, 3);
        autoFire = PlayerPrefs.GetInt(AutoKey, 0) == 1;
        testLoadout = PlayerPrefs.GetInt(LoadoutKey, 1) == 1;
    }

    /// G2: TEST = every weapon and unlimited spare ammo; NORMAL = fists + pistol and what you pick up, ammo runs out.
    public static bool TestLoadout { get { LoadControls(); return testLoadout; } }

    /// Camera turn speed multiplier (1 = default).
    public static float LookSpeed { get { LoadControls(); return lookSpeed; } }
    /// Look speed while aimed in, relative to normal (0.65 = default).
    public static float AimSpeed { get { LoadControls(); return aimSpeed; } }
    /// 0 Off, 1 Light, 2 Standard (default), 3 Strong.
    public static int AssistLevel { get { LoadControls(); return assistLevel; } }
    /// Fire by itself when the crosshair is on a threat.
    public static bool AutoFire { get { LoadControls(); return autoFire; } }
    static readonly float[] UiScales = { 0.88f, 1f, 1.14f };
    static readonly Color Ink = new Color(0.03f, 0.05f, 0.09f, 0.94f);
    static readonly Color Accent = new Color(1f, 0.66f, 0.18f);              // sodium amber
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

    public Quality CurrentQuality { get; private set; } = Quality.Balanced;
    public bool Fps60 { get; private set; }
    public int UiSize { get; private set; } = 1;

    private UniversalRenderPipelineAsset urp;
    private int origMsaa = -1, origCascades = -1; private float origShadow = -1f;
    private readonly Dictionary<CanvasScaler, Vector2> origRef = new Dictionary<CanvasScaler, Vector2>();
    private float prevTimeScale = 1f;

    private GameObject canvasGo, menu, displayTab, controlsTab;
    private RectTransform panel;
    private TextMeshProUGUI lookValue, aimValue;
    private int tab;
    private RectTransform pauseButton;
    private TextMeshProUGUI fpsNote;
    private readonly Dictionary<string, List<Image>> groups = new Dictionary<string, List<Image>>();

    // ------------------------------------------------------------------ set-up
    void Awake()
    {
        Instance = this;
        urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null) { origMsaa = urp.msaaSampleCount; origShadow = urp.shadowDistance; origCascades = urp.shadowCascadeCount; }
    }

    void Start()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas) continue;
            CanvasScaler s = c.GetComponent<CanvasScaler>();
            if (s != null && s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) origRef[s] = s.referenceResolution;
        }
        CurrentQuality = (Quality)Mathf.Clamp(PlayerPrefs.GetInt(QKey, 1), 0, 2);
        Fps60 = PlayerPrefs.GetInt(FpsKey, 0) == 1;
        UiSize = Mathf.Clamp(PlayerPrefs.GetInt(UiKey, 1), 0, 2);
        LundunHaptics.Enabled = PlayerPrefs.GetInt(HapticsKey, 1) == 1;
        LoadControls();
        if (LundunAimAssist.Instance != null) LundunAimAssist.Instance.Level = (LundunAimAssist.Strength)assistLevel;

        BuildUI();
        ApplyQuality(); ApplyFps(); ApplyUiSize();
        RefreshButtons();
    }

    void OnDestroy() { Restore(); if (canvasGo != null) Destroy(canvasGo); if (Instance == this) Instance = null; }
    void OnApplicationQuit() { Restore(); }

    /// Put the project's render settings back (only matters in the editor).
    void Restore()
    {
        if (urp == null || origMsaa < 0) return;
        urp.msaaSampleCount = origMsaa; urp.shadowDistance = origShadow; urp.shadowCascadeCount = origCascades;
    }

    // leaving the app pauses the game
    void OnApplicationPause(bool paused) { if (paused) Open(); }

    void Update()
    {
        if (pauseButton == null) return;
        bool show = !IsOpen && !VehicleSystem.Locked && !(LundunFullMap.Instance != null && LundunFullMap.Instance.IsOpen)
                    && !(PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead);
        if (pauseButton.gameObject.activeSelf != show) pauseButton.gameObject.SetActive(show);
        if (IsOpen && fpsNote != null && Time.unscaledTime % 0.5f < Time.unscaledDeltaTime) fpsNote.text = FpsNoteText();
    }

    // ------------------------------------------------------------------ open / close
    public void Open()
    {
        if (IsOpen || menu == null) return;
        if (VehicleSystem.Locked || (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead)) return;
        if (LundunFullMap.Instance != null && LundunFullMap.Instance.IsOpen) return;
        if (LundunWeapons.Instance != null) LundunWeapons.Instance.CloseWheel(false);     // G2: never stay in slow-mo
        IsOpen = true;
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        AudioListener.pause = true;
        menu.SetActive(true);
        FitPanel();
        if (fpsNote != null) fpsNote.text = FpsNoteText();
        RefreshButtons();
    }

    public void Resume()
    {
        if (!IsOpen) return;
        IsOpen = false;
        menu.SetActive(false);
        Time.timeScale = prevTimeScale;
        AudioListener.pause = false;
        LundunHaptics.Light();
    }

    // ------------------------------------------------------------------ the settings
    void SetQuality(int i) { CurrentQuality = (Quality)i; PlayerPrefs.SetInt(QKey, i); PlayerPrefs.Save(); ApplyQuality(); RefreshButtons(); }
    void SetFps(int i) { Fps60 = i == 1; PlayerPrefs.SetInt(FpsKey, i); PlayerPrefs.Save(); ApplyFps(); RefreshButtons(); }
    void SetUi(int i) { UiSize = i; PlayerPrefs.SetInt(UiKey, i); PlayerPrefs.Save(); ApplyUiSize(); RefreshButtons(); }
    void SetHaptics(int i) { LundunHaptics.Enabled = i == 0; PlayerPrefs.SetInt(HapticsKey, i == 0 ? 1 : 0); PlayerPrefs.Save(); RefreshButtons(); LundunHaptics.Light(); }
    void SetStats(int i) { if (LundunHud.Instance != null) LundunHud.Instance.ShowStats(i == 1); RefreshButtons(); }

    void SetTab(int i)
    {
        tab = i;
        if (displayTab != null) displayTab.SetActive(i == 0);
        if (controlsTab != null) controlsTab.SetActive(i == 1);
        RefreshButtons();
    }

    void StepLook(int dir)
    {
        lookSpeed = Mathf.Clamp(Mathf.Round((lookSpeed + dir * LookStep) / LookStep) * LookStep, LookMin, LookMax);
        PlayerPrefs.SetFloat(LookKey, lookSpeed); PlayerPrefs.Save(); RefreshButtons();
    }

    void StepAim(int dir)
    {
        aimSpeed = Mathf.Clamp(Mathf.Round((aimSpeed + dir * AimStep) / AimStep) * AimStep, AimMin, AimMax);
        PlayerPrefs.SetFloat(AimKey, aimSpeed); PlayerPrefs.Save(); RefreshButtons();
    }

    void SetAssist(int i)
    {
        assistLevel = i; PlayerPrefs.SetInt(AssistKey, i); PlayerPrefs.Save();
        if (LundunAimAssist.Instance != null) LundunAimAssist.Instance.Level = (LundunAimAssist.Strength)i;
        RefreshButtons();
    }

    void SetFire(int i) { autoFire = i == 1; PlayerPrefs.SetInt(AutoKey, i); PlayerPrefs.Save(); RefreshButtons(); }

    void SetLoadout(int i)
    {
        testLoadout = i == 0; PlayerPrefs.SetInt(LoadoutKey, testLoadout ? 1 : 0); PlayerPrefs.Save();
        if (LundunWeapons.Instance != null) LundunWeapons.Instance.SetTestMode(testLoadout);
        RefreshButtons();
    }

    /// Large UI on a short screen: shrink the panel so it always fits.
    void FitPanel()
    {
        if (panel == null || canvasGo == null) return;
        Canvas.ForceUpdateCanvases();
        float h = ((RectTransform)canvasGo.transform).rect.height;
        float need = panel.sizeDelta.y + 40f;
        float k = h > 0f ? Mathf.Min(1f, h / need) : 1f;
        panel.localScale = new Vector3(k, k, 1f);
    }

    void ApplyQuality()
    {
        // resolution range (LundunGraphics keeps 30 fps inside it), anti-aliasing, shadows, rain
        float min, max, start; int msaa, cascades; float shadow, drops;
        switch (CurrentQuality)
        {
            case Quality.Battery: min = 0.6f; max = 0.8f; start = 0.75f; msaa = 2; shadow = 40f; cascades = 1; drops = 0.55f; break;
            case Quality.Best: min = 0.75f; max = 1f; start = 0.95f; msaa = 4; shadow = 75f; cascades = 2; drops = 1f; break;
            default: min = 0.7f; max = 0.92f; start = 0.88f; msaa = 4; shadow = 60f; cascades = 2; drops = 0.8f; break;
        }
        if (urp != null) { urp.msaaSampleCount = msaa; urp.shadowDistance = shadow; urp.shadowCascadeCount = cascades; }
        if (LundunGraphics.Instance != null) LundunGraphics.Instance.SetLimits(min, max, start);
        if (LundunWeather.Instance != null) LundunWeather.Instance.DropScale = drops;
    }

    void ApplyFps()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Fps60 ? 60 : 30;
    }

    void ApplyUiSize()
    {
        float k = UiScales[UiSize];
        foreach (KeyValuePair<CanvasScaler, Vector2> kv in origRef)
            if (kv.Key != null) kv.Key.referenceResolution = kv.Value / k;            // smaller reference = bigger UI
        if (LundunHud.Instance != null) LundunHud.Instance.Relayout();
    }

    string FpsNoteText()
    {
        LundunGraphics g = LundunGraphics.Instance;
        string phone = g != null && g.GpuMs > 0.5f
            ? "GPU right now: " + g.GpuMs.ToString("0") + " ms a frame. 60 fps needs under 16 ms" + (g.GpuMs < 15f ? ", so it should hold." : ", so resolution will drop to hold it.")
            : "60 fps is smoother but uses more battery and gets warmer.";
        return phone;
    }

    // ------------------------------------------------------------------ UI (built in code)
    void BuildUI()
    {
        // its own full-screen canvas at the top of the scene (not inside the HUD canvas, which would squash it)
        canvasGo = new GameObject("LundunPause", typeof(RectTransform));
        Canvas c = canvasGo.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 85;
        CanvasScaler sc = canvasGo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)canvasGo.transform;

        // pause button, top centre (clear of the notch: landscape tops have no inset)
        pauseButton = Disc(root, Ink, 76);
        pauseButton.anchorMin = pauseButton.anchorMax = new Vector2(0.5f, 1f); pauseButton.pivot = new Vector2(0.5f, 1f);
        pauseButton.anchoredPosition = new Vector2(0f, -22f);
        Image pb = pauseButton.GetComponent<Image>(); pb.raycastTarget = true;
        Button b = pauseButton.gameObject.AddComponent<Button>(); b.targetGraphic = pb; b.onClick.AddListener(Open);
        pauseButton.gameObject.AddComponent<LundunPressFX>();
        Stretch(Img(pauseButton, LundunHudArt.Ring, new Color(1f, 1f, 1f, 0.3f), 76));
        Img(pauseButton, LundunHudArt.Pause, Color.white, 34);

        // the menu
        menu = new GameObject("Menu", typeof(RectTransform));
        menu.transform.SetParent(root, false);
        RectTransform mr = (RectTransform)menu.transform; Stretch(mr);
        Image dim = menu.AddComponent<Image>(); dim.color = new Color(0f, 0.01f, 0.03f, 0.72f);       // blocks touches to the game

        panel = Rect("Panel", mr);
        panel.sizeDelta = new Vector2(1000f, 850f);
        Image pi = panel.gameObject.AddComponent<Image>();
        pi.sprite = LundunHudArt.Panel; pi.type = Image.Type.Sliced; pi.pixelsPerUnitMultiplier = 0.35f; pi.color = Ink;

        TextMeshProUGUI title = Text(panel, "PAUSED", 52, Color.white);
        title.characterSpacing = 18f;
        Top(title.rectTransform, -26f, 64f);

        // A3: two tabs, DISPLAY and CONTROLS
        Tabs(panel, new[] { "DISPLAY", "CONTROLS" }, -104f);
        displayTab = Rect("DisplayTab", panel).gameObject; Stretch((RectTransform)displayTab.transform);
        controlsTab = Rect("ControlsTab", panel).gameObject; Stretch((RectTransform)controlsTab.transform);
        RectTransform dt = (RectTransform)displayTab.transform, ct = (RectTransform)controlsTab.transform;

        float y = -196f;
        Row(dt, "GRAPHICS", "quality", new[] { "BATTERY", "BALANCED", "BEST" }, SetQuality, ref y);
        Row(dt, "FRAME RATE", "fps", new[] { "30 FPS", "60 FPS" }, SetFps, ref y);
        fpsNote = Note(dt, "", ref y);
        Row(dt, "UI SIZE", "ui", new[] { "SMALL", "NORMAL", "LARGE" }, SetUi, ref y);
        Row(dt, "HAPTICS", "haptics", new[] { "ON", "OFF" }, SetHaptics, ref y);
        Row(dt, "PERFORMANCE", "stats", new[] { "HIDE", "SHOW" }, SetStats, ref y);

        y = -196f;
        lookValue = Stepper(ct, "LOOK SPEED", StepLook, ref y);
        aimValue = Stepper(ct, "AIM SPEED", StepAim, ref y);
        Row(ct, "AIM ASSIST", "assist", new[] { "OFF", "LIGHT", "STANDARD", "STRONG" }, SetAssist, ref y);
        Row(ct, "FIRE", "fire", new[] { "MANUAL", "AUTO" }, SetFire, ref y);
        Row(ct, "LOADOUT", "loadout", new[] { "TEST", "NORMAL" }, SetLoadout, ref y);
        Note(ct, "AUTO fires by itself when the crosshair turns red.  TEST: every weapon, endless spare ammo.  NORMAL: fists, pistol and what you find.", ref y);
        controlsTab.SetActive(false);

        RectTransform resume = Rect("Resume", panel);
        resume.anchorMin = resume.anchorMax = new Vector2(0.5f, 0f); resume.pivot = new Vector2(0.5f, 0f);
        resume.anchoredPosition = new Vector2(0f, 40f); resume.sizeDelta = new Vector2(420f, 96f);
        Image ri = resume.gameObject.AddComponent<Image>();
        ri.sprite = LundunHudArt.Pill; ri.type = Image.Type.Sliced; ri.pixelsPerUnitMultiplier = 0.67f; ri.color = Accent;
        Button rb = resume.gameObject.AddComponent<Button>(); rb.targetGraphic = ri; rb.onClick.AddListener(Resume);
        resume.gameObject.AddComponent<LundunPressFX>();
        TextMeshProUGUI rt = Text(resume, "RESUME", 38, new Color(0.08f, 0.05f, 0.02f)); Stretch(rt.rectTransform); rt.characterSpacing = 10f;

        menu.SetActive(false);
    }

    void Tabs(RectTransform panel, string[] names, float y)
    {
        List<Image> list = new List<Image>();
        float w = 260f, gap = 12f, x0 = -(names.Length * w + (names.Length - 1) * gap) * 0.5f;
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            RectTransform o = Rect("Tab_" + names[i], panel);
            o.anchorMin = o.anchorMax = new Vector2(0.5f, 1f); o.pivot = new Vector2(0f, 1f);
            o.anchoredPosition = new Vector2(x0 + i * (w + gap), y); o.sizeDelta = new Vector2(w, 60f);
            Image img = o.gameObject.AddComponent<Image>();
            img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.07f;
            Button bt = o.gameObject.AddComponent<Button>(); bt.targetGraphic = img;
            bt.onClick.AddListener(() => { SetTab(idx); LundunHaptics.Light(); });
            o.gameObject.AddComponent<LundunPressFX>();
            TextMeshProUGUI t = Text(o, names[i], 24, Color.white); Stretch(t.rectTransform); t.characterSpacing = 8f;
            list.Add(img);
        }
        groups["tab"] = list;
    }

    TextMeshProUGUI Label(RectTransform parent, string label, float y)
    {
        TextMeshProUGUI l = Text(parent, label, 26, Dim);
        l.characterSpacing = 8f; l.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform lr = l.rectTransform;
        lr.anchorMin = lr.anchorMax = new Vector2(0f, 1f); lr.pivot = new Vector2(0f, 1f);
        lr.anchoredPosition = new Vector2(56f, y); lr.sizeDelta = new Vector2(270f, RowH);
        return l;
    }

    /// Small grey explanation line.
    TextMeshProUGUI Note(RectTransform parent, string text, ref float y)
    {
        TextMeshProUGUI n = Text(parent, text, 21, Dim);
        n.alignment = TextAlignmentOptions.TopLeft;
        n.textWrappingMode = TextWrappingModes.Normal;
        RectTransform r = n.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(330f, y + 14f); r.sizeDelta = new Vector2(620f, 56f);
        y -= 50f;
        return n;
    }

    /// Label, then  [ − ]  value  [ + ].
    TextMeshProUGUI Stepper(RectTransform parent, string label, Action<int> step, ref float y)
    {
        Label(parent, label, y);
        for (int i = 0; i < 2; i++)
        {
            int dir = i == 0 ? -1 : 1;
            RectTransform o = Rect(dir < 0 ? "Less" : "More", parent);
            o.anchorMin = o.anchorMax = new Vector2(0f, 1f); o.pivot = new Vector2(0f, 1f);
            o.anchoredPosition = new Vector2(dir < 0 ? 330f : 330f + 620f - 140f, y); o.sizeDelta = new Vector2(140f, RowH);
            Image img = o.gameObject.AddComponent<Image>();
            img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.94f; img.color = new Color(1f, 1f, 1f, 0.08f);
            Button bt = o.gameObject.AddComponent<Button>(); bt.targetGraphic = img;
            bt.onClick.AddListener(() => { step(dir); LundunHaptics.Light(); });
            o.gameObject.AddComponent<LundunPressFX>();
            Img(o, dir < 0 ? LundunHudArt.Minus : LundunHudArt.Plus, Color.white, 34);
        }
        TextMeshProUGUI v = Text(parent, "", 34, Color.white);
        RectTransform vr = v.rectTransform;
        vr.anchorMin = vr.anchorMax = new Vector2(0f, 1f); vr.pivot = new Vector2(0f, 1f);
        vr.anchoredPosition = new Vector2(330f + 150f, y); vr.sizeDelta = new Vector2(620f - 300f, RowH);
        v.characterSpacing = 4f;
        y -= RowGap;
        return v;
    }

    /// Label on the left, a segmented choice on the right.
    void Row(RectTransform panel, string label, string key, string[] options, Action<int> pick, ref float y)
    {
        Label(panel, label, y);

        List<Image> list = new List<Image>();
        float total = 620f, gap = 10f, w = (total - gap * (options.Length - 1)) / options.Length;
        for (int i = 0; i < options.Length; i++)
        {
            int idx = i;
            RectTransform o = Rect("Opt_" + options[i], panel);
            o.anchorMin = o.anchorMax = new Vector2(0f, 1f); o.pivot = new Vector2(0f, 1f);
            o.anchoredPosition = new Vector2(330f + i * (w + gap), y); o.sizeDelta = new Vector2(w, RowH);
            Image img = o.gameObject.AddComponent<Image>();
            img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.84f;
            Button bt = o.gameObject.AddComponent<Button>(); bt.targetGraphic = img;
            bt.onClick.AddListener(() => { pick(idx); LundunHaptics.Light(); });
            o.gameObject.AddComponent<LundunPressFX>();
            TextMeshProUGUI t = Text(o, options[i], options.Length > 3 ? 23 : 26, Color.white); Stretch(t.rectTransform); t.characterSpacing = options.Length > 3 ? 2f : 4f;
            list.Add(img);
        }
        groups[key] = list;
        y -= RowGap;
    }

    const float RowH = 70f, RowGap = 88f;

    void RefreshButtons()
    {
        Mark("quality", (int)CurrentQuality);
        Mark("fps", Fps60 ? 1 : 0);
        Mark("ui", UiSize);
        Mark("haptics", LundunHaptics.Enabled ? 0 : 1);
        Mark("stats", LundunHud.Instance != null && LundunHud.Instance.StatsShown ? 1 : 0);
        Mark("tab", tab);
        Mark("assist", assistLevel);
        Mark("fire", autoFire ? 1 : 0);
        Mark("loadout", testLoadout ? 0 : 1);
        if (lookValue != null) lookValue.text = lookSpeed.ToString("0.0") + "×";
        if (aimValue != null) aimValue.text = Mathf.RoundToInt(aimSpeed * 100f) + "%";
    }

    void Mark(string key, int selected)
    {
        if (!groups.TryGetValue(key, out List<Image> list)) return;
        for (int i = 0; i < list.Count; i++)
        {
            bool on = i == selected;
            list[i].color = on ? Accent : new Color(1f, 1f, 1f, 0.08f);
            TextMeshProUGUI t = list[i].GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.color = on ? new Color(0.08f, 0.05f, 0.02f) : Color.white;
        }
    }

    // ------------------------------------------------------------------ helpers
    static RectTransform Rect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static RectTransform Img(RectTransform parent, Sprite s, Color c, float size)
    {
        RectTransform r = Rect("Icon", parent);
        r.sizeDelta = new Vector2(size, size);
        Image i = r.gameObject.AddComponent<Image>(); i.sprite = s; i.color = c; i.raycastTarget = false; i.preserveAspect = true;
        return r;
    }

    static RectTransform Disc(RectTransform parent, Color c, float size) { return Img(parent, LundunHudArt.Disc, c, size); }

    static TextMeshProUGUI Text(RectTransform parent, string text, float size, Color col)
    {
        RectTransform r = Rect("Text", parent);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.color = col; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    static void Top(RectTransform r, float y, float h) { r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f); r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0f, y); r.sizeDelta = new Vector2(0f, h); }
}
