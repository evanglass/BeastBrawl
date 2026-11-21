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
        public bool isBot;
        public ulong objectId;
        public int slot;
        public int team;
    }

    [Serializable]
    private class LobbyRoster
    {
        public List<LobbyPlayer> players = new();
        public int[] slots;
    }

    [SerializeField]
    private float lobbyCountdownDuration = 10f;

    [SerializeField]
    private float arenaPreviewDuration = 2f;

    [SerializeField]
    private float resultsDuration = 10f;

    internal const int MinSimulationRounds = 10;
    internal const int MaxSimulationRounds = 100;
    private const float SimulationRoundLimit = 120f;
    private const float SimulationIntermission = 1f;
    internal int SimulationRounds { get; private set; }
    internal int SimulationRoundsCompleted { get; private set; }
    internal string SimulationRoundOutcome { get; private set; }
    private bool IsSimulation => SimulationRounds > 0;

    private const int playersPerMatch = 4;

    private List<LobbyPlayer> players = new();
    private SceneLoader sceneLoader;
    private LANController lanController;
    private RoundManager roundManager;
    private readonly NetworkVariable<bool> customGame = new(false);
    private readonly NetworkVariable<MatchGameMode> gameMode = new(MatchGameMode.FreeForAll);
    private readonly NetworkVariable<int> teamSize = new(2);
    private readonly NetworkVariable<int> arenaSelection = new(0);
    private readonly NetworkVariable<int> matchWinnerTeam = new(0);
    private readonly NetworkVariable<Unity.Collections.FixedString64Bytes> customHostName = new();
    private int[] slots = System.Array.Empty<int>();
    private ulong? lobbyHost;
    private readonly NetworkVariable<ulong> lobbyHostClientId = new(ulong.MaxValue);
    public MatchGameMode GameMode => gameMode.Value;
    public int TeamSize => teamSize.Value;
    public int MatchWinnerTeam => matchWinnerTeam.Value;
    public int ArenaSelection => arenaSelection.Value;
    public bool IsTeamFight => GameMode == MatchGameMode.TeamFight;
    public int HumanCapacity => System.Array.FindAll(slots, slot => slot == MatchRules.Human).Length;
    public int AvailableHumanSlots => Mathf.Max(0, HumanCapacity - players.FindAll(player => !player.isBot).Count);
    public string HostName => customHostName.Value.ToString();
    private int OccupiedMask
    {
        get
        {
            int mask = 0;
            foreach (var player in players) mask |= 1 << player.slot;
            return mask;
        }
    }
    private bool customLobbyMode;
    private NetworkVariable<int> customCapacity = new(4);
    private NetworkVariable<int> customWins = new(3);
    private NetworkVariable<bool> customAvailable = new(false);
    public bool CustomAvailable => IsSpawned && customAvailable.Value && CurrentPhase == MatchPhase.Lobby;
    public int RequiredPlayers => IsSpawned ? customCapacity.Value : 4;
    public bool CustomGame => customGame.Value;
    public int WinsRequired => IsSpawned ? customWins.Value : 3;

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

    public PlayerVisual.Character GetCharacterForObject(NetworkObject obj)
    {
        foreach (LobbyPlayer player in players)
            if (player.objectId == obj.NetworkObjectId) return player.character;
        return PlayerVisual.Character.None;
    }

    public bool HasHuman(ulong clientId) => players.Exists(player => !player.isBot && player.clientId == clientId);
    public bool HasLobbyMember(ulong clientId) => HasHuman(clientId) || IsLobbyHost(clientId);

    public bool TryGetSlot(int slot, out LobbyPlayer player)
    {
        foreach (var entry in players)
            if (entry.slot == slot) { player = entry; return true; }
        player = default;
        return false;
    }

    public int GetTeamForObject(NetworkObject obj)
    {
        if (!IsTeamFight || obj == null) return MatchRules.NoTeam;
        foreach (var entry in players)
            if (entry.objectId == obj.NetworkObjectId) return entry.team;
        return MatchRules.NoTeam;
    }

    public bool CanSwitchTeam(ulong clientId)
    {
        if (CurrentPhase != MatchPhase.Lobby || !IsTeamFight || !CustomAvailable || slots.Length != RequiredPlayers) return false;
        int index = players.FindIndex(player => !player.isBot && player.clientId == clientId);
        return index >= 0 && MatchRules.FindHumanSlot(GameMode, TeamSize, slots, OccupiedMask,
            3 - players[index].team) >= 0;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SwitchTeamRpc(RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!CanSwitchTeam(clientId)) return;
        int index = players.FindIndex(player => !player.isBot && player.clientId == clientId);
        var player = players[index];
        player.team = 3 - player.team;
        player.slot = MatchRules.FindHumanSlot(GameMode, TeamSize, slots, OccupiedMask, player.team);
        if (!MatchRules.CharacterAllowed((int)player.character, player.team))
            player.character = PlayerVisual.OriginalCharacter(player.character);
        players[index] = player;
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(player.objectId, out var fighter))
        {
            fighter.GetComponent<PlayerNetworkBridge>().SetTeam(player.team);
            fighter.GetComponent<PlayerVisual>().AssignCharacter((int)player.character);
        }
        BroadcastRoster();
    }

    public bool CanChooseCustomCharacter(ulong clientId) =>
        IsSpawned && customAvailable.Value && IsTeamFight &&
        (CurrentPhase == MatchPhase.Lobby || CurrentPhase == MatchPhase.LobbyCountdown) &&
        players.Exists(player => !player.isBot && player.clientId == clientId && player.team == 2);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ChooseCustomCharacterRpc(int choice, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!CanChooseCustomCharacter(clientId) || !MatchRules.CharacterAllowed(choice, 2)) return;
        int index = players.FindIndex(player => !player.isBot && player.clientId == clientId);
        var player = players[index];
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(player.objectId, out var fighter)) return;
        player.character = (PlayerVisual.Character)choice;
        players[index] = player;
        fighter.GetComponent<PlayerVisual>().AssignCharacter(choice);
        BroadcastRoster();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void CreateCustomLobbyRpc(string playerName, int wins, MatchGameMode mode, int size, int arenaMask,
        int[] roster, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!customLobbyMode || CustomGame || CurrentPhase != MatchPhase.Lobby ||
            !lanController.CanConfigureCustomLobby(sender) ||
            !NetworkManager.ConnectedClients.ContainsKey(sender) || !ValidName(playerName) ||
            wins < 1 || wins > 3 || !MatchRules.ValidRoster(mode, size, roster, simulation: false) ||
            arenaMask <= 0 || arenaMask >= (1 << SceneLoader.CustomArenas.Length) || sceneLoader == null ||
            !TryGetArenas(arenaMask, out var arenas))
            return;

        var prefab = NetworkManager.NetworkConfig.PlayerPrefab;
        if (prefab == null || prefab.GetComponent<NetworkObject>() == null) return;

        ConfigureRoster(mode, size, roster, arenaMask, wins);
        // Resolve bot characters first, so the host can get an unused character when one is available.
        SpawnBots(prefab, useWorkers: true);
        // An all-bot custom roster leaves the host connected as a spectator.
        if (HumanCapacity > 0 && !RegisterHuman(sender, playerName.Trim()))
        {
            CancelCustomLobby();
            return;
        }

        lobbyHost = sender;
        customHostName.Value = playerName.Trim();
        lobbyHostClientId.Value = sender;
        sceneLoader.SetCustomArenas(arenas);
        customAvailable.Value = true;
        BroadcastRoster();
    }

    private void ConfigureRoster(MatchGameMode mode, int size, int[] roster, int arenaMask, int wins)
    {
        slots = (int[])roster.Clone();
        gameMode.Value = mode;
        teamSize.Value = mode == MatchGameMode.TeamFight ? size : 2;
        customGame.Value = true;
        customCapacity.Value = slots.Length;
        customWins.Value = wins;
        arenaSelection.Value = arenaMask;
    }

    private static bool TryGetArenas(int arenaMask, out List<string> arenas)
    {
        arenas = new List<string>();
        for (int i = 0; i < SceneLoader.CustomArenas.Length; i++)
        {
            if ((arenaMask & (1 << i)) == 0) continue;
            string arena = SceneLoader.CustomArenas[i];
            if (UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/" + arena + ".unity") < 0)
                return false;
            arenas.Add(arena);
        }

        return arenas.Count > 0;
    }

    private void SpawnBots(GameObject prefab, bool useWorkers)
    {
        var workers = useWorkers ? lanController.GetConnectedBotWorkers() : null;
        int botIndex = 0;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            int characterId = slots[slot];
            if (characterId == MatchRules.Human) continue;
            int team = MatchRules.TeamForSlot(GameMode, TeamSize, slot);
            if (characterId == MatchRules.RandomBot)
                characterId = UnityEngine.Random.Range((int)PlayerVisual.Character.Eagle, MatchRules.LastCharacterForTeam(team) + 1);

            var obj = Instantiate(prefab).GetComponent<NetworkObject>();
            if (workers != null && workers.Count > 0)
            {
                obj.DontDestroyWithOwner = true;
                obj.SpawnWithOwnership(workers[botIndex % workers.Count]);
            }
            else obj.Spawn();
            var bridge = obj.GetComponent<PlayerNetworkBridge>();
            bridge.SetTeam(team);
            bridge.MarkAsBot();
            obj.GetComponent<PlayerVisual>()?.AssignCharacter(characterId);
            var character = (PlayerVisual.Character)characterId;
            players.Add(new LobbyPlayer { clientId = ulong.MaxValue - (ulong)slot, objectId = obj.NetworkObjectId,
                playerName = IsSimulation ? PlayerVisual.CharacterName(character) : "Bot " + (botIndex + 1),
                character = character, isBot = true, ready = true, slot = slot, team = team });
            botIndex++;
        }
    }

