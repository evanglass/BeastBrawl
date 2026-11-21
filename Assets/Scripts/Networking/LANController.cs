using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net;
using System.Net.Sockets;
using System.Text;
#endif

public class LANController : MonoBehaviour
{
    [SerializeField]
    private ushort port = 7777;

    [SerializeField]
    private string onlineAddress = "129.146.35.223.sslip.io";

    [SerializeField]
    private ushort onlinePort = 443;

    [SerializeField]
    private ushort dedicatedServerPort = 18774;

    [SerializeField]
    private ushort discoveryPort = 47777;

    [SerializeField]
    private float discoveryDuration = 2f;

    private const string discoveryRequest = "FERAL_FIGHTERS_DISCOVER_V1";
    private const string discoveryResponse = "FERAL_FIGHTERS_HOST_V1";

    private NetworkManager nm;
    private readonly HashSet<ulong> botWorkers = new();
    private string botWorkerToken;
    private ulong? customCreator;

    internal bool IsBotWorker(ulong clientId) => botWorkers.Contains(clientId);

    internal List<ulong> GetConnectedBotWorkers()
    {
        var workers = new List<ulong>();
        foreach (ulong id in nm.ConnectedClientsIds)
            if (IsBotWorker(id)) workers.Add(id);
        return workers;
    }

    private Coroutine searchRoutine;

#if UNITY_EDITOR
    public const string LocalEditorScenePath = "Assets/NGO_Minimal_Setup/NGO_Setup.unity";
    internal bool IsLocalEditorSession { get; private set; }
    private bool virtualPlayer;
#endif
    public bool CustomLobbyMode { get; private set; }

    public string PlayerName { get; private set; } = "";

    public bool SetPlayerName(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return false;

        playerName = playerName.Trim();
        if (playerName.Length > 20 || playerName.IndexOfAny(new[] { '\r', '\n', '<', '>' }) >= 0)
            return false;

        PlayerName = playerName;
        return true;
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    private UdpClient discoveryHost;
    private UdpClient discoveryClient;
#endif

    public struct LANGameInfo
    {
        public string ipAddress;
        public ushort port;
        public string title;
        public int playerCount;
    }

    [Serializable]
    private class DiscoveryResponse
    {
        public string protocol;
        public ushort port;
        public string title;
        public int playerCount;
    }

    private void Awake()
    {
        ConfigureNetworking();
    }

    private void ConfigureNetworking()
    {
        nm = NetworkManager.Singleton;

        if (nm != null && !nm.IsListening)
        {
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = ApproveConnection;
        }
    }

    private void Start()
    {
#if UNITY_EDITOR
        if (gameObject.scene.path == LocalEditorScenePath && gameObject.scene == SceneManager.GetActiveScene())
        {
            IsLocalEditorSession = true;
            virtualPlayer = Array.IndexOf(Environment.GetCommandLineArgs(), "--virtual-project-clone") >= 0;
            Application.runInBackground = true;
            StartCoroutine(RunLocalEditorSession());
        }
#elif UNITY_SERVER
        ConfigureNetworking();
        if (nm == null)
        {
            Debug.LogError("Cannot start the dedicated server: no NetworkManager was found.");
            return;
        }

        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogError("Cannot start the dedicated server: no UnityTransport was found.");
            return;
        }

        transport.UseWebSockets = true;
        transport.UseEncryption = false;
        string[] args = Environment.GetCommandLineArgs();
        CustomLobbyMode = Array.IndexOf(args, "-custom-games") >= 0;
        botWorkerToken = Environment.GetEnvironmentVariable("FERAL_BOT_WORKER_TOKEN");
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-port" && ushort.TryParse(args[i + 1], out ushort roomPort) && roomPort > 0)
                dedicatedServerPort = roomPort;
        transport.SetConnectionData("127.0.0.1", dedicatedServerPort, "127.0.0.1");
        if (Array.IndexOf(args, "-bot-worker") >= 0)
        {
            if (string.IsNullOrEmpty(botWorkerToken))
            {
                Debug.LogError("Bot worker requires FERAL_BOT_WORKER_TOKEN.");
                return;
            }
            nm.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(botWorkerToken);
            if (!nm.StartClient()) Debug.LogError("Could not start the bot worker.");
            return;
        }

        if (!nm.StartServer())
            Debug.LogError("Could not start the dedicated server.");
        else if (CustomLobbyMode)
        {
            nm.SceneManager.SetClientSynchronizationMode(UnityEngine.SceneManagement.LoadSceneMode.Additive);
            nm.OnClientDisconnectCallback += clientId =>
            {
                if (clientId == customCreator) customCreator = null;
            };
        }
#else
        var args = Environment.GetCommandLineArgs();
        int endpoint = Array.IndexOf(args, "-custom-endpoint");
        if (endpoint >= 0 && endpoint + 2 < args.Length &&
            ushort.TryParse(args[endpoint + 2], out ushort customPort) && customPort > 0)
        {
            onlineAddress = args[endpoint + 1];
            onlinePort = customPort;
        }
#endif
    }

