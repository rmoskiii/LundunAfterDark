using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// S0: the start menu, the money on the HUD (Story only) and the story's autosave. Added by LundunGame to the city.
///   start menu  when the game launches: CONTINUE STORY (with your money and missions done), NEW STORY (asks first if
///               there's a story to lose) and FREE ROAM (the game as it's always been). The one you played last is lit
///   money       top right, under the weapon panel: "£12,450", with "+£500" in green or "−£100 Hospital bill" in red underneath for a moment
///               (editor and development builds: tap the money three times for £500 of test cash)
///   autosave    every 30 seconds, when you leave the app, and before switching modes
[DefaultExecutionOrder(310)]
public class LundunStoryUI : MonoBehaviour
{
    static readonly Color Ink = new Color(0.05f, 0.07f, 0.12f, 0.96f);
    static readonly Color Accent = new Color(1f, 0.66f, 0.18f);
    static readonly Color Dim = new Color(0.62f, 0.66f, 0.74f);
    static readonly Color Gain = new Color(0.35f, 0.95f, 0.45f), Loss = new Color(1f, 0.38f, 0.32f);

    private GameObject menuCanvas, confirm;
    private TextMeshProUGUI money, popup;
    private float popupUntil, nextSave, timeScaleBefore = 1f;
    private bool menuOpen;

    void OnEnable() { LundunWallet.Changed += OnMoney; }
    void OnDisable() { LundunWallet.Changed -= OnMoney; }

    IEnumerator Start()
    {
        yield return null;                                  // the HUD (and its font) builds itself first
        if (LundunGame.IsStory) { BuildMoney(); if (GetComponent<LundunCrew>() == null) gameObject.AddComponent<LundunCrew>(); if (GetComponent<LundunMissions>() == null) gameObject.AddComponent<LundunMissions>(); }   // S1 missions, S2 the crew
        if (LundunGame.ShowStartMenu) OpenMenu();
    }

    void Update()
    {
        if (popup != null && popup.gameObject.activeSelf)
        {
            float left = popupUntil - Time.unscaledTime;
            if (left <= 0f) popup.gameObject.SetActive(false);
            else { Color c = popup.color; c.a = Mathf.Clamp01(left / 0.6f); popup.color = c; }
        }
        if (!LundunGame.IsStory || menuOpen) return;
        LundunStory.Data.playSeconds += Time.unscaledDeltaTime;
        if (Time.unscaledTime >= nextSave)
        {
            nextSave = Time.unscaledTime + 30f;
            LundunStory.Write();                           // small file; ammo and pickups change without marking it
        }
    }

    void OnApplicationPause(bool paused) { if (paused && LundunGame.IsStory) LundunStory.Write(); }
    void OnApplicationQuit() { if (LundunGame.IsStory) LundunStory.Write(); }

    // ------------------------------------------------------------------ money on the HUD
    void BuildMoney()
    {
        // top right, just under the weapon panel (LundunWeapons: 330 × 112 at 36, 26 from the corner), in the HUD itself
        // so it stays when the on-foot controls hide for driving
        GameObject onFoot = GameObject.Find("OnFootControls");
        RectTransform parent = onFoot != null && onFoot.transform.parent is RectTransform
            ? (RectTransform)onFoot.transform.parent : MakeCanvas("LundunMoney", 60).transform as RectTransform;
        money = Label(parent, "money", 38f, Color.white, TextAlignmentOptions.Right);
        popup = Label(parent, "moneyPopup", 26f, Gain, TextAlignmentOptions.Right);
        RectTransform mr = money.rectTransform, pr = popup.rectTransform;
        mr.anchorMin = mr.anchorMax = new Vector2(1f, 1f); mr.pivot = new Vector2(1f, 1f);
        mr.anchoredPosition = new Vector2(-40f, -146f); mr.sizeDelta = new Vector2(420f, 46f);
        pr.anchorMin = pr.anchorMax = new Vector2(1f, 1f); pr.pivot = new Vector2(1f, 1f);
        pr.anchoredPosition = new Vector2(-40f, -190f); pr.sizeDelta = new Vector2(560f, 36f);
        popup.gameObject.SetActive(false);
        money.text = LundunWallet.Format(LundunWallet.Cash);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        money.raycastTarget = true;
        money.gameObject.AddComponent<LundunTripleTap>().Tapped += () => LundunWallet.Earn(500, "Test cash");
#endif
    }

