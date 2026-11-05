// Vehicles for Lundun traffic.
//   Lundun → Vehicles → Old: Make Kenney Cars from Models (V2: Lundun → 10 Import Car Models replaces it) : every car model (FBX/OBJ) in Assets/Vehicles/Models becomes a drivable,
//                                         stealable traffic car (Kenney Car Kit works as-is). Physics copied from your Kestrel.
//   Lundun → 11  Make Double-Decker Bus : builds an original Routemaster-style red double-decker (no TfL branding).
//   Lundun → Vehicles → Flip Car Models Front-to-Back : if the new cars drive backwards.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunVehicleBuilder
{
    const string ModelDir = "Assets/Vehicles/Models";
    const string PrefabDir = "Assets/Vehicles/Prefabs";
    const string MatDir = "Assets/Vehicles/Materials";

    // names containing these are parts, not whole cars
    static readonly string[] Skip = { "wheel", "tire", "tyre", "debris", "cone", "box", "barrier", "sign", "light", "kit", "police" };

    // ------------------------------------------------------------------ cars
    [MenuItem("Lundun/Vehicles/Old: Make Kenney Cars from Models", false, 61)]
    public static void MakeCars()
    {
        GameObject template; LundunPopulation pop;
        if (!Ready(out template, out pop)) return;
        EnsureFolder(ModelDir); EnsureFolder(PrefabDir);

        List<GameObject> made = new List<GameObject>();
        List<string> skipped = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            string lower = name.ToLowerInvariant();
            bool skip = false;
            foreach (string k in Skip) if (lower.Contains(k)) { skip = true; break; }
            if (skip) { skipped.Add(name); continue; }
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) continue;
            GameObject prefab = BuildCar(model, name, template);
            if (prefab != null) made.Add(prefab);
        }
        if (made.Count == 0)
        {
            Dialog("No car models found in " + ModelDir + ".\n\nDownload the Kenney Car Kit (kenney.nl, free/CC0), unzip it, and drag the files from its FBX folder into " + ModelDir + ", then run this again.");
            return;
        }

        // traffic uses the new cars (plus the repainted Kestrel, if it exists, as a rare sports car)
        GameObject kestrel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TrafficCar.prefab");
        if (kestrel != null) made.Add(kestrel);
        SerializedObject so = new SerializedObject(pop);
        SerializedProperty arr = so.FindProperty("carPrefabs");
        arr.arraySize = made.Count;
        for (int i = 0; i < made.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = made[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Lundun] Made " + (made.Count - (kestrel != null ? 1 : 0)) + " traffic cars in " + PrefabDir +
                  (skipped.Count > 0 ? "  (skipped parts: " + string.Join(", ", skipped.ToArray()) + ")" : "") +
                  ". Press Play. If they drive backwards: Lundun → Vehicles → Flip Car Models Front-to-Back.");
    }

    static float TargetLength(string lower)
    {
        if (lower.Contains("truck") || lower.Contains("garbage") || lower.Contains("fire")) return 7f;
        if (lower.Contains("ambulance")) return 5.8f;
        if (lower.Contains("van") || lower.Contains("delivery")) return 5.2f;
        if (lower.Contains("suv")) return 4.8f;
        return 4.5f;
    }

    static GameObject BuildCar(GameObject modelAsset, string name, GameObject template)
    {
        GameObject root = new GameObject("Veh_" + name);
        int layer = template.layer;
        root.layer = layer;
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        // lie it lengthways along +Z, at the right size, sitting on y = 0
        Bounds b = RendererBounds(root);
        if (b.size.x > b.size.z * 1.2f) { model.transform.localRotation = Quaternion.Euler(0, 90, 0) * model.transform.localRotation; b = RendererBounds(root); }
        float scale = TargetLength(name.ToLowerInvariant()) / Mathf.Max(0.01f, b.size.z);
        model.transform.localScale *= scale;
        b = RendererBounds(root);
        model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        b = RendererBounds(root);

        // wheels: use the model's own wheel parts if it has them
        List<Bounds> wheels = new List<Bounds>(); List<string> wheelNames = new List<string>();
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            if (r.name.ToLowerInvariant().Contains("wheel")) { wheels.Add(r.bounds); wheelNames.Add(r.name.ToLowerInvariant()); }
        float radius; List<Vector3> wheelPos = new List<Vector3>();
        if (wheels.Count >= 4)
        {
            radius = 0f; float frontZ = 0f; int fronts = 0;
            for (int i = 0; i < wheels.Count; i++)
            {
                wheelPos.Add(wheels[i].center); radius += wheels[i].size.y * 0.5f;
                if (wheelNames[i].Contains("front")) { frontZ += wheels[i].center.z; fronts++; }
            }
            radius /= wheels.Count;
            if (fronts > 0 && frontZ / fronts < 0f)
            {   // the model faces -Z: turn it round
                model.transform.RotateAround(Vector3.zero, Vector3.up, 180f);
                for (int i = 0; i < wheelPos.Count; i++) wheelPos[i] = new Vector3(-wheelPos[i].x, wheelPos[i].y, -wheelPos[i].z);
            }
        }
        else
        {
            radius = 0.33f * b.size.z / 4.5f;
            float wx = b.size.x * 0.38f, wz = b.size.z * 0.32f;
            wheelPos.Add(new Vector3(-wx, radius, wz)); wheelPos.Add(new Vector3(wx, radius, wz));
            wheelPos.Add(new Vector3(-wx, radius, -wz)); wheelPos.Add(new Vector3(wx, radius, -wz));
        }
        radius = Mathf.Clamp(radius, 0.25f, 0.6f);
        b = RendererBounds(root);

        AddPhysics(root, template, wheelPos, radius, b, 1f);
        root.AddComponent<LundunTrafficCar>();
        return Save(root, PrefabDir + "/Veh_" + name + ".prefab");
    }

    [MenuItem("Lundun/Vehicles/Flip Car Models Front-to-Back", false, 60)]
    public static void FlipCars()
    {
        int n = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Path.GetFileName(path).StartsWith("Veh_")) continue;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            Transform model = root.transform.Find("Model");
            if (model != null) { model.RotateAround(Vector3.zero, Vector3.up, 180f); PrefabUtility.SaveAsPrefabAsset(root, path); n++; }
            PrefabUtility.UnloadPrefabContents(root);
        }
        Debug.Log("[Lundun] Flipped " + n + " car models. Press Play.");
    }

    // ------------------------------------------------------------------ bus
    // An original Routemaster-style double-decker (no TfL branding, an invented operator): both decks of windows with
    // red pillars between them and a cream band, the rounded roof, the open platform at the back on the kerb side with
    // its pole and the stairs behind, the bonnet and grille at the front with the driver's cab on the right, round
    // headlamps, mirrors, destination blinds front and back (route number + where it's going: set when it's on the road,
    // by LundunBusBlind) and LUNDUN TRANSPORT down the side. All one mesh (a few materials) so it stays cheap on a phone.
    [MenuItem("Lundun/11  Make Double-Decker Bus", false, 11)]
    public static void MakeBus()
    {
        GameObject template; LundunPopulation pop;
        if (!Ready(out template, out pop)) return;
        EnsureFolder(PrefabDir); EnsureFolder(MatDir);

        Material red = Mat("Bus_Red", new Color(0.72f, 0.04f, 0.05f), 0.55f, Color.black);
        Material glass = Mat("Bus_Glass", new Color(0.05f, 0.06f, 0.08f), 0.9f, new Color(1f, 0.85f, 0.6f) * 0.5f);   // lit inside at night
        Material black = Mat("Bus_Black", new Color(0.03f, 0.03f, 0.035f), 0.3f, Color.black);
        Material roof = Mat("Bus_Roof", new Color(0.75f, 0.75f, 0.75f), 0.2f, Color.black);
        Material sign = Mat("Bus_Sign", new Color(0.02f, 0.02f, 0.02f), 0.5f, Color.black);
        Material lamp = Mat("Bus_Lamp", Color.white, 0.5f, Color.white * 2f);
        Material cream = Mat("Bus_Cream", new Color(0.93f, 0.88f, 0.74f), 0.4f, Color.black);
        Material chrome = Mat("Bus_Chrome", new Color(0.78f, 0.8f, 0.82f), 0.85f, Color.black);
        Material tail = Mat("Bus_Tail", new Color(0.6f, 0.02f, 0.02f), 0.6f, new Color(1f, 0.05f, 0.03f) * 1.2f);
        Material amber = Mat("Bus_Amber", new Color(0.9f, 0.5f, 0.05f), 0.6f, new Color(1f, 0.55f, 0.05f) * 0.6f);
        Material inside = Mat("Bus_Inside", new Color(0.16f, 0.12f, 0.1f), 0.2f, new Color(1f, 0.85f, 0.6f) * 0.25f);

        GameObject root = new GameObject("Bus_DoubleDecker");
        root.layer = template.layer;
        Transform vis = new GameObject("Model").transform;
        vis.SetParent(root.transform, false);
        const float L = 10.4f, W = 2.55f, H = 4.4f, Clear = 0.3f;
        float hw = W / 2f, front = L / 2f, rear = -L / 2f;

        // body: lower and upper decks, the cream band between them, the rounded roof
        Part(vis, red, new Vector3(0, Clear + 1.15f, (rear + front - 0.9f) / 2f), new Vector3(W, 2.3f, L - 0.9f));  // lower saloon (stops short of the bonnet and cab)
        Part(vis, red, new Vector3(0, 2.7f + 0.75f, 0), new Vector3(W, 1.5f, L));                                    // upper deck
        Part(vis, cream, new Vector3(0, 2.62f, 0), new Vector3(W + 0.03f, 0.14f, L + 0.03f));                        // between-decks band
        Cyl(vis, red, new Vector3(0, H - 0.22f, 0), new Vector3(W, L / 2f, 0.62f), new Vector3(90, 0, 0));           // rounded roof
        Part(vis, roof, new Vector3(0, H + 0.08f, 0), new Vector3(W * 0.55f, 0.04f, L - 0.6f));
        Part(vis, black, new Vector3(0, Clear + 0.17f, 0.2f), new Vector3(W + 0.02f, 0.34f, L - 0.4f));             // skirt

        // windows: a row each side on both decks, red pillars between the panes
        float[] lowerZ = { -2.75f, -1.45f, -0.15f, 1.15f, 2.45f };                                                   // behind the cab, ahead of the platform
        float[] upperZ = { -4.25f, -3.0f, -1.75f, -0.5f, 0.75f, 2.0f, 3.25f, 4.4f };
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (hw + 0.012f);
            foreach (float z in lowerZ) Part(vis, glass, new Vector3(x, 1.85f, z), new Vector3(0.03f, 0.85f, 1.12f));
            foreach (float z in upperZ) Part(vis, glass, new Vector3(x, 3.42f, z), new Vector3(0.03f, 0.8f, 1.08f));
            // rubber window surrounds read as a dark line along the top and bottom of each row
            Part(vis, black, new Vector3(x, 2.29f, -0.15f), new Vector3(0.025f, 0.04f, 6.3f));
            Part(vis, black, new Vector3(x, 3.83f, 0f), new Vector3(0.025f, 0.04f, 9.6f));
            // wheel arches
            Part(vis, black, new Vector3(side * (hw + 0.01f), 0.62f, front - 2.3f), new Vector3(0.03f, 0.75f, 1.35f));
            Part(vis, black, new Vector3(side * (hw + 0.01f), 0.62f, rear + 2.6f), new Vector3(0.03f, 0.75f, 1.35f));
        }
        // the driver's cab (right, offside) and its side window; the bonnet and grille in front of the lower deck
        Part(vis, red, new Vector3(0.6f, Clear + 1.1f, front - 0.45f), new Vector3(1.35f, 2.2f, 0.9f));               // cab
        Part(vis, glass, new Vector3(hw + 0.012f, 1.75f, front - 0.5f), new Vector3(0.03f, 0.8f, 0.7f));              // cab side window
        Part(vis, glass, new Vector3(0.6f, 1.8f, front + 0.012f), new Vector3(1.1f, 0.8f, 0.03f));                   // cab windscreen
        Part(vis, glass, new Vector3(-0.6f, 1.95f, front - 0.9f + 0.012f), new Vector3(1.0f, 0.8f, 0.03f));          // lower saloon front window (behind the bonnet)
        Part(vis, red, new Vector3(-0.6f, 0.95f, front - 0.45f), new Vector3(1.15f, 0.9f, 0.9f));                    // bonnet
        Part(vis, chrome, new Vector3(-0.6f, 0.95f, front + 0.012f), new Vector3(0.8f, 0.7f, 0.03f));                // radiator grille
        for (int i = 0; i < 6; i++) Part(vis, black, new Vector3(-0.6f, 0.7f + i * 0.1f, front + 0.03f), new Vector3(0.74f, 0.025f, 0.02f));
        Part(vis, chrome, new Vector3(0, 0.45f, front + 0.06f), new Vector3(W - 0.1f, 0.12f, 0.08f));                // bumper
        foreach (float x in new[] { -0.95f, 0.95f })
        {
            Cyl(vis, chrome, new Vector3(x, 1.0f, front + 0.02f), new Vector3(0.3f, 0.03f, 0.3f), new Vector3(90, 0, 0));   // headlamp rim
            Cyl(vis, lamp, new Vector3(x, 1.0f, front + 0.05f), new Vector3(0.24f, 0.02f, 0.24f), new Vector3(90, 0, 0));
            Part(vis, amber, new Vector3(x * 1.12f, 1.32f, front + 0.02f), new Vector3(0.14f, 0.1f, 0.03f));         // indicators
            Part(vis, black, new Vector3(x > 0 ? hw + 0.3f : -hw - 0.3f, 2.0f, front - 0.15f), new Vector3(0.06f, 0.36f, 0.22f));   // mirrors
            Part(vis, chrome, new Vector3(x > 0 ? hw + 0.15f : -hw - 0.15f, 2.0f, front - 0.15f), new Vector3(0.3f, 0.03f, 0.03f));
        }
        // upper deck front windows (two, split by a pillar)
        Part(vis, glass, new Vector3(-0.62f, 3.4f, front + 0.012f), new Vector3(1.05f, 0.82f, 0.03f));
        Part(vis, glass, new Vector3(0.62f, 3.4f, front + 0.012f), new Vector3(1.05f, 0.82f, 0.03f));

        // destination blinds: front (route number + destination), rear (route number), side (over the platform)
        Part(vis, sign, new Vector3(-0.25f, 2.78f + 0.0f, front + 0.012f), new Vector3(1.5f, 0.28f, 0.03f));        // front destination
        Part(vis, sign, new Vector3(0.82f, 2.78f, front + 0.012f), new Vector3(0.5f, 0.28f, 0.03f));                 // front route number
        Part(vis, sign, new Vector3(0.6f, 2.72f, rear - 0.012f), new Vector3(0.55f, 0.32f, 0.03f));                  // rear route number
        Part(vis, sign, new Vector3(-hw - 0.012f, 2.78f, rear + 1.2f), new Vector3(0.03f, 0.26f, 1.2f));             // side, by the platform

        // the open platform: back corner, kerb side (left): dark opening, floor, the pole, the stairs going up behind
        float pz = rear + 0.75f;
        Part(vis, inside, new Vector3(-hw + 0.45f, 1.45f, pz), new Vector3(0.9f, 2.1f, 1.5f));                        // the opening (inside, lit)
        Part(vis, black, new Vector3(-hw - 0.012f, 1.45f, pz), new Vector3(0.025f, 2.12f, 1.52f));                   // dark edge where the body's open
        Part(vis, inside, new Vector3(-hw - 0.013f, 1.45f, pz), new Vector3(0.02f, 2.06f, 1.46f));
        Part(vis, chrome, new Vector3(-hw + 0.25f, 0.42f, pz), new Vector3(0.5f, 0.06f, 1.5f));                      // platform floor (step)
        Cyl(vis, cream, new Vector3(-hw - 0.05f, 1.45f, pz + 0.55f), new Vector3(0.06f, 1.05f, 0.06f), Vector3.zero); // the pole
        for (int i = 0; i < 6; i++)                                                                                   // stairs, rising to the back
            Part(vis, black, new Vector3(-hw + 0.02f, 0.75f + i * 0.3f, pz - 0.45f + i * 0.1f), new Vector3(0.02f, 0.05f, 0.5f));
        Part(vis, glass, new Vector3(-hw + 0.012f - 0.02f, 3.45f, rear + 0.35f), new Vector3(0.03f, 0.75f, 0.45f));   // the little stair window

        // rear: upper rear window, tail lights, indicators, number plate
        Part(vis, glass, new Vector3(0, 3.4f, rear - 0.012f), new Vector3(W - 0.5f, 0.75f, 0.03f));
        Part(vis, glass, new Vector3(0.55f, 1.85f, rear - 0.012f), new Vector3(1.1f, 0.85f, 0.03f));                 // lower rear window
        foreach (float x in new[] { -1.05f, 1.05f })
        {
            Part(vis, tail, new Vector3(x, 0.95f, rear - 0.02f), new Vector3(0.22f, 0.3f, 0.03f));
            Part(vis, amber, new Vector3(x, 1.25f, rear - 0.02f), new Vector3(0.18f, 0.12f, 0.03f));
        }
        Part(vis, cream, new Vector3(0.55f, 0.65f, rear - 0.02f), new Vector3(0.6f, 0.16f, 0.02f));                 // plate
        Part(vis, chrome, new Vector3(0, 0.42f, rear - 0.05f), new Vector3(W - 0.1f, 0.1f, 0.06f));                  // rear bumper

        // wheels with hubs (the back ones doubled)
        float rad = 0.5f, frontAxle = front - 2.3f, rearAxle = rear + 2.6f;
        List<Vector3> wheels = new List<Vector3> {
            new Vector3(-1.05f, rad, frontAxle), new Vector3(1.05f, rad, frontAxle),
            new Vector3(-1.05f, rad, rearAxle), new Vector3(1.05f, rad, rearAxle) };
        foreach (Vector3 w in wheels)
        {
            float wx = w.x * 1.12f;
            Cyl(vis, black, new Vector3(wx, rad, w.z), new Vector3(rad * 2f, w.z < 0 ? 0.24f : 0.16f, rad * 2f), new Vector3(0, 0, 90));
            Cyl(vis, chrome, new Vector3(wx + Mathf.Sign(wx) * (w.z < 0 ? 0.125f : 0.085f), rad, w.z), new Vector3(rad * 1.0f, 0.02f, rad * 1.0f), new Vector3(0, 0, 90));
        }

        // all one mesh: one renderer, a submesh per material
        Mesh mesh = Combine(vis, "Bus_DoubleDecker_Mesh", out Material[] mats);
        GameObject body = new GameObject("Body");
        body.transform.SetParent(vis, false);
        body.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = body.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;

        // the blinds' words and the operator's name (filled in when it's on the road: LundunBusBlind)
        Words(vis, "BlindDest", new Vector3(-0.25f, 2.78f, front + 0.035f), 0f, new Vector2(1.45f, 0.25f), 1.2f, new Color(1f, 0.92f, 0.55f));
        Words(vis, "BlindRoute", new Vector3(0.82f, 2.78f, front + 0.035f), 0f, new Vector2(0.48f, 0.25f), 1.6f, new Color(1f, 0.92f, 0.55f));
        Words(vis, "BlindRoute", new Vector3(0.6f, 2.72f, rear - 0.035f), 180f, new Vector2(0.52f, 0.28f), 1.8f, new Color(1f, 0.92f, 0.55f));
        Words(vis, "BlindDest", new Vector3(-hw - 0.035f, 2.78f, rear + 1.2f), -90f, new Vector2(1.15f, 0.22f), 1.0f, new Color(1f, 0.92f, 0.55f));
        foreach (int side in new[] { -1, 1 })
            Words(vis, "Fleet", new Vector3(side * (hw + 0.03f), 1.12f, 0.6f), side * 90f, new Vector2(3.4f, 0.3f), 1.6f, new Color(0.95f, 0.85f, 0.45f), "LUNDUN TRANSPORT");
        root.AddComponent<LundunBusBlind>();

        foreach (Transform t in vis.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = template.layer;

        Bounds b = new Bounds(new Vector3(0, H / 2f, 0), new Vector3(W, H, L));
        AddPhysics(root, template, wheels, rad, b, 3.3f);
        // a bus handles like a bus
        SerializedObject cc = new SerializedObject(root.GetComponent<CarController>());
        SetF(cc, "maxSpeed", 12f); SetF(cc, "acceleration", 4f); SetF(cc, "turnSpeed", 40f); SetF(cc, "grip", 0.9f);
        cc.ApplyModifiedPropertiesWithoutUndo();
        LundunTrafficCar ai = root.AddComponent<LundunTrafficCar>();
        SerializedObject aso = new SerializedObject(ai);
        aso.FindProperty("isBus").boolValue = true;
        SetF(aso, "lookAhead", 10f); SetF(aso, "cornerSpeed", 4f);
        aso.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = Save(root, PrefabDir + "/Bus_DoubleDecker.prefab");
        SerializedObject so = new SerializedObject(pop);
        so.FindProperty("busPrefab").objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeObject = prefab;
        Debug.Log("[Lundun] Double-decker made (Routemaster-style). 2 bus routes: Peckam → Knightsbury Circus → Mayfield St, and Gilmore → Warehouses → Quay Bridge. Press Play.");
    }

    static void Cyl(Transform parent, Material m, Vector3 centre, Vector3 size, Vector3 euler)
    {
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(parent, false);
        c.transform.localPosition = centre; c.transform.localRotation = Quaternion.Euler(euler); c.transform.localScale = size;
        c.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    /// Every part under 'parent' into one mesh (a submesh per material), saved as an asset; the parts are removed.
    static Mesh Combine(Transform parent, string name, out Material[] materials)
    {
        Dictionary<Material, List<CombineInstance>> byMat = new Dictionary<Material, List<CombineInstance>>();
        List<GameObject> parts = new List<GameObject>();
        Matrix4x4 toParent = parent.worldToLocalMatrix;
        foreach (MeshFilter f in parent.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer r = f.GetComponent<MeshRenderer>();
            if (r == null || f.sharedMesh == null) continue;
            Material m = r.sharedMaterial;
            if (!byMat.TryGetValue(m, out List<CombineInstance> list)) byMat[m] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = f.sharedMesh, transform = toParent * f.transform.localToWorldMatrix });
            parts.Add(f.gameObject);
        }
        List<Material> mats = new List<Material>();
        List<CombineInstance> subs = new List<CombineInstance>();
        foreach (var kv in byMat)
        {
            Mesh sub = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            sub.CombineMeshes(kv.Value.ToArray(), true, true);
            subs.Add(new CombineInstance { mesh = sub, transform = Matrix4x4.identity });
            mats.Add(kv.Key);
        }
        Mesh mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(subs.ToArray(), false, false);
        mesh.RecalculateBounds();
        foreach (GameObject g in parts) Object.DestroyImmediate(g);
        EnsureFolder("Assets/Vehicles/Meshes");
        string path = "Assets/Vehicles/Meshes/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        materials = mats.ToArray();
        return mesh;
    }

    /// A line of words on the bus (TextMeshPro, flat on the body): name it so LundunBusBlind can fill it in.
    static void Words(Transform parent, string name, Vector3 at, float yaw, Vector2 box, float size, Color colour, string text = "")
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.transform.localRotation = Quaternion.Euler(0f, yaw + 180f, 0f);                 // TextMeshPro reads along -z
        RectTransform rt = (RectTransform)g.transform; rt.sizeDelta = box;
        TMPro.TextMeshPro t = g.AddComponent<TMPro.TextMeshPro>();
        t.text = text; t.fontSize = size; t.color = colour; t.fontStyle = TMPro.FontStyles.Bold;
        t.alignment = TMPro.TextAlignmentOptions.Center; t.textWrappingMode = TMPro.TextWrappingModes.NoWrap; t.overflowMode = TMPro.TextOverflowModes.Truncate;
        t.enableAutoSizing = true; t.fontSizeMin = 0.3f; t.fontSizeMax = size;
        MeshRenderer r = g.GetComponent<MeshRenderer>();
        if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    // ------------------------------------------------------------------ shared
    /// Physics copied from the Kestrel: Rigidbody, CarController (with this vehicle's wheels), VehicleHealth, bumper.
    internal static void AddPhysics(GameObject root, GameObject template, List<Vector3> wheels, float radius, Bounds b, float massScale)
    {
        Rigidbody rb = root.AddComponent<Rigidbody>();
        EditorUtility.CopySerialized(template.GetComponent<Rigidbody>(), rb);
        rb.mass *= massScale;

        BoxCollider box = root.AddComponent<BoxCollider>();
        float bottom = radius * 1.1f;
        box.center = new Vector3(0, (bottom + b.max.y) / 2f, b.center.z);
        box.size = new Vector3(b.size.x * 0.95f, b.max.y - bottom, b.size.z * 0.98f);

        CarController tcc = template.GetComponent<CarController>();
        CarController cc = root.AddComponent<CarController>();
        EditorUtility.CopySerialized(tcc, cc);
        SerializedObject so = new SerializedObject(cc);
        float rest = so.FindProperty("restLength").floatValue;
        float k = so.FindProperty("springStrength").floatValue * massScale;
        SetF(so, "springStrength", k);
        SetF(so, "springDamper", so.FindProperty("springDamper").floatValue * massScale);
        SetF(so, "wheelRadius", radius);
        float sag = rb.mass * 9.81f / (4f * k);                      // how far the springs settle
        SerializedProperty pts = so.FindProperty("springPoints");
        pts.arraySize = wheels.Count;
        for (int i = 0; i < wheels.Count; i++)
        {
            Transform sp = new GameObject("Spring_" + i).transform;
            sp.SetParent(root.transform, false);
            sp.localPosition = new Vector3(wheels[i].x, rest + radius - sag, wheels[i].z);
            pts.GetArrayElementAtIndex(i).objectReferenceValue = sp;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        VehicleHealth tvh = template.GetComponent<VehicleHealth>();
        if (tvh != null) { VehicleHealth vh = root.AddComponent<VehicleHealth>(); EditorUtility.CopySerialized(tvh, vh); }

        // bumper (knocks people over): copy the Kestrel's, point it at this body, fit it to the front
        CarBumper tb = template.GetComponentInChildren<CarBumper>(true);
        if (tb != null)
        {
            GameObject bumper = Object.Instantiate(tb.gameObject, root.transform);
            bumper.name = "Bumper";
            bumper.transform.localPosition = Vector3.zero; bumper.transform.localRotation = Quaternion.identity; bumper.transform.localScale = Vector3.one;
            SerializedObject bso = new SerializedObject(bumper.GetComponent<CarBumper>());
            bso.FindProperty("carBody").objectReferenceValue = rb;
            bso.ApplyModifiedPropertiesWithoutUndo();
            BoxCollider bc = bumper.GetComponent<BoxCollider>();
            if (bc != null) { bc.center = new Vector3(0, b.size.y * 0.4f, b.max.z - 0.2f); bc.size = new Vector3(b.size.x, b.size.y * 0.6f, 1.2f); }
        }
    }

    internal static bool Ready(out GameObject template, out LundunPopulation pop)
    {
        template = null; pop = null;
        if (SceneManager.GetActiveScene().name != "Lundun") { Dialog("Open the Lundun scene first."); return false; }
        foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (g.name == "Car" && g.GetComponent<CarController>() != null) template = g;
            if (g.name == "LundunPopulation") pop = g.GetComponent<LundunPopulation>();
        }
        if (template == null) { Dialog("Can't find your car (root object 'Car') to copy physics from."); return false; }
        if (pop == null) { Dialog("Run Lundun → 8  Build Population Manager first."); return false; }
        return true;
    }

    internal static Bounds RendererBounds(GameObject root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void Part(Transform parent, Material m, Vector3 centre, Vector3 size)
    {
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(parent, false);
        c.transform.localPosition = centre; c.transform.localScale = size;
        c.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    static Material Mat(string name, Color c, float smooth, Color emission)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        m = new Material(sh != null ? sh : Shader.Find("Standard"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); }
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    internal static GameObject Save(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    internal static void SetF(SerializedObject so, string p, float v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.floatValue = v; }
    internal static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    internal static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
