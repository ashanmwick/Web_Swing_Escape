using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hook <see cref="GoToLobby"/> up to a UI Button's OnClick to leave FreeRoam
/// and return to the Lobby scene.
/// </summary>
public class LobbyButton : MonoBehaviour
{
    [SerializeField] string lobbySceneName = "Lobby";

    public void GoToLobby()
    {
        // Free the cursor so scene-load / lobby UI is usable, in case the
        // player clicked this via a script rather than the mouse toggle.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadLobby();
        }
        else
        {
            SceneManager.LoadScene(lobbySceneName);
        }
    }
}
