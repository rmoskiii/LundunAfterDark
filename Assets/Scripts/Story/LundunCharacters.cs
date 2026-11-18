using System.Collections.Generic;
using UnityEngine;

/// Phase 2: who you play as in Story mode. One catalogue of playable characters (just James for now), the one this story
/// was started with, and the rules every character has to meet before it can be offered.
///   catalogue   LundunCharacters.All: James first. A new character is one more entry here, and only once it passes
///               Lundun → 34 Check Characters and has been approved: Humanoid rig on the shared animations, its own
///               first-person arms, a head bone for the camera, and hands that fit the guns (checked in Play).
///   the choice  picked on the CHOOSE YOUR CHARACTER screen after NEW STORY, saved in the story's own key/value list as
///               "story.character" (story.json, same save version). CONTINUE loads it; a save from before Phase 2 has
///               none and plays as James. Anything unknown, switched off or broken also falls back to James.
///   Free Roam   Phase 3: its own choice (the select screen every time you pick FREE ROAM), kept in PlayerPrefs
///               (FreeRoamKey), never the story file: the two can never overwrite each other.
/// Character (who) and outfit (what they wear: LundunOutfits) are kept apart, each saved on its own.
public static class LundunCharacters
{
    /// The story save's key for the chosen character.
    public const string StoryKey = "story.character";
    /// Free Roam's chosen character (PlayerPrefs, never the story file).
    public const string FreeRoamKey = "Lundun.FreeRoam.Character";
    public const string DefaultId = "james";

    /// How many greyed "coming soon" figures the select screen shows next to the real characters.
    public const int ComingSoon = 3;

    public class Def
    {
        public string id;
        public string name;            // on the select screen, in capitals
        public string tagline;         // one line under the name
        /// Where the model comes from: empty = the James already in the city (the scene's player); otherwise a
        /// Resources path to a humanoid model prefab.
        public string modelResource = "";
        /// Resources paths to this model's own first-person arms (made by Lundun → 33 for that model). James's are
        /// already on him (LundunViewModel), so his are empty.
        public string[] firstPersonArms = new string[0];
        /// Small corrections so guns sit right in this model's hands (zero for James, whose rig the guns were fitted to).
        public Vector3 rightHandNudge, leftHandNudge;
        /// Can be picked. Off = listed but not offered (e.g. still being checked).
        public bool selectable = true;
        /// Set once it has been looked at in Play with every gun (hands, first person, camera) and approved.
        public bool weaponsFitted;

        public bool IsSceneJames { get { return string.IsNullOrEmpty(modelResource); } }
    }

    static List<Def> all;
    /// Every character in the game, in the order the select screen shows them.
    public static IList<Def> All
    {
        get
        {
            if (all == null)
                all = new List<Def>
                {
                    new Def { id = DefaultId, name = "JAMES", tagline = "South of the river, born and raised.", weaponsFitted = true },
                    // more characters go here, one at a time, each only after it passes the checks and is approved
                };
            return all;
        }
    }

