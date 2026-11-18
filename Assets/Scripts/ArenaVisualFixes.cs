#if !UNITY_SERVER || UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Client-only platform artwork. Gameplay geometry and movement remain unchanged.</summary>
public sealed class ArenaVisualFixes : MonoBehaviour
{
    private Transform target;
    private Vector3 initialOffset;
    private Quaternion initialTargetRotation;
    private Quaternion initialVisualRotation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        for (int i = 0; i < SceneManager.sceneCount; i++) OnSceneLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.isLoaded) return;
        if (scene.name == "Arena_001") FixDesert(scene);
        if (scene.name == "Arena_02") TextureMountain(scene);
    }

    private static void FixDesert(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        Transform rightGroundTexture = null;
        Transform leftGround = null;
        foreach (GameObject root in roots)
        {
            if (root.name == "sandtexture_0 (7)") rightGroundTexture = root.transform;
            if (root.name == "Platform" && root.CompareTag("Level Geometry")) leftGround = root.transform;
        }
        if (rightGroundTexture != null && leftGround != null && GameObject.Find("Desert left ground artwork") == null)
        {
            GameObject copy = Instantiate(rightGroundTexture.gameObject);
            copy.name = "Desert left ground artwork";
            SceneManager.MoveGameObjectToScene(copy, scene);
            Vector3 pos = copy.transform.position;
            pos.x = leftGround.position.x;
            copy.transform.position = pos;
        }
        foreach (GameObject root in roots)
        {
            if (!root.name.StartsWith("sandtexture_0")) continue;
            MovingPlatform closest = null;
            float nearest = .35f * .35f;
            foreach (GameObject candidate in roots)
            {
                MovingPlatform platform = candidate.GetComponent<MovingPlatform>();
                if (platform == null) continue;
                Vector2 delta = root.transform.position - platform.transform.position;
                float distance = delta.sqrMagnitude;
                if (distance < nearest) { nearest = distance; closest = platform; }
            }
            if (closest == null || root.GetComponent<ArenaVisualFixes>() != null) continue;
            ArenaVisualFixes follower = root.AddComponent<ArenaVisualFixes>();
            follower.Bind(closest.transform);
            SpriteRenderer original = closest.GetComponent<SpriteRenderer>();
            if (original != null) original.enabled = false;
        }
    }

    private static void TextureMountain(Scene scene)
    {
        Texture2D texture = Resources.Load<Texture2D>("MenuArt/MountainPlatform");
        if (texture == null) return;
        texture.filterMode = FilterMode.Point;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 800f);
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (SpriteRenderer original in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (!original.enabled || !original.gameObject.activeInHierarchy || !original.CompareTag("Level Geometry") ||
                !original.name.ToLowerInvariant().Contains("platform") || original.sprite == null) continue;
            if (original.transform.Find("Local platform artwork") != null) continue;
            // Match the original sprite's local dimensions and world scale, while keeping
            // the artwork separate from physics and preserving zero-scale platform axes.
            Vector3 scale = original.transform.lossyScale;
            Vector2 size = Vector2.Scale(original.sprite.bounds.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            if (size.x <= 0 || size.y <= 0) continue;
            GameObject artwork = new GameObject("Local platform artwork");
            SceneManager.MoveGameObjectToScene(artwork, scene);
            artwork.transform.SetPositionAndRotation(original.transform.position + Vector3.back * .01f, original.transform.rotation);
            SpriteRenderer replacement = artwork.AddComponent<SpriteRenderer>();
            replacement.sprite = sprite;
            replacement.drawMode = SpriteDrawMode.Tiled;
            replacement.size = size;
            replacement.sharedMaterial = original.sharedMaterial;
            replacement.sortingLayerID = original.sortingLayerID;
            replacement.sortingOrder = original.sortingOrder;
            ArenaVisualFixes follower = artwork.AddComponent<ArenaVisualFixes>();
            follower.Bind(original.transform);
            original.enabled = false;
        }
    }

    private void Bind(Transform platform)
    {
        target = platform;
        initialOffset = transform.position - platform.position;
        initialTargetRotation = platform.rotation;
        initialVisualRotation = transform.rotation;
    }

    private void LateUpdate()
    {
        if (target == null) return;
        Quaternion delta = target.rotation * Quaternion.Inverse(initialTargetRotation);
        transform.position = target.position + delta * initialOffset;
        transform.rotation = delta * initialVisualRotation;
    }
}
#endif
