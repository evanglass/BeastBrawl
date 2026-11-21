using System.Collections.Generic;
using UnityEngine;

namespace Player.Bots
{
    internal sealed class BotCombatMemory
    {
        private sealed class TrackedPerceptionOfEnemy
        {   
            // Queue of enemy position history
            internal readonly Queue<(float Time, Vector2 Position)> PositionHistory = new(); 
            
            internal Vector2 SeenPosition; // the delayed position a bot actually reacts to
            internal bool WasInReach;      // Whether the trackedEnemy was in range for a punch
            internal float PursuitRetryAt; // Give this opponent a moment when the path fails.
            internal float ReactAt;        // The earliest time the bot may react after an enemy enters its reach
        }

        private readonly Dictionary<PlayerNetworkBridge, TrackedPerceptionOfEnemy> trackedEnemies = new();
        // Selection picks one target for navigation and combat on this decision tick.
        internal PlayerNetworkBridge Target { get; set; }
        private PlayerNetworkBridge enemy;
        private Vector2 previousEnemyPosition;
        private int approachTicks;
        private float punchReadyAt;
        private float? reachMultiplier;
        internal int SwingAttempts { get; private set; }
        private uint confirmedSwings;
        private uint swingsAtLastKite;
        internal bool HasLandedSwing => confirmedSwings != swingsAtLastKite;
        internal bool EnemyApproaching => approachTicks >= 2;
        internal float EffectiveReach
        {
            get
            {
                reachMultiplier ??= Sample(GameGlobals.Instance.botPunchReachMultiplier);
                return GameGlobals.Instance.punchReach * reachMultiplier.Value;
            }
        }

        private static float Sample(Vector2 range) =>
            Random.Range(Mathf.Max(0f, Mathf.Min(range.x, range.y)), Mathf.Max(0f, Mathf.Max(range.x, range.y)));
        
        // Captures all other player's data into a dictionary <PlayerNetworkBridge, Sight>
        internal void Capture(PlayerNetworkBridge bot)
        {
            float now = Time.fixedTime;                          // Gets current physics time
            Vector2 range = GameGlobals.Instance.botVisionDelay; // Records bot vision delay range from game globals
            
            // Records the time of the oldest tracked memories
            float oldestTimeToKeep = now - Mathf.Max(range.x, range.y) - Time.fixedDeltaTime;
            
            // Loops through each Player in the PlayerNetworkBridge:   
            foreach (var otherPlayer in PlayerNetworkBridge.All)
            {   
                // Skip any null players, or the owned bot itself
                if (!bot.IsOpponent(otherPlayer) || !otherPlayer.IsSpawned || otherPlayer.Eliminated) continue;
                
                // Either finds the enemy and its tracking perception data or creates a new entry
                if (!trackedEnemies.TryGetValue(otherPlayer, out var trackedPerceptionOfEnemy)) {
                    trackedPerceptionOfEnemy = new TrackedPerceptionOfEnemy();
                    trackedEnemies.Add(otherPlayer, trackedPerceptionOfEnemy);
                }
                    
                // Makes a copy of the other players position
                Vector2 enemyPosition = otherPlayer.transform.position;
                
                // Tracks the very first position of an enemy and makes that the current seenPosition reference
                if (trackedPerceptionOfEnemy.PositionHistory.Count == 0) trackedPerceptionOfEnemy.SeenPosition = enemyPosition;
                
                // Enqueues the current physics frame seen position and physics time of the otherPlayer
                trackedPerceptionOfEnemy.PositionHistory.Enqueue((now, enemyPosition));
                
                // Deque and PositionHistory entries that are older than the oldestTimeToKeep
                while (trackedPerceptionOfEnemy.PositionHistory.Count > 1 && trackedPerceptionOfEnemy.PositionHistory.Peek().Time < oldestTimeToKeep)
                    trackedPerceptionOfEnemy.PositionHistory.Dequeue();
            }
        }

        internal void UpdateVision()
        {
            float cutoff = Time.fixedTime - Sample(GameGlobals.Instance.botVisionDelay);
            foreach (var track in trackedEnemies.Values)
            {
                var previous = track.PositionHistory.Peek();
                track.SeenPosition = previous.Position;
                foreach (var sample in track.PositionHistory)
                {
                    if (sample.Time >= cutoff)
                    {
                        track.SeenPosition = Vector2.Lerp(previous.Position, sample.Position,
                            Mathf.InverseLerp(previous.Time, sample.Time, cutoff));
                        break;
                    }
                    track.SeenPosition = sample.Position;
                    previous = sample;
                }
            }
        }

        internal Vector2 PositionOf(PlayerNetworkBridge target) =>
            trackedEnemies.TryGetValue(target, out var track) ? track.SeenPosition : (Vector2)target.transform.position;

        internal void Observe(PlayerNetworkBridge bot, PlayerNetworkBridge target)
        {
            foreach (var other in PlayerNetworkBridge.All)
            {
                if (!bot.IsOpponent(other) || other.Eliminated || !trackedEnemies.TryGetValue(other, out var track))
                    continue;

                // Reaction starts on entry into ordinary reach. Per-punch aim
                // variation is checked separately when deciding to swing.
                bool inReach = BotBrain.InPunchReach(bot, other, Mathf.Max(GameGlobals.Instance.punchReach, EffectiveReach));
                if (inReach && !track.WasInReach)
                    track.ReactAt = Time.fixedTime + Sample(GameGlobals.Instance.botReactionDelay);
                track.WasInReach = inReach;
            }

            if (target == null || target != enemy)
            {
                enemy = target;
                approachTicks = 0;
                if (target != null) previousEnemyPosition = PositionOf(target);
                return;
            }

            Vector2 position = PositionOf(target);
            Vector2 towardBot = (Vector2)bot.transform.position - previousEnemyPosition;
            bool closing = Vector2.Dot(position - previousEnemyPosition, towardBot.normalized) > 0.01f;
            approachTicks = closing ? Mathf.Min(2, approachTicks + 1) : 0;
            previousEnemyPosition = position;
        }

        internal bool TryPunch(PlayerNetworkBridge target, float cooldown)
        {
            float now = Time.fixedTime;
            if (now < punchReadyAt || (trackedEnemies.TryGetValue(target, out var track) && now < track.ReactAt))
                return false;

            punchReadyAt = now + Mathf.Max(Time.fixedDeltaTime, cooldown) +
                Sample(GameGlobals.Instance.botPunchCooldownExtra);
            reachMultiplier = null;
            SwingAttempts++;
            return true;
        }

        internal bool CanPursue(PlayerNetworkBridge target) =>
            !trackedEnemies.TryGetValue(target, out var track) || Time.fixedUnscaledTime >= track.PursuitRetryAt;

        internal void PausePursuit(PlayerNetworkBridge target)
        {
            if (trackedEnemies.TryGetValue(target, out var track))
                track.PursuitRetryAt = Time.fixedUnscaledTime + 0.75f;
        }

        internal void ObserveConfirmedSwings(uint count) => confirmedSwings = count;

        internal void StartKite()
        {
            SwingAttempts = 0;
            swingsAtLastKite = confirmedSwings;
        }

        internal void Reset()
        {
            trackedEnemies.Clear();
            Target = null;
            enemy = null;
            approachTicks = 0;
            punchReadyAt = 0f;
            reachMultiplier = null;
            confirmedSwings = swingsAtLastKite = 0;
            SwingAttempts = 0;
        }
    }
}
