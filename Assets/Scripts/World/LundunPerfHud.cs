using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// E0: a small performance readout for checking the budgets on the iPhone (off unless you tick Show).
/// Frames per second (average and the worst 1% over the last few seconds), frame time, how much fine detail is being
/// drawn, and how many people and cars are about, coloured against the targets: a stable 30 fps, 60 as the stretch goal.
/// Lives on the "LundunPerformance" object (made by Lundun → 26).
public class LundunPerfHud : MonoBehaviour
{
    [SerializeField] private bool show = false;
    [Tooltip("Seconds of frames the averages cover")]
    [SerializeField] private float window = 4f;

    private const int Max = 1200;
    private readonly float[] times = new float[Max];
    private int head, count;
    private float nextText;
    private TextMeshProUGUI text;
    private GameObject canvasGo;
    private readonly StringBuilder sb = new StringBuilder();

    public bool Show { get { return show; } set { show = value; if (canvasGo != null) canvasGo.SetActive(value); } }

    void Start()
    {
        canvasGo = new GameObject("PerfHUD", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        Canvas c = canvasGo.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 90;
        CanvasScaler sc = canvasGo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        GameObject bg = new GameObject("Panel", typeof(RectTransform));
        bg.transform.SetParent(canvasGo.transform, false);
        RectTransform rt = (RectTransform)bg.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f); rt.anchoredPosition = new Vector2(30f, -170f); rt.sizeDelta = new Vector2(430f, 190f);
        bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(bg.transform, false);
        RectTransform tr = (RectTransform)tg.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(14, 8); tr.offsetMax = new Vector2(-14, -8);
        text = tg.AddComponent<TextMeshProUGUI>();
        text.fontSize = 26; text.color = Color.white; text.richText = true; text.raycastTarget = false; text.alignment = TextAlignmentOptions.TopLeft;
        canvasGo.SetActive(show);
    }

    void Update()
    {
        times[head] = Time.unscaledDeltaTime; head = (head + 1) % Max; if (count < Max) count++;
        if (!show || text == null || Time.unscaledTime < nextText) return;
        nextText = Time.unscaledTime + 0.5f;

        // the frames in the window, newest first
        float total = 0f; int n = 0;
        float[] w = new float[count];
        for (int i = 0; i < count && total < window; i++)
        {
            float t = times[(head - 1 - i + Max) % Max];
            w[n++] = t; total += t;
        }
        if (n == 0) return;
        float avgFps = n / Mathf.Max(0.0001f, total);
        System.Array.Sort(w, 0, n);
        int worst = Mathf.Max(1, n / 100);
        float slow = 0f; for (int i = n - worst; i < n; i++) slow += w[i];
        float lowFps = worst / Mathf.Max(0.0001f, slow);

        sb.Length = 0;
        sb.Append(Col(avgFps)).Append(avgFps.ToString("0")).Append(" fps</color>  <size=80%>1% low ").Append(Col(lowFps)).Append(lowFps.ToString("0")).Append("</color></size>\n");
        sb.Append("<size=80%>").Append((1000f / avgFps).ToString("0.0")).Append(" ms a frame\n");
        LundunStreaming st = LundunStreaming.Instance;
        if (st != null) sb.Append("Detail drawn ").Append(st.Visible).Append(" / ").Append(st.Total).Append('\n');
        sb.Append("People ").Append(FindObjectsByType<NPCBrain>(FindObjectsSortMode.None).Length)
          .Append("   Cars ").Append(FindObjectsByType<CarController>(FindObjectsSortMode.None).Length).Append("</size>");
        text.text = sb.ToString();
    }

    /// Green at 60, white at 30 or more, amber below 30, red below 24.
    static string Col(float fps) { return fps >= 58f ? "<color=#5fe08a>" : fps >= 29f ? "<color=#ffffff>" : fps >= 24f ? "<color=#ffc04a>" : "<color=#ff5a5a>"; }
}
