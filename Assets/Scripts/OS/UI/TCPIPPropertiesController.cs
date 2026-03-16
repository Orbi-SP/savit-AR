using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SavitGame.OS.Network;
using SavitGame.OS.UI;

namespace SavitGame.OS {
    public class TCPIPPropertiesController : MonoBehaviour {
        [Header("Window")]
        public GameObject propertiesWindow;
        
        [Header("IP Settings")]
        public Toggle dhcpToggle;
        public Toggle staticIPToggle;
        public TMP_InputField ipAddressInput;
        public TMP_InputField subnetMaskInput;
        public TMP_InputField defaultGatewayInput;
        
        [Header("DNS Settings")]
        public Toggle autoDNSToggle;
        public Toggle manualDNSToggle;
        public TMP_InputField preferredDNSInput;
        public TMP_InputField alternateDNSInput;
        
        [Header("Buttons")]
        public Button okButton;
        public Button cancelButton;
        public Button advancedButton;
        public Button closeButton; // Botão X para fechar a janela
        
        [Header("Validation")]
        public TextMeshProUGUI validationMessage;
        public Color validColor = Color.green;
        public Color invalidColor = Color.red;
        
        [Header("References")]
        public WindowsNetworkConfig networkConfig;
        
        private NetworkSettings tempSettings;
        
        private void Start() {
            SetupButtons();
            SetupToggles();
            SetupIPInputFormatting();
        }
        
        private void SetupIPInputFormatting() {
            SetupIPInput(ipAddressInput);
            SetupIPInput(subnetMaskInput);
            SetupIPInput(defaultGatewayInput);
            SetupIPInput(preferredDNSInput);
            SetupIPInput(alternateDNSInput);
        }
        
        private void SetupIPInput(TMP_InputField inputField) {
            if (inputField == null) return;
            
            inputField.contentType = TMP_InputField.ContentType.Standard;
            inputField.characterLimit = 15;
            
            inputField.onValueChanged.AddListener((value) => {
                string filtered = FilterIPInput(value);
                if (filtered != value) {
                    int caretPos = inputField.caretPosition;
                    inputField.text = filtered;
                    inputField.caretPosition = Mathf.Min(caretPos + (filtered.Length - value.Length), filtered.Length);
                }
            });
        }
        
        private string FilterIPInput(string input) {
            string result = "";
            int dotCount = 0;
            string currentSegment = "";
            
            foreach (char c in input) {
                if (char.IsDigit(c)) {
                    if (currentSegment.Length < 3) {
                        currentSegment += c;
                        result += c;
                        
                        if (currentSegment.Length == 3 && dotCount < 3) {
                            result += '.';
                            dotCount++;
                            currentSegment = "";
                        }
                    }
                }
                else if (c == '.' && dotCount < 3 && currentSegment.Length > 0) {
                    result += c;
                    dotCount++;
                    currentSegment = "";
                }
            }
            
            return result;
        }
        
        private void SetupButtons() {
            if (okButton != null) {
                okButton.onClick.AddListener(OnOKClicked);
            }
            
            if (cancelButton != null) {
                cancelButton.onClick.AddListener(OnCancelClicked);
            }
            
            if (advancedButton != null) {
                advancedButton.onClick.AddListener(OnAdvancedClicked);
            }
            
            if (closeButton != null) {
                closeButton.onClick.AddListener(OnCloseClicked);
            }
        }
        
        private void SetupToggles() {
            Debug.Log("=== SetupToggles START ===");
            
            if (dhcpToggle != null && staticIPToggle != null) {
                dhcpToggle.onValueChanged.RemoveAllListeners();
                staticIPToggle.onValueChanged.RemoveAllListeners();
                
                dhcpToggle.onValueChanged.AddListener(OnDHCPToggled);
                staticIPToggle.onValueChanged.AddListener(OnStaticIPToggled);
                
                OnStaticIPToggled(staticIPToggle.isOn);
            }
            
            Debug.Log($"DNS Toggles - AutoDNS: {autoDNSToggle != null}, ManualDNS: {manualDNSToggle != null}");
            
            if (autoDNSToggle != null && manualDNSToggle != null) {
                Debug.Log($"AutoDNS.isOn: {autoDNSToggle.isOn}, ManualDNS.isOn: {manualDNSToggle.isOn}");
                
                autoDNSToggle.onValueChanged.RemoveAllListeners();
                manualDNSToggle.onValueChanged.RemoveAllListeners();
                
                autoDNSToggle.onValueChanged.AddListener(OnAutoDNSToggled);
                manualDNSToggle.onValueChanged.AddListener(OnManualDNSToggled);
                
                UpdateDNSInputsState();
            } else {
                Debug.LogError("DNS Toggles são NULL! Verifique as referências no Inspector.");
            }
            
            AddInputValidation(ipAddressInput);
            AddInputValidation(subnetMaskInput);
            AddInputValidation(defaultGatewayInput);
            AddInputValidation(preferredDNSInput);
            AddInputValidation(alternateDNSInput);
            
            Debug.Log("=== SetupToggles END ===");
        }
        
