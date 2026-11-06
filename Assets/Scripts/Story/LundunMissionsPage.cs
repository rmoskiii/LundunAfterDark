using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// S6: the pause menu's MISSIONS tab (Story only): every mission passed, in order, with its best medal, best time and
/// what gold needs, and REPLAY (its start marker comes back; play it again, choose again). Scrolls when it's long.
public partial class LundunSettings
{
    private GameObject missionsTab;
    private RectTransform missionsList;
    private TextMeshProUGUI missionsNote;

    void BuildMissions(RectTransform panel)
    {
        missionsTab = Rect("MissionsTab", panel).gameObject; Stretch((RectTransform)missionsTab.transform);
        RectTransform mt = (RectTransform)missionsTab.transform;
        // a scrolling list between the tabs and the buttons at the bottom
        RectTransform view = Rect("View", mt);
        view.anchorMin = new Vector2(0f, 0f); view.anchorMax = new Vector2(1f, 1f);
        view.offsetMin = new Vector2(40f, 220f); view.offsetMax = new Vector2(-40f, -170f);
        view.gameObject.AddComponent<RectMask2D>();
        Image catcher = view.gameObject.AddComponent<Image>(); catcher.color = new Color(0f, 0f, 0f, 0.001f);
        missionsList = Rect("List", view);
        missionsList.anchorMin = new Vector2(0f, 1f); missionsList.anchorMax = new Vector2(1f, 1f); missionsList.pivot = new Vector2(0.5f, 1f);
        missionsList.anchoredPosition = Vector2.zero; missionsList.sizeDelta = new Vector2(0f, 100f);
        ScrollRect sr = view.gameObject.AddComponent<ScrollRect>();
        sr.content = missionsList; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 30f; sr.viewport = view;
        missionsNote = Text(mt, "", 22, Dim);
        missionsNote.alignment = TextAlignmentOptions.Center; missionsNote.textWrappingMode = TextWrappingModes.Normal;
        RectTransform nr = missionsNote.rectTransform;
        nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 0f); nr.pivot = new Vector2(0.5f, 0f);
        nr.anchoredPosition = new Vector2(0f, 160f); nr.sizeDelta = new Vector2(880f, 56f);
        missionsTab.SetActive(false);
    }

    void RefreshMissions()
    {
        if (missionsList == null) return;
        for (int i = missionsList.childCount - 1; i >= 0; i--) Destroy(missionsList.GetChild(i).gameObject);
        LundunStoryData d = LundunStory.Data;
        float y = 0f;
        int n = 0, shown = 0;
        foreach (LundunMission m in LundunMissionBook.All)
        {
            n++;
            if (!LundunMissionBook.Done(d, m.Id)) continue;
            Row(m, n, y);
            y -= 96f;
            shown++;
        }
        missionsList.sizeDelta = new Vector2(0f, Mathf.Max(100f, -y));
        missionsList.anchoredPosition = Vector2.zero;
        missionsNote.text = LundunMissions.Running ? "Finish or quit this mission to replay one."
                          : shown == 0 ? "Missions you've passed show here, with your medals and best times."
                          : "REPLAY brings its start marker back. Choose differently and the story follows.";
    }

    void Row(LundunMission m, int number, float y)
    {
        RectTransform row = Rect("Row_" + m.Id, missionsList);
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, y); row.sizeDelta = new Vector2(0f, 86f);
        Image bg = row.gameObject.AddComponent<Image>();
        bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 1.2f; bg.color = new Color(1f, 1f, 1f, 0.06f);

        int medal = LundunMissions.BestMedal(m.Id), best = LundunMissions.BestTime(m.Id);
        Color mc = medal == 3 ? new Color(1f, 0.82f, 0.25f) : medal == 2 ? new Color(0.8f, 0.84f, 0.9f) : medal == 1 ? new Color(0.85f, 0.55f, 0.3f) : new Color(1f, 1f, 1f, 0.15f);
        RectTransform disc = Disc(row, mc, 44);
        disc.anchorMin = disc.anchorMax = new Vector2(0f, 0.5f); disc.pivot = new Vector2(0f, 0.5f); disc.anchoredPosition = new Vector2(18f, 0f);

        TextMeshProUGUI title = Text(row, number.ToString("00") + "  " + m.Title.ToUpperInvariant(), 26, Color.white);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform tr = title.rectTransform;
        tr.anchorMin = tr.anchorMax = new Vector2(0f, 0.5f); tr.pivot = new Vector2(0f, 0.5f);
        tr.anchoredPosition = new Vector2(78f, 14f); tr.sizeDelta = new Vector2(520f, 36f);

        string medalName = medal == 3 ? "GOLD" : medal == 2 ? "SILVER" : medal == 1 ? "BRONZE" : "NO MEDAL";
        TextMeshProUGUI sub = Text(row, "<color=#" + ColorUtility.ToHtmlStringRGB(mc) + ">" + medalName + "</color>"
                                       + (best > 0 ? "   ·   best " + LundunMissions.Clock(best) : "")
                                       + (string.IsNullOrEmpty(m.MedalNote) || medal == 3 ? "" : "   ·   gold: " + m.MedalNote), 19, Dim);
        sub.richText = true; sub.alignment = TextAlignmentOptions.MidlineLeft; sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform sr = sub.rectTransform;
        sr.anchorMin = sr.anchorMax = new Vector2(0f, 0.5f); sr.pivot = new Vector2(0f, 0.5f);
        sr.anchoredPosition = new Vector2(78f, -18f); sr.sizeDelta = new Vector2(620f, 30f);

        RectTransform b = Rect("Replay", row);
        b.anchorMin = b.anchorMax = new Vector2(1f, 0.5f); b.pivot = new Vector2(1f, 0.5f);
        b.anchoredPosition = new Vector2(-14f, 0f); b.sizeDelta = new Vector2(190f, 60f);
        Image bi = b.gameObject.AddComponent<Image>();
        bi.sprite = LundunHudArt.Pill; bi.type = Image.Type.Sliced; bi.pixelsPerUnitMultiplier = 0.94f;
        bool can = !LundunMissions.Running && !LundunMissions.Replaying(m.Id);
        bi.color = can ? new Color(1f, 0.66f, 0.18f, 0.9f) : new Color(1f, 1f, 1f, 0.12f);
        Button bt = b.gameObject.AddComponent<Button>(); bt.targetGraphic = bi; bt.interactable = can;
        string id = m.Id, name = m.Title;
        bt.onClick.AddListener(() =>
        {
            LundunHaptics.Light();
            if (!LundunMissions.Replay(id)) return;
            Resume();
            LundunMissions.Toast("REPLAY: " + name + "\nIts start marker is on the map");
        });
        b.gameObject.AddComponent<LundunPressFX>();
        TextMeshProUGUI bl = Text(b, LundunMissions.Replaying(m.Id) ? "ON THE MAP" : "REPLAY", 22, Color.white); Stretch(bl.rectTransform);
    }
}
