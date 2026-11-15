using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Plays character effects locally using positions in this instance's camera.</summary>
public class CharacterSfxManager : MonoBehaviour
{
    public enum Effect { Attack, Jump, Hit, Land, BeastActivation, BeastAttack }

    [Serializable]
    public class SoundEffect
    {
        public AudioClip clip;
        [SoundPercent(100f, "Clip Volume")] public float volume = 1f;
        [Range(0.1f, 3f)] public float pitch = 1f;
        [SoundPercent(800f, "Volume Boost")] public float gain = 1f;
        [Range(0f, 1f)] public float bitCrush = 0f;
        [Min(0f)] public float startSeconds;
    }

    [Serializable]
    public class NamedSoundEffect
    {
        public string name;
        public SoundEffect sound = new SoundEffect();
    }

    [Serializable]
    public class CharacterSounds
    {
        public SoundEffect attack = new SoundEffect();
        public SoundEffect jump = new SoundEffect();
        public SoundEffect hit = new SoundEffect();
        public SoundEffect land = new SoundEffect();
        public SoundEffect beastActivation = new SoundEffect();
        public SoundEffect beastAttack = new SoundEffect();
        public NamedSoundEffect[] otherEffects = Array.Empty<NamedSoundEffect>();

        public SoundEffect Get(Effect effect)
        {
            switch (effect)
            {
                case Effect.Attack: return attack;
                case Effect.Jump: return jump;
                case Effect.Hit: return hit;
                case Effect.Land: return land;
                case Effect.BeastActivation: return beastActivation;
                case Effect.BeastAttack: return beastAttack;
                default: return null;
            }
        }
    }

    public static CharacterSfxManager Instance { get; private set; }

    [Header("Character Effects")]
    public CharacterSounds eagle = new CharacterSounds();
    public CharacterSounds bear = new CharacterSounds();
    public CharacterSounds cougar = new CharacterSounds();
    public CharacterSounds snake = new CharacterSounds();