    void OnMoney(long delta, string why)
    {
        if (money != null) money.text = LundunWallet.Format(LundunWallet.Cash);
        if (popup == null) return;
        popup.text = (delta >= 0 ? "+" : "−") + LundunWallet.Format(delta) + (string.IsNullOrEmpty(why) ? "" : "  " + why);
        popup.color = delta >= 0 ? Gain : Loss;
        popup.gameObject.SetActive(true);
        popupUntil = Time.unscaledTime + 2.6f;
    }

    // ------------------------------------------------------------------ the start menu
    void OpenMenu()
    {
        menuOpen = true;
        timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        menuCanvas = MakeCanvas("LundunStartMenu", 95);
        RectTransform root = (RectTransform)menuCanvas.transform;
        Image dim = Stretch(Rect("Dim", root)).gameObject.AddComponent<Image>();
        dim.color = new Color(0.01f, 0.015f, 0.04f, 0.9f);            // blocks touches to the game underneath

        TextMeshProUGUI title = Label(root, "LUNDUN AFTER DARK", 86f, Color.white, TextAlignmentOptions.Center);
        title.characterSpacing = 14f;
        Place(title.rectTransform, new Vector2(0f, 330f), new Vector2(1600f, 110f));
        TextMeshProUGUI sub = Label(root, "MAIN MENU", 30f, Dim, TextAlignmentOptions.Center);
        sub.characterSpacing = 12f;
        Place(sub.rectTransform, new Vector2(0f, 250f), new Vector2(1400f, 50f));

        bool hasStory = LundunStory.Exists;
        LundunGame.Mode last = LundunGame.LastPlayed;
        float y = 120f;
        if (hasStory)
        {
            LundunStoryData d = LundunStory.Data;
            Choice(root, "CONTINUE STORY", LundunWallet.Format(d.money) + " · " + d.missionsDone + " of 12 missions completed", y, last == LundunGame.Mode.Story,
                   () => Pick(LundunGame.Mode.Story, false));
            y -= 150f;
        }
        Choice(root, "NEW STORY", "Start a new journey through Lundun", y,
               !hasStory && last == LundunGame.Mode.Story, () => { if (hasStory) Confirm(); else Pick(LundunGame.Mode.Story, true); });
        y -= 150f;
        Choice(root, "FREE ROAM", "Explore the city. No missions. No limits.", y, last == LundunGame.Mode.FreeRoam,
               () => Pick(LundunGame.Mode.FreeRoam, false));
    }

    void Confirm()
    {
        if (confirm != null) { confirm.SetActive(true); return; }
        RectTransform root = (RectTransform)menuCanvas.transform;
        RectTransform box = Rect("Confirm", root);
        confirm = box.gameObject;
        Stretch(box);
        box.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform panel = Rect("Panel", box);
        Place(panel, Vector2.zero, new Vector2(1000f, 460f));
        Image pi = panel.gameObject.AddComponent<Image>();
        pi.sprite = LundunHudArt.Panel; pi.type = Image.Type.Sliced; pi.pixelsPerUnitMultiplier = 0.35f; pi.color = Ink;
        LundunStoryData d = LundunStory.Data;
        TextMeshProUGUI q = Label(panel, "Start a new story?", 46f, Color.white, TextAlignmentOptions.Center);
        Place(q.rectTransform, new Vector2(0f, 140f), new Vector2(900f, 60f));
        TextMeshProUGUI w = Label(panel, "Your story progress (" + LundunWallet.Format(d.money) + ", " + d.missionsDone + " missions) will be wiped. Free Roam isn't affected.",
                                  27f, Dim, TextAlignmentOptions.Center);
        w.textWrappingMode = TextWrappingModes.Normal;
        Place(w.rectTransform, new Vector2(0f, 50f), new Vector2(860f, 100f));
        Button(panel, "START AGAIN", new Vector2(-220f, -130f), new Vector2(400f, 96f), Accent, () => Pick(LundunGame.Mode.Story, true));
        Button(panel, "BACK", new Vector2(220f, -130f), new Vector2(400f, 96f), new Color(1f, 1f, 1f, 0.12f), () => confirm.SetActive(false));
    }

