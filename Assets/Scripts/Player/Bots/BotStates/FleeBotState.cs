namespace Player.Bots
{
    // Escape with a committed jump or drop before settling for an interior walk.
    public sealed class FleeBotState : IBotState
    {
        private readonly PlayerNetworkBridge threat;
        private readonly bool defensive;
        internal bool Blocked { get; private set; }

        public FleeBotState(PlayerNetworkBridge threat, bool defensive)
        {
            this.threat = threat;
            this.defensive = defensive;
        }

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        internal bool CanContinue(PlayerNetworkBridge bot, bool onCooldown) =>
            !Blocked && defensive == onCooldown && bot.IsOpponent(threat) && threat.IsSpawned &&
            !threat.Eliminated && threat.IsConscious && (defensive || threat.IsBeast) &&
            (!bot.OnFloor || BotBrain.Within(bot, threat, BotTuning.FleeExit));

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            var navigation = bot.GetComponent<BotInputDriver>().Navigation;
            if (navigation.TryDefensiveTransfer(bot, threat, out var input) ||
                BotBrain.TryDefensiveWalk(bot, retreating: true, out input))
                return input;

            Blocked = bot.OnFloor;
            return input;
        }
    }
}
