// Lundun → 27  The Lundun Wheel (E7)
// Builds the Wheel on the south bank by Rye Bridge (Generator.WheelSite) as a scene object of its own, "LundunWheel":
//   Base   legs, footings, the boarding platform, its steps, railings, kiosk and signs (solid; kept off the NavMesh)
//   Near   the turning rim and spokes, and the 32 capsules (LundunWheel turns them and keeps the capsules level)
//   Far    a simple version on the "Landmark" layer, which the camera draws much further than the rest, so the Wheel
//          stands on the skyline from across the city (fog held back by the Lundun/Landmark shader)
// It also adds the "Landmark" layer if the project hasn't got one, repaints the building kit's atlases (the Wheel's
// signs), puts the Wheel on the map (landmark icon, name label, PLACES, route to the gate) and redraws the map picture.
// E7b: the capsules get see-through glass, a cabin that lights up at night and people riding; the Wheel stands on a
// paved riverside plaza (benches, lamps) with a crowd (LundunWheelCrowd) that queues, rides and gets off.
//   Plaza  the paving (walkable, like a pavement), benches and bins, heritage lamps
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
        MeshData walk, deck, block;
        MeshData st = Generator.WheelStatic(out walk, out deck, out block);
        MeshData signs = Generator.WheelSignFaces();
        GameObject stGo = Part(baseT, st, true); Solid(stGo, 1);
        GameObject dkGo = Part(baseT, deck, true); Solid(dkGo, Generator.PavementArea);       // E7b: the platform is walkable
        Part(baseT, signs, true);
        Invisible(baseT, walk, Generator.PavementArea);
        Invisible(baseT, block, 1);

        // E7b: the plaza round it
        Transform plazaT = Child(root.transform, "Plaza");
        MeshData props, poles, heads, pools;
        MeshData plaza = Generator.WheelPlaza(out props, out poles, out heads, out pools);
        Solid(Part(plazaT, plaza, true), Generator.PavementArea);
        Solid(Part(plazaT, props, true), 1);
        Part(plazaT, poles, true);
        Part(plazaT, heads, true).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        GameObject pl = Part(plazaT, pools, true);
        Renderer plr = pl.GetComponent<Renderer>(); plr.shadowCastingMode = ShadowCastingMode.Off; plr.receiveShadows = false;

        // near: the rotor (rim, spokes, lights) and the capsules
        List<Renderer> lights = new List<Renderer>();
        MeshData rl;
        MeshData rot = Generator.WheelRotor(out rl);
        Transform rotor = Child(nearT, "Rotor"); rotor.localPosition = hub;
        Part(rotor, rot, false);
        lights.Add(Part(rotor, rl, false).GetComponent<Renderer>());
        MeshData cl, cg, ci, cc;
        MeshData cap = Generator.WheelCapsule(out cl, out cg, out ci, out cc);
        Mesh capMesh = Save(cap), capLights = Save(cl), capGlass = Save(cg), capCabin = Save(ci), capCabinLight = Save(cc);
        // riders: meshes for 1–8 people, two looks each (index count * 2 + variant; 0 and 1 = nobody)
        Mesh[] riders = new Mesh[18];
        for (int n = 1; n <= 8; n++) for (int v = 0; v < 2; v++) riders[n * 2 + v] = Save(Generator.WheelPassengers(n, v));
        Material interiorMat = Mat("WheelInterior"), glassMat = Mat("WheelGlass"), cabinLightMat = Mat("WheelCabinLight");
        Transform capsT = Child(nearT, "Capsules");
        Transform[] caps = new Transform[Generator.WheelCapsules];
        MeshFilter[] riderFilters = new MeshFilter[caps.Length];
        List<Renderer> glassR = new List<Renderer>(), cabinR = new List<Renderer>(), cabinLightR = new List<Renderer>();
        for (int k = 0; k < caps.Length; k++)
        {
            GameObject g = new GameObject("Capsule_" + k);
            g.transform.SetParent(capsT, false);
            float a = 2f * Mathf.PI * k / caps.Length;
            g.transform.localPosition = new Vector3(Mathf.Sin(a) * (float)Generator.WheelCapsuleR, (float)Generator.WheelHubY - Mathf.Cos(a) * (float)Generator.WheelCapsuleR, 0f);
            g.AddComponent<MeshFilter>().sharedMesh = capMesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = LundunGreyboxBuilder.WorldMaterial(cap.material);
            lights.Add(CapsulePart(g.transform, "Lights", capLights, LundunGreyboxBuilder.WorldMaterial(cl.material)));
            cabinR.Add(CapsulePart(g.transform, "Cabin", capCabin, interiorMat));
            cabinLightR.Add(CapsulePart(g.transform, "CabinLight", capCabinLight, cabinLightMat));
            Renderer rr = CapsulePart(g.transform, "Riders", null, interiorMat); rr.enabled = false;
            riderFilters[k] = rr.GetComponent<MeshFilter>();
            glassR.Add(CapsulePart(g.transform, "Glass", capGlass, glassMat));           // last: see-through, drawn after the rest
            caps[k] = g.transform;
        }

        // far: the skyline version on the Landmark layer
        MeshData fr, fl, fc;
        MeshData fs = Generator.WheelFar(out fr, out fl, out fc);
        Material farBody = FarMaterial("Landmark_Far", 0), farLights = FarMaterial("Landmark_FarLights", 1), farCaps = FarMaterial("Landmark_FarCapsules", 2);
        List<Renderer> farBodies = new List<Renderer>();
        farBodies.Add(FarPart(farT, fs, farBody));
        Transform farRotor = Child(farT, "FarRotor"); farRotor.localPosition = hub;
        farBodies.Add(FarPart(farRotor, fr, farBody));
        lights.Add(FarPart(farRotor, fl, farLights));
        Renderer farCapR = FarPart(farRotor, fc, farCaps);
        if (layer >= 0) foreach (Transform t in farT.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

        // the script
        double[] b = Generator.WheelBoardSpot, x = Generator.WheelExitSpot;
        LundunWheel w = root.AddComponent<LundunWheel>();
        w.SetData(rotor, farRotor, caps, nearT.gameObject, farT.gameObject, lights.ToArray(), farBodies.ToArray(),
                  (float)Generator.WheelHubY, (float)Generator.WheelCapsuleR, new Vector3((float)b[0], (float)b[1], (float)b[2]), new Vector3((float)x[0], (float)x[1], (float)x[2]));
        w.SetCabins(glassR.ToArray(), cabinR.ToArray(), cabinLightR.ToArray(), farCapR, riderFilters, riders, (float)Generator.CabinFloorY);
        EditorUtility.SetDirty(w);

        // E7b: the crowd
        LundunWheelCrowd crowd = root.AddComponent<LundunWheelCrowd>();
        List<double[]> nodes = Generator.WheelCrowdNodes(), spots = Generator.WheelCrowdSpots();
        List<int[]> edges = Generator.WheelCrowdEdges();
        Vector3[] nv = new Vector3[nodes.Count]; for (int i = 0; i < nv.Length; i++) nv[i] = V(nodes[i]);
        int[] ea = new int[edges.Count], eb = new int[edges.Count]; for (int i = 0; i < edges.Count; i++) { ea[i] = edges[i][0]; eb[i] = edges[i][1]; }
        Vector3[] sp = new Vector3[spots.Count], sl = new Vector3[spots.Count]; int[] sn = new int[spots.Count];
        for (int i = 0; i < spots.Count; i++) { sp[i] = V(spots[i]); sl[i] = new Vector3((float)spots[i][3], (float)spots[i][1], (float)spots[i][4]); sn[i] = (int)spots[i][5]; }
        crowd.SetData(nv, ea, eb, sp, sl, sn, Vs(Generator.WheelQueuePath), Vs(Generator.WheelExitPath), Generator.WheelQueueNode, Generator.WheelExitNode,
                      Generator.WheelArriveNodes, (float)Generator.WheelDoorZ, (float)Generator.WheelEdgeZ);
        EditorUtility.SetDirty(crowd);

        int verts = st.VertexCount + deck.VertexCount + plaza.VertexCount + props.VertexCount + rot.VertexCount + rl.VertexCount +
                    (cap.VertexCount + cl.VertexCount + cg.VertexCount + ci.VertexCount + cc.VertexCount) * caps.Length;
        return "The Lundun Wheel: " + (Generator.WheelHubY + Generator.WheelCapsuleR + 1.7).ToString("0") + " m tall, " + caps.Length + " capsules with lit cabins, " +
               "a riverside plaza with " + spots.Count + " places for the crowd to stop, " +
               verts + " vertices close up, a skyline version on layer " + (layer >= 0 ? LayerMask.LayerToName(layer) : "(none: add a 'Landmark' layer)") + ".";
    }

    static Vector3 V(double[] p) { return new Vector3((float)p[0], (float)p[1], (float)p[2]); }
    static Vector3[] Vs(double[][] ps) { Vector3[] v = new Vector3[ps.Length]; for (int i = 0; i < ps.Length; i++) v[i] = V(ps[i]); return v; }

    /// A child of a capsule with its own mesh and material (no shadows: lights, glass, the cabin and its riders).
    static Renderer CapsulePart(Transform capsule, string name, Mesh mesh, Material mat)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(capsule, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return r;
    }

    /// An invisible collider (walking surfaces, the keep-out wall) on a NavMesh area.
    static void Invisible(Transform parent, MeshData md, int area)
    {
        GameObject g = new GameObject(md.name); g.transform.SetParent(parent, false);
        g.AddComponent<MeshCollider>().sharedMesh = Save(md);
        LundunGreyboxBuilder.NavModifier(g, false, area);
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
        r.sharedMaterial = Mat(md.material);
        if (md.material == "WheelLights") r.shadowCastingMode = ShadowCastingMode.Off;
        if (isStatic) GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        return g;
    }

    /// A collider from the part's own mesh, on a NavMesh area (1 = not walkable).
    static GameObject Solid(GameObject g, int area)
    {
        g.AddComponent<MeshCollider>().sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
        LundunGreyboxBuilder.NavModifier(g, false, area);
        return g;
    }

    /// The Wheel's own materials (E7b), or the shared world ones.
    ///   WheelGlass       see-through glass (URP Lit, transparent); LundunWheel warms it at night
    ///   WheelInterior    the cabin and its riders: the kit's colours, glowing softly at night (LundunWheel)
    ///   WheelCabinLight  the light panel in each cabin's ceiling (LundunWheel turns it on at dusk)
    static Material Mat(string name)
    {
        if (name != "WheelGlass" && name != "WheelInterior" && name != "WheelCabinLight") return LundunGreyboxBuilder.WorldMaterial(name);
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        if (m == null) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetFloat("_Metallic", 0f);
        if (name == "WheelGlass")
        {
            m.SetColor("_BaseColor", new Color(0.62f, 0.76f, 0.86f, 0.3f));
            m.SetFloat("_Smoothness", 0.92f);
            m.SetFloat("_Surface", 1f);                 // transparent
            m.SetFloat("_Blend", 0f);                   // alpha
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 2f);                    // outside faces only: riders see straight out
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            Glow(m, Color.black);
        }
        else if (name == "WheelInterior")
        {
            Material kit = LundunGreyboxBuilder.WorldMaterial(Generator.KitMatB);
            Texture t = kit != null && kit.HasProperty("_BaseMap") ? kit.GetTexture("_BaseMap") : null;
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetTexture("_EmissionMap", t);            // lit from inside: its own colours, dimmed by day (LundunWheel)
            Glow(m, Color.black);
        }
        else
        {
            m.SetColor("_BaseColor", new Color(1f, 0.95f, 0.85f));
            m.SetFloat("_Smoothness", 0.5f);
            Glow(m, Color.black);
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Glow(Material m, Color c)
    {
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", c);
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

    /// The far materials (shader Lundun/Landmark): the body and the capsules sample the kit's atlas B for their swatch
    /// colours. kind: 0 body, 1 lights, 2 capsules (E7b: these glow warm at night, set by LundunWheel).
    static Material FarMaterial(string name, int kind)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Lundun/Landmark");
        if (sh == null) throw new System.Exception("Can't find the shader Lundun/Landmark (Assets/World/Shaders/LundunLandmark.shader).");
        if (m == null) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        if (kind != 1)
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
        if (kind == 2) m.SetFloat("_MinVisible", 0.6f);
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
