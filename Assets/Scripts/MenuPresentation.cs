using System;
using BeastBrawl.Music;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

/// <summary>Local-only visibility and presentation settings for screen menus.</summary>
public sealed class MenuPresentation : MonoBehaviour
{
    [Serializable] public class MenuEntry
    {
        public string name = "New menu";
        public bool visible;
        public GameObject root;
    }

    [Header("Menu visibility")]
    [Tooltip("Show the music queue controls. Music continues when this is off.")]
    public bool showMusicManagerMenu;
    [Tooltip("Show the assigned pause menu. This does not pause gameplay by itself.")]
    public bool showPauseMenu;
    public GameObject pauseMenuRoot;
    public MenuEntry[] otherMenus = Array.Empty<MenuEntry>();

    [Header("References")]
    public Canvas menuCanvas;
    public BeastBrawlMusicManager musicManager;

    private GameObject musicPanel;
    private Text status;
    private Font font;
    private Camera stackedOn;

    private void AttachCamera()
    {
        var arena = Camera.main;
        var menu = GetComponent<Camera>();
        if (arena == null || menu == null || arena == menu || arena == stackedOn) return;
        DetachCamera();
        var data = arena.GetUniversalAdditionalCameraData();
        if (data.renderType != CameraRenderType.Base || data.cameraStack == null) return;
        data.cameraStack.RemoveAll(camera => camera == null);
        if (!data.cameraStack.Contains(menu)) data.cameraStack.Add(menu);
        stackedOn = arena;
    }

    private void DetachCamera()
    {
        if (stackedOn != null)
        {
            var data = stackedOn.GetUniversalAdditionalCameraData();
            if (data.cameraStack != null) data.cameraStack.Remove(GetComponent<Camera>());
        }
        stackedOn = null;
    }

    private void OnEnable() { AttachCamera(); }
    private void OnDisable() { DetachCamera(); }

    private void Awake()
    {
        if (musicManager == null) musicManager = FindFirstObjectByType<BeastBrawlMusicManager>();
        if (musicManager != null) musicManager.showTestControls = false;
        if (menuCanvas == null) return;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildMusicMenu();
        ApplyVisibility();
    }

    private void Update()
    {
        if (stackedOn == null) AttachCamera();
        ApplyVisibility();
        if (musicPanel != null && musicPanel.activeSelf && musicManager != null && status != null)
            status.text = $"Bar {musicManager.BarInTrack}  Beat {musicManager.BeatInBar}  BPM {musicManager.Bpm}\nPlaying: {musicManager.CurrentTrackName}\nQueued: {musicManager.QueuedTrackName}";
    }

    private void ApplyVisibility()
    {
        if (musicPanel != null && musicPanel.activeSelf != showMusicManagerMenu) musicPanel.SetActive(showMusicManagerMenu);
        if (pauseMenuRoot != null && pauseMenuRoot.activeSelf != showPauseMenu) pauseMenuRoot.SetActive(showPauseMenu);
        if (otherMenus == null) return;
        foreach (var entry in otherMenus)
            if (entry != null && entry.root != null && entry.root.activeSelf != entry.visible) entry.root.SetActive(entry.visible);
    }

    private GameObject UIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private Text Label(Transform parent, string value, int height = 26)
    {
        var go = UIObject(value, parent);
        var text = go.AddComponent<Text>();
        text.font = font; text.fontSize = 17; text.color = Color.white; text.text = value;
        text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap;
        go.AddComponent<LayoutElement>().preferredHeight = height;
        return text;
    }

    private void Button(Transform parent, string caption, Action action, bool enabled = true)
    {
        var go = UIObject(caption, parent);
        go.AddComponent<LayoutElement>().preferredHeight = 30;
        var image = go.AddComponent<Image>(); image.color = new Color(.22f, .28f, .38f, 1f);
        var button = go.AddComponent<Button>(); button.targetGraphic = image; button.interactable = enabled;
        button.onClick.AddListener(() => action());
        var text = Label(go.transform, caption);
        var rect = (RectTransform)text.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(8, 0); rect.offsetMax = Vector2.zero;
    }

