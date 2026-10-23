// Lundun → 26  Expansion Framework (E0)
// Gets the city ready to grow, without changing anything already built:
// 1. Assets/World/LundunExpansion.json: the expansions' own world file (new areas go here, never into LundunWorld.json).
//    For now it only makes the playable area bigger: south to z −950 (Brixtun and the retail park) and north to
//    z 1900 (the royal quarter). Every menu reads both files the same way (Parse below).
// 2. rebuilds the parts of World that depend on the size of the map: the ground, the hedge and walls round the edge,
//    and the railway (which now runs on past the new south edge, so the train still leaves the map)
// 3. the train, the dressing (the railway fences follow the line) and the map picture (it grows with the world)
// 4. LundunMapRegistry: one list of places every expansion adds to (the GPS, minimap, full map and PLACES read it).
//    Rye Lane station moves onto it.
// 5. LundunPerformance: detail streaming (LundunStreaming) and a performance readout (LundunPerfHud, off until you tick Show)
// Afterwards: Lundun → 3 Bake Navigation, then Cmd+S.
using System.Collections.Generic;
using System.IO;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunExpansionBuilder
{
    const string GenDir = "Assets/World/Generated";

    /// The expanded playable area. The original city is x −120…1720, z −120…1320 (walls); the ground runs 180 m further.
    static readonly LwBounds Expanded = new LwBounds
    {
        wallMinX = -120, wallMaxX = 1720, wallMinZ = -950, wallMaxZ = 1900,
        groundMinX = -300, groundMaxX = 1900, groundMinZ = -1130, groundMaxZ = 2080
    };

    /// Every menu reads the world through here: the base world, with the expansion's bounds (and its areas kept aside).
    public static LundunWorldData Parse(string baseJson)
    {
        TextAsset x = AssetDatabase.LoadAssetAtPath<TextAsset>(LundunWorldFiles.ExpansionPath);
        return LundunWorldFiles.Parse(baseJson, x != null ? x.text : null);
    }

    /// Adds or replaces a place on the map (stations, landmarks, shops…). Makes the registry if there isn't one.
    public static void Register(string id, string name, string kind, Vector3 position, bool label)
    {
        LundunMapRegistry reg = Object.FindAnyObjectByType<LundunMapRegistry>(FindObjectsInactive.Include);
        if (reg == null) reg = new GameObject("LundunMapRegistry").AddComponent<LundunMapRegistry>();
        reg.Set(id, name, kind, position, label);
        EditorUtility.SetDirty(reg);
    }

    [MenuItem("Lundun/26  Expansion Framework (E0)", false, 32)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { EditorUtility.DisplayDialog("Lundun", "Open the Lundun scene first.", "OK"); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(LundunWorldFiles.BasePath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + LundunWorldFiles.BasePath, "OK"); return; }
        GameObject worldRoot = GameObject.Find("World");
        if (worldRoot == null) { EditorUtility.DisplayDialog("Lundun", "No 'World' object in the scene. Run Lundun → 1 Build Greybox World first.", "OK"); return; }

        string log;
        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Expansion file…", 0.05f);
            bool made = EnsureExpansionFile();
            TextAsset xj = AssetDatabase.LoadAssetAtPath<TextAsset>(LundunWorldFiles.ExpansionPath);

            EditorUtility.DisplayProgressBar("Lundun", "Growing the ground and moving the edge…", 0.2f);
            LundunWorldData data = Parse(json.text);
            GreyboxResult world = Generator.Build(data);
            int pieces = RefreshGroups(worldRoot, world, new[] { "Ground", "Bounds", "Railway" });

            EditorUtility.DisplayProgressBar("Lundun", "The train…", 0.4f);
            LundunRailwayBuilder.BuildTrain();

            // the map registry, with Rye Lane station on it
            double[] way = Generator.RailStationEntrance();
            if (way != null) Register("RAIL_RYE_LANE", LundunTrain.StationName, "rail", new Vector3((float)way[0], (float)way[1], (float)way[2]), false);

            // the game reads the expansions too (map labels, the GPS road graph)
            LundunNavigation nav = Object.FindAnyObjectByType<LundunNavigation>(FindObjectsInactive.Include);
            if (nav != null)
            {
                SerializedObject so = new SerializedObject(nav);
                SerializedProperty p = so.FindProperty("expansionJson");
                if (p != null) { p.objectReferenceValue = xj; so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            // performance: detail streaming and the readout
            GameObject perf = GameObject.Find("LundunPerformance");
            if (perf == null) perf = new GameObject("LundunPerformance");
            if (perf.GetComponent<LundunStreaming>() == null) perf.AddComponent<LundunStreaming>();
            if (perf.GetComponent<LundunPerfHud>() == null) perf.AddComponent<LundunPerfHud>();

            LwBounds b = data.worldBounds;
            log = "[Lundun] E0 expansion framework: " + (made ? "made " : "using ") + LundunWorldFiles.ExpansionPath +
                  "; playable area now x " + b.wallMinX + "…" + b.wallMaxX + ", z " + b.wallMinZ + "…" + b.wallMaxZ +
                  " (" + ((b.wallMaxX - b.wallMinX) / 1000f).ToString("0.0") + " × " + ((b.wallMaxZ - b.wallMinZ) / 1000f).ToString("0.0") + " km); " +
                  pieces + " ground, edge and railway pieces rebuilt.";
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Lundun", "Expansion framework failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.", "OK");
            return;
        }
        EditorUtility.ClearProgressBar();

        LundunDressingBuilder.Build();             // the railway's fences follow the longer line
        LundunMapSharpBuilder.Build();             // the map picture grows with the world
        LundunLightingDistrictBuilder.ApplyLook();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log(log + "\nNext: Lundun → 3 Bake Navigation, then Cmd+S. (LundunPerformance → LundunPerfHud → Show for the fps readout.)");
    }

    static bool EnsureExpansionFile()
    {
        if (File.Exists(LundunWorldFiles.ExpansionPath)) return false;
        LundunExpansionData x = new LundunExpansionData { schemaVersion = "1.0", worldBounds = Expanded };
        File.WriteAllText(LundunWorldFiles.ExpansionPath, JsonUtility.ToJson(x, true));
        AssetDatabase.ImportAsset(LundunWorldFiles.ExpansionPath);
        return true;
    }

    /// Rebuilds some of World's groups from a fresh Generator.Build, the same way menu 1 makes them
    /// (materials, colliders, NavMesh settings, shadows, static flags). The rest of World is left alone.
    public static int RefreshGroups(GameObject worldRoot, GreyboxResult world, string[] groups)
    {
        int n = 0;
        foreach (string group in groups)
        {
            Transform old = worldRoot.transform.Find(group);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform root = new GameObject(group).transform;
            root.SetParent(worldRoot.transform, false);
            string path = GenDir + "/Group_" + group + ".asset";
            AssetDatabase.DeleteAsset(path);
            Mesh container = null;
            foreach (MeshData md in world.meshes)
            {
                if (md.group != group || md.tri.Count == 0) continue;
                Mesh mesh = LundunLightingDistrictBuilder.ToMesh(md);
                if (container == null) { AssetDatabase.CreateAsset(mesh, path); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
                GameObject go = new GameObject(md.name);
                go.transform.SetParent(root, false);
                if (md.material == "ColliderOnly")
                {
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    if (md.navArea >= 0) LundunGreyboxBuilder.NavModifier(go, false, md.navArea);
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    n++;
                    continue;
                }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(md.material);
                if (group == "Ground" || md.material.StartsWith("LightPool") || md.material.StartsWith("LampHead")) mr.shadowCastingMode = ShadowCastingMode.Off;
                if (md.collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (md.navIgnore) LundunGreyboxBuilder.NavModifier(go, true, -1);
                else if (md.navArea >= 0) LundunGreyboxBuilder.NavModifier(go, false, md.navArea);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                n++;
            }
            if (group == "Bounds")
                foreach (ColliderBox c in world.walls)
                {
                    GameObject g = new GameObject(c.name);
                    g.transform.SetParent(root, false);
                    g.transform.position = new Vector3((float)c.cx, (float)c.cy, (float)c.cz);
                    g.AddComponent<BoxCollider>().size = new Vector3((float)c.sx, (float)c.sy, (float)c.sz);
                    LundunGreyboxBuilder.NavModifier(g, true, -1);
                    GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
                    n++;
                }
        }
        return n;
    }
}
