using UnityEngine;

public class BearBeast : Beast
{
    private PlayerVisual fighterVisual;
    private bool IsBlackBear => fighterVisual.PlayerCharacter == PlayerVisual.Character.BlackBear;

    public override float MoveSpeedMultiplier => (IsBlackBear ? GameGlobals.Instance.blackBearMoveSpeedMultiplier : GameGlobals.Instance.bearMoveSpeedMultiplier);
    public override float PunchDamageMultiplier => (IsBlackBear ? GameGlobals.Instance.blackBearPunchDamageMultiplier : GameGlobals.Instance.bearPunchDamageMultiplier);
    private void Awake() => fighterVisual = GetComponent<PlayerVisual>();
}
