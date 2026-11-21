using System.Collections.Generic;
using UnityEngine;

/// G3: always autumn. Sits on the LundunAutumn object (Lundun → 37) and does two small things:
///   • a few leaves drifting down from the trees near the camera: at most a dozen in the air at once, each a small
///     two-sided card, reused
///   • switches the fallen leaves and the turned crowns off beyond their draw distance, like the dressing's blocks
public class LundunFallingLeaves : MonoBehaviour
{
    [SerializeField] private Vector2[] trees = new Vector2[0];
    [SerializeField] private float near = 26f;
    [SerializeField] private int maxInAir = 12;
    [SerializeField] private Material[] leafMaterials = new Material[0];
    [SerializeField] private Renderer[] blocks = new Renderer[0];
    [SerializeField] private Vector3[] blockCentres = new Vector3[0];
    [SerializeField] private float[] blockFar = new float[0];

    class Leaf { public Transform t; public Vector3 from; public float ground, born, life, sway, spin, phase; public bool on; }
    readonly List<Leaf> leaves = new List<Leaf>();
    readonly List<int> close = new List<int>();
    float nextSpawn, nextScan;
    static readonly Color[] Cols = { new Color(0.79f, 0.47f, 0.17f), new Color(0.66f, 0.28f, 0.12f), new Color(0.85f, 0.66f, 0.23f), new Color(0.42f, 0.29f, 0.16f) };

    public void Set(Vector2[] treeSpots) { trees = treeSpots ?? new Vector2[0]; }
    public void SetMaterials(Material[] m) { leafMaterials = m ?? new Material[0]; }
    /// The blocks, and how far away each is still drawn (measured to its centre).
    public void SetBlocks(Renderer[] r, float[] far)
    {
        blocks = r ?? new Renderer[0];
        blockFar = far ?? new float[0];
        blockCentres = new Vector3[blocks.Length];
        for (int i = 0; i < blocks.Length; i++) if (blocks[i] != null) blockCentres[i] = blocks[i].bounds.center;
    }

    void Update()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            for (int i = 0; i < blocks.Length && i < blockFar.Length && i < blockCentres.Length; i++)
            {
                if (blocks[i] == null) continue;
                Vector3 d = blockCentres[i] - c; d.y = 0f;
                bool show = d.sqrMagnitude < blockFar[i] * blockFar[i];
                if (blocks[i].enabled != show) blocks[i].enabled = show;
            }
            close.Clear();
            float n2 = near * near;
            for (int i = 0; i < trees.Length; i++)
            {
                float dx = trees[i].x - c.x, dz = trees[i].y - c.z;
                if (dx * dx + dz * dz < n2) close.Add(i);
            }
        }
        if (trees.Length > 0 && close.Count > 0 && Time.time >= nextSpawn)
        {
            nextSpawn = Time.time + Random.Range(0.35f, 0.9f) * Mathf.Lerp(1.6f, 0.6f, Mathf.Clamp01(close.Count / 8f));
            Spawn(trees[close[Random.Range(0, close.Count)]], c.y);
        }
        foreach (Leaf l in leaves)
        {
            if (!l.on) continue;
            float k = (Time.time - l.born) / l.life;
            if (k >= 1f) { l.on = false; l.t.gameObject.SetActive(false); continue; }
            float y = Mathf.Lerp(l.from.y, l.ground + 0.03f, Mathf.SmoothStep(0f, 1f, k));
            float a = Time.time * 1.7f + l.phase;
            l.t.position = new Vector3(l.from.x + Mathf.Sin(a) * l.sway + k * 1.2f, y, l.from.z + Mathf.Cos(a * 0.8f) * l.sway * 0.6f);
            l.t.rotation = Quaternion.Euler(Mathf.Sin(a * 1.3f) * 50f, l.spin * Time.time, Mathf.Cos(a) * 40f);
        }
    }

    void Spawn(Vector2 tree, float c0y)
    {
        Leaf l = null;
        int inAir = 0;
        foreach (Leaf x in leaves) { if (x.on) inAir++; else if (l == null) l = x; }
        if (inAir >= maxInAir) return;
        if (l == null)
        {
            if (leaves.Count >= maxInAir) return;
            l = new Leaf { t = Make() };
            leaves.Add(l);
        }
        Vector2 off = Random.insideUnitCircle * 1.4f;
        off += off.sqrMagnitude > 0.0001f ? off.normalized * 0.5f : Vector2.right * 0.5f;     // clear of the trunk's collider
        float groundY = 0f;
        if (Physics.Raycast(new Vector3(tree.x + off.x, 30f, tree.y + off.y), Vector3.down, out RaycastHit hit, 40f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore)) groundY = hit.point.y;
        if (Mathf.Abs(groundY - c0y) > 12f) return;                                          // a roof or a bridge: not here
        l.ground = groundY;
        l.from = new Vector3(tree.x + off.x, groundY + Random.Range(3.4f, 5.6f), tree.y + off.y);
        l.born = Time.time; l.life = Random.Range(4.5f, 7f);
        l.sway = Random.Range(0.3f, 0.8f); l.spin = Random.Range(-120f, 120f); l.phase = Random.value * 10f;
        l.on = true;
        l.t.gameObject.SetActive(true);
        l.t.GetComponent<Renderer>().sharedMaterial = leafMaterials.Length > 0 && leafMaterials[0] != null
            ? leafMaterials[Random.Range(0, leafMaterials.Length)] : Mat(Random.Range(0, Cols.Length));
    }

    static Material[] mats;
    /// Only if Lundun → 37's leaf materials are missing.
    static Material Mat(int i)
    {
        if (mats == null)
        {
            mats = new Material[Cols.Length];
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            for (int k = 0; k < Cols.Length; k++)
            {
                Material m = new Material(s != null ? s : Shader.Find("Standard")) { name = "FallingLeaf" };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Cols[k]);
                m.color = Cols[k];
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);                 // both sides
                mats[k] = m;
            }
        }
        return mats[i];
    }

    Transform Make()
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = "FallingLeaf";
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(transform, false);
        g.transform.localScale = new Vector3(0.09f, 0.14f, 1f);
        Renderer r = g.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        g.SetActive(false);
        return g.transform;
    }
}
