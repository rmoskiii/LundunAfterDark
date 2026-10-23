// Lundun → 23  Street Furniture + Parked Cars (L3)
// 1. Lundun → 13: re-dresses the city, now with district furniture (area railings, front gardens and wheelie bins,
//    Rye Lane market, pub signs, planters, cabinets, cycle stands, estate fencing, roadworks, scaffolding, bollards)
// 2. Lundun → 22: re-lays the street markings (so the bays match this layout exactly)
// 3. parks cars in the bays: most are static (the traffic models, baked into a few big meshes per 100 m block, with
//    colliders so you can't walk or drive through them); one bay in four is kept for LundunPopulation's real cars,
//    which you can steal. Vans are mostly white.
// Afterwards: Lundun → 3 Bake Navigation, then Cmd+S. Lundun → 23b removes the parked cars.
using System.Collections.Generic;
using System.IO;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunParkedBuilder
{
    const string Dir = "Assets/World/Parked";
    const string PrefabDir = "Assets/Vehicles/Prefabs";
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string MeshPath = Dir + "/ParkedMeshes.asset";
    const string RootName = "LundunParked";

    [MenuItem("Lundun/23  Street Furniture + Parked Cars (L3)", false, 27)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { EditorUtility.DisplayDialog("Lundun", "Open the Lundun scene first.", "OK"); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + JsonPath, "OK"); return; }

        LundunDressingBuilder.Build();          // furniture
        LundunStreetsBuilder.Build();           // markings and bays (and district paving)
        try { Park(LundunExpansionBuilder.Parse(json.text), scene); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Lundun", "Parked cars failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.", "OK");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Lundun/23b Parked Cars – Remove", false, 28)]
    public static void Undo()
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Lundun] Parked cars removed (real parked cars go back to random kerbs). Street furniture stays (it's part of Lundun → 13). Cmd+S.");
    }

    static void Park(LundunWorldData data, Scene scene)
    {
        EditorUtility.DisplayProgressBar("Lundun", "Finding parking spaces…", 0.1f);
        GreyboxResult world = Generator.Build(data);
        DressingResult dressed = Generator.Dress(data, world);
        StreetResult streets = Generator.Streets(data, world, dressed);
        List<ParkSlot> slots = Generator.ParkingSlots(streets);

        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/World", "Parked");
        if (!AssetDatabase.IsValidFolder(Dir + "/Paint")) AssetDatabase.CreateFolder(Dir, "Paint");

        Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        List<string> missing = new List<string>();
        foreach (ParkSlot s in slots)
        {
            if (s.forPopulation || prefabs.ContainsKey(s.model)) continue;
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Veh_" + s.model + ".prefab");
            prefabs[s.model] = p;
            if (p == null) missing.Add(s.model);
        }
        GameObject fallback = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Veh_sedan.prefab");
        if (fallback == null) foreach (var kv in prefabs) if (kv.Value != null) { fallback = kv.Value; break; }
        if (fallback == null) { EditorUtility.DisplayDialog("Lundun", "No car prefabs in " + PrefabDir + ". Run Lundun → 10 Make Cars from Models first.", "OK"); return; }

        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        AssetDatabase.DeleteAsset(MeshPath);
        GameObject root = new GameObject(RootName);
        Transform cols = new GameObject("Colliders").transform; cols.SetParent(root.transform, false);

        // gather every car part, per 100 m block, per (painted) material
        Dictionary<string, Dictionary<Material, List<CombineInstance>>> blocks = new Dictionary<string, Dictionary<Material, List<CombineInstance>>>();
        Dictionary<string, Material> paints = new Dictionary<string, Material>();
        int cars = 0, painted = 0;
        EditorUtility.DisplayProgressBar("Lundun", "Parking cars…", 0.3f);
        foreach (ParkSlot s in slots)
        {
            if (s.forPopulation) continue;
            GameObject prefab = prefabs.ContainsKey(s.model) && prefabs[s.model] != null ? prefabs[s.model] : fallback;
            Matrix4x4 at = Matrix4x4.TRS(new Vector3((float)s.x, 0.02f, (float)s.z), Quaternion.Euler(0f, (float)s.yaw, 0f), Vector3.one);
            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
            string key = Mathf.FloorToInt(((float)s.x + 300f) / 100f) + "_" + Mathf.FloorToInt(((float)s.z + 300f) / 100f);
            Dictionary<Material, List<CombineInstance>> block;
            if (!blocks.TryGetValue(key, out block)) { block = new Dictionary<Material, List<CombineInstance>>(); blocks[key] = block; }
            bool gotPaint = false;
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null || InBumper(mf.transform)) continue;
                Mesh mesh = mf.sharedMesh;
                Material[] mats = mr.sharedMaterials;
                Matrix4x4 m = at * toRoot * mf.transform.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material mat = mats.Length == 0 ? null : mats[Mathf.Min(sub, mats.Length - 1)];
                    if (mat == null) continue;
                    if (IsPaint(mat)) { mat = Paint(paints, mat, s.colour); gotPaint = true; }
                    List<CombineInstance> list;
                    if (!block.TryGetValue(mat, out list)) { list = new List<CombineInstance>(); block[mat] = list; }
                    list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = m });
                }
            }
            if (gotPaint) painted++;
            // a collider the size of the car's own body box
            BoxCollider pb = prefab.GetComponent<BoxCollider>();
            GameObject c = new GameObject("Car_" + cars);
            c.transform.SetParent(cols, false);
            c.transform.SetPositionAndRotation(new Vector3((float)s.x, 0.02f, (float)s.z), Quaternion.Euler(0f, (float)s.yaw, 0f));
            BoxCollider bc = c.AddComponent<BoxCollider>();
            if (pb != null) { bc.center = pb.center; bc.size = pb.size; }
            else { bc.center = new Vector3(0f, 0.8f, 0f); bc.size = new Vector3(1.8f, 1.4f, (float)s.length - 0.2f); }
            GameObjectUtility.SetStaticEditorFlags(c, StaticEditorFlags.BatchingStatic);
            cars++;
        }

        // bake
        EditorUtility.DisplayProgressBar("Lundun", "Baking parked cars…", 0.7f);
        Mesh container = null;
        List<Renderer> renderers = new List<Renderer>();
        List<string> keys = new List<string>(blocks.Keys); keys.Sort();
        foreach (string key in keys)
            foreach (var kv in blocks[key])
            {
                Mesh mesh = new Mesh { name = "Parked_" + key + "_" + kv.Key.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
                GameObject g = new GameObject(mesh.name);
                g.transform.SetParent(root.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = ShadowCastingMode.On;
                GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                renderers.Add(r);
            }

        // the bays kept for real cars
        List<Vector4> free = new List<Vector4>();
        foreach (ParkSlot s in slots) if (s.forPopulation) free.Add(new Vector4((float)s.x, (float)s.z, (float)s.yaw, (float)s.length));
        LundunParking lp = root.AddComponent<LundunParking>();
        lp.SetData(free.ToArray(), renderers.ToArray());
        EditorUtility.SetDirty(lp);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Dictionary<string, int> byModel = new Dictionary<string, int>();
        foreach (ParkSlot s in slots) if (!s.forPopulation) { int n; byModel.TryGetValue(s.model, out n); byModel[s.model] = n + 1; }
        List<string> parts = new List<string>(); foreach (var kv in byModel) parts.Add(kv.Key + " " + kv.Value);
        Debug.Log("[Lundun] Parked " + cars + " cars (" + string.Join(", ", parts.ToArray()) + ") in " + renderers.Count + " baked pieces; " + free.Count +
                  " bays kept for stealable cars." + (missing.Count > 0 ? "  Missing prefabs (used a saloon instead): " + string.Join(", ", missing.ToArray()) : "") +
                  (painted == 0 ? "  Note: these car models have no 'Paint' material, so they keep their original colours." : "") +
                  "\nNext: Lundun → 3 Bake Navigation, then Cmd+S.");
    }

    static bool InBumper(Transform t) { for (; t != null; t = t.parent) if (t.GetComponent<CarBumper>() != null) return true; return false; }

    /// Same rule as LundunPopulation.Repaint: body materials are the ones named Paint / Roof.
    static bool IsPaint(Material m) { return m.name.Contains("Paint") || m.name.Contains("Roof"); }

    static Material Paint(Dictionary<string, Material> cache, Material src, int colour)
    {
        string key = src.name + "_" + Generator.ParkColourNames[colour];
        Material m;
        if (cache.TryGetValue(key, out m)) return m;
        string path = Dir + "/Paint/" + key.Replace(" ", "_") + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(src) { name = key }; AssetDatabase.CreateAsset(m, path); }
        double[] c = Generator.ParkColours[colour];
        m.SetColor("_BaseColor", new Color((float)c[0], (float)c[1], (float)c[2], 1f));
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        cache[key] = m;
        return m;
    }
}
