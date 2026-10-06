using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
public class HitMarkerUI : MonoBehaviour
{
    [SerializeField] private float showTime = 0.15f;
    [SerializeField] private Color hitColour = Color.white;
    [SerializeField] private Color killColour = Color.red;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip killSound;

    private CanvasGroup group;
    private AudioSource audioSource;
    private Image[] lines;
    private float timer;

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        audioSource = GetComponent<AudioSource>();
        lines = GetComponentsInChildren<Image>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
    }

    public void Show(bool isKill)
    {
        timer = isKill ? showTime * 2f : showTime;

        foreach (Image line in lines)
        {
            line.color = isKill ? killColour : hitColour;
        }

        transform.localScale = Vector3.one * (isKill ? 1.4f : 1f);
        group.alpha = 1f;

        AudioClip clip = isKill ? killSound : hitSound;
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
    }

    void Update()
    {
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            group.alpha = Mathf.Clamp01(timer / showTime);
        }
    }
}