    internal bool CanConfigureCustomLobby(ulong clientId) =>
        !IsBotWorker(clientId) && (nm.IsHost ? clientId == NetworkManager.ServerClientId : customCreator == clientId);

    [Serializable]
    private sealed class CustomLobbyList
    {
        public CustomLobbyListing[] lobbies;
    }

    internal IEnumerator BrowseCustomLobbies(Action<CustomLobbyListing[], string> completed)
    {
#if UNITY_EDITOR
        if (IsLocalEditorSession)
        {
            var match = FindFirstObjectByType<MatchManager>();
            if (nm != null && nm.IsConnectedClient && match != null && match.CustomAvailable &&
                match.AvailableHumanSlots > 0)
                completed(new[] { new CustomLobbyListing
                {
                    host = match.HostName, mode = (int)match.GameMode, teamSize = match.TeamSize,
                    occupied = match.PlayerCount, capacity = match.RequiredPlayers,
                    availableHumans = match.AvailableHumanSlots
                } }, null);
            else completed(Array.Empty<CustomLobbyListing>(), null);
            yield break;
        }
#endif
        using var request = UnityWebRequest.Get($"https://{onlineAddress}:{onlinePort}/custom/lobbies");
        request.timeout = 8;
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            completed(Array.Empty<CustomLobbyListing>(), "The lobby service is unavailable. Try Refresh.");
            yield break;
        }

        try
        {
            var result = JsonUtility.FromJson<CustomLobbyList>(request.downloadHandler.text);
            completed(result?.lobbies ?? Array.Empty<CustomLobbyListing>(), null);
        }
        catch (ArgumentException)
        {
            completed(Array.Empty<CustomLobbyListing>(), "The lobby service returned an invalid list.");
        }
    }

    internal bool ConnectPublicCustomLobby(string lobbyId)
    {
        if (lobbyId != null && !Guid.TryParse(lobbyId, out _)) return false;
        ConfigureNetworking();
        if (nm == null || nm.IsListening) return false;
        var transport = nm.GetComponent<UnityTransport>();
        if (transport == null) return false;

        string path = lobbyId == null ? "/custom/host" : "/custom/join/" + lobbyId;
        transport.UseWebSockets = true;
        transport.UseEncryption = true;
        transport.SetClientSecrets(onlineAddress);
        transport.SetConnectionData(onlineAddress, onlinePort);
        transport.ConnectionData.WebSocketPath = path;
        nm.NetworkConfig.ConnectionData = Array.Empty<byte>();
        return nm.StartClient();
    }

    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        MatchManager match = FindFirstObjectByType<MatchManager>();
        bool acceptsPlayers = match == null || !match.IsSpawned ||
            match.CurrentPhase == MatchManager.MatchPhase.Lobby ||
            match.CurrentPhase == MatchManager.MatchPhase.LobbyCountdown;
        int humanConnections = 0;
        foreach (ulong id in nm.ConnectedClientsIds)
            if (!IsBotWorker(id)) humanConnections++;
        bool worker = CustomLobbyMode && !string.IsNullOrEmpty(botWorkerToken) &&
            request.Payload != null && System.Text.Encoding.UTF8.GetString(request.Payload) == botWorkerToken;
        var workers = GetConnectedBotWorkers();
        botWorkers.RemoveWhere(id => !workers.Contains(id));
        bool roomAvailable;
        if (worker) roomAvailable = workers.Count < 3;
        else if (!CustomLobbyMode) roomAvailable = humanConnections < 4;
        else if (match != null && match.CustomGame)
            roomAvailable = match.CustomAvailable && humanConnections < match.HumanCapacity;
        else
            roomAvailable = nm.IsHost ? humanConnections < MatchRules.MaxSlots : !customCreator.HasValue;
        if (acceptsPlayers && roomAvailable && CustomLobbyMode && !nm.IsHost && !worker && !customCreator.HasValue)
            customCreator = request.ClientNetworkId;
        if (acceptsPlayers && roomAvailable && worker)
            botWorkers.Add(request.ClientNetworkId);

        response.Approved = acceptsPlayers && roomAvailable;
        response.CreatePlayerObject = response.Approved && !CustomLobbyMode;
        response.Pending = false;

        if (!response.Approved)
            response.Reason = acceptsPlayers ? "This game is full." : "This game has already started.";
    }

    public void HostGame()
    {
        ConfigureNetworking();
        if (nm == null)
        {
            Debug.LogWarning("No NetworkManager is available for LAN hosting.");
            return;
        }

        UnityTransport transport = nm.GetComponent<UnityTransport>();
        transport.UseWebSockets = false;
        transport.UseEncryption = false;
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
        
        bool started = nm.StartHost();
        if (started)
        {
            StartDiscoveryHost();
        }
        else
        {
            Debug.LogWarning("Could not start LAN host.");
        }
    }

