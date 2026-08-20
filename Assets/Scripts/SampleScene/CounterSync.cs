using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CounterSync : NetworkBehaviour
{
    public NetworkVariable<int> counter = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField] private TMP_Text counterText;

    public override void OnNetworkSpawn()
    {
        counter.OnValueChanged += (oldValue, newValue) =>
        {
            UpdateText(newValue);
        };

        UpdateText(counter.Value);
    }

    private void UpdateText(int value)
    {
        if (counterText != null)
        {
            counterText.text = $"Counter: {value}";
        }
    }

    public void OnClickIncrement()
    {
        IncrementRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void IncrementRpc()
    {
        counter.Value++;
    }
}