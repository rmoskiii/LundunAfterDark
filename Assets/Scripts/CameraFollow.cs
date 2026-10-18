using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private TouchLookArea touchLook;
    [Tooltip("For editor testing. Always off on a real phone.")]
    [SerializeField] private bool useMouseLook = false;

    [Header("On foot")]
    [SerializeField] private float distance = 3.7f;
    [SerializeField] private float height = 0.7f;
    [SerializeField] private float shoulderOffset = 0.6f;

    [Header("In a vehicle")]
    [SerializeField] private float vehicleDistance = 7f;
    [SerializeField] private float vehicleHeight = 1.6f;
    [SerializeField] private float vehicleFollowSpeed = 3f;   // how fast it swings back behind the car
    [SerializeField] private float lookIdleTime = 1f;          // seconds after you stop dragging before it swings back

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float touchSensitivity = 0.2f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Turning response (A1)")]
    [Tooltip("Slow drags turn a little less (precise); quick flicks turn more")]
    [SerializeField] private bool lookAcceleration = true;
    [SerializeField] private float slowDragGain = 0.85f;
    [SerializeField] private float fastDragGain = 1.5f;
    [Tooltip("Drag speed (screen heights per second) where slow ends and fast begins")]
    [SerializeField] private Vector2 dragSpeedRange = new Vector2(0.4f, 2.5f);

    [Header("Recoil recovery (A1)")]
    [Tooltip("How quickly the aim settles back after the kick (per second)")]
    [SerializeField] private float recoilRecovery = 7f;
    [Tooltip("Pause after a shot before it starts settling")]
    [SerializeField] private float recoilRecoveryDelay = 0.06f;

    [Header("Aiming in (A2: the AIM button)")]
    [SerializeField] private float aimDistance = 1.7f;
    [SerializeField] private float aimHeight = 0.58f;
    [SerializeField] private float aimShoulder = 0.72f;
    [SerializeField] private float aimFov = 40f;

    [Header("Field of view")]
    [SerializeField] private float normalFov = 60f;
    [SerializeField] private float sprintFov = 75f;
    [SerializeField] private float fovChangeSpeed = 6f;

    [Header("Shake")]
    [SerializeField] private float shakeDecay = 15f;

    private InputAction lookAction;
    private Camera cam;
    private PlayerController player;
    private bool mouseLookActive;
    private bool vehicleMode;
    private float yaw;
    private float pitch = 10f;
    private float shake;
    private float lastLookTime;
    private float currentDistance;
    private float currentHeight;
    private float currentShoulder;
    private Vector2 recoilDebt;            // (yaw, pitch) kick that hasn't settled back yet
    private float lastRecoilTime = -99f;
    private float dragGain = 1f;
    private LundunAimAssist assist;

    public float Yaw => yaw;
    public float Pitch => pitch;

    void Start()
    {
        lookAction = InputSystem.actions.FindAction("Look");
        cam = GetComponent<Camera>();

        mouseLookActive = useMouseLook && !Application.isMobilePlatform;
        if (mouseLookActive) Cursor.lockState = CursorLockMode.Locked;

        currentDistance = distance;
        currentHeight = height;
        currentShoulder = shoulderOffset;
        SetTarget(target, false);

        assist = GetComponent<LundunAimAssist>();
        if (assist == null) assist = gameObject.AddComponent<LundunAimAssist>();
    }

    // Called by VehicleSystem when getting in or out
    public void SetTarget(Transform newTarget, bool isVehicle)
    {
        target = newTarget;
        vehicleMode = isVehicle;
        if (target == null) return;

        yaw = target.eulerAngles.y;
        player = target.GetComponent<PlayerController>();
    }

    public void AddRecoil(float up, float sideways)
    {
        pitch -= up; yaw += sideways;
        recoilDebt += new Vector2(sideways, -up);
        lastRecoilTime = Time.time;
    }
    public void AddShake(float amount) { shake = Mathf.Max(shake, amount); }
    public void Nudge(float yawDelta, float pitchDelta) { yaw += yawDelta; pitch += pitchDelta; }

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 look = Vector2.zero;
        if (mouseLookActive) look += lookAction.ReadValue<Vector2>() * mouseSensitivity;
        if (touchLook != null)
        {
            Vector2 drag = touchLook.ConsumeDelta();
            if (lookAcceleration && !vehicleMode)
            {
                float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
                float speed = drag.magnitude / Mathf.Max(1f, Screen.height) / dt;            // screen heights per second
                float want = Mathf.Lerp(slowDragGain, fastDragGain, Mathf.InverseLerp(dragSpeedRange.x, dragSpeedRange.y, speed));
                dragGain = drag.sqrMagnitude > 0f ? Mathf.Lerp(dragGain, want, 0.5f) : 1f;   // eased, so it never jumps mid-drag
                drag *= dragGain;
            }
            look += drag * touchSensitivity * LundunSettings.LookSpeed;      // A3: LOOK SPEED setting
        }

        if (look.sqrMagnitude > 0.0001f) lastLookTime = Time.time;

        // A2: aimed in = calmer look speed
        float aimIn = vehicleMode ? 0f : LundunAim.Eased;
        if (aimIn > 0f) look *= Mathf.Lerp(1f, LundunAim.Sensitivity, aimIn);

        // aim assist: a little stickier while the crosshair is on someone
        if (!vehicleMode && assist != null) look *= assist.LookScale;

        yaw += look.x;
        pitch -= look.y;

        if (!vehicleMode)
        {
            // pulling down against the kick counts toward settling it, so you never over-correct
            if (recoilDebt.y < 0f && look.y < 0f) recoilDebt.y = Mathf.Min(0f, recoilDebt.y - look.y);
            if (Time.time - lastRecoilTime > recoilRecoveryDelay && recoilDebt.sqrMagnitude > 0.000001f)
            {
                Vector2 back = recoilDebt * (1f - Mathf.Exp(-recoilRecovery * Time.deltaTime));
                yaw -= back.x; pitch -= back.y;
                recoilDebt -= back;
            }

            // aim assist: gently keep up with a moving threat while you're aiming or firing
            if (assist != null)
            {
                Vector2 follow = assist.FollowStep(yaw, pitch);
                yaw += follow.x; pitch += follow.y;
            }
        }
        else recoilDebt = Vector2.zero;

        // In a car: if you're not looking around, swing back behind the car
        if (vehicleMode && Time.time - lastLookTime > lookIdleTime)
        {
            yaw = Mathf.LerpAngle(yaw, target.eulerAngles.y, vehicleFollowSpeed * Time.deltaTime);
            pitch = Mathf.Lerp(pitch, 12f, vehicleFollowSpeed * Time.deltaTime);
        }

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // Ease between on-foot and in-car framing
        float step = 5f * Time.deltaTime;
        currentDistance = Mathf.Lerp(currentDistance, vehicleMode ? vehicleDistance : distance, step);
        currentHeight = Mathf.Lerp(currentHeight, vehicleMode ? vehicleHeight : height, step);
        currentShoulder = Mathf.Lerp(currentShoulder, vehicleMode ? 0f : shoulderOffset, step);

        // A2: tighter over the shoulder while aimed in
        float camDistance = Mathf.Lerp(currentDistance, aimDistance, aimIn);
        float camHeight = Mathf.Lerp(currentHeight, aimHeight, aimIn);
        float camShoulder = Mathf.Lerp(currentShoulder, aimShoulder, aimIn);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = target.position
                           + Vector3.up * camHeight
                           + rotation * Vector3.right * camShoulder;

        transform.position = focusPoint - rotation * Vector3.forward * camDistance;
        transform.rotation = rotation;

        if (shake > 0f)
        {
            transform.position += Random.insideUnitSphere * shake;
            shake = Mathf.Lerp(shake, 0f, shakeDecay * Time.deltaTime);
            if (shake < 0.001f) shake = 0f;
        }

        float targetFov = (player != null && player.IsSprinting) ? sprintFov : normalFov;
        if (aimIn > 0f)
        {
            // the aim blend is already eased, so follow it closely
            targetFov = Mathf.Lerp(targetFov, aimFov, aimIn);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 20f * Time.deltaTime);
        }
        else cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, fovChangeSpeed * Time.deltaTime);
    }
}