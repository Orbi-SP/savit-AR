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

    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;
    private float accum;
    private bool prevHolding;
    private bool isSnapped;
    [Header("Snap")]
    public float maxSnapDistance = 0.2f; // ajuste conforme necessário

    void Start()
    {
        originalLocalPos = transform.localPosition;
        originalLocalRot = transform.localRotation;
        accum = 0f;

        // Esses objetos são movidos por script; física dinâmica aqui costuma causar jitter/"voar".
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (slots == null || slots.Length == 0)
            slots = FindObjectsOfType<RAMSlot>();

        useAxisX = true;
    }

    void Update()
    {
        if (api == null) return;
        if (isSnapped) return;

        bool holding = api.IsHolding;

        if (holding)
        {
            // Posição contínua: mapeia HandPositionX (0-1) para os limites de movimento
            // 0 = limite esquerdo (moveLimits.x), 1 = limite direito (moveLimits.y)
            float targetAccum = Mathf.Lerp(moveLimits.y, moveLimits.x, api.HandPositionX);
            accum = Mathf.Lerp(accum, targetAccum, Time.deltaTime * 8f);
        }
        else
        {
            accum = Mathf.Lerp(accum, 0f, Time.deltaTime * 5f);
        }

        accum = Mathf.Clamp(accum, moveLimits.x, moveLimits.y);

        Vector3 currentLocalPos = transform.localPosition;

        float offsetX = useAxisX ? accum : 0f;
        float offsetZ = useAxisX ? 0f : accum;
        float targetX = originalLocalPos.x + offsetX;
        float targetZ = originalLocalPos.z + offsetZ;
        float targetY = holding ? liftY : originalLocalPos.y;

        Vector3 finalTarget = new Vector3(targetX, targetY, targetZ);
        transform.localPosition = Vector3.Lerp(currentLocalPos, finalTarget, Time.deltaTime * 5f);

        // Rotação
        if (holding)
        {
            var targetLocalRot = Quaternion.Euler(270f, originalLocalRot.eulerAngles.y, originalLocalRot.eulerAngles.z);
            transform.localRotation = Quaternion.Lerp(transform.localRotation, targetLocalRot, Time.deltaTime * 10f);
        }
        else
        {
            transform.localRotation = Quaternion.Lerp(transform.localRotation, originalLocalRot, Time.deltaTime * 5f);
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
