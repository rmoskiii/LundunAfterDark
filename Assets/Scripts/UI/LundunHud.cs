using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// V2a: the Lundun HUD look. Restyles the existing HUD at start-up, without changing what any button does:
///   • one font (Rajdhani, SIL Open Font License) across every on-screen text
///   • combat and movement buttons: dark frosted discs with a thin rim and an icon (no words)
///   • contextual actions (ENTER / STEAL a car, the hire bike, the Tube): a compact pill with icon + short label,
///     stacked on the right, shown only while the action is available
///   • everything kept clear of the Dynamic Island / rounded corners (iPhone safe area)
///   • buttons press in, with a light tap of haptics
///   • the performance numbers hidden; tap the clock three times to show / hide them
/// Added to the HUD Canvas by Lundun → 16.
[DefaultExecutionOrder(-500)]
public class LundunHud : MonoBehaviour
{
    public static LundunHud Instance { get; private set; }

    [SerializeField] private Font font;                                   // Assets/UI/Fonts/Rajdhani-Bold.ttf

    [Header("Palette")]
    [SerializeField] private Color fill = new Color(0.04f, 0.06f, 0.11f, 0.55f);       // night navy, frosted
    [SerializeField] private Color rim = new Color(1f, 1f, 1f, 0.32f);
    [SerializeField] private Color iconColour = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color fireRim = new Color(1f, 0.36f, 0.28f, 0.9f);
    [SerializeField] private Color pillFill = new Color(0.03f, 0.05f, 0.09f, 0.84f);
    [SerializeField] private Color carAccent = new Color(1f, 0.66f, 0.18f);              // sodium amber
    [SerializeField] private Color bikeAccent = new Color(0.1f, 0.72f, 0.68f);           // Lundun Cycles teal
    [SerializeField] private Color tubeAccent = new Color(0.16f, 0.24f, 0.62f);          // Underground navy

    [Header("Layout")]
    [Tooltip("Contextual prompts: right edge, measured from the screen's right (1920x1080 layout)")]
    [SerializeField] private float pillRight = -130f;
    [SerializeField] private float pillHeight = 96f;
    [SerializeField] private float pillGap = 16f;
    [SerializeField] private float pillLowestY = 520f;
    [Tooltip("R1: use LundunStick for the joystick (grab it anywhere on the base, follows its own finger, never sticks). " +
             "Untick to go back to Unity's OnScreenStick")]
    [SerializeField] private bool useLundunStick = true;
    [Tooltip("Extra space kept inside the iPhone's safe area")]
    [SerializeField] private float safeMargin = 12f;

    [Header("Debug")]
    [SerializeField] private bool statsOnByDefault = false;

    const string StatsKey = "Lundun.ShowStats";
    private TMP_FontAsset fontAsset;
    private bool styled;
    private Rect lastSafe;
    private Vector2 lastScreen;
    private readonly Dictionary<RectTransform, Vector2> homePos = new Dictionary<RectTransform, Vector2>();
    private readonly List<Canvas> hudCanvases = new List<Canvas>();
    private TextMeshProUGUI enterLabel;
    private VehicleSystem vehicles;
    private float nextFontSweep, nextSafeCheck;

    public TMP_FontAsset FontAsset { get { return fontAsset; } }

