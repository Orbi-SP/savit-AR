using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SavitGame.OS.UI {
    public class MessagePopup : MonoBehaviour {
        [Header("UI Elements")]
        public GameObject popupPanel;
        public TextMeshProUGUI messageText;
        public TextMeshProUGUI titleText;
        public Button okButton;
        
        [Header("Colors")]
        public Color successColor = Color.green;
        public Color errorColor = Color.red;
        
        private static MessagePopup instance;
        private System.Action onCloseCallback;
        
        private void Awake() {
            if (instance == null) {
                instance = this;
                Debug.Log("MessagePopup instance criada!");
            } else {
                Destroy(gameObject);
                return;
            }
            
            // Verificar referências
            if (popupPanel == null) {
                Debug.LogError("MessagePopup: popupPanel está NULL! Atribua no Inspector!");
            }
            if (messageText == null) {
                Debug.LogError("MessagePopup: messageText está NULL! Atribua no Inspector!");
            }
            if (titleText == null) {
                Debug.LogError("MessagePopup: titleText está NULL! Atribua no Inspector!");
            }
            if (okButton == null) {
                Debug.LogError("MessagePopup: okButton está NULL! Atribua no Inspector!");
            }
            
            // Esconder pop-up ao iniciar
            if (popupPanel != null) {
                popupPanel.SetActive(false);
                Debug.Log("MessagePopup: popupPanel escondido no Awake");
            }
            
            // Configurar botão OK
            if (okButton != null) {
                okButton.onClick.RemoveAllListeners();
                okButton.onClick.AddListener(OnOKButtonClicked);
            }
        }
        
        public static void ShowSuccess(string message, string title = "Sucesso", System.Action onClose = null) {
            if (instance != null) {
                instance.onCloseCallback = onClose;
                instance.Show(title, message, instance.successColor);
            } else {
                Debug.LogWarning("MessagePopup instance não encontrada!");
            }
        }
        
        public static void ShowError(string message, string title = "Erro", System.Action onClose = null) {
            if (instance != null) {
                instance.onCloseCallback = onClose;
                instance.Show(title, message, instance.errorColor);
            } else {
                Debug.LogWarning("MessagePopup instance não encontrada!");
            }
        }
        
        private void Show(string title, string message, Color titleColor) {
            Debug.Log($"[MessagePopup.Show] Tentando mostrar: {title} - {message}");
            
            if (popupPanel == null) {
                Debug.LogError("MessagePopup.Show: popupPanel é NULL! Não pode mostrar o pop-up!");
                return;
            }
            
            popupPanel.SetActive(true);
            Debug.Log("MessagePopup: popupPanel.SetActive(true) executado!");
            
            if (titleText != null) {
                titleText.text = title;
                titleText.color = titleColor;
                Debug.Log($"MessagePopup: Título configurado para '{title}'");
            } else {
                Debug.LogWarning("MessagePopup: titleText é NULL!");
            }
            
            if (messageText != null) {
                messageText.text = message;
                Debug.Log($"MessagePopup: Mensagem configurada para '{message}'");
            } else {
                Debug.LogWarning("MessagePopup: messageText é NULL!");
            }
            
            Debug.Log($"[MessagePopup] {title}: {message}");
        }
        
        private void OnOKButtonClicked() {
            Debug.Log("MessagePopup.OnOKButtonClicked() - Botão OK clicado");
            Hide();
            
            // Executar callback se existir
            if (onCloseCallback != null) {
                Debug.Log("MessagePopup: Executando callback onClose");
                onCloseCallback.Invoke();
                onCloseCallback = null; // Limpar callback
            }
        }
        
        private void Hide() {
            Debug.Log("MessagePopup.Hide() chamado");
            if (popupPanel != null) {
                popupPanel.SetActive(false);
                Debug.Log("MessagePopup: popupPanel escondido");
            }
        }
    }
}
