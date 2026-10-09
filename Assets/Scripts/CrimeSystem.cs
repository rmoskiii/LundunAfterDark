using UnityEngine;

public enum CrimeType { Gunfire, AssaultPedestrian, HitAndRun, KillPedestrian }

// One place every crime gets reported to. Anything can call CrimeSystem.Report(...)
public static class CrimeSystem
{
    public static void Report(CrimeType type, Vector3 position)
    {
        if (WantedSystem.Instance != null)
            WantedSystem.Instance.AddCrime(type, position);
    }
}