#if UNITY_EDITOR
    private bool CanStartSimulation => IsSpawned && IsHost && customLobbyMode && !CustomGame &&
        CurrentPhase == MatchPhase.Lobby && lanController?.IsLocalEditorSession == true &&
        GameGlobals.Instance != null && GameGlobals.Instance.simulationMode;

    internal bool StartSimulation(int rounds, int arenaMask, MatchGameMode mode, int size, int[] roster)
    {
        if (!CanStartSimulation || rounds < MinSimulationRounds || rounds > MaxSimulationRounds ||
            !MatchRules.ValidRoster(mode, size, roster, simulation: true) ||
            sceneLoader == null || roundManager == null || !TryGetArenas(arenaMask, out var arenas))
            return false;

        var prefab = NetworkManager.NetworkConfig.PlayerPrefab;
        if (prefab == null || prefab.GetComponent<NetworkObject>() == null) return false;

        SimulationRounds = rounds;
        SimulationRoundsCompleted = 0;
        SimulationRoundOutcome = "";
        ConfigureRoster(mode, size, roster, arenaMask, 3);
        sceneLoader.SetCustomArenas(arenas);
        SpawnBots(prefab, useWorkers: false);
        BroadcastRoster();

        var ui = GetComponent<SimulationMatchUI>();
        if (ui == null) ui = gameObject.AddComponent<SimulationMatchUI>();
        ui.Initialize(this, sceneLoader);
        return true;
    }

    internal void ResetSimulationRound()
    {
        if (!IsSpawned || !IsHost || !IsSimulation || CurrentPhase != MatchPhase.Fighting) return;
        FinishSimulationRound("Round reset", scoreSurvivors: true);
    }
