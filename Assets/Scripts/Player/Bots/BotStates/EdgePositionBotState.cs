using System.Collections.Generic;
using UnityEngine;

namespace Player.Bots
{
    internal sealed class EdgePositionBotState : IBotState
    {
        internal sealed class Attempts
        {
            private readonly Dictionary<PlayerNetworkBridge, (Vector2 Position, Collider2D Platform, float RetryAt)>
                attempted = new();

            internal void Reset() => attempted.Clear();

            internal bool CanTry(PlayerNetworkBridge target, Vector2 position, Collider2D platform) =>
                !attempted.TryGetValue(target, out var last) || platform != last.Platform ||
                (position - last.Position).sqrMagnitude >= 0.75f * 0.75f ||
                Time.fixedUnscaledTime >= last.RetryAt;

            internal void Remember(PlayerNetworkBridge target, Vector2 position, Collider2D platform, bool aligned) =>
                attempted[target] = (position, platform, aligned ? float.PositiveInfinity : Time.fixedUnscaledTime + 2f);
        }

        internal PlayerNetworkBridge Target { get; }
        private readonly Collider2D platform;
        private readonly float side;
        private readonly float deadline;
        private Vector2 lastTargetPosition;
        private Vector2 position;
        private bool aligned;
        private bool routeFailed;

        private EdgePositionBotState(PlayerNetworkBridge target, Collider2D platform, Vector2 targetPosition,
            Vector2 position, float side)
        {
            Target = target;
            this.platform = platform;
            lastTargetPosition = targetPosition;
            this.position = position;
            this.side = side;
            deadline = Time.fixedUnscaledTime + 3f;
        }

        internal static bool TryEnter(PlayerNetworkBridge bot, Attempts attempts, out EdgePositionBotState state)
        {
            state = null;
            if (!bot.OnFloor || bot.BotArena == null || !bot.BotArena.HasRecoveryGeometry) return false;

            float best = float.PositiveInfinity;
            float spacing = Spacing(bot);
            foreach (var target in PlayerNetworkBridge.All)
            {
                if (!bot.IsOpponent(target) || !TryGetTarget(bot, target, out var point, out var support) ||
                    !IsClosest(bot, target, point) || !attempts.CanTry(target, point, support))
                    continue;

                float distance = (point - (Vector2)bot.transform.position).sqrMagnitude;
                if (distance >= best) continue;

                // Start with the nearer ledge on the platform holding the opponent.
                float side = point.x <= support.bounds.center.x ? -1f : 1f;
                if (!BotNavigator.TryEdgePosition(bot, support, point, side, spacing, out var goal))
                {
                    side = -side;
                    if (!BotNavigator.TryEdgePosition(bot, support, point, side, spacing, out goal))
                        continue;
                }
                if (AtPosition(bot, support, goal))
                {
                    attempts.Remember(target, point, support, aligned: true);
                    continue;
                }

                best = distance;
                state = new EdgePositionBotState(target, support, point, goal, side);
            }
            return state != null;
        }

        internal bool CanContinue(PlayerNetworkBridge bot)
        {
            if (!TryGetTarget(bot, Target, out var point, out var support) || support != platform ||
                !IsClosest(bot, Target, point) ||
                !BotNavigator.TryEdgePosition(bot, platform, point, side, Spacing(bot), out position))
                return false;

            lastTargetPosition = point;
            aligned = AtPosition(bot, platform, position);
            return !aligned && !routeFailed && Time.fixedUnscaledTime < deadline;
        }

        internal void Leave(Attempts attempts)
        {
            if (Target != null)
                attempts.Remember(Target, lastTargetPosition, platform, aligned);
        }

        public IBotState NextState(PlayerNetworkBridge bot) => BotBrain.Choose(bot, this);

        public BotInputFrame ReadController(PlayerNetworkBridge bot)
        {
            var navigation = bot.GetComponent<BotInputDriver>().Navigation;
            routeFailed = !navigation.TrySteerToPosition(bot, position, platform, out var input);
            return input;
        }

        private static float Spacing(PlayerNetworkBridge bot) =>
            Mathf.Clamp(bot.GetComponent<BotInputDriver>().CombatMemory.EffectiveReach * 0.7f, 0.4f, 0.85f);

        private static bool AtPosition(PlayerNetworkBridge bot, Collider2D platform, Vector2 goal) =>
            bot.OnFloor && Mathf.Abs(bot.transform.position.x - goal.x) <= 0.2f &&
            BotNavigator.SupportUnder(bot, bot, false) == platform;

        private static bool TryGetTarget(PlayerNetworkBridge bot, PlayerNetworkBridge target,
            out Vector2 position, out Collider2D support)
        {
            position = default;
            support = null;
            if (!bot.IsOpponent(target) || !target.IsSpawned || target.Eliminated || target.IsConscious) return false;
            var body = target.GetComponent<Collider2D>();
            if (body == null || !body.isActiveAndEnabled) return false;

            position = target.transform.position;
            support = BotNavigator.SupportUnder(target, bot, false);
            return support != null;
        }

        private static bool IsClosest(PlayerNetworkBridge bot, PlayerNetworkBridge target, Vector2 point)
        {
            float distance = (point - (Vector2)bot.transform.position).sqrMagnitude;
            foreach (var fighter in PlayerNetworkBridge.All)
            {
                // The downed fighter's allies aren't competing to set up this ring-out.
                if (fighter == bot || !target.IsOpponent(fighter) || !fighter.IsSpawned ||
                    fighter.Eliminated || !fighter.IsConscious) continue;

                float otherDistance = (point - (Vector2)fighter.transform.position).sqrMagnitude;
                // Use the same tie breaker on every bot.
                if (otherDistance < distance || (otherDistance == distance && fighter.NetworkObjectId < bot.NetworkObjectId))
                    return false;
            }
            return true;
        }
    }
}
