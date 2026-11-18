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
        [DllImport("__Internal")] static extern int Haunter_TutorialSmokeRequested();
        [DllImport("__Internal")] static extern int Haunter_ExpansionSmokeRequested();
        [DllImport("__Internal")] static extern int Haunter_PreviewLevel();
        [DllImport("__Internal")] static extern int Haunter_ObserverRequested();
        [DllImport("__Internal")] static extern int Haunter_CaptureShot();
        [DllImport("__Internal")] static extern int Haunter_CaptureReady();
        [DllImport("__Internal")] static extern void Haunter_RecordCaptureState(string json);
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
            public bool pointerLocked, touchControls, navigationReady;
            public Vector3 playerPosition, playerRotation, cameraRotation, exitPosition, creaturePosition, creatureTarget;
            public bool crouching, sprinting, chargingThrow, creatureInvestigating, creatureDistracted, creatureLingering;
            public float throwCharge, caneReady, distractionSeconds;
            public string heldObject, nearbyObject;
            public string tutorialStage;
            public bool tutorialFrozen;
            public int level, primaryClaim, secondClaim;
            public string levelName, objective, introStage, primaryBehavior, secondaryBehavior;
            public float acousticLoad, hearingConfidence, secondDistractionSeconds;
            public bool reducedDistortion, crossingCollapsed, quietZone, primaryReflection, secondaryDistracted;
            public Vector3 primaryEstimate, primarySoundOrigin, secondaryPosition, routePoint,
                primaryLure, secondaryLure, finalStaging;
        }

        bool diagnostics;
        bool smoke;
        bool observer;
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
            smoke = SmokeRequested || TutorialSmokeRequested || ExpansionSmokeRequested || CaptureShot > 0;
            observer = Haunter_ObserverRequested() != 0;
            diagnostics = Haunter_DiagnosticsRequested() != 0;
        }

        void Update()
        {
            var session = GameSession.Instance;
            if (!session) return;
            if (observer && !session.GetComponent<HaunterDeveloperView>())
                session.gameObject.AddComponent<HaunterDeveloperView>();
            TouchControls.Publish(session);
            bool locked = Haunter_HasPointerLock() != 0;
            if (!smoke && !TouchControls.Enabled && session.State == SessionState.Playing)
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
                pointerLocked = locked, touchControls = TouchControls.Enabled,
                navigationReady = session.Campaign.Room ? session.Campaign.Room.NavMeshReady : world && world.NavMeshReady,
                playerPosition = player.transform.position, playerRotation = player.transform.eulerAngles,
                cameraRotation = player.ViewCamera ? player.ViewCamera.transform.eulerAngles : Vector3.zero,
                exitPosition = session.Exit.position, creaturePosition = creature.transform.position,
                creatureTarget = creature.TargetPosition, crouching = player.IsCrouching, sprinting = player.IsSprinting,
                chargingThrow = player.ChargingThrow, throwCharge = player.ThrowCharge, caneReady = player.CaneReady,
                heldObject = player.HeldObject ? player.HeldObject.DisplayName : "",
                nearbyObject = player.NearbyObject ? player.NearbyObject.DisplayName : "",
                creatureInvestigating = creature.IsInvestigating, creatureDistracted = creature.IsCommittedToDistraction,
                creatureLingering = creature.IsLingeringAtDistraction, distractionSeconds = creature.DistractionTimeRemaining,
                tutorialStage = session.Tutorial != null ? session.Tutorial.Stage.ToString() : "Inactive",
                tutorialFrozen = session.Tutorial != null && session.Tutorial.WorldFrozen,
                level = session.LevelNumber, levelName = session.Campaign.Title, objective = session.Campaign.Objective,
                introStage = session.Campaign.IntroStage, crossingCollapsed = session.Campaign.CrossingCollapsed,
                quietZone = session.Campaign.Quiet, acousticLoad = NoiseSystem.Instance ? NoiseSystem.Instance.AcousticLoad : 0,
                reducedDistortion = session.ReducedDistortion, routePoint = session.Campaign.Bearing,
                primaryBehavior = creature.BehaviorName, primaryClaim = creature.ClaimedNoiseEventId,
                primaryEstimate = creature.LastHeardPosition, primarySoundOrigin = creature.LastHeardOrigin,
                primaryReflection = creature.LastHeardReflection, hearingConfidence = creature.HearingConfidence
            };
            var second = session.Campaign.SecondCreature;
            if (second)
            {
                snapshot.secondaryPosition = second.transform.position;
                snapshot.secondaryBehavior = second.BehaviorName;
                snapshot.secondClaim = second.ClaimedNoiseEventId;
                snapshot.secondaryDistracted = second.IsCommittedToDistraction;
                snapshot.secondDistractionSeconds = second.DistractionTimeRemaining;
            }
            var room = session.Campaign.Room;
            if (room)
            {
                snapshot.primaryLure = room.PrimaryLure;
                snapshot.secondaryLure = room.SecondaryLure;
                snapshot.finalStaging = room.FinalStaging;
            }
            Haunter_PublishState(JsonUtility.ToJson(snapshot));
        }
#endif

        // Called by the template's large touch menu buttons; normal session guards still apply.
        public void TouchMenuAction(string action)
        {
            if (!TouchControls.Enabled) return;
            var session = GameSession.Instance;
            if (session == null) return;
            if (action == "begin") session.Begin();
            else if (action == "distortion" && session.State == SessionState.Paused) session.ToggleDistortion();
            else if (action == "resume") session.Resume();
            else if (action == "skip")
            {
                if (session.State == SessionState.Ready) session.Begin(true);
                else if (session.State == SessionState.Paused)
                {
                    if (session.Tutorial.Active) session.Tutorial.Skip();
                    session.Resume();
                }
            }
            else if (action == "restart" && session.State != SessionState.Playing) session.Restart();
        }

        public static void NotifyPointerLockRequest()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            acquiredPointerLock = false;
            pointerLockRequestedAt = Time.unscaledTime;
            // Use the Begin/Resume gesture's transient activation even when its key-up
            // has already happened before Unity processes this frame.
            if (!TouchControls.Enabled && !SmokeRequested && !TutorialSmokeRequested && !ExpansionSmokeRequested) Haunter_RequestPointerLock();
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

        public static bool TutorialSmokeRequested
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Haunter_TutorialSmokeRequested() != 0;
#else
                return false;
#endif
            }
        }

        public static bool ExpansionSmokeRequested
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Haunter_ExpansionSmokeRequested() != 0;
#else
                return Array.IndexOf(Environment.GetCommandLineArgs(), "-haunterExpansionSmoke") >= 0;
#endif
            }
        }

        public static int PreviewLevel
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Haunter_PreviewLevel();
#else
                foreach (string arg in Environment.GetCommandLineArgs())
                    if (arg.StartsWith("-haunterLevel=") && int.TryParse(arg.Substring(14), out int level)) return level;
                return 0;
#endif
            }
        }

        public static int CaptureShot
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Haunter_CaptureShot();
#else
                return 0;
#endif
            }
        }

        public static bool CaptureReady
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return Haunter_CaptureReady() != 0;
#else
                return false;
#endif
            }
        }

        public static void RecordCaptureState(string json)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Haunter_RecordCaptureState(json);
#endif
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
