using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BustedSystem : MonoBehaviour
{
    [Header("Arrest rules")]
    [SerializeField] private float arrestRange = 5f;
    [SerializeField] private float maxSpeedOnFoot = 3f;     // sprinting away = can't be grabbed
    [SerializeField] private float maxSpeedInCar = 2f;      // stopped or boxed in
    [SerializeField] private float timeToArrest = 2.5f;

    [Header("References")]
    [SerializeField] private VehicleSystem vehicleSystem;
    [SerializeField] private Transform policeStation;       // where you wake up
    [SerializeField] private Image arrestMeter;
    [SerializeField] private Image blackout;
    [SerializeField] private TMP_Text bigText;

    private float progress;
    private bool playingSequence;

    void Start()
    {
        SetMeter(0f);
        SetBlackout(0f);
        bigText.text = "";
    }

    void Update()
    {
        if (playingSequence) return;

        WantedSystem wanted = WantedSystem.Instance;
        if (wanted == null || wanted.Stars == 0)
        {
            progress = 0f;
            SetMeter(0f);
            return;
        }

        // Is any police unit close enough, and can it see us?
        bool copNearby = false;
        Vector3 playerPos = wanted.PlayerTarget.position;
        foreach (PoliceAI cop in PoliceAI.Active)
        {
            if (cop.CanSeeTarget && Vector3.Distance(cop.transform.position, playerPos) < arrestRange)
            {
                copNearby = true;
                break;
            }
        }

        float speedLimit = vehicleSystem.IsDriving ? maxSpeedInCar : maxSpeedOnFoot;
        bool beingArrested = copNearby && vehicleSystem.PlayerSpeed < speedLimit;

        // Fill while caught, drain (faster) when you break free
        progress += beingArrested ? Time.deltaTime : -Time.deltaTime * 1.5f;
        progress = Mathf.Clamp(progress, 0f, timeToArrest);
        SetMeter(progress / timeToArrest);

        if (progress >= timeToArrest) StartCoroutine(Busted());
    }

    IEnumerator Busted()
    {
        playingSequence = true;
        progress = 0f;
        SetMeter(0f);
        vehicleSystem.LockControls(true);

        // Slow motion + "BUSTED"
        Time.timeScale = 0.35f;
        bigText.text = "BUSTED";
        yield return Fade(0f, 0.5f, 0.6f);
        yield return new WaitForSecondsRealtime(1.5f);

        // Fade to black
        yield return Fade(0.5f, 1f, 0.5f);
        Time.timeScale = 1f;

        // Reset the world while the screen is black
        foreach (PoliceAI cop in PoliceAI.Active.ToArray()) Destroy(cop.gameObject);
        WantedSystem.Instance.ClearWanted();
        vehicleSystem.RespawnPlayer(policeStation.position, policeStation.rotation);

        yield return new WaitForSecondsRealtime(0.8f);
        bigText.text = "";
        yield return Fade(1f, 0f, 1f);

        vehicleSystem.LockControls(false);
        playingSequence = false;
    }

    // Uses real time, so it still works while the game is in slow motion
    IEnumerator Fade(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            SetBlackout(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetBlackout(to);
    }

    void SetMeter(float amount)
    {
        arrestMeter.fillAmount = amount;
        arrestMeter.enabled = amount > 0.01f;
    }

    void SetBlackout(float alpha)
    {
        blackout.color = new Color(0f, 0f, 0f, alpha);
    }
}