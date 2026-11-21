using UnityEngine;

namespace Player.Bots
{
    public sealed class BeastAttackBotState : IBotState
    {
        private float nextPunchTime;
        private float? eagleAimStarted;
        private PatrolBotState search;

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            var enemy = BotBrain.Target(bot);
            var beast = bot.ActiveBotBeast;
            if (enemy == null || beast == null) return default;

            if (beast.AutoAttack)
                return PursueOrSearch(bot, enemy);

            if (beast.HandlesAttack)
            {
                Vector2 position = bot.transform.position;
                Vector2 offset = BotBrain.SeenPosition(bot, enemy) - position;
                if (beast is EagleBeast)
                {
                    var eagle = bot.GetComponent<EagleVisual>();
                    if (eagle.CurrentPhase != EagleVisual.Phase.Aiming) eagleAimStarted = null;
                    if (eagle.CurrentPhase == EagleVisual.Phase.FreeFlight)
                    {
                        float x = BotNavigator.FindSpot(bot, BotNavigator.Spot.Center, out _).x - position.x;
                        return new BotInputFrame { Horizontal = Mathf.Abs(x) > 0.25f ? Mathf.Sign(x) : 0f };
                    }
                    if (eagle.CurrentPhase != EagleVisual.Phase.Aiming) return default;
                    eagleAimStarted ??= Time.fixedTime;
                    if (eagle.CanHitWithBoom(offset))
                        return new BotInputFrame { Punch = TryPunch(0.12f) };
                    if (Time.fixedTime - eagleAimStarted.Value >= GameGlobals.Instance.botCenterAfterStall)
                        return new BotInputFrame { Punch = TryPunch(0.12f) };

                    float angle = Mathf.Clamp(Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg, -150f, -30f);
                    float turn = angle - eagle.AimAngle;
                    return new BotInputFrame { Horizontal = Mathf.Clamp(turn / 6f, -1f, 1f) };
                }

                float reach = GameGlobals.Instance.punchReach;
                if (offset.sqrMagnitude > reach * reach && BotBrain.CanNavigateAsBeast(bot))
                    return PursueOrSearch(bot, enemy);
                return new BotInputFrame { Punch = offset.sqrMagnitude <= reach * reach && TryPunch(0.12f) };
            }

            if (!BotBrain.InPunchReach(bot, enemy))
                return PursueOrSearch(bot, enemy);

            return new BotInputFrame
            {
                Horizontal = BotBrain.Face(bot, enemy),
                Punch = BotBrain.FacingTarget(bot, enemy) &&
                    bot.GetComponent<BotInputDriver>().CombatMemory.TryPunch(enemy,
                    GameGlobals.Instance.punchCooldown * beast.PunchCooldownMultiplier)
            };
        }

        // A grounded beast should look for another approach when pursuit is blocked.
        private BotInputFrame PursueOrSearch(PlayerNetworkBridge bot, PlayerNetworkBridge enemy)
        {
            if (!BotBrain.CanNavigateAsBeast(bot)) return default;
            var driver = bot.GetComponent<BotInputDriver>();
            if (driver.CombatMemory.CanPursue(enemy))
            {
                var input = BotBrain.Pursue(bot, enemy);
                if (input.Horizontal != 0f || input.Jump || driver.Navigation.HasNavigationRoute ||
                    !bot.OnFloor || BotBrain.InPunchReach(bot, enemy))
                    return input;

                driver.CombatMemory.PausePursuit(enemy);
            }

            search ??= new PatrolBotState(bot);
            return search.ReadController(bot);
        }

        private bool TryPunch(float interval)
        {
            if (Time.fixedUnscaledTime < nextPunchTime) return false;
            nextPunchTime = Time.fixedUnscaledTime + Mathf.Max(0.12f, interval);
            return true;
        }
    }
}
