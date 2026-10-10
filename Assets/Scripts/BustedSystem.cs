using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class BustedSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VehicleSystem vehicleSystem;
    [SerializeField] private Transform policeStation;     // where you appear after arrest

    [Header("UI")]
    [SerializeField] private Image arrestMeter;           // Image Type: Filled
    [SerializeField] private Image blackout;              // full-screen black Image
    [SerializeField] private GameObject bigText;          // the "BUSTED" text

    [Header("Rules")]
    [SerializeField] private float arrestRange = 6f;      // how close a police car must be
    [SerializeField] private float maxEscapeSpeed = 3f;   // slower than this = they can grab you
    [SerializeField] private float timeToArrest = 2f;     // seconds to fill the meter

    private float progress;
    private bool busted;

    void Start()
    {
        arrestMeter.gameObject.SetActive(false);
        bigText.SetActive(false);
        SetBlackout(0f);
    }

    void Update()
    {
        if (busted) return;

        if (CanBeArrested()) progress += Time.deltaTime / timeToArrest;
        else progress -= Time.deltaTime / timeToArrest * 1.5f;   // drains a bit faster
        progress = Mathf.Clamp01(progress);

        arrestMeter.fillAmount = progress;
        arrestMeter.gameObject.SetActive(progress > 0f);

        if (progress >= 1f) StartCoroutine(BustedSequence());
    }

    bool CanBeArrested()
    {
        if (WantedSystem.Instance.Stars == 0) return false;
        if (vehicleSystem.PlayerSpeed > maxEscapeSpeed) return false;

        Vector3 playerPos = vehicleSystem.PlayerTarget.position;
        foreach (PoliceAI cop in PoliceAI.Active)
        {
            if (!cop.CanSeeTarget) continue;
            if (Vector3.Distance(cop.transform.position, playerPos) < arrestRange) return true;
        }
        return false;
    }

    IEnumerator BustedSequence()
    {
        busted = true;
        arrestMeter.gameObject.SetActive(false);
        vehicleSystem.LockControls(true);

        // Slow-mo + BUSTED text
        Time.timeScale = 0.3f;
        bigText.SetActive(true);
        yield return new WaitForSecondsRealtime(2f);

        // Fade to black
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            SetBlackout(t);
            yield return null;
        }
        SetBlackout(1f);

        // Reset everything while the screen is black
        Time.timeScale = 1f;
        bigText.SetActive(false);
        WantedSystem.Instance.ClearWanted();
        vehicleSystem.RespawnPlayer(policeStation.position, policeStation.rotation);
        vehicleSystem.LockControls(false);
        progress = 0f;
        yield return new WaitForSecondsRealtime(0.5f);

        // Fade back in
        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime)
        {
            SetBlackout(t);
            yield return null;
        }
        SetBlackout(0f);
        busted = false;
    }

    void SetBlackout(float alpha)
    {
        Color c = blackout.color;
        c.a = alpha;
        blackout.color = c;
        blackout.raycastTarget = alpha > 0.01f;   // don't block touches when invisible
    }
}