using UnityEngine;

namespace Player.Bots
{
    // Choose the target and state before asking a state for controller input.
    public static class BotBrain
    {
        public static PlayerNetworkBridge NearestEnemy(PlayerNetworkBridge bot) => Nearest(bot, beastOnly: false);

        internal static PlayerNetworkBridge Target(PlayerNetworkBridge bot) =>
            bot.GetComponent<BotInputDriver>().CombatMemory.Target;

        internal static bool CanNavigateAsBeast(PlayerNetworkBridge bot) =>
            bot.ActiveBotBeast is { ControlsMovement: false, MoveSpeedMultiplier: > 0f };

        // Prefer a target we can fight now. Keep the old one when the options are close.
        internal static PlayerNetworkBridge SelectTarget(PlayerNetworkBridge bot, PlayerNetworkBridge previous,
            PlayerNetworkBridge positioningTarget)
        {
            var combat = bot.GetComponent<BotInputDriver>().CombatMemory;
            var support = BotNavigator.SupportUnder(bot, bot, false);
            PlayerNetworkBridge best = null;
            float bestCost = float.PositiveInfinity;
            foreach (var other in PlayerNetworkBridge.All)
            {
                if (!bot.IsOpponent(other) || !other.IsSpawned || other.Eliminated) continue;
                Vector2 seen = SeenPosition(bot, other);
                var platform = BotNavigator.SupportUnder(other, bot, observedPosition: seen);
                bool inReach = InPunchReach(bot, other);
                float cost = Vector2.Distance(bot.transform.position, seen);
                if (platform == null) cost += 6f;
                else if (platform != support) cost += 2f;
                if (!combat.CanPursue(other)) cost += 5f;
                if (other.IsConscious && other.IsBeast) cost -= 0.5f;
                if (!other.IsConscious && platform == support && support != null &&
                    Mathf.Min(seen.x - support.bounds.min.x, support.bounds.max.x - seen.x) <= 1.5f)
                    cost -= 1f;

                if (other == previous) cost -= 0.9f;
                if (other == positioningTarget) cost -= 12f;
                if (inReach) cost -= other.IsConscious ? 100f : 80f;
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = other;
            }
            return best;
        }

        internal static Vector2 SeenPosition(PlayerNetworkBridge bot, PlayerNetworkBridge enemy)
        {
            var driver = bot.GetComponent<BotInputDriver>();
            return driver != null ? driver.CombatMemory.PositionOf(enemy) : (Vector2)enemy.transform.position;
        }

        internal static PlayerNetworkBridge EnemyInPunchReach(PlayerNetworkBridge bot)
        {
            var target = Target(bot);
            return bot.IsOpponent(target) && InPunchReach(bot, target) ? target : null;
        }

        private static PlayerNetworkBridge Nearest(PlayerNetworkBridge bot, bool beastOnly, bool consciousOnly = false)
        {
            PlayerNetworkBridge nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (PlayerNetworkBridge other in PlayerNetworkBridge.All)
            {
                if (!bot.IsOpponent(other) || !other.IsSpawned || other.Eliminated ||
                    (beastOnly && !other.IsBeast) || (consciousOnly && !other.IsConscious)) continue;

                float distance = (SeenPosition(bot, other) - (Vector2)bot.transform.position).sqrMagnitude;
                if (distance >= nearestDistance) continue;

                nearestDistance = distance;
                nearest = other;
            }
            return nearest;
        }

        internal static bool InPunchReach(PlayerNetworkBridge bot, PlayerNetworkBridge enemy) =>
            InPunchReach(bot, enemy, bot.GetComponent<BotInputDriver>().CombatMemory.EffectiveReach);

        internal static bool InPunchReach(PlayerNetworkBridge bot, PlayerNetworkBridge enemy, float reach)
        {
            Vector2 origin = bot.transform.position;
            Vector2 seen = SeenPosition(bot, enemy);
            Vector2 shift = seen - (Vector2)enemy.transform.position;
            var body = enemy.GetComponent<Collider2D>();
            var punch = bot.GetComponent<PlayerPunch>();
            int facing = DesiredFacing(bot, seen);
            float reachScale = reach / Mathf.Max(0.01f, GameGlobals.Instance.punchReach);
            if (punch != null && punch.TryGetBotAttackBounds(facing, reachScale, out var attack))
            {
                if (body == null || !body.isActiveAndEnabled)
                    return seen.x >= attack.min.x && seen.x <= attack.max.x &&
                        seen.y >= attack.min.y && seen.y <= attack.max.y;

                Bounds perceived = body.bounds;
                perceived.center += (Vector3)shift;
                return attack.min.x <= perceived.max.x && attack.max.x >= perceived.min.x &&
                    attack.min.y <= perceived.max.y && attack.max.y >= perceived.min.y;
            }

            // Keep the old estimate for fighters without an attack box.
            Vector2 target = body != null && body.isActiveAndEnabled
                ? (Vector2)body.bounds.ClosestPoint(origin - shift) + shift
                : seen;
            Vector2 offset = target - origin;
            return Mathf.Abs(offset.x) <= reach &&
                Mathf.Abs(offset.y) <= BotTuning.PunchHeightTolerance;
        }

