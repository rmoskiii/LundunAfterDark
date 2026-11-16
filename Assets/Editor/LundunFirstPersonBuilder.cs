// Lundun → 33  First Person Arms (V1)
//   Cuts just the arms (upper arms, forearms, hands and fingers: his own sleeves and hands) out of each of James's
//   skinned meshes and saves them in Assets/World/FirstPerson, then gives James a LundunViewModel that draws them in
//   first person on his own skeleton (so they're wherever the gun rig puts his hands). Run it once; again if James's
//   model ever changes. Then Cmd+S.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LundunFirstPersonBuilder
{
    const string Folder = "Assets/World/FirstPerson";

    [MenuItem("Lundun/33  First Person Arms (V1)", false, 39)]
    public static void Build()
    {
        PlayerController pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (pc == null) { Dialog("Can't find James (PlayerController) in the open scene. Open the Lundun scene first."); return; }
        Animator anim = pc.GetComponentInChildren<Animator>(true);
        if (anim == null || !anim.isHuman || anim.avatar == null) { Dialog("James's Animator isn't a Humanoid rig, so his arms can't be found."); return; }

        // the arm bones: both upper arms and everything below them (forearms, hands, fingers)
        List<Transform> roots = new List<Transform>();
        foreach (HumanBodyBones hb in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
        {
            Transform t = anim.GetBoneTransform(hb);
            if (t != null) roots.Add(t);
        }
        if (roots.Count == 0) { Dialog("James's rig has no arm bones."); return; }

        if (!AssetDatabase.IsValidFolder(Folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/World")) AssetDatabase.CreateFolder("Assets", "World");
            AssetDatabase.CreateFolder("Assets/World", "FirstPerson");
        }

        List<Mesh> meshes = new List<Mesh>();
        List<string> sources = new List<string>();
        List<string> report = new List<string>();
        foreach (SkinnedMeshRenderer smr in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null || smr.name.EndsWith("_FPArms")) continue;
            Mesh src = Readable(smr);
            if (src == null) { report.Add(smr.name + ": couldn't read the mesh"); continue; }
            Mesh arms = CutArms(src, smr.bones, roots);
            if (arms == null) { report.Add(smr.name + ": no arms in it"); continue; }
            string path = Folder + "/" + Safe(smr.name) + "_Arms.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(arms, path);
            meshes.Add(arms);
            sources.Add(smr.name);
            report.Add(smr.name + ": " + arms.vertexCount + " vertices of arms (of " + src.vertexCount + ")");
        }
        AssetDatabase.SaveAssets();
        if (meshes.Count == 0) { Dialog("No arms found in James's meshes:\n" + string.Join("\n", report.ToArray())); return; }

        LundunViewModel vm = pc.GetComponent<LundunViewModel>();
        if (vm == null) vm = Undo.AddComponent<LundunViewModel>(pc.gameObject);
        Undo.RecordObject(vm, "First person arms");
        vm.SetArms(meshes.ToArray(), sources.ToArray());
        EditorUtility.SetDirty(vm);
        EditorSceneManager.MarkSceneDirty(pc.gameObject.scene);
        Debug.Log("[Lundun] V1: first-person arms cut from James's model (" + string.Join("; ", report.ToArray()) +
                  "). Saved in " + Folder + ". Cmd+S, then Play and press the eye button.");
    }

    /// The mesh with its vertex data readable (in the editor it nearly always is; if not, the model is set to Read/Write).
    static Mesh Readable(SkinnedMeshRenderer smr)
    {
        Mesh m = smr.sharedMesh;
        if (m.isReadable && m.vertexCount > 0) return m;
        string path = AssetDatabase.GetAssetPath(m);
        ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi != null && !mi.isReadable)
        {
            mi.isReadable = true;
            mi.SaveAndReimport();
            m = smr.sharedMesh;
        }
        return m != null && m.vertexCount > 0 ? m : null;
    }

    /// A new mesh of only the triangles whose three corners are mostly moved by arm bones (same skeleton, same bind poses).
    static Mesh CutArms(Mesh src, Transform[] bones, List<Transform> roots)
    {
        BoneWeight[] bw = src.boneWeights;
        if (bw == null || bw.Length != src.vertexCount || bones == null) return null;
        bool[] armBone = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            Transform b = bones[i];
            foreach (Transform r in roots) if (b != null && (b == r || b.IsChildOf(r))) armBone[i] = true;
        }
        bool[] armVert = new bool[bw.Length];
        for (int v = 0; v < bw.Length; v++)
        {
            BoneWeight w = bw[v];
            int best = w.boneIndex0; float bwgt = w.weight0;
            if (w.weight1 > bwgt) { best = w.boneIndex1; bwgt = w.weight1; }
            if (w.weight2 > bwgt) { best = w.boneIndex2; bwgt = w.weight2; }
            if (w.weight3 > bwgt) { best = w.boneIndex3; }
            armVert[v] = best >= 0 && best < armBone.Length && armBone[best];
        }

        Vector3[] pos = src.vertices, nrm = src.normals;
        Vector4[] tan = src.tangents;
        Vector2[] uv = src.uv, uv2 = src.uv2;
        Color[] col = src.colors;
        int[] map = new int[pos.Length];
        for (int i = 0; i < map.Length; i++) map[i] = -1;
        List<Vector3> nPos = new List<Vector3>(), nNrm = new List<Vector3>();
        List<Vector4> nTan = new List<Vector4>();
        List<Vector2> nUv = new List<Vector2>(), nUv2 = new List<Vector2>();
        List<Color> nCol = new List<Color>();
        List<BoneWeight> nBw = new List<BoneWeight>();
        List<int>[] subs = new List<int>[src.subMeshCount];
        int kept = 0;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            subs[s] = new List<int>();
            int[] tri = src.GetTriangles(s);
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                if (!armVert[a] || !armVert[b] || !armVert[c]) continue;
                foreach (int v in new[] { a, b, c })
                {
                    if (map[v] < 0)
                    {
                        map[v] = nPos.Count;
                        nPos.Add(pos[v]);
                        if (nrm.Length == pos.Length) nNrm.Add(nrm[v]);
                        if (tan.Length == pos.Length) nTan.Add(tan[v]);
                        if (uv.Length == pos.Length) nUv.Add(uv[v]);
                        if (uv2.Length == pos.Length) nUv2.Add(uv2[v]);
                        if (col.Length == pos.Length) nCol.Add(col[v]);
                        nBw.Add(bw[v]);
                    }
                    subs[s].Add(map[v]);
                }
                kept++;
            }
        }
        if (kept == 0) return null;

        Mesh m = new Mesh { name = src.name + "_FPArms" };
        if (nPos.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(nPos);
        if (nNrm.Count == nPos.Count) m.SetNormals(nNrm);
        if (nTan.Count == nPos.Count) m.SetTangents(nTan);
        if (nUv.Count == nPos.Count) m.SetUVs(0, nUv);
        if (nUv2.Count == nPos.Count) m.SetUVs(1, nUv2);
        if (nCol.Count == nPos.Count) m.SetColors(nCol);
        m.subMeshCount = subs.Length;
        for (int s = 0; s < subs.Length; s++) m.SetTriangles(subs[s], s);
        m.boneWeights = nBw.ToArray();
        m.bindposes = src.bindposes;
        m.RecalculateBounds();
        return m;
    }

    static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }
}
