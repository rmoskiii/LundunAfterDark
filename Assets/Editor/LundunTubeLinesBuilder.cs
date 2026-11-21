// G6a: Lundun → 38  Tube Lines: New Stations. The nine new Underground entrances of the G6 plan.
// Works out where the city's (and, if they're built, Brixtun's and Kingsmere's) dressing went, the same way
// Lundun → 13 / 28 / 29 / 30 / 37 do, in memory, finds the nearest place each new station fits (just behind a
// pavement, clear of everything), and adds ONE new object, "LundunTubeLines", next to everything else:
//   • the entrances (the same look as the park ones: tiled walls, railings, the name board, the totem), their walls
//     solid, on their own texture (the dressing's isn't touched)
//   • a LundunExpansionArea on it with the stations' names, tops and ways out: the Tube picks them up when the game
//     starts, like Brixtun's (LundunDressing), so TAKE THE TUBE works there and they're on the minimap
//   • each station on the map registry (the full map, PLACES and the GPS)
// It never touches LundunDressing, World, the expansions, the people or anything else: no need to rerun 13.
// Safe to run again (it replaces only LundunTubeLines and its own map entries). Lundun → 38b takes it all out again.
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

public static class LundunTubeLinesBuilder
{
    const string Dir = "Assets/World/TubeLines";
    const string AtlasPath = Dir + "/TubeEntrances.png";
    const string MeshPath = Dir + "/TubeEntranceMeshes.asset";
    const string DataPath = "Assets/World/LundunWorld.json";
    const string RootName = "LundunTubeLines";
    const string MapPrefix = "TUBE_G6_";

