using UnityEngine;
using UnityEngine.InputSystem;

namespace SavitGame.OS {
    public class MonitorInteractable : MonoBehaviour {
        [Header("References")]
        public WindowsOSDisplay osDisplay;
        
        [Header("Interaction Settings")]
        public float interactionDistance = 2f;
        public KeyCode interactKey = KeyCode.E;
        public string playerTag = "Player";
        
        [Header("UI Feedback")]
        public GameObject interactionPrompt;
        
        private Transform player;
        private bool isPlayerNear;
        private bool isOSActive;
        private MonoBehaviour[] playerScripts;
        
        private void Start() {
            GameObject playerObj = GameObject.FindGameObjectWithTag(playerTag);
            if (playerObj != null) {
                player = playerObj.transform;
                // Pega todos os scripts do player para desabilitar depois
                playerScripts = playerObj.GetComponents<MonoBehaviour>();
            }
            
            if (interactionPrompt != null) {
                interactionPrompt.SetActive(false);
            }
        }
        
        private void Update() {
            CheckPlayerDistance();
            
            // Teclado (editor) — no AR mobile use botões de UI
            if (Keyboard.current != null) {
                if (isPlayerNear && Keyboard.current.eKey.wasPressedThisFrame) {
                    ToggleOS();
                }
                if (isOSActive && Keyboard.current.escapeKey.wasPressedThisFrame) {
                    CloseOS();
                }
            }
        }
        
        private void CheckPlayerDistance() {
            if (player == null) return;
            
            float distance = Vector3.Distance(transform.position, player.position);
            bool wasNear = isPlayerNear;
            isPlayerNear = distance <= interactionDistance;
            
            if (isPlayerNear != wasNear) {
                UpdateInteractionPrompt();
            }
        }
        
        private void UpdateInteractionPrompt() {
            if (interactionPrompt != null) {
                interactionPrompt.SetActive(isPlayerNear && !isOSActive);
            }
        }
        
        private void ToggleOS() {
            if (isOSActive) {
                CloseOS();
            } else {
                OpenOS();
            }
        }
        
        private void OpenOS() {
            if (osDisplay == null) return;
            
            isOSActive = true;
            osDisplay.ActivateOS();
            
            // Desabilitar controle do jogador
            SetPlayerControlEnabled(false);
            
            // Mostrar cursor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            UpdateInteractionPrompt();
        }
        
        private void CloseOS() {
            if (osDisplay == null) return;
            
            isOSActive = false;
            osDisplay.DeactivateOS();
            
            // Reabilitar controle do jogador
            SetPlayerControlEnabled(true);
            
            // Esconder cursor
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            UpdateInteractionPrompt();
        }
        
        private void SetPlayerControlEnabled(bool enabled) {
            if (playerScripts == null) return;
            
            foreach (var script in playerScripts) {
                // Não desabilitar este script
                if (script == this) continue;
                
                // Desabilitar scripts de movimento/câmera
                if (script.GetType().Name.Contains("Camera") || 
                    script.GetType().Name.Contains("Movement") ||
                    script.GetType().Name.Contains("Player") ||
                    script.GetType().Name.Contains("Controller")) {
                    script.enabled = enabled;
                }
            }
        }
        
        private void OnDrawGizmosSelected() {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactionDistance);
        }
    }
}