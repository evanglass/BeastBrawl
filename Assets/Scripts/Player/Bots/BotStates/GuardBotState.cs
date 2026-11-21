using UnityEngine;

namespace Player.Bots
{
    public sealed class GuardBotState : IBotState
    {
        private readonly BotNavigator.Spot spot;

        public GuardBotState(PlayerNetworkBridge bot) => spot = (BotNavigator.Spot)((bot.NetworkObjectId / 2) % 5);

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            Vector2 target = BotNavigator.FindSpot(bot, spot, out var platform);
            return BotBrain.NavigateToSpot(bot, target, platform);
        }
    }
}
