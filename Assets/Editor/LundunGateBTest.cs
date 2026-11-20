// Lundun → Test: Phase 5 gate B (Editor, in Play mode only; nothing here goes into the game or the scene)
//   Try the new CCTV pieces before any mission uses them:
//     Camera + fuse box + recorder   a sweeping mission camera in front of James, a fuse box beside him (cuts it for
//                                    60 s) and a recorder (wipe it). Spotted / shot-out / wiped show in the Console
//     CCTV footage                   the new black-and-white CCTV cut, from a camera up high looking at James, with
//                                    a tracking box (SUBJECT, or his car's PLATE if he's driving) and a caption
//     Suppressor on/off              pretend it's bought (works in Free Roam too, for the test only)
//     Clear                          removes the test props
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class LundunGateBTest
{
    static readonly List<GameObject> made = new List<GameObject>();

    static bool Playing()
    {
        if (Application.isPlaying && LundunMissionRun.James() != null) return true;
        EditorUtility.DisplayDialog("Lundun", "Press Play first (and pick a mode), then use this.", "OK");
        return false;
    }

    [MenuItem("Lundun/Test: Phase 5 gate B/Camera + fuse box + recorder", false, 60)]
    static void Props()
    {
        if (!Playing()) return;
        Clear();
        Transform j = LundunMissionRun.James();
        Vector3 fwd = j.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 ground = LundunMissionRun.Ground(j.position);

        // the camera: 9 m ahead, 3.5 m up, looking back at where James is standing, sweeping
        Vector3 lens = LundunMissionRun.Ground(ground + fwd * 9f) + Vector3.up * 3.5f;
        float yaw = Quaternion.LookRotation(-fwd).eulerAngles.y;
        LundunMissionCam cam = LundunMissionCam.Create(null, "test_cam", lens, yaw, 35f);
        cam.Spotted += (c, clear) => { Debug.Log("[Lundun] Test camera SPOTTED James (" + (clear ? "identified" : "unclear") + ")"); LundunMissions.Toast("TEST: camera spotted you\n" + (clear ? "identified" : "face not clear")); c.ResetSpotted(); };
        cam.Down += (c, quiet) => { Debug.Log("[Lundun] Test camera shot out (" + (quiet ? "quiet" : "loud") + ")"); };
        made.Add(cam.gameObject);

        // a floodlight by the camera, so the fuse box has a light to cut
        GameObject flood = new GameObject("TestFloodlight");
        flood.transform.position = lens + Vector3.up * 0.6f;
        flood.transform.rotation = Quaternion.LookRotation((ground - flood.transform.position).normalized);
        Light l = flood.AddComponent<Light>(); l.type = LightType.Spot; l.range = 18f; l.spotAngle = 70f; l.intensity = 6f; l.color = new Color(1f, 0.95f, 0.85f);
        made.Add(flood);

        // the fuse box: 2.5 m to his right, facing him
        Vector3 fb = LundunMissionRun.Ground(ground + right * 2.5f);
        LundunFuseBox box = LundunFuseBox.Create(null, fb, Quaternion.LookRotation(-right).eulerAngles.y, new[] { cam }, new[] { l });
        box.PowerCut += () => Debug.Log("[Lundun] Test fuse box: power cut (60 s)");
        box.PowerBack += () => Debug.Log("[Lundun] Test fuse box: power back");
        made.Add(box.gameObject);

        // the recorder: 2.5 m to his left, on the ground, facing him
        Vector3 rc = LundunMissionRun.Ground(ground - right * 2.5f);
        LundunRecorder rec = LundunRecorder.Create(null, "test_recorder", rc, Quaternion.LookRotation(right).eulerAngles.y);
        rec.Wiped += () => Debug.Log("[Lundun] Test recorder wiped");
        made.Add(rec.gameObject);

        Debug.Log("[Lundun] Gate B test props placed: camera ahead (sweeping), fuse box to the right, recorder to the left.");
    }

    [MenuItem("Lundun/Test: Phase 5 gate B/CCTV footage (black and white cut)", false, 61)]
    static void Footage()
    {
        if (!Playing()) return;
        if (LundunCctvFeed.Instance == null) { EditorUtility.DisplayDialog("Lundun", "No CCTV feed in this scene (LundunCctv adds it).", "OK"); return; }
        Transform j = LundunMissionRun.James();
        Vector3 back = -j.forward; back.y = 0f; back.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, back);
        Vector3 lens = j.position + back * 6f + side * 4f + Vector3.up * 5f;     // high on a wall, off to one side
        bool driving = LundunMissionRun.Driving();
        LundunCctvFeed.Instance.StartCoroutine(LundunCctvFeed.Scripted(
            lens, () => LundunMissionRun.James().position + Vector3.up * 0.9f,
            "CAM 0147  ·  RYE LANE / BELLENDEN RD",
            driving ? "PLATE READ  ·  LK19 XRT  ·  REPORTED STOLEN" : "SUBJECT IDENTIFIED",
            6f,
            driving ? LundunCctvFeed.Mark.Plate : LundunCctvFeed.Mark.Subject,
            () => LundunMissionRun.James().position + Vector3.up * (driving ? 0.5f : 1.0f),
            driving ? "LK19 XRT" : "SUBJECT 01"));
    }

    [MenuItem("Lundun/Test: Phase 5 gate B/Suppressor on or off", false, 62)]
    static void Suppressor()
    {
        if (!Playing()) return;
        LundunSuppressor.TestOverride = !LundunSuppressor.TestOverride;
        LundunSuppressor.Ensure();
        LundunMissions.Toast("TEST: suppressor " + (LundunSuppressor.TestOverride ? "ON\nEquip the pistol" : "OFF"));
    }

    [MenuItem("Lundun/Test: Phase 5 gate B/Clear test props", false, 63)]
    static void Clear()
    {
        foreach (GameObject g in made) if (g != null) Object.Destroy(g);
        made.Clear();
    }
}
