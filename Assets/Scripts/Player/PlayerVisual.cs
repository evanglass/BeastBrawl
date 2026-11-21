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
    private float snakeBeastClipLength = 0.5f;

    [SerializeField]
    private RectTransform beastMeterFill;
    [SerializeField] private Color teamTwoBeastMeterColor = new Color32(122, 54, 123, 255);
    private UnityEngine.UI.Graphic beastMeterGraphic;
    private Color defaultBeastMeterColor;
    private int displayedMeterTeam = -1;

    public enum Character
    {
        None,
        Eagle,
        Bear,
        Cougar,
        Snake,
        Hawk,
        BlackBear,
        Panther,
        Mamba
    }

    public static Character OriginalCharacter(Character value) => value switch
    {
        Character.Hawk => Character.Eagle,
        Character.BlackBear => Character.Bear,
        Character.Panther => Character.Cougar,
        Character.Mamba => Character.Snake,
        _ => value
    };

    public static string CharacterName(Character value) =>
        value == Character.BlackBear ? "Black Bear" : value.ToString();

    [SerializeField] private RuntimeAnimatorController hawkAnimator;
    [SerializeField] private RuntimeAnimatorController blackBearAnimator;
    [SerializeField] private RuntimeAnimatorController pantherAnimator;
    [SerializeField] private RuntimeAnimatorController mambaAnimator;
    private RuntimeAnimatorController originalAnimator;
    private Character animatedCharacter;

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
        originalAnimator = animator.runtimeAnimatorController;
        if (beastMeterFill != null && beastMeterFill.TryGetComponent(out beastMeterGraphic))
            defaultBeastMeterColor = beastMeterGraphic.color;

        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name == "Snake_Beast")
            {
                snakeBeastClipLength = clip.length;
                break;
            }
        }
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

        if (animatedCharacter != PlayerCharacter)
        {
            animatedCharacter = PlayerCharacter;
            var controller = animatedCharacter switch
            {
                Character.Hawk => hawkAnimator,
                Character.BlackBear => blackBearAnimator,
                Character.Panther => pantherAnimator,
                Character.Mamba => mambaAnimator,
                _ => originalAnimator
            };
            animator.runtimeAnimatorController = controller != null ? controller : originalAnimator;
        }

        var original = OriginalCharacter(PlayerCharacter);
        animator.SetBool("Walking", Walking);
        animator.SetBool("Conscious", Conscious);
        animator.SetBool("Beast", bridge.IsBeast);
        animator.SetFloat("SnakeBeastSpeed", snakeBeastClipLength / Mathf.Max(bridge.SnakeBiteDelay, 0.02f));
        if (bridge.IsBeast && original != Character.Bear) animator.ResetTrigger("Punch");
        animator.SetInteger("Character", (int)original);
        animator.SetInteger("Facing", Facing);
        beastMeterFill.localScale = new Vector3(Mathf.Clamp01(bridge.BeastMeter / 100f), 1f, 1f);

        if (beastMeterGraphic != null && displayedMeterTeam != bridge.TeamId)
        {
            displayedMeterTeam = bridge.TeamId;
            beastMeterGraphic.color = displayedMeterTeam == 2 ? teamTwoBeastMeterColor : defaultBeastMeterColor;
        }

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
        bridge.RefreshCharacterSettings();
        eagleVisual?.RefreshTuning();
    }
}
