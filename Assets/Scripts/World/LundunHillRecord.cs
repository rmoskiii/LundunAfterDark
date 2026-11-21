using System.Collections.Generic;
using UnityEngine;

/// G7a: what Lundun → 40 (the hills) changed in the city, so Lundun → 40b can put every bit of it back exactly, and so
/// running 40 again starts from the flat city instead of raising the ground twice. Sits on the "LundunHills" object.
/// Nothing reads it while the game runs.
public class LundunHillRecord : MonoBehaviour
{
    [System.Serializable]
    public class Lifted { public Transform what; public Vector3 by; }

    [System.Serializable]
    public class Bent { public MeshFilter filter; public MeshCollider collider; public Mesh was; }

    [System.Serializable]
    public class Prop { public LundunSmash smash; public int index; public float by; }

    [Tooltip("Which hills, how high their tops are, how steep they get")]
    public string summary = "";
    public List<Lifted> lifted = new List<Lifted>();
    public List<Bent> bent = new List<Bent>();
    public List<Prop> props = new List<Prop>();
}
