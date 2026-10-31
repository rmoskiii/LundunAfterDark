using UnityEngine;

public class VehicleSystem : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private GameObject player;
    [SerializeField] private CameraFollow cameraFollow;
    [SerializeField] private float enterRange = 3.5f;

    [Header("UI groups")]
    [SerializeField] private GameObject onFootControls;
    [SerializeField] private GameObject drivingControls;
    [SerializeField] private GameObject enterButton;

    [Header("Driving buttons")]
    [SerializeField] private HoldButton gasButton;
    [SerializeField] private HoldButton brakeButton;
    [SerializeField] private HoldButton leftButton;
    [SerializeField] private HoldButton rightButton;

    [Header("V1: point-to-steer")]
    [Tooltip("Drive with the joystick: push the way you want to go and the car steers itself there; push further to go " +
             "faster; pull back to brake, then reverse. The Brake button becomes the HANDBRAKE. Untick for the old four buttons")]
    [SerializeField] private bool pointToSteer = true;
    [Tooltip("The stick this many degrees off the way the car is pointing = full steering lock")]
    [SerializeField] private float fullLockAngle = 32f;

    /// V1: the joystick lives here while point-to-steer is on (LundunHud moves it out of the on-foot controls), so it
    /// shows both on foot and driving.
    public static GameObject SharedControls;
    public bool PointToSteer => pointToSteer;

    // ------------------------------------------------------------------ L1: how James steers (Settings → CONTROLS → STEERING)
    /// PAD: drag your left thumb sideways to steer (more drag, more lock), GAS and BRAKE pedals on the right.
    /// ARROWS: left/right buttons and the same pedals. POINT: the joystick points where to go (V1).
    public enum Steering { Pad, Arrows, Point }
    const string SteeringKey = "Lundun.Steering", DriveInfoKey = "Lundun.DriveInfo";
    static bool steeringLoaded;
    static Steering steering = Steering.Pad;
    public static Steering SteeringMode
    {
        get { if (!steeringLoaded) { steering = (Steering)Mathf.Clamp(PlayerPrefs.GetInt(SteeringKey, 0), 0, 2); steeringLoaded = true; } return steering; }
        set { steering = value; steeringLoaded = true; PlayerPrefs.SetInt(SteeringKey, (int)value); PlayerPrefs.Save(); }
    }
    /// L1: the driving readout (Settings → DISPLAY → DRIVE INFO): for finding why a car won't drive.
    public static bool ShowDriveInfo
    {
        get { return PlayerPrefs.GetInt(DriveInfoKey, 0) == 1; }
        set { PlayerPrefs.SetInt(DriveInfoKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }
    private Vector2 sentNow;
    private UnityEngine.InputSystem.InputAction moveAction;

    private CarController nearbyCar;
    private CarController currentCar;
    private bool controlsLocked;

    // True while BUSTED / WASTED is playing (police freeze, nobody shoots)
    public static bool Locked { get; private set; }

    // Fired after BUSTED / WASTED put the player back at the police station / hospital (the clock jumps an hour)
    public static event System.Action Respawned;

    // Fired just before James gets into a car (traffic cars use it: the driver gets pulled out)
    public static event System.Action<CarController> Entering;
    public CarController CurrentCar => currentCar;

    // Whatever the police should chase: James on foot, or the car he's driving
    public Transform PlayerTarget => currentCar != null ? currentCar.transform : player.transform;

    public bool IsDriving => currentCar != null;
    /// The car close enough to get into right now (null if none). Read by the HUD for its ENTER / STEAL label.
    public CarController NearbyCar => nearbyCar;

    // How fast the player is going, on foot or in a car
    public float PlayerSpeed
    {
        get
        {
            if (currentCar != null) return Mathf.Abs(currentCar.Speed);
            Vector3 v = player.GetComponent<CharacterController>().velocity;
            v.y = 0f;
            return v.magnitude;
        }
    }

    void Start()
    {
        Locked = false;
        foreach (CarController car in CarController.All) car.IsDriven = false;
        ShowDrivingUI(false);
    }

    void Update()
    {
        if (controlsLocked) return;

        if (currentCar != null)
        {
            if (pointToSteer)
            {
                if (SteeringMode == Steering.Point) SteerByStick(); else DriveByPads();
                return;
            }
            // Turn held buttons into steering and throttle
            float throttle = (gasButton.IsHeld ? 1f : 0f) - (brakeButton.IsHeld ? 1f : 0f);
            float steer = (rightButton.IsHeld ? 1f : 0f) - (leftButton.IsHeld ? 1f : 0f);
            currentCar.SetTouchInput(throttle, steer);
            return;
        }

        // On foot: is there a car close enough to get into?
        nearbyCar = null;
        float closest = enterRange;
        foreach (CarController car in CarController.All)    // includes traffic and parked cars
        {
            if (car == null || !car.enabled) continue;
            if (car.GetComponent<PoliceAI>() != null) continue;   // can't steal police cars (yet)
            float d = Vector3.Distance(player.transform.position, car.transform.position);
            if (d < closest) { closest = d; nearbyCar = car; }
        }
        enterButton.SetActive(nearbyCar != null);
    }

    // ------------------------------------------------------------------ V1: point-to-steer
    /// The stick says where to go, on screen: up = the way the camera looks. The car steers towards that direction
    /// (full lock when it's fullLockAngle or more off), with less gas the sharper the turn. Pointing behind the car:
    /// brakes, and once nearly stopped, reverses (steering the back of the car towards it). Nothing pushed: it rolls.
    void SteerByStick()
    {
        if (moveAction == null) moveAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Move");
        Vector2 stick = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        float push = Mathf.Clamp01(stick.magnitude);
        float throttle = 0f, steer = 0f;
        Transform cam = Camera.main != null ? Camera.main.transform : null;
        if (push > 0.12f && cam != null)
        {
            Vector3 fwd = cam.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = cam.right; right.y = 0f; right.Normalize();
            Vector3 want = fwd * stick.y + right * stick.x;
            Vector3 carFwd = currentCar.transform.forward; carFwd.y = 0f; carFwd.Normalize();
            float angle = Vector3.SignedAngle(carFwd, want, Vector3.up);
            float speed = currentCar.Speed;
            if (Mathf.Abs(angle) <= 115f)
            {
                steer = Mathf.Clamp(angle / fullLockAngle, -1f, 1f);
                throttle = push * Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(50f, 115f, Mathf.Abs(angle)));
            }
            else if (speed > 1.5f) throttle = -push;                                  // behind you: brake
            else
            {
                float back = Vector3.SignedAngle(-carFwd, want, Vector3.up);         // reverse, the back of the car leading
                steer = -Mathf.Clamp(back / fullLockAngle, -1f, 1f);
                throttle = -push;
            }
        }
        currentCar.SetTouchInput(throttle, steer);
        currentCar.SetHandbrake(brakeButton != null && brakeButton.IsHeld);      // the Brake button is the HANDBRAKE now
    }

    // ------------------------------------------------------------------ L1: steering pad / arrows + pedals
    /// The pad (or arrows) steer, the GAS and BRAKE pedals drive: BRAKE slows, and once stopped it reverses.
    /// On a keyboard (the editor), WASD / arrows do the same when nothing's touched.
    void DriveByPads()
    {
        LundunDriveControls dc = LundunDriveControls.Instance;
        float steer = dc != null ? dc.Steer : 0f;
        float throttle = dc != null ? dc.Gas - dc.Brake : 0f;
        if (moveAction == null) moveAction = UnityEngine.InputSystem.InputSystem.actions.FindAction("Move");
        if (moveAction != null && (dc == null || !dc.Touched))
        {
            Vector2 k = moveAction.ReadValue<Vector2>();
            if (Mathf.Abs(steer) < 0.01f) steer = k.x;
            if (Mathf.Abs(throttle) < 0.01f) throttle = k.y;
        }
        sentNow = new Vector2(steer, throttle);
        currentCar.SetTouchInput(throttle, steer);
        currentCar.SetHandbrake(brakeButton != null && brakeButton.IsHeld);
    }

    // ------------------------------------------------------------------ L1: the driving readout
    void OnGUI()
    {
        if (currentCar == null || !ShowDriveInfo) return;
        CarController c = currentCar;
        Rigidbody rb = c.GetComponent<Rigidbody>();
        LundunCarDamage dmg = c.GetComponent<LundunCarDamage>();
        string touching = "";
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null)
        {
            System.Collections.Generic.List<Vector2> fingers = new System.Collections.Generic.List<Vector2>();
            UnityEngine.InputSystem.Touchscreen ts = UnityEngine.InputSystem.Touchscreen.current;
            if (ts != null)
                foreach (UnityEngine.InputSystem.Controls.TouchControl t in ts.touches)
                    if (t.press.isPressed) fingers.Add(t.position.ReadValue());
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            if (fingers.Count == 0 && mouse != null && mouse.leftButton.isPressed) fingers.Add(mouse.position.ReadValue());
            foreach (Vector2 f in fingers)
            {
                System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                UnityEngine.EventSystems.PointerEventData pe = new UnityEngine.EventSystems.PointerEventData(es) { position = f };
                es.RaycastAll(pe, hits);
                touching += "  finger→" + (hits.Count > 0 ? hits[0].gameObject.name : "nothing");
            }
        }
        string text = c.name + (c.enabled ? "" : " (SWITCHED OFF)") + "   driven " + c.IsDriven + "   locked " + controlsLocked + "   steering " + SteeringMode +
                      "\nsent steer/gas " + V(sentNow) + "   car used " + V(c.InputNow) +
                      "\nwheels on road " + c.GroundedWheels + "/4   speed " + (c.Speed * 2.237f).ToString("0") + " mph   handbrake " + c.Handbraking +
                      (rb != null ? "\nbody: kinematic " + rb.isKinematic + "  frozen " + rb.constraints + "  asleep " + rb.IsSleeping() + "  y " + c.transform.position.y.ToString("0.00") : "") +
                      (dmg != null ? "\ntouching: " + dmg.Touching : "") +
                      "\ntime scale " + Time.timeScale + touching;
        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(Screen.height / 40f), alignment = TextAnchor.UpperLeft, wordWrap = true };
        st.normal.textColor = Color.white;
        GUI.Box(new Rect(Screen.width * 0.2f, Screen.height * 0.12f, Screen.width * 0.6f, Screen.height * 0.26f), text, st);
    }

    static string V(Vector2 v) { return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ")"; }

    // Hooked up to the 🚗 button's On Click
    public void EnterCar()
    {
        if (nearbyCar == null || controlsLocked) return;

        if (Entering != null) Entering(nearbyCar);
        currentCar = nearbyCar;
        player.SetActive(false);                    // James "gets in"
        currentCar.IsDriven = true;
        currentCar.ReadsMoveAction = !pointToSteer;            // V1: the stick is worked out here instead
        cameraFollow.SetTarget(currentCar.transform, true);
        ShowDrivingUI(true);
    }

    // Hooked up to the exit button's On Click
    public void ExitCar()
    {
        if (currentCar == null) return;

        // UK cars: driver gets out on the right
        Transform car = currentCar.transform;
        player.transform.position = car.position + car.right * 1.8f + Vector3.up * 0.5f;
        player.transform.rotation = Quaternion.Euler(0f, car.eulerAngles.y, 0f);

        currentCar.SetTouchInput(0f, 0f);
        currentCar.SetHandbrake(false);
        currentCar.ReadsMoveAction = true;
        currentCar.IsDriven = false;
        player.SetActive(true);
        cameraFollow.SetTarget(player.transform, false);

        currentCar = null;
        ShowDrivingUI(false);
    }

    // Freeze the player during cutscenes like BUSTED
    public void LockControls(bool locked)
    {
        controlsLocked = locked;
        Locked = locked;
        onFootControls.SetActive(!locked && currentCar == null);
        drivingControls.SetActive(!locked && currentCar != null);
        if (SharedControls != null) SharedControls.SetActive(!locked);
        enterButton.SetActive(false);

        player.GetComponent<PlayerController>().enabled = !locked;
        player.GetComponent<PlayerCombat>().enabled = !locked;

        if (currentCar != null)
        {
            currentCar.SetTouchInput(0f, 0f);
            currentCar.SetHandbrake(false);
            currentCar.IsDriven = !locked;
        }
    }

    // Get out of any car and appear somewhere else
    public void RespawnPlayer(Vector3 position, Quaternion rotation)
    {
        if (currentCar != null) ExitCar();

        // A CharacterController ignores teleports unless you switch it off first
        CharacterController cc = player.GetComponent<CharacterController>();
        cc.enabled = false;
        player.transform.SetPositionAndRotation(position, rotation);
        cc.enabled = true;
        if (Respawned != null) Respawned();
    }

    void ShowDrivingUI(bool driving)
    {
        onFootControls.SetActive(!driving);
        drivingControls.SetActive(driving);
        enterButton.SetActive(false);
    }
}