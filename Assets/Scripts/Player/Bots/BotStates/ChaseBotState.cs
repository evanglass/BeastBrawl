using UnityEngine;

namespace Player.Bots
{
    public sealed class ChaseBotState : IBotState
    {
        internal sealed class Budget
        {
            private double duration;
            private double spent;
            private double cooldownRemaining;
            private double aggression;
            internal bool OnCooldown { get; private set; }

            internal void Reset()
            {
                duration = spent = cooldownRemaining = 0d;
                aggression = 1d;
                OnCooldown = false;
            }

            internal void StartRound(GameGlobals settings, PlayerVisual.Character character)
            {
                Reset();
                aggression = settings.GetAggressionMultiplier(character);
                duration = Sample(settings.ChaseDurationMin, settings.ChaseDurationMax, 4f, 10f) * aggression;
            }

            internal void Advance(GameGlobals settings, PlayerVisual.Character character, bool chasing, float elapsed)
            {
                if (OnCooldown)
                {
                    cooldownRemaining -= elapsed;
                    if (cooldownRemaining <= 0f) StartRound(settings, character);
                    return;
                }

                if (!chasing) return;

                spent += elapsed;
                if (spent < duration) return;

                OnCooldown = true;
                cooldownRemaining = Sample(settings.ChaseCooldownMin, settings.ChaseCooldownMax, 2f, 6f) / aggression;
            }

            private static float Sample(float min, float max, float defaultMin, float defaultMax)
            {
                min = float.IsNaN(min) || float.IsInfinity(min) ? defaultMin : Mathf.Max(0f, min);
                max = float.IsNaN(max) || float.IsInfinity(max) ? defaultMax : Mathf.Max(0f, max);
                return Random.Range(Mathf.Min(min, max), Mathf.Max(min, max));
            }
        }

        // Only charge the budget when we actually have pursuit input or a route.
        internal bool IsPursuing { get; private set; }

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            IsPursuing = false;
            var enemy = BotBrain.Target(bot);
            if (enemy == null) return default;

            var driver = bot.GetComponent<BotInputDriver>();
            var input = BotBrain.Pursue(bot, enemy);
            IsPursuing = input.Horizontal != 0f || input.Jump || driver.Navigation.HasNavigationRoute;
            if (!IsPursuing && bot.OnFloor)
                driver.CombatMemory.PausePursuit(enemy);
            return input;
        }
    }
}
