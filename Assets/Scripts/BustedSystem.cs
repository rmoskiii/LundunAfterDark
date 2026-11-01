using System.Collections;
using TMPro;
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
    [SerializeField] private float footReach = 1.8f;      // officer this close to James = grabbing him
    [SerializeField] private float carReach = 3f;         // officer this close to your car door
    [SerializeField] private float maxCarSpeed = 1.5f;    // can only be pulled out of a (nearly) stopped car
    [SerializeField] private float timeToArrest = 2.5f;   // seconds of being held to get BUSTED

    private float progress;
    private bool busted;
    private TMP_Text bigTextLabel;
    private Vector3 bigTextScale;

    void Start()
    {
        bigTextLabel = bigText.GetComponent<TMP_Text>();
        bigTextScale = bigText.transform.localScale;
        arrestMeter.gameObject.SetActive(false);
        bigText.SetActive(false);
        SetBlackout(0f);
    }

    void Update()
    {
        if (busted || (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead)) return;

        if (OfficerHasHold()) progress += Time.deltaTime / timeToArrest;
        else progress -= Time.deltaTime / timeToArrest * 2f;      // break free and it drains fast
        progress = Mathf.Clamp01(progress);

        arrestMeter.fillAmount = progress;
        arrestMeter.gameObject.SetActive(progress > 0f);

        if (progress >= 1f) StartCoroutine(BustedSequence());
    }

    // Only an officer ON FOOT can arrest you, by getting right up to you
    bool OfficerHasHold()
    {
        if (WantedSystem.Instance.Stars == 0) return false;

        bool inCar = vehicleSystem.IsDriving;
        if (inCar && vehicleSystem.PlayerSpeed > maxCarSpeed) return false;

        float reach = inCar ? carReach : footReach;
        Vector3 playerPos = vehicleSystem.PlayerTarget.position;

        foreach (PoliceOfficer officer in PoliceOfficer.Active)
        {
            if (officer == null || !officer.IsChasing) continue;
            Vector3 d = officer.transform.position - playerPos;
            d.y = 0f;
            if (d.magnitude < reach) return true;
        }
        return false;
    }

    IEnumerator BustedSequence()
    {
        busted = true;
        Vector3 arrestedAt = vehicleSystem.PlayerTarget.position;
        arrestMeter.gameObject.SetActive(false);
        vehicleSystem.LockControls(true);

        // Slow-mo + BUSTED slams onto the screen
        Time.timeScale = 0.3f;
        if (bigTextLabel != null)
        {
            bigTextLabel.text = "BUSTED";
            bigTextLabel.color = new Color(0.35f, 0.6f, 1f);    // police blue
        }
        bigText.SetActive(true);
        for (float t = 0f; t < 0.25f; t += Time.unscaledDeltaTime)
        {
            bigText.transform.localScale = bigTextScale * Mathf.Lerp(1.6f, 1f, t / 0.25f);
            yield return null;
        }
        bigText.transform.localScale = bigTextScale;
        yield return new WaitForSecondsRealtime(1.75f);

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
        // Each step on its own, so one problem can never leave you stuck on a black screen
        string releasedAt = null;
        try { WantedSystem.Instance.ClearWanted(); } catch (System.Exception e) { Debug.LogException(e); }
        try { PoliceAI.ClearAll(); } catch (System.Exception e) { Debug.LogException(e); }   // no cops waiting outside the station
        try
        {
            // the nearest police station to where you were arrested
            releasedAt = LundunPlaces.RespawnSpot("police", "SP_BUSTED", policeStation, arrestedAt, out Vector3 releaseAt, out Quaternion releaseRot);
            vehicleSystem.RespawnPlayer(releaseAt, releaseRot);
        }
        catch (System.Exception e) { Debug.LogException(e); }
        vehicleSystem.LockControls(false);
        progress = 0f;
        yield return new WaitForSecondsRealtime(0.5f);
        if (releasedAt != null && LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(releasedAt);

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
