// Lundun → 20  Surface Detail (V4)
// Works on the city that's already built (nothing is rebuilt, nothing moves):
//   1. draws the detail textures (LundunSurfaceArt) into Assets/World/Generated/Surface and imports them for the iPhone
//   2. tarmac on Road, paving slabs + granite kerb on Pavement, a facade per district on the Bld_* materials
//      (Peckam brick, Knightsbury stucco, Gilmore estate brick, Canary Quay warehouse brick, Canary towers glass/panels)
//   3. lines the paving up with each street (slabs follow the kerb), and points roofs at a clean bit of wall
//   4. adds soft contact shading: darker ground along every building's base (stronger in corners) and in the gutters
// A backup of the old material settings is kept; Lundun → 20b undoes all of it.
using System.Collections.Generic;
using System.IO;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunSurfaceBuilder
{
    const string Dir = "Assets/World/Generated/Surface";
    const string MatDir = "Assets/World/Materials";
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string BackupPath = Dir + "/SurfaceBackup.json";
    const string ShadeMeshes = Dir + "/ContactShadeMeshes.asset";
    const float ShadeY = 0.02f;

    [System.Serializable] class MatState { public string path, baseMapGuid; public Color colour; }
    [System.Serializable] class Backup { public List<MatState> mats = new List<MatState>(); }

    [MenuItem("Lundun/20  Surface Detail (V4)", false, 21)]
    public static void Build()
    {
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + JsonPath, "OK"); return; }
        LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
        Transform world = Root("World");
        if (world == null) { EditorUtility.DisplayDialog("Lundun", "No World object in the scene.", "OK"); return; }
        Ensure("Assets/World", "Generated"); Ensure("Assets/World/Generated", "Surface");

        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Drawing textures…", 0.1f);
            Texture2D tarmac = Save("Tarmac", SurfaceArt.Tarmac(), 8);
            Texture2D paving = SaveBands("Paving", 8);                // L2: four district pavements stacked (StreetArt.PavingBands)
            Dictionary<string, Texture2D> fac = new Dictionary<string, Texture2D>();
            foreach (string s in new[] { "brick", "stucco", "estate", "warehouse", "panels" })
                fac[s] = Save("Facade_" + s, SurfaceArt.Facade(s), 4);

            EditorUtility.DisplayProgressBar("Lundun", "Materials…", 0.35f);
            Backup bk = LoadOrMakeBackup();
            int mats = 0;
            mats += Apply(bk, MatDir + "/Road.mat", tarmac, 1.12f);
            mats += Apply(bk, MatDir + "/Pavement.mat", paving, 1.1f);
            foreach (string g in AssetDatabase.FindAssets("Bld_ t:Material", new[] { MatDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                string n = Path.GetFileNameWithoutExtension(p);
                if (n.StartsWith("Bld_Kit")) continue;          // L1: the building kit paints its own facades
                string style = n.Contains("CANARY_PEN") ? "panels" : n.Contains("KNIGHT") ? "stucco" : n.Contains("GILMOR") ? "estate" : n.Contains("CANARY") ? "warehouse" : "brick";
                mats += Apply(bk, p, fac[style], 1.06f);
            }

            EditorUtility.DisplayProgressBar("Lundun", "Lining the paving up with the streets…", 0.5f);
            int pv = PavementUVs(world, data, false);
            int roofs = RoofUVs(world, false);

            EditorUtility.DisplayProgressBar("Lundun", "Contact shading…", 0.7f);
            int strips = ContactShade(world, data);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[Lundun] Surface detail: " + mats + " materials textured, " + pv + " pavement pieces lined up, " + roofs +
                      " roof pieces fixed, " + strips + " contact-shading strips. Press Cmd+S. (Undo: Lundun → 20b)");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Lundun/20b Surface Detail – Undo", false, 22)]
    public static void Undo()
    {
        Transform world = Root("World");
        Backup bk = File.Exists(BackupPath) ? JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath)) : null;
        int restored = 0;
        if (bk != null)
            foreach (MatState s in bk.mats)
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(s.path);
                if (m == null) continue;
                Texture t = string.IsNullOrEmpty(s.baseMapGuid) ? null : AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(s.baseMapGuid));
                m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", s.colour);
                EditorUtility.SetDirty(m); restored++;
            }
        if (world != null)
        {
            TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (json != null) PavementUVs(world, LundunExpansionBuilder.Parse(json.text), true);
            RoofUVs(world, true);
            Transform shade = world.Find("ContactShade");
            if (shade != null) Object.DestroyImmediate(shade.gameObject);
        }
        if (File.Exists(BackupPath)) { AssetDatabase.DeleteAsset(BackupPath); }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Lundun] Surface detail undone: " + restored + " materials restored, paving / roofs back to before, shading removed. Press Cmd+S.");
    }

    // ------------------------------------------------------------------ textures
    static Texture2D Save(string name, SurfaceArt.Img im, int aniso)
    {
        string path = Dir + "/" + name + ".png";
        Texture2D t = new Texture2D(im.w, im.h, TextureFormat.RGBA32, false);
        t.LoadRawTextureData(im.rgba); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true; ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Repeat; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = aniso;
        ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.alphaSource = TextureImporterAlphaSource.None;
        TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings("iPhone");
        ps.overridden = true; ps.maxTextureSize = 1024; ps.format = TextureImporterFormat.ASTC_4x4; ps.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// L2: the paving bands (Peckam slabs, Knightsbury York stone, Canary granite, Gilmore asphalt). Repeats along the street,
    /// never wraps up/down (each band is picked by the pavement's UVs).
    static Texture2D SaveBands(string name, int aniso)
    {
        SurfaceArt.Img im = new SurfaceArt.Img(StreetArt.PaveW, StreetArt.PaveH);
        byte[] px = StreetArt.PavingBands();
        System.Array.Copy(px, im.rgba, px.Length);
        Texture2D t = Save(name, im, aniso);
        string path = AssetDatabase.GetAssetPath(t);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
        ti.maxTextureSize = 2048;
        TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings("iPhone");
        ps.maxTextureSize = 2048; ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ materials (with a one-time backup)
    static Backup LoadOrMakeBackup()
    {
        if (File.Exists(BackupPath)) return JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        Backup b = new Backup();
        List<string> paths = new List<string> { MatDir + "/Road.mat", MatDir + "/Pavement.mat" };
        foreach (string g in AssetDatabase.FindAssets("Bld_ t:Material", new[] { MatDir })) { string kp = AssetDatabase.GUIDToAssetPath(g); if (!Path.GetFileName(kp).StartsWith("Bld_Kit")) paths.Add(kp); }
        foreach (string p in paths)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            Texture t = m.GetTexture("_BaseMap");
            b.mats.Add(new MatState { path = p, baseMapGuid = t != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t)) : "", colour = m.GetColor("_BaseColor") });
        }
        File.WriteAllText(BackupPath, JsonUtility.ToJson(b, true));
        AssetDatabase.ImportAsset(BackupPath);
        return b;
    }

    /// Texture on, colour lifted a touch (the detail is a little darker on average). Always from the backed-up colour, so re-running doesn't drift.
    static int Apply(Backup bk, string path, Texture2D tex, float lift)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) return 0;
        Color orig = m.GetColor("_BaseColor");
        foreach (MatState s in bk.mats) if (s.path == path) orig = s.colour;
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
        Color c = orig * lift; c.a = orig.a;
        m.SetColor("_BaseColor", c);
        EditorUtility.SetDirty(m);
        return 1;
    }

    // ------------------------------------------------------------------ paving follows each street
    class RoadLine { public Vector2[] p; public float[] chain; public float half, pave; }

    static List<RoadLine> Lines(LundunWorldData d, bool needPavement)
    {
        Dictionary<string, LwRoadClass> cls = new Dictionary<string, LwRoadClass>();
        foreach (LwRoadClass c in d.roadClasses) cls[c.name] = c;
        List<RoadLine> list = new List<RoadLine>();
        foreach (LwRoad r in d.roads)
        {
            if (r.points == null || r.points.Length < 2 || !cls.ContainsKey(r.roadClass)) continue;
            float pave = cls[r.roadClass].pavement;
            if (needPavement && pave <= 0f) continue;
            RoadLine l = new RoadLine { p = new Vector2[r.points.Length], chain = new float[r.points.Length], half = r.width / 2f, pave = pave };
            for (int i = 0; i < r.points.Length; i++)
            {
                l.p[i] = new Vector2(r.points[i].x, r.points[i].z);
                l.chain[i] = i == 0 ? 0f : l.chain[i - 1] + Vector2.Distance(l.p[i - 1], l.p[i]);
            }
            list.Add(l);
        }
        return list;
    }

    static int PavementUVs(Transform world, LundunWorldData d, bool undo)
    {
        Material pave = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Pavement.mat");
        List<RoadLine> roads = Lines(d, true);
        if (!undo) Generator.Build(d);                            // L2: the generator knows the streets and districts
        float tile = (float)SurfaceArt.PavingTileM;
        int n = 0;
        foreach (MeshRenderer r in world.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.sharedMaterial != pave) continue;
            MeshFilter f = r.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null) continue;
            Mesh m = f.sharedMesh;
            Vector3[] v = m.vertices; Vector3[] nr = m.normals;
            if (nr == null || nr.Length != v.Length) { m.RecalculateNormals(); nr = m.normals; }
            Vector2[] uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = f.transform.TransformPoint(v[i]);
                if (undo) { uv[i] = new Vector2(w.x * 0.1f, w.z * 0.1f); continue; }
                float along, across;
                bool side = Mathf.Abs(f.transform.TransformDirection(nr[i]).y) < 0.5f;
                double bu, bv;                                            // L2: u along the street, v in this district's band
                if (Generator.PavementUV(w.x, w.z, side, out bu, out bv)) { uv[i] = new Vector2((float)bu, (float)bv); continue; }
                if (!Nearest(roads, new Vector2(w.x, w.z), out along, out across)) { uv[i] = new Vector2(w.x / tile, w.z / tile); continue; }
                uv[i] = new Vector2(along / tile, side ? 0.012f : Mathf.Max(0f, across) / tile);      // kerb faces show the granite kerb
            }
            m.uv = uv;
            EditorUtility.SetDirty(m);
            n++;
        }
        return n;
    }

    /// Distance along the nearest street, and distance out from its kerb.
    static bool Nearest(List<RoadLine> roads, Vector2 q, out float along, out float across)
    {
        along = across = 0f;
        float best = float.MaxValue; bool found = false;
        foreach (RoadLine l in roads)
            for (int i = 0; i + 1 < l.p.Length; i++)
            {
                Vector2 a = l.p[i], b = l.p[i + 1], ab = b - a;
                float len2 = ab.sqrMagnitude; if (len2 < 1e-4f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / len2);
                float dist = Vector2.Distance(q, a + ab * t);
                float outBy = dist - l.half;                                   // 0 at the kerb
                if (outBy < -0.6f || outBy > l.pave + 1.5f) continue;           // not this road's pavement
                float score = Mathf.Abs(outBy - l.pave * 0.5f);
                if (score < best) { best = score; along = l.chain[i] + Mathf.Sqrt(len2) * t; across = outBy; found = true; }
            }
        return found;
    }

    static int RoofUVs(Transform world, bool undo)
    {
        Transform b = world.Find("Buildings");
        if (b == null) return 0;
        Vector2 from = undo ? new Vector2((float)SurfaceArt.RoofU, (float)SurfaceArt.RoofV) : new Vector2(0.004f, 0.004f);
        Vector2 to = undo ? new Vector2(0.004f, 0.004f) : new Vector2((float)SurfaceArt.RoofU, (float)SurfaceArt.RoofV);
        int n = 0;
        foreach (MeshFilter f in b.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = f.sharedMesh; if (m == null) continue;
            MeshRenderer kr = f.GetComponent<MeshRenderer>();
            if (kr != null && kr.sharedMaterial != null && kr.sharedMaterial.name.StartsWith("Bld_Kit")) continue;   // L1: kit roofs are already textured
            Vector2[] uv = m.uv; bool changed = false;
            for (int i = 0; i < uv.Length; i++)
                if (Mathf.Abs(uv[i].x - from.x) < 0.0005f && Mathf.Abs(uv[i].y - from.y) < 0.0005f) { uv[i] = to; changed = true; }
            if (changed) { m.uv = uv; EditorUtility.SetDirty(m); n++; }
        }
        return n;
    }

    // ------------------------------------------------------------------ contact shading strips
    class Strips { public List<Vector3> v = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>(); }

    static int ContactShade(Transform world, LundunWorldData d)
    {
        Transform old = world.Find("ContactShade");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Material wallMat = ShadeMaterial("ContactShade_Wall", 0.55f);
        Material gutterMat = ShadeMaterial("ContactShade_Gutter", 0.38f);
        Dictionary<string, Strips> wall = new Dictionary<string, Strips>(), gutter = new Dictionary<string, Strips>();
        Physics.SyncTransforms();
        int mask = ~LayerMask.GetMask("Player", "NPC", "Vehicle", "UI");
        int count = 0;

        // round every building's base (corners overlap, so they come out darker)
        GreyboxResult R = Generator.Build(d);
        foreach (BuildingInfo bi in R.buildings)
        {
            Vector2 c = new Vector2((float)bi.c.x, (float)bi.c.z), fw = new Vector2((float)bi.dir.x, (float)bi.dir.z).normalized;
            Vector2 lf = new Vector2(-fw.y, fw.x);
            float hl = (float)bi.len / 2f, hd = (float)bi.dep / 2f;
            Vector2[] ring = { c - fw * hl - lf * hd, c + fw * hl - lf * hd, c + fw * hl + lf * hd, c - fw * hl + lf * hd };
            for (int s = 0; s < 4; s++)
            {
                Vector2 a = ring[s], b = ring[(s + 1) % 4], mid = (a + b) * 0.5f;
                Vector2 outN = new Vector2(-(b - a).y, (b - a).x).normalized;
                if (Vector2.Dot(outN, mid - c) < 0f) outN = -outN;
                float len = Vector2.Distance(a, b);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
                for (int k = 0; k < pieces; k++)
                {
                    Vector2 p0 = Vector2.Lerp(a, b, k / (float)pieces), p1 = Vector2.Lerp(a, b, (k + 1) / (float)pieces);
                    // stretch past the corners a little, so corners get a double helping
                    if (k == 0) p0 -= (b - a).normalized * 0.9f;
                    if (k == pieces - 1) p1 += (b - a).normalized * 0.9f;
                    float y0, y1;
                    if (!Ground(p0 + outN * 0.5f, mask, out y0) || !Ground(p1 + outN * 0.5f, mask, out y1)) continue;
                    AddStrip(Bucket(wall, (p0 + p1) * 0.5f), p0, p1, outN, 1.1f, y0, y1);
                    count++;
                }
            }
        }

        // gutters along the kerbs (not across junctions)
        List<RoadLine> roads = Lines(d, true);
        List<Vector3> junctions = new List<Vector3>();      // x, z, radius
        Dictionary<string, float> widths = new Dictionary<string, float>();
        foreach (LwRoad r in d.roads) widths[r.id] = r.width;
        foreach (LwJunction j in d.junctions)
        {
            float maxW = 0f;
            if (j.roads != null) foreach (string id in j.roads) { float w; if (widths.TryGetValue(id, out w)) maxW = Mathf.Max(maxW, w); }
            junctions.Add(new Vector3(j.x, j.z, maxW * 0.5f + 4f));
        }
        float yRoad = d.heights != null ? d.heights.roadSurface : 0.02f;
        foreach (LwRoad r in d.roads)
        {
            if (r.roadClass == "bridge" || r.points == null) continue;
            RoadLine l = null;
            foreach (RoadLine x in roads) if (x.p.Length == r.points.Length && x.p[0] == new Vector2(r.points[0].x, r.points[0].z)) { l = x; break; }
            if (l == null) continue;
            for (int i = 0; i + 1 < l.p.Length; i++)
            {
                Vector2 a = l.p[i], b = l.p[i + 1];
                float segLen = Vector2.Distance(a, b);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(segLen / 3f));
                Vector2 dir = (b - a).normalized, left = new Vector2(-dir.y, dir.x);
                for (int k = 0; k < pieces; k++)
                {
                    Vector2 q0 = Vector2.Lerp(a, b, k / (float)pieces), q1 = Vector2.Lerp(a, b, (k + 1) / (float)pieces);
                    if (NearJunction(junctions, q0) || NearJunction(junctions, q1)) continue;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector2 e0 = q0 + left * (s * l.half), e1 = q1 + left * (s * l.half);
                        AddStrip(Bucket(gutter, (e0 + e1) * 0.5f), e0, e1, -left * s, 0.7f, yRoad, yRoad);
                        count++;
                    }
                }
            }
        }

        // save as meshes in one asset, one object per 200 m block
        GameObject root = new GameObject("ContactShade");
        root.transform.SetParent(world, false);
        if (File.Exists(ShadeMeshes)) AssetDatabase.DeleteAsset(ShadeMeshes);
        Mesh container = null;
        foreach (var kv in wall) container = MakeObject(root.transform, "Base_" + kv.Key, kv.Value, wallMat, container);
        foreach (var kv in gutter) container = MakeObject(root.transform, "Gutter_" + kv.Key, kv.Value, gutterMat, container);
        return count;
    }

    static bool NearJunction(List<Vector3> js, Vector2 q)
    {
        foreach (Vector3 j in js) if ((q - new Vector2(j.x, j.y)).sqrMagnitude < j.z * j.z) return true;
        return false;
    }

    static bool Ground(Vector2 at, int mask, out float y)
    {
        y = 0f;
        if (!Physics.Raycast(new Vector3(at.x, 2.5f, at.y), Vector3.down, out RaycastHit hit, 5f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (hit.point.y > 0.6f) return false;          // landed on something that isn't the street
        y = hit.point.y;
        return true;
    }

    static Strips Bucket(Dictionary<string, Strips> d, Vector2 p)
    {
        string k = Mathf.FloorToInt((p.x + 300f) / 200f) + "_" + Mathf.FloorToInt((p.y + 300f) / 200f);
        Strips s; if (!d.TryGetValue(k, out s)) { s = new Strips(); d[k] = s; }
        return s;
    }

    /// A flat strip from the line a→b outwards by width: dark at the line (u = 0), clear at the far edge (u = 1).
    static void AddStrip(Strips s, Vector2 a, Vector2 b, Vector2 outN, float width, float ya, float yb)
    {
        int i = s.v.Count;
        Vector2 a2 = a + outN * width, b2 = b + outN * width;
        s.v.Add(new Vector3(a.x, ya + ShadeY, a.y)); s.v.Add(new Vector3(b.x, yb + ShadeY, b.y));
        s.v.Add(new Vector3(b2.x, yb + ShadeY, b2.y)); s.v.Add(new Vector3(a2.x, ya + ShadeY, a2.y));
        s.uv.Add(new Vector2(0f, 0.5f)); s.uv.Add(new Vector2(0f, 0.5f)); s.uv.Add(new Vector2(1f, 0.5f)); s.uv.Add(new Vector2(1f, 0.5f));
        // face up whichever way round the points came
        Vector3 n = Vector3.Cross(s.v[i + 1] - s.v[i], s.v[i + 3] - s.v[i]);
        if (n.y >= 0f) { s.t.Add(i); s.t.Add(i + 3); s.t.Add(i + 1); s.t.Add(i + 1); s.t.Add(i + 3); s.t.Add(i + 2); }
        else { s.t.Add(i); s.t.Add(i + 1); s.t.Add(i + 3); s.t.Add(i + 1); s.t.Add(i + 2); s.t.Add(i + 3); }
    }

    static Mesh MakeObject(Transform parent, string name, Strips s, Material mat, Mesh container)
    {
        if (s.v.Count == 0) return container;
        Mesh m = new Mesh { name = "Shade_" + name };
        if (s.v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
        m.SetVertices(s.v); m.SetUVs(0, s.uv); m.SetTriangles(s.t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        if (container == null) { AssetDatabase.CreateAsset(m, ShadeMeshes); container = m; }
        else AssetDatabase.AddObjectToAsset(m, container);
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        g.isStatic = true;
        return container;
    }

    /// Black, see-through (URP Unlit), alpha from a soft gradient texture.
    static Material ShadeMaterial(string name, float strength)
    {
        string tp = Dir + "/ShadeGradient.png";
        if (!File.Exists(tp))
        {
            Texture2D t = new Texture2D(64, 4, TextureFormat.RGBA32, false);
            for (int x = 0; x < 64; x++)
            {
                float u = x / 63f, a = Mathf.Pow(1f - u, 2.2f);
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            File.WriteAllBytes(tp, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(tp);
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(tp);
            ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = false; ti.alphaIsTransparency = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        string mp = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            AssetDatabase.CreateAsset(m, mp);
        }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
        m.SetColor("_BaseColor", new Color(0f, 0f, 0f, strength));
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent - 50;        // under rain, halos and streaks
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ helpers
    static Transform Root(string name)
    {
        foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == name) return g.transform;
        return null;
    }

    static void Ensure(string parent, string child) { if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child); }
}
