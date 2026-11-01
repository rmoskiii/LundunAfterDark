using UnityEngine;

/// V3: where a car's roof and nose are, measured from its model by Lundun → 10 (the model's mesh can't be read on the
/// phone). Used for things put on the car at runtime: the armed-response roof marker and push bar.
public class LundunCarShape : MonoBehaviour
{
    [Tooltip("The top middle of the roof (car space)")]
    [SerializeField] private Vector3 roof = new Vector3(0f, 1.4f, -0.3f);
    [SerializeField] private float roofWidth = 1.1f, roofLength = 1.4f;
    [Tooltip("The front of the car at bumper height (car space)")]
    [SerializeField] private Vector3 nose = new Vector3(0f, 0.5f, 2.3f);
    [SerializeField] private float width = 1.8f, height = 1.5f;

    public Vector3 Roof => roof;
    public float RoofWidth => roofWidth;
    public float RoofLength => roofLength;
    public Vector3 Nose => nose;
    public float Width => width;
    public float Height => height;

    public void Set(Vector3 roofTop, float roofW, float roofL, Vector3 front, float w, float h)
    {
        roof = roofTop; roofWidth = roofW; roofLength = roofL; nose = front; width = w; height = h;
    }
}
