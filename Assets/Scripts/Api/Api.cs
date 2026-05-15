using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.ARFoundation;

public class Api : MonoBehaviour
{
    [Header("On-Device Hand Tracking (preferencial no celular)")]
    [Tooltip("Auto-preenchido em runtime se vazio.")]
    public SavitGame.AR.MediaPipeHandTracker handTracker;

    [Header("Configurações API HTTP (fallback para PC/Editor)")]
    public string apiURL = "http://127.0.0.1:5000";
    public CameraFeed cameraFeed;

    [Header("Objeto a controlar (apenas se a cena exigir)")]
    public GameObject objectToMove;
    public enum SceneType { RAM, Gabinete, OutraCena }
    public SceneType currentScene = SceneType.RAM;

    // ==== Saídas de gesto para outros scripts ====
    private bool isHolding = false;
    private string currentSide = "center";
    private float handPosX = 0.5f;
    private float handPosY = 0.5f;

    public bool IsHolding => isHolding;
    public string CurrentSide => currentSide;
    public float HandPositionX => handPosX;
    public float HandPositionY => handPosY;
    public GameObject currentHeld;
    bool prevHolding = false;

    // ==== Estado interno (só usado se o Api controla o objeto) ====
    private Vector3 originalPos;
    private Quaternion originalRot;
    private float accumulatedZ;
    private bool driveTransform;

    /// <summary>
    /// True when using on-device MediaPipe HandTracker (no HTTP needed).
    /// </summary>
    public bool IsOnDevice => handTracker != null && handTracker.enabled && handTracker.gameObject.activeInHierarchy;

    void Start()
    {
        // ──── Auto-setup: encontra ou cria o HandTracker automaticamente ────
        if (handTracker == null)
            handTracker = FindFirstObjectByType<SavitGame.AR.MediaPipeHandTracker>();

        // Se ainda não existe, tenta criar automaticamente no celular
        if (handTracker == null)
        {
            var arCamMgr = FindFirstObjectByType<ARCameraManager>();
            if (arCamMgr != null)
            {
                // Cria o MediaPipeHandTracker no mesmo GameObject do ARCameraManager
                handTracker = arCamMgr.gameObject.AddComponent<SavitGame.AR.MediaPipeHandTracker>();
                handTracker.arCameraManager = arCamMgr;
                Debug.Log($"[Api] ✅ MediaPipeHandTracker CRIADO automaticamente em '{arCamMgr.gameObject.name}'");
            }
            else
            {
                Debug.LogWarning("[Api] ARCameraManager não encontrado — HandTracker não pode ser criado. Usando fallback HTTP.");
            }
        }

        Debug.Log($"[Api] Start: handTracker={(handTracker != null ? handTracker.gameObject.name : "NULL")} " +
                  $"IsOnDevice={IsOnDevice} currentScene={currentScene}");

        driveTransform = (currentScene == SceneType.RAM);

        if (driveTransform && objectToMove != null)
        {
            originalPos = objectToMove.transform.position;
            originalRot = objectToMove.transform.rotation;
            accumulatedZ = originalPos.z;
        }

        // Só inicia polling HTTP se não tiver tracker on-device
        if (!IsOnDevice)
        {
            Debug.Log("[Api] Sem HandTracker on-device — usando fallback HTTP para " + apiURL);
            StartCoroutine(SendToApiRoutine());
        }
        else
        {
            Debug.Log("[Api] Usando MediaPipe HandTracker on-device (sem servidor HTTP).");
        }
    }

    void Update()
    {
        // Se usando on-device, lê dados do tracker local a cada frame
        if (IsOnDevice)
        {
            isHolding = handTracker.IsHolding;
            currentSide = handTracker.CurrentSide;
            handPosX = handTracker.HandPositionX;
            handPosY = handTracker.HandPositionY;
        }

        // Notifica liberação (transição prevHolding -> !isHolding)
        if (prevHolding && !isHolding)
        {
            if (currentHeld != null)
            {
                var releasable = currentHeld.GetComponent<IReleasable>();
                if (releasable != null)
                {
                    releasable.OnRelease();
                }
            }
        }

        prevHolding = isHolding;

        // Se o Api não deve mover o objectToMove, saia cedo
        if (!driveTransform) return;
        if (objectToMove == null) return;

        // Movimento simples baseado em currentSide / isHolding
        if (currentSide == "center")
            accumulatedZ = Mathf.Lerp(accumulatedZ, originalPos.z, Time.deltaTime * 5f);
        else if (currentSide == "right")
            accumulatedZ += 0.3f;
        else if (currentSide == "left")
            accumulatedZ -= 0.3f;

        float targetY = isHolding ? 5.5f : originalPos.y;
        Vector3 targetPos = new Vector3(originalPos.x, targetY, accumulatedZ);

        objectToMove.transform.position =
            Vector3.Lerp(objectToMove.transform.position, targetPos, Time.deltaTime * 5f);

        Quaternion targetRot = isHolding
            ? Quaternion.Euler(-90f, originalRot.eulerAngles.y, originalRot.eulerAngles.z)
            : originalRot;

        objectToMove.transform.rotation =
            Quaternion.Lerp(objectToMove.transform.rotation, targetRot, Time.deltaTime * 5f);
    }

    // ──── Fallback HTTP (usado apenas no PC/Editor) ────────────────────────

    IEnumerator SendToApiRoutine()
    {
        while (true)
        {
            // Se o tracker on-device ficou disponível, para o polling HTTP
            if (IsOnDevice)
            {
                Debug.Log("[Api] HandTracker on-device detectado — parando polling HTTP.");
                yield break;
            }

            if (cameraFeed == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            var frame = cameraFeed.GetCurrentFrame();
            if (frame == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            byte[] imageBytes = frame.EncodeToJPG();
            Destroy(frame);

            var www = new UnityWebRequest(apiURL, "POST");
            www.uploadHandler = new UploadHandlerRaw(imageBytes);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/octet-stream");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string response = www.downloadHandler.text.Trim().ToLower();
                string[] parts = response.Split(' ');

                if (parts.Length >= 2)
                {
                    isHolding = (parts[0] == "hold");
                    currentSide = parts[1];
                }
                else
                {
                    isHolding = (response == "hold");
                    currentSide = "center";
                }

                // HTTP não retorna posição contínua — usar mapeamento discreto
                if (currentSide == "left") handPosX = 0.15f;
                else if (currentSide == "right") handPosX = 0.85f;
                else handPosX = 0.5f;
                handPosY = 0.5f;
            }
            else
            {
                Debug.LogWarning("Erro na API: " + www.error);
            }

            yield return new WaitForSeconds(0.15f);
        }
    }
}
