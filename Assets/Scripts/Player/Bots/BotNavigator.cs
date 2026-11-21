using System;
using System.Collections.Generic;
using Random = UnityEngine.Random;
using UnityEngine;

namespace Player.Bots
{
    public sealed class BotNavigator
    {
        internal enum Spot { Center, Left, Right, Bottom, Top, LowerLeft, LowerRight, UpperLeft, UpperRight }

        private static readonly RaycastHit2D[] GroundHits = new RaycastHit2D[128];
        private static readonly ContactFilter2D GroundFilter = new() { useTriggers = false };
        private Vector2? previousPosition;
        private float previousTime;
        private Vector2 velocity;
        private float nextJumpTime;
        private Collider2D landingPlatform;
        private float landingFraction;
        private float startedAt;
        private float landingDeadline;
        private bool leftFloor;
        private float secondJumpDue = float.PositiveInfinity;
        private float doubleJumpFlightEnd;
        private float doubleJumpStartX, doubleJumpStartTime, doubleJumpSpeed, doubleJumpTravel;
        private float escapeDirection;
        private float escapeUntil;
        private Vector2? centerStallOrigin;
        private float centerStallSince;
        private float stallRetryAt;
        private bool routeStalled;
        private Collider2D routeDestination, routeFrom, routePlatform, previousRoutePlatform;
        private float routeLaunchX, routeLandingX, routeDuration, routeSecondJump;
        private bool routeDrop, routeInTransit;
        private float routeBestProgress, routeProgressSince, nextRoutePlan;
        private Collider2D failedFrom, failedTo;
        private float failedUntil;
        // Defensive goals use the same launch and landing controller as chase.
        private bool defensiveTransfer;
        internal float RetreatRetryAt { get; private set; }
        private const float RouteRetryDelay = 0.5f;
        internal bool HasNavigationRoute => routePlatform != null;


        internal void Observe(PlayerNetworkBridge bot)
        {
            Vector2 position = bot.transform.position;
            float now = Time.fixedUnscaledTime;
            velocity = previousPosition.HasValue && now > previousTime
                ? (position - previousPosition.Value) / (now - previousTime)
                : Vector2.zero;
            previousPosition = position;
            previousTime = now;

            // Remember a planned drop before the offstage reflex decides who steers the fall.
            if (routeDrop && !routeInTransit && !bot.OnFloor && routePlatform != null &&
                routePlatform.isActiveAndEnabled && Mathf.Abs(position.x - routeLaunchX) < 0.75f)
            {
                routeInTransit = true;
                BeginLanding(bot, routePlatform, routeLandingX, routeDuration + 0.75f, out _, jumped: false);
            }
            if (landingPlatform == null) return;

            leftFloor |= !bot.OnFloor;
            Bounds bounds = landingPlatform.bounds;
            float landingX = Mathf.Lerp(bounds.min.x, bounds.max.x, landingFraction);
            if (!landingPlatform.isActiveAndEnabled || now >= landingDeadline ||
                (leftFloor && bot.OnFloor) ||
                (float.IsPositiveInfinity(secondJumpDue) && now > startedAt + Time.fixedDeltaTime && velocity.y < 0f &&
                 TryGetSurface(bot, landingPlatform, landingX, out float top) && position.y < top))
                landingPlatform = null;
        }

        internal void Reset()
        {
            previousPosition = null;
            velocity = Vector2.zero;
            nextJumpTime = 0f;
            landingPlatform = null;
            stallRetryAt = RetreatRetryAt = 0f;
            ResetStall();
            ClearRoute();
            failedFrom = failedTo = null;
            failedUntil = 0f;
        }

        internal void ClearRoute(bool keepLanding = false)
        {
            if (!keepLanding && routeInTransit && landingPlatform == routePlatform)
                landingPlatform = null;
            routeDestination = routeFrom = routePlatform = previousRoutePlatform = null;
            nextRoutePlan = 0f;
            routeInTransit = defensiveTransfer = false;
        }

        internal bool FollowLanding(PlayerNetworkBridge bot, out float horizontal)
        {
            horizontal = 0f;
            if (landingPlatform == null) return false;

            Bounds bounds = landingPlatform.bounds;
            float x = Mathf.Lerp(bounds.min.x, bounds.max.x, landingFraction);
            horizontal = DirectionTo(bot, x);
            if (Time.fixedTime < doubleJumpFlightEnd && doubleJumpTravel > .001f)
            {
                float t = Time.fixedTime - doubleJumpStartTime;
                float acceleration = Mathf.Max(.01f, GameGlobals.Instance.acceleration);
                float speed = GameGlobals.Instance.moveSpeed * (bot.ActiveBotBeast?.MoveSpeedMultiplier ?? 1f);
                float step = .06f;
                float x0 = Mathf.LerpUnclamped(doubleJumpStartX, x,
                    HorizontalTravel(t, doubleJumpSpeed, speed, acceleration) / doubleJumpTravel);
                float x1 = Mathf.LerpUnclamped(doubleJumpStartX, x,
                    HorizontalTravel(t + step, doubleJumpSpeed, speed, acceleration) / doubleJumpTravel);
                horizontal = Mathf.Clamp(((x1 - x0) / step + (x0 - bot.transform.position.x) * 8f) /
                    Mathf.Max(.01f, speed), -1f, 1f);
            }
            return true;
        }

        // Keep the second jump with the landing plan, even if the bot switches to recovery in midair.
        internal void ApplyPlannedAirJump(PlayerNetworkBridge bot, ref BotInputFrame input)
        {
            if (landingPlatform == null || Time.fixedTime >= doubleJumpFlightEnd || !bot.IsConscious ||
                bot.Eliminated || bot.OnFloor || bot.ActiveBotBeast?.ControlsMovement == true)
                return;

            if (FollowLanding(bot, out float steer)) input.Horizontal = steer;
            if (Time.fixedTime < secondJumpDue || bot.JumpsRemain <= 0) return;
            secondJumpDue = float.PositiveInfinity;
            nextJumpTime = Time.fixedUnscaledTime + BotTuning.JumpInterval;
            input.Jump = true;
        }

        private bool CanJump(PlayerNetworkBridge bot, Collider2D support, bool recovering) =>
            bot.JumpsRemain > 0 && bot.BotJumpSpeed > 0f && bot.BotGravity > 0f &&
            Time.fixedUnscaledTime >= nextJumpTime &&
            (recovering ? velocity.y < 0f : bot.OnFloor ||
                (support != null && Mathf.Abs(velocity.y) < 0.1f && SupportUnder(bot, bot, false) == support));

