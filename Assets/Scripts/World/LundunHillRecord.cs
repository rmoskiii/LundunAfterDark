using System.Collections.Generic;
using UnityEngine;

/// G7: what Lundun → 40 (the hills) changed in the city, so Lundun → 40b can put every bit of it back exactly, and so
/// running 40 again starts from the flat city instead of raising the ground twice. Sits on the "LundunHills" object.
/// G7b: it also keeps the hills' heights, so things placed while the game runs (traffic, people, bikes, police) can
/// stand on the ground rather than inside a hill (LundunGround.Height).
public class LundunHillRecord : MonoBehaviour
{
    [System.Serializable]
    public class Lifted { public Transform what; public Vector3 by; }

    [System.Serializable]
    public class Bent { public MeshFilter filter; public MeshCollider collider; public Mesh was; }

    [System.Serializable]
    public class Prop { public LundunSmash smash; public int index; public float by; }      // (G7a's: still put back)

    [System.Serializable]
    public class Setting { public Component on; public string path; public float by; }     // a position in a script's settings

    [System.Serializable]
    public class Ground { public string name; public float x0, z0, cell; public int nx, nz; public float[] h; }

    [Tooltip("Which hills, how high their tops are, how steep they get")]
    public string summary = "";
    public List<Lifted> lifted = new List<Lifted>();
    public List<Bent> bent = new List<Bent>();
    public List<Prop> props = new List<Prop>();
    public List<Setting> settings = new List<Setting>();
    public List<Ground> ground = new List<Ground>();

    void Awake() { LundunGround.Use(this); }
    void OnEnable() { LundunGround.Use(this); }
    void OnDestroy() { LundunGround.Forget(this); }
}
