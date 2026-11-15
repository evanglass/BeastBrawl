using Unity.Netcode;
using UnityEngine;

public class PlayerVisual : NetworkBehaviour
{
    private PlayerNetworkBridge bridge;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private Rigidbody2D rb;

    public enum Character
    {
        None,
        Eagle,
        Bear,
        Cougar,
        Snake
    }

    private NetworkVariable<Character> character = new(
        Character.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public Character PlayerCharacter => character.Value;

    public bool Walking => bridge.Walking;

    public int Facing => bridge?.Facing ?? 1;

    public bool Punching { get; private set; } = false;

    public bool Conscious => bridge?.IsConscious ?? true;

    private void Awake()
    {
        bridge = GetComponent<PlayerNetworkBridge>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (!IsClient || !IsSpawned)
            return;

        animator.SetBool("Walking", Walking);
        animator.SetBool("Punching", Punching);
        animator.SetBool("Conscious", Conscious);
        animator.SetInteger("Character", (int)PlayerCharacter);
        animator.SetInteger("Facing", Facing);

        spriteRenderer.flipX = Facing < 0;
    }

    public void AssignCharacter(int characterIndex)
    {
        if (!IsServer)
            return;

        character.Value = (Character)characterIndex;
    }
}
