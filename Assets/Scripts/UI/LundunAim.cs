using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// A2: the AIM button (aim down sights, over the shoulder).
///   • tap = aim on / off;  hold = aim while held, let go to stop
///   • slide your thumb on it to steer, like the fire button
///   • while aiming: camera tight over the shoulder, narrower view, calmer look speed, James walks (no sprint)
///   • going into aim snaps onto the nearest threat close to the crosshair (never ordinary pedestrians)
///   • a small "next target" button shows while aiming if another threat is in view: tap it to move to the next one
/// Built automatically next to the fire button by LundunAimAssist. Hidden with the rest of the on-foot controls.
public class LundunAim : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public static LundunAim Instance { get; private set; }
    /// Aiming right now.
    public static bool Aiming { get { return Instance != null && Instance.aiming; } }
    /// 0 = normal camera … 1 = fully aimed in (eased; CameraFollow uses it).
    public static float Blend { get { return Instance != null ? Instance.blend : 0f; } }
    /// Look speed while aimed in, relative to normal (A3: the AIM SPEED setting in the pause menu).
    public static float Sensitivity { get { return LundunSettings.AimSpeed; } }

    [Header("Feel")]
    [SerializeField] private float aimMoveSpeed = 3.2f;
    [Tooltip("Seconds to go fully in / out")]
    [SerializeField] private float blendTime = 0.16f;
    [Tooltip("Held longer than this = hold-to-aim (let go to stop); shorter = tap toggles")]
    [SerializeField] private float holdThreshold = 0.3f;

    [Header("Layout (1920x1080, from the bottom-right corner)")]
    [SerializeField] private Vector2 aimPosition = new Vector2(-690f, 480f);
    [SerializeField] private float aimSize = 150f;
    [SerializeField] private Vector2 switchPosition = new Vector2(-840f, 610f);
    [SerializeField] private float switchSize = 104f;

    static readonly Color Fill = new Color(0.04f, 0.06f, 0.11f, 0.55f);
    static readonly Color Rim = new Color(1f, 1f, 1f, 0.32f);
    static readonly Color OnFill = new Color(1f, 1f, 1f, 0.9f);
    static readonly Color OnIcon = new Color(0.04f, 0.06f, 0.11f, 1f);

    private bool aiming, wasOnAtPress;
    private float blend, pressTime, nextSwitchCheck;
    private Image disc, icon;
    private GameObject switchButton;
    private PlayerController player;
    private PlayerHealth health;
    private Animator animator;

    /// Makes the AIM and next-target buttons beside the fire button.
    public static LundunAim Build(RectTransform fireButton, TouchLookArea look)
    {
        RectTransform parent = fireButton.parent as RectTransform;
        if (parent == null) return null;
        Transform old = parent.Find("AimButton");
        if (old != null) return old.GetComponent<LundunAim>();

        GameObject sw = MakeButton(parent, "AimSwitchButton", LundunHudArt.Switch, Vector2.zero, 0f, out _, out _);
        GameObject go = MakeButton(parent, "AimButton", LundunHudArt.Aim, Vector2.zero, 0f, out Image d, out Image i);
        LundunAim a = go.AddComponent<LundunAim>();
        a.disc = d; a.icon = i; a.switchButton = sw;
        a.Place((RectTransform)go.transform, a.aimPosition, a.aimSize);
        a.Place((RectTransform)sw.transform, a.switchPosition, a.switchSize);
        if (look != null) go.AddComponent<LundunFireAim>().Init(look);          // drag on it to steer
        sw.AddComponent<Button>().onClick.AddListener(a.NextTarget);
        sw.SetActive(false);
        return a;
    }

    static GameObject MakeButton(RectTransform parent, string name, Sprite iconSprite, Vector2 pos, float size, out Image disc, out Image icon)
    {
        GameObject g = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rt = (RectTransform)g.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        disc = g.GetComponent<Image>();
        disc.sprite = LundunHudArt.Disc; disc.preserveAspect = true; disc.color = Fill;

        GameObject r = new GameObject("LundunRim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rr = (RectTransform)r.transform; rr.SetParent(rt, false);
        rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = rr.offsetMax = Vector2.zero;
        Image ri = r.GetComponent<Image>(); ri.sprite = LundunHudArt.Ring; ri.color = Rim; ri.raycastTarget = false; ri.preserveAspect = true;

        GameObject ic = new GameObject("LundunIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform ir = (RectTransform)ic.transform; ir.SetParent(rt, false);
        ir.anchorMin = new Vector2(0.24f, 0.24f); ir.anchorMax = new Vector2(0.76f, 0.76f); ir.offsetMin = ir.offsetMax = Vector2.zero;
        icon = ic.GetComponent<Image>(); icon.sprite = iconSprite; icon.color = new Color(1f, 1f, 1f, 0.95f); icon.raycastTarget = false; icon.preserveAspect = true;

        g.AddComponent<LundunPressFX>();
        return g;
    }

    void Place(RectTransform rt, Vector2 pos, float size) { rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(size, size); }

    void Awake() { Instance = this; }

    void Start()
    {
        player = FindAnyObjectByType<PlayerController>();
        health = PlayerHealth.Instance;
        if (player != null) animator = player.GetComponentInChildren<Animator>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (switchButton != null) Destroy(switchButton);
    }

    // into a car, a cutscene, Wasted/Busted: the on-foot controls hide, so stop aiming
    void OnDisable() { SetAim(false); blend = 0f; }

    public void OnPointerDown(PointerEventData e)
    {
        wasOnAtPress = aiming;
        pressTime = Time.unscaledTime;
        if (!aiming) SetAim(true);
    }

    public void OnPointerUp(PointerEventData e)
    {
        float held = Time.unscaledTime - pressTime;
        if (wasOnAtPress && held < holdThreshold) SetAim(false);          // tap while aiming = stop
        else if (!wasOnAtPress && held >= holdThreshold) SetAim(false);   // held to aim, let go = stop
    }

    public void SetAim(bool on)
    {
        if (aiming == on) return;
        aiming = on;
        if (player != null) player.SpeedCap = on ? aimMoveSpeed : 0f;
        LundunAnim.Bool(animator, "Aim", on);
        if (disc != null) disc.color = on ? OnFill : Fill;
        if (icon != null) icon.color = on ? OnIcon : new Color(1f, 1f, 1f, 0.95f);
        if (on && LundunAimAssist.Instance != null) LundunAimAssist.Instance.SnapToBest();
        if (!on && switchButton != null) switchButton.SetActive(false);
        LundunHaptics.Light();
    }

    void NextTarget()
    {
        if (aiming && LundunAimAssist.Instance != null && LundunAimAssist.Instance.SwitchTarget()) LundunHaptics.Light();
    }

    void Update()
    {
        if (aiming && health != null && health.IsDead) SetAim(false);
        float want = aiming ? 1f : 0f;
        blend = Mathf.MoveTowards(blend, want, Time.deltaTime / Mathf.Max(0.01f, blendTime));

        if (switchButton != null && Time.unscaledTime >= nextSwitchCheck)
        {
            nextSwitchCheck = Time.unscaledTime + 0.15f;
            bool show = aiming && LundunAimAssist.Instance != null && LundunAimAssist.Instance.OtherThreatsInView() > 0;
            if (switchButton.activeSelf != show) switchButton.SetActive(show);
        }
    }

    /// Eased 0..1 for the camera.
    public static float Eased { get { float b = Blend; return b * b * (3f - 2f * b); } }
}
