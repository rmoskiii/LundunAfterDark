// Lundun → 24  Lighting by District (L5)
// 1. rebuilds the street lamps (World/Lamps) by district: orange sodium in Peckam and Gilmore (Gilmore's on the estates'
//    concrete columns), cool white LED on slim steel poles in Canary Quay, warm heritage lanterns in Knightsbury
// 2. the real lamp lights near the player take the colour of the lamp they sit in; so do the halos and rain streaks
// 3. light spills onto the pavement from lit shop windows, pubs and office lobbies (cheap additive glows, no real lights)
// 4. repaints the building kit's windows: every lit window is its own room (warm, soft white, cool LED, TV blue, red)
// Nothing else in the scene is touched. Afterwards: Cmd+S (no nav rebake needed: lamps have no colliders).
// Lundun → 24b puts the lamps back to one colour and removes the shop light.
using System.Collections.Generic;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunLightingDistrictBuilder
{
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string GenDir = "Assets/World/Generated";
    const string LampMeshPath = GenDir + "/LampMeshes.asset";
    const string SpillMeshPath = GenDir + "/ShopLightMeshes.asset";
    const string SpillRoot = "LundunShopLight";
    const float PaveTop = 0.15f;
    static readonly string[] SpillMats = { "LightPool_Shop", "LightPool_Pub", "LightPool_Lobby" };

    [MenuItem("Lundun/24  Lighting by District (L5)", false, 29)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { EditorUtility.DisplayDialog("Lundun", "Open the Lundun scene first.", "OK"); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + JsonPath, "OK"); return; }
        try
        {
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            EditorUtility.DisplayProgressBar("Lundun", "Working out the lamps…", 0.1f);
            GreyboxResult world = Generator.Build(data);
            int lampMeshes = RefreshLamps(world);
            EditorUtility.DisplayProgressBar("Lundun", "Lamp colours…", 0.4f);
            RegisterLamps(world, true);
            EditorUtility.DisplayProgressBar("Lundun", "Light from shops, pubs and lobbies…", 0.55f);
            DressingResult dressed = Generator.Dress(data, world);
            int spills = BuildSpill(world, dressed);
            EditorUtility.DisplayProgressBar("Lundun", "Repainting the windows…", 0.8f);
            LundunBuildingKitBuilder.EnsureMaterials(true);
            ApplyLook();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            int[] byStyle = new int[4];
            foreach (LampInfo l in world.lamps) byStyle[Mathf.Clamp(l.style, 0, 3)]++;
            Debug.Log("[Lundun] L5 lighting: " + world.lamps.Count + " lamps (" + byStyle[0] + " sodium, " + byStyle[1] + " sodium on estate columns, " +
                      byStyle[2] + " heritage, " + byStyle[3] + " LED) in " + lampMeshes + " meshes; " + spills + " shop / pub / lobby light spills; windows repainted." +
                      "\nNext: Cmd+S. (Tweak colours in Scripts/World/LundunLampColours.cs; untick 'Colour By District' on LundunLighting → LundunLampLights for one colour.)");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Lundun", "Lighting by district failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.", "OK");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Lundun/24b Lighting by District – Undo", false, 30)]
    public static void Undo()
    {
        GameObject old = GameObject.Find(SpillRoot);
        if (old != null) Object.DestroyImmediate(old);
        LundunLampLights ll = Object.FindAnyObjectByType<LundunLampLights>(FindObjectsInactive.Include);
        if (ll != null)
        {
            SerializedObject so = new SerializedObject(ll);
            so.FindProperty("colourByDistrict").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Lundun] Shop light removed; real lamp lights back to one colour. (The lamp posts stay by district: Lundun → 1 rebuilds everything.) Cmd+S.");
    }

    // ------------------------------------------------------------------ lamps
    /// Replaces World/Lamps with the district lamps. Returns the number of meshes.
    public static int RefreshLamps(GreyboxResult world)
    {
        GameObject w = GameObject.Find("World");
        if (w == null) throw new System.Exception("No 'World' object in the scene. Run Lundun → 1 Build Greybox World first.");
        Transform lamps = w.transform.Find("Lamps");
        if (lamps == null) { lamps = new GameObject("Lamps").transform; lamps.SetParent(w.transform, false); }
        for (int i = lamps.childCount - 1; i >= 0; i--) Object.DestroyImmediate(lamps.GetChild(i).gameObject);
        AssetDatabase.DeleteAsset(LampMeshPath);

        Mesh container = null;
        int n = 0;
        foreach (MeshData md in world.meshes)
        {
            if (md.group != "Lamps" || md.tri.Count == 0) continue;
            Mesh mesh = ToMesh(md);
            if (container == null) { AssetDatabase.CreateAsset(mesh, LampMeshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
            GameObject go = new GameObject(md.name);
            go.transform.SetParent(lamps, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(md.material);
            if (md.material.StartsWith("LightPool")) mr.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            n++;
        }
        return n;
    }

    /// Gives the lamp lights their styles and LundunLighting the district lamp materials. Also called by Lundun → 1.
    public static void RegisterLamps(GreyboxResult world, bool byDistrict)
    {
        Vector3[] pos = new Vector3[world.lamps.Count];
        int[] styles = new int[world.lamps.Count];
        for (int i = 0; i < pos.Length; i++)
        {
            LampInfo l = world.lamps[i];
            pos[i] = new Vector3((float)l.x, (float)l.y, (float)l.z);
            styles[i] = l.style;
        }
        LundunLampLights lamps = Object.FindAnyObjectByType<LundunLampLights>(FindObjectsInactive.Include);
        if (lamps != null)
        {
            lamps.SetLamps(pos, styles);
            if (byDistrict)
            {
                SerializedObject ls = new SerializedObject(lamps);
                ls.FindProperty("colourByDistrict").boolValue = true;
                ls.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(lamps);
        }

        // ground glow colours (in case the materials were made before L5)
        foreach (string m in new[] { "LightPool", "LightPool_Heritage", "LightPool_LED", "LightPool_Shop", "LightPool_Pub", "LightPool_Lobby" })
        {
            Material mat = LundunGreyboxBuilder.WorldMaterial(m);
            mat.SetColor("_Color", LundunLampColours.Pool(m));
            EditorUtility.SetDirty(mat);
        }

        LundunLighting ll = Object.FindAnyObjectByType<LundunLighting>(FindObjectsInactive.Include);
        if (ll == null) return;
        SerializedObject so = new SerializedObject(ll);
        so.FindProperty("lampGlow").colorValue = LundunLampColours.Sodium;
        SetArray(so.FindProperty("lampHeadVariants"), new Object[] { LundunGreyboxBuilder.WorldMaterial("LampHead_Heritage"), LundunGreyboxBuilder.WorldMaterial("LampHead_LED") });
        SerializedProperty glow = so.FindProperty("lampVariantGlow");
        glow.arraySize = 2;
        glow.GetArrayElementAtIndex(0).colorValue = LundunLampColours.Heritage;
        glow.GetArrayElementAtIndex(1).colorValue = LundunLampColours.LED;
        List<Object> pools = new List<Object>();
        foreach (string m in new[] { "LightPool_Heritage", "LightPool_LED", "LightPool_Shop", "LightPool_Pub", "LightPool_Lobby" }) pools.Add(LundunGreyboxBuilder.WorldMaterial(m));
        SetArray(so.FindProperty("poolVariants"), pools.ToArray());
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ll);
    }

    static void SetArray(SerializedProperty p, Object[] items)
    {
        p.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    public static void ApplyLook()
    {
        LundunLighting ll = Object.FindAnyObjectByType<LundunLighting>(FindObjectsInactive.Include);
        if (ll != null && ll.preset != null) ll.Apply(ll.preset.look);
    }

    // ------------------------------------------------------------------ light from shops, pubs and lobbies
    /// A soft glow on the pavement in front of every lit shop window, pub and office lobby, baked per 100 m block.
    static int BuildSpill(GreyboxResult world, DressingResult dressed)
    {
        List<double[]> spots = new List<double[]>(dressed.shopLights);
        foreach (BuildingInfo b in world.buildings)
        {
            Generator.KitPlan p = Generator.KitPlanFor(b);
            if (p == null || (p.type != Generator.KitType.Tower && p.type != Generator.KitType.Office)) continue;
            double fx = p.c.x + p.n.x * (p.D / 2), fz = p.c.z + p.n.z * (p.D / 2);
            spots.Add(new[] { fx, fz, p.n.x, p.n.z, System.Math.Min(8.0, p.W * 0.5), 2 });
        }

        GameObject old = GameObject.Find(SpillRoot);
        if (old != null) Object.DestroyImmediate(old);
        AssetDatabase.DeleteAsset(SpillMeshPath);
        GameObject root = new GameObject(SpillRoot);

        // per block, per kind
        Dictionary<string, List<Vector3>> verts = new Dictionary<string, List<Vector3>>();
        Dictionary<string, List<Vector2>> uvs = new Dictionary<string, List<Vector2>>();
        foreach (double[] s in spots)
        {
            int kind = Mathf.Clamp((int)s[5], 0, 2);
            Vector3 f = new Vector3((float)s[0], PaveTop + 0.03f, (float)s[1]);
            Vector3 n = new Vector3((float)s[2], 0f, (float)s[3]).normalized;
            Vector3 t = new Vector3(-n.z, 0f, n.x);
            float ha = (float)s[4] * 0.6f, depth = kind == 1 ? 3.0f : 3.4f;
            string key = kind + "_" + Mathf.FloorToInt((f.x + 300f) / 100f) + "_" + Mathf.FloorToInt((f.z + 300f) / 100f);
            List<Vector3> v; List<Vector2> u;
            if (!verts.TryGetValue(key, out v)) { v = new List<Vector3>(); verts[key] = v; u = new List<Vector2>(); uvs[key] = u; }
            else u = uvs[key];
            // the glow texture is a soft disc: its middle sits on the shop front, so only the outer half lands on the pavement
            Vector3 back = f - n * 0.15f, front = f + n * depth;
            v.Add(back - t * ha); u.Add(new Vector2(0f, 0.48f));
            v.Add(back + t * ha); u.Add(new Vector2(1f, 0.48f));
            v.Add(front + t * ha); u.Add(new Vector2(1f, 1f));
            v.Add(front - t * ha); u.Add(new Vector2(0f, 1f));
        }

        Mesh container = null;
        List<string> keys = new List<string>(verts.Keys); keys.Sort();
        foreach (string key in keys)
        {
            List<Vector3> v = verts[key];
            int[] tri = new int[v.Count / 4 * 6];
            for (int q = 0, k = 0; q < v.Count; q += 4)
            {
                tri[k++] = q; tri[k++] = q + 2; tri[k++] = q + 1;
                tri[k++] = q; tri[k++] = q + 3; tri[k++] = q + 2;
            }
            Mesh mesh = new Mesh { name = "ShopLight_" + key, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uvs[key]);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (container == null) { AssetDatabase.CreateAsset(mesh, SpillMeshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
            GameObject go = new GameObject(mesh.name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(SpillMats[int.Parse(key.Substring(0, 1))]);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }
        return spots.Count;
    }

    public static Mesh ToMesh(MeshData md)
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
}