        private static int DesiredFacing(PlayerNetworkBridge bot, Vector2 target)
        {
            float x = target.x - bot.transform.position.x;
            return Mathf.Abs(x) <= 0.01f ? (bot.Facing < 0 ? -1 : 1) : (int)Mathf.Sign(x);
        }

        internal static float Face(PlayerNetworkBridge bot, PlayerNetworkBridge enemy) =>
            BotTuning.FaceInput * DesiredFacing(bot, SeenPosition(bot, enemy));

        // Turn first so the server evaluates the swing with the intended facing.
        internal static bool FacingTarget(PlayerNetworkBridge bot, PlayerNetworkBridge enemy) =>
            bot.Facing == DesiredFacing(bot, SeenPosition(bot, enemy));

        internal static BotInputFrame Navigate(PlayerNetworkBridge bot, Vector2 target)
        {
            BotNavigator.Steer(bot, target, out float horizontal, out bool jump);
            return new BotInputFrame { Horizontal = horizontal, Jump = jump };
        }

        internal static BotInputFrame Pursue(PlayerNetworkBridge bot, PlayerNetworkBridge enemy)
        {
            Vector2 target = SeenPosition(bot, enemy);
            var destination = BotNavigator.SupportUnder(enemy, bot, observedPosition: target);
            return NavigateToSpot(bot, target, destination);
        }

        internal static BotInputFrame NavigateToSpot(PlayerNetworkBridge bot, Vector2 target,
            Collider2D destination, bool useRoute = true)
        {
            bool allowJump = destination != null && BotNavigator.SupportUnder(bot, bot, false) != destination;
            BotNavigator.Steer(bot, target, destination, allowJump, out float horizontal, out bool jump, useRoute);
            return new BotInputFrame { Horizontal = horizontal, Jump = jump };
        }

        public static IBotState Choose(PlayerNetworkBridge bot, IBotState current)
        {
            var stalled = current as StalledBotState;
            var normal = ChooseNormal(bot, stalled?.Previous ?? current, out var reachableEnemy);
            var driver = bot.GetComponent<BotInputDriver>();
            var navigation = driver.Navigation;

            // Ground-moving beasts can get stuck just like a normal chaser.
            bool beastPursuit = normal is BeastAttackBotState && CanNavigateAsBeast(bot);
            bool moving = normal is ChaseBotState or PatrolBotState or KiteBotState or FleeBotState || beastPursuit;
            bool canStall = moving && reachableEnemy == null && (!driver.ChaseOnCooldown || beastPursuit);
            if (stalled != null)
                return canStall && !stalled.Finished(bot) ? stalled : normal;

            if (canStall && navigation.IsStalled(bot, canUnstick: true, out bool tryUnstick))
                return new StalledBotState(bot, normal, tryUnstick);

            if (!canStall) navigation.ResetStall();

            if (current is EdgePositionBotState edge)
            {
                // Keep circling past the downed target unless another fight needs attention.
                if (bot.ActiveBotBeast?.ControlsMovement != true &&
                    (normal is ChaseBotState or PatrolBotState ||
                     (normal is AttackBotState && reachableEnemy == edge.Target)) && edge.CanContinue(bot))
                    return edge;
                return normal;
            }

            if (moving && (normal is ChaseBotState or PatrolBotState) &&
                EdgePositionBotState.TryEnter(bot, driver.EdgeAttempts, out var positioning))
                return positioning;

            return normal;
        }

