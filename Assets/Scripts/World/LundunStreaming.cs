using System.Collections.Generic;
using UnityEngine;

/// E0: switches small, far-away detail off as the map grows. The camera already stops drawing at the fog
/// (about 450 m) and the dressing and parked cars switch their own far blocks off; this does the same for the rest
/// of the fine detail that is invisible long before the fog: the street markings, puddles and litter, the shop-light
/// glows on the pavements, the street lamps, and anything an expansion adds under "Detail" roots.
/// Only renderers are switched (never colliders), a slice of them each frame, so it costs almost nothing.
/// Lives on the "LundunPerformance" object (made by Lundun → 26).
public class LundunStreaming : MonoBehaviour
{
    public static LundunStreaming Instance { get; private set; }

    [Tooltip("Scene paths whose renderers are fine detail (switched off beyond Detail Distance)")]
    [SerializeField] private string[] detailRoots = { "LundunStreets", "LundunShopLight", "World/Lamps" };
    [Tooltip("Fine detail further than this from the camera isn't drawn (m)")]
    [SerializeField] private float detailDistance = 240f;
    [Tooltip("Margin so things don't flicker on and off at the edge (m)")]
    [SerializeField] private float hysteresis = 12f;
    [SerializeField] private int checksPerFrame = 250;

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Bounds> bounds = new List<Bounds>();
    private int cursor, visible;

    public int Total { get { return renderers.Count; } }
    public int Visible { get { return visible; } }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() { Collect(); }

    /// Finds the detail renderers again (call after building something new at runtime).
    public void Collect()
    {
        renderers.Clear(); bounds.Clear();
        foreach (string path in detailRoots)
        {
            Transform root = Find(path);
            if (root == null) continue;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SkinnedMeshRenderer || r is ParticleSystemRenderer) continue;
                renderers.Add(r); bounds.Add(r.bounds);
            }
        }
        visible = renderers.Count;
        cursor = 0;
    }

    static Transform Find(string path)
    {
        string[] parts = path.Split('/');
        GameObject top = GameObject.Find(parts[0]);
        if (top == null) return null;
        Transform t = top.transform;
        for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
        return t;
    }

    void Update()
    {
        if (renderers.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 c = cam.transform.position;
        float on = detailDistance * detailDistance, off = (detailDistance + hysteresis) * (detailDistance + hysteresis);
        int n = Mathf.Min(checksPerFrame, renderers.Count);
        for (int k = 0; k < n; k++)
        {
            if (cursor >= renderers.Count) cursor = 0;
            Renderer r = renderers[cursor];
            if (r != null)
            {
                float d2 = (bounds[cursor].ClosestPoint(c) - c).sqrMagnitude;
                if (r.enabled && d2 > off) { r.enabled = false; visible--; }
                else if (!r.enabled && d2 < on) { r.enabled = true; visible++; }
            }
            cursor++;
        }
    }
}
