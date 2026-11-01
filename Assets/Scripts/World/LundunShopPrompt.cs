using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// E5: the PICK UP / STEAL button, shown when you're standing by something you can take (one for the whole game,
/// made the first time a store's items appear). E6: also the LIFT button, when you're standing in a store's lift.
public class LundunShopPrompt : MonoBehaviour
{
    [SerializeField] private float reach = 1.9f;

    private static LundunShopPrompt instance;
    private GameObject buttonGo;
    private TextMeshProUGUI label;
    private Image back;
    private LundunShopItem offered;
    private LundunShopInterior liftShop;
    private int liftIndex, liftStop;
    private VehicleSystem vehicles;
    private float nextCheck;

    public static void Ensure()
    {
        if (instance != null) return;
        GameObject g = new GameObject("LundunShopPrompt");
        instance = g.AddComponent<LundunShopPrompt>();
    }

    void Awake() { if (instance == null) instance = this; BuildUI(); }
    void OnDestroy() { if (instance == this) instance = null; }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.15f;
        offered = Nearest();
        liftShop = offered == null ? NearestLift(out liftIndex, out liftStop) : null;
        if (offered == null && liftShop == null) { buttonGo.SetActive(false); return; }
        if (offered != null)
        {
            label.text = (offered.IsSteal ? "STEAL" : "PICK UP") + "\n<size=70%>" + offered.DisplayName + "</size>";
            back.color = offered.IsSteal ? new Color(0.55f, 0.08f, 0.1f, 0.92f) : new Color(0.12f, 0.16f, 0.29f, 0.92f);
        }
        else
        {
            label.text = liftShop.LiftLabel(liftIndex, liftStop);
            back.color = new Color(0.16f, 0.17f, 0.18f, 0.92f);
        }
        buttonGo.SetActive(true);
    }

    LundunShopItem Nearest()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot || VehicleSystem.Locked) return null;
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        if (vehicles != null && vehicles.IsDriving) return null;
        if (LundunBikes.Instance != null && LundunBikes.Instance.Riding) return null;
        Vector3 p = ph.Body.position;
        LundunShopItem best = null; float bd = reach;
        foreach (LundunShopItem it in LundunShopItem.Active)
        {
            if (it == null || !it.Available) continue;
            Vector3 d = it.StandAt - p; d.y = 0f;
            float m = d.magnitude;
            if (m < bd && Mathf.Abs(it.StandAt.y - p.y) < 1.5f) { bd = m; best = it; }
        }
        return best;
    }

    /// E6: the store lift you're standing in, if any.
    LundunShopInterior NearestLift(out int lift, out int stop)
    {
        lift = -1; stop = -1;
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot || VehicleSystem.Locked) return null;
        Vector3 feet = ph.Body.position - Vector3.up * LundunPlaces.FeetOffset(ph.Body);
        foreach (LundunShopInterior s in LundunShopInterior.All)
            if (s != null && s.LiftCount > 0 && s.LiftAt(feet, out lift, out stop)) return s;
        return null;
    }

    void OnPress()
    {
        if (offered != null) offered.Take();
        else if (liftShop != null) liftShop.Ride(liftIndex, liftStop);
        offered = null; liftShop = null; buttonGo.SetActive(false); nextCheck = Time.time + 0.8f;
    }

    void BuildUI()
    {
        GameObject cg = new GameObject("ShopPromptUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 61;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        buttonGo = new GameObject("ShopPrompt", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        back = buttonGo.AddComponent<Image>(); back.color = new Color(0.12f, 0.16f, 0.29f, 0.92f);
        Button b = buttonGo.AddComponent<Button>(); b.targetGraphic = back; b.onClick.AddListener(OnPress);
        RectTransform rt = (RectTransform)buttonGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 230f); rt.sizeDelta = new Vector2(460f, 120f);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(buttonGo.transform, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        label = tg.AddComponent<TextMeshProUGUI>();
        label.fontSize = 38; label.alignment = TextAlignmentOptions.Center; label.color = Color.white;
        label.fontStyle = FontStyles.Bold; label.raycastTarget = false; label.richText = true;
        buttonGo.SetActive(false);
    }
}
