using System.Collections.Generic;
using Unity.Netcode;
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

    public enum CameraMode { Legacy, Clustered }

    [Header("Clustered mode")]
    [SerializeField] private CameraMode mode;
    [SerializeField, Min(.05f)] private float clusterRefresh = .15f;
    [SerializeField, Min(0f)] private float clusterPersistence = .5f;
    [SerializeField, Min(.1f)] private float mergeDistance = 8f;
    [SerializeField, Min(.1f)] private float splitDistance = 11f;
    [SerializeField, Min(.1f)] private float mergeSize = 9f;
    [SerializeField, Min(.1f)] private float splitSize = 11f;
    [SerializeField, Min(.1f)] private float finalTwoMaximumSize = 24f;
    [SerializeField, Min(.1f)] private float maximumTrackingSpeed = 30f;

    private static SharedArenaCamera active;
    public static Camera ActiveCamera => active != null && active.isActiveAndEnabled &&
        active.arenaCamera.isActiveAndEnabled ? active.arenaCamera : null;

    public CameraMode Mode
    {
        get => mode;
        set => mode = value;
    }

    private readonly ArenaCameraClusters clusters = new();
    private readonly List<ArenaCameraClusters.Fighter> samples = new(8);
    private MatchManager match;
    private CameraMode previousMode;
    private float nextClustering;
    private Bounds arenaBounds;
    private Scene arenaScene;
    private bool hasArenaBounds;
    private ulong? lastLocalFighter;

    private Camera arenaCamera;
    private Vector3 positionVelocity;
    private float zoomVelocity;
    private bool trackingFighters;

    private void Awake()
    {
        arenaCamera = GetComponent<Camera>();
        // Avoid Unity's bright default blue when an arena has not rendered behind the camera.
        arenaCamera.backgroundColor = new Color(.035f, .055f, .075f, 1f);
        previousMode = mode;
    }

    private void OnEnable()
    {
        active = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            OnSceneLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
    }

    private void OnDisable()
    {
        if (active == this) active = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        ResetClusters();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene != arenaScene) return;
        hasArenaBounds = false;
        ResetClusters();
    }

    private void ResetClusters()
    {
        clusters.Reset();
        nextClustering = 0f;
        lastLocalFighter = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (System.Array.IndexOf(SceneLoader.CustomArenas, scene.name) < 0) return;
        // Arena cameras are for authoring. The persistent gameplay camera owns the view and listener.
        var listener = arenaCamera.GetComponent<AudioListener>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                if (camera != arenaCamera) camera.enabled = false;
            foreach (var other in root.GetComponentsInChildren<AudioListener>(true))
                if (other != listener) other.enabled = false;
        }
        arenaScene = scene;
        ResetClusters();
        bool found = false;
        Bounds bounds = default;
        foreach (var collider in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (collider.gameObject.scene != scene || !collider.CompareTag("Level Geometry") || collider.isTrigger) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        hasArenaBounds = found;
        arenaBounds = bounds;
        if (!found || this.mode != CameraMode.Legacy) return;
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
        if (previousMode != mode)
        {
            previousMode = mode;
            ResetClusters();
            if (mode == CameraMode.Legacy)
            {
                positionVelocity = Vector3.zero;
                zoomVelocity = 0f;
            }
        }

        if (match == null) match = FindFirstObjectByType<MatchManager>();
        var network = NetworkManager.Singleton;
        if (match != null && match.IsSpawned && match.CurrentPhase == MatchManager.MatchPhase.Overview &&
            network != null && network.ServerTime.Time < match.PhaseDeadline)
        {
            if (mode == CameraMode.Clustered) TrackOverview();
            return;
        }

        if (mode == CameraMode.Clustered)
            TrackCluster(network);
        else
            TrackLegacy();
    }

    // Original shared framing, still used by the Legacy mode.
    private void TrackLegacy()
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

    private void TrackCluster(NetworkManager network)
    {
        if (network == null || !network.IsClient) return;
        samples.Clear();
        int local = -1;
        var playerObject = network.LocalClient?.PlayerObject;
        foreach (var fighter in PlayerNetworkBridge.All)
        {
            if (fighter == null || !fighter.IsSpawned || fighter.Eliminated) continue;
            var collider = fighter.GetComponent<Collider2D>();
            Bounds bounds = collider != null && collider.enabled
                ? collider.bounds : new Bounds(fighter.transform.position, Vector3.zero);
            if (fighter.NetworkObject == playerObject && !fighter.IsBotControlled) local = samples.Count;
            samples.Add(new ArenaCameraClusters.Fighter(fighter.NetworkObjectId, bounds, fighter.TeamId));
        }
        if (samples.Count == 0)
        {
            if (clusters.FighterCount != 0) ResetClusters();
            TrackOverview();
            return;
        }

        ulong? localId = local >= 0 ? samples[local].Id : null;
        float aspect = Mathf.Max(arenaCamera.aspect, .01f);
        Vector2 padding = new(horizontalPadding, verticalPadding);
        float normalMaximum = Mathf.Max(minimumSize, splitSize, mergeSize);
        float now = Time.unscaledTime;
        if (now >= nextClustering || localId != lastLocalFighter || !HasCurrentMembership())
        {
            lastLocalFighter = localId;
            nextClustering = now + Mathf.Max(.05f, clusterRefresh);
            samples.Sort((a, b) => a.Id.CompareTo(b.Id));
            local = localId.HasValue ? ArenaCameraClusters.Find(samples, localId.Value) : -1;
            clusters.Update(samples, now, Mathf.Max(0f, clusterPersistence), mergeDistance,
                Mathf.Max(mergeDistance, splitDistance), mergeSize, normalMaximum, aspect, padding);
        }

        var group = local >= 0 ? clusters.FindGroup(samples[local].Id) : clusters.SpectatorGroup();
        if (group == null) return;
        Bounds framing = ArenaCameraClusters.GetBounds(group, samples);
        bool finalTwo = samples.Count == 2;
        float maximum = finalTwo ? Mathf.Max(normalMaximum, finalTwoMaximumSize) : normalMaximum;
        // A distant shared view may need more room than the normal engagement zoom allows.
        float required = ArenaCameraClusters.SizeFor(framing, aspect, padding);
        float fightersOnly = ArenaCameraClusters.SizeFor(framing, aspect, Vector2.zero) + .15f;
        float targetSize = Mathf.Max(Mathf.Clamp(required, minimumSize, maximum), fightersOnly);
        Vector2 target = framing.center;
        if (hasArenaBounds && !finalTwo)
            target = KeepVisible(ConstrainView(target, arenaBounds, targetSize, aspect), framing, targetSize, aspect);
        MoveClusterCamera(target, targetSize);
    }

    private void TrackOverview()
    {
        if (!hasArenaBounds) return;
        float aspect = Mathf.Max(arenaCamera.aspect, .01f);
        float size = ArenaCameraClusters.SizeFor(arenaBounds, aspect, new Vector2(horizontalPadding, verticalPadding));
        MoveClusterCamera(arenaBounds.center, Mathf.Max(minimumSize, size));
    }

    private void MoveClusterCamera(Vector2 target, float targetSize)
    {
        float dt = Time.unscaledDeltaTime;
        float speed = Mathf.Max(.1f, maximumTrackingSpeed);
        Vector3 current = transform.position;
        Vector3 next = Vector3.SmoothDamp(current, new Vector3(target.x, target.y, current.z),
            ref positionVelocity, positionSmoothTime, speed, dt);
        // Cap the actual step too, including frames carrying velocity from an earlier target.
        transform.position = Vector3.MoveTowards(current, next, speed * dt);
        arenaCamera.orthographicSize = Mathf.SmoothDamp(arenaCamera.orthographicSize, targetSize,
            ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);
        trackingFighters = true;
    }

    private bool HasCurrentMembership()
    {
        // Check both directions so an elimination is reflected before the next scheduled regroup.
        if (clusters.FighterCount != samples.Count) return false;
        foreach (var sample in samples)
            if (clusters.FindGroup(sample.Id) == null) return false;
        return true;
    }

    private static Vector2 ConstrainView(Vector2 center, Bounds stage, float size, float aspect)
    {
        float x = Mathf.Min(stage.extents.x, size * aspect);
        float y = Mathf.Min(stage.extents.y, size);
        return new Vector2(Mathf.Clamp(center.x, stage.min.x + x, stage.max.x - x),
            Mathf.Clamp(center.y, stage.min.y + y, stage.max.y - y));
    }

    private static Vector2 KeepVisible(Vector2 center, Bounds focus, float size, float aspect)
    {
        const float margin = .35f;
        float x = Mathf.Max(focus.extents.x, size * aspect - margin);
        float y = Mathf.Max(focus.extents.y, size - margin);
        return new Vector2(Mathf.Clamp(center.x, focus.max.x - x, focus.min.x + x),
            Mathf.Clamp(center.y, focus.max.y - y, focus.min.y + y));
    }
}
