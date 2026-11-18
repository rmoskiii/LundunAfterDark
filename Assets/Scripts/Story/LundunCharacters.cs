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
///   Free Roam   untouched: it never reads or writes the story's choice. If Free Roam gets its own later it will be a
///               separate PlayerPrefs key (FreeRoamKey), so the two can never overwrite each other.
/// Character (who) and outfit (what they wear, Phase 3) are kept apart: nothing here is about clothes.
public static class LundunCharacters
{
    /// The story save's key for the chosen character.
    public const string StoryKey = "story.character";
    /// Reserved for a future Free Roam choice (PlayerPrefs, never the story file). Not used yet.
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

    // ------------------------------------------------------------------ the story's choice
    /// Picked on the select screen, written into the new story when it starts (LundunStory.Reset).
    public static string Pending { get; set; }

    /// LundunStory.Reset: a brand new story takes the character just picked (James if none was).
    public static void SeedNewStory()
    {
        Def d = Find(Pending);
        string id = d != null && d.selectable ? d.id : DefaultId;
        Pending = null;
        LundunStory.Set(StoryKey, id);
    }

    /// The character this story is played as. Story only: in Free Roam this is always James and nothing is read.
    public static Def Story
    {
        get
        {
            if (!LundunGame.IsStory) return Default;
            string id;
            if (!LundunStory.TryGet(StoryKey, out id)) return Default;      // a save from before Phase 2
            Def d = Find(id);
            if (d == null || !d.selectable || !CanLoad(d))
            {
                Debug.LogWarning("[Lundun] Story character '" + id + "' isn't available: playing as James.");
                return Default;
            }
            return d;
        }
    }

    /// LundunStoryUI (Story only): put the story's character in charge of the player. James is already the player in the
    /// city, so for him there's nothing to change. Another character's model goes in here when the first one is approved;
    /// until then anything else falls back to James.
    public static void ApplyStory()
    {
        if (!LundunGame.IsStory) return;
        Def d = Story;
        if (!d.IsSceneJames)
        {
            Debug.LogWarning("[Lundun] Story character '" + d.id + "': swapping models arrives with the first approved character. Playing as James.");
            d = Default;
        }
        Debug.Log("[Lundun] Story character: " + d.name);
    }
}
