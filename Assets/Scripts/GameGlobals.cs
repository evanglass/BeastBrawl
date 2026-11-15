using System;
using System.IO;
using UnityEngine;

public class GameGlobals : MonoBehaviour
{
    [Header("Consciousness")]
    public float consciousnessRecoveryRate = 5f;
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
    public Vector2 punchKnockback = new Vector2(20, 40);
    public float beastMeterGainRate = 5f;
    public float beastMeterGainPerHit = 5f;
    public float beastDuration = 5f;

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
