using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace Haunter
{
    /// <summary>Opt-in checks for the four-level build using its real physics, audio events and navigation.</summary>
    public sealed class ExpansionSmokeTest : MonoBehaviour
    {
        [Serializable] sealed class Report { public string[] passed, failed, errors, fixtures; }

        readonly List<string> passed = new List<string>();
        readonly List<string> failed = new List<string>();
        readonly List<string> errors = new List<string>();
        readonly List<string> fixtures = new List<string>();
        readonly List<NoiseEvent> impacts = new List<NoiseEvent>();
        readonly List<NoiseEvent> reflections = new List<NoiseEvent>();
        bool finished;
        float startedAt;
        int collapseSounds;
        GameSession game;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!BrowserBridge.ExpansionSmokeRequested) return;
#else
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-haunterExpansionSmoke") < 0) return;
#endif
            if (FindFirstObjectByType<ExpansionSmokeTest>()) return;
            var host = new GameObject("Expansion verification");
            DontDestroyOnLoad(host);
            host.AddComponent<ExpansionSmokeTest>();
        }

        void Awake()
        {
            startedAt = Time.realtimeSinceStartup;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Application.logMessageReceived += Log;
            NoiseSystem.Emitted += OnEmission;
            NoiseSystem.Heard += OnHearing;
        }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - startedAt < 210f) return;
            Check(false, "Expansion integration run completed before the real-time deadline");
            Finish();
        }

        void Log(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                errors.Add(message + "\n" + stackTrace);
        }

        void OnEmission(NoiseEvent noise)
        {
            if (noise.Kind == NoiseKind.Collapse) collapseSounds++;
            if (noise.Kind == NoiseKind.Impact) impacts.Add(noise);
        }

        void OnHearing(NoiseEvent noise)
        {
            if (noise.IsReflection) reflections.Add(noise);
        }

        IEnumerator Start()
        {
            yield return null;
            game = GameSession.Instance;
            if (!Check(game && game.Player && game.Creature && game.Campaign && game.Exit,
                "Archive and expansion services initialized")) { Finish(); yield break; }
            game.Player.AcceptInput = false;
            game.Begin(true);
            fixtures.Add("Skipped the separate guided-opening suite and staged the player beside the archive exit.");
            game.Creature.CatchSuppressed = true;
            game.Player.EnterLevel(game.Exit.position);
            yield return WaitFor(() => game.LevelNumber == 2, 3f);
            if (!Check(game.LevelNumber == 2, "Real archive exit trigger enters the courtyard")) { Finish(); yield break; }

            yield return TestCourtyard();
            if (game.LevelNumber != 3) { Finish(); yield break; }
            yield return TestBallroom();
            if (game.LevelNumber != 4) { Finish(); yield break; }
            yield return TestMaze();
            Finish();
        }

        IEnumerator TestCourtyard()
        {
            var room = game.Campaign.Room;
            var listener = game.Creature;
            Vector3 crossingFrom = room.CollapsePosition + new Vector3(0, .8f, -2.4f);
            Vector3 crossingTo = room.CollapsePosition + new Vector3(0, .8f, .82f);
            Check(!Physics.Linecast(crossingFrom, crossingTo, 1, QueryTriggerInteraction.Ignore), "Lower crossing begins open");
            Check(room.NavMeshReady && listener.Role == ListenerRole.Roamer, "Courtyard has a navigation mesh and a roaming listener");

            yield return WaitFor(() => game.Campaign.CrossingCollapsed, 7f);
            Check(game.Campaign.CrossingCollapsed && collapseSounds == 1 && NoiseSystem.Instance.ActivePulseCount > 0,
                "Timed collapse sends one crash event and an echo wave");
            Check(Physics.Linecast(crossingFrom, crossingTo, 1, QueryTriggerInteraction.Ignore),
                "Fallen masonry physically closes the lower crossing");
            Check(room.NavMeshReady && FindPath(room.Spawn, room.Exit.position, out _), "Rebaked courtyard retains a complete upstairs route");

            yield return WaitFor(() => listener.IsCommittedToDistraction, 3f);
            Check(listener.IsCommittedToDistraction && listener.DistractionTimeRemaining == 0f,
                "Scripted charge approaches the crash before starting its inspection");
            yield return WaitFor(() => listener.IsLingeringAtDistraction, 14f);
            if (Check(listener.IsLingeringAtDistraction && listener.DistractionTimeRemaining >= 4.75f,
                "Crash inspection begins near its destination with the full five seconds"))
            {
                float arrived = Time.time;
                int pawsteps = listener.TotalPawsteps;
                Vector3 accepted = listener.TargetPosition;
                NoiseSystem.Emit(listener.transform.position + Vector3.right * 2f, 1.5f, NoiseKind.Impact);
                yield return new WaitForSeconds(4.35f);
                Check(listener.IsLingeringAtDistraction && listener.TargetPosition == accepted && listener.TotalPawsteps > pawsteps,
                    "Later sounds cannot redirect the inspecting creature, which continues to make paw sounds");
                yield return WaitFor(() => !listener.IsCommittedToDistraction, 1.7f);
                Check(!listener.IsCommittedToDistraction && Mathf.Abs(Time.time - arrived - 5f) < .3f,
                    "The courtyard investigation lasts five full game seconds after arrival");
                yield return WaitFor(() => listener.BehaviorName == "Patrolling", 9f);
                Check(listener.BehaviorName == "Patrolling" && listener.PatrolWaypointIndex >= 0 && NearPatrol(listener, room.Patrol),
                    "Courtyard creature physically returns to its authored route and resumes patrol");
            }

            fixtures.Add("Staged at the courtyard side stairs. Real crouch movement, colliders, the ramp and the upstairs trigger remain active.");
            if (game.State == SessionState.Playing && FindPath(room.UpstairsApproach, room.Exit.position, out _))
            {
                game.Player.EnterLevel(room.UpstairsApproach);
                yield return WalkPath(room.Exit.position, true, 24f);
            }
            Check(game.LevelNumber == 3, "Crouching up the alternate route reaches the ballroom through its live exit");
        }

        IEnumerator TestBallroom()
        {
            var room = game.Campaign.Room;
            var listener = game.Creature;
            Vector3 probe = room.Spawn + Vector3.up * 1.3f;
            var directions = Vector3.back;
            bool hasWall = Physics.Raycast(probe, directions, out var wall, 4f, 1, QueryTriggerInteraction.Ignore);
            bool hasRoute = FindPath(room.Spawn, room.Exit.position, out var originalRoute);
            Check(room.QuietZones.Length > 0 && hasRoute && hasWall, "Ballroom has a walkable exit route, fixed walls and quiet spaces");
            game.Player.TapCane();
            float clearLoad = NoiseSystem.Instance.AcousticLoad;
            Check(clearLoad < .5f && NoiseSystem.Instance.ActivePulseCount > 0, "One cane tap reveals the room at low acoustic load");

            fixtures.Add("Injected several coincident cane-strength events near the ballroom patrol to make overlap repeatable.");
            // Keep the real player at the doorway. The artificial sounds give the creature
            // evidence it can pursue without moving the test player or the room's geometry.
            Vector3 origin = listener.transform.position + new Vector3(1.8f, .15f, -.8f);
            for (int i = 0; i < 5; ++i) NoiseSystem.Emit(origin, 1.35f, NoiseKind.Cane);
            Check(NoiseSystem.Instance.AcousticLoad > .75f, "Overlapping real acoustic events raise the bounded interference load");
            yield return WaitFor(() => listener.LastHeardReflection, 2.9f);
            Check(reflections.Exists(noise => noise.EventId > 0 && Vector3.Distance(noise.Position, noise.OriginalPosition) > .2f),
                "A delayed reflection has its own apparent position and keeps its parent event ID");
            Check(listener.LastHeardReflection && Vector3.Distance(listener.LastHeardPosition, listener.LastHeardOrigin) > .2f
                && listener.HearingConfidence < .75f, "Ballroom creature accepts an uncertain apparent location rather than the physical source");
            bool stillHasWall = Physics.Raycast(probe, directions, out var afterWall, 4f, 1, QueryTriggerInteraction.Ignore);
            bool stillHasRoute = FindPath(room.Spawn, room.Exit.position, out var distortedRoute);
            Check(hasWall && stillHasWall && afterWall.collider == wall.collider && Mathf.Abs(afterWall.distance - wall.distance) < .001f,
                "Acoustic interference leaves the probed collision surface unchanged");
            Check(hasRoute && stillHasRoute && SamePath(originalRoute, distortedRoute), "The same fixed navigation route survives maximum interference");
            bool savedReduction = game.ReducedDistortion;
            if (!savedReduction) game.ToggleDistortion();
            yield return null;
            Check(game.ReducedDistortion && Shader.GetGlobalFloat("_HaunterInterference") <= NoiseSystem.Instance.AcousticLoad * .24f,
                "Reduced distortion lowers visual interference without changing the acoustic load");
            if (!savedReduction) game.ToggleDistortion();

            if (room.QuietZones.Length > 0)
            {
                listener.TutorialHold = true;
                Vector3 center = room.QuietZones[0].center;
                center.y = room.Spawn.y;
                game.Player.EnterLevel(center);
                float loaded = NoiseSystem.Instance.AcousticLoad;
                yield return new WaitForSeconds(1f);
                Check(game.Campaign.Quiet && NoiseSystem.Instance.AcousticLoad + .22f < loaded,
                    "Resting in a quiet alcove dissipates interference");
                listener.TutorialHold = false;
            }

            fixtures.Add("Staged beside the ballroom's rear walkway, then physically crossed its exit trigger.");
            if (game.State == SessionState.Playing)
            {
                game.Player.EnterLevel(room.Exit.position + Vector3.back * 1.75f);
                yield return WalkPath(room.Exit.position, true, 6f);
            }
            Check(game.LevelNumber == 4, "Real ballroom exit trigger reaches the two-creature maze");
        }

        IEnumerator TestMaze()
        {
            var room = game.Campaign.Room;
            var guard = game.Creature;
            var roamer = game.Campaign.SecondCreature;
            if (!Check(guard && roamer && guard.Role == ListenerRole.ExitGuard && roamer.Role == ListenerRole.Roamer,
                "Final maze has an exit guard and a separate patrol creature")) yield break;
            Check(FindPath(room.Spawn, room.FinalStaging, out _) && FindPath(room.FinalStaging, room.Exit.position, out _),
                "Maze lanes connect the spawn, throw area and final door");

            fixtures.Add("Staged at the authored paired-throw mark. Both authored props are picked up within normal reach and released at normal throw speed. The escape uses real walking with both catches active.");
            game.Player.EnterLevel(room.FinalStaging);
            yield return null;
            var first = NearestProp(room, null);
            var second = NearestProp(room, first);
            if (!Check(first && second, "Two independent authored pickups are within normal reach of the staging mark")) yield break;

            if (!Check(ThrowToward(first, room.PrimaryLure), "Player releases one real prop toward the west lure")) yield break;
            yield return WaitFor(() => ImpactFrom(first).EventId > 0, 3f);
            NoiseEvent westImpact = ImpactFrom(first);
            int westId = westImpact.EventId;
            LogLure("west", westImpact, guard, roamer);
            bool oneClaim = westId > 0 && roamer.ClaimedNoiseEventId == westId && guard.ClaimedNoiseEventId == 0;
            Check(oneClaim, "The west prop's physical impact is claimed by just the roaming creature");
            if (!oneClaim) yield break;

            NoiseSystem.Emit(room.SecondaryLure, 1.5f, NoiseKind.Impact, first.gameObject, westId);
            Check(guard.ClaimedNoiseEventId == 0 && roamer.ClaimedNoiseEventId == westId,
                "An audible replay with the first throw's ID cannot claim the exit guard");

            if (!Check(ThrowToward(second, room.SecondaryLure), "Player releases a second real prop toward the east lure")) yield break;
            yield return WaitFor(() => ImpactFrom(second).EventId > 0, 3f);
            NoiseEvent eastImpact = ImpactFrom(second);
            int eastId = eastImpact.EventId;
            LogLure("east", eastImpact, guard, roamer);
            Check(eastId > 0 && eastId != westId && guard.ClaimedNoiseEventId == eastId && roamer.ClaimedNoiseEventId == westId,
                "Two distinct throw events can occupy the guard and roamer simultaneously");
            yield return WaitFor(() => guard.IsLingeringAtDistraction && roamer.IsLingeringAtDistraction, 4f);
            float overlap = Mathf.Min(guard.DistractionTimeRemaining, roamer.DistractionTimeRemaining);
            Check(overlap > 2f, "Both monsters have a measurable overlapping investigation window");
            Debug.Log("HAUNTER_EXPANSION_OVERLAP " + overlap.ToString("F2") + " seconds");
            float departed = Time.time;
            yield return WalkPath(room.Exit.position, false, 5f);
            Check(game.State == SessionState.Escaped && Time.time - departed < overlap,
                "The player can walk through the live exit before either investigation expires");
        }

        bool ThrowToward(ThrowableObject prop, Vector3 landing)
        {
            var player = game.Player;
            player.PickUp(prop);
            if (player.HeldObject != prop) return false;
            var camera = player.ViewCamera.transform;
            Vector3 direction = landing - camera.position;
            direction.y = 0f;
            float floorHeight = camera.position.y - landing.y - prop.HoldRadius * .5f;
            float travelTime = Mathf.Sqrt(Mathf.Max(.1f, 2f * floorHeight / -Physics.gravity.y));
            float speed = Mathf.Clamp((direction.magnitude - .7f) / travelTime, 7.2f, 15f);
            Vector3 aim = camera.position + (direction.normalized + Vector3.down * .14f) * 8f;
            player.GuideLookAt(aim, 1f);
            player.ThrowHeld((camera.forward + Vector3.up * .14f).normalized * speed);
            return player.HeldObject != prop;
        }

        ThrowableObject NearestProp(HaunterLevelWorld room, ThrowableObject omit)
        {
            ThrowableObject best = null;
            float distance = game.Player.PickupReach;
            foreach (var item in room.GetComponentsInChildren<ThrowableObject>())
            {
                if (item == omit) continue;
                float candidate = Vector3.Distance(game.Player.ViewCamera.transform.position, item.transform.position);
                if (candidate >= distance) continue;
                best = item;
                distance = candidate;
            }
            return best;
        }

        NoiseEvent ImpactFrom(ThrowableObject item) => impacts.Find(noise => noise.Source == item.gameObject);

        static void LogLure(string side, NoiseEvent noise, ListenerCreature guard, ListenerCreature roamer)
        {
            Debug.Log($"HAUNTER_EXPANSION_LURE {side} event={noise.EventId} at={noise.Position} loudness={noise.Loudness:F2} "
                + $"guard={guard.transform.position}/{guard.ClaimedNoiseEventId} roamer={roamer.transform.position}/{roamer.ClaimedNoiseEventId}");
        }

        IEnumerator WalkPath(Vector3 destination, bool crouch, float seconds)
        {
            var player = game.Player;
            if (!FindPath(player.transform.position, destination, out var path)) yield break;
            int level = game.LevelNumber;
            float until = Time.time + seconds;
            foreach (Vector3 corner in path.corners)
            {
                while (game.State == SessionState.Playing && game.LevelNumber == level && Time.time < until)
                {
                    Vector3 delta = corner - player.transform.position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= .13f * .13f) break;
                    Vector3 direction = player.transform.InverseTransformDirection(delta.normalized);
                    player.ApplyMovement(new Vector2(direction.x, direction.z), crouch, false);
                    yield return null;
                }
            }
            yield return null;
        }

        static bool FindPath(Vector3 from, Vector3 to, out NavMeshPath path)
        {
            path = new NavMeshPath();
            return NavMesh.SamplePosition(from, out var start, 1.2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(to, out var end, 1.2f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
        }

        static bool SamePath(NavMeshPath a, NavMeshPath b)
        {
            Vector3[] first = a.corners, second = b.corners;
            if (first.Length != second.Length) return false;
            for (int i = 0; i < first.Length; ++i)
                if (Vector3.Distance(first[i], second[i]) > .001f) return false;
            return true;
        }

        static bool NearPatrol(ListenerCreature listener, Vector3[] route)
        {
            foreach (var point in route) if (Vector3.Distance(listener.transform.position, point) < 1.4f) return true;
            return false;
        }

        static IEnumerator WaitFor(Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds * 2f;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }

        bool Check(bool condition, string name)
        {
            (condition ? passed : failed).Add(name);
            Debug.Log((condition ? "HAUNTER_EXPANSION_PASS " : "HAUNTER_EXPANSION_FAIL ") + name);
            return condition;
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            string report = JsonUtility.ToJson(new Report
            {
                passed = passed.ToArray(), failed = failed.ToArray(), errors = errors.ToArray(), fixtures = fixtures.ToArray()
            }, true);
#if UNITY_WEBGL && !UNITY_EDITOR
            BrowserBridge.RecordSmokeReport(report);
#else
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ExpansionEvidence");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "expansion-report.json"), report);
#endif
            Debug.Log("HAUNTER_EXPANSION_FINISHED passed=" + passed.Count + " failed=" + failed.Count + " errors=" + errors.Count);
#if !UNITY_WEBGL || UNITY_EDITOR
            Application.Quit(failed.Count == 0 && errors.Count == 0 ? 0 : 1);
#endif
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= Log;
            NoiseSystem.Emitted -= OnEmission;
            NoiseSystem.Heard -= OnHearing;
        }
    }
}
