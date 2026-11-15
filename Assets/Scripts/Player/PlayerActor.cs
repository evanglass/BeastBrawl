using UnityEngine;

public class PlayerActor : MonoBehaviour
{
    private PlayerNetworkBridge bridge;
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

    private void FixedUpdate()
    {
        if (!serverSimulated || playerState.Eliminated || !bridge.GameplayAllowed)
            return;

        if (transform.position.y < GameGlobals.Instance.boundaryY)
        {
            playerState.Eliminate();
        }

        playerState.TickState(Time.fixedDeltaTime);
    }
}
