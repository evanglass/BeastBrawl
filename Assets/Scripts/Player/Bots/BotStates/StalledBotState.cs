using UnityEngine;

namespace Player.Bots
{
    internal sealed class StalledBotState : IBotState
    {
        internal IBotState Previous { get; }
        private readonly Vector2 origin;
        private readonly float deadline;
        private readonly bool tryUnstick;
        private bool noMove;

        internal StalledBotState(PlayerNetworkBridge bot, IBotState previous, bool tryUnstick)
        {
            Previous = previous;
            origin = bot.transform.position;
            deadline = Time.fixedUnscaledTime + 1.25f;
            this.tryUnstick = tryUnstick;
        }

        internal bool Finished(PlayerNetworkBridge bot) =>
            noMove || Time.fixedUnscaledTime >= deadline ||
            (bot.OnFloor && ((Vector2)bot.transform.position - origin).sqrMagnitude >= 0.25f);

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            var driver = bot.GetComponent<BotInputDriver>();
            var navigation = driver.Navigation;
            if (navigation.FollowLanding(bot, out float horizontal))
                return new BotInputFrame { Horizontal = horizontal };

            BotInputFrame input = default;
            if (tryUnstick && (!driver.ChaseOnCooldown || BotBrain.CanNavigateAsBeast(bot)))
                navigation.Unstick(bot, BotBrain.Target(bot), ref input);
            if (input.Horizontal == 0f && !input.Jump)
                navigation.HeadToCenter(bot, ref input);

            noMove = bot.OnFloor && input.Horizontal == 0f && !input.Jump;
            return input;
        }
    }
}
