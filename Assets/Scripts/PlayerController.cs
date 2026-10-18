using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Walk and run")]
    [Tooltip("Walking pace (m/s), for a small push of the joystick")]
    [SerializeField] private float walkSpeed = 1.8f;
    [Tooltip("Full running pace (m/s), joystick pushed right out")]
    [SerializeField] private float runSpeed = 7.5f;
    [Tooltip("Joystick pushed up to this much (0-1) = walking")]
    [Range(0.1f, 0.9f)] [SerializeField] private float walkZone = 0.55f;
    [Tooltip("Joystick pushed this much (0-1) or more = full run. Between the two, walk speeds up into the run")]
    [Range(0.2f, 1f)] [SerializeField] private float runZone = 0.8f;
    [Tooltip("Ignore tiny joystick wobbles below this")]
    [SerializeField] private float stickDeadzone = 0.08f;
    [Tooltip("How far the on-screen joystick knob travels (pixels). More travel = easier to hold a walk. 0 = leave as set on the joystick")]
    [SerializeField] private float joystickTravel = 130f;
    [SerializeField] private float turnSpeed = 1080f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpHeight = 1.5f;

    [Header("Jump feel (R6)")]
    [Tooltip("Can still jump this long after running off an edge")]
    [SerializeField] private float coyoteTime = 0.12f;
    [Tooltip("A jump pressed this long before landing still happens")]
    [SerializeField] private float jumpBuffer = 0.15f;
    [Tooltip("How much you can steer in the air (0 = none, 1 = like on the ground)")]
    [Range(0f, 1f)] [SerializeField] private float airControl = 0.3f;
    [Tooltip("Faster than this when you jump = running jump")]
    [SerializeField] private float runJumpSpeed = 6f;
    [Tooltip("Stays stuck to the ground stepping off kerbs up to this drop (m)")]
    [SerializeField] private float groundSnap = 0.45f;

    [Header("Landing")]
    [SerializeField] private float hardLandingFrom = 2.5f;     // metres fallen
    [SerializeField] private float rollFrom = 5f;
    [SerializeField] private float hurtFrom = 9f;
    [SerializeField] private float damagePerMetre = 12f;
    [SerializeField] private float hardLandingPause = 0.45f;
    [SerializeField] private float rollTime = 0.8f;

    private CharacterController controller;
    private InputAction moveAction;
    private InputAction sprintAction;
    private InputAction jumpAction;
    private Transform cam;
    private float verticalVelocity;

    private float lastGroundedTime = -99f, jumpPressedTime = -99f, lockUntil, peakY;
    private bool jumping, rolling, wasGrounded;
    private Vector3 airVelocity, rollVelocity;
    private int groundMask;

    public bool IsSprinting { get; private set; }
    /// A2: while aiming, James can't go faster than this (m/s). 0 = no cap.
    public float SpeedCap { get; set; }
    /// In the air (jumped, or walked off something), until landing.
    public bool Airborne { get; private set; }
    public bool RunningJump { get; private set; }
    public float VerticalVelocity { get { return verticalVelocity; } }
    /// Fired at take-off (true = running jump).
    public event System.Action<bool> Jumped;
    /// Fired on landing: 0 = soft, 1 = hard, 2 = roll; plus metres fallen.
    public event System.Action<int, float> Landed;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        moveAction = InputSystem.actions.FindAction("Move");
        sprintAction = InputSystem.actions.FindAction("Sprint");
        jumpAction = InputSystem.actions.FindAction("Jump");
        cam = Camera.main.transform;
        if (joystickTravel > 0f)
            foreach (OnScreenStick stick in FindObjectsByType<OnScreenStick>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                stick.movementRange = joystickTravel;
        groundMask = ~((1 << gameObject.layer) | LayerMask.GetMask("NPC"));
    }

    void OnDisable()
    {
        // getting into a car / cutscenes: forget any jump in progress
        Airborne = false; jumping = false; rolling = false; lockUntil = 0f;
    }

    void Update()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        float strength = Mathf.Clamp01(input.magnitude);

        bool usingKeyboard = moveAction.activeControl != null
                          && moveAction.activeControl.device is Keyboard;

        if (usingKeyboard && strength > 0f)
        {
            // keyboard: walk, hold Shift (Sprint) to run
            strength = sprintAction != null && sprintAction.IsPressed() ? 1f : walkZone * 0.5f;
        }

        float speed = SpeedFor(strength);
        if (SpeedCap > 0f) speed = Mathf.Min(speed, SpeedCap);
        IsSprinting = speed >= runSpeed * 0.95f;

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
        Vector3 wanted = moveDirection * speed;

        bool grounded = controller.isGrounded;
        if (grounded) lastGroundedTime = Time.time;
        if (jumpAction.WasPressedThisFrame()) jumpPressedTime = Time.time;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        // Jump: on the ground (or just left it), not mid-landing; a press slightly early still counts
        bool canJump = !jumping && Time.time - lastGroundedTime <= coyoteTime && Time.time >= lockUntil;
        if (canJump && Time.time - jumpPressedTime <= jumpBuffer)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumping = true;
            jumpPressedTime = -99f;
            Vector3 flat = controller.velocity; flat.y = 0f;
            if (flat.sqrMagnitude < wanted.sqrMagnitude) flat = wanted;
            RunningJump = flat.magnitude >= runJumpSpeed;
            airVelocity = flat;
            StartAir();
            Jumped?.Invoke(RunningJump);
        }
        else if (!Airborne && !grounded && Time.time - lastGroundedTime > coyoteTime)
        {
            // walked off something
            Vector3 flat = controller.velocity; flat.y = 0f;
            airVelocity = flat;
            RunningJump = false;
            StartAir();
        }

        // Horizontal movement: full control on the ground, carried momentum in the air
        Vector3 move;
        if (Airborne)
        {
            airVelocity = Vector3.MoveTowards(airVelocity, wanted, airControl * 25f * Time.deltaTime);
            move = airVelocity;
        }
        else if (rolling && Time.time < lockUntil)
        {
            rollVelocity = Vector3.MoveTowards(rollVelocity, Vector3.zero, 6f * Time.deltaTime);
            move = rollVelocity;
        }
        else if (Time.time < lockUntil) move = wanted * 0.15f;     // knees bent after a heavy landing
        else { rolling = false; move = wanted; }

        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;

        controller.Move(move * Time.deltaTime);
        grounded = controller.isGrounded;

        // stepping down a kerb: stay on the ground instead of a tiny "fall"
        if (!grounded && wasGrounded && !jumping && verticalVelocity <= 0f &&
            Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, groundSnap + 0.1f, groundMask, QueryTriggerInteraction.Ignore))
        {
            controller.Move(Vector3.down * (hit.distance - 0.1f + 0.02f));
            grounded = controller.isGrounded;
            if (grounded) { lastGroundedTime = Time.time; Airborne = false; }
        }

        if (Airborne)
        {
            peakY = Mathf.Max(peakY, transform.position.y);
            if (grounded && verticalVelocity <= 0f) Land();
        }
        wasGrounded = grounded;
    }

    /// Joystick push (0-1) → speed: a small push walks, pushing further speeds up into a run.
    float SpeedFor(float strength)
    {
        if (strength < stickDeadzone) return 0f;
        if (strength <= walkZone) return walkSpeed;
        if (strength >= runZone) return runSpeed;
        float t = (strength - walkZone) / Mathf.Max(0.01f, runZone - walkZone);
        return Mathf.Lerp(walkSpeed, runSpeed, t * t * (3f - 2f * t));
    }

    void StartAir()
    {
        Airborne = true;
        peakY = transform.position.y;
    }

    void Land()
    {
        float fall = Mathf.Max(0f, peakY - transform.position.y);
        Airborne = false;
        jumping = false;
        int kind = fall >= rollFrom ? 2 : fall >= hardLandingFrom ? 1 : 0;
        if (kind == 2)
        {
            // keep going: roll out of it in the direction you were travelling
            rolling = true;
            Vector3 flat = airVelocity; flat.y = 0f;
            rollVelocity = flat.sqrMagnitude > 1f ? flat.normalized * Mathf.Min(flat.magnitude, 6f) : transform.forward * 3f;
            lockUntil = Time.time + rollTime;
        }
        else if (kind == 1) lockUntil = Time.time + hardLandingPause;

        if (fall >= hurtFrom && PlayerHealth.Instance != null)
            PlayerHealth.Instance.TakeDamage((fall - hurtFrom + 1f) * damagePerMetre * (kind == 2 ? 0.6f : 1f));

        Landed?.Invoke(kind, fall);
    }
}
