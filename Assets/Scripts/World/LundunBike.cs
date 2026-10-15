using UnityEngine;
using UnityEngine.InputSystem;

/// One Lundun Cycles hire bike (R9). Joystick up = pedal, down = brake, left/right = steer.
/// James sits on it (his legs pedal and his hands reach the bars through his Humanoid muscles),
/// the bike leans into corners, and a car hitting you (or you hitting a wall hard) knocks him off.
/// Spawned and recycled by LundunBikes.
[RequireComponent(typeof(CharacterController))]
public class LundunBike : MonoBehaviour
{
    [Header("Riding")]
    [SerializeField] private float topSpeed = 9f;
    [SerializeField] private float pedalAccel = 3.2f;
    [SerializeField] private float brakeDecel = 9f;
    [SerializeField] private float coastDrag = 0.5f;
    [SerializeField] private float steerRate = 110f;          // degrees per second at low speed
    [SerializeField] private float maxLean = 24f;
    [SerializeField] private float gravity = -20f;

    [Header("Crashing")]
    [SerializeField] private float carHitSpeed = 5f;          // a car moving this fast into you = off you come
    [SerializeField] private float wallCrashSpeed = 6.5f;     // riding into a wall this fast = off you come

    [Header("Rider pose (Humanoid muscles, −1..1)")]
    [SerializeField] private Vector3 seatOffset = new Vector3(0f, 0.02f, -0.18f);   // where James's feet-origin sits
    [SerializeField] private float legForward = 0.55f;
    [SerializeField] private float legPump = 0.22f;
    [SerializeField] private float kneeBend = -0.45f;
    [SerializeField] private float kneePump = 0.35f;
    [SerializeField] private float leanForward = 0.35f;
    [SerializeField] private float armsForward = 0.55f;
    [SerializeField] private float elbows = 0.6f;

    public bool Ridden { get { return rider != null; } }
    public float Speed { get { return speed; } }
    public bool Crashed { get; private set; }

    private CharacterController cc;
    private Transform body, frontWheel, backWheel;
    private Transform rider;
    private Animator riderAnim;
    private HumanPoseHandler poseHandler;
    private HumanPose pose;
    private int[] mLegFB = new int[2], mKnee = new int[2], mArmFB = new int[2], mArmDU = new int[2], mElbow = new int[2];
    private int mSpine = -1, mChest = -1;
    private InputAction move;
    private float speed, yaw, lean, wheelAngle, crank, vertical;
    private static readonly Collider[] hits = new Collider[8];
    private int vehicleMask;

    /// Builds the visible bike from the meshes made by Lundun → 13.
    public void Build(Mesh frame, Mesh wheel, Material mat)
    {
        cc = GetComponent<CharacterController>();
        cc.radius = 0.35f; cc.height = 1.4f; cc.center = new Vector3(0f, 0.75f, 0f); cc.stepOffset = 0.25f; cc.slopeLimit = 40f;
        body = new GameObject("Body").transform; body.SetParent(transform, false);
        Part("Frame", body, frame, mat, Vector3.zero);
        frontWheel = Part("FrontWheel", body, wheel, mat, new Vector3(0f, 0.33f, 0.52f));
        backWheel = Part("BackWheel", body, wheel, mat, new Vector3(0f, 0.33f, -0.52f));
        yaw = transform.eulerAngles.y;
        vehicleMask = LayerMask.GetMask("Vehicle");
        int player = LayerMask.NameToLayer("Player");
        if (player >= 0) gameObject.layer = player;           // traffic brakes for it like for James
    }

