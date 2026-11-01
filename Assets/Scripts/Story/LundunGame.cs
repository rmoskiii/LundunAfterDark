using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// S0: which game you're playing.
///   FREE ROAM the game as it has always been: every weapon (or the LOADOUT setting), no money, no story. Its saved
///            things (home safehouse, the clock, the gang areas you've found) stay in PlayerPrefs exactly as before.
///   STORY    the campaign: your own money, weapons and ammo, home safehouse, clock and map, all in a separate save file
///            (story.json). Nothing in Story ever reads or writes Free Roam's saved things, and the other way round.
/// Switching reloads the city in the other mode (the story saves first), so nothing carries over by accident.
/// Device settings (graphics, controls, sound, steering) are shared, as they belong to the phone, not the game.
public static class LundunGame
{
    public enum Mode { FreeRoam, Story }      // (FreeRoam = 0: the saved "last played" keeps its meaning)

    const string LastKey = "Lundun.LastMode";
    public static Mode Current { get; private set; }
    public static bool IsStory { get { return Current == Mode.Story; } }
    /// The start menu shows once when the game launches (not after switching modes).
    public static bool ShowStartMenu { get; set; }
    public static Mode LastPlayed { get { return (Mode)Mathf.Clamp(PlayerPrefs.GetInt(LastKey, 0), 0, 1); } }
    /// The city can be reloaded (it's in the build's scene list).
    public static bool CanSwitch { get { return SceneManager.GetActiveScene().buildIndex >= 0; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        // the world loads in the mode you played last (the start menu can change it); a story only if there is one
        Current = LastPlayed;
        if (Current == Mode.Story && !LundunStory.Exists) Current = Mode.FreeRoam;
        ShowStartMenu = true;
        LundunStory.Forget();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterFirstLoad()
    {
        SceneManager.sceneLoaded += (s, m) => Attach();
        Attach();
    }

    /// The start menu, the money on the HUD and the autosave live on one object, added to the city's scene.
    static void Attach()
    {
        if (UnityEngine.Object.FindAnyObjectByType<VehicleSystem>() == null) return;
        // a test scene that isn't in the build (it can't be reloaded): always the plain game, no menu
        if (!CanSwitch) { Current = Mode.FreeRoam; return; }
        if (UnityEngine.Object.FindAnyObjectByType<LundunStoryUI>() != null) return;
        new GameObject("LundunStory").AddComponent<LundunStoryUI>();
    }

    /// Play this mode (from the start menu or the pause menu). newStory: wipe the story save and begin again.
    /// Reloads the city unless it's already in that mode (and not starting a new story).
    public static void Play(Mode mode, bool newStory)
    {
        bool reload = mode != Current || newStory;
        // keep what you had: the story saves, and Free Roam keeps its clock as it would on quitting
        if (LundunClock.Instance != null) LundunClock.Instance.Save();
        if (IsStory) LundunStory.Write();
        if (newStory) LundunStory.Reset();
        Current = mode;
        PlayerPrefs.SetInt(LastKey, (int)mode); PlayerPrefs.Save();
        ShowStartMenu = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (reload && CanSwitch) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}

/// S0: everything a story remembers (story.json). Missions (S1) add to it; nothing here is used in Free Roam.
[Serializable]
public class LundunStoryData
{
    public int version = 1;
    public long money;
    public long earnedTotal;
    public int missionsDone;
    public string currentMission = "";
    public List<string> flags = new List<string>();
    public int[] trust = new int[6];                       // Marcus, Tayo, Kemi, Uncle Femi, Leah, (spare)
    public bool weaponsSaved;
    public bool[] weaponsOwned = new bool[5];
    public int[] spare = new int[5];
    public int[] inMag = new int[5];
    public List<string> medals = new List<string>();
    public string dailyDate = "";
    public List<string> dailyDone = new List<string>();
    public List<string> keys = new List<string>();         // the story's own copy of what Free Roam keeps in PlayerPrefs
    public List<string> values = new List<string>();       // (home safehouse, clock, gang areas found)
    public float playSeconds;
    public string savedAt = "";
}

/// S0: the story save file. Written whole each time (to a temporary file first, the last good one kept as a backup),
/// so a crash mid-write never loses the story.
public static class LundunStory
{
    static LundunStoryData data;
    static bool dirty;

    public static string FilePath { get { return Path.Combine(Application.persistentDataPath, "story.json"); } }
    static string BackupPath { get { return FilePath + ".bak"; } }
    public static bool Exists { get { try { return File.Exists(FilePath) || File.Exists(BackupPath); } catch { return false; } } }

    public static LundunStoryData Data { get { if (data == null) data = Load(); return data; } }
    public static void MarkDirty() { dirty = true; }
    public static bool Dirty { get { return dirty; } }

    /// Drop what's in memory (the next read loads the file).
    public static void Forget() { data = null; dirty = false; }

    static LundunStoryData Load()
    {
        foreach (string path in new[] { FilePath, BackupPath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                LundunStoryData d = JsonUtility.FromJson<LundunStoryData>(File.ReadAllText(path));
                if (d != null) return Fix(d);
            }
            catch (Exception e) { Debug.LogWarning("[Lundun] story save unreadable (" + path + "): " + e.Message); }
        }
        return new LundunStoryData();
    }

    static LundunStoryData Fix(LundunStoryData d)
    {
        if (d.flags == null) d.flags = new List<string>();
        if (d.medals == null) d.medals = new List<string>();
        if (d.dailyDone == null) d.dailyDone = new List<string>();
        if (d.keys == null || d.values == null || d.keys.Count != d.values.Count) { d.keys = new List<string>(); d.values = new List<string>(); }
        if (d.trust == null || d.trust.Length != 6) d.trust = new int[6];
        if (d.weaponsOwned == null || d.weaponsOwned.Length != 5) { d.weaponsOwned = new bool[5]; d.weaponsSaved = false; }
        if (d.spare == null || d.spare.Length != 5) { d.spare = new int[5]; d.weaponsSaved = false; }
        if (d.inMag == null || d.inMag.Length != 5) { d.inMag = new int[5]; d.weaponsSaved = false; }
        if (d.money < 0) d.money = 0;
        return d;
    }

    /// A new story: everything back to the start (the old file is kept as the backup until the first save).
    public static void Reset()
    {
        data = new LundunStoryData();
        dirty = true;
        WriteFile();
    }

    /// Save now (Story only): picks up the weapons and the clock first.
    public static void Write()
    {
        if (!LundunGame.IsStory) return;
        LundunWeapons w = LundunWeapons.Instance;
        if (w != null)
        {
            w.GetState(Data.weaponsOwned, Data.spare, Data.inMag);
            Data.weaponsSaved = true;
        }
        if (LundunClock.Instance != null) LundunClock.Instance.Remember();
        WriteFile();
    }

    static void WriteFile()
    {
        try
        {
            Data.savedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            string json = JsonUtility.ToJson(Data, true);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath)) File.Copy(FilePath, BackupPath, true);
            File.Copy(tmp, FilePath, true);
            File.Delete(tmp);
            dirty = false;
        }
        catch (Exception e) { Debug.LogWarning("[Lundun] couldn't save the story: " + e.Message); }
    }

    /// Story only: put back James's weapons and ammo as they were saved (a new story: fists and a pistol, full).
    public static void ApplyWeapons(LundunWeapons w)
    {
        if (!LundunGame.IsStory || w == null || !Data.weaponsSaved) return;
        w.SetState(Data.weaponsOwned, Data.spare, Data.inMag);
    }

    // ------------------------------------------------------------------ the story's own key/value store
    public static bool TryGet(string key, out string value)
    {
        int i = Data.keys.IndexOf(key);
        value = i >= 0 ? Data.values[i] : null;
        return i >= 0;
    }

    public static void Set(string key, string value)
    {
        int i = Data.keys.IndexOf(key);
        if (i >= 0) { if (Data.values[i] == value) return; Data.values[i] = value; }
        else { Data.keys.Add(key); Data.values.Add(value); }
        dirty = true;
    }
}

/// S0: the saved things that belong to a game (not to the phone): in Free Roam they're PlayerPrefs exactly as before;
/// in Story they live in the story save. Used for the home safehouse, the clock and the gang areas found.
public static class LundunSave
{
    public static string GetString(string key, string def)
    {
        if (!LundunGame.IsStory) return PlayerPrefs.GetString(key, def);
        string v; return LundunStory.TryGet(key, out v) ? v : def;
    }

