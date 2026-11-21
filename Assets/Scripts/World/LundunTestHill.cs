using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// G7 (step 1): the test hill's way there and back. Sits on the LundunTestHill object (Lundun → 39). Shows a small
/// TEST HILL button at the top of the screen; it takes James to the bottom of the test street (in his car if he's
/// driving, the car keeps its speed at zero) and BACK TO TOWN puts him back where he was. Not during a job, with the
/// police after him, on a bike, or down the Tube. Remove the hill (Lundun → 39b) and the button goes with it.
public class LundunTestHill : MonoBehaviour
{
    [SerializeField] private Vector3 start;
    [SerializeField] private float startYaw;

    bool there, busy;
    Vector3 backPos; Quaternion backRot;
    GameObject button;
    TextMeshProUGUI label;
    Image fade;

    public void Set(Vector3 startPos, float yaw) { start = startPos; startYaw = yaw; }

    void Start() { BuildUI(); }

    void Update()
    {
        if (button == null) return;
        bool show = !busy && (there || Allowed());
        if (button.activeSelf != show) button.SetActive(show);
        label.text = there ? "BACK TO TOWN" : "TEST HILL";
    }

    bool Allowed()
    {
        if (LundunMissions.Running || LundunTubeStation.Underground) return false;
        if (WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0) return false;
        if (LundunBikes.Instance != null && LundunBikes.Instance.Riding) return false;
        if (VehicleSystem.Locked || PlayerHealth.Instance == null || PlayerHealth.Instance.IsDead) return false;
        return true;
    }

    void Go()
    {
        if (busy) return;
        if (!there && !Allowed()) return;
        StartCoroutine(Move());
    }

    IEnumerator Move()
    {
        busy = true;
        VehicleSystem vs = FindAnyObjectByType<VehicleSystem>();
        if (vs != null) vs.LockControls(true);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.35f) { Fade(t); yield return null; }
        Fade(1f);
        Transform who = vs != null && vs.IsDriving ? vs.PlayerTarget : (PlayerHealth.Instance != null ? PlayerHealth.Instance.Body : null);
        if (who != null)
        {
            Vector3 to; Quaternion rot;
            if (!there)
            {
                backPos = who.position; backRot = who.rotation;
                to = start + (vs != null && vs.IsDriving ? Vector3.up * 0.6f : Vector3.zero);
                rot = Quaternion.Euler(0f, startYaw, 0f);
            }
            else { to = backPos + Vector3.up * 0.3f; rot = backRot; }
            Put(who, to, rot);
            there = !there;
        }
        yield return new WaitForSecondsRealtime(0.25f);
        if (vs != null) vs.LockControls(false);
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime / 0.35f) { Fade(t); yield return null; }
        Fade(0f);
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(there ? "TEST HILL\nflat · 5% · level · 10% · top · 14% down · 6% down" : "Back in town");
        busy = false;
    }

    static void Put(Transform who, Vector3 pos, Quaternion rot)
    {
        Rigidbody rb = who.GetComponent<Rigidbody>();
        CharacterController cc = who.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = pos; rb.rotation = rot; }
        who.SetPositionAndRotation(pos, rot);
        if (cc != null) cc.enabled = true;
        Physics.SyncTransforms();
    }

    void Fade(float a) { if (fade != null) { fade.color = new Color(0f, 0f, 0f, a); fade.raycastTarget = a > 0.01f; } }

    void BuildUI()
    {
        GameObject cg = new GameObject("TestHillUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas c = cg.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 58;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        button = new GameObject("TestHillButton", typeof(RectTransform));
        button.transform.SetParent(cg.transform, false);
        RectTransform rt = (RectTransform)button.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -150f); rt.sizeDelta = new Vector2(260f, 64f);
        Image img = button.AddComponent<Image>();
        img.color = new Color(0.12f, 0.3f, 0.16f, 0.9f);
        if (LundunHudArt.Pill != null) { img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.2f; }
        Button b = button.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(Go);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(button.transform, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        label = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) label.font = LundunHud.Instance.FontAsset;
        label.text = "TEST HILL"; label.fontSize = 28; label.fontStyle = FontStyles.Bold; label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white; label.raycastTarget = false;

        GameObject fg = new GameObject("Fade", typeof(RectTransform));
        fg.transform.SetParent(cg.transform, false);
        RectTransform frt = (RectTransform)fg.transform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
        fade = fg.AddComponent<Image>();
        Fade(0f);
    }
}
