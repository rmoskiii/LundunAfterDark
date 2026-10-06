using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private TouchLookArea touchLook;
    [SerializeField] private bool useMouseLook = false;

    [SerializeField] private float distance = 6.5f;
    [SerializeField] private float height = 1.8f;
    [SerializeField] private float shoulderOffset = 0.7f;
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float touchSensitivity = 0.2f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    [SerializeField] private float normalFov = 60f;
    [SerializeField] private float sprintFov = 75f;
    [SerializeField] private float fovChangeSpeed = 6f;

    [Header("Shake")]
    [SerializeField] private float shakeDecay = 15f;

    private InputAction lookAction;
    private Camera cam;
    private PlayerController player;
    private float yaw;
    private float pitch = 10f;
    private float shake;

    void Start()
    {
        lookAction = InputSystem.actions.FindAction("Look");
        cam = GetComponent<Camera>();

        if (useMouseLook) Cursor.lockState = CursorLockMode.Locked;

        if (target != null)
        {
            yaw = target.eulerAngles.y;
            player = target.GetComponent<PlayerController>();
        }
    }

    // Called by the gun each shot: nudges the aim up and slightly sideways
    public void AddRecoil(float up, float sideways)
    {
        pitch -= up;
        yaw += sideways;
    }

    public void AddShake(float amount)
    {
        shake = Mathf.Max(shake, amount);
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 look = Vector2.zero;

        if (useMouseLook)
        {
            look += lookAction.ReadValue<Vector2>() * mouseSensitivity;
        }

        if (touchLook != null)
        {
            look += touchLook.ConsumeDelta() * touchSensitivity;
        }

        yaw += look.x;
        pitch -= look.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = target.position
                           + Vector3.up * height
                           + rotation * Vector3.right * shoulderOffset;

        transform.position = focusPoint - rotation * Vector3.forward * distance;
        transform.rotation = rotation;

        // Screen shake: jiggle the camera, then let it settle
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