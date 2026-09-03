using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ロビーのルーム作成/参加UI。
/// 未接続 → 作成/参加ボタン。作成後 → 参加コード表示。参加を選ぶ → コード入力欄。
/// 退出・切断時はシーンを読み直して ChoicePanel に戻す。
/// </summary>
public class LobbyRoomUI : MonoBehaviour
{
    [Header("パネル")]
    [SerializeField] private GameObject choicePanel;   // 作成 / 参加 の2ボタン
    [SerializeField] private GameObject joinPanel;     // コード入力欄 + 参加ボタン
    [SerializeField] private GameObject statusPanel;   // 接続後の表示

    [Header("ボタン")]
    [SerializeField] private Button createButton;
    [SerializeField] private Button openJoinButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button leaveButton;   // StatusPanel に置く「戻る/退出」

    [Header("テキスト")]
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text joinCodeText;   // パネルの外に置く常時表示

    /// <summary>発行済みの参加コード。シーンを読み直しても消えないよう static。</summary>
    public static string CurrentJoinCode;

    /// <summary>再読み込み直後に強制的に ChoicePanel を出すためのフラグ。</summary>
    private static bool forceChoicePanel;

    /// <summary>切断メッセージ。次のシーンで表示する。</summary>
    private static string pendingMessage;

    private RelayManager relay;
    private bool leaving;

    private void Start()
    {
        relay = FindFirstObjectByType<RelayManager>();
        if (relay == null)
        {
            SetStatus("NetworkManager が見つかりません。NetworkBase シーンから再生してください");
            SetInteractable(false);
        }
        RefreshJoinCode();

        if (createButton != null) createButton.onClick.AddListener(OnCreate);
        if (openJoinButton != null) openJoinButton.onClick.AddListener(() => ShowPanel(joinPanel));
        if (backButton != null) backButton.onClick.AddListener(() => ShowPanel(choicePanel));
        if (joinButton != null) joinButton.onClick.AddListener(OnJoin);
        if (leaveButton != null) leaveButton.onClick.AddListener(OnLeave);

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            // 自分の接続が止まったとき(退出・ホスト切断・通信断)に呼ばれる
            nm.OnClientStopped += HandleStopped;
            nm.OnServerStopped += HandleStopped;
        }

        if (forceChoicePanel)
        {
            // 退出直後。Shutdown の完了を待ってから読み直しているので、素直に選択画面へ
            forceChoicePanel = false;
            ShowPanel(choicePanel);
            SetInteractable(true);
            if (!string.IsNullOrEmpty(pendingMessage))
            {
                SetStatus(pendingMessage);
                pendingMessage = null;
            }
        }
        else if (nm != null && (nm.IsClient || nm.IsServer))
        {
            ShowPanel(statusPanel);
            SetStatus(nm.IsHost ? "ホストとして待機中" : "接続済み");
        }
        else
        {
            ShowPanel(choicePanel);
        }
    }

    private void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnClientStopped -= HandleStopped;
        nm.OnServerStopped -= HandleStopped;
    }

    private async void OnCreate()
    {
        SetInteractable(false);
        SetStatus("ルームを作成中...");
        ShowPanel(statusPanel);

        string code = await relay.CreateRelay();

        if (string.IsNullOrEmpty(code))
        {
            SetStatus("作成に失敗しました");
            ShowPanel(choicePanel);
            SetInteractable(true);
            return;
        }

        CurrentJoinCode = code;
        RefreshJoinCode();
        SetStatus("他のプレイヤーの参加を待っています");
    }

    private async void OnJoin()
    {
        string code = codeInput != null ? codeInput.text.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(code)) return;

        SetInteractable(false);
        SetStatus("接続中...");
        ShowPanel(statusPanel);

        await relay.JoinRelay(code);

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient)
        {
            SetStatus("接続に失敗しました。コードを確認してください");
            ShowPanel(choicePanel);
            SetInteractable(true);
            return;
        }

        CurrentJoinCode = code;
        RefreshJoinCode();
        SetStatus("接続しました");
    }

    /// <summary>退出ボタン。自分から切る。</summary>
    private void OnLeave()
    {
        if (leaving) return;
        leaving = true;
        Debug.Log("[LobbyRoomUI] 退出します");
        StartCoroutine(LeaveRoutine(true, null));
    }

    /// <summary>ホストが落ちた・通信が切れた等、自分の意思によらず停止したとき。</summary>
    private void HandleStopped(bool wasHost)
    {
        if (leaving) return;
        leaving = true;
        Debug.Log("[LobbyRoomUI] 接続が終了しました");
        StartCoroutine(LeaveRoutine(false, wasHost ? null : "ホストとの接続が切れました"));
    }

    /// <summary>
    /// Shutdown は即座には終わらないので、完全に停止してからシーンを読み直す。
    /// ここを待たずに読み直すと、再開後も接続中と判定されて StatusPanel が出てしまう。
    /// </summary>
    private IEnumerator LeaveRoutine(bool callShutdown, string message)
    {
        forceChoicePanel = true;
        pendingMessage = message;
        CurrentJoinCode = null;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        var nm = NetworkManager.Singleton;
        if (callShutdown && nm != null && (nm.IsClient || nm.IsServer))
        {
            nm.Shutdown();
        }

        float elapsed = 0f;
        while (nm != null && (nm.ShutdownInProgress || nm.IsListening) && elapsed < 3f)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return null;   // 停止処理が完全に反映される1フレームを待つ

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void ShowPanel(GameObject target)
    {
        if (choicePanel != null) choicePanel.SetActive(choicePanel == target);
        if (joinPanel != null) joinPanel.SetActive(joinPanel == target);
        if (statusPanel != null) statusPanel.SetActive(statusPanel == target);
    }

    private void RefreshJoinCode()
    {
        if (joinCodeText == null) return;
        joinCodeText.text = string.IsNullOrEmpty(CurrentJoinCode)
            ? ""
            : "ルームID  " + CurrentJoinCode;
    }

    private void SetStatus(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.Log("[LobbyRoomUI] " + message);
    }

    private void SetInteractable(bool value)
    {
        if (createButton != null) createButton.interactable = value;
        if (openJoinButton != null) openJoinButton.interactable = value;
        if (joinButton != null) joinButton.interactable = value;
    }
}
