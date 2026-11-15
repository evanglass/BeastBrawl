using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MatchResultsUI : MonoBehaviour
{
    [Serializable]
    private class ResultRow
    {
        public GameObject root;
        public Image portrait;
        public Text playerName;
        public Text[] wins;
    }

    [SerializeField] private MatchManager matchManager;
    [SerializeField] private SceneLoader sceneLoader;
    [SerializeField] private GameObject eventSystem;
    [SerializeField] private GameObject panel;
    [SerializeField] private Text title;
    [SerializeField] private Text status;
    [SerializeField] private ResultRow[] rows;
    [SerializeField] private Image winnerPortrait;
    [SerializeField] private Image winnerAnimation;
    [SerializeField] private Button returnButton;
    [SerializeField] private Sprite[] eagleFrames;
    [SerializeField] private Sprite[] bearFrames;
    [SerializeField] private Sprite[] cougarFrames;
    [SerializeField] private Sprite[] snakeFrames;

    private NetworkManager networkManager;
    private bool disconnected;
    private bool leaving;
    private bool wasConnected;

    private void Start()
    {
        returnButton.onClick.AddListener(ReturnToMenu);
        networkManager = NetworkManager.Singleton;
        if (networkManager != null)
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnDestroy()
    {
        if (networkManager != null)
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!leaving && wasConnected && networkManager != null && clientId == networkManager.LocalClientId)
            disconnected = true;
    }

    private void ReturnToMenu()
    {
        leaving = true;
        disconnected = false;
        wasConnected = false;
        panel.SetActive(false);
        eventSystem.SetActive(false);
        sceneLoader.ReturnToMenu();
    }

    private void Update()
    {
        if (leaving)
        {
            if (networkManager != null && networkManager.IsListening)
                return;

            leaving = false;
        }

        bool connected = matchManager != null && matchManager.IsSpawned && matchManager.IsClient &&
            networkManager != null && networkManager.IsConnectedClient;

        if (connected)
        {
            wasConnected = true;
        }

        MatchManager.MatchPhase phase = connected ? matchManager.CurrentPhase : MatchManager.MatchPhase.Lobby;
        bool overview = connected && phase == MatchManager.MatchPhase.Overview &&
            networkManager.ServerTime.Time >= matchManager.PhaseDeadline;
        bool results = connected && phase == MatchManager.MatchPhase.RoundResults;
        bool finished = connected && phase == MatchManager.MatchPhase.MatchFinished;
        bool aborted = disconnected || (connected && phase == MatchManager.MatchPhase.Aborted);
        bool visible = results || finished || aborted;

        eventSystem.SetActive(overview || finished || aborted);
        if (panel.activeSelf != visible)
            panel.SetActive(visible);

        if (!visible)
            return;

        returnButton.gameObject.SetActive(finished || aborted);
        winnerPortrait.gameObject.SetActive(finished);
        winnerAnimation.gameObject.SetActive(finished);

        for (int i = 0; i < rows.Length; i++)
        {
            ResultRow row = rows[i];
            bool show = results && i < matchManager.PlayerCount;
            row.root.SetActive(show);
            if (!show)
                continue;

            MatchManager.LobbyPlayer player = matchManager.GetPlayer(i);
            row.playerName.text = player.playerName;
            Sprite[] frames = GetFrames(player.character);
            row.portrait.sprite = frames.Length > 0 ? frames[0] : null;

            for (int win = 0; win < row.wins.Length; win++)
                row.wins[win].gameObject.SetActive(win < player.wins);
        }

        if (aborted)
        {
            title.text = "MATCH ENDED";
            status.text = "A player disconnected or the arena could not be loaded.";
            return;
        }

        if (results)
        {
            RoundManager round = matchManager.GetComponent<RoundManager>();
            title.text = round.WinnerId == -1 ? "DRAW" : "ROUND COMPLETE";
            double remaining = matchManager.PhaseDeadline - networkManager.ServerTime.Time;
            status.text = $"NEXT ROUND IN  {Mathf.CeilToInt((float)Math.Max(0d, remaining))}";
            return;
        }

        title.text = "VICTORY";
        status.text = "";
        for (int i = 0; i < matchManager.PlayerCount; i++)
        {
            MatchManager.LobbyPlayer player = matchManager.GetPlayer(i);
            if (player.wins < 3)
                continue;

            status.text = player.playerName + " WINS!";
            Sprite[] frames = GetFrames(player.character);
            if (frames.Length > 0)
            {
                winnerPortrait.sprite = frames[0];
                winnerAnimation.sprite = frames[Mathf.FloorToInt(Time.unscaledTime * 6f) % frames.Length];
            }
            break;
        }
    }

    private Sprite[] GetFrames(PlayerVisual.Character character)
    {
        return character switch
        {
            PlayerVisual.Character.Eagle => eagleFrames,
            PlayerVisual.Character.Bear => bearFrames,
            PlayerVisual.Character.Cougar => cougarFrames,
            PlayerVisual.Character.Snake => snakeFrames,
            _ => Array.Empty<Sprite>()
        };
    }
}
