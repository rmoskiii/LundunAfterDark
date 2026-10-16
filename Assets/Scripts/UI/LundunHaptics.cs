#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

/// Light haptic taps on iPhone (Assets/Plugins/iOS/LundunHaptics.mm). Does nothing in the editor or on other platforms.
public static class LundunHaptics
{
    public static bool Enabled = true;
    static float lastTime;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void LundunHapticImpact(int style);
#endif

    public static void Light() { Impact(0); }
    public static void Medium() { Impact(1); }
    public static void Heavy() { Impact(2); }

    static void Impact(int style)
    {
        if (!Enabled) return;
        if (Time.unscaledTime - lastTime < 0.05f) return;      // never a buzz-storm
        lastTime = Time.unscaledTime;
#if UNITY_IOS && !UNITY_EDITOR
        LundunHapticImpact(style);
#endif
    }
}
