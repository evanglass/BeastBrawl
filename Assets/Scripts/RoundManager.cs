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

    private NetworkVariable<int> winnerId = new(
        -2,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int WinnerId => winnerId.Value;
    public ulong? RoundWinnerClientId { get; private set; }

    public void ResetRound()
    {
        if (!IsServer)
            return;

        players.Clear();
        roundPrepared = false;
        fightStartTime.Value = 0;
        winnerId.Value = -2;
        RoundWinnerClientId = null;
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

        spawnPositions = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None)
            .Where(point => point.gameObject.scene == arena)
            .Select(point => point.transform)
            .ToList();

        var eagleCorners = FindObjectsByType<EagleCorner>(FindObjectsSortMode.None)
            .Where(corner => corner.gameObject.scene == arena)
            .ToList();
        eagleLeftCorner = eagleCorners.FirstOrDefault(c => c.LeftCorner)?.transform;
        eagleRightCorner = eagleCorners.FirstOrDefault(c => !c.LeftCorner)?.transform;

        if (spawnPositions.Count < playersPerRound)
        {
            Debug.LogError($"Arena {arena.name} has {spawnPositions.Count} spawn points; {playersPerRound} are required.");
            return false;
        }

        arenaReady = true;
        return true;
    }

    private void RemoveDisconnectedPlayers()
    {
        players.RemoveAll(player =>
            player == null ||
            !player.IsSpawned ||
            !NetworkManager.ConnectedClients.ContainsKey(player.OwnerClientId)
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

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;

            if (playerObject == null)
                continue;

            if (playerObject == null || !playerObject.IsSpawned)
                continue;

            PlayerState playerState = playerObject.GetComponent<PlayerState>();

            if (playerState != null && !players.Contains(playerObject))
            {
                players.Add(playerObject);
            }

        }

        var matchManager = GetComponent<MatchManager>();
        if (matchManager != null && matchManager.CurrentPhase != MatchManager.MatchPhase.RoundCountdown)
            return;

        if (matchManager != null && players.Any(player => matchManager.GetCharacter(player.OwnerClientId) == PlayerVisual.Character.None))
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
            if (matchManager != null && players.Any(player => matchManager.GetCharacter(player.OwnerClientId) == PlayerVisual.Character.None))
            {
                fightStartTime.Value = 0;
                CurrentState = RoundState.WaitingForPlayers;
                return;
            }

            var randomIndices = Enumerable.Range(0, spawnPositions.Count).OrderBy(x => Random.value).ToArray();
            var characters = Enumerable.Range(0, 4).OrderBy(x => Random.value).ToArray();

            for (int i = 0; i < players.Count; i++)
            {
                var playerObject = players[i];
                var visual = playerObject.GetComponent<PlayerVisual>();
                if (visual != null)
                    visual.AssignCharacter(matchManager != null ? (int)matchManager.GetCharacter(playerObject.OwnerClientId) : characters[i % characters.Length] + 1);

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
        int nonEliminatedPlayers = players.Count(p => p != null && !p.GetComponent<PlayerState>().Eliminated);

        // A one-player round is useful for practicing and testing controls.
        if (nonEliminatedPlayers <= (playersPerRound == 1 ? 0 : 1))
        {
            CurrentState = RoundState.RoundEnding;
        }
    }

    private void EndRound()
    {
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
        winnerId.Value = winner != null ? (int)winner.OwnerClientId : -1;

        CurrentState = RoundState.RoundFinished;
    }

    private void FinishedRound()
    {

    }
}
