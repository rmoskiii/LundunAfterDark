using System;
using System.Collections.Generic;
using UnityEngine;

/// Phase 5 (gate B): a fuse box a mission puts on a wall. Walk up to it (on foot) and HOLD TO CUT THE POWER: its own
/// cameras and floodlights (the ones the mission handed it, never anything else in the city) go dark for 60 seconds,
/// with a countdown at the top of the screen, then come back on by themselves. Once per attempt unless the mission
/// gives it more uses.
public class LundunFuseBox : MonoBehaviour
{
    public float seconds = 60f;
    public float holdSeconds = 1.5f;
    public float reach = 1.8f;
    public int uses = 1;

    public readonly List<LundunMissionCam> cameras = new List<LundunMissionCam>();
    public readonly List<Light> lights = new List<Light>();
    public readonly List<Renderer> glows = new List<Renderer>();    // lamp heads that glow: hidden while the power's off

    public bool Off { get; private set; }
    public float Left { get { return Off ? Mathf.Max(0f, until - Time.time) : 0f; } }
    public event Action PowerCut, PowerBack;

    private float held, until;
    private Renderer lamp;
    private static Material boxMat, signMat, lampOn, lampOff;

    /// A fuse box on a wall at 'at', facing 'yaw' (its door faces this way), for this attempt.
    public static LundunFuseBox Create(LundunMissionRun run, Vector3 at, float yaw, IEnumerable<LundunMissionCam> cams = null, IEnumerable<Light> lamps = null)
    {
        GameObject g = new GameObject("FuseBox");
        g.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        LundunFuseBox f = g.AddComponent<LundunFuseBox>();
        if (cams != null) f.cameras.AddRange(cams);
        if (lamps != null) f.lights.AddRange(lamps);
        f.Build();
        if (run != null) run.Spawn(g);
        return f;
    }

    void Update()
    {
        if (Off)
        {
            int s = Mathf.CeilToInt(Left);
            LundunHoldPrompt.Timer("POWER CUT  <color=#FFA82E>" + (s / 60) + ":" + (s % 60).ToString("00") + "</color>");
            if (Time.time >= until) Restore();
            return;
        }
        if (uses <= 0) return;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot) { held = 0f; return; }
        Vector3 front = transform.position + transform.forward * 0.6f;
        if (LundunMissionRun.FlatDistance(ph.Body.position, front) > reach || Mathf.Abs(ph.Body.position.y - transform.position.y) > 2.5f) { held = 0f; return; }

        LundunHoldPrompt.Offer(this, "HOLD TO CUT THE POWER", held / holdSeconds, new Color(0.12f, 0.16f, 0.29f, 0.92f));
        if (LundunHoldPrompt.Held(this))
        {
            held += Time.deltaTime;
            if (held >= holdSeconds) Cut();
        }
        else held = Mathf.Max(0f, held - Time.deltaTime);
    }

    void Cut()
    {
        held = 0f; uses--;
        Off = true; until = Time.time + seconds;
        foreach (LundunMissionCam c in cameras) if (c != null) c.SetPower(false);
        foreach (Light l in lights) if (l != null) l.enabled = false;
        foreach (Renderer r in glows) if (r != null) r.enabled = false;
        if (lamp != null) lamp.sharedMaterial = lampOff;
        LundunHaptics.Medium();
        LundunMissions.Toast("Power's out\nCameras and floodlights down for " + Mathf.RoundToInt(seconds) + "s");
        try { PowerCut?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
    }

    void Restore()
    {
        Off = false;
        foreach (LundunMissionCam c in cameras) if (c != null) c.SetPower(true);
        foreach (Light l in lights) if (l != null) l.enabled = true;
        foreach (Renderer r in glows) if (r != null) r.enabled = true;
        if (lamp != null) lamp.sharedMaterial = lampOn;
        LundunMissions.Toast("The power's back on");
        try { PowerBack?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
    }

    /// The attempt's over with the power off: everything back on (the cameras and lights may outlive this box).
    void OnDestroy() { if (Off) { foreach (LundunMissionCam c in cameras) if (c != null) c.SetPower(true); foreach (Light l in lights) if (l != null) l.enabled = true; foreach (Renderer r in glows) if (r != null) r.enabled = true; } }

    // ------------------------------------------------------------------ the prop: a grey steel box, a yellow sign, a lamp
    void Build()
    {
        Mats();
        Part(PrimitiveType.Cube, "Box", new Vector3(0f, 1.35f, 0.12f), new Vector3(0.6f, 0.8f, 0.24f), boxMat, true);
        Part(PrimitiveType.Cube, "Door", new Vector3(0f, 1.35f, 0.245f), new Vector3(0.54f, 0.72f, 0.02f), boxMat, false);
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 1.52f, 0.258f), new Vector3(0.2f, 0.16f, 0.005f), signMat, false);
        Part(PrimitiveType.Cube, "Handle", new Vector3(0.2f, 1.3f, 0.265f), new Vector3(0.03f, 0.12f, 0.03f), boxMat, false);
        Part(PrimitiveType.Cylinder, "Conduit", new Vector3(0f, 0.5f, 0.1f), new Vector3(0.07f, 0.47f, 0.07f), boxMat, false);
        GameObject l = Part(PrimitiveType.Sphere, "Lamp", new Vector3(-0.2f, 1.62f, 0.26f), new Vector3(0.05f, 0.05f, 0.05f), lampOn, false);
        lamp = l.GetComponent<Renderer>();
    }

    GameObject Part(PrimitiveType t, string name, Vector3 at, Vector3 size, Material m, bool keepCollider)
    {
        GameObject g = GameObject.CreatePrimitive(t);
        g.name = name;
        if (!keepCollider) { Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c); }
        g.transform.SetParent(transform, false);
        g.transform.localPosition = at; g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>(); r.sharedMaterial = m;
        return g;
    }

    static void Mats()
    {
        if (boxMat != null) return;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        boxMat = Make(lit, new Color(0.42f, 0.45f, 0.44f));
        signMat = Make(unlit != null ? unlit : lit, new Color(1f, 0.8f, 0.1f));
        lampOn = Make(unlit != null ? unlit : lit, new Color(0.3f, 1f, 0.35f));
        lampOff = Make(unlit != null ? unlit : lit, new Color(0.12f, 0.03f, 0.03f));
    }

    static Material Make(Shader s, Color c)
    {
        Material m = new Material(s);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }
}
