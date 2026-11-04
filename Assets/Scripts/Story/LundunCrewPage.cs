using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// S5: the pause menu's CREW tab (Story only): how much each of the crew trusts James (three hearts each, drawn as
/// pips), what they'll do for him at three, and REPLAY for the missions with a choice in them (play one again and choose
/// the other way: the lines, the trust and who helps at the end follow).
public partial class LundunSettings
{
    private GameObject crewTab;
    private readonly List<Image[]> crewPips = new List<Image[]>();
    private readonly List<TextMeshProUGUI> crewNotes = new List<TextMeshProUGUI>();
    private readonly List<GameObject> replayButtons = new List<GameObject>();
    private TextMeshProUGUI replayNote;

    static readonly string[] ReplayIds = { "m05_uncles_rules", "m07_home_turf", "m08_blood_is_thicker" };
    static readonly string[] ReplayNames = { "05  UNCLE'S RULES", "07  HOME TURF", "08  BLOOD IS THICKER" };

    void BuildCrew(RectTransform panel)
    {
        crewTab = Rect("CrewTab", panel).gameObject; Stretch((RectTransform)crewTab.transform);
        RectTransform ct = (RectTransform)crewTab.transform;
        float y = -190f;
        for (int i = 0; i < LundunTrust.Crew.Length; i++)
        {
            string who = LundunTrust.Crew[i];
            TextMeshProUGUI name = Label(ct, LundunTrust.Pretty(who).ToUpperInvariant(), y);
            name.color = LundunMissions.ColourOf(who);
            Image[] pips = new Image[LundunTrust.Most];
            for (int k = 0; k < pips.Length; k++)
            {
                RectTransform d = Disc(ct, Color.white, 34);
                d.anchorMin = d.anchorMax = new Vector2(0f, 1f); d.pivot = new Vector2(0f, 1f);
                d.anchoredPosition = new Vector2(330f + k * 46f, y - 18f);
                pips[k] = d.GetComponent<Image>();
            }
            crewPips.Add(pips);
            TextMeshProUGUI n = Text(ct, "", 22, Dim);
            n.alignment = TextAlignmentOptions.MidlineLeft; n.textWrappingMode = TextWrappingModes.Normal; n.richText = true;
            RectTransform nr = n.rectTransform;
            nr.anchorMin = nr.anchorMax = new Vector2(0f, 1f); nr.pivot = new Vector2(0f, 1f);
            nr.anchoredPosition = new Vector2(480f, y); nr.sizeDelta = new Vector2(470f, RowH);
            crewNotes.Add(n);
            y -= 78f;
        }
        y -= 12f;
        Label(ct, "REPLAY A CHOICE", y);
        y -= 58f;
        float w = 290f, gap = 15f;
        for (int i = 0; i < ReplayIds.Length; i++)
        {
            string id = ReplayIds[i];
            RectTransform o = Rect("Replay_" + id, ct);
            o.anchorMin = o.anchorMax = new Vector2(0f, 1f); o.pivot = new Vector2(0f, 1f);
            o.anchoredPosition = new Vector2(56f + i * (w + gap), y); o.sizeDelta = new Vector2(w, 64f);
            Image img = o.gameObject.AddComponent<Image>();
            img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.94f; img.color = new Color(1f, 1f, 1f, 0.1f);
            Button bt = o.gameObject.AddComponent<Button>(); bt.targetGraphic = img;
            bt.onClick.AddListener(() =>
            {
                LundunHaptics.Light();
                if (!LundunMissions.Replay(id)) return;
                Resume();
                LundunMission m = LundunMissionBook.All.Find(x => x.Id == id);
                LundunMissions.Toast("REPLAY: " + (m != null ? m.Title : id) + "\nIts start marker is on the map");
            });
            o.gameObject.AddComponent<LundunPressFX>();
            TextMeshProUGUI t = Text(o, ReplayNames[i], 22, Color.white); Stretch(t.rectTransform); t.characterSpacing = 2f;
            replayButtons.Add(o.gameObject);
        }
        y -= 74f;
        replayNote = Note(ct, "", ref y);
        RectTransform rn = replayNote.rectTransform; rn.anchoredPosition = new Vector2(56f, rn.anchoredPosition.y); rn.sizeDelta = new Vector2(888f, 56f);
        crewTab.SetActive(false);
    }

    void RefreshCrew()
    {
        for (int i = 0; i < LundunTrust.Crew.Length && i < crewPips.Count; i++)
        {
            string who = LundunTrust.Crew[i];
            int t = Mathf.Max(0, LundunTrust.Of(who));
            Color c = LundunMissions.ColourOf(who);
            for (int k = 0; k < crewPips[i].Length; k++) crewPips[i][k].color = k < t ? c : new Color(1f, 1f, 1f, 0.12f);
            string perk = LundunTrust.Perks[i];
            crewNotes[i].text = !LundunTrust.Around(who) ? "<color=#9AA3B5>Gone with Marcus. Not around till this is over.</color>"
                              : t >= LundunTrust.Most ? "<color=#FFA82E>" + perk + "</color>  <size=80%>(coming soon)</size>"
                              : "At 3: " + perk;
        }
        bool any = false;
        LundunStoryData d = LundunStory.Data;
        for (int i = 0; i < ReplayIds.Length && i < replayButtons.Count; i++)
        {
            bool can = LundunMissionBook.Done(d, ReplayIds[i]);
            replayButtons[i].SetActive(can);
            any |= can;
        }
        replayNote.text = LundunMissions.Running ? "Finish or quit this mission first."
                        : any ? "Its start marker comes back: play it again and choose the other way. Lines and trust follow what you choose."
                        : "Missions with a choice in them can be replayed here once you've passed them.";
    }
}
