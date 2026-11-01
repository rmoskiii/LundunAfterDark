// Sets up Mixamo files as proper Humanoids from a script.
// When you pick "Humanoid" in the Inspector, Unity maps the bones for you; when a script does it, Unity doesn't —
// the avatar comes out empty (no bones mapped), so the character stands in a T-pose and slides.
// This fills in the same 52-bone mapping your working Mixamo models (Ch15, Ch37, Ch06) use, adjusted to each
// file's own bone-name prefix (Mixamo downloads vary: "mixamorig:", "mixamorig1:", "mixamorig7:" …).
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class LundunSkeletons
{
    // Mixamo bone (without its prefix) → Unity human bone. Copied from Assets/NPC/Ch15_nonPBR (works in your game).
    static readonly string[,] Map = {
        { "Hips", "Hips" }, { "LeftUpLeg", "LeftUpperLeg" }, { "RightUpLeg", "RightUpperLeg" }, { "LeftLeg", "LeftLowerLeg" },
        { "RightLeg", "RightLowerLeg" }, { "LeftFoot", "LeftFoot" }, { "RightFoot", "RightFoot" }, { "Spine", "Spine" },
        { "Spine1", "Chest" }, { "Spine2", "UpperChest" }, { "Neck", "Neck" }, { "Head", "Head" },
        { "LeftShoulder", "LeftShoulder" }, { "RightShoulder", "RightShoulder" }, { "LeftArm", "LeftUpperArm" }, { "RightArm", "RightUpperArm" },
        { "LeftForeArm", "LeftLowerArm" }, { "RightForeArm", "RightLowerArm" }, { "LeftHand", "LeftHand" }, { "RightHand", "RightHand" },
        { "LeftToeBase", "LeftToes" }, { "RightToeBase", "RightToes" },
        { "LeftHandThumb1", "Left Thumb Proximal" }, { "LeftHandThumb2", "Left Thumb Intermediate" }, { "LeftHandThumb3", "Left Thumb Distal" },
        { "LeftHandIndex1", "Left Index Proximal" }, { "LeftHandIndex2", "Left Index Intermediate" }, { "LeftHandIndex3", "Left Index Distal" },
        { "LeftHandMiddle1", "Left Middle Proximal" }, { "LeftHandMiddle2", "Left Middle Intermediate" }, { "LeftHandMiddle3", "Left Middle Distal" },
        { "LeftHandRing1", "Left Ring Proximal" }, { "LeftHandRing2", "Left Ring Intermediate" }, { "LeftHandRing3", "Left Ring Distal" },
        { "LeftHandPinky1", "Left Little Proximal" }, { "LeftHandPinky2", "Left Little Intermediate" }, { "LeftHandPinky3", "Left Little Distal" },
        { "RightHandThumb1", "Right Thumb Proximal" }, { "RightHandThumb2", "Right Thumb Intermediate" }, { "RightHandThumb3", "Right Thumb Distal" },
        { "RightHandIndex1", "Right Index Proximal" }, { "RightHandIndex2", "Right Index Intermediate" }, { "RightHandIndex3", "Right Index Distal" },
        { "RightHandMiddle1", "Right Middle Proximal" }, { "RightHandMiddle2", "Right Middle Intermediate" }, { "RightHandMiddle3", "Right Middle Distal" },
        { "RightHandRing1", "Right Ring Proximal" }, { "RightHandRing2", "Right Ring Intermediate" }, { "RightHandRing3", "Right Ring Distal" },
        { "RightHandPinky1", "Right Little Proximal" }, { "RightHandPinky2", "Right Little Intermediate" }, { "RightHandPinky3", "Right Little Distal" } };

    static readonly string[] Required = { "Hips", "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot", "Spine", "Head",
                                          "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftHand", "RightHand" };

    /// Fills in the importer's Humanoid mapping and T-pose from the file itself. Call before SaveAndReimport.
    /// Returns false (and leaves the importer alone) if this doesn't look like a Mixamo skeleton.
    public static bool ConfigureHumanoid(ModelImporter mi, string path)
    {
        // A file whose last Humanoid import failed may have no bones to read: import it plainly first.
        if (mi.animationType != ModelImporterAnimationType.Generic || mi.avatarSetup == ModelImporterAvatarSetup.CopyFromOther)
        {
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.sourceAvatar = null;
            mi.SaveAndReimport();
        }
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) return false;
        Transform[] all = go.GetComponentsInChildren<Transform>(true);
        string prefix = null;
        foreach (Transform t in all)
            if (t.name == "Hips" || t.name.EndsWith(":Hips")) { prefix = t.name.Substring(0, t.name.Length - 4); break; }
        if (prefix == null) { Debug.LogWarning("[Lundun] " + path + ": no Hips bone found — is it a Mixamo file?"); return false; }

        HashSet<string> names = new HashSet<string>();
        foreach (Transform t in all) names.Add(t.name);
        foreach (string r in Required)
            if (!names.Contains(prefix + r)) { Debug.LogWarning("[Lundun] " + path + ": missing bone " + prefix + r + " — not set up as Humanoid."); return false; }

        List<HumanBone> human = new List<HumanBone>();
        for (int i = 0; i < Map.GetLength(0); i++)
        {
            string bone = prefix + Map[i, 0];
            if (!names.Contains(bone)) continue;
            HumanBone hb = new HumanBone { boneName = bone, humanName = Map[i, 1] };
            hb.limit.useDefaultValues = true;
            human.Add(hb);
        }

        // Reference pose: Mixamo files are only *roughly* T-posed (arms a bit low, fingers curled), which Unity
        // flags as "Character is not in T pose" and which skews every animation. Your Ch15 / Ch37 setups hold a
        // clean T-pose; Mixamo skeletons share bone orientations, so borrow those rotations (by bone name, prefix
        // ignored) and keep this file's own bone lengths.
        Dictionary<string, Quaternion> tpose = TemplateTPose();
        List<SkeletonBone> skeleton = new List<SkeletonBone>();
        foreach (Transform t in all)
        {
            Quaternion rot = t.localRotation;
            Quaternion q;
            if (t != go.transform && tpose.TryGetValue(Suffix(t.name), out q) && Quaternion.Angle(q, t.localRotation) < 35f) rot = q;
            skeleton.Add(new SkeletonBone { name = t == go.transform ? go.name : t.name, position = t.localPosition, rotation = rot, scale = t.localScale });
        }

        HumanDescription hd = mi.humanDescription;
        hd.human = human.ToArray();
        hd.skeleton = skeleton.ToArray();
        hd.upperArmTwist = 0.5f; hd.lowerArmTwist = 0.5f; hd.upperLegTwist = 0.5f; hd.lowerLegTwist = 0.5f;
        hd.armStretch = 0.05f; hd.legStretch = 0.05f; hd.feetSpacing = 0f; hd.hasTranslationDoF = false;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.sourceAvatar = null;
        mi.humanDescription = hd;
        return true;
    }

    static Dictionary<string, Quaternion> tposeCache;
    static readonly string[] Templates = { "Assets/NPC/Ch15_nonPBR.fbx", "Assets/NPC/Ch37_nonPBR@Walking.fbx" };

    static string Suffix(string bone) { int i = bone.LastIndexOf(':'); return i >= 0 ? bone.Substring(i + 1) : bone; }

    /// Bone name (no prefix) → T-pose rotation, from your working Mixamo model's Humanoid setup.
    static Dictionary<string, Quaternion> TemplateTPose()
    {
        if (tposeCache != null) return tposeCache;
        tposeCache = new Dictionary<string, Quaternion>();
        foreach (string path in Templates)
        {
            ModelImporter t = AssetImporter.GetAtPath(path) as ModelImporter;
            if (t == null || t.humanDescription.skeleton == null) continue;
            foreach (SkeletonBone b in t.humanDescription.skeleton)
                if (b.name != null && b.name.Contains("mixamorig") && !tposeCache.ContainsKey(Suffix(b.name)))
                    tposeCache[Suffix(b.name)] = b.rotation;
            if (tposeCache.Count > 20) break;
        }
        if (tposeCache.Count == 0) Debug.LogWarning("[Lundun] Couldn't read a T-pose from Ch15 / Ch37 — using each file's own pose.");
        return tposeCache;
    }

    /// True if the file's avatar came out as a working Humanoid.
    public static bool AvatarOk(string path)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            Avatar a = o as Avatar;
            if (a != null) return a.isValid && a.isHuman;
        }
        return false;
    }
}
