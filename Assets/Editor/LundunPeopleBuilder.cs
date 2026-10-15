// Lundun → 12  Build People
// Takes Mixamo characters from Assets/People/Characters and animations from Assets/People/Animations and:
//   1. sets every file to a Humanoid rig (so any animation plays on any character), loops walk/run/idle, stops drift
//   2. builds one shared animator: walk/run, punch, pistol aim (+ backing off), flinch, punched, shoved, death
//   3. makes a pedestrian prefab per character (copying the brain/health/navigation from your NPC prefab)
//   4. gives the population manager the new people + the pistol model, and gives the police officers the new animations
//   5. adds a "LundunReactions" layer to James's animator so he reels when punched (his other animations untouched)
// Textures are capped at 1024 px for the phone. File names are matched loosely (Anim_Punch_reaction.fbx.fbx is fine).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunPeopleBuilder
{
    const string CharDir = "Assets/People/Characters";
    const string AnimDir = "Assets/People/Animations";
    const string PrefabDir = "Assets/People/Prefabs";
    const string ControllerPath = "Assets/People/PeopleAnimator.controller";
    const string TemplatePath = "Assets/Prefabs/NPC.prefab";
    const string GunPath = "Assets/Weapons/blaster-f.fbx";

    // file name (without .fbx) → used for; fallback = the clips your current NPC already has
    static readonly string[,] Clips = {
        { "Anim_Idle", "Assets/NPC/Ch37_nonPBR@Idle.fbx" },
        { "Anim_Walk", "Assets/NPC/Ch37_nonPBR@Walking.fbx" },
        { "Anim_Run", "Assets/NPC/Ch37_nonPBR@Running.fbx" },
        { "Anim_Death", "Assets/NPC/Ch37_nonPBR@Dying Backwards.fbx" },
        { "Anim_Punch", "" }, { "Anim_PistolIdle", "" }, { "Anim_PistolWalk", "" }, { "Anim_HitReact", "" },
        { "Anim_Punch_reaction", "" }, { "Anim_Shove_reaction", "" } };
    static readonly HashSet<string> Looping = new HashSet<string> { "Anim_Idle", "Anim_Walk", "Anim_Run", "Anim_PistolIdle", "Anim_PistolWalk" };
    // reactions are allowed to move and turn the body (the scripts let the clip carry the person while they stumble)
    static readonly HashSet<string> Moving = new HashSet<string> { "Anim_HitReact", "Anim_Punch_reaction", "Anim_Shove_reaction" };
    const int MaxTexture = 1024;

    [MenuItem("Lundun/12  Build People", false, 12)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
        if (template == null || template.GetComponent<NPCBrain>() == null) { Dialog("Can't find your NPC prefab at " + TemplatePath + "."); return; }
        EnsureFolder(CharDir); EnsureFolder(AnimDir); EnsureFolder(PrefabDir);

        List<string> chars = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharDir })) chars.Add(AssetDatabase.GUIDToAssetPath(guid));
        if (chars.Count == 0) { Dialog("No characters in " + CharDir + ".\n\nDownload them from mixamo.com (FBX for Unity, T-pose) and drop them in that folder — see the steps Claude gave you."); return; }

        try
        {
            // 1. import settings
            EditorUtility.DisplayProgressBar("Lundun", "Setting up characters…", 0.1f);
            foreach (string p in chars) SetupCharacter(p);
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            List<string> missing = new List<string>();
            for (int i = 0; i < Clips.GetLength(0); i++)
            {
                string key = Clips[i, 0];
                string path = FindAnim(key);
                if (path == null) path = Clips[i, 1];
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { missing.Add(key); continue; }
                EditorUtility.DisplayProgressBar("Lundun", "Setting up " + key + "…", 0.2f + 0.3f * i / Clips.GetLength(0));
                AnimationClip c = SetupAnimation(path, key, Looping.Contains(key), Moving.Contains(key));
                if (c != null) clips[key] = c; else missing.Add(key);
            }
            if (!clips.ContainsKey("Anim_Walk")) { Dialog("No walk animation found (Anim_Walk.fbx). Can't build people without it."); return; }

            // 2. animator
            EditorUtility.DisplayProgressBar("Lundun", "Building the animator…", 0.55f);
            AnimatorController ctrl = BuildController(clips);

            // 3. prefabs
            List<GameObject> made = new List<GameObject>();
            for (int i = 0; i < chars.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Lundun", "Making " + Path.GetFileNameWithoutExtension(chars[i]) + "…", 0.6f + 0.35f * i / chars.Count);
                GameObject prefab = BuildPerson(chars[i], template, ctrl);
                if (prefab != null) made.Add(prefab);
            }

            // 4. hand out
            int officers = 0;
            foreach (string op in new[] { "Assets/Prefabs/PoliceOfficer.prefab", "Assets/Prefabs/PoliceOfficerMale.prefab" })
            {
                GameObject root = File.Exists(op) ? PrefabUtility.LoadPrefabContents(op) : null;
                if (root == null) continue;
                Animator a = root.GetComponentInChildren<Animator>();
                if (a != null) { a.runtimeAnimatorController = ctrl; PrefabUtility.SaveAsPrefabAsset(root, op); officers++; }
                PrefabUtility.UnloadPrefabContents(root);
            }
            LundunPopulation pop = null;
            foreach (GameObject g in scene.GetRootGameObjects()) if (g.name == "LundunPopulation") pop = g.GetComponent<LundunPopulation>();
            if (pop != null)
            {
                SerializedObject so = new SerializedObject(pop);
                SerializedProperty arr = so.FindProperty("pedestrianPrefabs");
                if (made.Count == 0)
                {
                    // nothing usable: keep the city alive with your original, working NPC
                    Debug.LogWarning("[Lundun] None of the Mixamo characters came in as a valid Humanoid — using your original NPC prefab for now.");
                    made.Add(template);
                }
                arr.arraySize = made.Count;
                for (int i = 0; i < made.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = made[i];
                so.FindProperty("gunModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(GunPath);
                SerializedProperty pc = so.FindProperty("peopleController");
                if (pc != null) pc.objectReferenceValue = ctrl;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
            }
            bool james = AddPlayerReactions(scene, clips.ContainsKey("Anim_Punch_reaction") ? clips["Anim_Punch_reaction"] : null,
                                            clips.ContainsKey("Anim_Punch") ? clips["Anim_Punch"] : null);
            AssetDatabase.SaveAssets();
            LundunPeopleCheck.Check();          // print what Unity now sees, straight away
            Debug.Log("[Lundun] People built: " + made.Count + " characters, " + officers + " police officer prefab(s) upgraded, James's punch reaction " + (james ? "added." : "NOT added (see warning).") +
                      (missing.Count > 0 ? "  Missing animations (those moves are skipped): " + string.Join(", ", missing.ToArray()) : "  All animations found.") +
                      (pop == null ? "  (No LundunPopulation in the scene — run Lundun → 8 first.)" : "  Press Play."));
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // ------------------------------------------------------------------ import settings
    static void SetupCharacter(string path)
    {
        ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) return;
        // Humanoid with the bones actually mapped (a script has to do this itself — see LundunSkeletons)
        LundunSkeletons.ConfigureHumanoid(mi, path);
        mi.importAnimation = false;
        mi.SaveAndReimport();
        if (!LundunSkeletons.AvatarOk(path)) Debug.LogWarning("[Lundun] " + path + " still isn't a valid Humanoid — check its Rig tab (Configure…).");
        // Mixamo hides its textures inside the FBX: pull them out so the materials show
        string texDir = Path.GetDirectoryName(path).Replace('\\', '/') + "/Textures";
        EnsureFolder(texDir);
        if (mi.ExtractTextures(texDir)) AssetDatabase.Refresh();
        // phone-sized textures (Mixamo ships 2048–4096 px: ~50 MB per character otherwise)
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
        {
            TextureImporter ti = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
            if (ti == null || ti.maxTextureSize <= MaxTexture) continue;
            ti.maxTextureSize = MaxTexture;
            ti.SaveAndReimport();
        }
    }

    /// Anim_Punch_reaction → finds Anim_Punch_reaction.fbx, anim_punch_reaction.FBX, Anim_Punch_reaction.fbx.fbx, Anim_Punch_reaction 1.fbx …
    static string FindAnim(string key)
    {
        if (!Directory.Exists(AnimDir)) return null;
        string best = null;
        foreach (string f in Directory.GetFiles(AnimDir))
        {
            if (f.EndsWith(".meta")) continue;
            string n = Path.GetFileName(f);
            while (n.ToLowerInvariant().EndsWith(".fbx")) n = n.Substring(0, n.Length - 4);
            n = n.Trim();
            if (n.EndsWith(" 1") || n.EndsWith("(1)")) n = n.Substring(0, n.LastIndexOf(n.EndsWith(" 1") ? ' ' : '(')).Trim();
            if (string.Equals(n, key, System.StringComparison.OrdinalIgnoreCase)) { best = f.Replace('\\', '/'); break; }
        }
        return best;
    }

    static AnimationClip SetupAnimation(string path, string key, bool loop, bool moving)
    {
        // your existing NPC animations (fallbacks) are used as they are — never re-imported, so Prototype keeps working
        if (!path.StartsWith(AnimDir)) return FirstClip(path);
        ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) return null;
        // its own Humanoid avatar, bones mapped (Mixamo "Without Skin" files are in T-pose)
        if (!LundunSkeletons.ConfigureHumanoid(mi, path)) return null;
        mi.importAnimation = true;
        ModelImporterClipAnimation[] defs = mi.defaultClipAnimations;
        if (defs == null || defs.Length == 0) return null;
        ModelImporterClipAnimation c = defs[0];
        c.name = key.Replace("Anim_", "");
        c.loopTime = loop;
        c.lockRootHeightY = true; c.keepOriginalPositionY = true;             // no floating
        // XZ travel is taken out of the pose into "root motion": ignored normally (the NavMesh moves them, so walks
        // stay on the spot), used only while someone is stumbling from a shove or a hit
        c.lockRootPositionXZ = false; c.keepOriginalPositionXZ = true;
        c.lockRootRotation = !moving; c.keepOriginalOrientation = true;       // only reactions may turn the body (shove spin)
        mi.clipAnimations = new[] { c };
        mi.SaveAndReimport();
        return FirstClip(path);
    }

    static AnimationClip FirstClip(string path)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            AnimationClip clip = o as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview__")) return clip;
        }
        return null;
    }

    // ------------------------------------------------------------------ animator
    static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        // Rebuilt in place (never deleted), so every prefab that points at it keeps pointing at it.
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        else
        {
            foreach (AnimatorControllerParameter p in ctrl.parameters) ctrl.RemoveParameter(p);
            while (ctrl.layers.Length > 1) ctrl.RemoveLayer(ctrl.layers.Length - 1);
            AnimatorStateMachine old = ctrl.layers[0].stateMachine;
            foreach (AnimatorStateTransition t in old.anyStateTransitions) old.RemoveAnyStateTransition(t);
            foreach (ChildAnimatorState cs in old.states) old.RemoveState(cs.state);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)) if (o is BlendTree) Object.DestroyImmediate(o, true);
        }
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("AimMove", AnimatorControllerParameterType.Float);   // while aiming: + forwards, − backing off
        ctrl.AddParameter("Die", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Punch", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Punched", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Shoved", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Aim", AnimatorControllerParameterType.Bool);
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

        // walk / run blend by speed (m/s)
        BlendTree tree;
        AnimatorState loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        if (clips.ContainsKey("Anim_Idle")) tree.AddChild(clips["Anim_Idle"], 0f);
        tree.AddChild(clips["Anim_Walk"], 1.5f);
        if (clips.ContainsKey("Anim_Run")) tree.AddChild(clips["Anim_Run"], 5.5f);
        sm.defaultState = loco;

        // pistol stance while Aim is on
        AnimatorState aim = null;
        if (clips.ContainsKey("Anim_PistolIdle"))
        {
            // gun up: backing off (pistol walk is a backwards walk), standing, or walking/running at you
            BlendTree aimTree;
            aim = ctrl.CreateBlendTreeInController("AimPistol", out aimTree, 0);
            aimTree.blendParameter = "AimMove";
            aimTree.useAutomaticThresholds = false;
            if (clips.ContainsKey("Anim_PistolWalk")) aimTree.AddChild(clips["Anim_PistolWalk"], -1.3f);
            aimTree.AddChild(clips["Anim_PistolIdle"], 0f);
            aimTree.AddChild(clips["Anim_Walk"], 1.5f);
            if (clips.ContainsKey("Anim_Run")) aimTree.AddChild(clips["Anim_Run"], 5.5f);
            AnimatorStateTransition t1 = loco.AddTransition(aim); t1.AddCondition(AnimatorConditionMode.If, 0, "Aim"); t1.hasExitTime = false; t1.duration = 0.15f;
            AnimatorStateTransition t2 = aim.AddTransition(loco); t2.AddCondition(AnimatorConditionMode.IfNot, 0, "Aim"); t2.hasExitTime = false; t2.duration = 0.2f;
        }

        OneShot(sm, loco, aim, clips, "Anim_Punch", "Punch", 0.85f);
        OneShot(sm, loco, aim, clips, "Anim_HitReact", "Hit", 0.5f);              // short flinch, then they run
        OneShot(sm, loco, aim, clips, "Anim_Punch_reaction", "Punched", 0.85f);
        OneShot(sm, loco, aim, clips, "Anim_Shove_reaction", "Shoved", 0.94f);    // the full stumble and spin

        if (clips.ContainsKey("Anim_Death"))
        {
            AnimatorState death = sm.AddState("Death"); death.motion = clips["Anim_Death"];
            AnimatorStateTransition t = sm.AddAnyStateTransition(death);
            t.AddCondition(AnimatorConditionMode.If, 0, "Die"); t.hasExitTime = false; t.duration = 0.1f; t.canTransitionToSelf = false;
        }
        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }

    /// A move that plays once (from anywhere), then goes back to aiming (if Aim) or walking.
    static void OneShot(AnimatorStateMachine sm, AnimatorState loco, AnimatorState aim, Dictionary<string, AnimationClip> clips, string key, string trigger, float exitAt)
    {
        if (!clips.ContainsKey(key)) return;
        AnimatorState s = sm.AddState(trigger); s.motion = clips[key];
        AnimatorStateTransition into = sm.AddAnyStateTransition(s);
        into.AddCondition(AnimatorConditionMode.If, 0, trigger); into.hasExitTime = false; into.duration = 0.08f;
        into.canTransitionToSelf = false;
        if (aim != null)
        {
            AnimatorStateTransition back = s.AddTransition(aim); back.AddCondition(AnimatorConditionMode.If, 0, "Aim");
            back.hasExitTime = true; back.exitTime = exitAt; back.duration = 0.15f;
        }
        AnimatorStateTransition home = s.AddTransition(loco);
        home.hasExitTime = true; home.exitTime = exitAt; home.duration = 0.15f;
    }

    // ------------------------------------------------------------------ James
    /// Adds (or rebuilds) one extra layer on James's animator: idle "None", "Punched" (reels) and "Punch" (throws one).
    /// Weight 0 until LundunPlayerReact switches it on for a moment. Nothing else in his animator is touched.
    static bool AddPlayerReactions(Scene scene, AnimationClip punched, AnimationClip punch)
    {
        if (punched == null) { Debug.LogWarning("[Lundun] No Anim_Punch_reaction — James won't reel when punched."); return false; }
        Animator a = null;
        foreach (GameObject g in scene.GetRootGameObjects())
        {
            PlayerController pc = g.GetComponentInChildren<PlayerController>(true);
            if (pc != null) { a = pc.GetComponentInChildren<Animator>(true); break; }
        }
        AnimatorController ac = a != null ? a.runtimeAnimatorController as AnimatorController : null;
        if (ac == null) { Debug.LogWarning("[Lundun] Couldn't find James's animator controller — his punch reaction was skipped."); return false; }
        if (a.avatar == null || !a.avatar.isHuman) { Debug.LogWarning("[Lundun] James's model isn't Humanoid — his punch reaction was skipped."); return false; }

        string layerName = LundunPlayerReact.LayerName;
        for (int i = ac.layers.Length - 1; i >= 0; i--) if (ac.layers[i].name == layerName) ac.RemoveLayer(i);
        foreach (string trig in new[] { "Punched", "Punch" })
        {
            bool hasParam = false;
            foreach (AnimatorControllerParameter p in ac.parameters) if (p.name == trig) hasParam = true;
            if (!hasParam) ac.AddParameter(trig, AnimatorControllerParameterType.Trigger);
        }

        ac.AddLayer(layerName);
        AnimatorControllerLayer[] layers = ac.layers;
        AnimatorControllerLayer layer = layers[layers.Length - 1];
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        AnimatorStateMachine sm = layer.stateMachine;
        AnimatorState none = sm.AddState("None"); none.writeDefaultValues = false;
        sm.defaultState = none;
        AnimatorState hit = sm.AddState("Punched"); hit.motion = punched; hit.writeDefaultValues = false;
        AnimatorStateTransition t = sm.AddAnyStateTransition(hit);
        t.AddCondition(AnimatorConditionMode.If, 0, "Punched"); t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = true;
        AnimatorStateTransition back = hit.AddTransition(none);
        back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.1f;
        if (punch != null)
        {
            AnimatorState jab = sm.AddState("Punch"); jab.motion = punch; jab.writeDefaultValues = false;
            jab.speed = 1.4f;                                   // snappier for the player
            AnimatorStateTransition tj = sm.AddAnyStateTransition(jab);
            tj.AddCondition(AnimatorConditionMode.If, 0, "Punch"); tj.hasExitTime = false; tj.duration = 0.05f; tj.canTransitionToSelf = true;
            AnimatorStateTransition bj = jab.AddTransition(none);
            bj.hasExitTime = true; bj.exitTime = 0.85f; bj.duration = 0.1f;
        }
        layers[layers.Length - 1] = layer;
        ac.layers = layers;
        EditorUtility.SetDirty(ac);
        return true;
    }

    // ------------------------------------------------------------------ people
    static GameObject BuildPerson(string modelPath, GameObject template, AnimatorController ctrl)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) return null;
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath)) if (o is Avatar) avatar = (Avatar)o;
        if (avatar == null || !avatar.isHuman || !avatar.isValid) { Debug.LogWarning("[Lundun] " + modelPath + " didn't come in as a Humanoid — skipped. Re-download it as FBX for Unity, T-pose, with skin."); return null; }

        GameObject p = (GameObject)PrefabUtility.InstantiatePrefab(model);
        // a standalone prefab (not a linked copy of the model file), so re-importing the model can't knock the
        // animator controller or skeleton off it
        PrefabUtility.UnpackPrefabInstance(p, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        string name = Path.GetFileNameWithoutExtension(modelPath);
        p.name = name.StartsWith("Ped_") ? name : "Ped_" + name;
        foreach (Transform t in p.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = template.layer;

        Animator a = p.GetComponent<Animator>();
        if (a == null) a = p.AddComponent<Animator>();
        a.runtimeAnimatorController = ctrl;
        a.avatar = avatar;
        a.applyRootMotion = false;
        a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        // the same brain, health, collider and navigation as your NPC
        Copy<NavMeshAgent>(template, p);
        Copy<CapsuleCollider>(template, p);
        Copy<Health>(template, p);
        Copy<NPCBrain>(template, p);

        string path = PrefabDir + "/" + p.name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(p, path);
        Object.DestroyImmediate(p);
        return prefab;
    }

    static void Copy<T>(GameObject from, GameObject to) where T : Component
    {
        T src = from.GetComponent<T>();
        if (src == null) return;
        T dst = to.GetComponent<T>();
        if (dst == null) dst = to.AddComponent<T>();
        EditorUtility.CopySerialized(src, dst);
    }

    // ------------------------------------------------------------------ helpers
    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
