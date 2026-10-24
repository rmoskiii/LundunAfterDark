using System.Collections.Generic;
using UnityEngine;

/// E5: a prop that breaks: a promo stack of tins or bottles, a chiller's glass front, a sliding door's glass.
/// Shoot it (PlayerCombat calls HitAt) or hit it with something moving (a car) and it bursts into pieces that tumble
/// and fade away; it's put back a few minutes later when you're not looking. Sounds are made in code.
/// Added by Lundun → 28 (with the piece's colours: debrisUV is the patch of the shop atlas the bits are painted from).
public class LundunBreakable : MonoBehaviour
{
    public enum Kind { Stack, Glass }

    [SerializeField] private Kind kind = Kind.Stack;
    [SerializeField] private Vector4 debrisUV = new Vector4(0f, 0f, 1f, 1f);     // u0, v0, u1, v1
    [SerializeField] private float breakSpeed = 3f;                             // m/s an impact needs
    [SerializeField] private float respawnAfter = 240f;
    [SerializeField] private float debrisLifetime = 10f;

    private bool broken;
    private float brokenAt;
    private Renderer[] rends;
    private Collider[] cols;
    private Mesh debrisMesh;

    public bool Broken { get { return broken; } }
    public static event System.Action<LundunBreakable> Smashed;

    public void SetData(Kind k, Vector4 uv) { kind = k; debrisUV = uv; }

    void Awake()
    {
        rends = GetComponentsInChildren<Renderer>(true);
        cols = GetComponentsInChildren<Collider>(true);
    }

    /// A bullet (or anything else) hit this collider: break whatever breakable it belongs to.
    public static void HitAt(Collider c, Vector3 point, Vector3 dir)
    {
        if (c == null) return;
        LundunBreakable b = c.GetComponentInParent<LundunBreakable>();
        if (b != null) b.Break(point, dir.normalized * 3f);
    }

    void OnCollisionEnter(Collision c)
    {
        if (broken || c.rigidbody == null) return;
        if (c.relativeVelocity.magnitude < breakSpeed) return;
        Break(c.GetContact(0).point, c.rigidbody.linearVelocity * 0.4f);
    }

    void OnEnable() { TryRestore(); }

    void Update()
    {
        if (broken && (Time.frameCount & 63) == 0) TryRestore();
    }

    /// Put back once it's been a while and you're not right there.
    void TryRestore()
    {
        if (!broken || Time.time - brokenAt < respawnAfter) return;
        Camera cam = Camera.main;
        if (cam != null && Vector3.Distance(cam.transform.position, transform.position) < 25f) return;
        Restore();
    }

    void Restore()
    {
        broken = false;
        foreach (Renderer r in rends) if (r != null) r.enabled = true;
        foreach (Collider c in cols) if (c != null) c.enabled = true;
    }

