using UnityEngine;

/// Phase 5 (gate C1): M06's OFF CAMERA. While it's there, it watches for a street camera (LundunCctv) seeing James
/// within a few seconds of a shot being fired near him: his, Marcus's from the window, or the Riders'. The first time
/// it happens the shoot-out's on the council's footage: Caught, a toast, the police told (2★). Keep the fight to the
/// back streets, where the cameras can't see, and it never happens.
public class LundunOffCamera : MonoBehaviour
{
    public bool Caught { get; private set; }
    float lastJamesShot = -99f, next;

    public static LundunOffCamera Watch(LundunMissionRun run)
    {
        GameObject g = new GameObject("LundunOffCamera");
        if (run != null) run.Spawn(g);
        return g.AddComponent<LundunOffCamera>();
    }

    void OnEnable() { PlayerCombat.GunshotFired += Shot; }
    void OnDisable() { PlayerCombat.GunshotFired -= Shot; }
    void Shot(Vector3 at) { lastJamesShot = Time.time; }

    void Update()
    {
        if (Caught || Time.time < next) return;
        next = Time.time + 0.25f;
        if (!LundunCctv.Live || LundunCctv.Instance == null || !LundunCctv.Instance.OnCamera) return;
        float since = Time.time - Mathf.Max(lastJamesShot, LundunChaseCar.LastShotAt);
        if (since > 3f) return;
        Caught = true;
        LundunHaptics.Medium();
        LundunMissions.Toast("CAUGHT ON CAMERA\nThe shoot-out's on the council's footage");
        Transform j = LundunMissionRun.James();
        WantedSystem w = WantedSystem.Instance;
        if (w != null && j != null) w.RaiseTo(2, j.position, true);
        LundunCctvFeed.Caught(LundunCctv.Instance.FirstWatching);
    }
}
