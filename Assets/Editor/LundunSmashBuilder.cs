// Lundun → 30  Smashable Street Furniture (V4b)
//   Lists every piece of street furniture a car can knock over (in the city, Brixtun and Kingsmere) for LundunSmash:
//   bins, wheelie bins, cones, roadworks barriers and signs, trolleys, cycle stands, benches, railings, fly-tipping,
//   bollards, Belisha beacons, post boxes, cabinets, planters, fences, phone boxes, bus shelters, estate bin stores
//   (lamp posts are found at runtime from the lamp lights). It works the same places out again (same seeds as
//   Lundun → 13 / 28 / 29), so it must run AFTER those: run it last, again whenever you rebuild them.
//   It also makes the "Smash" layer (only cars hit what's on it) and takes the meshes the furniture is merged into
//   out of static batching (a statically batched mesh can't lose a bollard at runtime).
using System;
using System.Collections.Generic;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunSmashBuilder
{
    const string DataPath = "Assets/World/LundunWorld.json";
    const string RootName = "LundunSmash";

    [MenuItem("Lundun/30  Smashable Street Furniture (V4b)", false, 36)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null) { Dialog("Can't find " + DataPath + "."); return; }
        if (GameObject.Find("LundunDressing") == null) { Dialog("No LundunDressing in the scene: run Lundun → 13 (or 23) first."); return; }
        try
        {
            int layer = EnsureLayer("Smash");
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            List<LundunSmash.Prop> props = new List<LundunSmash.Prop>();
            Dictionary<string, int> counts = new Dictionary<string, int>();

            // the city (the same places Lundun → 13 used)
            EditorUtility.DisplayProgressBar("Lundun", "Finding the city's street furniture…", 0.1f);
            GreyboxResult world = Generator.Build(data);
            DressingResult d = Generator.Dress(data, world);
            Collect(d, props, counts);
            // the expansions (as Lundun → 28 / 29 build them)
            if (LundunGreyboxBuilder.HasExpansions() && GameObject.Find("LundunExpansion") != null)
            {
                try
                {
                    EditorUtility.DisplayProgressBar("Lundun", "Brixtun's…", 0.3f);
                    Generator.Build(data);
                    GreyboxResult x = Generator.BuildExpansion(data);
                    if (x.meshes.Count > 0) Collect(Generator.Dress(Generator.ExpansionWorld, x), props, counts);
                }
                catch (Exception e) { Debug.LogWarning("[Lundun] Brixtun's furniture skipped: " + e.Message); }
                try
                {
                    EditorUtility.DisplayProgressBar("Lundun", "Kingsmere's…", 0.45f);
                    Generator.Build(data);
                    GreyboxResult k = Generator.BuildKingsmere(data);
                    if (k.meshes.Count > 0) Collect(Generator.Dress(Generator.ExpansionWorld, k), props, counts);
                }
                catch (Exception e) { Debug.LogWarning("[Lundun] Kingsmere's furniture skipped: " + e.Message); }
                Generator.Build(data);                                  // leave the generator on the city
            }

            // the meshes they're merged into: the dressing, the lamps, the expansions
            EditorUtility.DisplayProgressBar("Lundun", "Finding the meshes…", 0.6f);
            Dictionary<long, bool> cells = new Dictionary<long, bool>();
            foreach (LundunSmash.Prop p in props) cells[Cell(p.at)] = true;
            List<Renderer> targets = new List<Renderer>();
            int unbatched = 0;
            foreach (string rootName in new[] { "LundunDressing", "World/Lamps", "LundunExpansion" })
            {
                GameObject r = GameObject.Find(rootName);
                if (r == null) continue;
                foreach (MeshRenderer mr in r.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    Material m = mr.sharedMaterial;
                    string mn = m != null ? m.name : "";
                    bool lamp = mn.StartsWith("LampPole") || mn.StartsWith("LampHead") || mn.StartsWith("LightPool");
                    bool dressing = mn.StartsWith("Dress") || HasAncestor(mr.transform, "Prefab Props") || HasAncestor(mr.transform, "Props");
                    if (!lamp && !dressing) continue;
                    if (!lamp && !Near(mr.bounds, cells)) continue;
                    targets.Add(mr);
                    if (Unbatch(mr.gameObject)) unbatched++;
                }
            }
            // hand-placed lamp posts move when knocked over: they can't be statically batched either
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name.StartsWith("LampPost") && t.parent != null && !t.parent.name.StartsWith("LampPost"))
                    foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) if (Unbatch(c.gameObject)) unbatched++;

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            GameObject root = new GameObject(RootName);
            LundunSmash smash = root.AddComponent<LundunSmash>();
            smash.SetData(props.ToArray(), targets.ToArray());
            EditorUtility.SetDirty(smash);
            EditorSceneManager.MarkSceneDirty(scene);

            List<string> parts = new List<string>();
            List<string> keys = new List<string>(counts.Keys); keys.Sort();
            foreach (string k in keys) parts.Add(k + " " + counts[k]);
            Debug.Log("[Lundun] V4b: " + props.Count + " things a car can knock over (" + string.Join(", ", parts.ToArray()) + "), plus every lamp post. " +
                      targets.Count + " meshes they're in (" + unbatched + " taken out of static batching)" + (layer < 0 ? ". No free layer for \"Smash\": railings will block people too." : ", layer \"Smash\" " + layer) +
                      ".\nRun this again after Lundun → 13, 23, 28 or 29. Then Cmd+S and Play.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Dialog("Smashable furniture failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    static readonly Dictionary<string, LundunSmash.Kind> Kinds = new Dictionary<string, LundunSmash.Kind>
    {
        { "bin", LundunSmash.Kind.Bin }, { "wheeliebin", LundunSmash.Kind.WheelieBin }, { "cone", LundunSmash.Kind.Cone },
        { "barrier", LundunSmash.Kind.Barrier }, { "sign", LundunSmash.Kind.Sign }, { "trolley", LundunSmash.Kind.Trolley },
        { "cyclestand", LundunSmash.Kind.CycleStand }, { "bench", LundunSmash.Kind.Bench }, { "railing", LundunSmash.Kind.Railing },
        { "sofa", LundunSmash.Kind.Sofa }, { "fridge", LundunSmash.Kind.Fridge }, { "mattress", LundunSmash.Kind.Mattress },
        { "tyres", LundunSmash.Kind.Tyres }, { "bollard", LundunSmash.Kind.Bollard }, { "postbox", LundunSmash.Kind.PostBox },
        { "cabinet", LundunSmash.Kind.Cabinet }, { "planter", LundunSmash.Kind.Planter }, { "fence", LundunSmash.Kind.Fence },
        { "estatebins", LundunSmash.Kind.EstateBins },
    };

    /// Everything breakable from one area's dressing: the rule-placed things, and the Belisha beacons, bus shelters
    /// and phone boxes (their size measured from your prefabs).
    static void Collect(DressingResult d, List<LundunSmash.Prop> props, Dictionary<string, int> counts)
    {
        foreach (BreakInfo b in d.breakables)
        {
            LundunSmash.Kind k;
            if (!Kinds.TryGetValue(b.kind, out k)) continue;
            props.Add(new LundunSmash.Prop { kind = k, at = new Vector3((float)b.x, (float)b.y, (float)b.z), yaw = (float)b.yaw, size = new Vector3((float)b.hw, (float)b.h, (float)b.hl) });
            Count(counts, b.kind);
        }
        Dictionary<string, Bounds> sizes = new Dictionary<string, Bounds>();
        foreach (PropPlacement p in d.prefabs)
        {
            LundunSmash.Kind k;
            if (p.kind == "Belisha") k = LundunSmash.Kind.Belisha;
            else if (p.kind == "BusStop") k = LundunSmash.Kind.BusShelter;
            else if (p.kind == "PhoneBox") k = LundunSmash.Kind.PhoneBox;
            else continue;
            Bounds lb;
            if (!sizes.TryGetValue(p.kind, out lb)) { lb = PrefabBounds("Assets/Prefabs/" + p.kind + ".prefab"); sizes[p.kind] = lb; }
            if (lb.size.sqrMagnitude < 1e-6f) continue;
            Quaternion rot = Quaternion.Euler(0f, (float)p.yaw, 0f);
            Vector3 foot = new Vector3((float)p.x, (float)p.y, (float)p.z) + rot * new Vector3(lb.center.x, lb.min.y, lb.center.z);
            props.Add(new LundunSmash.Prop { kind = k, at = foot, yaw = (float)p.yaw, size = new Vector3(lb.extents.x, lb.size.y, lb.extents.z) });
            Count(counts, p.kind);
        }
    }

    /// A prefab's renderers' box, in its own space (where it's placed from).
    static Bounds PrefabBounds(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return new Bounds();
        GameObject inst = Object.Instantiate(prefab);
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Bounds b = new Bounds(); bool any = false;
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        Object.DestroyImmediate(inst);
        return b;
    }

    static void Count(Dictionary<string, int> c, string k) { int n; c.TryGetValue(k, out n); c[k] = n + 1; }

    static long Cell(Vector3 p) { return ((long)Mathf.FloorToInt(p.x / 25f)) * 100003L + Mathf.FloorToInt(p.z / 25f); }

    static bool Near(Bounds b, Dictionary<long, bool> cells)
    {
        int x0 = Mathf.FloorToInt((b.min.x - 3f) / 25f), x1 = Mathf.FloorToInt((b.max.x + 3f) / 25f);
        int z0 = Mathf.FloorToInt((b.min.z - 3f) / 25f), z1 = Mathf.FloorToInt((b.max.z + 3f) / 25f);
        for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) if (cells.ContainsKey((long)x * 100003L + z)) return true;
        return false;
    }

    static bool HasAncestor(Transform t, string name)
    {
        for (; t != null; t = t.parent) if (t.name == name) return true;
        return false;
    }

    /// Out of static batching (other static flags stay).
    static bool Unbatch(GameObject g)
    {
        StaticEditorFlags f = GameObjectUtility.GetStaticEditorFlags(g);
        if ((f & StaticEditorFlags.BatchingStatic) == 0) return false;
        GameObjectUtility.SetStaticEditorFlags(g, f & ~StaticEditorFlags.BatchingStatic);
        return true;
    }

    /// The layer, made if the project hasn't got it (−1 if every layer is taken).
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

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }
}
