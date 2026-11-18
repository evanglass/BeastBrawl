using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [System.Serializable]
    private class PlayerCard
    {
        public Image background;
        public Text playerName;
        public Text characterName;
        public Text playerNumber;
        public Text iconLabel;
        public Image portrait;
    }

    [SerializeField]
    private MatchManager matchManager;

    [SerializeField]
    private GameObject lobbyPanel;

    [SerializeField]
    private Text statusText;

    [SerializeField]
    private Image panelBackground;

    [SerializeField]
    private Button readyButton;

    [SerializeField]
    private Text readyButtonText;

    [SerializeField]
    private PlayerCard[] playerCards;

    [SerializeField]
    private Sprite eaglePortrait;

    [SerializeField]
    private Sprite bearPortrait;

    [SerializeField]
    private Sprite cougarPortrait;

    [SerializeField]
    private Sprite snakePortrait;

    [SerializeField]
    private Color emptyCardColor = new Color32(21, 29, 40, 255);

    [SerializeField]
    private Color playerCardColor = new Color32(37, 58, 94, 255);

    [SerializeField]
    private Color localCardColor = new Color32(60, 94, 139, 255);

    private void Awake()
    {
        readyButton.onClick.AddListener(ReadyUp);
    }

    private void ReadyUp()
    {
        if (matchManager != null && matchManager.IsSpawned && matchManager.CurrentPhase == MatchManager.MatchPhase.Overview)
            matchManager.SetReadyRpc();
    }

    private void Update()
    {
        bool showLobby = matchManager != null && matchManager.IsSpawned && matchManager.IsClient &&
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient &&
            (matchManager.CurrentPhase == MatchManager.MatchPhase.Lobby ||
             matchManager.CurrentPhase == MatchManager.MatchPhase.LobbyCountdown ||
             matchManager.CurrentPhase == MatchManager.MatchPhase.LoadingArena ||
             matchManager.CurrentPhase == MatchManager.MatchPhase.Overview ||
             matchManager.CurrentPhase == MatchManager.MatchPhase.RoundCountdown);

        bool overview = showLobby && matchManager.CurrentPhase == MatchManager.MatchPhase.Overview &&
            NetworkManager.Singleton.ServerTime.Time >= matchManager.PhaseDeadline;
        bool countdown = showLobby && matchManager.CurrentPhase == MatchManager.MatchPhase.RoundCountdown;

        if (showLobby && matchManager.CurrentPhase == MatchManager.MatchPhase.Overview && !overview)
            showLobby = false;

        if (lobbyPanel.activeSelf != showLobby)
            lobbyPanel.SetActive(showLobby);

        if (!showLobby)
            return;

        panelBackground.color = new Color32(255, 255, 255, countdown ? (byte)0 : (byte)255);
        readyButton.gameObject.SetActive(overview);

        bool localReady = false;

        for (int i = 0; i < playerCards.Length; i++)
        {
            PlayerCard card = playerCards[i];
            card.background.gameObject.SetActive(!countdown);
            bool occupied = i < matchManager.PlayerCount;

            if (!occupied)
            {
                card.background.color = card.background.sprite != null ? Color.Lerp(emptyCardColor, Color.white, .65f) : emptyCardColor;
                card.playerName.text = "Waiting for player";
                card.characterName.text = "";
                card.playerNumber.text = "";
                card.iconLabel.text = "+";
                card.portrait.enabled = false;
                continue;
            }

            MatchManager.LobbyPlayer player = matchManager.GetPlayer(i);
            bool localPlayer = player.clientId == NetworkManager.Singleton.LocalClientId;
            if (localPlayer)
                localReady = player.ready;

            Color cardColor = localPlayer ? localCardColor : playerCardColor;
            card.background.color = card.background.sprite != null ? Color.Lerp(cardColor, Color.white, .75f) : cardColor;
            card.playerName.text = player.playerName;
            card.characterName.text = player.character.ToString().ToUpperInvariant();
            card.playerNumber.text = overview
                ? (localPlayer ? "YOU  •  " : "") + (player.ready ? "READY" : "NOT READY")
                : localPlayer ? "YOU" : "";
            card.iconLabel.text = "";
            card.portrait.sprite = GetPortrait(player.character);
            card.portrait.enabled = card.portrait.sprite != null;
        }

        readyButton.interactable = !localReady;
        readyButtonText.text = localReady ? "READY" : "READY UP";

        if (matchManager.CurrentPhase == MatchManager.MatchPhase.LobbyCountdown)
        {
            double remaining = matchManager.PhaseDeadline - NetworkManager.Singleton.ServerTime.Time;
            statusText.text = $"MATCH STARTING IN  {Mathf.CeilToInt((float)System.Math.Max(0d, remaining))}";
        }
        else if (matchManager.CurrentPhase == MatchManager.MatchPhase.LoadingArena)
        {
            statusText.text = "GETTING THE ARENA READY";
        }
        else if (overview)
        {
            statusText.text = "READY UP";
        }
        else if (countdown)
        {
            RoundManager round = matchManager.GetComponent<RoundManager>();
            double remaining = round.FightStartTime - NetworkManager.Singleton.ServerTime.Time;
            statusText.text = round.FightStartTime > 0 ? Mathf.Max(1, Mathf.CeilToInt((float)remaining)).ToString() : "";
        }
        else
        {
            statusText.text = "WAITING FOR PLAYERS";
        }
    }

    private Sprite GetPortrait(PlayerVisual.Character character)
    {
        return character switch
        {
            PlayerVisual.Character.Eagle => eaglePortrait,
            PlayerVisual.Character.Bear => bearPortrait,
            PlayerVisual.Character.Cougar => cougarPortrait,
            PlayerVisual.Character.Snake => snakePortrait,
            _ => null
        };
    }
}
