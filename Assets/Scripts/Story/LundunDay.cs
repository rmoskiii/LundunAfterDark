using UnityEngine;

/// S7: the story's days (Story only; added by LundunStoryUI). A new day starts at 06:00 on the city's clock, however
/// the time got there (it ticking on, a rest at the lock-up, a mission skipping to night). Things you can do once a day
/// use it: robbing each store, the lock-up's free ammo.
public class LundunDay : MonoBehaviour
{
    public static LundunDay Instance { get; private set; }
    const string Key = "Lundun.StoryHours";

    private float hours = -1f;          // hours of story time since the story's clock started
    private float lastHour = -1f;
    private float nextSave;

    /// Today's number (0 = the day the story began). -1 before the clock is there.
    public static int Today { get { return Instance != null && Instance.hours >= 0f ? Instance.DayOf(Instance.hours) : -1; } }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    int DayOf(float h) { return Mathf.FloorToInt((h - 6f) / 24f); }

    void Update()
    {
        LundunClock c = LundunClock.Instance;
        if (c == null) return;
        if (hours < 0f)
        {
            hours = Mathf.Max(0f, LundunSave.GetFloat(Key, c.Hour));
            lastHour = Mathf.Repeat(hours, 24f);
        }
        float step = Mathf.Repeat(c.Hour - lastHour, 24f);           // the clock only goes forwards
        if (step > 0f)
        {
            int before = DayOf(hours);
            hours += step;
            lastHour = c.Hour;
            if (DayOf(hours) != before) Save();
        }
        if (Time.unscaledTime >= nextSave) { nextSave = Time.unscaledTime + 5f; Save(); }
    }

    void Save() { if (hours >= 0f) LundunSave.SetFloat(Key, hours); }

    /// "Done today" bookkeeping, kept in the story save: did 'what' already happen today?
    public static bool DoneToday(string what)
    {
        int today = Today;
        return today >= 0 && LundunSave.GetInt("Lundun.Daily." + what, int.MinValue) == today;
    }

    public static void MarkToday(string what)
    {
        int today = Today;
        if (today >= 0) LundunSave.SetInt("Lundun.Daily." + what, today);
    }
}
