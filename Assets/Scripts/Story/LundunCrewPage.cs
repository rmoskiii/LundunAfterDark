using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// S5: the pause menu's CREW tab (Story only): how much each of the crew trusts James (three hearts each, drawn as
/// pips) and what they'll do for him at three. (S6: replaying missions moved to the MISSIONS tab.)
public partial class LundunSettings
{
    private GameObject crewTab;
    private readonly List<Image[]> crewPips = new List<Image[]>();
    private readonly List<TextMeshProUGUI> crewNotes = new List<TextMeshProUGUI>();

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
    }
}
