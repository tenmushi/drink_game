using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MemoryGameManager : NetworkBehaviour
{
    public static MemoryGameManager Instance { get; private set; }

    [Header("Board (2つの円軌道、半分ずつ・逆回転で交差させる)")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private int pairCount = 8;
    [SerializeField] private Vector3 ringACenter = new Vector3(-1.8f, 1.5f, 2f);
    [SerializeField] private Vector3 ringBCenter = new Vector3(1.8f, 1.5f, 2f);
    [SerializeField] private float ringRadius = 3f;
    [SerializeField] private float ringAngularSpeedDeg = 25f;

    [Header("Reveal Slots")]
    [SerializeField] private Transform revealSlotA;
    [SerializeField] private Transform revealSlotB;
    [SerializeField] private float revealDuration = 1f;

    [Header("Card Faces (プレースホルダー: 空なら自動生成、後で画像を割り当て可)")]
    [SerializeField] private List<Texture2D> cardFaceTextures = new List<Texture2D>();
    [SerializeField] private Texture2D cardBackTextureRingA;
    [SerializeField] private Texture2D cardBackTextureRingB;

    [Header("HUD")]
    [SerializeField] private Camera fixedCamera; // 固定視点カメラを直接参照(Camera.mainのタグ検索は使わない)
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text turnText;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button returnToLobbyButton;
    [SerializeField] private Button quickReturnToLobbyButton; // プレイ中もホストだけに常時表示

    public Transform RevealSlotA => revealSlotA;
    public Transform RevealSlotB => revealSlotB;

    private static readonly Color[] PlaceholderPalette =
    {
        Color.red, Color.green, Color.blue, Color.yellow,
        new Color(1f, 0.5f, 0f), Color.magenta, Color.cyan, new Color(0.6f, 0.3f, 0.9f),
        Color.white, new Color(0.3f, 0.7f, 0.3f), new Color(0.8f, 0.2f, 0.4f), new Color(0.2f, 0.5f, 0.8f)
    };

    // Turn order and score ride on NetworkPlayer.SeatIndex (Assets/Scripts/Lobby/NetworkPlayer.cs) -
    // this manager never keeps its own player roster.
    public NetworkVariable<int> CurrentTurnSeat = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Score0 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Score1 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Score2 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Score3 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> GameOver = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Placeholder materials are rebuilt identically (and locally) by every peer from the same
    // Inspector-serialized textures/palette, so pairId/ring is all that ever needs to travel over the network.
    private Material backMaterialRingA;
    private Material backMaterialRingB;
    private List<Material> faceMaterials;

    // Server-only board/selection bookkeeping.
    private readonly List<Card> cards = new List<Card>();
    private readonly List<Card> selectedCards = new List<Card>();
    private bool resolving;
    private int arrivedCount;
    private string lastTurnText;

    private Camera mainCamera;

    void Awake()
    {
        Instance = this;
        PrepareMaterials();

        // Lobby から一人称カメラを持ち越したままだと固定カメラと二重に描画されて向きがおかしくなる。
        // このミニゲームでは視点移動は不要なので、自分のプレイヤーの一人称視点は止めて固定カメラだけ使う。
        // NetworkManager.LocalClient.PlayerObject はシーン遷移直後だと(特にホスト以外のクライアントで)
        // まだ解決できないことがあるため、LocalNetworkPlayer() 頼みにせず全プレイヤーを見て回る -
        // SetFirstPersonViewActive は内部で IsOwner を見るので、他人の分を呼んでも何も起きない。
        // 重要: プレイヤーの一人称カメラも "MainCamera" タグを持っているため、これを無効化する前に
        // Camera.main を読むと、どちらが返るかは不定(2人目以降でカードの裏を向いたプレイヤー自身の
        // カメラが選ばれてしまうことがあった)。無効化を先に済ませてから解決する。
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            player.SetFirstPersonViewActive(false);
            player.SetBodyVisible(false); // カード列に体が浮いて映るのを防ぐ(所有者以外の体もここで消す)
        }
        mainCamera = fixedCamera != null ? fixedCamera : Camera.main;
        if (mainCamera != null)
        {
            mainCamera.gameObject.SetActive(true);
            var listener = mainCamera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = true;
        }
    }

    public override void OnNetworkSpawn()
    {
        Score0.OnValueChanged += (_, __) => RefreshScoreText();
        Score1.OnValueChanged += (_, __) => RefreshScoreText();
        Score2.OnValueChanged += (_, __) => RefreshScoreText();
        Score3.OnValueChanged += (_, __) => RefreshScoreText();
        GameOver.OnValueChanged += (_, isOver) => RefreshGameOverUI(isOver);
        CurrentTurnSeat.OnValueChanged += (_, __) => RefreshTurnText();
        RefreshScoreText();
        RefreshGameOverUI(GameOver.Value);
        RefreshTurnText();

        if (returnToLobbyButton != null)
        {
            returnToLobbyButton.onClick.AddListener(OnReturnToLobby);
        }
        if (quickReturnToLobbyButton != null)
        {
            quickReturnToLobbyButton.onClick.AddListener(OnReturnToLobby);
            quickReturnToLobbyButton.gameObject.SetActive(IsHost);
        }

        if (IsServer)
        {
            BuildBoard();
        }
    }

    void Update()
    {
        if (!IsSpawned) return;

        // LocalNetworkPlayer() can resolve a frame or two late on a joining client, so keep
        // retrying here until it succeeds instead of only reacting to CurrentTurnSeat changes.
        if (lastTurnText == null) RefreshTurnText();

        if (mainCamera == null) return;
        if (Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        // UX-only local gate (is it my seat's turn) - the server re-checks this for real.
        NetworkPlayer localPlayer = LocalNetworkPlayer();
        if (localPlayer == null || localPlayer.SeatIndex.Value != CurrentTurnSeat.Value) return;

        Vector2 screenPos = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            Card card = hit.collider.GetComponentInParent<Card>();
            if (card != null)
            {
                RequestSelectCardRpc(card.NetworkObject);
            }
        }
    }

    static NetworkPlayer LocalNetworkPlayer()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return null;
        NetworkObject playerObj = nm.LocalClient.PlayerObject;
        return playerObj != null ? playerObj.GetComponent<NetworkPlayer>() : null;
    }

    void PrepareMaterials()
    {
        backMaterialRingA = CreatePlaceholderMaterial(cardBackTextureRingA, new Color(0.7f, 0.1f, 0.1f));
        backMaterialRingB = CreatePlaceholderMaterial(cardBackTextureRingB, new Color(0.1f, 0.1f, 0.15f));
        faceMaterials = new List<Material>(pairCount);
        for (int i = 0; i < pairCount; i++)
        {
            Texture2D tex = i < cardFaceTextures.Count ? cardFaceTextures[i] : null;
            Color color = PlaceholderPalette[i % PlaceholderPalette.Length];
            faceMaterials.Add(CreatePlaceholderMaterial(tex, color));
        }
    }

    /// <summary>0 = ring A (left, red), 1 = ring B (right, dark).</summary>
    public Material GetBackMaterial(int ringIndex) => ringIndex == 0 ? backMaterialRingA : backMaterialRingB;

    public Material GetFaceMaterial(int pairId)
    {
        if (faceMaterials != null && pairId >= 0 && pairId < faceMaterials.Count)
        {
            return faceMaterials[pairId];
        }
        return backMaterialRingA;
    }

    Material CreatePlaceholderMaterial(Texture2D tex, Color fallback)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        var mat = new Material(shader);
        if (tex != null)
        {
            mat.mainTexture = tex;
            mat.color = Color.white;
            // The cube face that ends up toward the camera has both its U and V axes reversed
            // (verified directly against the mesh's UVs) - a 180-degree flip corrects it.
            mat.mainTextureScale = new Vector2(-1f, -1f);
            mat.mainTextureOffset = new Vector2(1f, 1f);
        }
        else
        {
            mat.color = fallback;
        }
        return mat;
    }

    /// <summary>Server only.</summary>
    void BuildBoard()
    {
        if (cardPrefab == null)
        {
            Debug.LogError("MemoryGameManager: cardPrefab is not assigned.");
            return;
        }

        var pairIds = new List<int>(pairCount * 2);
        for (int i = 0; i < pairCount; i++)
        {
            pairIds.Add(i);
            pairIds.Add(i);
        }
        Shuffle(pairIds);

        int totalCards = pairIds.Count;
        int perRing = totalCards / 2; // alternating assignment always splits evenly (totalCards = pairCount*2)
        float angularSpeedMag = ringAngularSpeedDeg * Mathf.Deg2Rad;
        int indexInRingA = 0;
        int indexInRingB = 0;
        for (int i = 0; i < totalCards; i++)
        {
            bool ringA = (i % 2 == 0);
            GameObject go = Instantiate(cardPrefab);
            Card card = go.GetComponent<Card>();

            Vector3 center = ringA ? ringACenter : ringBCenter;
            // Opposite spin per ring so the two circles visibly cross/weave against each other.
            float angularSpeed = ringA ? angularSpeedMag : -angularSpeedMag;
            int indexInRing = ringA ? indexInRingA++ : indexInRingB++;
            float startAngle = (Mathf.PI * 2f) * indexInRing / perRing;

            int ringIndex = ringA ? 0 : 1;
            card.ServerInit(pairIds[i], ringIndex, center, ringRadius, angularSpeed, startAngle);
            go.GetComponent<NetworkObject>().Spawn();
            cards.Add(card);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSelectCardRpc(NetworkObjectReference cardRef, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out var senderClient)) return;
        NetworkPlayer senderPlayer = senderClient.PlayerObject != null ? senderClient.PlayerObject.GetComponent<NetworkPlayer>() : null;
        if (senderPlayer == null || senderPlayer.SeatIndex.Value != CurrentTurnSeat.Value) return;

        if (!cardRef.TryGet(out NetworkObject netObj)) return;
        Card card = netObj.GetComponent<Card>();
        if (card != null)
        {
            TrySelect(card);
        }
    }

    /// <summary>Server only.</summary>
    void TrySelect(Card card)
    {
        if (!IsServer) return;
        if (card.State != CardState.Free) return;
        if (selectedCards.Count >= 2) return;
        if (selectedCards.Contains(card)) return;

        int slotIndex = selectedCards.Count == 0 ? 1 : 2; // 1=A, 2=B
        selectedCards.Add(card);
        card.MoveToSlot(slotIndex, () => OnCardArrivedAtSlot(card));
    }

    /// <summary>Server only.</summary>
    void OnCardArrivedAtSlot(Card card)
    {
        card.SetFaceUp(true);
        arrivedCount++;
        if (!resolving && selectedCards.Count == 2 && arrivedCount >= 2)
        {
            resolving = true;
            Invoke(nameof(ResolveSelection), revealDuration);
        }
    }

    /// <summary>Server only.</summary>
    void ResolveSelection()
    {
        resolving = false;
        arrivedCount = 0;
        Card a = selectedCards[0];
        Card b = selectedCards[1];
        selectedCards.Clear();

        if (a.PairId == b.PairId)
        {
            AddScore(CurrentTurnSeat.Value);
            a.SetMatched();
            b.SetMatched();
            cards.Remove(a);
            cards.Remove(b);

            if (cards.Count == 0)
            {
                GameOver.Value = true;
            }
        }
        else
        {
            a.ReturnToFree();
            b.ReturnToFree();
            AdvanceTurnToNextSeat();
        }
    }

    void RefreshScoreText()
    {
        if (scoreText == null) return;

        var lines = new List<string>();
        for (int seat = 0; seat < NetworkPlayer.SeatColors.Length; seat++)
        {
            if (!IsSeatConnected(seat)) continue;
            Color c = NetworkPlayer.SeatColors[seat];
            string hex = ColorUtility.ToHtmlStringRGB(c);
            lines.Add($"<color=#{hex}>user{seat + 1}</color>: {GetScore(seat)}組");
        }
        scoreText.text = string.Join("\n", lines);
    }

    void RefreshTurnText()
    {
        if (turnText == null) return;
        NetworkPlayer local = LocalNetworkPlayer();
        if (local == null) return; // retry next frame (see Update())

        int localSeat = local.SeatIndex.Value;
        int turnSeat = CurrentTurnSeat.Value;
        string text = turnSeat == localSeat ? "あなたのターンです" : $"user{turnSeat + 1}のターン中です";
        if (text == lastTurnText) return;
        lastTurnText = text;
        turnText.text = text;
    }

    void RefreshGameOverUI(bool isOver)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(isOver);
        if (!isOver) return;

        int bestSeat = -1;
        int bestScore = -1;
        bool tie = false;
        for (int seat = 0; seat < NetworkPlayer.SeatColors.Length; seat++)
        {
            if (!IsSeatConnected(seat)) continue;
            int score = GetScore(seat);
            if (score > bestScore)
            {
                bestScore = score;
                bestSeat = seat;
                tie = false;
            }
            else if (score == bestScore)
            {
                tie = true;
            }
        }

        if (resultText != null)
        {
            resultText.text = tie || bestSeat < 0 ? "引き分け!" : $"user{bestSeat + 1} の勝ち!";
        }

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        if (returnToLobbyButton != null)
        {
            returnToLobbyButton.gameObject.SetActive(isHost);
        }
    }

    void OnReturnToLobby()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost) return;
        nm.SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
    }

    static bool IsSeatConnected(int seat)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return false;
        foreach (var kvp in nm.ConnectedClients)
        {
            NetworkPlayer player = kvp.Value.PlayerObject != null ? kvp.Value.PlayerObject.GetComponent<NetworkPlayer>() : null;
            if (player != null && player.SeatIndex.Value == seat) return true;
        }
        return false;
    }

    int GetScore(int seat)
    {
        switch (seat)
        {
            case 0: return Score0.Value;
            case 1: return Score1.Value;
            case 2: return Score2.Value;
            case 3: return Score3.Value;
            default: return 0;
        }
    }

    void AddScore(int seat)
    {
        switch (seat)
        {
            case 0: Score0.Value++; break;
            case 1: Score1.Value++; break;
            case 2: Score2.Value++; break;
            case 3: Score3.Value++; break;
        }
    }

    void AdvanceTurnToNextSeat()
    {
        var seats = new List<int>();
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            NetworkPlayer player = kvp.Value.PlayerObject != null ? kvp.Value.PlayerObject.GetComponent<NetworkPlayer>() : null;
            if (player != null && player.SeatIndex.Value >= 0)
            {
                seats.Add(player.SeatIndex.Value);
            }
        }
        if (seats.Count == 0) return;
        seats.Sort();

        int current = CurrentTurnSeat.Value;
        foreach (int seat in seats)
        {
            if (seat > current)
            {
                CurrentTurnSeat.Value = seat;
                return;
            }
        }
        CurrentTurnSeat.Value = seats[0];
    }

    static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
