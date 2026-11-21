using UnityEngine;

public class CougarBeast : Beast
{
    [SerializeField]
    private Collider2D attackArea;

    private PlayerVisual fighterVisual;
    private bool IsPanther => fighterVisual.PlayerCharacter == PlayerVisual.Character.Panther;

    public override float MoveSpeedMultiplier => (IsPanther ? GameGlobals.Instance.pantherMoveSpeedMultiplier : GameGlobals.Instance.cougarMoveSpeedMultiplier);
    public override float PunchCooldownMultiplier => (IsPanther ? GameGlobals.Instance.pantherPunchCooldownMultiplier : GameGlobals.Instance.cougarPunchCooldownMultiplier);
    public override bool AutoAttack => true;
    public override Collider2D AttackArea => attackArea;
    private void Awake() => fighterVisual = GetComponent<PlayerVisual>();
}
