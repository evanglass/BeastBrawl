namespace Player.Bots
{
    public sealed class RecoverBotState : IBotState
    {
        public IBotState NextState(PlayerNetworkBridge bot) =>
            bot.OnFloor || bot.HasGroundBelow || bot.Eliminated
                ? BotBrain.Choose(bot, new IdleBotState())
                : this;

        public BotInputFrame ReadController(PlayerNetworkBridge bot) =>
            bot.GetComponent<BotInputDriver>().Navigation.Recover(bot);
    }
}
