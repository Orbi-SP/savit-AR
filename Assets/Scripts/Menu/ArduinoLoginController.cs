using UnityEngine;
using UnityEngine.SceneManagement;

public class ArduinoLoginController : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "BRP Sample SceneGabinete";

    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }
}
