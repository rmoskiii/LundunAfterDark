// Lundun → 25  Railway over Rye Lane (L6)
// 1. repaints the building kit's atlases (new strips: the viaduct arches, the bridge girders, the train, the station boards)
// 2. builds the railway into World/Railway: the embankment up from the south edge, girder bridges over the Ring and
//    Old Kent Lane, the brick viaduct along the river side of Peckam, the graffitied bridge over Rye Lane and Rye Lane
//    station with a train at the buffers. Solid (colliders) and kept off the NavMesh, so nobody walks through it.
// 3. refreshes the street lamps (the few under the bridges now hang from the bridge) and their L5 colours
// 4. re-dresses the city (Lundun → 13) to add the fences along the bank and the graffiti on the bridges
// 5. L6b: the Rye Lane shuttle (LundunTrain): a three-car train running the line, a passage and stairs up to the platform
// Afterwards: Lundun → 3 Bake Navigation, then Cmd+S. Menu 1 builds the railway too from now on.
// To build the city without it, set Generator.UseRailway = false (LundunRailway.cs) and run 1.
using System.Collections.Generic;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunRailwayBuilder
{
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string MeshPath = "Assets/World/Generated/RailwayMeshes.asset";
    const string TrainMeshPath = "Assets/World/Generated/TrainMeshes.asset";

    [MenuItem("Lundun/25  Railway over Rye Lane (L6)", false, 31)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { EditorUtility.DisplayDialog("Lundun", "Open the Lundun scene first.", "OK"); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + JsonPath, "OK"); return; }
        GameObject worldRoot = GameObject.Find("World");
        if (worldRoot == null) { EditorUtility.DisplayDialog("Lundun", "No 'World' object in the scene. Run Lundun → 1 Build Greybox World first.", "OK"); return; }

        string log = "";
        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Painting arches, girders and trains…", 0.05f);
            LundunBuildingKitBuilder.EnsureMaterials(true);

            EditorUtility.DisplayProgressBar("Lundun", "Laying the line…", 0.25f);
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            GreyboxResult world = Generator.Build(data);
            int pieces = BuildRailway(worldRoot, world);
            BuildTrain();
            double[] way = Generator.RailStationEntrance();         // E0: on the map, via the registry
            if (way != null) LundunExpansionBuilder.Register("RAIL_RYE_LANE", LundunTrain.StationName, "rail", new Vector3((float)way[0], (float)way[1], (float)way[2]), false);
            foreach (string l in world.log) if (l.StartsWith("railway")) log = l;

            EditorUtility.DisplayProgressBar("Lundun", "Street lamps…", 0.45f);
            LundunLightingDistrictBuilder.RefreshLamps(world);
            LundunLightingDistrictBuilder.RegisterLamps(world, false);
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Lundun", "Railway failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.", "OK");
            return;
        }
        EditorUtility.ClearProgressBar();

        LundunDressingBuilder.Build();          // fences and graffiti (and everything else, exactly as before)
        LundunLightingDistrictBuilder.ApplyLook();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Lundun] L6 " + log + ".\nNext: Lundun → 3 Bake Navigation (the railway is solid), then Cmd+S.");
    }

    /// L6b: the Rye Lane shuttle: three cars and the LundunTrain that runs them (a scene object of its own, so menu 1 keeps it).
    public static void BuildTrain()
    {
        GameObject old = GameObject.Find("LundunTrain");
        if (old != null) Object.DestroyImmediate(old);
        AssetDatabase.DeleteAsset(TrainMeshPath);
        double station, depot; double[] spot;
        List<double[]> pts = Generator.TrainPath(out station, out depot, out spot);
        if (pts.Count < 2) return;
        GameObject root = new GameObject("LundunTrain");
        Mesh container = null;
        Transform[] cars = new Transform[3];
        for (int k = 0; k < 3; k++)
        {
            MeshData[] md = Generator.TrainCarMeshes(k == 0, k == 2);
            GameObject car = new GameObject("Car_" + k);
            car.transform.SetParent(root.transform, false);
            for (int i = 0; i < md.Length; i++)
            {
                Mesh mesh = LundunLightingDistrictBuilder.ToMesh(md[i]);
                mesh.name = "Train" + k + "_" + md[i].name;
                if (container == null) { AssetDatabase.CreateAsset(mesh, TrainMeshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
                GameObject g = i == 0 ? car : new GameObject("Lights");
                if (i > 0) g.transform.SetParent(car.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(md[i].material);
                if (i > 0) mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            BoxCollider bc = car.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 1.75f, 0f); bc.size = new Vector3(2.8f, 2.9f, Generator.TrainCarLength);
            cars[k] = car.transform;
        }
        Vector3[] path = new Vector3[pts.Count];
        for (int i = 0; i < pts.Count; i++) path[i] = new Vector3((float)pts[i][0], (float)pts[i][1], (float)pts[i][2]);
        LundunTrain t = root.AddComponent<LundunTrain>();
        double[] way = Generator.RailStationEntrance();
        t.SetData(path, cars, (float)station, (float)depot, new Vector3((float)spot[0], (float)spot[1], (float)spot[2]),
                  way != null ? new Vector3((float)way[0], (float)way[1], (float)way[2]) : path[path.Length - 1]);
        // park it at the platform in the editor too
        for (int k = 0; k < 3; k++)
        {
            float front = (float)station - k * (Generator.TrainCarLength + Generator.TrainCarGap), rear = front - Generator.TrainCarLength;
            Vector3 pf = At(path, front - 3f), pr = At(path, rear + 3f);
            cars[k].SetPositionAndRotation((pf + pr) * 0.5f, Quaternion.LookRotation(pf - pr, Vector3.up));
        }
        EditorUtility.SetDirty(t);
    }

    static Vector3 At(Vector3[] path, float s)
    {
        float acc = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            float l = Vector3.Distance(path[i - 1], path[i]);
            if (acc + l >= s) return Vector3.Lerp(path[i - 1], path[i], (s - acc) / Mathf.Max(0.001f, l));
            acc += l;
        }
        return path[path.Length - 1];
    }

    static int BuildRailway(GameObject worldRoot, GreyboxResult world)
    {
        Transform old = worldRoot.transform.Find("Railway");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        AssetDatabase.DeleteAsset(MeshPath);
        Transform root = new GameObject("Railway").transform;
        root.SetParent(worldRoot.transform, false);

        Mesh container = null;
        int n = 0;
        foreach (MeshData md in world.meshes)
        {
            if (md.group != "Railway" || md.tri.Count == 0) continue;
            Mesh mesh = LundunLightingDistrictBuilder.ToMesh(md);
            if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
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
            if (md.material.StartsWith("LampHead")) mr.shadowCastingMode = ShadowCastingMode.Off;
            if (md.collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (md.navIgnore) LundunGreyboxBuilder.NavModifier(go, true, -1);
            else if (md.navArea >= 0) LundunGreyboxBuilder.NavModifier(go, false, md.navArea);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            n++;
        }
        return n;
    }
}