    public static void SetString(string key, string value)
    {
        if (!LundunGame.IsStory) { PlayerPrefs.SetString(key, value); return; }
        LundunStory.Set(key, value);
    }

    public static int GetInt(string key, int def)
    {
        if (!LundunGame.IsStory) return PlayerPrefs.GetInt(key, def);
        string v; int r;
        return LundunStory.TryGet(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
    }

    public static void SetInt(string key, int value)
    {
        if (!LundunGame.IsStory) { PlayerPrefs.SetInt(key, value); return; }
        LundunStory.Set(key, value.ToString(CultureInfo.InvariantCulture));
    }

    public static float GetFloat(string key, float def)
    {
        if (!LundunGame.IsStory) return PlayerPrefs.GetFloat(key, def);
        string v; float r;
        return LundunStory.TryGet(key, out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : def;
    }

    public static void SetFloat(string key, float value)
    {
        if (!LundunGame.IsStory) { PlayerPrefs.SetFloat(key, value); return; }
        LundunStory.Set(key, value.ToString("R", CultureInfo.InvariantCulture));
    }

    /// Write it down now (Free Roam: PlayerPrefs.Save, as before; Story: the story file).
    public static void Save()
    {
        if (!LundunGame.IsStory) { PlayerPrefs.Save(); return; }
        LundunStory.Write();
    }
}

/// S0: money (£), Story only. Missions, jobs and robberies pay in (S1 onwards); shops and bills take out.
public static class LundunWallet
{
    /// Money in (+) or out (−), and what for: the HUD shows it.
    public static event Action<long, string> Changed;

    public static long Cash { get { return LundunGame.IsStory ? LundunStory.Data.money : 0; } }
    public static bool CanAfford(long amount) { return LundunGame.IsStory && LundunStory.Data.money >= amount; }

    public static void Earn(long amount, string why)
    {
        if (!LundunGame.IsStory || amount <= 0) return;
        LundunStory.Data.money += amount;
        LundunStory.Data.earnedTotal += amount;
        LundunStory.MarkDirty();
        if (Changed != null) Changed(amount, why);
    }

    /// Takes up to 'amount' (never below £0). Returns what was actually taken.
    public static long Pay(long amount, string why)
    {
        if (!LundunGame.IsStory || amount <= 0) return 0;
        long take = Math.Min(amount, LundunStory.Data.money);
        if (take <= 0) return 0;
        LundunStory.Data.money -= take;
        LundunStory.MarkDirty();
        if (Changed != null) Changed(-take, why);
        return take;
    }

    /// No bills while you're new: nothing until three story missions are done.
    static bool Bills { get { return LundunGame.IsStory && LundunStory.Data.missionsDone >= 3; } }

    /// WASTED: £50 + 2% of your cash, at most £500.
    public static void HospitalBill()
    {
        if (!Bills) return;
        long cash = LundunStory.Data.money;
        Pay(Math.Min(500, 50 + cash * 2 / 100), "Hospital bill");
    }

    /// BUSTED: £100 + 5% of your cash, at most £1,000; you keep your weapons but lose some spare ammo.
    public static void ArrestFine()
    {
        if (!Bills) return;
        long cash = LundunStory.Data.money;
        Pay(Math.Min(1000, 100 + cash * 5 / 100), "Fine");
        if (LundunWeapons.Instance != null) LundunWeapons.Instance.LoseSpare(0.3f);
    }

    /// "£12,450"
    public static string Format(long amount) { return "£" + Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture); }
}
