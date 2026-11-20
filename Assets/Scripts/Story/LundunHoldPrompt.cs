using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Phase 5 (gate B): the one HOLD button the mission props share (fuse boxes, recorders), in the same place and style
/// as the till robbery's: bottom centre, a bar filling along its bottom edge while it's held. Whatever James is standing
/// at offers it each frame (Offer); nobody offering = hidden. Also a small countdown line at the top of the screen
/// (Timer), for a power cut.
public class LundunHoldPrompt : MonoBehaviour
{
    static LundunHoldPrompt instance;

    private GameObject buttonGo, timerGo;
    private Image buttonImg, fill;
    private TextMeshProUGUI label, timerText;
    private HoldButton hold;
    private int offeredFrame = -10, timerFrame = -10;
    private object offeredBy;

    static LundunHoldPrompt Get()
    {
        if (instance == null)
        {
            GameObject g = new GameObject("LundunHoldPrompt");
            instance = g.AddComponent<LundunHoldPrompt>();
            instance.Build();
        }
        return instance;
    }

    /// Show the button this frame for 'owner', with this label and how far along the hold is (0–1).
    public static void Offer(object owner, string text, float progress, Color colour)
    {
        LundunHoldPrompt p = Get();
        p.offeredFrame = Time.frameCount;
        p.offeredBy = owner;
        if (p.label.text != text) p.label.text = text;
        p.fill.fillAmount = Mathf.Clamp01(progress);
        p.buttonImg.color = colour;
        if (!p.buttonGo.activeSelf) p.buttonGo.SetActive(true);
    }

    /// The button is being held, and it's this owner's button.
    public static bool Held(object owner)
    {
        return instance != null && instance.offeredBy == owner && instance.hold != null && instance.hold.IsHeld && instance.buttonGo.activeSelf;
    }

    /// Show the countdown line this frame.
    public static void Timer(string text)
    {
        LundunHoldPrompt p = Get();
        p.timerFrame = Time.frameCount;
        if (p.timerText.text != text) p.timerText.text = text;
        if (!p.timerGo.activeSelf) p.timerGo.SetActive(true);
    }

    void LateUpdate()
    {
        if (buttonGo.activeSelf && Time.frameCount - offeredFrame > 1) { buttonGo.SetActive(false); offeredBy = null; }
        if (timerGo.activeSelf && Time.frameCount - timerFrame > 1) timerGo.SetActive(false);
    }

    void Build()
    {
        GameObject cg = new GameObject("HoldPromptUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas cv = cg.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 62;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        buttonGo = new GameObject("HoldButton", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        RectTransform rt = (RectTransform)buttonGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 380f); rt.sizeDelta = new Vector2(540f, 120f);
        buttonImg = buttonGo.AddComponent<Image>();
        if (LundunHudArt.Pill != null) { buttonImg.sprite = LundunHudArt.Pill; buttonImg.type = Image.Type.Sliced; buttonImg.pixelsPerUnitMultiplier = 0.8f; }
        hold = buttonGo.AddComponent<HoldButton>();
        GameObject fg = new GameObject("Fill", typeof(RectTransform));
        fg.transform.SetParent(buttonGo.transform, false);
        RectTransform fr = (RectTransform)fg.transform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(1f, 0f); fr.pivot = new Vector2(0.5f, 0f);
        fr.sizeDelta = new Vector2(-40f, 12f); fr.anchoredPosition = new Vector2(0f, 6f);
        fill = fg.AddComponent<Image>(); fill.color = new Color(1f, 0.82f, 0.25f); fill.type = Image.Type.Filled;
        fill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = 0f; fill.raycastTarget = false;
        label = Text(buttonGo.transform, 34f);
        RectTransform tr = label.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(10f, 18f); tr.offsetMax = new Vector2(-10f, 0f);
        buttonGo.SetActive(false);

        timerGo = new GameObject("Timer", typeof(RectTransform));
        timerGo.transform.SetParent(cg.transform, false);
        RectTransform tm = (RectTransform)timerGo.transform;
        tm.anchorMin = tm.anchorMax = new Vector2(0.5f, 1f); tm.pivot = new Vector2(0.5f, 1f);
        tm.anchoredPosition = new Vector2(0f, -262f); tm.sizeDelta = new Vector2(460f, 58f);
        Image tb = timerGo.AddComponent<Image>();
        if (LundunHudArt.Pill != null) { tb.sprite = LundunHudArt.Pill; tb.type = Image.Type.Sliced; tb.pixelsPerUnitMultiplier = 0.8f; }
        tb.color = new Color(0.04f, 0.06f, 0.11f, 0.8f); tb.raycastTarget = false;
        timerText = Text(timerGo.transform, 30f);
        RectTransform ttr = timerText.rectTransform; ttr.anchorMin = Vector2.zero; ttr.anchorMax = Vector2.one; ttr.offsetMin = ttr.offsetMax = Vector2.zero;
        timerGo.SetActive(false);
    }

    static TextMeshProUGUI Text(Transform parent, float size)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.color = Color.white;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
