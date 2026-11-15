using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class MatchManager : NetworkBehaviour
{
    public enum MatchPhase
    {
        Lobby,
        LobbyCountdown,
        LoadingArena,
        Overview,
        RoundCountdown,
        Fighting,
        RoundResults,
        MatchFinished,
        Aborted
    }

    [Serializable]
    public struct LobbyPlayer
    {
        public ulong clientId;
        public string playerName;
        public PlayerVisual.Character character;
        public bool ready;
        public int wins;
    }

    [Serializable]
    private class LobbyRoster
    {
        public List<LobbyPlayer> players = new();
    }

    [SerializeField]
    private float lobbyCountdownDuration = 10f;

    [SerializeField]
    private float arenaPreviewDuration = 2f;

    [SerializeField]
    private float resultsDuration = 10f;

    private const int playersPerMatch = 4;

    private List<LobbyPlayer> players = new();
    private SceneLoader sceneLoader;
    private RoundManager roundManager;

    private NetworkVariable<MatchPhase> phase = new(
        MatchPhase.Lobby,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<double> phaseDeadline = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public MatchPhase CurrentPhase => phase.Value;
    public double PhaseDeadline => phaseDeadline.Value;
    public int PlayerCount => players.Count;

    [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server, Delivery = RpcDelivery.Unreliable)]
    public void SyncPlatformRpc(string key, Vector2 position, float rotation)
    {
        MovingPlatform.ApplyNetworkPosition(key, position, rotation);
    }

    public LobbyPlayer GetPlayer(int index)
    {
        return players[index];
    }

    public PlayerVisual.Character GetCharacter(ulong clientId)
    {
        foreach (LobbyPlayer player in players)
        {
            if (player.clientId == clientId)
                return player.character;
        }

        return PlayerVisual.Character.None;
    }

    public override void OnNetworkSpawn()
    {
        sceneLoader = FindFirstObjectByType<SceneLoader>();
        roundManager = GetComponent<RoundManager>();

        if (IsServer)
        {
            players.Clear();
            phase.Value = MatchPhase.Lobby;
            phaseDeadline.Value = 0d;
            roundManager?.ResetRound();
            NetworkManager.OnClientDisconnectCallback += RemoveDisconnectedPlayer;

            if (sceneLoader != null)
                sceneLoader.ArenaLoadFinished += OnArenaLoadFinished;
        }

        if (IsClient)
        {
            phase.OnValueChanged += OnPhaseChanged;
            LANController lan = FindFirstObjectByType<LANController>();

            RequestRosterRpc();

            if (lan != null && !string.IsNullOrWhiteSpace(lan.PlayerName))
                SubmitPlayerNameRpc(lan.PlayerName);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback -= RemoveDisconnectedPlayer;

            if (sceneLoader != null)
                sceneLoader.ArenaLoadFinished -= OnArenaLoadFinished;
        }

        if (IsClient)
            phase.OnValueChanged -= OnPhaseChanged;

        players.Clear();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer)
            return;

        switch (CurrentPhase)
        {
            case MatchPhase.Lobby:
                WaitForPlayers();
                break;

            case MatchPhase.LobbyCountdown:
                UpdateLobbyCountdown();
                break;

            case MatchPhase.LoadingArena:
                // Wait for SceneLoader to report that the arena is ready.
                break;

            case MatchPhase.Overview:
                WaitForReadyPlayers();
                break;

            case MatchPhase.RoundCountdown:
                if (roundManager != null && roundManager.CurrentState == RoundManager.RoundState.RoundInProgress)
                    phase.Value = MatchPhase.Fighting;
                break;

            case MatchPhase.Fighting:
                CheckRoundResult();
                break;

            case MatchPhase.RoundResults:
                if (NetworkManager.ServerTime.Time >= phaseDeadline.Value)
                    StartNextRound();
                break;

            case MatchPhase.MatchFinished:
            case MatchPhase.Aborted:
                break;
        }
    }

    private void WaitForPlayers()
    {
        if (!HasFullLobby())
            return;

        phaseDeadline.Value = NetworkManager.ServerTime.Time + Mathf.Max(0f, lobbyCountdownDuration);
        phase.Value = MatchPhase.LobbyCountdown;
    }

    private void UpdateLobbyCountdown()
    {
        if (!HasFullLobby())
        {
            phaseDeadline.Value = 0d;
            phase.Value = MatchPhase.Lobby;
            return;
        }

        if (NetworkManager.ServerTime.Time < phaseDeadline.Value)
            return;

        StartArenaLoading();
    }

    private void StartArenaLoading()
    {
        phaseDeadline.Value = 0d;
        phase.Value = MatchPhase.LoadingArena;

#if UNITY_SERVER && !UNITY_EDITOR
        Debug.Log("FERAL_MATCHMAKER_ROOM_CLOSED");
#endif

        if (sceneLoader == null || sceneLoader.GetArenaCount() == 0)
        {
            Debug.LogError("Cannot load an arena: no SceneLoader or arena scenes are configured.");
            phase.Value = MatchPhase.Aborted;
            return;
        }

        int arenaIndex = UnityEngine.Random.Range(0, sceneLoader.GetArenaCount());

        if (!sceneLoader.LoadArena(arenaIndex))
        {
            Debug.LogError("Could not start loading the selected arena.");
            phase.Value = MatchPhase.Aborted;
        }
    }

    private void CheckRoundResult()
    {
        if (roundManager == null || roundManager.CurrentState != RoundManager.RoundState.RoundFinished)
            return;

        bool matchFinished = false;
        ulong? winner = roundManager.RoundWinnerClientId;

        if (winner.HasValue)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].clientId != winner.Value)
                    continue;

                LobbyPlayer player = players[i];
                player.wins++;
                players[i] = player;
                matchFinished = player.wins >= 3;
                break;
            }
        }

        BroadcastRoster();
        phaseDeadline.Value = matchFinished ? 0d : NetworkManager.ServerTime.Time + Mathf.Max(0f, resultsDuration);
        phase.Value = matchFinished ? MatchPhase.MatchFinished : MatchPhase.RoundResults;
    }

    private void StartNextRound()
    {
        if (!HasFullLobby())
        {
            phase.Value = MatchPhase.Aborted;
            return;
        }

        for (int i = 0; i < players.Count; i++)
        {
            LobbyPlayer player = players[i];
            player.ready = false;
            players[i] = player;
        }

        BroadcastRoster();
        roundManager.ResetRound();
        StartArenaLoading();
    }

    private void OnArenaLoadFinished(bool success)
    {
        if (!IsSpawned || !IsServer || CurrentPhase != MatchPhase.LoadingArena)
            return;

        if (!success || !HasFullLobby())
        {
            Debug.LogError("Cannot start the overview: the arena or lobby is not ready.");
            phase.Value = MatchPhase.Aborted;
            return;
        }

        phaseDeadline.Value = NetworkManager.ServerTime.Time + Mathf.Max(0f, arenaPreviewDuration);
        phase.Value = MatchPhase.Overview;
    }

    private void OnPhaseChanged(MatchPhase previous, MatchPhase current)
    {
        if (current == MatchPhase.MatchFinished)
            FindFirstObjectByType<LocalMusicDirector>()?.NotifyMatchFinished();
    }

    private bool HasFullLobby()
    {
        return players.Count == playersPerMatch && NetworkManager.ConnectedClients.Count == playersPerMatch;
    }

    private void WaitForReadyPlayers()
    {
        if (!HasFullLobby() || NetworkManager.ServerTime.Time < phaseDeadline.Value)
            return;

        foreach (LobbyPlayer player in players)
        {
            if (!player.ready)
                return;
        }

        phaseDeadline.Value = 0d;
        phase.Value = MatchPhase.RoundCountdown;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetReadyRpc(RpcParams rpcParams = default)
    {
        if (CurrentPhase != MatchPhase.Overview || NetworkManager.ServerTime.Time < phaseDeadline.Value)
            return;

        ulong clientId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].clientId != clientId || players[i].ready)
                continue;

            LobbyPlayer player = players[i];
            player.ready = true;
            players[i] = player;
            BroadcastRoster();
            return;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SubmitPlayerNameRpc(string playerName, RpcParams rpcParams = default)
    {
        if (CurrentPhase != MatchPhase.Lobby && CurrentPhase != MatchPhase.LobbyCountdown)
            return;

        if (string.IsNullOrWhiteSpace(playerName))
            return;

        string name = playerName.Trim();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 20 || name.IndexOfAny(new[] { '\r', '\n', '<', '>' }) >= 0)
            return;

        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.ConnectedClients.ContainsKey(clientId))
            return;

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].clientId == clientId)
            {
                LobbyPlayer player = players[i];
                player.playerName = name;
                players[i] = player;
                BroadcastRoster();
                return;
            }
        }

        if (players.Count >= playersPerMatch)
            return;

        List<PlayerVisual.Character> availableCharacters = new()
        {
            PlayerVisual.Character.Eagle,
            PlayerVisual.Character.Bear,
            PlayerVisual.Character.Cougar,
            PlayerVisual.Character.Snake
        };

        foreach (LobbyPlayer player in players)
            availableCharacters.Remove(player.character);

        if (availableCharacters.Count == 0)
            return;

        PlayerVisual.Character character = availableCharacters[UnityEngine.Random.Range(0, availableCharacters.Count)];
        players.Add(new LobbyPlayer { clientId = clientId, playerName = name, character = character });

        NetworkObject playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;
        if (playerObject != null)
            playerObject.GetComponent<PlayerVisual>()?.AssignCharacter((int)character);

        BroadcastRoster();
    }

    private void RemoveDisconnectedPlayer(ulong clientId)
    {
        if (CurrentPhase == MatchPhase.MatchFinished)
            return;

        bool removed = false;

        for (int i = players.Count - 1; i >= 0; i--)
        {
            if (players[i].clientId == clientId)
            {
                players.RemoveAt(i);
                removed = true;
            }
        }

        if (removed && IsSpawned)
        {
            BroadcastRoster();

            if (CurrentPhase != MatchPhase.Lobby && CurrentPhase != MatchPhase.LobbyCountdown &&
                CurrentPhase != MatchPhase.MatchFinished && CurrentPhase != MatchPhase.Aborted)
                phase.Value = MatchPhase.Aborted;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRosterRpc()
    {
        BroadcastRoster();
    }

    private void BroadcastRoster()
    {
        LobbyRoster roster = new LobbyRoster { players = players };
        ReceiveRosterRpc(JsonUtility.ToJson(roster));
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void ReceiveRosterRpc(string json)
    {
        if (IsServer)
            return;

        LobbyRoster roster = JsonUtility.FromJson<LobbyRoster>(json);
        players = roster?.players ?? new List<LobbyPlayer>();
    }
}
