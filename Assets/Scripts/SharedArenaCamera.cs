using UnityEngine;

/// <summary>Each game instance frames its locally observed fighters independently.</summary>
[RequireComponent(typeof(Camera))]
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

    private void Awake()
    {
        arenaCamera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        var fighters = FindObjectsByType<PlayerNetworkBridge>(FindObjectsSortMode.None);
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
        transform.position = Vector3.SmoothDamp(transform.position, target, ref positionVelocity, positionSmoothTime);

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

        float smoothedSize = Mathf.SmoothDamp(arenaCamera.orthographicSize, requiredSize, ref zoomVelocity, zoomSmoothTime);
        // Expand immediately when needed; ease in when there is spare room.
        arenaCamera.orthographicSize = Mathf.Max(requiredSize, smoothedSize);
    }
}