    public void Break(Vector3 at, Vector3 push)
    {
        if (broken) return;
        broken = true; brokenAt = Time.time;
        Material mat = null;
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in rends)
        {
            if (r == null) continue;
            if (mat == null) mat = r.sharedMaterial;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            r.enabled = false;
        }
        foreach (Collider c in cols) if (c != null) c.enabled = false;
        Debris(b, mat, at, push);
        AudioSource.PlayClipAtPoint(kind == Kind.Glass ? GlassClip() : CansClip(), at, 0.9f);
        Smashed?.Invoke(this);
    }

    // ------------------------------------------------------------------ the bits
    void Debris(Bounds b, Material mat, Vector3 at, Vector3 push)
    {
        if (mat == null) return;
        if (debrisMesh == null) debrisMesh = Cube(debrisUV);
        int n = kind == Kind.Glass ? Mathf.Clamp((int)(b.size.x * b.size.y * 2.5f), 8, 18) : 16;
        int layer = LayerMask.NameToLayer("Ignore Raycast");
        for (int i = 0; i < n; i++)
        {
            GameObject g = new GameObject("Debris");
            g.layer = layer < 0 ? 0 : layer;
            Vector3 p = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
            g.transform.SetPositionAndRotation(p, Random.rotation);
            g.transform.localScale = kind == Kind.Glass ? new Vector3(Random.Range(0.12f, 0.35f), Random.Range(0.1f, 0.3f), 0.012f)
                                                       : Vector3.one * Random.Range(0.1f, 0.2f);
            g.AddComponent<MeshFilter>().sharedMesh = debrisMesh;
            MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.AddComponent<BoxCollider>();
            Rigidbody rb = g.AddComponent<Rigidbody>();
            rb.mass = kind == Kind.Glass ? 0.2f : 0.4f;
            Vector3 away = (p - at); away.y = Mathf.Abs(away.y) + 0.3f;
            rb.AddForce((away.normalized * Random.Range(1.5f, 3.5f) + push) * rb.mass, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 0.5f, ForceMode.Impulse);
            Destroy(g, debrisLifetime + Random.Range(0f, 2f));
        }
    }

    /// A unit cube with every face painted from one patch of the atlas.
    static Mesh Cube(Vector4 r)
    {
        Mesh m = new Mesh { name = "Debris" };
        List<Vector3> v = new List<Vector3>(); List<Vector3> n = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (Vector3 d in dirs)
        {
            Vector3 a = Vector3.Cross(d, Mathf.Abs(d.y) > 0.5f ? Vector3.forward : Vector3.up), c = Vector3.Cross(d, a);
            int k = v.Count;
            v.Add((d - a - c) * 0.5f); v.Add((d + a - c) * 0.5f); v.Add((d + a + c) * 0.5f); v.Add((d - a + c) * 0.5f);
            for (int i = 0; i < 4; i++) n.Add(d);
            uv.Add(new Vector2(r.x, r.y)); uv.Add(new Vector2(r.z, r.y)); uv.Add(new Vector2(r.z, r.w)); uv.Add(new Vector2(r.x, r.w));
            t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2);
        }
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ sounds (made once, in code)
    static AudioClip glass, cans;

    static AudioClip GlassClip()
    {
        if (glass != null) return glass;
        int rate = 22050, len = (int)(1.1f * rate);
        float[] d = new float[len];
        System.Random r = new System.Random(3);
        float hp = 0f, last = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            float noise = (float)r.NextDouble() * 2f - 1f;
            hp = 0.6f * (hp + noise - last); last = noise;                         // bright, hissy crash
            float crash = hp * Mathf.Exp(-t * 7f) * 0.9f;
            float tinkle = 0f;
            for (int k = 0; k < 6; k++)
            {
                float t0 = 0.08f + k * 0.13f + (k * 37 % 11) * 0.01f;
                if (t > t0) tinkle += Mathf.Sin((t - t0) * 2f * Mathf.PI * (2600f + k * 530f)) * Mathf.Exp(-(t - t0) * 30f) * 0.25f;
            }
            d[i] = Mathf.Clamp(crash + tinkle, -1f, 1f);
        }
        glass = AudioClip.Create("GlassSmash", len, 1, rate, false);
        glass.SetData(d, 0);
        return glass;
    }

    static AudioClip CansClip()
    {
        if (cans != null) return cans;
        int rate = 22050, len = (int)(1.3f * rate);
        float[] d = new float[len];
        System.Random r = new System.Random(5);
        for (int k = 0; k < 14; k++)
        {
            int start = (int)((0.02f + (float)r.NextDouble() * 0.9f) * rate);
            float f = 700f + (float)r.NextDouble() * 900f, amp = 0.15f + (float)r.NextDouble() * 0.2f;
            for (int i = 0; i < (int)(0.18f * rate) && start + i < len; i++)
            {
                float t = (float)i / rate;
                d[start + i] += (Mathf.Sin(t * 2f * Mathf.PI * f) + 0.5f * Mathf.Sin(t * 2f * Mathf.PI * f * 2.7f)) * Mathf.Exp(-t * 28f) * amp
                                + ((float)r.NextDouble() - 0.5f) * Mathf.Exp(-t * 60f) * amp;
            }
        }
        for (int i = 0; i < len; i++) d[i] = Mathf.Clamp(d[i], -1f, 1f);
        cans = AudioClip.Create("CansTumble", len, 1, rate, false);
        cans.SetData(d, 0);
        return cans;
    }
}
