/// Phase 5 (gate B): what the cameras have on James, remembered in the story save (key/value, same save version), so
/// later missions can use it: Hollis shows the Basketful footage in M05 unless that camera was shot out in M02, the
/// palace strongroom holds the tape, the morning news depends on what exists...
///   Recorded(id)   a camera (or a mission's recorder) has footage of James for this moment
///   Destroyed(id)  the camera was shot out before it could keep anything
///   Wiped(id)      the recorder was wiped: the footage is gone
/// Each piece of evidence has its own id ("basketful_cam", "brixtun_station", "yard_recorder", "palace_recorder"...):
/// wiping one never touches another. Story only: Free Roam keeps no evidence.
public static class LundunEvidence
{
    public enum State { None, Recorded, Destroyed, Wiped }

    const string Prefix = "evidence:";

    public static State Get(string id)
    {
        if (!LundunGame.IsStory || string.IsNullOrEmpty(id)) return State.None;
        string v;
        if (!LundunStory.TryGet(Prefix + id, out v)) return State.None;
        switch (v)
        {
            case "recorded": return State.Recorded;
            case "destroyed": return State.Destroyed;
            case "wiped": return State.Wiped;
        }
        return State.None;
    }

    /// Footage of James exists for this (recorded and not since wiped).
    public static bool Exists(string id) { return Get(id) == State.Recorded; }

    public static void Recorded(string id) { Set(id, "recorded"); }
    public static void Destroyed(string id) { Set(id, "destroyed"); }
    public static void Wiped(string id) { Set(id, "wiped"); }

    static void Set(string id, string v)
    {
        if (!LundunGame.IsStory || string.IsNullOrEmpty(id)) return;
        LundunStory.Set(Prefix + id, v);
        UnityEngine.Debug.Log("[Lundun] Evidence " + id + ": " + v);
    }
}
