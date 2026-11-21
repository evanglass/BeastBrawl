using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class CharacterTest : MonoBehaviour
{
    [SerializeField]
    private NetworkObject playerPrefab;

    [SerializeField]
    private RoundManager roundManager;

    [SerializeField]
    private int dummyCount = 1;

    private PlayerVisual.Character selectedCharacter = PlayerVisual.Character.Eagle;
    private bool started;

    private void Awake()
    {
        roundManager.enabled = false;
    }

    private void OnGUI()
    {
        if (started)
            return;

        GUILayout.BeginArea(new Rect(20, 20, 250, 320), GUI.skin.box);
        GUILayout.Label("Character test");

        foreach (PlayerVisual.Character character in System.Enum.GetValues(typeof(PlayerVisual.Character)))
        {
            if (character != PlayerVisual.Character.None && GUILayout.Toggle(selectedCharacter == character, PlayerVisual.CharacterName(character)))
                selectedCharacter = character;
        }

        GUILayout.Label($"Training dummies: {dummyCount}");
        dummyCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(dummyCount, 1, 3));

        if (GUILayout.Button("Start test"))
            StartTest();

        GUILayout.EndArea();
    }

    private void StartTest()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (!network.StartHost())
        {
            Debug.LogError("Could not start the test host.");
            return;
        }

        started = true;
        NetworkObject player = network.LocalClient.PlayerObject;
        PreparePlayer(player, selectedCharacter, roundManager.spawnPositions[0].position);

        for (int i = 0; i < dummyCount; i++)
        {
            Vector3 position = roundManager.spawnPositions[1].position + Vector3.right * i;
            NetworkObject dummy = Instantiate(playerPrefab, position, Quaternion.identity);
            dummy.Spawn();
            PreparePlayer(dummy, PlayerVisual.Character.Bear, position);
        }
    }

    private void PreparePlayer(NetworkObject player, PlayerVisual.Character character, Vector3 position)
    {
        player.GetComponent<NetworkTransform>().Teleport(position, Quaternion.identity, player.transform.localScale);
        player.GetComponent<PlayerVisual>().AssignCharacter((int)character);
        player.GetComponent<PlayerState>().Reset();
        player.GetComponent<PlayerBeast>().Reset(character);
    }
}
