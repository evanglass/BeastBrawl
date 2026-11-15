using System;
using UnityEngine;

/// <summary>Plays character effects locally using positions in this instance's camera.</summary>
public class CharacterSfxManager : MonoBehaviour
{
    public enum Effect { Attack, Jump, Hit, Land }

    [Serializable]
    public class SoundEffect
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
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
        public NamedSoundEffect[] otherEffects = Array.Empty<NamedSoundEffect>();

        public SoundEffect Get(Effect effect)
        {
            switch (effect)
            {
                case Effect.Attack: return attack;
                case Effect.Jump: return jump;
                case Effect.Hit: return hit;
                case Effect.Land: return land;
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
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float stereoWidth = 1f;
    [Tooltip("Pitch variation from bottom to top of the camera view. Set to zero to disable height coloration.")]
    [SerializeField, Range(0f, 0.25f)] private float heightPitchVariation = 0.06f;
    [SerializeField] private Camera viewCamera;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Only one Character SFX Manager should be active in a scene.", this);
            enabled = false;
            return;
        }
        Instance = this;
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

    private void PlaySound(SoundEffect sound, Vector3 worldPosition)
    {
        if (sound == null || sound.clip == null || sound.volume <= 0f || masterVolume <= 0f) return;
        Camera camera = viewCamera != null ? viewCamera : Camera.main;
        Vector3 viewport = camera != null ? camera.WorldToViewportPoint(worldPosition) : new Vector3(0.5f, 0.5f, 0f);
        float pan = Mathf.Clamp(viewport.x * 2f - 1f, -1f, 1f) * stereoWidth;
        float height = Mathf.Clamp(viewport.y * 2f - 1f, -1f, 1f);
        var voice = new GameObject("Character SFX Voice");
        voice.transform.SetParent(transform, false);
        AudioSource source = voice.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.panStereo = pan;
        source.pitch = 1f + height * heightPitchVariation;
        source.volume = Mathf.Clamp01(sound.volume) * masterVolume;
        source.clip = sound.clip;
        source.Play();
        Destroy(voice, sound.clip.length / source.pitch + 0.1f);
    }
}
