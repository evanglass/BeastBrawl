
using UnityEngine;

public class MovingPlatform : MonoBehaviour
{
    // first label all values that will be able to change in the unity inspector
    [Header("Movement Settings")]
    [SerializeField] private bool horizontalMovement = false;
    [SerializeField] private bool verticalMovement = true;

    [SerializeField] private float travelDistance = 3f;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float pauseDuration = 1f;

    [Header("Rotation Settings")]
    [SerializeField] private bool rotatePlatform = false;
    [SerializeField] private bool clockwise = true;
    [SerializeField] private float rotationSpeed = 90f;

    private Rigidbody2D rb;

    private Vector2 startPosition;
    private Vector2 targetPosition;

    private float pauseTimer = 0f;
    private bool movingForward = true;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        startPosition = rb.position;

        if (verticalMovement)
        {
            targetPosition = startPosition + Vector2.up * travelDistance;
        }
        else if (horizontalMovement)
        {
            targetPosition = startPosition + Vector2.right * travelDistance;
        }
        else
        {
            targetPosition = startPosition;
        }
    }

    void FixedUpdate()
    {
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

            rb.MovePosition(newPosition);

            if (Vector2.Distance(newPosition, targetPosition) < 0.001f)
            {
                movingForward = !movingForward;

                if (movingForward)
                {
                    targetPosition = startPosition;
                }
                else
                {
                    if (verticalMovement)
                    {
                        targetPosition = startPosition + Vector2.up * travelDistance;
                    }
                    else
                    {
                        targetPosition = startPosition + Vector2.right * travelDistance;
                    }
                }

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
}
