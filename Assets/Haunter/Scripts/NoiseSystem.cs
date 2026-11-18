using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        bool soundUnavailable;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int Haunter_HasWebAudio();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; Heard = null; }

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

        public static void Emit(Vector3 position, float loudness, NoiseKind kind, GameObject source = null)
        {
            if (!Instance || !IsFinite(position) || float.IsNaN(loudness) || float.IsInfinity(loudness)) return;
            loudness = Mathf.Clamp(loudness, .05f, 2f);
            Instance.AddPulse(position, loudness);
            Instance.PlaySound(position, loudness, kind);
            Heard?.Invoke(new NoiseEvent { Position = position, Loudness = loudness, Kind = kind, Source = source });
        }

        static bool IsFinite(Vector3 p) => !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
            || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));

        void AddPulse(Vector3 position, float loudness)
        {
            if (pulses.Count == MaxPulses) pulses.RemoveAt(0);
            pulses.Add(new Pulse { Position = position, Start = Time.time,
                Radius = Mathf.Clamp(loudness * 16f, 3.5f, 28f), Strength = Mathf.Clamp(.6f + loudness * .3f, .5f, 1f) });
            EmittedCount++;
            UploadPulses();
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
        }

    }
}
