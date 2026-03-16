using UnityEngine;
using UnityEngine.InputSystem;

namespace SavitGame.OS {
    public class OverlayUIManager : MonoBehaviour {
        [Header("UI References")]
        [SerializeField] private Canvas osCanvas;
        [SerializeField] private GameObject desktopPanel;
        [SerializeField] private TCPIPPropertiesController tcpipController;
        
        [Header("Camera References")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Camera osCamera;
        
        private void Start() {
            SetupOverlayMode();
            HideUI();
        }
        
        private void Update() {
            // Fecha com botão Voltar no Android ou Escape no editor
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) {
                HideUI();
            }
        }
        
        private void SetupOverlayMode() {
            if (osCanvas != null) {
                osCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                osCanvas.sortingOrder = 100;
                Debug.Log("Canvas configurado para Screen Space - Overlay");
            }
            
            if (osCamera != null) {
                osCamera.gameObject.SetActive(false);
                Debug.Log("OSCamera desativada");
            }
        }
        
        public void ShowUI() {
            if (osCanvas != null) {
                osCanvas.gameObject.SetActive(true);
            }
            
            if (desktopPanel != null) {
                desktopPanel.SetActive(true);
            }
            
            if (tcpipController != null) {
                tcpipController.Show();
            }
            
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("UI Overlay ABERTA");
        }
        
        public void HideUI() {
            if (tcpipController != null) {
                tcpipController.Hide();
            }
            
            if (desktopPanel != null) {
                desktopPanel.SetActive(false);
            }
            
            if (osCanvas != null) {
                osCanvas.gameObject.SetActive(false);
            }
            
            Debug.Log("UI Overlay FECHADA");
        }
    }
}
