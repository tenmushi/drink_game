using Unity.Netcode;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    void OnGUI()
    {
        // NetworkManagerがまだ準備できていない場合は何もしない
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 200, 150));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Host")) NetworkManager.Singleton.StartHost();
            if (GUILayout.Button("Client")) NetworkManager.Singleton.StartClient();
        }
        else
        {
            GUILayout.Label(NetworkManager.Singleton.IsHost ? "Host起動中" : "Client起動中");
        }

        GUILayout.EndArea();
    }
}