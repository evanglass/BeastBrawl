using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Arena 5 is saved as normal Unity objects. This builds the first version from its source layout.
internal static class EnchantedForestAuthoring
{
    private const string Art = "Assets/Art/EnchantedForest/";
    private const string ScenePath = "Assets/Scenes/Arena_05_EnchantedForest.unity";
    private const string PrefabPath = "Assets/Prefabs/Arenas/EnchantedForest.prefab";
    private const string PreviewName = "Arena 5 | Fighter scale preview";
    private const string RootName = "Enchanted Forest";

    [Serializable] private class Platform { public string name, kind; public float x, top, width, height; }
    [Serializable] private class Point { public float x, y; }
    [Serializable] private class Zone { public float x, y, width, height; }
    [Serializable] private class Cloud { public float x, y, width, dx, dy, thickness; }
    [Serializable] private class Layout
    {
        public Platform[] platforms;
        public Point[] spawns;
        public Zone[] hazards;
        public Cloud cloud;
        public float ceiling;
    }

    [MenuItem("Tools/Arena 5/Build Forest Prefab and Show Preview")]
    public static void Build()
    {
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        if (scene.isDirty || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.LogWarning("Arena 5 already has work in progress or a saved prefab. Use the preview command to inspect it.");
            return;
        }

        ImportArt();
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Art + "Source~/Layout.json"));
        if (layout == null || layout.spawns.Length != 8) throw new InvalidOperationException("Arena 5 layout is incomplete.");
        foreach (var old in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(old);

        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        Populate(root, layout);

        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
        if (PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction) == null)
            throw new InvalidOperationException("Couldn't save the Enchanted Forest prefab.");
        EditorSceneManager.SaveScene(scene);
        RegisterScene();
        Validate(root);
        ShowPreview();
        SavePreviewImage(scene);
        Debug.Log("Arena 5 prefab and additive arena saved. Eight fighter spawns, one spore zone and five independent parallax layers validated.");
    }

    // Rebuild the existing prefab in place so the arena scene keeps its asset reference.
    public static void RebuildCompactForest()
    {
        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Art + "Source~/Layout.json"));
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            Populate(root, layout);
            Validate(root);
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException("Couldn't update the forest prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        CapturePreview();
        Debug.Log("COMPACT_FOREST: saved compact arena prefab.");
    }

    private static void Populate(GameObject root, Layout layout)
    {
        var scenery = Create("Parallax and decorative art", root.transform);
        BuildScenery(scenery.transform);

        var surfaces = Create("Solid fighting surfaces", root.transform);
        foreach (var p in layout.platforms)
            BuildPlatform(surfaces.transform, p);

        var spawns = Create("Fighter spawns", root.transform);
        for (int i = 0; i < layout.spawns.Length; i++)
        {
            var marker = Create($"Spawn {i + 1:00}", spawns.transform, layout.spawns[i].x, layout.spawns[i].y);
            marker.AddComponent<SpawnPoint>();
            Set(marker.GetComponent<SpawnPoint>(), "teamOnly", i != 0 && i != 2 && i != 4 && i != 7);
        }

        foreach (var point in new[] { (-27.5f, true), (27.5f, false) })
        {
            var corner = Create(point.Item2 ? "Eagle left perch" : "Eagle right perch", root.transform, point.Item1, 19);
            corner.AddComponent<EagleCorner>().LeftCorner = point.Item2;
        }

        var ceiling = Create("Upper ring-out ceiling", root.transform);
        Set(ceiling.AddComponent<ArenaRingOutCeiling>(), "height", layout.ceiling);
        for (int i = 0; i < layout.hazards.Length; i++) BuildSpores(root.transform, layout.hazards[i], i + 1);
        BuildCloud(root.transform, layout.cloud);

    }

    private static void ImportArt()
    {
        foreach (string path in Directory.GetFiles(Art, "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException($"Missing texture importer: {path}");
            bool spark = Path.GetFileNameWithoutExtension(path) == "Spark";
            importer.textureType = spark ? TextureImporterType.Default : TextureImporterType.Sprite;
            if (!spark) importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
    }

    private static void BuildScenery(Transform parent)
    {
        Layer(parent, "01 · Moon and sky", "Sky", 0, 9, 162, 104, 30, -50, .97f, .97f);
        Layer(parent, "02 · Mountain waterfalls", "DistantWaterfalls", 0, 10, 151, 75, 25, -44, .85f, .91f);
        Layer(parent, "03 · Forest ruins", "ForestRuins", -1, 6, 132, 61, 20, -38, .66f, .79f);
        Layer(parent, "04 · Near maple silhouettes", "NearMaples", 0, 4, 123, 64, 15, -29, .39f, .58f, .75f);
        ArtSprite(parent, "Pond · reflections and mist", "Pond", 0, -14.2f, 119, 15.2f, -24);
        ArtSprite(parent, "West maple · no trunk collision", "Maple", -24.6f, 4.95f, 21.3f, 23, -15);
        ArtSprite(parent, "East maple · no trunk collision", "Maple", 27.88f, 6.65f, 19.68f, 22, -16, flip: true);
        ArtSprite(parent, "Fairy cottage and warm windows", "Cottage", 24.026f, .85f, 14.35f, 14.2f, -13);
        ArtSprite(parent, "Central giant mushroom and stem", "Mushroom", 0, 6.29f, 21.4f, 20.6f, -10);
        Layer(parent, "05 · Foreground ferns", "ForegroundFerns", 0, -1.7f, 113, 39, -2, 5, .12f, .32f);
    }

    private static void BuildPlatform(Transform parent, Platform p)
    {
        var platform = Create(p.name, parent, p.x, p.top - p.height * .5f);
        platform.tag = "Level Geometry";
        var box = platform.AddComponent<BoxCollider2D>();
        box.size = new Vector2(p.width, p.height);
        if (p.kind is "branch" or "foliage") MakeOneWay(box);
        var art = p.kind switch
        {
            "wood" => "TimberBridge", "branch" => "Branch", "lily" => "LilyPad",
            "cap" => "CapWalkRim", "foliage" => "Branch", _ => "MossStone"
        };
        var height = p.kind switch
        {
            "foundation" => p.height * 1.22f, "buttress" => p.height * 1.8f, "lily" => 1.72f, "wood" => p.name == "Timber bridge" ? 3.2f : 1.85f,
            "cap" => Mathf.Max(.47f, p.height * .75f), "branch" or "foliage" => Mathf.Max(1.8f, p.height * 2.2f), _ => Mathf.Max(1.35f, p.height * 2.35f)
        };
        float topFromImage = p.kind switch
        {
            "wood" => .45f, "lily" => .28f, "branch" or "foliage" => .26f, _ => .10f
        };
        // Overlap the artwork so the retaining walls join the bottom of the base.
        float artTop = p.height * .5f + (p.kind == "buttress" ? .85f : 0f);
        ArtSprite(platform.transform, p.name + " · deck art", art, 0,
            artTop - (.5f - topFromImage) * height, p.width * 1.035f, height, -6,
            flip: p.kind == "branch" && p.x > 0);
    }

    // Branches and the cloud are solid from above and passable from below.
    private static void MakeOneWay(BoxCollider2D box)
    {
        var effector = box.GetComponent<PlatformEffector2D>();
        if (effector == null) effector = box.gameObject.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.useOneWayGrouping = true;
        effector.surfaceArc = 160f;
        box.usedByEffector = true;
    }

    private static void BuildSpores(Transform parent, Zone zone, int index)
    {
        var obj = Create("Spore hazard " + index, parent, zone.x, zone.y);
        var box = obj.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(zone.width, zone.height);
        var hazard = obj.AddComponent<ArenaSporeHazard>();
        Set(hazard, "sparkle", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Spark.png"));
        Set(hazard, "consciousnessLoss", 12f);
        Set(hazard, "tickInterval", 1f);
        ArtSprite(obj.transform, "Green spore curtain", "SporeCurtain", 0, 0, zone.width, zone.height, -11);
    }

    private static void BuildCloud(Transform parent, Cloud data)
    {
        var cloud = Create("Storm cloud · rising platform", parent, data.x, data.y);
        cloud.tag = "Level Geometry";
        var box = cloud.AddComponent<BoxCollider2D>();
        box.size = new Vector2(data.width, data.thickness);
        box.offset = new Vector2(0f, (.54f - data.thickness) * .5f);
        MakeOneWay(box);
        var body = cloud.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        var mover = cloud.AddComponent<MovingPlatform>();
        var direction = new Vector2(data.dx, data.dy);
        Set(mover, "directionOverride", direction);
        Set(mover, "travelDistance", direction.magnitude);
        Set(mover, "moveSpeed", 4.75f);
        Set(mover, "cycleInterval", 6f);
        Set(mover, "resetAfterAscent", true);
        Set(mover, "carryRiders", true);
        ArtSprite(cloud.transform, "Storm cloud art", "StormCloud", 0, -.55f, data.width * 1.25f, 2.45f, -5);
    }

    [MenuItem("Tools/Arena 5/Show Forest with Eagle and Bear")]
    public static void ShowPreview()
    {
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var preview = scene.GetRootGameObjects().FirstOrDefault(obj => obj.name == PreviewName);
        if (preview == null)
        {
            preview = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(preview, scene);
            PreviewFighter(preview.transform, "Eagle", "Eagle_Walk", -12, -4.9f);
            PreviewFighter(preview.transform, "Bear", "Bear_Walk", 0, 13.5f);
        }

        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.in2DMode = true;
            view.orthographic = true;
            view.LookAtDirect(new Vector3(-1, 3f, 0), Quaternion.identity, 40f);
            view.Repaint();
        }
        Selection.activeGameObject = scene.GetRootGameObjects().FirstOrDefault(obj => obj.name == RootName);
    }

    private static void PreviewFighter(Transform parent, string character, string clipName, float x, float floor)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/Sprite Sheets/{character}/{clipName}.anim");
        var sprite = AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .SelectMany(binding => AnimationUtility.GetObjectReferenceCurve(clip, binding))
            .Select(frame => frame.value).OfType<Sprite>().FirstOrDefault();
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var prefabRenderer = player.GetComponent<SpriteRenderer>();
        if (prefabRenderer == null) prefabRenderer = player.GetComponentInChildren<SpriteRenderer>();
        if (sprite == null) sprite = prefabRenderer.sprite;
        if (sprite == null) throw new InvalidOperationException("Couldn't load existing fighter artwork.");
        var sample = Create(character + " · real fighter scale", parent, x, floor - sprite.bounds.min.y * prefabRenderer.transform.lossyScale.y);
        sample.hideFlags = HideFlags.DontSave;
        sample.transform.localScale = prefabRenderer.transform.lossyScale;
        var renderer = sample.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 2;
    }

    [MenuItem("Tools/Arena 5/Capture Forest Preview")]
    public static void CapturePreview()
    {
        ShowPreview();
        SavePreviewImage(SceneManager.GetSceneByPath(ScenePath));
    }

    private static void SavePreviewImage(Scene scene)
    {
        var preview = scene.GetRootGameObjects().FirstOrDefault(obj => obj.name == PreviewName);
        if (preview == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var cameraObject = new GameObject("Temporary render camera") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(-.5f, 2f, -15f);
        camera.orthographic = true;
        camera.orthographicSize = 18.5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(23, 32, 56, 255);
        camera.scene = scene;
        var target = new RenderTexture(1600, 840, 24);
        camera.targetTexture = target;
        var prior = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            // The batch renderer can reuse the same sprite texture across the whole preview.
            bool batching = UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching;
            UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = false;
            try { camera.Render(); }
            finally { UnityEngine.Rendering.GraphicsSettings.useScriptableRenderPipelineBatching = batching; }
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(Art + "Source~/UnityScalePreview.png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = prior;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    private static void RegisterScene()
    {
        if (!EditorBuildSettings.scenes.Any(scene => scene.path == ScenePath && scene.enabled))
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(scene => scene.path != ScenePath)
                .Append(new EditorBuildSettingsScene(ScenePath, true)).ToArray();
    }

    private static void Validate(GameObject root)
    {
        if (root.GetComponentsInChildren<SpawnPoint>().Length != 8 ||
            root.GetComponentsInChildren<ArenaSporeHazard>().Length != 1 ||
            root.GetComponentsInChildren<MovingPlatform>().Length != 1 ||
            root.GetComponentsInChildren<Paralax>().Length != 5 ||
            root.GetComponentsInChildren<SpriteRenderer>().Any(renderer => renderer.sprite == null))
            throw new InvalidOperationException("Arena prefab validation failed.");
    }

    private static GameObject Create(string name, Transform parent, float x = 0f, float y = 0f, float z = 0f)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(x, y, z);
        return obj;
    }

    private static SpriteRenderer ArtSprite(Transform parent, string name, string asset, float x, float y,
        float width, float height, int order, float z = 0f, bool flip = false)
    {
        var obj = Create(name, parent, x, y, z);
        var renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + asset + ".png");
        if (renderer.sprite == null) throw new InvalidOperationException("Couldn't load " + asset);
        var bounds = renderer.sprite.bounds;
        obj.transform.localScale = new Vector3(width / bounds.size.x, height / bounds.size.y, 1);
        renderer.sortingOrder = order;
        renderer.flipX = flip;
        return renderer;
    }

    private static void Layer(Transform parent, string name, string texture, float x, float y, float width, float height,
        float z, int order, float horizontal, float vertical, float alpha = 1f)
    {
        var layer = ArtSprite(parent, name, texture, x, y, width, height, order, z);
        layer.color = new Color(1, 1, 1, alpha);
        var parallax = layer.gameObject.AddComponent<Paralax>();
        Set(parallax, "horizontalEffect", horizontal);
        Set(parallax, "verticalEffect", vertical);
        Set(parallax, "zoomEffect", .15f);
    }

    private static void Set(UnityEngine.Object owner, string property, object value)
    {
        var data = new SerializedObject(owner);
        var field = data.FindProperty(property);
        switch (value)
        {
            case bool b: field.boolValue = b; break;
            case float f: field.floatValue = f; break;
            case Vector2 v: field.vector2Value = v; break;
            case UnityEngine.Object asset: field.objectReferenceValue = asset; break;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
    }
}