    public static Def Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (Def d in All) if (d.id == id) return d;
        return null;
    }

    public static Def Default { get { return Find(DefaultId); } }

    /// The characters that can be picked right now.
    public static List<Def> Selectable
    {
        get
        {
            List<Def> list = new List<Def>();
            foreach (Def d in All) if (d.selectable && (d.IsSceneJames || CanLoad(d))) list.Add(d);
            if (list.Count == 0) list.Add(Default);
            return list;
        }
    }

    /// Its model is there and is a Humanoid (the shared animations need that). James in the scene always counts.
    public static bool CanLoad(Def d)
    {
        if (d == null) return false;
        if (d.IsSceneJames) return true;
        GameObject prefab = Resources.Load<GameObject>(d.modelResource);
        if (prefab == null) return false;
        Animator a = prefab.GetComponentInChildren<Animator>(true);
        return a != null && a.avatar != null && a.avatar.isHuman && a.avatar.isValid;
    }

    // ------------------------------------------------------------------ the choice, kept per mode
    // Story: the story's own key/value list (story.json), wiped with the story.
    // Free Roam (Phase 3): PlayerPrefs under its own keys. Neither mode ever reads or writes the other's.
    public const string StoryOutfitKey = "story.outfit";
    public const string FreeRoamOutfitKey = "Lundun.FreeRoam.Outfit";

    static string Read(LundunGame.Mode mode, string storyKey, string freeKey)
    {
        string v;
        if (mode == LundunGame.Mode.Story) return LundunStory.TryGet(storyKey, out v) ? v : null;
        v = PlayerPrefs.GetString(freeKey, "");
        return v.Length > 0 ? v : null;
    }

    static void Write(LundunGame.Mode mode, string storyKey, string freeKey, string value)
    {
        if (mode == LundunGame.Mode.Story) LundunStory.Set(storyKey, value);
        else { PlayerPrefs.SetString(freeKey, value); PlayerPrefs.Save(); }
    }

    /// Picked on the select screen for a new story, written into it when it starts (LundunStory.Reset).
    public static string Pending { get; set; }
    public static string PendingOutfit { get; set; }

    /// LundunStory.Reset: a brand new story takes the character and outfit just picked (James in Midnight if none were).
    public static void SeedNewStory()
    {
        Def d = Find(Pending);
        LundunStory.Set(StoryKey, d != null && d.selectable ? d.id : DefaultId);
        LundunOutfits.Outfit o = LundunOutfits.Find(PendingOutfit);
        LundunStory.Set(StoryOutfitKey, o != null ? o.id : LundunOutfits.DefaultId);
        Pending = null; PendingOutfit = null;
    }

    /// The character saved for a mode (James if none, or if it isn't available).
    public static Def SavedCharacter(LundunGame.Mode mode)
    {
        string id = Read(mode, StoryKey, FreeRoamKey);
        if (id == null) return Default;                                   // a save from before Phase 2 / Free Roam's first time
        Def d = Find(id);
        if (d == null || !d.selectable || !CanLoad(d))
        {
            Debug.LogWarning("[Lundun] " + mode + " character '" + id + "' isn't available: playing as James.");
            return Default;
        }
        return d;
    }

    /// The outfit saved for a mode (Midnight if none, or if it's unknown).
    public static LundunOutfits.Outfit SavedOutfit(LundunGame.Mode mode)
    {
        LundunOutfits.Outfit o = LundunOutfits.Find(Read(mode, StoryOutfitKey, FreeRoamOutfitKey));
        return o ?? LundunOutfits.Default;
    }

    /// Free Roam's select screen (PLAY): keep its character and outfit.
    public static void SaveFreeRoam(string characterId, string outfitId)
    {
        Write(LundunGame.Mode.FreeRoam, StoryKey, FreeRoamKey, Find(characterId) != null ? characterId : DefaultId);
        Write(LundunGame.Mode.FreeRoam, StoryOutfitKey, FreeRoamOutfitKey, LundunOutfits.Find(outfitId) != null ? outfitId : LundunOutfits.DefaultId);
    }

    /// The wardrobe: change the outfit for the mode being played, and wear it now.
    public static void ChangeOutfit(string outfitId)
    {
        LundunOutfits.Outfit o = LundunOutfits.Find(outfitId) ?? LundunOutfits.Default;
        Write(LundunGame.Current, StoryOutfitKey, FreeRoamOutfitKey, o.id);
        if (LundunGame.IsStory) LundunStory.Write();
        Wear(o.id);
    }

    /// The character for the mode being played now.
    public static Def Current { get { return SavedCharacter(LundunGame.Current); } }

    /// LundunStoryUI, when a mode starts (and Free Roam's PLAY when it doesn't reload): the mode's character and outfit
    /// on the player. James is already the player in the city, so his character needs no change. Another character's
    /// model goes in here when the first one is approved; until then anything else plays as James.
    public static void Apply()
    {
        Def d = Current;
        if (!d.IsSceneJames)
        {
            Debug.LogWarning("[Lundun] Character '" + d.id + "': swapping models arrives with the first approved character. Playing as James.");
            d = Default;
        }
        LundunOutfits.Outfit o = SavedOutfit(LundunGame.Current);
        string worn = Wear(o.id);
        Debug.Log("[Lundun] " + LundunGame.Current + " character: " + d.name + ", outfit: " + worn);
    }

    /// Put an outfit on the player now. Returns the one actually worn (Midnight if his outfits aren't built yet).
    static string Wear(string outfitId)
    {
        LundunOutfitSet set = LundunOutfitSet.OnPlayer;
        if (set == null)
        {
            if (outfitId != LundunOutfits.DefaultId)
                Debug.LogWarning("[Lundun] Outfits aren't built yet: Lundun → 35 Build Outfits, then Cmd+S. Wearing Midnight.");
            return LundunOutfits.DefaultId;
        }
        set.Wear(outfitId);
        return set.Wearing;
    }
}
