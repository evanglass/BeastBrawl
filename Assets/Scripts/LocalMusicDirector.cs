using System.Collections.Generic;
using System.Collections;
using BeastBrawl.Music;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Locally directs music from the authoritative round and replicated player states.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class LocalMusicDirector : MonoBehaviour
{
    [Header("Music manager")]
    [SerializeField] private BeastBrawlMusicManager music;
    [SerializeField] private bool persistAcrossScenes = true;
    [SerializeField] private bool manageRoundMusic = true;
    [SerializeField, Min(0)] private int drumsIndex;
    [SerializeField, Min(0)] private int drumSoloBars = 4;
    [SerializeField, Min(.05f)] private float calmFadeSeconds = .5f;

    [Header("Menu music")]
    [SerializeField] private AudioClip menuMusic;
    [SerializeField] private string menuSceneName = "MainMenu";
    [SerializeField, Range(0f, 1f)] private float menuMusicVolume = .7f;
    [SerializeField, Min(.05f)] private float menuFadeSeconds = 1.5f;

    [Header("Meter rules")]
    [SerializeField, Range(0, 100)] private float riffThreshold = 75f;
    [SerializeField, Range(0, 100)] private float clearThreshold = 20f;
    [SerializeField, Range(0, 100)] private float calmThreshold = 50f;
    [SerializeField] private bool queueMeterRiffs = true;
    [SerializeField] private bool queueBeastParts = true;

    private sealed class FighterState
    {
        public PlayerVisual.Character character;
        public float meter;
        public bool beast;
    }

    private static LocalMusicDirector instance;
    private readonly Dictionary<ulong, FighterState> fighters = new Dictionary<ulong, FighterState>();
    private readonly HashSet<ulong> seen = new HashSet<ulong>();
    private readonly List<int> beastRequests = new List<int>();
    private readonly List<int> buildupRequests = new List<int>();
    private RoundManager round;
    private bool inRound;
    private double introFightTime;
    private bool wasIntense;
    private AudioSource menuSource;
    private MusicMasterGain menuMasterGain;
    private Coroutine menuFade;
    private float menuFadeLevel;

    private void Awake()
    {
        if (music == null) music = GetComponent<BeastBrawlMusicManager>();
        if (music == null) music = FindFirstObjectByType<BeastBrawlMusicManager>();
        if (instance != null && instance != this)
        {
            if (music != null && music != instance.music) Destroy(music);
            Destroy(this);
            return;
        }
        instance = this;
        if (music != null && manageRoundMusic) music.playOnStart = false;
        if (persistAcrossScenes)
        {
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }
        menuSource = gameObject.AddComponent<AudioSource>();
        menuMasterGain = gameObject.AddComponent<MusicMasterGain>();
        menuSource.playOnAwake = false;
        menuSource.spatialBlend = 0f;
        menuSource.loop = true;
        menuSource.clip = menuMusic;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        if (instance == this && MenuIsLoaded()) PlayMenuMusic();
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

    private bool MenuIsLoaded() => !string.IsNullOrEmpty(menuSceneName) &&
        SceneManager.GetSceneByName(menuSceneName).isLoaded;

    private bool BattleSceneIsLoaded()
    {
        var loader = FindFirstObjectByType<SceneLoader>();
        if (loader == null) return !MenuIsLoaded();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene != loader.gameObject.scene && scene.name != menuSceneName)
                return true;
        }
        return false;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == menuSceneName &&
            (round == null || !round.IsSpawned || round.CurrentState != RoundManager.RoundState.RoundInProgress))
        {
            if (music != null) music.StopAllMusic();
            PlayMenuMusic();
        }
    }

    // The future match manager can call this at the end of a MATCH, not each round.
    public void NotifyMatchFinished()
    {
        if (music != null) music.StopAllMusic();
        PlayMenuMusic();
    }

    // The future ready-up flow can call this once all players are ready.
    public void NotifyPlayersReady() => FadeMenuMusicOut();

    public void PlayMenuMusic()
    {
        if (menuSource == null || menuMusic == null) return;
        if (menuFade != null) StopCoroutine(menuFade);
        if (!menuSource.isPlaying)
        {
            menuSource.clip = menuMusic;
            menuFadeLevel = 0f;
            menuSource.volume = 0f;
            menuSource.Play();
        }
        menuFade = StartCoroutine(FadeMenu(1f, .3f, false));
    }

    public void FadeMenuMusicOut(float duration = -1f)
    {
        if (menuSource == null || !menuSource.isPlaying) return;
        if (menuFade != null) StopCoroutine(menuFade);
        float fadeSeconds = duration < 0f ? menuFadeSeconds : duration;
        menuFade = StartCoroutine(FadeMenu(0f, Mathf.Max(.05f, fadeSeconds), true));
    }

    private IEnumerator FadeMenu(float target, float seconds, bool stop)
    {
        float initial = menuFadeLevel;
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            menuFadeLevel = Mathf.Lerp(initial, target, Mathf.Clamp01(elapsed / seconds));
            ApplyMenuVolume();
            yield return null;
        }
        menuFadeLevel = target;
        ApplyMenuVolume();
        if (stop) menuSource.Stop();
        menuFade = null;
    }

    private void ApplyMenuVolume()
    {
        if (menuSource != null)
        {
            menuSource.volume = Mathf.Clamp01(menuMusicVolume * menuFadeLevel);
            if (menuMasterGain != null) menuMasterGain.gain = music != null ? music.MasterVolume : 1f;
        }
    }

    private void Update()
    {
        ApplyMenuVolume();
        if (music == null) return;
        if (round == null || !round.IsSpawned) round = FindFirstObjectByType<RoundManager>();
        bool hasRound = round != null && round.IsSpawned;
        bool battleLoaded = BattleSceneIsLoaded();
        if (hasRound && manageRoundMusic && battleLoaded &&
            (round.CurrentState == RoundManager.RoundState.RoundStarting || round.CurrentState == RoundManager.RoundState.RoundInProgress) &&
            round.FightStartTime > 0 && round.FightStartTime != introFightTime)
        {
            introFightTime = round.FightStartTime;
            double remaining = round.FightStartTime - round.NetworkManager.ServerTime.Time;
            // If a scene arrives late, shorten the fade so it finishes before
            // recovered battle audio starts rather than overlapping both songs.
            FadeMenuMusicOut(Mathf.Min(menuFadeSeconds, Mathf.Max(.05f, (float)remaining - .1f)));
            music.BeginRoundIntro(AudioSettings.dspTime + remaining, drumsIndex);
        }
        if (!hasRound) introFightTime = 0;
        bool active = hasRound && round.CurrentState == RoundManager.RoundState.RoundInProgress;
        if (active != inRound)
        {
            inRound = active;
            fighters.Clear();
            wasIntense = false;
            if (!active && manageRoundMusic) music.FadeToDrums(calmFadeSeconds, drumSoloBars);
            // A late-joining client may first observe the match after the intro.
            if (active && manageRoundMusic && battleLoaded && introFightTime == 0)
            {
                introFightTime = round.FightStartTime;
                FadeMenuMusicOut(.05f);
                music.BeginRoundIntro(AudioSettings.dspTime + 0.2, drumsIndex);
            }
        }
        if (!inRound) return;

        seen.Clear();
        beastRequests.Clear();
        buildupRequests.Clear();
        bool anyFighter = false;
        bool allCalm = true;
        bool anyBeast = false;
        int qualifyingFighters = 0;
        foreach (var player in FindObjectsByType<PlayerNetworkBridge>(FindObjectsSortMode.None))
        {
            if (!player.IsSpawned || player.Eliminated) continue;
            var visual = player.GetComponent<PlayerVisual>();
            if (visual == null || visual.PlayerCharacter == PlayerVisual.Character.None) continue;
            anyFighter = true;
            ulong id = player.NetworkObjectId;
            seen.Add(id);
            float meter = player.BeastMeter;
            bool beast = player.IsBeast;
            if (beast) anyBeast = true;
            if (meter >= riffThreshold || beast) qualifyingFighters++;
            int character = MusicCharacter(visual.PlayerCharacter);
            if (!fighters.TryGetValue(id, out var previous))
            {
                previous = new FighterState { character = visual.PlayerCharacter, meter = 0f, beast = false };
                fighters[id] = previous;
            }
            if (previous.character != visual.PlayerCharacter)
            {
                music.ClearCharacterPending(MusicCharacter(previous.character));
                previous.character = visual.PlayerCharacter;
                previous.meter = 0f;
                previous.beast = false;
            }
            // Cleanup runs first, including the meter reset caused by entering beast mode.
            if (previous.meter >= clearThreshold && meter < clearThreshold)
                music.ClearCharacterPending(character);
            if (queueMeterRiffs && previous.meter < riffThreshold && meter >= riffThreshold)
                buildupRequests.Add(character);
            if (queueBeastParts && beast && !previous.beast)
                beastRequests.Add(character);
            previous.meter = meter;
            previous.beast = beast;
            if (meter >= calmThreshold) allCalm = false;
        }
        // Process beast events first, independent of player iteration order.
        // In a crowded state, drop new requests instead of building an endless queue.
        if (qualifyingFighters <= 2)
        {
            foreach (int character in beastRequests) music.QueueBeastSections(character);
            foreach (int character in buildupRequests) music.QueueBuildupRiff(character);
        }
        var removed = new List<ulong>();
        foreach (var item in fighters)
            if (!seen.Contains(item.Key)) { music.ClearCharacterPending(MusicCharacter(item.Value.character)); removed.Add(item.Key); }
        foreach (var id in removed) fighters.Remove(id);

        if (anyFighter && !allCalm) wasIntense = true;
        // Activation empties the meter, but the fight is not calm while beast is active.
        // Pending buildup still gets cleared above before the new beast cues are queued.
        if (anyFighter && allCalm && !anyBeast && wasIntense)
        {
            music.FadeToDrums(calmFadeSeconds, drumSoloBars);
            wasIntense = false;
        }
    }

    // Player character order differs from the music manager's order.
    private static int MusicCharacter(PlayerVisual.Character character)
    {
        switch (character)
        {
            case PlayerVisual.Character.Bear: return 1;
            case PlayerVisual.Character.Eagle: return 2;
            case PlayerVisual.Character.Cougar: return 3;
            case PlayerVisual.Character.Snake: return 4;
            default: return 0;
        }
    }
}
