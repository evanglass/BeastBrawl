using UnityEngine;

namespace Player.Bots
{
    public sealed class KiteBotState : IBotState
    {
        internal bool Retreated { get; private set; }

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            var enemy = BotBrain.Target(bot);
            if (enemy == null) return default;

            Retreated = true;
            float away = -Mathf.Sign(BotBrain.Face(bot, enemy));
            float x = BotBrain.SeenPosition(bot, enemy).x + away * (GameGlobals.Instance.punchReach + BotTuning.KiteMargin);
            Vector2 position = bot.transform.position;
            if ((x - position.x) * away <= 0f) return default;

            return BotBrain.Navigate(bot, new Vector2(x, position.y));
        }
    }
}
