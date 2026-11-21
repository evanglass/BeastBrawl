using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerPunch : NetworkBehaviour
{
    [SerializeField]
    private Collider2D punchArea;

    [SerializeField]
    private Transform punchPivot;

    [SerializeField]
    private float cooldown = 0f;

    private PlayerNetworkBridge bridge;
    private PlayerInput playerInput;
    private PlayerState playerState;
    private PlayerMove playerMove;
    private PlayerBeast playerBeast;
    private Rigidbody2D rb;

    private readonly NetworkVariable<uint> confirmedBotSwings = new(0,
        NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
    internal uint ConfirmedBotSwings => confirmedBotSwings.Value;

    internal bool TryGetBotAttackBounds(int facing, float reachScale, out Bounds bounds)
    {
        var beastArea = playerBeast.AttackArea;
        var area = beastArea != null ? beastArea : punchArea;
        bounds = default;
        if (area == null || !area.isActiveAndEnabled || punchPivot == null) return false;

        bounds = area.bounds;
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        float pivotX = punchPivot.position.x;
        if (beastArea == null && area is BoxCollider2D)
        {
            // The server sizes the normal attack box on spawn; use the same width on bot workers.
            size.x = GameGlobals.Instance.punchReach * Mathf.Abs(area.transform.lossyScale.x);
            center.x = pivotX + facing * size.x * 0.5f;
        }
        else if (area.transform.IsChildOf(punchPivot) &&
            facing != (punchPivot.localScale.x < 0f ? -1 : 1))
        {
            center.x = pivotX * 2f - center.x;
        }

        center.x = pivotX + (center.x - pivotX) * reachScale;
        size.x *= reachScale;
        bounds = new Bounds(center, size);
        return true;
    }

    private Collider2D[] hits = new Collider2D[10];

    public void ResetForRound()
    {
        cooldown = 0f;
    }

    private void Awake()
    {
        bridge = GetComponent<PlayerNetworkBridge>();
        playerInput = GetComponent<PlayerInput>();
        playerState = GetComponent<PlayerState>();
        playerMove = GetComponent<PlayerMove>();
        playerBeast = GetComponent<PlayerBeast>();
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer || punchArea is not BoxCollider2D box)
            return;

        float reach = Mathf.Max(0.01f, GameGlobals.Instance.punchReach);
        box.size = new Vector2(reach, box.size.y);
        box.offset = new Vector2(0f, box.offset.y);
        Vector3 position = box.transform.localPosition;
        position.x = reach * 0.5f;
        box.transform.localPosition = position;
    }

    private void Update()
    {
        if (!IsClient || !IsOwner || !IsSpawned || !playerInput.enabled)
            return;

        bool punch = playerInput.actions["Punch"].WasPressedThisFrame();

        if (punch)
        {
            bridge.RequestPunchServerRpc();
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned)
            return;

        punchPivot.localScale = new Vector3(bridge.Facing, 1f, 1f);
        
        if (!IsServer)
            return;

        bool wantsPunch = bridge.ConsumePunchRequest();

        if (!bridge.GameplayAllowed)
            return;

        if (!playerState.IsConscious && !playerState.Eliminated)
        {
            if (wantsPunch)
                playerState.GainConsciousness(Mathf.Max(0f, GameGlobals.Instance.consciousnessRecoveryPerPress));
            return;
        }

        if (playerBeast.HandlesAttack)
        {
            if (wantsPunch && playerState.IsConscious && !playerState.Eliminated)
                playerBeast.RequestBeastAttack();
            return;
        }

        if (cooldown > 0f)
        {
            cooldown -= Time.fixedDeltaTime;
            return;
        }

        if (!wantsPunch && !playerBeast.AutoAttack)
            return;

        if (!playerState.IsConscious || playerState.Eliminated)
            return;

        cooldown = GameGlobals.Instance.punchCooldown * playerBeast.GetPunchCooldownMultiplier();
        var visual = GetComponent<PlayerVisual>();
        var original = PlayerVisual.OriginalCharacter(visual.PlayerCharacter);
        bool cougarBeast = playerBeast.IsBeast && original == PlayerVisual.Character.Cougar;
        bool bearBeast = playerBeast.IsBeast && original == PlayerVisual.Character.Bear;
        if (!playerBeast.AutoAttack)
        {
            if (cougarBeast || bearBeast) visual.PlayBeastPunchRpc();
            else visual.PlayPunchRpc();
        }
        else if (!cougarBeast)
            visual.PlayEffectRpc(CharacterSfxManager.Effect.Attack);
        if (bearBeast)
            visual.PlayEffectRpc(CharacterSfxManager.Effect.BeastAttack);

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(LayerMask.GetMask("Players"));
        filter.useTriggers = false;

        Collider2D activeArea = playerBeast.AttackArea != null ? playerBeast.AttackArea : punchArea;
        Physics2D.SyncTransforms();
        int hitCount = activeArea.Overlap(filter, hits);

        bool landed = false;
        bool trackSwing = bridge.IsBotControlled && !playerBeast.IsBeast;
        for (int i = 0; i < hitCount; i++)
        {
            GameObject other = hits[i].gameObject;
            if (!other.CompareTag("Player") || other == gameObject)
            {
                continue;
            }

            PlayerPunch otherPunch = other.GetComponent<PlayerPunch>();
            if (otherPunch != null &&
                bridge.CanAffect(otherPunch.bridge, playerBeast.AllowsFriendlyFire))
            {
                landed |= !otherPunch.playerState.Eliminated;
                bool wasBeast = playerBeast.IsBeast;
                otherPunch.ApplyPunch(bridge, playerBeast.GetPunchDamageMultiplier(), playerBeast.GetPunchKnockbackMultiplier());
                playerBeast.Attack(other);
                if (wasBeast && !playerBeast.IsBeast)
                    break;
            }
        }
        if (trackSwing && landed) confirmedBotSwings.Value++;
    }

    private void ApplyPunch(PlayerNetworkBridge attacker, float punchDamageMultiplier, float knockbackMultiplier)
    {
        if (!IsSpawned || !IsServer)
            return;

        bool wasConscious = playerState.IsConscious;
        if (playerState.IsConscious && !playerState.Eliminated)
            GetComponent<PlayerVisual>().PlayEffectRpc(CharacterSfxManager.Effect.Hit);
        playerState.Punch(GameGlobals.Instance.punchDamage * punchDamageMultiplier);

        Vector2 knockback = new Vector2(
            GameGlobals.Instance.punchKnockback.x * knockbackMultiplier,
            GameGlobals.Instance.punchKnockback.y * knockbackMultiplier
        );

        if (!wasConscious)
            knockback = Vector2.Scale(knockback, GameGlobals.Instance.unconsciousPunchKnockbackMultiplier);

        if (playerMove.OnWall)
        {
            rb.AddForce(Vector2.up * knockback.y, ForceMode2D.Impulse);
            rb.AddForce(Vector2.right * knockback.x * playerMove.WallNormal.x, ForceMode2D.Impulse);
        } else
        {
            rb.AddForce(Vector2.up * knockback.y, ForceMode2D.Impulse);
            rb.AddForce(Vector2.right * knockback.x * attacker.Facing, ForceMode2D.Impulse);
        }
    }
}
