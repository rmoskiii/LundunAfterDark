using UnityEngine;

/// E8: one of Kingsmere's guards (built by Lundun → 29 from Generator.GuardParts: a body and four limbs as children named
/// Body, LegL, LegR, ArmL, ArmR, the legs pivoting at the hip and the arms at the shoulder).
///   standing his post   stock still
///   marching his beat   march distance metres forward at a slow ceremonial pace, halt, about-turn, march back, again
///   alerted             (LundunRoyalQuarter) he stops and turns to face the trespasser until they've gone, then goes back
/// Animated only near the camera, so eight of them cost nothing elsewhere.
public class LundunRoyalGuard : MonoBehaviour
{
    [Tooltip("Metres he marches out and back (0 = stands his post)")]
    [SerializeField] private float march = 0f;
    [SerializeField] private float pace = 1.05f;            // m/s, slow and ceremonial
    [SerializeField] private float stepsPerSecond = 1.6f;
    [SerializeField] private float legSwing = 26f, armSwing = 32f;
    [SerializeField] private float turnTime = 1.4f, haltTime = 1.2f;
    [SerializeField] private float animateWithin = 140f;

    Transform legL, legR, armL, armR;
    Vector3 home; float homeYaw;
    float along, dir = 1f, phase, timer;
    enum State { Post, Marching, Halting, Turning, Alert, Returning }
    State state;
    float turnFrom, turnTo;
    Transform alertOn;
    float alertUntil;

    public float March { get { return march; } set { march = Mathf.Max(0f, value); } }

    void Awake()
    {
        legL = transform.Find("LegL"); legR = transform.Find("LegR");
        armL = transform.Find("ArmL"); armR = transform.Find("ArmR");
        home = transform.position; homeYaw = transform.eulerAngles.y;
        state = march > 0.5f ? State.Marching : State.Post;
    }

    /// LundunRoyalQuarter: someone's where they shouldn't be. Face them for a while (renewed while they stay).
    public void Alert(Transform who, float forSeconds)
    {
        if (who == null) return;
        alertOn = who; alertUntil = Time.time + forSeconds;
        if (state != State.Alert) { state = State.Alert; Limbs(0f); }
    }

    void Update()
    {
        Camera cam = Camera.main;
        if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > animateWithin * animateWithin && state != State.Alert) return;
        float dt = Time.deltaTime;
        switch (state)
        {
            case State.Post:
                Limbs(0f);
                break;

            case State.Marching:
                along += pace * dt * dir;
                phase += dt * stepsPerSecond * Mathf.PI;
                Limbs(Mathf.Sin(phase));
                if ((dir > 0f && along >= march) || (dir < 0f && along <= 0f))
                {
                    along = Mathf.Clamp(along, 0f, march);
                    state = State.Halting; timer = haltTime; Limbs(0f);
                }
                Place();
                break;

            case State.Halting:
                timer -= dt; Limbs(0f);
                if (timer <= 0f)
                {
                    state = State.Turning; timer = 0f;
                    turnFrom = transform.eulerAngles.y; turnTo = turnFrom + 180f;
                }
                break;

            case State.Turning:
                timer += dt / turnTime;
                transform.rotation = Quaternion.Euler(0f, Mathf.Lerp(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, timer)), 0f);
                Limbs(Mathf.Sin(timer * Mathf.PI * 2f) * 0.3f);        // the about-turn's little stamp
                if (timer >= 1f) { dir = -dir; phase = 0f; state = State.Marching; }
                break;

            case State.Alert:
                Limbs(0f);
                if (alertOn != null)
                {
                    Vector3 d = alertOn.position - transform.position; d.y = 0f;
                    if (d.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 120f * dt);
                }
                if (Time.time > alertUntil || alertOn == null) { state = State.Returning; alertOn = null; }
                break;

            case State.Returning:
                Quaternion want = Quaternion.Euler(0f, homeYaw + (dir < 0f ? 180f : 0f), 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 90f * dt);
                if (Quaternion.Angle(transform.rotation, want) < 1f)
                {
                    transform.rotation = want;
                    state = march > 0.5f ? State.Marching : State.Post;
                }
                break;
        }
    }

    void Place()
    {
        Quaternion q = Quaternion.Euler(0f, homeYaw, 0f);
        transform.position = home + q * Vector3.forward * along;
    }

    void Limbs(float s)
    {
        if (legL != null) legL.localRotation = Quaternion.Euler(s * legSwing, 0f, 0f);
        if (legR != null) legR.localRotation = Quaternion.Euler(-s * legSwing, 0f, 0f);
        if (armL != null) armL.localRotation = Quaternion.Euler(-s * armSwing, 0f, 0f);
        if (armR != null) armR.localRotation = Quaternion.Euler(s * armSwing, 0f, 0f);
    }
}
