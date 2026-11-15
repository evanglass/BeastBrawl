
using System;
using UnityEngine;

namespace BeastBrawl.Music
{
    public sealed class BeastBrawlMusicManager : MonoBehaviour
    {
        public enum PlaybackMode
        {
            Stopped,
            Groove,
            Master
        }

        [Serializable]
        public sealed class Track
        {
            public string displayName;
            public AudioClip clip;

            [Min(1)] public int bpm = 100;
            [Min(1)] public int bars = 8;
        }

        [Header("Music Tracks")]
        public Track[] grooves =
        {
            new Track
            {
                displayName = "Groove 16 Bar",
                bpm = 100,
                bars = 16
            },
            new Track
            {
                displayName = "Groove 8 Bar",
                bpm = 100,
                bars = 8
            }
        };

        public Track[] masters =
        {
            new Track
            {
                displayName = "Master 32 Bar",
                bpm = 100,
                bars = 32
            }
        };

        public Track[] melodies =
        {
            new Track
            {
                displayName = "Eagle Man",
                bpm = 100,
                bars = 4
            },
            new Track
            {
                displayName = "Grandma Tarantula",
                bpm = 100,
                bars = 4
            },
            new Track
            {
                displayName = "Serpent Sam",
                bpm = 100,
                bars = 4
            },
            new Track
            {
                displayName = "Bruce the Bear",
                bpm = 100,
                bars = 4
            }
        };

        [Header("Playback")]
        public bool playOnStart = true;
        public bool showTestControls = true;

        [Range(0f, 1f)]
        public float grooveVolume = 0.8f;

        [Range(0f, 1f)]
        public float masterVolume = 0.8f;

        [Range(0f, 1f)]
        public float melodyVolume = 0.55f;

        public PlaybackMode Mode { get; private set; }
            = PlaybackMode.Stopped;

        public int Bpm { get; private set; } = 100;

        public int BeatInBar
        {
            get { return barClockBeat; }
        }

        public int BarInTrack
        {
            get { return barClockTrack; }
        }

        public string CurrentTrackName
        {
            get
            {
                return activeTrack == null
                    ? "None"
                    : activeTrack.displayName;
            }
        }

        public string QueuedTrackName
        {
            get
            {
                return pending && pendingTrack != null
                    ? pendingTrack.displayName
                    : "None";
            }
        }

        private readonly AudioSource[] grooveSources =
            new AudioSource[2];

        private readonly AudioSource[] masterSources =
            new AudioSource[2];

        private readonly AudioSource[] melodySources =
            new AudioSource[4];

        private readonly bool[] melodyPlaying = new bool[4];
        private readonly bool[] melodyScheduled = new bool[4];
        private readonly bool[] melodyTarget = new bool[4];

        private readonly double[] melodyTime = new double[4];

        private AudioSource activeSource;
        private AudioSource nextSource;

        private Track activeTrack;
        private Track pendingTrack;

        private PlaybackMode nextMode;

        private double sectionStart;
        private double switchTime;
        private double fadeStart;

        private bool pending;
        private bool masterExitFade;

        private int barClockBeat = 1;
        private int barClockTrack = 1;

        private string status = "Ready";

        private const int BeatsPerBar = 4;
        private const double SchedulingLead = 0.15;
        private const double FirstPlayLead = 0.30;

        private double BarSeconds
        {
            get { return 240.0 / Bpm; }
        }

        private int TrackBarCount
        {
            get
            {
                return activeTrack == null
                    ? 0
                    : activeTrack.bars;
            }
        }

        private void Awake()
        {
            for (int i = 0; i < grooveSources.Length; i++)
            {
                grooveSources[i] =
                    MakeSource("Groove Player " + (i + 1));
            }

            for (int i = 0; i < masterSources.Length; i++)
            {
                masterSources[i] =
                    MakeSource("Master Player " + (i + 1));
            }

            for (int i = 0; i < melodySources.Length; i++)
            {
                melodySources[i] =
                    MakeSource("Melody Player " + (i + 1));
            }
        }

