using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : NetworkBehaviour
{
    private PlayerInput playerInput;
    private PlayerNetworkBridge bridge;
    private Rigidbody2D rb;
    private PlayerState playerState;

    [SerializeField]
    private int jumpsRemain;

    [SerializeField]
    private bool onFloor;

    [SerializeField]
    private bool onWall;

    [SerializeField]
    private Vector2 wallNormal;

    [SerializeField]
    private ContactPoint2D[] contacts = new ContactPoint2D[10];

    public int Facing { get; private set; }
    public bool OnFloor => onFloor;
    public bool OnWall => onWall;
    public Vector2 WallNormal => wallNormal;

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        bridge = GetComponent<PlayerNetworkBridge>();
        rb = GetComponent<Rigidbody2D>();
        playerState = GetComponent<PlayerState>();
        rb.gravityScale = GameGlobals.Instance.gravityScale;
        Facing = 1;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        jumpsRemain = GameGlobals.Instance.maxJumps;
    }

    private void Update()
    {
        if (!IsClient || !IsOwner || !IsSpawned || !playerInput.enabled)
            return;

        float horizontal = playerInput.actions["Move"].ReadValue<Vector2>().x;
        bool jump = playerInput.actions["Jump"].WasPressedThisFrame();

        if (horizontal != 0f)
        {
            Facing = (int)Mathf.Sign(horizontal);
        }

        bridge.SetMoveHorizontalServerRpc(horizontal);
        
        if (jump)
        {
            bridge.RequestJumpServerRpc();
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned)
            return;

        if (!IsOwner && bridge.MoveHorizontal != 0f)
        {
            Facing = (int)Mathf.Sign(bridge.MoveHorizontal);
        }

        if (!IsServer)
            return;

        if (playerState.Eliminated)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        CheckContacts();

        bool wantsJump = bridge.ConsumeJumpRequest();

        if (!playerState.IsConscious)
            return;

        float targetSpeed = bridge.MoveHorizontal * GameGlobals.Instance.moveSpeed;

        float newSpeed = Mathf.MoveTowards(
            rb.linearVelocity.x,
            targetSpeed,
            GameGlobals.Instance.acceleration * Time.fixedDeltaTime
        );

        float yVelocity = rb.linearVelocity.y;

        if (onWall && Mathf.Abs(bridge.MoveHorizontal) > 0f && yVelocity < -GameGlobals.Instance.wallSlidingSpeed)
        {
            yVelocity = -GameGlobals.Instance.wallSlidingSpeed;
        }

        rb.linearVelocity = new Vector2(newSpeed, yVelocity);

        if (wantsJump && jumpsRemain > 0)
        {
            float xVelocity = rb.linearVelocity.x;

            if (onWall)
            {
                xVelocity = wallNormal.x * GameGlobals.Instance.wallJumpForce;
            }

            rb.linearVelocity = new Vector2(xVelocity, 0f);
            rb.AddForce(Vector2.up * GameGlobals.Instance.jumpForce, ForceMode2D.Impulse);
            jumpsRemain--;
        }
    }

    private void CheckContact(Vector2 normal)
    {
        float dotVertical = Vector2.Dot(normal, Vector2.up);
        float dotHorizontal = Mathf.Abs(Vector2.Dot(normal, Vector2.right));

        bool hitGround = dotVertical > 0.5f;
        bool hitWall = dotHorizontal > 0.5f;

        if (hitWall)
        {
            wallNormal = normal;
        }

        onFloor |= hitGround;
        onWall |= hitWall;
    }

    private void CheckContacts()
    {
        bool alreadyHaveJumps = onFloor || onWall;

        onFloor = false;
        onWall = false;

        int contactCount = rb.GetContacts(contacts);

        for (int i = 0; i < contactCount; i++)
        {
            if (contacts[i].collider.gameObject.CompareTag("Level Geometry"))
            {
                CheckContact(contacts[i].normal);
            }
        }

        if (!alreadyHaveJumps && (onFloor || onWall))
        {
            jumpsRemain = GameGlobals.Instance.maxJumps;
        }
    }
}
