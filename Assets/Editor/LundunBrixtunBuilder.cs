// Lundun → 28  Brixtun (E1)
// Builds Brixtun, south of the Ring, from Assets/World/LundunExpansion.json, under its own object
// "LundunExpansion/Brixtun" (the city's menus never touch it, and it never touches theirs):
//   Roads, Junctions, Parks (Windward Square, Brockmere Park), Buildings (Victorian parades with shops, terraces,
//   two 1930s department stores, the Regal Hall, the library, the Angel Fields estate)
//   Detail   lamps, street dressing, markings and grit, shop light (switched off far away by LundunStreaming)
//   Colliders  the dressing's solid bits
//   NavLinks   where Brixtun's roads cross onto the Ring (the city and Brixtun bake their NavMesh separately)
// Brixtun is built like Peckam (the town centre) and Gilmore (the estate), with its own shops, postcode, tags and
// landmark buildings, its own random numbers, and its own dressing texture.
// The city's side: where Brixtun Road, Atlantic Road, Stockwell Road, Loughborough Road and Denmore Hill meet the
// Ring, only the Ring's junction plates, markings and lamps change; the railway takes its final route through Brixtun
// (on a viaduct over Stockwell Road, Brixtun Road and Atlantic Road), so the train, the railway's fences and the map
// picture are redone too.
// Afterwards: Lundun → 3 Bake Navigation (the city and Brixtun; later, Lundun → 3b does Brixtun on its own), then Cmd+S.
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

public static class LundunBrixtunBuilder
{
    const string Dir = "Assets/World/Expansions/Brixtun";
    const string MeshPath = Dir + "/BrixtunMeshes.asset";
    const string AtlasPath = Dir + "/BrixtunDressingAtlas.png";
    const string GenDir = "Assets/World/Generated";
    const string RootName = "LundunExpansion", AreaName = "Brixtun";
    const string DetailPath = RootName + "/" + AreaName + "/Detail";
    const float PaveTop = 0.15f;

    static Mesh container;

