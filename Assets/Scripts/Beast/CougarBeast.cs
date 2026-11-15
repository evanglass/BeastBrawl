using UnityEngine;

public class CougarBeast : Beast
{
    [SerializeField]
    private Collider2D attackArea;

    public override float MoveSpeedMultiplier => 3f;
    public override float PunchCooldownMultiplier => 0.1f;
    public override bool AutoAttack => true;
    public override Collider2D AttackArea => attackArea;
}