#endif

    public static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 20 &&
        System.Text.Encoding.UTF8.GetByteCount(name.Trim()) <= 61 &&
        name.IndexOfAny(new[] { '\r', '\n', '<', '>' }) < 0;

    public override void OnNetworkSpawn()
    {
        sceneLoader = FindFirstObjectByType<SceneLoader>();
        roundManager = GetComponent<RoundManager>();
        lanController = FindFirstObjectByType<LANController>();

        if (IsServer)
        {
            players.Clear();
            customLobbyMode = lanController?.CustomLobbyMode == true;
            customGame.Value = false;
            gameMode.Value = MatchGameMode.FreeForAll;
            teamSize.Value = 2;
            arenaSelection.Value = matchWinnerTeam.Value = 0;
            slots = System.Array.Empty<int>();
            lobbyHost = null;
            lobbyHostClientId.Value = ulong.MaxValue;
            customHostName.Value = default;
            SimulationRounds = 0;
            SimulationRoundsCompleted = 0;
            SimulationRoundOutcome = "";
            customAvailable.Value = false;
            customCapacity.Value = playersPerMatch;
            customWins.Value = 3;
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
                {
                    if (IsSimulation) phaseDeadline.Value = NetworkManager.ServerTime.Time + SimulationRoundLimit;
                    phase.Value = MatchPhase.Fighting;
                }
                break;

            case MatchPhase.Fighting:
                CheckRoundResult();
                if (IsSimulation && CurrentPhase == MatchPhase.Fighting &&
                    NetworkManager.ServerTime.Time >= phaseDeadline.Value)
                    FinishSimulationRound("Time limit reached", scoreSurvivors: true);
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
        if (customLobbyMode && !CustomGame) return;
        if (!HasFullLobby())
            return;

        phaseDeadline.Value = NetworkManager.ServerTime.Time +
            (IsSimulation ? 0f : Mathf.Max(0f, lobbyCountdownDuration));
        phase.Value = MatchPhase.LobbyCountdown;
    }

    private void UpdateLobbyCountdown()
    {
        if (!HasFullLobby())
        {
            phaseDeadline.Value = 0d;
            phase.Value = MatchPhase.Lobby;
            PublishDiscovery();
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
        PublishDiscovery();

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

        if (IsSimulation)
        {
            FinishSimulationRound(IsTeamFight && roundManager.WinnerTeam != 0
                ? $"Team {roundManager.WinnerTeam} wins" : roundManager.RoundWinnerObjectId.HasValue ? "Round complete" : "Draw",
                scoreSurvivors: false);
            return;
        }

        bool matchFinished = false;
        ulong? winner = roundManager.RoundWinnerObjectId;

        int winningTeam = roundManager.WinnerTeam;
        if (IsTeamFight ? winningTeam != 0 : winner.HasValue)
        {
            for (int i = 0; i < players.Count; i++)
            {
                LobbyPlayer player = players[i];
                if (IsTeamFight ? player.team != winningTeam : player.objectId != winner.Value) continue;
                player.wins++;
                players[i] = player;
                matchFinished |= player.wins >= customWins.Value;
            }
        }

        // A whole team leaving mid-fight forfeits the match.
        if (IsTeamFight && winningTeam != 0 && !players.Exists(player => player.team == 3 - winningTeam))
            matchFinished = true;
        if (matchFinished) matchWinnerTeam.Value = winningTeam;
        BroadcastRoster();
        phaseDeadline.Value = matchFinished ? 0d : NetworkManager.ServerTime.Time + Mathf.Max(0f, resultsDuration);
        phase.Value = matchFinished ? MatchPhase.MatchFinished : MatchPhase.RoundResults;
    }

    private void FinishSimulationRound(string outcome, bool scoreSurvivors)
    {
        if (CurrentPhase != MatchPhase.Fighting) return;

        for (int i = 0; i < players.Count; i++)
        {
            LobbyPlayer player = players[i];
            bool scores = IsTeamFight
                ? !scoreSurvivors && roundManager.WinnerTeam == player.team
                : scoreSurvivors
                    ? NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(player.objectId, out var fighter) &&
                      fighter.GetComponent<PlayerState>() is { Eliminated: false }
                    : roundManager.RoundWinnerObjectId == player.objectId;
            if (!scores) continue;
            player.wins++;
            players[i] = player;
        }

        if (scoreSurvivors) roundManager.EndSimulationRound();
        SimulationRoundsCompleted++;
        SimulationRoundOutcome = scoreSurvivors ? outcome + (IsTeamFight ? " · draw" : " · survivors scored") : outcome;
        bool finished = SimulationRoundsCompleted >= SimulationRounds;
        BroadcastRoster();
        phaseDeadline.Value = finished ? 0d : NetworkManager.ServerTime.Time + SimulationIntermission;
        phase.Value = finished ? MatchPhase.MatchFinished : MatchPhase.RoundResults;
    }

    private bool HasRoundRoster() => IsTeamFight
        ? players.Exists(player => player.team == 1) && players.Exists(player => player.team == 2)
        : HasFullLobby();

    private void StartNextRound()
    {
        if (!HasRoundRoster())
        {
            phase.Value = MatchPhase.Aborted;
            return;
        }

        for (int i = 0; i < players.Count; i++)
        {
            LobbyPlayer player = players[i];
            player.ready = player.isBot;
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

        if (!success || !HasRoundRoster())
        {
            Debug.LogError("Cannot start the overview: the arena or lobby is not ready.");
            phase.Value = MatchPhase.Aborted;
            return;
        }

        phaseDeadline.Value = NetworkManager.ServerTime.Time +
            (IsSimulation ? 0f : Mathf.Max(0f, arenaPreviewDuration));
        phase.Value = MatchPhase.Overview;
    }

    private void OnPhaseChanged(MatchPhase previous, MatchPhase current)
    {
        if (current == MatchPhase.MatchFinished)
            FindFirstObjectByType<LocalMusicDirector>()?.NotifyMatchFinished();
    }

    private bool HasFullLobby()
    {
        return players.Count == RequiredPlayers && (CustomGame || NetworkManager.ConnectedClients.Count == playersPerMatch);
    }

    private void WaitForReadyPlayers()
    {
        if (!HasRoundRoster() || NetworkManager.ServerTime.Time < phaseDeadline.Value)
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

        ulong clientId = rpcParams.Receive.SenderClientId;
        if ((customLobbyMode && !CustomAvailable) || !ValidName(playerName) ||
            !NetworkManager.ConnectedClients.ContainsKey(clientId) || lanController?.IsBotWorker(clientId) == true) return;
        if (RegisterHuman(clientId, playerName.Trim())) BroadcastRoster();
    }

    private bool RegisterHuman(ulong clientId, string name)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].clientId == clientId)
            {
                LobbyPlayer player = players[i];
                player.playerName = name;
                players[i] = player;
                if (clientId == lobbyHost) customHostName.Value = name;
                return true;
            }
        }

        if (players.Count >= RequiredPlayers) return false;
        int occupied = OccupiedMask;
        int slot = CustomGame ? MatchRules.FindHumanSlot(GameMode, TeamSize, slots, occupied) : 0;
        if (!CustomGame)
            while ((occupied & (1 << slot)) != 0) slot++;
        if (slot < 0) return false;
        int team = MatchRules.TeamForSlot(GameMode, TeamSize, slot);

        List<PlayerVisual.Character> availableCharacters = new()
        {
            PlayerVisual.Character.Eagle,
            PlayerVisual.Character.Bear,
            PlayerVisual.Character.Cougar,
            PlayerVisual.Character.Snake
        };

        foreach (LobbyPlayer player in players)
            availableCharacters.Remove(player.character);

        if (availableCharacters.Count == 0 && !CustomGame) return false;
        PlayerVisual.Character character = availableCharacters.Count > 0
            ? availableCharacters[UnityEngine.Random.Range(0, availableCharacters.Count)]
            : (PlayerVisual.Character)UnityEngine.Random.Range((int)PlayerVisual.Character.Eagle, (int)PlayerVisual.Character.Snake + 1);
        NetworkObject playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;
        if (playerObject == null && customLobbyMode)
        {
            var prefab = NetworkManager.NetworkConfig.PlayerPrefab;
            if (prefab == null) return false;
            var instance = Instantiate(prefab);
            playerObject = instance.GetComponent<NetworkObject>();
            if (playerObject == null) { Destroy(instance); return false; }
            playerObject.SpawnAsPlayerObject(clientId);
        }
        if (playerObject == null) return false;
        players.Add(new LobbyPlayer { clientId = clientId, objectId = playerObject.NetworkObjectId, playerName = name, character = character, slot = slot, team = team });
        playerObject.GetComponent<PlayerNetworkBridge>().SetTeam(team);
        if (playerObject != null)
            playerObject.GetComponent<PlayerVisual>()?.AssignCharacter((int)character);

        return true;
    }

    private void RemoveDisconnectedPlayer(ulong clientId)
    {
        if (NetworkManager.ShutdownInProgress || CurrentPhase == MatchPhase.MatchFinished)
            return;

        if (lobbyHost == clientId && (CurrentPhase == MatchPhase.Lobby || CurrentPhase == MatchPhase.LobbyCountdown))
        {
            CancelCustomLobby();
            return;
        }

        bool removed = false;

        for (int i = players.Count - 1; i >= 0; i--)
        {
            LobbyPlayer entry = players[i];
            if (entry.isBot && lanController?.IsBotWorker(clientId) == true &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(entry.objectId, out var bot) &&
                bot.OwnerClientId == clientId)
                bot.RemoveOwnership();

            if (!entry.isBot && entry.clientId == clientId)
            {
                players.RemoveAt(i);
                removed = true;
            }
        }

        if (removed && IsSpawned)
        {
            BroadcastRoster();

            if ((!IsTeamFight || CurrentPhase != MatchPhase.Fighting) &&
                CurrentPhase != MatchPhase.Lobby && CurrentPhase != MatchPhase.LobbyCountdown &&
                CurrentPhase != MatchPhase.MatchFinished && CurrentPhase != MatchPhase.Aborted)
                phase.Value = MatchPhase.Aborted;
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void LeaveCustomLobbyRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!CustomGame || (CurrentPhase != MatchPhase.Lobby && CurrentPhase != MatchPhase.LobbyCountdown) ||
            !HasLobbyMember(sender)) return;

        if (sender == lobbyHost) CancelCustomLobby();
        else if (sender != NetworkManager.ServerClientId) NetworkManager.DisconnectClient(sender);
    }

    public bool IsLobbyHost(ulong clientId) => CustomGame && lobbyHostClientId.Value == clientId;

    private void CancelCustomLobby()
    {
        customAvailable.Value = false;
        var guests = players.FindAll(player => !player.isBot).ConvertAll(player => player.clientId);
        if (lobbyHost.HasValue && !guests.Contains(lobbyHost.Value)) guests.Add(lobbyHost.Value);
        var fighters = players.ConvertAll(player => player.objectId);
        players.Clear();
        lobbyHost = null;
        lobbyHostClientId.Value = ulong.MaxValue;
        customHostName.Value = default;
        slots = System.Array.Empty<int>();
        customGame.Value = false;
        gameMode.Value = MatchGameMode.FreeForAll;
        phase.Value = MatchPhase.Lobby;
        phaseDeadline.Value = 0d;
        roundManager?.ResetRound();
        BroadcastRoster();

        foreach (ulong id in fighters)
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var fighter)) fighter.Despawn();
        foreach (ulong guest in guests)
            if (guest != NetworkManager.ServerClientId && NetworkManager.ConnectedClients.ContainsKey(guest))
                NetworkManager.DisconnectClient(guest);

