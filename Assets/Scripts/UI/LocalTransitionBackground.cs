#if !UNITY_SERVER || UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Shows existing menu artwork behind UI while no arena or menu is loaded.</summary>
public sealed class LocalTransitionBackground : MonoBehaviour
{
    private Image background;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindFirstObjectByType<LocalTransitionBackground>() != null) return;
        GameObject root = new GameObject("Local transition background", typeof(RectTransform));
        DontDestroyOnLoad(root);
        root.AddComponent<LocalTransitionBackground>();
    }

    private void Awake()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -100;
        gameObject.AddComponent<CanvasScaler>();
        GameObject image = new GameObject("Existing menu artwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        image.transform.SetParent(transform, false);
        RectTransform rect = image.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        background = image.GetComponent<Image>();
        background.raycastTarget = false;
        Texture2D texture = Resources.Load<Texture2D>("MenuArt/Background");
        if (texture != null)
        {
            texture.filterMode = FilterMode.Point;
            background.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        }
        else background.color = new Color(.035f, .055f, .075f, 1f);
        UpdateVisibility();
        SceneManager.sceneLoaded += SceneChanged;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }

    private void SceneChanged(Scene scene, LoadSceneMode mode) => UpdateVisibility();
    private void SceneUnloaded(Scene scene) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (background == null) return;
        bool gameSceneVisible = SceneManager.GetSceneByName("MainMenu").isLoaded;
        foreach (string arena in SceneLoader.CustomArenas)
            gameSceneVisible |= SceneManager.GetSceneByName(arena).isLoaded;
        background.enabled = !gameSceneVisible;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= SceneChanged;
        SceneManager.sceneUnloaded -= SceneUnloaded;
    }
}
#endif
