using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Haunter
{
    /// <summary>Browser focus handling and opt-in, read-only diagnostics for the WebGL player.</summary>
    public sealed class BrowserBridge : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int Haunter_SmokeRequested();
        [DllImport("__Internal")] static extern int Haunter_DiagnosticsRequested();
        [DllImport("__Internal")] static extern int Haunter_HasPointerLock();
        [DllImport("__Internal")] static extern void Haunter_RequestPointerLock();
        [DllImport("__Internal")] static extern void Haunter_PublishState(string json);
        [DllImport("__Internal")] static extern void Haunter_RecordScreenshot(string name, string base64);
        [DllImport("__Internal")] static extern void Haunter_RecordSmokeReport(string json);

        [Serializable] sealed class Snapshot
        {
            public string coordinates = "Metres; x right/east, y up, z forward/north; origin at room centre.";
            public string state;
            public float elapsedSeconds;
            public int soundsMade, activePulses;
            public bool pointerLocked, navigationReady;
            public Vector3 playerPosition, playerRotation, cameraRotation, exitPosition, creaturePosition, creatureTarget;
            public bool crouching, sprinting, chargingThrow, creatureInvestigating, creatureDistracted, creatureLingering;
            public float throwCharge, caneReady, distractionSeconds;
            public string heldObject, nearbyObject;
        }

        bool diagnostics;
        bool smoke;
        static bool acquiredPointerLock;
        static float pointerLockRequestedAt;
        const float PointerLockGraceSeconds = 1f;
        float nextSnapshotAt;
        PrototypeWorld world;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            if (FindFirstObjectByType<BrowserBridge>()) return;
            DontDestroyOnLoad(new GameObject("Haunter browser bridge").AddComponent<BrowserBridge>().gameObject);
        }

        void Awake()
        {
            smoke = SmokeRequested;
            diagnostics = Haunter_DiagnosticsRequested() != 0;
        }

        void Update()
        {
            var session = GameSession.Instance;
            if (!session) return;
            bool locked = Haunter_HasPointerLock() != 0;
            if (!smoke && session.State == SessionState.Playing)
            {
                if (locked) acquiredPointerLock = true;
                // Esc may be consumed by the browser before Unity receives its key event.
                // A browser may also reject immediate recapture after Esc. Do not leave the
                // player moving without mouse look; a later Resume gesture can try again.
                else if (acquiredPointerLock || Time.unscaledTime - pointerLockRequestedAt >= PointerLockGraceSeconds)
                    session.Pause();
            }
            if (session.State != SessionState.Playing) acquiredPointerLock = false;
            if (!diagnostics || Time.unscaledTime < nextSnapshotAt) return;
            nextSnapshotAt = Time.unscaledTime + .1f;
            if (!world) world = FindFirstObjectByType<PrototypeWorld>();
            var player = session.Player;
            var creature = session.Creature;
            if (!player || !creature || !session.Exit) return;
            var snapshot = new Snapshot
            {
                state = session.State.ToString(), elapsedSeconds = session.ElapsedSeconds,
                soundsMade = session.SoundsMade, activePulses = NoiseSystem.Instance ? NoiseSystem.Instance.ActivePulseCount : 0,
                pointerLocked = locked, navigationReady = world && world.NavMeshReady,
                playerPosition = player.transform.position, playerRotation = player.transform.eulerAngles,
                cameraRotation = player.ViewCamera ? player.ViewCamera.transform.eulerAngles : Vector3.zero,
                exitPosition = session.Exit.position, creaturePosition = creature.transform.position,
                creatureTarget = creature.TargetPosition, crouching = player.IsCrouching, sprinting = player.IsSprinting,
                chargingThrow = player.ChargingThrow, throwCharge = player.ThrowCharge, caneReady = player.CaneReady,
                heldObject = player.HeldObject ? player.HeldObject.DisplayName : "",
                nearbyObject = player.NearbyObject ? player.NearbyObject.DisplayName : "",
                creatureInvestigating = creature.IsInvestigating, creatureDistracted = creature.IsCommittedToDistraction,
                creatureLingering = creature.IsLingeringAtDistraction, distractionSeconds = creature.DistractionTimeRemaining
            };
            Haunter_PublishState(JsonUtility.ToJson(snapshot));
        }
#endif

        public static void NotifyPointerLockRequest()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            acquiredPointerLock = false;
            pointerLockRequestedAt = Time.unscaledTime;
            // Use the Begin/Resume gesture's transient activation even when its key-up
            // has already happened before Unity processes this frame.
            if (!SmokeRequested) Haunter_RequestPointerLock();
#endif
        }

        public static bool SmokeRequested
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Haunter_SmokeRequested() != 0;
#else
                return false;
#endif
            }
        }

        public static void RecordScreenshot(string name, string base64)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Haunter_RecordScreenshot(name, base64);
#endif
        }

        public static void RecordSmokeReport(string json)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Haunter_RecordSmokeReport(json);
#endif
        }
    }
}
