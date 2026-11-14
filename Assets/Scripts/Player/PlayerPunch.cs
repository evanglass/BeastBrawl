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
    private Rigidbody2D rb;

    private Collider2D[] hits = new Collider2D[10];

    private void Awake()
    {
        bridge = GetComponent<PlayerNetworkBridge>();
        playerInput = GetComponent<PlayerInput>();
        playerState = GetComponent<PlayerState>();
        playerMove = GetComponent<PlayerMove>();
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

        if (cooldown > 0f)
        {
            cooldown -= Time.fixedDeltaTime;
            return;
        }

        if (!wantsPunch)
            return;

        if (!playerState.IsConscious || playerState.Eliminated)
            return;

        cooldown = GameGlobals.Instance.punchCooldown;

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(LayerMask.GetMask("Players"));
        filter.useTriggers = false;

        int hitCount = punchArea.Overlap(filter, hits);

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
                otherPunch.ApplyPunch(bridge);
            }
        }
    }

    private void ApplyPunch(PlayerNetworkBridge attacker)
    {
        if (!IsSpawned || !IsServer)
            return;
        playerState.Punch(GameGlobals.Instance.punchDamage);
        if (playerMove.OnWall)
        {
            rb.AddForce(Vector2.up * GameGlobals.Instance.punchKnockback.y, ForceMode2D.Impulse);
            rb.AddForce(Vector2.right * GameGlobals.Instance.punchKnockback.x * playerMove.WallNormal.x, ForceMode2D.Impulse);
        } else
        {
            rb.AddForce(Vector2.up * GameGlobals.Instance.punchKnockback.y, ForceMode2D.Impulse);
            rb.AddForce(Vector2.right * GameGlobals.Instance.punchKnockback.x * attacker.Facing, ForceMode2D.Impulse);
        }
    }
}
