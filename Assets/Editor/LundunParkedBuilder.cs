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

        GameObject old = GameObject.Find(RootName);
        GameObject root = new GameObject(RootName);
        List<Renderer> renderers; List<Vector4> free; int cars, painted; List<string> missing;
        if (!Bake(slots, root.transform, MeshPath, out renderers, out free, out cars, out painted, out missing))
        {
            Object.DestroyImmediate(root);                 // the old parked cars stay
            EditorUtility.DisplayDialog("Lundun", "No car prefabs in " + PrefabDir + ". Run Lundun → 10 Make Cars from Models first.", "OK");
            return;
        }
        if (old != null) Object.DestroyImmediate(old);

        // the bays kept for real cars
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

    /// Bakes the static parked cars for these slots under root (per 100 m block, per painted material, colliders under
    /// root/Colliders), into a mesh asset at meshPath. Also returns the bays kept for real cars. False if there are no
    /// car prefabs at all. (E3: Brixtun's menu uses it too.)
    public static bool Bake(List<ParkSlot> slots, Transform root, string meshPath, out List<Renderer> renderers, out List<Vector4> free,
                            out int cars, out int painted, out List<string> missing)
    {
        renderers = new List<Renderer>(); free = new List<Vector4>(); cars = 0; painted = 0;
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/World", "Parked");
        if (!AssetDatabase.IsValidFolder(Dir + "/Paint")) AssetDatabase.CreateFolder(Dir, "Paint");

        Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        missing = new List<string>();
        foreach (ParkSlot s in slots)
        {
            if (s.forPopulation || prefabs.ContainsKey(s.model)) continue;
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Veh_" + s.model + ".prefab");
            prefabs[s.model] = p;
            if (p == null) missing.Add(s.model);
        }
        GameObject fallback = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Veh_sedan.prefab");
        if (fallback == null) foreach (var kv in prefabs) if (kv.Value != null) { fallback = kv.Value; break; }
        if (fallback == null) return false;

        AssetDatabase.DeleteAsset(meshPath);
        Transform cols = new GameObject("Colliders").transform; cols.SetParent(root, false);

        // gather every car part, per 100 m block, per (painted) material
        Dictionary<string, Dictionary<Material, List<CombineInstance>>> blocks = new Dictionary<string, Dictionary<Material, List<CombineInstance>>>();
        Dictionary<string, Material> paints = new Dictionary<string, Material>();
        Dictionary<GameObject, ParkedModel> realModels = new Dictionary<GameObject, ParkedModel>();
        Transform realRoot = null;
        Mesh container = null;
        cars = 0; painted = 0;
        EditorUtility.DisplayProgressBar("Lundun", "Parking cars…", 0.3f);
        foreach (ParkSlot s in slots)
        {
            if (s.forPopulation) continue;
            GameObject prefab = prefabs.ContainsKey(s.model) && prefabs[s.model] != null ? prefabs[s.model] : fallback;
            GameObject real = LundunCarImporter.ForParkedSlot(s.model, s.x, s.z);       // V2: the real-car models, once made
            if (real != null)
            {
                // V2: the detailed cars aren't copied into the big block meshes (hundreds of copies made a 158 MB file);
                // each bay gets one object that shares its model's mesh, painted the bay's colour
                ParkedModel pm;
                if (!realModels.TryGetValue(real, out pm)) { pm = MakeParkedModel(real, meshPath, ref container); realModels[real] = pm; }
                if (realRoot == null) { realRoot = new GameObject("Cars").transform; realRoot.SetParent(root, false); }
                GameObject pc = new GameObject(real.name + "_" + cars);
                pc.transform.SetParent(realRoot, false);
                pc.transform.SetPositionAndRotation(new Vector3((float)s.x, 0.02f, (float)s.z), Quaternion.Euler(0f, (float)s.yaw, 0f));
                pc.AddComponent<MeshFilter>().sharedMesh = pm.mesh;
                MeshRenderer pr = pc.AddComponent<MeshRenderer>();
                Material[] pms = (Material[])pm.mats.Clone();
                LundunCarPaint jobs = real.GetComponent<LundunCarPaint>();
                if (jobs != null && pm.paintIndex >= 0)
                {
                    Material job = jobs.Named(Generator.ParkColourNames[s.colour], s.colour);
                    if (job != null) { pms[pm.paintIndex] = job; painted++; }
                }
                pr.sharedMaterials = pms;
                pr.shadowCastingMode = ShadowCastingMode.On;
                GameObjectUtility.SetStaticEditorFlags(pc, StaticEditorFlags.OccludeeStatic);   // not batching-static: that would copy the mesh again
                renderers.Add(pr);
                BoxCollider rpb = real.GetComponent<BoxCollider>();
                GameObject rc = new GameObject("Car_" + cars);
                rc.transform.SetParent(cols, false);
                rc.transform.SetPositionAndRotation(pc.transform.position, pc.transform.rotation);
                BoxCollider rbc = rc.AddComponent<BoxCollider>();
                if (rpb != null) { rbc.center = rpb.center; rbc.size = rpb.size; }
                else { rbc.center = new Vector3(0f, 0.8f, 0f); rbc.size = new Vector3(1.8f, 1.4f, (float)s.length - 0.2f); }
                GameObjectUtility.SetStaticEditorFlags(rc, StaticEditorFlags.BatchingStatic);
                cars++;
                continue;
            }
            LundunCarPaint paintJobs = prefab.GetComponent<LundunCarPaint>();
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
                    if (paintJobs != null && mr == paintJobs.Body && sub == paintJobs.Slot)
                    {   // V2: a ready-painted body in this bay's colour (or the nearest it has)
                        Material job = paintJobs.Named(Generator.ParkColourNames[s.colour], s.colour);
                        if (job != null) { mat = job; gotPaint = true; }
                    }
                    else if (IsPaint(mat)) { mat = Paint(paints, mat, s.colour); gotPaint = true; }
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
        renderers.RemoveAll(x => x == null || x.transform.parent != realRoot);   // keep the real cars' renderers
        List<string> keys = new List<string>(blocks.Keys); keys.Sort();
        foreach (string key in keys)
            foreach (var kv in blocks[key])
            {
                Mesh mesh = new Mesh { name = "Parked_" + key + "_" + kv.Key.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                if (container == null) { AssetDatabase.CreateAsset(mesh, meshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
                GameObject g = new GameObject(mesh.name);
                g.transform.SetParent(root, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = ShadowCastingMode.On;
                GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                renderers.Add(r);
            }

        // the bays kept for real cars
        free = new List<Vector4>();
        foreach (ParkSlot s in slots) if (s.forPopulation) free.Add(new Vector4((float)s.x, (float)s.z, (float)s.yaw, (float)s.length));
        return true;
    }

    class ParkedModel { public Mesh mesh; public Material[] mats; public int paintIndex = -1; }

    /// V2: one mesh for a whole car (body, wheels, plates), one part per material, saved once in the parked-cars asset.
    static ParkedModel MakeParkedModel(GameObject prefab, string meshPath, ref Mesh container)
    {
        Dictionary<Material, List<CombineInstance>> byMat = new Dictionary<Material, List<CombineInstance>>();
        List<Material> order = new List<Material>();
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || mf.sharedMesh == null || InBumper(mf.transform)) continue;
            Material[] mats = mr.sharedMaterials;
            for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
            {
                Material mat = mats.Length == 0 ? null : mats[Mathf.Min(sub, mats.Length - 1)];
                if (mat == null) continue;
                List<CombineInstance> list;
                if (!byMat.TryGetValue(mat, out list)) { list = new List<CombineInstance>(); byMat[mat] = list; order.Add(mat); }
                list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sub, transform = toRoot * mf.transform.localToWorldMatrix });
            }
        }
        List<CombineInstance> parts = new List<CombineInstance>();
        List<Mesh> temp = new List<Mesh>();
        foreach (Material m in order)
        {
            Mesh part = new Mesh { indexFormat = IndexFormat.UInt32 };
            part.CombineMeshes(byMat[m].ToArray(), true, true);
            temp.Add(part);
            parts.Add(new CombineInstance { mesh = part, subMeshIndex = 0, transform = Matrix4x4.identity });
        }
        Mesh mesh = new Mesh { name = "ParkedCar_" + prefab.name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), false, false);
        mesh.RecalculateBounds();
        foreach (Mesh t in temp) Object.DestroyImmediate(t);
        if (container == null) { AssetDatabase.CreateAsset(mesh, meshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);

        ParkedModel pm = new ParkedModel { mesh = mesh, mats = order.ToArray() };
        LundunCarPaint jobs = prefab.GetComponent<LundunCarPaint>();
        if (jobs != null && jobs.Body != null)
        {
            Material[] bm = jobs.Body.sharedMaterials;
            if (jobs.Slot >= 0 && jobs.Slot < bm.Length) pm.paintIndex = order.IndexOf(bm[jobs.Slot]);
        }
        return pm;
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
