using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
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
    private string onlineAddress = "beastnet.evanglass.me";

    [SerializeField]
    private ushort onlinePort = 18773;

    [SerializeField]
    private ushort dedicatedServerPort = 18774;

    [SerializeField]
    private ushort discoveryPort = 47777;

    [SerializeField]
    private float discoveryDuration = 2f;

    private const string discoveryRequest = "FERAL_FIGHTERS_DISCOVER_V1";
    private const string discoveryResponse = "FERAL_FIGHTERS_HOST_V1";

    private NetworkManager nm;
    private Coroutine searchRoutine;

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
#if UNITY_SERVER && !UNITY_EDITOR
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
        transport.SetConnectionData("127.0.0.1", dedicatedServerPort, "127.0.0.1");
        if (!nm.StartServer())
            Debug.LogError("Could not start the dedicated server.");
#endif
    }

    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        MatchManager match = FindFirstObjectByType<MatchManager>();
        bool acceptsPlayers = match == null || !match.IsSpawned ||
            match.CurrentPhase == MatchManager.MatchPhase.Lobby ||
            match.CurrentPhase == MatchManager.MatchPhase.LobbyCountdown;
        bool roomAvailable = nm.ConnectedClients.Count < 4;

        response.Approved = acceptsPlayers && roomAvailable;
        response.CreatePlayerObject = response.Approved;
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

    public bool JoinGame(string ipAddress)
    {
        return JoinGame(ipAddress, port);
    }

    public bool JoinOnline()
    {
        ConfigureNetworking();
        if (nm == null || nm.IsListening || string.IsNullOrWhiteSpace(onlineAddress) || onlinePort == 0)
            return false;

        UnityTransport transport = nm.GetComponent<UnityTransport>();
        if (transport == null)
            return false;

        CancelFindGames();
        transport.UseWebSockets = true;
        transport.UseEncryption = true;
        transport.SetClientSecrets(onlineAddress);
        transport.SetConnectionData(onlineAddress, onlinePort);
        return nm.StartClient();
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
