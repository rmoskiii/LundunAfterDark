using UnityEngine;
using UnityEngine.InputSystem;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float distance = 4.5f;
    [SerializeField] private float height = 1.6f;
    [SerializeField] private float shoulderOffset = 0.7f;
    [SerializeField] private float sensitivity = 0.15f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;
    [SerializeField] private bool lockCursor = true;

    private InputAction lookAction;
    private float yaw;
    private float pitch = 10f;

    void Start()
    {
        lookAction = InputSystem.actions.FindAction("Look");
        if (lockCursor) Cursor.lockState = CursorLockMode.Locked;
        yaw = target != null ? target.eulerAngles.y : 0f;
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 look = lookAction.ReadValue<Vector2>();
        yaw += look.x * sensitivity;
        pitch -= look.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = target.position
                           + Vector3.up * height
                           + rotation * Vector3.right * shoulderOffset;

        transform.position = focusPoint - rotation * Vector3.forward * distance;
        transform.rotation = rotation;
    }
}