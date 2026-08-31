using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ロビーのルーム作成/参加UI。
/// 未接続 → 作成/参加ボタン。作成後 → 参加コード表示。参加を選ぶ → コード入力欄。
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

    [Header("テキスト")]
    [SerializeField] private TMP_InputField codeInput;
    [SerializeField] private TMP_Text statusText;

    private RelayManager relay;

    private void Start()
    {
        relay = FindFirstObjectByType<RelayManager>();

        if (createButton != null) createButton.onClick.AddListener(OnCreate);
        if (openJoinButton != null) openJoinButton.onClick.AddListener(() => ShowPanel(joinPanel));
        if (backButton != null) backButton.onClick.AddListener(() => ShowPanel(choicePanel));
        if (joinButton != null) joinButton.onClick.AddListener(OnJoin);

        var nm = NetworkManager.Singleton;
        if (nm != null && (nm.IsClient || nm.IsServer))
        {
            // シーン再読み込み後: すでに接続済みなので選択UIは出さない
            ShowPanel(statusPanel);
            SetStatus(nm.IsHost ? "ホストとして待機中" : "接続済み");
        }
        else
        {
            ShowPanel(choicePanel);
        }
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

        SetStatus("参加コード: " + code + "\n他のプレイヤーの参加を待っています");
    }

    private async void OnJoin()
    {
        string code = codeInput != null ? codeInput.text.Trim() : "";
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

        SetStatus("接続しました");
    }

    private void ShowPanel(GameObject target)
    {
        if (choicePanel != null) choicePanel.SetActive(choicePanel == target);
        if (joinPanel != null) joinPanel.SetActive(joinPanel == target);
        if (statusPanel != null) statusPanel.SetActive(statusPanel == target);
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
