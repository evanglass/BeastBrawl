using Unity.Netcode;
using UnityEngine;

public class PlayerVisual : NetworkBehaviour
{
    public enum Character
    {
        None,
        Eagle,
        Bear,
        Cougar,
        Snake
    }

    public Character PlayerCharacter { get; private set; } = Character.None;

    public void AssignCharacter(int characterIndex)
    {
        PlayerCharacter = (Character)characterIndex;
    }
}
