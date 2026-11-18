using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Phase 3: the WARDROBE. Stand at any safehouse's front door (on foot) and a WARDROBE button comes up; tap it and a
/// strip of outfits opens along the bottom of the screen, with James still in view above it: tap one and he's wearing
/// it, there and then. Saved for the mode you're playing (Story in the story save, Free Roam in its own settings), so
/// the two never change each other's.
///   not while   on a mission, a mission's card is up, the police are after you (it says so), driving or riding
/// Added by LundunStoryUI in both modes. When James's home gets an interior, the wardrobe moves inside it.
public class LundunWardrobe : MonoBehaviour
{
    const float Reach = 2.4f;          // this close to a safehouse door
    const float Leave = 4f;            // walk further than this and the strip closes

    static readonly Color Blue = new Color(0.12f, 0.16f, 0.29f, 0.92f);
    static readonly Color Grey = new Color(0.2f, 0.2f, 0.22f, 0.92f);
    static readonly Color Accent = new Color(1f, 0.66f, 0.18f, 1f);

    private GameObject buttonGo, stripGo;
    private Image back;
    private TextMeshProUGUI label, stripNote;
    private readonly List<KeyValuePair<string, Image>> rings = new List<KeyValuePair<string, Image>>();
    private Vector3 openedAt;
    private bool wantedNow;
    private float nextCheck;
    private VehicleSystem vehicles;

    void Start() { BuildUI(); }

    void Update()
    {
        if (buttonGo == null || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.2f;

        bool open = stripGo.activeSelf;
        Vector3 door;
        bool near = AtDoor(out door);
        if (open)
        {
            PlayerHealth ph = PlayerHealth.Instance;
            bool moved = ph == null || ph.IsDead || LundunMissionRun.FlatDistance(ph.Body.position, openedAt) > Leave;
            if (moved || Blocked() || Wanted()) Close();
            buttonGo.SetActive(false);
            return;
        }
        if (!near || Blocked()) { buttonGo.SetActive(false); return; }
        wantedNow = Wanted();
        label.text = wantedNow ? "WARDROBE\n<size=65%>Lose the police first</size>" : "WARDROBE\n<size=65%>Change outfit</size>";
        back.color = wantedNow ? Grey : Blue;
        buttonGo.SetActive(true);
    }

    /// On foot at a safehouse's front door.
    bool AtDoor(out Vector3 door)
    {
        door = Vector3.zero;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot || VehicleSystem.Locked) return false;
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        if (vehicles != null && vehicles.IsDriving) return false;
        if (LundunBikes.Instance != null && LundunBikes.Instance.Riding) return false;
        LundunPlaces places = LundunPlaces.Instance;
        if (places == null || places.Places == null) return false;
        Vector3 j = ph.Body.position;
        foreach (LundunPlaces.Place p in places.Places)
        {
            if (p == null || p.kind != "safehouse") continue;
            if (LundunMissionRun.FlatDistance(p.door, j) < Reach && Mathf.Abs(p.door.y - j.y) < 2.5f) { door = p.door; return true; }
        }
        return false;
    }

    /// Story's missions come first: no wardrobe on a mission or with a mission's card up.
    static bool Blocked()
    {
        if (!LundunGame.IsStory) return false;
        return LundunMissions.Running || LundunMissions.CardOpen;
    }

    static bool Wanted() { WantedSystem w = WantedSystem.Instance; return w != null && w.Stars > 0; }

    void OnPress()
    {
        LundunHaptics.Light();
        if (wantedNow) { nextCheck = Time.time + 0.5f; return; }
        Open();
    }

