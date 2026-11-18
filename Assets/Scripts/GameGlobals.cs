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
