// Lundun → 13  Dress the City
// Places the street dressing by rule (LundunDressingGeometry), paints its one texture (LundunDressingAtlas)
// and builds it under a "LundunDressing" object:
//   • everything merged into 100 m blocks, 3 materials sharing one texture (+ your Belisha / BusStop / PhoneBox
//     prefabs merged the same way), so it's a handful of draw calls per block
//   • box colliders only on the bigger things (trees, shelters, phone boxes, cages, station surrounds, billboard posts)
// Safe to run again: it replaces the old dressing. Run Lundun → 3 afterwards so people walk round the new things.
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

public static class LundunDressingBuilder
{
    const string Dir = "Assets/World/Dressing";
    const string AtlasPath = Dir + "/DressingAtlas.png";
    const string MeshPath = Dir + "/DressingMeshes.asset";
    const string DataPath = "Assets/World/LundunWorld.json";
    const string RootName = "LundunDressing";

    [MenuItem("Lundun/13  Dress the City", false, 13)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null) { Dialog("Can't find " + DataPath + "."); return; }
        LundunWorldData data = JsonUtility.FromJson<LundunWorldData>(json.text);

        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Working out where everything goes…", 0.05f);
            GreyboxResult world = Generator.Build(data);           // same seed as menu 1, so it matches your city exactly
            DressingResult r = Generator.Dress(data, world);

            EnsureFolder(Dir);
            EditorUtility.DisplayProgressBar("Lundun", "Painting signs and graffiti…", 0.25f);
            Texture2D atlas = SaveAtlas(r.atlas);
            Material solid = MakeMaterial("Dress_Solid", atlas, false, false);
            Material cut = MakeMaterial("Dress_Cutout", atlas, true, false);
            Material glow = MakeMaterial("Dress_Glow", atlas, false, true);
            Dictionary<string, Material> mats = new Dictionary<string, Material> { { "DressSolid", solid }, { "DressCutout", cut }, { "DressGlow", glow } };

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            AssetDatabase.DeleteAsset(MeshPath);
            GameObject root = new GameObject(RootName);
            Transform blocks = new GameObject("Blocks").transform; blocks.SetParent(root.transform, false);
            Transform props = new GameObject("Prefab Props").transform; props.SetParent(root.transform, false);
            Transform cols = new GameObject("Colliders").transform; cols.SetParent(root.transform, false);
            Mesh container = null;

