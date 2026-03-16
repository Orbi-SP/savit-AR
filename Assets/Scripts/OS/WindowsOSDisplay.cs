using UnityEngine;
using SavitGame.OS.Network;

namespace SavitGame.OS {
    public class WindowsOSDisplay : MonoBehaviour {
        [Header("Display Settings")]
        public Material monitorScreenMaterial;
        public RenderTexture screenRenderTexture;
        public Camera osCamera;
        
        [Header("OS Components")]
        public Canvas osCanvas;
        public WindowsNetworkConfig networkConfig;
        
        [Header("Screen Resolution")]
        public int screenWidth = 1920;
        public int screenHeight = 1080;
        
        private void Start() {
            SetupRenderTexture();
            SetupOSCamera();
            SetupCanvas();
        }
        
        private void SetupRenderTexture() {
            if (screenRenderTexture == null) {
                screenRenderTexture = new RenderTexture(screenWidth, screenHeight, 24);
                screenRenderTexture.name = "OS_Screen";
            }
            
            if (monitorScreenMaterial != null) {
                monitorScreenMaterial.mainTexture = screenRenderTexture;
                
                // Configurar emissão para o material brilhar
                monitorScreenMaterial.EnableKeyword("_EMISSION");
                monitorScreenMaterial.SetTexture("_EmissionMap", screenRenderTexture);
                monitorScreenMaterial.SetColor("_EmissionColor", Color.white);
            }
        }
        
        private void SetupOSCamera() {
            if (osCamera != null) {
                osCamera.targetTexture = screenRenderTexture;
                osCamera.clearFlags = CameraClearFlags.SolidColor;
                osCamera.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
                osCamera.cullingMask = LayerMask.GetMask("UI");
            }
        }
        
        private void SetupCanvas() {
            if (osCanvas != null) {
                osCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                osCanvas.worldCamera = osCamera;
                // Comentado: deixe o MonitorClickInteraction gerenciar o estado
                // osCanvas.gameObject.SetActive(false);
            }
        }
        
        public void ActivateOS() {
            if (osCanvas != null) {
                osCanvas.gameObject.SetActive(true);
            }
        }
        
        public void DeactivateOS() {
            if (osCanvas != null) {
                osCanvas.gameObject.SetActive(false);
            }
        }
    }
}