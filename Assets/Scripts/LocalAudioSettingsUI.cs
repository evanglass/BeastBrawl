#if !UNITY_SERVER || UNITY_EDITOR
using BeastBrawl.Music;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>Per-player audio settings; does not pause or affect the networked match.</summary>
public sealed class LocalAudioSettingsUI : MonoBehaviour
{
    private const string MusicKey = "FeralFighters.MusicVolume";
    private const string EffectsKey = "FeralFighters.EffectsVolume";
    private BeastBrawlMusicManager music;
    private CharacterSfxManager effects;
    private float musicVolume;
    private float effectsVolume;
    private GameObject panel;
    private Font font;
    private Button openButton;
    private Slider musicSlider;
    private Slider effectsSlider;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindFirstObjectByType<LocalAudioSettingsUI>() != null) return;
        GameObject root = new GameObject("Local audio settings", typeof(RectTransform));
        DontDestroyOnLoad(root);
        root.AddComponent<LocalAudioSettingsUI>();
    }

    private void Awake()
    {
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 1f));
        effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 1f));
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        gameObject.AddComponent<GraphicRaycaster>();

        openButton = MakeButton("Settings", transform, new Vector2(130, -50), new Vector2(210, 58), true);
        openButton.onClick.AddListener(Toggle);
        Image background = MakeImage("Settings panel", transform, new Color(.035f, .055f, .075f, .97f));
        RectTransform rect = background.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(25, -90);
        rect.sizeDelta = new Vector2(570, 410);
        panel = background.gameObject;

        Text heading = MakeText("SETTINGS", panel.transform, 36);
        Place(heading.rectTransform, new Vector2(0, 145), new Vector2(500, 50));
        musicSlider = CreateSlider("Music", 65, musicVolume, value =>
        {
            musicVolume = value;
            PlayerPrefs.SetFloat(MusicKey, value);
            if (music != null) music.SetMasterVolume(value);
        });
        effectsSlider = CreateSlider("Sound effects", -45, effectsVolume, value =>
        {
            effectsVolume = value;
            PlayerPrefs.SetFloat(EffectsKey, value);
            if (effects != null) effects.SetMasterVolume(value);
        });
        Button close = MakeButton("Close", panel.transform, new Vector2(0, -160), new Vector2(210, 54), false);
        close.onClick.AddListener(Close);
        SetNavigation(musicSlider, null, effectsSlider);
        SetNavigation(effectsSlider, musicSlider, close);
        SetNavigation(close, effectsSlider, null);
        panel.SetActive(false);
    }

    public bool IsOpen => panel != null && panel.activeSelf;

    public void Toggle() { if (IsOpen) Close(); else Open(); }

    private void Open()
    {
        panel.SetActive(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(musicSlider.gameObject);
    }

    public void Close()
    {
        panel.SetActive(false);
        PlayerPrefs.Save();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(openButton.gameObject);
    }

    private static void SetNavigation(Selectable item, Selectable up, Selectable down)
    {
        var navigation = item.navigation;
        navigation.mode = Navigation.Mode.Explicit;
        navigation.selectOnUp = up;
        navigation.selectOnDown = down;
        item.navigation = navigation;
    }

    private void Update()
    {
        if (music == null)
        {
            music = FindFirstObjectByType<BeastBrawlMusicManager>();
            if (music != null) music.SetMasterVolume(musicVolume);
        }
        if (effects == null)
        {
            effects = FindFirstObjectByType<CharacterSfxManager>();
            if (effects != null) effects.SetMasterVolume(effectsVolume);
        }
    }

    private Slider CreateSlider(string label, float y, float initial, UnityEngine.Events.UnityAction<float> changed)
    {
        Text text = MakeText(label, panel.transform, 27);
        Place(text.rectTransform, new Vector2(-120, y + 22), new Vector2(260, 40));
        GameObject go = new GameObject(label + " slider", typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(panel.transform, false);
        RectTransform sliderRect = go.GetComponent<RectTransform>();
        Place(sliderRect, new Vector2(55, y - 17), new Vector2(380, 40));
        Image track = MakeImage("Track", go.transform, new Color(.18f, .24f, .28f, 1));
        Stretch(track.rectTransform, new Vector2(0, .35f), new Vector2(1, .65f));
        Image fill = MakeImage("Fill", track.transform, new Color(.4f, .72f, .69f, 1));
        Stretch(fill.rectTransform, Vector2.zero, Vector2.one);
        GameObject handleArea = new GameObject("Handle area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        Stretch(handleArea.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        handleArea.GetComponent<RectTransform>().offsetMin = new Vector2(10, 0);
        handleArea.GetComponent<RectTransform>().offsetMax = new Vector2(-10, 0);
        Image handle = MakeImage("Handle", handleArea.transform, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(22, 36);
        Slider slider = go.GetComponent<Slider>();
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = track;
        go.AddComponent<LocalControllerSelection>().outlineTarget = track;
        var colors = slider.colors;
        colors.normalColor = new Color(.5f, .5f, .5f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 2f;
        slider.colors = colors;
        slider.SetValueWithoutNotify(initial);
        slider.onValueChanged.AddListener(changed);
        // The track receives pointer events even when clicking away from the handle.
        handle.raycastTarget = true;
        track.raycastTarget = true;
        return slider;
    }

    private Button MakeButton(string label, Transform parent, Vector2 position, Vector2 size, bool topLeft)
    {
        Image image = MakeImage(label + " button", parent, new Color(.145f, .227f, .369f, 1));
        RectTransform rect = image.rectTransform;
        if (topLeft) rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        Place(rect, position, size);
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        image.gameObject.AddComponent<LocalControllerSelection>().outlineTarget = image;
        var colors = button.colors;
        colors.normalColor = new Color(.5f, .5f, .5f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.colorMultiplier = 2f;
        button.colors = colors;
        Text text = MakeText(label, image.transform, 25);
        Stretch(text.rectTransform, Vector2.zero, Vector2.one);
        return button;
    }

    private Text MakeText(string name, Transform parent, int size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.text = name;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Image MakeImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

public sealed class LocalControllerSelection : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    public Graphic outlineTarget;
    private Outline outline;

    public void OnSelect(BaseEventData eventData)
    {
        if (outlineTarget == null) return;
        if (outline == null) outline = outlineTarget.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(.4f, 1f, .8f, 1f);
        outline.effectDistance = new Vector2(3f, 3f);
        outline.enabled = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (outline != null) outline.enabled = false;
    }
}
#endif
