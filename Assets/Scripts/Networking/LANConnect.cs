using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class LANConnect : MonoBehaviour
{
    [SerializeField]
    private GameObject lanButton;

    [SerializeField]
    private GameObject quitButton;

    [SerializeField]
    private InputField addressInput;

    [SerializeField]
    private InputField playerNameInput;

    [SerializeField]
    private InputField onlineNameInput;

    [SerializeField]
    private Text onlineStatusText;

    [SerializeField]
    private GameObject onlineMenu;

    [SerializeField]
    private GameObject mainMenu;

    [SerializeField]
    private Text statusText;

    [SerializeField]
    private GameObject lanMenu;

    [SerializeField]
    private GameObject joinMenu;

    [SerializeField]
    private Transform gameListContent;

    [SerializeField]
    private GameObject gameEntryTemplate;

    [SerializeField]
    LANController lan;

    private NetworkManager networkManager;
    private bool connectingOnline;

    public GameObject LocalMainMenu => mainMenu;
    public InputField LocalNameTemplate => onlineNameInput;

    void Awake()
    {
        if (Application.platform == RuntimePlatform.WebGLPlayer && lanButton != null)
            lanButton.SetActive(false);

        if (Application.platform == RuntimePlatform.WebGLPlayer && quitButton != null)
            quitButton.SetActive(false);

        lan = FindFirstObjectByType<LANController>();
        if (lan != null)
        {
            playerNameInput.text = lan.PlayerName;
            onlineNameInput.text = lan.PlayerName;
        }

        networkManager = NetworkManager.Singleton;
        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback += OnOnlineConnected;
            networkManager.OnClientDisconnectCallback += OnOnlineDisconnected;
        }
    }

    private void OnDestroy()
    {
        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback -= OnOnlineConnected;
        networkManager.OnClientDisconnectCallback -= OnOnlineDisconnected;
    }

    public void OpenOnline()
    {
#if UNITY_EDITOR
        if (lan != null && lan.IsLocalEditorSession)
        {
            lan.OpenLocalPlayMenu();
            return;
        }
#endif
        if (lan != null)
        {
            var custom = lan.GetComponent<LocalCustomGameUI>();
            if (custom == null) custom = lan.gameObject.AddComponent<LocalCustomGameUI>();
            custom.Open(false);
            return;
        }
        OpenQuickOnline();
    }

    internal void OpenQuickOnline()
    {
        onlineNameInput.text = lan != null ? lan.PlayerName : "";
        onlineStatusText.text = "";
        mainMenu.SetActive(false);
        onlineMenu.SetActive(true);
    }

    internal void OpenLanMenu()
    {
        mainMenu.SetActive(false);
        lanMenu.SetActive(true);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void JoinOnline()
    {
        if (connectingOnline)
            return;

        if (lan == null || !lan.SetPlayerName(onlineNameInput.text))
        {
            onlineStatusText.text = "Enter a player name (1–20 characters).";
            return;
        }

        onlineNameInput.text = lan.PlayerName;
        onlineStatusText.text = "Connecting...";
        connectingOnline = true;

        if (!lan.JoinOnline())
        {
            connectingOnline = false;
            onlineStatusText.text = "Could not start the connection.";
        }
    }

    public void CloseOnline()
    {
        if (connectingOnline && networkManager != null && networkManager.IsListening)
        {
            connectingOnline = false;
            networkManager.Shutdown();
        }

        onlineMenu.SetActive(false);
        mainMenu.SetActive(true);
    }

    private void OnOnlineConnected(ulong clientId)
    {
        if (!connectingOnline || networkManager == null || clientId != networkManager.LocalClientId)
            return;

        connectingOnline = false;
        onlineStatusText.text = "Connected. Waiting for players...";
    }

    private void OnOnlineDisconnected(ulong clientId)
    {
        if (!connectingOnline || networkManager == null || clientId != networkManager.LocalClientId)
            return;

        connectingOnline = false;
        string reason = networkManager.DisconnectReason;
        onlineStatusText.text = string.IsNullOrWhiteSpace(reason) ? "Could not connect to the server." : reason;
    }

    public void HostGame()
    {
        if (!SavePlayerName())
            return;
        lan.HostGame();
    }

    public void OpenJoinGames()
    {
        if (!SavePlayerName())
            return;

        lanMenu.SetActive(false);
        joinMenu.SetActive(true);
        FindGames();
    }

    private bool SavePlayerName()
    {
        if (lan != null && playerNameInput != null && lan.SetPlayerName(playerNameInput.text))
        {
            statusText.text = "";
            playerNameInput.text = lan.PlayerName;
            return true;
        }

        if (statusText != null)
            statusText.text = "Enter a player name (1–20 characters).";
        return false;
    }

    public void FindGames()
    {
        foreach (Transform child in gameListContent)
        {
            if (child.gameObject != gameEntryTemplate)
                Destroy(child.gameObject);
        }

        lan.FindGames(games =>
        {
            foreach (LANController.LANGameInfo game in games)
            {
                GameObject entry = Instantiate(gameEntryTemplate, gameListContent);
                entry.GetComponentInChildren<Text>().text = $"{game.title}  ({game.playerCount}/4)  {game.ipAddress}:{game.port}";
                entry.GetComponent<Button>().onClick.AddListener(() => JoinGame(game));
                entry.SetActive(true);
            }
        });
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
