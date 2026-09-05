using Unity.Collections;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using UnityEngine;

/// <summary>
/// Player プレハブに付ける。席番号の同期・色分け・顔の向きの同期・自分だけカメラを有効化。
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    public static readonly Color[] SeatColors =
    {
        new Color(0.90f, 0.32f, 0.30f), // 0: 赤
        new Color(0.32f, 0.56f, 0.90f), // 1: 青
        new Color(0.36f, 0.75f, 0.40f), // 2: 緑
        new Color(0.95f, 0.80f, 0.32f), // 3: 黄
    };

    /// <summary>席番号。サーバーが決める。</summary>
    public NetworkVariable<int> SeatIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>視線角度 (x=左右, y=上下)。見た目だけの情報なので所有者が直接書く。</summary>
    public NetworkVariable<Vector2> LookAngles = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    /// <summary>表示名。ロビー入室時に入力したものを所有者が直接書く。空なら呼び出し側が
    /// "userN" 等にフォールバックする。</summary>
    public NetworkVariable<FixedString64Bytes> DisplayName = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    [Header("参照")]
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Transform eyesRoot;   // 目のまとめ役。頭の高さに置いて回転の中心にする
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener playerAudio;
    [SerializeField] private FirstPersonLook look;

    [Header("一人称表示")]
    [Tooltip("自分のプレイヤーの見た目を隠す。カメラが目の位置にあるため、切ると目玉の内側が映る")]
    [SerializeField] private bool hideOwnBody = true;
    [Tooltip("一人称視点で遊ぶシーン名。ここ以外のシーン(ミニゲーム等)では自動でカメラを止め、姿も隠す")]
    [SerializeField] private string firstPersonSceneName = "Lobby";


    [Header("同期の頻度と滑らかさ")]
    [SerializeField] private float sendInterval = 0.1f;   // 秒
    [SerializeField] private float sendThreshold = 1.5f;  // 度
    [SerializeField] private float turnSharpness = 12f;

    private float nextSendTime;

    /// <summary>ミニゲームなど一人称視点が不要な画面に入る時、自分のカメラ/視点操作を止めるために呼ぶ。</summary>
    public void SetFirstPersonViewActive(bool active)
    {
        if (!IsOwner) return;
        if (playerCamera != null) playerCamera.enabled = active;
        if (playerAudio != null) playerAudio.enabled = active;
        if (look != null) look.enabled = active;
    }

    public override void OnNetworkSpawn()
    {
        SeatIndex.OnValueChanged += HandleSeatChanged;
        ApplyColor(SeatIndex.Value);

        if (IsOwner)
        {
            // ロビーで入力した表示名を一度だけ書き込む(以降はシーンをまたいでも変わらない)。
            DisplayName.Value = ToSafeFixedString(LobbyRoomUI.LocalPlayerName);
        }

        // Player は動的スポーンされた NetworkObject なので、シーンを切り替えても破棄されずに生き残る。
        // つまり OnNetworkSpawn はセッション中に一度しか呼ばれない。
        // カメラと見た目の切り替えはシーンが変わるたびにやり直す必要があるため、
        // ここでは購読だけして実処理は ApplyViewForScene に任せる。
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        ApplyViewForScene(SceneManager.GetActiveScene().name);

        if (eyesRoot != null) eyesRoot.localRotation = TargetRotation();
    }

    public override void OnNetworkDespawn()
    {
        SeatIndex.OnValueChanged -= HandleSeatChanged;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
    }

    public override void OnDestroy()
    {
        // Despawn を経由せずに壊れた場合の保険。二重解除しても害はない。
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        base.OnDestroy();
    }

    private void HandleActiveSceneChanged(Scene previous, Scene next)
    {
        ApplyViewForScene(next.name);
    }

    /// <summary>
    /// シーン名を見て一人称視点にするかどうかを決める。
    /// firstPersonSceneName のとき: 自分のカメラで見る。他人の姿は見える(自分の体だけ隠す)。
    /// それ以外のとき: 自分のカメラを止めてシーン側の固定カメラに任せ、全員の姿を隠す。
    /// Player はシーンをまたいで生き残るため、隠さないと前のシーンの座席位置に浮いたまま映り込む。
    /// </summary>
    private void ApplyViewForScene(string sceneName)
    {
        bool firstPerson = sceneName == firstPersonSceneName;

        if (IsOwner && firstPerson)
        {
            // 自分のカメラを有効にする前にシーン側のカメラを掴む。順番が逆だと
            // Camera.main が自分のカメラを返してしまい、固定カメラを消し損ねる。
            if (playerCamera != null) playerCamera.enabled = false;
            Camera sceneCamera = Camera.main;
            if (sceneCamera != null && sceneCamera != playerCamera)
            {
                sceneCamera.gameObject.SetActive(false);
            }
        }

        SetFirstPersonViewActive(firstPerson);
        SetBodyVisible(firstPerson);
    }

    /// <summary>自分の体はカメラが目の位置にあるので、一人称のときも隠したままにする。</summary>
    private void SetBodyVisible(bool visible)
    {
        bool shouldShow = visible && !(IsOwner && hideOwnBody);
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = shouldShow;
        }
    }


    private void Update()
    {
        if (!IsSpawned) return;

        // 所有者は自分の角度を、変化したときだけ間引いて送る
        if (IsOwner && look != null)
        {
            var current = new Vector2(look.Yaw, look.Pitch);
            if (Time.time >= nextSendTime &&
                (current - LookAngles.Value).sqrMagnitude >= sendThreshold * sendThreshold)
            {
                LookAngles.Value = current;
                nextSendTime = Time.time + sendInterval;
            }
        }

        if (eyesRoot == null) return;

        if (IsOwner)
        {
            // 自分は遅延ゼロ
            eyesRoot.localRotation = TargetRotation();
        }
        else
        {
            // 他人は受け取った角度へ滑らかに追従(間引き送信のカクつきを隠す)
            float k = 1f - Mathf.Exp(-turnSharpness * Time.deltaTime);
            eyesRoot.localRotation = Quaternion.Slerp(eyesRoot.localRotation, TargetRotation(), k);
        }
    }

    /// <summary>自分は遅延ゼロの実測値、他人は同期された値を使う。</summary>
    private Quaternion TargetRotation()
    {
        Vector2 a = (IsOwner && look != null)
            ? new Vector2(look.Yaw, look.Pitch)
            : LookAngles.Value;
        return Quaternion.Euler(a.y, a.x, 0f);
    }

    private void HandleSeatChanged(int previous, int current)
    {
        ApplyColor(current);
    }

    /// <summary>FixedString64Bytesの容量(UTF8で61バイト)をどんな入力でも超えないよう、
    /// 溢れる場合は文字数を切り詰めてから変換する。</summary>
    private static FixedString64Bytes ToSafeFixedString(string value)
    {
        if (string.IsNullOrEmpty(value)) return default;
        string s = value;
        while (System.Text.Encoding.UTF8.GetByteCount(s) > 61 && s.Length > 0)
        {
            s = s.Substring(0, s.Length - 1);
        }
        return new FixedString64Bytes(s);
    }

    private void ApplyColor(int index)
    {
        if (bodyRenderer == null || index < 0) return;
        int i = Mathf.Clamp(index, 0, SeatColors.Length - 1);
        bodyRenderer.material.color = SeatColors[i];
        gameObject.name = "Player_Seat" + i + (IsOwner ? "_Me" : "");
    }
}
