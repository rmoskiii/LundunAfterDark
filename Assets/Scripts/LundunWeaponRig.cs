using System.Collections.Generic;
using UnityEngine;

/// G3: how James holds his guns. Runs after the animation each frame (works on any Humanoid rig, like the bike):
///   • both hands on the gun — long guns in the shoulder pocket, the pistol held out two-handed; fingers wrap the grips
///   • "low ready" (gun angled down) while just walking about; comes up to aim when you AIM or fire, then drops
///     again a couple of seconds after the last shot
///   • his upper body leans with where you're aiming (up / down), and the gun points at what the crosshair is on
///   • the gun kicks back on every shot and settles; the shotgun's pump racks (left hand riding it)
///   • reloads: the gun tilts, the magazine drops out and a fresh one goes in (shotgun: shells into the loading port)
/// Each gun is one of LundunGunModels (original, made in code). Added automatically by LundunWeapons.
public class LundunWeaponRig : MonoBehaviour
{
    public static LundunWeaponRig Instance { get; private set; }
    /// 0 = lowered … 1 = up and aiming (the camera uses it for its framing).
    public static float Raise => Instance != null ? Instance.raise * Instance.weight : 0f;

    [Header("Stance")]
    [Tooltip("Seconds after the last shot before the gun comes back down")]
    [SerializeField] private float lowerAfter = 2.2f;
    [Tooltip("How much of the up/down aim the spine takes (the arms do the rest)")]
    [Range(0f, 1f)] [SerializeField] private float spineAim = 0.65f;
    [SerializeField] private float pistolReach = 0.44f;
    [Tooltip("Upper body turned this far into the gun (degrees): long guns, raised / lowered; pistol")]
    [SerializeField] private float bladeLongUp = 32f, bladeLongLow = 22f, bladePistol = 10f;
    [Header("Hands (fine-tuning)")]
    [Range(0f, 1.5f)] [SerializeField] private float fingerCurl = 1f;
    [SerializeField] private Vector3 rightHandNudge = Vector3.zero;     // metres, in the gun's own space
    [SerializeField] private Vector3 leftHandNudge = Vector3.zero;

    // bones
    private Animator anim;
    private Transform spine, chest, upperChest;
    private Transform rUpper, rLower, rHand, lUpper, lLower, lHand;
    private readonly Transform[][] rFingers = new Transform[4][], lFingers = new Transform[4][];
    private Transform rIndexP, rMiddleP, rLittleP, lIndexP, lMiddleP, lLittleP;

    // guns
    private readonly Dictionary<LundunWeapons.Kind, Gun> guns = new Dictionary<LundunWeapons.Kind, Gun>();
    private Gun gun;
    private LundunWeapons.Kind kind = LundunWeapons.Kind.Fists;

    class Gun
    {
        public GameObject go;
        public Transform muzzle, eject, fore, stock, magWell, mag, pump;
        public Vector3 magRest, pumpRest;
        public bool longGun;
    }

    // motion
    private float weight, raise, suspendUntil, lastFire = -99f, kick, pumpStart = -99f;
    private bool pumpEjected;
    private PlayerController player;
    private LundunWeapons weapons;
    private int aimMask;

    public Transform Muzzle => gun != null && weight > 0.01f ? gun.muzzle : null;
    public Transform EjectPoint => gun != null ? gun.eject : null;
    public bool Ready => anim != null && rHand != null;

