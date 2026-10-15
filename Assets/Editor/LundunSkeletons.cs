// Mixamo names skeleton bones differently per download ("mixamorig:Hips", "mixamorig1:Hips", "mixamorig7:Hips" …).
// An animation can only borrow a character's skeleton (avatar) if the bone names match exactly, otherwise
// the animation can't drive the body and people stand in a T-pose. This picks a matching one.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class LundunSkeletons
{
    /// Name of the hips bone in a model / animation file (e.g. "mixamorig1:Hips"), or null.
    public static string HipsName(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (go == null) return null;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == "Hips" || t.name.EndsWith(":Hips")) return t.name;
        return null;
    }

    /// The first Humanoid avatar whose model uses exactly the same bone names as this animation file; null if none.
    public static Avatar Matching(string animPath, IEnumerable<Avatar> candidates)
    {
        string hips = HipsName(animPath);
        if (hips == null) return null;
        foreach (Avatar a in candidates)
        {
            if (a == null || !a.isHuman) continue;
            if (HipsName(AssetDatabase.GetAssetPath(a)) == hips) return a;
        }
        return null;
    }

    /// Humanoid avatars of every character model in a folder.
    public static List<Avatar> InFolder(string folder)
    {
        List<Avatar> list = new List<Avatar>();
        if (!AssetDatabase.IsValidFolder(folder)) return list;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
            {
                Avatar av = o as Avatar;
                if (av != null && av.isHuman) list.Add(av);
            }
        return list;
    }
}
