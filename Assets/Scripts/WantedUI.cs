using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Wanted stars. W2: solid while police can see you, flashing while they're searching; underneath, a status line —
/// SEEN (red), or SEARCHING with a bar that fills as you get closer to losing them (faster outside the search area).
public class WantedUI : MonoBehaviour
{
    [SerializeField] private Image[] stars;
    [SerializeField] private Color litColour = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color unlitColour = new Color(0f, 0f, 0f, 0.35f);
    [SerializeField] private float flashSpeed = 5f;

    private RectTransform status;
    private TextMeshProUGUI statusText;
    private Image statusBar, statusBack;
    private bool built;
    static readonly Color SeenRed = new Color(1f, 0.28f, 0.24f);
    static readonly Color SearchAmber = new Color(1f, 0.74f, 0.25f);

    void BuildStatus()
    {
        built = true;
        if (stars == null || stars.Length == 0 || stars[0] == null) return;
        Transform row = stars[0].transform.parent;
        GameObject g = new GameObject("WantedStatus", typeof(RectTransform));
        status = (RectTransform)g.transform;
        status.SetParent(row, false);
        status.anchorMin = status.anchorMax = new Vector2(0.5f, 0.5f);
        status.pivot = new Vector2(1f, 0.5f);
        status.anchoredPosition = new Vector2(34f, -50f);        // right edge under the first star
        status.sizeDelta = new Vector2(330f, 26f);
        statusBack = g.AddComponent<Image>();
        statusBack.sprite = LundunHudArt.Pill; statusBack.type = Image.Type.Sliced; statusBack.pixelsPerUnitMultiplier = 3.2f;
        statusBack.color = new Color(0.02f, 0.03f, 0.06f, 0.6f); statusBack.raycastTarget = false;

        GameObject tg = new GameObject("Text", typeof(RectTransform));
        RectTransform tr = (RectTransform)tg.transform; tr.SetParent(status, false);
        tr.anchorMin = new Vector2(0f, 0f); tr.anchorMax = new Vector2(0f, 1f); tr.pivot = new Vector2(0f, 0.5f);
        tr.anchoredPosition = new Vector2(12f, 0f); tr.sizeDelta = new Vector2(170f, 0f);
        statusText = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) statusText.font = LundunHud.Instance.FontAsset;
        statusText.fontSize = 19; statusText.characterSpacing = 6f; statusText.alignment = TextAlignmentOptions.MidlineLeft;
        statusText.raycastTarget = false; statusText.textWrappingMode = TextWrappingModes.NoWrap;

        GameObject bb = new GameObject("BarBack", typeof(RectTransform));
        RectTransform br = (RectTransform)bb.transform; br.SetParent(status, false);
        br.anchorMin = br.anchorMax = new Vector2(1f, 0.5f); br.pivot = new Vector2(1f, 0.5f);
        br.anchoredPosition = new Vector2(-12f, 0f); br.sizeDelta = new Vector2(130f, 8f);
        Image back = bb.AddComponent<Image>(); back.color = new Color(1f, 1f, 1f, 0.15f); back.raycastTarget = false;
        back.sprite = LundunHudArt.Pill; back.type = Image.Type.Sliced; back.pixelsPerUnitMultiplier = 10f;
        GameObject bf = new GameObject("Bar", typeof(RectTransform));
        RectTransform fr = (RectTransform)bf.transform; fr.SetParent(br, false);
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero;
        statusBar = bf.AddComponent<Image>(); statusBar.raycastTarget = false;
        statusBar.sprite = LundunHudArt.Panel; statusBar.type = Image.Type.Filled; statusBar.fillMethod = Image.FillMethod.Horizontal;
        g.SetActive(false);
    }

    void UpdateStatus(WantedSystem wanted)
    {
        if (!built) BuildStatus();
        if (status == null) return;
        bool show = wanted.Stars > 0;
        if (status.gameObject.activeSelf != show) status.gameObject.SetActive(show);
        if (!show) return;
        bool searching = wanted.IsSearching;
        if (wanted.Seen)
        {
            statusText.text = "SEEN";
            statusText.color = SeenRed;
            statusBar.transform.parent.gameObject.SetActive(false);
        }
        else if (!wanted.Hunting)
        {
            // a crime got reported: police are on their way to where it happened
            statusText.text = "ALERTED";
            statusText.color = SearchAmber;
            statusBar.transform.parent.gameObject.SetActive(false);
        }
        else if (!searching)
        {
            statusText.text = "SEEN";
            statusText.color = SeenRed;
            statusBar.transform.parent.gameObject.SetActive(false);
        }
        else
        {
            statusText.text = wanted.InSearchArea ? "SEARCHING" : "LOSING THEM";
            statusText.color = SearchAmber;
            statusBar.transform.parent.gameObject.SetActive(true);
            statusBar.fillAmount = wanted.EscapeProgress;
            statusBar.color = wanted.InSearchArea ? SearchAmber : new Color(0.45f, 0.95f, 0.55f);
        }
    }

    void Update()
    {
        WantedSystem wanted = WantedSystem.Instance;
        if (wanted == null) return;
        UpdateStatus(wanted);

        for (int i = 0; i < stars.Length; i++)
        {
            Color colour;

            if (wanted.Stars == 0)
                colour = Color.clear;                    // not wanted: hide the stars
            else if (i < wanted.Stars)
                colour = litColour;
            else
                colour = unlitColour;

            // Flash the lit stars while the police are searching (like GTA)
            if (i < wanted.Stars && wanted.IsSearching)
                colour.a = 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(Time.time * flashSpeed));

            stars[i].color = colour;
        }
    }
}