    void Open()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        openedAt = ph != null && ph.Body != null ? ph.Body.position : Vector3.zero;
        Fill();
        stripGo.SetActive(true);
        buttonGo.SetActive(false);
    }

    void Close()
    {
        if (stripGo != null) stripGo.SetActive(false);
        nextCheck = Time.time + 0.5f;
    }

    /// The outfits the player has (all of them once Lundun → 35 has been run; Midnight before that).
    void Fill()
    {
        Transform row = stripGo.transform.Find("Row");
        for (int i = row.childCount - 1; i >= 0; i--) Destroy(row.GetChild(i).gameObject);
        rings.Clear();
        LundunOutfitSet set = LundunOutfitSet.OnPlayer;
        List<LundunOutfits.Outfit> list = new List<LundunOutfits.Outfit>();
        foreach (LundunOutfits.Outfit o in LundunOutfits.All) if (o.id == LundunOutfits.DefaultId || (set != null && set.Has(o.id))) list.Add(o);
        string wearing = set != null && !string.IsNullOrEmpty(set.Wearing) ? set.Wearing : LundunCharacters.SavedOutfit(LundunGame.Current).id;

        float gap = 150f, x0 = -(list.Count - 1) * gap * 0.5f;
        for (int i = 0; i < list.Count; i++)
        {
            LundunOutfits.Outfit o = list[i];
            RectTransform r = Rt("Outfit_" + o.id, row);
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(x0 + i * gap, 14f);
            r.sizeDelta = new Vector2(136f, 150f);
            Image hit = r.gameObject.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f);
            Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = hit;
            string id = o.id;
            b.onClick.AddListener(() => Wear(id));
            r.gameObject.AddComponent<LundunPressFX>();
            Image edge = Img("Edge", r, LundunHudArt.Ring, new Color(1f, 1f, 1f, 0.3f), new Vector2(0f, 22f), new Vector2(80f, 80f));
            Img("Swatch", r, LundunHudArt.Disc, o.swatch, new Vector2(0f, 22f), new Vector2(72f, 72f));
            Image ring = Img("Picked", r, LundunHudArt.RingThick, Accent, new Vector2(0f, 22f), new Vector2(104f, 104f));
            ring.enabled = o.id == wearing;
            rings.Add(new KeyValuePair<string, Image>(o.id, ring));
            TextMeshProUGUI t = Text(r, o.name, 22f, Color.white);
            RectTransform tr = t.rectTransform; tr.anchorMin = new Vector2(0f, 0f); tr.anchorMax = new Vector2(1f, 0f);
            tr.pivot = new Vector2(0.5f, 0f); tr.anchoredPosition = new Vector2(0f, -6f); tr.sizeDelta = new Vector2(20f, 32f);
            edge.raycastTarget = false;
        }
        LundunOutfits.Outfit now = LundunOutfits.Find(wearing) ?? LundunOutfits.Default;
        stripNote.text = now.name + "  ·  " + now.note;
    }

    void Wear(string id)
    {
        LundunHaptics.Light();
        LundunCharacters.ChangeOutfit(id);
        foreach (KeyValuePair<string, Image> kv in rings) if (kv.Value != null) kv.Value.enabled = kv.Key == id;
        LundunOutfits.Outfit o = LundunOutfits.Find(id) ?? LundunOutfits.Default;
        stripNote.text = o.name + "  ·  " + o.note;
    }

    // ------------------------------------------------------------------ UI (the same pill and panel as the other prompts)
    void BuildUI()
    {
        GameObject cg = new GameObject("WardrobeUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 62;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        buttonGo = new GameObject("WardrobePrompt", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        back = buttonGo.AddComponent<Image>();
        Pill(back, 0.8f); back.color = Blue;
        Button b = buttonGo.AddComponent<Button>(); b.targetGraphic = back; b.onClick.AddListener(OnPress);
        buttonGo.AddComponent<LundunPressFX>();
        RectTransform rt = (RectTransform)buttonGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 230f); rt.sizeDelta = new Vector2(460f, 120f);
        label = Text(rt, "WARDROBE", 38f, Color.white);
        buttonGo.SetActive(false);

        // the strip: along the bottom, so James stays in view above it
        stripGo = new GameObject("WardrobeStrip", typeof(RectTransform));
        stripGo.transform.SetParent(cg.transform, false);
        Image si = stripGo.AddComponent<Image>();
        si.sprite = LundunHudArt.Panel; si.type = Image.Type.Sliced; si.pixelsPerUnitMultiplier = 0.35f;
        si.color = new Color(0.04f, 0.06f, 0.11f, 0.9f);
        RectTransform sr = (RectTransform)stripGo.transform;
        sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0f); sr.pivot = new Vector2(0.5f, 0f);
        sr.anchoredPosition = new Vector2(0f, 40f); sr.sizeDelta = new Vector2(1000f, 330f);
        TextMeshProUGUI title = Text(sr, "WARDROBE", 34f, Color.white);
        title.characterSpacing = 10f;
        Pin(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(600f, 44f));
        stripNote = Text(sr, "", 22f, new Color(1f, 1f, 1f, 0.6f));
        stripNote.fontStyle = FontStyles.Normal;
        Pin(stripNote.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(900f, 30f));
        RectTransform row = Rt("Row", sr);
        row.anchorMin = new Vector2(0f, 0f); row.anchorMax = new Vector2(1f, 0f); row.pivot = new Vector2(0.5f, 0f);
        row.anchoredPosition = new Vector2(0f, 20f); row.sizeDelta = new Vector2(0f, 200f);
        // DONE, top right of the strip
        RectTransform done = Rt("Done", sr);
        done.anchorMin = done.anchorMax = new Vector2(1f, 1f); done.pivot = new Vector2(1f, 1f);
        done.anchoredPosition = new Vector2(-16f, -14f); done.sizeDelta = new Vector2(150f, 60f);
        Image di = done.gameObject.AddComponent<Image>(); Pill(di, 0.94f); di.color = Blue;
        Button db = done.gameObject.AddComponent<Button>(); db.targetGraphic = di;
        db.onClick.AddListener(() => { LundunHaptics.Light(); Close(); });
        done.gameObject.AddComponent<LundunPressFX>();
        Text(done, "DONE", 26f, Color.white);
        stripGo.SetActive(false);
    }

    static RectTransform Rt(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static Image Img(string name, RectTransform parent, Sprite s, Color c, Vector2 at, Vector2 size)
    {
        RectTransform r = Rt(name, parent);
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = at; r.sizeDelta = size;
        Image im = r.gameObject.AddComponent<Image>();
        im.sprite = s; im.color = c; im.raycastTarget = false;
        return im;
    }

    static TextMeshProUGUI Text(RectTransform parent, string s, float size, Color c)
    {
        RectTransform r = Rt("Text", parent);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = s; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static void Pill(Image im, float ppu)
    {
        if (LundunHudArt.Pill == null) return;
        im.sprite = LundunHudArt.Pill; im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = ppu;
    }

    static void Pin(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(0.5f, anchor.y);
        r.anchoredPosition = pos; r.sizeDelta = size;
    }
}
