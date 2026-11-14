using UnityEngine;

public class PlayerState : MonoBehaviour
{
    [SerializeField]
    private float consciousness = 100f;

    [SerializeField]
    private int timesLostConsciousness = 0;

    [SerializeField]
    private bool isConscious = true;
    public bool IsConscious => isConscious;

    [SerializeField]
    private bool eliminated = false;
    public bool Eliminated => eliminated;

    public void ServerTickRecovery(float deltaTime)
    {
        GainConsciousness(deltaTime * GameGlobals.Instance.consciousnessRecoveryRate);
        if (consciousness >= 100f)
        {
            consciousness = 100f;
            if (!isConscious)
            {
                isConscious = true;
            }
        }
    }

    public void GainConsciousness(float amount)
    {
        consciousness += amount;
        if (consciousness >= 100f)
        {
            consciousness = 100f;
            if (!isConscious)
            {
                isConscious = true;
            }
        }

    }

    public void Punch(float amount)
    {
        if (!isConscious)
        {
            return;
        }

        float consciousnessMultiplier = 1 + timesLostConsciousness * GameGlobals.Instance.consciousnessLossMultiplier;
        consciousness -= amount * consciousnessMultiplier;
        if (consciousness <= 0f)
        {
            consciousness = 0f;
            timesLostConsciousness++;
            isConscious = false;
        }
    }

    public void Eliminate()
    {
        eliminated = true;
    }

    public void Reset()
    {
        consciousness = 100f;
        timesLostConsciousness = 0;
        isConscious = true;
        eliminated = false;
    }
}
