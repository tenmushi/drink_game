using Unity.Netcode;
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

    [Header("参照")]
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Transform eyesRoot;   // 目のまとめ役。頭の高さに置いて回転の中心にする
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener playerAudio;
    [SerializeField] private FirstPersonLook look;

    [Header("一人称表示")]
    [Tooltip("自分のプレイヤーの見た目を隠す。カメラが目の位置にあるため、切ると目玉の内側が映る")]
    [SerializeField] private bool hideOwnBody = true;

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

    /// <summary>ミニゲームなど体を見せたくない画面用。所有権を問わず誰の分でも呼べる(各クライアントが
    /// ローカルで見た目だけ消すので、ネットワーク同期は不要)。</summary>
    public void SetBodyVisible(bool visible)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = visible;
        }
    }

    public override void OnNetworkSpawn()
    {
        SeatIndex.OnValueChanged += HandleSeatChanged;
        ApplyColor(SeatIndex.Value);

        if (IsOwner)
        {
            if (playerCamera != null) playerCamera.enabled = true;
            if (playerAudio != null) playerAudio.enabled = true;
            if (look != null) look.enabled = true;

            if (Camera.main != null && Camera.main != playerCamera)
            {
                Camera.main.gameObject.SetActive(false);
            }

            // 自分の体と目は隠す。カメラが目の位置にあるので、そのままだと目玉の内側が映る
            if (hideOwnBody)
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
            }
        }

        if (eyesRoot != null) eyesRoot.localRotation = TargetRotation();
    }

    public override void OnNetworkDespawn()
    {
        SeatIndex.OnValueChanged -= HandleSeatChanged;
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

    private void ApplyColor(int index)
    {
        if (bodyRenderer == null || index < 0) return;
        int i = Mathf.Clamp(index, 0, SeatColors.Length - 1);
        bodyRenderer.material.color = SeatColors[i];
        gameObject.name = "Player_Seat" + i + (IsOwner ? "_Me" : "");
    }
}
