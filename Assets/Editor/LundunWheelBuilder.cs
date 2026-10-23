// Lundun → 27  The Lundun Wheel (E7)
// Builds the Wheel on the south bank by Rye Bridge (Generator.WheelSite) as a scene object of its own, "LundunWheel":
//   Base   legs, footings, the boarding platform, its steps, railings, kiosk and signs (solid; kept off the NavMesh)
//   Near   the turning rim and spokes, and the 32 capsules (LundunWheel turns them and keeps the capsules level)
//   Far    a simple version on the "Landmark" layer, which the camera draws much further than the rest, so the Wheel
//          stands on the skyline from across the city (fog held back by the Lundun/Landmark shader)
// It also adds the "Landmark" layer if the project hasn't got one, repaints the building kit's atlases (the Wheel's
// signs), puts the Wheel on the map (landmark icon, name label, PLACES, route to the gate) and redraws the map picture.
// Afterwards: Lundun → 3 Bake Navigation, then Cmd+S. Menu 1 leaves the Wheel alone.
using System.Collections.Generic;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunWheelBuilder
{
    const string MeshPath = "Assets/World/Generated/WheelMeshes.asset";
    const string MatDir = "Assets/World/Materials";
    const string RootName = "LundunWheel";

    [MenuItem("Lundun/27  The Lundun Wheel (E7)", false, 33)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { EditorUtility.DisplayDialog("Lundun", "Open the Lundun scene first.", "OK"); return; }
        string log;
        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "The Landmark layer…", 0.05f);
            int layer = EnsureLayer("Landmark");
            EditorUtility.DisplayProgressBar("Lundun", "Painting the Wheel's signs…", 0.15f);
            LundunBuildingKitBuilder.EnsureMaterials(true);
            EditorUtility.DisplayProgressBar("Lundun", "Building the Wheel…", 0.4f);
            log = BuildWheel(layer);
            EditorUtility.DisplayProgressBar("Lundun", "Putting it on the map…", 0.7f);
            double[] g = Generator.WheelGate;
            V2 gate = Generator.WheelToWorld(g[0], g[2]);
            LundunExpansionBuilder.Register("LANDMARK_LUNDUN_WHEEL", "The Lundun Wheel", "landmark", new Vector3((float)gate.x, 0f, (float)gate.z), true);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Lundun", "The Wheel failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.", "OK");
            return;
        }
        EditorUtility.ClearProgressBar();
        LundunMapSharpBuilder.Build();              // the map picture, with the Wheel on it
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Lundun] E7 " + log + "\nNext: Lundun → 3 Bake Navigation, then Cmd+S.");
    }

    static string BuildWheel(int layer)
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        AssetDatabase.DeleteAsset(MeshPath);
        container = null;

        V2 c = Generator.WheelSite;
        GameObject root = new GameObject(RootName);
        root.transform.SetPositionAndRotation(new Vector3((float)c.x, 0f, (float)c.z), Quaternion.Euler(0f, (float)Generator.WheelYaw, 0f));
        Transform baseT = Child(root.transform, "Base"), nearT = Child(root.transform, "Near"), farT = Child(root.transform, "Far");
        Vector3 hub = new Vector3(0f, (float)Generator.WheelHubY, 0f);

        // base: solid, never switched off
        MeshData walk;
        MeshData st = Generator.WheelStatic(out walk);
        MeshData signs = Generator.WheelSignFaces();
        GameObject stGo = Part(baseT, st, true); Solid(stGo, st);
        Part(baseT, signs, true);
        GameObject wk = new GameObject(walk.name); wk.transform.SetParent(baseT, false);
        Mesh wm = Save(walk); wk.AddComponent<MeshCollider>().sharedMesh = wm;
        LundunGreyboxBuilder.NavModifier(wk, false, 1);

        // near: the rotor (rim, spokes, lights) and the capsules
        List<Renderer> lights = new List<Renderer>();
        MeshData rl;
        MeshData rot = Generator.WheelRotor(out rl);
        Transform rotor = Child(nearT, "Rotor"); rotor.localPosition = hub;
        Part(rotor, rot, false);
        lights.Add(Part(rotor, rl, false).GetComponent<Renderer>());
        MeshData cl;
        MeshData cap = Generator.WheelCapsule(out cl);
        Mesh capMesh = Save(cap), capLights = Save(cl);
        Transform capsT = Child(nearT, "Capsules");
        Transform[] caps = new Transform[Generator.WheelCapsules];
        for (int k = 0; k < caps.Length; k++)
        {
            GameObject g = new GameObject("Capsule_" + k);
            g.transform.SetParent(capsT, false);
            float a = 2f * Mathf.PI * k / caps.Length;
            g.transform.localPosition = new Vector3(Mathf.Sin(a) * (float)Generator.WheelCapsuleR, (float)Generator.WheelHubY - Mathf.Cos(a) * (float)Generator.WheelCapsuleR, 0f);
            g.AddComponent<MeshFilter>().sharedMesh = capMesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = LundunGreyboxBuilder.WorldMaterial(cap.material);
            GameObject lg = new GameObject("Lights"); lg.transform.SetParent(g.transform, false);
            lg.AddComponent<MeshFilter>().sharedMesh = capLights;
            MeshRenderer lr = lg.AddComponent<MeshRenderer>(); lr.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(cl.material); lr.shadowCastingMode = ShadowCastingMode.Off;
            lights.Add(lr);
            caps[k] = g.transform;
        }

        // far: the skyline version on the Landmark layer
        MeshData fr, fl;
        MeshData fs = Generator.WheelFar(out fr, out fl);
        Material farBody = FarMaterial("Landmark_Far", true), farLights = FarMaterial("Landmark_FarLights", false);
        List<Renderer> farBodies = new List<Renderer>();
        farBodies.Add(FarPart(farT, fs, farBody));
        Transform farRotor = Child(farT, "FarRotor"); farRotor.localPosition = hub;
        farBodies.Add(FarPart(farRotor, fr, farBody));
        lights.Add(FarPart(farRotor, fl, farLights));
        if (layer >= 0) foreach (Transform t in farT.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        // the script
        double[] b = Generator.WheelBoardSpot, x = Generator.WheelExitSpot;
        LundunWheel w = root.AddComponent<LundunWheel>();
        w.SetData(rotor, farRotor, caps, nearT.gameObject, farT.gameObject, lights.ToArray(), farBodies.ToArray(),
                  (float)Generator.WheelHubY, (float)Generator.WheelCapsuleR, new Vector3((float)b[0], (float)b[1], (float)b[2]), new Vector3((float)x[0], (float)x[1], (float)x[2]));
        EditorUtility.SetDirty(w);
        int verts = st.VertexCount + rot.VertexCount + rl.VertexCount + (cap.VertexCount + cl.VertexCount) * caps.Length;
        return "The Lundun Wheel: " + (Generator.WheelHubY + Generator.WheelCapsuleR + 1.7).ToString("0") + " m tall, " + caps.Length + " capsules, " +
               verts + " vertices close up, a skyline version on layer " + (layer >= 0 ? LayerMask.LayerToName(layer) : "(none: add a 'Landmark' layer)") + ".";
    }

    static Mesh container;

    static Mesh Save(MeshData md)
    {
        Mesh mesh = LundunLightingDistrictBuilder.ToMesh(md);
        if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
        return mesh;
    }

    static Transform Child(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Part(Transform parent, MeshData md, bool isStatic)
    {
        GameObject g = new GameObject(md.name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = Save(md);
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(md.material);
        if (md.material == "WheelLights") r.shadowCastingMode = ShadowCastingMode.Off;
        if (isStatic) GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        return g;
    }

    static void Solid(GameObject g, MeshData md)
    {
        g.AddComponent<MeshCollider>().sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
        LundunGreyboxBuilder.NavModifier(g, false, 1);
    }

    static Renderer FarPart(Transform parent, MeshData md, Material mat)
    {
        GameObject g = new GameObject(md.name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = Save(md);
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    /// The far materials (shader Lundun/Landmark): the body samples the kit's atlas B for its swatch colours.
    static Material FarMaterial(string name, bool body)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Lundun/Landmark");
        if (sh == null) throw new System.Exception("Can't find the shader Lundun/Landmark (Assets/World/Shaders/LundunLandmark.shader).");
        if (m == null) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        if (body)
        {
            Material kit = LundunGreyboxBuilder.WorldMaterial(Generator.KitMatB);
            Texture t = kit != null && kit.HasProperty("_BaseMap") ? kit.GetTexture("_BaseMap") : null;
            m.SetTexture("_MainTex", t);
            m.SetColor("_Color", Color.white);
            m.SetColor("_EmissionColor", Color.black);
        }
        else
        {
            m.SetTexture("_MainTex", null);
            m.SetColor("_Color", Color.black);
            m.SetColor("_EmissionColor", Color.black);
            m.SetFloat("_MinVisible", 0.85f);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// The layer index called name, adding it to the first free user layer if the project hasn't got one.
    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        Object[] tm = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tm == null || tm.Length == 0) return -1;
        SerializedObject so = new SerializedObject(tm[0]);
        SerializedProperty layers = so.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = name; so.ApplyModifiedProperties(); return i; }
        }
        return -1;
    }
}
