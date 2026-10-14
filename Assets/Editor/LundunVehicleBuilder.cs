// Vehicles for Lundun traffic.
//   Lundun → 10  Make Cars from Models : every car model (FBX/OBJ) in Assets/Vehicles/Models becomes a drivable,
//                                         stealable traffic car (Kenney Car Kit works as-is). Physics copied from your Kestrel.
//   Lundun → 11  Make Double-Decker Bus : builds an original red double-decker from simple shapes (no TfL branding).
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
    [MenuItem("Lundun/10  Make Cars from Models", false, 10)]
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
        Material sign = Mat("Bus_Sign", new Color(0.1f, 0.1f, 0.1f), 0.5f, new Color(1f, 0.75f, 0.2f) * 1.5f);
        Material lamp = Mat("Bus_Lamp", Color.white, 0.5f, Color.white * 2f);

        GameObject root = new GameObject("Bus_DoubleDecker");
        root.layer = template.layer;
        Transform vis = new GameObject("Model").transform;
        vis.SetParent(root.transform, false);
        const float L = 10.4f, W = 2.55f, H = 4.4f, Clear = 0.3f;

        Part(vis, red, new Vector3(0, Clear + (H - Clear) / 2f, 0), new Vector3(W, H - Clear, L));                 // body
        Part(vis, black, new Vector3(0, Clear + 0.18f, 0), new Vector3(W + 0.02f, 0.36f, L + 0.02f));                // skirt
        Part(vis, roof, new Vector3(0, H + 0.03f, 0), new Vector3(W - 0.08f, 0.06f, L - 0.2f));                      // roof
        Part(vis, glass, new Vector3(0, 1.85f, -0.7f), new Vector3(W + 0.03f, 0.9f, 7.9f));                          // lower deck windows
        Part(vis, glass, new Vector3(0, 3.3f, 0f), new Vector3(W + 0.03f, 0.85f, L - 0.6f));                         // upper deck windows
        Part(vis, glass, new Vector3(0, 1.75f, L / 2f + 0.01f), new Vector3(W - 0.3f, 1.2f, 0.04f));                // windscreen
        Part(vis, glass, new Vector3(0, 3.3f, L / 2f + 0.01f), new Vector3(W - 0.3f, 0.85f, 0.04f));                // front upper window
        Part(vis, glass, new Vector3(0, 3.3f, -L / 2f - 0.01f), new Vector3(W - 0.4f, 0.8f, 0.04f));               // rear upper window
        Part(vis, glass, new Vector3(-W / 2f - 0.02f, 1.35f, L / 2f - 1.0f), new Vector3(0.04f, 2.0f, 1.1f));       // door (kerb side, left)
        Part(vis, sign, new Vector3(0, 4.0f, L / 2f + 0.02f), new Vector3(1.6f, 0.32f, 0.04f));                     // destination blind
        Part(vis, lamp, new Vector3(-0.95f, 0.75f, L / 2f + 0.02f), new Vector3(0.3f, 0.18f, 0.04f));               // headlights
        Part(vis, lamp, new Vector3(0.95f, 0.75f, L / 2f + 0.02f), new Vector3(0.3f, 0.18f, 0.04f));
        float rad = 0.5f, frontAxle = L / 2f - 2.3f, rearAxle = -L / 2f + 2.6f;
        List<Vector3> wheels = new List<Vector3> {
            new Vector3(-1.05f, rad, frontAxle), new Vector3(1.05f, rad, frontAxle),
            new Vector3(-1.05f, rad, rearAxle), new Vector3(1.05f, rad, rearAxle) };
        foreach (Vector3 w in wheels)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.name = "Wheel";
            c.transform.SetParent(vis, false);
            c.transform.localPosition = new Vector3(w.x * 1.12f, rad, w.z);
            c.transform.localRotation = Quaternion.Euler(0, 0, 90);
            c.transform.localScale = new Vector3(rad * 2f, 0.16f, rad * 2f);
            c.GetComponent<MeshRenderer>().sharedMaterial = black;
        }
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
        Debug.Log("[Lundun] Double-decker made. 2 bus routes: Peckam → Knightsbury Circus → Mayfield St, and Gilmore → Warehouses → Quay Bridge. Press Play.");
    }

    // ------------------------------------------------------------------ shared
    /// Physics copied from the Kestrel: Rigidbody, CarController (with this vehicle's wheels), VehicleHealth, bumper.
    static void AddPhysics(GameObject root, GameObject template, List<Vector3> wheels, float radius, Bounds b, float massScale)
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

    static bool Ready(out GameObject template, out LundunPopulation pop)
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

    static Bounds RendererBounds(GameObject root)
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

    static GameObject Save(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void SetF(SerializedObject so, string p, float v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.floatValue = v; }
    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