    static Transform Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 at)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return g.transform;
    }

    // ------------------------------------------------------------------ getting on and off
    public void Mount(Transform james)
    {
        rider = james;
        Crashed = false;
        CharacterController jc = james.GetComponent<CharacterController>();
        if (jc != null) jc.enabled = false;
        PlayerController pc = james.GetComponent<PlayerController>();
        if (pc != null) pc.enabled = false;
        james.SetParent(body, false);
        james.localPosition = seatOffset;
        james.localRotation = Quaternion.identity;
        riderAnim = james.GetComponentInChildren<Animator>();
        if (riderAnim != null && riderAnim.isHuman && riderAnim.avatar != null)
        {
            poseHandler = new HumanPoseHandler(riderAnim.avatar, riderAnim.transform);
            string[] names = HumanTrait.MuscleName;
            for (int s = 0; s < 2; s++)
            {
                string side = s == 0 ? "Left" : "Right";
                mLegFB[s] = System.Array.IndexOf(names, side + " Upper Leg Front-Back");
                mKnee[s] = System.Array.IndexOf(names, side + " Lower Leg Stretch");
                mArmFB[s] = System.Array.IndexOf(names, side + " Arm Front-Back");
                mArmDU[s] = System.Array.IndexOf(names, side + " Arm Down-Up");
                mElbow[s] = System.Array.IndexOf(names, side + " Forearm Stretch");
            }
            mSpine = System.Array.IndexOf(names, "Spine Front-Back");
            mChest = System.Array.IndexOf(names, "Chest Front-Back");
        }
        if (move == null) move = InputSystem.actions.FindAction("Move");
        yaw = transform.eulerAngles.y;
    }

    /// Off the bike: James stands beside it (or is thrown off when crashing). Returns James.
    public Transform Dismount(bool thrown)
    {
        Transform james = rider;
        if (james == null) return null;
        rider = null;
        poseHandler = null;
        james.SetParent(null, true);
        Vector3 side = transform.right * (thrown ? 1.4f : 0.9f);
        Vector3 at = transform.position + side + Vector3.up * 0.15f;
        james.SetPositionAndRotation(at, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        CharacterController jc = james.GetComponent<CharacterController>();
        if (jc != null) jc.enabled = true;
        PlayerController pc = james.GetComponent<PlayerController>();
        if (pc != null && !VehicleSystem.Locked) pc.enabled = true;
        if (thrown) { Crashed = true; lean = 80f; }
        speed = 0f;
        return james;
    }

    // ------------------------------------------------------------------ riding
    void Update()
    {
        float dt = Time.deltaTime;
        Vector2 input = rider != null && move != null && !VehicleSystem.Locked ? move.ReadValue<Vector2>() : Vector2.zero;

        if (!Crashed)
        {
            if (input.y > 0.15f) speed = Mathf.MoveTowards(speed, topSpeed * input.y, pedalAccel * dt);
            else if (input.y < -0.3f) speed = speed > 0.2f ? Mathf.MoveTowards(speed, 0f, brakeDecel * dt) : Mathf.MoveTowards(speed, -1.2f, 2f * dt);
            else speed = Mathf.MoveTowards(speed, 0f, coastDrag * dt);

            float turn = rider != null && Mathf.Abs(speed) > 0.3f ? input.x * steerRate * Mathf.Lerp(1f, 0.45f, Mathf.Abs(speed) / topSpeed) : 0f;
            yaw += turn * dt * Mathf.Sign(speed);
            float targetLean = -input.x * Mathf.Clamp01(Mathf.Abs(speed) / 4f) * maxLean;
            lean = Mathf.Lerp(lean, targetLean, 6f * dt);
        }
        else lean = Mathf.Lerp(lean, 82f, 8f * dt);                 // lying on its side

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        body.localRotation = Quaternion.Euler(0f, 0f, lean);

        vertical = cc.isGrounded ? -1f : vertical + gravity * dt;
        Vector3 vel = transform.forward * speed + Vector3.up * vertical;
        CollisionFlags flags = cc.Move(vel * dt);

        // wheels and pedals
        wheelAngle += speed / 0.33f * Mathf.Rad2Deg * dt;
        frontWheel.localRotation = Quaternion.Euler(wheelAngle, input.x * 25f, 0f);
        backWheel.localRotation = Quaternion.Euler(wheelAngle, 0f, 0f);
        crank += speed / 0.33f * 0.55f * dt;

        if (rider != null) CheckCrash(flags);
    }

    void CheckCrash(CollisionFlags flags)
    {
        // a car driving into you
        int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.7f, 1.1f, hits, vehicleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Rigidbody rb = hits[i].attachedRigidbody;
            if (rb == null) continue;
            Vector3 rel = rb.linearVelocity - transform.forward * speed;
            if (rel.magnitude > carHitSpeed) { Crash(rel.magnitude); return; }
        }
        // riding hard into something
        if ((flags & CollisionFlags.Sides) != 0 && speed > wallCrashSpeed) Crash(speed);
    }

    void Crash(float impact)
    {
        LundunBikes mgr = LundunBikes.Instance;
        if (mgr != null) mgr.ThrownOff(this, impact);
        else Dismount(true);
    }

    // ------------------------------------------------------------------ the rider's pose (after the Animator has run)
    void LateUpdate()
    {
        if (rider == null || poseHandler == null) return;
        poseHandler.GetHumanPose(ref pose);
        float pump = Mathf.Abs(speed) > 0.2f ? 1f : 0f;
        for (int s = 0; s < 2; s++)
        {
            float ph = crank + s * Mathf.PI;
            Set(mLegFB[s], legForward + legPump * pump * Mathf.Sin(ph));
            Set(mKnee[s], kneeBend + kneePump * pump * Mathf.Cos(ph));
            Set(mArmFB[s], armsForward);
            Set(mArmDU[s], 0.05f);
            Set(mElbow[s], elbows);
        }
        Set(mSpine, leanForward);
        Set(mChest, leanForward * 0.5f);
        poseHandler.SetHumanPose(ref pose);
    }

    void Set(int muscle, float v)
    {
        if (muscle >= 0 && pose.muscles != null && muscle < pose.muscles.Length) pose.muscles[muscle] = Mathf.Clamp(v, -1f, 1f);
    }
}
