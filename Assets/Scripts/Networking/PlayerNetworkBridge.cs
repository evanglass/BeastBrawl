using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerNetworkBridge : NetworkBehaviour
{
    public static readonly List<PlayerNetworkBridge> All = new();

    private PlayerState playerState;
    private PlayerMove playerMove;
    private PlayerBeast playerBeast;
    private Rigidbody2D rb;
    private MatchManager matchManager;
    private RoundManager roundManager;
    private NetworkVariable<bool> walking = new(false);

    [SerializeField, Min(0f)] private float stageRecoveryInset = 1f;
    [SerializeField, Min(0f)] private float stageRecoveryHeightMargin = 0.5f;
    private static readonly RaycastHit2D[] groundHits = new RaycastHit2D[128];
    private static readonly ContactFilter2D groundFilter = new() { useTriggers = false };

    private readonly NetworkVariable<bool> onFloor = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> onWall = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> jumpsRemain = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> hasGroundBelow = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> targetStageX = new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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
    public bool OnFloor => onFloor.Value;
    public bool OnWall => onWall.Value;
    public int JumpsRemain => jumpsRemain.Value;
    public bool HasGroundBelow => hasGroundBelow.Value;
    public float TargetStageX => targetStageX.Value;
    public float SnakeBiteDelay => snakeBiteDelay.Value;
    public bool GameplayAllowed => matchManager == null ||
        (matchManager.IsSpawned && matchManager.CurrentPhase == MatchManager.MatchPhase.Fighting);

    private readonly NetworkVariable<bool> botControlled = new(false);
    private BotInputDriver botDriver;
    internal bool IsBotControlled => botControlled.Value;

    private readonly NetworkVariable<int> teamId = new(MatchRules.NoTeam);
    public int TeamId => teamId.Value;

    internal void SetTeam(int team)
    {
        if (IsServer) teamId.Value = team;
    }

    internal bool IsOpponent(PlayerNetworkBridge other) =>
        other != null && other != this && MatchRules.AreOpposingTeams(TeamId, other.TeamId);

    internal bool CanAffect(PlayerNetworkBridge other, bool allowFriendlyFire = false) =>
        other != null && other != this && (allowFriendlyFire || IsOpponent(other));

    internal bool CanDriveBotInput => IsOwner;

    internal void MarkAsBot() => botControlled.Value = true;

    internal void DriveBotInput(BotInputFrame input)
    {
        if (!CanDriveBotInput) return;
        if (IsServer) ApplyBotInput(input);
        else SubmitBotInputRpc(input);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    internal void SubmitBotInputRpc(BotInputFrame input) => ApplyBotInput(input);

    private void ApplyBotInput(BotInputFrame input)
    {
        if (!botControlled.Value || !GameplayAllowed || !float.IsFinite(input.Horizontal)) return;
        SetMoveHorizontal(input.Horizontal);
        Jump |= input.Jump;
        Punch |= input.Punch;
        Beast |= input.Beast;
    }

    private void SetMoveHorizontal(float horizontal) => MoveHorizontal = Mathf.Clamp(horizontal, -1f, 1f);

    private void Awake()
    {
        playerState = GetComponent<PlayerState>();
        playerMove = GetComponent<PlayerMove>();
        playerBeast = GetComponent<PlayerBeast>();
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        matchManager = FindFirstObjectByType<MatchManager>();
        roundManager = matchManager != null ? matchManager.GetComponent<RoundManager>() : null;
        GetComponent<PlayerActor>().ServerSimulated = IsServer;
        GetComponent<PlayerInput>().enabled = IsClient && IsOwner && NetworkObject.IsPlayerObject;
        botControlled.OnValueChanged += OnBotControlChanged;
        OnBotControlChanged(false, botControlled.Value);

        if (IsServer)
        {
            RefreshCharacterSettings();
            Publish();
        }
    }

    internal void RefreshCharacterSettings()
    {
        if (IsServer)
            snakeBiteDelay.Value = GameGlobals.Instance.SnakeBiteDelayFor(GetComponent<PlayerVisual>().PlayerCharacter);
    }

    private void OnBotControlChanged(bool previous, bool current)
    {
        if (current && CanDriveBotInput && botDriver == null)
            botDriver = gameObject.AddComponent<BotInputDriver>();
    }

    public override void OnGainedOwnership()
    {
        OnBotControlChanged(false, botControlled.Value);
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        botControlled.OnValueChanged -= OnBotControlChanged;
        if (botDriver != null) Destroy(botDriver);
        botDriver = null;
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
        onFloor.Value = playerMove.OnFloor;
        onWall.Value = playerMove.OnWall;
        jumpsRemain.Value = playerMove.JumpsRemain;
        PublishRecoveryObservations();
    }

    internal RoundManager BotArena => roundManager;
    internal float BotJumpSpeed => GameGlobals.Instance.jumpForce / rb.mass;
    internal float BotGravity => Mathf.Abs(Physics2D.gravity.y) * rb.gravityScale;
    internal float BotReachableRise => stageRecoveryHeightMargin + (JumpsRemain > 0 && BotJumpSpeed > 0f
        ? (BotGravity > 0f ? JumpsRemain * BotJumpSpeed * BotJumpSpeed / (2f * BotGravity) : float.PositiveInfinity)
        : 0f);
    internal Beast ActiveBotBeast => IsBeast
        ? playerBeast.GetBeastForCharacter(GetComponent<PlayerVisual>().PlayerCharacter)
        : null;

    internal float ClampRecoveryX(Bounds bounds, float x)
    {
        float inset = Mathf.Min(stageRecoveryInset, bounds.extents.x);
        return Mathf.Clamp(x, bounds.min.x + inset, bounds.max.x - inset);
    }

    private void PublishRecoveryObservations()
    {
        Vector2 position = transform.position;
        float distance = Mathf.Max(0f, position.y - GameGlobals.Instance.boundaryY);
        if (roundManager == null || !roundManager.HasRecoveryGeometry)
        {
            hasGroundBelow.Value = false;
            return;
        }

        if (distance > 0f)
        {
            int hitCount = Physics2D.Raycast(position, Vector2.down, groundFilter, groundHits, distance);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D collider = groundHits[i].collider;
                if (!roundManager.IsRecoveryGeometry(collider))
                    continue;

                hasGroundBelow.Value = true;
                return;
            }
        }

        hasGroundBelow.Value = false;
        float reachableTop = position.y + BotReachableRise;
        float nearestDistance = float.PositiveInfinity;
        float targetX = targetStageX.Value;
        float lowestTop = float.PositiveInfinity;
        float lowestDistance = float.PositiveInfinity;
        float lowestTargetX = targetX;
        var geometry = roundManager.GetRecoveryGeometry();
        while (geometry.MoveNext())
        {
            Collider2D collider = geometry.Current;
            if (collider == null || !collider.isActiveAndEnabled)
                continue;

            Bounds bounds = collider.bounds;
            float distanceX = Mathf.Abs(position.x - Mathf.Clamp(position.x, bounds.min.x, bounds.max.x));
            float platformTargetX = ClampRecoveryX(bounds, position.x);
            float top = bounds.max.y;
            if (top < lowestTop || (top == lowestTop && distanceX < lowestDistance))
            {
                lowestTop = top;
                lowestDistance = distanceX;
                lowestTargetX = platformTargetX;
            }
            if (top <= reachableTop && distanceX < nearestDistance)
            {
                nearestDistance = distanceX;
                targetX = platformTargetX;
            }
        }
        targetStageX.Value = float.IsPositiveInfinity(nearestDistance) ? lowestTargetX : targetX;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void SetMoveHorizontalServerRpc(float horizontal)
    {
        if (!GameplayAllowed || botControlled.Value)
            return;

        SetMoveHorizontal(horizontal);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void RequestJumpServerRpc()
    {
        if (!GameplayAllowed || botControlled.Value)
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
        if (!GameplayAllowed || botControlled.Value)
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
        if (!GameplayAllowed || botControlled.Value)
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