        private AudioSource MakeSource(string objectName)
        {
            GameObject child = new GameObject(objectName);

            child.transform.SetParent(transform, false);

            AudioSource source = child.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = true;
            source.volume = 1f;

            return source;
        }

        private void Start()
        {
            if (playOnStart)
            {
                PlayGrooveNow(0);
            }
        }

        private void Update()
        {
            double now = AudioSettings.dspTime;

            if (activeTrack != null && now >= sectionStart)
            {
                double elapsed = Math.Max(
                    0,
                    now - sectionStart
                );

                double measure = BarSeconds;

                barClockTrack =
                    (int)(
                        Math.Floor(elapsed / measure)
                        % TrackBarCount
                    ) + 1;

                UpdateBeatClock(elapsed, measure);
            }

            if (masterExitFade &&
                activeSource != null &&
                now >= fadeStart)
            {
                double duration = switchTime - fadeStart;

                if (duration > 0)
                {
                    float fraction = Mathf.Clamp01(
                        (float)((now - fadeStart) / duration)
                    );

                    activeSource.volume =
                        masterVolume * (1f - fraction);
                }
            }

            if (pending && now >= switchTime)
            {
                if (activeSource != null)
                {
                    activeSource.Stop();
                }

                activeSource = nextSource;
                activeTrack = pendingTrack;

                Mode = nextMode;
                Bpm = activeTrack.bpm;

                sectionStart = switchTime;

                barClockTrack = 1;
                barClockBeat = 1;

                nextSource = null;
                pendingTrack = null;

                pending = false;
                masterExitFade = false;

                status = "Playing " + activeTrack.displayName;
            }

            for (int i = 0; i < melodySources.Length; i++)
            {
                if (!melodyScheduled[i])
                {
                    continue;
                }

                if (now < melodyTime[i])
                {
                    continue;
                }

                melodyScheduled[i] = false;
                melodyPlaying[i] = melodyTarget[i];

                if (!melodyPlaying[i])
                {
                    melodySources[i].Stop();
                }
            }
        }

        private void UpdateBeatClock(
            double elapsed,
            double measure
        )
        {
            double position = elapsed % measure;

            barClockBeat = Math.Min(
                BeatsPerBar,
                (int)Math.Floor(
                    position / (measure / BeatsPerBar)
                ) + 1
            );
        }

        // Direct AudioClip reference.
        // No Resources.Load or resource path required.

        private bool TryLoad(Track track, out AudioClip clip)
        {
            clip = null;

            if (track == null)
            {
                status = "Track configuration is missing";
                Debug.LogWarning(status);
                return false;
            }

            if (track.bpm < 1 || track.bars < 1)
            {
                status = "Invalid BPM or bar count";
                Debug.LogWarning(status);
                return false;
            }

            if (track.clip == null)
            {
                status = "AudioClip missing: " +
                    track.displayName;

                Debug.LogWarning(
                    status +
                    ". Drag an audio file into the Clip field."
                );

                return false;
            }

            clip = track.clip;

            double expected =
                track.bars * BeatsPerBar * 60.0 / track.bpm;

            double difference =
                Math.Abs(clip.length - expected);

            if (difference > 0.05)
            {
                Debug.LogWarning(
                    track.displayName +
                    ": duration " +
                    clip.length.ToString("F3") +
                    "s; expected " +
                    expected.ToString("F3") +
                    "s. Loop may not align with the clock."
                );
            }

            return true;
        }

        private AudioSource AvailableSource(PlaybackMode mode)
        {
            AudioSource[] sources =
                mode == PlaybackMode.Groove
                    ? grooveSources
                    : masterSources;

            foreach (AudioSource source in sources)
            {
                if (source != activeSource &&
                    source != nextSource)
                {
                    return source;
                }
            }

            return null;
        }

