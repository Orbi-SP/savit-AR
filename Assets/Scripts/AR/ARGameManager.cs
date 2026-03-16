using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.InputSystem;

namespace SavitGame.AR {
    /// <summary>
    /// Gerencia o fluxo geral do jogo em AR:
    /// welcome → scanning → posicionamento → jogando
    /// </summary>
    public class ARGameManager : MonoBehaviour {
        public enum ARGameState {
            Welcome,     // Tela inicial — só texto "Toque para começar"
            Scanning,    // Detectando superfície
            Placing,     // Aguardando toque para posicionar
            Playing      // Jogando
        }

        // Acesso estático para outros scripts verificarem o estado
        public static ARGameManager Instance { get; private set; }
        public bool IsPlaying => currentState == ARGameState.Playing;

        [Header("Referências")]
        public ARPlacementManager placementManager;
        public ARSession arSession;

        [Header("UI por Estado")]
        public GameObject welcomeText;   // "Toque na tela para começar"
        public GameObject scanningUI;    // "Aponte para uma superfície plana"
        public GameObject tapToPlaceUI;  // "Toque para posicionar"
        public GameObject playingUI;     // UI do jogo em si
        public GameObject resetButton;   // Botão para reposicionar

        [Header("Comportamento")]
        [Tooltip("Se falso, não mostra PlayingUI automaticamente após colocar a cena")]
        public bool showPlayingUIOnPlaced = false;

        [Header("Estado")]
        public ARGameState currentState = ARGameState.Welcome;

        private ARPlaneManager planeManager;

        private void Awake() {
            Instance = this;
        }

        private void Start() {
            if (placementManager == null) {
                placementManager = FindFirstObjectByType<ARPlacementManager>();
                if (placementManager == null)
                    Debug.LogWarning("ARGameManager: ARPlacementManager não encontrado na cena.");
            }

            planeManager = FindFirstObjectByType<ARPlaneManager>();
            if (planeManager != null) {
                planeManager.trackablesChanged.AddListener(OnPlanesChanged);
                // Desativa detecção de planos até o usuário tocar para começar
                planeManager.enabled = false;
            } else {
                Debug.LogWarning("ARGameManager: ARPlaneManager não encontrado na cena. O fluxo de Scanning→Placing pode não acontecer; placement pode depender do fallback pose.");
            }
            SetState(ARGameState.Welcome);
        }

        private void Update() {
            // No estado Welcome, qualquer toque avança para Scanning
            if (currentState != ARGameState.Welcome) return;

            bool tapped = false;
#if UNITY_EDITOR
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                tapped = true;
#else
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                tapped = true;
#endif
            if (tapped) SetState(ARGameState.Scanning);
        }

        private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args) {
            // Assim que detectar o primeiro plano, avança para Placing
            if (currentState == ARGameState.Scanning && (args.added.Count > 0 || args.updated.Count > 0)) {
                SetState(ARGameState.Placing);
            }
        }

        public void SetState(ARGameState newState) {
            currentState = newState;

            if (placementManager != null) {
                // Placement/ghost só no estado Placing.
                placementManager.SetPlacementAllowed(newState == ARGameState.Placing);
            }

            switch (newState) {
                case ARGameState.Welcome:
                    SetUI(welcome: true, scanning: false, tapToPlace: false, playing: false, reset: false);
                    break;

                case ARGameState.Scanning:
                    if (planeManager != null) planeManager.enabled = true; // começa a detectar só agora
                    SetUI(welcome: false, scanning: true, tapToPlace: false, playing: false, reset: false);
                    break;

                case ARGameState.Placing:
                    SetUI(welcome: false, scanning: false, tapToPlace: true, playing: false, reset: false);
                    break;

                case ARGameState.Playing:
                    SetUI(welcome: false, scanning: false, tapToPlace: false, playing: true, reset: true);
                    break;
            }
        }

        public void OnScenePlaced() {
            currentState = ARGameState.Playing;

            if (placementManager != null) {
                placementManager.SetPlacementAllowed(false);
            }

            if (showPlayingUIOnPlaced) {
                SetUI(welcome: false, scanning: false, tapToPlace: false, playing: true, reset: true);
            } else {
                SetUI(welcome: false, scanning: false, tapToPlace: false, playing: false, reset: false);
            }
        }

        public void ResetGame() {
            if (placementManager != null) placementManager.ResetPlacement();
            SetState(ARGameState.Placing);
        }

        private void SetUI(bool welcome, bool scanning, bool tapToPlace, bool playing, bool reset) {
            if (welcomeText != null)       welcomeText.SetActive(welcome);
            if (this.scanningUI != null)   this.scanningUI.SetActive(scanning);
            if (this.tapToPlaceUI != null) this.tapToPlaceUI.SetActive(tapToPlace);
            if (this.playingUI != null)    this.playingUI.SetActive(playing);
            if (this.resetButton != null)  this.resetButton.SetActive(reset);
        }

        private void OnDestroy() {
            if (planeManager != null)
                planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
        }
    }
}
