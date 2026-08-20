using Unity.Netcode;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    private string joinCodeInput = "";
    private string myJoinCode = "";
    private RelayManager relayManager;

    void Start()
    {
        relayManager = GetComponent<RelayManager>();
    }

    void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 200));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("部屋を作る (Host)"))
            {
                CreateRoom();
            }

            GUILayout.Space(10);

            joinCodeInput = GUILayout.TextField(joinCodeInput);
            if (GUILayout.Button("コードで参加 (Client)"))
            {
                JoinRoom();
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(myJoinCode))
            {
                GUILayout.Label("参加コード: " + myJoinCode);
            }
            GUILayout.Label(NetworkManager.Singleton.IsHost ? "Host起動中" : "Client起動中");
        }

        GUILayout.EndArea();
    }

    private async void CreateRoom()
    {
        myJoinCode = await relayManager.CreateRelay();
    }

    private async void JoinRoom()
    {
        await relayManager.JoinRelay(joinCodeInput);
    }
}