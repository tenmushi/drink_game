using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player プレハブに付ける。席番号の同期・色分け・自分だけカメラを有効化。
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

    public NetworkVariable<int> SeatIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener playerAudio;
    [SerializeField] private FirstPersonLook look;

    public override void OnNetworkSpawn()
    {
        SeatIndex.OnValueChanged += HandleSeatChanged;
        ApplyColor(SeatIndex.Value);

        if (IsOwner)
        {
            // 自分のプレイヤーだけカメラと操作を有効にする
            if (playerCamera != null) playerCamera.enabled = true;
            if (playerAudio != null) playerAudio.enabled = true;
            if (look != null) look.enabled = true;

            // シーンに置いてある観客用カメラを止める
            if (Camera.main != null && Camera.main != playerCamera)
            {
                Camera.main.gameObject.SetActive(false);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        SeatIndex.OnValueChanged -= HandleSeatChanged;
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
