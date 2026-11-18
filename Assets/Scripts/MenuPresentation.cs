#if !UNITY_SERVER || UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Local-only menu artwork and first-launch presentation.</summary>
public sealed class MenuPresentation : MonoBehaviour
{
    private static bool introPlayed;
    private static Sprite background, title, comic, card;
    private bool skipRequested;

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            skipRequested = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        introPlayed = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            SceneLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
    }

    private static Sprite LoadArt(string name)
    {
        Texture2D texture = Resources.Load<Texture2D>("MenuArt/" + name);
        if (texture == null) return null;
        texture.filterMode = FilterMode.Point;
        // The supplied title has isolated stray pixels far to the right of the logo.
        // Exclude that empty strip so the visible artwork is centered.
        float width = name == "Title" ? Mathf.Min(265, texture.width) : texture.width;
        return Sprite.Create(texture, new Rect(0, 0, width, texture.height), new Vector2(.5f, .5f));
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.isLoaded || (scene.name != "MainMenu" && scene.name != "NGO_Setup")) return;
        if (background == null) background = LoadArt("Background");
        if (title == null) title = LoadArt("Title");
        if (comic == null) comic = LoadArt("Comic");
        if (card == null) card = LoadArt("CharacterCard");
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (scene.name == "NGO_Setup" && root.name == "Lobby Canvas" && root.GetComponent<LobbyCardPresentation>() == null)
                root.AddComponent<LobbyCardPresentation>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (scene.name == "MainMenu" && child.name == "FERAL FIGHTERS" && title != null)
                {
                    Text heading = child.GetComponent<Text>();
                    if (heading != null && child.Find("Menu title artwork") == null)
                    {
                        Image logo = CreateImage("Menu title artwork", child, title);
                        logo.preserveAspect = true;
                        logo.rectTransform.sizeDelta = new Vector2(840, 380);
                        logo.rectTransform.anchoredPosition = new Vector2(0, -90);
                        heading.enabled = false;
                    }
                }
                Image image = child.GetComponent<Image>();
                if (image == null) continue;
                if (scene.name == "MainMenu" && child.name == "Background" && background != null)
                {
                    image.sprite = background;
                    image.color = Color.white;
                }
                else if (scene.name == "NGO_Setup" && child.name == "Lobby Panel" && background != null)
                {
                    image.sprite = background; // LobbyUI controls visibility and alpha.
                }
                else if (scene.name == "NGO_Setup" && child.name == "Match Results Panel" && background != null)
                {
                    image.sprite = background;
                    image.color = Color.white;
                }
                else if (scene.name == "NGO_Setup" && child.name.StartsWith("Player Card ") && card != null)
                {
                    image.sprite = card;
                }
            }
            if (scene.name == "MainMenu" && root.name == "Menu Canvas")
                CreateControlsMenu(root.transform);
        }
        if (scene.name != "MainMenu" || introPlayed || title == null || comic == null || background == null) return;
        introPlayed = true;
        GameObject presentation = new GameObject("Local intro presentation", typeof(RectTransform));
        SceneManager.MoveGameObjectToScene(presentation, scene);
        presentation.AddComponent<MenuPresentation>().StartCoroutine("PlayIntro");
    }

    private static void CreateControlsMenu(Transform canvas)
    {
        Transform main = canvas.Find("Main Menu");
        if (main == null || main.Find("Controls Button") != null) return;
        Transform online = main.Find("Online Game Button");
        if (online == null) return;
        Text originalLabel = online.GetComponentInChildren<Text>(true);
        if (originalLabel == null) return;

        Image panel = CreateImage("Controls Panel", main, null);
        Stretch(panel.rectTransform);
        panel.color = new Color(.035f, .055f, .075f, .97f);
        panel.raycastTarget = true;
        panel.gameObject.SetActive(false);

        Text heading = CreateControlsText("Controls heading", panel.transform, originalLabel.font, 64);
        heading.text = "CONTROLS";
        heading.rectTransform.anchoredPosition = new Vector2(0, 300);
        heading.rectTransform.sizeDelta = new Vector2(1000, 100);

        Text instructions = CreateControlsText("Controls instructions", panel.transform, originalLabel.font, 30);
        instructions.text = "• 4 Person Online Brawler\n\n" +
            "• Find 3 other friends or simply queue \"Online Play\"  for matchmaking\n\n" +
            "• Collect 3 crystal shards to win and release the beast within\n\n" +
            "• When your beast meter is full press \"T\" to release your beast\n\n" +
            "• If you are knocked unconscious, try spamming \"clicks\" to get up\n\n" +
            "• WASD - Movement, Space Bar - Jump, Click - to attack T - to transform";
        instructions.alignment = TextAnchor.MiddleLeft;
        instructions.rectTransform.sizeDelta = new Vector2(1200, 550);

        GameObject close = Instantiate(online.gameObject, panel.transform, false);
        close.name = "Close Controls Button";
        close.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -380);
        close.GetComponentInChildren<Text>(true).text = "Close";
        Button closeButton = close.GetComponent<Button>();
        closeButton.onClick = new Button.ButtonClickedEvent();
        closeButton.onClick.AddListener(() => panel.gameObject.SetActive(false));

        GameObject controls = Instantiate(online.gameObject, main, false);
        controls.name = "Controls Button";
        controls.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -190);
        controls.GetComponentInChildren<Text>(true).text = "Controls";
        Button controlsButton = controls.GetComponent<Button>();
        controlsButton.onClick = new Button.ButtonClickedEvent();
        controlsButton.onClick.AddListener(() => { panel.transform.SetAsLastSibling(); panel.gameObject.SetActive(true); closeButton.Select(); });

        CreateInfoMenu(main, online, originalLabel.font, "Credits", -270,
            "<b>Evan Glasscock</b>\nTeam Lead, Programmer, Network Specialist, Technical Artist, Gameplay Director\n\n" +
            "<b>Ethan Stewart</b>\nCreative Director, Story Artist, Sprite Designer, Visual Effects\n\n" +
            "<b>William Dumpert</b>\nBackground and Environment Artist, Combat and Systems Designer\n\n" +
            "<b>Joshua Daratsianakis</b>\nMusic Writer, Sound Engineer, Technical Artist\n\n" +
            "<b>Aeron Ward</b>\nSound Effects Sourcing, Game Ideas, Playtesting", false);
        TextAsset attributionFile = Resources.Load<TextAsset>("MenuArt/SoundAttributions");
        CreateInfoMenu(main, online, originalLabel.font, "Attributions", -350,
            attributionFile != null ? attributionFile.text : "Sound attribution information is unavailable.", true);
        panel.transform.SetAsLastSibling();
    }

    private static void CreateInfoMenu(Transform main, Transform template, Font font, string name,
        float buttonY, string content, bool scrollable)
    {
        Image panel = CreateImage(name + " Panel", main, null);
        Stretch(panel.rectTransform);
        panel.color = new Color(.035f, .055f, .075f, .97f);
        panel.raycastTarget = true;
        panel.gameObject.SetActive(false);

        Text heading = CreateControlsText(name + " heading", panel.transform, font, 64);
        heading.text = name.ToUpperInvariant();
        heading.rectTransform.anchoredPosition = new Vector2(0, 400);
        heading.rectTransform.sizeDelta = new Vector2(1200, 100);

        if (scrollable)
        {
            GameObject viewport = new GameObject("Attributions Scroll View", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewport.transform.SetParent(panel.transform, false);
            viewport.GetComponent<Image>().color = Color.clear;
            RectTransform viewRect = (RectTransform)viewport.transform;
            viewRect.sizeDelta = new Vector2(1500, 620);
            viewRect.anchoredPosition = new Vector2(0, 10);
            Text body = CreateControlsText("Attributions text", viewport.transform, font, 25);
            body.text = content;
            body.alignment = TextAnchor.UpperLeft;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            RectTransform bodyRect = body.rectTransform;
            bodyRect.anchorMin = new Vector2(0, 1);
            bodyRect.anchorMax = new Vector2(1, 1);
            bodyRect.pivot = new Vector2(.5f, 1);
            bodyRect.anchoredPosition = Vector2.zero;
            bodyRect.sizeDelta = new Vector2(0, 0);
            ContentSizeFitter fitter = body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewRect;
            scroll.content = bodyRect;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 45;
        }
        else
        {
            Text body = CreateControlsText(name + " text", panel.transform, font, name == "Credits" ? 27 : 38);
            body.text = content;
            body.rectTransform.sizeDelta = new Vector2(1200, 600);
        }

        GameObject close = Instantiate(template.gameObject, panel.transform, false);
        close.name = "Close " + name + " Button";
        close.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -400);
        close.GetComponentInChildren<Text>(true).text = "Close";
        Button closeButton = close.GetComponent<Button>();
        closeButton.onClick = new Button.ButtonClickedEvent();
        closeButton.onClick.AddListener(() => panel.gameObject.SetActive(false));

        GameObject button = Instantiate(template.gameObject, main, false);
        button.name = name + " Button";
        button.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, buttonY);
        button.GetComponentInChildren<Text>(true).text = name;
        Button component = button.GetComponent<Button>();
        component.onClick = new Button.ButtonClickedEvent();
        component.onClick.AddListener(() => { panel.transform.SetAsLastSibling(); panel.gameObject.SetActive(true); closeButton.Select(); });
        panel.transform.SetAsLastSibling();
    }

    private static Text CreateControlsText(string name, Transform parent, Font font, int size)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        item.transform.SetParent(parent, false);
        Text text = item.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private IEnumerator PlayIntro()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        gameObject.AddComponent<GraphicRaycaster>();
        RectTransform screen = (RectTransform)transform;
        Image backdrop = CreateImage("Intro background", transform, background);
        Stretch(backdrop.rectTransform);
        backdrop.raycastTarget = true; // Block menu buttons during the intro.
        Image page = CreateImage("Comic", transform, comic);
        page.rectTransform.anchorMin = page.rectTransform.anchorMax = new Vector2(.5f, .5f);

        Image hintBackground = CreateImage("Skip prompt background", transform, null);
        hintBackground.rectTransform.anchorMin = hintBackground.rectTransform.anchorMax = new Vector2(.5f, 1f);
        hintBackground.rectTransform.anchoredPosition = new Vector2(0, -42);
        hintBackground.rectTransform.sizeDelta = new Vector2(420, 52);
        hintBackground.color = new Color(.035f, .055f, .075f, .85f);
        CanvasGroup hintGroup = hintBackground.gameObject.AddComponent<CanvasGroup>();
        GameObject onlineButton = GameObject.Find("Online Game Button");
        Text menuLabel = onlineButton != null ? onlineButton.GetComponentInChildren<Text>(true) : null;
        Font menuFont = menuLabel != null ? menuLabel.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text hint = CreateControlsText("Skip prompt", hintBackground.transform, menuFont, 25);
        hint.text = "PRESS SPACE TO SKIP";
        Stretch(hint.rectTransform);
        // Pixel rectangles are measured from the top left of the 512 x 640 source page.
        Rect[] panels = { new Rect(18, 17, 185, 142), new Rect(208, 17, 286, 142),
            new Rect(18, 171, 320, 144), new Rect(343, 171, 151, 144),
            new Rect(18, 327, 154, 143), new Rect(176, 327, 318, 143),
            new Rect(18, 480, 287, 143), new Rect(312, 480, 182, 143) };
        yield return null; // Let the overlay canvas determine its screen dimensions.
        Vector3 previous = PanelView(panels[0], screen.rect.size);
        SetPage(page.rectTransform, previous);
        for (int i = 0; i < panels.Length && !skipRequested; i++)
        {
            Vector3 target = PanelView(panels[i], screen.rect.size);
            if (i > 0) yield return MovePage(page.rectTransform, previous, target, 2.2f);
            yield return WaitForIntro(1.7f);
            previous = target;
        }
        if (!skipRequested)
        {
            Vector3 fullPage = PanelView(new Rect(0, 0, 512, 640), screen.rect.size);
            yield return MovePage(page.rectTransform, previous, fullPage, 3f);
            yield return WaitForIntro(2f);
        }
        float t = 0;
        while (t < .8f)
        {
            t += Time.unscaledDeltaTime;
            float a = 1 - Mathf.Clamp01(t / .8f);
            page.color = new Color(1, 1, 1, a);
            backdrop.color = new Color(1, 1, 1, a);
            hintGroup.alpha = a;
            yield return null;
        }
        Destroy(gameObject);
    }

    private static Vector3 PanelView(Rect panel, Vector2 screen)
    {
        float scale = Mathf.Min(screen.x * .86f / panel.width, screen.y * .78f / panel.height);
        return new Vector3((256f - panel.center.x) * scale, (panel.center.y - 320f) * scale, scale);
    }
    private static void SetPage(RectTransform page, Vector3 view)
    {
        page.sizeDelta = new Vector2(512 * view.z, 640 * view.z);
        page.anchoredPosition = new Vector2(view.x, view.y);
    }
    private IEnumerator WaitForIntro(float duration)
    {
        float elapsed = 0;
        while (elapsed < duration && !skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private IEnumerator MovePage(RectTransform page, Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0;
        while (elapsed < duration && !skipRequested)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
            SetPage(page, Vector3.Lerp(from, to, t));
            yield return null;
        }
        if (!skipRequested) SetPage(page, to);
    }
}
#endif