        internal static Collider2D PlatformBelow(PlayerNetworkBridge bot, Vector2 position) =>
            GroundAt(bot, position, Mathf.Max(0f, position.y - GameGlobals.Instance.boundaryY));
        
        // Determines if there is a support object under the player and if so returns its 2D collider
        internal static Collider2D SupportUnder(PlayerNetworkBridge fighter, PlayerNetworkBridge observer,
            bool allowDistantGround = true, Vector2? observedPosition = null)
        {
            Vector2 position = observedPosition ?? (Vector2)fighter.transform.position;
            var body = fighter.GetComponent<Collider2D>();
            if (body != null && body.isActiveAndEnabled)
            {
                Bounds bounds = body.bounds;
                // The body can still rest on a ledge after its center crosses the edge.
                Vector2 foot = new(bounds.center.x, bounds.min.y + 0.15f);
                foot += position - (Vector2)fighter.transform.position;
                float side = bounds.extents.x * 0.85f;
                var support = 
                    // First try finding the ground with 3 RayCasts
                    GroundAt(observer, foot, 0.3f) ??
                    GroundAt(observer, foot + Vector2.left * side, 0.3f) ??
                    GroundAt(observer, foot + Vector2.right * side, 0.3f)??
                    
                    // As a final fallback, do a box cast the full width of the character
                    GroundBoxAt(observer, foot, new Vector2(bounds.size.x * 0.9f, 0.05f), 0.3f);
                
                // Return the support collider if found
                if (support != null) return support;
            }

            return allowDistantGround ? PlatformBelow(observer, position) : null;
        }
        
        // Check for a stall before the next state reads the controller.
        internal bool IsStalled(PlayerNetworkBridge bot, bool canUnstick, out bool tryUnstick)
        {
            tryUnstick = false;
            float now = Time.fixedUnscaledTime;
            if (!bot.OnFloor || landingPlatform != null || HasNavigationRoute || now < stallRetryAt)
            {
                ResetStall();
                return false;
            }

            if (routeStalled)
            {
                routeStalled = false;
                return true;
            }

            Vector2 position = bot.transform.position;
            float movement = canUnstick ? 0.35f : 0.5f;
            if (!centerStallOrigin.HasValue || (position - centerStallOrigin.Value).sqrMagnitude >= movement * movement)
            {
                centerStallOrigin = position;
                centerStallSince = now;
            }

            float elapsed = now - centerStallSince;
            if (elapsed >= GameGlobals.Instance.botCenterAfterStall) return true;
            return tryUnstick = canUnstick && elapsed >= 1.25f;
        }

        internal void ResetStall(bool afterAttempt = false)
        {
            centerStallOrigin = null;
            escapeUntil = 0f;
            routeStalled = false;
            if (afterAttempt) stallRetryAt = Time.fixedUnscaledTime + 1f;
        }

        // The stalled state tries to get back to the middle of the map.
        internal void HeadToCenter(PlayerNetworkBridge bot, ref BotInputFrame input)
        {   
            // Captures bot position and current time
            Vector2 position = bot.transform.position;

            // 1.) Finds the center of the map that corresponds to an actual platform point
            Vector2 center = FindSpot(bot, Spot.Center, out var centerPlatform);
            
            // 2.) Determines if the bot is standing on a platform, it can walk from 
            var support = SupportUnder(bot, bot, false);
            
            // 3.) If there is no center platform or no supporting platform, then return
            if (centerPlatform == null || support == null) {
                
                
                 
                // More Thorough check for unresolved stalled states
                
                /*
                 
                string character = bot.GetComponent<PlayerVisual>().PlayerCharacter.ToString();
                var body = bot.GetComponent<Collider2D>();
                var rb = bot.GetComponent<Rigidbody2D>();
                Bounds b = body.bounds;

                var filter = new ContactFilter2D();
                filter.NoFilter();                                   // include triggers and every layer
                var hits = new RaycastHit2D[16];
                int n = Physics2D.BoxCast(new Vector2(b.center.x, b.min.y + 0.15f),
                    new Vector2(b.size.x, 0.05f), 0f, Vector2.down, filter, hits, 0.5f);

                string found = "";
                for (int i = 0; i < n; i++)
                {
                    var c = hits[i].collider;
                    if (c.attachedRigidbody == rb) continue;         // skip the bot's own collider
                    found += $"\n  {c.name} tag={c.tag} trigger={c.isTrigger} normalY={hits[i].normal.y:F2} " +
                             $"fraction={hits[i].fraction:F2} inRecoverySet={bot.BotArena.IsRecoveryGeometry(c)}";
                }

                Debug.Log($"{character} no support | centerPlatform={(centerPlatform ? centerPlatform.name : "null")} " +
                          $"OnFloor={bot.OnFloor} gravity={rb.gravityScale} vel={rb.linearVelocity} " +
                          $"bodyMinY={b.min.y:F2} bodyWidth={b.size.x:F2} | under feet:{found}");
                return;
                
                */
                
                // /*
                
                // Simple check for unresolved stall states
                
                string character = bot.GetComponent<PlayerVisual>().PlayerCharacter.ToString();
                string reason = centerPlatform == null && support == null ? "no support and no center platform"
                    : centerPlatform == null ? "no center platform"
                    : "no support";
                Debug.Log($"{character} is stalled but there is {reason}");
                
                return;
                // */
                
            }
            
            
            if (centerPlatform == support && Mathf.Abs(position.x - center.x) <= 0.25f)
                return;

            Steer(bot, center, centerPlatform, true, out float horizontal, out bool jump, useRoute: false);
            if (horizontal == 0f && !jump)
            {
                // If the safe route stalls too, make a short centerward attempt.
                float offset = center.x - position.x;
                if (Mathf.Abs(offset) <= BotTuning.HorizontalDeadZone) return;
                horizontal = Mathf.Sign(offset);
                jump = CanJump(bot, support, recovering: false);
                if (jump) nextJumpTime = Time.fixedUnscaledTime + BotTuning.JumpInterval;
            }
            input.Horizontal = horizontal;
            input.Jump = jump;
        }

