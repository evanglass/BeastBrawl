using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Local custom-game screens use the existing Unity menu controls and artwork.
public sealed class LocalCustomGameUI : MonoBehaviour
{
    private LANConnect menu;
    private GameObject screen;
    private Button buttonTemplate;
    private Font font;
    private bool waitingForMembership;
    private float membershipDeadline;
    private bool virtualPlayer;
    private string page;
    private string playerName = "";
    private string message = "";
    private int wins = 3, capacity = 4, teamSize = 2;
    private MatchGameMode mode;
    private readonly int[] customSlots = new int[MatchRules.MaxSlots];
    private readonly int[] simulationSlots = { 1, 2, 3, 4, -1, -1, -1, -1 };
    private CustomLobbyListing[] listings = Array.Empty<CustomLobbyListing>();
    private int browserPage, selectedLobby = -1;
    private Coroutine refreshRoutine;
    private bool publicConnection;
    private bool sendHostSettings;
    private bool joinedWaitingLobby;
    private LANController lan;
#if UNITY_EDITOR
    private bool simulation;
    private string simulationRounds = "10";
#endif
    private static readonly string[] scenes = SceneLoader.CustomArenas;
    private readonly string[] names = { "Arena 1", "Arena 2", "Arena 3", "Forest", "Enchanted Forest" };
    private readonly bool[] selected = { true, true, true, false, true };
    public bool IsOpen => screen != null;

    public void Open(bool isVirtualPlayer)
    {
        virtualPlayer = isVirtualPlayer;
        lan = GetComponent<LANController>();
        menu = FindFirstObjectByType<LANConnect>();
        if (menu == null || menu.LocalMainMenu == null) return;
        buttonTemplate = menu.LocalMainMenu.GetComponentInChildren<Button>(true);
        var text = buttonTemplate != null ? buttonTemplate.GetComponentInChildren<Text>(true) : null;
        font = text != null ? text.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Show("play");
    }

    public void OpenBrowser()
    {
        if (menu == null) Open(false);
        if (menu == null) return;
        joinedWaitingLobby = false;
        message = "";
        Show("join");
        RefreshList();
    }

