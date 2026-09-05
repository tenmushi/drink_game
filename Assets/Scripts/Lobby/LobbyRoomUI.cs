using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
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
    [SerializeField] private Button startGameButton;   // StatusPanel に置く、ホストだけに見せる

    [Header("テキスト")]
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text joinCodeText;   // パネルの外に置く常時表示

    /// <summary>発行済みの参加コード。シーンを読み直しても消えないよう static。</summary>
    public static string CurrentJoinCode;

    /// <summary>再読み込み直後に強制的に ChoicePanel を出すためのフラグ。</summary>
    public static bool ForceChoicePanel;

    /// <summary>切断メッセージ。次のシーンで表示する。</summary>
    public static string PendingMessage;

    private RelayManager relay;
    private int lastSubmitFrame = -1;

    private void Start()
    {
        relay = FindFirstObjectByType<RelayManager>();
        if (relay == null)
        {
            const string msg = "NetworkManager がありません。NetworkBase シーンから再生してください";
            SetStatus(msg);
            SetInteractable(false);
            // ChoicePanel 表示中は StatusText が見えないので、常時表示側にも出す
            if (joinCodeText != null) joinCodeText.text = msg;
        }
        if (relay != null) RefreshJoinCode();

        if (createButton != null) createButton.onClick.AddListener(OnCreate);
        if (openJoinButton != null) openJoinButton.onClick.AddListener(() => ShowPanel(joinPanel));
        if (backButton != null) backButton.onClick.AddListener(() => ShowPanel(choicePanel));
        if (joinButton != null) joinButton.onClick.AddListener(TrySubmitJoin);
        if (leaveButton != null) leaveButton.onClick.AddListener(OnLeave);
        if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGame);
        if (codeInput != null) codeInput.onSubmit.AddListener(_ => TrySubmitJoin());

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            // 自分の接続が止まったとき(退出・ホスト切断・通信断)に呼ばれる
            nm.OnClientStopped += HandleStopped;
            nm.OnServerStopped += HandleStopped;
        }

        if (ForceChoicePanel)
        {
            // 退出直後。Shutdown の完了を待ってから読み直しているので、素直に選択画面へ
            ForceChoicePanel = false;
            ShowPanel(choicePanel);
            SetInteractable(true);
            if (!string.IsNullOrEmpty(PendingMessage))
            {
                SetStatus(PendingMessage);
                PendingMessage = null;
            }
        }
        else if (nm != null && (nm.IsClient || nm.IsServer))
        {
            ShowPanel(statusPanel);
            SetStatus(nm.IsHost ? "ホストとして待機中" : "接続済み");
            SetStartButtonVisible(nm.IsHost);
        }
        else
        {
            ShowPanel(choicePanel);
        }
    }

    private void Update()
    {
        // JoinPanel を開いている間は、入力欄にフォーカスが無くても Enter で参加できる
        if (joinPanel == null || !joinPanel.activeInHierarchy) return;

        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
        {
            TrySubmitJoin();
        }
    }

    /// <summary>Enter と参加ボタンの共通入口。同じフレームで二重に走らせない。</summary>
    private void TrySubmitJoin()
    {
        if (Time.frameCount == lastSubmitFrame) return;
        lastSubmitFrame = Time.frameCount;

        if (joinButton != null && !joinButton.interactable) return;
        OnJoin();
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
        SetStartButtonVisible(false); // 作成が終わるまでは押せる状態を見せない

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
        SetStartButtonVisible(true);
    }

    private async void OnJoin()
    {
        string code = codeInput != null ? codeInput.text.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(code)) return;

        // Relay の参加コードは英数字6文字。形式が違うなら通信する前に弾く
        if (!IsValidJoinCode(code))
        {
            SetStatus("ルームIDは英数字6文字です。入力を確認してください");
            return;
        }

        SetInteractable(false);
        SetStatus("接続中...");
        ShowPanel(statusPanel);

        await relay.JoinRelay(code);

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient)
        {
            // 失敗時に中途半端な状態が残ると、次の試行が不安定になる。確実に止めておく
            if (nm != null && (nm.IsListening || nm.ShutdownInProgress))
            {
                nm.Shutdown();
            }

            SetStatus("接続に失敗しました。コードを確認してください");
            ShowPanel(joinPanel);   // 入力欄を出したままにして、打ち直せるようにする
            SetInteractable(true);
            return;
        }

        CurrentJoinCode = code;
        RefreshJoinCode();
        SetStatus("接続しました");
        SetStartButtonVisible(false);
    }

    /// <summary>ホストだけが押せる「ゲーム開始」。MemoryGameシーンにはNetworkObjectが入るので、
    /// 全クライアントに同期されるNetworkManagerのSceneManager経由でロードする。</summary>
    private void OnStartGame()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost) return;
        nm.SceneManager.LoadScene("MemoryGame", LoadSceneMode.Single);
    }

    private void SetStartButtonVisible(bool visible)
    {
        if (startGameButton != null) startGameButton.gameObject.SetActive(visible);
    }

    /// <summary>退出ボタン。自分から切る。</summary>
    private void OnLeave()
    {
        Debug.Log("[LobbyRoomUI] 退出します");
        LeaveRunner.Run(true, null);
    }

    /// <summary>ホストが落ちた・通信が切れた等、自分の意思によらず停止したとき。</summary>
    private void HandleStopped(bool wasHost)
    {
        if (LeaveRunner.IsRunning) return;   // 自分で押した退出と二重に走らせない
        Debug.Log("[LobbyRoomUI] 接続が終了しました");
        LeaveRunner.Run(false, wasHost ? null : "ホストとの接続が切れました");
    }

    /// <summary>Relay の参加コード形式(英数字6文字)かどうか。</summary>
    private static bool IsValidJoinCode(string code)
    {
        if (code.Length != 6) return false;
        foreach (char c in code)
        {
            bool ok = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (!ok) return false;
        }
        return true;
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
