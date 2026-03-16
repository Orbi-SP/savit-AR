using UnityEngine;

public class CameraFeed : MonoBehaviour
{
    private WebCamTexture webcamTexture;
    public Vector2 size = new Vector2(0.9f, 0.6f); // Largura x Altura

    [Header("Posicionamento Personalizado")]
    public bool useCustomPosition = false;
    public Vector2 viewportPosition = new Vector2(0f, 1f); // (0,1) = canto superior esquerdo

    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;

        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("Nenhuma webcam detectada!");
            return;
        }

        webcamTexture = new WebCamTexture();
        webcamTexture.Play();

        Debug.Log("Webcam iniciada: " + webcamTexture.deviceName);

        Renderer renderer = GetComponent<Renderer>();
        renderer.material.mainTexture = webcamTexture;

        PositionInCorner();
    }

    void PositionInCorner()
    {
        if (mainCamera == null) return;

        // Permite escolher a posição da webcam na tela
        Vector3 viewportPos = useCustomPosition
            ? new Vector3(viewportPosition.x, viewportPosition.y, 1f)
            : new Vector3(0f, 1f, 1f); // default = canto superior esquerdo

        Vector3 worldPosition = mainCamera.ViewportToWorldPoint(viewportPos);
        transform.position = worldPosition;

        // Aplica inversão horizontal (espelho)
        transform.localScale = new Vector3(-size.x, size.y, 1f);
        transform.rotation = mainCamera.transform.rotation;
    }

    public Texture2D GetCurrentFrame()
    {
        if (webcamTexture == null || !webcamTexture.isPlaying)
            return null;

        Texture2D snap = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGB24, false);
        snap.SetPixels(webcamTexture.GetPixels());
        snap.Apply();
        return snap;
    }

    void OnDisable()
    {
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            webcamTexture.Stop();
        }
    }
}