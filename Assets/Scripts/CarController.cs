using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Suspension")]
    [SerializeField] private Transform[] springPoints;
    [SerializeField] private float restLength = 0.5f;
    [SerializeField] private float wheelRadius = 0.35f;
    [SerializeField] private float springStrength = 20000f;
    [SerializeField] private float springDamper = 3500f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Driving")]
    [SerializeField] private float maxSpeed = 22f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float brakeStrength = 20f;
    [SerializeField] private float maxReverseSpeed = 7f;
    [SerializeField] private float turnSpeed = 90f;
    [SerializeField, Range(0f, 1f)] private float grip = 0.85f;
    [SerializeField] private float coastDrag = 0.6f;

    [Header("V1: how it drives for James (traffic and police keep the settings above)")]
    [Tooltip("Top speed (m/s) when James drives: 31 = about 70 mph")]
    [SerializeField] private float playerMaxSpeed = 31f;
    [Tooltip("Pull away (m/s²), easing off towards top speed")]
    [SerializeField] private float playerAcceleration = 9f;
    [Tooltip("Brakes (m/s²) at full press; never lock (like ABS)")]
    [SerializeField] private float playerBrakes = 13f;
    [SerializeField] private float wheelbase = 2.6f;
    [Tooltip("Steering lock (degrees) slow, and at top speed: less lock at speed so it doesn't snap about")]
    [SerializeField] private float steerLockSlow = 34f, steerLockFast = 7f;
    [Tooltip("How fast the wheel turns (full lock per second); it comes back to centre twice as fast")]
    [SerializeField] private float steerRate = 3.2f;
    [Tooltip("Most sideways grip (m/s²) before the tyres slide")]
    [SerializeField] private float tyreGrip = 11f;
    [Tooltip("Handbrake: rear grip left (0-1), how much more the car rotates, how hard it slows")]
    [SerializeField] private float handbrakeGrip = 0.32f, handbrakeTurn = 1.5f, handbrakeDrag = 5f;

    private Rigidbody rb;
    private InputAction moveAction;
    private Vector2 touchInput;     // set by VehicleSystem from the on-screen buttons

    /// Every car currently in the world (player's, traffic, parked, police). Cars add/remove themselves.
    public static readonly System.Collections.Generic.List<CarController> All = new System.Collections.Generic.List<CarController>();

    public bool IsDriven { get; set; }

    // V1: for the wheels, sounds and skid marks (LundunCarFX), and the HUD
    /// The front wheels' angle (degrees, + = right).
    public float SteerAngle { get; private set; }
    /// The tyres are sliding or locked under hard braking (0 = gripping … 1 = sliding hard).
    public float Slip { get; private set; }
    /// The throttle being pressed (0-1) and whether the brakes or handbrake are on (for the engine note and lights).
    public float ThrottleNow { get; private set; }
    public bool Braking { get; private set; }
    public bool Handbraking { get; private set; }
    /// Forwards and sideways acceleration (m/s²), for the body leaning.
    public Vector2 BodyAccel { get; private set; }
    public float TopSpeed => IsDriven ? playerMaxSpeed : maxSpeed;
    private float steerNow, lastSpeed;
    /// V1: the Move action (keyboard / stick) is read directly while James drives. VehicleSystem turns this off for
    /// point-to-steer, which works the stick out itself and passes the result in SetTouchInput.
    public bool ReadsMoveAction { get; set; } = true;
    private bool handbrake;

    /// V1: the handbrake (James's HANDBRAKE button).
    public void SetHandbrake(bool on) { handbrake = on; }
    /// R3: AI drivers brake as hard as they press (throttle −0.3 = 30% brakes), for smooth stops. Off (the default, and
    /// always for James): any brake press is full brakes, as before.
    public bool ProportionalBrake { get; set; }
    public float Speed => Vector3.Dot(rb.linearVelocity, transform.forward);

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Awake()
    {
        if (GetComponent<LundunCarFX>() == null) gameObject.AddComponent<LundunCarFX>();   // V1: wheels, engine, skids
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
        moveAction = InputSystem.actions.FindAction("Move");
    }

    public void SetTouchInput(float throttle, float steer)
    {
        touchInput = new Vector2(steer, throttle);
    }

    void FixedUpdate()
    {
        int wheelsOnGround = ApplySuspension();
        if (wheelsOnGround == 0) return;

        // Touch buttons (player) or the police AI always count. The keyboard only drives the player's car.
        Vector2 input = touchInput;
        if (IsDriven && ReadsMoveAction) input += moveAction.ReadValue<Vector2>();
        input.x = Mathf.Clamp(input.x, -1f, 1f);
        input.y = Mathf.Clamp(input.y, -1f, 1f);

        float throttle = input.y;
        float steer = input.x;
        Vector3 forward = transform.forward;
        float speed = Speed;
        ThrottleNow = Mathf.Max(0f, throttle);

        if (IsDriven) { DriveJames(throttle, steer, speed); return; }      // V1
        Handbraking = false;
        Braking = throttle < -0.05f && speed > 0.5f;
        SteerAngle = steer * 30f * Mathf.Clamp01(Mathf.Abs(speed) / 2f + 0.3f);

        if (throttle > 0.05f)
        {
            if (speed < maxSpeed)
                rb.AddForce(forward * throttle * acceleration, ForceMode.Acceleration);
        }
        else if (throttle < -0.05f)
        {
            if (speed > 0.5f)
                rb.AddForce(-forward * brakeStrength * (ProportionalBrake && !IsDriven ? Mathf.Clamp01(-throttle) : 1f), ForceMode.Acceleration);
            else if (speed > -maxReverseSpeed)
                rb.AddForce(forward * throttle * acceleration * 0.6f, ForceMode.Acceleration);
        }
        else
        {
            rb.AddForce(-forward * speed * coastDrag, ForceMode.Acceleration);
        }

        float steerAmount = Mathf.Clamp01(Mathf.Abs(speed) / 4f) * Mathf.Sign(speed);
        float yaw = steer * turnSpeed * steerAmount * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yaw, 0f));

        Vector3 right = transform.right;
        float sideSpeed = Vector3.Dot(rb.linearVelocity, right);
        rb.AddForce(-right * sideSpeed * grip, ForceMode.VelocityChange);
        Slip = Mathf.Clamp01(Mathf.Max((Mathf.Abs(sideSpeed) - 1.5f) / 4f, Braking && speed > 12f && throttle < -0.9f ? 0.4f : 0f));
        Track(speed, sideSpeed);
    }

    void Track(float speed, float sideSpeed)
    {
        float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        Vector2 a = new Vector2((speed - lastSpeed) / dt, speed * rb.angularVelocity.y);   // forwards; sideways (cornering)
        BodyAccel = Vector2.Lerp(BodyAccel, a, 6f * dt);
        lastSpeed = speed;
    }

    // ------------------------------------------------------------------ V1: James driving
    /// Analogue steering that turns less at speed and eases back to centre; pull away hard, easing off near the top;
    /// brakes as hard as you press (never locking); tyres that grip up to a limit and then slide; a handbrake that
    /// kicks the back out; and a little help to stop spins (it can't hold a slide that has gone too far).
    void DriveJames(float throttle, float steer, float speed)
    {
        float dt = Time.fixedDeltaTime;
        float absV = Mathf.Abs(speed);
        Vector3 forward = transform.forward, right = transform.right;

        // steering: eases towards the stick, back to centre faster
        bool returning = Mathf.Abs(steer) < Mathf.Abs(steerNow) || Mathf.Sign(steer) != Mathf.Sign(steerNow);
        steerNow = Mathf.MoveTowards(steerNow, steer, steerRate * (returning ? 2f : 1f) * dt);
        float lockDeg = Mathf.Lerp(steerLockSlow, steerLockFast, Mathf.InverseLerp(5f, playerMaxSpeed, absV));
        SteerAngle = steerNow * lockDeg;

        // pedals
        Braking = false;
        if (throttle > 0.05f)
        {
            if (speed < -0.5f) { rb.AddForce(forward * playerBrakes * throttle, ForceMode.Acceleration); Braking = true; }
            else
            {
                float k = Mathf.Clamp01(1f - (speed / playerMaxSpeed) * (speed / playerMaxSpeed));
                rb.AddForce(forward * throttle * playerAcceleration * (0.25f + 0.75f * k) * (speed < playerMaxSpeed ? 1f : 0f), ForceMode.Acceleration);
            }
        }
        else if (throttle < -0.05f)
        {
            if (speed > 0.5f) { rb.AddForce(-forward * playerBrakes * Mathf.Clamp01(-throttle), ForceMode.Acceleration); Braking = true; }
            else if (speed > -maxReverseSpeed) rb.AddForce(forward * throttle * playerAcceleration * 0.5f, ForceMode.Acceleration);
        }
        else rb.AddForce(-forward * speed * coastDrag * 0.6f, ForceMode.Acceleration);

        Handbraking = handbrake && absV > 1f;
        if (Handbraking) rb.AddForce(-forward * Mathf.Sign(speed) * Mathf.Min(handbrakeDrag, absV / dt), ForceMode.Acceleration);

        // turning: like a real car (wheelbase and wheel angle), more with the handbrake on
        float yawRate = speed * Mathf.Tan(SteerAngle * Mathf.Deg2Rad) / Mathf.Max(1f, wheelbase);     // rad/s
        if (Handbraking && absV > 5f) yawRate *= handbrakeTurn;
        // never turn faster than the tyres can hold (a little over, so you can feel it slide on the limit; much more
        // with the handbrake), or any quick flick at speed would spin the car
        float maxYaw = tyreGrip * (Handbraking ? 2.4f : 1.1f) / Mathf.Max(3f, absV);
        yawRate = Mathf.Clamp(yawRate, -maxYaw, maxYaw);
        float sideSpeed = Vector3.Dot(rb.linearVelocity, right);
        // help against spins: sliding sideways a lot (without the handbrake) = turn in less
        if (!Handbraking && Mathf.Abs(sideSpeed) > 3.5f && Mathf.Sign(sideSpeed) == Mathf.Sign(yawRate)) yawRate *= 0.55f;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yawRate * Mathf.Rad2Deg * dt, 0f));

        // tyres: pull the car back in line, but only up to their grip (beyond it: it slides)
        float gripNow = tyreGrip * (Handbraking ? handbrakeGrip : 1f);
        float fix = Mathf.Clamp(sideSpeed, -gripNow * dt, gripNow * dt);
        rb.AddForce(-right * fix, ForceMode.VelocityChange);

        float slide = Mathf.Abs(sideSpeed) - Mathf.Abs(fix);
        Slip = absV < 3f ? 0f : Mathf.Clamp01(Mathf.Max((Mathf.Abs(sideSpeed) - 1.8f) / 4f, Handbraking ? 0.7f : 0f, Braking && absV > 10f && throttle < -0.85f ? 0.35f : 0f, slide > 0.01f ? 0.5f : 0f));
        Track(speed, sideSpeed);
    }

    int ApplySuspension()
    {
        int grounded = 0;
        float maxDistance = restLength + wheelRadius;

        foreach (Transform point in springPoints)
        {
            if (!Physics.Raycast(point.position, -transform.up, out RaycastHit hit,
                                 maxDistance, groundMask, QueryTriggerInteraction.Ignore))
                continue;

            grounded++;
            float compression = maxDistance - hit.distance;
            float springVelocity = Vector3.Dot(transform.up, rb.GetPointVelocity(point.position));
            float force = compression * springStrength - springVelocity * springDamper;
            rb.AddForceAtPosition(transform.up * force, point.position);
        }
        return grounded;
    }

    void OnDrawGizmos()
    {
        if (springPoints == null) return;
        Gizmos.color = Color.yellow;
        foreach (Transform p in springPoints)
            if (p != null) Gizmos.DrawLine(p.position, p.position - transform.up * (restLength + wheelRadius));
    }
}