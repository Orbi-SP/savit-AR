using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuController : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "BRP Sample SceneGabinete";

    [Tooltip("Exige login antes de \"Iniciar Jogo\". A tela é montada pelo LoginController.")]
    [SerializeField] private bool requireLogin = true;

    private void Awake()
    {
        if (requireLogin && GetComponent<LoginController>() == null)
            gameObject.AddComponent<LoginController>();
        if (GetComponent<RankingController>() == null)
            gameObject.AddComponent<RankingController>();
    }

    public void StartGame()
    {
        if (requireLogin && !AuthSession.IsLoggedIn)
        {
            Debug.LogWarning("[MenuController] Faça login antes de iniciar o jogo.");
            return;
        }

        LoginController.ApplyGameOrientation();
        SceneManager.LoadScene(gameSceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
