// Lundun → 34  Check Characters (Phase 2)
//   Goes through every character in LundunCharacters.All and checks what can be checked in the Editor, before a
//   character is ever offered on the select screen:
//     model        found (James: the player in the open scene; others: their Resources prefab)
//     rig          Humanoid with a valid avatar, so the shared animations (PlayerAnimator) drive it
//     head         a head bone (the camera follows it, and first person sits behind it)
//     gun bones    spine, chest, both arms and hands, index and middle fingers (the gun rig holds the guns with these)
//     arms         its own first-person arms (Lundun → 33 for that model)
//     guns fitted  marked as looked at in Play with every gun (can't be checked here: it needs eyes)
//     outfits      (Phase 3) how many of the outfits it has built (a note, not a pass/fail)
//   Writes a report to the Console and shows the summary. Changes nothing.
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class LundunCharacterCheck
{
    [MenuItem("Lundun/34  Check Characters (Phase 2)", false, 40)]
    public static void Check()
    {
        StringBuilder all = new StringBuilder();
        int ok = 0, total = 0;
        foreach (LundunCharacters.Def d in LundunCharacters.All)
        {
            total++;
            List<string> fails = new List<string>(), notes = new List<string>();
            CheckOne(d, fails, notes);
            bool pass = fails.Count == 0;
            if (pass) ok++;
            all.AppendLine((pass ? "PASS  " : "FAIL  ") + d.name + " (" + d.id + ")" + (d.selectable ? "" : "  [switched off]"));
            foreach (string f in fails) all.AppendLine("   ✗ " + f);
            foreach (string n in notes) all.AppendLine("   · " + n);
        }
        string report = ok + " of " + total + " character(s) pass.\n\n" + all;
        Debug.Log("[Lundun] Phase 2 character check:\n" + report);
        EditorUtility.DisplayDialog("Lundun: Check Characters", report, "OK");
    }

    static void CheckOne(LundunCharacters.Def d, List<string> fails, List<string> notes)
    {
        GameObject temp = null;
        Animator anim = null;
        try
        {
            if (d.IsSceneJames)
            {
                PlayerController pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
                if (pc == null) { fails.Add("no player (PlayerController) in the open scene: open the Lundun scene"); return; }
                anim = pc.GetComponentInChildren<Animator>(true);
                LundunViewModel vm = pc.GetComponent<LundunViewModel>();
                if (vm == null || !vm.HasArms) fails.Add("no first-person arms: run Lundun → 33, then Cmd+S");
                else notes.Add("first-person arms: on the player (LundunViewModel)");
                LundunOutfitSet outfits = pc.GetComponent<LundunOutfitSet>();
                int have = 0;
                if (outfits != null) foreach (LundunOutfits.Outfit o in LundunOutfits.All) if (outfits.Has(o.id)) have++;
                notes.Add(outfits == null ? "outfits: not built yet (Lundun → 35): Midnight only" : "outfits: " + have + " of " + LundunOutfits.All.Count);
            }
            else
            {
                GameObject prefab = Resources.Load<GameObject>(d.modelResource);
                if (prefab == null) { fails.Add("model not found at Resources/" + d.modelResource); return; }
                temp = (GameObject)Object.Instantiate(prefab);       // bones are only found on a live copy
                temp.hideFlags = HideFlags.HideAndDontSave;
                anim = temp.GetComponentInChildren<Animator>(true);
                if (d.firstPersonArms == null || d.firstPersonArms.Length == 0) fails.Add("no first-person arms listed (Lundun → 33 for this model)");
                else
                    foreach (string a in d.firstPersonArms)
                        if (Resources.Load<Mesh>(a) == null) fails.Add("first-person arms missing: Resources/" + a);
            }

            if (anim == null) { fails.Add("no Animator on the model"); return; }
            if (anim.avatar == null || !anim.avatar.isValid || !anim.avatar.isHuman) { fails.Add("not a valid Humanoid avatar: the shared animations can't drive it"); return; }
            notes.Add("rig: Humanoid (" + anim.avatar.name + ")");

            if (anim.GetBoneTransform(HumanBodyBones.Head) == null) fails.Add("no head bone: the camera can't follow it");
            else notes.Add("head bone: found");

            HumanBodyBones[] gun = {
                HumanBodyBones.Spine, HumanBodyBones.Chest,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand };
            List<string> missing = new List<string>();
            foreach (HumanBodyBones b in gun) if (anim.GetBoneTransform(b) == null) missing.Add(b.ToString());
            if (missing.Count > 0) fails.Add("gun rig bones missing: " + string.Join(", ", missing.ToArray()));
            else notes.Add("gun rig bones: all there");

            HumanBodyBones[] fingers = {
                HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal,
                HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal };
            int fingerCount = 0;
            foreach (HumanBodyBones b in fingers) if (anim.GetBoneTransform(b) != null) fingerCount++;
            if (fingerCount < fingers.Length) notes.Add("some finger bones missing: the grip on the guns will look stiff");

            if (!d.weaponsFitted) fails.Add("guns not yet checked in Play (hands, first person, camera): look, adjust the hand nudges, then mark weaponsFitted");
            else notes.Add("guns: checked in Play");
        }
        finally
        {
            if (temp != null) Object.DestroyImmediate(temp);
        }
    }
}
