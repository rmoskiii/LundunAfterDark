using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// S8: Marcus's backup call (Story only; added by LundunStoryUI). Unlocked by a silver or gold medal on side mission
/// 14 (Prove Yourself). Once a day, outside missions, when there's trouble (the police after you, a gang going for you,
/// someone squaring up): CALL MARCUS (a button under the minimap's corner). He turns up just behind James with his
/// pistol and fights beside him for three minutes, then heads off.
public class LundunBackup : MonoBehaviour
{
    public static LundunBackup Instance { get; private set; }
    public const string Flag = "marcus:backup";
    const float Lasts = 180f;

    /// Marcus is here, fighting beside James (LundunCrewCombat treats it like a mission fight).
    public static bool Active { get { return Instance != null && Instance.until > Time.time; } }

    private float until = -1f, nextCheck;
    private bool wasActive;
    private GameObject buttonGo;
    private TextMeshProUGUI label;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }
    void OnEnable() { LundunMissions.Passed += OnPassed; }
    void OnDisable() { LundunMissions.Passed -= OnPassed; }
    void Start() { BuildUI(); }

    void OnPassed(string id, int medal)
    {
        if (id != M14ProveYourself.MissionId || medal < 2) return;
        LundunStoryData d = LundunStory.Data;
        if (d == null || d.flags.Contains(Flag)) return;
        d.flags.Add(Flag);
        LundunStory.MarkDirty();
        StartCoroutine(Tell());
    }

    System.Collections.IEnumerator Tell()
    {
        yield return new WaitForSecondsRealtime(1f);
        while (Time.timeScale == 0f) yield return null;                         // after the pass screen
        LundunMissions.Toast("MARCUS HAS YOUR BACK\nIn trouble? CALL MARCUS, once a day");
    }

    void Update()
    {
        // his time's up (or a mission's started): he heads off
        if (wasActive && (!Active || LundunMissions.Running))
        {
            until = -1f;
            if (!LundunMissions.Running) { LundunCrew.Leave("MARCUS"); LundunCrew.Arm("MARCUS", false); }
            LundunMissions.Say("MARCUS", "Right, I'm off. Stay out of trouble. For once.", 3f);
        }
        wasActive = Active;
        if (buttonGo == null || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        bool show = CanCall() && Trouble();
        if (buttonGo.activeSelf != show) buttonGo.SetActive(show);
    }

    static bool CanCall()
    {
        LundunStoryData d = LundunStory.Data;
        if (d == null || !d.flags.Contains(Flag) || LundunMissions.Running || Active) return false;
        if (LundunDay.DoneToday("backup")) return false;
        PlayerHealth ph = PlayerHealth.Instance;
        return ph != null && !ph.IsDead && ph.OnFoot && !VehicleSystem.Locked && !LundunArches.Inside && !LundunPubs.InsidePub;
    }

    /// The police after him, or someone going for him close by.
    static bool Trouble()
    {
        if (WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0) return true;
        Transform j = LundunMissionRun.James();
        if (j == null) return false;
        foreach (LundunGangMember g in FindObjectsByType<LundunGangMember>(FindObjectsSortMode.None))
            if (g != null && !g.IsDead && g.FightingJames && LundunMissionRun.FlatDistance(g.transform.position, j.position) < 40f) return true;
        foreach (LundunAngryDriver a in FindObjectsByType<LundunAngryDriver>(FindObjectsSortMode.None))
            if (a != null && a.enabled && LundunMissionRun.FlatDistance(a.transform.position, j.position) < 30f) return true;
        return false;
    }

    void Call()
    {
        if (!CanCall()) return;
        LundunDay.MarkToday("backup");
        LundunStory.MarkDirty();
        until = Time.time + Lasts;
        LundunCrew.Join("MARCUS");
        LundunCrew.Arm("MARCUS", true);
        LundunMissions.Say("MARCUS", "(phone) Say less. I'm round the corner.", 3f);
        LundunHaptics.Medium();
        buttonGo.SetActive(false);
    }

    void BuildUI()
    {
        GameObject cg = new GameObject("BackupUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas cv = cg.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 61;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        buttonGo = new GameObject("CallMarcus", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        RectTransform rt = (RectTransform)buttonGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(40f, 120f); rt.sizeDelta = new Vector2(250f, 84f);
        Image bg = buttonGo.AddComponent<Image>();
        if (LundunHudArt.Pill != null) { bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 0.9f; }
        bg.color = new Color(0.75f, 0.2f, 0.17f, 0.92f);
        Button b = buttonGo.AddComponent<Button>(); b.targetGraphic = bg; b.onClick.AddListener(Call);
        buttonGo.AddComponent<LundunPressFX>();
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(buttonGo.transform, false);
        RectTransform tr = (RectTransform)tg.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        label = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) label.font = LundunHud.Instance.FontAsset;
        label.text = "CALL MARCUS\n<size=60%>backup · once a day</size>";
        label.fontSize = 28; label.alignment = TextAlignmentOptions.Center; label.color = Color.white; label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false; label.richText = true;
        buttonGo.SetActive(false);
    }
}
