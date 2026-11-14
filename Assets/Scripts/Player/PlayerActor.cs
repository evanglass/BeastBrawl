using UnityEngine;

public class PlayerActor : MonoBehaviour
{
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
    }

    private void FixedUpdate()
    {
        if (!serverSimulated)
            return;

        playerState.ServerTickRecovery(Time.fixedDeltaTime);
    }
}
