using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{
    public void MoveToLobby()
    {
        SceneManager.LoadScene("Lobby");
    }

    public void MoveToTitle()
    {
        SceneManager.LoadScene("Title");
    }
}
