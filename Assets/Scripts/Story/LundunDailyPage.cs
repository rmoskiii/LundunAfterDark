using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// S8: the pause menu's DAILY tab (Story only): today's three challenges (what, how far along, what it pays, a tick
/// when done), this week's count towards the £2,500 bonus, and when the next three come.
public partial class LundunSettings
{
    private GameObject dailyTab;
    private RectTransform dailyList;
    private TextMeshProUGUI dailyWeek, dailyNote;

    void BuildDaily(RectTransform panel)
    {
        dailyTab = Rect("DailyTab", panel).gameObject; Stretch((RectTransform)dailyTab.transform);
        RectTransform dt = (RectTransform)dailyTab.transform;
        TextMeshProUGUI title = Text(dt, "TODAY'S CHALLENGES", 30, Color.white);
        Top(title.rectTransform, -186f, 44f);
        dailyList = Rect("List", dt);
        Top(dailyList, -246f, 340f);
        dailyList.offsetMin = new Vector2(40f, dailyList.offsetMin.y); dailyList.offsetMax = new Vector2(-40f, dailyList.offsetMax.y);
        dailyWeek = Text(dt, "", 24, Color.white);
        dailyWeek.richText = true;
        Top(dailyWeek.rectTransform, -610f, 40f);
        dailyNote = Text(dt, "", 21, Dim);
        dailyNote.textWrappingMode = TextWrappingModes.Normal;
        Top(dailyNote.rectTransform, -656f, 60f);
        dailyNote.rectTransform.offsetMin = new Vector2(80f, dailyNote.rectTransform.offsetMin.y);
        dailyNote.rectTransform.offsetMax = new Vector2(-80f, dailyNote.rectTransform.offsetMax.y);
        dailyTab.SetActive(false);
    }

    void RefreshDaily()
    {
        if (dailyList == null) return;
        for (int i = dailyList.childCount - 1; i >= 0; i--) Destroy(dailyList.GetChild(i).gameObject);
        LundunDaily dd = LundunDaily.Instance;
        if (dd == null) { dailyNote.text = "Daily challenges start once you're out on the streets."; dailyWeek.text = ""; return; }
        float y = 0f;
        foreach (LundunDaily.Challenge c in dd.Current) { DailyRow(c, y); y -= 108f; }
        int wk = LundunDaily.WeekDone;
        dailyWeek.text = "THIS WEEK  <color=#FFA82E>" + Mathf.Min(wk, LundunDaily.WeekTarget) + " / " + LundunDaily.WeekTarget + "</color>   ·   "
                       + (LundunDaily.WeekPaid ? "bonus paid: " + LundunWallet.Format(LundunDaily.WeekBonus) : "do " + LundunDaily.WeekTarget + " for a " + LundunWallet.Format(LundunDaily.WeekBonus) + " bonus");
        System.TimeSpan left = System.DateTime.Today.AddDays(1) - System.DateTime.Now;
        dailyNote.text = "Three new challenges in " + (left.Hours > 0 ? left.Hours + "h " : "") + left.Minutes + "m (midnight). Missing a day costs nothing.";
    }

    void DailyRow(LundunDaily.Challenge c, float y)
    {
        bool done = LundunDaily.Done(c);
        float got = Mathf.Min(LundunDaily.Progress(c), c.need);
        RectTransform row = Rect("Row_" + c.id, dailyList);
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, y); row.sizeDelta = new Vector2(0f, 96f);
        Image bg = row.gameObject.AddComponent<Image>();
        bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 1.2f;
        bg.color = done ? new Color(0.3f, 0.85f, 0.4f, 0.14f) : new Color(1f, 1f, 1f, 0.06f);

        RectTransform disc = Disc(row, done ? new Color(0.35f, 0.9f, 0.45f) : new Color(1f, 1f, 1f, 0.15f), 44);
        disc.anchorMin = disc.anchorMax = new Vector2(0f, 0.5f); disc.pivot = new Vector2(0f, 0.5f); disc.anchoredPosition = new Vector2(20f, 0f);

        TextMeshProUGUI t = Text(row, c.text.ToUpperInvariant(), 25, done ? new Color(1f, 1f, 1f, 0.6f) : Color.white);
        t.alignment = TextAlignmentOptions.MidlineLeft; t.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform tr = t.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0f, 0.5f); tr.pivot = new Vector2(0f, 0.5f);
        tr.anchoredPosition = new Vector2(80f, 16f); tr.sizeDelta = new Vector2(600f, 36f);

        // the progress bar under it (for the ones that count up)
        string count = c.Unit == "m" ? Mathf.FloorToInt(got) + " / " + Mathf.RoundToInt(c.need) + " m"
                     : c.Unit == "£" ? LundunWallet.Format((long)got) + " / " + LundunWallet.Format((long)c.need)
                     : Mathf.FloorToInt(got) + " / " + Mathf.RoundToInt(c.need);
        TextMeshProUGUI p = Text(row, done ? "DONE" : count, 19, Dim);
        p.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform pr = p.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(0f, 0.5f); pr.pivot = new Vector2(0f, 0.5f);
        pr.anchoredPosition = new Vector2(80f, -20f); pr.sizeDelta = new Vector2(240f, 28f);
        RectTransform track = Rect("Track", row);
        track.anchorMin = track.anchorMax = new Vector2(0f, 0.5f); track.pivot = new Vector2(0f, 0.5f);
        track.anchoredPosition = new Vector2(330f, -20f); track.sizeDelta = new Vector2(340f, 10f);
        Image ti = track.gameObject.AddComponent<Image>(); ti.color = new Color(1f, 1f, 1f, 0.12f); ti.raycastTarget = false;
        RectTransform fill = Rect("Fill", track);
        fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(Mathf.Clamp01(done ? 1f : got / Mathf.Max(1f, c.need)), 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        Image fi = fill.gameObject.AddComponent<Image>(); fi.color = done ? new Color(0.35f, 0.9f, 0.45f) : Accent; fi.raycastTarget = false;

        TextMeshProUGUI pay = Text(row, "+" + LundunWallet.Format(c.pay), 26, done ? new Color(0.35f, 0.9f, 0.45f) : Accent);
        pay.alignment = TextAlignmentOptions.MidlineRight;
        RectTransform payR = pay.rectTransform;
        payR.anchorMin = payR.anchorMax = new Vector2(1f, 0.5f); payR.pivot = new Vector2(1f, 0.5f);
        payR.anchoredPosition = new Vector2(-24f, 0f); payR.sizeDelta = new Vector2(170f, 40f);
    }
}