    private void Loops(Transform content, string name, BeastBrawlMusicManager.MusicCategory category, BeastBrawlMusicManager.LoopTrack[] tracks)
    {
        Label(content, "— " + name + " —");
        if (tracks != null) for (int i = 0; i < tracks.Length; i++)
        {
            var track = tracks[i]; int index = i;
            Button(content, "Queue: " + (track == null ? "Empty" : track.displayName), () => musicManager.QueueUp(category, index), track != null && track.clip != null);
        }
        Button(content, "Queue down", () => musicManager.QueueDown(category));
        Button(content, "Clear waiting", () => musicManager.ClearPending(category));
    }

    private void Character(Transform content, int id, string name, BeastBrawlMusicManager.CharacterTracks tracks)
    {
        Label(content, "— " + name + " —");
        if (tracks == null) return;
        Button(content, "Random riff", () => musicManager.QueueCharacterRiff(id), HasClip(tracks.riffs));
        Button(content, "Random lead", () => musicManager.QueueCharacterLead(id), HasClip(tracks.leads));
        Features(content, id, tracks.riffs, false);
        Features(content, id, tracks.leads, true);
        Button(content, "Clear character waiting", () => musicManager.ClearCharacterPending(id));
    }

    private static bool HasClip(BeastBrawlMusicManager.FeatureTrack[] tracks)
    {
        if (tracks != null) foreach (var track in tracks) if (track != null && track.clip != null) return true;
        return false;
    }

    private void Features(Transform parent, int id, BeastBrawlMusicManager.FeatureTrack[] tracks, bool lead)
    {
        if (tracks == null) return;
        for (int i = 0; i < tracks.Length; i++)
        {
            int index = i; var track = tracks[i];
            Button(parent, (lead ? "Lead: " : "Riff: ") + (track == null ? "Empty" : track.displayName),
                () => { if (lead) musicManager.QueueSpecificLead(id, index); else musicManager.QueueSpecificRiff(id, index); }, track != null && track.clip != null);
        }
    }

    private void BuildMusicMenu()
    {
        if (musicManager == null) return;
        musicPanel = UIObject("Music Manager Menu", menuCanvas.transform);
        var panelRect = (RectTransform)musicPanel.transform;
        panelRect.anchorMin = new Vector2(0, 0); panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot = new Vector2(0, .5f); panelRect.offsetMin = new Vector2(12, 12); panelRect.offsetMax = new Vector2(470, -12);
        musicPanel.AddComponent<Image>().color = new Color(.05f, .07f, .11f, .92f);
        var scrollGo = UIObject("Scroll View", musicPanel.transform);
        var scrollRect = (RectTransform)scrollGo.transform; scrollRect.anchorMin = Vector2.zero; scrollRect.anchorMax = Vector2.one; scrollRect.offsetMin = new Vector2(8, 8); scrollRect.offsetMax = new Vector2(-8, -8);
        scrollGo.AddComponent<Image>().color = new Color(0, 0, 0, .01f);
        var mask = scrollGo.AddComponent<Mask>(); mask.showMaskGraphic = false;
        var scroll = scrollGo.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.viewport = scrollRect;
        var content = UIObject("Content", scrollGo.transform);
        var contentRect = (RectTransform)content.transform; contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = Vector2.one; contentRect.pivot = new Vector2(.5f, 1); contentRect.anchoredPosition = Vector2.zero; contentRect.sizeDelta = Vector2.zero;
        var layout = content.AddComponent<VerticalLayoutGroup>(); layout.spacing = 4; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; scroll.content = contentRect;
        Label(content.transform, "MUSIC QUEUE MANAGER");
        status = Label(content.transform, "", 72);
        Loops(content.transform, "Drums", BeastBrawlMusicManager.MusicCategory.Drums, musicManager.drums);
        Loops(content.transform, "Groove", BeastBrawlMusicManager.MusicCategory.Groove, musicManager.grooves);
        Character(content.transform, 1, "Bruce the Bear", musicManager.bruce);
        Character(content.transform, 2, "Eagle Man", musicManager.eagle);
        Character(content.transform, 3, "Crimson Cougar", musicManager.cougar);
        Character(content.transform, 4, "Serpent Sam", musicManager.serpent);
        Loops(content.transform, "Solo songs", BeastBrawlMusicManager.MusicCategory.SoloSong, musicManager.soloSongs);
        Loops(content.transform, "Solo leads", BeastBrawlMusicManager.MusicCategory.SoloLead, musicManager.soloLeads);
        Button(content.transform, "STOP ALL IMMEDIATELY", musicManager.StopAllMusic);
    }
}
