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

        [Tooltip("Arraste o objeto PowerSupply/Fonte aqui")]
        public GameObject powerSupply;

        [Tooltip("Arraste o MonitorD10 aqui")]
        public GameObject monitor;

        [Tooltip("Arraste o objeto HardDrive aqui")]
        public GameObject hardDrive;

        private void Awake() {
            AutoAssignMissingParts();
            ConfigurePowerSupplyPlacer();
            ConfigurePowerCableConnectors();
            ConfigureHardDrivePlacer();

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
            if (powerSupply == null) powerSupply = FindPowerSupply();
            // Evita falsos-positivos (ex.: outros filhos com "Monitor" no nome).
            // Se você remover o MonitorD10 do prefab, monitor deve permanecer null (comportamento ok).
            if (monitor == null) monitor = FindChildByNameContains("MonitorD10");
            // Nome exato: "HD" por substring pegaria HDMI, Shadow etc.
            if (hardDrive == null) hardDrive = FindChildByExactName("HardDrive", "HD_SATA");
        }

        private GameObject FindChildByExactName(params string[] names) {
            foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true)) {
                for (int i = 0; i < names.Length; i++) {
                    if (t.name.Equals(names[i], System.StringComparison.OrdinalIgnoreCase))
                        return t.gameObject;
                }
            }
            return null;
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

        private GameObject FindPowerSupply() {
            foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true)) {
                string n = t.name;
                if (n.Equals("PowerSupply", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("Fonte", System.StringComparison.OrdinalIgnoreCase) ||
                    n.Equals("CorsairSF600", System.StringComparison.OrdinalIgnoreCase))
                    return t.gameObject;
            }

            foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true)) {
                string n = t.name;
                if (n.IndexOf("SnapZone", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (n.IndexOf("PowerSupply", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Power Supply", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Fonte", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("CorsairSF600", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t.gameObject;
            }

            return null;
        }

        private void ConfigurePowerSupplyPlacer() {
            if (powerSupply == null) {
                Debug.LogWarning("[ARPartsManager] Fonte não encontrada para configurar o PowerSupplyPlacer.");
                return;
            }

            var placer = powerSupply.GetComponent<PowerSupplyPlacer>();
            if (placer == null) placer = powerSupply.AddComponent<PowerSupplyPlacer>();

            placer.api = GetComponentInParent<Api>();
            if (placer.api == null) placer.api = GetComponentInChildren<Api>(true);
            if (placer.api == null) placer.api = FindFirstObjectByType<Api>();

            if (placer.motherboardPlacer == null)
                placer.motherboardPlacer = GetComponentInChildren<MotherboardPlacer>(true);
            if (placer.motherboardPlacer == null)
                placer.motherboardPlacer = FindFirstObjectByType<MotherboardPlacer>();

            if (placer.snapZone == null) {
                var zoneObject = FindChildByNameContains("PowerSupplySnapZone");
                if (zoneObject != null) placer.snapZone = zoneObject.GetComponent<BoxCollider>();
            }

            if (placer.snapAnchor == null) placer.snapAnchor = FindSnapAnchor(placer.snapZone);

            if (placer.snapZone == null || placer.snapAnchor == null)
                Debug.LogWarning("[ARPartsManager] PowerSupplySnapZone/SnapAnchor não configurados para a fonte.");
        }

        private void ConfigurePowerCableConnectors() {
            // O LineRenderer do cabo fica no ATX24PinPSU (ponta presa na fonte).
            foreach (var line in GetComponentsInChildren<LineRenderer>(includeInactive: true)) {
                if (!line.name.Equals("ATX24PinPSU", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var connector = line.GetComponent<PowerCableConnector>();
                if (connector == null) connector = line.gameObject.AddComponent<PowerCableConnector>();

                if (connector.api == null) connector.api = GetComponentInParent<Api>();
                if (connector.api == null) connector.api = GetComponentInChildren<Api>(true);
                if (connector.powerSupplyPlacer == null && powerSupply != null)
                    connector.powerSupplyPlacer = powerSupply.GetComponent<PowerSupplyPlacer>();
            }
        }

        private void ConfigureHardDrivePlacer() {
            if (hardDrive == null) {
                Debug.LogWarning("[ARPartsManager] HD (HardDrive) não encontrado para configurar o HardDrivePlacer.");
                return;
            }

            var placer = hardDrive.GetComponent<HardDrivePlacer>();
            if (placer == null) placer = hardDrive.AddComponent<HardDrivePlacer>();

            if (placer.api == null) placer.api = GetComponentInParent<Api>();
            if (placer.api == null) placer.api = GetComponentInChildren<Api>(true);
            if (placer.api == null) placer.api = FindFirstObjectByType<Api>();

            if (placer.powerSupplyPlacer == null && powerSupply != null)
                placer.powerSupplyPlacer = powerSupply.GetComponent<PowerSupplyPlacer>();

            // O HD só é liberado depois do cabo ATX 24 pinos (ConfigurePowerCableConnectors roda antes).
            if (placer.powerCable == null)
                placer.powerCable = GetComponentInChildren<PowerCableConnector>(true);

            if (placer.snapZone == null) {
                var zoneObject = FindChildByNameContains("HardDriveSnapZone");
                if (zoneObject != null) placer.snapZone = zoneObject.GetComponent<BoxCollider>();
            }

            if (placer.snapAnchor == null) placer.snapAnchor = FindSnapAnchor(placer.snapZone);

            if (placer.sataPortPlaca == null) {
                var port = FindChildByNameContains("SATAPort_Placa");
                if (port != null) placer.sataPortPlaca = port.transform;
            }

            if (placer.snapZone == null || placer.snapAnchor == null)
                Debug.LogWarning("[ARPartsManager] HardDriveSnapZone/SnapAnchor não configurados para o HD.");
            if (placer.sataPortPlaca == null)
                Debug.LogWarning("[ARPartsManager] SATAPort_Placa não encontrado; o cabo SATA não será desenhado.");
        }

        private Transform FindSnapAnchor(BoxCollider zone) {
            if (zone == null) return null;

            foreach (var anchor in zone.GetComponentsInChildren<Transform>(true)) {
                if (anchor != zone.transform &&
                    anchor.name.Equals("SnapAnchor", System.StringComparison.OrdinalIgnoreCase))
                    return anchor;
            }
            return null;
        }

        public void ShowAllParts() {
            SetVisible(gabinete, true);
            SetVisible(ram, true);
            SetVisible(motherboard, true);
            SetVisible(powerSupply, true);
            SetVisible(monitor, true);
            SetVisible(hardDrive, true);
        }

        public void HideAllParts() {
            SetVisible(gabinete, false);
            SetVisible(ram, false);
            SetVisible(motherboard, false);
            SetVisible(powerSupply, false);
            SetVisible(monitor, false);
            SetVisible(hardDrive, false);
        }

        // ── Métodos chamados pelos botões da UI ──────────────────────────

        public void MostrarGabinete()    => SetVisible(gabinete, true);
        public void MostrarRAM()         => SetVisible(ram, true);
        public void MostrarMotherboard() => SetVisible(motherboard, true);
        public void MostrarPowerSupply() => SetVisible(powerSupply, true);
        public void MostrarMonitor()     => SetVisible(monitor, true);
        public void MostrarHardDrive()   => SetVisible(hardDrive, true);

        public void OcultarGabinete()    => SetVisible(gabinete, false);
        public void OcultarRAM()         => SetVisible(ram, false);
        public void OcultarMotherboard() => SetVisible(motherboard, false);
        public void OcultarPowerSupply() => SetVisible(powerSupply, false);
        public void OcultarMonitor()     => SetVisible(monitor, false);
        public void OcultarHardDrive()   => SetVisible(hardDrive, false);

        /// <summary>Alterna visibilidade de uma peça (toggle).</summary>
        public void ToggleGabinete()    => Toggle(gabinete);
        public void ToggleRAM()         => Toggle(ram);
        public void ToggleMotherboard() => Toggle(motherboard);
        public void TogglePowerSupply() => Toggle(powerSupply);
        public void ToggleMonitor()     => Toggle(monitor);
        public void ToggleHardDrive()   => Toggle(hardDrive);

        // ── Helpers ─────────────────────────────────────────────────────

        private void SetVisible(GameObject obj, bool visible) {
            if (obj != null) obj.SetActive(visible);
        }

        private void Toggle(GameObject obj) {
            if (obj != null) obj.SetActive(!obj.activeSelf);
        }
    }
}