    [MenuItem("Lundun/38  Tube Lines: New Stations (G6a)", false, 46)]
    static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null) { Dialog("Can't find " + DataPath + "."); return; }
        if (GameObject.Find("LundunDressing") == null) { Dialog("No LundunDressing in the scene: the stations are fitted round the dressing, so run Lundun → 13 first (you only ever need to once)."); return; }
        try
        {
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            TubeEntranceResult res = new TubeEntranceResult();

            // where everything went (the same passes Lundun → 13, 28, 29 and 30 make), and room for the new stations
            EditorUtility.DisplayProgressBar("Lundun", "Finding room for the city's new stations…", 0.1f);
            GreyboxResult world = Generator.Build(data);
            DressingResult dress = Generator.Dress(data, world);
            Generator.PlaceTubeEntrances(res, 0, world.meshes, dress.meshes);
            if (LundunGreyboxBuilder.HasExpansions() && GameObject.Find("LundunExpansion") != null)
            {
                try
                {
                    EditorUtility.DisplayProgressBar("Lundun", "Brixtun's…", 0.35f);
                    Generator.Build(data);
                    GreyboxResult x = Generator.BuildExpansion(data);
                    if (x.meshes.Count > 0) { DressingResult dx = Generator.Dress(Generator.ExpansionWorld, x); Generator.PlaceTubeEntrances(res, 1, x.meshes, dx.meshes); }
                }
                catch (Exception e) { Debug.LogWarning("[Lundun] Brixtun's new stations skipped: " + e.Message); }
                try
                {
                    EditorUtility.DisplayProgressBar("Lundun", "Kingsmere's…", 0.55f);
                    Generator.Build(data);
                    GreyboxResult k = Generator.BuildKingsmere(data);
                    if (k.meshes.Count > 0) { DressingResult dk = Generator.Dress(Generator.ExpansionWorld, k); Generator.PlaceTubeEntrances(res, 2, k.meshes, dk.meshes); }
                }
                catch (Exception e) { Debug.LogWarning("[Lundun] Kingsmere's new stations skipped: " + e.Message); }
                Generator.Build(data);                                  // leave the generator on the city
            }
            Generator.FinishTubeEntrances(res);
            if (res.entrances.Count == 0) { Dialog("No room was found for any of the new stations, so nothing was added.\n\n" + string.Join("\n", res.log.ToArray())); return; }

            // the texture and materials (its own)
            EditorUtility.DisplayProgressBar("Lundun", "Painting the signs…", 0.7f);
            EnsureFolder(Dir);
            Texture2D tex = SaveAtlas(res.atlas);
            Material solid = MakeMaterial("TubeEntrance_Solid", tex, false, false);
            Material cut = MakeMaterial("TubeEntrance_Rails", tex, true, false);
            Material glow = MakeMaterial("TubeEntrance_Signs", tex, false, true);
            Dictionary<string, Material> mats = new Dictionary<string, Material>
            {
                { Generator.TubeSolidMat, solid }, { Generator.TubeCutMat, cut }, { Generator.TubeGlowMat, glow },
            };

            // the one new object
            EditorUtility.DisplayProgressBar("Lundun", "Building the entrances…", 0.85f);
            Remove(false);
            AssetDatabase.DeleteAsset(MeshPath);
            GameObject root = new GameObject(RootName);
            Mesh container = null;
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
                mr.sharedMaterial = mats[md.material];
                mr.shadowCastingMode = md.material == Generator.TubeGlowMat ? ShadowCastingMode.Off : ShadowCastingMode.On;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Transform cols = new GameObject("Colliders").transform;
            cols.SetParent(root.transform, false);
            foreach (DressBox c in res.colliders)
            {
                GameObject g = new GameObject(c.name);
                g.transform.SetParent(cols, false);
                g.transform.SetPositionAndRotation(new Vector3((float)c.cx, (float)c.cy, (float)c.cz), Quaternion.Euler(0f, (float)c.yaw, 0f));
                g.AddComponent<BoxCollider>().size = new Vector3((float)c.sx, (float)c.sy, (float)c.sz);
            }

            // the stations: the Tube picks them up from here when the game starts (like Brixtun's); and on the map
            int n = res.entrances.Count;
            string[] names = new string[n]; Vector3[] tops = new Vector3[n], outs = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                TubeEntrance e = res.entrances[i];
                names[i] = e.name;
                tops[i] = new Vector3((float)e.open.x, 0f, (float)e.open.z);
                outs[i] = new Vector3((float)-e.f.x, 0f, (float)-e.f.z);              // out of the stairs, onto the pavement
                LundunExpansionBuilder.Register(MapPrefix + Slug(e.name), LundunTubeNetwork.Title(e.name) + " Station", "tube", tops[i], false);
            }
            LundunExpansionArea area = root.AddComponent<LundunExpansionArea>();
            area.SetData("Lundun Underground (G6)", new Vector3[0], new int[0], names, tops, outs);
            area.SetLook(glow, null);                                   // the signs light up at night, like the dressing's
            EditorUtility.SetDirty(area);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            string report = string.Join("\n", res.log.ToArray());
            Debug.Log("[Lundun] G6a new Tube stations (" + n + "):\n" + report + "\nNothing else in the scene changed. Cmd+S, then Play.");
            Dialog("New Tube stations: " + n + " of " + Generator.NewTubeSites.Length + "\n\n" + report +
                   (res.missed.Count > 0 ? "\n\n(Left out: " + string.Join(", ", res.missed.ToArray()) + ". If Brixtun or Kingsmere aren't built, theirs are left out.)" : "") +
                   "\n\nOnly the new LundunTubeLines object (and its map entries) were added. Press Cmd+S to save the scene.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Dialog("New Tube stations failed: " + e.Message + "\n\nNothing in the scene was changed. The Console has the details — copy them to Claude.");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Lundun/38b  Remove Tube Lines: New Stations (G6a)", false, 47)]
    static void RemoveMenu()
    {
        Remove(true);
        Debug.Log("[Lundun] G6a: LundunTubeLines and its map entries removed (its files in " + Dir + " are left; delete the folder if you like).");
    }

    static void Remove(bool markDirty)
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        LundunMapRegistry reg = Object.FindAnyObjectByType<LundunMapRegistry>(FindObjectsInactive.Include);
        if (reg != null)
        {
            foreach (TubeSite s in Generator.NewTubeSites) reg.Remove(MapPrefix + Slug(s.name));
            EditorUtility.SetDirty(reg);
        }
        if (markDirty) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static string Slug(string s) { return s.Replace(" ", "_").Replace("'", ""); }

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

    static Material MakeMaterial(string name, Texture2D tex, bool cutout, bool glow)
    {
        string path = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else m.shader = sh;
        m.SetTexture("_BaseMap", tex);
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
            m.SetTexture("_EmissionMap", tex);
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
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
