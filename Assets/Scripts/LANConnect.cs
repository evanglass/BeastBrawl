using UnityEngine;
using UnityEngine.UI;

public class LANConnect : MonoBehaviour
{
    [SerializeField]
    private GameObject lanButton;

    [SerializeField]
    private InputField addressInput;

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

    public void JoinGame(LANController.LANGameInfo game)
    {
        lan.JoinGame(game.ipAddress, game.port);
    }

    public void JoinManual()
    {
        JoinIP(addressInput.text);
    }

    public void JoinIP(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            Debug.LogWarning("Enter a host address.");
            return;
        }

        string address = ipAddress.Trim();
        int separator = -1;
        for (int i = address.Length - 1; i >= 0; i--)
        {
            if (address[i] == ']')
                break;
            if (address[i] == ':')
            {
                separator = i;
                break;
            }
        }

        if (!address.StartsWith("[") && address.IndexOf(':') != separator)
        {
            separator = -1;
        }

        if (separator < 0)
        {
            if (address.StartsWith("[") && address.EndsWith("]"))
                address = address.Substring(1, address.Length - 2);
            lan.JoinGame(address);
            return;
        }

        if (!ushort.TryParse(address.Substring(separator + 1), out ushort gamePort) || gamePort == 0)
        {
            Debug.LogWarning("Enter a valid port between 1 and 65535.");
            return;
        }

        string host = address.Substring(0, separator).Trim();
        if (host.StartsWith("[") && host.EndsWith("]"))
            host = host.Substring(1, host.Length - 2);
        lan.JoinGame(host, gamePort);
    }
}