    [MenuItem("Lundun/28  Brixtun (E1)", false, 34)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(LundunWorldFiles.BasePath);
        if (json == null) { Dialog("Can't find " + LundunWorldFiles.BasePath + "."); return; }
        GameObject worldRoot = GameObject.Find("World");
        if (worldRoot == null) { Dialog("No 'World' object in the scene. Run Lundun → 1 Build Greybox World first."); return; }
        if (!LundunGreyboxBuilder.HasExpansions()) { Dialog("Assets/World/LundunExpansion.json has no Brixtun in it (copy in the E1 version)."); return; }

        string log;
        try
        {
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            EditorUtility.DisplayProgressBar("Lundun", "Painting Brixtun's signs…", 0.03f);
            LundunBuildingKitBuilder.EnsureMaterials(true);

            // 1. the city's side: the joins on the Ring, and the railway's new route
            EditorUtility.DisplayProgressBar("Lundun", "Joining Brixtun onto the Ring…", 0.08f);
            GreyboxResult city = Generator.Build(data);
            int marks = RefreshMarkings(worldRoot, city);
            int pieces = LundunExpansionBuilder.RefreshGroups(worldRoot, city, new[] { "Junctions", "Railway" });
            LundunLightingDistrictBuilder.RefreshLamps(city);
            LundunLightingDistrictBuilder.RegisterLamps(city, true);

            // 2. Brixtun
            EditorUtility.DisplayProgressBar("Lundun", "Working out Brixtun…", 0.2f);
            GreyboxResult x = Generator.BuildExpansion(data);
            LundunWorldData xw = Generator.ExpansionWorld;
            DressingResult xd = Generator.Dress(xw, x);
            StreetResult xs = Generator.Streets(xw, x, xd);

            Directory.CreateDirectory(Dir);
            AssetDatabase.ImportAsset("Assets/World/Expansions");
            GameObject top = GameObject.Find(RootName);
            if (top == null) top = new GameObject(RootName);
            Transform old = top.transform.Find(AreaName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            AssetDatabase.DeleteAsset(MeshPath);
            container = null;
            Transform area = Child(top.transform, AreaName);
            Transform detail = Child(area, "Detail");

            EditorUtility.DisplayProgressBar("Lundun", "Building Brixtun's streets and buildings…", 0.35f);
            int parts = BuildWorld(area, detail, x);
            EditorUtility.DisplayProgressBar("Lundun", "Dressing Brixtun…", 0.6f);
            Material glow, globe;
            int dressing = BuildDressing(area, detail, xd, out glow, out globe);
            EditorUtility.DisplayProgressBar("Lundun", "Markings, litter, shop light…", 0.75f);
            int streets = BuildStreets(detail, xs);
            int spills = BuildSpill(detail, x, xd);
            int links = BuildLinks(area, data);

            // the runtime bit: lamps for the real lamp lights, the Tube station, the signs' glow
            LundunExpansionArea rt = area.gameObject.AddComponent<LundunExpansionArea>();
            Vector3[] lamps = new Vector3[x.lamps.Count]; int[] styles = new int[x.lamps.Count];
            for (int i = 0; i < lamps.Length; i++) { LampInfo l = x.lamps[i]; lamps[i] = new Vector3((float)l.x, (float)l.y, (float)l.z); styles[i] = l.style; }
            string[] names = new string[xd.stations.Count]; Vector3[] tops = new Vector3[names.Length], outs = new Vector3[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                StationInfo s = xd.stations[i];
                names[i] = s.name; tops[i] = new Vector3((float)s.x, 0f, (float)s.z);
                float yr = (float)s.yaw * Mathf.Deg2Rad; outs[i] = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
            }
            rt.SetData(AreaName, lamps, styles, names, tops, outs);
            rt.SetLook(glow, globe);
            EditorUtility.SetDirty(rt);

            StreamDetail();
            AssetDatabase.SaveAssets();
            log = "[Lundun] E1 Brixtun: " + string.Join("\n", x.log.ToArray()) + "\n" + parts + " street / building pieces, " + dressing + " dressing pieces, " +
                  streets + " marking and litter blocks, " + spills + " shop lights, " + links + " NavMesh links onto the Ring, stations: " + string.Join(", ", names) +
                  ".\nCity side: " + marks + " marking blocks, " + pieces + " junction and railway pieces, the lamps.";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.ClearProgressBar();
            Dialog("Brixtun failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.");
            return;
        }
        EditorUtility.ClearProgressBar();

        LundunRailwayBuilder.BuildTrain();         // the line's south end has moved
        LundunDressingBuilder.Build();             // the railway's fences follow the new route
        LundunMapSharpBuilder.Build();             // Brixtun on the map picture
        LundunLightingDistrictBuilder.ApplyLook();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log(log + "\nNext: Lundun → 3 Bake Navigation, then Cmd+S.");
    }

    // ------------------------------------------------------------------ the city's side
    /// Swaps World/Roads' markings for the new ones (the same everywhere except across the joins).
    static int RefreshMarkings(GameObject worldRoot, GreyboxResult city)
    {
        Transform roads = worldRoot.transform.Find("Roads");
        if (roads == null) throw new Exception("World/Roads not found. Run Lundun → 1 Build Greybox World first.");
        for (int i = roads.childCount - 1; i >= 0; i--) if (roads.GetChild(i).name.StartsWith("Markings")) Object.DestroyImmediate(roads.GetChild(i).gameObject);
        string path = GenDir + "/Group_RoadMarkings.asset";
        AssetDatabase.DeleteAsset(path);
        Mesh box = null; int n = 0;
        foreach (MeshData md in city.meshes)
        {
            if (md.group != "Roads" || !md.name.StartsWith("Markings") || md.tri.Count == 0) continue;
            Mesh mesh = LundunLightingDistrictBuilder.ToMesh(md);
            if (box == null) { AssetDatabase.CreateAsset(mesh, path); box = mesh; } else AssetDatabase.AddObjectToAsset(mesh, box);
            GameObject go = new GameObject(md.name);
            go.transform.SetParent(roads, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(md.material);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            LundunGreyboxBuilder.NavModifier(go, true, -1);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ Brixtun's streets and buildings
    static int BuildWorld(Transform area, Transform detail, GreyboxResult x)
    {
        Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
        int n = 0;
        foreach (MeshData md in x.meshes)
        {
            if (md.tri.Count == 0) continue;
            if (md.material == "Pavement") PavementUVs(md);
            bool isDetail = md.group == "Lamps";
            Transform parent;
            string key = (isDetail ? "D/" : "") + md.group;
            if (!groups.TryGetValue(key, out parent)) { parent = Child(isDetail ? detail : area, md.group); groups[key] = parent; }
            Mesh mesh = Save(LundunLightingDistrictBuilder.ToMesh(md));
            GameObject go = new GameObject(md.name);
            go.transform.SetParent(parent, false);
            if (md.material == "ColliderOnly")
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                if (md.navArea >= 0) LundunGreyboxBuilder.NavModifier(go, false, md.navArea);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                continue;
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(md.material);
            if (md.group == "Roads" || md.group == "Junctions" || (md.group == "Parks" && md.material == "Park") || md.name.StartsWith("SquarePaving") ||
                md.material.StartsWith("LightPool") || md.material.StartsWith("LampHead"))
                mr.shadowCastingMode = ShadowCastingMode.Off;
            if (md.collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (md.navIgnore) LundunGreyboxBuilder.NavModifier(go, true, -1);
            else if (md.navArea >= 0) LundunGreyboxBuilder.NavModifier(go, false, md.navArea);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            n++;
        }
        if (x.buildings.Count > 0) LundunBuildingKitBuilder.RegisterWindowGlow();
        return n;
    }

    /// The paving texture along each street, in its district's band (what Lundun → 20 does for the city).
    static void PavementUVs(MeshData m)
    {
        for (int t = 0; t + 2 < m.tri.Count; t += 3)
        {
            int i0 = m.tri[t], i1 = m.tri[t + 1], i2 = m.tri[t + 2];
            double ax = m.pos[i1 * 3] - m.pos[i0 * 3], ay = m.pos[i1 * 3 + 1] - m.pos[i0 * 3 + 1], az = m.pos[i1 * 3 + 2] - m.pos[i0 * 3 + 2];
            double bx = m.pos[i2 * 3] - m.pos[i0 * 3], by = m.pos[i2 * 3 + 1] - m.pos[i0 * 3 + 1], bz = m.pos[i2 * 3 + 2] - m.pos[i0 * 3 + 2];
            double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx, nl = Math.Sqrt(nx * nx + ny * ny + nz * nz) + 1e-12;
            bool side = Math.Abs(ny / nl) < 0.5;
            foreach (int i in new[] { i0, i1, i2 })
            {
                double u, v;
                if (Generator.PavementUV(m.pos[i * 3], m.pos[i * 3 + 2], side, out u, out v)) { m.uv[i * 2] = (float)u; m.uv[i * 2 + 1] = (float)v; }
            }
        }
    }

    // ------------------------------------------------------------------ dressing (its own texture)
    static int BuildDressing(Transform area, Transform detail, DressingResult r, out Material glow, out Material globe)
    {
        Texture2D atlas = SaveAtlas(r.atlas);
        Material solid = DressMaterial("Dress_Solid_Brixtun", atlas, false, false);
        Material cut = DressMaterial("Dress_Cutout_Brixtun", atlas, true, false);
        glow = DressMaterial("Dress_Glow_Brixtun", atlas, false, true);
        Dictionary<string, Material> mats = new Dictionary<string, Material> { { "DressSolid", solid }, { "DressCutout", cut }, { "DressGlow", glow } };
        Transform blocks = Child(detail, "Dressing"), props = Child(detail, "Props"), cols = Child(area, "Colliders");
        int n = 0;
        foreach (MeshData md in r.meshes)
        {
            if (md.tri.Count == 0) continue;
            GameObject go = new GameObject(md.name);
            go.transform.SetParent(blocks, false);
            go.AddComponent<MeshFilter>().sharedMesh = Save(LundunLightingDistrictBuilder.ToMesh(md));
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mats[md.material];
            mr.shadowCastingMode = md.material == "DressSolid" ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            n++;
        }
        n += BakePrefabs(r.prefabs, props, out globe);
        foreach (DressBox c in r.colliders)
        {
            GameObject g = new GameObject(c.name);
            g.transform.SetParent(cols, false);
            g.transform.SetPositionAndRotation(new Vector3((float)c.cx, (float)c.cy, (float)c.cz), Quaternion.Euler(0f, (float)c.yaw, 0f));
            g.AddComponent<BoxCollider>().size = new Vector3((float)c.sx, (float)c.sy, (float)c.sz);
        }
        return n;
    }

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

    /// The same three materials as the city's dressing (Lundun → 13), on Brixtun's texture.
    static Material DressMaterial(string name, Texture2D atlas, bool cutout, bool glow)
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
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON");
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

    /// Your Belisha / BusStop / PhoneBox prefabs, merged per 100 m block per material (as Lundun → 13 does). The Belisha
    /// globes use the city's blinking globe material (made by Lundun → 13).
    static int BakePrefabs(List<PropPlacement> list, Transform parent, out Material globeMat)
    {
        globeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Dressing/Dress_BelishaGlobe.mat");
        Dictionary<string, List<PropPlacement>> byKind = new Dictionary<string, List<PropPlacement>>();
        foreach (PropPlacement p in list) { List<PropPlacement> l; if (!byKind.TryGetValue(p.kind, out l)) byKind[p.kind] = l = new List<PropPlacement>(); l.Add(p); }
        Dictionary<string, Dictionary<Material, List<CombineInstance>>> buckets = new Dictionary<string, Dictionary<Material, List<CombineInstance>>>();
        foreach (var kv in byKind)
        {
            string path = "Assets/Prefabs/" + kv.Key + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { Debug.LogWarning("[Lundun] " + path + " not found — " + kv.Value.Count + " " + kv.Key + " skipped in Brixtun."); continue; }
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
                        if (kv.Key == "Belisha" && mf.name == "Globe" && globeMat != null) m = globeMat;
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
                Save(mesh);
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

    static int Block(double v) { return (int)Math.Floor((v - Generator.ChunkOrigin) / Generator.ChunkSize); }

    // ------------------------------------------------------------------ markings, litter, setts, puddles (Lundun → 22's materials)
    static int BuildStreets(Transform detail, StreetResult r)
    {
        const string sd = "Assets/World/Streets/";
        Material decal = AssetDatabase.LoadAssetAtPath<Material>(sd + Generator.StreetDecalMat + ".mat");
        Material setts = AssetDatabase.LoadAssetAtPath<Material>(sd + Generator.StreetSettsMat + ".mat");
        Material wet = AssetDatabase.LoadAssetAtPath<Material>(sd + Generator.StreetWetMat + ".mat");
        if (decal == null) { Debug.LogWarning("[Lundun] Street materials not found (run Lundun → 22 Streets once): Brixtun's road markings and litter skipped."); return 0; }
        Transform root = Child(detail, "Streets");
        int n = 0;
        foreach (MeshData md in r.meshes)
        {
            if (md.tri.Count == 0) continue;
            Material m = md.material == Generator.StreetSettsMat ? setts : md.material == Generator.StreetWetMat ? wet : decal;
            if (m == null) continue;
            GameObject go = new GameObject(md.name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = Save(LundunLightingDistrictBuilder.ToMesh(md));
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            n++;
        }
        return n;
    }

    // ------------------------------------------------------------------ light from shops and pubs (as Lundun → 24)
    static int BuildSpill(Transform detail, GreyboxResult world, DressingResult dressed)
    {
        string[] mats = { "LightPool_Shop", "LightPool_Pub", "LightPool_Lobby" };
        List<double[]> spots = new List<double[]>(dressed.shopLights);
        Dictionary<string, List<Vector3>> verts = new Dictionary<string, List<Vector3>>();
        Dictionary<string, List<Vector2>> uvs = new Dictionary<string, List<Vector2>>();
        foreach (double[] s in spots)
        {
            int kind = Mathf.Clamp((int)s[5], 0, 2);
            Vector3 f = new Vector3((float)s[0], PaveTop + 0.03f, (float)s[1]);
            Vector3 nrm = new Vector3((float)s[2], 0f, (float)s[3]).normalized;
            Vector3 t = new Vector3(-nrm.z, 0f, nrm.x);
            float ha = (float)s[4] * 0.6f, depth = kind == 1 ? 3.0f : 3.4f;
            string key = kind + "_" + Mathf.FloorToInt((f.x + 300f) / 100f) + "_" + Mathf.FloorToInt((f.z + 300f) / 100f);
            List<Vector3> v; List<Vector2> u;
            if (!verts.TryGetValue(key, out v)) { v = new List<Vector3>(); verts[key] = v; u = new List<Vector2>(); uvs[key] = u; }
            else u = uvs[key];
            Vector3 back = f - nrm * 0.15f, front = f + nrm * depth;
            v.Add(back - t * ha); u.Add(new Vector2(0f, 0.48f));
            v.Add(back + t * ha); u.Add(new Vector2(1f, 0.48f));
            v.Add(front + t * ha); u.Add(new Vector2(1f, 1f));
            v.Add(front - t * ha); u.Add(new Vector2(0f, 1f));
        }
        Transform root = Child(detail, "ShopLight");
        List<string> keys = new List<string>(verts.Keys); keys.Sort();
        foreach (string key in keys)
        {
            List<Vector3> v = verts[key];
            int[] tri = new int[v.Count / 4 * 6];
            for (int q = 0, k = 0; q < v.Count; q += 4)
            {
                tri[k++] = q; tri[k++] = q + 2; tri[k++] = q + 1;
                tri[k++] = q; tri[k++] = q + 3; tri[k++] = q + 2;
            }
            Mesh mesh = new Mesh { name = "ShopLight_" + key, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetUVs(0, uvs[key]); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Save(mesh);
            GameObject go = new GameObject(mesh.name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LundunGreyboxBuilder.WorldMaterial(mats[int.Parse(key.Substring(0, 1))]);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }
        return spots.Count;
    }

    // ------------------------------------------------------------------ NavMesh links across the seam
    /// The city and Brixtun bake their NavMesh separately (Lundun → 3 / 3b), split just south of the Ring. Every Brixtun
    /// road that crosses the split gets a link the width of its carriageway and pavements, so people and police cars
    /// can still go between them.
    static int BuildLinks(Transform area, LundunWorldData data)
    {
        Type linkType = FindType("Unity.AI.Navigation.NavMeshLink");
        if (linkType == null) { Debug.LogWarning("[Lundun] AI Navigation's NavMeshLink not found — no links across the seam."); return 0; }
        Transform root = Child(area, "NavLinks");
        Dictionary<string, float> pave = new Dictionary<string, float>();
        foreach (LwRoadClass c in data.roadClasses) pave[c.name] = c.pavement;
        foreach (LwRoadClass c in data.expansion.roadClasses) pave[c.name] = c.pavement;
        float seam = LundunGreyboxBuilder.NavSeamZ;
        int n = 0;
        foreach (LwRoad r in data.expansion.roads)
            for (int i = 0; i + 1 < r.points.Length; i++)
            {
                Vector3 a = new Vector3(r.points[i].x, 0f, r.points[i].z), b = new Vector3(r.points[i + 1].x, 0f, r.points[i + 1].z);
                if ((a.z - seam) * (b.z - seam) > 0f || Mathf.Approximately(a.z, b.z)) continue;
                float t = (seam - a.z) / (b.z - a.z);
                Vector3 p = Vector3.Lerp(a, b, t), d = (b - a).normalized;
                if (d.z > 0f) d = -d;                                  // pointing south, into Brixtun
                float p2; pave.TryGetValue(r.roadClass, out p2);
                GameObject g = new GameObject("Link_" + r.name);
                g.transform.SetParent(root, false);
                g.transform.SetPositionAndRotation(p + Vector3.up * 0.1f, Quaternion.LookRotation(d));
                Component link = g.AddComponent(linkType);
                SerializedObject so = new SerializedObject(link);
                SetVec(so, "m_StartPoint", new Vector3(0f, 0f, -3f));
                SetVec(so, "m_EndPoint", new Vector3(0f, 0f, 3f));
                SetFloat(so, "m_Width", r.width + 2f * p2 - 1f);
                SetBool(so, "m_Bidirectional", true);
                SetInt(so, "m_Area", 0);
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
        return n;
    }

    static void SetVec(SerializedObject so, string p, Vector3 v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.vector3Value = v; }
    static void SetFloat(SerializedObject so, string p, float v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.floatValue = v; }
    static void SetBool(SerializedObject so, string p, bool v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.boolValue = v; }
    static void SetInt(SerializedObject so, string p, int v) { SerializedProperty sp = so.FindProperty(p); if (sp != null) sp.intValue = v; }

    // ------------------------------------------------------------------ helpers
    /// LundunStreaming switches Brixtun's fine detail off far away, like the city's.
    static void StreamDetail()
    {
        LundunStreaming st = Object.FindAnyObjectByType<LundunStreaming>(FindObjectsInactive.Include);
        if (st == null) return;
        SerializedObject so = new SerializedObject(st);
        SerializedProperty roots = so.FindProperty("detailRoots");
        if (roots == null) return;
        for (int i = 0; i < roots.arraySize; i++) if (roots.GetArrayElementAtIndex(i).stringValue == DetailPath) return;
        roots.arraySize++;
        roots.GetArrayElementAtIndex(roots.arraySize - 1).stringValue = DetailPath;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static Mesh Save(Mesh mesh)
    {
        if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; } else AssetDatabase.AddObjectToAsset(mesh, container);
        return mesh;
    }

    static Transform Child(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t != null) return t;
        t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
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

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }
}