        private void UpdateDNSInputsState() {
            bool manualDNS = manualDNSToggle != null && manualDNSToggle.isOn;
            Debug.Log($"UpdateDNSInputsState - ManualDNS: {manualDNS}");
            
            if (preferredDNSInput != null) {
                preferredDNSInput.interactable = manualDNS;
                Debug.Log($"PreferredDNS interactable set to: {manualDNS}");
            }
            if (alternateDNSInput != null) {
                alternateDNSInput.interactable = manualDNS;
                Debug.Log($"AlternateDNS interactable set to: {manualDNS}");
            }
        }
        
        private void AddInputValidation(TMP_InputField inputField) {
            if (inputField != null) {
                inputField.onValueChanged.AddListener(_ => ValidateInputsVisual());
            }
        }
        
        private void ValidateInputsVisual() {
            bool isValid = true;
            string message = "";
            
            if (staticIPToggle != null && staticIPToggle.isOn) {
                if (!string.IsNullOrEmpty(ipAddressInput?.text) && !NetworkSettings.ValidateIPAddress(ipAddressInput?.text)) {
                    isValid = false;
                    message = "Invalid IP Address format";
                }
                else if (!string.IsNullOrEmpty(subnetMaskInput?.text) && !NetworkSettings.ValidateIPAddress(subnetMaskInput?.text)) {
                    isValid = false;
                    message = "Invalid Subnet Mask format";
                }
                else if (!string.IsNullOrEmpty(defaultGatewayInput?.text) && !NetworkSettings.ValidateIPAddress(defaultGatewayInput?.text)) {
                    isValid = false;
                    message = "Invalid Default Gateway format";
                }
            }
            
            if (manualDNSToggle != null && manualDNSToggle.isOn) {
                if (!string.IsNullOrEmpty(preferredDNSInput?.text) && !NetworkSettings.ValidateIPAddress(preferredDNSInput?.text)) {
                    isValid = false;
                    message = "Invalid Preferred DNS format";
                }
                else if (!string.IsNullOrEmpty(alternateDNSInput?.text) && 
                         !NetworkSettings.ValidateIPAddress(alternateDNSInput?.text)) {
                    isValid = false;
                    message = "Invalid Alternate DNS format";
                }
            }
            
            if (validationMessage != null) {
                // Só mostrar se houver uma mensagem
                if (!string.IsNullOrEmpty(message)) {
                    validationMessage.text = message;
                    validationMessage.color = isValid ? validColor : invalidColor;
                    validationMessage.enabled = true;
                } else {
                    validationMessage.enabled = false;
                }
            }
        }
        
        public void Show() {
            if (propertiesWindow != null) {
                propertiesWindow.SetActive(true);
            }
            
            // Limpar e esconder mensagem de validação ao abrir
            ClearValidationMessage();
        }
        
        public void Hide() {
            if (propertiesWindow != null) {
                propertiesWindow.SetActive(false);
            }
            
            // Limpar mensagem ao fechar
            ClearValidationMessage();
        }
        
        private void ClearValidationMessage() {
            if (validationMessage != null) {
                validationMessage.text = "";
                validationMessage.enabled = false;
            }
        }
        
        public void PopulateFields(NetworkSettings settings) {
            tempSettings = new NetworkSettings(settings);
            
            // Limpar mensagem de validação
            ClearValidationMessage();
            
            if (dhcpToggle != null && staticIPToggle != null) {
                dhcpToggle.isOn = settings.useDHCP;
                staticIPToggle.isOn = !settings.useDHCP;
            }
            
            if (ipAddressInput != null) {
                ipAddressInput.text = settings.ipAddress;
                ipAddressInput.interactable = !settings.useDHCP;
            }
            
            if (subnetMaskInput != null) {
                subnetMaskInput.text = settings.subnetMask;
                subnetMaskInput.interactable = !settings.useDHCP;
            }
            
            if (defaultGatewayInput != null) {
                defaultGatewayInput.text = settings.defaultGateway;
                defaultGatewayInput.interactable = !settings.useDHCP;
            }
            
            if (preferredDNSInput != null) {
                preferredDNSInput.text = settings.preferredDNS;
            }
            
            if (alternateDNSInput != null) {
                alternateDNSInput.text = settings.alternateDNS;
            }
            
            // Não mostrar validação ao carregar os campos
        }
        
