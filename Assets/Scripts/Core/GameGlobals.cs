using System;
using System.IO;
using UnityEngine;

public class GameGlobals : MonoBehaviour
{
    [Header("Consciousness")]
    public float consciousnessRecoveryRate = 5f;
    public float consciousnessRecoveryPerPress = 2f;
    public float consciousnessLossMultiplier = 2f;
    public float boundaryY = -10f;

    [Header("Movement")]
    public float moveSpeed = 7f;
    public float acceleration = 60f;
    public float jumpForce = 10f;
    public float gravityScale = 5f;
    public float wallSlidingSpeed = 0.5f;
    public float wallJumpForce = 5f;
    public int maxJumps = 2;

    [Header("Damage")]
    public float punchDamage = 20f;
    public float punchCooldown = 1f;
    public float punchReach = 1f;
    public Vector2 punchKnockback = new Vector2(20, 40);
    public Vector2 unconsciousPunchKnockbackMultiplier = Vector2.one;
    public float beastMeterGainRate = 5f;
    public float beastMeterGainPerHit = 5f;
    public float beastDuration = 5f;

    [Header("Simulation")]
    public bool simulationMode;

    [Header("Bot Combat")]
    public Vector2 botReactionDelay = new(0.1f, 0.3f);
    public Vector2 botPunchCooldownExtra = new(0f, 0.2f);
    public Vector2 botPunchReachMultiplier = new(0.8f, 1.05f);
    public Vector2 botVisionDelay = new(0.1f, 0.15f);
    [Min(0f)] public float botCenterAfterStall = 3f;

    [Header("Bot Chase")]
    [Min(0f)] public float ChaseDurationMin = 4f;
    [Min(0f)] public float ChaseDurationMax = 10f;
    [Min(0f)] public float ChaseCooldownMin = 2f;
    [Min(0f)] public float ChaseCooldownMax = 6f;

    [Min(0f)] public float EagleAggressionMultiplier = 1f;
    [Min(0f)] public float BearAggressionMultiplier = 1f;
    [Min(0f)] public float CougarAggressionMultiplier = 1f;
    [Min(0f)] public float SnakeAggressionMultiplier = 1f;

    [Min(0f)] public float HawkAggressionMultiplier = 1f;
    [Min(0f)] public float BlackBearAggressionMultiplier = 1f;
    [Min(0f)] public float PantherAggressionMultiplier = 1f;
    [Min(0f)] public float MambaAggressionMultiplier = 1f;

    internal float GetAggressionMultiplier(PlayerVisual.Character character)
    {
        float multiplier = character switch
        {
            PlayerVisual.Character.Eagle => EagleAggressionMultiplier,
            PlayerVisual.Character.Bear => BearAggressionMultiplier,
            PlayerVisual.Character.Cougar => CougarAggressionMultiplier,
            PlayerVisual.Character.Snake => SnakeAggressionMultiplier,
            PlayerVisual.Character.Hawk => HawkAggressionMultiplier,
            PlayerVisual.Character.BlackBear => BlackBearAggressionMultiplier,
            PlayerVisual.Character.Panther => PantherAggressionMultiplier,
            PlayerVisual.Character.Mamba => MambaAggressionMultiplier,
            _ => 1f
        };
        return multiplier > 0f && !float.IsInfinity(multiplier) ? multiplier : 1f;
    }

    [Header("Eagle")]
    public float eagleBoomRange = 35f;
    public float eagleBoomHalfAngle = 15f;
    public float eagleCornerSpeed = 15f;
    public float eagleAimSpeed = 90f;
    public float eagleBoomKnockback = 30f;
    public float unconsciousEagleKnockbackMultiplier = 1f;
    public float eagleFlightSpeed = 10f;
    public float eagleFlightDuration = 2f;

    [Header("Bear")]
    public float bearMoveSpeedMultiplier = 0.5f;
    public float bearPunchDamageMultiplier = 3f;

    [Header("Cougar")]
    public float cougarMoveSpeedMultiplier = 3f;
    public float cougarPunchCooldownMultiplier = 0.1f;

    [Header("Snake")]
    public float snakeBiteDelay = 1.5f;

    [Header("Hawk")]
    public float hawkBoomRange = 35f;
    public float hawkBoomHalfAngle = 15f;
    public float hawkCornerSpeed = 15f;
    public float hawkAimSpeed = 90f;
    public float hawkBoomKnockback = 30f;
    public float unconsciousHawkKnockbackMultiplier = 1f;
    public float hawkFlightSpeed = 10f;
    public float hawkFlightDuration = 2f;

    [Header("Black Bear")]
    public float blackBearMoveSpeedMultiplier = 0.5f;
    public float blackBearPunchDamageMultiplier = 3f;

    [Header("Panther")]
    public float pantherMoveSpeedMultiplier = 3f;
    public float pantherPunchCooldownMultiplier = 0.1f;

    [Header("Mamba")]
    public float mambaBiteDelay = 1.5f;

    internal float SnakeBiteDelayFor(PlayerVisual.Character character) =>
        character == PlayerVisual.Character.Mamba ? mambaBiteDelay : snakeBiteDelay;

    public static GameGlobals Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

#if UNITY_SERVER && !UNITY_EDITOR
        LoadServerConfiguration();
#endif
    }

#if UNITY_SERVER && !UNITY_EDITOR
    private void LoadServerConfiguration()
    {
        string configPath = Environment.GetEnvironmentVariable("FERAL_GAME_CONFIG");
        if (string.IsNullOrWhiteSpace(configPath))
            configPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "gameglobals.json");

        if (!File.Exists(configPath))
        {
            Debug.Log($"No game configuration found at {configPath}; using scene values.");
            return;
        }

        try
        {
            JsonUtility.FromJsonOverwrite(File.ReadAllText(configPath), this);
            Debug.Log($"Loaded game configuration from {configPath}.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Could not load game configuration from {configPath}: {exception.Message}");
        }
    }
#endif
}
