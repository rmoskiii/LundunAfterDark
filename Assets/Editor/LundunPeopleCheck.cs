// Lundun → Check People
// Prints, for every pedestrian prefab, the police officers and your original NPC, exactly what Unity sees:
// is the skeleton (avatar) valid and Humanoid, which animator controller is on it, and which clips that controller plays.
// Changes nothing. Copy the Console output to Claude if people still aren't animating.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class LundunPeopleCheck
{
    [MenuItem("Lundun/Check People", false, 60)]
    public static void Check()
    {
        List<string> paths = new List<string>();
        if (AssetDatabase.IsValidFolder("Assets/People/Prefabs"))
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/People/Prefabs" })) paths.Add(AssetDatabase.GUIDToAssetPath(g));
        foreach (string p in new[] { "Assets/Prefabs/PoliceOfficer.prefab", "Assets/Prefabs/PoliceOfficerMale.prefab", "Assets/Prefabs/NPC.prefab" })
            if (File.Exists(p)) paths.Add(p);

        System.Text.StringBuilder sb = new System.Text.StringBuilder("[Lundun] Check People\n");
        HashSet<RuntimeAnimatorController> controllers = new HashSet<RuntimeAnimatorController>();
        foreach (string p in paths)
        {
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            Animator a = go != null ? go.GetComponentInChildren<Animator>(true) : null;
            sb.Append("  ").Append(Path.GetFileNameWithoutExtension(p)).Append(": ");
            if (a == null) { sb.Append("NO ANIMATOR\n"); continue; }
            Avatar av = a.avatar;
            sb.Append("avatar=").Append(av == null ? "NONE" : av.name)
              .Append(av != null ? (av.isValid ? " valid" : " INVALID") : "")
              .Append(av != null ? (av.isHuman ? " humanoid" : " NOT-HUMANOID") : "")
              .Append(" | controller=").Append(a.runtimeAnimatorController == null ? "NONE" : a.runtimeAnimatorController.name)
              .Append(" | culling=").Append(a.cullingMode)
              .Append(" | on='").Append(a.gameObject.name).Append("'");
            if (av != null)
            {
                string src = AssetDatabase.GetAssetPath(av);
                ModelImporter mi = AssetImporter.GetAtPath(src) as ModelImporter;
                if (mi != null) sb.Append(" | rig=").Append(mi.animationType).Append("/").Append(mi.avatarSetup).Append(" bones=").Append(mi.humanDescription.human != null ? mi.humanDescription.human.Length : 0);
            }
            sb.Append("\n");
            if (a.runtimeAnimatorController != null) controllers.Add(a.runtimeAnimatorController);
        }
        foreach (RuntimeAnimatorController rc in controllers)
        {
            sb.Append("  controller ").Append(rc.name).Append(" plays:\n");
            AnimatorController ac = rc as AnimatorController;
            foreach (AnimationClip c in rc.animationClips)
            {
                string cp = AssetDatabase.GetAssetPath(c);
                ModelImporter mi = AssetImporter.GetAtPath(cp) as ModelImporter;
                sb.Append("    ").Append(c == null ? "(missing)" : c.name).Append("  ").Append(cp)
                  .Append(c != null ? (c.isHumanMotion ? "  humanoid" : "  NOT-HUMANOID") : "")
                  .Append(mi != null ? "  rig=" + mi.animationType + "/" + mi.avatarSetup : "").Append("\n");
            }
            if (ac != null && ac.layers.Length > 0 && ac.layers[0].stateMachine.defaultState != null)
                sb.Append("    starts in: ").Append(ac.layers[0].stateMachine.defaultState.name).Append("\n");
        }
        Debug.Log(sb.ToString());
    }
}
