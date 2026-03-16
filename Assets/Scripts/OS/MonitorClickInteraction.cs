using UnityEngine;
using UnityEngine.InputSystem;
using SavitGame.AR;

namespace SavitGame.OS {
    /// <summary>
    /// Detecta cliques no monitor 3D e abre a interface em modo Overlay.
    /// MODO OVERLAY: UI aparece diretamente na tela (muito mais simples!)
    /// Só responde quando o jogo está em estado Playing.
    /// </summary>
    public class MonitorClickInteraction : MonoBehaviour {
        [Header("Overlay Manager")]
        [SerializeField] private OverlayUIManager overlayManager;
        
        [Header("Camera")]
        [SerializeField] private Camera mainCamera;
        
        private void Update() {
            // Não registra toques antes do jogo começar
            if (ARGameManager.Instance == null || !ARGameManager.Instance.IsPlaying) return;

            bool pressed = false;
            Vector2 pressPosition = Vector2.zero;

#if UNITY_EDITOR
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) {
                pressed = true;
                pressPosition = Mouse.current.position.ReadValue();
            }
#else
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) {
                pressed = true;
                pressPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            }
#endif

            if (pressed && mainCamera != null) {
                Ray ray = mainCamera.ScreenPointToRay(pressPosition);
                if (Physics.Raycast(ray, out RaycastHit hit)) {
                    if (hit.collider.gameObject == gameObject) {
                        Debug.Log($"🖱️ Tocou no monitor: {gameObject.name}");
                        OpenOverlay();
                    }
                }
            }
        }
        
        private void OnMouseDown() {
            Debug.Log($"🖱️ OnMouseDown no monitor: {gameObject.name}");
            OpenOverlay();
        }
        
        private void OpenOverlay() {
            if (overlayManager != null) {
                overlayManager.ShowUI();
            } else {
                Debug.LogError("❌ OverlayUIManager não está configurado!");
            }
        }
    }
}
