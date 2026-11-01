using UnityEngine;
using UnityEngine.InputSystem;

/// One Lundun Cycles hire bike (R9). Joystick up = pedal, down = brake, left/right = steer.
/// James sits on it (hips on the saddle, feet on the turning pedals, hands on the grips),
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

    [Header("Rider (positions on the bike: x right, y up, z forward, metres)")]
    [SerializeField] private Vector3 seatOffset = new Vector3(0f, 0.02f, -0.18f);   // where James's feet-origin sits
    [Tooltip("Where his hips go: just above the saddle")]
    [SerializeField] private Vector3 hipsOnBike = new Vector3(0f, 1.0f, -0.2f);
    [Tooltip("Middle of the pedal cranks")]
    [SerializeField] private Vector3 crankCentre = new Vector3(0f, 0.30f, -0.02f);
    [SerializeField] private float crankRadius = 0.16f;
    [SerializeField] private float pedalWidth = 0.14f;
    [Tooltip("Ankle height above the pedal")]
    [SerializeField] private float ankleAbovePedal = 0.07f;
    [Tooltip("Right handlebar grip (the left one is mirrored)")]
    [SerializeField] private Vector3 rightGrip = new Vector3(0.30f, 1.04f, 0.27f);
    [Tooltip("Lean his back forward (degrees)")]
    [SerializeField] private float leanForward = 18f;

    public bool Ridden { get { return rider != null; } }
    public float Speed { get { return speed; } }
    public bool Crashed { get; private set; }

    private CharacterController cc;
    private Transform body, frontWheel, backWheel;
    private Transform rider;
    private Animator riderAnim;
    // the rider's bones (found through his Humanoid avatar, so any Mixamo rig works)
    private Transform bHips, bSpine;
    private Transform[] bThigh = new Transform[2], bShin = new Transform[2], bFoot = new Transform[2];
    private Transform[] bArm = new Transform[2], bForearm = new Transform[2], bHand = new Transform[2];
    private InputAction move;
    private float speed, yaw, lean, wheelAngle, crank, vertical;
    private static readonly Collider[] hits = new Collider[8];
    private int vehicleMask;
    private float riderFeet;          // James's root sits this far above his feet (his capsule is centred on it)

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
        riderFeet = LundunPlaces.FeetOffset(james);
        CharacterController jc = james.GetComponent<CharacterController>();
        if (jc != null) jc.enabled = false;
        PlayerController pc = james.GetComponent<PlayerController>();
        if (pc != null) pc.enabled = false;
        james.SetParent(body, false);
        james.localPosition = seatOffset + Vector3.up * riderFeet;     // seatOffset is where his FEET go
        james.localRotation = Quaternion.identity;
        riderAnim = james.GetComponentInChildren<Animator>();
        bHips = null;
        if (riderAnim != null && riderAnim.isHuman && riderAnim.avatar != null)
        {
            bHips = riderAnim.GetBoneTransform(HumanBodyBones.Hips);
            bSpine = riderAnim.GetBoneTransform(HumanBodyBones.Spine);
            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                bThigh[s] = riderAnim.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                bShin[s] = riderAnim.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                bFoot[s] = riderAnim.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                bArm[s] = riderAnim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                bForearm[s] = riderAnim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                bHand[s] = riderAnim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            }
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
        bHips = null;
        james.SetParent(null, true);
        Vector3 side = transform.right * (thrown ? 1.4f : 0.9f);
        Vector3 at = transform.position + side + Vector3.up * (0.15f + riderFeet);
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
    // Hips onto the saddle, back leant forward, then each leg bent so its foot sits on its (turning) pedal and each arm
    // bent so its hand sits on its grip. Done with bone positions, so it works on any Humanoid rig.
    void LateUpdate()
    {
        if (rider == null || bHips == null) return;
        Transform b = body;

        // 1. sit down: hips over the saddle
        bHips.position = b.TransformPoint(hipsOnBike);

        // 2. lean forward over the bars
        if (bSpine != null) bSpine.rotation = Quaternion.AngleAxis(leanForward, b.right) * bSpine.rotation;

        for (int s = 0; s < 2; s++)
        {
            float side = s == 0 ? -1f : 1f;                      // left, right

            // 3. legs: the pedals go round with the crank, half a turn apart
            float ph = crank + (s == 0 ? 0f : Mathf.PI);
            Vector3 pedal = crankCentre + new Vector3(side * pedalWidth, -Mathf.Cos(ph) * crankRadius, Mathf.Sin(ph) * crankRadius);
            Vector3 ankle = b.TransformPoint(pedal + Vector3.up * ankleAbovePedal);
            Vector3 kneePole = b.forward + b.up * 0.3f + b.right * (side * 0.15f);       // knees forward, a touch out
            if (bThigh[s] != null && bShin[s] != null && bFoot[s] != null) TwoBone(bThigh[s], bShin[s], bFoot[s], ankle, kneePole);
            if (bFoot[s] != null)
            {
                // foot pointing forward along the bike
                bFoot[s].rotation = Quaternion.FromToRotation(FootForward(s), b.forward) * bFoot[s].rotation;
            }

            // 4. arms: hands on the grips, elbows down and out
            Vector3 grip = b.TransformPoint(new Vector3(side * Mathf.Abs(rightGrip.x), rightGrip.y, rightGrip.z));
            Vector3 elbowPole = -b.up * 0.6f + b.right * (side * 0.6f) - b.forward * 0.3f;
            if (bArm[s] != null && bForearm[s] != null && bHand[s] != null) TwoBone(bArm[s], bForearm[s], bHand[s], grip, elbowPole);
        }
    }

    /// The direction the foot bone currently points (ankle → toes), whatever axis the rig uses.
    Vector3 FootForward(int s)
    {
        Transform f = bFoot[s];
        if (f.childCount > 0) { Vector3 d = f.GetChild(0).position - f.position; d.y = 0f; if (d.sqrMagnitude > 1e-6f) return d.normalized; }
        Vector3 fwd = body.forward; return fwd;
    }

    /// Two-bone IK: bends upper → lower → end so the end lands on target, the middle joint bending towards pole.
    static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
    {
        Vector3 a = upper.position, m = lower.position, e = end.position;
        float la = (m - a).magnitude, lb = (e - m).magnitude;
        if (la < 1e-4f || lb < 1e-4f) return;
        Vector3 toT = target - a;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 0.001f, la + lb - 0.001f);
        Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : (e - a).normalized;
        Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(m - a, dir);
        bend.Normalize();
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        float ang = Mathf.Acos(cosA);
        Vector3 joint = a + dir * (Mathf.Cos(ang) * la) + bend * (Mathf.Sin(ang) * la);
        upper.rotation = Quaternion.FromToRotation(m - a, joint - a) * upper.rotation;
        m = lower.position; e = end.position;
        Vector3 endAt = a + dir * d;
        lower.rotation = Quaternion.FromToRotation(e - m, endAt - m) * lower.rotation;
    }
}
