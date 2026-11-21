// G3: Lundun → 37  Autumn Leaves. Always autumn.
// Works out where the city's (and, if they're built, Brixtun's and Kingsmere's) dressing went, the same way
// Lundun → 13 / 28 / 29 / 30 do, in memory, and adds ONE new object, "LundunAutumn", next to everything else:
//   • fallen leaves in the gutters, on the pavements, under the street trees and the odd few on the road (most in
//     Peckam, fewest in Knightsbury and Kingsmere)
//   • about one street tree in five turned (amber, rust or gold) and one in seven turning at the top; the rest stay
//     green. The park trees stay green
//   • a few leaves falling near the camera
// It never touches LundunDressing, World, the expansions, the people or anything else: no need to rerun 13.
// Safe to run again (it replaces only LundunAutumn). Lundun → 37b takes it out again.
using System;
using System.Collections.Generic;
using System.IO;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunAutumnBuilder
{
    const string Dir = "Assets/World/Autumn";
    const string AtlasPath = Dir + "/AutumnLeaves.png";
    const string MeshPath = Dir + "/AutumnMeshes.asset";
    const string DataPath = "Assets/World/LundunWorld.json";
    const string RootName = "LundunAutumn";
    static readonly Color[] FallingColours = { new Color(0.79f, 0.47f, 0.17f), new Color(0.66f, 0.28f, 0.12f), new Color(0.85f, 0.66f, 0.23f), new Color(0.42f, 0.29f, 0.16f) };

    [MenuItem("Lundun/37  Autumn Leaves (G3)", false, 44)]
    static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null) { Dialog("Can't find " + DataPath + "."); return; }
        if (GameObject.Find("LundunDressing") == null) { Dialog("No LundunDressing in the scene: the leaves follow the dressing's trees, so run Lundun → 13 first (you only ever need to once)."); return; }
        try
        {
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            AutumnResult res = new AutumnResult();

            // where everything went (the same passes Lundun → 13, 28, 29 and 30 make)
            EditorUtility.DisplayProgressBar("Lundun", "Finding the city's trees and gutters…", 0.1f);
            GreyboxResult world = Generator.Build(data);
            Generator.Dress(data, world);
            Generator.Autumn(res, 0);
            if (LundunGreyboxBuilder.HasExpansions() && GameObject.Find("LundunExpansion") != null)
            {
                try
                {
                    EditorUtility.DisplayProgressBar("Lundun", "Brixtun's…", 0.3f);
                    Generator.Build(data);
                    GreyboxResult x = Generator.BuildExpansion(data);
                    if (x.meshes.Count > 0) { Generator.Dress(Generator.ExpansionWorld, x); Generator.Autumn(res, 1); }
                }
                catch (Exception e) { Debug.LogWarning("[Lundun] Brixtun's leaves skipped: " + e.Message); }
                try
                {
                    EditorUtility.DisplayProgressBar("Lundun", "Kingsmere's…", 0.45f);
                    Generator.Build(data);
                    GreyboxResult k = Generator.BuildKingsmere(data);
                    if (k.meshes.Count > 0) { Generator.Dress(Generator.ExpansionWorld, k); Generator.Autumn(res, 2); }
                }
                catch (Exception e) { Debug.LogWarning("[Lundun] Kingsmere's leaves skipped: " + e.Message); }
                Generator.Build(data);                                  // leave the generator on the city
            }

            // the texture and materials (its own: the dressing's atlas is never touched)
            EditorUtility.DisplayProgressBar("Lundun", "Painting the leaves…", 0.6f);
            EnsureFolder(Dir);
            Texture2D tex = SaveAtlas(res.atlas);
            Material litter = MakeMaterial("Autumn_Litter", tex, true);
            Material crown = MakeMaterial("Autumn_Crown", tex, false);
            Material[] falling = new Material[FallingColours.Length];
            for (int i = 0; i < falling.Length; i++) falling[i] = MakeFalling("Autumn_Falling" + i, FallingColours[i]);
            Dictionary<string, Material> mats = new Dictionary<string, Material> { { Generator.AutumnLitterMat, litter }, { Generator.AutumnCrownMat, crown } };

            // the one new object
            EditorUtility.DisplayProgressBar("Lundun", "Laying them down…", 0.75f);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            AssetDatabase.DeleteAsset(MeshPath);
            GameObject root = new GameObject(RootName);
            Mesh container = null;
            List<Renderer> blocks = new List<Renderer>();
            List<float> far = new List<float>();
            foreach (MeshData md in res.meshes)
            {
                if (md.tri.Count == 0) continue;
                Mesh mesh = ToMesh(md);
                if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; }
                else AssetDatabase.AddObjectToAsset(mesh, container);
                GameObject go = new GameObject(md.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                bool isCrown = md.material == Generator.AutumnCrownMat;
                mr.sharedMaterial = mats[md.material];
                mr.shadowCastingMode = isCrown ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                blocks.Add(mr);
                far.Add(isCrown ? 265f : 190f);                  // crowns as far as the dressing's trees; leaves on the ground a bit less
            }
            LundunFallingLeaves fl = root.AddComponent<LundunFallingLeaves>();
            Vector2[] trees = new Vector2[res.fallFrom.Count];
            for (int i = 0; i < trees.Length; i++) trees[i] = new Vector2((float)res.fallFrom[i][0], (float)res.fallFrom[i][1]);
            fl.Set(trees);
            fl.SetMaterials(falling);
            fl.SetBlocks(blocks.ToArray(), far.ToArray());
            EditorUtility.SetDirty(fl);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            string report = string.Join("\n", res.log.ToArray());
            Debug.Log("[Lundun] G3 autumn:\n" + report + "\n" + blocks.Count + " blocks, " + trees.Length + " trees leaves fall from. Nothing else in the scene changed. Cmd+S, then Play.");
            Dialog("Autumn's here.\n\n" + report + "\n\nOnly the new LundunAutumn object was added. Press Cmd+S to save the scene.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Dialog("Autumn leaves failed: " + e.Message + "\n\nNothing in the scene was changed. The Console has the details — copy them to Claude.");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Lundun/37b  Remove Autumn Leaves (G3)", false, 45)]
    static void Remove()
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null) { Object.DestroyImmediate(old); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }
        Debug.Log("[Lundun] G3: LundunAutumn removed (its files in " + Dir + " are left; delete the folder if you like).");
    }

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
        ti.maxTextureSize = 512;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
    }

    static Shader SimpleLit()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        return sh != null ? sh : Shader.Find("Universal Render Pipeline/Lit");
    }

    static Material LoadOrCreate(string name)
    {
        string path = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(SimpleLit()); AssetDatabase.CreateAsset(m, path); }
        else m.shader = SimpleLit();
        return m;
    }

    static Material MakeMaterial(string name, Texture2D tex, bool cutout)
    {
        Material m = LoadOrCreate(name);
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.1f);
        m.enableInstancing = false;
        if (cutout)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 2f);
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            m.SetFloat("_AlphaClip", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 2f);
            m.renderQueue = (int)RenderQueue.Geometry;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material MakeFalling(string name, Color c)
    {
        Material m = LoadOrCreate(name);
        m.SetTexture("_BaseMap", null);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.1f);
        m.SetFloat("_AlphaClip", 0f);
        m.DisableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", 0f);                                     // both sides: it's a tumbling card
        m.renderQueue = (int)RenderQueue.Geometry;
        m.enableInstancing = true;
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
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
