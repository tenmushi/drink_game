using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 退出・切断時の復帰処理を実行する使い捨てオブジェクト。
///
/// LobbyRoomUI 自身に持たせると、UI パネルが非アクティブになった瞬間に
/// コルーチンを開始できなくなる(Unity の仕様)。
/// 独立した GameObject を DontDestroyOnLoad で立てて、そこで走らせる。
/// </summary>
public class LeaveRunner : MonoBehaviour
{
    public static bool IsRunning { get; private set; }

    public static void Run(bool callShutdown, string message)
    {
        if (IsRunning) return;
        IsRunning = true;

        var go = new GameObject("~LeaveRunner");
        DontDestroyOnLoad(go);
        go.AddComponent<LeaveRunner>().StartCoroutine(Routine(go, callShutdown, message));
    }

    private static IEnumerator Routine(GameObject self, bool callShutdown, string message)
    {
        LobbyRoomUI.ForceChoicePanel = true;
        LobbyRoomUI.PendingMessage = message;
        LobbyRoomUI.CurrentJoinCode = null;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        var nm = NetworkManager.Singleton;
        if (callShutdown && nm != null && (nm.IsClient || nm.IsServer))
        {
            nm.Shutdown();
        }

        // Shutdown は即座には終わらない。完全に停止するまで待つ。
        // ここを待たずに読み直すと、再開後も接続中と判定されてしまう。
        float elapsed = 0f;
        while (nm != null && (nm.ShutdownInProgress || nm.IsListening) && elapsed < 3f)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return null;

        IsRunning = false;
        Destroy(self);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
