using UnityEngine;

public class BearBeast : Beast
{
    public override float MoveSpeedMultiplier => GameGlobals.Instance.bearMoveSpeedMultiplier;
    public override float PunchDamageMultiplier => GameGlobals.Instance.bearPunchDamageMultiplier;
}
