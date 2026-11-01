// Lundun → 29  Kingsmere, the royal quarter (E8)
// Builds Kingsmere, north of the Ring, from Assets/World/LundunExpansion.json, under its own object
// "LundunExpansion/Kingsmere" (the city's menus never touch it, nor does Brixtun's, and it never touches theirs):
//   Roads, Junctions, Parks (St Jarvis's Park, the palace gardens, the Memorial's island), Buildings (grand stucco
//   terraces and mansion blocks, Knightsbury-style shops on Admiral's Walk and Pall Row), Kingsmere (the palace, the
//   forecourt and its railings, the Memorial, Kingsmere Arch, the Parade, the lake and its bridges, the barracks)
//   Detail   lamps, street dressing (railings, gilded gates, flags, deckchairs…), markings and grit, shop light
//   Colliders, Parked (static parked cars + bays kept for real ones), PoliceSpawns
//   Guards   eight guards (LundunRoyalGuard): four at the sentry boxes, two marching the front of the palace, two at
//            the barracks gate
//   LundunRoyalQuarter: the forecourt, the palace and the barracks are out of bounds (trespassing = police), and the
//            guards tell you to stand back from the railings
//   Map: Kingsmere Palace, the Parade, St Jarvis's Park, the Arch and the Memorial; the map picture is redone
// The city's side: where the Parade, Birdcage Walk, Mayfield Street, Admiral's Walk and Regency Street meet the Ring,
// only the Ring's junction plates, markings and lamps change.
// Kingsmere is north of the NavMesh seam, so the city's NavMesh covers it: afterwards Lundun → 3 Bake Navigation, Cmd+S.
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

public static class LundunKingsmereBuilder
{
    const string RootName = "LundunExpansion", AreaName = "Kingsmere";
    const float GuardGround = 0.15f;          // the forecourt's gravel and the barracks yard (pavement height)