    void Awake()
    {
        Instance = this;
        if (font != null)
        {
            try
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(font);
                if (fontAsset != null)
                {
                    fontAsset.name = "Rajdhani (Lundun)";
                    TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
                    if (fallback != null)
                    {
                        if (fontAsset.fallbackFontAssetTable == null) fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                        fontAsset.fallbackFontAssetTable.Add(fallback);       // any symbol Rajdhani lacks still shows
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Lundun] HUD font couldn't be made: " + e.Message); fontAsset = null; }
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    IEnumerator Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        // let everything that builds its own UI in Start (bike, Tube, clock, stats, health bar) go first
        yield return null;
        yield return null;
        Apply();
    }

    void Update()
    {
        if (!styled) return;
        Speedo();

        // ENTER your own car, STEAL anyone else's
        if (enterLabel != null && enterLabel.gameObject.activeInHierarchy && vehicles != null)
        {
            CarController near = vehicles.NearbyCar;
            Transform mine = LundunPlaces.Instance != null ? LundunPlaces.Instance.Kestrel : null;
            string want = near != null && mine != null && near.transform == mine ? "ENTER" : "STEAL";
            if (enterLabel.text != want) enterLabel.text = want;
        }

        // texts created later (map labels, toasts, Tube list…) get the font too
        if (fontAsset != null && Time.unscaledTime >= nextFontSweep)
        {
            nextFontSweep = Time.unscaledTime + 2f;
            foreach (Canvas c in hudCanvases) if (c != null) ApplyFont(c.transform);
        }

        // rotated, or the safe area changed
        if (Time.unscaledTime >= nextSafeCheck)
        {
            nextSafeCheck = Time.unscaledTime + 0.5f;
            if (Screen.safeArea != lastSafe || new Vector2(Screen.width, Screen.height) != lastScreen) ApplySafeArea();
        }
    }

    // ------------------------------------------------------------------ the restyle
    public void Apply()
    {
        if (styled) return;
        styled = true;

        hudCanvases.Clear();
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) hudCanvases.Add(c);
        if (fontAsset != null) foreach (Canvas c in hudCanvases) ApplyFont(c.transform);

        // combat and movement: icons only
        Round("FireButton", LundunHudArt.Crosshair, fireRim, 0.5f);
        Round("JumpButton", LundunHudArt.Jump, rim, 0.46f);
        Round("LeftButton", LundunHudArt.Left, rim, 0.42f);
        Round("RightButton", LundunHudArt.Right, rim, 0.42f);
        Round("GasButton", LundunHudArt.Gas, rim, 0.4f);
        Round("BrakeButton", LundunHudArt.Brake, rim, 0.4f);
        Round("ExitButton", LundunHudArt.Exit, rim, 0.48f);
        Joystick();
        if (vehicles != null && vehicles.PointToSteer) DrivingHud();

        // contextual: icon + short label, stacked on the right
        float y = pillLowestY;
        RectTransform enter = Find("EnterCarButton");
        if (enter != null)
        {
            enterLabel = MakeLabel(enter, "STEAL");
            Pill(enter, LundunHudArt.Car, carAccent, enterLabel, y);
        }
        y += pillHeight + pillGap;
        RectTransform bike = Find("BikeButton");
        if (bike != null) Pill(bike, LundunHudArt.Bike, bikeAccent, bike.GetComponentInChildren<TextMeshProUGUI>(true), y);
        y += pillHeight + pillGap;
        RectTransform tubeUi = Find("TubeUI");
        RectTransform tube = tubeUi != null ? (RectTransform)tubeUi.Find("Button") : null;
        if (tube != null) Pill(tube, LundunHudArt.Tube, tubeAccent, tube.GetComponentInChildren<TextMeshProUGUI>(true), y);

        // Tube station list: rounded panel and buttons
        RectTransform tubePanel = Find("TubePanel");
        if (tubePanel != null)
        {
            Image pi = tubePanel.GetComponent<Image>();
            if (pi != null) { pi.sprite = LundunHudArt.Panel; pi.type = Image.Type.Sliced; pi.pixelsPerUnitMultiplier = 0.6f; }
            foreach (Button sb in tubePanel.GetComponentsInChildren<Button>(true))
            {
                Image bi = sb.GetComponent<Image>();
                if (bi != null) { bi.sprite = LundunHudArt.Pill; bi.type = Image.Type.Sliced; bi.pixelsPerUnitMultiplier = 1f; }
                if (sb.GetComponent<LundunPressFX>() == null) sb.gameObject.AddComponent<LundunPressFX>();
            }
        }

        // wanted stars, clock, dev buttons
        RectTransform stars = Find("WantedStars");
        if (stars != null)
            foreach (Image s in stars.GetComponentsInChildren<Image>(true)) { s.sprite = LundunHudArt.Star; s.preserveAspect = true; s.type = Image.Type.Simple; }
        RectTransform clock = Find("Clock");
        if (clock != null)
        {
            TextMeshProUGUI ct = clock.GetComponent<TextMeshProUGUI>();
            if (ct != null) { ct.raycastTarget = true; ct.characterSpacing = 2f; }
            clock.gameObject.AddComponent<LundunTripleTap>().Tapped += ToggleStats;
            foreach (Transform sib in clock.parent)
                if (sib.name.StartsWith("Dev_"))
                {
                    Image img = sib.GetComponent<Image>();
                    if (img != null) { img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 2.6f; img.color = new Color(0f, 0f, 0f, 0.45f); }
                }
        }

        SetStats(PlayerPrefs.GetInt(StatsKey, statsOnByDefault ? 1 : 0) == 1);
        ApplySafeArea();
        if (GetComponent<LundunSettings>() == null) gameObject.AddComponent<LundunSettings>();      // V5: pause menu + settings
    }

    void ApplyFont(Transform root)
    {
        foreach (TextMeshProUGUI t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (t.font == fontAsset) continue;
            t.font = fontAsset;
            t.fontStyle &= ~FontStyles.Bold;              // the font is already bold
        }
    }

    // ------------------------------------------------------------------ buttons
    /// Disc + rim + icon. The button's own Image stays as the touch area, so its wiring is untouched.
    void Round(string name, Sprite icon, Color rimColour, float iconSize)
    {
        RectTransform b = Find(name);
        if (b == null) return;
        Image img = b.GetComponent<Image>();
        if (img != null) { img.sprite = LundunHudArt.Disc; img.type = Image.Type.Simple; img.preserveAspect = true; img.color = fill; }
        foreach (TextMeshProUGUI t in b.GetComponentsInChildren<TextMeshProUGUI>(true)) t.gameObject.SetActive(false);   // icons only
        Child(b, "LundunRim", LundunHudArt.Ring, rimColour, Vector2.zero, true);
        float s = Mathf.Min(b.rect.width, b.rect.height) * iconSize;
        Child(b, "LundunIcon", icon, iconColour, new Vector2(s, s), false);
        if (b.GetComponent<LundunPressFX>() == null) b.gameObject.AddComponent<LundunPressFX>();
    }

    void Joystick()
    {
        RectTransform bse = Find("JoystickBase");
        if (bse == null) return;
        Image img = bse.GetComponent<Image>();
        if (img != null) { img.sprite = LundunHudArt.Disc; img.type = Image.Type.Simple; img.preserveAspect = true; img.color = new Color(fill.r, fill.g, fill.b, 0.35f); }
        Child(bse, "LundunRim", LundunHudArt.Ring, rim, Vector2.zero, true).transform.SetAsFirstSibling();
        RectTransform handle = Find("JoystickHandle");
        if (handle != null)
        {
            Image h = handle.GetComponent<Image>();
            if (h != null) { h.sprite = LundunHudArt.Disc; h.type = Image.Type.Simple; h.preserveAspect = true; h.color = new Color(1f, 1f, 1f, 0.82f); }
            Child(handle, "LundunRim", LundunHudArt.Ring, new Color(0f, 0f, 0f, 0.25f), Vector2.zero, true);
            if (useLundunStick) UseLundunStick(bse, handle);
        }
    }

    /// R1: swap Unity's stick (on the knob) for LundunStick (on the base), feeding the same gamepad stick with the same
    /// travel. The old one stays on the knob, switched off.
    void UseLundunStick(RectTransform bse, RectTransform handle)
    {
        UnityEngine.InputSystem.OnScreen.OnScreenStick old = handle.GetComponent<UnityEngine.InputSystem.OnScreen.OnScreenStick>();
        string path = old != null && !string.IsNullOrEmpty(old.controlPath) ? old.controlPath : "<Gamepad>/leftStick";
        float range = old != null ? old.movementRange : 130f;
        if (old != null) old.enabled = false;
        LundunStick stick = bse.GetComponent<LundunStick>();
        if (stick == null) stick = bse.gameObject.AddComponent<LundunStick>();
        stick.Setup(handle, path, range);
    }

    // ------------------------------------------------------------------ V1: driving with the joystick
    private TextMeshProUGUI speedNumber;
    private int shownMph = -1;

    /// Point-to-steer: the joystick moves out of the on-foot controls into its own group (so it shows while driving
    /// too), the gas and arrow buttons go, the Brake button becomes the HANDBRAKE (where the gas pedal was), and a
    /// speedometer in mph sits above it.
    void DrivingHud()
    {
        RectTransform stick = Find("JoystickBase");
        RectTransform onFoot = stick != null ? stick.parent as RectTransform : null;
        if (stick != null && onFoot != null && onFoot.name != "SharedControls")
        {
            RectTransform shared = new GameObject("SharedControls", typeof(RectTransform)).GetComponent<RectTransform>();
            shared.SetParent(onFoot.parent, false);
            shared.anchorMin = onFoot.anchorMin; shared.anchorMax = onFoot.anchorMax; shared.pivot = onFoot.pivot;
            shared.offsetMin = onFoot.offsetMin; shared.offsetMax = onFoot.offsetMax; shared.localScale = onFoot.localScale;
            shared.SetSiblingIndex(onFoot.GetSiblingIndex());
            stick.SetParent(shared, false);
            VehicleSystem.SharedControls = shared.gameObject;
        }
        foreach (string n in new[] { "GasButton", "LeftButton", "RightButton" })
        {
            RectTransform b = Find(n);
            if (b != null) b.gameObject.SetActive(false);
        }
        RectTransform gas = Find("GasButton"), brake = Find("BrakeButton");
        if (brake != null)
        {
            if (gas != null) { brake.anchorMin = gas.anchorMin; brake.anchorMax = gas.anchorMax; brake.anchoredPosition = gas.anchoredPosition; brake.sizeDelta = gas.sizeDelta; }
            Transform icon = brake.Find("LundunIcon");
            if (icon != null)
            {
                Image im = icon.GetComponent<Image>();
                if (im != null) im.sprite = LundunHudArt.Handbrake;
                ((RectTransform)icon).sizeDelta = brake.sizeDelta * 0.5f;
            }
            Transform r = brake.Find("LundunRim");
            if (r != null) { Image ri = r.GetComponent<Image>(); if (ri != null) ri.color = fireRim; }
            // the speedometer, above it
            GameObject sp = new GameObject("Speedometer", typeof(RectTransform));
            RectTransform srt = (RectTransform)sp.transform;
            srt.SetParent(brake.parent, false);
            srt.anchorMin = srt.anchorMax = brake.anchorMin;
            srt.pivot = new Vector2(0.5f, 0f);
            srt.anchoredPosition = brake.anchoredPosition + new Vector2(0f, brake.sizeDelta.y * 0.5f + 24f);
            srt.sizeDelta = new Vector2(240f, 120f);
            speedNumber = MakeText(srt, "0", 76f, new Vector2(0f, 26f));
            TextMeshProUGUI unit = MakeText(srt, "MPH", 26f, new Vector2(0f, -8f));
            unit.color = new Color(1f, 1f, 1f, 0.6f); unit.characterSpacing = 8f;

            // L1: the steering pad / arrows and the GAS and BRAKE pedals (Settings → CONTROLS → STEERING)
            if (GetComponent<LundunDriveControls>() == null)
                gameObject.AddComponent<LundunDriveControls>().Setup((RectTransform)brake.parent, brake, srt, stick, fontAsset);
        }
    }

    TextMeshProUGUI MakeText(RectTransform parent, string text, float size, Vector2 at)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        RectTransform rt = (RectTransform)g.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = at; rt.sizeDelta = new Vector2(240f, size * 1.2f);
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) t.font = fontAsset;
        t.text = text; t.fontSize = size; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    void Speedo()
    {
        if (speedNumber == null || vehicles == null || !vehicles.IsDriving) return;
        int mph = Mathf.RoundToInt(vehicles.PlayerSpeed * 2.23694f);
        if (mph == shownMph) return;
        shownMph = mph;
        speedNumber.text = mph.ToString();
    }

