// G2: the new deaths (Lundun > 36). Run it once after putting the two Mixamo clips in Assets/People/Animations:
//     Anim_DeathBlownBack.fbx    Mixamo "Shot And Blown Back"    heavy guns, shot from the front
//     Anim_DeathForward.fbx      Mixamo "Falling Forward Death"  shot from behind
// It sets each clip's import (Humanoid, no loop, root baked into the pose like the old death) and ADDS two states
// to the people's animator (DeathBlown, DeathForward) with their triggers (DieBlown, DieForward), wired from Any
// State the same way as the old "Die". It never removes or changes anything already in the animator: the old Death
// state, its clip and its "Die" trigger stay exactly as they are. Safe to run again (it only fills in what's
// missing). Lundun > 36b takes out only what 36 added.
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class LundunDeathBuilder
{
    const string Controller = "Assets/People/PeopleAnimator.controller";
    static readonly string[,] Deaths =
    {
        // file, state, trigger
        { "Assets/People/Animations/Anim_DeathBlownBack.fbx", "DeathBlown", "DieBlown" },
        { "Assets/People/Animations/Anim_DeathForward.fbx", "DeathForward", "DieForward" },
    };

    [MenuItem("Lundun/36  Death Reactions (G2)", false, 42)]
    static void Build()
    {
        AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (ac == null) { EditorUtility.DisplayDialog("Death reactions", "Couldn't find " + Controller, "OK"); return; }
        AnimatorStateMachine sm = ac.layers[0].stateMachine;
        AnimatorStateTransition old = sm.anyStateTransitions.FirstOrDefault(t => t.conditions.Any(c => c.parameter == "Die"));
        string report = "";
        for (int i = 0; i < Deaths.GetLength(0); i++)
        {
            string file = Deaths[i, 0], stateName = Deaths[i, 1], trigger = Deaths[i, 2];
            AnimationClip clip = Import(file);
            if (clip == null) { report += "\n• " + System.IO.Path.GetFileName(file) + ": not found, skipped (that death stays the old one)"; continue; }
            if (!ac.parameters.Any(p => p.name == trigger)) ac.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
            AnimatorState st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
            if (st == null) st = sm.AddState(stateName, new Vector3(50f, 120f + i * 60f, 0f));
            st.motion = clip;
            st.writeDefaultValues = true;
            if (!sm.anyStateTransitions.Any(t => t.destinationState == st))
            {
                AnimatorStateTransition t = sm.AddAnyStateTransition(st);
                t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                t.hasExitTime = false;
                t.duration = old != null ? old.duration : 0.1f;
                t.hasFixedDuration = old == null || old.hasFixedDuration;
                t.offset = 0f;
                t.canTransitionToSelf = false;
                t.interruptionSource = TransitionInterruptionSource.None;
            }
            report += "\n• " + stateName + " (" + trigger + "): " + clip.name;
        }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log("[Lundun] Death reactions:" + report);
        EditorUtility.DisplayDialog("Death reactions (G2)", "Done:" + report + "\n\nThe old death (Die) is unchanged.", "OK");
    }

    [MenuItem("Lundun/36b  Remove Death Reactions (G2)", false, 43)]
    static void Remove()
    {
        AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        if (ac == null) return;
        AnimatorStateMachine sm = ac.layers[0].stateMachine;
        for (int i = 0; i < Deaths.GetLength(0); i++)
        {
            string stateName = Deaths[i, 1], trigger = Deaths[i, 2];
            AnimatorState st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
            if (st != null)
            {
                foreach (AnimatorStateTransition t in sm.anyStateTransitions.Where(t => t.destinationState == st).ToArray()) sm.RemoveAnyStateTransition(t);
                sm.RemoveState(st);
            }
            int idx = System.Array.FindIndex(ac.parameters, p => p.name == trigger);
            if (idx >= 0) ac.RemoveParameter(idx);
        }
        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        Debug.Log("[Lundun] Death reactions removed: everyone uses the old death again.");
    }

    /// The clip's import set like the old death's: Humanoid, no loop, root rotation and position baked into the pose
    /// (so the fall moves the body, not the character's position), based on the original root.
    static AnimationClip Import(string file)
    {
        ModelImporter mi = AssetImporter.GetAtPath(file) as ModelImporter;
        if (mi == null) return null;
        bool changed = false;
        if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; changed = true; }
        if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
        if (!mi.importAnimation) { mi.importAnimation = true; changed = true; }
        ModelImporterClipAnimation[] clips = mi.clipAnimations;
        if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
        foreach (ModelImporterClipAnimation c in clips)
        {
            c.loopTime = false;
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = clips;
        changed = true;
        if (changed) mi.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }
}