        private void Begin(Track track, PlaybackMode mode)
        {
            AudioClip clip;

            if (!TryLoad(track, out clip))
            {
                return;
            }

            StopAllMusic();

            AudioSource source = AvailableSource(mode);

            if (source == null)
            {
                status = "No available audio player";
                return;
            }

            source.clip = clip;
            source.loop = true;

            source.volume =
                mode == PlaybackMode.Groove
                    ? grooveVolume
                    : masterVolume;

            sectionStart =
                AudioSettings.dspTime + FirstPlayLead;

            source.PlayScheduled(sectionStart);

            activeSource = source;
            activeTrack = track;

            Mode = mode;
            Bpm = track.bpm;

            status = "Playing " + track.displayName;
        }

        private double NextBoundary(double period)
        {
            double now =
                AudioSettings.dspTime + SchedulingLead;

            if (now < sectionStart)
            {
                return sectionStart;
            }

            double count =
                Math.Floor(
                    (now - sectionStart) / period
                ) + 1;

            return sectionStart + count * period;
        }

        private void Queue(
            Track track,
            PlaybackMode mode
        )
        {
            AudioClip clip;

            if (!TryLoad(track, out clip))
            {
                return;
            }

            if (pending)
            {
                status =
                    "Transition already queued; wait until it completes";

                Debug.LogWarning(status);
                return;
            }

            if (Mode == PlaybackMode.Stopped)
            {
                Begin(track, mode);
                return;
            }

            double when;

            if (Mode == PlaybackMode.Groove &&
                mode == PlaybackMode.Groove)
            {
                // Wait for the current groove loop to finish.

                when = NextBoundary(
                    activeTrack.bars * BarSeconds
                );
            }
            else if (Mode == PlaybackMode.Master)
            {
                // Finish the next complete bar,
                // fading during that final bar.

                when =
                    NextBoundary(BarSeconds) + BarSeconds;
            }
            else
            {
                // Groove to master:
                // switch at the next bar boundary.

                when = NextBoundary(BarSeconds);
            }

            AudioSource destination = AvailableSource(mode);

            if (destination == null)
            {
                status = "No available music player";
                return;
            }

            destination.clip = clip;
            destination.loop = true;

            destination.volume =
                mode == PlaybackMode.Groove
                    ? grooveVolume
                    : masterVolume;

            destination.PlayScheduled(when);

            if (activeSource != null)
            {
                activeSource.SetScheduledEndTime(when);
            }

            nextSource = destination;
            pendingTrack = track;
            nextMode = mode;

            switchTime = when;
            pending = true;

            masterExitFade =
                Mode == PlaybackMode.Master;

            if (masterExitFade)
            {
                fadeStart = when - BarSeconds;
            }

            if (mode == PlaybackMode.Master)
            {
                StopMelodiesAt(when);
            }

            status = "Queued " + track.displayName;

            Debug.Log(
                status +
                " at DSP " +
                when.ToString("F3")
            );
        }

        public void PlayGrooveNow(int index)
        {
            if (grooves == null ||
                index < 0 ||
                index >= grooves.Length)
            {
                return;
            }

            Begin(grooves[index], PlaybackMode.Groove);
        }

        public void PlayMasterNow(int index)
        {
            if (masters == null ||
                index < 0 ||
                index >= masters.Length)
            {
                return;
            }

            Begin(masters[index], PlaybackMode.Master);
        }

        public void QueueGroove(int index)
        {
            if (grooves == null ||
                index < 0 ||
                index >= grooves.Length)
            {
                return;
            }

            Queue(grooves[index], PlaybackMode.Groove);
        }

        public void QueueMaster(int index)
        {
            if (masters == null ||
                index < 0 ||
                index >= masters.Length)
            {
                return;
            }

            Queue(masters[index], PlaybackMode.Master);
        }

        public void EndMasterToGroove(int index)
        {
            if (Mode != PlaybackMode.Master)
            {
                status = "Master is not playing";
                return;
            }

            QueueGroove(index);
        }

