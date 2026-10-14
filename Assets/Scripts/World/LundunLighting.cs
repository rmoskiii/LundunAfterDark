using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Applies a lighting preset to the whole scene: moon/sun, fill light, sky, fog, window glow, lamps and camera grade.
/// Lives on the "LundunLighting" object (made by Lundun → 1  Build Greybox World).
/// In the editor it re-applies live, so you can tweak the preset and see it straight away.
/// Pass 4 (day/night clock) will call Apply(LightingLook) with a blend between presets.
[ExecuteAlways]
public class LundunLighting : MonoBehaviour
{
    [Tooltip("The look to show. Swap Night / Dawn / Day here, or edit the preset itself.")]
    public LundunLightingPreset preset;

    [Header("Filled in by the Lundun menu")]
    [SerializeField] private Light sun;
    [SerializeField] private Volume grade;
    [SerializeField] private Material sky;
    [SerializeField] private Material[] windowMaterials;
    [SerializeField] private Material lampHeadMaterial;
    [SerializeField] private Material lightPoolMaterial;
    [SerializeField] private LundunLampLights lampLights;

    [Header("Strengths at lampLevel = 1")]
    [SerializeField] private float lightPoolStrength = 0.7f;
    [SerializeField] private Color lampGlow = new Color(1f, 0.82f, 0.5f);
    [SerializeField] private float lampGlowStrength = 2.5f;

    public LightingLook Current { get; private set; }
    private bool appliedInPlay;

    void OnEnable() { if (preset != null) Apply(preset.look); }

    void Update()
    {
        // Edit mode: follow the preset live. Play mode: apply once; the clock (Pass 4) takes over later.
        if (preset == null) return;
        if (!Application.isPlaying) Apply(preset.look);
        else if (!appliedInPlay) { Apply(preset.look); appliedInPlay = true; }
    }

    public void Apply(LightingLook l)
    {
        Current = l;
        if (sun != null)
        {
            sun.color = l.sunColor;
            sun.intensity = l.sunIntensity;
            sun.shadowStrength = l.shadowStrength;
            sun.transform.rotation = Quaternion.Euler(l.sunAngles.x, l.sunAngles.y, 0f);
            RenderSettings.sun = sun;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = l.ambientSky;
        RenderSettings.ambientEquatorColor = l.ambientHorizon;
        RenderSettings.ambientGroundColor = l.ambientGround;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = l.skyHorizon;
        RenderSettings.fogStartDistance = l.fogStart;
        RenderSettings.fogEndDistance = l.fogEnd;

        if (sky != null)
        {
            sky.SetColor("_TopColor", l.skyTop);
            sky.SetColor("_HorizonColor", l.skyHorizon);
            sky.SetColor("_BottomColor", l.skyBottom);
            RenderSettings.skybox = sky;
        }

        if (windowMaterials != null)
            foreach (Material m in windowMaterials)
                if (m != null) m.SetColor("_EmissionColor", Color.white * l.windowGlow);
        if (lampHeadMaterial != null) lampHeadMaterial.SetColor("_EmissionColor", lampGlow * (lampGlowStrength * l.lampLevel));
        if (lightPoolMaterial != null) lightPoolMaterial.SetFloat("_Intensity", lightPoolStrength * l.lampLevel);
        if (lampLights != null) lampLights.Level = l.lampLevel;

        if (grade != null)
        {
            VolumeProfile p = Application.isPlaying ? grade.profile : grade.sharedProfile;
            if (p != null)
            {
                ColorAdjustments ca;
                if (p.TryGet(out ca))
                {
                    ca.postExposure.Override(l.postExposure);
                    ca.contrast.Override(l.contrast);
                    ca.saturation.Override(l.saturation);
                }
                Bloom b;
                if (p.TryGet(out b)) b.intensity.Override(l.bloom);
            }
        }
    }
}
