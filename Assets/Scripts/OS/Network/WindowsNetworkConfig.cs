using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SavitGame.OS.Network {
    public class WindowsNetworkConfig : MonoBehaviour {
        [Header("UI References")]
        public NCPAController ncpaController;
        public TCPIPPropertiesController tcpipController;
        
        [Header("Status Display")]
        public TextMeshProUGUI statusText;
        public Image statusIcon;
        public Color successColor = Color.green;
        public Color errorColor = Color.red;
        
        private NetworkSettings currentSettings;
        private NetworkSettings backupSettings;
        
        private void Start() {
            currentSettings = new NetworkSettings();
            currentSettings.LoadFromPlayerPrefs();
            backupSettings = new NetworkSettings(currentSettings);
            
            UpdateAllDisplays();
        }
        
        public void OpenNCPA() {
            if (ncpaController != null) {
                ncpaController.Show();
            }
            
            if (tcpipController != null) {
                tcpipController.Hide();
            }
        }
        
        public void OpenTCPIPProperties() {
            if (tcpipController != null) {
                tcpipController.Show();
                tcpipController.PopulateFields(currentSettings);
            }
            
            if (ncpaController != null) {
                ncpaController.Hide();
            }
        }
        
        public void ApplyNetworkSettings(NetworkSettings newSettings) {
            // Criar backup antes de aplicar mudanças
            if (currentSettings != null) {
                backupSettings = new NetworkSettings(currentSettings);
            }
            
            // Aplicar novas configurações
            currentSettings = new NetworkSettings(newSettings);
            currentSettings.SaveToPlayerPrefs();
            
            UpdateAllDisplays();
            ShowStatus("Configurações de rede aplicadas com sucesso!", false);
            
            Debug.Log($"Configurações aplicadas:\nDHCP: {currentSettings.useDHCP}\nIP: {currentSettings.ipAddress}");
        }
        
        public void CancelNetworkSettings() {
            if (backupSettings != null) {
                currentSettings = new NetworkSettings(backupSettings);
                UpdateAllDisplays();
                ShowStatus("Changes cancelled", false);
            } else {
                ShowStatus("No changes to cancel", false);
            }
        }
        
        public NetworkSettings GetCurrentSettings() {
            return new NetworkSettings(currentSettings);
        }
        
        private void UpdateAllDisplays() {
            if (ncpaController != null) {
                ncpaController.UpdateNetworkInfo(currentSettings);
            }
        }
        
        private void ShowStatus(string message, bool isError) {
            if (statusText != null) {
                statusText.text = message;
                statusText.color = isError ? errorColor : successColor;
            }
            
            if (statusIcon != null) {
                statusIcon.color = isError ? errorColor : successColor;
            }
            
            CancelInvoke(nameof(ClearStatus));
            Invoke(nameof(ClearStatus), 3f);
        }
        
        private void ClearStatus() {
            if (statusText != null) {
                statusText.text = "";
            }
        }
    }
}