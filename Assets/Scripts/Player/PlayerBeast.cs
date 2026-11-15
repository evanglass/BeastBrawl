using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBeast : NetworkBehaviour
{
    private PlayerNetworkBridge bridge;
    private PlayerInput playerInput;
    private PlayerState playerState;
    
    private float beastTimer = 0f;

    [SerializeField]
    private bool isBeast = false;
    public bool IsBeast => isBeast;

    private void Awake()
    {
        bridge = GetComponent<PlayerNetworkBridge>();
        playerInput = GetComponent<PlayerInput>();
        playerState = GetComponent<PlayerState>();
    }

    private void Update()
    {
        if (!IsClient || !IsOwner || !IsSpawned || !playerInput.enabled)
            return;

        bool beast = playerInput.actions["Beast"].WasPressedThisFrame();

        if (beast)
        {
            bridge.RequestBeastServerRpc();
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
            return;

        bool wantsBeast = bridge.ConsumeBeastRequest();

        if (isBeast)
        {
            beastTimer -= Time.fixedDeltaTime;

            if (beastTimer <= 0f)
            {
                isBeast = false;
                playerState.LeashBeast();
            }

            return;
        }

        if (!wantsBeast)
            return;

        if (!playerState.IsConscious || playerState.Eliminated)
            return;

        if (playerState.BeastMeter == 100f)
        {
            isBeast = true;
            playerState.UnleashBeast();
            beastTimer = GameGlobals.Instance.beastDuration;
        }
    }

    public void Reset()
    {
        isBeast = false;
        beastTimer = 0f;
    }
}