#if UNITY_SERVER && !UNITY_EDITOR
        Debug.Log("FERAL_MATCHMAKER_ROOM_CLOSED");
#endif
    }

    private void PublishDiscovery()
    {
#if UNITY_SERVER && !UNITY_EDITOR
        if (!customLobbyMode) return;
        var listing = new CustomLobbyListing
        {
            host = HostName,
            mode = (int)GameMode,
            teamSize = TeamSize,
            occupied = PlayerCount,
            capacity = RequiredPlayers,
            availableHumans = CustomAvailable ? AvailableHumanSlots : 0,
            humanCapacity = HumanCapacity
        };
        Debug.Log("FERAL_CUSTOM_LOBBY " + JsonUtility.ToJson(listing));
#endif
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestRosterRpc()
    {
        BroadcastRoster();
    }

    private void BroadcastRoster()
    {
        LobbyRoster roster = new LobbyRoster { players = players, slots = slots };
        ReceiveRosterRpc(JsonUtility.ToJson(roster));
        PublishDiscovery();
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void ReceiveRosterRpc(string json)
    {
        if (IsServer)
            return;

        LobbyRoster roster = JsonUtility.FromJson<LobbyRoster>(json);
        players = roster?.players ?? new List<LobbyPlayer>();
        slots = roster?.slots ?? System.Array.Empty<int>();
    }
}
