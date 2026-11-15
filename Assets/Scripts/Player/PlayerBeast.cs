using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBeast : NetworkBehaviour
{
    private PlayerNetworkBridge bridge;
    private PlayerInput playerInput;
    private PlayerState playerState;

    private Beast currentBeast;
    
    private float beastTimer = 0f;

    [SerializeField]
    private bool isBeast = false;
    public bool IsBeast => isBeast;
    public bool AutoAttack => isBeast && currentBeast != null && currentBeast.AutoAttack;
    public Collider2D AttackArea => isBeast && currentBeast != null ? currentBeast.AttackArea : null;
    public bool ControlsMovement => isBeast && currentBeast != null && currentBeast.ControlsMovement;
    public bool HandlesAttack => isBeast && currentBeast != null && currentBeast.HandlesAttack;

    private void Awake()
    {
        bridge = GetComponent<PlayerNetworkBridge>();
        playerInput = GetComponent<PlayerInput>();
        playerState = GetComponent<PlayerState>();
    }

    private void Update()
    {
        if (!IsClient || !IsOwner || !IsSpawned || !playerInput.enabled)
            return;

        bool beast = playerInput.actions["Beast"].WasPressedThisFrame();

        if (beast)
        {
            bridge.RequestBeastServerRpc();
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
            return;

        bool wantsBeast = bridge.ConsumeBeastRequest();

        if (isBeast)
        {
            currentBeast.TickBeast(Time.fixedDeltaTime);
            if (!isBeast || !currentBeast.UsesBeastTimer)
                return;

            beastTimer -= Time.fixedDeltaTime;

            if (beastTimer <= 0f)
                EndBeast();

            return;
        }

        if (!wantsBeast)
            return;

        if (!playerState.IsConscious || playerState.Eliminated)
            return;

        if (playerState.BeastMeter == 100f && currentBeast != null)
        {
            isBeast = true;
            playerState.UnleashBeast();
            currentBeast.OnBeastStart(gameObject);
            beastTimer = GameGlobals.Instance.beastDuration;
            if (isBeast)
                GetComponent<PlayerVisual>().PlayEffectRpc(CharacterSfxManager.Effect.BeastActivation);
        }
    }

    public float GetMoveSpeedMultiplier()
    {
        if (!isBeast)
            return 1f;
        return currentBeast.MoveSpeedMultiplier;
    }

    public float GetPunchCooldownMultiplier()
    {
        if (!isBeast)
            return 1f;
        return currentBeast.PunchCooldownMultiplier;
    }

    public float GetPunchDamageMultiplier()
    {
        if (!isBeast)
            return 1f;
        return currentBeast.PunchDamageMultiplier;
    }

    public float GetPunchKnockbackMultiplier()
    {
        if (!isBeast)
            return 1f;
        return currentBeast.PunchKnockbackMultiplier;
    }

    public void Attack(GameObject target)
    {
        if (!IsServer || !isBeast)
        {
            return;
        }
        currentBeast.OnPunchHit(target);
    }

    public void RequestBeastAttack()
    {
        if (IsServer && HandlesAttack)
            currentBeast.RequestAttack();
    }

    public void EndBeast()
    {
        if (!IsServer || !isBeast)
        {
            return;
        }
        isBeast = false;
        currentBeast.OnBeastEnd(gameObject);
        playerState.LeashBeast();
    }

    public void Reset(PlayerVisual.Character character)
    {
        EndBeast();
        isBeast = false;
        beastTimer = 0f;
        currentBeast = character switch
        {
            PlayerVisual.Character.Eagle => GetComponent<EagleBeast>(),
            PlayerVisual.Character.Bear => GetComponent<BearBeast>(),
            PlayerVisual.Character.Cougar => GetComponent<CougarBeast>(),
            PlayerVisual.Character.Snake => GetComponent<SnakeBeast>(),
            _ => null
        };
    }
}
