using UnityEngine;
using UnityEngine.SceneManagement;

public class ArduinoLoginController : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "BRP Sample SceneGabinete";

    public void StartGame()
    {
        // Mesmo caminho do menu: sem login não entra no jogo.
        if (!AuthSession.IsLoggedIn)
        {
            Debug.LogWarning("[ArduinoLoginController] Faça login antes de iniciar o jogo.");
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }
}
