using UnityEngine;

/// E8: Kingsmere's rules (on LundunExpansion/Kingsmere, filled in by Lundun → 29).
///   warning zone      the strip in front of the palace railings: a guard tells you to stand back (now and then)
///   restricted zones  the forecourt, the palace and its gardens, the barracks: you're trespassing. The guards and
///                     cameras always see it (no witness needed): one star straight away, and staying inside adds more
///                     every few seconds (trespassing alone stops at two stars, see WantedSystem). The guards nearby
///                     stop and face you.
/// Only on foot or in a car at street level: flying over in the helicopter isn't trespassing.
public class LundunRoyalQuarter : MonoBehaviour
{
    [Header("Filled in by Lundun → 29 (world x0, z0, x1, z1)")]
    [SerializeField] private Vector4[] restricted = new Vector4[0];
    [SerializeField] private Vector4[] warning = new Vector4[0];
    [SerializeField] private LundunRoyalGuard[] guards = new LundunRoyalGuard[0];

    [Header("Rules")]
    [SerializeField] private float reportEvery = 5f;
    [SerializeField] private float warnEvery = 20f;
    [SerializeField] private float guardsSeeWithin = 70f;
    [SerializeField] private float aboveGroundIgnore = 12f;      // higher than this (a helicopter) doesn't count

    float nextCheck, nextReport, lastWarn = -999f;
    bool inside;

    public void Set(Vector4[] restrictedZones, Vector4[] warningZones, LundunRoyalGuard[] royalGuards)
    {
        restricted = restrictedZones ?? new Vector4[0];
        warning = warningZones ?? new Vector4[0];
        guards = royalGuards ?? new LundunRoyalGuard[0];
    }

    public Vector4[] Restricted { get { return restricted; } }

    static bool In(Vector4 r, Vector3 p) { return p.x >= r.x && p.x <= r.z && p.z >= r.y && p.z <= r.w; }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.25f;
        WantedSystem w = WantedSystem.Instance;
        if (w == null || w.PlayerTarget == null) return;
        Transform player = w.PlayerTarget;
        Vector3 p = player.position;
        if (p.y > aboveGroundIgnore) { inside = false; return; }

        bool nowInside = false;
        foreach (Vector4 r in restricted) if (In(r, p)) { nowInside = true; break; }

        if (nowInside)
        {
            if (!inside)
            {
                Toast("TRESPASSING — the royal guard has called the police");
                LundunHaptics.Medium();
                nextReport = 0f;
            }
            if (Time.time >= nextReport)
            {
                nextReport = Time.time + reportEvery;
                CrimeSystem.Report(CrimeType.Trespass, p);
            }
            foreach (LundunRoyalGuard g in guards)
                if (g != null && (g.transform.position - p).sqrMagnitude < guardsSeeWithin * guardsSeeWithin)
                    g.Alert(player, 4f);
        }
        else
        {
            bool near = false;
            foreach (Vector4 r in warning) if (In(r, p)) { near = true; break; }
            if (near && Time.time - lastWarn > warnEvery)
            {
                lastWarn = Time.time;
                Toast("Guard: \"Stand back from the railings!\"");
            }
        }
        inside = nowInside;
    }

    static void Toast(string message)
    {
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(message);
    }
}
