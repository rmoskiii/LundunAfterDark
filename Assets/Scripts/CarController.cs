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

    private Rigidbody rb;
    private InputAction moveAction;
    private Vector2 touchInput;     // set by VehicleSystem from the on-screen buttons

    public bool IsDriven { get; set; }
    public float Speed => Vector3.Dot(rb.linearVelocity, transform.forward);

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

        Vector2 input = Vector2.zero;
        if (IsDriven)
        {
            input = moveAction.ReadValue<Vector2>() + touchInput;   // keyboard in the editor, buttons on the phone
            input = Vector2.ClampMagnitude(input, 1f);
        }

        float throttle = input.y;
        float steer = input.x;
        Vector3 forward = transform.forward;
        float speed = Speed;

        if (throttle > 0.05f)
        {
            if (speed < maxSpeed)
                rb.AddForce(forward * throttle * acceleration, ForceMode.Acceleration);
        }
        else if (throttle < -0.05f)
        {
            if (speed > 0.5f)
                rb.AddForce(-forward * brakeStrength, ForceMode.Acceleration);
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