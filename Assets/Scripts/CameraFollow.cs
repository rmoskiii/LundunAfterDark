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

    public void AddRecoil(float up, float sideways) { pitch -= up; yaw += sideways; }
    public void AddShake(float amount) { shake = Mathf.Max(shake, amount); }
    public void Nudge(float yawDelta, float pitchDelta) { yaw += yawDelta; pitch += pitchDelta; }

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 look = Vector2.zero;
        if (mouseLookActive) look += lookAction.ReadValue<Vector2>() * mouseSensitivity;
        if (touchLook != null) look += touchLook.ConsumeDelta() * touchSensitivity;

        if (look.sqrMagnitude > 0.0001f) lastLookTime = Time.time;

        yaw += look.x;
        pitch -= look.y;

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

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = target.position
                           + Vector3.up * currentHeight
                           + rotation * Vector3.right * currentShoulder;

        transform.position = focusPoint - rotation * Vector3.forward * currentDistance;
        transform.rotation = rotation;

        if (shake > 0f)
        {
            transform.position += Random.insideUnitSphere * shake;
            shake = Mathf.Lerp(shake, 0f, shakeDecay * Time.deltaTime);
            if (shake < 0.001f) shake = 0f;
        }

        float targetFov = (player != null && player.IsSprinting) ? sprintFov : normalFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, fovChangeSpeed * Time.deltaTime);
    }
}