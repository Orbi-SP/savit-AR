using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SavitGame.OS.Network;

namespace SavitGame.OS {
    public class NCPAController : MonoBehaviour {
        [Header("Window")]
        public GameObject ncpaWindow;
        
        [Header("Network Adapter Display")]
        public TextMeshProUGUI adapterNameText;
        public TextMeshProUGUI connectionStatusText;
        public Image connectionStatusIcon;
        
        [Header("Network Info")]
        public TextMeshProUGUI ipAddressText;
        public TextMeshProUGUI subnetMaskText;
        public TextMeshProUGUI defaultGatewayText;
        public TextMeshProUGUI dnsServersText;
        public TextMeshProUGUI dhcpStatusText;
        
        [Header("Buttons")]
        public Button propertiesButton;
        public Button disableButton;
        public Button diagnoseButton;
        public Button closeButton;
        
        [Header("References")]
        public WindowsNetworkConfig networkConfig;
        
        private void Start() {
            SetupButtons();
            Hide();
        }
        
        private void SetupButtons() {
            if (propertiesButton != null) {
                propertiesButton.onClick.AddListener(OnPropertiesClicked);
            }
            
            if (disableButton != null) {
                disableButton.onClick.AddListener(OnDisableClicked);
            }
            
            if (diagnoseButton != null) {
                diagnoseButton.onClick.AddListener(OnDiagnoseClicked);
            }
            
            if (closeButton != null) {
                closeButton.onClick.AddListener(Hide);
            }
        }
        
        public void Show() {
            if (ncpaWindow != null) {
                ncpaWindow.SetActive(true);
            }
        }
        
        public void Hide() {
            if (ncpaWindow != null) {
                ncpaWindow.SetActive(false);
            }
        }
        
        public void UpdateNetworkInfo(NetworkSettings settings) {
            if (adapterNameText != null) {
                adapterNameText.text = "Ethernet Adapter";
            }
            
            if (connectionStatusText != null) {
                connectionStatusText.text = "Connected";
                connectionStatusText.color = Color.green;
            }
            
            if (connectionStatusIcon != null) {
                connectionStatusIcon.color = Color.green;
            }
            
            if (ipAddressText != null) {
                ipAddressText.text = $"IPv4 Address: {settings.ipAddress}";
            }
            
            if (subnetMaskText != null) {
                subnetMaskText.text = $"Subnet Mask: {settings.subnetMask}";
            }
            
            if (defaultGatewayText != null) {
                defaultGatewayText.text = $"Default Gateway: {settings.defaultGateway}";
            }
            
            if (dnsServersText != null) {
                dnsServersText.text = $"DNS Servers:\n{settings.preferredDNS}\n{settings.alternateDNS}";
            }
            
            if (dhcpStatusText != null) {
                dhcpStatusText.text = settings.useDHCP ? "DHCP Enabled" : "Static IP";
            }
        }
        
        private void OnPropertiesClicked() {
            if (networkConfig != null) {
                networkConfig.OpenTCPIPProperties();
            }
        }
        
        private void OnDisableClicked() {
            Debug.Log("Disable network adapter clicked");
        }
        
        private void OnDiagnoseClicked() {
            Debug.Log("Diagnose network clicked");
        }
    }
}