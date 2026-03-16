using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class RAMSlot : MonoBehaviour
{
    [Header("Snap")]
    public Transform snapAnchor;               // 🟢 Nome padrão para funcionar com RAMModule
    public float snapTolerance = 0.06f;

    [Header("Referências")]
    public MotherboardState motherboard;

    private BoxCollider box;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;

        if (motherboard == null)
        {
            motherboard = GetComponentInParent<MotherboardState>();
        }
    }

    public bool IsAlignedXZ(Vector3 worldPos)
    {
        if (box == null) return false;

        Vector3 local = transform.InverseTransformPoint(worldPos);
        Vector3 half = box.size * 0.5f;

        bool insideX = Mathf.Abs(local.x) <= (half.x + snapTolerance);
        bool insideZ = Mathf.Abs(local.z) <= (half.z + snapTolerance);

        return insideX && insideZ;
    }
}
