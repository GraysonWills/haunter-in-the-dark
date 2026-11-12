using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Haunter
{
    /// <summary>Opt-in integration checks using normal tutorial actions and live physics.</summary>
    public sealed class TutorialSmokeTest : MonoBehaviour
    {
        [Serializable] sealed class Report { public string[] passed, failed, errors; }
        readonly List<string> passed = new List<string>();
        readonly List<string> failed = new List<string>();
        readonly List<string> errors = new List<string>();
        string evidence;
        bool finished, reached;
        float startedAt;
        int targetImpacts;
        ThrowableObject observedObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!BrowserBridge.TutorialSmokeRequested) return;
#else
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--haunter-tutorial-smoke") < 0) return;
#endif
            if (FindFirstObjectByType<TutorialSmokeTest>()) return;
            DontDestroyOnLoad(new GameObject("Tutorial verification").AddComponent<TutorialSmokeTest>().gameObject);
        }

        void Awake()
        {
            startedAt = Time.realtimeSinceStartup;
            // Hidden native windows can render thousands of frames per second without vsync.
            // Keep controller displacement representative of an interactive 60 Hz player.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
#if !UNITY_WEBGL || UNITY_EDITOR
            evidence = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TutorialEvidence");
            Directory.CreateDirectory(evidence);
#endif
            Application.logMessageReceived += OnLog;
            NoiseSystem.Heard += OnNoise;
        }

        void Update()
        {
            if (!finished && Time.realtimeSinceStartup - startedAt > 180f)
            {
                Check(false, "Tutorial integration run completed within 180 seconds");
                Finish();
            }
        }

        void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                errors.Add(message + "\n" + trace);
        }

        void OnNoise(NoiseEvent noise)
        {
            if (observedObject && noise.Source == observedObject.gameObject && noise.Kind == NoiseKind.Impact)
                targetImpacts++;
        }

        void Check(bool result, string name)
        {
            (result ? passed : failed).Add(name);
            Debug.Log((result ? "HAUNTER_TUTORIAL_PASS " : "HAUNTER_TUTORIAL_FAIL ") + name);
        }

        static bool Allows(PlayerController player, PlayerActions action) => (player.AllowedActions & action) == action;

        IEnumerator Start()
        {
            yield return null;
            var session = GameSession.Instance;
            Check(session && session.Player && session.Creature && session.Tutorial, "Tutorial and scene references initialized");
            if (!session || !session.Player || !session.Creature || !session.Tutorial) { Finish(); yield break; }
            var player = session.Player;
            var creature = session.Creature;
            var tutorial = session.Tutorial;
            player.AcceptInput = false;
            session.Begin();
            yield return null;
            Check(tutorial.Active && tutorial.Stage == TutorialStage.TapCane, "A fresh run requires the first cane tap");
            Check(Allows(player, PlayerActions.Cane) && !Allows(player, PlayerActions.Move)
                && !Allows(player, PlayerActions.Pickup) && !Allows(player, PlayerActions.Throw),
                "Opening input gate accepts cane and blocks movement and object actions");
            Vector3 startPosition = player.transform.position;
            var arbitraryProp = FindFirstObjectByType<ThrowableObject>();
            player.PickUp(arbitraryProp);
            player.ApplyMovement(Vector2.up, false, true);
            yield return new WaitForSecondsRealtime(.2f);
            Check(player.HeldObject == null && PlayerController.HorizontalDistance(startPosition, player.transform.position) < .01f
                && tutorial.Stage == TutorialStage.TapCane, "Unrequested actions cannot bypass the cane instruction");
            yield return Capture("01-tap-cane.png");
            int pawsteps = creature.TotalPawsteps;
            int pulses = NoiseSystem.Instance.EmittedCount;
            player.TapCane();
            yield return null;
            Check(NoiseSystem.Instance.EmittedCount > pulses && tutorial.Stage == TutorialStage.RevealCreature,
                "A real cane echo begins the creature introduction");
            yield return new WaitForSecondsRealtime(.8f);
            Check(creature.TotalPawsteps > pawsteps, "The introduction emits the creature's own paw echoes");
            Check(!Allows(player, PlayerActions.Move) && !Allows(player, PlayerActions.Look),
                "Creature introduction controls the camera and keeps the player still");
            yield return Capture("02-creature-introduction.png");
            yield return WaitForStage(tutorial, TutorialStage.Move, 6f);
            if (!reached) { Finish(); yield break; }
            Check(Allows(player, PlayerActions.Move), "Movement is enabled after the creature introduction");
            yield return WalkUntilFootsteps(session, 1);
            Check(tutorial.FootstepsTaken == 1 && tutorial.Stage == TutorialStage.Move,
                "The first real walking footstep does not prematurely freeze the lesson");
            Vector3 beforeChase = creature.transform.position;
            yield return WalkUntilFootsteps(session, 2);
            Check(tutorial.FootstepsTaken == 2 && tutorial.Stage == TutorialStage.Charge,
                "The second real walking footstep triggers the creature's charge");
            yield return WaitForStage(tutorial, TutorialStage.FocusPickup, 8f);
            if (!reached) { Finish(); yield break; }
            Check(Vector3.Distance(beforeChase, creature.transform.position) > .5f,
                "The creature physically runs toward the player before the freeze");
            Check(tutorial.WorldFrozen && Time.timeScale == 0f, "The emergency lesson freezes game time");
            Vector3 frozenCreature = creature.transform.position;
            Vector3 frozenPlayer = player.transform.position;
            float frozenTime = Time.time;
            yield return WaitForStage(tutorial, TutorialStage.Pickup, 3f);
            if (!reached) { Finish(); yield break; }
            yield return null; // Let the last guided camera frame settle onto the pickup target.
            Check(Time.time == frozenTime && Vector3.Distance(creature.transform.position, frozenCreature) < .01f
                && Vector3.Distance(player.transform.position, frozenPlayer) < .01f,
                "Camera guidance advances while both actors remain frozen");
            observedObject = tutorial.TargetObject;
            Check(observedObject && !observedObject.IsHeld, "Frozen pickup points to an available physical object");
            if (!observedObject) { Finish(); yield break; }
            Check(PickupRayReaches(player, observedObject), "The guided camera makes the required object selectable with E");
            Check(Allows(player, PlayerActions.Pickup) && !Allows(player, PlayerActions.Move)
                && !Allows(player, PlayerActions.Look) && !Allows(player, PlayerActions.Cane)
                && !Allows(player, PlayerActions.Throw), "Frozen pickup permits only the requested object action");
            Vector3 objectPosition = observedObject.transform.position;
            Quaternion cameraRotation = player.ViewCamera.transform.rotation;
            pulses = NoiseSystem.Instance.EmittedCount;
            player.TapCane();
            player.ApplyMovement(Vector2.up, false, true);
            foreach (var item in FindObjectsByType<ThrowableObject>(FindObjectsSortMode.None))
                if (item != observedObject) { player.PickUp(item); break; }
            yield return new WaitForSecondsRealtime(.3f);
            Check(player.HeldObject == null && NoiseSystem.Instance.EmittedCount == pulses
                && Vector3.Distance(player.transform.position, frozenPlayer) < .01f
                && Vector3.Distance(observedObject.transform.position, objectPosition) < .01f
                && Quaternion.Angle(cameraRotation, player.ViewCamera.transform.rotation) < .1f,
                "Wrong pickup, cane, movement and gravity cannot escape the frozen instruction");
            Debug.Log("HAUNTER_TUTORIAL_FROZEN held=" + (player.HeldObject != null)
                + " newPulses=" + (NoiseSystem.Instance.EmittedCount - pulses)
                + " playerDrift=" + Vector3.Distance(player.transform.position, frozenPlayer)
                + " objectDrift=" + Vector3.Distance(observedObject.transform.position, objectPosition)
                + " cameraDrift=" + Quaternion.Angle(cameraRotation, player.ViewCamera.transform.rotation));
            yield return CheckPause(session, "frozen pickup", 0f);
            yield return Capture("03-frozen-pickup.png");
            player.PickUp(observedObject);
            yield return null;
            Check(player.HeldObject == observedObject && observedObject.IsHeld && observedObject.Body.isKinematic,
                "Requested pickup attaches the real prop while time remains frozen");
            var block = new MaterialPropertyBlock();
            observedObject.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
            Check(block.GetFloat("_AlwaysVisible") > .5f, "Tutorial pickup retains the visible hand outline");
            yield return WaitForStage(tutorial, TutorialStage.Throw, 4f);
            if (!reached) { Finish(); yield break; }
            Check(tutorial.WorldFrozen && Time.timeScale == 0f && Allows(player, PlayerActions.Throw)
                && !Allows(player, PlayerActions.Move) && !Allows(player, PlayerActions.Look)
                && !Allows(player, PlayerActions.Drop), "Aimed throw freezes movement and camera while permitting only throw");
            player.DropHeld();
            Check(player.HeldObject == observedObject, "Dropping cannot bypass the required throw");
            yield return Capture("04-aimed-throw.png");
            targetImpacts = 0;
            player.ThrowHeld(Vector3.zero); // Tutorial aims the normal throw at its selected reachable landing point.
            Vector3 pursuitTarget = creature.TargetPosition;
            foreach (var item in FindObjectsByType<ThrowableObject>(FindObjectsSortMode.None))
                if (item != observedObject)
                {
                    NoiseSystem.Emit(creature.transform.position + Vector3.left * .5f, 1.5f, NoiseKind.Impact, item.gameObject);
                    break;
                }
            Check(!creature.IsCommittedToDistraction && Vector3.Distance(creature.TargetPosition, pursuitTarget) < .01f,
                "An unrelated impact cannot steal the authored thrown-object demonstration");
            yield return null;
            Check(player.HeldObject == null && !observedObject.IsHeld && !observedObject.Body.isKinematic,
                "Throw releases the physical prop instead of merely advancing the prompt");
            Check(tutorial.Stage == TutorialStage.WatchDistraction && Time.timeScale == 1f,
                "Throw resumes physics to demonstrate the distraction");
            yield return WaitForStage(tutorial, TutorialStage.Crouch, 15f);
            if (!reached) { Finish(); yield break; }
            Check(targetImpacts > 0 && creature.IsLingeringAtDistraction,
                "A real collision draws the creature to inspect the thrown object");
            Check(creature.DistractionTimeRemaining >= 4.5f, "Inspection begins with the existing full five-second linger");
            Vector3 crouchStart = player.transform.position;
            for (int i = 0; i < 4; i++) { player.ApplyMovement(Vector2.down, false, true); yield return null; }
            Check(PlayerController.HorizontalDistance(crouchStart, player.transform.position) < .01f && !tutorial.HasCrouched,
                "Standing movement stays blocked until crouch is used");
            float crouchDeadline = Time.realtimeSinceStartup + 2f;
            while (PlayerController.HorizontalDistance(crouchStart, player.transform.position) < .3f
                && Time.realtimeSinceStartup < crouchDeadline)
            {
                player.ApplyMovement(Vector2.down, true, false);
                yield return null;
            }
            Check(player.IsCrouching && tutorial.HasCrouched
                && PlayerController.HorizontalDistance(crouchStart, player.transform.position) > .01f,
                "Holding crouch permits quiet movement during the distraction");
            yield return CheckPause(session, "live distraction", 1f);
            yield return Capture("05-crouch-and-slip-past.png");
            yield return WaitForStage(tutorial, TutorialStage.Complete, 20f);
            if (!reached) { Finish(); yield break; }
            Check(!tutorial.Active && creature.IsGuardingExit && Time.timeScale == 1f
                && player.AllowedActions == PlayerActions.All, "The guard returns and the completed tutorial restores normal play");
            yield return new WaitForSecondsRealtime(.3f);
            Check(tutorial.Stage == TutorialStage.Complete, "Completed tutorial does not restart during the same run");
            session.Catch();
            Check(session.State == SessionState.Caught, "A completed tutorial still permits the normal death state");
            yield return RestartRun();
            session = GameSession.Instance;
            Check(session && session.Tutorial.Stage == TutorialStage.TapCane && session.Tutorial.FootstepsTaken == 0,
                "Restarting after death resets the complete tutorial");

            // Exercise cancellation while waiting for input, during pans, and while physics runs.
            foreach (TutorialStage stage in new[] { TutorialStage.TapCane, TutorialStage.RevealCreature,
                TutorialStage.Move, TutorialStage.Charge, TutorialStage.FocusPickup, TutorialStage.Pickup,
                TutorialStage.FocusThrow, TutorialStage.Throw, TutorialStage.WatchDistraction, TutorialStage.Crouch })
            {
                session = GameSession.Instance;
                yield return AdvanceTo(session, stage);
                if (!reached) { Finish(); yield break; }
                bool wasFrozen = session.Tutorial.WorldFrozen;
                // The paused-menu skip must not accidentally resume an otherwise paused game.
                if (stage == TutorialStage.Throw) session.Pause();
                session.Tutorial.Skip();
                yield return null;
                Check(session.Tutorial.Stage == TutorialStage.Skipped && !session.Tutorial.Active
                    && session.Player.AllowedActions == PlayerActions.All,
                    "Skip restores ordinary controls from " + stage);
                if (stage == TutorialStage.Throw)
                {
                    Check(session.State == SessionState.Paused && Time.timeScale == 0f,
                        "Skipping from the pause menu preserves the user's pause");
                    session.Resume();
                }
                Check(Time.timeScale == 1f && !session.Tutorial.WorldFrozen,
                    "Skip restores live game time from " + stage + (wasFrozen ? " (frozen)" : ""));
                yield return RestartRun();
            }
            foreach (Vector2 openingMove in new[] { Vector2.down, Vector2.right })
            {
                session = GameSession.Instance;
                string direction = openingMove == Vector2.down ? "backward" : "sideways";
                yield return AdvanceTo(session, TutorialStage.Pickup, openingMove);
                if (!reached) { Finish(); yield break; }
                yield return null;
                var target = session.Tutorial.TargetObject;
                Check(target && Vector3.Distance(session.Player.ViewCamera.transform.position, target.transform.position)
                    <= session.Player.PickupReach && PickupRayReaches(session.Player, target),
                    "The required object stays selectable and in reach after " + direction + " opening steps");
                observedObject = target;
                targetImpacts = 0;
                yield return AdvanceTo(session, TutorialStage.Crouch, openingMove);
                if (!reached) { Finish(); yield break; }
                Check(targetImpacts > 0 && session.Creature.IsLingeringAtDistraction,
                    "A real distraction succeeds after " + direction + " opening steps");
                float crouchLimit = Time.realtimeSinceStartup + 2f;
                while (!session.Tutorial.HasCrouched && Time.realtimeSinceStartup < crouchLimit)
                {
                    session.Player.ApplyMovement(Vector2.down, true, false);
                    yield return null;
                }
                yield return WaitForStage(session.Tutorial, TutorialStage.Complete, 20f);
                if (!reached) { Finish(); yield break; }
                Check(session.State == SessionState.Playing && !session.Tutorial.Active,
                    "Tutorial finishes safely after " + direction + " opening steps");
                yield return RestartRun();
            }
            Check(errors.Count == 0, "No Unity errors or exceptions during tutorial and cancellation checks");
            Finish();
        }

        IEnumerator WalkUntilFootsteps(GameSession session, int count)
        {
            Vector3 start = session.Player.transform.position;
            int frames = Time.frameCount;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (session.Tutorial.FootstepsTaken < count && session.Tutorial.Stage == TutorialStage.Move
                && Time.realtimeSinceStartup < deadline)
            {
                session.Player.ApplyMovement(Vector2.up, false, false);
                yield return null;
            }
            Debug.Log("HAUNTER_TUTORIAL_WALK expected=" + count + " heard=" + session.Tutorial.FootstepsTaken
                + " stage=" + session.Tutorial.Stage + " start=" + start + " end=" + session.Player.transform.position
                + " frames=" + (Time.frameCount - frames) + " dt=" + Time.deltaTime
                + " grounded=" + session.Player.GetComponent<CharacterController>().isGrounded);
        }

        IEnumerator WaitForStage(TutorialDirector tutorial, TutorialStage stage, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (tutorial && tutorial.Stage != stage && Time.realtimeSinceStartup < deadline
                && GameSession.Instance && GameSession.Instance.State == SessionState.Playing) yield return null;
            reached = tutorial && tutorial.Stage == stage;
            Check(reached, "Reaches " + stage + " without bypassing actions"
                + (reached ? "" : " (actual " + (tutorial ? tutorial.Stage.ToString() : "destroyed") + ")"));
        }

        IEnumerator CheckPause(GameSession session, string context, float restoredScale)
        {
            var stage = session.Tutorial.Stage;
            var rotation = session.Player.ViewCamera.transform.rotation;
            float linger = session.Creature.DistractionTimeRemaining;
            session.Pause();
            yield return new WaitForSecondsRealtime(.3f);
            Check(session.State == SessionState.Paused && Time.timeScale == 0f && session.Tutorial.Stage == stage
                && Quaternion.Angle(rotation, session.Player.ViewCamera.transform.rotation) < .1f
                && Mathf.Abs(session.Creature.DistractionTimeRemaining - linger) < .03f,
                "Pause suspends tutorial camera, stage and creature timer during " + context);
            session.Resume();
            yield return null;
            Check(session.State == SessionState.Playing && Time.timeScale == restoredScale,
                "Resume restores the correct time scale during " + context);
        }

        IEnumerator RestartRun()
        {
            var oldSession = GameSession.Instance;
            oldSession.Restart();
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((!GameSession.Instance || GameSession.Instance == oldSession) && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return null;
            var session = GameSession.Instance;
            if (!session || session == oldSession) { Check(false, "Scene reload completes"); Finish(); yield break; }
            session.Player.AcceptInput = false;
            session.Begin();
            yield return null;
        }

        IEnumerator AdvanceTo(GameSession session, TutorialStage target, Vector2? openingMove = null)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (session.Tutorial.Stage != target && session.State == SessionState.Playing
                && Time.realtimeSinceStartup < deadline)
            {
                switch (session.Tutorial.Stage)
                {
                    case TutorialStage.TapCane: session.Player.TapCane(); break;
                    case TutorialStage.Move: session.Player.ApplyMovement(openingMove ?? Vector2.up, false, false); break;
                    case TutorialStage.Pickup: session.Player.PickUp(session.Tutorial.TargetObject); break;
                    case TutorialStage.Throw: session.Player.ThrowHeld(Vector3.zero); break;
                    case TutorialStage.Crouch: session.Player.ApplyMovement(Vector2.zero, true, false); break;
                }
                yield return null;
            }
            reached = session.Tutorial.Stage == target;
            Check(reached, "Prepare skip at " + target + (reached ? "" : " (actual " + session.Tutorial.Stage + ")"));
        }

        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            if (screenshot)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                BrowserBridge.RecordScreenshot(filename, Convert.ToBase64String(screenshot.EncodeToPNG()));
