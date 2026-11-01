// Lundun menu: builds the greybox city from Assets/World/LundunWorld.json.
// Menu order: 0 Create scene → 1 Build world → 2 Place gameplay → 3 Bake navigation → 4 iPhone settings.
using System;
using System.Collections.Generic;
using System.Reflection;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunGreyboxBuilder
{
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string ScenePath = "Assets/Scenes/Lundun.unity";
    const string PrototypePath = "Assets/Prototype.unity";
    const string GenDir = "Assets/World/Generated";
    const string MatDir = "Assets/World/Materials";
    const string MeshAssetPath = GenDir + "/LundunGreyboxMeshes.asset";
    const string MobileRPPath = "Assets/Settings/Mobile_RPAsset.asset";

    // ------------------------------------------------------------------ 0
    [MenuItem("Lundun/0  Create Lundun Scene (copy of Prototype)", false, 0)]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PrototypePath) == null)
        { Dialog("Can't find " + PrototypePath + "."); return; }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Lundun", "Replace " + ScenePath + " with a fresh copy of Prototype?\n\nPrototype itself is not changed.", "Replace", "Cancel"))
            return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // release the file before replacing it
        AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(PrototypePath, ScenePath)) { Dialog("Copy failed."); return; }
        AssetDatabase.Refresh();

        // Lundun becomes the scene the app starts in. Prototype stays in the list, switched off.
        SetStartScene();

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("[Lundun] Lundun scene created from Prototype and set as the start scene. Next: Lundun → 1  Build Greybox World.");
    }

    // ------------------------------------------------------------------ 1
    [MenuItem("Lundun/1  Build Greybox World", false, 1)]
    public static void BuildWorld()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first (Assets/Scenes/Lundun).\nThis menu never touches Prototype."); return; }
        LundunWorldData data = LoadData();
        if (data == null) return;

        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Working out roads, river and buildings…", 0.05f);
            GreyboxResult r = Generator.Build(data);

            GameObject old = GameObject.Find("World");
            if (old != null) Object.DestroyImmediate(old);
            EnsureFolder(GenDir); EnsureFolder(MatDir);
            AssetDatabase.DeleteAsset(MeshAssetPath);

            GameObject root = new GameObject("World");
            Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
            Dictionary<string, Material> mats = new Dictionary<string, Material>();
            Mesh container = null;
            int done = 0;
            foreach (MeshData md in r.meshes)
            {
                done++;
                if (md.tri.Count == 0) continue;
                EditorUtility.DisplayProgressBar("Lundun", "Building " + md.name, 0.1f + 0.75f * done / r.meshes.Count);
                Mesh mesh = ToMesh(md);
                if (container == null) { AssetDatabase.CreateAsset(mesh, MeshAssetPath); container = mesh; }
                else AssetDatabase.AddObjectToAsset(mesh, container);

                GameObject go = new GameObject(md.name);
                go.transform.SetParent(Group(root, groups, md.group), false);
                if (md.material == "ColliderOnly")
                {
                    // L6: invisible walking surfaces (the station stairs' ramps, the platform's back)
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    if (md.navArea >= 0) AddNavModifier(go, false, md.navArea);
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    continue;
                }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GetMaterial(mats, md.material);
                if (md.group == "Roads" || md.group == "Junctions" || md.group == "Ground" || md.group == "Parks" && md.material == "Park" || md.material == "Water" || md.material.StartsWith("LightPool"))
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                if (md.collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (md.navIgnore) AddNavModifier(go, true, -1);
                else if (md.navArea >= 0) AddNavModifier(go, false, md.navArea);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }

            foreach (Marker m in r.markers)
            {
                GameObject g = new GameObject(m.id);
                g.transform.SetParent(Group(root, groups, m.group), false);
                g.transform.SetPositionAndRotation(new Vector3((float)m.x, (float)m.y, (float)m.z), Quaternion.Euler(0, (float)m.yaw, 0));
            }

            foreach (Beacon b in r.beacons)
            {
                GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(cyl.GetComponent<Collider>());
                cyl.name = "Beacon_" + b.id;
                cyl.transform.SetParent(Group(root, groups, "Beacons"), false);
                cyl.transform.position = new Vector3((float)b.x, (float)b.height / 2f, (float)b.z);
                cyl.transform.localScale = new Vector3((float)b.radius * 2f, (float)b.height / 2f, (float)b.radius * 2f);
                MeshRenderer mr = cyl.GetComponent<MeshRenderer>();
                mr.sharedMaterial = GetMaterial(mats, BeaconMaterial(b.kind));
                mr.shadowCastingMode = ShadowCastingMode.Off;
                AddNavModifier(cyl, true, -1);
            }

            foreach (ColliderBox c in r.walls)
            {
                GameObject g = new GameObject(c.name);
                g.transform.SetParent(Group(root, groups, "Bounds"), false);
                g.transform.position = new Vector3((float)c.cx, (float)c.cy, (float)c.cz);
                g.AddComponent<BoxCollider>().size = new Vector3((float)c.sx, (float)c.sy, (float)c.sz);
                AddNavModifier(g, true, -1);
                GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
            }

            AssetDatabase.SaveAssets();
            EnsureNavigationSurface();
            List<Vector3> lampList = new List<Vector3>();
            foreach (LampInfo lp in r.lamps) lampList.Add(new Vector3((float)lp.x, (float)lp.y, (float)lp.z));
            SetupLighting(lampList);
            LundunLightingDistrictBuilder.RegisterLamps(r, false);      // L5: lamp colours by district
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("[Lundun] Greybox built. " + string.Join("\n", r.log.ToArray()) + "\nNext: Lundun → 2  Place Gameplay Objects.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Dialog("Build failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Lundun/Clear Greybox World", false, 50)]
    public static void ClearWorld()
    {
        if (SceneManager.GetActiveScene().name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        GameObject old = GameObject.Find("World");
        if (old != null) Object.DestroyImmediate(old);
        AssetDatabase.DeleteAsset(MeshAssetPath);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    // ------------------------------------------------------------------ 2
    static readonly string[] StayPut = { "World", "Navigation", "LundunRuntime", "PeckamStreet", "LawAndOrder", "VehicleSystem", "Canvas", "EventSystem", "Main Camera", "Directional Light", "Global Volume", "WastedVolume", "Image", "LundunLighting" };
    static readonly string[] SwitchOff = { "Plane", "NavMesh", "PoliceSpawns" };

    [MenuItem("Lundun/2  Place Gameplay Objects", false, 2)]
    public static void PlaceGameplay()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Transform world = FindRoot("World");
        if (world == null) { Dialog("Build the world first: Lundun → 1  Build Greybox World."); return; }
        LundunWorldData data = LoadData();
        if (data == null) return;

        Transform playerStart = Find(world, "SP_PLAYER_START"), kestrel = Find(world, "SP_KESTREL");
        Transform busted = Find(world, "SP_BUSTED"), wasted = Find(world, "SP_WASTED");
        Transform policeGroup = Find(world, "PoliceSpawns");
        if (playerStart == null || kestrel == null || busted == null || wasted == null || policeGroup == null)
        { Dialog("Spawn points missing under World. Run Lundun → 1  Build Greybox World again."); return; }

        // 1. The old high street moves, as one piece, to its slot in the city
        Transform street = FindRoot("PeckamStreet");
        if (street == null) street = new GameObject("PeckamStreet").transform;
        Vector3 streetPos = new Vector3(data.existingStreet.centreX, 0, data.existingStreet.centreZ);
        List<string> moved = new List<string>(), off = new List<string>();
        GameObject james = null, car = null;
        foreach (GameObject g in scene.GetRootGameObjects())
        {
            if (g.name == "James") { james = g; continue; }
            if (g.name == "Car") { car = g; continue; }
            if (Array.IndexOf(SwitchOff, g.name) >= 0) { if (g.activeSelf) { g.SetActive(false); off.Add(g.name); } continue; }
            if (Array.IndexOf(StayPut, g.name) >= 0 || g.name.StartsWith("Lundun") || g.transform == street) continue;
            if (g.GetComponent<RectTransform>() != null || g.GetComponent<Canvas>() != null || g.GetComponent<Camera>() != null
                || g.GetComponent<Light>() != null || g.GetComponent<Volume>() != null || HasComponent(g, "EventSystem")) continue;
            g.transform.SetParent(street, false);      // keeps its old position, now relative to the street's new spot
            moved.Add(g.name);
        }
        street.position = streetPos;

        // 2. James and his car go to their spawn points
        if (james != null)
        {
            CharacterController cc = james.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            james.transform.SetPositionAndRotation(new Vector3(playerStart.position.x, james.transform.position.y, playerStart.position.z), playerStart.rotation);
            if (cc != null) cc.enabled = true;
        }
        else Debug.LogWarning("[Lundun] No root object called 'James' — move the player to SP_PLAYER_START yourself.");
        if (car != null)
            car.transform.SetPositionAndRotation(new Vector3(kestrel.position.x, Mathf.Max(car.transform.position.y, 1f), kestrel.position.z), kestrel.rotation);
        else Debug.LogWarning("[Lundun] No root object called 'Car' — move it to SP_KESTREL yourself.");

        // 3. Wire respawn points and police spawn points
        int wired = 0;
        wired += SetRef("BustedSystem", "policeStation", busted) ? 1 : 0;
        wired += SetRef("WastedSystem", "hospital", wasted) ? 1 : 0;
        Component director = FindComponent("PoliceDirector");
        if (director != null)
        {
            SerializedObject so = new SerializedObject(director);
            SerializedProperty arr = so.FindProperty("spawnPoints");
            if (arr != null)
            {
                arr.arraySize = policeGroup.childCount;
                for (int i = 0; i < policeGroup.childCount; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = policeGroup.GetChild(i);
                wired++;
            }
            SerializedProperty min = so.FindProperty("minSpawnDistance");
            if (min != null) min.floatValue = 70f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else Debug.LogWarning("[Lundun] PoliceDirector not found — police spawn points not wired.");

        // 4. Runtime helper: 30 fps, water = WASTED, lost car comes back
        Transform rt = FindRoot("LundunRuntime");
        if (rt == null) rt = new GameObject("LundunRuntime").transform;
        LundunWorldRuntime runtime = rt.GetComponent<LundunWorldRuntime>();
        if (runtime == null) runtime = rt.gameObject.AddComponent<LundunWorldRuntime>();
        SerializedObject rso = new SerializedObject(runtime);
        if (car != null) rso.FindProperty("playerCar").objectReferenceValue = car.transform;
        rso.FindProperty("carRespawn").objectReferenceValue = kestrel;
        rso.ApplyModifiedPropertiesWithoutUndo();

        // 5. Camera sees far enough for the fog
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.farClipPlane = data.camera.farClip > 0 ? data.camera.farClip : 450f;
            if (james != null) cam.transform.position = james.transform.position + new Vector3(0, 5, 8);
            EditorUtility.SetDirty(cam);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (james != null) Selection.activeGameObject = james;
        Debug.Log("[Lundun] Gameplay placed.\n  Moved into PeckamStreet: " + string.Join(", ", moved.ToArray()) +
                  "\n  Switched off: " + (off.Count > 0 ? string.Join(", ", off.ToArray()) : "nothing") +
                  "\n  References wired: " + wired + "/3, police spawn points: " + policeGroup.childCount +
                  "\nNext: Lundun → 3  Bake Navigation.");
    }

    [MenuItem("Lundun/Fix: Tag Pavements for Pedestrians", false, 47)]
    public static void TagPavements()
    {
        Scene scene = SceneManager.GetActiveScene();
        Transform world = FindRoot("World");
        if (scene.name != "Lundun" || world == null) { Dialog("Open the Lundun scene (with the world built) first."); return; }
        int n = 0;
        foreach (Transform t in world.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Pavements")) continue;
            Component mod = null;
            Type mt = FindType("Unity.AI.Navigation.NavMeshModifier");
            if (mt != null) mod = t.GetComponent(mt);
            if (mod == null) { AddNavModifier(t.gameObject, false, Generator.PavementArea); n++; continue; }
            SerializedObject so = new SerializedObject(mod);
            SetBool(so, "m_IgnoreFromBuild", false);
            SetBool(so, "m_OverrideArea", true);
            SetInt(so, "m_Area", Generator.PavementArea);
            so.ApplyModifiedPropertiesWithoutUndo();
            n++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Lundun] " + n + " pavement pieces tagged as pavement (NavMesh area 4). Now run Lundun → 3  Bake Navigation.");
    }

    [MenuItem("Lundun/Update Spawn Points from Data", false, 46)]
    public static void UpdateSpawns()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Transform world = FindRoot("World");
        LundunWorldData data = LoadData();
        if (world == null || data == null) { Dialog("Build the world first."); return; }
        int moved = 0;
        foreach (LwSpawn sp in data.spawns)
        {
            Transform t = Find(world, sp.id);
            if (t == null) continue;
            t.SetPositionAndRotation(new Vector3(sp.x, t.position.y, sp.z), Quaternion.Euler(0, sp.yaw, 0));
            moved++;
        }
        Transform kestrel = Find(world, "SP_KESTREL"), car = FindRoot("Car");
        if (kestrel != null && car != null)
        {
            Rigidbody rb = car.GetComponent<Rigidbody>();
            car.SetPositionAndRotation(new Vector3(kestrel.position.x, Mathf.Max(car.position.y, 1f), kestrel.position.z), kestrel.rotation);
            if (rb != null) rb.position = car.position;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Lundun] " + moved + " spawn points updated" + (car != null ? "; car moved to SP_KESTREL." : "."));
    }

    // ------------------------------------------------------------------ 3
    [MenuItem("Lundun/3  Bake Navigation", false, 3)]
    public static void BakeNavigation()
    {
        if (SceneManager.GetActiveScene().name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Component surface = EnsureNavigationSurface();
        if (surface == null) return;
        Selection.activeGameObject = surface.gameObject;
        Component x = EnsureExpansionSurface();                       // E1: the expansions bake on their own (null without any)
        Bake(x != null ? new Object[] { surface, x } : new Object[] { surface });
    }

    /// E1: rebakes just the expansion areas (much quicker than the whole city).
    [MenuItem("Lundun/3b Bake Navigation – Expansions only", false, 3)]
    public static void BakeExpansionNavigation()
    {
        if (SceneManager.GetActiveScene().name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        if (EnsureNavigationSurface() == null) return;                 // (keeps the city's surface out of the expansions)
        Component x = EnsureExpansionSurface();
        if (x == null) { Dialog("There are no expansion areas yet."); return; }
        Selection.activeGameObject = x.gameObject;
        Bake(new Object[] { x });
    }

    static void Bake(Object[] surfaces)
    {
        try
        {
            Type mgr = FindType("Unity.AI.Navigation.Editor.NavMeshAssetManager");
            PropertyInfo inst = mgr == null ? null : mgr.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            object instance = inst == null ? null : inst.GetValue(null, null);
            MethodInfo start = mgr == null ? null : mgr.GetMethod("StartBakingSurfaces", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (instance != null && start != null)
            {
                start.Invoke(instance, new object[] { surfaces });
                Debug.Log("[Lundun] NavMesh bake started for " + surfaces.Length + " area(s) (progress bar bottom-right). Takes a minute or two. Then press Play.");
                return;
            }
        }
        catch (Exception e) { Debug.LogWarning("[Lundun] Automatic bake unavailable: " + e.Message); }
        Dialog("The Navigation object is selected.\nIn the Inspector, on Nav Mesh Surface, click Bake.");
    }

    // ------------------------------------------------------------------ 4
    [MenuItem("Lundun/4  iPhone Settings", false, 4)]
    public static void IPhoneSettings()
    {
        Object rp = AssetDatabase.LoadAssetAtPath<Object>(MobileRPPath);
        if (rp == null) { Dialog("Can't find " + MobileRPPath + "."); return; }
        SerializedObject so = new SerializedObject(rp);
        SetFloat(so, "m_RenderScale", 0.75f);
        SetFloat(so, "m_ShadowDistance", 40f);
        SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);   // matches the 8 real lamp lights
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);
        AssetDatabase.SaveAssets();
        Debug.Log("[Lundun] Mobile URP: render scale 0.75, shadow distance 40 m, 8 lights per object. 30 fps is set by LundunRuntime when the game starts.");
    }

    // ------------------------------------------------------------------ lighting
    const string LightingDir = "Assets/World/Lighting";

    [MenuItem("Lundun/Lighting/Night", false, 20)] public static void Night() { UsePreset("Night"); }
    [MenuItem("Lundun/Lighting/Dawn", false, 21)] public static void Dawn() { UsePreset("Dawn"); }
    [MenuItem("Lundun/Lighting/Day", false, 22)] public static void Day() { UsePreset("Day"); }
    [MenuItem("Lundun/Lighting/Rebuild Lighting Rig", false, 40)] public static void RebuildRig()
    {
        if (SceneManager.GetActiveScene().name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        SetupLighting(null);
    }

    static void UsePreset(string which)
    {
        Transform t = FindRoot("LundunLighting");
        LundunLighting ll = t != null ? t.GetComponent<LundunLighting>() : null;
        if (ll == null) { Dialog("No LundunLighting object yet. Run Lundun → 1  Build Greybox World."); return; }
        ll.preset = GetPreset(which);
        ll.Apply(ll.preset.look);
        EditorUtility.SetDirty(ll);
        Selection.activeObject = ll.preset;      // shows the preset in the Inspector, ready to tweak
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static LundunLightingPreset GetPreset(string which)
    {
        EnsureFolder(LightingDir);
        string path = LightingDir + "/" + which + ".asset";
        LundunLightingPreset p = AssetDatabase.LoadAssetAtPath<LundunLightingPreset>(path);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<LundunLightingPreset>();
            p.look = LightingLook.Default(which);
            AssetDatabase.CreateAsset(p, path);
        }
        return p;
    }

    /// Creates/refreshes the lighting rig: presets, sky, camera grade, LundunLighting + lamp light pool.
    /// lamps = lamp head positions from the generator (null = keep the ones already stored).
    static void SetupLighting(List<Vector3> lamps)
    {
        GetPreset("Dawn"); GetPreset("Day");
        LundunLightingPreset night = GetPreset("Night");
        Dictionary<string, Material> mats = new Dictionary<string, Material>();

        // directional light (moon/sun)
        Light sun = null;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun == null) { sun = new GameObject("Directional Light").AddComponent<Light>(); sun.type = LightType.Directional; }
        sun.shadows = LightShadows.Soft;

        // camera: sky behind everything, far enough for the fog
        Camera cam = Camera.main;
        if (cam != null) { cam.clearFlags = CameraClearFlags.Skybox; cam.farClipPlane = 450f; EditorUtility.SetDirty(cam); }

        // Lundun's own camera grade (the old SampleScene profile used ACES, which crushes the dark)
        Volume vol = null;
        foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            if (v.gameObject.name == "Global Volume") { vol = v; break; }
        if (vol == null) { vol = new GameObject("Global Volume").AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 0; }
        vol.sharedProfile = GradeProfile();
        EditorUtility.SetDirty(vol);

        // rig object
        Transform rt = FindRoot("LundunLighting");
        GameObject rig = rt != null ? rt.gameObject : new GameObject("LundunLighting");
        LundunLampLights lampLights = rig.GetComponent<LundunLampLights>();
        if (lampLights == null) lampLights = rig.AddComponent<LundunLampLights>();
        if (lamps != null) { lampLights.SetLamps(lamps.ToArray()); EditorUtility.SetDirty(lampLights); }
        LundunLighting ll = rig.GetComponent<LundunLighting>();
        if (ll == null) ll = rig.AddComponent<LundunLighting>();

        List<Material> windows = new List<Material>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && m.name.StartsWith("Bld_")) windows.Add(m);
        }
        SerializedObject so = new SerializedObject(ll);
        so.FindProperty("preset").objectReferenceValue = ll.preset != null ? ll.preset : night;
        so.FindProperty("sun").objectReferenceValue = sun;
        so.FindProperty("grade").objectReferenceValue = vol;
        so.FindProperty("sky").objectReferenceValue = GetMaterial(mats, "Sky");
        so.FindProperty("lampHeadMaterial").objectReferenceValue = GetMaterial(mats, "LampHead");
        so.FindProperty("lightPoolMaterial").objectReferenceValue = GetMaterial(mats, "LightPool");
        so.FindProperty("lampLights").objectReferenceValue = lampLights;
        SerializedProperty arr = so.FindProperty("windowMaterials");
        arr.arraySize = windows.Count;
        for (int i = 0; i < windows.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = windows[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        ll.Apply(ll.preset.look);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static VolumeProfile GradeProfile()
    {
        EnsureFolder(LightingDir);
        string path = LightingDir + "/LundunGrade.asset";
        VolumeProfile p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (p != null) return p;
        p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, path);

        Tonemapping tm = p.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.Neutral);
        ColorAdjustments ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.6f); ca.contrast.Override(8f); ca.saturation.Override(-8f);
        Bloom bl = p.Add<Bloom>(true);
        bl.intensity.Override(0.6f); bl.threshold.Override(0.95f); bl.scatter.Override(0.6f);
        Vignette vg = p.Add<Vignette>(true);
        vg.intensity.Override(0.18f);
        foreach (VolumeComponent c in p.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, p); }
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        return p;
    }

    /// Soft round glow used by the fake lamp light on the ground.
    static Texture2D PoolTexture()
    {
        string path = GenDir + "/LightPoolGlow.asset";
        Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (t != null) return t;
        const int S = 128;
        t = new Texture2D(S, S, TextureFormat.RGBA32, true);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float v = Mathf.Clamp01(1f - d);
                v = v * v * (3f - 2f * v);           // smooth edge
                t.SetPixel(x, y, new Color(v, v, v, v));
            }
        t.Apply(true);
        t.wrapMode = TextureWrapMode.Clamp;
        t.name = "LightPoolGlow";
        AssetDatabase.CreateAsset(t, path);
        return t;
    }

    // ------------------------------------------------------------------ tidy
    [MenuItem("Lundun/Tidy: Move Scene to Assets/Scenes + Add to Build", false, 45)]
    public static void TidyScene()
    {
        Scene s = SceneManager.GetActiveScene();
        if (s.name != "Lundun") { Dialog("Open your Lundun scene (the one with the city) first."); return; }
        EditorSceneManager.SaveScene(s);
        string cur = s.path;
        if (cur != ScenePath)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                long size = new System.IO.FileInfo(ScenePath).Length;
                if (size > 100000) { Dialog("There's already a full Lundun scene at " + ScenePath + ". Not touching anything — tell Claude."); return; }
                AssetDatabase.DeleteAsset(ScenePath);          // the old empty one
            }
            string err = AssetDatabase.MoveAsset(cur, ScenePath);
            if (!string.IsNullOrEmpty(err)) { Dialog("Couldn't move the scene: " + err); return; }
            string oldData = cur.Substring(0, cur.Length - ".unity".Length);    // NavMesh data folder next to the scene
            string newData = ScenePath.Substring(0, ScenePath.Length - ".unity".Length);
            if (AssetDatabase.IsValidFolder(oldData) && !AssetDatabase.IsValidFolder(newData)) AssetDatabase.MoveAsset(oldData, newData);
            string parent = System.IO.Path.GetDirectoryName(cur).Replace('\\', '/');
            if (parent != "Assets/Scenes" && AssetDatabase.IsValidFolder(parent) && AssetDatabase.FindAssets("", new[] { parent }).Length == 0)
                AssetDatabase.DeleteAsset(parent);
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        SetStartScene();
        Debug.Log("[Lundun] Scene is at " + ScenePath + " and is the first scene in the build. iPhone builds now open Lundun.");
    }

    static void SetStartScene()
    {
        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>();
        list.Add(new EditorBuildSettingsScene(ScenePath, true));
        foreach (EditorBuildSettingsScene sc in EditorBuildSettings.scenes)
            if (sc.path != ScenePath && AssetDatabase.LoadAssetAtPath<SceneAsset>(sc.path) != null) list.Add(new EditorBuildSettingsScene(sc.path, false));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ------------------------------------------------------------------ helpers
    static void Dialog(string msg) { EditorUtility.DisplayDialog("Lundun", msg, "OK"); }

    static LundunWorldData LoadData()
    {
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { Dialog("Can't find " + JsonPath + ".\nCopy LundunWorld.json into Assets/World."); return null; }
        LundunWorldData d = LundunExpansionBuilder.Parse(json.text);
        if (d == null || d.roads == null || d.roads.Length == 0 || d.junctions == null) { Dialog("LundunWorld.json didn't load (no roads)."); return null; }
        if (d.schemaVersion != "1.1") Debug.LogWarning("[Lundun] World data schema " + d.schemaVersion + " (expected 1.1).");
        return d;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static Transform Group(GameObject root, Dictionary<string, Transform> groups, string name)
    {
        Transform t;
        if (groups.TryGetValue(name, out t)) return t;
        t = new GameObject(name).transform;
        t.SetParent(root.transform, false);
        groups[name] = t;
        return t;
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
        Mesh mesh = new Mesh();
        mesh.name = md.name;
        mesh.indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(md.tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ----- materials (created once; edit the colours in Assets/World/Materials freely)
    static string BeaconMaterial(string kind)
    {
        switch (kind)
        {
            case "safehouse": return "Beacon_Safehouse";
            case "police": return "Beacon_Police";
            case "hospital": return "Beacon_Hospital";
            case "landmark": return "Beacon_Landmark";
            default: return "Beacon_Mission";
        }
    }

    /// L5: the shared world materials (made the same way as menu 1 makes them), for other builders.
    public static Material WorldMaterial(string name) { return GetMaterial(new Dictionary<string, Material>(), name); }

    static Material GetMaterial(Dictionary<string, Material> cache, string name)
    {
        Material m;
        if (cache.TryGetValue(name, out m)) return m;
        if (name.StartsWith("Bld_Kit")) { m = LundunBuildingKitBuilder.MaterialFor(name); cache[name] = m; return m; }   // L1: the building kit
        string path = MatDir + "/" + name + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            bool unlit = name.StartsWith("Beacon_");
            Shader sh = Shader.Find(name == "Sky" ? "Lundun/SkyGradient" : name.StartsWith("LightPool") ? "Lundun/LightPool" : unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            m.name = name;
            Style(m, name);
            AssetDatabase.CreateAsset(m, path);
        }
        cache[name] = m;
        return m;
    }

    static void Style(Material m, string name)
    {
        Color c = new Color(0.5f, 0.5f, 0.5f); float smooth = 0.2f;
        switch (name)
        {
            case "Ground": c = new Color(0.20f, 0.26f, 0.18f); smooth = 0.05f; break;
            case "Road": c = new Color(0.16f, 0.16f, 0.18f); smooth = 0.35f; break;
            case "Pavement": c = new Color(0.45f, 0.45f, 0.46f); smooth = 0.15f; break;
            case "Marking": c = new Color(0.92f, 0.92f, 0.88f); Emit(m, new Color(0.12f, 0.12f, 0.11f)); break;
            case "Water": c = new Color(0.06f, 0.12f, 0.20f); smooth = 0.92f; break;
            case "Wall": c = new Color(0.42f, 0.38f, 0.33f); break;
            case "Concrete": c = new Color(0.50f, 0.49f, 0.47f); break;
            case "Parapet": c = new Color(0.58f, 0.56f, 0.52f); break;
            case "LampPole": c = new Color(0.10f, 0.11f, 0.12f); smooth = 0.4f; break;
            case "LampHead": c = new Color(1f, 0.9f, 0.7f); Emit(m, new Color(1f, 0.82f, 0.5f) * 2.5f); break;
            // L5: lamps by district
            case "LampPole_Concrete": c = new Color(0.52f, 0.51f, 0.49f); smooth = 0.1f; break;
            case "LampPole_Heritage": c = new Color(0.04f, 0.045f, 0.05f); smooth = 0.6f; break;
            case "LampPole_Steel": c = new Color(0.55f, 0.58f, 0.6f); smooth = 0.65f; break;
            case "LampHead_Heritage": c = new Color(1f, 0.95f, 0.85f); smooth = 0.9f; Emit(m, LundunLampColours.Heritage * 2.5f); break;
            case "LampHead_LED": c = new Color(0.9f, 0.95f, 1f); Emit(m, LundunLampColours.LED * 2.5f); break;
            case "Park": c = new Color(0.17f, 0.36f, 0.16f); smooth = 0.05f; break;
            case "RailBank": c = new Color(0.22f, 0.28f, 0.15f); smooth = 0.04f; break;     // L6: the railway embankment
            case "WheelLights": c = new Color(0.9f, 0.95f, 1f); Emit(m, new Color(0.82f, 0.9f, 1f) * 2.6f); break;   // E7: the Wheel's lights (LundunWheel dims them by day)
            case "Trunk": c = new Color(0.30f, 0.22f, 0.14f); break;
            case "Leaves": c = new Color(0.12f, 0.30f, 0.13f); smooth = 0.05f; break;
            case "Hedge": c = new Color(0.08f, 0.20f, 0.09f); smooth = 0.05f; break;
            case "Beacon_Safehouse": c = new Color(0.2f, 1f, 0.5f, 1f); break;
            case "Beacon_Police": c = new Color(0.25f, 0.5f, 1f, 1f); break;
            case "Beacon_Hospital": c = new Color(1f, 0.3f, 0.3f, 1f); break;
            case "Beacon_Landmark": c = new Color(1f, 0.85f, 0.3f, 1f); break;
            case "Beacon_Mission": c = new Color(1f, 0.55f, 0.1f, 1f); break;
        }
        if (name == "Sky") return;                 // colours come from the lighting preset
        if (name.StartsWith("LightPool"))
        {
            m.SetTexture("_MainTex", PoolTexture());
            m.SetColor("_Color", LundunLampColours.Pool(name));
            m.SetFloat("_Intensity", 0.7f);
            return;
        }
        if (name.StartsWith("Bld_"))
        {
            c = BuildingTint(name);
            Texture2D albedo, glow;
            WindowTextures(out albedo, out glow);
            m.SetTexture("_BaseMap", albedo);
            Emit(m, Color.white * 1.3f);
            m.SetTexture("_EmissionMap", glow);
            smooth = 0.25f;
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        m.enableInstancing = true;
    }

    static void Emit(Material m, Color e)
    {
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", e);
    }

    static Color BuildingTint(string name)
    {
        string[] p = name.Split('_');           // Bld_<district>_<variant>
        string d = p.Length > 1 ? p[1] : "";
        int v = p.Length > 2 ? int.Parse(p[p.Length - 1]) : 0;
        Color[] set;
        if (name.Contains("CANARY_PEN")) set = new[] { new Color(0.55f, 0.62f, 0.70f), new Color(0.45f, 0.52f, 0.60f), new Color(0.62f, 0.66f, 0.70f) };
        else if (d == "PECKAM") set = new[] { new Color(0.55f, 0.36f, 0.28f), new Color(0.62f, 0.45f, 0.33f), new Color(0.48f, 0.32f, 0.27f) };
        else if (d == "KNIGHT") set = new[] { new Color(0.78f, 0.75f, 0.68f), new Color(0.66f, 0.66f, 0.68f), new Color(0.72f, 0.68f, 0.60f) };
        else if (d == "GILMOR") set = new[] { new Color(0.55f, 0.53f, 0.50f), new Color(0.50f, 0.46f, 0.42f), new Color(0.60f, 0.58f, 0.54f) };
        else set = new[] { new Color(0.45f, 0.40f, 0.33f), new Color(0.38f, 0.40f, 0.42f), new Color(0.50f, 0.36f, 0.28f) };   // Canary Quay warehouses
        return set[Mathf.Clamp(v, 0, 2)];
    }

    static Texture2D cachedAlbedo, cachedGlow;

    /// 8 × 8 bays (each 4 m wide, 3.5 m tall). Albedo: white wall, dark glass. Emission: ~35 % of windows lit.
    static void WindowTextures(out Texture2D albedo, out Texture2D glow)
    {
        string ap = GenDir + "/WindowsAlbedo.asset", gp = GenDir + "/WindowsGlow.asset";
        if (cachedAlbedo == null) cachedAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ap);
        if (cachedGlow == null) cachedGlow = AssetDatabase.LoadAssetAtPath<Texture2D>(gp);
        if (cachedAlbedo == null || cachedGlow == null)
        {
            const int S = 256, B = 32;
            Texture2D a = new Texture2D(S, S, TextureFormat.RGBA32, true), g = new Texture2D(S, S, TextureFormat.RGBA32, true);
            System.Random rnd = new System.Random(7);
            Color wall = Color.white, glass = new Color(0.16f, 0.18f, 0.22f), dark = Color.black;
            Color[] warm = { new Color(1f, 0.85f, 0.55f), new Color(1f, 0.95f, 0.8f), new Color(0.75f, 0.85f, 1f) };
            for (int by = 0; by < S / B; by++)
                for (int bx = 0; bx < S / B; bx++)
                {
                    bool lit = rnd.NextDouble() < 0.35;
                    Color lc = warm[rnd.Next(warm.Length)] * (0.6f + (float)rnd.NextDouble() * 0.4f);
                    for (int y = 0; y < B; y++)
                        for (int x = 0; x < B; x++)
                        {
                            bool win = x >= 7 && x < 25 && y >= 8 && y < 26;
                            a.SetPixel(bx * B + x, by * B + y, win ? glass : wall);
                            g.SetPixel(bx * B + x, by * B + y, win && lit ? lc : dark);
                        }
                }
            a.Apply(true); g.Apply(true);
            a.wrapMode = g.wrapMode = TextureWrapMode.Repeat;
            a.filterMode = g.filterMode = FilterMode.Bilinear;
            a.name = "WindowsAlbedo"; g.name = "WindowsGlow";
            AssetDatabase.CreateAsset(a, ap); AssetDatabase.CreateAsset(g, gp);
            cachedAlbedo = a; cachedGlow = g;
        }
        albedo = cachedAlbedo; glow = cachedGlow;
    }

    // ----- navigation (com.unity.ai.navigation, accessed by name so this file compiles even if the package moves)
    static Type FindType(string fullName)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = asm.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }

    static bool warnedNav;

    /// L6: for other builders.
    public static void NavModifier(GameObject go, bool ignore, int area) { AddNavModifier(go, ignore, area); }

    static void AddNavModifier(GameObject go, bool ignore, int area)
    {
        Type t = FindType("Unity.AI.Navigation.NavMeshModifier");
        if (t == null) { if (!warnedNav) Debug.LogWarning("[Lundun] AI Navigation package not found."); warnedNav = true; return; }
        Component c = go.AddComponent(t);
        SerializedObject so = new SerializedObject(c);
        SetBool(so, "m_IgnoreFromBuild", ignore);
        if (area >= 0) { SetBool(so, "m_OverrideArea", true); SetInt(so, "m_Area", area); }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static Component EnsureNavigationSurface()
    {
        Type t = FindType("Unity.AI.Navigation.NavMeshSurface");
        if (t == null) { Dialog("AI Navigation package not found (com.unity.ai.navigation)."); return null; }
        Transform navT = FindRoot("Navigation");
        GameObject nav = navT != null ? navT.gameObject : new GameObject("Navigation");
        Component s = nav.GetComponent(t);
        if (s == null) s = nav.AddComponent(t);
        SerializedObject so = new SerializedObject(s);
        int mask = ~0;
        foreach (string layer in new[] { "Player", "Vehicle", "NPC", "Bumper", "UI" })
        {
            int l = LayerMask.NameToLayer(layer);
            if (l >= 0) mask &= ~(1 << l);
        }
        // E1: with expansion areas the city keeps to its side of the seam (they bake separately: Lundun → 3b)
        if (HasExpansions()) SetVolume(so, nav.transform, new Vector3(-500f, -40f, NavSeamZ), new Vector3(2200f, 400f, 2400f));
        else SetInt(so, "m_CollectObjects", 0);          // All
        SetInt(so, "m_UseGeometry", 1);             // Physics colliders
        SetInt(so, "m_LayerMask", mask);
        SetBool(so, "m_OverrideVoxelSize", true);
        SetFloat(so, "m_VoxelSize", 0.25f);
        SetBool(so, "m_OverrideTileSize", true);
        SetInt(so, "m_TileSize", 256);
        so.ApplyModifiedPropertiesWithoutUndo();
        return s;
    }

    /// E1: where the city's NavMesh stops and the expansions' starts (just south of the Ring). Roads crossing it are
    /// joined by NavMesh links (made by the expansion menus).
    public const float NavSeamZ = 22f;

    /// E1: the expansion areas' own NavMesh surface ("Navigation_Expansions"), everything south of the seam. Null if
    /// there are no expansion areas.
    static Component EnsureExpansionSurface()
    {
        if (!HasExpansions()) return null;
        Type t = FindType("Unity.AI.Navigation.NavMeshSurface");
        if (t == null) return null;
        Component city = EnsureNavigationSurface();
        Transform navT = FindRoot("Navigation_Expansions");
        GameObject nav = navT != null ? navT.gameObject : new GameObject("Navigation_Expansions");
        nav.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Component s = nav.GetComponent(t);
        if (s == null) s = nav.AddComponent(t);
        SerializedObject so = new SerializedObject(s);
        if (city != null)
        {
            SerializedProperty cm = new SerializedObject(city).FindProperty("m_LayerMask");
            if (cm != null) SetInt(so, "m_LayerMask", cm.intValue);
        }
        SetVolume(so, nav.transform, new Vector3(-500f, -40f, -1400f), new Vector3(2200f, 400f, NavSeamZ));
        SetInt(so, "m_UseGeometry", 1);
        SetBool(so, "m_OverrideVoxelSize", true);
        SetFloat(so, "m_VoxelSize", 0.25f);
        SetBool(so, "m_OverrideTileSize", true);
        SetInt(so, "m_TileSize", 256);
        so.ApplyModifiedPropertiesWithoutUndo();
        return s;
    }

    /// Collect by volume: the box from min to max (world space).
    static void SetVolume(SerializedObject so, Transform t, Vector3 min, Vector3 max)
    {
        SetInt(so, "m_CollectObjects", 1);           // Volume
        Vector3 a = t.InverseTransformPoint(min), b = t.InverseTransformPoint(max);
        Vector3 lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
        SerializedProperty c = so.FindProperty("m_Center"), z = so.FindProperty("m_Size");
        if (c != null) c.vector3Value = (lo + hi) * 0.5f;
        if (z != null) z.vector3Value = hi - lo;
    }

    /// Are there expansion areas (roads in LundunExpansion.json)?
    public static bool HasExpansions()
    {
        TextAsset x = AssetDatabase.LoadAssetAtPath<TextAsset>(LundunWorldFiles.ExpansionPath);
        if (x == null) return false;
        LundunExpansionData d = JsonUtility.FromJson<LundunExpansionData>(x.text);
        return d != null && d.roads != null && d.roads.Length > 0;
    }

    static void SetInt(SerializedObject so, string p, int v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.intValue = v; else Debug.LogWarning("[Lundun] Property not found: " + p); }
    static void SetBool(SerializedObject so, string p, bool v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.boolValue = v; else Debug.LogWarning("[Lundun] Property not found: " + p); }
    static void SetFloat(SerializedObject so, string p, float v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.floatValue = v; else Debug.LogWarning("[Lundun] Property not found: " + p); }

    // ----- scene lookups
    static Transform FindRoot(string name)
    {
        foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == name) return g.transform;
        return null;
    }

    static Transform Find(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    static bool HasComponent(GameObject g, string typeName)
    {
        foreach (Component c in g.GetComponents<Component>()) if (c != null && c.GetType().Name == typeName) return true;
        return false;
    }

    static Component FindComponent(string typeName)
    {
        foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Component c in g.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == typeName) return c;
        return null;
    }

    static bool SetRef(string typeName, string field, Object value)
    {
        Component c = FindComponent(typeName);
        if (c == null) { Debug.LogWarning("[Lundun] " + typeName + " not found in the scene."); return false; }
        SerializedObject so = new SerializedObject(c);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning("[Lundun] " + typeName + "." + field + " not found."); return false; }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }
}
