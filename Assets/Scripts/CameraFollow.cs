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

    private InputAction lookAction;
    private Camera cam;
    private PlayerController player;
    private float yaw;
    private float pitch = 10f;

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

        // Widen the view while sprinting so speed feels fast
        float targetFov = (player != null && player.IsSprinting) ? sprintFov : normalFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, fovChangeSpeed * Time.deltaTime);
    }
}