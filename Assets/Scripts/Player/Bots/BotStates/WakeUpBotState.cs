using UnityEngine;

namespace Player.Bots
{
    public sealed class WakeUpBotState : IBotState
    {
        private float nextPunchTime;

        public IBotState NextState(PlayerNetworkBridge bot) =>
            bot.IsConscious ? BotBrain.Choose(bot, new IdleBotState()) : this;

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            if (bot.IsConscious || Time.fixedUnscaledTime < nextPunchTime) return default;

            nextPunchTime = Time.fixedUnscaledTime + 0.125f;
            return new BotInputFrame { Punch = true };
        }
    }
}