        internal void Unstick(PlayerNetworkBridge bot, PlayerNetworkBridge enemy, ref BotInputFrame input)
        {
            if (!bot.IsOpponent(enemy) || landingPlatform != null || BotBrain.InPunchReach(bot, enemy)) return;

            float now = Time.fixedUnscaledTime;
            if (now < escapeUntil)
            {
                input.Horizontal = escapeDirection;
                return;
            }

            var support = SupportUnder(bot, bot, false);
            if (support == null && !bot.OnFloor) return;

            Vector2 position = bot.transform.position;
            Vector2 target = BotBrain.SeenPosition(bot, enemy);
            var destination = SupportUnder(enemy, bot, observedPosition: target);
            if (destination != support &&
                TryStartJump(bot, support, target, destination, false, out float jumpDirection))
            {
                input.Horizontal = jumpDirection;
                input.Jump = true;
                return;
            }

            // Break stalemates even when the normal route planner cannot confirm a landing.
            float offset = target.x - position.x;
            escapeDirection = Mathf.Abs(offset) > BotTuning.HorizontalDeadZone
                ? Mathf.Sign(offset) : (Random.value < 0.5f ? -1f : 1f);
            escapeUntil = now + 0.65f;
            input.Horizontal = escapeDirection;
            input.Jump = CanJump(bot, support, recovering: false);
            if (input.Jump) nextJumpTime = now + BotTuning.JumpInterval;
        }

        public static void Steer(PlayerNetworkBridge bot, Vector2 target, out float horizontal, out bool jump) =>
            Steer(bot, target, null, false, out horizontal, out jump);

        internal static void Steer(PlayerNetworkBridge bot, Vector2 target, Collider2D destination, bool allowJump,
            out float horizontal, out bool jump, bool useRoute = true)
        {
            horizontal = 0f;
            jump = false;
            var navigator = bot.GetComponent<BotInputDriver>()?.Navigation;
            if (navigator == null || bot.BotArena == null || !bot.BotArena.HasRecoveryGeometry ||
                bot.ActiveBotBeast?.ControlsMovement == true) return;
            if (useRoute)
            {
                if (destination == null || !destination.isActiveAndEnabled ||
                    (bot.OnFloor && SupportUnder(bot, bot, false) == destination))
                    navigator.ClearRoute();
                else if ((allowJump || navigator.HasNavigationRoute) &&
                    navigator.FollowRoute(bot, destination, out horizontal, out jump)) return;
            }

            if (!bot.OnFloor && navigator.FollowLanding(bot, out horizontal)) return;

            Vector2 position = bot.transform.position;
            var support = SupportUnder(bot, bot);
            bool platformChange = destination != null && destination != support;
            float desiredX = target.x;
            
            
            if (platformChange && Mathf.Abs(desiredX - position.x) <= BotTuning.HorizontalDeadZone)
            {
                Vector2 middle = FindSpot(bot, Spot.Center, out var middlePlatform);
                if (middlePlatform != null && Mathf.Abs(middle.x - position.x) > BotTuning.HorizontalDeadZone)
                {
                    desiredX = middle.x;
                    target = middle;
                    destination = middlePlatform;
                }
            }

            if (allowJump && destination != null && destination != support &&
                navigator.TryStartJump(bot, support, target, destination, false, out horizontal))
            {
                jump = true;
                return;
            }

            if (support != null)
            {
                Bounds bounds = support.bounds;
                float inset = Mathf.Min(0.35f, bounds.extents.x);
                desiredX = Mathf.Clamp(desiredX, bounds.min.x + inset, bounds.max.x - inset);
            }
            float direction = DirectionTo(bot, desiredX);
            if (direction == 0f) return;

            float speed = Mathf.Max(0f, direction * navigator.velocity.x);
            float brake = speed * speed / (2f * Mathf.Max(0.01f, GameGlobals.Instance.acceleration));
            float remaining = Mathf.Abs(desiredX - position.x);
            float lookAhead = Mathf.Max(Mathf.Min(0.75f, remaining), brake + speed * Time.fixedDeltaTime);
            Vector2 origin = position + new Vector2(direction * lookAhead, 0.15f);
            // Check the entire lookahead on the same support, so a nearby lower
            // platform does not turn a gap into a walkable edge.
            if (support != null && GroundAt(bot, origin, 1.5f) == support)
            {
                float maxSpeed = GameGlobals.Instance.moveSpeed * (bot.ActiveBotBeast?.MoveSpeedMultiplier ?? 1f);
                float approachSpeed = Mathf.Sqrt(2f * GameGlobals.Instance.acceleration * remaining);
                horizontal = direction * Mathf.Clamp01(approachSpeed / Mathf.Max(0.01f, maxSpeed));
            }
        }

        private struct RouteEdge
        {
            internal Collider2D From, To;
            internal float Launch, Landing, Duration, SecondJump;
            internal bool Drop;
        }

        // Find a sequence of feasible platform transfers, retaining only the first transfer.
        private bool PlanRoute(PlayerNetworkBridge bot, Collider2D support, Collider2D destination)
        {
            var platforms = new List<Collider2D> { support };
            var parent = new List<int> { -1 };
            var edges = new List<RouteEdge> { default };
            for (int index = 0; index < platforms.Count; index++)
            {
                var geometry = bot.BotArena.GetRecoveryGeometry();
                while (geometry.MoveNext())
                {
                    var candidate = geometry.Current;
                    if (candidate == null || !candidate.isActiveAndEnabled || platforms.Contains(candidate) ||
                        (candidate == previousRoutePlatform && candidate != destination) ||
                        (Time.fixedUnscaledTime < failedUntil && platforms[index] == failedFrom && candidate == failedTo))
                        continue;
                    // The storm cloud can carry riders, but it isn't a safe stepping stone.
                    if (candidate != destination && candidate.TryGetComponent(out MovingPlatform mover) && mover.LeavesArena)
                        continue;
                    float startX = index == 0 ? bot.transform.position.x : edges[index].Landing;
                    if (!FindTransfer(bot, platforms[index], candidate, startX, out RouteEdge edge)) continue;
                    platforms.Add(candidate);
                    parent.Add(index);
                    edges.Add(edge);
                    if (candidate != destination) continue;
                    int step = platforms.Count - 1;
                    while (parent[step] > 0) step = parent[step];
                    edge = edges[step];
                    SetTransfer(edge);
                    return true;
                }
            }
            return false;
        }