#if UNITY_EDITOR
    internal void OpenLocalPlayMenu()
    {
        var ui = GetComponent<LocalCustomGameUI>();
        if (ui == null) ui = gameObject.AddComponent<LocalCustomGameUI>();
        ui.Open(virtualPlayer);
    }

    private IEnumerator RunLocalEditorSession()
    {
        // Let NGO_Setup finish startup, including SceneLoader's local menu.
        yield return null;
        var sceneLoader = FindFirstObjectByType<SceneLoader>();
        bool wasConnected = false;
        var retry = new WaitForSecondsRealtime(1f);

        while (sceneLoader != null && NetworkManager.Singleton != null)
        {
            var network = NetworkManager.Singleton;
            if (virtualPlayer && !network.IsListening && wasConnected)
            {
                wasConnected = false;
                sceneLoader.ReturnToMenu();
            }
            wasConnected |= network.IsConnectedClient;

            if (!network.IsListening && !sceneLoader.ReturningToMenu)
            {
                if (virtualPlayer) JoinGame("127.0.0.1");
                else if (HostLocalTest())
                {
                    network.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
                    network.SceneManager.VerifySceneBeforeLoading = (_, scene, _) => scene != "MainMenu";
                }
            }

            yield return retry;
        }
    }

    private bool HostLocalTest()
    {
        ConfigureNetworking();
        if (nm == null || nm.IsListening) return false;
        var transport = nm.GetComponent<UnityTransport>();
        if (transport == null) return false;
        transport.UseWebSockets = false;
        transport.UseEncryption = false;
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");
        CustomLobbyMode = true;
        return nm.StartHost();
    }
#endif

    public bool JoinGame(string ipAddress)
    {
        return JoinGame(ipAddress, port);
    }

    public bool JoinOnline()
    {
        Debug.LogWarning("Online games are disabled on the local development branch.");
        return false;
    }

    public bool JoinGame(string ipAddress, ushort gamePort)
    {
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            Debug.LogWarning("LAN games are not available in Web builds.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ipAddress) || gamePort == 0)
        {
            Debug.LogWarning("Enter a host address and a valid port.");
            return false;
        }

        ConfigureNetworking();
        if (nm == null || nm.IsListening)
        {
            Debug.LogWarning("Cannot join while networking is unavailable or already running.");
            return false;
        }

        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport == null)
        {
            Debug.LogWarning("No UnityTransport is available for LAN joining.");
            return false;
        }

        CancelFindGames();
        transport.UseWebSockets = false;
        transport.UseEncryption = false;
        transport.SetConnectionData(ipAddress.Trim(), gamePort);
        transport.ConnectionData.WebSocketPath = "/";
        bool started = nm.StartClient();
        if (!started)
            Debug.LogWarning("Could not start LAN client.");
        return started;
    }

    public void FindGames(Action<List<LANGameInfo>> onComplete)
    {
        CancelFindGames();
        searchRoutine = StartCoroutine(SearchForGames(onComplete));
    }

    public void CancelFindGames()
    {
        if (searchRoutine != null)
            StopCoroutine(searchRoutine);
        searchRoutine = null;
#if !UNITY_WEBGL || UNITY_EDITOR
        discoveryClient?.Close();
        discoveryClient = null;
#endif
    }

    private void StartDiscoveryHost()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        discoveryHost?.Close();
        discoveryHost = null;
        try
        {
            discoveryHost = new UdpClient(discoveryPort);
        }
        catch (SocketException exception)
        {
            Debug.LogWarning($"Could not start LAN discovery: {exception.Message}");
        }
