using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RoundManager : NetworkBehaviour
{
    public List<Transform> spawnPositions = new();
    public Transform eagleLeftCorner;
    public Transform eagleRightCorner;
    public List<NetworkObject> players;
    public int playersPerRound = 2;
    [Min(1f)] public float roundIntroSeconds = 4f;
    private bool roundPrepared;
    private NetworkVariable<double> fightStartTime = new(0);
    public double FightStartTime => fightStartTime.Value;
    private bool arenaReady;
    private readonly HashSet<Collider2D> recoveryGeometry = new();
    internal HashSet<Collider2D>.Enumerator GetRecoveryGeometry() => recoveryGeometry.GetEnumerator();
    internal bool HasRecoveryGeometry => recoveryGeometry.Count > 0;
    internal bool IsRecoveryGeometry(Collider2D collider) => recoveryGeometry.Contains(collider);

    private NetworkVariable<int> winnerId = new(
        -2,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<int> winnerTeam = new(0);
    public int WinnerTeam => winnerTeam.Value;
    public int WinnerId => winnerId.Value;
    public ulong? RoundWinnerClientId { get; private set; }
    public ulong? RoundWinnerObjectId { get; private set; }

    public void ResetRound()
    {
        if (!IsServer)
            return;

        players.Clear();
        roundPrepared = false;
        fightStartTime.Value = 0;
        winnerId.Value = -2;
        winnerTeam.Value = 0;
        RoundWinnerClientId = null;
        RoundWinnerObjectId = null;
        CurrentState = RoundState.WaitingForPlayers;
    }

    public enum RoundState
    {
        WaitingForPlayers,
        RoundStarting,
        RoundInProgress,
        RoundEnding,
        RoundFinished
    }

    // Round transitions originate on the server and are observed by each local audio director.
    private NetworkVariable<RoundState> currentState = new(
        RoundState.WaitingForPlayers,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    public RoundState CurrentState
    {
        get => currentState.Value;
        private set => currentState.Value = value;
    }

    public void ClearArena()
    {
        arenaReady = false;
        recoveryGeometry.Clear();
        spawnPositions.Clear();
        eagleLeftCorner = null;
        eagleRightCorner = null;
    }

    public bool SetArena(Scene arena)
    {
        ClearArena();
        if (!arena.IsValid() || !arena.isLoaded)
        {
            Debug.LogError("Cannot find spawn points: arena is not loaded.");
            return false;
        }

        var match = GetComponent<MatchManager>();
        spawnPositions = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None)
            .Where(point => point.gameObject.scene == arena && (!point.TeamOnly || match?.IsTeamFight == true))
            .Select(point => point.transform)
            .ToList();

        var eagleCorners = FindObjectsByType<EagleCorner>(FindObjectsSortMode.None)
            .Where(corner => corner.gameObject.scene == arena)
            .ToList();
        eagleLeftCorner = eagleCorners.FirstOrDefault(c => c.LeftCorner)?.transform;
        eagleRightCorner = eagleCorners.FirstOrDefault(c => !c.LeftCorner)?.transform;

        int needed = Mathf.Max(playersPerRound, match?.RequiredPlayers ?? playersPerRound);
        if (spawnPositions.Count < needed)
        {
            Debug.LogError($"Arena {arena.name} has {spawnPositions.Count} spawn points; {needed} are required.");
            return false;
        }

        arenaReady = true;
        CacheRecoveryGeometry(arena);
        return true;
    }

    internal void CacheRecoveryGeometry(Scene arena)
    {
        recoveryGeometry.Clear();
        // Fighters live in the network setup scene. Their platforms live in the additive arena.
        foreach (GameObject root in arena.GetRootGameObjects())
        {
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
            {
                if (!collider.isTrigger && collider.CompareTag("Level Geometry") &&
                    collider.GetComponentInParent<PlayerNetworkBridge>() == null)
                    recoveryGeometry.Add(collider);
            }
        }
    }

    private void RemoveDisconnectedPlayers()
    {
        players.RemoveAll(player =>
            player == null ||
            !player.IsSpawned ||
            (player.OwnerClientId != NetworkManager.ServerClientId &&
             !NetworkManager.ConnectedClients.ContainsKey(player.OwnerClientId))
        );
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer)
            return;

        switch (CurrentState)
        {
            case RoundState.WaitingForPlayers:
                CollectPlayers();
                break;
            case RoundState.RoundStarting:
                StartRound();
                break;
            case RoundState.RoundInProgress:
                RoundLoop();
                break;
            case RoundState.RoundEnding:
                EndRound();
                break;
            case RoundState.RoundFinished:
                FinishedRound();
                break;
        }
    }

    private void CollectPlayers()
    {
        RemoveDisconnectedPlayers();
        var matchManager = GetComponent<MatchManager>();

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;

            if (playerObject == null)
                continue;

            if (playerObject == null || !playerObject.IsSpawned)
                continue;

            if (matchManager != null && matchManager.GetCharacterForObject(playerObject) == PlayerVisual.Character.None)
                continue;

            PlayerState playerState = playerObject.GetComponent<PlayerState>();

            if (playerState != null && !players.Contains(playerObject))
            {
                players.Add(playerObject);
            }

        }

        if (matchManager != null && matchManager.CustomGame)
        {
            foreach (var obj in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
                if (obj.IsSpawned && !obj.IsPlayerObject && obj.GetComponent<PlayerState>() != null &&
                    matchManager.GetCharacterForObject(obj) != PlayerVisual.Character.None && !players.Contains(obj))
                    players.Add(obj);
        }
        if (matchManager != null && matchManager.CurrentPhase != MatchManager.MatchPhase.RoundCountdown)
            return;

        if (matchManager != null && players.Any(player => matchManager.GetCharacterForObject(player) == PlayerVisual.Character.None))
            return;

        if (arenaReady && players.Count >= playersPerRound && players.Count <= spawnPositions.Count)
        {
            roundPrepared = false;
            fightStartTime.Value = NetworkManager.ServerTime.Time + Mathf.Max(1f, roundIntroSeconds);
            CurrentState = RoundState.RoundStarting;
        }
    }

    private void StartRound()
    {
        RemoveDisconnectedPlayers();
        if (!arenaReady || players.Count < playersPerRound || spawnPositions.Count < players.Count || spawnPositions.Any(position => position == null))
        {
            roundPrepared = false;
            fightStartTime.Value = 0;
            CurrentState = RoundState.WaitingForPlayers;
            return;
        }

        if (!roundPrepared)
        {
            var matchManager = GetComponent<MatchManager>();
            if (matchManager != null && players.Any(player => matchManager.GetCharacterForObject(player) == PlayerVisual.Character.None))
            {
                fightStartTime.Value = 0;
                CurrentState = RoundState.WaitingForPlayers;
                return;
            }

            var randomIndices = Enumerable.Range(0, spawnPositions.Count).OrderBy(x => Random.value)
                .Take(players.Count).ToArray();
            if (matchManager?.IsTeamFight == true)
            {
                players.Sort((a, b) => matchManager.GetTeamForObject(a).CompareTo(matchManager.GetTeamForObject(b)));
                randomIndices = randomIndices.OrderBy(index => spawnPositions[index].position.x).ToArray();
                if (Random.value < .5f) System.Array.Reverse(randomIndices);
            }
            var characters = Enumerable.Range(0, 4).OrderBy(x => Random.value).ToArray();

            for (int i = 0; i < players.Count; i++)
            {
                var playerObject = players[i];
                var visual = playerObject.GetComponent<PlayerVisual>();
                if (visual != null)
                    visual.AssignCharacter(matchManager != null ? (int)matchManager.GetCharacterForObject(playerObject) : characters[i % characters.Length] + 1);

                var spawnPosition = spawnPositions[randomIndices[i % spawnPositions.Count]];

                var rb = playerObject.GetComponent<Rigidbody2D>();
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;

                var networkTransform = playerObject.GetComponent<NetworkTransform>();
                networkTransform.Teleport(
                    spawnPosition.position,
                    spawnPosition.rotation,
                    playerObject.transform.localScale
                );

                var playerState = playerObject.GetComponent<PlayerState>();

                if (playerState != null)
                {
                    playerState.Reset();
                    playerObject.GetComponent<PlayerBeast>()?.Reset(visual != null ? visual.PlayerCharacter : PlayerVisual.Character.None);
                    playerObject.GetComponent<PlayerMove>()?.ResetForRound();
                    playerObject.GetComponent<PlayerPunch>()?.ResetForRound();
                }
            }
            roundPrepared = true;
        }
        if (NetworkManager.ServerTime.Time < fightStartTime.Value) return;
        CurrentState = RoundState.RoundInProgress;
    }

    private void RoundLoop()
    {
        RemoveDisconnectedPlayers();
        var match = GetComponent<MatchManager>();
        if (match?.IsTeamFight == true)
        {
            int teamsAlive = AliveTeams(match);
            if (teamsAlive == 0 || MatchRules.WinningTeam(teamsAlive) != MatchRules.NoTeam)
                CurrentState = RoundState.RoundEnding;
            return;
        }

        int nonEliminatedPlayers = players.Count(p => p != null && !p.GetComponent<PlayerState>().Eliminated);
        int participants = players.Count(p => p != null);

        // A one-player round is useful for practicing and testing controls.
        if (nonEliminatedPlayers <= (participants == 1 ? 0 : 1))
        {
            CurrentState = RoundState.RoundEnding;
        }
    }

    private int AliveTeams(MatchManager match)
    {
        int mask = 0;
        foreach (var fighter in players)
        {
            if (fighter == null || !fighter.IsSpawned || fighter.GetComponent<PlayerState>().Eliminated) continue;
            int team = match.GetTeamForObject(fighter);
            if (team > 0) mask |= 1 << (team - 1);
        }
        return mask;
    }

    private void EndRound()
    {
        var match = GetComponent<MatchManager>();
        if (match?.IsTeamFight == true)
        {
            winnerTeam.Value = MatchRules.WinningTeam(AliveTeams(match));
            winnerId.Value = winnerTeam.Value == 0 ? -1 : -2;
            CurrentState = RoundState.RoundFinished;
            return;
        }

        NetworkObject winner = players.FirstOrDefault(p => p != null && !p.GetComponent<PlayerState>().Eliminated);

        if (winner != null)
        {
            Debug.Log($"Player {winner.OwnerClientId} wins the round!");
        }
        else
        {
            Debug.Log("No winner this round.");
        }

        RoundWinnerClientId = winner != null ? winner.OwnerClientId : null;
        RoundWinnerObjectId = winner != null ? winner.NetworkObjectId : null;
        winnerId.Value = winner != null ? (int)winner.OwnerClientId : -1;

        CurrentState = RoundState.RoundFinished;
    }

    internal void EndSimulationRound()
    {
        if (!IsServer) return;
        RoundWinnerClientId = null;
        RoundWinnerObjectId = null;
        winnerId.Value = -1;
        winnerTeam.Value = 0;
        CurrentState = RoundState.RoundFinished;
    }

    private void FinishedRound()
    {

    }
}
