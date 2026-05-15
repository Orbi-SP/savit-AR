using UnityEngine;

public class ApiSceneConfigurator : MonoBehaviour
{
    [Header("Referências (auto-preenchido se vazio)")]
    public Api apiController;
    public RAMModule ramModule;
    public MotherboardPlacer motherboardPlacer;

    private Api.SceneType previousScene;

    void Start()
    {
        // Auto-find de todas as referências
        if (apiController == null) apiController = FindFirstObjectByType<Api>();
        if (ramModule == null) ramModule = FindFirstObjectByType<RAMModule>();
        if (motherboardPlacer == null) motherboardPlacer = FindFirstObjectByType<MotherboardPlacer>();

        Debug.Log($"[ApiSceneConfigurator] Start: api={(apiController != null ? "OK" : "NULL")} " +
                  $"ram={(ramModule != null ? "OK" : "NULL")} " +
                  $"mb={(motherboardPlacer != null ? "OK" : "NULL")} " +
                  $"scene={apiController?.currentScene}");

        if (apiController != null)
        {
            previousScene = apiController.currentScene;
            ApplySceneConfiguration();
        }
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

    void ApplySceneConfiguration()
    {
        switch (apiController.currentScene)
        {
            case Api.SceneType.RAM:
                if (ramModule != null) ramModule.api = apiController;
                if (motherboardPlacer != null) motherboardPlacer.api = null;
                Debug.Log("⚙️ Cena RAM: RAM usa Api, Motherboard sem Api.");
                break;

            case Api.SceneType.Gabinete:
                if (ramModule != null) ramModule.api = null;
                if (motherboardPlacer != null) motherboardPlacer.api = apiController;
                Debug.Log("⚙️ Cena Gabinete: Motherboard usa Api, RAM sem Api.");
                break;

            default:
                if (ramModule != null) ramModule.api = null;
                if (motherboardPlacer != null) motherboardPlacer.api = null;
                Debug.Log("⚙️ Outra Cena: Nenhum recebe Api.");
                break;
        }
    }
}