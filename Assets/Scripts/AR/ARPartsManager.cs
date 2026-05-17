using UnityEngine;

namespace SavitGame.AR {
    /// <summary>
    /// Controla a visibilidade das peças de hardware no AR.
    /// Coloque este script no GameScenePrefab e no GameScenePreviewPrefab.
    /// - Preview (com GhostPreview): peças ficam visíveis para mostrar o ghost.
    /// - Cena real: começa oculta e aparece apenas após confirmar o placement.
    /// </summary>
    public class ARPartsManager : MonoBehaviour {
        [Header("Peças de Hardware")]
        [Tooltip("Arraste o PCCaseMM aqui")]
        public GameObject gabinete;

        [Tooltip("Arraste o objeto Ram aqui")]
        public GameObject ram;

        [Tooltip("Arraste o objeto Motherboards aqui")]
        public GameObject motherboard;

        [Tooltip("Arraste o MonitorD10 aqui")]
        public GameObject monitor;

        private void Awake() {
            AutoAssignMissingParts();

            // Configura visibilidade inicial sem sobrescrever decisões posteriores.
            // Importante: ConfirmPlacement() chama ShowAllParts() imediatamente após instanciar,
            // então não podemos esconder novamente em Start().
            if (GetComponent<GhostPreview>() != null) {
                ShowAllParts();
            } else {
                HideAllParts();
            }
        }

        private void AutoAssignMissingParts() {
            if (gabinete == null) gabinete = FindChildByNameContains("PCCaseMM", "Gabinete", "Case");
            if (ram == null) ram = FindChildByNameContains("Ram", "RAM");
            if (motherboard == null) motherboard = FindChildByNameContains("Motherboards", "Motherboard", "PlacaMae");
            // Evita falsos-positivos (ex.: outros filhos com "Monitor" no nome).
            // Se você remover o MonitorD10 do prefab, monitor deve permanecer null (comportamento ok).
            if (monitor == null) monitor = FindChildByNameContains("MonitorD10");
        }

        private GameObject FindChildByNameContains(params string[] nameCandidates) {
            foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true)) {
                string n = t.name;
                for (int i = 0; i < nameCandidates.Length; i++) {
                    if (n.IndexOf(nameCandidates[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return t.gameObject;
                }
            }
            return null;
        }

        public void ShowAllParts() {
            SetVisible(gabinete, true);
            SetVisible(ram, true);
            SetVisible(motherboard, true);
            SetVisible(monitor, true);
        }

        public void HideAllParts() {
            SetVisible(gabinete, false);
            SetVisible(ram, false);
            SetVisible(motherboard, false);
            SetVisible(monitor, false);
        }

        // ── Métodos chamados pelos botões da UI ──────────────────────────

        public void MostrarGabinete()    => SetVisible(gabinete, true);
        public void MostrarRAM()         => SetVisible(ram, true);
        public void MostrarMotherboard() => SetVisible(motherboard, true);
        public void MostrarMonitor()     => SetVisible(monitor, true);

        public void OcultarGabinete()    => SetVisible(gabinete, false);
        public void OcultarRAM()         => SetVisible(ram, false);
        public void OcultarMotherboard() => SetVisible(motherboard, false);
        public void OcultarMonitor()     => SetVisible(monitor, false);

        /// <summary>Alterna visibilidade de uma peça (toggle).</summary>
        public void ToggleGabinete()    => Toggle(gabinete);
        public void ToggleRAM()         => Toggle(ram);
        public void ToggleMotherboard() => Toggle(motherboard);
        public void ToggleMonitor()     => Toggle(monitor);

        // ── Helpers ─────────────────────────────────────────────────────

        private void SetVisible(GameObject obj, bool visible) {
            if (obj != null) obj.SetActive(visible);
        }

        private void Toggle(GameObject obj) {
            if (obj != null) obj.SetActive(!obj.activeSelf);
        }
    }
}
