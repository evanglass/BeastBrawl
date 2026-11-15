using UnityEngine;

public class LANConnect : MonoBehaviour
{
    [SerializeField]
    private GameObject lanButton;

    [SerializeField]
    LANController lan;

    void Awake()
    {
        if (Application.platform == RuntimePlatform.WebGLPlayer && lanButton != null)
            lanButton.SetActive(false);

        lan = FindObjectOfType<LANController>();
    }

    void HostGame()
    {
        lan.HostGame();
    }

    void FindGames()
    {
    }

    void JoinGame()
    {
    }

    void JoinIP(string ipAddress)
    {
        //lan.JoinGame(ipAddress);
    }
}
