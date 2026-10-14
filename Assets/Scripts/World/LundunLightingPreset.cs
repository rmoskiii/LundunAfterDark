using System;
using UnityEngine;

/// Everything that makes up one "look" of the city (night, dawn, day…).
/// Edit the values on the preset assets in Assets/World/Lighting while the scene is open — it updates live.
[Serializable]
public struct LightingLook
{
    [Header("Moon / sun")]
    public Color sunColor;
    public float sunIntensity;
    [Tooltip("x = height above the horizon (degrees), y = compass direction it shines from (degrees)")]
    public Vector2 sunAngles;
    [Range(0, 1)] public float shadowStrength;

    [Header("Fill light (lights everything the lamps don't reach)")]
    public Color ambientSky;
    public Color ambientHorizon;
    public Color ambientGround;

    [Header("Sky and fog (fog fades into the horizon colour)")]
    public Color skyTop;
    public Color skyHorizon;
    public Color skyBottom;
    public float fogStart;
    public float fogEnd;

    [Header("City lights (0 = off, 1 = normal)")]
    public float windowGlow;
    public float lampLevel;

    [Header("Camera grade")]
    public float postExposure;
    public float contrast;
    public float saturation;
    public float bloom;

    public static LightingLook Lerp(LightingLook a, LightingLook b, float t)
    {
        t = Mathf.Clamp01(t);
        LightingLook r;
        r.sunColor = Color.Lerp(a.sunColor, b.sunColor, t);
        r.sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t);
        r.sunAngles = new Vector2(Mathf.Lerp(a.sunAngles.x, b.sunAngles.x, t), Mathf.LerpAngle(a.sunAngles.y, b.sunAngles.y, t));
        r.shadowStrength = Mathf.Lerp(a.shadowStrength, b.shadowStrength, t);
        r.ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t);
        r.ambientHorizon = Color.Lerp(a.ambientHorizon, b.ambientHorizon, t);
        r.ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t);
        r.skyTop = Color.Lerp(a.skyTop, b.skyTop, t);
        r.skyHorizon = Color.Lerp(a.skyHorizon, b.skyHorizon, t);
        r.skyBottom = Color.Lerp(a.skyBottom, b.skyBottom, t);
        r.fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t);
        r.fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t);
        r.windowGlow = Mathf.Lerp(a.windowGlow, b.windowGlow, t);
        r.lampLevel = Mathf.Lerp(a.lampLevel, b.lampLevel, t);
        r.postExposure = Mathf.Lerp(a.postExposure, b.postExposure, t);
        r.contrast = Mathf.Lerp(a.contrast, b.contrast, t);
        r.saturation = Mathf.Lerp(a.saturation, b.saturation, t);
        r.bloom = Mathf.Lerp(a.bloom, b.bloom, t);
        return r;
    }

    /// Starting values for the three presets the Lundun menu creates.
    public static LightingLook Default(string which)
    {
        LightingLook l;
        switch (which)
        {
            case "Dawn":
                l.sunColor = new Color(1f, 0.62f, 0.42f); l.sunIntensity = 0.9f; l.sunAngles = new Vector2(8f, 95f); l.shadowStrength = 0.6f;
                l.ambientSky = new Color(0.55f, 0.52f, 0.62f); l.ambientHorizon = new Color(0.62f, 0.46f, 0.40f); l.ambientGround = new Color(0.20f, 0.18f, 0.16f);
                l.skyTop = new Color(0.25f, 0.35f, 0.60f); l.skyHorizon = new Color(0.95f, 0.62f, 0.46f); l.skyBottom = new Color(0.22f, 0.20f, 0.22f);
                l.fogStart = 150f; l.fogEnd = 430f;
                l.windowGlow = 0.5f; l.lampLevel = 0.4f;
                l.postExposure = 0.3f; l.contrast = 6f; l.saturation = 0f; l.bloom = 0.4f;
                return l;
            case "Day":
                l.sunColor = new Color(1f, 0.96f, 0.88f); l.sunIntensity = 1.3f; l.sunAngles = new Vector2(50f, 150f); l.shadowStrength = 0.8f;
                l.ambientSky = new Color(0.62f, 0.68f, 0.80f); l.ambientHorizon = new Color(0.55f, 0.55f, 0.55f); l.ambientGround = new Color(0.30f, 0.28f, 0.25f);
                l.skyTop = new Color(0.25f, 0.45f, 0.80f); l.skyHorizon = new Color(0.72f, 0.80f, 0.88f); l.skyBottom = new Color(0.40f, 0.40f, 0.42f);
                l.fogStart = 180f; l.fogEnd = 440f;
                l.windowGlow = 0f; l.lampLevel = 0f;
                l.postExposure = 0f; l.contrast = 5f; l.saturation = 0f; l.bloom = 0.25f;
                return l;
            default: // Night — London: purple-orange sky glow, bright blue fill so streets read
                l.sunColor = new Color(0.62f, 0.70f, 0.95f); l.sunIntensity = 0.8f; l.sunAngles = new Vector2(45f, 210f); l.shadowStrength = 0.6f;
                l.ambientSky = new Color(0.42f, 0.46f, 0.62f); l.ambientHorizon = new Color(0.36f, 0.31f, 0.36f); l.ambientGround = new Color(0.16f, 0.14f, 0.14f);
                l.skyTop = new Color(0.03f, 0.04f, 0.10f); l.skyHorizon = new Color(0.26f, 0.17f, 0.21f); l.skyBottom = new Color(0.06f, 0.05f, 0.06f);
                l.fogStart = 110f; l.fogEnd = 400f;
                l.windowGlow = 1.6f; l.lampLevel = 1f;
                l.postExposure = 0.6f; l.contrast = 8f; l.saturation = -8f; l.bloom = 0.6f;
                return l;
        }
    }
}

[CreateAssetMenu(menuName = "Lundun/Lighting Preset", fileName = "LightingPreset")]
public class LundunLightingPreset : ScriptableObject
{
    public LightingLook look = LightingLook.Default("Night");
}