        public void SetMelody(int index, bool enabled)
        {
            if (index < 0 ||
                index >= melodySources.Length ||
                melodies == null ||
                index >= melodies.Length)
            {
                return;
            }

            if (Mode != PlaybackMode.Groove)
            {
                return;
            }

            if (pending &&
                nextMode == PlaybackMode.Master)
            {
                return;
            }

            if (melodyScheduled[index] ||
                melodyPlaying[index] == enabled)
            {
                return;
            }

            Track track = melodies[index];

            if (track == null || track.bpm != Bpm)
            {
                status =
                    "Melody BPM must match groove BPM";

                return;
            }

            AudioSource source = melodySources[index];

            double when = NextBoundary(BarSeconds);

            if (enabled)
            {
                AudioClip clip;

                if (!TryLoad(track, out clip))
                {
                    return;
                }

                source.clip = clip;
                source.loop = true;
                source.volume = melodyVolume;

                source.PlayScheduled(when);
            }
            else
            {
                source.SetScheduledEndTime(when);
            }

            melodyTime[index] = when;
            melodyTarget[index] = enabled;
            melodyScheduled[index] = true;
        }

        private void StopMelodiesAt(double when)
        {
            for (int i = 0; i < melodySources.Length; i++)
            {
                if (!melodyPlaying[i] &&
                    !melodyScheduled[i])
                {
                    continue;
                }

                melodySources[i].SetScheduledEndTime(when);

                melodyTarget[i] = false;
                melodyTime[i] = when;
                melodyScheduled[i] = true;
            }
        }

        public void StopAllMusic()
        {
            foreach (AudioSource source in grooveSources)
            {
                if (source != null)
                {
                    source.Stop();
                }
            }

            foreach (AudioSource source in masterSources)
            {
                if (source != null)
                {
                    source.Stop();
                }
            }

            foreach (AudioSource source in melodySources)
            {
                if (source != null)
                {
                    source.Stop();
                }
            }

            for (int i = 0; i < melodyPlaying.Length; i++)
            {
                melodyPlaying[i] = false;
                melodyScheduled[i] = false;
                melodyTarget[i] = false;
            }

            activeSource = null;
            nextSource = null;

            activeTrack = null;
            pendingTrack = null;

            pending = false;
            masterExitFade = false;

            Mode = PlaybackMode.Stopped;

            barClockBeat = 1;
            barClockTrack = 1;

            status = "Stopped";
        }

        private void OnGUI()
        {
            if (!showTestControls)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(12, 12, 330, 540),
                GUI.skin.box
            );

            GUILayout.Label("BEASTBRAWL MUSIC TEST");

            GUILayout.Label(
                "Mode: " + Mode +
                " | BPM: " + Bpm
            );

            GUILayout.Label(
                "Track: " + CurrentTrackName
            );

            GUILayout.Label(
                "Bar: " + BarInTrack +
                " | Beat: " + BeatInBar
            );

            GUILayout.Label(
                "Queue: " + QueuedTrackName
            );

            GUILayout.Label(
                "Status: " + status
            );

            GUILayout.Space(8);

            if (GUILayout.Button("PLAY Groove 16 NOW"))
            {
                PlayGrooveNow(0);
            }

            if (GUILayout.Button("PLAY Groove 8 NOW"))
            {
                PlayGrooveNow(1);
            }

            if (GUILayout.Button("QUEUE Groove 16 at groove end"))
            {
                QueueGroove(0);
            }

            if (GUILayout.Button("QUEUE Groove 8 at groove end"))
            {
                QueueGroove(1);
            }

            GUILayout.Space(8);

            if (GUILayout.Button("QUEUE Master 32 at next bar"))
            {
                QueueMaster(0);
            }

            if (GUILayout.Button("END Master -> Groove 16"))
            {
                EndMasterToGroove(0);
            }

            if (GUILayout.Button("END Master -> Groove 8"))
            {
                EndMasterToGroove(1);
            }

            GUILayout.Space(8);

            for (int i = 0; i < 4; i++)
            {
                bool on = melodyScheduled[i]
                    ? melodyTarget[i]
                    : melodyPlaying[i];

                string buttonText =
                    "Melody " + (i + 1) +
                    (on ? " OFF" : " ON");

                if (GUILayout.Button(buttonText))
                {
                    SetMelody(i, !on);
                }
            }

            GUILayout.Space(8);

            if (GUILayout.Button("STOP ALL"))
            {
                StopAllMusic();
            }

            GUILayout.EndArea();
        }
    }
}
