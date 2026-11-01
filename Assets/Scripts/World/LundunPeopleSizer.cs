using UnityEngine;
using UnityEngine.AI;

/// Safety net for oversized people (Ped_Casual_3 came in from Mixamo about 2.2× too tall).
/// Lundun → Fix People Sizes corrects the prefab in the editor; this makes sure that, even if that prefab change
/// is ever lost or not committed, nobody walks the streets at the wrong size: every pedestrian is measured as they're
/// created and, if their hips are way off normal height, scaled to normal (their NavMeshAgent and collider are
/// adjusted to match, so they still walk and bump like everyone else). People already the right size are untouched.
public static class LundunPeopleSizer
{
    const float NormalHips = 0.97f;              // metres from feet to hips for an adult
    const float Low = 0.7f, High = 1.3f;          // anything outside this is clearly the wrong size

    public static void Normalize(GameObject person)
    {
        if (person == null) return;
        Transform root = person.transform;
        Transform hips = null;
        foreach (Transform t in person.GetComponentsInChildren<Transform>(true))
            if (t.name == "Hips" || t.name.EndsWith(":Hips")) { hips = t; break; }
        if (hips == null) return;

        // hip height in the root's own space (before any scale on the root)
        Matrix4x4 m = Matrix4x4.identity;
        for (Transform t = hips; t != null && t != root; t = t.parent)
            m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * m;
        float local = m.MultiplyPoint3x4(Vector3.zero).y;
        if (local <= 0.01f) return;
        float now = root.localScale.y;
        float world = local * now;
        if (world >= Low && world <= High) return;              // already the right size (or the prefab fix is in)

        float want = NormalHips / local;
        float f = now / want;                                    // agent and collider stay the same size in the world
        root.localScale = Vector3.one * want;
        NavMeshAgent agent = person.GetComponent<NavMeshAgent>();
        if (agent != null) { agent.radius *= f; agent.height *= f; agent.baseOffset *= f; }
        CapsuleCollider col = person.GetComponent<CapsuleCollider>();
        if (col != null) { col.radius *= f; col.height *= f; col.center *= f; }
        Debug.Log("[Lundun] " + person.name + " was " + world.ToString("0.00") + " m at the hips: resized to normal (×" + want.ToString("0.000") + ").");
    }
}
