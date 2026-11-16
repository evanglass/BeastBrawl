using UnityEngine;

public class CougarBeast : Beast
{
    [SerializeField]
    private Collider2D attackArea;

    public override float MoveSpeedMultiplier => GameGlobals.Instance.cougarMoveSpeedMultiplier;
    public override float PunchCooldownMultiplier => GameGlobals.Instance.cougarPunchCooldownMultiplier;
    public override bool AutoAttack => true;
    public override Collider2D AttackArea => attackArea;
}
