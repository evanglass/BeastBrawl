using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class RoundManager : NetworkBehaviour
{
    public List<Transform> spawnPositions;
    public List<NetworkObject> players;
    public int playersPerRound = 2;

    private NetworkVariable<int> winnerId = new(
        -2,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int WinnerId => winnerId.Value;

    public enum RoundState
    {
        WaitingForPlayers,
        RoundStarting,
        RoundInProgress,
        RoundEnding,
        RoundFinished
    }

    public RoundState CurrentState { get; private set; } = RoundState.WaitingForPlayers;

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

            if (players.Count >= playersPerRound)
            {
                CurrentState = RoundState.RoundStarting;
            }
        }
    }

    private void StartRound()
    {
        RemoveDisconnectedPlayers();
        if (players.Count < playersPerRound)
        {
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
                visual.AssignCharacter(characters[i % characters.Length]);
            
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
            }
        }
        CurrentState = RoundState.RoundInProgress;
    }

    private void RoundLoop()
    {
        int nonEliminatedPlayers = players.Count(p => p != null && !p.GetComponent<PlayerState>().Eliminated);
        
        if (nonEliminatedPlayers <= 1)
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

        winnerId.Value = winner != null ? (int)winner.OwnerClientId : -1;

        CurrentState = RoundState.RoundFinished;
    }

    private void FinishedRound()
    {
        
    }
}
