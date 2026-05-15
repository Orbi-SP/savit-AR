using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.ARFoundation;

public class Api : MonoBehaviour
{
    [Header("On-Device Hand Tracking")]
    public SavitGame.AR.MediaPipeHandTracker handTracker;

    [Header("Configurações API HTTP (fallback PC)")]
    public string apiURL = "http://127.0.0.1:5000";
    public CameraFeed cameraFeed;

    [Header("Cena")]
    public GameObject objectToMove;
    public enum SceneType { RAM, Gabinete, OutraCena }
    public SceneType currentScene = SceneType.RAM;

    // ══════ Saídas públicas (lidas por RAMModule, MotherboardPlacer, etc.) ══════
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

    // Estado interno
    private Vector3 originalPos;
    private Quaternion originalRot;
    private float accumulatedZ;
    private bool driveTransform;

    public bool IsOnDevice => handTracker != null && handTracker.enabled && handTracker.gameObject.activeInHierarchy;

    void Start()
    {
        // ──── Auto-setup: encontra ou CRIA o HandTracker ────
        if (handTracker == null)
            handTracker = FindFirstObjectByType<SavitGame.AR.MediaPipeHandTracker>();

        if (handTracker == null)
        {
            var arCam = FindFirstObjectByType<ARCameraManager>();
            if (arCam != null)
            {
                handTracker = arCam.gameObject.AddComponent<SavitGame.AR.MediaPipeHandTracker>();
                handTracker.arCameraManager = arCam;
                Debug.Log($"[Api] ✅ HandTracker CRIADO em '{arCam.gameObject.name}'");
            }
        }

        Debug.Log($"[Api] Start: tracker={(handTracker != null ? "OK" : "NULL")} scene={currentScene}");

        driveTransform = (currentScene == SceneType.RAM);
        if (driveTransform && objectToMove != null)
        {
            originalPos = objectToMove.transform.position;
            originalRot = objectToMove.transform.rotation;
            accumulatedZ = originalPos.z;
        }

        if (!IsOnDevice)
        {
            Debug.Log("[Api] Sem HandTracker — fallback HTTP");
            StartCoroutine(SendToApiRoutine());
        }
        else
        {
            Debug.Log("[Api] ✅ Usando HandTracker on-device");
        }
    }

    void Update()
    {
        // Ler dados do tracker on-device
        if (IsOnDevice)
        {
            isHolding = handTracker.IsHolding;
            currentSide = handTracker.CurrentSide;
            handPosX = handTracker.HandPositionX;
            handPosY = handTracker.HandPositionY;
        }

        // Notifica liberação
        if (prevHolding && !isHolding && currentHeld != null)
        {
            var releasable = currentHeld.GetComponent<IReleasable>();
            releasable?.OnRelease();
        }
        prevHolding = isHolding;

        // Movimento direto do objectToMove (só na cena RAM se configurado)
        if (!driveTransform || objectToMove == null) return;

        if (currentSide == "center")
            accumulatedZ = Mathf.Lerp(accumulatedZ, originalPos.z, Time.deltaTime * 5f);
        else if (currentSide == "right")
            accumulatedZ += 0.3f;
        else if (currentSide == "left")
            accumulatedZ -= 0.3f;

        float targetY = isHolding ? 5.5f : originalPos.y;
        objectToMove.transform.position = Vector3.Lerp(
            objectToMove.transform.position,
            new Vector3(originalPos.x, targetY, accumulatedZ),
            Time.deltaTime * 5f);

        objectToMove.transform.rotation = Quaternion.Lerp(
            objectToMove.transform.rotation,
            isHolding ? Quaternion.Euler(-90f, originalRot.eulerAngles.y, originalRot.eulerAngles.z) : originalRot,
            Time.deltaTime * 5f);
    }

    // ──── Fallback HTTP ────────────────────────────────────────────────────

    IEnumerator SendToApiRoutine()
    {
        while (true)
        {
            if (IsOnDevice) { Debug.Log("[Api] Tracker ativo — parando HTTP"); yield break; }

            if (cameraFeed == null) { yield return new WaitForSeconds(0.5f); continue; }

            var frame = cameraFeed.GetCurrentFrame();
            if (frame == null) { yield return new WaitForSeconds(0.5f); continue; }

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

                handPosX = currentSide == "left" ? 0.15f : currentSide == "right" ? 0.85f : 0.5f;
                handPosY = 0.5f;
            }

            yield return new WaitForSeconds(0.15f);
        }
    }
}
