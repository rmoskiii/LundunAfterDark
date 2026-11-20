using System;
using System.Collections.Generic;
using UnityEngine;

/// Phase 5 (gate C3): M09's ally, while James sneaks round Hollis's yard.
///   KEMI     stays by the car and watches the yard from the fence: when a guard's coming his way she says so
///            ("(phone) Guard, your left, coming this way"), no more often than every eight seconds
///   MARCUS   comes over the fence with him, and once a night can put a guard down quietly: James close behind a
///            guard who hasn't seen him (within 3.5 m, outside his torch): HOLD for MARCUS: TAKE HIM DOWN. The
///            guard drops, his torch goes out, and he's out of the count (he never wakes in this attempt)
/// Stops when the alarm goes (Active false).
public class LundunYardHelp : MonoBehaviour
{
    public string ally = "KEMI";
    public Func<List<LundunGuard>> guards;
    public Func<bool> active;
    /// MARCUS: a guard he put down (the mission takes him off its lists).
    public event Action<LundunGuard> TookDown;

    float nextCall, held;
    int takedowns = 1;
    readonly Dictionary<LundunGuard, float> calledAbout = new Dictionary<LundunGuard, float>();

    public static LundunYardHelp Create(LundunMissionRun run, string who, Func<List<LundunGuard>> gs, Func<bool> isOn)
    {
        GameObject g = new GameObject("LundunYardHelp");
        if (run != null) run.Spawn(g);
        LundunYardHelp h = g.AddComponent<LundunYardHelp>();
        h.ally = who; h.guards = gs; h.active = isOn;
        return h;
    }

    void Update()
    {
        if (active != null && !active()) return;
        Transform j = LundunMissionRun.James();
        List<LundunGuard> gs = guards != null ? guards() : null;
        if (j == null || gs == null || LundunMissionRun.Driving()) return;
        if (ally == "KEMI") Lookout(j, gs);
        else if (ally == "MARCUS") Takedown(j, gs);
    }

    // ------------------------------------------------------------------ Kemi on the phone
    void Lookout(Transform j, List<LundunGuard> gs)
    {
        if (Time.time < nextCall || LundunMissions.Talking) return;
        LundunGuard best = null; float bd = 16f;
        foreach (LundunGuard g in gs)
        {
            if (g == null || !g.enabled) continue;
            float d = LundunMissionRun.FlatDistance(g.transform.position, j.position);
            if (d > bd) continue;
            Vector3 toJ = j.position - g.transform.position; toJ.y = 0f;
            if (Vector3.Angle(g.transform.forward, toJ) > 70f) continue;              // only one heading his way
            float last;
            if (calledAbout.TryGetValue(g, out last) && Time.time - last < 20f) continue;
            best = g; bd = d;
        }
        if (best == null) return;
        calledAbout[best] = Time.time;
        nextCall = Time.time + 8f;
        Vector3 rel = j.InverseTransformPoint(best.transform.position);
        string where = Mathf.Abs(rel.x) > Mathf.Abs(rel.z) ? (rel.x < 0f ? "your left" : "your right") : (rel.z > 0f ? "in front of you" : "behind you");
        string[] lines =
        {
            "(phone) Guard, " + where + ", coming this way.",
            "(phone) Torch " + where + ". Get down.",
            "(phone) James. " + char.ToUpper(where[0]) + where.Substring(1) + ". He's walking straight at you.",
        };
        LundunMissions.Say("KEMI", lines[UnityEngine.Random.Range(0, lines.Length)], 3f);
    }

    // ------------------------------------------------------------------ Marcus, once
    void Takedown(Transform j, List<LundunGuard> gs)
    {
        if (takedowns <= 0) return;
        LundunGuard target = null; float bd = 3.5f;
        foreach (LundunGuard g in gs)
        {
            if (g == null || !g.enabled) continue;
            float d = LundunMissionRun.FlatDistance(g.transform.position, j.position);
            if (d > bd || g.Sees(j.position) > 0f) continue;                            // close, and not in his light
            target = g; bd = d;
        }
        if (target == null) { held = 0f; return; }
        LundunHoldPrompt.Offer(this, "MARCUS: TAKE HIM DOWN", held / 0.8f, new Color(0.45f, 0.1f, 0.12f, 0.92f));
        if (LundunHoldPrompt.Held(this))
        {
            held += Time.deltaTime;
            if (held >= 0.8f) Drop(target);
        }
        else held = Mathf.Max(0f, held - Time.deltaTime);
    }

    void Drop(LundunGuard g)
    {
        held = 0f; takedowns--;
        g.enabled = false;
        foreach (Light l in g.GetComponentsInChildren<Light>(true)) l.enabled = false;
        // down on the ground
        Animator a = g.GetComponentInChildren<Animator>();
        if (a != null) a.enabled = false;
        Transform t = g.transform;
        t.rotation = Quaternion.LookRotation(Vector3.up, -t.forward) * Quaternion.Euler(0f, 0f, 0f);
        t.position += Vector3.up * 0.25f;
        foreach (Collider c in g.GetComponentsInChildren<Collider>()) c.enabled = false;
        LundunHaptics.Medium();
        LundunMissions.Say("MARCUS", "(quietly) Sleep tight.", 2.5f);
        LundunMissions.Toast("Marcus put him down. Quietly.\nHe can only do that once");
        try { TookDown?.Invoke(g); } catch (Exception e) { Debug.LogException(e); }
    }
}
