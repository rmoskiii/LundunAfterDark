// Lundun → 22  Streets (L2)
// 1. runs Lundun → 20 (Surface Detail), which now gives every district its own paving (Peckam concrete slabs,
//    Knightsbury York stone, Canary granite, Gilmore patched asphalt)
// 2. wears the white road markings (Marking.mat gets a worn-paint texture; the road shows through the worn bits)
// 3. adds LundunStreets: double / single yellows, parking bays, bus stop boxes, red bus lanes, give-way lines, SLOW,
//    20 roundels, lane arrows, LOOK RIGHT, KEEP CLEAR, manholes, gullies, pavement covers, patches, cracks, oil stains,
//    and granite setts on Knightsbury Mews, Choumert Grove and the warehouse streets
// 4. makes the new surfaces get wet in the rain, and saves the parking bays for L3 (Assets/World/Streets/ParkingBays.json)
// Nothing under the decals changes: roads, kerbs, colliders and the NavMesh stay as they are (no re-bake needed).
// Lundun → 22b takes it all off again (the paving stays: Lundun → 20b undoes that).
using System.Collections.Generic;
using System.IO;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunStreetsBuilder
{
    const string Dir = "Assets/World/Streets";
    const string MatDir = "Assets/World/Materials";
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string MeshPath = Dir + "/StreetMeshes.asset";
    const string MarkingBackup = Dir + "/MarkingBackup.json";
    const string RootName = "LundunStreets";

    [System.Serializable] class MarkState { public string baseMapGuid; public float clip, cutoff; public Color colour; }
    [System.Serializable] public class BayList { public List<Bay> bays = new List<Bay>(); }
    [System.Serializable] public class Bay { public string road; public int side; public float ch0, ch1, lateral, x0, z0, x1, z1; }

    [MenuItem("Lundun/22  Streets (L2)", false, 25)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { EditorUtility.DisplayDialog("Lundun", "Open the Lundun scene first.", "OK"); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + JsonPath, "OK"); return; }
        LundunWorldData data = JsonUtility.FromJson<LundunWorldData>(json.text);
        try
        {
            // 1. district paving (and the rest of the V4 surface pass)
            LundunSurfaceBuilder.Build();

            EditorUtility.DisplayProgressBar("Lundun", "Working out the street markings…", 0.1f);
            GreyboxResult world = Generator.Build(data);
            DressingResult dressed = Generator.Dress(data, world);
            StreetResult r = Generator.Streets(data, world, dressed);

            Ensure("Assets", "World"); Ensure("Assets/World", "Streets");
            EditorUtility.DisplayProgressBar("Lundun", "Painting lines, setts and ironwork…", 0.35f);
            Texture2D decals = SaveTexture(Dir + "/StreetDecals.png", StreetArt.Decals(), StreetArt.DecW, StreetArt.DecH, true, TextureWrapMode.Repeat, TextureWrapMode.Clamp, 4096);
            Texture2D setts = SaveTexture(Dir + "/Setts.png", StreetArt.Setts(), StreetArt.SettsPx, StreetArt.SettsPx, false, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 512);
            Texture2D worn = SaveTexture(Dir + "/WornPaint.png", StreetArt.WornPaint(), StreetArt.WornPx, StreetArt.WornPx, true, TextureWrapMode.Repeat, TextureWrapMode.Repeat, 256);

            Material decalMat = DecalMaterial(decals);
            Material settsMat = SettsMaterial(setts);
            WearMarkings(worn);

            // 3. the decal objects, one per 100 m block
            EditorUtility.DisplayProgressBar("Lundun", "Laying it on the streets…", 0.6f);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            AssetDatabase.DeleteAsset(MeshPath);
            GameObject root = new GameObject(RootName);
            Mesh container = null;
            int n = 0;
            foreach (MeshData md in r.meshes)
            {
                if (md.tri.Count == 0) continue;
                Mesh mesh = ToMesh(md);
                if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
                GameObject go = new GameObject(md.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = md.material == Generator.StreetSettsMat ? settsMat : decalMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                n++;
            }

            // 4. rain, and the bays for L3
            RegisterWet(decalMat, 0.7f, 0.25f); RegisterWet(settsMat, 0.8f, 0.35f);
            BayList bl = new BayList();
            foreach (ParkingBay b in r.bays)
                bl.bays.Add(new Bay { road = b.road, side = b.side, ch0 = (float)b.ch0, ch1 = (float)b.ch1, lateral = (float)b.lateral, x0 = (float)b.x0, z0 = (float)b.z0, x1 = (float)b.x1, z1 = (float)b.z1 });
            File.WriteAllText(Dir + "/ParkingBays.json", JsonUtility.ToJson(bl, true));
            AssetDatabase.ImportAsset(Dir + "/ParkingBays.json");

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Lundun] " + string.Join("\n", r.log.ToArray()) + "\n" + n + " street blocks. Press Cmd+S. (Undo: Lundun → 22b)");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Lundun", "Streets failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.", "OK");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Lundun/22b Streets – Undo", false, 26)]
    public static void Undo()
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        Material mark = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Marking.mat");
        if (mark != null && File.Exists(MarkingBackup))
        {
            MarkState st = JsonUtility.FromJson<MarkState>(File.ReadAllText(MarkingBackup));
            Texture t = string.IsNullOrEmpty(st.baseMapGuid) ? null : AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(st.baseMapGuid));
            mark.SetTexture("_BaseMap", t);
            mark.SetFloat("_AlphaClip", st.clip); mark.SetFloat("_Cutoff", st.cutoff);
            if (st.clip > 0.5f) mark.EnableKeyword("_ALPHATEST_ON"); else mark.DisableKeyword("_ALPHATEST_ON");
            mark.renderQueue = -1;
            mark.SetColor("_BaseColor", st.colour);
            EditorUtility.SetDirty(mark);
            AssetDatabase.DeleteAsset(MarkingBackup);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Lundun] Street detail removed and road markings back to plain. (District paving: Lundun → 20b.) Press Cmd+S.");
    }

    // ------------------------------------------------------------------ materials
    static Material DecalMaterial(Texture2D tex)
    {
        Material m = LoadOrCreate(Dir + "/" + Generator.StreetDecalMat + ".mat");
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.AlphaTest;
        m.SetFloat("_Smoothness", 0.25f); m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material SettsMaterial(Texture2D tex)
    {
        Material m = LoadOrCreate(Dir + "/" + Generator.StreetSettsMat + ".mat");
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", new Color(0.62f, 0.62f, 0.64f));
        m.SetFloat("_Smoothness", 0.3f); m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material LoadOrCreate(string path)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh) { name = Path.GetFileNameWithoutExtension(path) };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// Worn white lines: the markings' world-space UVs (10 m) pick up the worn-paint alpha.
    static void WearMarkings(Texture2D worn)
    {
        Material mark = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Marking.mat");
        if (mark == null) { Debug.LogWarning("[Lundun] Marking.mat not found — white lines left as they are."); return; }
        if (!File.Exists(MarkingBackup))
        {
            Texture t = mark.GetTexture("_BaseMap");
            MarkState st = new MarkState
            {
                baseMapGuid = t != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t)) : "",
                clip = mark.HasProperty("_AlphaClip") ? mark.GetFloat("_AlphaClip") : 0f,
                cutoff = mark.HasProperty("_Cutoff") ? mark.GetFloat("_Cutoff") : 0.5f,
                colour = mark.GetColor("_BaseColor")
            };
            File.WriteAllText(MarkingBackup, JsonUtility.ToJson(st, true));
        }
        mark.SetTexture("_BaseMap", worn);
        mark.SetTextureScale("_BaseMap", Vector2.one); mark.SetTextureOffset("_BaseMap", Vector2.zero);
        mark.SetFloat("_AlphaClip", 1f); mark.SetFloat("_Cutoff", 0.5f); mark.EnableKeyword("_ALPHATEST_ON");
        mark.renderQueue = (int)RenderQueue.AlphaTest;
        EditorUtility.SetDirty(mark);
    }

    static void RegisterWet(Material m, float smooth, float darken)
    {
        LundunWeather w = null;
        foreach (LundunWeather x in Object.FindObjectsByType<LundunWeather>(FindObjectsInactive.Include)) { w = x; break; }
        if (w == null) return;                                      // no weather yet: menu 19 adds these when you run it
        SerializedObject so = new SerializedObject(w);
        SerializedProperty arr = so.FindProperty("surfaces");
        if (arr == null) return;
        for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("material").objectReferenceValue == m) return;
        arr.arraySize++;
        SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
        e.FindPropertyRelative("material").objectReferenceValue = m;
        e.FindPropertyRelative("wetSmoothness").floatValue = smooth;
        e.FindPropertyRelative("darken").floatValue = darken;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------------ helpers
    static Texture2D SaveTexture(string path, byte[] rgba, int w, int h, bool alphaClip, TextureWrapMode wu, TextureWrapMode wv, int max)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.LoadRawTextureData(rgba); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true; ti.mipmapEnabled = true;
        ti.wrapModeU = wu; ti.wrapModeV = wv;
        ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8;
        ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = alphaClip;
        if (alphaClip) { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.5f; }   // thin lines don't vanish in the distance
        ti.maxTextureSize = max; ti.textureCompression = TextureImporterCompression.CompressedHQ;
        TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings("iPhone");
        ps.overridden = true; ps.maxTextureSize = max; ps.format = TextureImporterFormat.ASTC_4x4; ps.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Mesh ToMesh(MeshData md)
    {
        int n = md.VertexCount;
        List<Vector3> v = new List<Vector3>(n);
        List<Vector2> uv = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            v.Add(new Vector3(md.pos[i * 3], md.pos[i * 3 + 1], md.pos[i * 3 + 2]));
            uv.Add(new Vector2(md.uv[i * 2], md.uv[i * 2 + 1]));
        }
        Mesh mesh = new Mesh { name = md.name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(md.tri, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    static void Ensure(string parent, string child) { if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child); }
}
