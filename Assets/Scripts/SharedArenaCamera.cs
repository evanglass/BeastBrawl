using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Each game instance frames its locally observed fighters independently.</summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-50)]
public class SharedArenaCamera : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.16f;
    [SerializeField, Min(0.01f)] private float zoomSmoothTime = 0.2f;
    [SerializeField, Min(0.1f)] private float minimumSize = 6f;
    [SerializeField, Min(0f)] private float horizontalPadding = 2f;
    [SerializeField, Min(0f)] private float verticalPadding = 2f;

    private Camera arenaCamera;
    private Vector3 positionVelocity;
    private float zoomVelocity;
    private bool trackingFighters;

    private void Awake()
    {
        arenaCamera = GetComponent<Camera>();
        // Avoid Unity's bright default blue when an arena has not rendered behind the camera.
        arenaCamera.backgroundColor = new Color(.035f, .055f, .075f, 1f);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Arena_001" && scene.name != "Arena_02" && scene.name != "Arena_03") return;
        bool found = false;
        Bounds bounds = default;
        foreach (var collider in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (collider.gameObject.scene != scene || !collider.CompareTag("Level Geometry") || collider.isTrigger) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        if (!found) return;
        transform.position = new Vector3(bounds.center.x, bounds.center.y, transform.position.z);
        arenaCamera.orthographicSize = Mathf.Max(minimumSize,
            bounds.extents.y + verticalPadding,
            (bounds.extents.x + horizontalPadding) / Mathf.Max(arenaCamera.aspect, .01f));
        positionVelocity = Vector3.zero;
        zoomVelocity = 0f;
        trackingFighters = false;
    }

    private void LateUpdate()
    {
        var fighters = FindObjectsByType<PlayerNetworkBridge>(FindObjectsSortMode.None);
        var match = FindFirstObjectByType<MatchManager>();
        if (match != null && match.IsSpawned && match.CurrentPhase == MatchManager.MatchPhase.Overview &&
            Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.ServerTime.Time < match.PhaseDeadline) return;
        Vector2 center = Vector2.zero;
        int count = 0;
        foreach (var fighter in fighters)
        {
            if (!fighter.IsSpawned || fighter.Eliminated) continue;
            center += (Vector2)fighter.transform.position;
            count++;
        }
        if (count == 0) return;

        center /= count;
        Vector3 target = new Vector3(center.x, center.y, transform.position.z);
        transform.position = trackingFighters
            ? Vector3.SmoothDamp(transform.position, target, ref positionVelocity, positionSmoothTime)
            : target;

        // Frame the furthest fighter from the current smoothed camera center, so
        // position smoothing cannot temporarily move a fighter out of view.
        float requiredSize = minimumSize;
        float aspect = Mathf.Max(arenaCamera.aspect, 0.01f);
        foreach (var fighter in fighters)
        {
            if (!fighter.IsSpawned || fighter.Eliminated) continue;
            var collider = fighter.GetComponent<Collider2D>();
            Bounds bounds = collider != null ? collider.bounds : new Bounds(fighter.transform.position, Vector3.zero);
            float xExtent = Mathf.Max(Mathf.Abs(bounds.min.x - transform.position.x), Mathf.Abs(bounds.max.x - transform.position.x));
            float yExtent = Mathf.Max(Mathf.Abs(bounds.min.y - transform.position.y), Mathf.Abs(bounds.max.y - transform.position.y));
            requiredSize = Mathf.Max(requiredSize, (xExtent + horizontalPadding) / aspect, yExtent + verticalPadding);
        }

        float smoothedSize = trackingFighters
            ? Mathf.SmoothDamp(arenaCamera.orthographicSize, requiredSize, ref zoomVelocity, zoomSmoothTime)
            : requiredSize;
        // Expand immediately when needed; ease in when there is spare room.
        arenaCamera.orthographicSize = Mathf.Max(requiredSize, smoothedSize);
        trackingFighters = true;
    }
}
