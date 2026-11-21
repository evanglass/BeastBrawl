namespace Player.Bots
{
    public sealed class AttackBotState : IBotState
    {
        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            var enemy = BotBrain.EnemyInPunchReach(bot);
            if (enemy == null)
                return default;

            return new BotInputFrame
            {
                Horizontal = BotBrain.Face(bot, enemy),
                Punch = BotBrain.FacingTarget(bot, enemy) &&
                    bot.GetComponent<BotInputDriver>().CombatMemory.TryPunch(enemy, GameGlobals.Instance.punchCooldown)
            };
        }
    }
}
