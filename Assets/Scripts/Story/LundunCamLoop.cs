using System;
using System.Collections.Generic;
using UnityEngine;

/// Phase 5 (gate D): Leah's loop (M12, if the palace's control room was spotted from the Wheel in M11). Once per
/// attempt, while James is inside and nobody's raised the alarm: HOLD for LEAH: LOOP THE CAMERAS. She feeds the
/// palace's cameras the same empty minute over and over: they're blind for 45 seconds (a countdown at the top), then
/// back. Guards' torches still count.
public class LundunCamLoop : MonoBehaviour
{
    public Func<List<LundunMissionCam>> cameras;
    public Func<bool> allowed;
    public float seconds = 45f;

    int uses = 1;
    float held, until;
    bool looping;

    public static LundunCamLoop Create(LundunMissionRun run, Func<List<LundunMissionCam>> cams, Func<bool> ok)
    {
        GameObject g = new GameObject("LundunCamLoop");
        if (run != null) run.Spawn(g);
        LundunCamLoop l = g.AddComponent<LundunCamLoop>();
        l.cameras = cams; l.allowed = ok;
        return l;
    }

    void Update()
    {
        if (looping)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, until - Time.time));
            LundunHoldPrompt.Timer("CAMERAS LOOPED  <color=#FFA82E>0:" + s.ToString("00") + "</color>");
            if (Time.time >= until) Stop();
            return;
        }
        if (uses <= 0 || (allowed != null && !allowed())) { held = 0f; return; }
        LundunHoldPrompt.Offer(this, "LEAH: LOOP THE CAMERAS", held / 0.6f, new Color(0.1f, 0.32f, 0.4f, 0.92f));
        if (LundunHoldPrompt.Held(this))
        {
            held += Time.deltaTime;
            if (held >= 0.6f) Loop();
        }
        else held = Mathf.Max(0f, held - Time.deltaTime);
    }

    void Loop()
    {
        held = 0f; uses--;
        looping = true; until = Time.time + seconds;
        List<LundunMissionCam> cs = cameras != null ? cameras() : null;
        if (cs != null) foreach (LundunMissionCam c in cs) if (c != null) c.SetPower(false);
        LundunHaptics.Medium();
        LundunMissions.Say("LEAH", "(phone) I'm in their control room feed. Looping it... now. You've got forty-five seconds. Go.", 4f);
    }

    void Stop()
    {
        looping = false;
        List<LundunMissionCam> cs = cameras != null ? cameras() : null;
        if (cs != null) foreach (LundunMissionCam c in cs) if (c != null && !c.Destroyed) c.SetPower(true);
        LundunMissions.Toast("The loop's over: the cameras are back");
    }

    void OnDestroy()
    {
        if (!looping) return;
        List<LundunMissionCam> cs = cameras != null ? cameras() : null;
        if (cs != null) foreach (LundunMissionCam c in cs) if (c != null) c.SetPower(true);
    }
}
