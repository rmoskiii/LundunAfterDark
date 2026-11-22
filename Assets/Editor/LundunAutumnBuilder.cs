// G3: Lundun → 37  Autumn Leaves. Always autumn.
// Works out where the city's (and, if they're built, Brixtun's and Kingsmere's) dressing went, the same way
// Lundun → 13 / 28 / 29 / 30 do, in memory, and adds ONE new object, "LundunAutumn", next to everything else:
//   • fallen leaves in the gutters, on the pavements, under the street trees and the odd few on the road (most in
//     Peckam, fewest in Knightsbury and Kingsmere)
//   • G8d: late-autumn trees. Half the street trees nearly bare (trunk, branches, twigs, a clump or two of leaves),
//     a third half-bare (thin amber, rust and gold clumps), the rest a full turned crown; in the parks three in ten
//     stay green, the rest half-bare or bare. G8d also halves the leaves on the ground
//   • a few leaves falling near the camera
// G8d: to show the bare branches, the old green crowns of those trees come out of the dressing's and parks' meshes:
// each mesh with one is swapped for a copy without it (the originals are never changed, and LundunAutumn remembers
// every swap, so running 37 again or 37b puts them back exactly). If the city has hills (40), 37 takes them out first
// and puts them back after, so everything still sits on them. No need to rerun 13, or 40.
// Safe to run again (it replaces LundunAutumn). Lundun → 37b takes it all out again.
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
    const string ThinPath = Dir + "/AutumnBareCrowns.asset";               // G8d: the copies without the old crowns
    const string DataPath = "Assets/World/LundunWorld.json";
    const string RootName = "LundunAutumn";
    static readonly Color[] FallingColours = { new Color(0.79f, 0.47f, 0.17f), new Color(0.66f, 0.28f, 0.12f), new Color(0.85f, 0.66f, 0.23f), new Color(0.42f, 0.29f, 0.16f) };

    [MenuItem("Lundun/37  Autumn Leaves + Bare Trees (G3, G8d)", false, 44)]
    static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null) { Dialog("Can't find " + DataPath + "."); return; }
        if (GameObject.Find("LundunDressing") == null) { Dialog("No LundunDressing in the scene: the leaves follow the dressing's trees, so run Lundun → 13 first (you only ever need to once)."); return; }
        bool hills = LundunHillsBuilder.Present;
        string hillsMsg = null;
        try
        {
            // G8d: the hills out first (so every mesh is as 13 made it), and the last autumn's swaps put back
            if (hills) { EditorUtility.DisplayProgressBar("Lundun", "Taking the hills out for a moment…", 0.02f); LundunHillsBuilder.TakeOut(); }
            PutBackCrowns();

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
            Material clump = MakeMaterial("Autumn_Clumps", tex, true);
            clump.SetFloat("_Cull", 0f);                                     // G8d: the last leaves, seen from both sides
            EditorUtility.SetDirty(clump);
            Material[] falling = new Material[FallingColours.Length];
            for (int i = 0; i < falling.Length; i++) falling[i] = MakeFalling("Autumn_Falling" + i, FallingColours[i]);
            Dictionary<string, Material> mats = new Dictionary<string, Material> { { Generator.AutumnLitterMat, litter }, { Generator.AutumnCrownMat, crown }, { Generator.AutumnClumpMat, clump } };

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
                bool isCrown = md.material != Generator.AutumnLitterMat;         // crowns, branches and the leaves on them
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

            // G8d: the old green crowns out of the dressing's and parks' meshes (copies; the originals stay as they are)
            EditorUtility.DisplayProgressBar("Lundun", "Taking the old green crowns off the bare trees…", 0.85f);
            LundunAutumnRecord rec = root.AddComponent<LundunAutumnRecord>();
            int thinned = ThinCrowns(res, rec);
            EditorUtility.SetDirty(rec);
            if (hills) { EditorUtility.DisplayProgressBar("Lundun", "Putting the hills back…", 0.9f); hillsMsg = LundunHillsBuilder.PutIn(); hills = false; }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            string report = string.Join("\n", res.log.ToArray());
            Debug.Log("[Lundun] G3 autumn:\n" + report + "\n" + blocks.Count + " blocks, " + trees.Length + " trees leaves fall from. Nothing else in the scene changed. Cmd+S, then Play.");
            Dialog("Autumn's here.\n\n" + report + "\n\n" + thinned + " of the city's meshes swapped for copies without the bare trees' old crowns (37b puts them back)."
                + (hillsMsg != null ? "\n\nThe hills were taken out and put back (so run Lundun → 3 now)." : "") + "\n\nPress Cmd+S to save the scene.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (hills) { try { LundunHillsBuilder.PutIn(); } catch (Exception e2) { Debug.LogException(e2); } }
            Dialog("Autumn leaves failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude. Run 37b to put the trees back as they were.");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // ------------------------------------------------------------------ G8d: the bare trees' old crowns
    /// Puts back every mesh the last autumn swapped (from LundunAutumn's record), and removes LundunAutumn.
    static int PutBackCrowns()
    {
        GameObject old = GameObject.Find(RootName);
        int n = 0;
        if (old != null)
        {
            LundunAutumnRecord rec = old.GetComponent<LundunAutumnRecord>();
            if (rec != null)
                for (int i = rec.swaps.Count - 1; i >= 0; i--)
                {
                    LundunAutumnRecord.Swap sw = rec.swaps[i];
                    if (sw.filter == null) continue;
                    sw.filter.sharedMesh = sw.was;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(sw.filter);
                    EditorUtility.SetDirty(sw.filter);
                    n++;
                }
            Object.DestroyImmediate(old);
        }
        AssetDatabase.DeleteAsset(ThinPath);
        return n;
    }

    /// Every dressing block (DressSolid…) and park canopy block (TreeCanopies…) with a bare tree's old crown in it
    /// gets a copy without those triangles. A street crown is told by its colour (one of the dressing's leaf colours)
    /// and where it is; a park canopy by where it is (those blocks hold nothing else).
    static int ThinCrowns(AutumnResult res, LundunAutumnRecord rec)
    {
        // the crowns, by 16 m square
        Dictionary<long, List<double[]>> grid = new Dictionary<long, List<double[]>>();
        System.Action<double[], double, double, double, bool> add = (c, x, z, rad, street) =>
        {
            for (double gx = Math.Floor((x - rad) / 16); gx <= Math.Floor((x + rad) / 16); gx++)
                for (double gz = Math.Floor((z - rad) / 16); gz <= Math.Floor((z + rad) / 16); gz++)
                {
                    long k = ((long)gx << 32) ^ (uint)(int)gz;
                    List<double[]> l;
                    if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<double[]>();
                    l.Add(c);
                }
        };
        // street: x, z, r, yLow, yHigh, 1 ; park: x, z, R, y0, y1, 0
        foreach (double[] h in res.hideStreet) { double[] c = { h[0], h[1], h[4] + 0.15, h[2] + 2.1, h[2] + 2.2 + h[3] + 0.2, 1 }; add(c, h[0], h[1], c[2], true); }
        foreach (double[] h in res.hidePark) { double[] c = { h[0], h[1], h[2] + 0.15, h[3] - 0.1, h[4] + 0.1, 0 }; add(c, h[0], h[1], c[2], false); }
        if (grid.Count == 0) return 0;

        Mesh container = null;
        int swapped = 0;
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mf.sharedMesh == null) continue;
            string name = mf.gameObject.name;
            bool street = name.StartsWith("DressSolid"), park = name.StartsWith("TreeCanopies");
            if (!street && !park) continue;
            if (mf.GetComponentInParent<LundunAutumnRecord>() != null) continue;
            Mesh src = mf.sharedMesh;
            if (!src.isReadable) continue;
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            Vector3[] v = src.vertices; Vector2[] uv = src.uv;
            bool anyGone = false;
            List<int>[] keep = new List<int>[src.subMeshCount];
            for (int s = 0; s < src.subMeshCount; s++)
            {
                List<int> tri = new List<int>(); src.GetTriangles(tri, s);
                List<int> k = new List<int>(tri.Count);
                for (int t = 0; t + 2 < tri.Count; t += 3)
                {
                    int a = tri[t], b = tri[t + 1], c = tri[t + 2];
                    if (Crown(m, v, uv, a, b, c, street, grid, res.leafUV)) { anyGone = true; continue; }
                    k.Add(a); k.Add(b); k.Add(c);
                }
                keep[s] = k;
            }
            if (!anyGone) continue;
            Mesh copy = Object.Instantiate(src);
            copy.name = src.name + " (bare trees)";
            for (int s = 0; s < keep.Length; s++) copy.SetTriangles(keep[s], s, true);
            if (container == null) { AssetDatabase.CreateAsset(copy, ThinPath); container = copy; }
            else AssetDatabase.AddObjectToAsset(copy, container);
            rec.swaps.Add(new LundunAutumnRecord.Swap { filter = mf, was = src });
            mf.sharedMesh = copy;
            PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
            EditorUtility.SetDirty(mf);
            swapped++;
        }
        return swapped;
    }

    static bool Crown(Matrix4x4 m, Vector3[] v, Vector2[] uv, int a, int b, int c, bool street, Dictionary<long, List<double[]>> grid, List<double[]> leafUV)
    {
        if (street)
        {
            if (uv.Length != v.Length) return false;
            Vector2 ua = uv[a];
            if ((uv[b] - ua).sqrMagnitude > 1e-9f || (uv[c] - ua).sqrMagnitude > 1e-9f) return false;     // one flat colour
            bool leaf = false;
            foreach (double[] q in leafUV) if (Math.Abs(q[0] - ua.x) < 2e-4 && Math.Abs(q[1] - ua.y) < 2e-4) { leaf = true; break; }
            if (!leaf) return false;
        }
        Vector3 p = m.MultiplyPoint3x4((v[a] + v[b] + v[c]) / 3f);
        List<double[]> l;
        long k = ((long)Math.Floor(p.x / 16) << 32) ^ (uint)(int)Math.Floor(p.z / 16);
        if (!grid.TryGetValue(k, out l)) return false;
        foreach (double[] cr in l)
        {
            if ((cr[5] > 0.5) != street) continue;
            double dx = p.x - cr[0], dz = p.z - cr[1];
            if (dx * dx + dz * dz <= cr[2] * cr[2] && p.y >= cr[3] && p.y <= cr[4]) return true;
        }
        return false;
    }

    [MenuItem("Lundun/37b  Remove Autumn Leaves + Bare Trees (G3, G8d)", false, 45)]
    static void Remove()
    {
        bool hills = LundunHillsBuilder.Present;
        if (hills) LundunHillsBuilder.TakeOut();
        int n = PutBackCrowns();                                        // the trees' crowns back, LundunAutumn gone
        if (hills) LundunHillsBuilder.PutIn();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Lundun] G3/G8d: LundunAutumn removed, " + n + " meshes put back as they were.");
        Dialog("Autumn is gone and the trees have their green crowns back (" + n + " meshes put back)." + (hills ? "\n\nThe hills were taken out and put back: run Lundun → 3." : "") + "\n\nPress Cmd+S to save the scene.");
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
