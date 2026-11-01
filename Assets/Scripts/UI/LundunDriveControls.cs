using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// L1: the driving controls for PAD and ARROWS steering (Settings → CONTROLS → STEERING). Made by LundunHud.
///   PAD     put your left thumb down anywhere on the left side and slide it sideways: a little for a little, about a
///           thumb's width for full lock; let go and the wheel straightens. A bar shows where the wheel is.
///   ARROWS  a left and a right button instead.
///   pedals  GAS (tall, bottom right) and BRAKE (beside it): BRAKE slows you, and once you've stopped it reverses.
///           The HANDBRAKE button sits above the brake; the speedometer above the gas.
/// With POINT steering (V1) these hide and the joystick, handbrake and speedometer go back where they were.
public class LundunDriveControls : MonoBehaviour
{
    public static LundunDriveControls Instance { get; private set; }

    /// −1 full left … +1 full right.
    public float Steer { get; private set; }
    public float Gas { get; private set; }
    public float Brake { get; private set; }
    /// Something on the pad or pedals is being touched right now.
    public bool Touched => padFinger >= 0 || gasHeld || brakeHeld || leftHeld || rightHeld;

    const float FullLock = 150f;          // slide this far (canvas units, about a thumb's width) for full lock

    private RectTransform container, padArea, knob, track, arrows, pedals, handbrake, speedo, joystick;
    private Vector2 handbrakePos, handbrakeSize, speedoPos;
    private Vector2 handbrakeAnchorMin, handbrakeAnchorMax, speedoAnchorMin, speedoAnchorMax;
    private VehicleSystem vehicles;
    private Canvas canvas;
    private int padFinger = -1;
    private float padOrigin;
    private bool gasHeld, brakeHeld, leftHeld, rightHeld;
    private VehicleSystem.Steering shownMode = (VehicleSystem.Steering)(-1);
    private bool shownDriving;

    public void Setup(RectTransform drivingGroup, RectTransform handbrakeButton, RectTransform speedometer, RectTransform joystickBase, TMP_FontAsset font)
    {
        Instance = this;
        container = drivingGroup; handbrake = handbrakeButton; speedo = speedometer; joystick = joystickBase;
        vehicles = FindAnyObjectByType<VehicleSystem>();
        canvas = container.GetComponentInParent<Canvas>();
        if (handbrake != null) { handbrakePos = handbrake.anchoredPosition; handbrakeSize = handbrake.sizeDelta; handbrakeAnchorMin = handbrake.anchorMin; handbrakeAnchorMax = handbrake.anchorMax; }
        if (speedo != null) { speedoPos = speedo.anchoredPosition; speedoAnchorMin = speedo.anchorMin; speedoAnchorMax = speedo.anchorMax; }

        // the pad: the left side of the screen, behind every button
        padArea = Rect("SteerPad", container);
        padArea.anchorMin = new Vector2(0f, 0f); padArea.anchorMax = new Vector2(0.45f, 0.72f);
        padArea.offsetMin = padArea.offsetMax = Vector2.zero;
        padArea.SetAsFirstSibling();
        Image pi = padArea.gameObject.AddComponent<Image>(); pi.color = new Color(1f, 1f, 1f, 0.001f);       // invisible, still touchable
        PadInput pad = padArea.gameObject.AddComponent<PadInput>(); pad.owner = this;
        track = Rect("Track", padArea);
        track.anchorMin = track.anchorMax = new Vector2(0f, 0f); track.pivot = new Vector2(0.5f, 0.5f);
        track.anchoredPosition = new Vector2(250f, 150f); track.sizeDelta = new Vector2(FullLock * 2f + 90f, 22f);
        Image ti = track.gameObject.AddComponent<Image>(); ti.sprite = LundunHudArt.Pill; ti.type = Image.Type.Sliced; ti.pixelsPerUnitMultiplier = 3f; ti.color = new Color(0f, 0f, 0f, 0.35f); ti.raycastTarget = false;
        knob = Rect("Knob", track);
        knob.sizeDelta = new Vector2(96f, 96f);
        Image ki = knob.gameObject.AddComponent<Image>(); ki.sprite = LundunHudArt.Disc; ki.preserveAspect = true; ki.color = new Color(1f, 1f, 1f, 0.8f); ki.raycastTarget = false;
        Image kr = Rect("Rim", knob).gameObject.AddComponent<Image>(); Stretch((RectTransform)kr.transform); kr.sprite = LundunHudArt.Ring; kr.color = new Color(0f, 0f, 0f, 0.25f); kr.raycastTarget = false;
        Text(track, "STEER", 20f, new Vector2(0f, -52f), font).color = new Color(1f, 1f, 1f, 0.45f);

        // arrows (ARROWS mode)
        arrows = Rect("SteerArrows", container);
        arrows.anchorMin = arrows.anchorMax = new Vector2(0f, 0f); arrows.pivot = new Vector2(0f, 0f);
        arrows.anchoredPosition = new Vector2(70f, 70f); arrows.sizeDelta = new Vector2(400f, 180f);
        Round(arrows, "Left", LundunHudArt.Left, new Vector2(80f, 80f), 160f, v => leftHeld = v);
        Round(arrows, "Right", LundunHudArt.Right, new Vector2(300f, 80f), 160f, v => rightHeld = v);

        // the pedals
        pedals = Rect("Pedals", container);
        pedals.anchorMin = pedals.anchorMax = new Vector2(1f, 0f); pedals.pivot = new Vector2(1f, 0f);
        pedals.anchoredPosition = new Vector2(-50f, 50f); pedals.sizeDelta = new Vector2(440f, 330f);
        Pedal(pedals, "GasPedal", "GAS", LundunHudArt.Gas, new Vector2(-105f, 165f), new Vector2(190f, 310f), v => gasHeld = v, font);
        Pedal(pedals, "BrakePedal", "BRAKE", LundunHudArt.Brake, new Vector2(-325f, 95f), new Vector2(190f, 170f), v => brakeHeld = v, font);
        Apply();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        Steer = Mathf.Clamp(Steer, -1f, 1f);
        float target = padFinger >= 0 ? Steer : (leftHeld ? -1f : 0f) + (rightHeld ? 1f : 0f);
        if (padFinger < 0) Steer = target;
        Gas = gasHeld ? 1f : 0f;
        Brake = brakeHeld ? 1f : 0f;
        if (knob != null) knob.anchoredPosition = new Vector2(Steer * FullLock, 0f);

        bool driving = vehicles != null && vehicles.IsDriving;
        if (VehicleSystem.SteeringMode != shownMode || driving != shownDriving) Apply();
    }

