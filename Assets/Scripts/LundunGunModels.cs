using System.Collections.Generic;
using UnityEngine;

/// G3: James's guns, built in code (original designs, no downloads): hard-surface shapes with chamfered edges,
/// real-world sizes, gunmetal / polymer / tan finishes, sights and optics. Each gun is +Z forward, +Y up, with the
/// origin where the right hand grips. Named child points tell the rig where everything is:
///   Muzzle, Eject (shells come out here, flying right), Fore (left hand), Stock (shoulder), MagWell,
///   and moving parts: Mag (drops out on reload), Pump (shotgun).
public static class LundunGunModels
{
    // ------------------------------------------------------------------ materials (made once)
    static Material metal, polymer, tan, steel, brassMat, redMat, lens, dot;

    static Material Lit(Color c, float metallic, float smooth, string name)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(s != null ? s : Shader.Find("Standard")) { name = "Gun_" + name };
        m.SetColor("_BaseColor", c); m.color = c;
        m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smooth);
        return m;
    }

    static void EnsureMaterials()
    {
        if (metal != null) return;
        metal = Lit(new Color(0.11f, 0.115f, 0.125f), 0.8f, 0.6f, "Gunmetal");
        polymer = Lit(new Color(0.075f, 0.078f, 0.082f), 0f, 0.34f, "Polymer");
        tan = Lit(new Color(0.5f, 0.42f, 0.3f), 0f, 0.28f, "Tan");
        steel = Lit(new Color(0.28f, 0.28f, 0.29f), 0.95f, 0.72f, "Steel");
        brassMat = Lit(new Color(0.78f, 0.58f, 0.24f), 1f, 0.7f, "Brass");
        redMat = Lit(new Color(0.55f, 0.06f, 0.05f), 0f, 0.45f, "ShellRed");
        lens = Lit(new Color(0.08f, 0.16f, 0.14f, 1f), 0.2f, 0.98f, "Lens");
        dot = Lit(new Color(1f, 0.1f, 0.08f), 0f, 0.5f, "RedDot");
        dot.EnableKeyword("_EMISSION"); dot.SetColor("_EmissionColor", new Color(4f, 0.3f, 0.2f));
    }

    public static Material Brass { get { EnsureMaterials(); return brassMat; } }
    public static Material ShellRed { get { EnsureMaterials(); return redMat; } }

    // ------------------------------------------------------------------ building blocks
    /// Collects shapes into one mesh with a sub-mesh per material.
    public class Builder
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly Dictionary<Material, List<int>> tris = new Dictionary<Material, List<int>>();

        void Face(Material m, Vector3[] poly, Matrix4x4 xf, Vector3 outward)
        {
            // flat-shaded face, wound so it faces outward (computed in the shape's own space)
            Vector3 nrm = Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]).normalized;
            bool flip = Vector3.Dot(nrm, outward) < 0f;
            if (flip) nrm = -nrm;
            Vector3 wn = xf.MultiplyVector(nrm).normalized;
            int b = v.Count;
            foreach (Vector3 p in poly) { v.Add(xf.MultiplyPoint3x4(p)); n.Add(wn); }
            if (!tris.TryGetValue(m, out List<int> t)) { t = new List<int>(); tris[m] = t; }
            for (int i = 1; i + 1 < poly.Length; i++)
            {
                if (flip) { t.Add(b); t.Add(b + i + 1); t.Add(b + i); }
                else { t.Add(b); t.Add(b + i); t.Add(b + i + 1); }
            }
        }

        /// A box with chamfered edges (bevel in metres).
        public void Box(Material m, Vector3 centre, Vector3 size, float bevel = 0.003f, Vector3 euler = default(Vector3))
        {
            Matrix4x4 xf = Matrix4x4.TRS(centre, Quaternion.Euler(euler), Vector3.one);
            Vector3 h = size * 0.5f;
            float b = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.45f);
            if (b <= 0.0001f)
            {
                for (int ax = 0; ax < 3; ax++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        Vector3 o = Vector3.zero; o[ax] = sg;
                        int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                        Vector3[] q = new Vector3[4];
                        for (int k = 0; k < 4; k++)
                        {
                            Vector3 p = Vector3.zero; p[ax] = sg * h[ax];
                            p[a1] = (k == 0 || k == 3 ? -1 : 1) * h[a1]; p[a2] = (k < 2 ? -1 : 1) * h[a2];
                            q[k] = p;
                        }
                        Face(m, q, xf, o);
                    }
                return;
            }
            // corner points: for each corner, one point pulled back along each axis
            System.Func<int, int, int, int, Vector3> P = (i, j, k, axis) =>
            {
                Vector3 p = new Vector3(i * (h.x - b), j * (h.y - b), k * (h.z - b));
                p[axis] = new[] { i, j, k }[axis] * h[axis];
                return p;
            };
            // 6 main faces
            for (int ax = 0; ax < 3; ax++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3[] q = new Vector3[4]; int c = 0;
                    foreach (int s1 in new[] { -1, 1, 1, -1 })
                    {
                        int s2 = c < 2 ? -1 : 1;
                        int[] ijk = new int[3]; ijk[ax] = sg; ijk[(ax + 1) % 3] = s1; ijk[(ax + 2) % 3] = s2;
                        q[c++] = P(ijk[0], ijk[1], ijk[2], ax);
                    }
                    Vector3 o = Vector3.zero; o[ax] = sg;
                    Face(m, q, xf, o);
                }
            // 12 edge strips
            for (int along = 0; along < 3; along++)
            {
                int a1 = (along + 1) % 3, a2 = (along + 2) % 3;
                for (int s1 = -1; s1 <= 1; s1 += 2)
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        int[] lo = new int[3], hi = new int[3];
                        lo[along] = -1; hi[along] = 1; lo[a1] = hi[a1] = s1; lo[a2] = hi[a2] = s2;
                        Vector3[] q = { P(lo[0], lo[1], lo[2], a1), P(hi[0], hi[1], hi[2], a1), P(hi[0], hi[1], hi[2], a2), P(lo[0], lo[1], lo[2], a2) };
                        Vector3 o = Vector3.zero; o[a1] = s1; o[a2] = s2;
                        Face(m, q, xf, o);
                    }
            }
            // 8 corner triangles
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    for (int k = -1; k <= 1; k += 2)
                        Face(m, new[] { P(i, j, k, 0), P(i, j, k, 1), P(i, j, k, 2) }, xf, new Vector3(i, j, k));
        }

        /// A cylinder from a to b.
        public void Cylinder(Material m, Vector3 a, Vector3 b, float radius, int sides = 12)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-5f) return;
            Matrix4x4 xf = Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.forward, axis / len), Vector3.one);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * radius, p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * radius;
                Vector3 mid = (p0 + p1) * 0.5f;
                Face(m, new[] { p0, p1, p1 + Vector3.forward * len, p0 + Vector3.forward * len }, xf, mid);
            }
            Vector3[] cap0 = new Vector3[sides], cap1 = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float ang = i * Mathf.PI * 2f / sides;
                cap0[i] = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * radius;
                cap1[i] = cap0[i] + Vector3.forward * len;
            }
            Face(m, cap0, xf, Vector3.back);
            Face(m, cap1, xf, Vector3.forward);
        }

        /// A thin flat panel (for lenses).
        public void Panel(Material m, Vector3 centre, Vector2 size, Vector3 facing)
        {
            Matrix4x4 xf = Matrix4x4.TRS(centre, Quaternion.LookRotation(facing), Vector3.one);
            Vector3 h = new Vector3(size.x * 0.5f, size.y * 0.5f, 0f);
            Face(m, new[] { new Vector3(-h.x, -h.y, 0), new Vector3(h.x, -h.y, 0), new Vector3(h.x, h.y, 0), new Vector3(-h.x, h.y, 0) }, xf, Vector3.forward);
            Face(m, new[] { new Vector3(-h.x, -h.y, 0), new Vector3(h.x, -h.y, 0), new Vector3(h.x, h.y, 0), new Vector3(-h.x, h.y, 0) }, xf, Vector3.back);
        }

        public GameObject Build(string name, Transform parent)
        {
            GameObject g = new GameObject(name);
            if (parent != null) g.transform.SetParent(parent, false);
            if (v.Count == 0) return g;
            Mesh mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n);
            List<Material> mats = new List<Material>(tris.Keys);
            mesh.subMeshCount = mats.Count;
            for (int i = 0; i < mats.Count; i++) mesh.SetTriangles(tris[mats[i]], i);
            mesh.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats.ToArray();
            return g;
        }
    }

    static Transform Point(Transform parent, string name, Vector3 pos, Vector3 euler = default(Vector3))
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos; t.localRotation = Quaternion.Euler(euler);
        return t;
    }

    // ------------------------------------------------------------------ the guns
    public static GameObject Build(LundunWeapons.Kind kind)
    {
        EnsureMaterials();
        switch (kind)
        {
            case LundunWeapons.Kind.Smg: return Smg();
            case LundunWeapons.Kind.Carbine: return Carbine();
            case LundunWeapons.Kind.Shotgun: return Shotgun();
            case LundunWeapons.Kind.Fists: return null;
            default: return Pistol();
        }
    }

    static GameObject Pistol()
    {
        GameObject root = new GameObject("Gun_Pistol");
        Builder b = new Builder();
        b.Box(metal, new Vector3(0f, 0.047f, 0.045f), new Vector3(0.029f, 0.032f, 0.19f), 0.004f);            // slide
        for (int i = 0; i < 6; i++) b.Box(steel, new Vector3(0f, 0.05f, -0.035f + i * 0.006f), new Vector3(0.0305f, 0.022f, 0.0025f), 0f);   // slide serrations
        b.Box(polymer, new Vector3(0f, 0.022f, 0.038f), new Vector3(0.027f, 0.021f, 0.155f), 0.003f);          // frame
        b.Box(polymer, new Vector3(0f, -0.032f, -0.014f), new Vector3(0.029f, 0.105f, 0.047f), 0.006f, new Vector3(17f, 0f, 0f));    // grip (raked back)
        b.Box(polymer, new Vector3(0f, -0.011f, 0.042f), new Vector3(0.008f, 0.005f, 0.046f), 0.0015f);        // trigger guard
        b.Box(polymer, new Vector3(0f, 0.004f, 0.064f), new Vector3(0.008f, 0.026f, 0.006f), 0.0015f);
        b.Box(metal, new Vector3(0f, 0.004f, 0.034f), new Vector3(0.004f, 0.018f, 0.006f), 0.001f, new Vector3(15f, 0f, 0f));      // trigger
        b.Cylinder(steel, new Vector3(0f, 0.047f, 0.138f), new Vector3(0f, 0.047f, 0.142f), 0.0055f, 10);       // barrel crown
        b.Box(metal, new Vector3(0f, 0.066f, 0.128f), new Vector3(0.004f, 0.006f, 0.006f), 0.001f);            // front sight
        b.Box(metal, new Vector3(0f, 0.066f, -0.042f), new Vector3(0.019f, 0.006f, 0.008f), 0.001f);           // rear sight
        b.Box(polymer, new Vector3(0f, 0.016f, 0.1f), new Vector3(0.028f, 0.008f, 0.04f), 0.002f);             // rail
        b.Build("Body", root.transform);
        Builder mag = new Builder();
        mag.Box(metal, new Vector3(0f, -0.086f, -0.04f), new Vector3(0.031f, 0.012f, 0.05f), 0.003f, new Vector3(17f, 0f, 0f));
        mag.Build("Mag", root.transform);
        Point(root.transform, "Muzzle", new Vector3(0f, 0.047f, 0.145f));
        Point(root.transform, "Eject", new Vector3(0.018f, 0.055f, 0.045f));
        Point(root.transform, "Fore", new Vector3(-0.022f, -0.028f, 0.0f));
        Point(root.transform, "MagWell", new Vector3(0f, -0.085f, -0.03f));
        return root;
    }

    static GameObject Smg()
    {
        GameObject root = new GameObject("Gun_SMG");
        Builder b = new Builder();
        b.Box(metal, new Vector3(0f, 0.042f, 0.055f), new Vector3(0.04f, 0.066f, 0.25f), 0.005f);             // receiver
        b.Box(polymer, new Vector3(0f, 0.04f, 0.215f), new Vector3(0.046f, 0.054f, 0.08f), 0.006f);           // front shroud
        for (int i = 0; i < 3; i++) b.Box(metal, new Vector3(0f, 0.04f, 0.19f + i * 0.022f), new Vector3(0.047f, 0.012f, 0.008f), 0.001f);
        b.Cylinder(steel, new Vector3(0f, 0.045f, 0.25f), new Vector3(0f, 0.045f, 0.3f), 0.011f, 14);           // barrel
        b.Cylinder(metal, new Vector3(0f, 0.045f, 0.29f), new Vector3(0f, 0.045f, 0.315f), 0.014f, 14);         // muzzle device
        b.Box(polymer, new Vector3(0f, -0.04f, -0.008f), new Vector3(0.03f, 0.1f, 0.044f), 0.006f, new Vector3(15f, 0f, 0f));       // grip
        b.Box(polymer, new Vector3(0f, -0.003f, 0.035f), new Vector3(0.008f, 0.005f, 0.045f), 0.0015f);       // trigger guard
        b.Box(metal, new Vector3(0f, 0.081f, 0.055f), new Vector3(0.022f, 0.009f, 0.17f), 0.001f);            // top rail
        for (int i = 0; i < 12; i++) b.Box(metal, new Vector3(0f, 0.087f, -0.025f + i * 0.0145f), new Vector3(0.024f, 0.004f, 0.006f), 0f);
        // red-dot optic
        b.Box(polymer, new Vector3(0f, 0.1f, 0.035f), new Vector3(0.028f, 0.03f, 0.048f), 0.006f);
        b.Panel(lens, new Vector3(0f, 0.104f, 0.0605f), new Vector2(0.02f, 0.018f), Vector3.forward);
        b.Panel(dot, new Vector3(0f, 0.104f, 0.0595f), new Vector2(0.0025f, 0.0025f), Vector3.back);
        // folding wire stock
        b.Box(metal, new Vector3(0.016f, 0.06f, -0.17f), new Vector3(0.006f, 0.008f, 0.22f), 0.002f);
        b.Box(metal, new Vector3(-0.016f, 0.06f, -0.17f), new Vector3(0.006f, 0.008f, 0.22f), 0.002f);
        b.Box(polymer, new Vector3(0f, 0.04f, -0.285f), new Vector3(0.042f, 0.085f, 0.014f), 0.004f);         // butt plate
        b.Box(metal, new Vector3(-0.024f, 0.05f, 0.11f), new Vector3(0.012f, 0.01f, 0.018f), 0.002f);        // charging handle
        b.Build("Body", root.transform);
        Builder mag = new Builder();
        mag.Box(metal, new Vector3(0f, -0.06f, 0.088f), new Vector3(0.025f, 0.13f, 0.038f), 0.004f, new Vector3(-6f, 0f, 0f));
        mag.Box(polymer, new Vector3(0f, -0.128f, 0.095f), new Vector3(0.028f, 0.012f, 0.042f), 0.003f, new Vector3(-6f, 0f, 0f));
        mag.Build("Mag", root.transform);
        Point(root.transform, "Muzzle", new Vector3(0f, 0.045f, 0.318f));
        Point(root.transform, "Eject", new Vector3(0.022f, 0.06f, 0.075f));
        Point(root.transform, "Fore", new Vector3(0f, 0.008f, 0.215f));
        Point(root.transform, "Stock", new Vector3(0f, 0.04f, -0.29f));
        Point(root.transform, "MagWell", new Vector3(0f, -0.02f, 0.085f));
        return root;
    }

    static GameObject Carbine()
    {
        GameObject root = new GameObject("Gun_Carbine");
        Builder b = new Builder();
        b.Box(metal, new Vector3(0f, 0.058f, 0.045f), new Vector3(0.044f, 0.058f, 0.25f), 0.005f);           // upper receiver
        b.Box(metal, new Vector3(0f, 0.012f, 0.03f), new Vector3(0.04f, 0.044f, 0.2f), 0.005f);               // lower receiver
        b.Box(tan, new Vector3(0f, 0.058f, 0.32f), new Vector3(0.05f, 0.056f, 0.3f), 0.008f);                 // handguard
        for (int i = 0; i < 7; i++)                                                                          // handguard slots
        {
            b.Box(polymer, new Vector3(0.0255f, 0.058f, 0.21f + i * 0.036f), new Vector3(0.002f, 0.016f, 0.022f), 0f);
            b.Box(polymer, new Vector3(-0.0255f, 0.058f, 0.21f + i * 0.036f), new Vector3(0.002f, 0.016f, 0.022f), 0f);
        }
        b.Cylinder(steel, new Vector3(0f, 0.058f, 0.47f), new Vector3(0f, 0.058f, 0.57f), 0.0095f, 14);         // barrel
        b.Cylinder(metal, new Vector3(0f, 0.058f, 0.565f), new Vector3(0f, 0.058f, 0.625f), 0.0145f, 14);       // muzzle brake
        for (int i = 0; i < 3; i++) b.Box(polymer, new Vector3(0f, 0.071f, 0.578f + i * 0.015f), new Vector3(0.016f, 0.004f, 0.006f), 0f);
        b.Box(metal, new Vector3(0f, 0.092f, 0.2f), new Vector3(0.024f, 0.011f, 0.56f), 0.001f);              // top rail
        for (int i = 0; i < 30; i++) b.Box(metal, new Vector3(0f, 0.099f, -0.07f + i * 0.0185f), new Vector3(0.026f, 0.004f, 0.007f), 0f);
        // holographic sight
        b.Box(polymer, new Vector3(0f, 0.11f, 0.075f), new Vector3(0.036f, 0.016f, 0.075f), 0.004f);
        b.Box(polymer, new Vector3(0.017f, 0.135f, 0.085f), new Vector3(0.004f, 0.035f, 0.05f), 0.002f);
        b.Box(polymer, new Vector3(-0.017f, 0.135f, 0.085f), new Vector3(0.004f, 0.035f, 0.05f), 0.002f);
        b.Box(polymer, new Vector3(0f, 0.154f, 0.085f), new Vector3(0.038f, 0.005f, 0.05f), 0.002f);
        b.Panel(lens, new Vector3(0f, 0.135f, 0.1f), new Vector2(0.029f, 0.032f), Vector3.forward);
        b.Panel(dot, new Vector3(0f, 0.135f, 0.099f), new Vector2(0.003f, 0.003f), Vector3.back);
        // grip, trigger guard, vertical foregrip
        b.Box(polymer, new Vector3(0f, -0.042f, -0.022f), new Vector3(0.03f, 0.1f, 0.044f), 0.006f, new Vector3(18f, 0f, 0f));
        b.Box(polymer, new Vector3(0f, -0.014f, 0.035f), new Vector3(0.008f, 0.005f, 0.05f), 0.0015f);
        b.Cylinder(polymer, new Vector3(0f, 0.03f, 0.27f), new Vector3(0f, -0.055f, 0.27f), 0.015f, 12);
        // stock
        b.Cylinder(metal, new Vector3(0f, 0.05f, -0.07f), new Vector3(0f, 0.05f, -0.26f), 0.015f, 14);
        b.Box(tan, new Vector3(0f, 0.035f, -0.235f), new Vector3(0.046f, 0.088f, 0.16f), 0.008f);
        b.Box(polymer, new Vector3(0f, 0.03f, -0.32f), new Vector3(0.048f, 0.108f, 0.018f), 0.005f);
        b.Box(metal, new Vector3(0.024f, 0.06f, 0.075f), new Vector3(0.006f, 0.022f, 0.06f), 0.002f);         // ejection port cover
        b.Box(metal, new Vector3(0f, 0.08f, -0.085f), new Vector3(0.03f, 0.01f, 0.02f), 0.002f);              // charging handle
        b.Build("Body", root.transform);
        Builder mag = new Builder();
        mag.Box(metal, new Vector3(0f, -0.045f, 0.1f), new Vector3(0.023f, 0.09f, 0.058f), 0.004f, new Vector3(-6f, 0f, 0f));
        mag.Box(metal, new Vector3(0f, -0.115f, 0.112f), new Vector3(0.023f, 0.07f, 0.056f), 0.004f, new Vector3(-18f, 0f, 0f));
        mag.Box(polymer, new Vector3(0f, -0.15f, 0.124f), new Vector3(0.027f, 0.01f, 0.062f), 0.003f, new Vector3(-18f, 0f, 0f));
        mag.Build("Mag", root.transform);
        Point(root.transform, "Muzzle", new Vector3(0f, 0.058f, 0.628f));
        Point(root.transform, "Eject", new Vector3(0.027f, 0.062f, 0.075f));
        Point(root.transform, "Fore", new Vector3(0f, -0.02f, 0.27f));
        Point(root.transform, "Stock", new Vector3(0f, 0.035f, -0.33f));
        Point(root.transform, "MagWell", new Vector3(0f, -0.02f, 0.1f));
        return root;
    }

    static GameObject Shotgun()
    {
        GameObject root = new GameObject("Gun_Shotgun");
        Builder b = new Builder();
        b.Box(metal, new Vector3(0f, 0.045f, 0.055f), new Vector3(0.044f, 0.07f, 0.22f), 0.006f);             // receiver
        b.Cylinder(steel, new Vector3(0f, 0.066f, 0.16f), new Vector3(0f, 0.066f, 0.68f), 0.0125f, 16);         // barrel
        b.Cylinder(metal, new Vector3(0f, 0.034f, 0.16f), new Vector3(0f, 0.034f, 0.6f), 0.0125f, 16);          // magazine tube
        b.Box(metal, new Vector3(0f, 0.05f, 0.6f), new Vector3(0.018f, 0.045f, 0.02f), 0.003f);               // barrel clamp
        b.Cylinder(brassMat, new Vector3(0f, 0.081f, 0.672f), new Vector3(0f, 0.084f, 0.672f), 0.003f, 8);       // bead sight
        b.Box(polymer, new Vector3(0f, 0.012f, -0.05f), new Vector3(0.034f, 0.05f, 0.1f), 0.008f, new Vector3(-14f, 0f, 0f));       // wrist / grip
        b.Box(polymer, new Vector3(0f, 0.03f, -0.21f), new Vector3(0.04f, 0.045f, 0.29f), 0.01f, new Vector3(-4f, 0f, 0f));        // stock comb
        b.Box(polymer, new Vector3(0f, -0.005f, -0.25f), new Vector3(0.038f, 0.06f, 0.2f), 0.012f, new Vector3(-12f, 0f, 0f));      // stock belly
        b.Box(polymer, new Vector3(0f, 0.012f, -0.36f), new Vector3(0.044f, 0.11f, 0.02f), 0.005f, new Vector3(-5f, 0f, 0f));      // recoil pad
        b.Box(polymer, new Vector3(0f, -0.012f, 0.04f), new Vector3(0.008f, 0.005f, 0.045f), 0.0015f);       // trigger guard
        for (int i = 0; i < 4; i++)                                                                          // side shell carrier
        {
            b.Box(polymer, new Vector3(-0.025f, 0.045f, 0.02f + i * 0.026f), new Vector3(0.004f, 0.05f, 0.022f), 0.001f);
            b.Cylinder(redMat, new Vector3(-0.032f, 0.022f, 0.02f + i * 0.026f), new Vector3(-0.032f, 0.07f, 0.02f + i * 0.026f), 0.0095f, 10);
            b.Cylinder(brassMat, new Vector3(-0.032f, 0.016f, 0.02f + i * 0.026f), new Vector3(-0.032f, 0.024f, 0.02f + i * 0.026f), 0.0102f, 10);
        }
        b.Build("Body", root.transform);
        Builder pump = new Builder();
        pump.Box(polymer, new Vector3(0f, 0.03f, 0.31f), new Vector3(0.05f, 0.046f, 0.18f), 0.01f);
        for (int i = 0; i < 7; i++) pump.Box(metal, new Vector3(0f, 0.03f, 0.24f + i * 0.023f), new Vector3(0.0515f, 0.04f, 0.006f), 0.001f);
        pump.Build("Pump", root.transform);
        Point(root.transform, "Muzzle", new Vector3(0f, 0.066f, 0.685f));
        Point(root.transform, "Eject", new Vector3(0.026f, 0.05f, 0.07f));
        Point(root.transform.Find("Pump"), "Fore", new Vector3(0f, 0.008f, 0.31f));
        Point(root.transform, "Stock", new Vector3(0f, 0.012f, -0.37f));
        Point(root.transform, "MagWell", new Vector3(0f, 0.0f, 0.08f));         // loading port, underneath
        return root;
    }

    /// A spent case for the shell ejector: a brass case (or a red shotgun shell with a brass base).
    public static Mesh CaseMesh(bool shotgunShell)
    {
        EnsureMaterials();
        Builder b = new Builder();
        if (shotgunShell)
        {
            b.Cylinder(redMat, new Vector3(0f, 0f, -0.024f), new Vector3(0f, 0f, 0.035f), 0.0095f, 10);
            b.Cylinder(brassMat, new Vector3(0f, 0f, -0.035f), new Vector3(0f, 0f, -0.022f), 0.0102f, 10);
        }
        else b.Cylinder(brassMat, new Vector3(0f, 0f, -0.011f), new Vector3(0f, 0f, 0.011f), 0.0048f, 8);
        GameObject g = b.Build("Case", null);
        Mesh m = g.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(g);
        return m;
    }

    public static Material[] CaseMaterials(bool shotgunShell)
    {
        EnsureMaterials();
        return shotgunShell ? new[] { redMat, brassMat } : new[] { brassMat };
    }
}
