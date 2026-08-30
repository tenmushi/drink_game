using UnityEngine;

/// <summary>
/// ロビーにプレイヤーを出す。今は単独プレイ用に Seat_0 へ1体だけ。
/// ネットワーク対応時は Start() の自動スポーンをやめ、
/// サーバーが接続順に SpawnAt(index) を呼ぶ形に差し替える。
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [Header("スポーンさせるプレイヤー")]
    [SerializeField] private GameObject playerPrefab;

    [Header("座席(Seat_0〜3 を順に入れる)")]
    [SerializeField] private Transform[] seats;

    [Header("スポーン後に消すシーン側のカメラ")]
    [SerializeField] private Camera sceneCamera;

    [Header("単独テスト用: 起動時に Seat_0 へ出す")]
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private int testSeatIndex = 0;

    private void Start()
    {
        if (spawnOnStart) SpawnAt(testSeatIndex);
    }

    public GameObject SpawnAt(int seatIndex)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[PlayerSpawner] playerPrefab が未設定です");
            return null;
        }
        if (seats == null || seats.Length == 0)
        {
            Debug.LogError("[PlayerSpawner] seats が未設定です");
            return null;
        }

        seatIndex = Mathf.Clamp(seatIndex, 0, seats.Length - 1);
        Transform seat = seats[seatIndex];

        GameObject player = Instantiate(playerPrefab, seat.position, seat.rotation);
        player.name = "Player_Seat" + seatIndex;

        // プレイヤー側のカメラと二重にならないよう、シーンのカメラを止める
        if (sceneCamera != null) sceneCamera.gameObject.SetActive(false);

        return player;
    }
}
