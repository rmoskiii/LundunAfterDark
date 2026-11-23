using System.Collections.Generic;
using UnityEngine;

/// G8e: what Lundun → 42 (walk-down Tube entrances) changed, so running 42 again, or 42b, puts it all back exactly:
/// the meshes swapped for copies with the stairwells cut out of the pavement (the originals are never changed), and
/// the old solid boxes over the dressing's entrances that it switched off. Sits on "LundunTubeStairs".
public class LundunTubeStairsRecord : MonoBehaviour
{
    [System.Serializable]
    public class Swap { public MeshFilter filter; public MeshCollider collider; public Mesh was; }

    public List<Swap> swaps = new List<Swap>();
    public List<Collider> switchedOff = new List<Collider>();
}
