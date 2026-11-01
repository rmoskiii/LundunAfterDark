using UnityEngine;

/// L3: on James's body (LundunPopulation adds it). His character controller reports the moment it touches something;
/// when that's a person, the bump happens there and then, on contact, instead of waiting for the next distance check.
/// Ground, kerbs and walls are skipped cheaply (each collider is only looked at once to see whether it's a person).
public class LundunBumpSensor : MonoBehaviour
{
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == null || hit.normal.y > 0.6f) return;          // what he's standing or stepping on
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop != null) pop.Touched(hit.collider, false);
    }
}
