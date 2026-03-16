using UnityEngine;

public class ApiSceneConfigurator : MonoBehaviour
{
    [Header("Referências")]
    public Api apiController;
    public RAMModule ramModule;
    public MotherboardPlacer motherboardPlacer;

    private Api.SceneType previousScene;

    void Start()
    {
        if (apiController == null) apiController = FindObjectOfType<Api>();
        if (ramModule == null) ramModule = FindObjectOfType<RAMModule>();
        if (motherboardPlacer == null) motherboardPlacer = FindObjectOfType<MotherboardPlacer>();

        previousScene = apiController.currentScene;
        ApplySceneConfiguration();
    }

    void Update()
    {
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