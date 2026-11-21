using UnityEngine;

namespace Player.Bots
{
    public sealed class BeastTransformBotState : IBotState
    {
        private float nextAttempt;

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            if (bot.IsBeast || !bot.IsConscious || Time.fixedUnscaledTime < nextAttempt)
                return default;

            nextAttempt = Time.fixedUnscaledTime + 0.5f;
            return new BotInputFrame { Beast = true };
        }
    }
}
