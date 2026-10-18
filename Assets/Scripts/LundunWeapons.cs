using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// G1: James's weapons — Fists, Pistol, SMG, Carbine, Shotgun. One fire button and one AIM button for all of them.
///   • Pistol  semi-auto: one shot per tap (rate-limited), accurate, ~3 body shots
///   • SMG     full-auto: fast and forgiving up close, spread grows the longer you hold — short bursts pay off
///   • Carbine full-auto: steadier, climbing recoil you can pull against, strong at range
///   • Shotgun pump: 8 pellets, one shot up close, weak at distance; reloads a shell at a time
///   • Fists   the fire button throws punches
/// Headshots ×2 (×1.5 per shotgun pellet), damage falls off with distance, magazines and reloads.
/// The weapon panel sits top right (like GTA): tap = next weapon, swipe left = previous, hold = reload.
/// Editor: keys 1–5 pick a weapon, Q / E cycle, R reloads.
/// Test loadout (on by default for now): all weapons, unlimited reserve ammo. Added automatically by PlayerCombat.
public class LundunWeapons : MonoBehaviour
{
    public enum Kind { Fists, Pistol, Smg, Carbine, Shotgun }

    [System.Serializable]
    public class Weapon
    {
        public string name = "PISTOL";
        public Kind kind = Kind.Pistol;
        public bool automatic = false;
        [Tooltip("Shots per second (pump/semi: the fastest you can tap)")]
        public float fireRate = 4f;
        public float damage = 34f;
        public int pellets = 1;
        public float headshot = 2f;
        public float range = 80f;
        [Tooltip("Full damage up to here (m)…")] public float falloffStart = 25f;
        [Tooltip("…down to Falloff Min by here")] public float falloffEnd = 60f;
        [Range(0.05f, 1f)] public float falloffMin = 0.7f;

        [Header("Accuracy (degrees)")]
        public float spread = 0.4f;
        [Tooltip("Extra spread added by each shot")] public float bloomPerShot = 0.5f;
        public float bloomMax = 2f;
        [Tooltip("How quickly the extra spread settles (per second)")] public float bloomRecovery = 6f;
        [Tooltip("Shotgun: the pellet cone")] public float pelletCone = 0f;

        [Header("Recoil (degrees)")]
        public float recoilUp = 1.1f;
        public float recoilSide = 0.25f;
        [Tooltip("Carbine: the climb steers gently left/right in a pattern you can learn")] public bool patterned = false;

        [Header("Ammo")]
        public int magazine = 12;
        public int reserve = 96;
        public float reloadTime = 1.4f;
        [Tooltip("Shotgun: loads one shell at a time (and you can fire between shells)")] public bool shellByShell = false;

        [Header("Feel")]
        public float shake = 0.05f;
        public float pitch = 1.1f;
        public float volume = 0.85f;
        [Tooltip("Size of James's gun model for this weapon (until each gets its own model in G3)")]
        public float modelScale = 1f;
        [Tooltip("How much near-miss forgiveness the aim assist gives this weapon")]
        public float assist = 1f;

        [HideInInspector] public int inMag, spare;
    }

    public static LundunWeapons Instance { get; private set; }

    [Header("Test mode")]
    [Tooltip("All four guns, for testing (later: pick-ups and shops)")]
    [SerializeField] private bool testLoadout = true;
    [Tooltip("Never run out of spare ammo (magazines and reloads still happen)")]
    [SerializeField] private bool unlimitedReserve = true;

    [Header("Feel")]
    [SerializeField] private float switchTime = 0.3f;
    [Tooltip("Spread while aimed in (AIM button)")]
    [SerializeField] private float aimedSpread = 0.55f;
    [Tooltip("Spread while moving fast")]
    [SerializeField] private float movingSpread = 1.35f;

    [Header("Weapons")]
    [SerializeField] private Weapon[] weapons = Defaults();

    private readonly bool[] owned = new bool[5];
    private int current = 1;
    private float bloom, readyAt, reloadStart, reloadEnd, nextShell;
    private bool reloading;
    private int patternStep;
    private CharacterController cc;
    private GunHold gun;
    private Vector3 gunScale = Vector3.one;
    private RectTransform crosshair;
    private Vector3 crosshairScale = Vector3.one;

    // HUD
    private RectTransform panel;
    private Image icon, reloadBar, reloadIcon;
    private TextMeshProUGUI ammoText, spareText, nameText;
    private readonly List<Image> dots = new List<Image>();

