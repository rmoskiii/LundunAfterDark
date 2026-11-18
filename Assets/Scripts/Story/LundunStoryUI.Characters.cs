using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// Phase 2: CHOOSE YOUR CHARACTER, before a new story starts. Phase 3: before Free Roam too (every time you pick it, with
/// your last choice already picked), and an outfit to go with it.
///   left    the character in 3D, standing in his idle, turning slowly; drag to turn him yourself. Under him his name
///           and a row of outfit swatches: tap one and he's wearing it
///   right   a card for each character (James), then greyed figures marked COMING SOON for the ones still to come
///   bottom  START STORY (wipes the old story, if there is one, and starts with this character and outfit) or, for Free
///           Roam, PLAY (keeps them for Free Roam only); BACK goes to the main menu with nothing wiped or saved
/// Story's choice lives in the story save, Free Roam's in its own settings (LundunCharacters): neither touches the other.
/// The 3D preview is a copy of the character's model on a little lit stage far below the city, filmed by its own
/// camera into a picture on the screen. It all goes away when the screen closes.
public partial class LundunStoryUI
{
    static readonly Color Grey = new Color(1f, 1f, 1f, 0.16f);

    private GameObject select;
    private LundunGame.Mode selectMode;
    private LundunCharacters.Def picked;
    private string pickedOutfit;
    private TextMeshProUGUI pickedName, pickedLine, selectNote;
    private readonly List<Image> cardRims = new List<Image>();
    private readonly List<KeyValuePair<string, Image>> swatchRings = new List<KeyValuePair<string, Image>>();
    private CharacterStage stage;

    void OpenCharacterSelect(LundunGame.Mode mode)
    {
        if (menuCanvas == null) return;
        if (confirm != null) confirm.SetActive(false);
        if (select != null) Destroy(select);
        LundunHaptics.Light();
        selectMode = mode;
        bool story = mode == LundunGame.Mode.Story;

        RectTransform root = (RectTransform)menuCanvas.transform;
        RectTransform box = Stretch(Rect("CharacterSelect", root));
        select = box.gameObject;
        box.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.015f, 0.04f, 0.97f);   // over the main menu, takes the touches

        TextMeshProUGUI title = Label(box, "CHOOSE YOUR CHARACTER", 60f, Color.white, TextAlignmentOptions.Center);
        title.characterSpacing = 12f;
        Place(title.rectTransform, new Vector2(0f, 425f), new Vector2(1600f, 76f));
        TextMeshProUGUI sub = Label(box, story ? "STORY MODE" : "FREE ROAM", 26f, Dim, TextAlignmentOptions.Center);
        sub.characterSpacing = 12f;
        Place(sub.rectTransform, new Vector2(0f, 370f), new Vector2(1400f, 40f));

        // who's picked to begin with: a new story starts as James in Midnight; Free Roam as you left it
        List<LundunCharacters.Def> chars = LundunCharacters.Selectable;
        picked = chars[0];
        pickedOutfit = LundunOutfits.DefaultId;
        if (!story)
        {
            LundunCharacters.Def saved = LundunCharacters.SavedCharacter(mode);
            if (chars.Contains(saved)) picked = saved;
            pickedOutfit = LundunCharacters.SavedOutfit(mode).id;
        }

        // left: the 3D preview in a panel, his name and outfits under it
        RectTransform panel = Rect("Preview", box);
        Place(panel, new Vector2(-320f, -42f), new Vector2(600f, 700f));   // (fits a wide phone: about ±490 tall)
        Image pi = panel.gameObject.AddComponent<Image>();
        pi.sprite = LundunHudArt.Panel; pi.type = Image.Type.Sliced; pi.pixelsPerUnitMultiplier = 0.35f; pi.color = Ink;
        RectTransform view = Rect("View", panel);
        view.anchorMin = new Vector2(0f, 0.31f); view.anchorMax = new Vector2(1f, 1f);
        view.offsetMin = new Vector2(12f, 0f); view.offsetMax = new Vector2(-12f, -12f);
        RawImage pic = view.gameObject.AddComponent<RawImage>();
        pic.color = new Color(1f, 1f, 1f, 0f);                          // shows once there's a picture
        TextMeshProUGUI hint = Label(view, "DRAG TO TURN", 22f, new Color(Dim.r, Dim.g, Dim.b, 0.8f), TextAlignmentOptions.Center);
        hint.characterSpacing = 10f;
        RectTransform hr = hint.rectTransform; hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(1f, 0f);
        hr.pivot = new Vector2(0.5f, 0f); hr.anchoredPosition = new Vector2(0f, 6f); hr.sizeDelta = new Vector2(0f, 34f);
        pickedName = Label(panel, "", 48f, Color.white, TextAlignmentOptions.Center);
        pickedName.characterSpacing = 14f;
        RectTransform nr = pickedName.rectTransform; nr.anchorMin = new Vector2(0f, 0.225f); nr.anchorMax = new Vector2(1f, 0.31f); nr.offsetMin = nr.offsetMax = Vector2.zero;
        pickedLine = Label(panel, "", 23f, Dim, TextAlignmentOptions.Center);
        RectTransform lr = pickedLine.rectTransform; lr.anchorMin = new Vector2(0f, 0.165f); lr.anchorMax = new Vector2(1f, 0.225f); lr.offsetMin = lr.offsetMax = Vector2.zero;
        Swatches(panel, new Vector2(0.5f, 0.09f), id => ChooseOutfit(id));

