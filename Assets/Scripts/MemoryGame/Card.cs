using System;
using Unity.Netcode;
using UnityEngine;

public enum CardState
{
    Free,
    Selected,
    Returning,
    Matched
}

public class Card : NetworkBehaviour
{
    [SerializeField] private MeshRenderer faceRenderer;
    [SerializeField] private float slotMoveSpeed = 8f;
    [SerializeField] private float returnMoveSpeed = 2f;

    public NetworkVariable<int> NetPairId = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // 0 = ring A (left, red back), 1 = ring B (right, dark back).
    public NetworkVariable<int> NetRingIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> NetState = new NetworkVariable<int>(
        (int)CardState.Free, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> NetFaceUp = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // Which reveal slot this card is heading to/sitting in while Selected: 0=none, 1=A, 2=B.
    public NetworkVariable<int> NetRevealSlot = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Fixed orbit slot params, set once by the server before Spawn(). Combined with the
    // Netcode-synced clock, every peer derives the same position with zero transform sync.
    public NetworkVariable<Vector3> NetOrbitCenter = new NetworkVariable<Vector3>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> NetOrbitRadius = new NetworkVariable<float>(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> NetOrbitAngularSpeed = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> NetSlotAngleOffset = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int PairId => NetPairId.Value;
    public CardState State => (CardState)NetState.Value;
    public bool IsFaceUp => NetFaceUp.Value;

    // Server-only: fires once this card's local simulation reaches its reveal slot.
    private Action serverArrivedCallback;

    // ApplyFaceMaterial can run before this client's local MemoryGameManager.Instance is ready
    // (observed on joining clients, not the host) - if so, keep retrying every frame until it sticks
    // instead of leaving the card on its default placeholder material forever.
    private bool lastAppliedFaceUp;
    private bool materialApplied;

    void Awake()
    {
        if (faceRenderer == null)
        {
            faceRenderer = GetComponent<MeshRenderer>();
        }
    }

    public override void OnNetworkSpawn()
    {
        NetFaceUp.OnValueChanged += HandleFaceUpChanged;
        NetState.OnValueChanged += HandleStateChanged;
        ApplyFaceMaterial(NetFaceUp.Value);
        if (State == CardState.Free)
        {
            transform.position = LiveOrbitPosition();
        }
        if (State == CardState.Matched)
        {
            HideMatchedCard();
        }
    }

    public override void OnNetworkDespawn()
    {
        NetFaceUp.OnValueChanged -= HandleFaceUpChanged;
        NetState.OnValueChanged -= HandleStateChanged;
    }

    void HandleFaceUpChanged(bool previous, bool current)
    {
        ApplyFaceMaterial(current);
    }

    void HandleStateChanged(int previous, int current)
    {
        if ((CardState)current == CardState.Matched)
        {
            HideMatchedCard();
        }
    }

    // Netcode's NetworkObject.Despawn() has proven unreliable for these dynamically-spawned
    // cards (internal NullRefs), so a matched card is simply hidden/disabled locally on every
    // peer instead of being despawned - the NetworkObject stays alive but invisible and inert.
    void HideMatchedCard()
    {
        if (faceRenderer != null) faceRenderer.enabled = false;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    void ApplyFaceMaterial(bool faceUp)
    {
        lastAppliedFaceUp = faceUp;
        if (faceRenderer == null || MemoryGameManager.Instance == null)
        {
            materialApplied = false;
            return;
        }
        faceRenderer.sharedMaterial = faceUp
            ? MemoryGameManager.Instance.GetFaceMaterial(NetPairId.Value)
            : MemoryGameManager.Instance.GetBackMaterial(NetRingIndex.Value);
        materialApplied = true;
    }

    /// <summary>Server only. Assigns this card's fixed orbit slot, pair id and ring before Spawn().</summary>
    public void ServerInit(int pairId, int ringIndex, Vector3 center, float radius, float angularSpeed, float initialAngle)
    {
        NetPairId.Value = pairId;
        NetRingIndex.Value = ringIndex;
        NetOrbitCenter.Value = center;
        NetOrbitRadius.Value = radius;
        NetOrbitAngularSpeed.Value = angularSpeed;
        NetSlotAngleOffset.Value = initialAngle - angularSpeed * NetworkTimeNow();
        NetState.Value = (int)CardState.Free;
        transform.position = LiveOrbitPosition();
    }

    static float NetworkTimeNow()
    {
        return NetworkManager.Singleton != null ? (float)NetworkManager.Singleton.ServerTime.Time : Time.time;
    }

    Vector3 LiveOrbitPosition()
    {
        float angle = NetSlotAngleOffset.Value + NetOrbitAngularSpeed.Value * NetworkTimeNow();
        float x = Mathf.Cos(angle) * NetOrbitRadius.Value;
        float y = Mathf.Sin(angle) * NetOrbitRadius.Value;
        return NetOrbitCenter.Value + new Vector3(x, y, 0f);
    }

    void Update()
    {
        if (!materialApplied)
        {
            ApplyFaceMaterial(lastAppliedFaceUp);
        }

        switch (State)
        {
            case CardState.Free:
                transform.position = LiveOrbitPosition();
                break;

            case CardState.Selected:
            {
                Vector3 target = ResolveSlotTarget();
                transform.position = Vector3.MoveTowards(transform.position, target, slotMoveSpeed * Time.deltaTime);
                // Arrival decides game state, so only the server's own simulation may act on it -
                // clients' copies of this animation might land a few frames apart and that's fine.
                if (IsServer && (transform.position - target).sqrMagnitude < 0.0004f)
                {
                    var callback = serverArrivedCallback;
                    serverArrivedCallback = null;
                    callback?.Invoke();
                }
                break;
            }

            case CardState.Returning:
            {
                Vector3 target = LiveOrbitPosition();
                transform.position = Vector3.MoveTowards(transform.position, target, returnMoveSpeed * Time.deltaTime);
                if (IsServer && (transform.position - target).sqrMagnitude < 0.0009f)
                {
                    NetState.Value = (int)CardState.Free;
                }
                break;
            }
        }
    }

    Vector3 ResolveSlotTarget()
    {
        if (MemoryGameManager.Instance == null) return transform.position;
        Transform slot = NetRevealSlot.Value == 1 ? MemoryGameManager.Instance.RevealSlotA : MemoryGameManager.Instance.RevealSlotB;
        return slot != null ? slot.position : transform.position;
    }

    /// <summary>Server only.</summary>
    public void MoveToSlot(int slotIndex, Action onArrivedServer)
    {
        if (!IsServer) return;
        NetRevealSlot.Value = slotIndex;
        serverArrivedCallback = onArrivedServer;
        NetState.Value = (int)CardState.Selected;
    }

    /// <summary>Server only.</summary>
    public void ReturnToFree()
    {
        if (!IsServer) return;
        NetFaceUp.Value = false;
        NetState.Value = (int)CardState.Returning;
    }

    /// <summary>Server only.</summary>
    public void SetMatched()
    {
        if (!IsServer) return;
        NetState.Value = (int)CardState.Matched;
    }

    /// <summary>Server only.</summary>
    public void SetFaceUp(bool faceUp)
    {
        if (!IsServer) return;
        NetFaceUp.Value = faceUp;
    }
}
