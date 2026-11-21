using UnityEngine;

/// G2: which way someone dies when they're shot dead (pedestrians, police, gang members: anyone on the people's
/// animator). Picked from the killing shot:
///   from behind (the shot pushes them the way they face)   DieForward   they pitch forward
///   a heavy gun (shotgun, carbine), from the front or side  DieBlown     thrown back off their feet
///   anything else (pistol, SMG)                             Die          the old fall (Mixamo "Dying Backwards")
/// For a front or side kill they turn to face the shot first, so the fall goes away from the gun. If the animator
/// doesn't have the new death (the clips haven't been added with Lundun > 36), it's the old "Die", exactly as before.
/// Not for bodies launched by a car (physics), and not for the story's crew (they're only ever downed).
public static class LundunDeaths
{
    public const string Forward = "DieForward", Blown = "DieBlown", Old = "Die";

    public static void Play(Animator a, Health h, Transform body)
    {
        if (a == null) return;
        string pick = Old;
        if (h != null && h.KilledByGunfire && !h.DiedFromPhysics && body != null)
        {
            Vector3 push = h.DeathForce; push.y = 0f;
            if (push.sqrMagnitude > 0.0001f)
            {
                push.Normalize();
                Vector3 facing = body.forward; facing.y = 0f;
                bool fromBehind = facing.sqrMagnitude > 0.0001f && Vector3.Dot(facing.normalized, push) > 0.35f;
                bool heavy = h.LastWeapon == LundunWeapons.Kind.Shotgun || h.LastWeapon == LundunWeapons.Kind.Carbine;
                if (fromBehind) pick = Forward;
                else
                {
                    body.rotation = Quaternion.LookRotation(-push);              // face the gun: the fall goes away from it
                    if (heavy) pick = Blown;
                }
            }
        }
        if (pick != Old && !Has(a, pick)) pick = Old;
        a.SetTrigger(pick);
    }

    static bool Has(Animator a, string p)
    {
        foreach (AnimatorControllerParameter ap in a.parameters) if (ap.name == p) return true;
        return false;
    }
}