    void Pick(LundunGame.Mode mode, bool newStory)
    {
        LundunHaptics.Light();
        bool same = mode == LundunGame.Current && !newStory;
        if (same)
        {
            // already loaded in this mode: just close the menu and play
            LundunGame.Play(mode, false);
            CloseMenu();
            return;
        }
        if (menuCanvas != null)
        {
            TextMeshProUGUI loading = Label((RectTransform)menuCanvas.transform, "LOADING " + (mode == LundunGame.Mode.Story ? "STORY" : "FREE ROAM") + "…", 40f, Accent, TextAlignmentOptions.Center);
            Place(loading.rectTransform, new Vector2(0f, -400f), new Vector2(900f, 60f));
            if (mode == LundunGame.Mode.FreeRoam)
            {
                TextMeshProUGUI tag = Label((RectTransform)menuCanvas.transform, "The whole city is yours. Explore, drive, fight, and cause chaos.", 26f, Dim, TextAlignmentOptions.Center);
                Place(tag.rectTransform, new Vector2(0f, -455f), new Vector2(1400f, 44f));
            }
        }
        StartCoroutine(PlayNextFrame(mode, newStory));
    }

    IEnumerator PlayNextFrame(LundunGame.Mode mode, bool newStory)
    {
        yield return null;                                   // let "LOADING…" show
        LundunGame.Play(mode, newStory);
    }

    void CloseMenu()
    {
        menuOpen = false;
        if (menuCanvas != null) Destroy(menuCanvas);
        Time.timeScale = timeScaleBefore;
    }

    // ------------------------------------------------------------------ helpers
    void Choice(RectTransform root, string title, string note, float y, bool lit, System.Action pick)
    {
        RectTransform b = Button(root, title, new Vector2(0f, y), new Vector2(860f, 120f), lit ? Accent : new Color(1f, 1f, 1f, 0.1f), pick);
        TextMeshProUGUI t = b.GetComponentInChildren<TextMeshProUGUI>();
        RectTransform tr = t.rectTransform; tr.anchorMin = new Vector2(0f, 0.42f); tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        TextMeshProUGUI n = Label(b, note, 24f, lit ? new Color(0.12f, 0.08f, 0.02f) : Dim, TextAlignmentOptions.Center);
        RectTransform nr = n.rectTransform; nr.anchorMin = Vector2.zero; nr.anchorMax = new Vector2(1f, 0.45f); nr.offsetMin = nr.offsetMax = Vector2.zero;
    }

    RectTransform Button(RectTransform parent, string text, Vector2 at, Vector2 size, Color colour, System.Action click)
    {
        RectTransform r = Rect("Button_" + text, parent);
        Place(r, at, size);
        Image img = r.gameObject.AddComponent<Image>();
        img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.6f; img.color = colour;
        Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(() => click());
        r.gameObject.AddComponent<LundunPressFX>();
        bool lit = colour.a > 0.5f;
        TextMeshProUGUI t = Label(r, text, 40f, lit ? new Color(0.08f, 0.05f, 0.02f) : Color.white, TextAlignmentOptions.Center);
        t.characterSpacing = 10f;
        Stretch(t.rectTransform);
        return r;
    }

    static GameObject MakeCanvas(string name, int order)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        Canvas c = g.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
        CanvasScaler sc = g.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        g.AddComponent<GraphicRaycaster>();
        return g;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static RectTransform Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; return r; }

    static void Place(RectTransform r, Vector2 at, Vector2 size)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = at; r.sizeDelta = size;
    }

    static TextMeshProUGUI Label(RectTransform parent, string text, float size, Color col, TextAlignmentOptions align)
    {
        RectTransform r = Rect("Text", parent);
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.color = col; t.alignment = align; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