    public Weapon Current => weapons[current];
    public bool IsFists => Current.kind == Kind.Fists;
    public bool Reloading => reloading;
    public bool Ready => Time.time >= readyAt && !(reloading && !Current.shellByShell);
    /// Spread right now (degrees), for the shot and the crosshair.
    public float SpreadNow
    {
        get
        {
            Weapon w = Current;
            float s = w.spread + bloom;
            s *= Mathf.Lerp(1f, aimedSpread, LundunAim.Eased);
            if (cc != null) { Vector3 v = cc.velocity; v.y = 0f; if (v.magnitude > 3f) s *= movingSpread; }
            return s;
        }
    }

    public static Weapon[] Defaults()
    {
        return new[]
        {
            new Weapon { name = "FISTS", kind = Kind.Fists, automatic = true, fireRate = 1.6f, damage = 20f, range = 2f,
                         spread = 0f, bloomPerShot = 0f, bloomMax = 0f, recoilUp = 0f, recoilSide = 0f, magazine = 0, reserve = 0, reloadTime = 0f,
                         shake = 0.03f, modelScale = 0f },
            new Weapon { name = "PISTOL", kind = Kind.Pistol, automatic = false, fireRate = 4f, damage = 34f, headshot = 2f, range = 80f,
                         falloffStart = 25f, falloffEnd = 60f, falloffMin = 0.7f, spread = 0.35f, bloomPerShot = 0.55f, bloomMax = 2f, bloomRecovery = 6f,
                         recoilUp = 1.2f, recoilSide = 0.3f, magazine = 12, reserve = 96, reloadTime = 1.4f,
                         shake = 0.05f, pitch = 1.12f, volume = 0.8f, modelScale = 0.85f, assist = 1f },
            new Weapon { name = "SMG", kind = Kind.Smg, automatic = true, fireRate = 13f, damage = 18f, headshot = 2f, range = 60f,
                         falloffStart = 12f, falloffEnd = 35f, falloffMin = 0.55f, spread = 1.1f, bloomPerShot = 0.32f, bloomMax = 4.5f, bloomRecovery = 7f,
                         recoilUp = 0.38f, recoilSide = 0.32f, magazine = 30, reserve = 180, reloadTime = 2f,
                         shake = 0.035f, pitch = 1.28f, volume = 0.65f, modelScale = 1f, assist = 1.15f },
            new Weapon { name = "CARBINE", kind = Kind.Carbine, automatic = true, fireRate = 10f, damage = 26f, headshot = 2f, range = 120f,
                         falloffStart = 40f, falloffEnd = 90f, falloffMin = 0.75f, spread = 0.45f, bloomPerShot = 0.22f, bloomMax = 2.6f, bloomRecovery = 6f,
                         recoilUp = 0.55f, recoilSide = 0.2f, patterned = true, magazine = 30, reserve = 150, reloadTime = 2.2f,
                         shake = 0.045f, pitch = 0.95f, volume = 0.85f, modelScale = 1.3f, assist = 0.9f },
            new Weapon { name = "SHOTGUN", kind = Kind.Shotgun, automatic = false, fireRate = 1.2f, damage = 16f, pellets = 8, headshot = 1.5f, range = 40f,
                         falloffStart = 6f, falloffEnd = 22f, falloffMin = 0.2f, spread = 0f, bloomPerShot = 0f, bloomMax = 0f, pelletCone = 4.5f,
                         recoilUp = 3.2f, recoilSide = 0.6f, magazine = 6, reserve = 36, reloadTime = 0.5f, shellByShell = true,
                         shake = 0.14f, pitch = 0.72f, volume = 1f, modelScale = 1.4f, assist = 1.25f },
        };
    }

    void Awake()
    {
        Instance = this;
        if (weapons == null || weapons.Length != 5) weapons = Defaults();
        for (int i = 0; i < weapons.Length; i++) { weapons[i].inMag = weapons[i].magazine; weapons[i].spare = weapons[i].reserve; }
        owned[0] = true; owned[1] = true;
        if (testLoadout) for (int i = 0; i < owned.Length; i++) owned[i] = true;
        cc = GetComponent<CharacterController>();
        if (cc == null) cc = GetComponentInParent<CharacterController>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        gun = FindAnyObjectByType<GunHold>();
        if (gun != null) gunScale = gun.transform.localScale;
        GameObject ch = GameObject.Find("Crosshair");
        if (ch != null) { crosshair = (RectTransform)ch.transform; crosshairScale = crosshair.localScale; }
        BuildHud();
        Equip(1, true);
    }

