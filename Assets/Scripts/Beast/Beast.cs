using UnityEngine;

public abstract class Beast : MonoBehaviour
{
    public virtual float MoveSpeedMultiplier => 1f;
    public virtual float PunchCooldownMultiplier => 1f;
    public virtual float PunchDamageMultiplier => 1f;
    public virtual float PunchKnockbackMultiplier => 1f;
    public virtual bool AutoAttack => false;
    public virtual Collider2D AttackArea => null;
    public virtual bool ControlsMovement => false;
    public virtual bool HandlesAttack => false;
    public virtual bool UsesBeastTimer => true;

    public virtual void OnBeastStart(GameObject player) { }
    public virtual void OnBeastEnd(GameObject player) { }
    public virtual void OnPunchHit(GameObject target) { }
    public virtual void TickBeast(float deltaTime) { }
    public virtual void RequestAttack() { }
}
