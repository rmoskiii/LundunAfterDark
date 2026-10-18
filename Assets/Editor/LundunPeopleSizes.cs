// Lundun → Fix People Sizes
// Some Mixamo downloads come in at the wrong size (Ped_Casual_3 was about 2.2x too tall).
// This measures each pedestrian's hip height, and any that are well off the others get their prefab scaled to normal height.
// Their NavMeshAgent and collider are shrunk back by the same amount, so they still walk and bump like everyone else.
// It doesn't re-import anything, and doesn't touch skeletons, avatars or the PeopleAnimator. Safe to run any number of times.
// Lundun → 12 runs it automatically at the end.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public static class LundunPeopleSizes
{
    const string PrefabDir = "Assets/People/Prefabs";
    const string TemplatePath = "Assets/Prefabs/NPC.prefab";
    const float Tolerance = 0.15f;          // within 15% of everyone else = left exactly as it is

    [MenuItem("Lundun/Fix People Sizes", false, 61)]
    public static void Menu() { Fix(); }

    /// Returns how many prefabs were resized. quiet = only log if something had to change.
    public static int Fix() { return Fix(false); }

    public static int Fix(bool quiet)
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir)) { Debug.LogWarning("[Lundun] No " + PrefabDir + " folder — run Lundun → 12 first."); return 0; }
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);

        // 1. everyone's hip height, in their own (unscaled) units
        List<string> paths = new List<string>();
        List<float> hips = new List<float>();
        foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            float h = HipHeight(go);
            if (h <= 0f) { Debug.LogWarning("[Lundun] " + p + ": no Hips bone found — size left alone."); continue; }
            paths.Add(p); hips.Add(h);
        }
        if (hips.Count == 0) return 0;

        // 2. "normal" = the middle one (one giant can't drag it)
        List<float> sorted = new List<float>(hips); sorted.Sort();
        float normal = sorted[sorted.Count / 2];

        // 3. scale the odd ones out
        System.Text.StringBuilder sb = new System.Text.StringBuilder("[Lundun] People sizes (normal hip height " + normal.ToString("0.00") + " m):\n");
        int changed = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            float want = Mathf.Abs(hips[i] / normal - 1f) > Tolerance ? normal / hips[i] : 1f;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            float now = asset.transform.localScale.y;
            string name = System.IO.Path.GetFileNameWithoutExtension(paths[i]);
            sb.Append("  ").Append(name).Append(": hips ").Append(hips[i].ToString("0.00")).Append(" m");
            if (Mathf.Abs(now - want) < 0.001f) { sb.Append(want == 1f ? "  ok\n" : "  already scaled x" + want.ToString("0.000") + "\n"); continue; }

            GameObject root = PrefabUtility.LoadPrefabContents(paths[i]);
            try
            {
                root.transform.localScale = Vector3.one * want;
                Compensate(root, template, want);
                PrefabUtility.SaveAsPrefabAsset(root, paths[i]);
                changed++;
                sb.Append("  → scaled x").Append(want.ToString("0.000")).Append("\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        sb.Append(changed == 0 ? "  Nothing needed changing." : "  " + changed + " prefab(s) resized — commit the changed prefab(s) in Assets/People/Prefabs.");
        if (!quiet || changed > 0) Debug.Log(sb.ToString());
        return changed;
    }

    /// Hip height above the prefab's feet (its root), in the prefab's own units: unaffected by any scale we've already set.
    static float HipHeight(GameObject go)
    {
        if (go == null) return 0f;
        Transform hips = null;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == "Hips" || t.name.EndsWith(":Hips")) { hips = t; break; }
        if (hips == null) return 0f;
        // the hips' position in the root's own space (walks up through any armature in between)
        Matrix4x4 m = Matrix4x4.identity;
        for (Transform t = hips; t != null && t != go.transform; t = t.parent)
            m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * m;
        return m.MultiplyPoint3x4(Vector3.zero).y;
    }

    /// The agent and collider are on the scaled root, so give them the template's sizes divided by the scale:
    /// in the world they stay exactly the size of your NPC's.
    static void Compensate(GameObject root, GameObject template, float s)
    {
        NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
        NavMeshAgent ta = template != null ? template.GetComponent<NavMeshAgent>() : null;
        if (agent != null)
        {
            agent.radius = (ta != null ? ta.radius : 0.5f) / s;
            agent.height = (ta != null ? ta.height : 2f) / s;
            agent.baseOffset = (ta != null ? ta.baseOffset : 0f) / s;
        }
        CapsuleCollider col = root.GetComponent<CapsuleCollider>();
        CapsuleCollider tc = template != null ? template.GetComponent<CapsuleCollider>() : null;
        if (col != null)
        {
            col.radius = (tc != null ? tc.radius : 0.3f) / s;
            col.height = (tc != null ? tc.height : 1.8f) / s;
            col.center = (tc != null ? tc.center : new Vector3(0f, 0.9f, 0f)) / s;
        }
    }
}
