// G8e: Lundun → 42  Walk-down Tube Entrances. Every Underground entrance (the dressing's, menu 13, and G6a's, 38)
// becomes a real stairwell: steps cut into the pavement going down about 3 m, a landing, tiled walls, and a lit way in
// at the bottom; walk in and you're in the ticket hall (G5), just as with the TAKE THE TUBE button (which stays, with
// SKIP). Nothing is rebuilt:
//   • the pavement, ground and paving round each entrance get a copy with the stairwell cut out exactly (and the
//     painted steps gone); the copy replaces the mesh and its collider's. The originals are never changed
//   • the old solid box over each dressing entrance is switched off (walls and a slope take its place)
//   • the stairwells themselves are one new object, LundunTubeStairs, which also remembers every change: 42 again, or
//     42b, puts everything back exactly
// The order things are applied in is autumn (37) → Tube stairs (42) → hills (40): 42 takes the hills out first and
// puts them back after, and 37 does the same with both, so you never need to rerun anything else. Then 3 (walking map).
using System;
using System.Collections.Generic;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunTubeStairsBuilder
{
    const string Dir = "Assets/World/TubeStairs";
    const string MeshPath = Dir + "/TubeStairMeshes.asset";
    const string CutPath = Dir + "/TubeStairCuts.asset";
    const string DataPath = "Assets/World/LundunWorld.json";
    const string RootName = "LundunTubeStairs";

    [MenuItem("Lundun/42  Walk-down Tube Entrances (G8e)", false, 54)]
    static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        if (GameObject.Find("LundunDressing") == null) { Dialog("No street dressing in the scene: run Lundun → 13 first."); return; }
        bool hills = LundunHillsBuilder.Present;
        try
        {
            if (hills) { EditorUtility.DisplayProgressBar("Lundun", "Taking the hills out for a moment…", 0.02f); LundunHillsBuilder.TakeOut(); }
            string msg = Apply();
            if (hills) { EditorUtility.DisplayProgressBar("Lundun", "Putting the hills back…", 0.9f); LundunHillsBuilder.PutIn(); msg += "\n\nThe hills were taken out and put back."; hills = false; }
            EditorUtility.ClearProgressBar();
            Dialog(msg + "\n\nNow run Lundun → 3 (Bake Navigation), then save the scene (Cmd+S).");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (hills) { try { LundunHillsBuilder.PutIn(); } catch (Exception e2) { Debug.LogException(e2); } }
            EditorUtility.ClearProgressBar();
            Dialog("Something went wrong making the stairwells (see the Console). Run Lundun → 42b to put the entrances back as they were.");
        }
    }

    [MenuItem("Lundun/42b  Remove Walk-down Tube Entrances (G8e)", false, 55)]
    static void Remove()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!Present) { Dialog("There are no walk-down entrances in this scene."); return; }
        bool hills = LundunHillsBuilder.Present;
        if (hills) LundunHillsBuilder.TakeOut();
        int n = TakeOut();
        if (hills) LundunHillsBuilder.PutIn();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Dialog("The entrances are back as they were (" + n + " changes put back)." + (hills ? "\n\nThe hills were taken out and put back." : "")
            + "\n\nRun Lundun → 3, then save (Cmd+S).");
    }

    // ------------------------------------------------------------------ for 37 (autumn), which comes before this
    public static bool Present { get { return GameObject.Find(RootName) != null; } }

    /// Everything 42 did put back exactly, and LundunTubeStairs removed. Returns how many changes.
    public static int TakeOut()
    {
        GameObject old = GameObject.Find(RootName);
        int n = 0;
        if (old != null)
        {
            LundunTubeStairsRecord rec = old.GetComponent<LundunTubeStairsRecord>();
            if (rec != null)
            {
                for (int i = rec.swaps.Count - 1; i >= 0; i--)
                {
                    LundunTubeStairsRecord.Swap sw = rec.swaps[i];
                    if (sw.filter != null) { sw.filter.sharedMesh = sw.was; Touched(sw.filter); n++; }
                    if (sw.collider != null) { sw.collider.sharedMesh = sw.was; Touched(sw.collider); n++; }
                }
                foreach (Collider c in rec.switchedOff) if (c != null) { c.enabled = true; Touched(c); n++; }
            }
            Object.DestroyImmediate(old);
        }
        AssetDatabase.DeleteAsset(CutPath);
        AssetDatabase.DeleteAsset(MeshPath);
        return n;
    }

    /// 42's work (with the hills out): takes out the last stairwells, then makes them all. Returns the message.
    public static string Apply()
    {
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        if (json == null) return "Can't find " + DataPath + ".";
        TakeOut();
        EditorUtility.DisplayProgressBar("Lundun", "Finding every Tube entrance…", 0.1f);
        LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
        bool withNew = GameObject.Find("LundunTubeLines") != null;
        bool withExp = GameObject.Find("LundunExpansion") != null && LundunGreyboxBuilder.HasExpansions();
        TubeStairsResult r = Generator.TubeStairs(data, withNew, withExp);

        EnsureFolder(Dir);
        GameObject root = new GameObject(RootName);
        LundunTubeStairsRecord rec = root.AddComponent<LundunTubeStairsRecord>();

        // 1. cut the stairwells out of the pavement
        EditorUtility.DisplayProgressBar("Lundun", "Cutting the stairwells into the pavement…", 0.35f);
        int cut = Cut(r, rec, root);

        // 2. the old solid boxes over the dressing's entrances off
        foreach (BoxCollider bc in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (bc.gameObject.name != "Underground" || !bc.enabled) continue;
            Vector3 p = bc.transform.position;
            foreach (StairHole h in r.holes)
                if (h.fromDressing && (new Vector2(p.x - (float)h.c.x, p.z - (float)h.c.z)).sqrMagnitude < 2.5f * 2.5f)
                {
                    bc.enabled = false; Touched(bc);
                    rec.switchedOff.Add(bc);
                    break;
                }
        }

        // 3. the stairwells: meshes, colliders, and the way in at the bottom of each
        EditorUtility.DisplayProgressBar("Lundun", "Building the steps…", 0.75f);
        Dictionary<string, Material> mats = new Dictionary<string, Material>
        {
            { Generator.StairTileMat, Plain("TubeStairs_Tile", new Color(0.86f, 0.83f, 0.74f), 0.45f, false) },
            { Generator.StairStepMat, Plain("TubeStairs_Steps", new Color(0.36f, 0.36f, 0.37f), 0.15f, false) },
            { Generator.StairNosingMat, Plain("TubeStairs_Edges", new Color(0.92f, 0.76f, 0.12f), 0.2f, false) },
            { Generator.StairDarkMat, Plain("TubeStairs_WayIn", new Color(0.03f, 0.03f, 0.04f), 0f, false) },
            { Generator.StairGlowMat, Plain("TubeStairs_Lights", new Color(1f, 0.97f, 0.88f), 0f, true) },
        };
        Mesh container = null;
        foreach (MeshData md in r.meshes)
        {
            Mesh mesh = ToMesh(md);
            if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; }
            else AssetDatabase.AddObjectToAsset(mesh, container);
            GameObject go = new GameObject(md.name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mats[md.material];
            mr.shadowCastingMode = md.material == Generator.StairGlowMat ? ShadowCastingMode.Off : ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }
        GameObject cols = new GameObject("Colliders");
        cols.transform.SetParent(root.transform, false);
        int ways = 0;
        foreach (StairBox b in r.boxes)
        {
            string name = b.name, station = "";
            int bar = name.IndexOf('|');
            if (bar >= 0) { station = name.Substring(bar + 1); name = name.Substring(0, bar); }
            GameObject g = new GameObject(name);
            g.transform.SetParent(cols.transform, false);
            g.transform.SetPositionAndRotation(new Vector3((float)b.cx, (float)b.cy, (float)b.cz), Quaternion.Euler((float)b.pitch, (float)b.yaw, 0f));
            BoxCollider bc = g.AddComponent<BoxCollider>();
            bc.size = new Vector3((float)b.sx, (float)b.sy, (float)b.sz);
            if (b.trigger)
            {
                bc.isTrigger = true;
                g.AddComponent<LundunTubeWayIn>().Set(station);
                g.layer = 2;                                                    // Ignore Raycast: the camera and shots pass through
                ways++;
            }
        }
        EditorUtility.SetDirty(rec);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        string msg = ways + " Tube entrances are now stairwells you can walk down (" + string.Join("; ", r.log.ToArray()) + "). "
            + cut + " pavement and ground meshes were swapped for copies with the stairwells cut out; "
            + rec.switchedOff.Count + " old solid boxes switched off.";
        if (!withNew) msg += "\n\n(The G6a stations aren't in this scene, so only the dressing's entrances were done.)";
        Debug.Log("[Lundun] G8e: " + msg.Replace("\n", " "));
        return msg;
    }

    // ------------------------------------------------------------------ cutting
    static int Cut(TubeStairsResult r, LundunTubeStairsRecord rec, GameObject root)
    {
        if (r.holes.Count == 0) return 0;
        Mesh container = null;
        int n = 0;
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Mesh src = mf.sharedMesh;
            if (src == null || Excluded(mf.transform)) continue;
            Bounds wb = WorldBounds(src, mf.transform);
            List<StairHole> near = new List<StairHole>();
            foreach (StairHole h in r.holes)
            {
                double rad = Math.Sqrt(h.u0 * h.u0 + h.hw * h.hw) + 1;
                if (wb.max.x < h.c.x - rad || wb.min.x > h.c.x + rad || wb.max.z < h.c.z - rad || wb.min.z > h.c.z + rad) continue;
                if (wb.min.y > h.y + 0.3 || wb.max.y < h.y - 0.35) continue;
                near.Add(h);
            }
            if (near.Count == 0) continue;
            if (!src.isReadable) { Debug.LogWarning("[Lundun] G8e: " + src.name + " isn't readable, so it wasn't cut"); continue; }
            Mesh copy = CutMesh(src, mf.transform, near);
            if (copy == null) continue;
            if (container == null) { AssetDatabase.CreateAsset(copy, CutPath); container = copy; }
            else AssetDatabase.AddObjectToAsset(copy, container);
            MeshCollider mc = mf.GetComponent<MeshCollider>();
            if (mc != null && mc.sharedMesh != src) mc = null;
            rec.swaps.Add(new LundunTubeStairsRecord.Swap { filter = mf, collider = mc, was = src });
            mf.sharedMesh = copy; Touched(mf);
            if (mc != null) { mc.sharedMesh = copy; Touched(mc); }
            n++;
        }
        return n;
    }

    /// A copy of a mesh with every hole cut out of its flat parts at pavement level. Null if nothing was cut.
    static Mesh CutMesh(Mesh src, Transform t, List<StairHole> holes)
    {
        for (int s = 0; s < src.subMeshCount; s++) if (src.GetTopology(s) != MeshTopology.Triangles) return null;
        Matrix4x4 toWorld = t.localToWorldMatrix, toLocal = t.worldToLocalMatrix;
        Vector3[] v = src.vertices; Vector3[] nrm = src.normals; Vector4[] tan = src.tangents;
        Vector2[] uv = src.uv, uv2 = src.uv2; Color[] col = src.colors;
        int n = v.Length;
        bool hasN = nrm.Length == n, hasT = tan.Length == n, hasUv = uv.Length == n, hasUv2 = uv2.Length == n, hasC = col.Length == n;
        int stride = 3 + (hasN ? 3 : 0) + (hasT ? 4 : 0) + (hasUv ? 2 : 0) + (hasUv2 ? 2 : 0) + (hasC ? 4 : 0);
        List<float> d = new List<float>(n * stride + 64);
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
        bool any = false;
        List<int>[] subs = new List<int>[src.subMeshCount];
        for (int s = 0; s < src.subMeshCount; s++)
        {
            List<int> tris = new List<int>();
            src.GetTriangles(tris, s);
            foreach (StairHole h in holes)
            {
                double lo = h.y - 0.35, hi = h.y + 0.3;
                Func<int, int, int, bool> flat = (a, b, c) =>
                    d[a * stride + 1] >= lo && d[a * stride + 1] <= hi && d[b * stride + 1] >= lo && d[b * stride + 1] <= hi && d[c * stride + 1] >= lo && d[c * stride + 1] <= hi;
                bool cut;
                tris = Generator.CutRect(d, stride, tris, h.c, h.f, h.u0, h.u1, h.hw, flat, out cut);
                any |= cut;
            }
            subs[s] = tris;
        }
        if (!any) return null;

        int m = d.Count / stride;
        List<Vector3> pv = new List<Vector3>(m), pn = new List<Vector3>(hasN ? m : 0);
        List<Vector4> pt = new List<Vector4>(hasT ? m : 0);
        List<Vector2> pu = new List<Vector2>(hasUv ? m : 0), pu2 = new List<Vector2>(hasUv2 ? m : 0);
        List<Color> pc = new List<Color>(hasC ? m : 0);
        for (int i = 0; i < m; i++)
        {
            int o = i * stride;
            pv.Add(toLocal.MultiplyPoint3x4(new Vector3(d[o], d[o + 1], d[o + 2]))); o += 3;
            if (hasN) { Vector3 nn = new Vector3(d[o], d[o + 1], d[o + 2]); pn.Add(nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.up); o += 3; }
            if (hasT) { Vector3 tt = new Vector3(d[o], d[o + 1], d[o + 2]); if (tt.sqrMagnitude > 1e-12f) tt.Normalize(); pt.Add(new Vector4(tt.x, tt.y, tt.z, d[o + 3] < 0 ? -1f : 1f)); o += 4; }
            if (hasUv) { pu.Add(new Vector2(d[o], d[o + 1])); o += 2; }
            if (hasUv2) { pu2.Add(new Vector2(d[o], d[o + 1])); o += 2; }
            if (hasC) { pc.Add(new Color(d[o], d[o + 1], d[o + 2], d[o + 3])); o += 4; }
        }
        Mesh mesh = new Mesh { name = src.name + " (Tube stairs)" };
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

    /// Not the stairs' business: the stairwells themselves, the hills' and autumn's own objects, and anything that moves.
    static bool Excluded(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p.name == RootName || p.name == "LundunHills" || p.GetComponent<LundunTestHill>() != null) return true;
            if (p.GetComponent<Camera>() != null || p.GetComponent<Canvas>() != null || p.GetComponent<CharacterController>() != null) return true;
            if (p.GetComponent<Animator>() != null || p.GetComponent<Rigidbody>() != null) return true;
        }
        return false;
    }

    static Bounds WorldBounds(Mesh m, Transform t)
    {
        Bounds lb = m.bounds;
        Bounds b = new Bounds(t.TransformPoint(lb.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            b.Encapsulate(t.TransformPoint(lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return b;
    }

    // ------------------------------------------------------------------ helpers
    static Material Plain(string name, Color c, float smooth, bool glow)
    {
        string path = Dir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else m.shader = sh;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        if (glow)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 1.6f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh ToMesh(MeshData md)
    {
        int n = md.VertexCount;
        List<Vector3> v = new List<Vector3>(n);
        for (int i = 0; i < n; i++) v.Add(new Vector3(md.pos[i * 3], md.pos[i * 3 + 1], md.pos[i * 3 + 2]));
        Mesh mesh = new Mesh { name = md.name, indexFormat = n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(v);
        mesh.SetTriangles(md.tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

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