    private void Show(string next)
    {
        page = next;
        waitingForMembership = false;
        if (screen != null) Destroy(screen);
        menu.LocalMainMenu.SetActive(false);
        screen = new GameObject("Custom Game Screen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        screen.transform.SetParent(menu.LocalMainMenu.transform.parent, false);
        var rect = (RectTransform)screen.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        var bg = screen.GetComponent<Image>();
        bg.color = new Color32(15, 30, 46, 255);
        screen.transform.SetAsLastSibling();
        Draw();
    }

    private RectTransform Place(GameObject item, float x, float y, float width, float height)
    {
        item.transform.SetParent(screen.transform, false);
        var r = (RectTransform)item.transform;
        r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
        r.pivot = new Vector2(.5f, .5f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(width, height);
        return r;
    }

    private Text Label(string text, float x, float y, float width, int size = 30)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        Place(go, x, y, width, 54);
        var label = go.GetComponent<Text>();
        label.font = font; label.fontSize = size; label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter; label.text = text; label.raycastTarget = false;
        return label;
    }

    private void Action(string text, float x, float y, float width, UnityEngine.Events.UnityAction action)
    {
        if (buttonTemplate == null) return;
        var button = Instantiate(buttonTemplate, screen.transform);
        button.gameObject.name = text;
        Place(button.gameObject, x, y, width, 62);
        button.gameObject.SetActive(true);
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
        var label = button.GetComponentInChildren<Text>(true);
        if (label != null) { label.text = text; label.fontSize = 27; }
    }

    private void NameField(float y)
    {
        Label("PLAYER NAME", 0, y + 53, 640, 25);
        var input = Instantiate(menu.LocalNameTemplate, screen.transform);
        input.gameObject.name = "Custom Player Name";
        Place(input.gameObject, 0, y, 620, 58);
        input.gameObject.SetActive(true);
        input.onValueChanged = new InputField.OnChangeEvent();
        input.onEndEdit = new InputField.EndEditEvent();
        input.text = playerName;
        input.onValueChanged.AddListener(value => playerName = value);
    }

    private void Close()
    {
        if (refreshRoutine != null) StopCoroutine(refreshRoutine);
        refreshRoutine = null;
        if (screen != null) Destroy(screen);
        screen = null;
        waitingForMembership = false;
        if (menu != null && menu.LocalMainMenu != null) menu.LocalMainMenu.SetActive(true);
    }

    private void Back(string target)
    {
        message = "";
        if (waitingForMembership && publicConnection && NetworkManager.Singleton?.IsListening == true)
            NetworkManager.Singleton.Shutdown();
        waitingForMembership = sendHostSettings = publicConnection = false;
        if (target == "") Close(); else Show(target);
    }

    private void Draw()
    {
        if (page == "play")
        {
            Label("PLAY GAME", 0, 320, 800, 48);
            Action("Online Game", 0, 160, 620, () =>
            {
                Close();
                menu.OpenQuickOnline();
            });
            Action("Custom Game", 0, 60, 620, () => Back("custom"));
            if (Application.platform != RuntimePlatform.WebGLPlayer)
                Action("LAN", 0, -40, 620, () =>
                {
#if UNITY_EDITOR
                    if (lan != null && lan.IsLocalEditorSession)
                    {
                        message = "LAN is not available in this local test.";
                        Show("play");
                        return;
                    }
#endif
                    Close();
                    menu.OpenLanMenu();
                });
            Action("Back", 0, -300, 400, () => Back(""));
        }
        else if (page == "custom")
        {
            Label("CUSTOM GAME", 0, 320, 800, 48);
            Action("Start Custom Game", 0, 130, 620, () => {
                if (virtualPlayer) { message = "Start the game from Player 1."; Show("custom"); }
                else Back("host");
            });
            Action("Browse Custom Games", 0, 30, 620, OpenBrowser);
            Action("Back", 0, -300, 400, () => Back("play"));
        }
        else if (page == "join")
        {
            DrawBrowser();
        }
        else
        {
            DrawHost();
        }
        if (!string.IsNullOrEmpty(message)) Label(message, 0, -360, 1200, 24);
    }

    private void DrawBrowser()
    {
        Label("CUSTOM GAMES", 0, 355, 900, 48);
        NameField(240);
        const int pageSize = 4;
        if (listings.Length == 0) Label("No joinable lobbies found.", 0, 65, 900, 26);
        for (int i = browserPage * pageSize; i < Mathf.Min(listings.Length, (browserPage + 1) * pageSize); i++)
        {
            int index = i;
            var listing = listings[i];
            string modeLabel = MatchRules.ModeLabel((MatchGameMode)listing.mode, listing.teamSize);
            Action($"{(selectedLobby == i ? "✓   " : "")}{listing.host}   ·   {modeLabel}   ·   " +
                $"{listing.occupied}/{listing.capacity}   ·   {listing.availableHumans} human open",
                0, 132 - (i % pageSize) * 67, 1250, () => { selectedLobby = index; Show("join"); });
        }
        Action("Refresh", -410, -180, 230, RefreshList);
        Action("Join Selected", 0, -180, 340, JoinSelected);
        if (listings.Length > pageSize)
            Action($"Page {browserPage + 1}/{(listings.Length + pageSize - 1) / pageSize}", 415, -180, 240,
                () => { browserPage = (browserPage + 1) % ((listings.Length + pageSize - 1) / pageSize); Show("join"); });
        Action("Back", 0, -278, 330, () => Back("custom"));
    }

    private void RefreshList()
    {
        if (lan == null || waitingForMembership) return;
        if (refreshRoutine != null) StopCoroutine(refreshRoutine);
        message = "Refreshing lobbies…";
        Show("join");
        refreshRoutine = StartCoroutine(lan.BrowseCustomLobbies((result, error) =>
        {
            if (screen == null || page != "join" || waitingForMembership) return;
            listings = result;
            selectedLobby = -1;
            browserPage = 0;
            message = error ?? "";
            Show("join");
        }));
    }

    private void JoinSelected()
    {
        if (waitingForMembership) return;
        if (!MatchManager.ValidName(playerName)) message = "Enter a valid player name (1–20 characters).";
        else if (selectedLobby < 0 || selectedLobby >= listings.Length) message = "Select a lobby first.";
        else
        {
            var selected = listings[selectedLobby];
            if (string.IsNullOrEmpty(selected.id))
            {
                var match = FindFirstObjectByType<MatchManager>();
                if (match != null && match.CustomAvailable && match.AvailableHumanSlots > 0)
                {
                    match.SubmitPlayerNameRpc(playerName);
                    WaitForLobby("join", "Joining lobby…", isPublic: false);
                    return;
                }
                RefreshList();
                return;
            }

            if (lan.SetPlayerName(playerName) && lan.ConnectPublicCustomLobby(selected.id))
            {
                WaitForLobby("join", "Joining lobby…", isPublic: true);
                return;
            }
            message = "Could not connect. Refresh the list and try again.";
        }
        Show("join");
    }

    private void WaitForLobby(string target, string status, bool isPublic, bool configure = false)
    {
        message = status;
        Show(target);
        publicConnection = isPublic;
        sendHostSettings = configure;
        waitingForMembership = true;
        membershipDeadline = Time.unscaledTime + (isPublic ? 40f : 5f);
        foreach (var button in screen.GetComponentsInChildren<Button>())
            if (button.name != "Back") button.interactable = false;
        foreach (var input in screen.GetComponentsInChildren<InputField>())
            input.interactable = false;
    }

    private void DrawHost()
    {
        Label(page == "host" ? "START CUSTOM GAME" : "CUSTOM GAME LOBBY", 0, 430, 1100, 42);
#if UNITY_EDITOR
        bool offerSimulation = !virtualPlayer && GameGlobals.Instance?.simulationMode == true &&
            FindFirstObjectByType<LANController>()?.IsLocalEditorSession == true;
        if (offerSimulation)
        {
            Action(simulation ? "Simulation Mode: On" : "Simulation Mode: Off", 0, 266, 470, () =>
            {
                simulation = !simulation;
                message = "";
                Show("host");
            });
        }
        else simulation = false;
        if (!simulation && page == "host") NameField(offerSimulation ? 342 : 305);
#else
        if (page == "host") NameField(305);
#endif
        Label("ARENAS", -410, 200, 420, 28);
        for (int i = 0; i < scenes.Length; i++)
        {
            int index = i;
            bool available = SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/" + scenes[i] + ".unity") >= 0;
            Action((selected[i] ? "✓  " : "○  ") + names[i] + (available ? "" : " (unavailable)"),
                -410, 134 - i * 65, 440, () => {
                    if (available) selected[index] = !selected[index];
                    else message = "This arena is not enabled in the active build profile.";
                    Show(page);
                });
        }
        bool botsOnly = false;
#if UNITY_EDITOR
        botsOnly = simulation;
#endif
        Label(botsOnly ? "SIMULATION SETTINGS" : "MATCH SETTINGS", 390, 210, 690, 28);
        Action(MatchRules.ModeLabel(mode, teamSize), 390, 145, 530, () =>
        {
            mode = mode == MatchGameMode.TeamFight ? MatchGameMode.FreeForAll : MatchGameMode.TeamFight;
            Show("host");
        });
        Action(mode == MatchGameMode.TeamFight ? $"Team size: {teamSize}v{teamSize}" : "Lobby size: " + capacity,
            390, 72, 530, () =>
            {
                if (mode == MatchGameMode.TeamFight) teamSize = teamSize == 4 ? 2 : teamSize + 1;
                else capacity = capacity == 4 ? 2 : capacity + 1;
                Show("host");
            });

        var roster = botsOnly ? simulationSlots : customSlots;
        int count = mode == MatchGameMode.TeamFight ? teamSize * 2 : capacity;
        for (int i = 0; i < count; i++)
        {
            int slot = i;
            int team = MatchRules.TeamForSlot(mode, teamSize, i);
            int row = team == 0 ? i : i % teamSize;
            float x = team == 0 ? 390 : team == 1 ? 220 : 566;
            // Resizing teams can move a saved Team 2 choice into another slot.
            if (roster[i] > 0 && !MatchRules.CharacterAllowed(roster[i], team))
                roster[i] = (int)PlayerVisual.OriginalCharacter((PlayerVisual.Character)roster[i]);
            string value = roster[i] == MatchRules.Human ? "Human" :
                roster[i] == MatchRules.RandomBot ? "Random Bot" : PlayerVisual.CharacterName((PlayerVisual.Character)roster[i]) + " Bot";
            Action($"{(team > 0 ? "T" + team + " · " : "")}{row + 1}: {value}", x, -5 - row * 67,
                team == 0 ? 520 : 330, () =>
                {
                    int choice = roster[slot];
                    roster[slot] = choice == MatchRules.LastCharacterForTeam(team) ? MatchRules.RandomBot :
                        choice == MatchRules.RandomBot ? (botsOnly ? 1 : MatchRules.Human) : choice + 1;
                    Show("host");
                });
        }
#if UNITY_EDITOR
        if (simulation)
        {
            Label("Rounds (10–100)", -415, -215, 410, 24);
            var input = Instantiate(menu.LocalNameTemplate, screen.transform);
            Place(input.gameObject, -415, -266, 300, 58);
            input.onValueChanged = new InputField.OnChangeEvent();
            input.onEndEdit = new InputField.EndEditEvent();
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = 3;
            input.text = simulationRounds;
            input.onValueChanged.AddListener(value => simulationRounds = value);
            input.gameObject.SetActive(true);
            Action("Start Simulation", 315, -325, 455, StartSimulation);
        }
        else
#endif
        {
            Action("Crystals to win: " + wins, -409, -250, 440, () => { wins = wins % 3 + 1; Show("host"); });
            Action("Open Custom Lobby", 315, -325, 455, OpenLobby);
        }
        Action("Back", -325, -325, 330, () => Back("custom"));
    }

    private int[] SelectedRoster(bool botsOnly)
    {
        int count = mode == MatchGameMode.TeamFight ? teamSize * 2 : capacity;
        var roster = new int[count];
        Array.Copy(botsOnly ? simulationSlots : customSlots, roster, count);
        return roster;
    }

    private void Update()
    {
        var network = NetworkManager.Singleton;
        var match = FindFirstObjectByType<MatchManager>();
        if (joinedWaitingLobby)
        {
            if (match != null && match.IsSpawned && match.CurrentPhase >= MatchManager.MatchPhase.LoadingArena)
                joinedWaitingLobby = false;
            else if (network?.IsConnectedClient != true || match == null || !match.HasLobbyMember(network.LocalClientId))
                OpenBrowser();
        }
        if (!waitingForMembership) return;

        if (network?.IsConnectedClient == true && match != null && match.IsSpawned)
        {
            if (sendHostSettings)
            {
                sendHostSettings = false;
                membershipDeadline = Time.unscaledTime + 5f;
                SendLobbySettings(match);
            }
            if (match.HasLobbyMember(network.LocalClientId))
            {
                ShowJoinedLobby();
                return;
            }
        }

        if (Time.unscaledTime < membershipDeadline && (!publicConnection || network?.IsListening == true)) return;
        if (publicConnection && network?.IsListening == true) network.Shutdown();
        publicConnection = sendHostSettings = false;
        message = page == "host" ? "Could not open the lobby." : "Could not join. Refresh the list and try again.";
        Show(page);
    }

    private void ShowJoinedLobby(bool trackMembership = true)
    {
        Close();
        joinedWaitingLobby = trackMembership;
        menu.LocalMainMenu.SetActive(false);
    }

    private int SelectedArenaMask()
    {
        int arenaMask = 0;
        for (int i = 0; i < scenes.Length; i++)
            if (selected[i] && SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/" + scenes[i] + ".unity") >= 0)
                arenaMask |= 1 << i;
        return arenaMask;
    }

#if UNITY_EDITOR
    private void StartSimulation()
    {
        var match = FindFirstObjectByType<MatchManager>();
        int arenaMask = SelectedArenaMask();
        if (!int.TryParse(simulationRounds, out int rounds) || rounds < MatchManager.MinSimulationRounds ||
            rounds > MatchManager.MaxSimulationRounds)
            message = "Enter a round count from 10 to 100.";
        else if (arenaMask == 0) message = "Select at least one available arena.";
        else if (match == null || !match.StartSimulation(rounds, arenaMask, mode, teamSize, SelectedRoster(botsOnly: true)))
            message = "Could not start the simulation.";
        else
        {
            ShowJoinedLobby(trackMembership: false);
            return;
        }

        Show("host");
    }
#endif

    private void OpenLobby()
    {
        if (waitingForMembership) return;
        var match = FindFirstObjectByType<MatchManager>();
        if (!MatchManager.ValidName(playerName)) message = "Enter a valid player name (1–20 characters).";
        else if (SelectedArenaMask() == 0) message = "Select at least one available arena.";
        else if (NetworkManager.Singleton?.IsConnectedClient == true && match?.IsSpawned == true)
        {
            if (match.CustomGame) message = "A lobby is already open. Choose Browse Custom Games.";
            else
            {
                SendLobbySettings(match);
                WaitForLobby("host", "Opening lobby…", isPublic: false);
                return;
            }
        }
        else if (lan != null && lan.ConnectPublicCustomLobby(null))
        {
            WaitForLobby("host", "Opening lobby…", isPublic: true, configure: true);
            return;
        }
        else message = "Could not connect to the custom game service.";
        Show("host");
    }

    private void SendLobbySettings(MatchManager match) =>
        match.CreateCustomLobbyRpc(playerName, wins, mode, teamSize, SelectedArenaMask(), SelectedRoster(botsOnly: false));
}