        stage = new GameObject("CharacterStage").AddComponent<CharacterStage>();
        pic.gameObject.AddComponent<CharacterTurn>().stage = stage;
        stage.Show(picked, pic);
        stage.Dress(pickedOutfit);

        // right: the cards, two by two
        cardRims.Clear();
        Vector2[] at = { new Vector2(210f, 140f), new Vector2(520f, 140f), new Vector2(210f, -170f), new Vector2(520f, -170f) };
        int slot = 0;
        foreach (LundunCharacters.Def d in chars)
        {
            if (slot >= at.Length) break;
            LundunCharacters.Def pick = d;
            Card(box, at[slot++], d.name, "", true, () => Choose(pick));
        }
        for (int i = 0; i < LundunCharacters.ComingSoon && slot < at.Length; i++)
            Card(box, at[slot++], "COMING SOON", "", false, () =>
            {
                LundunHaptics.Light();
                if (selectNote != null) selectNote.text = "More characters arrive in future updates.";
            });

        selectNote = Label(box, "", 24f, Dim, TextAlignmentOptions.Center);
        Place(selectNote.rectTransform, new Vector2(365f, -340f), new Vector2(700f, 34f));

        Button(box, "BACK", new Vector2(210f, -410f), new Vector2(290f, 92f), new Color(1f, 1f, 1f, 0.12f), CloseCharacterSelect);
        Button(box, story ? "START STORY" : "PLAY", new Vector2(520f, -410f), new Vector2(290f, 92f), Accent, StartWithCharacter);

