using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNetworkBridge : NetworkBehaviour
{
    private PlayerState playerState;
    private PlayerMove playerMove;
    private Rigidbody2D rb;
    private NetworkVariable<bool> walking = new(false);

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

    [SerializeField]
    private NetworkVariable<int> facing = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public float MoveHorizontal { get; private set; }
    public bool Jump { get; private set; }
    public bool Punch { get; private set; }
    public bool Beast { get; private set; }

    public bool IsConscious => isConscious.Value;

    public bool Eliminated => eliminated.Value;

    public int Facing => facing.Value;
    public bool Walking => walking.Value;

    private void Awake()
    {
        playerState = GetComponent<PlayerState>();
        playerMove = GetComponent<PlayerMove>();
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        GetComponent<PlayerActor>().ServerSimulated = IsServer;
        GetComponent<PlayerInput>().enabled = IsClient && IsOwner;

        if (IsServer)
        {
            Publish();
        }
    }

    public override void OnNetworkDespawn()
    {
        GetComponent<PlayerActor>().ServerSimulated = false;
        GetComponent<PlayerInput>().enabled = false;
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
        facing.Value = playerMove.Facing;
        walking.Value = Mathf.Abs(rb.linearVelocity.x) > 0.1f;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void SetMoveHorizontalServerRpc(float horizontal)
    {
        MoveHorizontal = Mathf.Clamp(horizontal, -1f, 1f);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void RequestJumpServerRpc()
    {
        Jump = true;
    }

    public bool ConsumeJumpRequest()
    {
        if (!IsServer)
            return false;

        bool val = Jump;
        Jump = false;
        return val;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void RequestPunchServerRpc()
    {
        Punch = true;
    }

    public bool ConsumePunchRequest()
    {
        if (!IsServer)
            return false;
        bool val = Punch;
        Punch = false;
        return val;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void RequestBeastServerRpc()
    {
        Beast = true;
    }

    public bool ConsumeBeastRequest()
    {
        if (!IsServer)
            return false;
        bool val = Beast;
        Beast = false;
        return val;
    }
}
