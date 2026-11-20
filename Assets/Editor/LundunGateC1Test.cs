// Phase 5 (gate C1) test rig, in Play mode (Lundun > Test: Phase 5 gate C1):
//     Rider on a motorbike    a Rye Lane Rider on a bike, 60 m behind James, chasing him (as in M06)
//     Leah, here              Leah in her control-room look, in front of James (to check the jacket and lanyard)
//     Clear                   removes them
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class LundunGateC1Test
{
    static readonly List<GameObject> made = new List<GameObject>();

    static Transform James()
    {
        if (!Application.isPlaying) { Debug.Log("[Lundun] Press Play first."); return null; }
        return LundunMissionRun.James();
    }

    [MenuItem("Lundun/Test: Phase 5 gate C1/Rider on a motorbike", false, 70)]
    static void Bike()
    {
        Transform j = James();
        LundunPopulation pop = LundunPopulation.Instance;
        if (j == null || pop == null) return;
        Vector3 back = -j.forward; back.y = 0f; back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
        Vector3 pos; Quaternion rot;
        if (!LundunStoryPlaces.Kerb(j.position + back * 60f, out pos, out rot)) { pos = LundunMissionRun.Ground(j.position + back * 30f); rot = Quaternion.LookRotation(-back); }
        GameObject g = pop.NewCar(null, pos, rot, 14);
        if (g == null) return;
        LundunMotorbike.Dress(g);
        LundunChaseCar cc = g.AddComponent<LundunChaseCar>();
        cc.Begin(0.5f);
        made.Add(g);
        Debug.Log("[Lundun] TEST: Rider on a bike at " + pos.ToString("F0"));
    }

    [MenuItem("Lundun/Test: Phase 5 gate C1/Leah, here", false, 71)]
    static void Leah()
    {
        Transform j = James();
        if (j == null) return;
        GameObject g = LundunCast.Make("LEAH", null);
        if (g == null) return;
        UnityEngine.AI.NavMeshAgent ag = g.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag != null) ag.enabled = false;
        Vector3 f = j.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
        g.transform.SetPositionAndRotation(LundunMissionRun.Ground(j.position + f * 2.5f), Quaternion.LookRotation(-f));
        g.SetActive(true);
        made.Add(g);
    }

    [MenuItem("Lundun/Test: Phase 5 gate C1/Clear", false, 72)]
    static void Clear()
    {
        foreach (GameObject g in made) if (g != null) Object.Destroy(g);
        made.Clear();
    }
}
