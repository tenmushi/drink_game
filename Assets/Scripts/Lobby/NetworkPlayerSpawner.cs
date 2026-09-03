using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// サーバー(ホスト)だけが動く。接続してきた順に空いている席へプレイヤーを出す。
/// シーンが再読み込みされても二重に出ないよう、PlayerObject の有無で判定する。
/// </summary>
public class NetworkPlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform[] seats;

    private void Start()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogError("[Spawner] NetworkManager が見つかりません。NetworkBase シーンから再生してください。");
            return;
        }

        if (nm.IsServer) Begin();
        else nm.OnServerStarted += Begin;
    }

    private void OnDestroy()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        nm.OnServerStarted -= Begin;
        nm.OnClientConnectedCallback -= SpawnFor;
        if (nm.SceneManager != null) nm.SceneManager.OnLoadComplete -= HandleLoadComplete;
    }

    private void Begin()
    {
        var nm = NetworkManager.Singleton;
        if (!nm.IsServer) return;

        nm.OnClientConnectedCallback -= SpawnFor;
        nm.OnClientConnectedCallback += SpawnFor;

        if (nm.SceneManager != null)
        {
            nm.SceneManager.OnLoadComplete -= HandleLoadComplete;
            nm.SceneManager.OnLoadComplete += HandleLoadComplete;
        }

        // すでに繋がっている人(ホスト自身を含む)を拾う
        foreach (var id in nm.ConnectedClientsIds) SpawnFor(id);
    }

    private void HandleLoadComplete(ulong clientId, string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        SpawnFor(clientId);
    }

    private void SpawnFor(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (playerPrefab == null || seats == null || seats.Length == 0) return;

        // すでにプレイヤーを持っていれば何もしない(二重スポーン防止)
        if (nm.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null) return;

        int seat = FindFreeSeat();
        if (seat < 0)
        {
            Debug.LogWarning("[Spawner] 満席です clientId=" + clientId);
            return;
        }

        var go = Instantiate(playerPrefab, seats[seat].position, seats[seat].rotation);
        var netObj = go.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(clientId);

        var player = go.GetComponent<NetworkPlayer>();
        if (player != null) player.SeatIndex.Value = seat;

        Debug.Log("[Spawner] clientId=" + clientId + " を Seat_" + seat + " に配置");
    }

    private int FindFreeSeat()
    {
        var nm = NetworkManager.Singleton;
        var used = new HashSet<int>();

        foreach (var client in nm.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var p = client.PlayerObject.GetComponent<NetworkPlayer>();
            if (p != null && p.SeatIndex.Value >= 0) used.Add(p.SeatIndex.Value);
        }

        for (int i = 0; i < seats.Length; i++)
        {
            if (!used.Contains(i)) return i;
        }
        return -1;
    }
}
