using System.Collections.Generic;
using UnityEngine;

/// Lundun HUD art, drawn in code at start-up (no image files): button discs, rings, the rounded "pill" behind
/// contextual prompts, and white icons. Everything is a signed-distance shape, so edges stay smooth at any size.
/// All original designs.
public static class LundunHudArt
{
    public delegate float Shape(Vector2 p);      // p in 0..1 (y up); returns distance to the edge (negative = inside)

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // ------------------------------------------------------------------ the sprites
    public static Sprite Disc => Get("disc", 256, p => Circle(p, C, 0.48f));
    public static Sprite Ring => Get("ring", 256, p => RingD(p, C, 0.475f, 0.016f));
    public static Sprite RingThick => Get("ringThick", 256, p => RingD(p, C, 0.465f, 0.035f));
    /// Rounded rectangle for 9-slicing (pills, panels).
    public static Sprite Pill => Get("pill", 96, p => RBox(p, C, new Vector2(0.48f, 0.48f), 0.3f), new Vector4(32, 32, 32, 32));
    public static Sprite Panel => Get("panel", 64, p => RBox(p, C, new Vector2(0.48f, 0.48f), 0.2f), new Vector4(16, 16, 16, 16));

    public static Sprite Crosshair => Get("i_cross", 128, p => Min(
        RingD(p, C, 0.26f, 0.06f),
        Seg(p, new Vector2(0.5f, 0.06f), new Vector2(0.5f, 0.3f), 0.06f),
        Seg(p, new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.94f), 0.06f),
        Seg(p, new Vector2(0.06f, 0.5f), new Vector2(0.3f, 0.5f), 0.06f),
        Seg(p, new Vector2(0.7f, 0.5f), new Vector2(0.94f, 0.5f), 0.06f),
        Circle(p, C, 0.045f)));

    public static Sprite Jump => Get("i_jump", 128, p => Min(
        Line(p, 0.085f, V(0.22f, 0.52f), V(0.5f, 0.8f), V(0.78f, 0.52f)),
        Line(p, 0.085f, V(0.22f, 0.24f), V(0.5f, 0.52f), V(0.78f, 0.24f))));

    public static Sprite Left => Get("i_left", 128, p => Line(p, 0.11f, V(0.64f, 0.18f), V(0.32f, 0.5f), V(0.64f, 0.82f)));
    public static Sprite Right => Get("i_right", 128, p => Line(p, 0.11f, V(0.36f, 0.18f), V(0.68f, 0.5f), V(0.36f, 0.82f)));
    public static Sprite Gas => Get("i_gas", 128, p => Round(Poly(p, V(0.5f, 0.82f), V(0.82f, 0.28f), V(0.18f, 0.28f)), 0.03f));
    public static Sprite Brake => Get("i_brake", 128, p => Round(Poly(p, V(0.5f, 0.18f), V(0.18f, 0.72f), V(0.82f, 0.72f)), 0.03f));

    public static Sprite Exit => Get("i_exit", 128, p => Min(
        Line(p, 0.07f, V(0.52f, 0.2f), V(0.2f, 0.2f), V(0.2f, 0.8f), V(0.52f, 0.8f)),
        Seg(p, V(0.42f, 0.5f), V(0.84f, 0.5f), 0.08f),
        Line(p, 0.08f, V(0.68f, 0.34f), V(0.84f, 0.5f), V(0.68f, 0.66f))));

    public static Sprite Car => Get("i_car", 128, p => Sub(Min(
        RBox(p, V(0.5f, 0.44f), V(0.4f, 0.11f), 0.07f),
        Round(Poly(p, V(0.28f, 0.52f), V(0.36f, 0.72f), V(0.64f, 0.72f), V(0.74f, 0.52f)), 0.03f),
        Circle(p, V(0.29f, 0.3f), 0.1f), Circle(p, V(0.71f, 0.3f), 0.1f)),
        Min(Circle(p, V(0.29f, 0.3f), 0.04f), Circle(p, V(0.71f, 0.3f), 0.04f),
            Round(Poly(p, V(0.38f, 0.56f), V(0.42f, 0.67f), V(0.49f, 0.67f), V(0.49f, 0.56f)), 0.01f),
            Round(Poly(p, V(0.53f, 0.56f), V(0.53f, 0.67f), V(0.61f, 0.67f), V(0.66f, 0.56f)), 0.01f))));

    public static Sprite Bike => Get("i_bike", 128, p => Min(
        RingD(p, V(0.26f, 0.34f), 0.15f, 0.05f), RingD(p, V(0.74f, 0.34f), 0.15f, 0.05f),
        Line(p, 0.05f, V(0.26f, 0.34f), V(0.47f, 0.34f), V(0.4f, 0.6f), V(0.26f, 0.34f)),
        Line(p, 0.05f, V(0.47f, 0.34f), V(0.66f, 0.6f), V(0.74f, 0.34f)),
        Seg(p, V(0.4f, 0.6f), V(0.66f, 0.6f), 0.05f),
        Seg(p, V(0.33f, 0.66f), V(0.47f, 0.66f), 0.06f),
        Line(p, 0.05f, V(0.66f, 0.6f), V(0.62f, 0.72f), V(0.72f, 0.74f))));

    /// Lundun Underground: our diamond with a U in it.
    public static Sprite Tube => Get("i_tube", 128, p => Min(
        Sub(Poly(p, V(0.5f, 0.94f), V(0.94f, 0.5f), V(0.5f, 0.06f), V(0.06f, 0.5f)),
            Poly(p, V(0.5f, 0.84f), V(0.84f, 0.5f), V(0.5f, 0.16f), V(0.16f, 0.5f))),
        Sub(RBox(p, V(0.5f, 0.47f), V(0.15f, 0.17f), 0.13f), Min(RBox(p, V(0.5f, 0.52f), V(0.075f, 0.16f), 0.07f), RBox(p, V(0.5f, 0.66f), V(0.075f, 0.06f), 0f)))));

    public static Sprite Star => Get("i_star", 128, p => Round(Poly(p, StarPoints()), 0.02f));
    public static Sprite Hand => Get("i_hand", 128, p => Min(        // "interact"
        RBox(p, V(0.5f, 0.36f), V(0.2f, 0.2f), 0.1f),
        Seg(p, V(0.36f, 0.5f), V(0.36f, 0.78f), 0.09f), Seg(p, V(0.47f, 0.52f), V(0.47f, 0.86f), 0.09f),
        Seg(p, V(0.58f, 0.52f), V(0.58f, 0.82f), 0.09f), Seg(p, V(0.68f, 0.48f), V(0.68f, 0.7f), 0.09f),
        Seg(p, V(0.3f, 0.4f), V(0.2f, 0.56f), 0.09f)));

    // V2b: full map
    public static Sprite Close => Get("i_close", 128, p => Min(
        Seg(p, V(0.26f, 0.26f), V(0.74f, 0.74f), 0.1f), Seg(p, V(0.26f, 0.74f), V(0.74f, 0.26f), 0.1f)));
    public static Sprite Plus => Get("i_plus", 128, p => Min(
        Seg(p, V(0.22f, 0.5f), V(0.78f, 0.5f), 0.1f), Seg(p, V(0.5f, 0.22f), V(0.5f, 0.78f), 0.1f)));
    public static Sprite Minus => Get("i_minus", 128, p => Seg(p, V(0.22f, 0.5f), V(0.78f, 0.5f), 0.1f));
    public static Sprite Locate => Get("i_locate", 128, p => Min(
        RingD(p, C, 0.24f, 0.07f), Circle(p, C, 0.09f),
        Seg(p, V(0.5f, 0.08f), V(0.5f, 0.22f), 0.07f), Seg(p, V(0.5f, 0.78f), V(0.5f, 0.92f), 0.07f),
        Seg(p, V(0.08f, 0.5f), V(0.22f, 0.5f), 0.07f), Seg(p, V(0.78f, 0.5f), V(0.92f, 0.5f), 0.07f)));
    /// Map pin: a teardrop with a hole, point at the bottom centre.
    public static Sprite Pin => Get("i_pin", 128, p => Sub(
        Min(Circle(p, V(0.5f, 0.64f), 0.27f), Round(Poly(p, V(0.5f, 0.06f), V(0.27f, 0.52f), V(0.73f, 0.52f)), 0.01f)),
        Circle(p, V(0.5f, 0.64f), 0.1f)));
    public static Sprite List => Get("i_list", 128, p => Min(
        Circle(p, V(0.22f, 0.72f), 0.06f), Circle(p, V(0.22f, 0.5f), 0.06f), Circle(p, V(0.22f, 0.28f), 0.06f),
        Seg(p, V(0.38f, 0.72f), V(0.82f, 0.72f), 0.09f), Seg(p, V(0.38f, 0.5f), V(0.82f, 0.5f), 0.09f), Seg(p, V(0.38f, 0.28f), V(0.82f, 0.28f), 0.09f)));

    // V5: pause menu
    public static Sprite Pause => Get("i_pause", 128, p => Min(
        RBox(p, V(0.36f, 0.5f), V(0.08f, 0.26f), 0.05f), RBox(p, V(0.64f, 0.5f), V(0.08f, 0.26f), 0.05f)));

    /// Soft dark fade (opaque at the top, clear at the bottom), stretched behind headers.
    public static Sprite FadeDown
    {
        get
        {
            if (cache.TryGetValue("fadeDown", out Sprite s) && s != null) return s;
            Texture2D tex = new Texture2D(4, 128, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            Color32[] px = new Color32[4 * 128];
            for (int y = 0; y < 128; y++)
            {
                float a = Mathf.SmoothStep(0f, 1f, y / 127f);            // row 0 = bottom
                for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, 4, 128), new Vector2(0.5f, 0.5f), 100f);
            cache["fadeDown"] = s;
            return s;
        }
    }

    // ------------------------------------------------------------------ drawing
    static readonly Vector2 C = new Vector2(0.5f, 0.5f);
    static Vector2 V(float x, float y) { return new Vector2(x, y); }

    static Sprite Get(string key, int size, Shape shape, Vector4 border = default(Vector4))
    {
        if (cache.TryGetValue(key, out Sprite s) && s != null) return s;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.name = "LundunHud_" + key;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        Color32[] px = new Color32[size * size];
        float inv = 1f / size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = shape(new Vector2((x + 0.5f) * inv, (y + 0.5f) * inv)) * size;   // in pixels
                byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        s.name = tex.name;
        cache[key] = s;
        return s;
    }

    // ------------------------------------------------------------------ signed-distance shapes (0..1 units)
    public static float Circle(Vector2 p, Vector2 c, float r) { return (p - c).magnitude - r; }
    public static float RingD(Vector2 p, Vector2 c, float r, float thick) { return Mathf.Abs((p - c).magnitude - r) - thick * 0.5f; }
    public static float Seg(Vector2 p, Vector2 a, Vector2 b, float thick)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, Vector2.Dot(ba, ba)));
        return (pa - ba * h).magnitude - thick * 0.5f;
    }
    public static float Line(Vector2 p, float thick, params Vector2[] pts)
    {
        float d = float.MaxValue;
        for (int i = 0; i + 1 < pts.Length; i++) d = Mathf.Min(d, Seg(p, pts[i], pts[i + 1], thick));
        return d;
    }
    public static float RBox(Vector2 p, Vector2 c, Vector2 half, float r)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
        Vector2 qm = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return qm.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }
    /// Exact signed distance to any closed polygon (convex or not).
    public static float Poly(Vector2 p, params Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]);
        float s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            Vector2 e = v[j] - v[i], w = p - v[i];
            Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Mathf.Max(1e-9f, Vector2.Dot(e, e)));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }
    public static float Round(float d, float r) { return d - r; }
    public static float Sub(float a, float b) { return Mathf.Max(a, -b); }
    public static float Min(params float[] d) { float m = float.MaxValue; foreach (float x in d) m = Mathf.Min(m, x); return m; }

    static Vector2[] StarPoints()
    {
        Vector2[] v = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
            float r = (i % 2 == 0) ? 0.44f : 0.19f;
            v[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.47f + Mathf.Sin(a) * r);
        }
        return v;
    }
}