        private void SetTransfer(RouteEdge edge)
        {
            routeFrom = edge.From;
            routePlatform = edge.To;
            routeLaunchX = edge.Launch;
            routeLandingX = edge.Landing;
            routeDuration = edge.Duration;
            routeSecondJump = edge.SecondJump;
            routeDrop = edge.Drop;
            routeInTransit = false;
            routeBestProgress = float.PositiveInfinity;
            routeProgressSince = Time.fixedUnscaledTime;
        }

        private bool FindTransfer(PlayerNetworkBridge bot, Collider2D from, Collider2D to, float? startX, out RouteEdge edge,
            Func<RouteEdge, bool> suitable = null, bool jumpOnlyFromHere = false)
        {
            edge = default;
            var body = bot.GetComponent<Collider2D>();
            float halfWidth = body == null ? 0.2f : body.bounds.extents.x;
            float speed = GameGlobals.Instance.moveSpeed * (bot.ActiveBotBeast?.MoveSpeedMultiplier ?? 1f);
            float acceleration = Mathf.Max(0.01f, GameGlobals.Instance.acceleration);
            if (speed <= 0f || bot.BotJumpSpeed <= 0f || bot.BotGravity <= 0f) return false;
            Bounds a = from.bounds, b = to.bounds;
            float insetA = Mathf.Min(a.extents.x, Mathf.Max(0.05f, halfWidth));
            float insetB = Mathf.Max(0.05f, halfWidth);
            if (b.extents.x < insetB) return false;
            // Save the extra jump for transfers that a single jump cannot reach.
            int jumpOptions = !jumpOnlyFromHere && GameGlobals.Instance.maxJumps >= 2 ? 2 : 1;
            for (int jumpCount = 1; jumpCount <= jumpOptions; jumpCount++)
            for (int i = startX.HasValue ? -1 : 0; i < (jumpOnlyFromHere ? 0 : 5); i++)
            {
                float launch = i < 0 ? (jumpOnlyFromHere ? startX.Value :
                    Mathf.Clamp(startX.Value, a.min.x + insetA, a.max.x - insetA)) :
                    Mathf.Lerp(a.min.x + insetA, a.max.x - insetA, i / 4f);
                if (!TryGetSurface(bot, from, launch, out float floor) ||
                    (startX.HasValue && !HasWalkableSurface(bot, from, startX.Value, launch)))
                    continue;
                for (int j = 0; j < 5; j++)
                {
                    float landing = Mathf.Lerp(b.min.x + insetB, b.max.x - insetB, j / 4f);
                    if (!TryGetSurface(bot, to, landing, out float top)) continue;
                    float secondJump = jumpCount == 2 ? bot.BotJumpSpeed / bot.BotGravity : 0f;
                    if (!JumpFits(bot, from, to, new Vector2(launch, floor), landing, top,
                        jumpOnlyFromHere ? velocity.x : 0f, out float duration, secondJump)) continue;
                    var transfer = new RouteEdge { From = from, To = to, Launch = launch, Landing = landing,
                        Duration = duration, SecondJump = secondJump };
                    if (suitable != null && !suitable(transfer)) continue;
                    edge = transfer;
                    return true;
                }
            }
            if (jumpOnlyFromHere) return false;

            // Walk off an edge when a lower platform is reachable during a fall.
            for (int side = -1; side <= 1; side += 2)
            {
                float launch = side < 0 ? a.min.x - halfWidth - 0.1f : a.max.x + halfWidth + 0.1f;
                float edgeX = side < 0 ? a.min.x + insetA : a.max.x - insetA;
                if (!TryGetSurface(bot, from, edgeX, out float floor) ||
                    (startX.HasValue && !HasWalkableSurface(bot, from, startX.Value, edgeX)))
                    continue;
                float landing = Mathf.Clamp(launch, b.min.x + insetB, b.max.x - insetB);
                if (!TryGetSurface(bot, to, landing, out float top) || top >= floor) continue;
                float duration = Mathf.Sqrt(2f * (floor - top) / bot.BotGravity);
                if (Mathf.Abs(landing - launch) > HorizontalTravel(duration, 0f, speed, acceleration) ||
                    !ClearApproach(bot, null, to, new Vector2(launch, floor), landing, 0f, bot.BotGravity, duration)) continue;
                var transfer = new RouteEdge { From = from, To = to, Launch = launch, Landing = landing, Duration = duration, Drop = true };
                if (suitable != null && !suitable(transfer)) continue;
                edge = transfer;
                return true;
            }
            return false;
        }

        private bool FollowRoute(PlayerNetworkBridge bot, Collider2D destination, out float horizontal, out bool jump)
        {
            horizontal = 0f;
            jump = false;
            float now = Time.fixedUnscaledTime;
            var support = SupportUnder(bot, bot, false);
            if (destination != routeDestination)
            {
                if (!bot.OnFloor && FollowLanding(bot, out horizontal)) return true;
                ClearRoute();
                routeDestination = destination;
            }
            if (routePlatform != null && (routeFrom == null || !routePlatform.isActiveAndEnabled ||
                !routeFrom.isActiveAndEnabled || (!routeInTransit && !routeDrop && support != routeFrom)))
            {
                ClearRoute();
                routeDestination = destination;
            }
            if (routePlatform != null && routeInTransit && support == routePlatform && bot.OnFloor)
            {
                previousRoutePlatform = routeFrom;
                routePlatform = null;
                routeInTransit = false;
            }
            if (support == destination && bot.OnFloor)
            {
                ClearRoute();
                return false;
            }
            if (routePlatform == null)
            {
                if (now < nextRoutePlan) return true;
                if (support == null)
                {
                    return FollowLanding(bot, out horizontal);
                }
                bool found = PlanRoute(bot, support, destination);
                if (!found && previousRoutePlatform != null)
                {
                    previousRoutePlatform = null;
                    found = PlanRoute(bot, support, destination);
                }
                if (!found)
                {
                    nextRoutePlan = now + RouteRetryDelay;
                    return true;
                }
            }

            return FollowTransfer(bot, support, out horizontal, out jump);
        }

