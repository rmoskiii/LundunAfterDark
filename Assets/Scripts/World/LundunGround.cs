using UnityEngine;

/// G7b: how high the ground is at a point, now the city has hills (Lundun → 40). 0 everywhere else, and everywhere
/// if there are no hills in the scene. For anything placed while the game runs at street level from the road plan
/// (cars, people, bikes, bus stops, police): put it at y = Height(x, z) instead of y = 0.
public static class LundunGround
{
    static LundunHillRecord hills;

    public static void Use(LundunHillRecord r) { if (r != null) hills = r; }
    public static void Forget(LundunHillRecord r) { if (hills == r) hills = null; }

    public static float Height(float x, float z)
    {
        if (hills == null || hills.ground == null) return 0f;
        float h = 0f;
        foreach (LundunHillRecord.Ground g in hills.ground)
        {
            if (g == null || g.h == null || g.nx < 2 || g.nz < 2) continue;
            float fx = (x - g.x0) / g.cell, fz = (z - g.z0) / g.cell;
            if (fx < 0f || fz < 0f || fx >= g.nx - 1 || fz >= g.nz - 1) continue;
            int i = (int)fx, j = (int)fz; float tx = fx - i, tz = fz - j;
            float a = g.h[j * g.nx + i], b = g.h[j * g.nx + i + 1], c = g.h[(j + 1) * g.nx + i], d = g.h[(j + 1) * g.nx + i + 1];
            h += Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }
        return h;
    }

    public static float Height(Vector3 p) { return Height(p.x, p.z); }

    /// The same point, standing on the ground (its y measured from the ground instead of from 0).
    public static Vector3 On(Vector3 p) { p.y += Height(p.x, p.z); return p; }
}
