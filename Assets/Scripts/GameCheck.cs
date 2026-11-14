using Unity.Netcode;
using UnityEngine;

public class GameCheck : MonoBehaviour
{
    [SerializeField]
    private Transform winUI;
    [SerializeField]
    private Transform loseUI;
    [SerializeField]
    private Transform drawUI;

    [SerializeField]
    private RoundManager roundManager;

    // Update is called once per frame
    void Update()
    {
        if (!roundManager.IsSpawned || !roundManager.IsClient)
            return;

        int winner = roundManager.WinnerId;

        winUI.gameObject.SetActive(
            winner >= 0 && winner == (int)NetworkManager.Singleton.LocalClientId
        );

        loseUI.gameObject.SetActive(
            winner >= 0 && winner != (int)NetworkManager.Singleton.LocalClientId
        );

        drawUI.gameObject.SetActive(winner == -1);

    }
}
