
using System;
using System.Collections.Generic;
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

        public enum MusicCategory
        {
            Drums,
            Groove,
            Riff,
            Lead,
            SoloSong,
            SoloLead
        }

        [Serializable]
        public sealed class LoopTrack
        {
            public string displayName = "New Track";
            public AudioClip clip;

            [Min(1)]
            public int bars = 8;

            [Range(0f, 1f)]
            public float volume = 1f;
        }

        [Serializable]
        public sealed class FeatureTrack
        {
            public string displayName = "New Track";
            public AudioClip clip;

            public bool hasLeadIn;

            [Min(0)]
            public int leadInBars;

            [Range(0f, 1f)]
            public float volume = 1f;
        }

        [Serializable]
        public sealed class CharacterTracks
        {
            public FeatureTrack[] riffs =
                new FeatureTrack[0];

            public FeatureTrack[] leads =
                new FeatureTrack[0];
        }

        [Header("Drums")]
        public LoopTrack[] drums =
        {
            new LoopTrack
            {
                displayName = "Drums",
                bars = 8
            }
        };

        [Header("Stereo Grooves")]
        public LoopTrack[] grooves =
        {
            new LoopTrack
            {
                displayName = "Groove 16",
                bars = 16
            }
        };

        [Header("Bruce the Bear")]
        public CharacterTracks bruce =
            new CharacterTracks();

        [Header("Eagle Man")]
        public CharacterTracks eagle =
            new CharacterTracks();

        [Header("Crimson Cougar")]
        public CharacterTracks cougar =
            new CharacterTracks();

        [Header("Serpent Sam")]
        public CharacterTracks serpent =
            new CharacterTracks();

        [Header("Solo Songs")]
        public LoopTrack[] soloSongs =
            new LoopTrack[0];

        [Header("Solo Leads")]
        public LoopTrack[] soloLeads =
            new LoopTrack[0];

        [Header("Playback")]
        public bool playOnStart = true;
        public bool showTestControls = true;

        [Min(1)]
        public int globalBpm = 100;

        [Range(0f, 1f)]
        public float drumVolume = 0.8f;

        [Range(0f, 1f)]
        public float grooveVolume = 0.8f;

        [Range(0f, 1f)]
        public float riffVolume = 0.65f;

        [Range(0f, 1f)]
        public float leadVolume = 0.65f;

        [Range(0f, 1f)]
        public float soloVolume = 0.8f;

        [Range(-80f, -20f)]
        public float fadeSilenceDb = -80f;

        [Range(0f, 1f)]
        public float sidePan = 0.5f;

        [Min(1)]
        public int soloLeadPanCycleBars = 4;

        [Header("Automation")]
        public bool automaticGrooveReturn = true;
        public bool alternateDrumsEvery16Bars = true;

        private const int BeatsPerBar = 4;
        private const int BarsPerBlock = 4;
        private const int DrumSectionBars = 16;

        private const double ScheduleLead = 0.75;
        private const double FirstDelay = 1.5;
        private const double Safety = 0.05;

        private const int SearchLimit = 256;

        private sealed class Voice
        {
            public AudioSource source;
            public Job current;
        }

        private sealed class Job
        {
            public MusicCategory category;
            public Voice voice;
            public AudioClip clip;
            public string name;

            public int character;
            public int block;
            public int side;

            public double start;
            public double anchor;
            public double end;

            public double fadeInStart = double.NaN;
            public double fadeInEnd = double.NaN;

            public double fadeOutStart = double.NaN;
            public double fadeOutEnd = double.NaN;

            public float gain;

            public bool loop;
            public bool committed;
            public bool cancelled;
            public bool failed;
            public bool autoGroove;

            public int prerollBars;
        }

        private readonly List<Job> jobs =
            new List<Job>();

        private readonly Dictionary<int, int> sides =
            new Dictionary<int, int>();

        private readonly Dictionary<MusicCategory, Voice[]> pools =
            new Dictionary<MusicCategory, Voice[]>();

        private readonly Dictionary<MusicCategory, int>
            featureNextBlock =
                new Dictionary<MusicCategory, int>();

        private double origin;

        private int alternateSide = -1;
        private int drumIndex = -1;
        private int nextDrumBlock = -1;
        private int forcedDrumIndex = -1;

        private bool drumsEnabled;

        private Job pendingAutoGroove;
        private Job currentGroove;

        private double manualGrooveExitUntil;

        private bool initialized;

        private string status = "Ready";
        private Vector2 scroll;

        private double BarTime
        {
            get
            {
                return 240.0 / globalBpm;
            }
        }

        private double BlockTime
        {
            get
            {
                return 4.0 * BarTime;
            }
        }

        public int Bpm
        {
            get
            {
                return globalBpm;
            }
        }

        public int BarInTrack
        {
            get
            {
                if (!initialized)
                    return 1;

                double elapsed = Math.Max(
                    0,
                    AudioSettings.dspTime - origin
                );

                return Math.Max(
                    1,
                    (int)Math.Floor(
                        elapsed / BarTime
                    ) + 1
                );
            }
        }

        public int BeatInBar
        {
            get
            {
                if (!initialized)
                    return 1;

                double elapsed = Math.Max(
                    0,
                    AudioSettings.dspTime - origin
                );

                return (int)Math.Floor(
                    (elapsed % BarTime) /
                    (BarTime / BeatsPerBar)
                ) + 1;
            }
        }

        public PlaybackMode Mode
        {
            get
            {
                double now = AudioSettings.dspTime;

                foreach (Job job in jobs)
                {
                    if (Valid(job) &&
                        job.category ==
                            MusicCategory.SoloSong &&
                        job.start <= now &&
                        now < job.end)
                    {
                        return PlaybackMode.Master;
                    }
                }

                foreach (Job job in jobs)
                {
                    if (Valid(job) &&
                        job.start <= now &&
                        now < job.end)
                    {
                        return PlaybackMode.Groove;
                    }
                }

                return PlaybackMode.Stopped;
            }
        }

        public string CurrentTrackName
        {
            get
            {
                return Names(false);
            }
        }

        public string QueuedTrackName
        {
            get
            {
                return Names(true);
            }
        }

        private void Awake()
        {
            globalBpm = Mathf.Max(1, globalBpm);

            CreatePool(MusicCategory.Drums, 2);
            CreatePool(MusicCategory.Groove, 2);
            CreatePool(MusicCategory.Riff, 2);
            CreatePool(MusicCategory.Lead, 2);
            CreatePool(MusicCategory.SoloSong, 2);
            CreatePool(MusicCategory.SoloLead, 1);

            origin = AudioSettings.dspTime + FirstDelay;

            initialized = true;
        }

        private void CreatePool(
            MusicCategory category,
            int count
        )
        {
            Voice[] voices = new Voice[count];

            for (int i = 0; i < count; i++)
            {
                GameObject child = new GameObject(
                    category + " Player " + (i + 1)
                );

                child.transform.SetParent(
                    transform,
                    false
                );

                AudioSource source =
                    child.AddComponent<AudioSource>();

                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.loop = false;

                voices[i] = new Voice
                {
                    source = source
                };
            }

            pools[category] = voices;
            featureNextBlock[category] = 0;
        }

        private void Start()
        {
            if (playOnStart &&
                grooves != null &&
                grooves.Length > 0 &&
                grooves[0] != null &&
                grooves[0].clip != null)
            {
                QueueGroove(0);
            }
        }

        private double At(int block)
        {
            return origin + block * BlockTime;
        }

        private int NextBlock(
            double secondsBeforeAnchor
        )
        {
            double deadline =
                AudioSettings.dspTime +
                ScheduleLead +
                secondsBeforeAnchor;

            return Math.Max(
                0,
                (int)Math.Ceiling(
                    (deadline - origin) /
                    BlockTime - 1e-9
                )
            );
        }

        private static bool Valid(Job job)
        {
            return job != null &&
                   !job.cancelled &&
                   !job.failed;
        }

        private static bool Intersects(
            double a,
            double b,
            double c,
            double d
        )
        {
            return a < d && c < b;
        }

        private static double Duration(AudioClip clip)
        {
            return (double)clip.samples /
                   clip.frequency;
        }

        private int Intro(FeatureTrack track)
        {
            return track.hasLeadIn
                ? Mathf.Max(0, track.leadInBars)
                : 0;
        }

        private float CategoryGain(
            MusicCategory category
        )
        {
            switch (category)
            {
                case MusicCategory.Drums:
                    return drumVolume;

                case MusicCategory.Groove:
                    return grooveVolume;

                case MusicCategory.Riff:
                    return riffVolume;

                case MusicCategory.Lead:
                    return leadVolume;

                default:
                    return soloVolume;
            }
        }

        private bool CheckLoop(
            LoopTrack track,
            MusicCategory category
        )
        {
            if (track == null ||
                track.clip == null ||
                track.bars < 1)
            {
                status = "Missing clip or bars";
                return false;
            }

            if ((category == MusicCategory.Groove ||
                 category == MusicCategory.Drums) &&
                Math.Abs(
                    Duration(track.clip) -
                    track.bars * BarTime
                ) > 0.015)
            {
                status =
                    "Audio length / BPM mismatch: " +
                    track.displayName;

                Debug.LogWarning(status, this);
                return false;
            }

            if (category == MusicCategory.Groove &&
                track.bars < 4)
            {
                return false;
            }

            return true;
        }

        private bool CheckFeature(FeatureTrack track)
        {
            if (track == null ||
                track.clip == null)
            {
                status = "Missing riff or lead clip";
                return false;
            }

            if (Duration(track.clip) + 0.015 <
                (4 + Intro(track)) * BarTime)
            {
                status =
                    "Feature shorter than 4 bars plus intro: " +
                    track.displayName;

                Debug.LogWarning(status, this);
                return false;
            }

            return true;
        }

        private Voice Reserve(
            MusicCategory category,
            double start,
            double end
        )
        {
            foreach (Voice voice in pools[category])
            {
                bool free = true;

                foreach (Job job in jobs)
                {
                    if (Valid(job) &&
                        job.voice == voice &&
                        Intersects(
                            start - Safety,
                            end,
                            job.start,
                            job.end + Safety
                        ))
                    {
                        free = false;
                        break;
                    }
                }

                if (free)
                    return voice;
            }

            return null;
        }

        private bool HasCategory(
            MusicCategory category,
            double time
        )
        {
            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    job.category == category &&
                    job.start <= time &&
                    time < job.end)
                {
                    return true;
                }
            }

            return false;
        }

        private bool AnyFeatureBetween(
            double begin,
            double end
        )
        {
            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    (job.category == MusicCategory.Lead ||
                     job.category == MusicCategory.Riff) &&
                    Intersects(
                        begin,
                        end,
                        job.start,
                        job.anchor + BlockTime
                    ))
                {
                    return true;
                }
            }

            return false;
        }

        private bool SoloConflict(
            double start,
            double end
        )
        {
            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    job.category ==
                        MusicCategory.SoloSong &&
                    Intersects(
                        start,
                        end,
                        job.start,
                        job.end
                    ))
                {
                    return true;
                }
            }

            return false;
        }

        private CharacterTracks Character(int id)
        {
            switch (id)
            {
                case 1:
                    return bruce;

                case 2:
                    return eagle;

                case 3:
                    return cougar;

                case 4:
                    return serpent;

                default:
                    return null;
            }
        }

        private string CharacterName(int id)
        {
            switch (id)
            {
                case 1:
                    return "Bruce";

                case 2:
                    return "Eagle";

                case 3:
                    return "Cougar";

                case 4:
                    return "Serpent";

                default:
                    return "Unknown";
            }
        }

        private FeatureTrack RandomFeature(
            FeatureTrack[] tracks
        )
        {
            List<FeatureTrack> possible =
                new List<FeatureTrack>();

            if (tracks != null)
            {
                foreach (FeatureTrack track in tracks)
                {
                    if (track != null &&
                        track.clip != null &&
                        CheckFeature(track))
                    {
                        possible.Add(track);
                    }
                }
            }

            if (possible.Count == 0)
                return null;

            return possible[
                UnityEngine.Random.Range(
                    0,
                    possible.Count
                )
            ];
        }

        private int PanSide(
            int character,
            int block
        )
        {
            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    job.character == character &&
                    job.block == block &&
                    job.side != 0)
                {
                    return job.side;
                }
            }

            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    job.character != 0 &&
                    job.character != character &&
                    job.block == block &&
                    job.side != 0)
                {
                    sides[character] = -job.side;
                    return -job.side;
                }
            }

            int prior;

            if (sides.TryGetValue(character, out prior))
                return prior;

            int chosen = alternateSide;

            alternateSide = -chosen;
            sides[character] = chosen;

            return chosen;
        }

        private bool OccupiedFeatureBlock(
            MusicCategory category,
            int character,
            int block
        )
        {
            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    (job.category == MusicCategory.Riff ||
                     job.category == MusicCategory.Lead) &&
                    job.block == block &&
                    (job.category == category ||
                     job.character == character))
                {
                    return true;
                }
            }

            return false;
        }

        private bool QueueFeature(
            MusicCategory category,
            int character,
            FeatureTrack track
        )
        {
            if (!initialized ||
                !CheckFeature(track))
            {
                return false;
            }

            int introBars = Intro(track);

            int first = Math.Max(
                NextBlock(
                    (introBars +
                     (category == MusicCategory.Lead
                         ? 4
                         : 0)) * BarTime
                ),
                featureNextBlock[category]
            );

            for (int block = first;
                 block < first + SearchLimit;
                 block++)
            {
                if (OccupiedFeatureBlock(
                    category,
                    character,
                    block
                ))
                {
                    continue;
                }

                double anchor = At(block);

                double start =
                    anchor - introBars * BarTime;

                double end =
                    start + Duration(track.clip);

                if (SoloConflict(start, end))
                    continue;

                Voice voice = Reserve(
                    category,
                    start,
                    end
                );

                if (voice == null)
                    continue;

                Job job = new Job
                {
                    category = category,
                    voice = voice,
                    clip = track.clip,
                    name = track.displayName,
                    character = character,

                    side = PanSide(
                        character,
                        block
                    ),

                    block = block,
                    start = start,
                    anchor = anchor,
                    end = end,

                    gain =
                        CategoryGain(category) *
                        track.volume
                };

                jobs.Add(job);

                featureNextBlock[category] = block + 1;

                if (category == MusicCategory.Lead)
                {
                    EndGrooveAt(
                        start + 4 * BarTime,
                        2
                    );
                }
                else
                {
                    bool precedingRiff = false;

                    foreach (Job other in jobs)
                    {
                        if (other != job &&
                            Valid(other) &&
                            other.category ==
                                MusicCategory.Riff &&
                            other.block == block - 1)
                        {
                            precedingRiff = true;
                        }
                    }

                    if (precedingRiff)
                    {
                        EndGrooveAt(anchor, 2);
                    }
                }

                CancelAutomaticIfConflicting(
                    start,
                    anchor + BlockTime
                );

                status =
                    "Queued " +
                    CharacterName(character) +
                    " " + category +
                    " at bar " +
                    (4 * block + 1);

                return true;
            }

            status =
                "No free musical slot for " +
                track.displayName;

            Debug.LogWarning(status, this);
            return false;
        }

        public void QueueCharacterRiff(int characterId)
        {
            CharacterTracks character =
                Character(characterId);

            if (character == null)
                return;

            FeatureTrack track =
                RandomFeature(character.riffs);

            if (track != null)
            {
                QueueFeature(
                    MusicCategory.Riff,
                    characterId,
                    track
                );
            }
            else
            {
                status =
                    "No valid riffs for " +
                    CharacterName(characterId);
            }
        }

        public void QueueCharacterLead(int characterId)
        {
            CharacterTracks character =
                Character(characterId);

            if (character == null)
                return;

            FeatureTrack track =
                RandomFeature(character.leads);

            if (track != null)
            {
                QueueFeature(
                    MusicCategory.Lead,
                    characterId,
                    track
                );
            }
            else
            {
                status =
                    "No valid leads for " +
                    CharacterName(characterId);
            }
        }

        public void QueueSpecificRiff(
            int characterId,
            int index
        )
        {
            CharacterTracks c = Character(characterId);

            if (c != null &&
                c.riffs != null &&
                index >= 0 &&
                index < c.riffs.Length)
            {
                QueueFeature(
                    MusicCategory.Riff,
                    characterId,
                    c.riffs[index]
                );
            }
        }

        public void QueueSpecificLead(
            int characterId,
            int index
        )
        {
            CharacterTracks c = Character(characterId);

            if (c != null &&
                c.leads != null &&
                index >= 0 &&
                index < c.leads.Length)
            {
                QueueFeature(
                    MusicCategory.Lead,
                    characterId,
                    c.leads[index]
                );
            }
        }

        private bool AddDrumSegment(
            int index,
            int block
        )
        {
            if (drums == null ||
                index < 0 ||
                index >= drums.Length ||
                !CheckLoop(
                    drums[index],
                    MusicCategory.Drums
                ))
            {
                return false;
            }

            LoopTrack track = drums[index];

            double start = At(block);

            double end =
                start + DrumSectionBars * BarTime;

            if (SoloConflict(start, end))
                return false;

            Voice voice = Reserve(
                MusicCategory.Drums,
                start,
                end
            );

            if (voice == null)
                return false;

            jobs.Add(new Job
            {
                category = MusicCategory.Drums,
                voice = voice,
                clip = track.clip,
                name = track.displayName,
                block = block,
                start = start,
                anchor = start,
                end = end,
                loop = true,

                gain =
                    drumVolume * track.volume
            });

            drumIndex = index;

            nextDrumBlock =
                block +
                DrumSectionBars / BarsPerBlock;

            return true;
        }

        public void QueueDrums(int index)
        {
            if (!initialized ||
                drums == null ||
                index < 0 ||
                index >= drums.Length ||
                !CheckLoop(
                    drums[index],
                    MusicCategory.Drums
                ))
            {
                return;
            }

            if (!drumsEnabled)
            {
                int block = NextBlock(0);

                if (AddDrumSegment(index, block))
                {
                    drumsEnabled = true;

                    status =
                        "Queued drums at bar " +
                        (4 * block + 1);
                }
            }
            else
            {
                forcedDrumIndex = index;

                status =
                    "Drum change queued for next " +
                    "16-bar boundary";
            }
        }

        private void MaintainDrums(double now)
        {
            if (!drumsEnabled ||
                nextDrumBlock < 0)
            {
                return;
            }

            double start = At(nextDrumBlock);

            if (start > now + BlockTime)
                return;

            int next = forcedDrumIndex >= 0
                ? forcedDrumIndex
                : drumIndex;

            if (forcedDrumIndex < 0 &&
                alternateDrumsEvery16Bars &&
                drums != null &&
                drums.Length > 1)
            {
                for (int step = 1;
                     step <= drums.Length;
                     step++)
                {
                    int candidate =
                        (drumIndex + step) %
                        drums.Length;

                    if (drums[candidate] != null &&
                        drums[candidate].clip != null &&
                        CheckLoop(
                            drums[candidate],
                            MusicCategory.Drums
                        ))
                    {
                        next = candidate;
                        break;
                    }
                }
            }

            if (AddDrumSegment(next, nextDrumBlock))
            {
                forcedDrumIndex = -1;
            }
        }

        private LoopTrack RandomGroove()
        {
            List<LoopTrack> available =
                new List<LoopTrack>();

            if (grooves != null)
            {
                foreach (LoopTrack track in grooves)
                {
                    if (track != null &&
                        track.clip != null &&
                        CheckLoop(
                            track,
                            MusicCategory.Groove
                        ))
                    {
                        available.Add(track);
                    }
                }
            }

            if (available.Count == 0)
                return null;

            return available[
                UnityEngine.Random.Range(
                    0,
                    available.Count
                )
            ];
        }

        private bool QueueGrooveAt(
            LoopTrack track,
            int fullBlock,
            int fadeBars,
            bool automatic
        )
        {
            if (!CheckLoop(
                track,
                MusicCategory.Groove
            ))
            {
                return false;
            }

            double anchor = At(fullBlock);

            double start =
                anchor - fadeBars * BarTime;

            if (start <
                AudioSettings.dspTime + ScheduleLead)
            {
                return false;
            }

            if (SoloConflict(
                start,
                anchor + Duration(track.clip)
            ))
            {
                return false;
            }

            if (AnyLeadBetween(
                start,
                anchor + Duration(track.clip)
            ))
            {
                return false;
            }

            Voice voice = Reserve(
                MusicCategory.Groove,
                start,
                double.PositiveInfinity
            );

            if (voice == null)
                return false;

            Job job = new Job
            {
                category = MusicCategory.Groove,
                voice = voice,
                clip = track.clip,
                name = track.displayName,
                block = fullBlock,
                start = start,
                anchor = anchor,
                end = double.PositiveInfinity,
                loop = true,

                gain =
                    grooveVolume * track.volume,

                fadeInStart = start,
                fadeInEnd = anchor,

                prerollBars = fadeBars,
                autoGroove = automatic
            };

            Job previous = CurrentGrooveJob();

            jobs.Add(job);

            if (previous != null &&
                previous != job &&
                previous.end > anchor)
            {
                EndJob(previous, anchor, 2);
            }

            currentGroove = job;

            if (automatic)
            {
                pendingAutoGroove = job;
            }
            else
            {
                pendingAutoGroove = null;
            }

            status =
                (automatic ? "AUTO " : "Queued ") +
                "groove " +
                track.displayName +
                " full at bar " +
                (fullBlock * 4 + 1);

            return true;
        }

        private Job CurrentGrooveJob()
        {
            double now = AudioSettings.dspTime;

            if (Valid(currentGroove) &&
                currentGroove.end > now)
            {
                return currentGroove;
            }

            Job latest = null;

            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    job.category ==
                        MusicCategory.Groove &&
                    job.end > now &&
                    (latest == null ||
                     job.start > latest.start))
                {
                    latest = job;
                }
            }

            return latest;
        }

        private bool AnyLeadBetween(
            double start,
            double end
        )
        {
            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    job.category ==
                        MusicCategory.Lead &&
                    Intersects(
                        start,
                        end,
                        job.start,
                        job.anchor + BlockTime
                    ))
                {
                    return true;
                }
            }

            return false;
        }

        public void QueueGroove(int index)
        {
            if (grooves == null ||
                index < 0 ||
                index >= grooves.Length ||
                !CheckLoop(
                    grooves[index],
                    MusicCategory.Groove
                ))
            {
                return;
            }

            CancelPendingAutomatic();

            int first = NextBlock(2 * BarTime);

            for (int block = first;
                 block < first + SearchLimit;
                 block++)
            {
                if (QueueGrooveAt(
                    grooves[index],
                    block,
                    2,
                    false
                ))
                {
                    return;
                }
            }

            status =
                "No legal groove start available";
        }

        private void EndJob(
            Job job,
            double end,
            int fadeBars
        )
        {
            if (!Valid(job) ||
                end >= job.end)
            {
                return;
            }

            if (job.start >= end)
            {
                Cancel(job);
                return;
            }

            job.end = end;

            if (fadeBars > 0)
            {
                job.fadeOutStart =
                    end - fadeBars * BarTime;

                job.fadeOutEnd = end;
            }

            if (job.committed &&
                job.voice.current == job &&
                end > AudioSettings.dspTime + Safety)
            {
                job.voice.source.SetScheduledEndTime(end);
            }
        }

        private void EndGrooveAt(
            double end,
            int fadeBars
        )
        {
            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    job.category ==
                        MusicCategory.Groove &&
                    job.start < end &&
                    job.end > end)
                {
                    EndJob(job, end, fadeBars);
                }
            }

            if (currentGroove != null &&
                currentGroove.end <= end)
            {
                currentGroove = null;
            }
        }

        private void Cancel(Job job)
        {
            if (!Valid(job))
                return;

            if (job.committed &&
                job.voice.current == job)
            {
                job.voice.source.Stop();
                job.voice.current = null;
            }

            job.cancelled = true;

            if (currentGroove == job)
                currentGroove = null;

            if (pendingAutoGroove == job)
                pendingAutoGroove = null;
        }

        private void CancelPendingAutomatic()
        {
            if (Valid(pendingAutoGroove) &&
                pendingAutoGroove.start >
                AudioSettings.dspTime + ScheduleLead)
            {
                Cancel(pendingAutoGroove);
            }
        }

        private void CancelAutomaticIfConflicting(
            double start,
            double end
        )
        {
            if (Valid(pendingAutoGroove) &&
                pendingAutoGroove.start >
                AudioSettings.dspTime + ScheduleLead &&
                Intersects(
                    start,
                    end,
                    pendingAutoGroove.start,
                    pendingAutoGroove.anchor
                ))
            {
                Cancel(pendingAutoGroove);
            }
        }

        // Automatic groove uses the SAME two-bar
        // fade and SAME next-boundary calculation
        // as pressing the manual QUEUE UP button.
        private void AutomaticGrooveCheck(double now)
        {
            if (!automaticGrooveReturn ||
                !drumsEnabled ||
                !HasCategory(
                    MusicCategory.Drums,
                    now
                ))
            {
                return;
            }

            // A previously scheduled automatic groove
            // no longer blocks recovery after it ends.
            if (pendingAutoGroove != null &&
                (!Valid(pendingAutoGroove) ||
                 pendingAutoGroove.end <= now))
            {
                pendingAutoGroove = null;
            }

            if (Valid(pendingAutoGroove) ||
                CurrentGrooveJob() != null ||
                now < manualGrooveExitUntil)
            {
                return;
            }

            // Use the exact manual queue calculation.
            int first = NextBlock(2 * BarTime);

            for (int block = first;
                 block < first + SearchLimit;
                 block++)
            {
                double anchor = At(block);
                double fadeStart =
                    anchor - 2 * BarTime;

                // Do not automatically bring back a
                // groove during queued riffs or leads.
                if (AnyFeatureBetween(
                    now,
                    anchor + BlockTime
                ))
                {
                    return;
                }

                if (SoloConflict(
                    fadeStart,
                    anchor + BlockTime
                ))
                {
                    continue;
                }

                LoopTrack selected = RandomGroove();

                if (selected == null)
                {
                    return;
                }

                if (QueueGrooveAt(
                    selected,
                    block,
                    2,
                    true
                ))
                {
                    status =
                        "AUTO groove " +
                        selected.displayName +
                        " | manual queue timing | " +
                        "full at bar " +
                        (block * 4 + 1);

                    return;
                }
            }
        }

        public void QueueDown(MusicCategory category)
        {
            if (category == MusicCategory.Riff ||
                category == MusicCategory.Lead)
            {
                ClearPending(category);
                return;
            }

            if (category == MusicCategory.Groove)
            {
                CancelPendingAutomatic();

                Job groove = CurrentGrooveJob();

                if (groove == null)
                {
                    status = "No groove active";
                    return;
                }

                int fadeStartBlock = NextBlock(0);

                double fadeStart =
                    At(fadeStartBlock);

                double fadeEnd =
                    fadeStart + 8 * BarTime;

                EndGrooveAt(fadeEnd, 8);

                manualGrooveExitUntil = fadeEnd;

                status =
                    "Groove queued down: " +
                    "8-bar fade from bar " +
                    (fadeStartBlock * 4 + 1);

                return;
            }

            if (category == MusicCategory.Drums)
            {
                drumsEnabled = false;
                forcedDrumIndex = -1;
                nextDrumBlock = -1;

                double end = At(NextBlock(0));

                foreach (Job job in jobs)
                {
                    if (Valid(job) &&
                        job.category ==
                            MusicCategory.Drums &&
                        job.end > end)
                    {
                        EndJob(job, end, 0);
                    }
                }

                status =
                    "Drums ending on next " +
                    "four-bar boundary";

                return;
            }

            double boundary =
                At(NextBlock(0));

            foreach (Job job in jobs)
            {
                if (Valid(job) &&
                    job.category == category &&
                    job.end > boundary)
                {
                    EndJob(job, boundary, 0);
                }
            }
        }

        public void QueueSoloSong(int index)
        {
            if (soloSongs == null ||
                index < 0 ||
                index >= soloSongs.Length ||
                !CheckLoop(
                    soloSongs[index],
                    MusicCategory.SoloSong
                ))
            {
                return;
            }

            LoopTrack track = soloSongs[index];

            double duration =
                Duration(track.clip);

            int first = NextBlock(0);

            for (int block = first;
                 block < first + SearchLimit;
                 block++)
            {
                double start = At(block);

                bool occupied = false;

                foreach (Job job in jobs)
                {
                    if (Valid(job) &&
                        Intersects(
                            start,
                            start + duration,
                            job.start,
                            job.end
                        ))
                    {
                        occupied = true;
                        break;
                    }
                }

                if (occupied)
                    continue;

                Voice voice = Reserve(
                    MusicCategory.SoloSong,
                    start,
                    start + duration
                );

                if (voice == null)
                    continue;

                jobs.Add(new Job
                {
                    category = MusicCategory.SoloSong,
                    voice = voice,
                    clip = track.clip,
                    name = track.displayName,
                    block = block,
                    start = start,
                    anchor = start,
                    end = start + duration,

                    gain =
                        soloVolume * track.volume
                });

                status =
                    "Queued solo song " +
                    track.displayName;

                return;
            }

            status =
                "Solo song unavailable while " +
                "other layers are looping";
        }

        public void QueueSoloLead(int index)
        {
            if (soloLeads == null ||
                index < 0 ||
                index >= soloLeads.Length ||
                !CheckLoop(
                    soloLeads[index],
                    MusicCategory.SoloLead
                ))
            {
                return;
            }

            LoopTrack track = soloLeads[index];

            double duration =
                Duration(track.clip);

            int first = NextBlock(0);

            for (int block = first;
                 block < first + SearchLimit;
                 block++)
            {
                double start = At(block);

                if (SoloConflict(
                    start,
                    start + duration
                ))
                {
                    continue;
                }

                Voice voice = Reserve(
                    MusicCategory.SoloLead,
                    start,
                    start + duration
                );

                if (voice == null)
                    continue;

                jobs.Add(new Job
                {
                    category = MusicCategory.SoloLead,
                    voice = voice,
                    clip = track.clip,
                    name = track.displayName,
                    block = block,
                    start = start,
                    anchor = start,
                    end = start + duration,

                    gain =
                        soloVolume * track.volume
                });

                status =
                    "Queued solo lead " +
                    track.displayName;

                return;
            }
        }

        public void QueueUp(
            MusicCategory category,
            int index
        )
        {
            switch (category)
            {
                case MusicCategory.Drums:
                    QueueDrums(index);
                    break;

                case MusicCategory.Groove:
                    QueueGroove(index);
                    break;

                case MusicCategory.SoloSong:
                    QueueSoloSong(index);
                    break;

                case MusicCategory.SoloLead:
                    QueueSoloLead(index);
                    break;

                default:
                    status =
                        "Use character riff or lead queue";
                    break;
            }
        }

        public void RequestMusic(
            MusicCategory category,
            int characterId = 0,
            int index = -1,
            bool enabled = true
        )
        {
            if (!enabled)
            {
                QueueDown(category);
                return;
            }

            if (category == MusicCategory.Riff)
            {
                if (index < 0)
                {
                    QueueCharacterRiff(characterId);
                }
                else
                {
                    QueueSpecificRiff(
                        characterId,
                        index
                    );
                }

                return;
            }

            if (category == MusicCategory.Lead)
            {
                if (index < 0)
                {
                    QueueCharacterLead(characterId);
                }
                else
                {
                    QueueSpecificLead(
                        characterId,
                        index
                    );
                }

                return;
            }

            if (index < 0)
            {
                status =
                    "Select a track index for " +
                    category;

                return;
            }

            QueueUp(category, index);
        }

        private float Envelope(
            Job job,
            double now
        )
        {
            if (job.category != MusicCategory.Groove)
                return 1f;

            if (!double.IsNaN(job.fadeOutStart) &&
                now >= job.fadeOutStart)
            {
                double length =
                    job.fadeOutEnd -
                    job.fadeOutStart;

                float fraction = length <= 0
                    ? 1f
                    : Mathf.Clamp01(
                        (float)(
                            (now - job.fadeOutStart) /
                            length
                        )
                    );

                if (fraction >= 1f)
                    return 0f;

                return Mathf.Pow(
                    10f,
                    Mathf.Lerp(
                        0f,
                        fadeSilenceDb,
                        fraction
                    ) / 20f
                );
            }

            if (!double.IsNaN(job.fadeInStart) &&
                now < job.fadeInEnd)
            {
                double length =
                    job.fadeInEnd -
                    job.fadeInStart;

                float fraction = length <= 0
                    ? 1f
                    : Mathf.Clamp01(
                        (float)(
                            (now - job.fadeInStart) /
                            length
                        )
                    );

                if (fraction <= 0f)
                    return 0f;

                return Mathf.Pow(
                    10f,
                    Mathf.Lerp(
                        fadeSilenceDb,
                        0f,
                        fraction
                    ) / 20f
                );
            }

            return 1f;
        }

        private void Commit(Job job, double now)
        {
            if (!Valid(job) ||
                job.committed ||
                job.start > now + ScheduleLead)
            {
                return;
            }

            if (job.start <= now + Safety)
            {
                job.failed = true;

                status =
                    "MISSED DSP DEADLINE: " +
                    job.name;

                Debug.LogError(status, this);
                return;
            }

            if (job.voice.current != null &&
                job.voice.current != job)
            {
                Job previous =
                    job.voice.current;

                if (Valid(previous) &&
                    previous.end + 0.005 > now)
                {
                    return;
                }

                job.voice.source.Stop();
                job.voice.current = null;
            }

            AudioSource source =
                job.voice.source;

            source.Stop();

            source.clip = job.clip;
            source.loop = job.loop;

            source.panStereo =
                job.side * sidePan;

            source.volume = job.gain;
            source.timeSamples = 0;

            if (job.category == MusicCategory.Groove)
            {
                int preroll =
                    (int)Math.Round(
                        job.prerollBars *
                        BarTime *
                        job.clip.frequency
                    );

                source.timeSamples = Mathf.Clamp(
                    job.clip.samples - preroll,
                    0,
                    job.clip.samples - 1
                );

                source.volume = 0f;
            }

            source.PlayScheduled(job.start);

            if (!double.IsInfinity(job.end))
            {
                source.SetScheduledEndTime(job.end);
            }

            job.voice.current = job;
            job.committed = true;
        }

        private void Update()
        {
            if (!initialized)
                return;

            double now = AudioSettings.dspTime;

            MaintainDrums(now);
            AutomaticGrooveCheck(now);

            foreach (Job job in jobs)
            {
                Commit(job, now);
            }

            foreach (Job job in jobs)
            {
                if (!Valid(job) ||
                    !job.committed ||
                    job.voice.current != job ||
                    now < job.start ||
                    now >= job.end)
                {
                    continue;
                }

                if (job.category ==
                    MusicCategory.Groove)
                {
                    job.voice.source.volume =
                        job.gain *
                        Envelope(job, now);
                }

                if (job.category ==
                    MusicCategory.SoloLead)
                {
                    double period =
                        Math.Max(
                            1,
                            soloLeadPanCycleBars
                        ) * BarTime;

                    job.voice.source.panStereo =
                        sidePan *
                        (float)Math.Sin(
                            2.0 * Math.PI *
                            (now - job.start) /
                            period
                        );
                }
            }

            for (int i = jobs.Count - 1;
                 i >= 0;
                 i--)
            {
                Job job = jobs[i];

                if (!job.cancelled &&
                    !job.failed &&
                    now < job.end + Safety)
                {
                    continue;
                }

                if (job.voice.current == job)
                {
                    job.voice.source.Stop();
                    job.voice.current = null;
                }

                if (pendingAutoGroove == job)
                    pendingAutoGroove = null;

                if (currentGroove == job)
                    currentGroove = null;

                jobs.RemoveAt(i);
            }
        }

        public void ClearPending(
            MusicCategory category
        )
        {
            double now = AudioSettings.dspTime;

            for (int i = jobs.Count - 1;
                 i >= 0;
                 i--)
            {
                if (Valid(jobs[i]) &&
                    jobs[i].category == category &&
                    jobs[i].start >
                        now + ScheduleLead)
                {
                    Cancel(jobs[i]);
                }
            }

            status =
                "Cleared future " +
                category;
        }

        public void ClearCharacterPending(
            int characterId
        )
        {
            double now = AudioSettings.dspTime;

            for (int i = jobs.Count - 1;
                 i >= 0;
                 i--)
            {
                if (Valid(jobs[i]) &&
                    jobs[i].character ==
                        characterId &&
                    jobs[i].start >
                        now + ScheduleLead)
                {
                    Cancel(jobs[i]);
                }
            }

            status =
                "Cleared future " +
                CharacterName(characterId);
        }

        public void StopAllMusic()
        {
            if (!initialized)
                return;

            foreach (Voice[] pool in pools.Values)
            {
                foreach (Voice voice in pool)
                {
                    voice.source.Stop();
                    voice.source.clip = null;
                    voice.current = null;
                }
            }

            jobs.Clear();
            sides.Clear();

            alternateSide = -1;
            drumIndex = -1;
            nextDrumBlock = -1;
            forcedDrumIndex = -1;

            drumsEnabled = false;

            pendingAutoGroove = null;
            currentGroove = null;

            manualGrooveExitUntil = 0;

            featureNextBlock[
                MusicCategory.Riff
            ] = 0;

            featureNextBlock[
                MusicCategory.Lead
            ] = 0;

            status =
                "Stopped; global clock continues";
        }

        public void PlayGrooveNow(int index)
        {
            StopAllMusic();
            QueueGroove(index);
        }

        public void PlayMasterNow(int index)
        {
            StopAllMusic();
            QueueSoloSong(index);
        }

        public void QueueMaster(int index)
        {
            QueueSoloSong(index);
        }

        public void EndMasterToGroove(int index)
        {
            QueueDown(MusicCategory.SoloSong);
            QueueGroove(index);
        }

        public void QueueRiff(int id)
        {
            QueueCharacterRiff(id);
        }

        public void QueueLead(int id)
        {
            QueueCharacterLead(id);
        }

        public void SetMelody(
            int id,
            bool enabled
        )
        {
            if (enabled)
            {
                QueueCharacterLead(id);
            }
            else
            {
                ClearCharacterPending(id);
            }
        }

        private string Names(bool upcoming)
        {
            double now = AudioSettings.dspTime;

            List<string> names =
                new List<string>();

            foreach (Job job in jobs)
            {
                if (!Valid(job))
                    continue;

                bool show = upcoming
                    ? job.start > now
                    : job.start <= now &&
                      now < job.end;

                if (show)
                {
                    names.Add(
                        job.name +
                        (upcoming
                            ? " @bar " +
                              (job.block * 4 + 1)
                            : "")
                    );
                }
            }

            return names.Count == 0
                ? "None"
                : string.Join(
                    " | ",
                    names.ToArray()
                );
        }

        private int CountTracks(
            FeatureTrack[] tracks
        )
        {
            int count = 0;

            if (tracks != null)
            {
                foreach (FeatureTrack track in tracks)
                {
                    if (track != null &&
                        track.clip != null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private void DrawFeatureList(
            int id,
            FeatureTrack[] tracks,
            bool lead
        )
        {
            if (tracks == null)
                return;

            for (int i = 0;
                 i < tracks.Length;
                 i++)
            {
                FeatureTrack track = tracks[i];

                GUILayout.BeginHorizontal();

                GUILayout.Label(
                    track == null
                        ? "Empty"
                        : track.displayName
                );

                GUI.enabled =
                    track != null &&
                    track.clip != null;

                int index = i;

                if (GUILayout.Button(
                    lead
                        ? "QUEUE LEAD"
                        : "QUEUE RIFF",
                    GUILayout.Width(115)
                ))
                {
                    if (lead)
                    {
                        QueueSpecificLead(
                            id,
                            index
                        );
                    }
                    else
                    {
                        QueueSpecificRiff(
                            id,
                            index
                        );
                    }
                }

                GUI.enabled = true;

                GUILayout.EndHorizontal();
            }
        }

        private void DrawCharacter(int id)
        {
            CharacterTracks c = Character(id);

            if (c == null)
                return;

            GUILayout.Space(8);

            GUILayout.Label(
                "=== " +
                CharacterName(id).ToUpperInvariant() +
                " ==="
            );

            GUILayout.Label(
                "Riffs: " +
                CountTracks(c.riffs) +
                " | Leads: " +
                CountTracks(c.leads)
            );

            GUILayout.BeginHorizontal();

            GUI.enabled =
                CountTracks(c.riffs) > 0;

            if (GUILayout.Button("RANDOM RIFF"))
            {
                QueueCharacterRiff(id);
            }

            GUI.enabled =
                CountTracks(c.leads) > 0;

            if (GUILayout.Button("RANDOM LEAD"))
            {
                QueueCharacterLead(id);
            }

            GUI.enabled = true;

            GUILayout.EndHorizontal();

            DrawFeatureList(
                id,
                c.riffs,
                false
            );

            DrawFeatureList(
                id,
                c.leads,
                true
            );

            if (GUILayout.Button(
                "CLEAR CHARACTER WAITING"
            ))
            {
                ClearCharacterPending(id);
            }
        }

        private void DrawLoops(
            MusicCategory category,
            LoopTrack[] tracks
        )
        {
            GUILayout.Space(8);

            GUILayout.Label(
                "=== " +
                category.ToString().ToUpperInvariant() +
                " ==="
            );

            if (tracks != null)
            {
                for (int i = 0;
                     i < tracks.Length;
                     i++)
                {
                    LoopTrack track = tracks[i];

                    GUILayout.BeginHorizontal();

                    GUILayout.Label(
                        track == null
                            ? "Empty"
                            : track.displayName
                    );

                    GUI.enabled =
                        track != null &&
                        track.clip != null;

                    int index = i;

                    if (GUILayout.Button(
                        "QUEUE UP",
                        GUILayout.Width(115)
                    ))
                    {
                        QueueUp(
                            category,
                            index
                        );
                    }

                    GUI.enabled = true;

                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("QUEUE DOWN"))
            {
                QueueDown(category);
            }

            if (GUILayout.Button("CLEAR WAITING"))
            {
                ClearPending(category);
            }

            GUILayout.EndHorizontal();
        }

        private void OnGUI()
        {
            if (!showTestControls ||
                !initialized)
            {
                return;
            }

            float width = Mathf.Max(
                200,
                Mathf.Min(
                    530,
                    Screen.width - 24
                )
            );

            float height = Mathf.Max(
                200,
                Mathf.Min(
                    800,
                    Screen.height - 24
                )
            );

            GUILayout.BeginArea(
                new Rect(
                    12,
                    12,
                    width,
                    height
                ),
                GUI.skin.box
            );

            GUILayout.Label(
                "BEASTBRAWL MUSIC QUEUE MANAGER"
            );

            GUILayout.Label(
                "Global bar " +
                BarInTrack +
                " | Beat " +
                BeatInBar +
                " | BPM " +
                Bpm
            );

            GUILayout.Label(
                "Playing: " +
                CurrentTrackName
            );

            GUILayout.Label(
                "Queued: " +
                QueuedTrackName
            );

            GUILayout.Label(
                "Status: " +
                status
            );

            scroll = GUILayout.BeginScrollView(
                scroll,
                GUILayout.ExpandHeight(true)
            );

            DrawLoops(
                MusicCategory.Drums,
                drums
            );

            DrawLoops(
                MusicCategory.Groove,
                grooves
            );

            DrawCharacter(1);
            DrawCharacter(2);
            DrawCharacter(3);
            DrawCharacter(4);

            DrawLoops(
                MusicCategory.SoloSong,
                soloSongs
            );

            DrawLoops(
                MusicCategory.SoloLead,
                soloLeads
            );

            GUILayout.EndScrollView();

            if (GUILayout.Button(
                "STOP ALL IMMEDIATELY",
                GUILayout.Height(34)
            ))
            {
                StopAllMusic();
            }

            GUILayout.EndArea();
        }
    }
}
