using UnityEngine;

public class MotherboardPlacer : MonoBehaviour
{
    [Header("Referências")]
    public Api api;
    public BoxCollider snapZone;     // BoxCollider (IsTrigger) da área de encaixe
    public Transform snapAnchor;     // Transform final da placa
    public MotherboardState motherboardState; // arraste aqui no Inspector
    
    [Header("Câmeras")]
    public Camera secondCamera;      // Câmera da cena do gabinete
    public Camera desktopCamera;     // Câmera do desktop (DesktopCamera)
    
    [Header("Movimento")]
    public bool useAxisX = true;
    public float moveSpeed = 2.0f;
    public float liftY = 6.5f;
    public Vector2 moveLimits = new Vector2(-0.5f, 0.5f);

    [Header("Snap")]
    [Tooltip("Margem de tolerância no plano X/Z para encaixe.")]
    public float snapTolerance = 0.05f;



    private Vector3 originalPos;
    private Quaternion originalRot;
    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;
    private float accum;
    private bool prevHolding;
    private bool isSnapped;

    private void ResolveRefsIfNeeded()
    {
        var root = transform.root;

        if (api == null && root != null) api = root.GetComponentInChildren<Api>(true);
        if (api == null) api = FindFirstObjectByType<Api>();

        if (motherboardState == null && root != null) motherboardState = root.GetComponentInChildren<MotherboardState>(true);
        if (motherboardState == null) motherboardState = FindFirstObjectByType<MotherboardState>();
    }

    void Start()
    {
        originalPos = transform.position;
        originalRot = transform.rotation;
        originalLocalPos = transform.localPosition;
        originalLocalRot = transform.localRotation;
        accum = 0f;

        // Auto-find referências se não atribuídas no Inspector
        ResolveRefsIfNeeded();

        // Movimentação é por script; física dinâmica aqui tende a dar instabilidade.
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        
        // Garantir que no início da cena Gabinete:
        // - Second Camera está ativa (para ver o gabinete)
        // - Desktop Camera está desativada (só ativa depois do snap)
        if (secondCamera != null)
        {
            secondCamera.enabled = true;
            Debug.Log("🎥 Start: Second Camera ATIVADA (cena Gabinete)");
        }
        
        if (desktopCamera != null)
        {
            desktopCamera.enabled = false;
            Debug.Log("🎥 Start: Desktop Camera DESATIVADA (aguardando snap)");
        }

        Debug.Log($"[MotherboardPlacer] Start: api={(api != null ? "OK" : "NULL")} motherboardState={(motherboardState != null ? "OK" : "NULL")}");
    }

    private void OnEnable()
    {
        Debug.Log("[MotherboardPlacer] OnEnable");
        ResolveRefsIfNeeded();
    }

    void Update()
    {
        if (isSnapped) return;
        ResolveRefsIfNeeded();
        if (api == null) return;
        if (api.currentScene != Api.SceneType.Gabinete) return;

        // Antes da RAM estar instalada, a placa-mãe não deve sequer “levantar/mover”.
        bool requirementsOk = motherboardState == null || motherboardState.HasRequiredMemory;

        bool holding = api.IsHolding;
        if (!requirementsOk) holding = false;

        // Posição contínua: mapeia HandPositionX (0-1) para os limites de movimento
        if (holding)
        {
            float targetAccum = Mathf.Lerp(moveLimits.y, moveLimits.x, api.HandPositionX);
            accum = Mathf.Lerp(accum, targetAccum, Time.deltaTime * 8f);
        }
        else
        {
            accum = Mathf.Lerp(accum, 0f, Time.deltaTime * 5f);
        }

        accum = Mathf.Clamp(accum, moveLimits.x, moveLimits.y);

        Vector3 targetLocal = originalLocalPos;
        if (useAxisX) targetLocal.x = originalLocalPos.x + accum;
        else          targetLocal.z = originalLocalPos.z + accum;
        targetLocal.y = holding ? liftY : originalLocalPos.y;

        transform.localPosition = Vector3.Lerp(transform.localPosition, targetLocal, Time.deltaTime * 5f);
        transform.localRotation = originalLocalRot;

        // snap só ao SOLTAR e se estiver alinhado em X/Z com a zona
        if (prevHolding && !holding && IsAlignedXZ(transform.position))
        {
            if (motherboardState != null && !motherboardState.HasRequiredMemory)
            {
                Debug.Log("Instale a memória RAM na placa antes de colocá-la no gabinete.");
                prevHolding = holding;
                return;
            }
            DoSnap();
            return;
        }
        prevHolding = holding;
    }

    // ✔️ Checagem em X/Z no espaço local da zona (NÃO multiplica por lossyScale)
    bool IsAlignedXZ(Vector3 worldPos)
    {
        if (snapZone == null) return false;

        Vector3 local = snapZone.transform.InverseTransformPoint(worldPos);
        Vector3 half = snapZone.size * 0.5f;

        bool insideX = Mathf.Abs(local.x) <= (half.x + snapTolerance);
        bool insideZ = Mathf.Abs(local.z) <= (half.z + snapTolerance);

        return insideX && insideZ;
    }

    void DoSnap()
    {
        Debug.Log("=== DoSnap INICIADO ===");
        
        if (snapAnchor != null)
        {
            transform.SetPositionAndRotation(snapAnchor.position, snapAnchor.rotation);
        }
        else
        {
            Vector3 p = transform.position;
            transform.SetPositionAndRotation(
                new Vector3(p.x, originalPos.y, p.z),
                Quaternion.Euler(-90f, originalRot.eulerAngles.y, originalRot.eulerAngles.z)
            );
        }

        isSnapped = true;
        Debug.Log("✅ Motherboard encaixada (Snap aplicado).");
        
        // Trocar câmeras após 3 segundos
        Debug.Log("Aguardando 3 segundos antes de trocar câmeras...");
        StartCoroutine(SwitchCamerasAfterDelay(3f));
        
        Debug.Log("=== DoSnap FINALIZADO ===");
    }
    
    System.Collections.IEnumerator SwitchCamerasAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Debug.Log("Chamando SwitchCameras()...");
        SwitchCameras();
    }
    
    void SwitchCameras()
    {
        Debug.Log("=== SwitchCameras INICIADO ===");
        
        if (secondCamera != null)
        {
            Debug.Log($"Second Camera antes: enabled = {secondCamera.enabled}");
            secondCamera.enabled = false;
            Debug.Log($"🎥 Second Camera desativada. Enabled agora = {secondCamera.enabled}");
        }
        else
        {
            Debug.LogWarning("⚠️ Second Camera não está atribuída no Inspector!");
        }
        
        if (desktopCamera != null)
        {
            Debug.Log($"Desktop Camera antes: enabled = {desktopCamera.enabled}");
            desktopCamera.enabled = true;
            Debug.Log($"🎥 Desktop Camera ativada. Enabled agora = {desktopCamera.enabled}");
        }
        else
        {
            Debug.LogWarning("⚠️ Desktop Camera não está atribuída no Inspector!");
        }
        
        Debug.Log("=== SwitchCameras FINALIZADO ===");
    }

}