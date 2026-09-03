using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// NetworkBase シーンに置く。NetworkManager を生かしたまま最初の画面へ移す。
/// NetworkManager は自身で DontDestroyOnLoad するので、Single ロードでも消えない。
/// </summary>
public class BootstrapLoader : MonoBehaviour
{
    [Tooltip("起動後に開くシーン。デバッグ中は Lobby を直接指定してもよい")]
    [SerializeField] private string firstScene = "Title";

    private void Start()
    {
        SceneManager.LoadScene(firstScene, LoadSceneMode.Single);
    }
}
