using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class WastedSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VehicleSystem vehicleSystem;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Transform hospital;          // where you wake up

    [Header("UI + effects")]
    [SerializeField] private Image blackout;              // same Blackout image BUSTED uses
    [SerializeField] private GameObject wastedText;
    [SerializeField] private Volume greyscaleVolume;      // optional: drains the colour
    [SerializeField] private AudioSource wastedSting;     // optional

    void Start()
    {
        wastedText.SetActive(false);
        if (greyscaleVolume != null) greyscaleVolume.weight = 0f;
        playerHealth.Died += OnDied;
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.Died -= OnDied;
    }

    void OnDied() => StartCoroutine(WastedSequence());

    IEnumerator WastedSequence()
    {
        bool onFoot = !vehicleSystem.IsDriving;
        Transform body = playerHealth.Body;
        Animator anim = body.GetComponentInChildren<Animator>();

        vehicleSystem.LockControls(true);
        if (onFoot && anim != null) anim.speed = 0f;      // freeze the pose...
        Time.timeScale = 0.3f;
        if (wastedSting != null) wastedSting.Play();

        // ...topple over while the colour drains away
        Quaternion start = body.rotation;
        Quaternion end = start * Quaternion.Euler(-85f, 0f, 0f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            if (onFoot) body.rotation = Quaternion.Slerp(start, end, t * t);
            if (greyscaleVolume != null) greyscaleVolume.weight = t;
            yield return null;
        }

        wastedText.SetActive(true);
        yield return new WaitForSecondsRealtime(2f);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            SetBlackout(t);
            yield return null;
        }
        SetBlackout(1f);

        // Reset everything while the screen is black
        Time.timeScale = 1f;
        wastedText.SetActive(false);
        if (greyscaleVolume != null) greyscaleVolume.weight = 0f;
        if (anim != null) anim.speed = 1f;

        WantedSystem.Instance.ClearWanted();
        vehicleSystem.RespawnPlayer(hospital.position, hospital.rotation);
        playerHealth.ResetHealth();
        vehicleSystem.LockControls(false);
        yield return new WaitForSecondsRealtime(0.5f);

        for (float t = 1f; t > 0f; t -= Time.unscaledDeltaTime)
        {
            SetBlackout(t);
            yield return null;
        }
        SetBlackout(0f);
    }

    void SetBlackout(float alpha)
    {
        Color c = blackout.color;
        c.a = alpha;
        blackout.color = c;
        blackout.raycastTarget = alpha > 0.01f;
    }
}