        private static IBotState ChooseNormal(PlayerNetworkBridge bot, IBotState current,
            out PlayerNetworkBridge reachableEnemy)
        {
            reachableEnemy = null;
            if (bot.Eliminated || !bot.IsConscious)
                return current;

            if (bot.IsBeast)
            {
                if (CanNavigateAsBeast(bot)) reachableEnemy = EnemyInPunchReach(bot);
                return current is BeastAttackBotState ? current : new BeastAttackBotState();
            }

            if (bot.BeastMeter >= 99.9f)
                return current is BeastTransformBotState ? current : new BeastTransformBotState();

            reachableEnemy = EnemyInPunchReach(bot);
            var enemy = Target(bot);
            if (enemy == null)
                return current is IdleBotState ? current : new IdleBotState();

            var driver = bot.GetComponent<BotInputDriver>();
            var combat = driver.CombatMemory;
            bool inReach = reachableEnemy != null;
            bool canFlee = !HasUnconsciousOpponent(bot);
            if (driver.ChaseOnCooldown && canFlee &&
                ChooseFlee(bot, current, defensive: true, out var retreat))
                return retreat;

            if (current is KiteBotState kite && (!kite.Retreated || (inReach && combat.EnemyApproaching)))
                return current;

            if (inReach && combat.SwingAttempts >= BotTuning.PunchesBeforeKite && combat.HasLandedSwing)
            {
                combat.StartKite();
                return new KiteBotState();
            }

            // Trade blows when an opponent is in range. Flee is for the space between fights.
            if (!inReach && !driver.ChaseOnCooldown && canFlee &&
                ChooseFlee(bot, current, defensive: false, out var flee))
                return flee;

            if (inReach)
                return current is AttackBotState ? current : new AttackBotState();

            if (driver.ChaseOnCooldown || !combat.CanPursue(enemy))
                return current is PatrolBotState ? current : new PatrolBotState(bot);

            return current is ChaseBotState ? current : new ChaseBotState();
        }

        internal static bool HasUnconsciousOpponent(PlayerNetworkBridge bot)
        {
            foreach (var other in PlayerNetworkBridge.All)
                if (bot.IsOpponent(other) && other.IsSpawned && !other.Eliminated && !other.IsConscious)
                    return true;
            return false;
        }

        private static bool ChooseFlee(PlayerNetworkBridge bot, IBotState current, bool defensive, out FleeBotState flee)
        {
            flee = current as FleeBotState;
            if (flee != null && flee.CanContinue(bot, defensive)) return true;
            var navigation = bot.GetComponent<BotInputDriver>().Navigation;
            if (Time.fixedUnscaledTime < navigation.RetreatRetryAt) return false;
            var threat = Nearest(bot, beastOnly: !defensive, consciousOnly: true);
            if (threat == null || !Within(bot, threat, BotTuning.FleeEnter)) return false;
            flee = new FleeBotState(threat, defensive);
            return true;
        }

        internal static PlayerNetworkBridge DefensiveThreat(PlayerNetworkBridge bot)
        {
            var threat = Nearest(bot, beastOnly: false, consciousOnly: true);
            return threat != null && Within(bot, threat, BotTuning.FleeExit) ? threat : null;
        }

        // Stay on this platform and avoid walking closer to a conscious opponent.
        internal static bool TryDefensiveWalk(PlayerNetworkBridge bot, bool retreating, out BotInputFrame input)
        {
            input = default;
            if (!bot.OnFloor) return false;
            var support = BotNavigator.SupportUnder(bot, bot, false);
            if (support == null) return false;

            Bounds bounds = support.bounds;
            var body = bot.GetComponent<Collider2D>();
            float inset = Mathf.Max(0.35f, body == null ? 0f : body.bounds.extents.x);
            if (bounds.extents.x <= inset) return false;

            Vector2 origin = bot.transform.position;
            float startClearance = DefensiveClearance(bot, origin, origin.x);
            float bestClearance = startClearance;
            for (int i = 0; i < (retreating ? 2 : 1); i++)
            {
                float x = retreating ? (i == 0 ? Mathf.Lerp(bounds.min.x + inset, bounds.center.x, 0.35f) :
                    Mathf.Lerp(bounds.max.x - inset, bounds.center.x, 0.35f)) : bounds.center.x;
                float clearance = DefensiveClearance(bot, new Vector2(x, origin.y), x);
                if ((retreating && clearance <= bestClearance) ||
                    DefensiveClearance(bot, origin, x) < startClearance ||
                    !BotNavigator.TryWalkOnSupport(bot, support, x, out var candidate))
                    continue;

                bestClearance = clearance;
                input = candidate;
            }
            return input.Horizontal != 0f;
        }

        internal static float DefensiveClearance(PlayerNetworkBridge bot, Vector2 origin, float endX)
        {
            float clearance = float.PositiveInfinity;
            float left = Mathf.Min(origin.x, endX);
            float right = Mathf.Max(origin.x, endX);
            foreach (var enemy in PlayerNetworkBridge.All)
            {
                if (!bot.IsOpponent(enemy) || !enemy.IsSpawned || enemy.Eliminated || !enemy.IsConscious)
                    continue;

                Vector2 seen = SeenPosition(bot, enemy);
                Vector2 closest = new(Mathf.Clamp(seen.x, left, right), origin.y);
                clearance = Mathf.Min(clearance, (seen - closest).sqrMagnitude);
            }
            return clearance;
        }

        internal static bool Within(PlayerNetworkBridge bot, PlayerNetworkBridge other, float distance) =>
            (SeenPosition(bot, other) - (Vector2)bot.transform.position).sqrMagnitude <= distance * distance;
    }
}
