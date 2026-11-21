using System.Text;
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

    [SerializeField] private Sprite hawkPortrait;
    [SerializeField] private Sprite blackBearPortrait;
    [SerializeField] private Sprite pantherPortrait;
    [SerializeField] private Sprite mambaPortrait;

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

    private Text[] teamRosters;
    private Button switchTeamButton;
    private Button leaveLobbyButton;
    private Button characterButton;
    private int selectedCharacter;

    private void Awake()
    {
        readyButton.onClick.AddListener(ReadyUp);
    }

    private void CreateCustomControls()
    {
        if (teamRosters != null) return;
        teamRosters = new Text[2];
        for (int i = 0; i < teamRosters.Length; i++)
        {
            var obj = new GameObject("Team " + (i + 1) + " Roster", typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(lobbyPanel.transform, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = new Vector2(.08f + i * .45f, .24f);
            rect.anchorMax = new Vector2(.47f + i * .45f, .73f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = obj.GetComponent<Text>();
            text.font = statusText.font;
            text.fontSize = 28;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 13;
            text.resizeTextMaxSize = 28;
            text.alignment = TextAnchor.UpperCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            teamRosters[i] = text;
        }

        switchTeamButton = MakeLobbyButton("SWITCH TEAM", -180, () => matchManager.SwitchTeamRpc());
        leaveLobbyButton = MakeLobbyButton("LEAVE LOBBY", 180, LeaveLobby);
        characterButton = MakeLobbyButton("CHARACTER", 0, () =>
        {
            int next = selectedCharacter >= MatchRules.LastCharacterForTeam(2) ? 1 : selectedCharacter + 1;
            matchManager.ChooseCustomCharacterRpc(next);
        });
        var characterRect = (RectTransform)characterButton.transform;
        characterRect.anchoredPosition = new Vector2(0, 123);
        characterRect.sizeDelta = new Vector2(400, 52);
    }

    private Button MakeLobbyButton(string label, float x, UnityEngine.Events.UnityAction action)
    {
        var button = Instantiate(readyButton, lobbyPanel.transform);
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
        rect.anchoredPosition = new Vector2(x, 60);
        rect.sizeDelta = new Vector2(310, 56);
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
        var text = button.GetComponentInChildren<Text>(true);
        if (text != null) text.text = label;
        return button;
    }

    private void LeaveLobby()
    {
        matchManager.LeaveCustomLobbyRpc();
        var lan = FindFirstObjectByType<LANController>();
        if (lan == null) return;
        var browser = lan.GetComponent<LocalCustomGameUI>();
        if (browser == null) browser = lan.gameObject.AddComponent<LocalCustomGameUI>();
        browser.OpenBrowser();
    }

    private void UpdateCustomControls(bool overview, bool countdown)
    {
        if (!matchManager.CustomGame && teamRosters == null) return;
        CreateCustomControls();
        bool teams = matchManager.IsTeamFight && !countdown;
        for (int team = 1; team <= 2; team++)
        {
            var text = teamRosters[team - 1];
            text.gameObject.SetActive(teams);
            if (!teams) continue;
            var roster = new StringBuilder(team == 1 ? "TEAM 1\n\n" : "TEAM 2\n\n");
            for (int i = 0; i < matchManager.TeamSize; i++)
            {
                int slot = (team - 1) * matchManager.TeamSize + i;
                if (!matchManager.TryGetSlot(slot, out var player))
                    roster.AppendLine($"{i + 1}.  Open human slot");
                else
                {
                    bool local = !player.isBot && player.clientId == NetworkManager.Singleton.LocalClientId;
                    string role = player.isBot ? " · BOT" : local ? " · YOU" : "";
                    string ready = overview && !player.isBot ? (player.ready ? " · READY" : " · NOT READY") : "";
                    roster.AppendLine($"{i + 1}.  {player.playerName} · {PlayerVisual.CharacterName(player.character)}{role}{ready}");
                }
            }
            text.text = roster.ToString();
        }

        var phase = matchManager.CurrentPhase;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        switchTeamButton.gameObject.SetActive(teams && phase == MatchManager.MatchPhase.Lobby);
        switchTeamButton.interactable = matchManager.CanSwitchTeam(localId);
        bool canChoose = matchManager.CanChooseCustomCharacter(localId);
        characterButton.gameObject.SetActive(canChoose);
        if (canChoose)
        {
            for (int slot = matchManager.TeamSize; slot < matchManager.TeamSize * 2; slot++)
            {
                if (!matchManager.TryGetSlot(slot, out var player) || player.isBot || player.clientId != localId) continue;
                selectedCharacter = (int)player.character;
                var characterLabel = characterButton.GetComponentInChildren<Text>(true);
                if (characterLabel != null) characterLabel.text = "CHARACTER · " + PlayerVisual.CharacterName(player.character);
                break;
            }
        }
        bool canLeave = matchManager.CustomGame &&
            (phase == MatchManager.MatchPhase.Lobby || phase == MatchManager.MatchPhase.LobbyCountdown);
        leaveLobbyButton.gameObject.SetActive(canLeave);
        var label = leaveLobbyButton.GetComponentInChildren<Text>(true);
        if (label != null) label.text = matchManager.IsLobbyHost(localId) ? "CANCEL LOBBY" : "LEAVE LOBBY";
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

        // A connected test client may not have joined a lobby yet.
        if (showLobby) showLobby = matchManager.HasLobbyMember(NetworkManager.Singleton.LocalClientId);
        var customScreen = FindFirstObjectByType<LocalCustomGameUI>();
        if (customScreen != null && customScreen.IsOpen) showLobby = false;

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
        readyButton.gameObject.SetActive(overview && matchManager.HasHuman(NetworkManager.Singleton.LocalClientId));

        bool localReady = false;
        for (int i = 0; i < matchManager.PlayerCount; i++)
        {
            var member = matchManager.GetPlayer(i);
            if (!member.isBot && member.clientId == NetworkManager.Singleton.LocalClientId) localReady = member.ready;
        }
        UpdateCustomControls(overview, countdown);

        for (int i = 0; i < playerCards.Length; i++)
        {
            PlayerCard card = playerCards[i];
            card.background.gameObject.SetActive(!matchManager.IsTeamFight && !countdown && i < matchManager.RequiredPlayers);
            bool occupied = matchManager.TryGetSlot(i, out var player);

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

            bool localPlayer = player.clientId == NetworkManager.Singleton.LocalClientId;
            if (localPlayer)
                localReady = player.ready;

            Color cardColor = localPlayer ? localCardColor : playerCardColor;
            card.background.color = card.background.sprite != null ? Color.Lerp(cardColor, Color.white, .75f) : cardColor;
            card.playerName.text = player.playerName;
            card.characterName.text = PlayerVisual.CharacterName(player.character).ToUpperInvariant();
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
            statusText.text = matchManager.IsTeamFight
                ? $"{MatchRules.ModeLabel(matchManager.GameMode, matchManager.TeamSize)}  ·  {matchManager.PlayerCount}/{matchManager.RequiredPlayers}"
                : $"WAITING FOR PLAYERS  {matchManager.PlayerCount}/{matchManager.RequiredPlayers}";
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
            PlayerVisual.Character.Hawk => hawkPortrait,
            PlayerVisual.Character.BlackBear => blackBearPortrait,
            PlayerVisual.Character.Panther => pantherPortrait,
            PlayerVisual.Character.Mamba => mambaPortrait,
            _ => null
        };
    }
}