            // 1. rule-placed meshes
            int done = 0;
            foreach (MeshData md in r.meshes)
            {
                if (++done % 20 == 0) EditorUtility.DisplayProgressBar("Lundun", "Building blocks…", 0.3f + 0.35f * done / r.meshes.Count);
                if (md.tri.Count == 0) continue;
                Mesh mesh = ToMesh(md);
                container = SaveMesh(mesh, container);
                GameObject go = new GameObject(md.name);
                go.transform.SetParent(blocks, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[md.material];
                mr.shadowCastingMode = md.material == "DressSolid" ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            // 2. your prefabs (Belisha / BusStop / PhoneBox), merged per block per material
            EditorUtility.DisplayProgressBar("Lundun", "Merging bus shelters, phone boxes and Belisha beacons…", 0.7f);
            Material globe;
            int prefabParts = BakePrefabs(r.prefabs, props, ref container, out globe);

            // 3. colliders
            EditorUtility.DisplayProgressBar("Lundun", "Colliders…", 0.85f);
            foreach (DressBox c in r.colliders)
            {
                GameObject g = new GameObject(c.name);
                g.transform.SetParent(cols, false);
                g.transform.SetPositionAndRotation(new Vector3((float)c.cx, (float)c.cy, (float)c.cz), Quaternion.Euler(0f, (float)c.yaw, 0f));
                g.AddComponent<BoxCollider>().size = new Vector3((float)c.sx, (float)c.sy, (float)c.sz);
            }

            // 4. clear the park trees standing where the stations and cages now go, and the buildings standing on
            //    the hospital / police / safehouse plots (only those meshes are swapped; nothing else moves)
            int treeBlocks = RefreshParkTrees(world, ref container);
            int buildingBlocks = RefreshBuildings(world, ref container);

            // 4b. hospitals, police stations, safehouses: where James appears, where the Kestrel parks, map markers
            LundunPlaces places = root.AddComponent<LundunPlaces>();
            LundunPlaces.Place[] list = new LundunPlaces.Place[r.places.Count];
            for (int i = 0; i < list.Length; i++)
            {
                PlaceInfo pi = r.places[i];
                list[i] = new LundunPlaces.Place { id = pi.id, kind = pi.kind, name = pi.name,
                    door = new Vector3((float)pi.doorX, 0f, (float)pi.doorZ), doorYaw = (float)pi.doorYaw,
                    hasCar = pi.hasCar, car = new Vector3((float)pi.carX, 0f, (float)pi.carZ), carYaw = (float)pi.carYaw };
                GameObject m = new GameObject("Place_" + pi.id);         // map marker (the minimap follows these)
                m.transform.SetParent(root.transform, false);
                m.transform.SetPositionAndRotation(list[i].door, Quaternion.Euler(0f, list[i].doorYaw, 0f));
            }
            GameObject carRoot = GameObject.Find("Car");
            places.SetData(list, carRoot != null ? carRoot.transform : null);
            if (carRoot == null) Debug.LogWarning("[Lundun] No root object called 'Car' — the Kestrel won't come home to safehouses.");

            // 5. the runtime bit: glow by time of day, blinking beacons, bus stops, draw distance
            LundunDressing rt = root.AddComponent<LundunDressing>();
            Vector4[] stops = new Vector4[r.busStops.Count];
            for (int i = 0; i < stops.Length; i++) { BusStopInfo b = r.busStops[i]; stops[i] = new Vector4((float)b.x, (float)b.z, (float)b.dx, (float)b.dz); }
            string[] names = new string[r.stations.Count]; Vector3[] spots = new Vector3[r.stations.Count];
            for (int i = 0; i < names.Length; i++) { names[i] = r.stations[i].name; spots[i] = new Vector3((float)r.stations[i].x, 0f, (float)r.stations[i].z); }
            rt.SetData(stops, glow, globe, names, spots);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("[Lundun] City dressed. " + string.Join("\n", r.log.ToArray()) +
"\n" + prefabParts + " prefab blocks merged, " + treeBlocks + " park tree blocks and " + buildingBlocks + " building blocks refreshed. Places: " + r.places.Count + ". Stations: " + string.Join(", ", names) +
                      "\nNext: Lundun → 3  Bake Navigation (so people walk round the new things), Cmd+S, Play.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Dialog("Dressing failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // ------------------------------------------------------------------ atlas + materials
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
        ti.maxTextureSize = 4096;
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
            m.SetFloat("_Cull", 0f);                                    // railings, fences, graffiti: both sides
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

    // ------------------------------------------------------------------ prefabs → merged blocks
    static int BakePrefabs(List<PropPlacement> list, Transform parent, ref Mesh container, out Material globeMat)
    {
        globeMat = null;
        Dictionary<string, List<PropPlacement>> byKind = new Dictionary<string, List<PropPlacement>>();
        foreach (PropPlacement p in list) { List<PropPlacement> l; if (!byKind.TryGetValue(p.kind, out l)) byKind[p.kind] = l = new List<PropPlacement>(); l.Add(p); }

        // block key → material → pieces
        Dictionary<string, Dictionary<Material, List<CombineInstance>>> buckets = new Dictionary<string, Dictionary<Material, List<CombineInstance>>>();
        foreach (var kv in byKind)
        {
            string path = "Assets/Prefabs/" + kv.Key + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[Lundun] " + path + " not found — " + kv.Value.Count + " " + kv.Key + " skipped."); continue; }
            GameObject inst = (GameObject)Object.Instantiate(prefab);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;
            List<MeshFilter> filters = new List<MeshFilter>(inst.GetComponentsInChildren<MeshFilter>(true));
            foreach (PropPlacement p in kv.Value)
            {
                Matrix4x4 place = Matrix4x4.TRS(new Vector3((float)p.x, (float)p.y, (float)p.z), Quaternion.Euler(0f, (float)p.yaw, 0f), Vector3.one);
                string key = Block(p.x) + "_" + Block(p.z);
                Dictionary<Material, List<CombineInstance>> byMat;
                if (!buckets.TryGetValue(key, out byMat)) buckets[key] = byMat = new Dictionary<Material, List<CombineInstance>>();
                foreach (MeshFilter mf in filters)
                {
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    Material[] ms = mr.sharedMaterials;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount && s < ms.Length; s++)
                    {
                        Material m = ms[s];
                        if (m == null) continue;
                        if (kv.Key == "Belisha" && mf.name == "Globe") { if (globeMat == null) globeMat = GlobeMaterial(m); m = globeMat; }
                        List<CombineInstance> ci;
                        if (!byMat.TryGetValue(m, out ci)) byMat[m] = ci = new List<CombineInstance>();
                        ci.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = place * toRoot * mf.transform.localToWorldMatrix });
                    }
                }
            }
            Object.DestroyImmediate(inst);
        }

        int made = 0;
        List<string> keys = new List<string>(buckets.Keys); keys.Sort();
        foreach (string key in keys)
            foreach (var mk in buckets[key])
            {
                Mesh mesh = new Mesh { name = "Props_" + key + "_" + mk.Key.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(mk.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                container = SaveMesh(mesh, container);
                GameObject go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mk.Key;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                made++;
            }
        return made;
    }

    /// Swaps World/Parks tree meshes for the new ones (same trees, minus those in the station and cage spots).
    static int RefreshParkTrees(GreyboxResult world, ref Mesh container)
    {
        GameObject w = GameObject.Find("World");
        Transform parks = w != null ? w.transform.Find("Parks") : null;
        if (parks == null) { Debug.LogWarning("[Lundun] World/Parks not found — park trees not refreshed (run Lundun → 1 first)."); return 0; }
        Material trunk = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/Trunk.mat");
        Material leaves = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/Leaves.mat");
        for (int i = parks.childCount - 1; i >= 0; i--)
        {
            Transform c = parks.GetChild(i);
            if (c.name.StartsWith("TreeTrunks") || c.name.StartsWith("TreeCanopies")) Object.DestroyImmediate(c.gameObject);
        }
        int n = 0;
        Type modifier = FindType("Unity.AI.Navigation.NavMeshModifier");
        foreach (MeshData md in world.meshes)
        {
            if (md.group != "Parks" || (md.material != "Trunk" && md.material != "Leaves") || md.tri.Count == 0) continue;
            Mesh mesh = ToMesh(md);
            container = SaveMesh(mesh, container);
            GameObject go = new GameObject(md.name);
            go.transform.SetParent(parks, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = md.material == "Trunk" ? trunk : leaves;
            if (md.collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (modifier != null)
            {
                Component c = go.AddComponent(modifier);
                SerializedObject so = new SerializedObject(c);
                SerializedProperty ig = so.FindProperty("m_IgnoreFromBuild");
                if (ig != null) { ig.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            n++;
        }
        return n;
    }

    /// Swaps World/Buildings for the new set (the same buildings, minus the ones on the R7 plots).
    static int RefreshBuildings(GreyboxResult world, ref Mesh container)
    {
        GameObject w = GameObject.Find("World");
        Transform group = w != null ? w.transform.Find("Buildings") : null;
        if (group == null) { Debug.LogWarning("[Lundun] World/Buildings not found — buildings not refreshed (run Lundun → 1 first)."); return 0; }
        foreach (MeshData md in world.meshes)
            if (md.group == "Buildings" && AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/" + md.material + ".mat") == null)
            { Debug.LogWarning("[Lundun] Building material " + md.material + " missing — buildings not refreshed. Run Lundun → 1 once, then 13 again."); return 0; }
        for (int i = group.childCount - 1; i >= 0; i--) Object.DestroyImmediate(group.GetChild(i).gameObject);
        int n = 0;
        foreach (MeshData md in world.meshes)
        {
            if (md.group != "Buildings" || md.tri.Count == 0) continue;
            Mesh mesh = ToMesh(md);
            container = SaveMesh(mesh, container);
            GameObject go = new GameObject(md.name);
            go.transform.SetParent(group, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/" + md.material + ".mat");
            if (md.collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            n++;
        }
        return n;
    }

    static Type FindType(string fullName)
    {
        foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = a.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }

    static int Block(double v) { return (int)Math.Floor((v - Generator.ChunkOrigin) / Generator.ChunkSize); }

    /// The beacons' globe material, copied so all the merged globes can blink together at runtime.
    static Material GlobeMaterial(Material original)
    {
        string path = Dir + "/Dress_BelishaGlobe.mat";
        AssetDatabase.DeleteAsset(path);
        Material m = new Material(original) { name = "Dress_BelishaGlobe" };
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (!m.HasProperty("_EmissionColor") || m.GetColor("_EmissionColor").maxColorComponent < 0.1f)
            m.SetColor("_EmissionColor", new Color(1f, 0.54f, 0f) * 4f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ------------------------------------------------------------------ helpers
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

    static Mesh SaveMesh(Mesh mesh, Mesh container)
    {
        if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); return mesh; }
        AssetDatabase.AddObjectToAsset(mesh, container);
        return container;
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
