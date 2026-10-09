using UnityEngine;
using UnityEngine.UI;

public class WantedUI : MonoBehaviour
{
    [SerializeField] private Image[] stars;
    [SerializeField] private Color litColour = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color unlitColour = new Color(0f, 0f, 0f, 0.35f);
    [SerializeField] private float flashSpeed = 5f;

    void Update()
    {
        WantedSystem wanted = WantedSystem.Instance;
        if (wanted == null) return;

        for (int i = 0; i < stars.Length; i++)
        {
            Color colour;

            if (wanted.Stars == 0)
                colour = Color.clear;                    // not wanted: hide the stars
            else if (i < wanted.Stars)
                colour = litColour;
            else
                colour = unlitColour;

            // Flash the lit stars while the police are searching (like GTA)
            if (i < wanted.Stars && wanted.IsSearching)
                colour.a = 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(Time.time * flashSpeed));

            stars[i].color = colour;
        }
    }
}