using System.Collections.Generic;
using UnityEngine;

public class EagleBeast : Beast
{
    [SerializeField]
    private float cornerSpeed = 15f;

    [SerializeField]
    private float aimSpeed = 90f;

    [SerializeField]
    private float boomRange = 16f;

    [SerializeField]
    private float boomHalfAngle = 30f;

    [SerializeField]
    private float boomKnockback = 30f;

    [SerializeField]
    private float flightSpeed = 10f;

    [SerializeField]
    private float flightDuration = 2f;

    private Rigidbody2D rb;
    private PlayerState playerState;
    private PlayerBeast playerBeast;
    private PlayerMove playerMove;
    private PlayerNetworkBridge bridge;
    private EagleVisual visual;
    private Vector2 corner;
    private int boomDirection;
    private float originalGravity;
    private float aimAngle;
    private float aimTimer;
    private float cornerTravelTimer;
    private float flightTimer;
    private EagleVisual.Phase phase;

    public override bool ControlsMovement => true;
    public override bool HandlesAttack => true;
    public override bool UsesBeastTimer => false;
    public float BoomRange => boomRange;
    public float BoomHalfAngle => boomHalfAngle;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerState = GetComponent<PlayerState>();
        playerBeast = GetComponent<PlayerBeast>();
        playerMove = GetComponent<PlayerMove>();
        bridge = GetComponent<PlayerNetworkBridge>();
        visual = GetComponent<EagleVisual>();
    }

    public override void OnBeastStart(GameObject player)
    {
        RoundManager round = FindFirstObjectByType<RoundManager>();
        Transform target = round == null ? null :
            (playerMove.Facing < 0 ? round.eagleLeftCorner : round.eagleRightCorner);

        if (visual == null)
        {
            Debug.LogWarning("Eagle beast needs EagleVisual.", this);
            playerBeast.EndBeast();
            return;
        }

        if (target != null) corner = target.position;
        else
        {
            Camera camera = Camera.main;
            if (camera != null)
            {
                var point = camera.ViewportToWorldPoint(new Vector3(playerMove.Facing < 0 ? .12f : .88f, .85f,
                    Mathf.Abs(camera.transform.position.z - transform.position.z)));
                corner = new Vector2(point.x, point.y);
            }
            else corner = rb.position + new Vector2(playerMove.Facing < 0 ? -5f : 5f, 5f);
        }
        boomDirection = -playerMove.Facing;
        aimAngle = playerMove.Facing < 0 ? -45f : -135f;
        originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
        aimTimer = GameGlobals.Instance.beastDuration;
        cornerTravelTimer = Mathf.Max(3f, Vector2.Distance(rb.position, corner) / Mathf.Max(1f, cornerSpeed) * 2f + 1f);
        phase = EagleVisual.Phase.FlyingToCorner;
        visual.SetAim(phase, aimAngle);
    }

    public override void TickBeast(float deltaTime)
    {
        if (playerState.Eliminated || !playerState.IsConscious)
        {
            playerBeast.EndBeast();
            return;
        }

        if (phase == EagleVisual.Phase.Aiming)
        {
            aimTimer -= deltaTime;
            if (aimTimer <= 0f)
            {
                StartFreeFlight();
                return;
            }
        }

        if (phase == EagleVisual.Phase.FlyingToCorner)
        {
            cornerTravelTimer -= deltaTime;
            bool isFarAway = Mathf.Abs(rb.position.y - corner.y) > 0.05f;
            Vector2 destination = isFarAway ? new Vector2(rb.position.x, corner.y) : corner;

            Vector2 next = Vector2.MoveTowards(rb.position, destination, cornerSpeed * deltaTime);
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(next);

            if (Vector2.Distance(rb.position, corner) < 0.05f || cornerTravelTimer <= 0f)
            {
                phase = EagleVisual.Phase.Aiming;
                visual.SetAim(phase, aimAngle);
            }
        }
        else if (phase == EagleVisual.Phase.Aiming)
        {
            rb.linearVelocity = Vector2.zero;
            aimAngle = Mathf.Clamp(aimAngle + bridge.MoveHorizontal * aimSpeed * deltaTime, -150f, -30f);
            visual.SetAim(phase, aimAngle);
        }
        else if (phase == EagleVisual.Phase.FreeFlight)
        {
            rb.linearVelocity = new Vector2(bridge.MoveHorizontal * flightSpeed, 0f);
            
            flightTimer -= deltaTime;
            if (flightTimer <= 0f)
                playerBeast.EndBeast();
        }
    }

    public override void RequestAttack()
    {
        if (phase != EagleVisual.Phase.Aiming)
            return;

        Vector2 direction = Direction(aimAngle);
        float minDot = Mathf.Cos(boomHalfAngle * Mathf.Deg2Rad);
        HashSet<Rigidbody2D> hitBodies = new HashSet<Rigidbody2D>();

        Collider2D[] hits = Physics2D.OverlapCircleAll(rb.position, boomRange, LayerMask.GetMask("Players"));

        foreach (Collider2D hit in hits)
        {
            Rigidbody2D target = hit.attachedRigidbody;
            if (target == null || target == rb || hit.isTrigger || !hitBodies.Add(target))
                continue;

            PlayerState targetState = target.GetComponent<PlayerState>();
            Vector2 offset = target.position - rb.position;
            if (targetState == null || targetState.Eliminated || offset.sqrMagnitude == 0f)
                continue;

            if (Vector2.Dot(direction, offset.normalized) >= minDot)
                target.AddForce(Vector2.right * boomDirection * boomKnockback, ForceMode2D.Impulse);
        }

        visual.PlayBoomRpc(rb.position, aimAngle);
        StartFreeFlight();
    }

    private void StartFreeFlight()
    {
        phase = EagleVisual.Phase.FreeFlight;
        flightTimer = flightDuration;
        visual.SetAim(phase, aimAngle);
    }

    public override void OnBeastEnd(GameObject player)
    {
        if (phase != EagleVisual.Phase.Inactive)
        {
            rb.gravityScale = originalGravity;
        }

        phase = EagleVisual.Phase.Inactive;
        if (visual != null)
            visual.SetAim(phase, aimAngle);
    }

    private static Vector2 Direction(float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }
}