        // Execute the committed transfer until we land or stop making progress.
        private bool FollowTransfer(PlayerNetworkBridge bot, Collider2D support, out float horizontal, out bool jump)
        {
            horizontal = 0f;
            jump = false;
            float now = Time.fixedUnscaledTime;
            float positionX = bot.transform.position.x;
            float progress = Mathf.Abs(positionX - (routeInTransit ? routeLandingX : routeLaunchX));
            if (progress + 0.15f < routeBestProgress)
            {
                routeBestProgress = progress;
                routeProgressSince = now;
            }
            // A flight has its own deadline; do not mistake vertical travel for a stall.
            if ((!routeInTransit && now - routeProgressSince > 1.25f) ||
                (routeInTransit && (now > landingDeadline ||
                    (bot.OnFloor && support != null && support != routePlatform &&
                     (support != routeFrom || leftFloor)))))
            {
                routeStalled = true;
                failedFrom = routeFrom;
                failedTo = routePlatform;
                failedUntil = now + 2f;
                routePlatform = landingPlatform = null;
                routeInTransit = false;
                nextRoutePlan = now + RouteRetryDelay;
                return true;
            }
            if (routeInTransit)
            {
                leftFloor |= !bot.OnFloor;
                if (!FollowLanding(bot, out horizontal)) horizontal = DirectionTo(bot, routeLandingX);
                return true;
            }
            if (routeDrop)
            {
                if (support != routeFrom || !bot.OnFloor)
                {
                    routeInTransit = true;
                    BeginLanding(bot, routePlatform, routeLandingX, routeDuration + 0.75f,
                        out horizontal, jumped: false);
                }
                else
                {
                    // Keep walking outward until the body clears the ledge.
                    horizontal = routeLaunchX < routeFrom.bounds.center.x ? -1f : 1f;
                }
                return true;
            }
            float remaining = Mathf.Abs(positionX - routeLaunchX);
            float maxSpeed = GameGlobals.Instance.moveSpeed * (bot.ActiveBotBeast?.MoveSpeedMultiplier ?? 1f);
            float approachSpeed = Mathf.Sqrt(2f * GameGlobals.Instance.acceleration * remaining);
            horizontal = DirectionTo(bot, routeLaunchX) * Mathf.Clamp01(approachSpeed / Mathf.Max(0.01f, maxSpeed));
            if (remaining <= 0.2f && CanJump(bot, support, false) && (routeSecondJump <= 0f || bot.JumpsRemain >= 2))
            {
                // Recheck the planned jump with the speed we actually reached.
                if (support == null || support != routeFrom ||
                    !TryGetSurface(bot, routePlatform, routeLandingX, out float top) ||
                    !TryGetSurface(bot, support, positionX, out float floor) ||
                    !JumpFits(bot, support, routePlatform, new Vector2(positionX, floor), routeLandingX, top,
                        velocity.x, out float flight, routeSecondJump))
                {
                    // Let the bot settle at the launch point before giving up on the transfer.
                    return true;
                }

                routeInTransit = true;
                BeginLanding(bot, routePlatform, routeLandingX, flight + 0.75f, out horizontal);
                if (routeSecondJump > 0f)
                {
                    doubleJumpStartX = positionX;
                    doubleJumpStartTime = Time.fixedTime;
                    doubleJumpSpeed = Mathf.Sign(routeLandingX - positionX) * velocity.x;
                    doubleJumpTravel = HorizontalTravel(flight, doubleJumpSpeed, maxSpeed,
                        Mathf.Max(.01f, GameGlobals.Instance.acceleration));
                    secondJumpDue = Time.fixedTime + routeSecondJump;
                    doubleJumpFlightEnd = Time.fixedTime + flight;
                    FollowLanding(bot, out horizontal);
                }
                jump = true;
            }
            return true;
        }

        internal BotInputFrame Recover(PlayerNetworkBridge bot)
        {
            if (FollowLanding(bot, out float horizontal))
                return new BotInputFrame { Horizontal = horizontal };

            Vector2 position = bot.transform.position;
            Vector2 target = new(bot.TargetStageX, position.y);
            bool jump = TryStartJump(bot, null, target, null, true, out horizontal);
            return new BotInputFrame { Horizontal = jump ? horizontal : DirectionTo(bot, target.x), Jump = jump };
        }
        
        
        // Uses single Raycasts to find the ground platform's 2D collider below a player
        private static Collider2D GroundAt(PlayerNetworkBridge bot, Vector2 origin, float distance)
        {
            if (bot.BotArena == null || distance <= 0f) return null;
            int count = Physics2D.Raycast(origin, Vector2.down, GroundFilter, GroundHits, distance);
            for (int i = 0; i < count; i++)
                if (GroundHits[i].normal.y > 0.5f && bot.BotArena.IsRecoveryGeometry(GroundHits[i].collider))
                    return GroundHits[i].collider;
            return null;
        }
        
        // Like GroundAt, but sweeps a box instead of one ray, so a thin platform under any part of the feet is found.
        private static Collider2D GroundBoxAt(PlayerNetworkBridge bot, Vector2 origin, Vector2 size, float distance)
        {
            if (bot.BotArena == null || distance <= 0f) return null;
            int count = Physics2D.BoxCast(origin, size, 0f, Vector2.down, GroundFilter, GroundHits, distance);
            for (int i = 0; i < count; i++)
                if (GroundHits[i].fraction > 0f &&                       // ignore anything the box starts inside (like a wall beside the bot)
                    GroundHits[i].normal.y > 0.5f && bot.BotArena.IsRecoveryGeometry(GroundHits[i].collider))
                    return GroundHits[i].collider;
            return null;
        }

        private static float DirectionTo(PlayerNetworkBridge bot, float x)
        {
            float offset = x - bot.transform.position.x;
            return Mathf.Abs(offset) <= BotTuning.HorizontalDeadZone ? 0f : Mathf.Sign(offset);
        }

        private bool TryStartJump(PlayerNetworkBridge bot, Collider2D support, Vector2 target,
            Collider2D destination, bool recovering, out float horizontal)
        {
            horizontal = 0f;
            if (!CanJump(bot, support, recovering) || !TryFindLanding(bot, support, target, destination,
                out var platform, out float x, out float duration)) return false;

            BeginLanding(bot, platform, x, duration + 0.15f, out horizontal);
            return true;
        }

        private void BeginLanding(PlayerNetworkBridge bot, Collider2D platform, float x, float time,
            out float horizontal, bool jumped = true)
        {
            Bounds bounds = platform.bounds;
            landingPlatform = platform;
            landingFraction = Mathf.InverseLerp(bounds.min.x, bounds.max.x, x);
            startedAt = Time.fixedUnscaledTime;
            secondJumpDue = float.PositiveInfinity;
            doubleJumpFlightEnd = 0f;
            landingDeadline = startedAt + time;
            leftFloor = !bot.OnFloor;
            if (jumped) nextJumpTime = startedAt + BotTuning.JumpInterval;
            horizontal = DirectionTo(bot, x);
        }

