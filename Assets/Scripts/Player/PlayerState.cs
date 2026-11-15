using UnityEngine;

public class PlayerState : MonoBehaviour
{
    [SerializeField]
    private float beastMeter = 0f;
    public float BeastMeter => beastMeter;

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

    private bool shouldRechargeBeastMeter = true;

    public void TickState(float deltaTime)
    {
        if (shouldRechargeBeastMeter)
            beastMeter = Mathf.Clamp(beastMeter + deltaTime * GameGlobals.Instance.beastMeterGainRate, 0f, 100f);

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

        if (shouldRechargeBeastMeter)
            beastMeter = Mathf.Clamp(beastMeter + GameGlobals.Instance.beastMeterGainPerHit, 0f, 100f);
    }

    public void Eliminate()
    {
        eliminated = true;
    }

    public void UnleashBeast()
    {
        beastMeter = 0f;
        shouldRechargeBeastMeter = false;
    }

    public void LeashBeast()
    {
        shouldRechargeBeastMeter = true;
    }

    public void Reset()
    {
        consciousness = 100f;
        timesLostConsciousness = 0;
        isConscious = true;
        eliminated = false;
        beastMeter = 0f;
        shouldRechargeBeastMeter = true;
    }
}
