using System;
using System.Collections.Generic;
using UnityEngine;

namespace Haunter
{
    public enum NoiseKind { Footstep, Cane, Impact, Creature, Intro }

    public struct NoiseEvent
    {
        public Vector3 Position;
        public float Loudness;
        public NoiseKind Kind;
        public GameObject Source;
    }

    /// <summary>One source of truth for hearing, echo waves and positional sound.</summary>
    public sealed class NoiseSystem : MonoBehaviour
    {
        public static NoiseSystem Instance { get; private set; }
        public static event Action<NoiseEvent> Heard;
        public const int MaxPulses = 32;
        [Range(4f, 24f)] public float WaveSpeed = 12f;
        [Range(.2f, 3f)] public float Afterglow = 1.15f;
        public int ActivePulseCount => pulses.Count;
        public int EmittedCount { get; private set; }

        struct Pulse { public Vector3 Position; public float Start, Radius, Strength; }
        readonly List<Pulse> pulses = new List<Pulse>(MaxPulses);
        readonly Vector4[] origins = new Vector4[MaxPulses];
        readonly Vector4[] settings = new Vector4[MaxPulses];
        readonly AudioSource[] voices = new AudioSource[20];
        readonly AudioClip[] clips = new AudioClip[5];
        int nextVoice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; Heard = null; }

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < clips.Length; ++i) clips[i] = MakeSound((NoiseKind)i);
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
            UploadPulses();
        }

        public static void Emit(Vector3 position, float loudness, NoiseKind kind, GameObject source = null)
        {
            if (!Instance || !IsFinite(position) || float.IsNaN(loudness) || float.IsInfinity(loudness)) return;
            loudness = Mathf.Clamp(loudness, .05f, 2f);
            Instance.AddPulse(position, loudness, kind);
            Heard?.Invoke(new NoiseEvent { Position = position, Loudness = loudness, Kind = kind, Source = source });
        }

        static bool IsFinite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
            || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));

        void AddPulse(Vector3 position, float loudness, NoiseKind kind)
        {
            if (pulses.Count == MaxPulses) pulses.RemoveAt(0);
            pulses.Add(new Pulse { Position = position, Start = Time.time,
                Radius = Mathf.Clamp(loudness * 16f, 3.5f, 28f), Strength = Mathf.Clamp(.6f + loudness * .3f, .5f, 1f) });
            EmittedCount++;
            var voice = voices[nextVoice++ % voices.Length];
            voice.transform.position = position;
            voice.pitch = UnityEngine.Random.Range(.92f, 1.08f);
            voice.clip = clips[(int)kind];
            voice.volume = kind == NoiseKind.Intro ? .15f : Mathf.Clamp(loudness * .6f, .12f, .85f);
            voice.Play();
            UploadPulses();
        }

        void Update()
        {
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
        }

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; Shader.SetGlobalInt("_EchoCount", 0); }
            foreach (var clip in clips) if (clip) Destroy(clip);
        }

        // Original procedural sound palette: no downloaded assets or licensing dependencies.
        static AudioClip MakeSound(NoiseKind kind)
        {
            const int sampleRate = 22050;
            float length = kind == NoiseKind.Impact ? .65f : kind == NoiseKind.Intro ? 1f : .32f;
            var samples = new float[(int)(sampleRate * length)];
            var random = new System.Random(714 + (int)kind);
            float lowNoise = 0;
            for (int i = 0; i < samples.Length; ++i)
            {
                float t = (float)i / sampleRate;
                float noise = (float)random.NextDouble() * 2 - 1;
                lowNoise = Mathf.Lerp(lowNoise, noise, .12f);
                float sound;
                switch (kind)
                {
                    case NoiseKind.Cane:
                        sound = (Mathf.Sin(t * 1250 * Mathf.PI * 2) * .45f + noise * .45f) * Mathf.Exp(-t * 38)
                            + Mathf.Sin(t * 410 * Mathf.PI * 2) * Mathf.Exp(-t * 16) * .15f; break;
                    case NoiseKind.Impact:
                        sound = (Mathf.Sin(t * 790 * Mathf.PI * 2) * .22f + Mathf.Sin(t * 1263 * Mathf.PI * 2) * .17f) * Mathf.Exp(-t * 9)
                            + noise * Mathf.Exp(-t * 43) * .5f; break;
                    case NoiseKind.Creature:
                        sound = Mathf.Sin(t * (62 - t * 32) * Mathf.PI * 2) * Mathf.Exp(-t * 19) * .7f
                            + lowNoise * Mathf.Exp(-t * 15) * .65f; break;
                    case NoiseKind.Intro:
                        sound = Mathf.Sin(t * (330 - t * 140) * Mathf.PI * 2) * Mathf.Exp(-t * 5) * .16f; break;
                    default:
                        sound = lowNoise * Mathf.Exp(-t * 22) * 1.5f + Mathf.Sin(t * 105 * Mathf.PI * 2) * Mathf.Exp(-t * 28) * .45f; break;
                }
                samples[i] = Mathf.Clamp(sound * Mathf.Min(t * 1200, 1), -.9f, .9f);
            }
            var clip = AudioClip.Create("Echo " + kind, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
