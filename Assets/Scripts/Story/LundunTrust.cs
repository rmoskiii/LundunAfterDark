using System.Collections.Generic;
using UnityEngine;

/// S5: how much the crew trust James, 0–3 each (hearts on the pause menu's CREW page), kept in the story save (LundunStoryData.trust: Marcus, Tayo,
/// Kemi, Uncle Femi, Leah). Worked out from where everyone started and the choices made, so a replayed mission that's
/// chosen the other way changes it (and a save from before S5 picks up mission 05's choice the first time it loads).
/// Start: 1 each, Marcus and Uncle Femi 2.
///   05 Uncle's Rules        pay £1,000 → Uncle Femi +1          promise a favour → Uncle Femi −1 (and Hollis is owed one)
///   07 Home Turf            talked them down → Kemi +1, Marcus −1          fought → Marcus +1
///   08 Blood Is Thicker     handed Tayo to Marcus → Marcus +1 (Tayo's gone till the end)   let him go → Tayo +2, Marcus −1
/// At 3 each has a perk (they arrive in later passes; the CREW page says what they'll be).
public static class LundunTrust
{
    public static readonly string[] Crew = { "MARCUS", "TAYO", "KEMI", "UNCLE FEMI", "LEAH" };
    static readonly int[] Start = { 2, 1, 1, 2, 1 };
    public const int Most = 3;

    public const string HollisFavour = "hollis:favour";
    public const string TayoGone = "tayo:gone";

    /// What each one does for James at 3 ♥ (the plan's perks; not in the game yet).
    public static readonly string[] Perks =
    {
        "Backup: rings round to help in a fight",
        "Knows a fence: more for what you nick",
        "Mates' rates at the arches garage",
        "Lends you his spare car",
        "Patches you up: full health once a day",
    };

    static int Index(string who) { return System.Array.IndexOf(Crew, who); }

    /// 0–3 (−1 for someone who isn't crew).
    public static int Of(string who)
    {
        int i = Index(who);
        LundunStoryData d = LundunStory.Data;
        if (i < 0 || d == null) return -1;
        if (d.trust == null || d.trust.Length < 6) Recalc(false);
        return Mathf.Clamp(d.trust[i], 0, Most);
    }

    public static bool Has(string flag) { LundunStoryData d = LundunStory.Data; return d != null && d.flags.Contains(flag); }
    public static bool Chose(string key, bool first) { return LundunChoice.Chose(key, first); }
    public static bool Made(string key) { return Chose(key, true) || Chose(key, false); }

    /// Work it out again from the start values and the choices (announce: a line for each change: "Kemi will
    /// remember that."). Saved with the story.
    public static void Recalc(bool announce)
    {
        LundunStoryData d = LundunStory.Data;
        if (d == null) return;
        int[] t = (int[])Start.Clone();
        if (Chose("choice:m05", true)) t[3] += 1;
        if (Chose("choice:m05", false)) t[3] -= 1;
        if (Chose("choice:m07", true)) { t[2] += 1; t[0] -= 1; }
        if (Chose("choice:m07", false)) t[0] += 1;
        if (Chose("choice:m08", true)) t[0] += 1;
        if (Chose("choice:m08", false)) { t[1] += 2; t[0] -= 1; }
        for (int i = 0; i < t.Length; i++) t[i] = Mathf.Clamp(t[i], 0, Most);

        Flag(d, HollisFavour, Chose("choice:m05", false));
        Flag(d, TayoGone, Chose("choice:m08", true));

        if (d.trust == null || d.trust.Length < 6) { int[] n = new int[6]; if (d.trust != null) System.Array.Copy(d.trust, n, Mathf.Min(d.trust.Length, 6)); d.trust = n; }
        bool first = !d.flags.Contains("trust:set");                       // a save from before S5: nothing to announce
        bool changed = first;
        List<string> notes = new List<string>();
        for (int i = 0; i < t.Length; i++)
        {
            if (d.trust[i] == t[i]) continue;
            if (!first) notes.Add(Note(Crew[i], t[i] - d.trust[i]));
            d.trust[i] = t[i];
            changed = true;
        }
        if (first) d.flags.Add("trust:set");
        if (changed) LundunStory.MarkDirty();
        if (announce && notes.Count > 0) LundunMissions.Toast(string.Join("\n", notes.ToArray()));
    }

