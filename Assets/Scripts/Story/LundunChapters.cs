/// Phase 4: where James is on the climb, shown at the top of each mission card. No rank system: just the chapter of
/// the story a mission belongs to, so the rise from newcomer to kingmaker is visible.
///   CHAPTER 1 · NEWCOMER    M01–M03   others explain the game to him
///   CHAPTER 2 · RUNNER      M04–M05   trusted with jobs, owes a favour
///   CHAPTER 3 · PLAYER      M06–M08   his name's ringing; rivals take notice
///   CHAPTER 4 · OPERATOR    M09–M11   he plans, the family follows
///   CHAPTER 5 · KINGMAKER   M12       everyone waits on his call
///   SIDE JOB                M13–M15   (not part of the story's order)
public static class LundunChapters
{
    public static string For(LundunMission m)
    {
        if (m == null) return "";
        if (!m.Story) return "SIDE JOB";
        switch (m.Id)
        {
            case "m01_one_last_favour":
            case "m02_easy_money":
            case "m03_no_way_home":        return "CHAPTER 1  ·  NEWCOMER";
            case "m04_estate_business":
            case "m05_uncles_rules":       return "CHAPTER 2  ·  RUNNER";
            case "m06_passenger_problems":
            case "m07_home_turf":
            case "m08_blood_is_thicker":   return "CHAPTER 3  ·  PLAYER";
            case "m09_paper_trail":
            case "m10_open_for_business":
            case "m11_family_meeting":     return "CHAPTER 4  ·  OPERATOR";
            case "m12_lundun_after_dark":  return "CHAPTER 5  ·  KINGMAKER";
        }
        return "";
    }
}
