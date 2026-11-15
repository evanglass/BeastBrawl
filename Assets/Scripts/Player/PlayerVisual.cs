using Unity.Netcode;
using UnityEngine;

public class PlayerVisual : NetworkBehaviour
{
    private PlayerNetworkBridge bridge;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private Rigidbody2D rb;
    private EagleVisual eagleVisual;
    private MatchManager matchManager;

    [SerializeField]
    private RectTransform beastMeterFill;

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

    public int Facing => eagleVisual != null && eagleVisual.FacingOverride != 0 ? eagleVisual.FacingOverride : bridge?.Facing ?? 1;

    public bool Conscious => bridge?.IsConscious ?? true;

    private void Awake()
    {
        bridge = GetComponent<PlayerNetworkBridge>();
        eagleVisual = GetComponent<EagleVisual>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (!IsClient || !IsSpawned)
            return;

        if (matchManager == null)
            matchManager = FindFirstObjectByType<MatchManager>();

        bool showPlayer = matchManager == null || (matchManager.IsSpawned &&
            (matchManager.CurrentPhase == MatchManager.MatchPhase.RoundCountdown ||
             matchManager.CurrentPhase == MatchManager.MatchPhase.Fighting));
        spriteRenderer.enabled = showPlayer;
        beastMeterFill.parent.gameObject.SetActive(showPlayer);

        animator.SetBool("Walking", Walking);
        animator.SetBool("Conscious", Conscious);
        animator.SetBool("Beast", bridge.IsBeast);
        if (bridge.IsBeast && PlayerCharacter != Character.Bear) animator.ResetTrigger("Punch");
        animator.SetInteger("Character", (int)PlayerCharacter);
        animator.SetInteger("Facing", Facing);
        beastMeterFill.localScale = new Vector3(Mathf.Clamp01(bridge.BeastMeter / 100f), 1f, 1f);

        spriteRenderer.flipX = Facing < 0;
    }

    [Rpc(SendTo.ClientsAndHost)]
    public void PlayEffectRpc(CharacterSfxManager.Effect effect)
    {
        PlayLocalEffect(effect);
    }

    private void PlayLocalEffect(CharacterSfxManager.Effect effect)
    {
        var sounds = CharacterSfxManager.GetOrCreate();
        if (sounds == null) return;
        if (effect == CharacterSfxManager.Effect.BeastActivation)
            sounds.PlayBeastActivation(PlayerCharacter, transform);
        else sounds.Play(PlayerCharacter, effect, transform.position);
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    public void PlayPunchRpc()
    {
        animator.SetTrigger("Punch");
        PlayLocalEffect(CharacterSfxManager.Effect.Attack);
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    public void PlayBeastPunchRpc()
    {
        animator.SetTrigger("Punch");
    }

    public void AssignCharacter(int characterIndex)
    {
        if (!IsServer)
            return;

        character.Value = (Character)characterIndex;
    }
}
