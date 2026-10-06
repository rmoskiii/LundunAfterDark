using UnityEngine;

public class MuzzleFlash : MonoBehaviour
{
    [SerializeField] private GameObject flashVisual;
    [SerializeField] private Light flashLight;
    [SerializeField] private float flashTime = 0.04f;

    private float timer;

    void Awake()
    {
        SetVisible(false);
    }

    public void Flash()
    {
        timer = flashTime;

        if (flashVisual != null)
        {
            // Random spin and size so no two flashes look the same
            flashVisual.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            flashVisual.transform.localScale = Vector3.one * Random.Range(0.25f, 0.4f);
        }

        SetVisible(true);
    }

    void Update()
    {
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f) SetVisible(false);
        }
    }

    void SetVisible(bool visible)
    {
        if (flashVisual != null) flashVisual.SetActive(visible);
        if (flashLight != null) flashLight.enabled = visible;
    }
}