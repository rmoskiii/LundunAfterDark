using System;
using UnityEngine;

/// Phase 5 (gate B): a CCTV recorder a mission puts somewhere (the yard cabin, the palace security room...). Walk up to
/// it and HOLD TO WIPE THE FOOTAGE (about three seconds): its own evidence is wiped (LundunEvidence), its lights go out,
/// and the mission hears about it (Wiped). Only this recorder's footage: never anything else James is on.
public class LundunRecorder : MonoBehaviour
{
    public string evidenceId = "";
    public float holdSeconds = 3f;
    public float reach = 1.6f;

    public bool IsWiped { get; private set; }
    public event Action Wiped;

    private float held;
    private Renderer screen, rec;
    private static Material caseMat, screenOn, screenOff, recOn, recOff;

    /// A recorder sitting on a desk/shelf at 'at' (the top it stands on), its front facing 'yaw', for this attempt.
    public static LundunRecorder Create(LundunMissionRun run, string evidenceId, Vector3 at, float yaw)
    {
        GameObject g = new GameObject("Recorder_" + evidenceId);
        g.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        LundunRecorder r = g.AddComponent<LundunRecorder>();
        r.evidenceId = evidenceId;
        r.Build();
        if (run != null) run.Spawn(g);
        return r;
    }

    void Update()
    {
        if (rec != null && !IsWiped) rec.enabled = Mathf.Repeat(Time.time, 1f) < 0.6f;     // the REC light blinks
        if (IsWiped) return;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot) { held = 0f; return; }
        Vector3 front = transform.position + transform.forward * 0.6f;
        if (LundunMissionRun.FlatDistance(ph.Body.position, front) > reach || Mathf.Abs(ph.Body.position.y - transform.position.y) > 2.2f) { held = 0f; return; }

        LundunHoldPrompt.Offer(this, "HOLD TO WIPE THE FOOTAGE", held / holdSeconds, new Color(0.62f, 0.08f, 0.1f, 0.92f));
        if (LundunHoldPrompt.Held(this))
        {
            held += Time.deltaTime;
            if (held >= holdSeconds) Wipe();
        }
        else held = Mathf.Max(0f, held - Time.deltaTime * 0.5f);
    }

    void Wipe()
    {
        IsWiped = true; held = 0f;
        LundunEvidence.Wiped(evidenceId);
        if (screen != null) screen.sharedMaterial = screenOff;
        if (rec != null) { rec.sharedMaterial = recOff; rec.enabled = true; }
        LundunHaptics.Medium();
        LundunMissions.Toast("Footage wiped");
        try { Wiped?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
    }

    // ------------------------------------------------------------------ the prop: a black recorder box, a little monitor
    void Build()
    {
        Mats();
        Part(PrimitiveType.Cube, "Case", new Vector3(0f, 0.05f, 0f), new Vector3(0.44f, 0.1f, 0.34f), caseMat, true);
        Part(PrimitiveType.Cube, "Monitor", new Vector3(0f, 0.3f, -0.04f), new Vector3(0.42f, 0.3f, 0.06f), caseMat, false);
        screen = Part(PrimitiveType.Cube, "Screen", new Vector3(0f, 0.3f, -0.008f), new Vector3(0.38f, 0.26f, 0.005f), screenOn, false).GetComponent<Renderer>();
        Part(PrimitiveType.Cube, "Stand", new Vector3(0f, 0.13f, -0.04f), new Vector3(0.05f, 0.08f, 0.05f), caseMat, false);
        rec = Part(PrimitiveType.Sphere, "RecLight", new Vector3(0.17f, 0.07f, 0.171f), new Vector3(0.02f, 0.02f, 0.02f), recOn, false).GetComponent<Renderer>();
    }

    GameObject Part(PrimitiveType t, string name, Vector3 at, Vector3 size, Material m, bool keepCollider)
    {
        GameObject g = GameObject.CreatePrimitive(t);
        g.name = name;
        if (!keepCollider) { Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c); }
        g.transform.SetParent(transform, false);
        g.transform.localPosition = at; g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static void Mats()
    {
        if (caseMat != null) return;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Shader u = unlit != null ? unlit : lit;
        caseMat = Make(lit, new Color(0.06f, 0.06f, 0.07f));
        screenOn = Make(u, new Color(0.55f, 0.6f, 0.62f));      // a grey picture: the cameras' feed
        screenOff = Make(u, new Color(0.02f, 0.02f, 0.03f));
        recOn = Make(u, new Color(1f, 0.1f, 0.08f));
        recOff = Make(u, new Color(0.1f, 0.02f, 0.02f));
    }

    static Material Make(Shader s, Color c)
    {
        Material m = new Material(s);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }
}
