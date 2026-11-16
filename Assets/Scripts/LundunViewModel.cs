using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// V1: first person, COD-style. While the camera is behind James's eyes (CameraFollow.FirstPersonNow):
///   his body       still there, but only as a shadow (head and all): nothing of him gets in the way of the view, and
///                  no more cut-off neck (the old way shrank his head away)
///   his arms       a copy of just his arms (his own sleeves and hands, cut from his model by Lundun → 33) on his own
///                  skeleton, so they're exactly where the gun rig puts his hands
///   the gun        framed from the eyes (LundunWeaponRig.FirstPersonPose): at the hip whenever a gun's out, sights up
///                  when aiming
///   own camera     arms and gun go on a layer of their own, drawn by a second camera stacked on top of the main one:
///                  they never poke into a wall or a car, and keep their own steady field of view while the world zooms
///   crosshair      fades out while aimed in (the sight is the crosshair)
/// Added to James by Lundun → 33 (with the arms), or by the gun rig at runtime (gun only, no arms) if that wasn't run.
[DefaultExecutionOrder(60)]
public class LundunViewModel : MonoBehaviour
{
    [Tooltip("Lundun → 33: just the arms of each of James's skinned meshes")]
    [SerializeField] private Mesh[] armMeshes = new Mesh[0];
    [Tooltip("Which of his skinned meshes each one was cut from (by name)")]
    [SerializeField] private string[] armSources = new string[0];

    [Header("View model camera")]
    [SerializeField] private float hipFov = 58f;
    [SerializeField] private float aimFov = 48f;

    /// First person's view model is showing right now.
    public static bool Active { get; private set; }
    /// There's a view model to switch to (so the camera needn't shrink his head away).
    public static bool Available { get; private set; }

    static int layer = -2;
    /// The layer the arms and gun are drawn on (a free one; -1 if none is free).
    public static int Layer { get { if (layer == -2) layer = PickLayer(); return layer; } }

    static int PickLayer()
    {
        int named = LayerMask.NameToLayer("FirstPerson");
        if (named >= 0) return named;
        for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
        return -1;
    }

    private readonly List<SkinnedMeshRenderer> arms = new List<SkinnedMeshRenderer>();
    private readonly List<Renderer> body = new List<Renderer>();
    private readonly List<ShadowCastingMode> bodyWas = new List<ShadowCastingMode>();
    private Camera main, vmCam;
    private bool on;
    private Graphic crosshair;
    private float crosshairAlpha = -1f;
    private bool lookedForCrosshair;

#if UNITY_EDITOR
    /// Lundun → 33.
    public void SetArms(Mesh[] meshes, string[] sources) { armMeshes = meshes; armSources = sources; }
#endif

    void Start()
    {
        Animator anim = GetComponentInChildren<Animator>();
        Transform model = anim != null ? anim.transform : transform;
        foreach (SkinnedMeshRenderer src in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            int i = System.Array.IndexOf(armSources, src.name);
            if (i < 0 || i >= armMeshes.Length || armMeshes[i] == null) continue;
            GameObject g = new GameObject(src.name + "_FPArms");
            g.transform.SetParent(src.transform.parent, false);
            g.transform.localPosition = src.transform.localPosition;
            g.transform.localRotation = src.transform.localRotation;
            g.transform.localScale = src.transform.localScale;
            SkinnedMeshRenderer smr = g.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = armMeshes[i];
            smr.bones = src.bones;
            smr.rootBone = src.rootBone;
            smr.sharedMaterials = src.sharedMaterials;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = false;
            smr.enabled = false;
            if (Layer >= 0) g.layer = Layer;
            arms.Add(smr);
        }
    }

    void OnEnable() { Available = Layer >= 0; }
    void OnDisable() { Switch(false); Available = false; }

    void LateUpdate()
    {
        bool want = CameraFollow.FirstPersonNow && isActiveAndEnabled;
        if (want != on) Switch(want);
        if (!on) return;
        float ads = LundunAim.Eased;
        if (vmCam != null) vmCam.fieldOfView = Mathf.Lerp(hipFov, aimFov, ads);
        if (crosshair != null && crosshairAlpha >= 0f)
        {
            Color c = crosshair.color;
            c.a = crosshairAlpha * (1f - ads);
            crosshair.color = c;
        }
    }

    void Switch(bool to)
    {
        on = to;
        Active = to;

        // his body: shadow only while we're behind his eyes
        if (to)
        {
            body.Clear(); bodyWas.Clear();
            Animator anim = GetComponentInChildren<Animator>();
            Transform model = anim != null ? anim.transform : transform;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (r is SkinnedMeshRenderer && arms.Contains((SkinnedMeshRenderer)r)) continue;
                body.Add(r); bodyWas.Add(r.shadowCastingMode);
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }
        else
        {
            for (int i = 0; i < body.Count; i++) if (body[i] != null) body[i].shadowCastingMode = bodyWas[i];
            body.Clear(); bodyWas.Clear();
        }

        foreach (SkinnedMeshRenderer a in arms) if (a != null) a.enabled = to;
        if (LundunWeaponRig.Instance != null) LundunWeaponRig.Instance.SetViewModel(to, Layer);
        SetUpCamera(to);
        Crosshair(to);
    }

    /// The second camera: on the main camera, drawing only the view-model layer, on top (its own depth).
    void SetUpCamera(bool to)
    {
        if (Layer < 0) return;
        if (main == null || !main.isActiveAndEnabled) main = Camera.main;
        if (main == null) return;
        main.cullingMask &= ~(1 << Layer);
        if (vmCam == null && to)
        {
            try
            {
                GameObject g = new GameObject("ViewModelCamera");
                g.transform.SetParent(main.transform, false);
                vmCam = g.AddComponent<Camera>();
                vmCam.cullingMask = 1 << Layer;
                vmCam.nearClipPlane = 0.01f;
                vmCam.farClipPlane = 5f;
                vmCam.fieldOfView = hipFov;
                vmCam.allowMSAA = main.allowMSAA;
                vmCam.allowHDR = main.allowHDR;
                UniversalAdditionalCameraData vd = vmCam.GetUniversalAdditionalCameraData();
                UniversalAdditionalCameraData md = main.GetUniversalAdditionalCameraData();
                vd.renderType = CameraRenderType.Overlay;
                vd.renderShadows = false;
                // (an overlay camera always starts from a clear depth buffer: the arms and gun draw over everything)
                vd.renderPostProcessing = md.renderPostProcessing;
                if (!md.cameraStack.Contains(vmCam)) md.cameraStack.Add(vmCam);
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        if (vmCam != null) vmCam.enabled = to;
    }

    /// The crosshair fades out while aimed in, and comes back as it was.
    void Crosshair(bool to)
    {
        if (!lookedForCrosshair)
        {
            lookedForCrosshair = true;
            GameObject g = GameObject.Find("Crosshair");
            if (g != null) crosshair = g.GetComponent<Graphic>();
        }
        if (crosshair == null) return;
        if (to) crosshairAlpha = crosshair.color.a;
        else if (crosshairAlpha >= 0f)
        {
            Color c = crosshair.color; c.a = crosshairAlpha; crosshair.color = c;
            crosshairAlpha = -1f;
        }
    }

    /// Lundun → 33 found arms to draw.
    public bool HasArms => armMeshes != null && armMeshes.Length > 0;
}
