using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private AnimationCurve speedCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 4f),
        new Keyframe(0.85f, 8f),
        new Keyframe(1f, 14f));

    [SerializeField] private float sprintThreshold = 0.85f;
    [SerializeField] private float keyboardWalkStrength = 0.6f;
    [SerializeField] private float turnSpeed = 1080f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpHeight = 1.5f;

    private CharacterController controller;
    private InputAction moveAction;
    private InputAction sprintAction;
    private InputAction jumpAction;
    private Transform cam;
    private float verticalVelocity;

    public bool IsSprinting { get; private set; }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        moveAction = InputSystem.actions.FindAction("Move");
        sprintAction = InputSystem.actions.FindAction("Sprint");
        jumpAction = InputSystem.actions.FindAction("Jump");
        cam = Camera.main.transform;
    }

    void Update()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        float strength = Mathf.Clamp01(input.magnitude);

        bool usingKeyboard = moveAction.activeControl != null
                          && moveAction.activeControl.device is Keyboard;

        if (usingKeyboard && strength > 0f)
        {
            strength = sprintAction.IsPressed() ? 1f : keyboardWalkStrength;
        }

        float speed = speedCurve.Evaluate(strength);
        IsSprinting = strength >= sprintThreshold;

        Vector3 camForward = cam.forward;
        camForward.y = 0f;
        camForward.Normalize();

        Vector3 camRight = cam.right;
        camRight.y = 0f;
        camRight.Normalize();

        Quaternion targetRotation = Quaternion.LookRotation(camForward);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRotation, turnSpeed * Time.deltaTime);

        Vector3 moveDirection = (camForward * input.y + camRight * input.x).normalized;
        Vector3 move = moveDirection * speed;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        // Jump: only when standing on something
        if (controller.isGrounded && jumpAction.WasPressedThisFrame())
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);
    }
}