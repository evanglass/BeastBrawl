#if UNITY_EDITOR
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public sealed class SimulationMatchUI : MonoBehaviour
{
    private static readonly Color Background = new(0.06f, 0.12f, 0.19f, 0.97f);
    private static readonly Color ButtonColor = new(0.19f, 0.32f, 0.50f);
    private MatchManager match;
    private GameObject canvasRoot;
    private GameObject livePanel;
    private GameObject finalPanel;
    private Text liveTitle;
    private Text liveScores;
    private Text finalTitle;
    private Text finalScores;
    private Button resetButton;
    private Font font;
    private int displayedRounds = -1;

    internal void Initialize(MatchManager manager, SceneLoader sceneLoader)
    {
        match = manager;
        if (canvasRoot != null) return;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        canvasRoot = new GameObject("Simulation UI", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasRoot.transform.SetParent(transform, false);
        var canvas = canvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;
        var scaler = canvasRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var live = Panel("Live simulation scores", canvasRoot.transform, Background);
        live.anchorMin = live.anchorMax = live.pivot = Vector2.one;
        live.anchoredPosition = new Vector2(-24, -24);
        live.sizeDelta = new Vector2(370, 310);
        livePanel = live.gameObject;
        liveTitle = Label(live, new Vector2(0, 116), new Vector2(320, 48), 28, TextAnchor.MiddleCenter);
        liveScores = Label(live, new Vector2(0, 5), new Vector2(310, 166), 24, TextAnchor.UpperLeft);
        resetButton = CreateButton(live, "Reset Round", new Vector2(0, -113), new Vector2(310, 48));
        resetButton.onClick.AddListener(match.ResetSimulationRound);

        var final = Panel("Simulation results", canvasRoot.transform, new Color(0.04f, 0.08f, 0.13f, 0.98f));
        final.anchorMin = Vector2.zero;
        final.anchorMax = Vector2.one;
        final.offsetMin = final.offsetMax = Vector2.zero;
        finalPanel = final.gameObject;
        finalTitle = Label(final, new Vector2(0, 245), new Vector2(900, 100), 48, TextAnchor.MiddleCenter);
        finalScores = Label(final, new Vector2(0, 10), new Vector2(700, 350), 38, TextAnchor.MiddleCenter);
        CreateButton(final, "Return to Menu", new Vector2(0, -252), new Vector2(370, 64))
            .onClick.AddListener(sceneLoader.ReturnToMenu);
        finalPanel.SetActive(false);
        displayedRounds = -1;
        Update();
    }

    private void Update()
    {
        if (canvasRoot == null) return;
        bool active = match != null && match.IsSpawned && match.IsHost && match.SimulationRounds > 0;
        canvasRoot.SetActive(active);
        if (!active)
        {
            displayedRounds = -1;
            return;
        }

        int completed = match.SimulationRoundsCompleted;
        var phase = match.CurrentPhase;
        bool fighting = phase == MatchManager.MatchPhase.Fighting;
        bool aborted = phase == MatchManager.MatchPhase.Aborted;
        bool finished = phase == MatchManager.MatchPhase.MatchFinished || aborted;
        livePanel.SetActive(!finished);
        finalPanel.SetActive(finished);
        resetButton.interactable = fighting;

        if (displayedRounds != completed)
        {
            var scores = new StringBuilder();
            if (match.IsTeamFight)
            {
                for (int team = 1; team <= 2; team++)
                {
                    if (team > 1) scores.AppendLine();
                    int wins = 0;
                    for (int i = 0; i < match.PlayerCount; i++)
                    {
                        var fighter = match.GetPlayer(i);
                        if (fighter.team == team) wins = Mathf.Max(wins, fighter.wins);
                    }
                    scores.Append("Team ").Append(team).Append("   ").Append(wins);
                }
            }
            else
            {
                for (int i = 0; i < match.PlayerCount; i++)
                {
                    var fighter = match.GetPlayer(i);
                    if (i > 0) scores.AppendLine();
                    scores.Append(fighter.character).Append("   ").Append(fighter.wins);
                }
            }
            liveScores.text = scores.ToString();
            finalScores.text = $"{completed} / {match.SimulationRounds} rounds completed\n\n{scores}";
            displayedRounds = completed;
        }

        if (finished)
        {
            finalTitle.text = aborted ? "SIMULATION ENDED EARLY" : "FINAL SIMULATION TALLY";
            return;
        }

        if (fighting)
        {
            int remaining = Mathf.Max(0, Mathf.CeilToInt((float)(match.PhaseDeadline -
                NetworkManager.Singleton.ServerTime.Time)));
            liveTitle.text = $"Round {completed + 1}/{match.SimulationRounds}  ·  {remaining / 60}:{remaining % 60:00}";
        }
        else if (phase == MatchManager.MatchPhase.RoundResults)
        {
            liveTitle.text = match.SimulationRoundOutcome;
        }
        else liveTitle.text = $"Round {completed + 1}/{match.SimulationRounds}  ·  Getting ready";
    }

    private static RectTransform Panel(string title, Transform parent, Color color)
    {
        var panel = new GameObject(title, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return (RectTransform)panel.transform;
    }

    private Text Label(Transform parent, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
    {
        var obj = new GameObject("Simulation label", typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        var rect = (RectTransform)obj.transform;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var label = obj.GetComponent<Text>();
        label.font = font;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private Button CreateButton(Transform parent, string title, Vector2 position, Vector2 size)
    {
        var rect = Panel(title, parent, ButtonColor);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        Label(rect, Vector2.zero, size, 25, TextAnchor.MiddleCenter).text = title;
        return button;
    }
}
#endif