    /// Shows the controls for the steering chosen, and moves the handbrake and speedometer to suit.
    void Apply()
    {
        shownMode = VehicleSystem.SteeringMode;
        shownDriving = vehicles != null && vehicles.IsDriving;
        bool pads = shownMode != VehicleSystem.Steering.Point;
        if (padArea != null) padArea.gameObject.SetActive(pads && shownMode == VehicleSystem.Steering.Pad);
        if (arrows != null) arrows.gameObject.SetActive(pads && shownMode == VehicleSystem.Steering.Arrows);
        if (pedals != null) pedals.gameObject.SetActive(pads);
        if (!pads) { padFinger = -1; gasHeld = brakeHeld = leftHeld = rightHeld = false; Steer = 0f; }
        // the joystick: on foot always; driving only for POINT
        if (joystick != null) joystick.gameObject.SetActive(!shownDriving || !pads);
        if (handbrake != null)
        {
            handbrake.anchorMin = pads ? new Vector2(1f, 0f) : handbrakeAnchorMin;
            handbrake.anchorMax = pads ? new Vector2(1f, 0f) : handbrakeAnchorMax;
            handbrake.anchoredPosition = pads ? new Vector2(-50f - 325f, 50f + 95f + 85f + 90f) : handbrakePos;
            handbrake.sizeDelta = pads ? new Vector2(130f, 130f) : handbrakeSize;
        }
        if (speedo != null)
        {
            speedo.anchorMin = pads ? new Vector2(1f, 0f) : speedoAnchorMin;
            speedo.anchorMax = pads ? new Vector2(1f, 0f) : speedoAnchorMax;
            speedo.anchoredPosition = pads ? new Vector2(-50f - 105f, 50f + 330f + 20f) : speedoPos;
        }
    }

    // ------------------------------------------------------------------ the pad
    internal void PadDown(PointerEventData e)
    {
        if (padFinger >= 0) return;
        padFinger = e.pointerId;
        padOrigin = ToCanvas(e.position).x;
        Steer = 0f;
        LundunHaptics.Light();
    }

    internal void PadDrag(PointerEventData e)
    {
        if (e.pointerId != padFinger) return;
        float dx = ToCanvas(e.position).x - padOrigin;
        if (Mathf.Abs(dx) < 6f) dx = 0f;
        // past full lock, the start point follows the thumb, so coming back steers straight away
        if (dx > FullLock) { padOrigin += dx - FullLock; dx = FullLock; }
        if (dx < -FullLock) { padOrigin += dx + FullLock; dx = -FullLock; }
        Steer = dx / FullLock;
    }

    internal void PadUp(PointerEventData e)
    {
        if (e.pointerId != padFinger) return;
        padFinger = -1;
        Steer = 0f;
    }