    [Header("Camera-relative sound")]
    [Tooltip("Overall sound-effects level, from 0% to 400%.")]
    [SerializeField, SoundPercent(400f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float stereoWidth = 1f;
    [Tooltip("Pitch variation from bottom to top of the camera view. Set to zero to disable height coloration.")]
    [SerializeField, Range(0f, 0.25f)] private float heightPitchVariation = 0.06f;
    [SerializeField] private Camera viewCamera;
    private readonly List<CharacterSfxFilter> activeFilters = new List<CharacterSfxFilter>();
    public float MasterVolume => Mathf.Clamp(masterVolume, 0f, 4f);
    public void SetMasterVolume(float value) => masterVolume = Mathf.Clamp(value, 0f, 4f);

    private void Update()
    {
        for (int i = activeFilters.Count - 1; i >= 0; i--)
        {
            if (activeFilters[i] == null) activeFilters.RemoveAt(i);
            else activeFilters[i].masterGain = MasterVolume;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    public static CharacterSfxManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        var prefab = Resources.Load<GameObject>("CharacterSfxManager");
        if (prefab == null)
        {
            Debug.LogError("Character SFX Manager prefab is missing from Resources.");
            return null;
        }
        return Instantiate(prefab).GetComponent<CharacterSfxManager>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Play(PlayerVisual.Character character, Effect effect, Vector3 worldPosition)
    {
        CharacterSounds sounds = GetCharacterSounds(character);
        if (sounds != null) PlaySound(sounds.Get(effect), worldPosition);
    }

    public void PlayMoving(PlayerVisual.Character character, Effect effect, Vector3 origin, Vector3 velocity, float travelSeconds)
    {
        CharacterSounds sounds = GetCharacterSounds(character);
        if (sounds == null) return;
        float duration = PlaySound(sounds.Get(effect), origin, out AudioSource source);
        if (source != null) StartCoroutine(MoveSound(source, sounds.Get(effect), origin, velocity, travelSeconds, duration));
    }

    private IEnumerator MoveSound(AudioSource source, SoundEffect sound, Vector3 origin, Vector3 velocity, float travelSeconds, float duration)
    {
        float elapsed = 0f;
        while (source != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Vector3 position = origin + velocity * Mathf.Min(elapsed, Mathf.Max(0f, travelSeconds));
            SetPosition(source, sound, position);
            yield return null;
        }
    }

    // Cougar and Snake have a single transformation-to-special sequence per activation.
    public void PlayBeastActivation(PlayerVisual.Character character, Transform fighter)
    {
        CharacterSounds sounds = GetCharacterSounds(character);
        if (sounds == null || fighter == null) return;
        float duration = PlaySound(sounds.beastActivation, fighter.position);
        if (character == PlayerVisual.Character.Cougar || character == PlayerVisual.Character.Snake)
            StartCoroutine(PlayFollowingSound(sounds.beastAttack, fighter, duration));
    }

    private IEnumerator PlayFollowingSound(SoundEffect sound, Transform fighter, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        if (fighter != null) PlaySound(sound, fighter.position);
    }

    /// <summary>Call when a future character-specific gameplay event occurs.</summary>
    public void PlayOther(PlayerVisual.Character character, string effectName, Vector3 worldPosition)
    {
        CharacterSounds sounds = GetCharacterSounds(character);
        if (sounds?.otherEffects == null) return;
        foreach (NamedSoundEffect effect in sounds.otherEffects)
        {
            if (effect != null && effect.name == effectName)
            {
                PlaySound(effect.sound, worldPosition);
                return;
            }
        }
    }

    private CharacterSounds GetCharacterSounds(PlayerVisual.Character character)
    {
        switch (character)
        {
            case PlayerVisual.Character.Eagle: return eagle;
            case PlayerVisual.Character.Bear: return bear;
            case PlayerVisual.Character.Cougar: return cougar;
            case PlayerVisual.Character.Snake: return snake;
            default: return null;
        }
    }

    private float PlaySound(SoundEffect sound, Vector3 worldPosition)
    {
        return PlaySound(sound, worldPosition, out _);
    }

    private void SetPosition(AudioSource source, SoundEffect sound, Vector3 worldPosition)
    {
        Camera camera = viewCamera != null ? viewCamera : Camera.main;
        Vector3 viewport = camera != null ? camera.WorldToViewportPoint(worldPosition) : new Vector3(0.5f, 0.5f, 0f);
        source.panStereo = Mathf.Clamp(viewport.x * 2f - 1f, -1f, 1f) * stereoWidth;
        float height = Mathf.Clamp(viewport.y * 2f - 1f, -1f, 1f);
        source.pitch = Mathf.Clamp(sound.pitch * (1f + height * heightPitchVariation), 0.1f, 3f);
        source.transform.position = worldPosition;
    }

    private float PlaySound(SoundEffect sound, Vector3 worldPosition, out AudioSource source)
    {
        source = null;
        if (sound == null || sound.clip == null || sound.volume <= 0f) return 0f;
        var voice = new GameObject("Character SFX Voice");
        voice.transform.SetParent(transform, false);
        source = voice.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        SetPosition(source, sound, worldPosition);
        source.volume = Mathf.Clamp01(sound.volume);
        source.clip = sound.clip;
        float offset = Mathf.Clamp(sound.startSeconds, 0f, Mathf.Max(0f, sound.clip.length - 1f / sound.clip.frequency));
        source.timeSamples = Mathf.Clamp(Mathf.RoundToInt(offset * sound.clip.frequency), 0, sound.clip.samples - 1);
        var filter = voice.AddComponent<CharacterSfxFilter>();
        filter.gain = sound.gain;
        filter.bitCrush = sound.bitCrush;
        filter.masterGain = MasterVolume;
        activeFilters.Add(filter);
        source.Play();
        float duration = (sound.clip.length - offset) / source.pitch;
        Destroy(voice, duration + 0.1f);
        return duration;
    }
}

// Serialized values remain multipliers so existing scenes and sound settings stay intact.
public sealed class SoundPercentAttribute : PropertyAttribute
{
    public readonly float maximum;
    public readonly string label;
    public SoundPercentAttribute(float maximum, string label = null)
    {
        this.maximum = maximum;
        this.label = label;
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(SoundPercentAttribute))]
public sealed class SoundPercentDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        var settings = (SoundPercentAttribute)attribute;
        var caption = new GUIContent((settings.label ?? label.text) + " (%)", label.tooltip);
        UnityEditor.EditorGUI.BeginProperty(position, caption, property);
        UnityEditor.EditorGUI.BeginChangeCheck();
        float percent = UnityEditor.EditorGUI.Slider(position, caption, property.floatValue * 100f, 0f, settings.maximum);
        if (UnityEditor.EditorGUI.EndChangeCheck()) property.floatValue = percent / 100f;
        UnityEditor.EditorGUI.EndProperty();
    }
}
#endif

// Applied only to the requested voices; the original audio assets remain untouched.
public sealed class CharacterSfxFilter : MonoBehaviour
{
    public float gain = 1f;
    public float bitCrush;
    public volatile float masterGain = 1f;

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float master = masterGain;
        for (int i = 0; i < data.Length; i++)
        {
            float sample = data[i] * gain;
            float crushed = Mathf.Round(sample * 64f) / 64f;
            data[i] = Mathf.Clamp(Mathf.Lerp(sample, crushed, bitCrush), -1f, 1f) * master;
        }
    }
}
