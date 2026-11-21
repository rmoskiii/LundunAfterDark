// G7 (step 1): Lundun → 39  Test Hill. A hill to try out before any of the city gets one.
// Builds ONE new object, "LundunTestHill", far out of town (4 km east and north of the map, nothing else there):
// a street that climbs and drops at a few different gradients, the land rising and falling with it, terraced houses
// stepping up and down the hill, pavements and kerbs, street lamps and a gradient sign at each change:
//     flat start  ·  5% up  ·  level  ·  10% up  ·  the top (level)  ·  14% down  ·  6% down  ·  flat end, turning circle
// In the game a TEST HILL button (top of the screen) takes James there (in his car if he's driving) and back again,
// to see how cars, running, the camera and the look work on slopes. Nothing in the city changes: no rerun of anything.
// Lundun → 39b takes it out again.
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunTestHillBuilder
{
    const string Dir = "Assets/World/TestHill";
    const string MeshPath = Dir + "/TestHillMeshes.asset";
    const string RootName = "LundunTestHill";
    static readonly Vector3 Origin = new Vector3(4000f, 0f, 4000f);

    // the street's height along its length (z metres, height metres); Signs[i] names the stretch from Knees[i] to Knees[i + 1]
    static readonly Vector2[] Knees =
    {
        new Vector2(-60f, 0f), new Vector2(60f, 0f),          // flat start
        new Vector2(180f, 6f),                                 // 5% up
        new Vector2(220f, 6f),                                 // level
        new Vector2(300f, 14f),                                // 10% up
        new Vector2(340f, 14f),                                // the top
        new Vector2(410f, 4.2f),                               // 14% down
        new Vector2(480f, 0f),                                 // 6% down
        new Vector2(640f, 0f),                                 // flat end
    };
    static readonly string[] Signs = { "", "5% UP", "LEVEL", "10% UP", "THE TOP", "14% DOWN", "6% DOWN", "FLAT" };
    const float ZStart = -60f, ZEnd = 640f, RoadHW = 3.75f, PaveW = 3f, Kerb = 0.15f;
    const float TurnZ = 600f, TurnR = 14f;

    static float[] profile;      // smoothed height every metre from ZStart

    [MenuItem("Lundun/39  Test Hill (G7)", false, 48)]
    static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Material road = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/Road.mat");
        Material pave = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/Pavement.mat");
        Material mark = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/Marking.mat");
        Material grass = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/Park.mat");
        if (grass == null) grass = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/Ground.mat");
        if (road == null || pave == null || grass == null) { Dialog("Couldn't find the street materials in Assets/World/Materials (Road, Pavement, Park). Has Lundun → 1 been run?"); return; }
        if (mark == null) mark = road;

        EnsureFolder(Dir);
        Material[] brick = { Plain("Hill_Brick1", new Color(0.55f, 0.31f, 0.24f)), Plain("Hill_Brick2", new Color(0.62f, 0.45f, 0.33f)), Plain("Hill_Render", new Color(0.82f, 0.79f, 0.72f)) };
        Material roof = Plain("Hill_Roof", new Color(0.22f, 0.23f, 0.26f));
        Material dark = Plain("Hill_Dark", new Color(0.08f, 0.08f, 0.09f));
        Material window = Plain("Hill_Window", new Color(0.16f, 0.2f, 0.26f));
        Material glow = Plain("Hill_LampGlow", new Color(1f, 0.92f, 0.75f), new Color(1f, 0.86f, 0.6f) * 2.2f);
        Material sign = Plain("Hill_Sign", Color.white);

        Smooth();
        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        AssetDatabase.DeleteAsset(MeshPath);
        GameObject root = new GameObject(RootName);
        root.transform.position = Origin;
        Mesh container = null;

        // 1. the land: a hill across the street's whole length, easing down to flat ground 150 m either side
        Mesh land = Land();
        Add(root, "Land", land, grass, true, ref container);
        GameObject flat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        flat.name = "FlatGround"; flat.transform.SetParent(root.transform, false);
        flat.transform.localPosition = new Vector3(0f, -0.55f, 290f); flat.transform.localScale = new Vector3(1400f, 1f, 1400f);
        flat.GetComponent<MeshRenderer>().sharedMaterial = grass;

        // 2. the street: carriageway, pavements with their kerbs, the turning circle at the end
        Add(root, "Carriageway", Street(), road, true, ref container);
        Add(root, "Pavements", Pavements(), pave, true, ref container);
        Add(root, "TurningCircle", Disc(TurnZ, TurnR, 0.02f), road, true, ref container);
        Add(root, "Markings", Markings(), mark, false, ref container);

        // 3. terraced houses stepping up and down the hill, lamps, the gradient signs
        Transform houses = new GameObject("Houses").transform; houses.SetParent(root.transform, false);
        int n = 0;
        foreach (int side in new[] { -1, 1 })
            for (float z = 0f; z < 570f; z += 7f)
            {
                if (Mathf.Abs(z - TurnZ) < TurnR + 8f) continue;
                House(houses, side, z, brick[(n++ * 7 + (side > 0 ? 1 : 0)) % brick.Length], roof, window, dark);
            }
        Transform lamps = new GameObject("Lamps").transform; lamps.SetParent(root.transform, false);
        for (float z = -40f; z < 600f; z += 30f) foreach (int side in new[] { -1, 1 }) Lamp(lamps, side, z + (side > 0 ? 15f : 0f), dark, glow);
        Transform signs = new GameObject("Signs").transform; signs.SetParent(root.transform, false);
        for (int i = 1; i < Knees.Length - 1; i++) Sign(signs, Knees[i].x - 6f, Signs[i], dark, sign);      // just before each change: what's next

        // 4. the way there and back (the TEST HILL button)
        LundunTestHill th = root.AddComponent<LundunTestHill>();
        th.Set(Origin + new Vector3(-1.9f, Height(-40f) + 0.3f, -40f), 0f);
        EditorUtility.SetDirty(th);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        Debug.Log("[Lundun] G7 test hill built far out of town (" + Origin + "). Nothing in the city changed. Cmd+S, then Play and tap TEST HILL.");
        Dialog("The test hill's built, 4 km out of town (nothing in the city changed).\n\nPress Cmd+S, then Play: TEST HILL at the top of the screen takes James there (in his car if he's driving) and BACK TO TOWN brings him back.\n\nThe street: flat · 5% up · level · 10% up · the top · 14% down · 6% down · flat, with a turning circle at the end.");
    }

    [MenuItem("Lundun/39b  Remove Test Hill (G7)", false, 49)]
    static void Remove()
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null) { Object.DestroyImmediate(old); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }
        Debug.Log("[Lundun] G7 test hill removed (its files in " + Dir + " are left; delete the folder if you like).");
    }

    // ------------------------------------------------------------------ the street's height
    static void Smooth()
    {
        int n = Mathf.CeilToInt(ZEnd - ZStart) + 1;
        float[] raw = new float[n];
        for (int i = 0; i < n; i++)
        {
            float z = ZStart + i;
            float y = Knees[Knees.Length - 1].y;
            for (int k = 0; k + 1 < Knees.Length; k++)
                if (z >= Knees[k].x && z <= Knees[k + 1].x) { y = Mathf.Lerp(Knees[k].y, Knees[k + 1].y, (z - Knees[k].x) / (Knees[k + 1].x - Knees[k].x)); break; }
            raw[i] = y;
        }
        // round off every change of gradient (twice over ±9 m), so cars roll over the top instead of jumping
        for (int pass = 0; pass < 2; pass++)
        {
            float[] o = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = 0f; int c = 0;
                for (int j = i - 9; j <= i + 9; j++) { s += raw[Mathf.Clamp(j, 0, n - 1)]; c++; }
                o[i] = s / c;
            }
            raw = o;
        }
        profile = raw;
    }

    static float Height(float z)
    {
        float f = Mathf.Clamp(z - ZStart, 0f, profile.Length - 1.001f);
        int i = (int)f;
        return Mathf.Lerp(profile[i], profile[i + 1], f - i);
    }

    /// The land's height: the street's, out to 45 m either side, easing down to flat by 150 m.
    static float Ground(float x, float z)
    {
        float k = Mathf.InverseLerp(150f, 45f, Mathf.Abs(x));
        return Height(z) * Mathf.SmoothStep(0f, 1f, k);
    }

    // ------------------------------------------------------------------ meshes
    static Mesh Land()
    {
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        const float step = 4f, x0 = -170f, x1 = 170f, z0 = -120f, z1 = 700f;
        int nx = Mathf.RoundToInt((x1 - x0) / step) + 1, nz = Mathf.RoundToInt((z1 - z0) / step) + 1;
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                float x = x0 + i * step, z = z0 + j * step;
                float y = Ground(x, z) - (Mathf.Abs(x) < RoadHW + PaveW + 0.5f ? 0.05f : 0f);
                v.Add(new Vector3(x, y, z)); uv.Add(new Vector2((Origin.x + x) * 0.1f, (Origin.z + z) * 0.1f));
            }
        for (int j = 0; j + 1 < nz; j++)
            for (int i = 0; i + 1 < nx; i++)
            {
                int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
            }
        return Make("Land", v, uv, t);
    }

    static Mesh Street()
    {
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        for (float z = ZStart; z <= ZEnd - 0.5f; z += 2f)
        {
            float z2 = Mathf.Min(z + 2f, ZEnd);
            Quad(v, uv, t, new Vector3(-RoadHW, Height(z) + 0.02f, z), new Vector3(RoadHW, Height(z) + 0.02f, z),
                           new Vector3(RoadHW, Height(z2) + 0.02f, z2), new Vector3(-RoadHW, Height(z2) + 0.02f, z2), true);
        }
        return Make("Carriageway", v, uv, t);
    }

    static Mesh Pavements()
    {
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        const float tile = 9f, kv = 0.012f;
        for (float z = ZStart; z <= ZEnd - 0.5f; z += 2f)
        {
            float z2 = Mathf.Min(z + 2f, ZEnd);
            if (z2 > TurnZ - TurnR - 1f && z < TurnZ + TurnR + 1f) continue;            // the turning circle
            float y1 = Height(z), y2 = Height(z2);
            foreach (int s in new[] { -1, 1 })
            {
                float xi = s * RoadHW, xo = s * (RoadHW + PaveW);
                // top
                QuadUV(v, uv, t, new Vector3(xi, y1 + Kerb, z), new Vector3(xi, y2 + Kerb, z2), new Vector3(xo, y2 + Kerb, z2), new Vector3(xo, y1 + Kerb, z),
                       new Vector2(z / tile, 0f), new Vector2(z2 / tile, 0f), new Vector2(z2 / tile, PaveW / tile), new Vector2(z / tile, PaveW / tile), Vector3.up);
                // the kerb, facing the road
                QuadUV(v, uv, t, new Vector3(xi, y1, z), new Vector3(xi, y2, z2), new Vector3(xi, y2 + Kerb, z2), new Vector3(xi, y1 + Kerb, z),
                       new Vector2(z / tile, kv), new Vector2(z2 / tile, kv), new Vector2(z2 / tile, kv), new Vector2(z / tile, kv), new Vector3(-s, 0f, 0f));
                // the back edge, down to the land
                QuadUV(v, uv, t, new Vector3(xo, y1 - 0.3f, z), new Vector3(xo, y2 - 0.3f, z2), new Vector3(xo, y2 + Kerb, z2), new Vector3(xo, y1 + Kerb, z),
                       new Vector2(z / tile, kv), new Vector2(z2 / tile, kv), new Vector2(z2 / tile, kv), new Vector2(z / tile, kv), new Vector3(s, 0f, 0f));
            }
        }
        return Make("Pavements", v, uv, t);
    }

    static Mesh Disc(float zc, float r, float lift)
    {
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        float y = Height(zc) + lift;
        v.Add(new Vector3(0f, y, zc)); uv.Add(new Vector2(Origin.x * 0.1f, (Origin.z + zc) * 0.1f));
        const int seg = 32;
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector3 p = new Vector3(Mathf.Sin(a) * r, y, zc + Mathf.Cos(a) * r);
            v.Add(p); uv.Add(new Vector2((Origin.x + p.x) * 0.1f, (Origin.z + p.z) * 0.1f));
            if (i > 0) { t.Add(0); t.Add(i); t.Add(i + 1); }
        }
        return Make("TurningCircle", v, uv, t);
    }

    static Mesh Markings()
    {
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        for (float z = ZStart + 2f; z + 3f < TurnZ - TurnR; z += 9f)
            Strip(v, uv, t, 0f, 0.12f, z, z + 3f);                          // the dashed centre line
        foreach (int s in new[] { -1, 1 })
            for (float z = ZStart; z + 2f < TurnZ - TurnR; z += 2f) Strip(v, uv, t, s * (RoadHW - 0.4f), 0.1f, z, z + 2f);   // the edge lines
        return Make("Markings", v, uv, t);
    }

    static void Strip(List<Vector3> v, List<Vector2> uv, List<int> t, float x, float w, float z0, float z1)
    {
        for (float z = z0; z < z1 - 0.01f; z += 1f)
        {
            float a = z, b = Mathf.Min(z + 1f, z1);
            Quad(v, uv, t, new Vector3(x - w / 2, Height(a) + 0.04f, a), new Vector3(x + w / 2, Height(a) + 0.04f, a),
                           new Vector3(x + w / 2, Height(b) + 0.04f, b), new Vector3(x - w / 2, Height(b) + 0.04f, b), true);
        }
    }

    static void Quad(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool upward)
    {
        QuadUV(v, uv, t, a, b, c, d, W(a), W(b), W(c), W(d), upward ? Vector3.up : Vector3.down);
    }

    static Vector2 W(Vector3 p) { return new Vector2((Origin.x + p.x) * 0.1f, (Origin.z + p.z) * 0.1f); }

    /// a, b, c, d round the quad, facing `want` (the winding's picked to match).
    static void QuadUV(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 want)
    {
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) >= 0f) { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
        else { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
    }

    static Mesh Make(string name, List<Vector3> v, List<Vector2> uv, List<int> t)
    {
        Mesh m = new Mesh { name = "TestHill_" + name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static void Add(GameObject root, string name, Mesh mesh, Material mat, bool solid, ref Mesh container)
    {
        if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; }
        else AssetDatabase.AddObjectToAsset(mesh, container);
        GameObject g = new GameObject(name);
        g.transform.SetParent(root.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = name == "Markings" ? ShadowCastingMode.Off : ShadowCastingMode.On;
        if (solid) g.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    // ------------------------------------------------------------------ houses, lamps, signs
    static void House(Transform parent, int side, float z, Material wall, Material roof, Material window, Material dark)
    {
        const float w = 6.6f, depth = 9f, tall = 7f, setback = 1.5f;
        float xf = side * (RoadHW + PaveW + setback), xb = side * (RoadHW + PaveW + setback + depth);
        float y0 = Mathf.Min(Ground(xf, z - w / 2), Ground(xf, z + w / 2), Ground(xb, z - w / 2), Ground(xb, z + w / 2)) - 0.4f;
        float yf = Mathf.Min(Height(z - w / 2), Height(z + w / 2)) + Kerb;          // the front door's at the pavement's level
        float top = Mathf.Max(Height(z - w / 2), Height(z + w / 2)) + Kerb + tall;
        float cx = (xf + xb) / 2f;
        GameObject h = Box(parent, "House", new Vector3(cx, (y0 + top) / 2f, z), new Vector3(depth, top - y0, w - 0.15f), wall, true);
        Box(h.transform.parent, "Roof", new Vector3(cx, top + 0.25f, z), new Vector3(depth + 0.4f, 0.5f, w + 0.05f), roof, false);
        float face = side * (RoadHW + PaveW + setback - 0.04f);
        Box(parent, "Door", new Vector3(face, yf + 1.05f, z - 1.6f), new Vector3(0.1f, 2.1f, 1f), dark, false);
        Box(parent, "Window", new Vector3(face, yf + 1.5f, z + 1.2f), new Vector3(0.1f, 1.3f, 1.8f), window, false);
        Box(parent, "Window", new Vector3(face, yf + 4.6f, z - 1.2f), new Vector3(0.1f, 1.3f, 1.4f), window, false);
        Box(parent, "Window", new Vector3(face, yf + 4.6f, z + 1.3f), new Vector3(0.1f, 1.3f, 1.4f), window, false);
        // a step or two up to the pavement where the land's lower than it
        Box(parent, "FrontPath", new Vector3(side * (RoadHW + PaveW + setback / 2f), yf - 0.15f, z - 1.6f), new Vector3(setback + 0.1f, 0.3f, 1.4f), dark, true);
    }

    static void Lamp(Transform parent, int side, float z, Material dark, Material glow)
    {
        float x = side * (RoadHW + 0.5f), y = Height(z) + Kerb;
        Box(parent, "LampPost", new Vector3(x, y + 3f, z), new Vector3(0.16f, 6f, 0.16f), dark, true);
        Box(parent, "LampArm", new Vector3(x - side * 0.6f, y + 5.95f, z), new Vector3(1.3f, 0.1f, 0.12f), dark, false);
        Box(parent, "LampHead", new Vector3(x - side * 1.15f, y + 5.85f, z), new Vector3(0.5f, 0.12f, 0.3f), glow, false);
    }

    static void Sign(Transform parent, float z, string text, Material dark, Material face)
    {
        if (string.IsNullOrEmpty(text)) return;
        float x = -(RoadHW + 1.2f), y = Height(z) + Kerb;
        Box(parent, "SignPost", new Vector3(x, y + 1.1f, z), new Vector3(0.1f, 2.2f, 0.1f), dark, true);
        Box(parent, "SignBoard", new Vector3(x, y + 2.4f, z), new Vector3(1.6f, 0.7f, 0.06f), face, false);
        GameObject g = new GameObject("SignText", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        g.transform.localPosition = new Vector3(x, y + 2.4f, z - 0.05f);
        ((RectTransform)g.transform).sizeDelta = new Vector2(1.5f, 0.6f);
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = text; t.color = new Color(0.1f, 0.1f, 0.12f); t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true; t.fontSizeMin = 0.5f; t.fontSizeMax = 4f;
    }

    static GameObject Box(Transform parent, string name, Vector3 at, Vector3 size, Material m, bool solid)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.transform.localScale = size;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        if (!solid) Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    static Material Plain(string name, Color c, Color? emission = null)
    {
        string path = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.15f);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
