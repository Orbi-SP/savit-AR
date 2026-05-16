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

    private float grabHandX;
    private float grabAccum;
    [Header("Snap")]
    public float maxSnapDistance = 0.2f; // ajuste conforme necessário

    void Start()
    {
        originalLocalPos = transform.localPosition;
        originalLocalRot = transform.localRotation;
        accum = 0f;

        // Auto-find Api se não atribuído no Inspector
            if (api == null)
            {
                var root = transform.root;
                if (root != null) api = root.GetComponentInChildren<Api>(true);
                if (api == null) api = FindFirstObjectByType<Api>();
            }

        // Esses objetos são movidos por script; física dinâmica aqui costuma causar jitter/"voar".
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (slots == null || slots.Length == 0)
            slots = FindObjectsByType<RAMSlot>(FindObjectsSortMode.None);

        useAxisX = true;

        Debug.Log($"[RAMModule] Start: api={(api != null ? "OK" : "NULL")} slots={slots?.Length ?? 0}");
    }

    void Update()
    {
        if (api == null) return;
        if (api.currentScene != Api.SceneType.RAM) return;
        if (isSnapped) return;

        bool holding = api.IsHolding;

        // Ao começar a segurar, captura o offset para evitar “teleporte”.
        if (holding && !prevHolding)
        {
            grabHandX = api.HandPositionX;
            grabAccum = accum;
        }

        if (holding)
        {
            // Movimento RELATIVO: a peça acompanha a variação da mão a partir do ponto de pega.
            // Isso evita inversão de direção e evita pulo inicial.
            float movementRange = (moveLimits.y - moveLimits.x);
            float targetAccum = grabAccum + (api.HandPositionX - grabHandX) * movementRange;
            accum = Mathf.Lerp(accum, targetAccum, Time.deltaTime * 10f);
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
        if (api != null && api.currentScene != Api.SceneType.RAM) return;
        TrySnapToNearestSlot();
    }

    void TrySnapToNearestSlot()
    {
        if (isSnapped) return;
        if (slots == null || slots.Length == 0) return;

        RAMSlot best = null;
        float bestDist = float.MaxValue;

        // Regra de snap: precisa estar ALINHADO (em cima do slot) e perto do anchor.
        foreach (var slot in slots)
        {
            if (slot == null || slot.snapAnchor == null) continue;

            // Alinhamento X/Z no espaço do slot (evita encaixar “no ar” só por distância).
            if (!slot.IsAlignedXZ(transform.position)) continue;

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

                // Registrar RAM no MotherboardState (não depender apenas da referência do slot)
                MotherboardState state = best.motherboard;
                if (state == null)
                {
                    var root = transform.root;
                    if (root != null) state = root.GetComponentInChildren<MotherboardState>(true);
                }
                if (state == null)
                {
                    state = FindFirstObjectByType<MotherboardState>();
                }

                if (state != null)
                {
                    Debug.Log($"[RAMModule] RegisterRam -> state='{state.name}' (slot='{best.name}')");
                    state.RegisterRam(this);
                }
                else
                {
                    Debug.LogWarning($"[RAMModule] ❌ Não achei MotherboardState para registrar RAM (slot='{best.name}').");
                }

            if (lockWhenSnapped)
            {
                var rb = GetComponent<Rigidbody>();
                if (rb) { rb.isKinematic = true; rb.useGravity = false; }
                var col = GetComponent<Collider>();
                if (col) col.enabled = false;
            }

            Debug.Log("✅ RAM encaixada no Anchor (como filho direto do snapAnchor)." );

            // 🔄 Trocar para cena do gabinete após RAM encaixada
            if (api != null)
            {
                var prev = api.currentScene;
                api.currentScene = Api.SceneType.Gabinete;
                Debug.Log($"[RAMModule] 🔄 currentScene {prev} → {api.currentScene}");
                Debug.Log("🔄 Cena trocada para Gabinete automaticamente.");
            }

        }
        else
        {
            Debug.Log("❌ RAM fora do slot / distante para snap.");
        }
    }

}
