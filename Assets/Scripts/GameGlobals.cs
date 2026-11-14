using UnityEngine;

public class GameGlobals : MonoBehaviour
{
    public float consciousnessRecoveryRate = 5f;
    public float consciousnessLossMultiplier = 2f;

    public static GameGlobals Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }
}
