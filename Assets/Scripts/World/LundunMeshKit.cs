using System.Collections.Generic;
using UnityEngine;

/// Builds one mesh (a submesh per material) from boxes and cylinders in code, for things made at runtime (the pubs):
/// one renderer instead of dozens of primitives.
public class LundunMeshKit
{
    readonly List<Vector3> v = new List<Vector3>();
    readonly List<Vector3> n = new List<Vector3>();
    readonly List<Vector2> uv = new List<Vector2>();
    readonly List<int>[] tris;

    public LundunMeshKit(int materials)
    {
        tris = new List<int>[materials];
        for (int i = 0; i < materials; i++) tris[i] = new List<int>();
    }

    static readonly Vector3[] FaceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

    /// A box: centre, size, turned by rot (about its centre).
    public void Box(int mat, Vector3 c, Vector3 size, Quaternion rot)
    {
        Vector3 h = size * 0.5f;
        foreach (Vector3 f in FaceN)
        {
            Vector3 a = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up;     // two axes across the face
            Vector3 b = Vector3.Cross(f, a);
            Vector3 fc = Vector3.Scale(f, h);
            Vector3 ea = Vector3.Scale(a, h), eb = Vector3.Scale(b, h);
            int i0 = v.Count;
            Vector3[] corners = { fc - ea - eb, fc + ea - eb, fc + ea + eb, fc - ea + eb };
            foreach (Vector3 p in corners) { v.Add(c + rot * p); n.Add(rot * f); }
            float su = Vector3.Dot(Abs(a), size), sv = Vector3.Dot(Abs(b), size);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(su, 0)); uv.Add(new Vector2(su, sv)); uv.Add(new Vector2(0, sv));
            tris[mat].AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
        }
    }
    public void Box(int mat, Vector3 c, Vector3 size) { Box(mat, c, size, Quaternion.identity); }

    /// A cylinder standing up (along rot's up), centre c.
    public void Cyl(int mat, Vector3 c, float radius, float height, Quaternion rot, int seg = 12)
    {
        float hh = height * 0.5f;
        int baseIdx = v.Count;
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            v.Add(c + rot * (d * radius + Vector3.up * -hh)); n.Add(rot * d); uv.Add(new Vector2(i / (float)seg, 0));
            v.Add(c + rot * (d * radius + Vector3.up * hh)); n.Add(rot * d); uv.Add(new Vector2(i / (float)seg, 1));
        }
        for (int i = 0; i < seg; i++)
        {
            int a0 = baseIdx + i * 2;
            tris[mat].AddRange(new[] { a0, a0 + 1, a0 + 3, a0, a0 + 3, a0 + 2 });
        }
        foreach (int s in new[] { -1, 1 })
        {
            int ci = v.Count;
            v.Add(c + rot * (Vector3.up * hh * s)); n.Add(rot * (Vector3.up * s)); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(c + rot * (d * radius + Vector3.up * hh * s)); n.Add(rot * (Vector3.up * s)); uv.Add(new Vector2(d.x * 0.5f + 0.5f, d.z * 0.5f + 0.5f));
            }
            for (int i = 0; i < seg; i++)
            {
                if (s > 0) tris[mat].AddRange(new[] { ci, ci + i + 2, ci + i + 1 });
                else tris[mat].AddRange(new[] { ci, ci + i + 1, ci + i + 2 });
            }
        }
    }
    public void Cyl(int mat, Vector3 c, float radius, float height) { Cyl(mat, c, radius, height, Quaternion.identity); }

    static Vector3 Abs(Vector3 a) { return new Vector3(Mathf.Abs(a.x), Mathf.Abs(a.y), Mathf.Abs(a.z)); }

    public Mesh Build(string name)
    {
        Mesh m = new Mesh { name = name };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
        m.subMeshCount = tris.Length;
        for (int i = 0; i < tris.Length; i++) m.SetTriangles(tris[i], i);
        m.RecalculateBounds();
        return m;
    }

    /// A plain URP Lit material (emissive if glow isn't black).
    public static Material Mat(string name, Color c, float smooth, Color glow)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(s != null ? s : Shader.Find("Standard")) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (glow.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", glow); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        m.enableInstancing = true;
        return m;
    }
}
