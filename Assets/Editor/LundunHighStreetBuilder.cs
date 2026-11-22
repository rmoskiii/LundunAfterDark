// G8b: Lundun → 41  High Street Shops. Shop fronts along every agreed high street (the G8b map): Brixtun Road, Electric
// Parade, Coldharbor Lane, Atlantic Road, Acre Lane, Stockwell Road; Gilmore Road; Commercial Way, Dock Street, Wharf
// Road, Westferry Road, Quay Loop, Tower Plaza. Every shop-type building facing one of them gets a front on each unit
// that hasn't got one (lit sign, window with its interior, glass door; some shuttered). Houses and flats stay homes.
// Additive: it adds one object, "LundunHighStreets", with its own meshes, atlas and materials (Assets/World/HighStreets),
// and changes nothing that's already there. Lundun → 41b takes it away again. Running 41 again replaces it.
// Then run Lundun → 40 (so the hills lift the new shops too), then 3, then save.
using System.Collections.Generic;
using System.IO;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunHighStreetBuilder
{
    const string Dir = "Assets/World/HighStreets";
    const string AtlasPath = Dir + "/HighStreetAtlas.png";
    const string MeshPath = Dir + "/HighStreetMeshes.asset";
    const string DataPath = "Assets/World/LundunWorld.json";
    const string RootName = "LundunHighStreets";

    [MenuItem("Lundun/41  High Street Shops (G8b)", false, 52)]
    static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null) { Dialog("Can't find " + DataPath + "."); return; }
        if (GameObject.Find("LundunDressing") == null) { Dialog("No street dressing in the scene: run Lundun → 13 first."); return; }

        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Finding the empty shop units on the high streets…", 0.05f);
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            HighStreetResult r = Generator.G8HighStreetShops(data);

            EnsureFolder(Dir);
            EditorUtility.DisplayProgressBar("Lundun", "Painting the shop signs…", 0.6f);
            Texture2D atlas = SaveAtlas(r.atlas);
            Dictionary<string, Material> mats = new Dictionary<string, Material>
            {
                { "HighSolid", MakeMaterial("HighStreet_Solid", atlas, false, false) },
                { "HighCutout", MakeMaterial("HighStreet_Cutout", atlas, true, false) },
                { "HighGlow", MakeMaterial("HighStreet_Glow", atlas, false, true) },
            };

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            AssetDatabase.DeleteAsset(MeshPath);
            GameObject root = new GameObject(RootName);
            Mesh container = null;
            int done = 0;
            foreach (MeshData md in r.meshes)
            {
                if (++done % 10 == 0) EditorUtility.DisplayProgressBar("Lundun", "Building the shop fronts…", 0.65f + 0.3f * done / r.meshes.Count);
                if (md.tri.Count == 0) continue;
                Mesh mesh = ToMesh(md);
                if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; }
                else AssetDatabase.AddObjectToAsset(mesh, container);
                GameObject go = new GameObject(md.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[md.material];
                mr.shadowCastingMode = md.material == "HighSolid" ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorUtility.ClearProgressBar();

            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, int> kv in r.perRoad) lines.Add(kv.Key + " " + kv.Value);
            lines.Sort();
            string msg = r.added + " shop fronts added (" + r.shut + " of them shuttered):\n" + string.Join(", ", lines);
            msg += GameObject.Find("LundunHills") != null
                ? "\n\nThe city has hills: now run Lundun → 40 (so the new shops sit on them too), then 3, then save (Cmd+S)."
                : "\n\nNow save the scene (Cmd+S).";
            Debug.Log("[Lundun] G8b: " + msg.Replace("\n\n", "  ").Replace("\n", " "));
            Dialog(msg);
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogException(e);
            Dialog("Something went wrong adding the shops (see the Console). Nothing else in the scene was changed.");
        }
    }

    [MenuItem("Lundun/41b  Remove High Street Shops (G8b)", false, 53)]
    static void Remove()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject old = GameObject.Find(RootName);
        if (old == null) { Dialog("There are no G8b shops in this scene."); return; }
        Object.DestroyImmediate(old);
        AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Dialog("The G8b shop fronts are gone (everything else is as it was).\n\nIf the city has hills, run Lundun → 40, then 3. Then save (Cmd+S).");
    }

    // ------------------------------------------------------------------ the same atlas, materials and meshes as the dressing's
    static Texture2D SaveAtlas(DressAtlas a)
    {
        Texture2D t = new Texture2D(a.W, a.H, TextureFormat.RGBA32, false);
        Color32[] c = new Color32[a.W * a.H];
        for (int i = 0; i < c.Length; i++) c[i] = new Color32(a.px[i * 4], a.px[i * 4 + 1], a.px[i * 4 + 2], a.px[i * 4 + 3]);
        t.SetPixels32(c);
        t.Apply();
        File.WriteAllBytes(AtlasPath, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.anisoLevel = 2;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
    }

    static Material MakeMaterial(string name, Texture2D atlas, bool cutout, bool glow)
    {
        string path = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else m.shader = sh;
        m.SetTexture("_BaseMap", atlas);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.2f);
        m.enableInstancing = false;
        if (cutout)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 2f);
            m.renderQueue = (int)RenderQueue.Geometry;
        }
        if (glow)
        {
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", atlas);
            m.SetColor("_EmissionColor", Color.white * 1.5f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
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
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(md.tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, i));
        AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }
}