#endif
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        if (discoveryHost == null)
            return;

        if (nm == null || !nm.IsHost || !nm.IsListening)
        {
            discoveryHost.Close();
            discoveryHost = null;
            return;
        }

        try
        {
            for (int i = 0; i < 64 && discoveryHost.Available > 0; i++)
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] request = discoveryHost.Receive(ref sender);
                if (Encoding.UTF8.GetString(request) != discoveryRequest)
                    continue;

                DiscoveryResponse response = new DiscoveryResponse
                {
                    protocol = discoveryResponse,
                    port = port,
                    title = "Feral Fighters",
                    playerCount = nm.ConnectedClients.Count
                };
                byte[] message = Encoding.UTF8.GetBytes(JsonUtility.ToJson(response));
                discoveryHost.Send(message, message.Length, sender);
            }
        }
        catch (SocketException exception)
        {
            Debug.LogWarning($"LAN discovery host stopped: {exception.Message}");
            discoveryHost.Close();
            discoveryHost = null;
        }
#endif
    }

    private IEnumerator SearchForGames(Action<List<LANGameInfo>> onComplete)
    {
        List<LANGameInfo> games = new List<LANGameInfo>();
        yield return null;
#if !UNITY_WEBGL || UNITY_EDITOR
        try
        {
            discoveryClient = new UdpClient(0);
            discoveryClient.EnableBroadcast = true;
            byte[] request = Encoding.UTF8.GetBytes(discoveryRequest);
            discoveryClient.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, discoveryPort));
        }
        catch (SocketException exception)
        {
            Debug.LogWarning($"Could not search for LAN games: {exception.Message}");
            discoveryClient?.Close();
            discoveryClient = null;
        }

        float endTime = Time.realtimeSinceStartup + Mathf.Max(0f, discoveryDuration);
        while (discoveryClient != null && Time.realtimeSinceStartup < endTime)
        {
            try
            {
                for (int i = 0; i < 64 && discoveryClient.Available > 0; i++)
                {
                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    byte[] message = discoveryClient.Receive(ref sender);
                    DiscoveryResponse response = JsonUtility.FromJson<DiscoveryResponse>(Encoding.UTF8.GetString(message));
                    if (response == null || response.protocol != discoveryResponse || response.port == 0)
                        continue;

                    string address = sender.Address.ToString();
                    if (games.Exists(game => game.ipAddress == address && game.port == response.port))
                        continue;

                    games.Add(new LANGameInfo
                    {
                        ipAddress = address,
                        port = response.port,
                        title = response.title,
                        playerCount = response.playerCount
                    });
                }
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"LAN search stopped: {exception.Message}");
                discoveryClient.Close();
                discoveryClient = null;
            }
            catch (ArgumentException)
            {
                // Ignore malformed discovery responses.
            }
            yield return null;
        }

        discoveryClient?.Close();
        discoveryClient = null;
#else
        Debug.LogWarning("LAN discovery is not available in Web builds.");
        yield return null;
#endif
        searchRoutine = null;
        onComplete?.Invoke(games);
    }

    private void OnDestroy()
    {
        CancelFindGames();
#if !UNITY_WEBGL || UNITY_EDITOR
        discoveryHost?.Close();
        discoveryHost = null;
#endif
    }
}