    void Awake()
    {
        Instance = this;
        player = GetComponent<PlayerController>();
        if (player == null) player = GetComponentInParent<PlayerController>();
        weapons = GetComponent<LundunWeapons>();
        aimMask = ~LayerMask.GetMask("Player", "UI", "Ignore Raycast");
        anim = (player != null ? player.gameObject : gameObject).GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman) { anim = null; return; }
        spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        upperChest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
        rUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); rLower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        lUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); lLower = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        HumanBodyBones[,] fr = {
            { HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal },
            { HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal },
            { HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal },
            { HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal } };
        HumanBodyBones[,] fl = {
            { HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal },
            { HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal },
            { HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal },
            { HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal } };
        for (int f = 0; f < 4; f++)
        {
            rFingers[f] = new[] { anim.GetBoneTransform(fr[f, 0]), anim.GetBoneTransform(fr[f, 1]), anim.GetBoneTransform(fr[f, 2]) };
            lFingers[f] = new[] { anim.GetBoneTransform(fl[f, 0]), anim.GetBoneTransform(fl[f, 1]), anim.GetBoneTransform(fl[f, 2]) };
        }
        rIndexP = rFingers[0][0]; rMiddleP = rFingers[1][0]; rLittleP = rFingers[3][0];
        lIndexP = lFingers[0][0]; lMiddleP = lFingers[1][0]; lLittleP = lFingers[3][0];
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ------------------------------------------------------------------ called by the weapon system
    public void Equip(LundunWeapons.Kind k)
    {
        kind = k;
        foreach (Gun g in guns.Values) if (g.go != null) g.go.SetActive(false);
        gun = null;
        if (k == LundunWeapons.Kind.Fists || !Ready) return;
        if (!guns.TryGetValue(k, out gun) || gun.go == null)
        {
            GameObject go = LundunGunModels.Build(k);
            if (go == null) return;
            go.transform.SetParent(transform, false);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            gun = new Gun { go = go };
            gun.muzzle = Find(go.transform, "Muzzle"); gun.eject = Find(go.transform, "Eject"); gun.fore = Find(go.transform, "Fore");
            gun.stock = Find(go.transform, "Stock"); gun.magWell = Find(go.transform, "MagWell");
            gun.mag = Find(go.transform, "Mag"); gun.pump = Find(go.transform, "Pump");
            if (gun.mag != null) gun.magRest = gun.mag.localPosition;
            if (gun.pump != null) gun.pumpRest = gun.pump.localPosition;
            gun.longGun = gun.stock != null;
            guns[k] = gun;
        }
        gun.go.SetActive(weight > 0.01f);
        pumpStart = -99f;
    }

    /// A shot went off: up into the aim, kick.
    public void Fired(LundunWeapons.Weapon w)
    {
        lastFire = Time.time;
        raise = Mathf.Max(raise, 0.85f);
        kick = Mathf.Min(1.6f, kick + Mathf.Clamp(w.recoilUp, 0.3f, 3.5f) * 0.45f);
    }

    /// The shotgun racks its pump (and throws out the spent shell).
    public void Pump() { pumpStart = Time.time; pumpEjected = false; }

    /// Let the animation have his arms for a moment (throwing a punch, being hit).
    public void Suspend(float seconds) { suspendUntil = Mathf.Max(suspendUntil, Time.time + seconds); }

    // ------------------------------------------------------------------ every frame, after the animation
    void LateUpdate()
    {
        if (!Ready) return;
        bool can = gun != null && player != null && player.enabled && player.gameObject.activeInHierarchy && !VehicleSystem.Locked
                   && Time.time >= suspendUntil && !(PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead);
        weight = Mathf.MoveTowards(weight, can ? 1f : 0f, Time.deltaTime * 6f);
        if (gun != null && gun.go.activeSelf != weight > 0.01f) gun.go.SetActive(weight > 0.01f);
        if (weight <= 0f || gun == null) return;

        bool wantUp = LundunAim.Aiming || Time.time - lastFire < lowerAfter;
        raise = Mathf.MoveTowards(raise, wantUp ? 1f : 0f, Time.deltaTime * (wantUp ? 8f : 2.5f));
        kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 9f) * Mathf.Exp(-14f * Time.deltaTime);
        float up = Ease(raise);

        Transform body = player.transform;
        Vector3 bFwd = body.forward, bRight = body.right, bUp = Vector3.up;

        // what the crosshair is on
        Camera cam = Camera.main;
        Vector3 aimPoint = body.position + bFwd * 50f + Vector3.up * 1.5f;
        if (cam != null)
        {
            Ray r = new Ray(cam.transform.position, cam.transform.forward);
            aimPoint = Physics.Raycast(r, out RaycastHit h, 150f, aimMask, QueryTriggerInteraction.Ignore) && h.distance > 2f
                ? h.point : r.GetPoint(60f);
        }

        // 1. the spine leans with the aim (up / down)
        Vector3 aimDir = (aimPoint - (body.position + Vector3.up * 1.45f)).normalized;
        float pitchUp = Mathf.Asin(Mathf.Clamp(aimDir.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lean = -pitchUp * spineAim * up * weight;
        Transform[] sp = { spine, chest, upperChest };
        int count = 0; foreach (Transform t in sp) if (t != null) count++;
        foreach (Transform t in sp) if (t != null) t.rotation = Quaternion.AngleAxis(lean / Mathf.Max(1, count), bRight) * t.rotation;

        // 1b. a bladed stance: the upper body turns into the gun, bringing the left shoulder forward
        float blade = gun.longGun ? Mathf.Lerp(bladeLongLow, bladeLongUp, up) : bladePistol * up;
        blade *= weight;
        foreach (Transform t in sp) if (t != null) t.rotation = Quaternion.AngleAxis(blade / Mathf.Max(1, count), bUp) * t.rotation;

        // 2. where the gun goes
        Vector3 shoulderR = rUpper.position, shoulderL = lUpper != null ? lUpper.position : shoulderR - bRight * 0.35f;
        Vector3 mid = (shoulderR + shoulderL) * 0.5f;
        Quaternion lowRot = Quaternion.AngleAxis(-24f, bUp) * Quaternion.AngleAxis(34f, bRight) * Quaternion.LookRotation(bFwd, bUp);
        Vector3 gunPos; Quaternion gunRot;
        if (gun.longGun)
        {
            Vector3 pocket = shoulderR - bRight * 0.07f + bFwd * 0.03f - bUp * 0.03f;
            Vector3 cheek = pocket + bUp * 0.06f;
            Quaternion upRot = Quaternion.LookRotation((aimPoint - cheek).normalized, bUp);
            gunRot = Quaternion.Slerp(lowRot, upRot, up);
            Vector3 stockL = gun.stock.localPosition;
            gunPos = Vector3.Lerp(pocket - bUp * 0.06f + bRight * 0.03f, pocket, up) - gunRot * stockL;
        }
        else
        {
            Vector3 upGrip = mid + bFwd * pistolReach + bRight * 0.05f - bUp * 0.02f;
            Vector3 lowGrip = mid + bFwd * 0.26f + bRight * 0.1f - bUp * 0.3f;
            Quaternion upRot = Quaternion.LookRotation((aimPoint - upGrip).normalized, bUp);
            Quaternion lowP = Quaternion.AngleAxis(-15f, bUp) * Quaternion.AngleAxis(48f, bRight) * Quaternion.LookRotation(bFwd, bUp);
            gunRot = Quaternion.Slerp(lowP, upRot, up);
            gunPos = Vector3.Lerp(lowGrip, upGrip, up);
        }

        // recoil kick: back and muzzle up
        gunPos -= gunRot * Vector3.forward * (kick * 0.035f);
        gunRot = gunRot * Quaternion.Euler(-kick * 4.5f, 0f, 0f);

        // reload: tilt the gun, swap the magazine (or load shells)
        Vector3 leftTarget = gun.fore != null ? gun.fore.position : gunPos;
        bool leftOnMag = false;
        float tilt = 0f;
        if (weapons != null && weapons.Reloading)
        {
            float p = weapons.ReloadProgress;
            tilt = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
            gunRot = gunRot * Quaternion.Euler(14f * tilt, 0f, -32f * tilt);
            gunPos -= bUp * (0.05f * tilt);
            leftOnMag = gun.magWell != null && p > 0.15f && p < 0.85f;
        }
        gun.go.transform.SetPositionAndRotation(gunPos, gunRot);
        AnimateParts(p: weapons != null && weapons.Reloading ? weapons.ReloadProgress : -1f, shellReload: weapons != null && weapons.Current.shellByShell);
        if (leftOnMag)
        {
            leftTarget = weapons.Current.shellByShell
                ? gun.magWell.position - gunRot * Vector3.up * (0.03f + 0.03f * Mathf.Abs(Mathf.Sin(Time.time * 9f)))
                : (gun.mag != null ? gun.mag.position : gun.magWell.position) - gunRot * Vector3.up * 0.04f;
        }

        // 3. arms onto the gun, hands round the grips
        Vector3 gF = gunRot * Vector3.forward, gU = gunRot * Vector3.up, gR = gunRot * Vector3.right;
        // right hand: on the grip, back of the hand to the right, knuckles down the front of the grip
        Vector3 rFinger = (gF * 0.75f - gU * 0.45f - gR * 0.2f).normalized;
        Vector3 rAcross = (-gU * 0.95f - gF * 0.3f).normalized;
        Vector3 rWrist = gunPos - gU * 0.04f - rFinger * 0.062f + gR * 0.018f + gunRot * rightHandNudge;
        PlaceArm(rUpper, rLower, rHand, rWrist, -bUp * 0.7f + bRight * 0.7f - bFwd * 0.2f, rMiddleP, rIndexP, rLittleP, rFinger, rAcross, rFingers, true);

        // left hand: under the handguard / on the foregrip / round the pistol grip / on the magazine
        Vector3 lFinger, lAcross, lWrist;
        if (leftOnMag)
        {
            lFinger = (gF * 0.4f + gR * 0.8f).normalized; lAcross = -gF;
            lWrist = leftTarget - lFinger * 0.06f - gR * 0.02f;
        }
        else if (gun.longGun && kind == LundunWeapons.Kind.Carbine)
        {
            lFinger = (gF * 0.55f + gR * 0.75f).normalized; lAcross = -gU;           // round the vertical foregrip
            lWrist = leftTarget - lFinger * 0.055f - gR * 0.025f + gU * 0.01f;
        }
        else if (gun.longGun)
        {
            lFinger = (gR * 0.85f + gF * 0.35f + gU * 0.2f).normalized; lAcross = -gF;  // palm up under the forend / pump
            lWrist = leftTarget - lFinger * 0.05f - gU * 0.035f - gR * 0.015f;
        }
        else
        {
            lFinger = (gF * 0.45f + gR * 0.55f - gU * 0.5f).normalized; lAcross = -gU;  // cupping the pistol grip
            lWrist = gunPos - gU * 0.06f - gR * 0.03f - lFinger * 0.05f;
        }
        lWrist += gunRot * leftHandNudge;
        PlaceArm(lUpper, lLower, lHand, lWrist, -bUp * 0.7f - bRight * 0.6f - bFwd * 0.2f, lMiddleP, lIndexP, lLittleP, lFinger, lAcross, lFingers, false);
    }

    void AnimateParts(float p, bool shellReload)
    {
        // magazine: out and back in during a reload
        if (gun.mag != null)
        {
            float drop = 0f;
            if (p >= 0f && !shellReload)
            {
                if (p < 0.2f) drop = 0f;
                else if (p < 0.4f) drop = Mathf.InverseLerp(0.2f, 0.4f, p);
                else if (p < 0.55f) drop = 1f;
                else if (p < 0.8f) drop = 1f - Mathf.InverseLerp(0.55f, 0.8f, p);
            }
            gun.mag.localPosition = gun.magRest + Vector3.down * (0.16f * drop) + Vector3.back * (0.03f * drop);
            bool show = drop < 0.98f;
            foreach (Renderer r in gun.mag.GetComponentsInChildren<Renderer>()) r.enabled = show;
        }
        // pump: back and forward after each shotgun shot (and the spent shell flies out at the back)
        if (gun.pump != null)
        {
            float t = Time.time - pumpStart, back = 0f;
            if (t >= 0f && t < 0.3f) back = t < 0.13f ? t / 0.13f : 1f - (t - 0.13f) / 0.17f;
            gun.pump.localPosition = gun.pumpRest + Vector3.back * (0.075f * Mathf.Clamp01(back));
            if (!pumpEjected && t >= 0.12f && t < 1f)
            {
                pumpEjected = true;
                if (gun.eject != null) LundunCombatFX.Eject(gun.eject, true);
            }
        }
    }

    /// Two-bone IK for the arm (blended by weight), then turn the hand to the grip and wrap the fingers.
    void PlaceArm(Transform upper, Transform lower, Transform hand, Vector3 wrist, Vector3 pole,
                  Transform middleP, Transform indexP, Transform littleP, Vector3 fingerDir, Vector3 across,
                  Transform[][] fingers, bool right)
    {
        if (upper == null || lower == null || hand == null) return;
        Vector3 target = Vector3.Lerp(hand.position, wrist, weight);
        TwoBone(upper, lower, hand, target, pole);
        if (middleP == null || indexP == null || littleP == null) return;

        // turn the hand: knuckles where we want them, then roll so the index is on top
        Quaternion before = hand.rotation;
        Vector3 curF = middleP.position - hand.position;
        hand.rotation = Quaternion.FromToRotation(curF, fingerDir) * hand.rotation;
        Vector3 curA = Vector3.ProjectOnPlane(littleP.position - indexP.position, fingerDir);
        Vector3 wantA = Vector3.ProjectOnPlane(across, fingerDir);
        if (curA.sqrMagnitude > 1e-8f && wantA.sqrMagnitude > 1e-8f)
            hand.rotation = Quaternion.FromToRotation(curA, wantA) * hand.rotation;
        hand.rotation = Quaternion.Slerp(before, hand.rotation, weight);

        // wrap the fingers round the grip
        Vector3 f = (middleP.position - hand.position).normalized;
        Vector3 a = (littleP.position - indexP.position).normalized;
        Vector3 palm = (right ? -1f : 1f) * Vector3.Cross(f, a).normalized;
        Vector3 axis = Vector3.Cross(f, palm).normalized;
        for (int i = 0; i < 4; i++)
        {
            Transform[] chain = fingers[i];
            if (chain == null) continue;
            bool trigger = right && i == 0;                                 // trigger finger stays straighter
            float[] bend = trigger ? new[] { 22f, 30f, 18f } : new[] { 55f, 70f, 45f };
            for (int j = 0; j < 3; j++)
                if (chain[j] != null) chain[j].rotation = Quaternion.AngleAxis(bend[j] * fingerCurl * weight, axis) * chain[j].rotation;
        }
    }

    static float Ease(float t) { return t * t * (3f - 2f * t); }

    static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    /// Two-bone IK (same maths as the bike rider): end lands on target, middle joint bends towards pole.
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