        private bool TryFindLanding(PlayerNetworkBridge bot, Collider2D support, Vector2 target,
            Collider2D destination, out Collider2D platform, out float landingX, out float duration)
        {
            Vector2 position = bot.transform.position;
            float jumpSpeed = bot.BotJumpSpeed;
            float gravity = bot.BotGravity;
            float speed = GameGlobals.Instance.moveSpeed * (bot.ActiveBotBeast?.MoveSpeedMultiplier ?? 1f);
            float acceleration = Mathf.Max(0.01f, GameGlobals.Instance.acceleration);
            float bestScore = float.PositiveInfinity;
            float supportScore = support == null ? float.PositiveInfinity :
                (new Vector2(bot.ClampRecoveryX(support.bounds, target.x), support.bounds.max.y) - target).sqrMagnitude;
            bool bestIsDestination = false;
            platform = null;
            landingX = position.x;
            duration = 0f;
            if (bot.BotArena == null) return false;

            var body = bot.GetComponent<Collider2D>();
            float footOffset = body == null ? 0f : Mathf.Max(0f, position.y - body.bounds.min.y);
            float feet = position.y - footOffset;
            if (support != null && TryGetSurface(bot, support, position.x, out float floor))
                feet = Mathf.Max(feet, floor);
            var geometry = bot.BotArena.GetRecoveryGeometry();
            while (geometry.MoveNext())
            {
                var candidate = geometry.Current;
                if (candidate == null || candidate == support || !candidate.isActiveAndEnabled) continue;
                Bounds bounds = candidate.bounds;
                float inset = Mathf.Min(bounds.extents.x, Mathf.Max(0.05f, body == null ? 0f : body.bounds.extents.x));
                float left = bounds.min.x + inset;
                float right = bounds.max.x - inset;

                // Sample the actual surface at useful landing positions, including sloped platforms.
                for (int i = 0; i < 6; i++)
                {
                    float x = i == 0 ? Mathf.Clamp(target.x, left, right) :
                        i == 1 ? Mathf.Clamp(position.x, left, right) : Mathf.Lerp(left, right, (i - 2) / 3f);
                    float offset = x - position.x;
                    if (support != null && Mathf.Abs(offset) <= BotTuning.HorizontalDeadZone) continue;
                    if (!TryGetSurface(bot, candidate, x, out float top)) continue;

                    float discriminant = jumpSpeed * jumpSpeed - 2f * gravity * (top - feet);
                    if (discriminant < 0f) continue;
                    float flight = (jumpSpeed + Mathf.Sqrt(discriminant)) / gravity;
                    if (Mathf.Abs(offset) > HorizontalTravel(flight, Mathf.Sign(offset) * velocity.x, speed, acceleration))
                        continue;

                    bool isDestination = candidate == destination;
                    float score = (new Vector2(x, top) - target).sqrMagnitude;
                    if (ArenaSporeHazard.Contains(candidate.gameObject.scene, new Vector2(x, top + .8f))) score += 36f;
                    if (candidate.TryGetComponent(out MovingPlatform mover) && mover.LeavesArena) score += 81f;
                    if ((!isDestination && score >= supportScore) ||
                        (bestIsDestination && !isDestination) ||
                        (bestIsDestination == isDestination && score >= bestScore))
                        continue;
                    if (!ClearApproach(bot, support, candidate, new Vector2(position.x, feet),
                        x, jumpSpeed, gravity, flight, Mathf.Sign(offset) * velocity.x)) continue;

                    bestScore = score;
                    bestIsDestination = isDestination;
                    platform = candidate;
                    landingX = x;
                    duration = flight;
                }
            }
            return platform != null;
        }

        private static bool TryGetSurface(PlayerNetworkBridge bot, Collider2D platform, float x, out float y)
        {
            Bounds bounds = platform.bounds;
            int count = Physics2D.Raycast(new Vector2(x, bounds.max.y + 0.1f), Vector2.down,
                GroundFilter, GroundHits, bounds.size.y + 0.2f);
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (!bot.BotArena.IsRecoveryGeometry(hit.collider) || hit.normal.y <= 0.5f)
                    continue;
                y = hit.point.y;
                return hit.collider == platform && y > GameGlobals.Instance.boundaryY;
            }
            y = 0f;
            return false;
        }

        private static float HorizontalTravel(float time, float initialSpeed, float maxSpeed, float acceleration)
        {
            float accelerating = Mathf.Clamp((maxSpeed - initialSpeed) / acceleration, 0f, time);
            return initialSpeed * accelerating + 0.5f * acceleration * accelerating * accelerating +
                Mathf.Min(initialSpeed + acceleration * accelerating, maxSpeed) * (time - accelerating);
        }

        private static bool JumpFits(PlayerNetworkBridge bot, Collider2D from, Collider2D to, Vector2 feet,
            float x, float top, float currentSpeed, out float duration, float secondJump = 0f)
        {
            duration = 0f;
            float gravity = bot.BotGravity;
            float jump = bot.BotJumpSpeed;
            if (gravity <= 0f || jump <= 0f) return false;

            float secondJumpRise = jump * secondJump - .5f * gravity * secondJump * secondJump;
            float discriminant = jump * jump - 2f * gravity * (top - feet.y - secondJumpRise);
            if (discriminant < 0f) return false;
            duration = secondJump + (jump + Mathf.Sqrt(discriminant)) / gravity;
            float speed = GameGlobals.Instance.moveSpeed * (bot.ActiveBotBeast?.MoveSpeedMultiplier ?? 1f);
            float acceleration = Mathf.Max(0.01f, GameGlobals.Instance.acceleration);
            float directedSpeed = Mathf.Sign(x - feet.x) * currentSpeed;
            return Mathf.Abs(x - feet.x) <= HorizontalTravel(duration, directedSpeed, speed, acceleration) &&
                ClearApproach(bot, from, to, feet, x, jump, gravity, duration, directedSpeed, secondJump);
        }

