using UnityEngine;

public class GameGlobals : MonoBehaviour
{
    [Header("Consciousness")]
    public float consciousnessRecoveryRate = 5f;
    public float consciousnessLossMultiplier = 2f;

    [Header("Movement")]
    public float moveSpeed = 7f;
    public float acceleration = 60f;
    public float jumpForce = 10f;
    public float gravityScale = 5f;
    public float wallSlidingSpeed = 0.5f;
    public float wallJumpForce = 5f;
    public int maxJumps = 2;

    [Header("Damage")]
    public float punchDamage = 20f;
    public float punchCooldown = 1f;
    public Vector2 punchKnockback = new Vector2(20, 40);

    public static GameGlobals Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
}
