using UnityEngine;

public class ApiSceneConfigurator : MonoBehaviour
{
    [Header("Referências (auto-preenchido se vazio)")]
    public Api apiController;
    public RAMModule ramModule;
    public MotherboardPlacer motherboardPlacer;
    public MotherboardState motherboardState;

    [Header("Fluxo")]
    [Tooltip("Força a cena inicial ao iniciar (evita estados inconsistentes após salvar prefab/cena).")]
    public bool forceStartScene = true;
    public Api.SceneType startScene = Api.SceneType.RAM;

    private Api.SceneType previousScene;
    private bool subscribed;

    private void Awake()
    {
        // Auto-find de todas as referências o mais cedo possível (antes de Start() de outros scripts).
        var root = transform.root;

        if (apiController == null) apiController = GetComponentInParent<Api>();
        if (apiController == null && root != null) apiController = root.GetComponentInChildren<Api>(true);
        if (apiController == null) apiController = FindFirstObjectByType<Api>();

        if (ramModule == null && root != null) ramModule = root.GetComponentInChildren<RAMModule>(true);
        if (ramModule == null) ramModule = FindFirstObjectByType<RAMModule>();

        if (motherboardPlacer == null && root != null) motherboardPlacer = root.GetComponentInChildren<MotherboardPlacer>(true);
        if (motherboardPlacer == null) motherboardPlacer = FindFirstObjectByType<MotherboardPlacer>();

        if (motherboardState == null && root != null) motherboardState = root.GetComponentInChildren<MotherboardState>(true);
        if (motherboardState == null) motherboardState = FindFirstObjectByType<MotherboardState>();

        if (apiController != null && forceStartScene)
        {
            // Evita regressão: se a RAM já estiver instalada, não force voltar para RAM.
            var desired = startScene;
            if (motherboardState != null && motherboardState.HasRequiredMemory)
            {
                desired = Api.SceneType.Gabinete;
            }

            if (apiController.currentScene != desired)
            {
                Debug.Log($"[ApiSceneConfigurator] forceStartScene: {apiController.currentScene} → {desired} (hasRam={motherboardState?.HasRequiredMemory})");
                apiController.currentScene = desired;
            }
        }

        if (apiController != null)
        {
            previousScene = apiController.currentScene;
            ApplySceneConfiguration();
        }

        SubscribeMotherboardEventsIfNeeded();
    }

    private void OnDestroy()
    {
        if (!subscribed) return;
        if (motherboardState == null) return;

        motherboardState.onRequirementsMet.RemoveListener(OnMotherboardRequirementsMet);
        motherboardState.onRequirementsUnmet.RemoveListener(OnMotherboardRequirementsUnmet);
        subscribed = false;
    }

    void Start()
    {
        Debug.Log($"[ApiSceneConfigurator] Start: api={(apiController != null ? "OK" : "NULL")} " +
                  $"ram={(ramModule != null ? "OK" : "NULL")} " +
                  $"mb={(motherboardPlacer != null ? "OK" : "NULL")} " +
                  $"mbState={(motherboardState != null ? "OK" : "NULL")} " +
                  $"scene={apiController?.currentScene}");

        SubscribeMotherboardEventsIfNeeded();
    }

    void Update()
    {
        if (apiController == null) return;

        if (apiController.currentScene != previousScene)
        {
            previousScene = apiController.currentScene;
            ApplySceneConfiguration();
        }
    }

    private void SubscribeMotherboardEventsIfNeeded()
    {
        if (subscribed) return;
        if (motherboardState == null) return;

        motherboardState.onRequirementsMet.AddListener(OnMotherboardRequirementsMet);
        motherboardState.onRequirementsUnmet.AddListener(OnMotherboardRequirementsUnmet);
        subscribed = true;
    }

    private void OnMotherboardRequirementsMet()
    {
        if (apiController == null) apiController = FindFirstObjectByType<Api>();
        if (apiController == null) return;

        if (apiController.currentScene != Api.SceneType.Gabinete)
        {
            var prev = apiController.currentScene;
            apiController.currentScene = Api.SceneType.Gabinete;
            Debug.Log($"[ApiSceneConfigurator] ✅ Requisitos OK (RAM instalada). currentScene {prev} → {apiController.currentScene}");

            previousScene = apiController.currentScene;
            ApplySceneConfiguration();
        }
    }

    private void OnMotherboardRequirementsUnmet()
    {
        // Mantém simples: não volta para RAM automaticamente.
        Debug.Log("[ApiSceneConfigurator] ℹ️ Requisitos voltaram a ficar incompletos.");
    }

    void ApplySceneConfiguration()
    {
        switch (apiController.currentScene)
        {
            case Api.SceneType.RAM:
                if (ramModule != null)
                {
                    ramModule.api = apiController;
                }
                if (motherboardPlacer != null)
                {
                    // Mantém o componente habilitado (ordem de Start/Enable no Unity é imprevisível),
                    // e deixa o gate acontecer pelo próprio MotherboardPlacer via api.currentScene.
                    motherboardPlacer.api = apiController;
                }
                Debug.Log("⚙️ Cena RAM: RAM usa Api; Motherboard mantém Api mas fica bloqueada por estado.");
                break;

            case Api.SceneType.Gabinete:
                if (ramModule != null)
                {
                    ramModule.api = null;
                    // Deixe o gate acontecer no próprio RAMModule via api.currentScene.
                }
                if (motherboardPlacer != null)
                {
                    motherboardPlacer.api = apiController;
                }
                Debug.Log("⚙️ Cena Gabinete: Motherboard usa Api, RAM sem Api.");
                break;

            default:
                if (ramModule != null)
                {
                    ramModule.api = null;
                }
                if (motherboardPlacer != null)
                {
                    motherboardPlacer.api = apiController;
                }
                Debug.Log("⚙️ Outra Cena: RAM sem Api; Motherboard mantém Api mas o gate depende do estado.");
                break;
        }
    }
}