        private static bool ClearApproach(PlayerNetworkBridge bot, Collider2D support, Collider2D destination,
            Vector2 start, float x, float jumpSpeed, float gravity, float duration, float initialSpeed = 0f, float secondJump = 0f)
        {
            // Sweep most of the fighter's body, leaving room for the feet to touch down.
            var body = bot.GetComponent<Collider2D>();
            Vector2 size = body == null ? new Vector2(0.3f, 0.6f) : (Vector2)body.bounds.size * 0.85f;
            Vector2 centerOffset = Vector2.up * (size.y * 0.5f + 0.05f);
            Vector2 previous = start + centerOffset;
            float acceleration = Mathf.Max(0.01f, GameGlobals.Instance.acceleration);
            float speed = GameGlobals.Instance.moveSpeed * (bot.ActiveBotBeast?.MoveSpeedMultiplier ?? 1f);
            float travel = HorizontalTravel(duration, initialSpeed, speed, acceleration);
            int segments = secondJump > 0f ? 16 : 8;
            for (int i = 1; i <= segments; i++)
            {
                float fraction = (float)i / segments;
                float time = duration * fraction;
                float progress = travel > 0.001f
                    ? HorizontalTravel(time, initialSpeed, speed, acceleration) / travel : fraction;
                float firstTime = secondJump > 0f ? Mathf.Min(time, secondJump) : time;
                float airTime = time - firstTime;
                Vector2 next = new(Mathf.LerpUnclamped(start.x, x, progress),
                    start.y + jumpSpeed * (firstTime + airTime) -
                    .5f * gravity * (firstTime * firstTime + airTime * airTime));
                next += centerOffset;
                Vector2 segment = next - previous;
                int count = Physics2D.BoxCast(previous, size, 0f, segment.normalized, GroundFilter, GroundHits, segment.magnitude);
                for (int hit = 0; hit < count; hit++)
                {
                    var collider = GroundHits[hit].collider;
                    if (!bot.BotArena.IsRecoveryGeometry(collider)) continue;
                    // Jump-through decks only stop a fighter falling onto their top.
                    if (collider.usedByEffector && collider.TryGetComponent(out PlatformEffector2D deck) &&
                        deck.useOneWay && (segment.y >= 0f ||
                            previous.y - centerOffset.y < collider.bounds.max.y - .06f))
                        continue;
                    if (collider == support && i == 1 && GroundHits[hit].fraction == 0f) continue;
                    if (collider == destination && i == segments && GroundHits[hit].normal.y > 0.5f) continue;
                    return false;
                }
                previous = next;
            }
            return true;
        }

        // Look just past the ledge so we don't aim at a platform that catches the fall.
        internal static bool TryEdgePosition(PlayerNetworkBridge bot, Collider2D platform, Vector2 opponent,
            float side, float spacing, out Vector2 position)
        {
            position = default;
            Bounds bounds = platform.bounds;
            float edge = side < 0f ? bounds.min.x : bounds.max.x;
            var body = bot.GetComponent<Collider2D>();
            float inset = Mathf.Max(0.35f, body == null ? 0f : body.bounds.extents.x);
            if (bounds.size.x <= 2f * inset ||
                PlatformBelow(bot, new Vector2(edge + side * inset, bounds.max.y + 0.1f)) != null)
                return false;

            float x = Mathf.Clamp(opponent.x - side * spacing, bounds.min.x + inset, bounds.max.x - inset);
            if ((opponent.x - x) * side < 0.25f || !TryGetSurface(bot, platform, x, out float top))
                return false;

            position = new Vector2(x, top);
            return true;
        }

        private static bool DefensiveLandingSafer(PlayerNetworkBridge bot, PlayerNetworkBridge threat,
            Collider2D platform, float x)
        {
            if (threat == null || !TryGetSurface(bot, platform, x, out float top)) return false;
            Vector2 origin = bot.transform.position;
            Vector2 danger = BotBrain.SeenPosition(bot, threat);
            var body = bot.GetComponent<Collider2D>();
            float footOffset = body == null ? 0.6f : Mathf.Max(0f, origin.y - body.bounds.min.y);
            Vector2 goal = new(x, top + footOffset);
            if (ArenaSporeHazard.Contains(platform.gameObject.scene, goal)) return false;
            return BotBrain.DefensiveClearance(bot, goal, goal.x) >
                    BotBrain.DefensiveClearance(bot, origin, origin.x) + 0.5f &&
                (goal - danger).sqrMagnitude > (origin - danger).sqrMagnitude + 0.5f;
        }

