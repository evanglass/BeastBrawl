using Unity.Netcode;
using UnityEngine;

public class PlayerNetworkBridge : NetworkBehaviour
{
    private PlayerState playerState;

    [SerializeField]
    private NetworkVariable<bool> isConscious = new(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField]
    private NetworkVariable<bool> eliminated = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsConscious => isConscious.Value;

    public bool Eliminated => eliminated.Value;

    private void Awake()
    {
        playerState = GetComponent<PlayerState>();
    }

    public override void OnNetworkSpawn()
    {
        GetComponent<PlayerActor>().ServerSimulated = IsServer;

        if (IsServer)
            Publish();
    }

    public override void OnNetworkDespawn()
    {
        GetComponent<PlayerActor>().ServerSimulated = false;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
            return;

        Publish();
    }

    private void Publish()
    {
        if (!IsServer)
            return;

        isConscious.Value = playerState.IsConscious;
        eliminated.Value = playerState.Eliminated;
    }
}