        Choose(picked, false);
        ChooseOutfit(pickedOutfit, false);
    }

    /// The outfits this character has, as a row of colour swatches (tap one to wear it). Shared with the wardrobe.
    /// Returns how many there are.
    int Swatches(RectTransform parent, Vector2 anchor, System.Action<string> tap)
    {
        swatchRings.Clear();
        List<LundunOutfits.Outfit> list = Wearable();
        float gap = 96f, x0 = -(list.Count - 1) * gap * 0.5f;
        for (int i = 0; i < list.Count; i++)
        {
            LundunOutfits.Outfit o = list[i];
            RectTransform r = Rect("Outfit_" + o.id, parent);
            r.anchorMin = r.anchorMax = anchor;
            r.anchoredPosition = new Vector2(x0 + i * gap, 0f);
            r.sizeDelta = new Vector2(84f, 84f);
            Image hit = r.gameObject.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f);   // the whole square takes the tap
            Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = hit;
            string id = o.id;
            b.onClick.AddListener(() => tap(id));
            r.gameObject.AddComponent<LundunPressFX>();
            Image edge = Rect("Edge", r).gameObject.AddComponent<Image>();                       // a faint ring, so dark ones show
            edge.sprite = LundunHudArt.Ring; edge.color = new Color(1f, 1f, 1f, 0.28f); edge.raycastTarget = false;
            Place(edge.rectTransform, Vector2.zero, new Vector2(64f, 64f));
            Image disc = Rect("Swatch", r).gameObject.AddComponent<Image>();
            disc.sprite = LundunHudArt.Disc; disc.color = o.swatch; disc.raycastTarget = false;
            Place(disc.rectTransform, Vector2.zero, new Vector2(58f, 58f));
            Image ring = Rect("Picked", r).gameObject.AddComponent<Image>();
            ring.sprite = LundunHudArt.RingThick; ring.color = Accent; ring.raycastTarget = false; ring.enabled = false;
            Place(ring.rectTransform, Vector2.zero, new Vector2(84f, 84f));
            swatchRings.Add(new KeyValuePair<string, Image>(id, ring));
        }
        return list.Count;
    }

    /// The outfits the player can actually wear (all of them once Lundun → 35 has been run; Midnight before that).
    static List<LundunOutfits.Outfit> Wearable()
    {
        List<LundunOutfits.Outfit> list = new List<LundunOutfits.Outfit>();
        LundunOutfitSet set = LundunOutfitSet.OnPlayer;
        foreach (LundunOutfits.Outfit o in LundunOutfits.All) if (o.id == LundunOutfits.DefaultId || (set != null && set.Has(o.id))) list.Add(o);
        return list;
    }

    void ShowPickedSwatch(string id)
    {
        foreach (KeyValuePair<string, Image> kv in swatchRings) if (kv.Value != null) kv.Value.enabled = kv.Key == id;
    }

    void ChooseOutfit(string id, bool tap = true)
    {
        LundunOutfits.Outfit o = LundunOutfits.Find(id) ?? LundunOutfits.Default;
        if (tap) LundunHaptics.Light();
        pickedOutfit = o.id;
        if (pickedLine != null) pickedLine.text = o.name + "  ·  " + o.note;
        ShowPickedSwatch(o.id);
        if (stage != null && tap) stage.Dress(o.id);
    }

    /// A character card (or a greyed "coming soon" one): a figure and its name; amber when it's the one picked.
    void Card(RectTransform parent, Vector2 at, string name, string note, bool real, System.Action click)
    {
        RectTransform r = Rect("Card_" + name, parent);
        Place(r, at, new Vector2(290f, 290f));
        Image bg = r.gameObject.AddComponent<Image>();
        bg.sprite = LundunHudArt.Panel; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 0.35f;
        bg.color = real ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.04f);
        Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = bg; b.onClick.AddListener(() => click());
        r.gameObject.AddComponent<LundunPressFX>();

        if (real)
        {
            Image rim = Rect("Rim", r).gameObject.AddComponent<Image>();   // the picked one: amber edge, warm fill
            Stretch(rim.rectTransform);
            rim.sprite = LundunHudArt.PanelRim; rim.type = Image.Type.Sliced; rim.pixelsPerUnitMultiplier = 0.35f;
            rim.color = Accent; rim.raycastTarget = false; rim.enabled = false;
            cardRims.Add(bg);
        }

        Image fig = Rect("Figure", r).gameObject.AddComponent<Image>();
        fig.sprite = LundunHudArt.Person; fig.preserveAspect = true; fig.raycastTarget = false;
        fig.color = real ? new Color(1f, 1f, 1f, 0.92f) : Grey;
        RectTransform fr = fig.rectTransform; fr.anchorMin = new Vector2(0.1f, 0.24f); fr.anchorMax = new Vector2(0.9f, 0.95f); fr.offsetMin = fr.offsetMax = Vector2.zero;
        if (!real)
        {
            Image lk = Rect("Lock", r).gameObject.AddComponent<Image>();
            lk.sprite = LundunHudArt.Lock; lk.preserveAspect = true; lk.raycastTarget = false; lk.color = new Color(1f, 1f, 1f, 0.45f);
            Place(lk.rectTransform, new Vector2(0f, 24f), new Vector2(64f, 64f));
        }
        TextMeshProUGUI t = Label(r, name, real ? 34f : 24f, real ? Color.white : new Color(1f, 1f, 1f, 0.4f), TextAlignmentOptions.Center);
        t.characterSpacing = 10f;
        RectTransform tr = t.rectTransform; tr.anchorMin = new Vector2(0f, 0.05f); tr.anchorMax = new Vector2(1f, 0.22f); tr.offsetMin = tr.offsetMax = Vector2.zero;
    }

    void Choose(LundunCharacters.Def d, bool tap = true)
    {
        if (d == null) return;
        if (tap) LundunHaptics.Light();
        picked = d;
        if (pickedName != null) pickedName.text = d.name;
        List<LundunCharacters.Def> chars = LundunCharacters.Selectable;
        for (int i = 0; i < cardRims.Count; i++)
        {
            Image bg = cardRims[i];
            if (bg == null) continue;
            bool on = i < chars.Count && chars[i] == d;
            bg.color = on ? new Color(Accent.r, Accent.g, Accent.b, 0.2f) : new Color(1f, 1f, 1f, 0.08f);
            Transform rim = bg.transform.Find("Rim");
            if (rim != null) rim.GetComponent<Image>().enabled = on;
        }
        if (stage != null && tap) { stage.Show(d, null); stage.Dress(pickedOutfit); }
    }

    void StartWithCharacter()
    {
        string who = picked != null ? picked.id : LundunCharacters.DefaultId;
        string what = pickedOutfit ?? LundunOutfits.DefaultId;
        LundunGame.Mode mode = selectMode;
        CloseCharacterSelect(false);
        if (mode == LundunGame.Mode.Story)
        {
            LundunCharacters.Pending = who;
            LundunCharacters.PendingOutfit = what;
            Pick(LundunGame.Mode.Story, true);             // wipes the old story and starts the new one with these
            return;
        }
        LundunCharacters.SaveFreeRoam(who, what);          // Free Roam's own settings (never the story)
        if (LundunGame.Current == LundunGame.Mode.FreeRoam) LundunCharacters.Apply();   // no reload in the same mode: wear it now
        Pick(LundunGame.Mode.FreeRoam, false);
    }

    void CloseCharacterSelect() { CloseCharacterSelect(true); }

    void CloseCharacterSelect(bool tap)
    {
        if (tap) LundunHaptics.Light();
        if (stage != null) Destroy(stage.gameObject);
        stage = null;
        if (select != null) Destroy(select);
        select = null;
        cardRims.Clear();
        swatchRings.Clear();
    }

    // ------------------------------------------------------------------ the 3D preview
    /// Drag on the picture to turn the character.
    class CharacterTurn : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public CharacterStage stage;
        public void OnBeginDrag(PointerEventData e) { if (stage != null) stage.Grab(true); }
        public void OnDrag(PointerEventData e) { if (stage != null) stage.Turn(e.delta.x); }
        public void OnEndDrag(PointerEventData e) { if (stage != null) stage.Grab(false); }
    }

    /// The little stage: the model, three lights and a camera into a picture. Runs while the game is paused (the
    /// menu stops time), so everything uses unscaled time.
    class CharacterStage : MonoBehaviour
    {
        static readonly Vector3 Spot = new Vector3(0f, -400f, 0f);     // under the city, out of every other camera's way
        const int W = 864, H = 706;          // (the shape of the picture area)

        private RawImage pic;
        private RenderTexture rt;
        private Camera cam;
        private Transform model;
        private int layer = -1;
        private Camera main;
        private int mainMaskWas;
        private float yaw = 20f, spin, lastTouch = -10f;
        private bool held;

        public void Grab(bool on) { held = on; lastTouch = Time.unscaledTime; if (on) spin = 0f; }
        public void Turn(float dx)
        {
            float d = -dx * 0.35f;
            yaw += d;
            spin = d / Mathf.Max(Time.unscaledDeltaTime, 0.008f);
            lastTouch = Time.unscaledTime;
        }

        /// Put an outfit on the preview copy.
        public void Dress(string outfitId)
        {
            LundunOutfitSet set = LundunOutfitSet.OnPlayer;
            if (set != null && model != null) set.WearOn(model.gameObject, outfitId);
        }

        public void Show(LundunCharacters.Def d, RawImage into)
        {
            if (into != null) pic = into;
            try
            {
                if (cam == null) Build();
                if (model != null) Destroy(model.gameObject);
                model = Copy(d);
                if (model == null) { Debug.LogWarning("[Lundun] character select: no model to show for " + d.id); return; }
                Frame();
                if (pic != null) { pic.texture = rt; pic.color = Color.white; }
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void Build()
        {
            transform.position = Spot;
            layer = PickLayer();

            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "CharacterPreview", antiAliasing = 2 };
            GameObject cg = new GameObject("PreviewCamera");
            cg.transform.SetParent(transform, false);
            cam = cg.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 0f);   // see-through onto the panel (or the panel's own navy)
            cam.cullingMask = layer >= 0 ? 1 << layer : 0;
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 9f;                                       // (under 10 m: the height fog leaves it alone)
            cam.depth = -30f;
            cam.allowHDR = false;
            cam.targetTexture = rt;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresDepthTexture = false;
                data.requiresColorTexture = false;
            }

            // warm key from the front right, cool fill from the left, amber rim from behind (sodium street light)
            Lamp("Key", new Vector3(1.3f, 2.1f, 2.2f), new Color(1f, 0.9f, 0.78f), 3.2f);
            Lamp("Fill", new Vector3(-1.8f, 1.3f, 1.6f), new Color(0.55f, 0.66f, 1f), 1.4f);
            Lamp("Rim", new Vector3(0.4f, 2.3f, -1.8f), new Color(1f, 0.66f, 0.25f), 3.4f);

            // the main camera never sees the stage
            main = Camera.main;
            if (main != null && layer >= 0) { mainMaskWas = main.cullingMask; main.cullingMask &= ~(1 << layer); }
        }

        void Lamp(string name, Vector3 at, Color c, float intensity)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(transform, false);
            g.transform.localPosition = at;
            Light l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c; l.intensity = intensity; l.range = 7f;
            l.shadows = LightShadows.None;
            if (layer >= 0) l.cullingMask = 1 << layer;
        }

        /// A free layer for the stage (not the first-person one, which can be on while this is open).
        static int PickLayer()
        {
            int named = LayerMask.NameToLayer("CharacterPreview");
            if (named >= 0) return named;
            int vm = LundunViewModel.Layer;
            for (int i = 30; i >= 8; i--) if (i != vm && string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return vm;
        }

        /// A still copy of the character: just the skeleton, the skinned meshes and the animator, in his idle.
        Transform Copy(LundunCharacters.Def d)
        {
            GameObject source = null;
            if (d.IsSceneJames)
            {
                PlayerController pc = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
                Animator a = pc != null ? pc.GetComponentInChildren<Animator>(true) : null;
                if (a != null) source = a.gameObject;
            }
            else source = Resources.Load<GameObject>(d.modelResource);
            if (source == null) return null;

            // made under a switched-off holder, so none of the copy's scripts wake up before they're taken off
            GameObject hold = new GameObject("Hold");
            hold.SetActive(false);
            hold.transform.SetParent(transform, false);
            GameObject g = Instantiate(source, hold.transform);
            g.name = "Preview_" + d.id;
            Strip(g);
            g.transform.SetParent(transform, false);
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Destroy(hold);
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer >= 0 ? layer : t.gameObject.layer;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                r.shadowCastingMode = ShadowCastingMode.Off;     // (first person makes his body shadow-only: not here)
                r.receiveShadows = false;
            }
            Animator anim = g.GetComponent<Animator>();
            if (anim == null) anim = g.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                Animator from = source.GetComponent<Animator>();
                if (from == null) from = source.GetComponentInChildren<Animator>(true);
                if (anim.runtimeAnimatorController == null && from != null) anim.runtimeAnimatorController = from.runtimeAnimatorController;
                if (anim.runtimeAnimatorController == null)
                {
                    PlayerController pc = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
                    Animator pa = pc != null ? pc.GetComponentInChildren<Animator>(true) : null;
                    if (pa != null) anim.runtimeAnimatorController = pa.runtimeAnimatorController;   // the shared animations
                }
                anim.applyRootMotion = false;
                anim.updateMode = AnimatorUpdateMode.UnscaledTime;    // the menu has stopped time
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.enabled = true;
            }
            return g.transform;
        }

        /// Off with everything but the body: guns in his hands, first-person arms, scripts, colliders, lights.
        static void Strip(GameObject g)
        {
            List<GameObject> drop = new List<GameObject>();
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
            {
                if (t == g.transform) continue;
                if (t.name.EndsWith("_FPArms")) { drop.Add(t.gameObject); continue; }
                Renderer r = t.GetComponent<Renderer>();
                if (r != null && !(r is SkinnedMeshRenderer)) drop.Add(t.gameObject);   // a gun, a muzzle flash…
            }
            foreach (GameObject o in drop) if (o != null) DestroyImmediate(o);

            // scripts first, then everything else that isn't the body (some parts need others gone first)
            for (int pass = 0; pass < 3; pass++)
                foreach (Component c in g.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is Animator || c is SkinnedMeshRenderer) continue;
                    if (pass == 0 && !(c is MonoBehaviour)) continue;
                    try { DestroyImmediate(c); } catch { }
                }
        }

        /// Stand him in the middle of the picture, head to toe with a little room.
        void Frame()
        {
            Bounds b = new Bounds(model.position + Vector3.up * 0.9f, Vector3.one * 0.1f);
            bool any = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float h = any ? Mathf.Clamp(b.size.y, 1.2f, 2.4f) : 1.8f;
            float dist = h * 0.56f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 aim = transform.position + Vector3.up * (h * 0.5f);
            cam.transform.position = aim + new Vector3(0f, h * 0.06f, dist);
            cam.transform.LookAt(aim);
        }

        void Update()
        {
            if (model == null) return;
            float dt = Time.unscaledDeltaTime;
            if (!held)
            {
                spin = Mathf.Lerp(spin, 0f, 1f - Mathf.Exp(-4f * dt));           // a flick carries on, then settles
                yaw += spin * dt;
                if (Time.unscaledTime - lastTouch > 2.5f) yaw += 14f * dt;        // left alone: a slow turn
            }
            model.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void OnDestroy()
        {
            if (main != null && layer >= 0) main.cullingMask = mainMaskWas;
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (pic != null) pic.texture = null;
        }
    }
}
