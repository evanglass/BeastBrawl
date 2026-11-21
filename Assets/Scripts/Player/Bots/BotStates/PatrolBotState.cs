using UnityEngine;

namespace Player.Bots
{
    // Hold a useful waypoint until we get there or the navigator rules it out.
    public sealed class PatrolBotState : IBotState
    {
        private static readonly BotNavigator.Spot[] Route =
        {
            BotNavigator.Spot.Center, BotNavigator.Spot.Top, BotNavigator.Spot.LowerLeft,
            BotNavigator.Spot.UpperRight, BotNavigator.Spot.LowerRight, BotNavigator.Spot.UpperLeft
        };

        private int waypoint;
        private float retryAt;

        public PatrolBotState(PlayerNetworkBridge bot) => waypoint = (int)(bot.NetworkObjectId % (ulong)Route.Length);

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            var driver = bot.GetComponent<BotInputDriver>();
            var navigation = driver.Navigation;
            if (driver.ChaseOnCooldown && !BotBrain.CanNavigateAsBeast(bot) && !BotBrain.HasUnconsciousOpponent(bot))
            {
                var threat = BotBrain.DefensiveThreat(bot);
                if (navigation.TryDefensiveTransfer(bot, threat, out var defensiveInput)) return defensiveInput;
                BotBrain.TryDefensiveWalk(bot, retreating: threat != null, out defensiveInput);
                return defensiveInput;
            }

            if (Time.fixedUnscaledTime < retryAt) return default;
            for (int i = 0; i < Route.Length; i++)
            {
                Vector2 target = BotNavigator.FindSpot(bot, Route[waypoint], out var platform);
                if (platform != null && navigation.TrySteerToPosition(bot, target, platform, out var input))
                    return input;
                waypoint = (waypoint + 1) % Route.Length;
            }

            // Give blocked waypoints a moment before trying the loop again.
            retryAt = Time.fixedUnscaledTime + 0.5f;
            return default;
        }
    }
}