    /// Give James a weapon (pick-ups, shops, a dropped carbine — G2).
    public void Give(Kind k, int ammo)
    {
        int i = (int)k;
        owned[i] = true;
        weapons[i].spare += ammo;
        Refresh();
    }

    // ------------------------------------------------------------------ firing (called by PlayerCombat)
    /// Use one round. False if empty (and starts a reload).
    public bool TakeRound()
    {
        Weapon w = Current;
        if (w.kind == Kind.Fists) return true;
        if (w.inMag <= 0) { StartReload(); return false; }
        if (reloading && w.shellByShell) reloading = false;        // firing interrupts a shell-by-shell reload
        w.inMag--;
        bloom = Mathf.Min(w.bloomMax, bloom + w.bloomPerShot);
        patternStep++;
        Refresh();
        if (w.inMag == 0) StartReload();
        return true;
    }

    /// The recoil for this shot: (up, sideways) degrees.
    public Vector2 Recoil()
    {
        Weapon w = Current;
        float side = w.patterned ? Mathf.Sin(patternStep * 0.55f) * w.recoilSide + Random.Range(-0.08f, 0.08f)
                                 : Random.Range(-w.recoilSide, w.recoilSide);
        float up = w.recoilUp * Random.Range(0.9f, 1.1f);
        if (w.patterned) up *= Mathf.Lerp(0.8f, 1.15f, Mathf.Clamp01(patternStep / 12f));     // the climb builds a little
        return new Vector2(up, side);
    }

    /// Damage at a distance (before headshots).
    public float DamageAt(float metres)
    {
        Weapon w = Current;
        float t = Mathf.InverseLerp(w.falloffStart, w.falloffEnd, metres);
        return w.damage * Mathf.Lerp(1f, w.falloffMin, t);
    }

    public void StartReload()
    {
        Weapon w = Current;
        if (w.kind == Kind.Fists || reloading || w.inMag >= w.magazine) return;
        if (!unlimitedReserve && w.spare <= 0) return;
        reloading = true;
        reloadStart = Time.time;
        reloadEnd = Time.time + (w.shellByShell ? w.reloadTime * (w.magazine - w.inMag) : w.reloadTime);
        nextShell = Time.time + w.reloadTime;
        LundunHaptics.Light();
        Refresh();
    }

    void FinishReload()
    {
        Weapon w = Current;
        int need = w.magazine - w.inMag;
        int got = unlimitedReserve ? need : Mathf.Min(need, w.spare);
        w.inMag += got;
        if (!unlimitedReserve) w.spare -= got;
        reloading = false;
        Refresh();
    }

    // ------------------------------------------------------------------ switching
    public void Next(int dir)
    {
        for (int step = 1; step <= weapons.Length; step++)
        {
            int i = ((current + dir * step) % weapons.Length + weapons.Length) % weapons.Length;
            if (owned[i]) { Equip(i, false); return; }
        }
    }

    public void Equip(int i, bool silent)
    {
        if (i < 0 || i >= weapons.Length || !owned[i]) return;
        if (i == current && !silent) return;
        current = i;
        reloading = false;
        bloom = 0f;
        patternStep = 0;
        readyAt = Time.time + (silent ? 0f : switchTime);
        if (gun != null)
        {
            Weapon w = Current;
            gun.gameObject.SetActive(w.kind != Kind.Fists);
            gun.transform.localScale = gunScale * Mathf.Max(0.01f, w.modelScale);
        }
        if (!silent) LundunHaptics.Light();
        Refresh();
    }

    void Update()
    {
        Weapon w = Current;
        // the spread settles back when you're not firing
        bloom = Mathf.MoveTowards(bloom, 0f, w.bloomRecovery * Time.deltaTime * (Time.time - lastShotTime > 0.12f ? 1f : 0.15f));

        if (reloading)
        {
            if (w.shellByShell)
            {
                if (Time.time >= nextShell)
                {
                    if (unlimitedReserve || w.spare > 0) { w.inMag++; if (!unlimitedReserve) w.spare--; }
                    nextShell = Time.time + w.reloadTime;
                    if (w.inMag >= w.magazine || (!unlimitedReserve && w.spare <= 0)) reloading = false;
                    Refresh();
                }
            }
            else if (Time.time >= reloadEnd) FinishReload();
        }

        if (crosshair != null)
        {
            float k = IsFists ? 0.8f : 1f + SpreadNow * 0.35f;
            crosshair.localScale = crosshairScale * k;
        }

        UpdateReloadBar();
        EditorKeys();
    }

