using System.Collections.Generic;
using UnityEngine;

/// The city has ~1000 street lamps but a phone can only afford a handful of real lights.
/// This keeps a small pool of real spot lights and moves them to the lamps nearest the player,
/// fading them by distance so nothing pops. Every other lamp shows a cheap fake glow on the ground.
/// High-street LampPost prefabs join the pool too (their own lights are switched off at start
/// and their settings are copied, so they look the same as you tuned them).
public class LundunLampLights : MonoBehaviour
{
    [Tooltip("Lamp head positions, filled in by Lundun → 1  Build Greybox World")]
    [SerializeField] private Vector3[] lamps = new Vector3[0];
    [Tooltip("L5: each lamp's style (0 sodium, 1 sodium estate, 2 heritage, 3 LED), filled in by Lundun → 24")]
    [SerializeField] private int[] styles = new int[0];
    [Tooltip("L5: use each lamp's own colour (by district) instead of one colour for all")]
    [SerializeField] private bool colourByDistrict = true;

    [Header("Pool")]
    [SerializeField] private int poolSize = 8;
    [SerializeField] private float fullBrightWithin = 30f;
    [SerializeField] private float offBeyond = 55f;
    [SerializeField] private float refreshEvery = 0.2f;

    [Header("Light settings (copied from your LampPost prefab light if one is found)")]
    [SerializeField] private Color colour = new Color(1f, 0.70f, 0.35f);
    [SerializeField] private float intensity = 80f;
    [SerializeField] private float range = 20f;
    [SerializeField] private float spotAngle = 80f;

    /// 0..1, set by LundunLighting (lamps off in the day).
    public float Level { get; set; } = 1f;

    private readonly List<Vector3> all = new List<Vector3>();
    private readonly List<Color> colours = new List<Color>();
    private Light[] pool;
    private float[] bestDist;
    private int[] bestIndex;
    private VehicleSystem vehicles;
    private float next;

    public void SetLamps(Vector3[] positions) { lamps = positions; }
    /// L5: positions and styles together.
    public void SetLamps(Vector3[] positions, int[] lampStyles) { lamps = positions; styles = lampStyles; }
    /// Every lamp head (built lamps + the high street's), for the glow halos (V3a).
    public IReadOnlyList<Vector3> Positions { get { return all; } }
    public Color Colour { get { return colour; } }
    /// L5: each lamp's light colour, in the same order as Positions.
    public IReadOnlyList<Color> Colours { get { return colours; } }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        all.AddRange(lamps);
        for (int i = 0; i < lamps.Length; i++)
            colours.Add(colourByDistrict && styles != null && i < styles.Length ? LundunLampColours.ForStyle(styles[i]) : colour);

        // Take over the hand-placed high-street lamp lights
        bool copied = false;
        foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (l.type != LightType.Spot && l.type != LightType.Point) continue;
            if (l.transform.IsChildOf(transform)) continue;
            if (!IsLampPost(l.transform)) continue;
            if (!copied) { colour = l.color; intensity = l.intensity; range = l.range; if (l.type == LightType.Spot) spotAngle = l.spotAngle; copied = true; }
            all.Add(l.transform.position);
            colours.Add(colourByDistrict ? LundunLampColours.Sodium : colour);    // the old high street is in Peckam
            l.enabled = false;
        }

        poolSize = Mathf.Max(1, poolSize);
        pool = new Light[poolSize];
        bestDist = new float[poolSize];
        bestIndex = new int[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            GameObject g = new GameObject("LampLight_" + i);
            g.transform.SetParent(transform, false);
            g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);    // pointing straight down
            Light l = g.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = colour;
            l.range = range;
            l.spotAngle = spotAngle;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            l.enabled = false;
            pool[i] = l;
        }
    }

    static bool IsLampPost(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (p.name.StartsWith("LampPost")) return true;
        return false;
    }

    void Update()
    {
        if (pool == null || Time.time < next) return;
        next = Time.time + refreshEvery;

        Vector3 centre = vehicles != null ? vehicles.PlayerTarget.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);

        // nearest poolSize lamps (simple insertion; ~1000 checks, 5 times a second)
        for (int i = 0; i < poolSize; i++) { bestDist[i] = float.MaxValue; bestIndex[i] = -1; }
        float limit = offBeyond * offBeyond;
        for (int k = 0; k < all.Count; k++)
        {
            Vector3 d = all[k] - centre; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq > limit || sq >= bestDist[poolSize - 1]) continue;
            int j = poolSize - 1;
            while (j > 0 && bestDist[j - 1] > sq) { bestDist[j] = bestDist[j - 1]; bestIndex[j] = bestIndex[j - 1]; j--; }
            bestDist[j] = sq; bestIndex[j] = k;
        }

        for (int i = 0; i < poolSize; i++)
        {
            Light l = pool[i];
            if (bestIndex[i] < 0 || Level <= 0.01f) { l.enabled = false; continue; }
            float dist = Mathf.Sqrt(bestDist[i]);
            float fade = 1f - Mathf.InverseLerp(fullBrightWithin, offBeyond, dist);
            l.transform.position = all[bestIndex[i]];
            if (colourByDistrict) l.color = colours[bestIndex[i]];
            l.intensity = intensity * Level * fade;
            l.enabled = fade > 0.01f;
        }
    }
}
