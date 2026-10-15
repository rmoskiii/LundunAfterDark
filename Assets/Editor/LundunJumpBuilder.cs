// Lundun → 14  James's Jump
// Adds a "LundunAir" layer to James's animator (legs + body only, so his arms keep holding the rifle):
//   standing take-off / running take-off → falling loop → soft landing / heavy landing / roll.
// Stops the old single "JumpUp" state from playing (it's left in place, just not used) and puts
// a LundunJump component on James. A backup of his animator is saved first (PlayerAnimator_beforeR6).
// Animations come from Assets/People/Animations (Mixamo, FBX for Unity, Without Skin):
//   Anim_JumpStart ("Jumping Up"), Anim_RunJump ("Running Jump"), Anim_Fall ("Falling Idle"),
//   Anim_LandSoft ("Falling To Landing"), Anim_LandHard ("Hard Landing"), Anim_Roll ("Falling To Roll").
// Missing ones fall back to the old Jump Up clip or are skipped. Safe to run again.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunJumpBuilder
{
    const string AnimDir = "Assets/People/Animations";
    const string MaskPath = "Assets/People/Animations/LundunAirMask.mask";
    const string OldJump = "Assets/Animations/X Bot@Jump Up.fbx";

    static readonly string[] Keys = { "Anim_JumpStart", "Anim_RunJump", "Anim_Fall", "Anim_LandSoft", "Anim_LandHard", "Anim_Roll" };

    [MenuItem("Lundun/14  James's Jump", false, 14)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }

        PlayerController pc = null;
        foreach (GameObject g in scene.GetRootGameObjects()) { pc = g.GetComponentInChildren<PlayerController>(true); if (pc != null) break; }
        if (pc == null) { Dialog("Couldn't find James (no PlayerController in the scene)."); return; }
        Animator a = pc.GetComponentInChildren<Animator>(true);
        AnimatorController ac = a != null ? a.runtimeAnimatorController as AnimatorController : null;
        if (ac == null) { Dialog("Couldn't find James's animator controller."); return; }
        if (a.avatar == null || !a.avatar.isHuman) { Dialog("James's model isn't Humanoid, so the new jump can't be shared onto him."); return; }

        // backup, once
        string path = AssetDatabase.GetAssetPath(ac);
        string backup = Path.GetDirectoryName(path).Replace('\\', '/') + "/PlayerAnimator_beforeR6.controller";
        if (!File.Exists(backup)) AssetDatabase.CopyAsset(path, backup);

        // clips
        Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        List<string> missing = new List<string>();
        foreach (string key in Keys)
        {
            string p = FindAnim(key);
            AnimationClip c = p != null ? Setup(p, key) : null;
            if (c != null) clips[key] = c; else missing.Add(key);
        }
        AnimationClip old = FirstClip(OldJump);
        if (!clips.ContainsKey("Anim_JumpStart") && old != null) clips["Anim_JumpStart"] = old;
        if (!clips.ContainsKey("Anim_Fall")) clips["Anim_Fall"] = clips.ContainsKey("Anim_JumpStart") ? clips["Anim_JumpStart"] : old;
        if (!clips.ContainsKey("Anim_RunJump") && clips.ContainsKey("Anim_JumpStart")) clips["Anim_RunJump"] = clips["Anim_JumpStart"];
        if (clips["Anim_Fall"] == null) { Dialog("No jump animations found in " + AnimDir + " (and the old Jump Up clip is missing)."); return; }

        // the old single jump state: leave it, just stop going into it
        AnimatorStateMachine baseSm = ac.layers[0].stateMachine;
        int cut = 0;
        foreach (ChildAnimatorState cs in baseSm.states)
            foreach (AnimatorStateTransition t in cs.state.transitions)
                if (t.destinationState != null && t.destinationState.name == "JumpUp") { cs.state.RemoveTransition(t); cut++; }
        foreach (AnimatorStateTransition t in baseSm.anyStateTransitions)
            if (t.destinationState != null && t.destinationState.name == "JumpUp") { baseSm.RemoveAnyStateTransition(t); cut++; }

        // parameters
        Param(ac, "AirJump", AnimatorControllerParameterType.Trigger);
        Param(ac, "AirLanded", AnimatorControllerParameterType.Trigger);
        Param(ac, "AirRun", AnimatorControllerParameterType.Bool);
        Param(ac, "AirFalling", AnimatorControllerParameterType.Bool);
        Param(ac, "AirLand", AnimatorControllerParameterType.Int);
        Param(ac, "AirVSpeed", AnimatorControllerParameterType.Float);

        // the layer (rebuilt each run), placed under the punch-reactions layer
        for (int i = ac.layers.Length - 1; i >= 0; i--) if (ac.layers[i].name == LundunJump.LayerName) ac.RemoveLayer(i);
        ac.AddLayer(LundunJump.LayerName);
        AnimatorControllerLayer[] layers = ac.layers;
        AnimatorControllerLayer air = layers[layers.Length - 1];
        air.defaultWeight = 0f;
        air.blendingMode = AnimatorLayerBlendingMode.Override;
        air.avatarMask = Mask();
        int reactions = -1;
        for (int i = 0; i < layers.Length; i++) if (layers[i].name == LundunPlayerReact.LayerName) reactions = i;
        if (reactions >= 0) { layers[layers.Length - 1] = layers[reactions]; layers[reactions] = air; }
        else layers[layers.Length - 1] = air;
        ac.layers = layers;
        BuildStates(air.stateMachine, clips);
        EditorUtility.SetDirty(ac);

        // the driver
        if (pc.GetComponent<LundunJump>() == null) pc.gameObject.AddComponent<LundunJump>();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Lundun] James's jump built (" + cut + " old jump transition(s) switched off, backup: " + backup + ")." +
                  (missing.Count > 0 ? "  Not found, using fallbacks: " + string.Join(", ", missing.ToArray()) : "  All 6 animations found.") +
                  "  Cmd+S, then Play.");
    }

    static void BuildStates(AnimatorStateMachine sm, Dictionary<string, AnimationClip> clips)
    {
        AnimatorState none = sm.AddState("None"); none.writeDefaultValues = false;
        sm.defaultState = none;
        AnimatorState start = State(sm, "JumpStart", clips["Anim_JumpStart"]);
        AnimatorState run = State(sm, "RunJump", clips["Anim_RunJump"]);
        AnimatorState fall = State(sm, "Fall", clips["Anim_Fall"]);
        AnimatorState soft = clips.ContainsKey("Anim_LandSoft") ? State(sm, "LandSoft", clips["Anim_LandSoft"]) : null;
        AnimatorState hard = clips.ContainsKey("Anim_LandHard") ? State(sm, "LandHard", clips["Anim_LandHard"]) : null;
        AnimatorState roll = clips.ContainsKey("Anim_Roll") ? State(sm, "Roll", clips["Anim_Roll"]) : null;
        if (hard == null) hard = soft;
        if (roll == null) roll = hard;

        // take-off (skipping the clips' crouch, because the physics jump starts straight away)
        AnimatorStateTransition t;
        t = sm.AddAnyStateTransition(start); t.AddCondition(AnimatorConditionMode.If, 0, "AirJump"); t.AddCondition(AnimatorConditionMode.IfNot, 0, "AirRun");
        t.hasExitTime = false; t.duration = 0.06f; t.offset = 0.18f; t.canTransitionToSelf = true;
        t = sm.AddAnyStateTransition(run); t.AddCondition(AnimatorConditionMode.If, 0, "AirJump"); t.AddCondition(AnimatorConditionMode.If, 0, "AirRun");
        t.hasExitTime = false; t.duration = 0.06f; t.offset = 0.22f; t.canTransitionToSelf = true;

        // walked off something
        t = none.AddTransition(fall); t.AddCondition(AnimatorConditionMode.If, 0, "AirFalling"); t.hasExitTime = false; t.duration = 0.15f;

        foreach (AnimatorState up in new[] { start, run })
        {
            t = up.AddTransition(fall); t.AddCondition(AnimatorConditionMode.Less, -2f, "AirVSpeed"); t.hasExitTime = false; t.duration = 0.2f;
            t = up.AddTransition(fall); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.15f;
        }

        // landings, from anywhere in the air
        foreach (AnimatorState from in new[] { start, run, fall })
        {
            AnimatorState[] dest = { soft, hard, roll };
            for (int k = 0; k < 3; k++)
            {
                if (dest[k] == null) continue;
                t = from.AddTransition(dest[k]);
                t.AddCondition(AnimatorConditionMode.If, 0, "AirLanded");
                t.AddCondition(AnimatorConditionMode.Equals, k, "AirLand");
                t.hasExitTime = false; t.duration = 0.05f;
            }
            // no landing clips / missed it: back to normal once on the ground
            t = from.AddTransition(none); t.AddCondition(AnimatorConditionMode.IfNot, 0, "AirFalling");
            t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.1f;
        }
        foreach (AnimatorState land in new[] { soft, hard, roll })
        {
            if (land == null) continue;
            if (land.transitions.Length > 0) continue;          // shared fallback state already wired
            t = land.AddTransition(none); t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.12f;
            t = land.AddTransition(fall); t.AddCondition(AnimatorConditionMode.If, 0, "AirFalling"); t.hasExitTime = false; t.duration = 0.1f;
        }
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, AnimationClip clip)
    {
        AnimatorState s = sm.AddState(name);
        s.motion = clip;
        s.writeDefaultValues = false;
        return s;
    }

    static void Param(AnimatorController ac, string name, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter p in ac.parameters) if (p.name == name) return;
        ac.AddParameter(name, type);
    }

    /// Legs, hips, spine and head move with the jump; arms (and the rifle) stay as they are.
    static AvatarMask Mask()
    {
        AvatarMask m = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (m == null) { m = new AvatarMask(); AssetDatabase.CreateAsset(m, MaskPath); }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) m.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
        foreach (AvatarMaskBodyPart p in new[] { AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers,
                                                 AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK })
            m.SetHumanoidBodyPartActive(p, false);
        EditorUtility.SetDirty(m);
        return m;
    }

    static AnimationClip Setup(string path, string key)
    {
        ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) return null;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
        ModelImporterClipAnimation[] defs = mi.defaultClipAnimations;
        if (defs == null || defs.Length == 0) return null;
        ModelImporterClipAnimation c = defs[0];
        c.name = key.Replace("Anim_", "");
        c.loopTime = key == "Anim_Fall";
        c.lockRootRotation = true; c.keepOriginalOrientation = true;
        c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = true;    // forward travel is the physics' job
        bool onGround = key == "Anim_LandSoft" || key == "Anim_LandHard" || key == "Anim_Roll";
        // take-off / fall: the up-and-down comes from the physics, so the clip's own rise is dropped;
        // landings: keep the crouch, measured from the feet so they stay on the ground
        c.lockRootHeightY = onGround; c.keepOriginalPositionY = false; c.heightFromFeet = onGround;
        mi.clipAnimations = new[] { c };
        mi.SaveAndReimport();
        return FirstClip(path);
    }

    static AnimationClip FirstClip(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            AnimationClip clip = o as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview__")) return clip;
        }
        return null;
    }

    /// Loose file-name match: Anim_Fall.fbx, anim_fall.FBX, Anim_Fall.fbx.fbx, Anim_Fall 1.fbx …
    static string FindAnim(string key)
    {
        if (!Directory.Exists(AnimDir)) return null;
        foreach (string f in Directory.GetFiles(AnimDir))
        {
            if (f.EndsWith(".meta")) continue;
            string n = Path.GetFileName(f);
            while (n.ToLowerInvariant().EndsWith(".fbx")) n = n.Substring(0, n.Length - 4);
            n = n.Trim();
            if (n.EndsWith(" 1") || n.EndsWith("(1)")) n = n.Substring(0, n.LastIndexOf(n.EndsWith(" 1") ? ' ' : '(')).Trim();
            if (string.Equals(n, key, System.StringComparison.OrdinalIgnoreCase)) return f.Replace('\\', '/');
        }
        return null;
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }
}
