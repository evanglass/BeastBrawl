
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class MovingPlatform : MonoBehaviour
{
    // first label all values that will be able to change in the unity inspector
    [Header("Movement Settings")]
    [SerializeField] private bool horizontalMovement = false;
    [SerializeField] private bool verticalMovement = true;

    [SerializeField] private float travelDistance = 3f;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float pauseDuration = 1f;

    [Header("Optional Route")]
    [SerializeField] private Vector2 directionOverride;
    [SerializeField] private bool resetAfterAscent;
    [SerializeField, Min(0f)] private float cycleInterval;
    [SerializeField] private bool carryRiders;
    public bool CarryRiders => carryRiders;
    public bool LeavesArena => resetAfterAscent;
    public Vector2 RiderVelocity { get; private set; }

    [Header("Rotation Settings")]
    [SerializeField] private bool rotatePlatform = false;
    [SerializeField] private bool clockwise = true;
    [SerializeField] private float rotationSpeed = 90f;

    private Rigidbody2D rb;

    private Vector2 startPosition;
    private Vector2 targetPosition;

    private float pauseTimer = 0f;
    private bool movingForward = true;
    private static readonly Dictionary<string, MovingPlatform> platforms = new();
    private string networkKey;
    private MatchManager matchManager;
    private float syncTimer;
    private Vector2 networkPosition;
    private float networkRotation;
    private bool receivedPosition;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        startPosition = rb.position;
        networkKey = gameObject.scene.name;
        for (Transform current = transform; current != null; current = current.parent)
            networkKey += "/" + current.name;
        networkKey += "/" + System.BitConverter.SingleToInt32Bits(startPosition.x) + "/" + System.BitConverter.SingleToInt32Bits(startPosition.y);
        platforms[networkKey] = this;
        matchManager = FindFirstObjectByType<MatchManager>();

        targetPosition = EndPosition();
        if (resetAfterAscent) pauseTimer = cycleInterval;
    }

    private Vector2 EndPosition()
    {
        Vector2 direction = directionOverride.sqrMagnitude > 0f ? directionOverride.normalized :
            verticalMovement ? Vector2.up : horizontalMovement ? Vector2.right : Vector2.zero;
        return startPosition + direction * travelDistance;
    }

    void FixedUpdate()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && !NetworkManager.Singleton.IsServer)
        {
            if (receivedPosition)
            {
                rb.MovePosition(Vector2.Lerp(rb.position, networkPosition, Mathf.Clamp01(Time.fixedDeltaTime * 10f)));
                rb.MoveRotation(Mathf.LerpAngle(rb.rotation, networkRotation, Mathf.Clamp01(Time.fixedDeltaTime * 10f)));
            }
            return;
        }

        if (matchManager != null && matchManager.IsSpawned && matchManager.IsServer && (horizontalMovement || verticalMovement || rotatePlatform))
        {
            syncTimer -= Time.fixedDeltaTime;
            if (syncTimer <= 0f)
            {
                matchManager.SyncPlatformRpc(networkKey, rb.position, rb.rotation);
                syncTimer = 0.1f;
            }
        }

        RiderVelocity = Vector2.zero;
        // The cloud resets only after it has cleared the ring-out ceiling.
        if (resetAfterAscent && !movingForward)
        {
            rb.position = startPosition;
            targetPosition = EndPosition();
            movingForward = true;
            pauseTimer = cycleInterval;
            return;
        }

        // MOVEMENT
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
        }
        else if (horizontalMovement || verticalMovement)
        {
            Vector2 newPosition = Vector2.MoveTowards(
                rb.position,
                targetPosition,
                moveSpeed * Time.fixedDeltaTime
            );

            RiderVelocity = (newPosition - rb.position) / Time.fixedDeltaTime;
            rb.MovePosition(newPosition);

            if (Vector2.Distance(newPosition, targetPosition) < 0.001f)
            {
                movingForward = !movingForward;

                targetPosition = movingForward ? EndPosition() : startPosition;

                pauseTimer = pauseDuration;
            }
        }

        // ROTATION
        if (rotatePlatform)
        {
            float direction = clockwise ? -1f : 1f;

            float rotationAmount =
                rotationSpeed * direction * Time.fixedDeltaTime;

            rb.MoveRotation(rb.rotation + rotationAmount);
        }
    }

    public static void ApplyNetworkPosition(string key, Vector2 position, float rotation)
    {
        if (!platforms.TryGetValue(key, out MovingPlatform platform))
            return;

        // Skip interpolation across the offscreen cloud reset.
        if (!platform.receivedPosition ||
            (platform.resetAfterAscent && (platform.rb.position - position).sqrMagnitude > 100f))
        {
            platform.rb.position = position;
            platform.rb.rotation = rotation;
        }
        platform.networkPosition = position;
        platform.networkRotation = rotation;
        platform.receivedPosition = true;
    }

    private void OnDestroy()
    {
        if (networkKey != null && platforms.TryGetValue(networkKey, out MovingPlatform platform) && platform == this)
            platforms.Remove(networkKey);
    }
}
