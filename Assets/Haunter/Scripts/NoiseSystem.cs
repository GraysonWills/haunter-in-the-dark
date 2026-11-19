using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Haunter
{
    public enum NoiseKind { Footstep, Cane, Impact, Creature, Intro, Collapse }

    public struct NoiseEvent
    {
        public Vector3 Position;
        public float Loudness;
        public NoiseKind Kind;
        public GameObject Source;
        public int EventId;
        public Vector3 OriginalPosition;
        public float Confidence;
        public bool IsReflection;
    }

    /// <summary>One source of truth for hearing, echo waves and positional sound.</summary>
    public sealed class NoiseSystem : MonoBehaviour
    {
        public static NoiseSystem Instance { get; private set; }
        public static event Action<NoiseEvent> Heard;
        public static event Action<NoiseEvent> Emitted;
        public const int MaxPulses = 32;
        [Range(4f, 24f)] public float WaveSpeed = 12f;
        [Range(.2f, 3f)] public float Afterglow = 1.15f;
        public int ActivePulseCount => pulses.Count;
        public int EmittedCount { get; private set; }
        public float AcousticLoad { get; private set; }
        public bool PlaybackAvailable => !soundUnavailable;

        struct Pulse { public Vector3 Position; public float Start, Radius, Strength; }
        struct Reflection { public float At; public NoiseEvent Noise; }
        static readonly Vector3[] ReflectionDirections = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        const int ReflectionLimit = 12;
        readonly List<Reflection> reflections = new List<Reflection>(ReflectionLimit);
        Bounds[] quietZones = Array.Empty<Bounds>();
        int level = 1;
        static int nextEventId;
        readonly List<Pulse> pulses = new List<Pulse>(MaxPulses);
        readonly Vector4[] origins = new Vector4[MaxPulses];
        readonly Vector4[] settings = new Vector4[MaxPulses];
        readonly AudioSource[] voices = new AudioSource[20];
        readonly AudioClip[] clips = new AudioClip[Enum.GetValues(typeof(NoiseKind)).Length];
        int nextVoice;
        bool soundUnavailable;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int Haunter_HasWebAudio();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; Heard = null; Emitted = null; nextEventId = 0; }

        void Awake()
        {
            Instance = this;
            UploadPulses();
#if UNITY_WEBGL && !UNITY_EDITOR
            soundUnavailable = Haunter_HasWebAudio() == 0;
            if (soundUnavailable) return;
#endif
            for (int i = 0; i < clips.Length; ++i)
                clips[i] = Resources.Load<AudioClip>("HaunterSounds/" + (NoiseKind)i);
            for (int i = 0; i < voices.Length; ++i)
            {
                var voice = new GameObject("Spatial sound " + i).AddComponent<AudioSource>();
                voice.transform.SetParent(transform);
                voice.spatialBlend = 1f;
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.minDistance = 1.5f;
                voice.maxDistance = 30f;
                voice.dopplerLevel = 0;
                voice.playOnAwake = false;
                voices[i] = voice;
            }
        }

        public static int NewEventId() => ++nextEventId;

        public void ConfigureRoom(int number, Bounds[] zones)
        {
            level = number;
            quietZones = zones ?? Array.Empty<Bounds>();
            AcousticLoad = 0f;
            pulses.Clear();
            reflections.Clear();
            foreach (var voice in voices) if (voice) voice.Stop();
            UploadPulses();
        }

        public bool InQuietZone(Vector3 point)
        {
            foreach (var zone in quietZones) if (zone.Contains(point)) return true;
            return false;
        }

        internal void ShowBallroomEcho(Vector3 position, float loudness, float load)
        {
            if (level != 3) return;
            // This authored lesson uses the ordinary reveal and interference renderer.
            // The staged echoes do not count as player noise or give the listener a target.
            AcousticLoad = Mathf.Max(AcousticLoad, load);
            AddPulse(position, loudness);
            PlaySound(position, loudness * .58f, NoiseKind.Cane);
        }

        public static void Emit(Vector3 position, float loudness, NoiseKind kind, GameObject source = null, int eventId = 0)
        {
            if (!Instance || !IsFinite(position) || float.IsNaN(loudness) || float.IsInfinity(loudness)) return;
            loudness = Mathf.Clamp(loudness, .05f, 2f);
            var noise = new NoiseEvent
            {
                Position = position, OriginalPosition = position, Loudness = loudness, Kind = kind,
                Source = source, EventId = eventId > 0 ? eventId : NewEventId(), Confidence = 1f
            };
            Instance.AddPulse(position, loudness, kind == NoiseKind.Collapse);
            Instance.PlaySound(position, loudness, kind);
            Instance.EmittedCount++;
            Emitted?.Invoke(noise);
            if (!Instance.QueueAcoustics(noise)) Heard?.Invoke(noise);
        }

        static bool IsFinite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
            || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));

        void AddPulse(Vector3 position, float loudness, bool collapse = false)
        {
            if (pulses.Count == MaxPulses) pulses.RemoveAt(0);
            pulses.Add(new Pulse { Position = position, Start = Time.time,
                Radius = collapse ? 42f : Mathf.Clamp(loudness * 16f, 3.5f, 28f),
                Strength = Mathf.Clamp(.6f + loudness * .3f, .5f, 1f) });
            UploadPulses();
        }

        // Reflections are finite, attenuated descendants of a real sound. Neither the visual
        // descendants nor their estimated hearing positions alter the physical world.
        bool QueueAcoustics(NoiseEvent noise)
        {
            if (level != 3 || noise.Kind == NoiseKind.Intro) return false;
            bool quiet = InQuietZone(noise.Position);
            // A patrolling creature still produces visible and audible paw echoes. Its
            // repeating gait contributes less energy than the room can shed on its own.
            float energy = noise.Loudness * (noise.Kind == NoiseKind.Creature ? .04f : .28f) * (quiet ? .25f : 1f);
            AcousticLoad = Mathf.Clamp01(AcousticLoad + energy);
            if (quiet || reflections.Count >= ReflectionLimit) return false;

            float nearest = 14f;
            Vector3 surface = noise.Position;
            Vector3 origin = noise.Position + Vector3.up * .6f;
            foreach (var direction in ReflectionDirections)
            {
                if (!Physics.Raycast(origin, direction, out var hit, nearest, 1, QueryTriggerInteraction.Ignore) ||
                    hit.distance < .75f) continue;
                nearest = hit.distance;
                surface = hit.point + hit.normal * .7f;
                surface.y = noise.Position.y;
            }
            if (surface == noise.Position) return false;

            var reflected = noise;
            reflected.IsReflection = true;
            reflected.Position = Vector3.Lerp(noise.Position, surface, .75f);
            reflected.Confidence = Mathf.Lerp(.75f, .42f, AcousticLoad);
            reflected.Loudness = noise.Loudness * .70f / (1f + nearest * .045f);
            reflections.Add(new Reflection { At = Time.time + Mathf.Clamp(nearest * .068f, .16f, .82f), Noise = reflected });

            // In dense overlap the strongest locator can be the indirect arrival. Deliver just
            // that estimate for this sound family and let investigation ownership do the rest.
            return AcousticLoad > .57f && noise.Kind != NoiseKind.Creature;
        }

        void PlaySound(Vector3 position, float loudness, NoiseKind kind)
        {
            if (soundUnavailable) return;
            var clip = clips[(int)kind];
            var voice = voices[nextVoice++ % voices.Length];
            if (!clip || !voice) return;
            try
            {
                voice.transform.position = position;
                voice.pitch = UnityEngine.Random.Range(.92f, 1.08f);
                voice.clip = clip;
                voice.volume = kind == NoiseKind.Intro ? .15f : Mathf.Clamp(loudness * .6f, .12f, .85f);
                var game = GameSession.Instance;
                if (level == 4 && game && game.Player && game.Player.ViewCamera &&
                    Physics.Linecast(position + Vector3.up * .25f, game.Player.ViewCamera.transform.position,
                        1, QueryTriggerInteraction.Ignore))
                    voice.volume *= .48f;
                voice.Play();
            }
            catch (Exception error)
            {
                soundUnavailable = true;
                Debug.LogWarning("Echo audio playback is unavailable: " + error.Message);
            }
        }
        void Update()
        {
            if (level == 3)
            {
                var game = GameSession.Instance;
                bool quiet = game && game.Player && InQuietZone(game.Player.transform.position);
                AcousticLoad = Mathf.MoveTowards(AcousticLoad, 0, Time.deltaTime * (quiet ? .55f : .19f));
                for (int i = 0; i < reflections.Count;)
                {
                    var reflection = reflections[i];
                    if (Time.time < reflection.At) { i++; continue; }
                    reflections.RemoveAt(i);
                    AddPulse(reflection.Noise.Position, reflection.Noise.Loudness);
                    PlaySound(reflection.Noise.Position, reflection.Noise.Loudness * .43f, reflection.Noise.Kind);
                    Heard?.Invoke(reflection.Noise);
                }
            }
            for (int i = pulses.Count - 1; i >= 0; --i)
                if (Time.time - pulses[i].Start > pulses[i].Radius / WaveSpeed + Afterglow * 4f)
                    pulses.RemoveAt(i);
            UploadPulses();
        }

        void UploadPulses()
        {
            for (int i = 0; i < pulses.Count; ++i)
            {
                var p = pulses[i];
                origins[i] = new Vector4(p.Position.x, p.Position.y, p.Position.z, p.Start);
                settings[i] = new Vector4(p.Radius, WaveSpeed, p.Strength, Afterglow);
            }
            Shader.SetGlobalInt("_EchoCount", pulses.Count);
            Shader.SetGlobalVectorArray("_EchoOrigins", origins);
            Shader.SetGlobalVectorArray("_EchoSettings", settings);
            Shader.SetGlobalFloat("_EchoTime", Time.time);
            bool reduced = GameSession.Instance && GameSession.Instance.ReducedDistortion;
            Shader.SetGlobalFloat("_HaunterInterference", AcousticLoad * (reduced ? .16f : 1f));
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Shader.SetGlobalInt("_EchoCount", 0);
                Shader.SetGlobalFloat("_HaunterInterference", 0);
            }
        }

    }
}
