using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// A1: aim assist and aiming feel, on foot. Default strength: Standard.
///   • slowdown: turning gets a little stickier while the crosshair passes over a person (anyone)
///   • follow:   while you're actively aiming or firing, it gently tracks a moving HOSTILE in the crosshair
///   • forgiveness: a near-miss on a HOSTILE still lands (a small cone that tightens with distance)
///   • the crosshair turns red over a hostile
/// Hostile = police officers chasing James, and anyone squaring up to him or pulling a gun (LundunAngryDriver).
/// Ordinary pedestrians get only the slowdown: they're never tracked or magnetised (free aim).
/// Nothing helps through walls or cars. Added automatically by CameraFollow.
public class LundunAimAssist : MonoBehaviour
{
    public static LundunAimAssist Instance { get; private set; }

    public enum Strength { Off, Light, Standard, Strong }

    [SerializeField] private Strength strength = Strength.Standard;
    [SerializeField] private float range = 70f;
    [Tooltip("Where on a person we aim (metres above their feet)")]
    [SerializeField] private float chestHeight = 1.25f;
    [SerializeField] private Color hostileCrosshair = new Color(1f, 0.25f, 0.2f, 1f);
    [Tooltip("Auto-fire: how long the crosshair must be red before it shoots")]
    [SerializeField] private float autoFireDelay = 0.12f;

    //                                         Off   Light  Standard  Strong
    static readonly float[] SlowMin       = { 1f,   0.78f, 0.62f,    0.48f };   // look speed right on a target
    static readonly float[] SlowRadiusM   = { 0f,   0.8f,  1.1f,     1.4f };    // how wide the sticky zone is (metres at the target)
    static readonly float[] FollowDegSec  = { 0f,   9f,    16f,      26f };     // tracking help for a moving hostile
    static readonly float[] MagnetRadiusM = { 0f,   0.3f,  0.55f,    0.85f };   // near-miss forgiveness (metres at the target)
    static readonly float[] SnapDeg       = { 0f,   8f,    12f,      17f };     // A2: going into aim snaps to a threat this close to the crosshair

    public Strength Level { get { return strength; } set { strength = value; } }
    /// Multiply look input by this (CameraFollow does).
    public float LookScale { get; private set; } = 1f;
    /// The person nearest the crosshair right now (null if none).
    public Health Hover { get; private set; }
    public bool HoverHostile { get; private set; }
    /// A3 auto-fire: the crosshair has been on a threat (red) long enough to shoot by itself.
    public bool AutoFireReady { get { return onHostileSince > 0f && Time.time - onHostileSince >= autoFireDelay; } }

    class Candidate { public Health h; public Transform t; public bool hostile; }
    private readonly List<Candidate> candidates = new List<Candidate>();
    private static readonly Collider[] found = new Collider[96];
    private Camera cam;
    private Transform player;
    private VehicleSystem vehicles;
    private Graphic crosshair;
    private Color crosshairColour = Color.white;
    private int npcMask, losMask;
    private float nextScan;
    private Vector3 hoverAim;
    private float hoverRatio = 99f;
    private static float lastShot = -99f;
    private float onHostileSince = -1f;
    // A2: snapping to a threat (going into aim, or switching targets)
    private Candidate snap, current;
    private float snapUntil;

