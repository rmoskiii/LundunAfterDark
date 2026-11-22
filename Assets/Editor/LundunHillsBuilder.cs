// G7: Lundun → 40  Hills. The city's hills, bent into the scene in place:
//   G7a RYE HILL       Rye Common, east of Peckam Rye: a grassy hill, about 7 m, never steeper than about 12%
//   G7b BROCKMERE HILL Brockmere Park on top, 18 m up; Brixtun Hill and the estate streets climb to it (about 8%)
//   G7b DENMORE HILL   the street from Peckam down into Brixtun goes over a 12 m crest (about 9%)
// On Brockmere and Denmore the streets slope with the ground (cut nearly level across, with a bank either side) and
// every building stays level: it sits at its front door's height, with a stone plinth under its downhill side.
//
// Nothing is rebuilt, so nothing else needs running again (only the walking map, Lundun → 3):
//   * big meshes on a hill (ground, roads, pavements, buildings, the merged dressing, parked cars, leaves, ...) get a
//     bent copy, cut into pieces no wider than 6.5 m where they lie on the ground; the copy replaces the mesh (and the
//     collider's mesh). A building's own vertices all go up by its one height, so it stays square
//   * small things (colliders, lamps, signs, markers, police spawn points ...) are lifted whole
//   * positions kept in scripts' settings (street lamps' lights, the things a car can knock over, Tube entrances,
//     bike docks ...) are lifted the same way
//   * plinths are added under the buildings that stand on a slope (LundunHills/Plinths)
//   * the hills' heights are kept on LundunHills, so traffic, people and police placed while the game runs stand on
//     the ground (LundunGround)
// The original meshes are never touched (the bent copies live in Assets/World/Hills), and the "LundunHills" object
// remembers everything that changed: Lundun → 40b puts it all back, and running 40 again starts from flat.
// If you ever rerun Lundun → 1, 13, 21, 22, 23, 28, 30, 37 or 38, run 40 again afterwards (then 3).
using System.Collections.Generic;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class LundunHillsBuilder
{
    const string Dir = "Assets/World/Hills";
    const string MeshPath = Dir + "/HillMeshes.asset";
    const string DataPath = "Assets/World/LundunWorld.json";
    const string RootName = "LundunHills";
    const string PlinthMat = Dir + "/Hill_Plinth.mat";
    const double RyeHeight = 7.5;             // Rye Hill's full height; the rounded top comes out at about 7.3 m
    const double MaxPiece = 6.5;              // bent meshes are cut into pieces no wider than this (metres)
    const float SmallObject = 12f;            // anything narrower than this is lifted whole rather than bent
    const float HighestThing = 25f, LowestThing = -3f;   // ignore what isn't at street level (the Tube is at 420 m)

    [MenuItem("Lundun/40  Hills: Rye, Brockmere, Denmore (G7)", false, 50)]
    static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Dialog(Apply());
    }

    /// G8d: for other menus that change what the hills bent (37 autumn): are there hills in the scene; take them out
    /// (the city flat again, exactly as it was); put them back in (as menu 40, without its message box).
    public static bool Present { get { return GameObject.Find(RootName) != null; } }
    public static int TakeOut() { return PutBack(); }
    public static string PutIn() { return Apply(); }

    /// Menu 40's work: takes out any hills there already, then makes them all. Returns the message to show.
    static string Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null) return "Can't find " + DataPath + ".";
        if (GameObject.Find("World") == null) return "No World in the scene: run Lundun → 1 first.";

        int undone = PutBack();
        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Working out the hills from the city plan…", 0.03f);
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            HillSet hills = Generator.Hills(data, RyeHeight);

            EnsureFolder(Dir);
            AssetDatabase.DeleteAsset(MeshPath);
            GameObject root = new GameObject(RootName);
            LundunHillRecord rec = root.AddComponent<LundunHillRecord>();
            rec.summary = hills.Summary();
            KeepGround(hills, rec);

            List<string> skipped = new List<string>();
            Mesh container = null;
            int pieces = Bend(hills, rec, root, skipped, ref container);
            int lifted = Lift(hills, rec, root);
            int settings = LiftSettings(hills, rec);
            int plinths = Plinths(hills, root, ref container);

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(rec);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorUtility.ClearProgressBar();

            string msg = "Hills (top, steepest): " + rec.summary + ".\n\n" + rec.bent.Count + " meshes bent (" + pieces + " triangles now), "
                + lifted + " things lifted, " + settings + " positions in scripts lifted, " + hills.buildings.Count + " buildings kept level ("
                + plinths + " with a plinth).";
            if (undone > 0) msg += "\n(The hills that were there before were taken out first.)";
            if (skipped.Count > 0) msg += "\n\nCouldn't bend (not readable): " + string.Join(", ", skipped);
            msg += "\n\nNow run Lundun → 3 (Bake Navigation) so people can walk up them, then save the scene (Cmd+S).";
            Debug.Log("[Lundun] " + msg.Replace("\n\n", "  ").Replace("\n", " "));
            return msg;
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogException(e);
            return "Something went wrong making the hills (see the Console). Run Lundun → 40b to put the city back flat.";
        }
    }

    /// The hills' heights for the game (every 4 m is plenty for standing things on the ground).
    static void KeepGround(HillSet hills, LundunHillRecord rec)
    {
        foreach (HillGrid g in hills.grids)
        {
            const float cell = 4f;
            LundunHillRecord.Ground k = new LundunHillRecord.Ground { name = g.name, x0 = (float)g.xMin, z0 = (float)g.zMin, cell = cell };
            k.nx = (int)System.Math.Ceiling((g.xMax - g.xMin) / cell) + 2;
            k.nz = (int)System.Math.Ceiling((g.zMax - g.zMin) / cell) + 2;
            k.h = new float[k.nx * k.nz];
            for (int j = 0; j < k.nz; j++)
                for (int i = 0; i < k.nx; i++)
                    k.h[j * k.nx + i] = (float)g.At(k.x0 + i * cell, k.z0 + j * cell);
            rec.ground.Add(k);
        }
    }

    [MenuItem("Lundun/40b  Remove Hills (G7)", false, 51)]
    static void Remove()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        int n = PutBack();
        if (n == 0) { Dialog("There are no hills in this scene."); return; }
        AssetDatabase.DeleteAsset(MeshPath);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Dialog("The city is flat again (" + n + " changes put back).\n\nRun Lundun → 3 (Bake Navigation), then save the scene (Cmd+S).");
    }

    /// Undoes whatever the record says (newest first) and removes the LundunHills object. Returns how many changes.
    static int PutBack()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null) return 0;
        LundunHillRecord rec = root.GetComponent<LundunHillRecord>();
        int n = 0;
        if (rec != null)
        {
            for (int i = rec.props.Count - 1; i >= 0; i--)
            {
                LundunHillRecord.Prop p = rec.props[i];
                if (p.smash == null) continue;
                SerializedObject so = new SerializedObject(p.smash);
                SerializedProperty arr = so.FindProperty("props");
                if (arr == null || p.index >= arr.arraySize) continue;
                SerializedProperty at = arr.GetArrayElementAtIndex(p.index).FindPropertyRelative("at");
                Vector3 v = at.vector3Value; v.y -= p.by; at.vector3Value = v;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            for (int i = rec.settings.Count - 1; i >= 0; i--)
            {
                LundunHillRecord.Setting st = rec.settings[i];
                if (st.on == null) continue;
                SerializedObject so = new SerializedObject(st.on);
                SerializedProperty p = so.FindProperty(st.path);
                if (p == null || p.propertyType != SerializedPropertyType.Vector3) continue;
                Vector3 v = p.vector3Value; v.y -= st.by; p.vector3Value = v;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            for (int i = rec.lifted.Count - 1; i >= 0; i--)
            {
                LundunHillRecord.Lifted l = rec.lifted[i];
                if (l.what == null) continue;
                l.what.position -= l.by;
                Touched(l.what);
                n++;
            }
            for (int i = rec.bent.Count - 1; i >= 0; i--)
            {
                LundunHillRecord.Bent b = rec.bent[i];
                if (b.filter != null) { b.filter.sharedMesh = b.was; Touched(b.filter); n++; }
                if (b.collider != null) { b.collider.sharedMesh = b.was; Touched(b.collider); n++; }
            }
        }
        Object.DestroyImmediate(root);
        return Mathf.Max(n, 1);
    }

    // ----- 1. bend the big meshes

    class Job { public Mesh src; public Transform t; public MeshFilter mf; public List<MeshCollider> mcs = new List<MeshCollider>(); }

    static int Bend(HillSet hill, LundunHillRecord rec, GameObject root, List<string> skipped, ref Mesh container)
    {
        // every mesh (drawn, or only a collider) over the rising ground that's too big to lift whole
        Dictionary<(Mesh, Transform), Job> jobs = new Dictionary<(Mesh, Transform), Job>();
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mf.sharedMesh == null || Excluded(mf.transform)) continue;
            Bounds b = WorldBounds(mf.sharedMesh, mf.transform);
            if (!OnHill(hill, b) || !Big(b)) continue;
            Job j = JobFor(jobs, mf.sharedMesh, mf.transform);
            j.mf = mf;
        }
        foreach (MeshCollider mc in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mc.sharedMesh == null || Excluded(mc.transform)) continue;
            Bounds b = WorldBounds(mc.sharedMesh, mc.transform);
            if (!OnHill(hill, b) || !Big(b)) continue;
            JobFor(jobs, mc.sharedMesh, mc.transform).mcs.Add(mc);
        }

        int done = 0, tris = 0;
        foreach (Job j in jobs.Values)
        {
            done++;
            EditorUtility.DisplayProgressBar("Lundun", "Bending " + j.src.name + "…", 0.1f + 0.7f * done / jobs.Count);
            if (!j.src.isReadable) { skipped.Add(j.src.name); continue; }
            Mesh bent = BendMesh(j.src, j.t, hill);
            if (bent == null) continue;                               // nothing of it actually rose
            if (container == null) { AssetDatabase.CreateAsset(bent, MeshPath); container = bent; }
            else AssetDatabase.AddObjectToAsset(bent, container);
            for (int s = 0; s < bent.subMeshCount; s++) tris += (int)bent.GetIndexCount(s) / 3;
            if (j.mf != null)
            {
                rec.bent.Add(new LundunHillRecord.Bent { filter = j.mf, was = j.src });
                j.mf.sharedMesh = bent; Touched(j.mf);
            }
            foreach (MeshCollider mc in j.mcs)
            {
                rec.bent.Add(new LundunHillRecord.Bent { collider = mc, was = j.src });
                mc.sharedMesh = bent; Touched(mc);
            }
        }
        return tris;
    }

    static Job JobFor(Dictionary<(Mesh, Transform), Job> jobs, Mesh m, Transform t)
    {
        Job j;
        if (!jobs.TryGetValue((m, t), out j)) jobs[(m, t)] = j = new Job { src = m, t = t };
        return j;
    }

    /// A bent copy of a mesh (as it sits under transform t): cut into small pieces over the hill, every vertex raised by
    /// the ground under it, normals and tangents tilted with the slope. Null when none of it is over rising ground.
    static Mesh BendMesh(Mesh src, Transform t, HillSet hill)
    {
        for (int s = 0; s < src.subMeshCount; s++) if (src.GetTopology(s) != MeshTopology.Triangles) return null;
        Matrix4x4 toWorld = t.localToWorldMatrix, toLocal = t.worldToLocalMatrix;
        Vector3[] v = src.vertices; Vector3[] nrm = src.normals; Vector4[] tan = src.tangents;
        Vector2[] uv = src.uv, uv2 = src.uv2; Color[] col = src.colors;
        int n = v.Length;
        bool hasN = nrm.Length == n, hasT = tan.Length == n, hasUv = uv.Length == n, hasUv2 = uv2.Length == n, hasC = col.Length == n;
        int stride = 3 + (hasN ? 3 : 0) + (hasT ? 4 : 0) + (hasUv ? 2 : 0) + (hasUv2 ? 2 : 0) + (hasC ? 4 : 0);

        // one row of floats per vertex: world position first (so pieces are measured on the ground), then the rest as is
        List<float> d = new List<float>(n * stride * 2);
        for (int i = 0; i < n; i++)
        {
            Vector3 w = toWorld.MultiplyPoint3x4(v[i]);
            d.Add(w.x); d.Add(w.y); d.Add(w.z);
            if (hasN) { d.Add(nrm[i].x); d.Add(nrm[i].y); d.Add(nrm[i].z); }
            if (hasT) { d.Add(tan[i].x); d.Add(tan[i].y); d.Add(tan[i].z); d.Add(tan[i].w); }
            if (hasUv) { d.Add(uv[i].x); d.Add(uv[i].y); }
            if (hasUv2) { d.Add(uv2[i].x); d.Add(uv2[i].y); }
            if (hasC) { d.Add(col[i].r); d.Add(col[i].g); d.Add(col[i].b); d.Add(col[i].a); }
        }
        // a mesh lying on the ground (ground, roads, pavements, markings, leaves: under 60 cm high) follows the ground
        // everywhere; a building's own mesh (nearly all of it at a building) goes up with its building; the rest (the
        // dressing, parked cars) follows the ground, except what's up on a building's front (HillSet.Lift)
        Bounds wb = WorldBounds(src, t);
        bool flat = wb.size.y <= 0.6f;
        bool ofBuilding = false;
        if (!flat)
        {
            int inB = 0, step = Mathf.Max(1, n / 400), seen = 0;
            for (int i = 0; i < n; i += step) { seen++; if (hill.InBuilding(d[i * stride], d[i * stride + 2])) inB++; }
            ofBuilding = inB >= seen * 0.9f;
        }
        Dictionary<int, HillBuilding> owner = new Dictionary<int, HillBuilding>();
        Dictionary<int, float> lift = new Dictionary<int, float>();
        System.Func<int, float> liftOf = i =>
        {
            float h;
            if (!lift.TryGetValue(i, out h))
            {
                HillBuilding whole = null;
                h = flat ? (float)hill.At(d[i * stride], d[i * stride + 2]) : (float)hill.Lift(d[i * stride], d[i * stride + 1], d[i * stride + 2], ofBuilding, out whole);
                lift[i] = h; owner[i] = whole;
            }
            return h;
        };
        System.Func<int, HillBuilding> ownerOf = i => { liftOf(i); return owner[i]; };
        System.Func<int, int, int, bool> overHill = (a, b, c) =>
        {
            double x0 = Mathf.Min(d[a * stride], Mathf.Min(d[b * stride], d[c * stride])), x1 = Mathf.Max(d[a * stride], Mathf.Max(d[b * stride], d[c * stride]));
            double z0 = Mathf.Min(d[a * stride + 2], Mathf.Min(d[b * stride + 2], d[c * stride + 2])), z1 = Mathf.Max(d[a * stride + 2], Mathf.Max(d[b * stride + 2], d[c * stride + 2]));
            if (!hill.Rises(x0, z0, x1, z1)) return false;
            HillBuilding ha = ownerOf(a);
            return ha == null || ha != ownerOf(b) || ha != ownerOf(c);       // a building's own faces stay whole
        };
        List<int>[] subs = new List<int>[src.subMeshCount];
        for (int s = 0; s < src.subMeshCount; s++)
        {
            List<int> tris = new List<int>();
            src.GetTriangles(tris, s);
            subs[s] = Generator.Tessellate(d, stride, tris, MaxPiece, overHill);
        }

        int m = d.Count / stride;
        bool rose = false;
        List<Vector3> pv = new List<Vector3>(m), pn = new List<Vector3>(hasN ? m : 0);
        List<Vector4> pt = new List<Vector4>(hasT ? m : 0);
        List<Vector2> pu = new List<Vector2>(hasUv ? m : 0), pu2 = new List<Vector2>(hasUv2 ? m : 0);
        List<Color> pc = new List<Color>(hasC ? m : 0);
        Matrix4x4 normalToWorld = toLocal.transpose, normalToLocal = toWorld.transpose;
        for (int i = 0; i < m; i++)
        {
            int o = i * stride;
            float x = d[o], z = d[o + 2];
            float h = liftOf(i);
            double gx = 0, gz = 0;
            if (h > 0.0005f) { rose = true; if (ownerOf(i) == null) hill.Slope(x, z, out gx, out gz); }
            pv.Add(toLocal.MultiplyPoint3x4(new Vector3(x, d[o + 1] + h, z)));
            o += 3;
            if (hasN)
            {
                Vector3 nl = new Vector3(d[o], d[o + 1], d[o + 2]); o += 3;
                if (gx != 0 || gz != 0)
                {
                    // the ground's shear (y += height(x, z)) turns a normal by its inverse transpose
                    Vector3 nw = normalToWorld.MultiplyVector(nl);
                    nw = new Vector3(nw.x - (float)gx * nw.y, nw.y, nw.z - (float)gz * nw.y);
                    nl = normalToLocal.MultiplyVector(nw);
                }
                pn.Add(nl.sqrMagnitude > 1e-12f ? nl.normalized : Vector3.up);
            }
            if (hasT)
            {
                Vector3 tl = new Vector3(d[o], d[o + 1], d[o + 2]); float tw = d[o + 3]; o += 4;
                if (gx != 0 || gz != 0)
                {
                    Vector3 tw3 = toWorld.MultiplyVector(tl);
                    tw3.y += (float)gx * tw3.x + (float)gz * tw3.z;
                    tl = toLocal.MultiplyVector(tw3);
                }
                if (tl.sqrMagnitude > 1e-12f) tl.Normalize();
                pt.Add(new Vector4(tl.x, tl.y, tl.z, tw < 0f ? -1f : 1f));
            }
            if (hasUv) { pu.Add(new Vector2(d[o], d[o + 1])); o += 2; }
            if (hasUv2) { pu2.Add(new Vector2(d[o], d[o + 1])); o += 2; }
            if (hasC) { pc.Add(new Color(d[o], d[o + 1], d[o + 2], d[o + 3])); o += 4; }
        }
        if (!rose) return null;

        Mesh mesh = new Mesh();
        mesh.name = src.name + " (Hills)";
        mesh.indexFormat = m > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(pv);
        if (hasN) mesh.SetNormals(pn);
        if (hasT) mesh.SetTangents(pt);
        if (hasUv) mesh.SetUVs(0, pu);
        if (hasUv2) mesh.SetUVs(1, pu2);
        if (hasC) mesh.SetColors(pc);
        mesh.subMeshCount = subs.Length;
        for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s, false);
        mesh.RecalculateBounds();
        if (!hasN) mesh.RecalculateNormals();
        return mesh;
    }

    // ----- 2. lift the small things whole

    static int Lift(HillSet hill, LundunHillRecord rec, GameObject root)
    {
        EditorUtility.DisplayProgressBar("Lundun", "Lifting what stands on the hills…", 0.85f);
        HashSet<Transform> bent = new HashSet<Transform>();
        foreach (LundunHillRecord.Bent b in rec.bent)
        {
            if (b.filter != null) bent.Add(b.filter.transform);
            if (b.collider != null) bent.Add(b.collider.transform);
        }
        Dictionary<Transform, Bounds?> cache = new Dictionary<Transform, Bounds?>();
        HashSet<Transform> lifted = new HashSet<Transform>();

        // things with a shape (a mesh drawn, or a collider): lift the largest small group they belong to, so a bench
        // made of several parts moves as one
        List<Transform> shaped = new List<Transform>();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) shaped.Add(r.transform);
        foreach (Collider c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None)) shaped.Add(c.transform);
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) shaped.Add(l.transform);
        foreach (Transform t in shaped)
        {
            if (bent.Contains(t) || t.gameObject == root) continue;
            Bounds? b = GroupBounds(t, cache);
            if (b == null || !OnHill(hill, b.Value) || Big(b.Value) || Excluded(t)) continue;
            Transform top = t;
            while (top.parent != null && top.parent.parent != null)              // never a scene root (a whole group)
            {
                Bounds? pb = GroupBounds(top.parent, cache);
                if (pb == null || Big(pb.Value) || HasAny(top.parent, bent)) break;
                top = top.parent;
            }
            lifted.Add(top);
        }
        // and plain markers (no shape) sitting on the hill
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.childCount == 0 && t.parent != null && hill.Lift(t.position.x, t.position.y, t.position.z) > 0.002 && !bent.Contains(t)
                && t.GetComponents<Component>().Length == 1 && !Excluded(t))
                lifted.Add(t);

        // cars and anything else left in the scene that moves by physics: just put them on the ground
        foreach (Rigidbody rb in Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (rb.isKinematic) continue;
            Transform t = rb.transform;
            bool skip = false;
            for (Transform p = t; p != null; p = p.parent)
                if (p.name == RootName || p.GetComponent<CharacterController>() != null || p.GetComponent<Animator>() != null || (p != t && p.GetComponent<Rigidbody>() != null)) skip = true;
            if (skip || t.position.y > HighestThing || t.position.y < LowestThing) continue;
            float h = (float)hill.At(t.position.x, t.position.z);
            if (h < 0.002f) continue;
            Vector3 by = Vector3.up * h;
            t.position += by;
            Touched(t);
            rec.lifted.Add(new LundunHillRecord.Lifted { what = t, by = by });
        }

        int n = 0;
        foreach (Transform t in lifted)
        {
            if (UnderAny(t, lifted)) continue;                                      // its parent is going up already
            Bounds? b = GroupBounds(t, cache);
            Vector3 at = b != null ? b.Value.center : t.position;
            if (b != null) { if (b.Value.min.y > HighestThing || b.Value.max.y < LowestThing) continue; }
            else if (t.position.y > HighestThing || t.position.y < LowestThing) continue;
            float h = (float)hill.Lift(at.x, b != null ? b.Value.min.y : t.position.y, at.z);
            if (h < 0.002f) continue;
            Vector3 by = Vector3.up * h;
            t.position += by;
            Touched(t);
            rec.lifted.Add(new LundunHillRecord.Lifted { what = t, by = by });
            n++;
        }
        return n;
    }

    /// The world box round everything with a shape under t (meshes, colliders, lights). Null if nothing.
    static Bounds? GroupBounds(Transform t, Dictionary<Transform, Bounds?> cache)
    {
        Bounds? got;
        if (cache.TryGetValue(t, out got)) return got;
        bool any = false; Bounds b = new Bounds();
        foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) Grow(ref b, ref any, WorldBounds(mf.sharedMesh, mf.transform));
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            if (!(r is MeshRenderer)) Grow(ref b, ref any, r.bounds);
        foreach (Collider c in t.GetComponentsInChildren<Collider>(true)) Grow(ref b, ref any, ColliderBounds(c));
        foreach (Light l in t.GetComponentsInChildren<Light>(true)) Grow(ref b, ref any, new Bounds(l.transform.position, Vector3.zero));
        got = any ? b : (Bounds?)null;
        cache[t] = got;
        return got;
    }

    static void Grow(ref Bounds b, ref bool any, Bounds add)
    {
        if (!any) { b = add; any = true; } else b.Encapsulate(add);
    }

    /// A collider's world box, worked out from its shape (a disabled collider's own bounds are empty).
    static Bounds ColliderBounds(Collider c)
    {
        Transform t = c.transform;
        if (c is BoxCollider bc) return Box(t, bc.center, bc.size);
        if (c is SphereCollider sc) return Box(t, sc.center, Vector3.one * sc.radius * 2f);
        if (c is CapsuleCollider cc) { Vector3 s = Vector3.one * cc.radius * 2f; s[cc.direction] = Mathf.Max(cc.height, cc.radius * 2f); return Box(t, cc.center, s); }
        if (c is MeshCollider mc && mc.sharedMesh != null) return WorldBounds(mc.sharedMesh, t);
        return new Bounds(t.position, Vector3.zero);
    }

    static Bounds Box(Transform t, Vector3 centre, Vector3 size)
    {
        Bounds b = new Bounds(t.TransformPoint(centre), Vector3.zero);
        for (int i = 0; i < 8; i++)
            b.Encapsulate(t.TransformPoint(centre + Vector3.Scale(size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return b;
    }

    static Bounds WorldBounds(Mesh m, Transform t) { return Box(t, m.bounds.center, m.bounds.size); }

    static bool HasAny(Transform t, HashSet<Transform> set)
    {
        foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) if (set.Contains(c)) return true;
        return false;
    }

    static bool UnderAny(Transform t, HashSet<Transform> set)
    {
        for (Transform p = t.parent; p != null; p = p.parent) if (set.Contains(p)) return true;
        return false;
    }

    // ----- 3. positions kept in scripts' settings (lamp lights, things a car can knock over, Tube entrances, ...)

    static int LiftSettings(HillSet hill, LundunHillRecord rec)
    {
        EditorUtility.DisplayProgressBar("Lundun", "Lifting the positions scripts keep…", 0.92f);
        int n = 0;
        foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // falling leaves find the ground themselves; the hills' own record; the player, cars, people (Excluded)
            if (mb == null || mb is LundunHillRecord || mb is LundunFallingLeaves || Excluded(mb.transform)) continue;
            SerializedObject so = new SerializedObject(mb);
            SerializedProperty p = so.GetIterator();
            bool changed = false, enter = true;
            int guard = 0;
            while (p.Next(enter) && guard++ < 500000)
            {
                enter = Worth(p);
                if (p.propertyType != SerializedPropertyType.Vector3) continue;
                Vector3 v = p.vector3Value;
                if (v.y > HighestThing || v.y < LowestThing) continue;
                float h = (float)hill.Lift(v.x, v.y, v.z);
                if (h < 0.002f) continue;
                v.y += h; p.vector3Value = v;
                rec.settings.Add(new LundunHillRecord.Setting { on = mb, path = p.propertyPath, by = h });
                changed = true; n++;
            }
            if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); Touched(mb); }
        }
        return n;
    }

    /// Only look inside things that can hold a position: not long lists of numbers, names or references.
    static bool Worth(SerializedProperty p)
    {
        if (p.propertyType != SerializedPropertyType.Generic) return false;
        if (!p.isArray) return true;
        string t = p.arrayElementType;
        return !(t == "float" || t == "int" || t == "double" || t == "bool" || t == "string" || t == "char" || t == "byte" || t == "long"
            || t == "Vector2" || t == "Vector4" || t == "Color" || t == "Quaternion" || t == "Vector2Int" || t.StartsWith("PPtr<"));
    }

    // ----- 4. plinths under the buildings on a slope (they'd otherwise float over the downhill pavement)

    static int Plinths(HillSet hill, GameObject root, ref Mesh container)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(PlinthMat);
        if (mat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(sh);
            mat.SetColor("_BaseColor", new Color(0.46f, 0.43f, 0.39f));      // London stock-brick plinth, sooty
            mat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(mat, PlinthMat);
        }
        GameObject group = new GameObject("Plinths");
        group.transform.SetParent(root.transform, false);
        // one mesh per 200 m square of the map
        Dictionary<long, List<HillBuilding>> blocks = new Dictionary<long, List<HillBuilding>>();
        int n = 0;
        foreach (HillBuilding b in hill.buildings)
        {
            if (b.lift - b.low < 0.05) continue;
            long k = ((long)System.Math.Floor(b.c.x / 200) << 32) ^ (uint)(int)System.Math.Floor(b.c.z / 200);
            List<HillBuilding> l;
            if (!blocks.TryGetValue(k, out l)) blocks[k] = l = new List<HillBuilding>();
            l.Add(b); n++;
        }
        foreach (KeyValuePair<long, List<HillBuilding>> kv in blocks)
        {
            List<Vector3> v = new List<Vector3>(); List<Vector3> nr = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> tri = new List<int>();
            foreach (HillBuilding b in kv.Value)
            {
                const double Out = 0.03;                                    // just proud of the walls above
                float top = (float)b.lift, bottom = (float)b.low - 0.4f;
                V2[] c =
                {
                    b.c + b.f * (b.hl + Out) + b.l * (b.hd + Out), b.c - b.f * (b.hl + Out) + b.l * (b.hd + Out),
                    b.c - b.f * (b.hl + Out) - b.l * (b.hd + Out), b.c + b.f * (b.hl + Out) - b.l * (b.hd + Out),
                };
                for (int s = 0; s < 4; s++)
                {
                    V2 a = c[s], e = c[(s + 1) % 4];
                    V2 mid = (a + e) * 0.5;
                    V2 outward = (mid - b.c).Norm;
                    Vector3 pa = new Vector3((float)a.x, bottom, (float)a.z), pb = new Vector3((float)e.x, bottom, (float)e.z);
                    Vector3 qa = new Vector3((float)a.x, top, (float)a.z), qb = new Vector3((float)e.x, top, (float)e.z);
                    Vector3 nn = new Vector3((float)outward.x, 0f, (float)outward.z);
                    int i0 = v.Count;
                    v.Add(pa); v.Add(pb); v.Add(qb); v.Add(qa);
                    for (int q = 0; q < 4; q++) nr.Add(nn);
                    float len = (float)V2.Dist(a, e);
                    uv.Add(new Vector2(0f, bottom)); uv.Add(new Vector2(len, bottom)); uv.Add(new Vector2(len, top)); uv.Add(new Vector2(0f, top));
                    // face outwards whichever way round the corners go
                    Vector3 fn = Vector3.Cross(pb - pa, qa - pa);
                    if (Vector3.Dot(fn, nn) >= 0f) { tri.Add(i0); tri.Add(i0 + 1); tri.Add(i0 + 2); tri.Add(i0); tri.Add(i0 + 2); tri.Add(i0 + 3); }
                    else { tri.Add(i0); tri.Add(i0 + 2); tri.Add(i0 + 1); tri.Add(i0); tri.Add(i0 + 3); tri.Add(i0 + 2); }
                }
            }
            Mesh m = new Mesh { name = "Plinths_" + kv.Key };
            m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
            if (container == null) { AssetDatabase.CreateAsset(m, MeshPath); container = m; }
            else AssetDatabase.AddObjectToAsset(m, container);
            GameObject go = new GameObject(m.name);
            go.transform.SetParent(group.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = m;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }
        return n;
    }

    // ----- shared

    static bool OnHill(HillSet hill, Bounds b)
    {
        if (b.min.y > HighestThing || b.max.y < LowestThing) return false;
        return hill.Rises(b.min.x, b.min.z, b.max.x, b.max.z);
    }

    static bool Big(Bounds b) { return b.size.x > SmallObject || b.size.z > SmallObject; }

    /// Not the hill's business: the player, people, cars, cameras, the HUD, the test hill, and anything that moves by
    /// physics (they find the ground themselves).
    static bool Excluded(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p.name == RootName || p.GetComponent<LundunTestHill>() != null) return true;
            if (p.GetComponent<Camera>() != null || p.GetComponent<Canvas>() != null || p.GetComponent<CharacterController>() != null) return true;
            if (p.GetComponent<Animator>() != null || p.GetComponent<UnityEngine.AI.NavMeshAgent>() != null) return true;
            Rigidbody rb = p.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) return true;
        }
        return false;
    }

    /// Saves a change on a prefab's copy in the scene too (harmless on anything else).
    static void Touched(Object o)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        EditorUtility.SetDirty(o);
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, i));
        AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }
}
