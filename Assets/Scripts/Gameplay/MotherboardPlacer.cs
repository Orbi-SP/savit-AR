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

    [Header("Debug")]
    public bool drawSnapGizmo = true;   // liga/desliga gizmo

    private Vector3 originalPos;
    private Quaternion originalRot;
    private float accum;
    private bool prevHolding;
    private bool isSnapped;

    void Start()
    {
        originalPos = transform.position;
        originalRot = transform.rotation;
        accum = 0f;
        
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
    }

    void Update()
    {
        if (isSnapped || api == null) return;

        bool holding = api.IsHolding;
        string side = api.CurrentSide;

        // move lateral só enquanto segura
        float dir = holding ? (side == "right" ? -1f : side == "left" ? +1f : 0f) : 0f;
        accum += dir * moveSpeed * Time.deltaTime;

        if (holding && side == "center")
            accum = Mathf.Lerp(accum, 0f, Time.deltaTime * 5f);

        accum = Mathf.Clamp(accum, moveLimits.x, moveLimits.y);

        Vector3 target = originalPos;
        if (useAxisX) target.x = originalPos.x + accum;
        else          target.z = originalPos.z + accum;
        target.y = holding ? liftY : originalPos.y;

        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 5f);
        transform.rotation = originalRot;

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

    void OnDrawGizmosSelected()
    {
        if (!drawSnapGizmo || snapZone == null) return;

        // ⚠️ Não multiplica por lossyScale AQUI, pois a matrix já contém a escala
        Gizmos.color = Color.green;
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = snapZone.transform.localToWorldMatrix;

        // desenha um wire cube com tolerância extra no plano X/Z
        Vector3 sizeWithTol = new Vector3(
            snapZone.size.x + 2f * snapTolerance,
            snapZone.size.y, // Y não importa pro teste, mas desenhamos pra visualizar
            snapZone.size.z + 2f * snapTolerance
        );

        Gizmos.DrawWireCube(Vector3.zero, sizeWithTol);

        Gizmos.matrix = prev;
    }
}