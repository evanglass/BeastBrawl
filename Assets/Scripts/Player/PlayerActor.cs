using UnityEngine;

public class PlayerActor : MonoBehaviour
{
    private PlayerNetworkBridge bridge;
    private float nextEnvironmentalTick;
    [SerializeField]
    private bool serverSimulated;

    public bool ServerSimulated
    {
        get => serverSimulated;
        set => serverSimulated = value;
    }

    [SerializeField]
    private PlayerState playerState;

    void Awake()
    {
        playerState = GetComponent<PlayerState>();
        bridge = GetComponent<PlayerNetworkBridge>();
    }

    internal void ApplyEnvironmentalExposure(float amount, float interval)
    {
        if (!serverSimulated || !bridge.GameplayAllowed || playerState.Eliminated ||
            !playerState.IsConscious || Time.fixedTime < nextEnvironmentalTick) return;

        nextEnvironmentalTick = Time.fixedTime + interval;
        playerState.ReduceConsciousness(amount);
    }

    private void FixedUpdate()
    {
        if (!serverSimulated || playerState.Eliminated || !bridge.GameplayAllowed)
        {
            nextEnvironmentalTick = 0f;
            return;
        }

        if (transform.position.y < GameGlobals.Instance.boundaryY || ArenaRingOutCeiling.IsAbove(transform.position.y))
        {
            playerState.Eliminate();
        }

        playerState.TickState(Time.fixedDeltaTime);
    }
}