    static void Flag(LundunStoryData d, string f, bool on)
    {
        if (on && !d.flags.Contains(f)) d.flags.Add(f);
        else if (!on) d.flags.Remove(f);
    }

    /// "Kemi will remember that." / "Marcus didn't like that."
    static string Note(string who, int change)
    {
        string name = Pretty(who);
        if (change >= 2) return name + " won't forget that.";
        if (change > 0) return name + " will remember that.";
        return name + " didn't like that.";
    }

    public static string Pretty(string who)
    {
        string[] w = who.ToLowerInvariant().Split(' ');
        for (int i = 0; i < w.Length; i++) if (w[i].Length > 0) w[i] = char.ToUpperInvariant(w[i][0]) + w[i].Substring(1);
        return string.Join(" ", w);
    }

    /// S5: who's with James at the end (mission 11's getaway): talked down at the market → Kemi drives; fought → Marcus
    /// covers him.
    public static string FinaleAlly { get { return Chose("choice:m07", true) ? "KEMI" : "MARCUS"; } }

    /// Is this one about (Tayo isn't, after he was handed over, until the finale)?
    public static bool Around(string who) { return !(who == "TAYO" && Has(TayoGone)); }

    /// S5: lines the crew say in the car that depend on what's happened (added to their usual ones).
    public static string[] Banter(string who, string[] usual)
    {
        List<string> more = new List<string>();
        int t = Of(who);
        switch (who)
        {
            case "MARCUS":
                if (Chose("choice:m07", false)) more.Add("The way you handled them at the market. Proper. Dad would've been proud.");
                if (Chose("choice:m07", true)) more.Add("Talking. At the market. They were taking the stalls' money, James.");
                if (Chose("choice:m08", false)) more.Add("You let him walk. After everything. I'm still not over it.");
                if (Chose("choice:m08", true)) more.Add("Tayo's safe where he is. Don't ask where.");
                if (Has(HollisFavour)) more.Add("A favour. To Hollis. You know what that means round here?");
                break;
            case "KEMI":
                if (Chose("choice:m07", true)) more.Add("Mrs Okafor gave me free plantain. Because of you. At the market.");
                if (Chose("choice:m07", false)) more.Add("The market's still talking about that fight. Not in a good way.");
                if (Has(LundunStoryShop.SmashedFlag)) more.Add("I keep seeing the shop. All that glass.");
                if (t >= Most) more.Add("You know I've got your back. Whatever this turns into.");
                break;
            case "UNCLE FEMI":
                if (Has(HollisFavour)) more.Add("A favour, James. A favour to that man. I do not sleep.");
                if (Chose("choice:m05", true)) more.Add("You paid him. I will pay you back. Every penny, you hear me?");
                if (Has(LundunStoryShop.SmashedFlag)) more.Add("Eight years I kept that shop. Eight years.");
                break;
            case "TAYO":
                if (Chose("choice:m08", false)) more.Add("I owe you, bro. For real. I'll make it right.");
                break;
            case "LEAH":
                if (Has(LundunStoryShop.SmashedFlag)) more.Add("Your uncle's sleeping on your sofa. Is everyone alright, actually?");
                if (Chose("choice:m07", false)) more.Add("Kemi said you were in a fight at the market. Let me see your hands.");
                break;
        }
        if (more.Count == 0) return usual;
        // a story line comes up about as often as all the usual ones together
        if (Random.value < 0.5f) return more.ToArray();
        return usual;
    }
}
