using UnityEngine;

/// Shared police shooting maths (police cars and officers on foot).
/// Old way: aim at a fixed height and only count a hit if one exact ray touched you — nearly everything missed.
/// New way: roll the dice first; a hit lands unless a wall/building is actually in the way.
public static class PoliceAim
{
    static readonly RaycastHit[] hits = new RaycastHit[16];
    static int blockMask = -1;

    /// Things that stop bullets: the world (Default layer). Not people, not cars' bumpers.
    static int BlockMask
    {
        get
        {
            if (blockMask == -1) blockMask = LayerMask.GetMask("Default");
            return blockMask;
        }
    }

    /// Middle of the target's body (or car).
    public static Vector3 Centre(Transform target)
    {
        Collider c = target.GetComponent<Collider>();
        if (c == null) c = target.GetComponentInChildren<Collider>();
        return c != null ? c.bounds.center : target.position + Vector3.up;
    }

    /// Fires one shot. Returns where the tracer should end.
    public static Vector3 Fire(Transform shooter, Vector3 from, Transform target, float hitChance, float range,
                               float damageOnFoot, float damageToCar)
    {
        Vector3 aim = Centre(target);
        Vector3 to = aim - from;
        float dist = to.magnitude;

        if (Random.value < hitChance)
        {
            Vector3 blockedAt;
            if (Blocked(shooter, target, from, aim, out blockedAt)) return blockedAt;

            // more stars = they shoot to kill
            int stars = WantedSystem.Instance != null ? WantedSystem.Instance.Stars : 3;
            float scale = 1f + 0.25f * Mathf.Max(0, stars - 3);
            VehicleHealth car = target.GetComponent<VehicleHealth>();
            if (car != null) car.TakeDamage(damageToCar * scale);
            else if (PlayerHealth.Instance != null) PlayerHealth.Instance.TakeDamage(damageOnFoot * scale);
            return aim;
        }

        // A miss goes past you, so you see and hear it
        Vector3 side = Vector3.Cross(Vector3.up, to.normalized);
        Vector3 missAim = aim + side * Random.Range(1.2f, 2.5f) * (Random.value < 0.5f ? -1f : 1f) + Vector3.up * Random.Range(-0.4f, 0.9f);
        Vector3 dir = (missAim - from).normalized;
        Vector3 end = from + dir * range;
        if (Physics.Raycast(from, dir, out RaycastHit h, range, BlockMask, QueryTriggerInteraction.Ignore)) end = h.point;
        return end;
    }

    static bool Blocked(Transform shooter, Transform target, Vector3 from, Vector3 to, out Vector3 at)
    {
        at = to;
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return false;
        int n = Physics.RaycastNonAlloc(from, d / len, hits, len, BlockMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Transform t = hits[i].transform;
            if (t.IsChildOf(target) || (shooter != null && t.IsChildOf(shooter))) continue;   // own car parts don't count
            if (hits[i].distance < best) { best = hits[i].distance; at = hits[i].point; }
        }
        return best < float.MaxValue;
    }
}
