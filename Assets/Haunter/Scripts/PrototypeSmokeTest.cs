using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace Haunter
{
    /// <summary>Opt-in integration checks in the actual player, including rendered evidence.</summary>
    public sealed class PrototypeSmokeTest : MonoBehaviour
    {
        [Serializable] class Report { public string[] passed; public string[] failed; public string[] errors; }
        readonly List<string> passed = new List<string>();
        readonly List<string> failed = new List<string>();
        readonly List<string> errors = new List<string>();
        string evidence;
        int impacts;
        int measuredPixels;
        int coloredPixels;
        bool captured;
        bool finished;
        float startedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!BrowserBridge.SmokeRequested) return;
#else
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--haunter-smoke") < 0) return;
#endif
            if (FindFirstObjectByType<PrototypeSmokeTest>()) return;
            DontDestroyOnLoad(new GameObject("Prototype verification").AddComponent<PrototypeSmokeTest>().gameObject);
        }

        void Awake()
        {
            startedAt = Time.realtimeSinceStartup;
#if !UNITY_WEBGL || UNITY_EDITOR
            evidence = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Evidence");
            Directory.CreateDirectory(evidence);
#endif
            Application.logMessageReceived += OnLog;
        }

        void Update()
        {
            if (!finished && Time.realtimeSinceStartup - startedAt > 90f)
            {
                Check(false, "Integration run timed out (possible pause, unexpected catch or coroutine exception)");
                Finish();
            }
        }

        void OnLog(string condition, string trace, LogType kind)
        {
            if (kind == LogType.Exception || kind == LogType.Error || kind == LogType.Assert)
                errors.Add(condition + "\n" + trace);
        }

        void Check(bool condition, string name)
        {
            (condition ? passed : failed).Add(name);
            Debug.Log((condition ? "HAUNTER_PASS " : "HAUNTER_FAIL ") + name);
        }

        IEnumerator Start()
        {
            yield return null;
            var session = GameSession.Instance;
            Check(session && session.Player && session.Creature && session.Exit, "Scene references initialized");
            if (!session || !session.Player || !session.Creature || !session.Exit) { Finish(); yield break; }
            session.Player.AcceptInput = false;
            var creature = session.Creature;
            var creatureAgent = creature.GetComponent<NavMeshAgent>();
            // The more sensitive guard must not hunt during unrelated rendering/physics checks.
            // Leave navigation available; disabling the listener unsubscribes it from hearing.
            creature.enabled = false;
            if (creatureAgent.isOnNavMesh) creatureAgent.isStopped = true;
            Check(session.State == SessionState.Ready, "Title screen starts paused");
            yield return Capture("01-title.png");
            Check(measuredPixels > 100, "Title UI is rendered in the player");
            session.Begin();
            NoiseSystem.Heard += TraceNoise;
            yield return new WaitForSeconds(.2f);
            Check(session.State == SessionState.Playing, "Begin enters gameplay");
            var world = FindFirstObjectByType<PrototypeWorld>();
            Check(world && world.NavMeshReady, "Runtime navigation mesh built");
            var path = new NavMeshPath();
            bool reachable = NavMesh.SamplePosition(session.Player.transform.position, out var start, 2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(session.Exit.position, out var end, 2f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
            Check(reachable, "Exit reachable through authored room");
            yield return new WaitForSeconds(5f);
            yield return Capture("02-darkness.png");
            int darkPixels = measuredPixels;
            session.Player.TapCane();
            yield return new WaitForSeconds(.55f);
            yield return Capture("03-cane-ripple.png");
            Check(captured && measuredPixels > darkPixels + 150, "Cane wave visibly reveals monochrome room edges");
            Check(coloredPixels == 0, "Rendered room contains only monochrome pixels");
            Check(NoiseSystem.Instance.ActivePulseCount > 0, "Sound creates active shader pulses");

            var items = FindObjectsByType<ThrowableObject>(FindObjectsSortMode.None);
            Check(items.Length >= 20, "Environment contains at least twenty throwable props");
            if (items.Length < 2) { Finish(); yield break; }
            var prop = items[0];
            session.Player.PickUp(prop);
            yield return null;
            Check(prop.IsHeld && prop.Body.isKinematic && session.Player.HeldObject == prop, "Pickup attaches physical object to hand");
            var renderer = prop.GetComponentInChildren<Renderer>();
            if (!renderer) { Check(false, "Throwable renderer exists"); Finish(); yield break; }
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Check(block.GetFloat("_AlwaysVisible") > .5f, "Held object receives persistent outline");
            yield return Capture("04-held-object.png");
            var carryController = session.Player.GetComponent<CharacterController>();
            Vector3 originalPosition = session.Player.transform.position;
            Quaternion originalRotation = session.Player.transform.rotation;
            carryController.enabled = false;
            session.Player.transform.SetPositionAndRotation(new Vector3(0, .03f, -14.6f), Quaternion.Euler(0, 180, 0));
            carryController.enabled = true;
            yield return new WaitForSeconds(1.2f);
            yield return Capture("04b-held-near-wall.png");
            Check(prop.transform.lossyScale.x < .8f, "Held viewmodel retracts in front of nearby walls");
            Check(CountWorldPixels(session.Player.ViewCamera) > 200, "Held outline remains visibly rendered against a nearby wall");
            Debug.Log("HAUNTER_HAND camera=" + session.Player.ViewCamera.transform.position + " forward=" + session.Player.ViewCamera.transform.forward
                + " prop=" + prop.transform.position + " scale=" + prop.transform.lossyScale + " radius=" + prop.HoldRadius);
            carryController.enabled = false;
            session.Player.transform.SetPositionAndRotation(originalPosition, originalRotation);
            carryController.enabled = true;
            yield return new WaitForSeconds(.3f);
            NoiseSystem.Heard += CountImpact;
            session.Player.ThrowHeld((session.Player.ViewCamera.transform.forward + Vector3.up * .2f).normalized * 11f);
            Check(session.Player.HeldObject == null, "Throw clears the player's hand state");
            Check(!prop.IsHeld && !prop.Body.isKinematic, "Throw restores physics");
            Check(Mathf.Abs(prop.transform.lossyScale.x - 1f) < .01f, "Throw restores original physical prop scale");
            renderer.GetPropertyBlock(block);
            Check(block.GetFloat("_AlwaysVisible") < .5f, "Thrown object returns to sound-only visibility");
            yield return new WaitForSeconds(3.5f);
            Check(impacts > 0, "Thrown prop collision produces an audible impact");
            NoiseSystem.Heard -= CountImpact;
            session.Player.enabled = false; // Isolate AI assertions from live keyboard/controller input.
            foreach (var item in items)
            {
                if (!item.Body.isKinematic)
                {
                    item.Body.velocity = Vector3.zero;
                    item.Body.angularVelocity = Vector3.zero;
                }
                item.Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                item.Body.isKinematic = true;
            }
            MovePlayer(session.Player, new Vector3(9.5f, .03f, -13.5f));
            creature.enabled = true;
            yield return null;
            Check(creature.ChaseSpeed > session.Player.SprintSpeed, "Creature chase speed exceeds player sprint speed");
            Check(Vector3.Distance(creature.GuardPosition, session.Exit.position) <= 3f,
                "Creature guard position stays near the exit");
            Check(Vector3.Distance(creature.transform.position, creature.GuardPosition) < .8f,
                "Creature begins on its exit guard position");
            Check(creature.HearingRange(NoiseKind.Footstep, .48f) > 18f
                && creature.HearingRange(NoiseKind.Footstep, .48f) < 19f
                && Mathf.Abs(creature.HearingRange(NoiseKind.Footstep, .25f) - 5f) < .1f
                && creature.HearingRange(NoiseKind.Footstep, .9f) <= 28.1f,
                "Walking, crouching and sprint hearing are reduced to about eighteen, five and twenty-eight metres");
            Check(creature.HearingRange(NoiseKind.Cane, .9f) < 13f
                && creature.HearingRange(NoiseKind.Impact, 1.5f) <= 8.1f,
                "Cane and object hearing are reduced as well");

            Vector3 guardTarget = creature.TargetPosition;
            NoiseSystem.Emit(creature.GuardPosition + Vector3.back * 25f, .48f, NoiseKind.Footstep, session.Player.gameObject);
            NoiseSystem.Emit(creature.GuardPosition + Vector3.back * 9f, .25f, NoiseKind.Footstep, session.Player.gameObject);
            NoiseSystem.Emit(creature.GuardPosition + Vector3.back * 20f, .9f, NoiseKind.Cane, session.Player.gameObject);
            NoiseSystem.Emit(creature.GuardPosition + Vector3.right * 9f, 1.5f, NoiseKind.Impact, prop.gameObject);
            Check(!creature.IsInvestigating && Vector3.Distance(guardTarget, creature.TargetPosition) < .01f,
                "Previously audible distant footsteps, cane taps and impacts no longer attract the guard");

            Vector3 walkingTarget = creature.GuardPosition + Vector3.back * 15f;
            NoiseSystem.Emit(walkingTarget, .48f, NoiseKind.Footstep, session.Player.gameObject);
            Check(creature.IsInvestigating && !creature.IsCommittedToDistraction
                && Vector3.Distance(creature.TargetPosition, walkingTarget) < .1f,
                "Ordinary footsteps still trigger a player pursuit at fifteen metres");
            int initialPawsteps = creature.TotalPawsteps;
            Vector3 decoy = creature.GuardPosition + Vector3.right * 7.5f;
            NoiseSystem.Emit(decoy, 1.5f, NoiseKind.Impact, prop.gameObject);
            Check(creature.IsCommittedToDistraction && Vector3.Distance(creature.TargetPosition, decoy) < .1f,
                "A thrown impact immediately diverts a fresh player pursuit");
            Check(creatureAgent.speed > session.Player.SprintSpeed, "Live navigation chase speed exceeds player sprint speed");
            Check(!creature.IsLingeringAtDistraction, "Distraction linger does not begin before reaching the impact");
            float arrivalDeadline = Time.time + 12f;
            float peakSpeed = 0f;
            float nextNoise = Time.time;
            bool travelTargetStable = true;
            while (!creature.IsLingeringAtDistraction && Time.time < arrivalDeadline && session.State == SessionState.Playing)
            {
                peakSpeed = Mathf.Max(peakSpeed, creatureAgent.velocity.magnitude);
                if (Time.time >= nextNoise)
                {
                    DisturbCommittedCreature(creature, session.Player.gameObject, prop.gameObject, items[1].gameObject);
                    nextNoise = Time.time + .3f;
                }
                travelTargetStable &= creature.IsCommittedToDistraction && Vector3.Distance(creature.TargetPosition, decoy) < .01f;
                yield return null;
            }
            Check(peakSpeed > session.Player.SprintSpeed, "Creature actually moves faster than a sprinting player");
            Check(travelTargetStable, "Player sounds, other objects and repeated bounces cannot redirect an approaching distraction");
            Check(creature.IsLingeringAtDistraction && PlayerController.HorizontalDistance(creature.transform.position, decoy) < .7f,
                "Creature reaches the distant impact instead of stopping after a few pawsteps");
            Check(Mathf.Abs(creature.DistractionLingerSeconds - 5f) < .1f && creature.DistractionTimeRemaining >= 4.8f,
                "A full five-second distraction timer begins on arrival");
            float lingerStarted = Time.time;
            Vector3 lingerPosition = creature.transform.position;
            bool lingerStable = true;
            float maxLingerDrift = 0f;
            float maxTargetDelta = 0f;
            bool didLoseLingerState = false;
            nextNoise = Time.time;
            while (Time.time - lingerStarted < 4.5f && session.State == SessionState.Playing)
            {
                if (Time.time >= nextNoise)
                {
                    DisturbCommittedCreature(creature, session.Player.gameObject, prop.gameObject, items[1].gameObject);
                    nextNoise = Time.time + .3f;
                }
                lingerStable &= creature.IsLingeringAtDistraction && creature.IsCommittedToDistraction
                    && Vector3.Distance(creature.TargetPosition, decoy) < .01f
                    && Vector3.Distance(creature.transform.position, lingerPosition) < .2f;
                maxLingerDrift = Mathf.Max(maxLingerDrift, Vector3.Distance(creature.transform.position, lingerPosition));
                maxTargetDelta = Mathf.Max(maxTargetDelta, Vector3.Distance(creature.TargetPosition, decoy));
                didLoseLingerState |= !creature.IsLingeringAtDistraction || !creature.IsCommittedToDistraction;
                yield return null;
            }
            Debug.Log("HAUNTER_LINGER maxDrift=" + maxLingerDrift.ToString("F4") + " maxTargetDelta=" + maxTargetDelta.ToString("F4")
                + " lostState=" + didLoseLingerState + " remaining=" + creature.DistractionTimeRemaining.ToString("F3")
                + " elapsed=" + (Time.time - lingerStarted).ToString("F3") + " start=" + lingerPosition
                + " end=" + creature.transform.position + " velocity=" + creatureAgent.velocity);
            Check(lingerStable, "Creature remains at the impact while player, cane and other impact noises occur");
            Check(creature.DistractionTimeRemaining < .75f, "Repeated noises do not restart or extend the five-second timer");
            bool returnedFromDecoy = false;
            bool checkedReturnTime = false;
            float guardDeadline = Time.time + 7f;
            while (!creature.IsGuardingExit && Time.time < guardDeadline && session.State == SessionState.Playing)
            {
                returnedFromDecoy |= creature.IsReturningToExit;
                if (creature.IsReturningToExit && !checkedReturnTime)
                {
                    checkedReturnTime = true;
                    Check(Time.time - lingerStarted >= 4.9f && Time.time - lingerStarted < 5.5f,
                        "Return begins only after five seconds at the distraction");
                }
                yield return null;
            }
            Check(returnedFromDecoy && !creature.IsCommittedToDistraction && creature.IsGuardingExit
                && Vector3.Distance(creature.transform.position, creature.GuardPosition) < .8f,
                "After the complete linger the creature returns to guard the exit");

            Vector3 crouchingTarget = creature.transform.position + Vector3.back * 4f;
            NoiseSystem.Emit(crouchingTarget, .25f, NoiseKind.Footstep, session.Player.gameObject);
            Check(creature.IsInvestigating && Vector3.Distance(creature.TargetPosition, crouchingTarget) < .1f,
                "Nearby crouched footsteps still trigger investigation after the distraction ends");
            Vector3 previousTarget = creature.TargetPosition;
            int previousSteps = creature.RemainingSteps;
            NoiseSystem.Emit(creature.transform.position + Vector3.right * 2f, 1.5f, NoiseKind.Creature, creature.gameObject);
            Check(Vector3.Distance(previousTarget, creature.TargetPosition) < .01f && previousSteps == creature.RemainingSteps,
                "Creature ignores its own paw sounds");
            MovePlayer(session.Player, new Vector3(-9.5f, .03f, -13.5f));
            yield return new WaitForSeconds(.2f);
            Check(Vector3.Distance(creature.TargetPosition, crouchingTarget) < .1f,
                "Creature follows the heard position without tracking a silent player");
            bool returnedFromPlayer = false;
            guardDeadline = Time.time + 15f;
            while (!creature.IsGuardingExit && Time.time < guardDeadline && session.State == SessionState.Playing)
            {
                returnedFromPlayer |= creature.IsReturningToExit;
                yield return null;
            }
            Check(!creature.IsInvestigating && creature.RemainingSteps == 0, "Player investigation still has a finite budget");
            Check(returnedFromPlayer && creature.IsGuardingExit
                && Vector3.Distance(creature.transform.position, session.Exit.position) <= 3f,
                "After losing the player the creature returns to the exit");
            Check(creature.TotalPawsteps > initialPawsteps, "Investigation produces physical pawstep echoes");

            if (session.State != SessionState.Playing)
            {
                Check(false, "AI fixture leaves the silent player safely outside the pursuit path");
                Finish();
                yield break;
            }

            // Frame the creature for visual inspection without changing its normal gameplay visibility.
            var view = session.Player.ViewCamera;
            session.Player.enabled = false;
            Vector3 cameraPosition = CreatureVantage(creature.transform.position);
            view.transform.position = cameraPosition;
            view.transform.LookAt(creature.transform.position + Vector3.up * .7f);
            NoiseSystem.Emit(creature.transform.position + Vector3.up * .05f, .8f, NoiseKind.Creature, creature.gameObject);
            yield return new WaitForSeconds(.35f);
            yield return Capture("05-creature-echo.png");
            Check(CountWorldPixels(view) > 200, "Creature echo evidence is rendered from an unobstructed camera");
            var controller = session.Player.GetComponent<CharacterController>();
            controller.enabled = false;
            session.Player.transform.position = creature.transform.position + creature.transform.forward * .7f;
            controller.enabled = true;
            yield return null;
            yield return null;
            Check(session.State == SessionState.Caught, "Catch produces loss state");
            yield return Capture("06-caught.png");
            session.Restart();
            yield return new WaitForSecondsRealtime(.8f);
            session = GameSession.Instance;
            Check(session && session.State == SessionState.Ready, "Restart restores clean title state");
            if (!session) { Finish(); yield break; }
            session.Begin();
            session.Player.AcceptInput = false;
            yield return null;
            controller = session.Player.GetComponent<CharacterController>();
            session.Player.enabled = false;
            controller.height = 1.15f;
            controller.center = Vector3.up * .575f;
            session.Player.ViewCamera.transform.localPosition = Vector3.up * 1.02f;
            MovePlayer(session.Player, new Vector3(0, .03f, 6f));
            creature = session.Creature;
            var newProp = FindFirstObjectByType<ThrowableObject>();
            NoiseSystem.Emit(creature.GuardPosition + Vector3.right * 7f, 1.5f, NoiseKind.Impact, newProp.gameObject);
            Check(creature.IsCommittedToDistraction, "A doorway distraction creates a real opportunity to sneak past");
            float sneakDeadline = Time.time + 8f;
            float sneakDistance = 0f;
            int sneakFootsteps = 0;
            bool stayedCommitted = true;
            while (session.State == SessionState.Playing && Time.time < sneakDeadline)
            {
                Vector3 oldPosition = session.Player.transform.position;
                controller.Move((Vector3.forward * session.Player.CrouchSpeed + Vector3.down * 2f) * Time.deltaTime);
                Vector3 displacement = session.Player.transform.position - oldPosition;
                displacement.y = 0;
                sneakDistance += displacement.magnitude;
                if (sneakDistance >= 1f)
                {
                    sneakDistance %= 1f;
                    sneakFootsteps++;
                    NoiseSystem.Emit(session.Player.transform.position + Vector3.up * .04f, .25f,
                        NoiseKind.Footstep, session.Player.gameObject);
                }
                stayedCommitted &= creature.IsCommittedToDistraction;
                yield return null;
            }
            Check(session.State == SessionState.Escaped, "Crouch-speed movement reaches the physical exit during the distraction");
            Check(stayedCommitted && sneakFootsteps >= 5 && creature.IsCommittedToDistraction,
                "Creature stays distracted through the player's audible sneak-past escape");
            yield return Capture("07-escaped.png");
            Check(errors.Count == 0, "No Unity runtime errors or exceptions");
            Finish();
        }

        void CountImpact(NoiseEvent noise) { if (noise.Kind == NoiseKind.Impact) impacts++; }

        static void DisturbCommittedCreature(ListenerCreature creature, GameObject player, GameObject originalProp, GameObject otherProp)
        {
            Vector3 position = creature.transform.position;
            NoiseSystem.Emit(position + Vector3.back * 2f, .9f, NoiseKind.Footstep, player);
            NoiseSystem.Emit(position + Vector3.left * 2f, .9f, NoiseKind.Cane, player);
            NoiseSystem.Emit(position + Vector3.forward * 2f, 1.5f, NoiseKind.Impact, otherProp);
            NoiseSystem.Emit(creature.TargetPosition + Vector3.right * .5f, 1.5f, NoiseKind.Impact, originalProp);
        }

        static void MovePlayer(PlayerController player, Vector3 position)
        {
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        static Vector3 CreatureVantage(Vector3 creaturePosition)
        {
            Vector3 focus = creaturePosition + Vector3.up * .7f;
            foreach (float radius in new[] { 4.5f, 3f, 2f })
                for (int i = 0; i < 16; i++)
                {
                    float angle = (225f + i * 22.5f) * Mathf.Deg2Rad;
                    Vector3 candidate = creaturePosition + new Vector3(Mathf.Sin(angle) * radius, 1.55f, Mathf.Cos(angle) * radius);
                    if (Physics.CheckSphere(candidate, .25f, 1, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.Linecast(candidate, focus, 1, QueryTriggerInteraction.Ignore)) continue;
                    return candidate;
                }
            return creaturePosition + Vector3.up * 3;
        }

        static int CountWorldPixels(Camera camera)
        {
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = RenderTexture.GetTemporary(640, 360, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(640, 360, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
            pixels.Apply();
            int count = 0;
            foreach (var pixel in pixels.GetPixels32()) if (pixel.r > 60) count++;
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            Destroy(pixels);
            return count;
        }

        void TraceNoise(NoiseEvent noise)
        {
            if (noise.Kind != NoiseKind.Creature && noise.Kind != NoiseKind.Intro)
                Debug.Log("HAUNTER_NOISE t=" + Time.time.ToString("F2") + " kind=" + noise.Kind + " at=" + noise.Position
                    + " loudness=" + noise.Loudness.ToString("F2") + " source=" + (noise.Source ? noise.Source.name : "test"));
        }

        IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            captured = screenshot != null;
            measuredPixels = 0;
            coloredPixels = 0;
            if (screenshot)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                BrowserBridge.RecordScreenshot(filename, Convert.ToBase64String(screenshot.EncodeToPNG()));
#else
                File.WriteAllBytes(Path.Combine(evidence, filename), screenshot.EncodeToPNG());
#endif
                Color32[] pixels = screenshot.GetPixels32();
                int w = screenshot.width, h = screenshot.height;
                // Ignore UI and viewmodel at the right/bottom; count only the room ahead.
                for (int y = h / 4; y < h * 4 / 5; y++)
                    for (int x = w / 8; x < w * 3 / 4; x++)
                    {
                        Color32 pixel = pixels[y * w + x];
                        if (pixel.r > 40) measuredPixels++;
                        if (Mathf.Abs(pixel.r - pixel.g) > 2 || Mathf.Abs(pixel.g - pixel.b) > 2) coloredPixels++;
                    }
                Destroy(screenshot);
            }
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
            File.WriteAllText(Path.Combine(evidence, "smoke-report.json"), report);
#endif
            Debug.Log("HAUNTER_SMOKE_FINISHED passed=" + passed.Count + " failed=" + failed.Count + " errors=" + errors.Count);
#if !UNITY_WEBGL || UNITY_EDITOR
            Application.Quit(failed.Count == 0 && errors.Count == 0 ? 0 : 1);
#endif
        }

        void OnDestroy() { Application.logMessageReceived -= OnLog; NoiseSystem.Heard -= CountImpact; NoiseSystem.Heard -= TraceNoise; }
    }
}
