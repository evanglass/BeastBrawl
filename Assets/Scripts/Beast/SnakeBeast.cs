using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class SnakeBeast : Beast
{
    [SerializeField]
    private float biteDelay = 1.5f;

    private PlayerState targetState;
    private Rigidbody2D rb;
    private PlayerState playerState;
    private PlayerBeast playerBeast;
    private float originalGravity;
    private float biteTimer;
    private bool attached;

    public override bool ControlsMovement => true;
    public override bool HandlesAttack => true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerState = GetComponent<PlayerState>();
        playerBeast = GetComponent<PlayerBeast>();
    }

    public override void OnBeastStart(GameObject player)
    {
        targetState = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (PlayerState candidate in FindObjectsByType<PlayerState>(FindObjectsSortMode.None))
        {
            NetworkObject networkObject = candidate.GetComponent<NetworkObject>();

            if (candidate == playerState || candidate.Eliminated)
                continue;

            if (networkObject == null || !networkObject.IsSpawned)
                continue;

            float distance = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                targetState = candidate;
            }
        }

        if (targetState == null)
        {
            playerBeast.EndBeast();
            return;
        }

        originalGravity = rb.gravityScale;
        attached = true;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
        GetComponent<NetworkTransform>().Teleport(targetState.transform.position, transform.rotation, transform.localScale);
        biteTimer = biteDelay;
    }

    public override void TickBeast(float deltaTime)
    {
        if (playerState.Eliminated || !playerState.IsConscious || targetState == null || targetState.Eliminated || !targetState.GetComponent<NetworkObject>().IsSpawned)
        {
            playerBeast.EndBeast();
            return;
        }

        rb.linearVelocity = Vector2.zero;
        rb.MovePosition(targetState.transform.position);
        biteTimer -= deltaTime;
        if (biteTimer > 0f)
            return;

        targetState.KnockOut();
        playerBeast.EndBeast();
    }

    public override void OnBeastEnd(GameObject player)
    {
        if (attached)
            rb.gravityScale = originalGravity;

        attached = false;
        targetState = null;
    }
}