    /// Compact contextual prompt: accent disc with icon on the left, short label on the right, width fits the label.
    void Pill(RectTransform b, Sprite icon, Color accent, TextMeshProUGUI label, float y)
    {
        b.anchorMin = b.anchorMax = new Vector2(1f, 0f);
        b.pivot = new Vector2(1f, 0.5f);
        b.anchoredPosition = new Vector2(pillRight, y);
        b.sizeDelta = new Vector2(260f, pillHeight);
        Image img = b.GetComponent<Image>();
        if (img != null) { img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 32f * 2f / pillHeight; img.preserveAspect = false; img.color = pillFill; }

        float d = pillHeight - 22f;
        RectTransform disc = Child(b, "LundunIconDisc", LundunHudArt.Disc, accent, new Vector2(d, d), false);
        disc.anchorMin = disc.anchorMax = new Vector2(0f, 0.5f);
        disc.anchoredPosition = new Vector2(11f + d * 0.5f, 0f);
        RectTransform ic = Child(disc, "LundunIcon", icon, iconColour, new Vector2(d * 0.62f, d * 0.62f), false);
        ic.anchoredPosition = Vector2.zero;

        if (label != null)
        {
            RectTransform lt = label.rectTransform;
            lt.anchorMin = Vector2.zero; lt.anchorMax = Vector2.one; lt.pivot = new Vector2(0.5f, 0.5f);
            lt.offsetMin = new Vector2(d + 26f, 0f); lt.offsetMax = new Vector2(-24f, 0f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontSize = 34f;
            label.characterSpacing = 3f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = Color.white;
            label.raycastTarget = false;
            LundunPill fit = b.GetComponent<LundunPill>();
            if (fit == null) fit = b.gameObject.AddComponent<LundunPill>();
            fit.Init(label, d + 26f + 24f);
        }
        if (b.GetComponent<LundunPressFX>() == null) b.gameObject.AddComponent<LundunPressFX>();
    }

    TextMeshProUGUI MakeLabel(RectTransform parent, string text)
    {
        foreach (TextMeshProUGUI t in parent.GetComponentsInChildren<TextMeshProUGUI>(true)) if (t.name == "LundunLabel") return t;
        GameObject g = new GameObject("LundunLabel", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        TextMeshProUGUI l = g.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) l.font = fontAsset;
        l.text = text;
        return l;
    }

    RectTransform Child(RectTransform parent, string name, Sprite sprite, Color colour, Vector2 size, bool stretch)
    {
        Transform old = parent.Find(name);
        if (old != null) return (RectTransform)old;
        GameObject g = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rt = (RectTransform)g.transform;
        rt.SetParent(parent, false);
        if (stretch) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        else { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.sizeDelta = size; }
        Image img = g.GetComponent<Image>();
        img.sprite = sprite; img.color = colour; img.raycastTarget = false; img.preserveAspect = true;
        return rt;
    }

    RectTransform Find(string name)
    {
        foreach (Canvas c in hudCanvases)
        {
            if (c == null) continue;
            if (c.name == name) return (RectTransform)c.transform;
            Transform t = FindDeep(c.transform, name);
            if (t != null) return (RectTransform)t;
        }
        return null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            Transform t = FindDeep(child, name);
            if (t != null) return t;
        }
        return null;
    }

