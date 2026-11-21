using Player.Bots;
using UnityEngine;

// Runs on the bot's worker client or on the Editor host for host-owned bots.
// It sends controller input through NGO. Only the server simulates physics.
[RequireComponent(typeof(PlayerNetworkBridge))]

public sealed class BotInputDriver : MonoBehaviour
{
    private PlayerNetworkBridge bridge;
    private PlayerVisual visual;
    private PlayerPunch punch;
    private IBotState state = new IdleBotState();
    private float nextInputTime;
    private float nextTraceTime;
    private float tracePeriod = 1.0f; // Traces states, stalls, inputs, and other actions at this period

    private readonly ChaseBotState.Budget chaseBudget = new();
    internal bool ChaseOnCooldown => chaseBudget.OnCooldown;

    private bool wasPlaying; // Check for whether a round was being played in the previous frame
    internal EdgePositionBotState.Attempts EdgeAttempts { get; } = new();
    internal BotNavigator Navigation { get; } = new();
    internal BotCombatMemory CombatMemory { get; } = new();

    private void Awake()
    {
        bridge = GetComponent<PlayerNetworkBridge>();
        visual = GetComponent<PlayerVisual>();
        punch = GetComponent<PlayerPunch>();
    }
    
    // Core bot loop fixed update is 50Hz while bot inputs are roughly 16 - 17 Hz
    private void FixedUpdate()
    {   
        // Ensures the bot is spawned in the network and is owned by a worker (or host when playing in an Editor) client,
        // not a player client
        if (!bridge.IsSpawned || !bridge.CanDriveBotInput) return;
        
        // End-of-round cleanup. GameplayAllowed determines whether a round is being fought
        if (!bridge.GameplayAllowed)
        {   
            // If gameplay ends but the previous tick was an active round, reset data and turn off wasPlaying
            if (wasPlaying)
            {
                Navigation.Reset();
                CombatMemory.Reset();
                chaseBudget.Reset();
                EdgeAttempts.Reset();
                state = new IdleBotState();
                wasPlaying = false;
            }
            return;
        }
        
        if (!wasPlaying)
        {
            chaseBudget.StartRound(GameGlobals.Instance, visual.PlayerCharacter);
            CombatMemory.ObserveConfirmedSwings(punch == null ? 0u : punch.ConfirmedBotSwings);
            CombatMemory.StartKite();
            wasPlaying = true;
        }
        else
        {
            // The current state owned the elapsed physics step.
            chaseBudget.Advance(GameGlobals.Instance, visual.PlayerCharacter, state is ChaseBotState { IsPursuing: true }, Time.fixedDeltaTime);
        }
        
        // Captures all other spawned and non-eliminated player's position data into a dictionary <PlayerNetworkBridge, Sight> each physics frame
        CombatMemory.Capture(bridge);
        
        // If the physics time is less than the nextInputTime then return
        if (Time.fixedUnscaledTime < nextInputTime && !(ChaseOnCooldown && state is ChaseBotState)) return;
        
        // Schedule the next input time 50ms into the future (actually runs every 60ms since it the physics time updates every 20ms.
        // So bot inputs run at roughly 16-17Hz
        nextInputTime = Time.fixedUnscaledTime + 0.05f;
        Navigation.Observe(bridge);
        CombatMemory.UpdateVision();
        CombatMemory.ObserveConfirmedSwings(punch == null ? 0u : punch.ConfirmedBotSwings);
        // Look at the same opponent when picking a state, steering, and reacting.
        CombatMemory.Target = BotBrain.SelectTarget(bridge, CombatMemory.Target, (state as EdgePositionBotState)?.Target);
        CombatMemory.Observe(bridge, CombatMemory.Target);
        
        // Update the Bot State based on an initial reflex check or the passed NextState from the current state 
        var nextState = BotReflexes.Check(bridge, state) ?? state.NextState(bridge);
        if (nextState != state)
        {
            if (state is StalledBotState) Navigation.ResetStall(afterAttempt: true);
            if (state is EdgePositionBotState edge) edge.Leave(EdgeAttempts);
            if (nextState is EdgePositionBotState positioning) CombatMemory.Target = positioning.Target;
            Navigation.ClearRoute(keepLanding: !bridge.OnFloor);
        }
        state = nextState;
        
        // Read input from the updated bot state
        BotInputFrame input = state.ReadController(bridge);
        Navigation.ApplyPlannedAirJump(bridge, ref input);


        // Simple bot trace for checking behavior
        if (Time.fixedUnscaledTime >= nextTraceTime && bridge.IsConscious)
        {
            nextTraceTime = Time.fixedUnscaledTime + tracePeriod;
            Debug.Log($"{visual.PlayerCharacter} | {state.GetType().Name} | stalled={state is StalledBotState}");
        }
        
        // Helps bots commit to landing spots when jumping and recovering from falls
        /*
        if (bridge.IsConscious && Navigation.FollowLanding(bridge, out float landingHorizontal))
        {
            if (!input.Jump) input.Horizontal = landingHorizontal;
        }
        */
        
        // Prevents movement changes while in beast mode. Most likely not needed unless new beasts need to restrict movement
        /*
        if (bridge.ActiveBotBeast?.ControlsMovement == true &&
            !(state is BeastAttackBotState && bridge.ActiveBotBeast is EagleBeast))
            input.Horizontal = 0f;
        */
        
        bridge.DriveBotInput(input); // Submits the definitive bot input for this bot input frame
        
    }
}
