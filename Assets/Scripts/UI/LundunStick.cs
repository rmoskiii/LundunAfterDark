using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;

/// R1: the on-foot joystick. Replaces Unity's OnScreenStick (left in the scene, switched off by LundunHud, so it's
/// easy to go back), feeding the same virtual gamepad stick, so PlayerController and everything else read it exactly
/// as before. What's different:
///   • your thumb can land anywhere on the base (and a little round it), not only on the small knob
///   • it responds the moment you move (no drag threshold)
///   • it follows its own finger: another finger tapping a button never moves it or lets it go
///   • it always lets go: finger lifted, finger lost (the system took the touch, a swipe off the screen), the HUD
///     hidden (getting into a car, the map, BUSTED / WASTED), the app losing focus or being paused
/// Set up at runtime by LundunHud (no menu to run); movementRange is set by PlayerController's Joystick Travel.
public class LundunStick : OnScreenControl, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    [InputControl(layout = "Vector2")]
    [SerializeField] private string m_ControlPath = "<Gamepad>/leftStick";
    [Tooltip("How far the knob travels (canvas units) for a full push")]
    [SerializeField] private float movementRange = 130f;
    [SerializeField] private RectTransform handle;
    [Tooltip("Extra touch area round the base (canvas units), so a thumb landing just off it still grabs the stick")]
    [SerializeField] private float hitPadding = 50f;

    protected override string controlPathInternal { get => m_ControlPath; set => m_ControlPath = value; }

    public float MovementRange { get => movementRange; set => movementRange = Mathf.Max(1f, value); }
    /// A finger is on the stick right now.
    public bool Held => held;

    private bool held;
    private int pointerId = int.MinValue, touchId;
    private bool mouse;
    private Vector2 downLocal, handleHome;
    private RectTransform area;

    /// LundunHud: the knob to move, the control to feed (the old stick's), and its travel.
    public void Setup(RectTransform knob, string path, float range)
    {
        handle = knob;
        if (handle != null) handleHome = handle.anchoredPosition;
        MovementRange = range;
        if (!string.IsNullOrEmpty(path) && path != controlPath)
        {
            // re-register with the new path cleanly (switch off, change, switch on)
            bool was = enabled;
            enabled = false;
            m_ControlPath = path;
            enabled = was;
        }
        MakeHitArea();
    }

    void Awake()
    {
        if (handle != null) handleHome = handle.anchoredPosition;
    }

    /// An invisible, slightly bigger touch area behind the base (touches on it reach this component).
    void MakeHitArea()
    {
        if (transform.Find("LundunStickHitArea") != null) return;
        GameObject g = new GameObject("LundunStickHitArea", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        g.transform.SetParent(transform, false);
        g.transform.SetAsFirstSibling();
        area = (RectTransform)g.transform;
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(-hitPadding, -hitPadding); area.offsetMax = new Vector2(hitPadding, hitPadding);
        UnityEngine.UI.Image img = g.GetComponent<UnityEngine.UI.Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;
    }

    // ------------------------------------------------------------------ touches
    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = false;          // move at once, not after the finger has slid a few pixels
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (held) return;                            // already following a finger: ignore the second one
        held = true;
        pointerId = eventData.pointerId;
        ExtendedPointerEventData ext = eventData as ExtendedPointerEventData;
        touchId = ext != null ? ext.touchId : 0;
        mouse = ext != null && ext.pointerType == UIPointerType.MouseOrPen && Mouse.current != null && ext.device == Mouse.current;
        downLocal = Local(eventData);
        Move(Vector2.zero);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!held || eventData.pointerId != pointerId) return;
        Move(Local(eventData) - downLocal);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!held || eventData.pointerId != pointerId) return;
        Release();
    }

    Vector2 Local(PointerEventData e)
    {
        RectTransform space = handle != null && handle.parent is RectTransform p ? p : (RectTransform)transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(space, e.position, e.pressEventCamera, out Vector2 local);
        return local;
    }

    void Move(Vector2 offset)
    {
        offset = Vector2.ClampMagnitude(offset, movementRange);
        if (handle != null) handle.anchoredPosition = handleHome + offset;
        SendValueToControl(offset / movementRange);
    }

    /// Let go: knob back to the middle, stick back to zero.
    public void Release()
    {
        bool was = held;
        held = false;
        pointerId = int.MinValue; touchId = 0; mouse = false;
        if (handle != null) handle.anchoredPosition = handleHome;
        if (was || isActiveAndEnabled) SendValueToControl(Vector2.zero);
    }

    // ------------------------------------------------------------------ always let go
    void Update()
    {
        if (held && !FingerStillDown()) Release();
    }

    /// The finger (or mouse button, in the Editor) holding the stick is still pressed. Catches lifts the UI missed.
    bool FingerStillDown()
    {
        if (touchId != 0)
        {
            Touchscreen ts = Touchscreen.current;
            if (ts == null) return false;
            foreach (UnityEngine.InputSystem.Controls.TouchControl t in ts.touches)
                if (t.touchId.ReadValue() == touchId) return t.press.isPressed;
            return false;
        }
        if (mouse) return Mouse.current != null && Mouse.current.leftButton.isPressed;     // the Editor's mouse
        return true;                                                          // anything else: trust the UI's own lift
    }

    void OnApplicationFocus(bool focus) { if (!focus && held) Release(); }
    void OnApplicationPause(bool paused) { if (paused && held) Release(); }

    protected override void OnDisable()
    {
        if (held) Release();
        if (handle != null) handle.anchoredPosition = handleHome;
        base.OnDisable();
    }
}