        private void OnDHCPToggled(bool isOn) {
            Debug.Log($"DHCP Toggle: {isOn}");
            if (ipAddressInput != null) ipAddressInput.interactable = !isOn;
            if (subnetMaskInput != null) subnetMaskInput.interactable = !isOn;
            if (defaultGatewayInput != null) defaultGatewayInput.interactable = !isOn;
        }
        
        private void OnStaticIPToggled(bool isOn) {
            Debug.Log($"StaticIP Toggle: {isOn}");
            if (ipAddressInput != null) ipAddressInput.interactable = isOn;
            if (subnetMaskInput != null) subnetMaskInput.interactable = isOn;
            if (defaultGatewayInput != null) defaultGatewayInput.interactable = isOn;
        }
        
        private void OnAutoDNSToggled(bool isOn) {
            Debug.Log($"AutoDNS Toggle: {isOn}");
            UpdateDNSInputsState();
        }
        
        private void OnManualDNSToggled(bool isOn) {
            Debug.Log($"ManualDNS Toggle: {isOn}");
            UpdateDNSInputsState();
        }
        
        private bool ValidateInputs() {
            bool isValid = true;
            string message = "";
            
            // Só valida IP estático se a opção estiver marcada
            if (staticIPToggle != null && staticIPToggle.isOn) {
                if (!NetworkSettings.ValidateIPAddress(ipAddressInput?.text)) {
                    isValid = false;
                    message = "Formato de Endereço IP inválido";
                }
                else if (!NetworkSettings.ValidateIPAddress(subnetMaskInput?.text)) {
                    isValid = false;
                    message = "Formato de Máscara de Sub-rede inválido";
                }
                else if (!NetworkSettings.ValidateIPAddress(defaultGatewayInput?.text)) {
                    isValid = false;
                    message = "Formato de Gateway Padrão inválido";
                }
            }
            
            // Só valida DNS manual se a opção estiver marcada
            if (manualDNSToggle != null && manualDNSToggle.isOn) {
                if (!NetworkSettings.ValidateIPAddress(preferredDNSInput?.text)) {
                    isValid = false;
                    message = "Formato de DNS Preferencial inválido";
                }
                else if (!string.IsNullOrEmpty(alternateDNSInput?.text) && 
                         !NetworkSettings.ValidateIPAddress(alternateDNSInput?.text)) {
                    isValid = false;
                    message = "Formato de DNS Alternativo inválido";
                }
            }
            
            if (validationMessage != null) {
                validationMessage.text = message;
                validationMessage.color = isValid ? validColor : invalidColor;
            }
            
            return isValid;
        }
        
        private void OnOKClicked() {
            if (!ValidateInputs()) {
                Debug.LogWarning("Validação falhou! Verifique os campos.");
                
                // Mostrar pop-up de erro
                MessagePopup.ShowError("Por favor, preencha todos os campos corretamente");
                return;
            }
            
            NetworkSettings newSettings = new NetworkSettings {
                useDHCP = dhcpToggle != null && dhcpToggle.isOn,
                ipAddress = ipAddressInput?.text ?? "",
                subnetMask = subnetMaskInput?.text ?? "",
                defaultGateway = defaultGatewayInput?.text ?? "",
                preferredDNS = preferredDNSInput?.text ?? "",
                alternateDNS = alternateDNSInput?.text ?? ""
            };
            
            if (networkConfig != null) {
                networkConfig.ApplyNetworkSettings(newSettings);
                Debug.Log("Configurações de rede aplicadas com sucesso!");
            }
            
            // Mostrar pop-up de sucesso e fechar janela quando clicar OK no pop-up
            MessagePopup.ShowSuccess(
                "Configuracoes de rede aplicadas com sucesso!",
                "Sucesso",
                () => {
                    Debug.Log("Callback do MessagePopup: Fechando janela TCP/IP");
                    Hide();
                }
            );
        }
        
        private void OnCancelClicked() {
            if (networkConfig != null) {
                networkConfig.CancelNetworkSettings();
            }
            
            Hide();
        }
        
        private void OnCloseClicked() {
            // Botão X fecha igual ao Cancel (descarta mudanças)
            OnCancelClicked();
        }
        
        private void OnAdvancedClicked() {
            Debug.Log("Advanced settings clicked");
        }
    }
}