    private float lastShotTime = -99f;
    public void NotifyShot() { lastShotTime = Time.time; }

    void EditorKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.digit1Key.wasPressedThisFrame) Equip(0, false);
        if (kb.digit2Key.wasPressedThisFrame) Equip(1, false);
        if (kb.digit3Key.wasPressedThisFrame) Equip(2, false);
        if (kb.digit4Key.wasPressedThisFrame) Equip(3, false);
        if (kb.digit5Key.wasPressedThisFrame) Equip(4, false);
        if (kb.qKey.wasPressedThisFrame) Next(-1);
        if (kb.eKey.wasPressedThisFrame) Next(1);
        if (kb.rKey.wasPressedThisFrame) StartReload();
    }

    // ------------------------------------------------------------------ the HUD panel (top right)
    void BuildHud()
    {
        GameObject holder = GameObject.Find("OnFootControls");        // hides with the rest when driving
        if (holder == null) return;
        Transform old = holder.transform.Find("WeaponPanel");
        if (old != null) Destroy(old.gameObject);

        panel = NewRect("WeaponPanel", holder.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-36f, -26f);
        panel.sizeDelta = new Vector2(330f, 112f);
        Image bg = panel.gameObject.AddComponent<Image>();
        bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 0.85f;
        bg.color = new Color(0.03f, 0.05f, 0.09f, 0.72f);
        panel.gameObject.AddComponent<LundunWeaponPanelInput>().Init(this);
        panel.gameObject.AddComponent<LundunPressFX>();

        RectTransform ir = NewRect("Icon", panel);
        ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
        ir.anchoredPosition = new Vector2(18f, 6f); ir.sizeDelta = new Vector2(150f, 92f);
        icon = ir.gameObject.AddComponent<Image>(); icon.preserveAspect = true; icon.raycastTarget = false;

        ammoText = Text(panel, "", 46, TextAlignmentOptions.BottomRight);
        RectTransform ar = ammoText.rectTransform;
        ar.anchorMin = ar.anchorMax = new Vector2(1f, 0.5f); ar.pivot = new Vector2(1f, 0.5f);
        ar.anchoredPosition = new Vector2(-82f, 10f); ar.sizeDelta = new Vector2(110f, 56f);
        spareText = Text(panel, "", 24, TextAlignmentOptions.BottomLeft);
        RectTransform sr = spareText.rectTransform;
        sr.anchorMin = sr.anchorMax = new Vector2(1f, 0.5f); sr.pivot = new Vector2(0f, 0.5f);
        sr.anchoredPosition = new Vector2(-80f, 4f); sr.sizeDelta = new Vector2(70f, 40f);
        spareText.color = new Color(1f, 1f, 1f, 0.6f);
        nameText = Text(panel, "", 20, TextAlignmentOptions.Center);
        RectTransform nr = nameText.rectTransform;
        nr.anchorMin = nr.anchorMax = new Vector2(1f, 0f); nr.pivot = new Vector2(1f, 0f);
        nr.anchoredPosition = new Vector2(-16f, 10f); nr.sizeDelta = new Vector2(150f, 26f);
        nameText.characterSpacing = 6f; nameText.color = new Color(1f, 1f, 1f, 0.7f);

        // reload: a thin bar along the bottom and a small arrow icon
        RectTransform rb = NewRect("ReloadBar", panel);
        rb.anchorMin = new Vector2(0f, 0f); rb.anchorMax = new Vector2(1f, 0f); rb.pivot = new Vector2(0f, 0f);
        rb.offsetMin = new Vector2(26f, 4f); rb.offsetMax = new Vector2(-26f, 9f);
        reloadBar = rb.gameObject.AddComponent<Image>(); reloadBar.color = new Color(1f, 0.66f, 0.18f, 0.95f);
        reloadBar.type = Image.Type.Filled; reloadBar.fillMethod = Image.FillMethod.Horizontal; reloadBar.raycastTarget = false;
        reloadBar.sprite = LundunHudArt.Panel;
        RectTransform ri = NewRect("ReloadIcon", panel);
        ri.anchorMin = ri.anchorMax = new Vector2(1f, 0.5f); ri.anchoredPosition = new Vector2(-150f, 8f); ri.sizeDelta = new Vector2(40f, 40f);
        reloadIcon = ri.gameObject.AddComponent<Image>(); reloadIcon.sprite = LundunHudArt.Reload; reloadIcon.raycastTarget = false;
        reloadIcon.color = new Color(1f, 0.66f, 0.18f);

        // slot dots under the panel: which weapon of how many
        for (int i = 0; i < weapons.Length; i++)
        {
            RectTransform d = NewRect("Dot" + i, panel);
            d.anchorMin = d.anchorMax = new Vector2(0.5f, 0f); d.pivot = new Vector2(0.5f, 1f);
            d.anchoredPosition = new Vector2((i - (weapons.Length - 1) * 0.5f) * 18f, -8f); d.sizeDelta = new Vector2(10f, 10f);
            Image di = d.gameObject.AddComponent<Image>(); di.sprite = LundunHudArt.Disc; di.raycastTarget = false;
            dots.Add(di);
        }
    }

    void Refresh()
    {
        if (panel == null) return;
        Weapon w = Current;
        icon.sprite = IconFor(w.kind);
        bool fists = w.kind == Kind.Fists;
        ammoText.text = fists ? "" : w.inMag.ToString();
        ammoText.color = !fists && w.inMag <= Mathf.Max(1, w.magazine / 4) ? new Color(1f, 0.45f, 0.35f) : Color.white;
        spareText.text = fists ? "" : "/ " + w.spare;          // test mode: the reserve never goes down
        nameText.text = w.name;
        for (int i = 0; i < dots.Count; i++)
        {
            dots[i].gameObject.SetActive(owned[i]);
            dots[i].color = i == current ? Color.white : new Color(1f, 1f, 1f, 0.3f);
        }
    }

    void UpdateReloadBar()
    {
        if (reloadBar == null) return;
        Weapon w = Current;
        bool show = reloading;
        if (reloadBar.gameObject.activeSelf != show) reloadBar.gameObject.SetActive(show);
        if (reloadIcon.gameObject.activeSelf != show) reloadIcon.gameObject.SetActive(show);
        if (!show) return;
        float total = Mathf.Max(0.01f, reloadEnd - reloadStart);
        reloadBar.fillAmount = w.shellByShell ? (float)w.inMag / Mathf.Max(1, w.magazine) : Mathf.Clamp01((Time.time - reloadStart) / total);
        reloadIcon.rectTransform.localEulerAngles = new Vector3(0f, 0f, -Time.time * 360f);
    }

    static Sprite IconFor(Kind k)
    {
        switch (k)
        {
            case Kind.Fists: return LundunHudArt.Fist;
            case Kind.Smg: return LundunHudArt.Smg;
            case Kind.Carbine: return LundunHudArt.Carbine;
            case Kind.Shotgun: return LundunHudArt.Shotgun;
            default: return LundunHudArt.Pistol;
        }
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static TextMeshProUGUI Text(RectTransform parent, string text, float size, TextAlignmentOptions align)
    {
        TextMeshProUGUI t = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.alignment = align; t.color = Color.white; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}

/// The weapon panel's touch: tap = next weapon, swipe left = previous, hold = reload.
public class LundunWeaponPanelInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    private LundunWeapons weapons;
    private float downAt;
    private Vector2 dragged;
    private bool held;

    public void Init(LundunWeapons w) { weapons = w; }

    public void OnPointerDown(PointerEventData e) { downAt = Time.unscaledTime; dragged = Vector2.zero; held = false; }
    public void OnDrag(PointerEventData e) { dragged += e.delta; }

    void Update()
    {
        if (downAt > 0f && !held && Time.unscaledTime - downAt > 0.45f && dragged.magnitude < 30f)
        {
            held = true;
            if (weapons != null) weapons.StartReload();
        }
    }

    public void OnPointerUp(PointerEventData e)
    {
        float t = downAt; downAt = 0f;
        if (weapons == null || held || t <= 0f) return;
        if (dragged.x < -40f) weapons.Next(-1);
        else weapons.Next(1);
    }
}