#else
                File.WriteAllBytes(Path.Combine(evidence, filename), screenshot.EncodeToPNG());
#endif
                Destroy(screenshot);
            }
        }

        static bool PickupRayReaches(PlayerController player, ThrowableObject target)
        {
            Transform view = player.ViewCamera.transform;
            return Physics.Raycast(view.position, view.forward, out var hit, player.PickupReach,
                ~(1 << 9), QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<ThrowableObject>() == target;
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            string report = JsonUtility.ToJson(new Report
                { passed = passed.ToArray(), failed = failed.ToArray(), errors = errors.ToArray() }, true);
#if UNITY_WEBGL && !UNITY_EDITOR
            BrowserBridge.RecordSmokeReport(report);
#else
            File.WriteAllText(Path.Combine(evidence, "tutorial-report.json"), report);
#endif
            Debug.Log("HAUNTER_TUTORIAL_FINISHED passed=" + passed.Count + " failed=" + failed.Count + " errors=" + errors.Count);
#if !UNITY_WEBGL || UNITY_EDITOR
            Application.Quit(failed.Count == 0 && errors.Count == 0 ? 0 : 1);
#endif
        }

        void OnDestroy() { Application.logMessageReceived -= OnLog; NoiseSystem.Heard -= OnNoise; }
    }
}
