using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RAMModule : MonoBehaviour, IReleasable
{
    [Header("Gestos / API")]
    public Api api;

    [Header("Movimento")]
    public bool useAxisX = true;
    public float moveSpeed = 2f;
    public float liftY = 6.0f;
    public Vector2 moveLimits = new Vector2(-0.4f, 0.4f);

    [Header("Slots")]
    public RAMSlot[] slots;
    public bool lockWhenSnapped = true;

    private Vector3 originalPos;
    private Quaternion originalRot;
    private float accum;
    private bool prevHolding;
    private bool isSnapped;
    [Header("Snap")]
    public float maxSnapDistance = 0.2f; // ajuste conforme necessário

    void Start()
    {
        originalPos = transform.position;
        originalRot = transform.rotation;
        accum = 0f;

        if (slots == null || slots.Length == 0)
            slots = FindObjectsOfType<RAMSlot>();

        useAxisX = true;
    }

    void Update()
    {
        if (api == null) return;
        if (isSnapped) return;

        bool holding = api.IsHolding;
        string side = api.CurrentSide;

        float dir = 0f;
        if (holding)
        {
            if (side == "right") dir = -1f;
            else if (side == "left") dir = +1f;

            accum += dir * moveSpeed * Time.deltaTime;
        }
        else
        {
            accum = Mathf.Lerp(accum, 0f, Time.deltaTime * 5f);
        }

        accum = Mathf.Clamp(accum, moveLimits.x, moveLimits.y);

        Vector3 currentPos = transform.position;

        float offsetX = useAxisX ? accum : 0f;
        float offsetZ = useAxisX ? 0f : accum;
        float targetX = originalPos.x + offsetX;
        float targetZ = originalPos.z + offsetZ;
        float targetY = holding ? liftY : originalPos.y;

        Vector3 finalTarget = new Vector3(targetX, targetY, targetZ);
        transform.position = Vector3.Lerp(currentPos, finalTarget, Time.deltaTime * 5f);

        // Rotação
        if (holding)
        {
            var targetRot = Quaternion.Euler(270f, originalRot.eulerAngles.y, originalRot.eulerAngles.z);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * 10f);
        }
        else
        {
            transform.rotation = Quaternion.Lerp(transform.rotation, originalRot, Time.deltaTime * 5f);
        }

        // Snap ao soltar
        if (prevHolding && !holding)
        {
            TrySnapToNearestSlot();
        }

        prevHolding = holding;
    }

    public void OnRelease()
    {
        TrySnapToNearestSlot();
    }

    void TrySnapToNearestSlot()
{
    if (isSnapped) return;
    if (slots == null || slots.Length == 0) return;

    RAMSlot best = null;
    float bestDist = float.MaxValue;

    foreach (var slot in slots)
    {
        if (slot == null || slot.snapAnchor == null) continue;

        float dist = Vector3.Distance(transform.position, slot.snapAnchor.position);

        if (dist < bestDist)
        {
            best = slot;
            bestDist = dist;
        }
    }

    // ⛔️ Só faz o snap se estiver suficientemente perto
    if (best != null && bestDist <= maxSnapDistance)
    {
        transform.SetParent(best.snapAnchor, worldPositionStays: false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
        Debug.Log("📌 RAM Parent = " + transform.parent?.name);

        isSnapped = true;

        if (best.motherboard != null)
            best.motherboard.RegisterRam(this);

        if (lockWhenSnapped)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb) { rb.isKinematic = true; rb.useGravity = false; }
            var col = GetComponent<Collider>();
            if (col) col.enabled = false;
        }

        Debug.Log("✅ RAM encaixada no Anchor (como filho direto do snapAnchor).");

        // 🔄 Trocar para cena do gabinete após RAM encaixada
        if (api != null)
        {
            api.currentScene = Api.SceneType.Gabinete;
            Debug.Log("🔄 Cena trocada para Gabinete automaticamente.");
        }

    }
    else
    {
        Debug.Log("❌ RAM muito distante para snap.");
    }
}

}
