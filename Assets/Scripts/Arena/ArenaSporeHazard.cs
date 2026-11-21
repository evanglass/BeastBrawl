using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The server samples the entire box once per physics step. Extra colliders can't add extra ticks.
[RequireComponent(typeof(BoxCollider2D))]
public sealed class ArenaSporeHazard : MonoBehaviour
{
    [SerializeField, Min(0f)] private float consciousnessLoss = 12f;
    [SerializeField, Min(0.1f)] private float tickInterval = 1f;
    [SerializeField] private Texture2D sparkle;
    [SerializeField, Min(0f)] private float particlesPerSecond = 24f;

    private static readonly HashSet<ArenaSporeHazard> zones = new();
    private readonly Collider2D[] hits = new Collider2D[64];
    private readonly Dictionary<PlayerActor, float> nextTick = new();
    private readonly HashSet<PlayerActor> inside = new();
    private readonly List<PlayerActor> departed = new();
    private BoxCollider2D zone;
    private ContactFilter2D fighterFilter;
    private MatchManager match;
    private Material particleMaterial;

    private void Awake()
    {
        zone = GetComponent<BoxCollider2D>();
        fighterFilter.SetLayerMask(LayerMask.GetMask("Players"));
        fighterFilter.useTriggers = false;
    }

    private void OnEnable() => zones.Add(this);
    private void OnDisable()
    {
        zones.Remove(this);
        nextTick.Clear();
    }

    private void Start()
    {
        match = FindFirstObjectByType<MatchManager>();
#if !UNITY_SERVER || UNITY_EDITOR
        CreateSpores();
#endif
    }

    private void FixedUpdate()
    {
        if (match == null || !match.IsSpawned || !match.IsServer ||
            match.CurrentPhase != MatchManager.MatchPhase.Fighting)
        {
            nextTick.Clear();
            return;
        }

        float now = Time.fixedTime;
        float interval = Mathf.Max(0.1f, tickInterval);
        inside.Clear();
        int count = zone.Overlap(fighterFilter, hits);
        for (int i = 0; i < count; i++)
        {
            var body = hits[i].attachedRigidbody;
            if (body == null || !body.TryGetComponent(out PlayerActor fighter) || !inside.Add(fighter))
                continue;

            if (!nextTick.TryGetValue(fighter, out float due))
                nextTick.Add(fighter, now + interval);
            else if (now >= due)
            {
                fighter.ApplyEnvironmentalExposure(consciousnessLoss, interval);
                nextTick[fighter] = now + interval;
            }
        }

        departed.Clear();
        foreach (var entry in nextTick)
            if (!inside.Contains(entry.Key)) departed.Add(entry.Key);
        foreach (var fighter in departed) nextTick.Remove(fighter);
    }

    // Pathfinding uses this for landing points; the trigger is never treated as a platform.
    internal static bool Contains(Scene scene, Vector2 point)
    {
        foreach (var hazard in zones)
            if (hazard.gameObject.scene == scene && hazard.zone.OverlapPoint(point)) return true;
        return false;
    }

    private void CreateSpores()
    {
        if (sparkle == null || particlesPerSecond <= 0f) return;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) return;

        var emitter = new GameObject("Drifting spores");
        emitter.transform.SetParent(transform, false);
        emitter.transform.localPosition = zone.offset;
        var spores = emitter.AddComponent<ParticleSystem>();
        spores.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = spores.main;
        main.loop = true;
        main.duration = 5f;
        main.prewarm = true;
        main.maxParticles = 120;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 3.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.19f);
        main.startSpeed = 0f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color32(117, 167, 67, 190), new Color32(208, 218, 145, 240));
        main.gravityModifier = 0.006f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = spores.emission;
        emission.rateOverTime = particlesPerSecond;
        var shape = spores.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(zone.size.x, zone.size.y * .9f, .01f);
        var fade = spores.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f),
                new GradientAlphaKey(.8f, .75f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        particleMaterial = new Material(shader) { mainTexture = sparkle };
        var renderer = emitter.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.sortingOrder = -7;
        spores.Play();
    }

    private void OnDestroy()
    {
        if (particleMaterial != null) Destroy(particleMaterial);
    }
}
