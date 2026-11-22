using System.Collections.Generic;
using UnityEngine;

/// G8d: which of the city's meshes Lundun → 37 swapped for a copy without the old green crowns of the trees it made
/// half-bare or bare (the originals are never changed), so running 37 again, or 37b, can put each one back exactly.
/// Sits on the "LundunAutumn" object. Nothing reads it while the game runs.
public class LundunAutumnRecord : MonoBehaviour
{
    [System.Serializable]
    public class Swap { public MeshFilter filter; public Mesh was; }

    public List<Swap> swaps = new List<Swap>();
}
