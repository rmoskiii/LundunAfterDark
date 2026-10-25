using UnityEngine;

/// V2: a car model's paint jobs. The real-car models have their paint baked into a texture (with the shading), so a
/// colour tint can't repaint them; instead Lundun → 10 makes a few ready-painted versions of the body and lists them
/// here. Each car picks one when it's made (traffic, stealable parked cars), and the baked parked cars pick by name.
public class LundunCarPaint : MonoBehaviour
{
    [Tooltip("The body (the part with the paint)")]
    [SerializeField] private Renderer body;
    [Tooltip("Which of the body's materials is the paint")]
    [SerializeField] private int slot;
    [SerializeField] private Material[] variants = new Material[0];
    [Tooltip("Pick a paint job at random when the car is made (off = keep the one it has)")]
    [SerializeField] private bool randomOnStart = true;

    public Material[] Variants => variants;
    public Renderer Body => body;
    public int Slot => slot;

    public void Setup(Renderer r, int materialSlot, Material[] paints)
    {
        body = r; slot = materialSlot; variants = paints;
    }

    void Awake()
    {
        if (randomOnStart && variants != null && variants.Length > 0) Use(Random.Range(0, variants.Length));
    }

    public void Use(int i)
    {
        if (body == null || variants == null || variants.Length == 0) return;
        Material[] mats = body.sharedMaterials;
        if (slot < 0 || slot >= mats.Length) return;
        mats[slot] = variants[Mathf.Clamp(i, 0, variants.Length - 1)];
        body.sharedMaterials = mats;
    }

    /// The paint job whose name has this colour in it ("silver", "black"…), or the one at fallback.
    public Material Named(string colour, int fallback)
    {
        if (variants == null || variants.Length == 0) return null;
        string c = colour.ToLowerInvariant();
        if (c == "blue") c = "navy";
        foreach (Material m in variants) if (m != null && m.name.ToLowerInvariant().Contains(c)) return m;
        return variants[System.Math.Abs(fallback) % variants.Length];
    }
}