    public static void NotifyShot() { lastShot = Time.time; }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        strength = (Strength)LundunSettings.AssistLevel;        // A3: AIM ASSIST setting
        vehicles = FindAnyObjectByType<VehicleSystem>();
        PlayerController pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) player = pc.transform;
        npcMask = LayerMask.GetMask("NPC");
        if (npcMask == 0) npcMask = ~0;
        losMask = ~LayerMask.GetMask("Player", "NPC", "UI");

        // the older AimAssist (scene component) pulled toward anyone, all the time: this replaces it
        foreach (AimAssist old in FindObjectsByType<AimAssist>(FindObjectsSortMode.None)) old.enabled = false;

        GameObject ch = GameObject.Find("Crosshair");
        if (ch != null) { crosshair = ch.GetComponent<Graphic>(); if (crosshair != null) crosshairColour = crosshair.color; }

        // the fire button doubles as an aim pad: hold to shoot, drag to steer
        GameObject fire = GameObject.Find("FireButton");
        TouchLookArea look = FindAnyObjectByType<TouchLookArea>();
        if (fire != null && look != null && fire.GetComponent<LundunFireAim>() == null)
            fire.AddComponent<LundunFireAim>().Init(look);
        // A2: the AIM button beside it
        if (fire != null) LundunAim.Build((RectTransform)fire.transform, look);
    }

    bool Active()
    {
        // Off still spots threats (red crosshair, auto-fire); it just gives no help (every help value is zero/one at Off)
        return cam != null && player != null && player.gameObject.activeInHierarchy
               && !(vehicles != null && vehicles.IsDriving);
    }

    void Update()
    {
        LookScale = 1f; Hover = null; HoverHostile = false; hoverRatio = 99f;
        if (!Active()) { SetCrosshair(false); onHostileSince = -1f; return; }

        if (Time.time >= nextScan) { nextScan = Time.time + 0.1f; Scan(); }

        int s = (int)strength;
        Vector3 cp = cam.transform.position, fwd = cam.transform.forward;
        Candidate best = null; Vector3 bestAim = Vector3.zero;
        foreach (Candidate c in candidates)
        {
            if (c.h == null || c.h.IsDead || c.t == null) continue;
            Vector3 aim = c.t.position + Vector3.up * chestHeight;
            Vector3 to = aim - cp;
            float d = to.magnitude;
            if (d < 1f || d > range) continue;
            float cone = Mathf.Atan2(Mathf.Max(SlowRadiusM[s], 0.8f), d) * Mathf.Rad2Deg;
            float ang = Vector3.Angle(fwd, to);
            float ratio = ang / Mathf.Max(0.01f, cone);
            if (ratio >= 1f || ratio >= hoverRatio) continue;
            if (Physics.Raycast(cp, to / d, d - 0.6f, losMask, QueryTriggerInteraction.Ignore)) continue;    // behind a wall or car
            hoverRatio = ratio; best = c; bestAim = aim;
        }
        if (best != null)
        {
            Hover = best.h; HoverHostile = best.hostile; hoverAim = bestAim;
            if (best.hostile) current = best;
            LookScale = Mathf.Lerp(SlowMin[s], 1f, hoverRatio * hoverRatio);
        }
        bool onHostile = HoverHostile && hoverRatio < 0.7f;
        SetCrosshair(onHostile);
        if (!onHostile) onHostileSince = -1f;
        else if (onHostileSince < 0f) onHostileSince = Time.time;
    }

    /// Tracking help for a moving hostile, while you're aiming or firing. Returns (yaw, pitch) change in degrees.
    public Vector2 FollowStep(float yaw, float pitch)
    {
        if (!Active()) { snap = null; return Vector2.zero; }

        // A2: a quick, smooth turn onto the threat we just snapped to
        if (snap != null)
        {
            if (Time.time > snapUntil || !Usable(snap) || !LundunAim.Aiming) snap = null;
            else
            {
                Vector2 want = AnglesTo(AimPoint(snap));
                float sy = Mathf.DeltaAngle(yaw, want.x), sp = Mathf.DeltaAngle(pitch, want.y);
                if (Mathf.Abs(sy) < 0.3f && Mathf.Abs(sp) < 0.3f) snap = null;
                float k = 1f - Mathf.Exp(-22f * Time.deltaTime);
                return new Vector2(sy * k, sp * k);
            }
        }

        if (Hover == null || !HoverHostile) return Vector2.zero;
        bool aiming = LundunAim.Aiming || Time.time - TouchLookArea.LastDragTime < 0.35f || Time.time - lastShot < 0.3f;
        if (!aiming) return Vector2.zero;
        Vector3 to = hoverAim - cam.transform.position;
        float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        float dy = Mathf.DeltaAngle(yaw, wantYaw), dp = Mathf.DeltaAngle(pitch, wantPitch);
        float maxStep = FollowDegSec[(int)strength] * Time.deltaTime * (1f - hoverRatio * 0.5f);
        return new Vector2(Mathf.Clamp(dy, -maxStep, maxStep), Mathf.Clamp(dp, -maxStep, maxStep) * 0.6f);
    }

    /// Where a shot from the camera should go: straight ahead, or onto a hostile you only just missed.
    public Vector3 ShotDirection(Vector3 origin, Vector3 forward) { return ShotDirection(origin, forward, 1f); }

    /// G1: scale = how forgiving this weapon is (spray / shotgun a little more, carbine a little less).
    public Vector3 ShotDirection(Vector3 origin, Vector3 forward, float scale)
    {
        if (!Active()) return forward;
        int s = (int)strength;
        Vector3 bestDir = forward; float bestRatio = 1f;
        foreach (Candidate c in candidates)
        {
            if (!c.hostile || c.h == null || c.h.IsDead || c.t == null) continue;
            Vector3 aim = c.t.position + Vector3.up * chestHeight;
            Vector3 to = aim - origin;
            float d = to.magnitude;
            if (d < 1f || d > range) continue;
            float cone = Mathf.Atan2(MagnetRadiusM[s] * scale, d) * Mathf.Rad2Deg;
            float ratio = Vector3.Angle(forward, to) / Mathf.Max(0.01f, cone);
            if (ratio >= bestRatio) continue;
            if (Physics.Raycast(origin, to / d, d - 0.6f, losMask, QueryTriggerInteraction.Ignore)) continue;
            bestRatio = ratio; bestDir = to / d;
        }
        return bestDir;
    }

    // ------------------------------------------------------------------ A2: snap and switch
    bool Usable(Candidate c) { return c != null && c.h != null && !c.h.IsDead && c.t != null; }
    Vector3 AimPoint(Candidate c) { return c.t.position + Vector3.up * chestHeight; }

    Vector2 AnglesTo(Vector3 point)
    {
        Vector3 to = point - cam.transform.position;
        return new Vector2(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg,
                           -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg);
    }

    bool Visible(Candidate c, out float angle, out float signedYaw)
    {
        angle = 999f; signedYaw = 0f;
        if (!Usable(c) || !c.hostile) return false;
        Vector3 cp = cam.transform.position, to = AimPoint(c) - cp;
        float d = to.magnitude;
        if (d < 1f || d > range) return false;
        angle = Vector3.Angle(cam.transform.forward, to);
        Vector3 f = cam.transform.forward; f.y = 0f; Vector3 t = to; t.y = 0f;
        signedYaw = Vector3.SignedAngle(f, t, Vector3.up);
        return !Physics.Raycast(cp, to / d, d - 0.6f, losMask, QueryTriggerInteraction.Ignore);
    }

    /// Going into aim: turn onto the threat closest to the crosshair, if one's near enough. Pedestrians never.
    public bool SnapToBest()
    {
        if (!Active()) return false;
        if (Time.time >= nextScan) { nextScan = Time.time + 0.1f; Scan(); }
        float cone = SnapDeg[(int)strength];
        Candidate best = null; float bestAng = cone;
        foreach (Candidate c in candidates)
            if (Visible(c, out float ang, out _) && ang < bestAng) { bestAng = ang; best = c; }
        if (best == null || cone <= 0f) return false;
        StartSnap(best);
        return true;
    }

    /// Next threat to the right of the current one (wraps round to the far left). Only threats on screen.
    public bool SwitchTarget()
    {
        if (!Active() || strength == Strength.Off) return false;
        float half = HalfWidthDeg();
        float from = 0f;
        if (Usable(current) && Visible(current, out _, out float cy)) from = cy;
        Candidate right = null, wrap = null; float rightYaw = 999f, wrapYaw = 999f;
        foreach (Candidate c in candidates)
        {
            if (c == current || !Visible(c, out float ang, out float sy) || Mathf.Abs(sy) > half) continue;
            if (sy > from + 0.5f) { if (sy < rightYaw) { rightYaw = sy; right = c; } }
            else if (sy < wrapYaw) { wrapYaw = sy; wrap = c; }
        }
        Candidate next = right != null ? right : wrap;
        if (next == null) return false;
        StartSnap(next);
        return true;
    }

    /// How many threats on screen besides the one you're on (shows the next-target button).
    public int OtherThreatsInView()
    {
        if (!Active() || cam == null || strength == Strength.Off) return 0;
        float half = HalfWidthDeg();
        int n = 0;
        foreach (Candidate c in candidates)
            if (c != current && Visible(c, out _, out float sy) && Mathf.Abs(sy) <= half) n++;
        return n;
    }

    float HalfWidthDeg() { return Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg * 0.95f; }

    void StartSnap(Candidate c) { snap = c; current = c; snapUntil = Time.time + 0.35f; }

    void Scan()
    {
        Health keepCurrent = Usable(current) ? current.h : null, keepSnap = Usable(snap) ? snap.h : null;
        current = null; snap = null;
        candidates.Clear();
        int n = Physics.OverlapSphereNonAlloc(player.position, range, found, npcMask, QueryTriggerInteraction.Ignore);
        WantedSystem w = WantedSystem.Instance;
        for (int i = 0; i < n; i++)
        {
            Health h = found[i].GetComponentInParent<Health>();
            if (h == null || h.IsDead) continue;
            bool dup = false;
            foreach (Candidate c in candidates) if (c.h == h) { dup = true; break; }
            if (dup) continue;
            PoliceOfficer cop = h.GetComponent<PoliceOfficer>();
            NPCBrain ped = h.GetComponent<NPCBrain>();
            if (cop == null && ped == null) continue;                       // people only
            LundunAngryDriver angry = h.GetComponent<LundunAngryDriver>();
            bool hostile = (cop != null && (cop.IsChasing || (w != null && w.Stars > 0))) || (angry != null && angry.enabled);
            Candidate nc = new Candidate { h = h, t = h.transform, hostile = hostile };
            candidates.Add(nc);
            if (h == keepCurrent) current = nc;
            if (h == keepSnap) snap = nc;
        }
    }

    void SetCrosshair(bool onHostile)
    {
        if (crosshair == null) return;
        Color want = onHostile ? hostileCrosshair : crosshairColour;
        if (crosshair.color != want) crosshair.color = want;
    }
}