    // ------------------------------------------------------------------ performance numbers
    /// V5: after the UI size changes, fit everything to the safe area again.
    public void Relayout() { Canvas.ForceUpdateCanvases(); ApplySafeArea(); }

    public bool StatsShown { get { return PlayerPrefs.GetInt(StatsKey, statsOnByDefault ? 1 : 0) == 1; } }

    public void ShowStats(bool on)
    {
        PlayerPrefs.SetInt(StatsKey, on ? 1 : 0); PlayerPrefs.Save();
        SetStats(on);
    }

    void ToggleStats()
    {
        bool on = PlayerPrefs.GetInt(StatsKey, statsOnByDefault ? 1 : 0) != 1;
        PlayerPrefs.SetInt(StatsKey, on ? 1 : 0); PlayerPrefs.Save();
        SetStats(on);
        LundunHaptics.Medium();
    }

    void SetStats(bool on)
    {
        RectTransform pop = Find("PopulationStats");
        if (pop != null) pop.gameObject.SetActive(on);
        RectTransform gfx = Find("GraphicsStats");
        if (gfx != null) gfx.gameObject.SetActive(on);
    }

    // ------------------------------------------------------------------ iPhone safe area
    // Controls are grouped by the corner / edge they hang from; each group moves inwards together, just far enough
    // to clear the rounded corners and the Dynamic Island. Same for every HUD canvas, so prompts stay lined up.
    void ApplySafeArea()
    {
        lastSafe = Screen.safeArea;
        lastScreen = new Vector2(Screen.width, Screen.height);
        if (Screen.width <= 0) return;

        List<RectTransform> items = new List<RectTransform>();
        float k = 0f;
        foreach (Canvas c in hudCanvases)
        {
            if (c == null) continue;
            RectTransform cr = (RectTransform)c.transform;
            if (cr.rect.width <= 0f) continue;
            if (k <= 0f) k = cr.rect.width / Screen.width;                    // pixels → layout units
            Collect(cr, items);
        }
        if (k <= 0f) return;
        float left = lastSafe.xMin * k, right = (Screen.width - lastSafe.xMax) * k;
        float bottom = lastSafe.yMin * k, top = (Screen.height - lastSafe.yMax) * k;

        Dictionary<Vector2, Vector2> need = new Dictionary<Vector2, Vector2>();     // per anchor corner: x and y push
        foreach (RectTransform r in items)
        {
            Vector2 p = Home(r), s = r.sizeDelta, pv = r.pivot, a = r.anchorMin;
            need.TryGetValue(a, out Vector2 n);
            if (a.x == 0f && left > 0f) n.x = Mathf.Max(n.x, left + safeMargin - (p.x - pv.x * s.x));
            if (a.x == 1f && right > 0f) n.x = Mathf.Max(n.x, right + safeMargin + (p.x + (1f - pv.x) * s.x));
            if (a.y == 0f && bottom > 0f) n.y = Mathf.Max(n.y, bottom + safeMargin - (p.y - pv.y * s.y));
            if (a.y == 1f && top > 0f) n.y = Mathf.Max(n.y, top + safeMargin + (p.y + (1f - pv.y) * s.y));
            need[a] = n;
        }
        foreach (RectTransform r in items)
        {
            Vector2 p = Home(r), a = r.anchorMin;
            Vector2 n = need[a];
            if (a.x == 0f) p.x += n.x; else if (a.x == 1f) p.x -= n.x;
            if (a.y == 0f) p.y += n.y; else if (a.y == 1f) p.y -= n.y;
            r.anchoredPosition = p;
        }
    }

    Vector2 Home(RectTransform r)
    {
        if (!homePos.TryGetValue(r, out Vector2 p)) { p = r.anchoredPosition; homePos[r] = p; }
        return p;
    }

    static readonly HashSet<string> skip = new HashSet<string> {
        "LookArea", "HitMarker", "FullMap", "Blackout", "BigText", "WastedText", "NavToast", "Crosshair",
        "TubePanel", "TubeBlackout", "PopulationStats", "ArrestMeter" };

    /// Corner-anchored controls, looking inside full-screen holders (OnFootControls, DrivingControls).
    static void Collect(RectTransform parent, List<RectTransform> into)
    {
        foreach (Transform t in parent)
        {
            RectTransform r = t as RectTransform;
            if (r == null || skip.Contains(r.name)) continue;
            bool point = r.anchorMin == r.anchorMax;
            bool corner = point && (r.anchorMin.x == 0f || r.anchorMin.x == 1f || r.anchorMin.y == 0f || r.anchorMin.y == 1f);
            if (corner) into.Add(r);
            else if (!point && r.anchorMin == Vector2.zero && r.anchorMax == Vector2.one && r.offsetMin == Vector2.zero && r.offsetMax == Vector2.zero)
                Collect(r, into);
        }
    }
}