    [MenuItem("Lundun/29  Kingsmere, the Royal Quarter (E8)", false, 35)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { LundunBrixtunBuilder.Dialog("Open the Lundun scene first."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(LundunWorldFiles.BasePath);
        if (json == null) { LundunBrixtunBuilder.Dialog("Can't find " + LundunWorldFiles.BasePath + "."); return; }
        GameObject worldRoot = GameObject.Find("World");
        if (worldRoot == null) { LundunBrixtunBuilder.Dialog("No 'World' object in the scene. Run Lundun → 1 Build Greybox World first."); return; }
        if (!LundunGreyboxBuilder.HasExpansions()) { LundunBrixtunBuilder.Dialog("Assets/World/LundunExpansion.json has no expansion areas in it (copy in the E8 version)."); return; }

        string log;
        LundunBrixtunBuilder.UseArea(AreaName, "KGM");
        try
        {
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            EditorUtility.DisplayProgressBar("Lundun", "Gilding Kingsmere's gates…", 0.03f);
            LundunBuildingKitBuilder.EnsureMaterials(true);

            // 1. the city's side: the joins on the Ring's north side
            EditorUtility.DisplayProgressBar("Lundun", "Joining Kingsmere onto the Ring…", 0.08f);
            GreyboxResult city = Generator.Build(data);
            int marks = LundunBrixtunBuilder.RefreshMarkings(worldRoot, city);
            int pieces = LundunExpansionBuilder.RefreshGroups(worldRoot, city, new[] { "Junctions" });
            LundunLightingDistrictBuilder.RefreshLamps(city);
            LundunLightingDistrictBuilder.RegisterLamps(city, true);

            // 2. Kingsmere
            EditorUtility.DisplayProgressBar("Lundun", "Working out Kingsmere…", 0.2f);
            GreyboxResult x = Generator.BuildKingsmere(data);
            if (x.meshes.Count == 0) throw new Exception("LundunExpansion.json has no Kingsmere roads (copy in the E8 version).");
            LundunWorldData xw = Generator.ExpansionWorld;
            DressingResult xd = Generator.Dress(xw, x);
            StreetResult xs = Generator.Streets(xw, x, xd);

            Directory.CreateDirectory("Assets/World/Expansions/" + AreaName);
            AssetDatabase.ImportAsset("Assets/World/Expansions");
            GameObject top = GameObject.Find(RootName);
            if (top == null) top = new GameObject(RootName);
            Transform old = top.transform.Find(AreaName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            AssetDatabase.DeleteAsset("Assets/World/Expansions/" + AreaName + "/" + AreaName + "Meshes.asset");
            Transform area = LundunBrixtunBuilder.Child(top.transform, AreaName);
            Transform detail = LundunBrixtunBuilder.Child(area, "Detail");

            EditorUtility.DisplayProgressBar("Lundun", "Building the palace, the Parade and the streets…", 0.35f);
            int parts = LundunBrixtunBuilder.BuildWorld(area, detail, x);
            EditorUtility.DisplayProgressBar("Lundun", "Dressing Kingsmere…", 0.6f);
            Material glow, globe;
            int dressing = LundunBrixtunBuilder.BuildDressing(area, detail, xd, out glow, out globe);
            EditorUtility.DisplayProgressBar("Lundun", "Markings, gravel, shop light…", 0.75f);
            int streets = LundunBrixtunBuilder.BuildStreets(detail, xs);
            int spills = LundunBrixtunBuilder.BuildSpill(detail, x, xd);

            EditorUtility.DisplayProgressBar("Lundun", "Parked cars, police, the guards…", 0.85f);
            Vector4[] bays; Renderer[] parked;
            int cars = LundunBrixtunBuilder.BuildParked(area, xs, out bays, out parked);
            Transform[] police = LundunBrixtunBuilder.BuildPoliceSpawns(area, x);
            Vector4[] docks = new Vector4[xd.bikeDocks.Count], docksOut = new Vector4[xd.bikeDocks.Count];
            for (int i = 0; i < docks.Length; i++)
            {
                BikeDockInfo k = xd.bikeDocks[i];
                docks[i] = new Vector4((float)k.x, (float)k.z, (float)k.alongX, (float)k.alongZ);
                docksOut[i] = new Vector4((float)k.outX, (float)k.outZ, 0f, 0f);
            }
            LundunRoyalGuard[] guards = BuildGuards(area);
            BuildRules(area, guards);

            List<Generator.MapPlace> places = Generator.KingsmerePlaces();
            foreach (Generator.MapPlace mp in places)
                LundunExpansionBuilder.Register(mp.id, mp.name, mp.kind, new Vector3((float)mp.x, 0f, (float)mp.z), mp.label);

            // the runtime bit: lamps for the real lamp lights, the signs' glow and the beacons, street life
            LundunExpansionArea rt = area.gameObject.AddComponent<LundunExpansionArea>();
            Vector3[] lamps = new Vector3[x.lamps.Count]; int[] styles = new int[x.lamps.Count];
            for (int i = 0; i < lamps.Length; i++) { LampInfo l = x.lamps[i]; lamps[i] = new Vector3((float)l.x, (float)l.y, (float)l.z); styles[i] = l.style; }
            rt.SetData(AreaName, lamps, styles, new string[0], new Vector3[0], new Vector3[0]);
            rt.SetLook(glow, globe);
            rt.SetStreetLife(docks, docksOut, bays, parked, police);
            EditorUtility.SetDirty(rt);

            LundunBrixtunBuilder.StreamDetail();
            AssetDatabase.SaveAssets();
            log = "[Lundun] E8 Kingsmere: " + string.Join("\n", x.log.ToArray()) + "\n" + parts + " street / building pieces, " + dressing + " dressing pieces, " +
                  streets + " marking and litter blocks, " + spills + " shop lights, " + cars + " parked cars (" + bays.Length + " bays kept for real ones), " +
                  police.Length + " police spawn points, " + docks.Length + " bike docks, " + guards.Length + " guards; on the map: " + places.Count + " places." +
                  "\nCity side: " + marks + " marking blocks, " + pieces + " junction pieces, the lamps.";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.ClearProgressBar();
            LundunBrixtunBuilder.Dialog("Kingsmere failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.");
            LundunBrixtunBuilder.UseArea("Brixtun", "BRX");
            return;
        }
        EditorUtility.ClearProgressBar();
        LundunBrixtunBuilder.UseArea("Brixtun", "BRX");

        LundunMapSharpBuilder.Build();             // Kingsmere (and Brixtun) on the map picture
        LundunLightingDistrictBuilder.ApplyLook();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log(log + "\nNext: Lundun → 3 Bake Navigation, then Cmd+S.");
    }

    /// The guards: each a body and four limbs (so LundunRoyalGuard can swing them), at his post.
    static LundunRoyalGuard[] BuildGuards(Transform area)
    {
        Dictionary<string, MeshData> partsData = Generator.GuardParts();
        Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        foreach (var kv in partsData) meshes[kv.Key] = LundunBrixtunBuilder.Save(LundunLightingDistrictBuilder.ToMesh(kv.Value));
        Material mat = LundunGreyboxBuilder.WorldMaterial(Generator.KitMatB);
        Transform root = LundunBrixtunBuilder.Child(area, "Guards");
        List<LundunRoyalGuard> list = new List<LundunRoyalGuard>();
        foreach (GuardInfo g in Generator.KingsmereGuards)
        {
            GameObject go = new GameObject("Guard_" + (list.Count + 1).ToString("00"));
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(new Vector3((float)g.x, GuardGround, (float)g.z), Quaternion.Euler(0f, (float)g.yaw, 0f));
            foreach (var kv in meshes)
            {
                GameObject p = new GameObject(kv.Key);
                p.transform.SetParent(go.transform, false);
                float pivot = kv.Key.StartsWith("Leg") ? 0.95f : kv.Key.StartsWith("Arm") ? 1.45f : 0f;
                p.transform.localPosition = new Vector3(0f, pivot, 0f);
                p.AddComponent<MeshFilter>().sharedMesh = kv.Value;
                MeshRenderer mr = p.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            // something solid to bump into (moves when he marches, so a kinematic body); kept out of the NavMesh bake
            CapsuleCollider cc = go.AddComponent<CapsuleCollider>();
            cc.center = new Vector3(0f, 1.1f, 0f); cc.height = 2.2f; cc.radius = 0.3f;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            LundunGreyboxBuilder.NavModifier(go, true, -1);
            LundunRoyalGuard rg = go.AddComponent<LundunRoyalGuard>();
            rg.March = (float)g.march;
            EditorUtility.SetDirty(rg);
            list.Add(rg);
        }
        return list.ToArray();
    }

    /// LundunRoyalQuarter: where you mustn't go, where the guards warn you off.
    static void BuildRules(Transform area, LundunRoyalGuard[] guards)
    {
        Func<List<double[]>, Vector4[]> V = l =>
        {
            Vector4[] a = new Vector4[l.Count];
            for (int i = 0; i < a.Length; i++) a[i] = new Vector4((float)l[i][0], (float)l[i][1], (float)l[i][2], (float)l[i][3]);
            return a;
        };
        LundunRoyalQuarter rq = area.gameObject.AddComponent<LundunRoyalQuarter>();
        rq.Set(V(Generator.KingsmereRestricted), V(Generator.KingsmereWarning), guards);
        EditorUtility.SetDirty(rq);
    }
}
