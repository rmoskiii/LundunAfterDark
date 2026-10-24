// Lundun → 28  Brixtun + Ferndale Retail Park (E1–E6)
// Builds Brixtun, south of the Ring, from Assets/World/LundunExpansion.json, under its own object
// "LundunExpansion/Brixtun" (the city's menus never touch it, and it never touches theirs):
//   Roads, Junctions, Parks (Windward Square, Brockmere Park), Buildings (Victorian parades with shops, terraces,
//   two 1930s department stores, the Regal Hall, the library, the Angel Fields estate)
//   Detail   lamps, street dressing, markings and grit, shop light (switched off far away by LundunStreaming)
//   Colliders  the dressing's solid bits
//   NavLinks   where Brixtun's roads cross onto the Ring (the city and Brixtun bake their NavMesh separately)
//   E2: Brixtun station on the viaduct (built with the railway, in World/Railway): the train now runs Rye Lane ⇄ Brixtun
//   E3: Parked (static parked cars + bays kept for real ones), PoliceSpawns, bike docks (in the dressing), and Market:
//       the stalls (in the dressing) and LundunMarketCrowd, the traders and shoppers
//   Map: Brixtun station, the market, Windward Square, the Regal Hall, the department stores and the library
//   E4: RetailPark: Ferndale Retail Park in the south-east (Basketful, Grocera, ValuStack, FLATPAK Home Store, their car
//       parks and yards, the wasteland, ramps and ruins; its parked cars in RetailParked, its fences, containers and signs
//       in the dressing); the stores and the park on the map
//   E5: Shops: walk-in Basketful, Grocera and ValuStack (LundunShopInterior: the inside switched on near the store,
//       sliding doors, shoppers and staff, things to pick up or steal, displays and chiller glass that break)
//   E6: FLATPAK Home Store walk-in too: two floors (ground: market hall, self-serve warehouse, tills, bistro; first:
//       the showroom's room sets and the café), escalators that carry you, stairs, two lifts; each floor's detail drawn
//       only when you're on it; on the map with its own icon
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
    const string ParkedPath = Dir + "/BrixtunParked.asset";
    const string RetailParkedPath = Dir + "/RetailParked.asset";
    const string AtlasPath = Dir + "/BrixtunDressingAtlas.png";
    const string GenDir = "Assets/World/Generated";
    const string RootName = "LundunExpansion", AreaName = "Brixtun";
    const string DetailPath = RootName + "/" + AreaName + "/Detail";
    const float PaveTop = 0.15f;

    static Mesh container;

    [MenuItem("Lundun/28  Brixtun + Retail Park (E1–E6)", false, 34)]
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

            // E3: street life
            EditorUtility.DisplayProgressBar("Lundun", "Parked cars, police, bikes, the market…", 0.85f);
            Vector4[] bays; Renderer[] parked;
            int cars = BuildParked(area, xs, out bays, out parked);
            Transform[] police = BuildPoliceSpawns(area, x);
            Vector4[] docks = new Vector4[xd.bikeDocks.Count], docksOut = new Vector4[xd.bikeDocks.Count];
            for (int i = 0; i < docks.Length; i++)
            {
                BikeDockInfo k = xd.bikeDocks[i];
                docks[i] = new Vector4((float)k.x, (float)k.z, (float)k.alongX, (float)k.alongZ);
                docksOut[i] = new Vector4((float)k.outX, (float)k.outZ, 0f, 0f);
            }
            int stalls = BuildMarket(area, xd);
            EditorUtility.DisplayProgressBar("Lundun", "Fitting out the supermarkets and FLATPAK…", 0.9f);
            string shops = BuildShops(area);
            List<Generator.MapPlace> places = Generator.ExpansionPlaces();
            foreach (Generator.MapPlace mp in places)
                LundunExpansionBuilder.Register(mp.id, mp.id == "RAIL_BRIXTUN" ? LundunTrain.BrixtunName : mp.name + ((mp.kind == "shop" || mp.kind == "homestore") && WalkIn(mp.id) ? " (walk in)" : ""), mp.kind, new Vector3((float)mp.x, 0f, (float)mp.z), mp.label);
            string retail = shops.Length > 0 ? "\nE5: " + shops : "";
            foreach (string l in x.log) if (l.StartsWith(Generator.RetailParkName)) retail = "\nE4: " + l + "." + retail;

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
            rt.SetStreetLife(docks, docksOut, bays, parked, police);
            EditorUtility.SetDirty(rt);

            StreamDetail();
            AssetDatabase.SaveAssets();
            log = "[Lundun] E1 Brixtun: " + string.Join("\n", x.log.ToArray()) + "\n" + parts + " street / building pieces, " + dressing + " dressing pieces, " +
                  streets + " marking and litter blocks, " + spills + " shop lights, " + links + " NavMesh links onto the Ring, stations: " + string.Join(", ", names) +
                  ".\nE3: " + cars + " parked cars (" + bays.Length + " bays kept for real ones), " + police.Length + " police spawn points, " + docks.Length +
                  " bike docks, " + stalls + " market stalls; on the map: " + places.Count + " places." + retail +
                  "\nCity side: " + marks + " marking blocks, " + pieces + " junction and railway pieces (with Brixtun station), the lamps.";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.ClearProgressBar();
            Dialog("Brixtun failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.");
            return;
        }
        EditorUtility.ClearProgressBar();

        LundunRailwayBuilder.BuildTrain();         // the line's south end has moved; E2: Rye Lane ⇄ Brixtun
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
            if (md.group == "Roads" || md.group == "Junctions" || (md.group == "Parks" && md.material == "Park") || md.name.StartsWith("SquarePaving") || md.name.StartsWith("RP_CarParks") || md.name.StartsWith("RP_Paving") || md.name.StartsWith("RP_Markings") ||
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

    // ------------------------------------------------------------------ E3: street life
    /// Brixtun's parked cars, baked like the city's (Lundun → 23) into its own asset; the bays kept for real cars go to
    /// LundunParking at runtime (via LundunExpansionArea).
    static int BuildParked(Transform area, StreetResult xs, out Vector4[] bays, out Renderer[] parked)
    {
        bays = new Vector4[0]; parked = new Renderer[0];
        List<Vector4> allBays = new List<Vector4>(); List<Renderer> allParked = new List<Renderer>();
        int total = 0;
        // the streets' cars, and (E4) the retail park's, each in its own asset (they're big)
        List<ParkSlot>[] sets = { Generator.ParkingSlots(xs), Generator.RetailParkSlots() };
        string[] roots = { "Parked", "RetailParked" }, paths = { ParkedPath, RetailParkedPath };
        for (int i = 0; i < sets.Length; i++)
        {
            if (sets[i].Count == 0) { AssetDatabase.DeleteAsset(paths[i]); continue; }
            Transform root = Child(area, roots[i]);
            List<Renderer> renderers; List<Vector4> free; int cars, painted; List<string> missing;
            if (!LundunParkedBuilder.Bake(sets[i], root, paths[i], out renderers, out free, out cars, out painted, out missing))
            {
                Debug.LogWarning("[Lundun] No car prefabs in Assets/Vehicles/Prefabs (Lundun → 10): Brixtun has no parked cars.");
                return 0;
            }
            allBays.AddRange(free); allParked.AddRange(renderers); total += cars;
        }
        bays = allBays.ToArray(); parked = allParked.ToArray();
        return total;
    }

    /// Fixed police spawn points along Brixtun's roads (the fallback when PoliceDirector can't find a road spot).
    static Transform[] BuildPoliceSpawns(Transform area, GreyboxResult x)
    {
        Transform root = Child(area, "PoliceSpawns");
        List<Transform> list = new List<Transform>();
        foreach (Marker m in x.markers)
        {
            if (m.group != "PoliceSpawns") continue;
            GameObject g = new GameObject("SP_POLICE_BRX_" + (list.Count + 1).ToString("00"));
            g.transform.SetParent(root, false);
            g.transform.SetPositionAndRotation(new Vector3((float)m.x, (float)m.y, (float)m.z), Quaternion.Euler(0f, (float)m.yaw, 0f));
            list.Add(g.transform);
        }
        return list.ToArray();
    }

    /// The market's people: where they walk, the spot in front of each stall, the traders' places.
    static int BuildMarket(Transform area, DressingResult xd)
    {
        MarketInfo mi = xd.market;
        if (mi == null || mi.stalls.Count == 0) return 0;
        const float y = PaveTop;
        GameObject go = new GameObject("Market");
        go.transform.SetParent(area, false);
        Vector3[] nodes = new Vector3[mi.nodes.Count];
        for (int i = 0; i < nodes.Length; i++) nodes[i] = new Vector3((float)mi.nodes[i].x, y, (float)mi.nodes[i].z);
        int[] ea = new int[mi.edges.Count], eb = new int[mi.edges.Count];
        for (int i = 0; i < ea.Length; i++) { ea[i] = mi.edges[i][0]; eb[i] = mi.edges[i][1]; }
        Vector3[] spots = new Vector3[mi.spots.Count], look = new Vector3[mi.spots.Count]; int[] sn = new int[mi.spots.Count];
        for (int i = 0; i < spots.Length; i++)
        {
            double[] s = mi.spots[i];
            spots[i] = new Vector3((float)s[0], y, (float)s[1]); look[i] = new Vector3((float)s[2], y, (float)s[3]); sn[i] = (int)s[4];
        }
        Vector3[] tAt = new Vector3[mi.traders.Count], tFace = new Vector3[mi.traders.Count];
        for (int i = 0; i < tAt.Length; i++)
        {
            double[] t = mi.traders[i];
            tAt[i] = new Vector3((float)t[0], y, (float)t[1]); tFace[i] = new Vector3((float)t[2], 0f, (float)t[3]);
        }
        LundunMarketCrowd crowd = go.AddComponent<LundunMarketCrowd>();
        crowd.SetData(nodes, ea, eb, spots, look, sn, mi.gates.ToArray(), tAt, tFace);
        EditorUtility.SetDirty(crowd);
        return mi.stalls.Count;
    }

    // ------------------------------------------------------------------ E5: the walk-in supermarkets
    const string ShopAtlasPath = Dir + "/ShopAtlas.png", ShopFloorPath = Dir + "/ShopFloor.png", ShopCeilingPath = Dir + "/ShopCeiling.png";
    const string HomeAtlasPath = Dir + "/HomeAtlas.png", HomeConcretePath = Dir + "/HomeConcrete.png", HomeStepsPath = Dir + "/HomeSteps.png";
    static Dictionary<string, Material> shopMats;

    static bool WalkIn(string placeId)
    {
        foreach (string s in new[] { "SHOP_BASKETFUL", "SHOP_GROCERA", "SHOP_VALUSTACK", "SHOP_FLATPAK" }) if (placeId == s) return true;
        return false;
    }

    static string BuildShops(Transform area)
    {
        List<ShopInterior> list = Generator.ShopInteriors();
        if (list.Count == 0) return "";
        ShopMaterials();
        Transform root = Child(area, "Shops");
        List<string> parts = new List<string>();
        foreach (ShopInterior si in list)
        {
            GameObject shop = new GameObject("Shop_" + si.id);
            shop.transform.SetParent(root, false);
            Transform shell = Child(shop.transform, "Shell"), detail = Child(shop.transform, "Detail");
            // E6: a store with a first floor keeps each floor's detail under its own object (Detail/L0, Detail/L1)
            bool two = si.upperY > 0;
            Transform[] lv = two ? new[] { Child(detail, "L0"), Child(detail, "L1") } : new[] { detail };
            float upper = (float)si.upperY;
            Func<double, int> levelOf = yb => two && yb >= upper - 0.6 ? 1 : 0;
            // shell: always there (floor and walls solid)
            foreach (MeshData md in si.shell) ShopMesh(md, shell, md.collider, md.navArea);
            // E6: the slopes of the stairs and escalators, the balustrades: colliders only (walkable)
            foreach (MeshData md in si.walk)
            {
                if (md.tri.Count == 0) continue;
                GameObject g = new GameObject(md.name);
                g.transform.SetParent(shell, false);
                g.AddComponent<MeshCollider>().sharedMesh = Save(LundunLightingDistrictBuilder.ToMesh(md));
                LundunGreyboxBuilder.NavModifier(g, false, md.navArea);
                GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
            }
            // E6: the escalators' moving treads (in the shell: you see them from both floors)
            List<Renderer> treads = new List<Renderer>();
            foreach (MeshData md in si.steps)
            {
                GameObject g = ShopMesh(md, shell, false, -1);
                GameObjectUtility.SetStaticEditorFlags(g, (StaticEditorFlags)0);        // its texture scrolls; keep it its own
                Renderer r = g.GetComponent<Renderer>(); if (r != null) treads.Add(r);
            }
            // detail: fittings and lights (no colliders: the fixtures get boxes), the fixtures' colliders
            foreach (MeshData md in si.detail) ShopMesh(md, lv[0], false, -1);
            foreach (MeshData md in si.upper) ShopMesh(md, lv[two ? 1 : 0], false, -1);
            Transform[] cols = new Transform[lv.Length], brk = new Transform[lv.Length], items = new Transform[lv.Length];
            for (int k = 0; k < lv.Length; k++) { cols[k] = Child(lv[k], "Colliders"); brk[k] = Child(lv[k], "Breakables"); items[k] = Child(lv[k], "Items"); }
            foreach (DressBox c in si.colliders)
            {
                GameObject g = new GameObject(c.name);
                g.transform.SetParent(cols[levelOf(c.cy - c.sy / 2)], false);
                g.transform.SetPositionAndRotation(new Vector3((float)c.cx, (float)c.cy, (float)c.cz), Quaternion.Euler(0f, (float)c.yaw, 0f));
                g.AddComponent<BoxCollider>().size = new Vector3((float)c.sx, (float)c.sy, (float)c.sz);
            }
            // things that break
            foreach (ShopProp p in si.breakables)
            {
                GameObject g = ShopMesh(p.mesh, brk[levelOf(p.cy - p.sy / 2)], false, -1, false, p.kind == "glass");     // stacks stay in the NavMesh bake (people go round them)
                BoxCollider bc = g.AddComponent<BoxCollider>();
                bc.center = new Vector3((float)p.cx, (float)p.cy, (float)p.cz);
                bc.size = new Vector3((float)p.sx, (float)p.sy, (float)Math.Max(0.1, p.sz));
                LundunBreakable b = g.AddComponent<LundunBreakable>();
                b.SetData(p.kind == "glass" ? LundunBreakable.Kind.Glass : LundunBreakable.Kind.Stack,
                          new Vector4((float)p.debrisUV[0], (float)p.debrisUV[1], (float)p.debrisUV[2], (float)p.debrisUV[3]));
                EditorUtility.SetDirty(b);
            }
            // things to take
            foreach (ShopItem it in si.items)
            {
                GameObject g = ShopMesh(it.mesh, items[levelOf(it.y)], false, -1);
                LundunShopItem c = g.AddComponent<LundunShopItem>();
                c.SetData(it.id, it.name, it.steal, new Vector3((float)it.x, (float)it.y, (float)it.z));
                EditorUtility.SetDirty(c);
            }
            // the sliding doors (always there: you see them from outside); E6: and the lifts' steel doors on each floor
            List<Transform> lefts = new List<Transform>(), rights = new List<Transform>(); List<Vector3> centres = new List<Vector3>();
            List<float> halves = new List<float>(), reach = new List<float>();
            foreach (ShopDoor d in si.doors)
            {
                GameObject door = new GameObject(d.glass ? "Doors" : "LiftDoors");
                door.transform.SetParent(shop.transform, false);
                door.transform.SetPositionAndRotation(new Vector3((float)d.x, (float)d.y, (float)d.z), Quaternion.LookRotation(new Vector3((float)d.ox, 0f, (float)d.oz)));
                foreach (MeshData leaf in new[] { d.left, d.right })
                {
                    GameObject g = ShopMesh(leaf, door.transform, false, -1, true);
                    BoxCollider bc = g.AddComponent<BoxCollider>();
                    bool isLeft = leaf == d.left;
                    bc.center = new Vector3(isLeft ? -(float)d.half / 2 : (float)d.half / 2, (float)d.top / 2 + 0.08f, 0f);
                    bc.size = new Vector3((float)d.half, (float)d.top - 0.16f, 0.06f);
                    if (d.glass)
                    {
                        LundunBreakable b = g.AddComponent<LundunBreakable>();
                        b.SetData(LundunBreakable.Kind.Glass, new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
                    }
                    (isLeft ? lefts : rights).Add(g.transform);
                }
                centres.Add(door.transform.position);
                halves.Add((float)d.half); reach.Add((float)d.reach);
            }
            // the people
            GameObject people = new GameObject("People");
            people.transform.SetParent(detail, false);
            MarketInfo mi = si.crowd;
            const float y = 0.16f;
            Vector3[] nodes = new Vector3[mi.nodes.Count];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = new Vector3((float)mi.nodes[i].x, i < si.nodeY.Count ? (float)si.nodeY[i] : y, (float)mi.nodes[i].z);
            int[] ea = new int[mi.edges.Count], eb = new int[mi.edges.Count];
            for (int i = 0; i < ea.Length; i++) { ea[i] = mi.edges[i][0]; eb[i] = mi.edges[i][1]; }
            Vector3[] spots = new Vector3[mi.spots.Count], look = new Vector3[mi.spots.Count]; int[] sn = new int[mi.spots.Count];
            for (int i = 0; i < spots.Length; i++)
            {
                double[] sp = mi.spots[i]; float sy = sp.Length > 5 ? (float)sp[5] : y;
                spots[i] = new Vector3((float)sp[0], sy, (float)sp[1]); look[i] = new Vector3((float)sp[2], sy, (float)sp[3]); sn[i] = (int)sp[4];
            }
            Vector3[] tAt = new Vector3[mi.traders.Count], tFace = new Vector3[mi.traders.Count];
            for (int i = 0; i < tAt.Length; i++) { double[] t = mi.traders[i]; tAt[i] = new Vector3((float)t[0], t.Length > 4 ? (float)t[4] : y, (float)t[1]); tFace[i] = new Vector3((float)t[2], 0f, (float)t[3]); }
            LundunMarketCrowd crowd = people.AddComponent<LundunMarketCrowd>();
            crowd.SetData(nodes, ea, eb, spots, look, sn, mi.gates.ToArray(), tAt, tFace);
            if (two) crowd.SetTuning(110f, 130f, 0.8f, 8, 16);          // E6: a big store: more people, and they run while you're anywhere inside
            else crowd.SetTuning(60f, 75f, 0.55f, 6, tAt.Length);
            EditorUtility.SetDirty(crowd);
            // the store itself
            LundunShopInterior rt = shop.AddComponent<LundunShopInterior>();
            rt.SetData(si.name, new Vector3((float)si.x0, 0f, (float)si.z0), new Vector3((float)si.x1, 0f, (float)si.z1), detail.gameObject,
                       lefts.ToArray(), rights.ToArray(), centres.ToArray(), (float)Generator.DoorHalf);
            if (two)
            {
                Vector4[] wells = new Vector4[si.wells.Count];
                for (int i = 0; i < wells.Length; i++) wells[i] = new Vector4((float)si.wells[i][0], (float)si.wells[i][1], (float)si.wells[i][2], 0f);
                rt.SetLevels(new[] { lv[0].gameObject, lv[1].gameObject }, upper, wells, (float)si.insideTop, halves.ToArray(), reach.ToArray());
                List<Vector3> path = new List<Vector3>(); List<float> half = new List<float>();
                foreach (double[] e in si.escalators)
                {
                    for (int k = 0; k < 4; k++) path.Add(new Vector3((float)e[k * 3], (float)e[k * 3 + 1], (float)e[k * 3 + 2]));
                    half.Add((float)e[12]);
                }
                rt.SetEscalators(path.ToArray(), half.ToArray(), treads.ToArray());
                Vector3[] at = new Vector3[si.lifts.Count]; List<float> floors = new List<float>(); List<string> names = new List<string>();
                for (int i = 0; i < at.Length; i++)
                {
                    ShopLift l = si.lifts[i];
                    at[i] = new Vector3((float)l.x, 0f, (float)l.z);
                    foreach (double f in l.floors) floors.Add((float)f);
                    names.AddRange(l.names);
                }
                rt.SetLifts(at, floors.ToArray(), names.ToArray());
            }
            EditorUtility.SetDirty(rt);
            parts.AddRange(si.log);
        }
        return string.Join("; ", parts.ToArray()) + ".";
    }

    /// One of a shop's meshes as a GameObject (the shop materials, no shadows from the flat bits).
    static GameObject ShopMesh(MeshData md, Transform parent, bool collider, int navArea, bool keepLocal = false, bool ignoreNav = true)
    {
        GameObject go = new GameObject(md.name);
        go.transform.SetParent(parent, keepLocal);
        if (keepLocal) { go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; }
        if (md.tri.Count == 0) return go;
        Mesh mesh = Save(LundunLightingDistrictBuilder.ToMesh(md));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        Material m;
        mr.sharedMaterial = shopMats.TryGetValue(md.material, out m) ? m : LundunGreyboxBuilder.WorldMaterial(md.material);
        mr.shadowCastingMode = md.material == Generator.ShopAtlasMat && !md.name.StartsWith("Shell") ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (collider)
        {
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (navArea >= 0) LundunGreyboxBuilder.NavModifier(go, false, navArea);
        }
        else if (ignoreNav) LundunGreyboxBuilder.NavModifier(go, true, -1);
        GameObjectUtility.SetStaticEditorFlags(go, keepLocal ? (StaticEditorFlags)0 : StaticEditorFlags.BatchingStatic);
        return go;
    }

    /// The shop materials: the painted atlas and the tiled floor and ceiling, lit from within; and the glass.
    static void ShopMaterials()
    {
        DressAtlas a = Generator.ShopAtlas();
        Texture2D atlas = SaveTexture(ShopAtlasPath, a.W, a.H, a.px, false);
        Texture2D floor = SaveTexture(ShopFloorPath, 256, 256, Generator.ShopTileTexture(false, 256), true);
        Texture2D ceil = SaveTexture(ShopCeilingPath, 256, 256, Generator.ShopTileTexture(true, 256), true);
        // E6: FLATPAK's own atlas, its concrete floor, the escalator treads
        DressAtlas h = Generator.HomeAtlas();
        Texture2D home = SaveTexture(HomeAtlasPath, h.W, h.H, h.px, false);
        Texture2D conc = SaveTexture(HomeConcretePath, 256, 256, Generator.HomeConcreteTexture(256), true);
        Texture2D steps = SaveTexture(HomeStepsPath, 64, 64, Generator.HomeStepsTexture(64), true);
        shopMats = new Dictionary<string, Material>
        {
            { Generator.ShopAtlasMat, ShopMaterial(Generator.ShopAtlasMat, atlas, 0.55f, false) },
            { Generator.ShopFloorMat, ShopMaterial(Generator.ShopFloorMat, floor, 0.45f, false) },
            { Generator.ShopCeilingMat, ShopMaterial(Generator.ShopCeilingMat, ceil, 0.6f, false) },
            { Generator.ShopGlassMat, ShopMaterial(Generator.ShopGlassMat, null, 0f, true) },
            { Generator.HomeAtlasMat, ShopMaterial(Generator.HomeAtlasMat, home, 0.55f, false) },
            { Generator.HomeConcreteMat, ShopMaterial(Generator.HomeConcreteMat, conc, 0.45f, false) },
            { Generator.HomeStepsMat, ShopMaterial(Generator.HomeStepsMat, steps, 0.4f, false) },
        };
    }

    static Texture2D SaveTexture(string path, int w, int h, byte[] px, bool repeat)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] c = new Color32[w * h];
        for (int i = 0; i < c.Length; i++) c[i] = new Color32(px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]);
        t.SetPixels32(c); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None;
        ti.mipmapEnabled = true; ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear; ti.anisoLevel = repeat ? 4 : 2; ti.maxTextureSize = 4096;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material ShopMaterial(string name, Texture2D tex, float glow, bool glass)
    {
        string path = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
        if (glass)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(0.78f, 0.9f, 0.94f, 0.22f));
            m.SetFloat("_Smoothness", 0.9f);
        }
        else
        {
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", name == Generator.ShopFloorMat || name == Generator.HomeConcreteMat ? 0.55f : 0.15f);
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", Color.white * glow);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.renderQueue = (int)RenderQueue.Geometry;
        }
        m.enableInstancing = false;
        EditorUtility.SetDirty(m);
        return m;
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
