using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void LoadFreeRoam()
    {
        SceneManager.LoadScene("FreeRoam");
    }

    public void LoadLobby()
    {
        SceneManager.LoadScene("Lobby");
    }
}