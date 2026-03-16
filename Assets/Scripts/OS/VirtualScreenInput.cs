using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace SavitGame.OS {
    public class VirtualScreenInput : MonoBehaviour {
        [Header("References")]
        public Camera mainCamera;
        public Camera osCamera;
        public Canvas osCanvas;
        public RenderTexture renderTexture;
        
        [Header("Monitor Settings")]
        public Transform monitorScreen; // Transform da tela do monitor
        public Vector2 screenSize = new Vector2(1920, 1080); // Tamanho da RenderTexture
        
        [Header("Calibration")]
        public Vector2 pixelOffset = new Vector2(0, -75); // Offset em pixels (X, Y)
        public KeyCode increaseXKey = KeyCode.RightArrow; // Aumenta offset X
        public KeyCode decreaseXKey = KeyCode.LeftArrow;  // Diminui offset X
        public KeyCode increaseYKey = KeyCode.UpArrow;    // Aumenta offset Y
        public KeyCode decreaseYKey = KeyCode.DownArrow;  // Diminui offset Y
        public float offsetStep = 5f; // Pixels por tecla pressionada
        
        [Header("Debug")]
        public bool showDebugInfo = true; // Mostra informações de debug no Console
        
        private EventSystem eventSystem;
        private GraphicRaycaster graphicRaycaster;
        
        private void Start() {
            if (mainCamera == null) {
                mainCamera = Camera.main;
            }
            
            eventSystem = EventSystem.current;
            if (eventSystem == null) {
                Debug.LogError("EventSystem não encontrado! Adicione um EventSystem à cena.");
            }
            
            if (osCanvas != null) {
                graphicRaycaster = osCanvas.GetComponent<GraphicRaycaster>();
                if (graphicRaycaster == null) {
                    Debug.LogError("GraphicRaycaster não encontrado no Canvas!");
                }
            }
        }
        
        private void Update() {
            // Desabilita em modo Overlay - não precisa de conversão de coordenadas
            if (osCanvas != null && osCanvas.renderMode == RenderMode.ScreenSpaceOverlay) {
                return;
            }
            
            if (!osCanvas.gameObject.activeSelf) return;
            
            // Calibração em tempo real com setas do teclado
            HandleCalibrationInput();
            
            // Detecta clique/toque no monitor
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) {
                ProcessClick();
            }
        }
        
        private void HandleCalibrationInput() {
            bool changed = false;
            
            if (Keyboard.current == null) return;
            if (Keyboard.current.rightArrowKey.isPressed) {
                pixelOffset.x += offsetStep * Time.deltaTime * 10f;
                changed = true;
            }
            if (Keyboard.current.leftArrowKey.isPressed) {
                pixelOffset.x -= offsetStep * Time.deltaTime * 10f;
                changed = true;
            }
            if (Keyboard.current.upArrowKey.isPressed) {
                pixelOffset.y += offsetStep * Time.deltaTime * 10f;
                changed = true;
            }
            if (Keyboard.current.downArrowKey.isPressed) {
                pixelOffset.y -= offsetStep * Time.deltaTime * 10f;
                changed = true;
            }
            
            if (changed && showDebugInfo) {
                Debug.Log($"⚙️ Offset ajustado: X={pixelOffset.x:F1}, Y={pixelOffset.y:F1}");
            }
        }
        
        private void ProcessClick() {
            Vector2 mousePos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit)) {
                // Verifica se acertou o monitor
                if (hit.collider.gameObject == monitorScreen.gameObject || 
                    hit.collider.transform.IsChildOf(monitorScreen)) {
                    
                    // Converte hit point para espaço local do monitor
                    Vector3 localPoint = monitorScreen.InverseTransformPoint(hit.point);
                    
                    // Pega bounds do mesh renderer
                    MeshRenderer meshRenderer = monitorScreen.GetComponent<MeshRenderer>();
                    if (meshRenderer == null) {
                        Debug.LogError("❌ MeshRenderer não encontrado no monitorScreen!");
                        return;
                    }
                    
                    Bounds localBounds = meshRenderer.localBounds;
                    
                    // Calcula UV normalizado (0-1) com base nos bounds locais
                    // Monitor está no plano XZ (não XY!)
                    float uvX = Mathf.InverseLerp(localBounds.min.x, localBounds.max.x, localPoint.x);
                    float uvY = Mathf.InverseLerp(localBounds.min.z, localBounds.max.z, localPoint.z);
                    
                    // Inverte apenas X (horizontal está espelhado)
                    uvX = 1f - uvX;
                    // uvY já está correto, não inverte
                    
                    // Converte UV para pixel na RenderTexture
                    Vector2 pixelPos = new Vector2(
                        uvX * screenSize.x + pixelOffset.x,
                        uvY * screenSize.y + pixelOffset.y
                    );
                    
                    if (showDebugInfo) {
                        Debug.Log($"🎯 UV: ({uvX:F3}, {uvY:F3}) → Pixel: ({pixelPos.x:F1}, {pixelPos.y:F1})");
                    }
                    
                    // Simula clique na UI
                    SimulateUIClick(pixelPos);
                }
            }
        }
        
        private void SimulateUIClick(Vector2 renderTexturePixel) {
            if (eventSystem == null || graphicRaycaster == null || osCamera == null) return;
            
            // Converte pixel da RenderTexture para viewport (0-1)
            Vector2 viewportPoint = new Vector2(
                renderTexturePixel.x / screenSize.x,
                renderTexturePixel.y / screenSize.y
            );
            
            // Converte viewport para screen space da OSCamera
            Vector2 screenPoint = new Vector2(
                viewportPoint.x * osCamera.pixelWidth,
                viewportPoint.y * osCamera.pixelHeight
            );
            
            // Cria dados de evento apontando para a posição na UI
            PointerEventData pointerData = new PointerEventData(eventSystem) {
                position = screenPoint
            };
            
            // Faz raycast usando o GraphicRaycaster do Canvas
            List<RaycastResult> results = new List<RaycastResult>();
            graphicRaycaster.Raycast(pointerData, results);
            
            if (results.Count > 0) {
                GameObject hitObject = results[0].gameObject;
                Debug.Log($"✅ UI element clicado: {hitObject.name}");
                
                // Dispara eventos de clique
                ExecuteEvents.Execute(hitObject, pointerData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(hitObject, pointerData, ExecuteEvents.pointerClickHandler);
                ExecuteEvents.Execute(hitObject, pointerData, ExecuteEvents.pointerUpHandler);
            }
        }
    }
}
