using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.UIElements;
#endif

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
            else
            {
                activeFilters[i].masterGain = MasterVolume;
#if UNITY_WEBGL && !UNITY_EDITOR
                AudioSource source = activeFilters[i].GetComponent<AudioSource>();
                if (source != null)
                    source.volume = Mathf.Clamp01(activeFilters[i].baseVolume * MasterVolume);
#endif
            }
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
        character = PlayerVisual.OriginalCharacter(character);
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
        switch (PlayerVisual.OriginalCharacter(character))
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
        filter.baseVolume = Mathf.Clamp01(sound.volume);
        filter.bitCrush = sound.bitCrush;
        filter.masterGain = MasterVolume;
#if UNITY_WEBGL && !UNITY_EDITOR
        source.volume = Mathf.Clamp01(filter.baseVolume * MasterVolume);
#endif
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
    public override UnityEngine.UIElements.VisualElement CreatePropertyGUI(UnityEditor.SerializedProperty property)
    {
        var settings = (SoundPercentAttribute)attribute;
        var slider = new UnityEngine.UIElements.Slider((settings.label ?? property.displayName) + " (%)", 0f, settings.maximum)
        {
            showInputField = true,
            value = property.floatValue * 100f,
            tooltip = property.tooltip
        };
        slider.RegisterValueChangedCallback(evt =>
        {
            property.serializedObject.Update();
            property.floatValue = evt.newValue / 100f;
            property.serializedObject.ApplyModifiedProperties();
        });
        UnityEditor.UIElements.BindingExtensions.TrackPropertyValue(slider, property,
            p => slider.SetValueWithoutNotify(p.floatValue * 100f));
        return slider;
    }

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

[UnityEditor.CustomPropertyDrawer(typeof(CharacterSfxManager.SoundEffect))]
public sealed class CharacterSoundEffectDrawer : UnityEditor.PropertyDrawer
{
    public override UnityEngine.UIElements.VisualElement CreatePropertyGUI(UnityEditor.SerializedProperty property)
    {
        var foldout = new UnityEngine.UIElements.Foldout { text = property.displayName, value = property.isExpanded };
        foldout.RegisterValueChangedCallback(e => property.isExpanded = e.newValue);
        var volume = property.FindPropertyRelative("volume");
        var gain = property.FindPropertyRelative("gain");
        foldout.Add(new UnityEditor.UIElements.PropertyField(property.FindPropertyRelative("clip")));
        var slider = new UnityEngine.UIElements.Slider("Volume (%)", 0f, 800f)
        {
            showInputField = true,
            value = volume.floatValue * gain.floatValue * 100f
        };
        slider.RegisterValueChangedCallback(e =>
        {
            property.serializedObject.Update();
            volume.floatValue = 1f;
            gain.floatValue = e.newValue / 100f;
            property.serializedObject.ApplyModifiedProperties();
        });
        UnityEditor.UIElements.BindingExtensions.TrackPropertyValue(slider, volume,
            p => slider.SetValueWithoutNotify(p.floatValue * gain.floatValue * 100f));
        UnityEditor.UIElements.BindingExtensions.TrackPropertyValue(slider, gain,
            p => slider.SetValueWithoutNotify(volume.floatValue * p.floatValue * 100f));
        foldout.Add(slider);
        foldout.Add(new UnityEditor.UIElements.PropertyField(property.FindPropertyRelative("pitch")));
        foldout.Add(new UnityEditor.UIElements.PropertyField(property.FindPropertyRelative("bitCrush")));
        foldout.Add(new UnityEditor.UIElements.PropertyField(property.FindPropertyRelative("startSeconds")));
        return foldout;
    }

    public override float GetPropertyHeight(UnityEditor.SerializedProperty property, GUIContent label)
        => UnityEditor.EditorGUIUtility.singleLineHeight * (property.isExpanded ? 6 : 1) +
           UnityEditor.EditorGUIUtility.standardVerticalSpacing * (property.isExpanded ? 5 : 0);

    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        float line = UnityEditor.EditorGUIUtility.singleLineHeight;
        float step = line + UnityEditor.EditorGUIUtility.standardVerticalSpacing;
        var row = new Rect(position.x, position.y, position.width, line);
        property.isExpanded = UnityEditor.EditorGUI.Foldout(row, property.isExpanded, label, true);
        if (!property.isExpanded) return;
        UnityEditor.EditorGUI.indentLevel++;
        row.y += step;
        UnityEditor.EditorGUI.PropertyField(row, property.FindPropertyRelative("clip"));
        var volume = property.FindPropertyRelative("volume");
        var gain = property.FindPropertyRelative("gain");
        row.y += step;
        UnityEditor.EditorGUI.BeginChangeCheck();
        float percent = UnityEditor.EditorGUI.Slider(row, "Volume (%)", volume.floatValue * gain.floatValue * 100f, 0f, 800f);
        if (UnityEditor.EditorGUI.EndChangeCheck()) { volume.floatValue = 1f; gain.floatValue = percent / 100f; }
        foreach (string name in new[] { "pitch", "bitCrush", "startSeconds" })
        {
            row.y += step;
            UnityEditor.EditorGUI.PropertyField(row, property.FindPropertyRelative(name));
        }
        UnityEditor.EditorGUI.indentLevel--;
    }
}
#endif

// Applied only to the requested voices; the original audio assets remain untouched.
public sealed class CharacterSfxFilter : MonoBehaviour
{
    public float gain = 1f;
    public float baseVolume = 1f;
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