        // Keep an escape jump or drop until it lands. Only look for another one after it ends.
        internal bool TryDefensiveTransfer(PlayerNetworkBridge bot, PlayerNetworkBridge threat, out BotInputFrame input)
        {
            input = default;
            var support = SupportUnder(bot, bot, false);
            if (defensiveTransfer)
            {
                if (routePlatform != null && routePlatform.isActiveAndEnabled &&
                    !(routeInTransit && leftFloor && bot.OnFloor && support == routePlatform) &&
                    (routeInTransit || (support == routeFrom && DefensiveLandingSafer(bot, threat,
                        routePlatform, routeLandingX))))
                {
                    FollowTransfer(bot, support, out input.Horizontal, out input.Jump);
                    if (HasNavigationRoute) return true;

                    RetreatRetryAt = Time.fixedUnscaledTime + RouteRetryDelay;
                }
                ClearRoute();
            }

            if (!bot.OnFloor) return FollowLanding(bot, out input.Horizontal);
            if (support == null || threat == null || bot.BotArena == null ||
                Time.fixedUnscaledTime < RetreatRetryAt) return false;

            Vector2 origin = bot.transform.position;
            Vector2 danger = BotBrain.SeenPosition(bot, threat);
            var body = bot.GetComponent<Collider2D>();
            var threatBody = threat.GetComponent<Collider2D>();
            float bodyHeight = body == null ? 0.6f : body.bounds.extents.y;
            float threatHeight = threatBody == null ? 0.6f : threatBody.bounds.extents.y;
            float clearance = BotBrain.DefensiveClearance(bot, origin, origin.x);
            bool SaferLanding(RouteEdge edge) => DefensiveLandingSafer(bot, threat, edge.To, edge.Landing);

            // Hop past a nearby threat only if we can clear its attack height.
            bool PastThreat(RouteEdge edge)
            {
                float offset = edge.Landing - origin.x;
                float cross = (danger.x - origin.x) / offset;
                if (float.IsNaN(cross) || cross <= 0f || cross >= 1f ||
                    Mathf.Abs(offset) < BotTuning.HorizontalDeadZone || !SaferLanding(edge))
                    return false;

                float speed = GameGlobals.Instance.moveSpeed;
                float accel = Mathf.Max(0.01f, GameGlobals.Instance.acceleration);
                float initialSpeed = Mathf.Sign(offset) * velocity.x;
                float total = HorizontalTravel(edge.Duration, initialSpeed, speed, accel);
                float lo = 0f, hi = edge.Duration;
                for (int i = 0; i < 6; i++)
                {
                    float t = (lo + hi) * 0.5f;
                    if (HorizontalTravel(t, initialSpeed, speed, accel) < cross * total) lo = t;
                    else hi = t;
                }

                float feet = origin.y - bodyHeight + bot.BotJumpSpeed * hi - 0.5f * bot.BotGravity * hi * hi;
                float attackClearance = Mathf.Max(threatHeight, BotTuning.PunchHeightTolerance);
                return feet > danger.y + attackClearance + 0.15f;
            }

            RouteEdge selected = default;
            if (CanJump(bot, support, false) &&
                Mathf.Abs(danger.y - origin.y) <= bodyHeight + threatHeight &&
                FindTransfer(bot, support, support, origin.x, out selected, PastThreat, jumpOnlyFromHere: true))
            {
                SetTransfer(selected);
            }
            else
            {
                // Prefer a different height, and only walk to a ledge as part of a planned drop.
                float best = float.PositiveInfinity;
                var geometry = bot.BotArena.GetRecoveryGeometry();
                while (geometry.MoveNext())
                {
                    var candidate = geometry.Current;
                    if (candidate == null || candidate == support || !candidate.isActiveAndEnabled ||
                        (failedFrom == support && failedTo == candidate && Time.fixedUnscaledTime < failedUntil))
                        continue;

                    bool SafeTransfer(RouteEdge edge)
                    {
                        float launch = bot.ClampRecoveryX(support.bounds, edge.Launch);
                        return SaferLanding(edge) && HasWalkableSurface(bot, support, launch) &&
                            BotBrain.DefensiveClearance(bot, origin, launch) >= clearance;
                    }

                    if (!FindTransfer(bot, support, candidate, origin.x, out var edge, SafeTransfer) ||
                        (!edge.Drop && !CanJump(bot, support, false))) continue;
                    float cost = Mathf.Abs(edge.Launch - origin.x) + edge.Duration;
                    if (Mathf.Abs(candidate.bounds.max.y - support.bounds.max.y) < 0.4f) cost += 2f;
                    if (cost >= best) continue;
                    best = cost;
                    selected = edge;
                }
                if (selected.To == null)
                {
                    RetreatRetryAt = Time.fixedUnscaledTime + RouteRetryDelay;
                    return false;
                }
                SetTransfer(selected);
            }

            defensiveTransfer = true;
            FollowTransfer(bot, support, out input.Horizontal, out input.Jump);
            return HasNavigationRoute;
        }

        internal static bool TryWalkOnSupport(PlayerNetworkBridge bot, Collider2D support, float x,
            out BotInputFrame input)
        {
            input = default;
            if (!HasWalkableSurface(bot, support, x)) return false;

            Steer(bot, new Vector2(x, bot.transform.position.y), support, false,
                out input.Horizontal, out input.Jump, useRoute: false);
            return input.Horizontal != 0f;
        }

        private static bool HasWalkableSurface(PlayerNetworkBridge bot, Collider2D support, float x) =>
            HasWalkableSurface(bot, support, bot.transform.position.x, x);

        private static bool HasWalkableSurface(PlayerNetworkBridge bot, Collider2D support, float from, float x)
        {
            int steps = Mathf.CeilToInt(Mathf.Abs(x - from) / 0.5f);
            for (int i = 1; i <= steps; i++)
                if (!TryGetSurface(bot, support, Mathf.Lerp(from, x, (float)i / steps), out _))
                    return false;

            return true;
        }

        internal bool TrySteerToPosition(PlayerNetworkBridge bot, Vector2 position, Collider2D platform,
            out BotInputFrame input)
        {
            input = default;
            if (bot.OnFloor && SupportUnder(bot, bot, false) == platform)
            {
                ClearRoute();
                return TryWalkOnSupport(bot, platform, position.x, out input);
            }

            if (platform == null || !platform.isActiveAndEnabled || bot.BotArena == null ||
                !FollowRoute(bot, platform, out input.Horizontal, out input.Jump)) return false;
            return HasNavigationRoute || input.Horizontal != 0f || input.Jump;
        }

        internal static Vector2 FindSpot(PlayerNetworkBridge bot, Spot spot, out Collider2D platform)
        {
            Vector2 position = bot.transform.position;
            platform = null;
            if (bot.BotArena == null) return position;

            Bounds stage = default;
            bool found = false;
            var geometry = bot.BotArena.GetRecoveryGeometry();
            while (geometry.MoveNext())
            {
                var collider = geometry.Current;
                if (collider == null || !collider.isActiveAndEnabled) continue;
                if (collider.bounds.max.y <= GameGlobals.Instance.boundaryY) continue;
                if (found) stage.Encapsulate(collider.bounds);
                else { stage = collider.bounds; found = true; }
            }
            if (!found) return position;

            Vector2 goal = stage.center;
            if (spot is Spot.Left or Spot.LowerLeft or Spot.UpperLeft) goal.x = stage.min.x;
            if (spot is Spot.Right or Spot.LowerRight or Spot.UpperRight) goal.x = stage.max.x;
            if (spot is Spot.Bottom or Spot.LowerLeft or Spot.LowerRight) goal.y = stage.min.y;
            if (spot is Spot.Top or Spot.UpperLeft or Spot.UpperRight) goal.y = stage.max.y;

            float best = float.PositiveInfinity;
            Vector2 target = position;
            geometry = bot.BotArena.GetRecoveryGeometry();
            while (geometry.MoveNext())
            {
                var collider = geometry.Current;
                if (collider == null || !collider.isActiveAndEnabled) continue;
                if (collider.TryGetComponent(out MovingPlatform mover) && mover.LeavesArena) continue;
                Bounds bounds = collider.bounds;
                if (bounds.max.y <= GameGlobals.Instance.boundaryY) continue;
                float x = bot.ClampRecoveryX(bounds, goal.x);
                if (!TryGetSurface(bot, collider, x, out float top)) continue;
                Vector2 point = new(x, top);
                float score = (point - goal).sqrMagnitude;
                if (ArenaSporeHazard.Contains(collider.gameObject.scene, point + Vector2.up * .8f)) score += 36f;
                if (score >= best) continue;
                best = score;
                target = point;
                platform = collider;
            }
            return target;
        }
    }
}
