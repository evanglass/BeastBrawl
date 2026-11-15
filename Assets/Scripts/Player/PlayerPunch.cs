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

    private Collider2D[] hits = new Collider2D[10];

    private void Awake()
    {
        bridge = GetComponent<PlayerNetworkBridge>();
        playerInput = GetComponent<PlayerInput>();
        playerState = GetComponent<PlayerState>();
        playerMove = GetComponent<PlayerMove>();
        playerBeast = GetComponent<PlayerBeast>();
        rb = GetComponent<Rigidbody2D>();
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
        if (!playerBeast.AutoAttack)
            GetComponent<PlayerVisual>().PlayPunchRpc();
        else
            GetComponent<PlayerVisual>().PlayEffectRpc(CharacterSfxManager.Effect.Attack);

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(LayerMask.GetMask("Players"));
        filter.useTriggers = false;

        Collider2D activeArea = playerBeast.AttackArea != null ? playerBeast.AttackArea : punchArea;
        int hitCount = activeArea.Overlap(filter, hits);

        for (int i = 0; i < hitCount; i++)
        {
            GameObject other = hits[i].gameObject;
            if (!other.CompareTag("Player") || other == gameObject)
            {
                continue;
            }

            PlayerPunch otherPunch = other.GetComponent<PlayerPunch>();
            if (otherPunch != null)
            {
                bool wasBeast = playerBeast.IsBeast;
                otherPunch.ApplyPunch(bridge, playerBeast.GetPunchDamageMultiplier(), playerBeast.GetPunchKnockbackMultiplier());
                playerBeast.Attack(other);
                if (wasBeast && !playerBeast.IsBeast)
                    break;
            }
        }
    }

    private void ApplyPunch(PlayerNetworkBridge attacker, float punchDamageMultiplier, float knockbackMultiplier)
    {
        if (!IsSpawned || !IsServer)
            return;
        if (playerState.IsConscious && !playerState.Eliminated)
            GetComponent<PlayerVisual>().PlayEffectRpc(CharacterSfxManager.Effect.Hit);
        playerState.Punch(GameGlobals.Instance.punchDamage * punchDamageMultiplier);

        Vector2 knockback = new Vector2(
            GameGlobals.Instance.punchKnockback.x * knockbackMultiplier,
            GameGlobals.Instance.punchKnockback.y * knockbackMultiplier
        );

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
