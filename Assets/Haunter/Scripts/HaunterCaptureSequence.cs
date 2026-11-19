using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Haunter
{
    /// <summary>Opt-in staged filming. The normal controller, physics, acoustics and AI play each shot.</summary>
    public sealed class HaunterCaptureSequence : MonoBehaviour
    {
        [Serializable]
        private sealed class CaptureState
        {
            public int shot, level;
            public string stage, label, detail, error;
            public float time;
            public bool recording, done;
        }

        private readonly List<NoiseEvent> impacts = new List<NoiseEvent>();
        private GameSession game;
        private CaptureState state;
        private float recordingStarted;
        private GUIStyle watermark;

#if UNITY_WEBGL && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            int shot = BrowserBridge.CaptureShot;
            if (shot < 1 || shot > 8 || FindFirstObjectByType<HaunterCaptureSequence>()) return;
            var sequence = new GameObject("Haunter in-engine capture").AddComponent<HaunterCaptureSequence>();
            sequence.state = new CaptureState { shot = shot, error = "" };
        }
#endif

        private void OnEnable() => NoiseSystem.Emitted += RememberImpact;

        private void OnDisable() => NoiseSystem.Emitted -= RememberImpact;

        private void RememberImpact(NoiseEvent noise)
        {
            if (noise.Kind == NoiseKind.Impact) impacts.Add(noise);
        }

        private IEnumerator Start()
        {
            if (state == null) yield break;
            yield return null;
            game = GameSession.Instance;
            if (!game || !game.Player || !game.Campaign)
            {
                Fail("The playable scene did not initialize.");
                yield break;
            }
            Mark("ready", "READY TO CAPTURE", "Staged in-engine demonstration.");
            yield return Until(() => BrowserBridge.CaptureReady, 90f);
            if (!BrowserBridge.CaptureReady)
            {
                Fail("The browser recorder did not signal readiness.");
                yield break;
            }

            recordingStarted = Time.realtimeSinceStartup;
            state.recording = true;
            game.Player.AcceptInput = false;
            game.Begin(true);
            switch (state.shot)
            {
                case 1: yield return CourtyardArrival(); break;
                case 2: yield return CourtyardEscape(); break;
                case 3: yield return CourtyardObservation(); break;
                case 4: yield return BallroomArrival(); break;
                case 5: yield return BallroomComparison(); break;
                case 6: yield return BallroomObservation(); break;
                case 7: yield return MazeEscape(false); break;
                case 8: yield return MazeEscape(true); break;
            }
            if (state.done) yield break;
            yield return new WaitForSecondsRealtime(.85f);
            state.recording = false;
            state.done = true;
            Mark("complete", "CAPTURE COMPLETE", "");
        }

        private IEnumerator CourtyardArrival()
        {
            Mark("archive-exit", "LEAVING THE ARCHIVE", "Staged beside the live archive exit with the old guard held behind the camera.");
            game.Creature.CatchSuppressed = true;
            game.Creature.TutorialHold = true;
            game.Player.EnterLevel(game.Exit.position + new Vector3(1.8f, 0, -1.1f));
            LookAt(game.Exit.position + Vector3.up * 1.1f);
            game.Player.TapCane();
            yield return new WaitForSecondsRealtime(.5f);
            yield return WalkPath(game.Exit.position, true, 5f);
            yield return Until(() => game.LevelNumber == 2 && game.State == SessionState.Playing, 4f);
            if (game.LevelNumber != 2) { Fail("The real archive exit did not enter the courtyard."); yield break; }

            var room = game.Campaign.Room;
            Mark("crossing", "AN OPEN CROSSING", "The apparent doorway is still open.");
            LookAt(room.ApparentCrossing + Vector3.up);
            game.Player.TapCane();
            yield return WalkPath(room.CrossingLookout, false, 8f);
            if (game.Campaign.IntroStage != "Collapse") { Fail("Walking halfway did not trigger the collapse."); yield break; }
            Mark("collapse", "THE CROSSING COLLAPSES", "The whole canopy and arch fall before the live crash blocks the doorway.");
            yield return Until(() => game.Campaign.CrossingCollapsed, 4f);
            if (!game.Campaign.CrossingCollapsed) { Fail("The crossing collapse was not observed."); yield break; }
            yield return Until(() => game.Creature.IsCommittedToDistraction, 5f);
            if (!game.Creature.IsCommittedToDistraction) { Fail("The crash did not start the creature's approach."); yield break; }
            Mark("charge", "THE CRASH DRAWS IT OUT", "The campaign briefly guides the player's view to the service arch.");
            yield return Until(() => game.Campaign.IntroStage == "Roaming", 5f);
            yield return FollowCreature(2.6f);
            LookAt(room.CollapsePosition + Vector3.up);
            yield return Until(() => game.Creature.IsLingeringAtDistraction, 10f);
            if (!game.Creature.IsLingeringAtDistraction) { Fail("The listener did not reach the crash site."); yield break; }
            Mark("inspection", "IT INSPECTS THE DEBRIS", "The arrival starts a five-second inspection.");
            yield return new WaitForSecondsRealtime(2.2f);
        }

        private IEnumerator CourtyardEscape()
        {
            if (!Enter(2)) yield break;
            game.Player.EnterLevel(game.Campaign.Room.CrossingLookout);
            Mark("settle", "AFTER THE COLLAPSE", "Wait for the normal patrol to start.");
            yield return Until(() => game.Campaign.IntroStage == "Roaming" && game.Creature.BehaviorName == "Patrolling", 28f);
            var room = game.Campaign.Room;
            var creature = game.Creature;
            if (creature.BehaviorName != "Patrolling") { Fail("Normal courtyard patrol did not start."); yield break; }

            Mark("patrol", "READ THE PATROL", "Staged near an accessible can in the east service lane.");
            game.Player.EnterLevel(new Vector3(6.7f, 0, 5.25f));
            yield return FollowCreature(1.1f);
            var prop = NearestProp();
            Vector3 landing = new Vector3(1.5f, 0, 6f);
            Mark("throw", "SEND IT ACROSS THE COURTYARD", "A real thrown object's impact can pull it away from the stairs.");
            if (!ThrowToward(prop, landing)) { Fail("The courtyard prop could not be picked up and thrown."); yield break; }
            yield return Until(() => WasClaimed(creature, prop), 5f);
            if (!WasClaimed(creature, prop)) { Fail("The courtyard listener did not claim the physical throw."); yield break; }
            yield return FollowCreature(.8f);

            // This edit point avoids spending the clip following the full east perimeter.
            // The ramp and live exit below are still walked with colliders and the creature active.
            Mark("stairs", "TAKE THE SIDE STAIRS", "Cut to the base of the real route, then walk the stairs with collision active.");
            game.Player.EnterLevel(room.UpstairsApproach);
            yield return WalkPath(room.Exit.position, true, 25f);
            yield return Until(() => game.LevelNumber == 3 && game.State == SessionState.Playing, 4f);
            if (game.LevelNumber != 3) { Fail("The alternate staircase did not reach the ballroom."); yield break; }
            Mark("upstairs-exit", "THE UPSTAIRS ENTRANCE", "Crossing the live upper exit enters the ballroom.");
            yield return new WaitForSecondsRealtime(1.3f);
        }

        private IEnumerator CourtyardObservation()
        {
            if (!Enter(2)) yield break;
            game.Player.EnterLevel(game.Campaign.Room.CrossingLookout);
            Mark("settle", "DEVELOPER VIEW", "Wait for the ordinary patrol to start after the collapse.");
            var listener = game.Creature;
            yield return Until(() => game.Campaign.IntroStage == "Roaming" && listener.BehaviorName == "Patrolling", 28f);
            if (listener.BehaviorName != "Patrolling") { Fail("The observer did not see the patrol begin."); yield break; }
            Mark("patrol", "FOLLOW THE PATROL", "The camera follows the actual listener, with no player tracking marker.");
            yield return new WaitForSecondsRealtime(1.8f);
            if (!TryAudibleProbe(listener, out Vector3 probe)) { Fail("No reachable acoustic probe point near the patrol."); yield break; }
            Mark("heard", "AN AUDIBLE IMPACT", "Developer acoustic probe. The listener's normal hearing and pathfinding resolve it.");
            NoiseSystem.Emit(probe, 1.5f, NoiseKind.Impact);
            yield return Until(() => listener.IsLingeringAtDistraction, 11f);
            if (!listener.IsLingeringAtDistraction) { Fail("The listener did not inspect the reachable probe."); yield break; }
            Mark("linger", "FIVE SECONDS AT THE SOUND", "The investigation timer starts on arrival.");
            yield return Until(() => !listener.IsCommittedToDistraction, 7f);
            Mark("return", "BACK TO THE PATROL", "The listener walks to a reachable route point.");
            yield return Until(() => listener.BehaviorName == "Patrolling", 12f);
            if (listener.BehaviorName != "Patrolling") { Fail("The listener did not return to its patrol."); yield break; }
            yield return new WaitForSecondsRealtime(1.3f);
        }

        private IEnumerator BallroomArrival()
        {
            if (!Enter(3)) yield break;
            Mark("ballroom-entry", "ENTER THE BALLROOM", "Start at the authored ballroom entrance.");
            game.Player.TapCane();
            yield return new WaitForSecondsRealtime(3.5f);
            Mark("overlap", "THE ECHOES START TO OVERLAP", "Walk and tap the cane using the ordinary input behavior.");
            yield return WalkPath(new Vector3(-3f, 0, -6.7f), false, 7f, true);
            if (game.State != SessionState.Playing) { Fail("The player was caught during the ballroom demonstration."); yield break; }
            LookAt(new Vector3(0, 1.5f, 11f));
            if (game.Player.CaneReady > .99f) game.Player.TapCane();
            yield return new WaitForSecondsRealtime(1.3f);
            if (game.State != SessionState.Playing) { Fail("The player was caught before the quiet alcove cut."); yield break; }
            Mark("recovery", "A QUIETER SIDE ALCOVE", "Cut to the authored quiet pocket and show the live decay of interference.");
            Vector3 alcove = game.Campaign.Room.QuietZones[0].center;
            alcove.y = 0f;
            game.Player.EnterLevel(alcove);
            LookAt(alcove + new Vector3(1f, 1.5f, 4.8f));
            yield return new WaitForSecondsRealtime(3.1f);
            game.Player.TapCane();
            yield return new WaitForSecondsRealtime(3.5f);
        }

        private IEnumerator BallroomComparison()
        {
            if (!Enter(3)) yield break;
            game.Player.EnterLevel(new Vector3(0, 0, -10.8f));
            LookAt(new Vector3(0, 1.6f, 11f));
            // Hold the same camera pose for both observations. The stimulus travels through
            // the exact sound and shader path used during play.
            game.Creature.CatchSuppressed = true;
            Mark("clear", "ONE TAP. A CLEAR REVEAL.", "Locked camera for a fixed-geometry comparison.");
            game.Player.TapCane();
            yield return new WaitForSecondsRealtime(4.7f);
            Mark("interference", "SAME ROOM. OVERLAPPING ECHOES.", "Developer sound cues raise the ordinary live interference level.");
            for (int i = 0; i < 5; ++i)
            {
                float offset = (i - 2) * 2.05f;
                NoiseSystem.Emit(new Vector3(offset, .1f, -6.2f + (i % 2)), 1.35f, NoiseKind.Cane);
                yield return new WaitForSecondsRealtime(.22f);
            }
            if (NoiseSystem.Instance.AcousticLoad < .64f) { Fail("The comparison did not produce sustained interference."); yield break; }
            yield return new WaitForSecondsRealtime(2.4f);
            Mark("decay", "AS THE WAVES DECAY", "Architecture, interaction points and camera remain fixed.");
            yield return new WaitForSecondsRealtime(4.4f);
            game.Player.TapCane();
            yield return new WaitForSecondsRealtime(3f);
        }

        private IEnumerator BallroomObservation()
        {
            if (!Enter(3)) yield break;
            var listener = game.Creature;
            Mark("patrol", "DEVELOPER VIEW / BALLROOM", "The marks show evidence received by the actual listener.");
            yield return new WaitForSecondsRealtime(3.65f);

            // Prime the visual/acoustic field far from the actor instead of giving it
            // a recent direct fix that would correctly take priority over a reflection.
            for (int i = 0; i < 5; ++i)
                NoiseSystem.Emit(new Vector3(0, .1f, -17.6f), 1.15f, NoiseKind.Cane);
            Vector3 source = listener.transform.position + new Vector3(1.8f, .12f, -.8f);
            Mark("probe", "ONE SOURCE, TWO POSSIBLE LOCATIONS", "A developer-generated acoustic pulse uses the live reflection model.");
            NoiseSystem.Emit(source, 1.65f, NoiseKind.Cane);
            yield return Until(() => listener.LastHeardReflection &&
                Vector3.Distance(listener.LastHeardPosition, listener.LastHeardOrigin) > .2f, 4f);
            if (!listener.LastHeardReflection || Vector3.Distance(listener.LastHeardPosition, listener.LastHeardOrigin) <= .2f)
            {
                Fail("No distinct reflected location was accepted by the listener in this take.");
                yield break;
            }
            Mark("reflection", "IT FOLLOWS THE REFLECTION", "The developer view labels the real source separately from the listener's estimate.");
            yield return new WaitForSecondsRealtime(7.4f);
            Mark("memory-decay", "THE SOUND MEMORY FADES", "The uncertainty display loses confidence without new audible evidence.");
            yield return new WaitForSecondsRealtime(3.4f);
        }

        private IEnumerator MazeEscape(bool useBoth)
        {
            if (!Enter(4)) yield break;
            var room = game.Campaign.Room;
            var guard = game.Creature;
            var roamer = game.Campaign.SecondCreature;
            if (!roamer) { Fail("The maze roamer was not present."); yield break; }
            Mark("roles", "A GUARD AND A ROAMER", "Staged at the authored throw mark. The creatures keep their normal hearing, catch and movement rules.");
            game.Player.EnterLevel(room.FinalStaging);
            Vector3 between = (guard.transform.position + roamer.transform.position) * .5f;
            LookAt(between + Vector3.up * 1.2f);
            // Use a presentation cue that makes no claim about a listener having heard it.
            NoiseSystem.Emit(between + Vector3.up * .15f, .85f, NoiseKind.Intro);
            yield return new WaitForSecondsRealtime(1.3f);

            var west = NearestProp();
            var east = NearestProp(west);
            Mark("first-throw", "ONE THROW, ONE INVESTIGATOR", "Real pickup, flight and impact. The other creature remains available.");
            if (!ThrowToward(west, room.PrimaryLure)) { Fail("Could not throw the first maze prop."); yield break; }
            yield return Until(() => HasImpact(west), 5f);
            if (!WasClaimed(roamer, west) || guard.IsCommittedToDistraction)
            { Fail("The first throw did not occupy just the roamer."); yield break; }

            if (!useBoth)
            {
                Mark("guard-stays", "THE EXIT GUARD STAYS", "Turning to the guard shows why the first lure does not clear the door.");
                LookAt(guard.transform.position + Vector3.up);
                yield return new WaitForSecondsRealtime(3.1f);
                Mark("roamer-inspects", "THE ROAMER CHECKS THE SOUND", "Its investigation timer expires before it physically resumes the west patrol.");
                yield return FollowCreature(roamer, 7.2f);
                Mark("guard-still-there", "THE EXIT IS STILL GUARDED", "The single throw did not move the exit guard.");
                LookAt(guard.transform.position + Vector3.up);
                yield return new WaitForSecondsRealtime(3f);
                yield break;
            }

            Mark("second-throw", "A SECOND IMPACT DRAWS THE GUARD", "A distinct prop claims the second creature.");
            if (!ThrowToward(east, room.SecondaryLure)) { Fail("Could not throw the second maze prop."); yield break; }
            yield return Until(() => HasImpact(east), 5f);
            if (!WasClaimed(guard, east) || !WasClaimed(roamer, west))
            { Fail("The two props did not claim different listeners simultaneously."); yield break; }
            LookAt(room.Exit.position + Vector3.up);
            yield return Until(() => Mathf.Abs(guard.transform.position.x - room.Exit.position.x) > 1.6f, 5f);
            if (!guard.IsCommittedToDistraction || !roamer.IsCommittedToDistraction)
            { Fail("The overlap ended before the guard cleared the aisle."); yield break; }
            Mark("escape", "WALK THROUGH THE OPENING", "Both listeners are committed. The player walks through the live exit with capture active.");
            yield return WalkPath(room.Exit.position, false, 5f);
            if (game.State != SessionState.Escaped) { Fail("The actual final exit was not reached."); yield break; }
            Mark("final-exit", "ESCAPED", "The four-level run reached its real ending.");
            yield return new WaitForSecondsRealtime(2.3f);
        }

        private bool Enter(int level)
        {
            if (game.Campaign.Enter(level)) return true;
            Fail("Campaign level " + level + " could not be staged for the recording.");
            return false;
        }

        private IEnumerator FollowCreature(float seconds)
            => FollowCreature(game.Creature, seconds);

        private IEnumerator FollowCreature(ListenerCreature creature, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end && game.State == SessionState.Playing && creature)
            {
                game.Player.GuideLookAt(creature.transform.position + Vector3.up, 1f - Mathf.Exp(-7f * Time.deltaTime));
                yield return null;
            }
        }

        private void LookAt(Vector3 target) => game.Player.GuideLookAt(target, 1f);

        private IEnumerator WalkPath(Vector3 target, bool crouch, float seconds, bool tapWhileMoving = false)
        {
            var player = game.Player;
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(player.transform.position, out var start, 1.2f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(target, out var end, 1.2f, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete) yield break;
            int level = game.LevelNumber;
            float deadline = Time.realtimeSinceStartup + seconds;
            foreach (Vector3 corner in path.corners)
            {
                while (game.State == SessionState.Playing && game.LevelNumber == level && Time.realtimeSinceStartup < deadline)
                {
                    Vector3 delta = corner - player.transform.position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < .13f * .13f) break;
                    Vector3 heading = delta.normalized;
                    player.GuideLookAt(player.ViewCamera.transform.position + heading * 8f, 1f - Mathf.Exp(-8f * Time.deltaTime));
                    Vector3 local = player.transform.InverseTransformDirection(heading);
                    player.ApplyMovement(new Vector2(local.x, local.z), crouch, false);
                    if (tapWhileMoving && player.CaneReady > .99f) player.TapCane();
                    yield return null;
                }
            }
            yield return null;
        }

        private ThrowableObject NearestProp(ThrowableObject omit = null)
        {
            ThrowableObject best = null;
            float distance = game.Player.PickupReach;
            foreach (var item in game.Campaign.Room.GetComponentsInChildren<ThrowableObject>())
            {
                if (item == omit || item.IsHeld) continue;
                float candidate = Vector3.Distance(game.Player.ViewCamera.transform.position, item.transform.position);
                if (candidate >= distance) continue;
                best = item;
                distance = candidate;
            }
            return best;
        }

        private bool ThrowToward(ThrowableObject prop, Vector3 landing)
        {
            if (!prop) return false;
            var player = game.Player;
            player.PickUp(prop);
            if (player.HeldObject != prop) return false;
            var camera = player.ViewCamera.transform;
            Vector3 delta = landing - camera.position;
            delta.y = 0;
            float height = camera.position.y - landing.y - prop.HoldRadius * .5f;
            float travel = Mathf.Sqrt(Mathf.Max(.1f, 2f * height / -Physics.gravity.y));
            float speed = Mathf.Clamp((delta.magnitude - .7f) / travel, 7.2f, 15f);
            player.GuideLookAt(camera.position + (delta.normalized + Vector3.down * .14f) * 8f, 1f);
            player.ThrowHeld((camera.forward + Vector3.up * .14f).normalized * speed);
            return player.HeldObject != prop;
        }

        private bool HasImpact(ThrowableObject prop) => prop && impacts.Exists(noise => noise.Source == prop.gameObject);

        private bool WasClaimed(ListenerCreature creature, ThrowableObject prop)
            => prop && creature && creature.ClaimedNoiseEventId > 0 &&
                impacts.Exists(noise => noise.Source == prop.gameObject && noise.EventId == creature.ClaimedNoiseEventId);

        private static bool TryAudibleProbe(ListenerCreature creature, out Vector3 result)
        {
            Vector3 origin = creature.transform.position;
            var directions = new[] { creature.transform.forward, Vector3.right, Vector3.left, Vector3.back };
            var path = new NavMeshPath();
            foreach (Vector3 direction in directions)
            {
                if (!NavMesh.SamplePosition(origin + direction * 4f, out var hit, 1.4f, NavMesh.AllAreas) ||
                    Vector3.Distance(hit.position, origin) < 2f ||
                    !NavMesh.CalculatePath(origin, hit.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                result = hit.position;
                return true;
            }
            result = Vector3.zero;
            return false;
        }

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        private void Fail(string message)
        {
            state.error = message;
            state.recording = false;
            state.done = true;
            Mark("failed", "CAPTURE FAILED", "The take was not verified.");
            Debug.LogWarning("Haunter capture " + state.shot + ": " + message);
        }

        private void Mark(string stage, string label, string detail)
        {
            state.level = game ? game.LevelNumber : 0;
            state.stage = stage;
            state.label = label;
            state.detail = detail;
            state.time = recordingStarted > 0 ? Time.realtimeSinceStartup - recordingStarted : 0;
            BrowserBridge.RecordCaptureState(JsonUtility.ToJson(state));
        }

        private void OnGUI()
        {
            if (state == null || !state.recording || HaunterDeveloperView.IsActive) return;
            if (watermark == null)
                watermark = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 14 };
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1f));
            GUI.Box(new Rect(460, 679, 360, 28), "");
            GUI.Label(new Rect(460, 679, 360, 28), "IN-ENGINE CAPTURE  /  STAGED SETUP", watermark);
            GUI.matrix = previous;
        }
    }
}
