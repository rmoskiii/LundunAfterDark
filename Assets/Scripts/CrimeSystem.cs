using UnityEngine;

public enum CrimeType
{
    Gunfire,
    AssaultPedestrian,
    HitAndRun,
    KillPedestrian,
    AssaultOfficer,
    KillOfficer,
    DestroyPoliceCar,
    Shoplifting,         // E5: stealing from a shop (only counts if someone sees it)
    Trespass,            // E8: inside the palace railings or the barracks (the guards and cameras always see it)
    CarTheft             // G8a: breaking into a parked car (only counts if someone sees or hears it)
}

// One place every crime gets reported to. Anything can call CrimeSystem.Report(...)
public static class CrimeSystem
{
    /// G8.0 (notify only, for the incident log): every crime reported here, seen or not, before it's passed on.
    public static event System.Action<CrimeType, Vector3> Reported;

    public static void Report(CrimeType type, Vector3 position)
    {
        try { Reported?.Invoke(type, position); } catch (System.Exception e) { Debug.LogException(e); }   // G8.0: notify only
        if (WantedSystem.Instance != null)
            WantedSystem.Instance.AddCrime(type, position);
    }
}