    float ScaleFactor() { return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f; }
    Vector2 ToCanvas(Vector2 screen) { return screen / ScaleFactor(); }

    /// Lets go of everything (a cutscene, leaving the app).
    public void Release() { padFinger = -1; gasHeld = brakeHeld = leftHeld = rightHeld = false; Steer = 0f; }
    void OnApplicationPause(bool paused) { if (paused) Release(); }

    // ------------------------------------------------------------------ building
    static RectTransform Rect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static TextMeshProUGUI Text(RectTransform parent, string text, float size, Vector2 at, TMP_FontAsset font)
    {
        RectTransform r = Rect("Text", parent);
        r.anchoredPosition = at; r.sizeDelta = new Vector2(220f, size * 1.4f);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
        t.characterSpacing = 6f; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    void Round(RectTransform parent, string name, Sprite icon, Vector2 at, float size, System.Action<bool> held)
    {
        RectTransform b = Rect(name, parent);
        b.anchorMin = b.anchorMax = new Vector2(0f, 0f); b.anchoredPosition = at; b.sizeDelta = new Vector2(size, size);
        Image img = b.gameObject.AddComponent<Image>(); img.sprite = LundunHudArt.Disc; img.preserveAspect = true; img.color = new Color(0.05f, 0.07f, 0.12f, 0.62f);
        Image rim = Rect("Rim", b).gameObject.AddComponent<Image>(); Stretch((RectTransform)rim.transform); rim.sprite = LundunHudArt.Ring; rim.color = new Color(1f, 1f, 1f, 0.35f); rim.raycastTarget = false;
        RectTransform ic = Rect("Icon", b); ic.sizeDelta = new Vector2(size * 0.42f, size * 0.42f);
        Image ii = ic.gameObject.AddComponent<Image>(); ii.sprite = icon; ii.preserveAspect = true; ii.raycastTarget = false;
        b.gameObject.AddComponent<HoldInput>().changed = held;
        b.gameObject.AddComponent<LundunPressFX>();
    }

    void Pedal(RectTransform parent, string name, string label, Sprite icon, Vector2 centre, Vector2 size, System.Action<bool> held, TMP_FontAsset font)
    {
        RectTransform b = Rect(name, parent);
        b.anchorMin = b.anchorMax = new Vector2(1f, 0f); b.anchoredPosition = centre; b.sizeDelta = size;
        Image img = b.gameObject.AddComponent<Image>(); img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.9f;
        img.color = new Color(0.05f, 0.07f, 0.12f, 0.62f);
        Image rim = Rect("Rim", b).gameObject.AddComponent<Image>(); Stretch((RectTransform)rim.transform);
        rim.sprite = LundunHudArt.Pill; rim.type = Image.Type.Sliced; rim.pixelsPerUnitMultiplier = 0.9f; rim.color = new Color(1f, 1f, 1f, 0.12f); rim.raycastTarget = false;
        RectTransform ic = Rect("Icon", b); ic.anchoredPosition = new Vector2(0f, 14f); ic.sizeDelta = new Vector2(size.x * 0.45f, size.x * 0.45f);
        Image ii = ic.gameObject.AddComponent<Image>(); ii.sprite = icon; ii.preserveAspect = true; ii.raycastTarget = false;
        Text(b, label, 22f, new Vector2(0f, -size.y * 0.5f + 26f), font).color = new Color(1f, 1f, 1f, 0.7f);
        HoldInput h = b.gameObject.AddComponent<HoldInput>();
        h.changed = v => { held(v); img.color = v ? new Color(1f, 0.62f, 0.15f, 0.75f) : new Color(0.05f, 0.07f, 0.12f, 0.62f); };
        b.gameObject.AddComponent<LundunPressFX>();
    }

    // ------------------------------------------------------------------ touch handlers
    class PadInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        public LundunDriveControls owner;
        public void OnInitializePotentialDrag(PointerEventData e) { e.useDragThreshold = false; }     // no dead zone before it moves
        public void OnPointerDown(PointerEventData e) { owner.PadDown(e); }
        public void OnDrag(PointerEventData e) { owner.PadDrag(e); }
        public void OnPointerUp(PointerEventData e) { owner.PadUp(e); }
    }

    class HoldInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public System.Action<bool> changed;
        int finger = -1;
        public void OnPointerDown(PointerEventData e) { if (finger >= 0) return; finger = e.pointerId; if (changed != null) changed(true); LundunHaptics.Light(); }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId != finger) return; finger = -1; if (changed != null) changed(false); }
        void OnDisable() { if (finger >= 0 && changed != null) changed(false); finger = -1; }
    }
}
