using System.Collections.Generic;
using UnityEngine;

/// Turns LundunGunSynth's sounds into AudioClips (made once, on first use, then reused).
public static class LundunGunAudio
{
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    public static AudioClip Shot(LundunWeapons.Kind k)
    {
        switch (k)
        {
            case LundunWeapons.Kind.Smg: return Get("Smg", () => LundunGunSynth.MakeShot(LundunGunSynth.Smg));
            case LundunWeapons.Kind.Carbine: return Get("Carbine", () => LundunGunSynth.MakeShot(LundunGunSynth.Carbine));
            case LundunWeapons.Kind.Shotgun: return Get("Shotgun", () => LundunGunSynth.MakeShot(LundunGunSynth.Shotgun));
            case LundunWeapons.Kind.Fists: return null;
            default: return Get("Pistol", () => LundunGunSynth.MakeShot(LundunGunSynth.Pistol));
        }
    }

    public static AudioClip Pump => Get("Pump", LundunGunSynth.MakePump);
    public static AudioClip Dry => Get("Dry", LundunGunSynth.MakeDry);
    public static AudioClip MagOut => Get("MagOut", LundunGunSynth.MakeMagOut);
    public static AudioClip MagIn => Get("MagIn", LundunGunSynth.MakeMagIn);
    public static AudioClip Shell => Get("Shell", LundunGunSynth.MakeShell);

    /// Make them all now (call at start, so the first shot doesn't hitch).
    public static void Warm()
    {
        foreach (LundunWeapons.Kind k in System.Enum.GetValues(typeof(LundunWeapons.Kind))) Shot(k);
        AudioClip[] warm = { Pump, Dry, MagOut, MagIn, Shell };
    }

    static AudioClip Get(string name, System.Func<float[]> make)
    {
        if (clips.TryGetValue(name, out AudioClip c) && c != null) return c;
        float[] d = make();
        c = AudioClip.Create("LundunGun_" + name, d.Length, 1, LundunGunSynth.Rate, false);
        c.SetData(d, 0);
        clips[name] = c;
        return c;
    }
}
