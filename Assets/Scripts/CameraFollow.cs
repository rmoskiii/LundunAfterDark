using UnityEngine;
using UnityEngine.InputSystem;

/// v50: runs after James's animation and the gun rig (so first person sits exactly at his eyes).
[DefaultExecutionOrder(50)]
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
    [Header("V1: speed")]
    [Tooltip("At full speed the camera pulls back this much further (m), rises a little, and widens by Speed Fov degrees")]
    [SerializeField] private float speedPullBack = 2.2f;
    [SerializeField] private float speedRise = 0.35f;
    [SerializeField] private float speedFov = 9f;
    [Tooltip("The speed (m/s) that counts as full speed for that (28 = about 63 mph)")]
    [SerializeField] private float speedForFull = 28f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float touchSensitivity = 0.2f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Turning response (A1)")]
    [Tooltip("Slow drags turn a little less (precise); quick flicks turn more")]
    [SerializeField] private bool lookAcceleration = true;
    [SerializeField] private float slowDragGain = 0.85f;
    [SerializeField] private float fastDragGain = 1.5f;
    [Tooltip("Drag speed (screen heights per second) where slow ends and fast begins")]
    [SerializeField] private Vector2 dragSpeedRange = new Vector2(0.4f, 2.5f);

    [Header("Recoil recovery (A1)")]
    [Tooltip("How quickly the aim settles back after the kick (per second)")]
    [SerializeField] private float recoilRecovery = 7f;
    [Tooltip("Pause after a shot before it starts settling")]
    [SerializeField] private float recoilRecoveryDelay = 0.06f;

    [Header("Aiming in (A2: the AIM button)")]
    [SerializeField] private float aimDistance = 1.7f;
    [SerializeField] private float aimHeight = 0.58f;
    [SerializeField] private float aimShoulder = 0.72f;
    [SerializeField] private float aimFov = 40f;

    [Header("Framing (G3)")]
    [Tooltip("Just walking about: pull back a little to show more of the street")]
    [SerializeField] private float exploreDistanceScale = 1.12f;
    [SerializeField] private float exploreExtraHeight = 0.12f;
    [Tooltip("Gun up (fired recently): a little closer, more over the shoulder")]
    [SerializeField] private float combatDistanceScale = 0.9f;
    [SerializeField] private float combatExtraShoulder = 0.1f;

    [Header("Walls (G3)")]
    [Tooltip("Keep the camera out of buildings: it slides in when something big is behind you")]
    [SerializeField] private bool cameraCollision = true;
    [SerializeField] private float collisionRadius = 0.22f;
    [Tooltip("Ignore things smaller than this (lamp posts, bollards) so the camera doesn't jump")]
    [SerializeField] private float ignoreSmallerThan = 3f;
    [SerializeField] private LayerMask collisionLayers = 1;      // Default = buildings and the world
    [Header("Indoors (walk-in shops)")]
    [SerializeField] private float indoorDistanceScale = 0.62f;  // closer inside a shop
    [SerializeField] private float indoorHeightScale = 0.8f;     // and a little lower, under the ceiling
    private float indoor;
    private float collideDistance = -1f;

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
    private Rigidbody targetBody;        // V1: the car being followed (for its speed)
    private float speedNow;
    private float yaw;
    private float pitch = 10f;
    private float shake;
    private float lastLookTime;
    private float currentDistance;
    private float currentHeight;
    private float currentShoulder;
    private Vector2 recoilDebt;            // (yaw, pitch) kick that hasn't settled back yet
    private float lastRecoilTime = -99f;
    private float dragGain = 1f;
    private LundunAimAssist assist;

    public float Yaw => yaw;
    public float Pitch => pitch;
    /// S8: following the helicopter: much further back and higher.
    public static bool HeliMode;

    // ------------------------------------------------------------------ v50: first person (on foot only)
    [Header("First person (v50)")]
    [SerializeField] private float fpFov = 70f;
    [SerializeField] private float fpSprintFov = 80f;
    [SerializeField] private float fpAimFov = 45f;
    [SerializeField] private float fpMinPitch = -75f;
    [SerializeField] private float fpMaxPitch = 70f;
    [Tooltip("The eyes, from the head bone: up and forward (m)")]
    [SerializeField] private Vector3 fpEye = new Vector3(0f, 0.075f, 0.11f);

    const string ViewKey = "Lundun.Camera", LastFirstKey = "Lundun.CameraFirst";
    static int mode = -1;
    /// 0 = third person, 1 = first person, 2 = first person, steady (no head bob).
    public static int Mode
    {
        get { if (mode < 0) mode = Mathf.Clamp(PlayerPrefs.GetInt(ViewKey, 0), 0, 2); return mode; }
        set
        {
            mode = Mathf.Clamp(value, 0, 2);
            PlayerPrefs.SetInt(ViewKey, mode);
            if (mode > 0) PlayerPrefs.SetInt(LastFirstKey, mode);
            PlayerPrefs.Save();
        }
    }
    /// The eye button on the HUD: third ↔ first (the first-person style you last chose).
    public static void ToggleView()
    {
        Mode = Mode == 0 ? Mathf.Clamp(PlayerPrefs.GetInt(LastFirstKey, 1), 1, 2) : 0;
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(Mode == 0 ? "THIRD PERSON" : "FIRST PERSON");
    }
    /// First person right now (on foot, nothing else holding the camera).
    public static bool FirstPersonNow { get; private set; }

    private Transform fpHead;               // James's head bone (shrunk away while we're behind his eyes)
    private Transform fpHeadOf;             // whose head it is
    private Vector3 fpHeadScale = Vector3.one;
    private bool fpHidden;
    private float fp;                       // 0 third … 1 first (eased)
    private Vector3 fpSmooth;
    private float fpEyeHeight = -1f, fpRoll, fpLastYaw, fpPull;
    private float normalNear = -1f;

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

        assist = GetComponent<LundunAimAssist>();
        if (assist == null) assist = gameObject.AddComponent<LundunAimAssist>();
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

    bool Big(Collider c) { return c != null && c.bounds.size.magnitude >= ignoreSmallerThan; }

    public void AddRecoil(float up, float sideways)
    {
        if (FirstPersonNow) { up *= 1.3f; sideways *= 1.2f; }          // v50: the kick reads stronger from the eyes
        pitch -= up; yaw += sideways;
        recoilDebt += new Vector2(sideways, -up);
        lastRecoilTime = Time.time;
    }
    public void AddShake(float amount) { shake = Mathf.Max(shake, amount); }
    public void Nudge(float yawDelta, float pitchDelta) { yaw += yawDelta; pitch += pitchDelta; }

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 look = Vector2.zero;
        if (mouseLookActive) look += lookAction.ReadValue<Vector2>() * mouseSensitivity;
        if (touchLook != null)
        {
            Vector2 drag = touchLook.ConsumeDelta();
            if (lookAcceleration && !vehicleMode)
            {
                float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
                float speed = drag.magnitude / Mathf.Max(1f, Screen.height) / dt;            // screen heights per second
                float want = Mathf.Lerp(slowDragGain, fastDragGain, Mathf.InverseLerp(dragSpeedRange.x, dragSpeedRange.y, speed));
                dragGain = drag.sqrMagnitude > 0f ? Mathf.Lerp(dragGain, want, 0.5f) : 1f;   // eased, so it never jumps mid-drag
                drag *= dragGain;
            }
            look += drag * touchSensitivity * LundunSettings.LookSpeed;      // A3: LOOK SPEED setting
        }

        if (look.sqrMagnitude > 0.0001f) lastLookTime = Time.time;

        // A2: aimed in = calmer look speed
        float aimIn = vehicleMode ? 0f : LundunAim.Eased;
        if (aimIn > 0f) look *= Mathf.Lerp(1f, LundunAim.Sensitivity, aimIn);

        // aim assist: a little stickier while the crosshair is on someone
        if (!vehicleMode && assist != null) look *= assist.LookScale;

        yaw += look.x;
        pitch -= look.y;

        if (!vehicleMode)
        {
            // pulling down against the kick counts toward settling it, so you never over-correct
            if (recoilDebt.y < 0f && look.y < 0f) recoilDebt.y = Mathf.Min(0f, recoilDebt.y - look.y);
            if (Time.time - lastRecoilTime > recoilRecoveryDelay && recoilDebt.sqrMagnitude > 0.000001f)
            {
                Vector2 back = recoilDebt * (1f - Mathf.Exp(-recoilRecovery * Time.deltaTime));
                yaw -= back.x; pitch -= back.y;
                recoilDebt -= back;
            }

            // aim assist: gently keep up with a moving threat while you're aiming or firing
            if (assist != null)
            {
                Vector2 follow = assist.FollowStep(yaw, pitch);
                yaw += follow.x; pitch += follow.y;
            }
        }
        else recoilDebt = Vector2.zero;

        // In a car: if you're not looking around, swing back behind the car
        if (vehicleMode && Time.time - lastLookTime > lookIdleTime)
        {
            yaw = Mathf.LerpAngle(yaw, target.eulerAngles.y, vehicleFollowSpeed * Time.deltaTime);
            pitch = Mathf.Lerp(pitch, 12f, vehicleFollowSpeed * Time.deltaTime);
        }

        // v50: first person? (never in a car, on the Wheel or the train, flying, dead, or with no head to look from)
        bool wantFp = WantFirstPerson();
        fp = Mathf.MoveTowards(fp, wantFp ? 1f : 0f, Time.deltaTime * (wantFp ? 5f : 7f));
        if (wantFp && fp < 0.05f) fp = 0.05f;
        if (vehicleMode || HeliMode) fp = 0f;                                  // into a car: straight to the car camera
        FirstPersonNow = wantFp && fp > 0.5f;
        pitch = Mathf.Clamp(pitch, Mathf.Lerp(minPitch, fpMinPitch, fp), Mathf.Lerp(maxPitch, fpMaxPitch, fp));

        // Ease between on-foot and in-car framing
        float step = 5f * Time.deltaTime;
        // V1: faster = further back, a touch higher, wider
        float fast = 0f;
        if (vehicleMode && target != null)
        {
            if (targetBody == null || targetBody.transform != target) targetBody = target.GetComponent<Rigidbody>();
            if (targetBody != null) fast = Mathf.Clamp01(targetBody.linearVelocity.magnitude / Mathf.Max(1f, speedForFull));
        }
        speedNow = Mathf.Lerp(speedNow, fast, 2f * Time.deltaTime);
        float vDist = HeliMode ? 15f : vehicleDistance + speedPullBack * speedNow, vHeight = HeliMode ? 3.6f : vehicleHeight + speedRise * speedNow;
        currentDistance = Mathf.Lerp(currentDistance, vehicleMode ? vDist : distance, step);
        currentHeight = Mathf.Lerp(currentHeight, vehicleMode ? vHeight : height, step);
        currentShoulder = Mathf.Lerp(currentShoulder, vehicleMode ? 0f : shoulderOffset, step);

        // A2: tighter over the shoulder while aimed in
        float camDistance = Mathf.Lerp(currentDistance, aimDistance, aimIn);
        float camHeight = Mathf.Lerp(currentHeight, aimHeight, aimIn);
        float camShoulder = Mathf.Lerp(currentShoulder, aimShoulder, aimIn);

        // G3: wider while just walking about, a touch closer while the gun's up
        if (!vehicleMode)
        {
            float combat = LundunWeaponRig.Raise * (1f - aimIn);
            float explore = (1f - aimIn) * (1f - combat);
            camDistance *= Mathf.Lerp(1f, exploreDistanceScale, explore) * Mathf.Lerp(1f, combatDistanceScale, combat);
            camHeight += exploreExtraHeight * explore;
            camShoulder += combatExtraShoulder * combat;
        }

        // inside a walk-in shop (Basketful and the rest, LAGOS FOODS): closer and lower, easing in and out
        bool inShop = !vehicleMode && (LundunShopInterior.At(target.position) != null ||
                      (LundunStoryShop.Instance != null && LundunStoryShop.Instance.Ready && LundunStoryShop.Instance.Inside(target.position)) || LundunPubs.InsidePub || LundunArches.Inside);
        indoor = Mathf.MoveTowards(indoor, inShop ? 1f : 0f, 2f * Time.deltaTime);
        if (indoor > 0f) { camDistance *= Mathf.Lerp(1f, indoorDistanceScale, indoor); camHeight *= Mathf.Lerp(1f, indoorHeightScale, indoor); }

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + Vector3.up * camHeight;
        Vector3 focusPoint = pivot + rotation * Vector3.right * camShoulder;

        // G3: never inside a wall. First the shoulder offset (standing against a wall), then the arm back.
        float dist = camDistance;
        if (cameraCollision)
        {
            if (Physics.Linecast(pivot, focusPoint, out RaycastHit side, collisionLayers, QueryTriggerInteraction.Ignore) && Big(side.collider))
                focusPoint = side.point - (focusPoint - pivot).normalized * (collisionRadius + 0.05f);
            float want = camDistance;
            if (Physics.SphereCast(focusPoint, collisionRadius, -(rotation * Vector3.forward), out RaycastHit hit, camDistance, collisionLayers, QueryTriggerInteraction.Ignore)
                && Big(hit.collider))
                want = Mathf.Max(0.35f, hit.distance - 0.05f);
            if (collideDistance < 0f || want < collideDistance) collideDistance = want;            // pull in at once
            else collideDistance = Mathf.MoveTowards(collideDistance, want, 6f * Time.deltaTime);  // ease back out
            dist = Mathf.Min(camDistance, collideDistance);
        }

        transform.position = focusPoint - rotation * Vector3.forward * dist;
        transform.rotation = rotation;
        if (fp > 0f) FirstPerson(rotation, aimIn);
        else HideHead(false);

        if (shake > 0f)
        {
            transform.position += Random.insideUnitSphere * shake;
            shake = Mathf.Lerp(shake, 0f, shakeDecay * Time.deltaTime);
            if (shake < 0.001f) shake = 0f;
        }

        float targetFov = (player != null && player.IsSprinting) ? Mathf.Lerp(sprintFov, fpSprintFov, fp) : Mathf.Lerp(normalFov, fpFov, fp);
        float aimFovNow = Mathf.Lerp(aimFov, fpAimFov, fp);
        if (vehicleMode) targetFov = normalFov + speedFov * speedNow;               // V1
        if (aimIn > 0f)
        {
            // the aim blend is already eased, so follow it closely
            targetFov = Mathf.Lerp(targetFov, aimFovNow, aimIn);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 20f * Time.deltaTime);
        }
        else cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, fovChangeSpeed * Time.deltaTime);
    }

    // ------------------------------------------------------------------ v50: first person
    bool WantFirstPerson()
    {
        if (Mode == 0 || vehicleMode || HeliMode || target == null || player == null) return false;
        if (!player.gameObject.activeInHierarchy) return false;
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead) return false;
        if (LundunBikes.Instance != null && LundunBikes.Instance.Riding) return false;
        FindHead();
        return fpHead != null;
    }

    void FindHead()
    {
        if (fpHeadOf == target && fpHead != null) return;
        HideHead(false);
        fpHead = null; fpHeadOf = target;
        Animator a = target != null ? target.GetComponentInChildren<Animator>() : null;
        if (a != null && a.isHuman && a.avatar != null) fpHead = a.GetBoneTransform(HumanBodyBones.Head);
        if (fpHead != null) fpHeadScale = fpHead.localScale;
        fpEyeHeight = -1f;
    }

    /// Shrink James's head away (his hair and hat with it) so the camera can sit behind his eyes: his real arms,
    /// hands and gun stay. Put back the moment we leave first person.
    void HideHead(bool hide)
    {
        if (fpHead == null) { fpHidden = false; return; }
        if (hide) { fpHead.localScale = fpHeadScale * 0.001f; fpHidden = true; }
        else if (fpHidden) { fpHead.localScale = fpHeadScale; fpHidden = false; }
    }

    void FirstPerson(Quaternion rotation, float aimIn)
    {
        if (fpHead == null) { HideHead(false); return; }
        // where his eyes are: the head bone (it bobs with his walk), or STEADY: a smoothed eye height above his feet
        Quaternion bodyYaw = Quaternion.Euler(0f, yaw, 0f);
        Vector3 eye = fpHead.position + bodyYaw * fpEye;
        float h = eye.y - target.position.y;
        fpEyeHeight = fpEyeHeight < 0f ? h : Mathf.Lerp(fpEyeHeight, h, 3f * Time.deltaTime);
        if (Mode == 2) eye.y = target.position.y + fpEyeHeight;
        if ((fpSmooth - eye).sqrMagnitude > 1f) fpSmooth = eye;
        fpSmooth = Vector3.Lerp(fpSmooth, eye, 1f - Mathf.Exp(-(Mode == 2 ? 18f : 30f) * Time.deltaTime));

        // a touch of sway: roll into quick turns (not while aimed in)
        float dYaw = Mathf.DeltaAngle(fpLastYaw, yaw); fpLastYaw = yaw;
        float wantRoll = Mode == 2 ? 0f : Mathf.Clamp(-dYaw * 0.35f, -3f, 3f) * (1f - aimIn);
        fpRoll = Mathf.Lerp(fpRoll, wantRoll, 8f * Time.deltaTime);

        // right up against a wall: ease the eyes back a little so the gun doesn't poke through it
        float pull = 0f;
        if (Physics.SphereCast(fpSmooth - rotation * Vector3.forward * 0.1f, 0.08f, rotation * Vector3.forward, out RaycastHit wall, 0.7f, collisionLayers, QueryTriggerInteraction.Ignore))
            pull = Mathf.Clamp01((0.7f - wall.distance) / 0.5f) * 0.22f;
        fpPull = Mathf.Lerp(fpPull, pull, 12f * Time.deltaTime);

        Vector3 fpPos = fpSmooth - rotation * Vector3.forward * fpPull;
        Quaternion fpRot = rotation * Quaternion.Euler(0f, 0f, fpRoll);
        float k = Ease(fp);
        transform.position = Vector3.Lerp(transform.position, fpPos, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, fpRot, k);
        HideHead(fp > 0.6f);
        if (normalNear < 0f) normalNear = cam.nearClipPlane;
        cam.nearClipPlane = Mathf.Lerp(normalNear, 0.03f, k);
    }

    static float Ease(float t) { return t * t * (3f - 2f * t); }

    void Update()
    {
        // back from first person: the usual near clip again
        if (fp <= 0f && normalNear > 0f && cam != null && !Mathf.Approximately(cam.nearClipPlane, normalNear)) cam.nearClipPlane = normalNear;
    }

    void OnDisable()
    {
        // the binoculars, a cutscene camera: give James his head back
        HideHead(false); FirstPersonNow = false; fp = 0f;
        if (cam != null && normalNear > 0f) cam.nearClipPlane = normalNear;
    }
}
