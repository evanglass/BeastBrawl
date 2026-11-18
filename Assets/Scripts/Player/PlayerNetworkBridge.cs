using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNetworkBridge : NetworkBehaviour
{
    private PlayerState playerState;
    private PlayerMove playerMove;
    private PlayerBeast playerBeast;
    private Rigidbody2D rb;
    private MatchManager matchManager;
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

    [SerializeField]
    private NetworkVariable<float> beastMeter = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField]
    private NetworkVariable<bool> isBeast = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<float> snakeBiteDelay = new(
        1.5f,
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
    public float BeastMeter => beastMeter.Value;
    public bool IsBeast => isBeast.Value;
    public float SnakeBiteDelay => snakeBiteDelay.Value;
    public bool GameplayAllowed => matchManager == null ||
        (matchManager.IsSpawned && matchManager.CurrentPhase == MatchManager.MatchPhase.Fighting);

    private void Awake()
    {
        playerState = GetComponent<PlayerState>();
        playerMove = GetComponent<PlayerMove>();
        playerBeast = GetComponent<PlayerBeast>();
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        matchManager = FindFirstObjectByType<MatchManager>();
        GetComponent<PlayerActor>().ServerSimulated = IsServer;
        GetComponent<PlayerInput>().enabled = IsClient && IsOwner && NetworkObject.IsPlayerObject;

        if (IsServer)
        {
            snakeBiteDelay.Value = GameGlobals.Instance.snakeBiteDelay;
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

        if (!GameplayAllowed)
            MoveHorizontal = 0f;

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
        beastMeter.Value = playerState.BeastMeter;
        isBeast.Value = playerBeast.IsBeast;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void SetMoveHorizontalServerRpc(float horizontal)
    {
        if (!GameplayAllowed)
            return;

        MoveHorizontal = Mathf.Clamp(horizontal, -1f, 1f);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void RequestJumpServerRpc()
    {
        if (!GameplayAllowed)
            return;

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
        if (!GameplayAllowed)
            return;

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
        if (!GameplayAllowed)
            return;

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
