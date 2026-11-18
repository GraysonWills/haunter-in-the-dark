using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Haunter
{
    public enum ListenerRole { ExitGuard, Roamer }

    /// <summary>Acts on audible evidence and returns to its assigned route or exit.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class ListenerCreature : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private Transform torso;
        [SerializeField] private Transform head;
        [SerializeField] private Transform[] upperLegs = new Transform[4];
        [SerializeField] private Transform[] lowerLegs = new Transform[4];
        [SerializeField] private Transform[] paws = new Transform[4];
        [SerializeField, Range(.1f, 1f)] private float pawInterval = .22f;
        [SerializeField, Range(1f, 10f)] private float movementSpeed = 5.8f;
        [SerializeField, Range(1f, 10f)] private float returnSpeed = 4.8f;
        [SerializeField] private float movementHearingScale = 80f;
        [SerializeField] private float caneHearingMultiplier = 14f;
        [SerializeField] private float impactHearingMultiplier = 6f;
        [SerializeField, Range(1f, 12f)] private float objectListenSeconds = 5f;
        [SerializeField] private Vector3 guardOffset = new Vector3(0, 0, -2.2f);
        public int RemainingSteps { get; private set; }
        public Vector3 TargetPosition { get; private set; }
        public Vector3 GuardPosition { get; private set; }
        public float ChaseSpeed => movementSpeed;
        public bool IsInvestigating => mode == Behavior.Investigating || IsCommittedToDistraction;
        public bool IsCommittedToDistraction => mode == Behavior.DistractionApproach || mode == Behavior.DistractionLinger;
        public bool IsLingeringAtDistraction => mode == Behavior.DistractionLinger;
        public float DistractionTimeRemaining => IsLingeringAtDistraction ? Mathf.Max(0, listenRemaining) : 0;
        public float DistractionLingerSeconds => objectListenSeconds;
        public bool IsReturningToExit => Role == ListenerRole.ExitGuard && mode == Behavior.Returning;
        public bool IsGuardingExit => Role == ListenerRole.ExitGuard && mode == Behavior.Guarding;
        public int TotalPawsteps { get; private set; }
        public bool TutorialHold { get; set; }
        public bool TutorialHearingSuppressed { get; set; }
        public GameObject TutorialImpactSource { get; set; }
        public bool CatchSuppressed { get; set; }
        public ListenerRole Role { get; private set; }
        public int ClaimedNoiseEventId { get; private set; }
        public Vector3 LastHeardPosition { get; private set; }
        public Vector3 LastHeardOrigin { get; private set; }
        public bool LastHeardReflection { get; private set; }
        public bool HasHeardSound { get; private set; }
        public float HearingConfidence => heardConfidence * Mathf.Clamp01(1f - (Time.time - heardAt) / 7f);
        public int PatrolWaypointIndex { get; private set; } = -1;
        public string BehaviorName => approachListening > 0f ? "Listening" :
            mode == Behavior.Returning ? (Role == ListenerRole.Roamer ? "ReturningToPatrol" : "ReturningToExit") : mode.ToString();
        public float TravelRemaining
        {
            get
            {
                if (agent == null || !agent.isOnNavMesh || !agent.hasPath) return 0;
                float distance = agent.remainingDistance;
                return float.IsInfinity(distance) || float.IsNaN(distance) ? 0 : distance;
            }
        }

        enum Behavior { Guarding, Patrolling, Investigating, DistractionApproach, DistractionLinger, Returning }
        Behavior mode;
        Vector3 distractionDestination;
        float distractionTravelDeadline;
        float listenRemaining;
        float stalledTime;
        float approachListening;
        float heardAt;
        float heardConfidence;
        float patrolRetryAt;
        bool navigationStarted;
        bool scriptedInspection;
        bool movedAroundDebris;
        Quaternion inspectionFacing;
        Vector3 returnDestination;
        Vector3? explicitGuardAnchor;
        Vector3[] patrol = Array.Empty<Vector3>();

        const int RememberedEvents = 256;
        const float PatrolSpeed = 2.45f;
        const float RouteRetryDelay = .65f;
        static readonly List<ListenerCreature> listeners = new List<ListenerCreature>(2);
        static readonly HashSet<int> resolvedEvents = new HashSet<int>();
        static readonly Queue<int> recentEvents = new Queue<int>(RememberedEvents);

        private NavMeshAgent agent;
        private float pawClock;
        private float gaitClock;
        private float idleClock;
        private int nextPaw;
        private readonly int[] pawOrder = { 0, 3, 1, 2 };
        private float PawPeriod => pawInterval * (Role == ListenerRole.Roamer ? 1.23f : 1f);
        private static readonly Vector3[] SoundSearchOffsets =
        {
            Vector3.zero, Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            new Vector3(1.5f, 0, 1.5f), new Vector3(-1.5f, 0, 1.5f),
            new Vector3(1.5f, 0, -1.5f), new Vector3(-1.5f, 0, -1.5f),
            Vector3.forward * 2.2f, Vector3.back * 2.2f, Vector3.left * 2.2f, Vector3.right * 2.2f
        };
        private static readonly Vector3[] HipPoints =
        {
            new Vector3(-.43f, 1.04f, .59f), new Vector3(.43f, 1.04f, .59f),
            new Vector3(-.40f, 1.03f, -.57f), new Vector3(.40f, 1.03f, -.57f)
        };

        public void Initialize(Transform playerTransform, Material material)
        {
            player = playerTransform;
            ConfigureAgent();
            // The saved scene enables navigation only after the runtime room bake.
            agent.enabled = false;
            if (torso != null) return;
            torso = Make("Long ribcage", PrimitiveType.Capsule, new Vector3(0, 1.09f, 0), new Vector3(.64f, .92f, .66f), material);
            torso.localRotation = Quaternion.Euler(90, 0, 0);
            Make("Shoulders", PrimitiveType.Sphere, new Vector3(0, 1.12f, .55f), new Vector3(.9f, .65f, .66f), material);
            Make("Haunches", PrimitiveType.Sphere, new Vector3(0, 1.08f, -.57f), new Vector3(.8f, .6f, .66f), material);
            head = Make("Blind head", PrimitiveType.Sphere, new Vector3(0, 1.1f, 1.0f), new Vector3(.53f, .44f, .7f), material);
            Make("Long muzzle", PrimitiveType.Capsule, new Vector3(0, .94f, 1.25f), new Vector3(.32f, .25f, .35f), material).localRotation = Quaternion.Euler(90, 0, 0);
            Make("Lower jaw", PrimitiveType.Cube, new Vector3(0, .81f, 1.20f), new Vector3(.27f, .07f, .53f), material);
            for (int side = -1; side <= 1; side += 2)
            {
                var ear = Make("Listening horn", PrimitiveType.Capsule, new Vector3(side * .24f, 1.43f, .88f), new Vector3(.085f, .27f, .09f), material);
                ear.localRotation = Quaternion.Euler(-18, 0, side * -27);
                for (int tooth = 0; tooth < 3; tooth++)
                    Make("Tooth", PrimitiveType.Cube, new Vector3(side * .12f, .85f, 1.07f + tooth * .13f), new Vector3(.035f, .1f, .035f), material);
            }
            for (int i = 0; i < 4; i++)
            {
                upperLegs[i] = Make("Upper leg " + (i + 1), PrimitiveType.Capsule, Vector3.zero, Vector3.one, material);
                lowerLegs[i] = Make("Lower leg " + (i + 1), PrimitiveType.Capsule, Vector3.zero, Vector3.one, material);
                paws[i] = Make("Paw " + (i + 1), PrimitiveType.Sphere, Vector3.zero, new Vector3(.29f, .16f, .43f), material);
            }
            PrototypeWorld.Rod("Tail base", transform, new Vector3(0, 1.12f, -.8f), new Vector3(0, .9f, -1.5f), .08f, material, 10);
            PrototypeWorld.Rod("Tail tip", transform, new Vector3(0, .9f, -1.5f), new Vector3(.22f, .52f, -1.9f), .045f, material, 10);
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = .52f;
            capsule.height = 1.45f;
            capsule.center = new Vector3(0, .8f, 0);
            var body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            AnimateLegs(0);
        }

        private Transform Make(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var obj = PrototypeWorld.Primitive(name, type, transform, position, scale, material, false);
            obj.layer = 10;
            return obj.transform;
        }

        private void ConfigureAgent()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.radius = .55f;
            agent.height = 1.6f;
            agent.baseOffset = 0;
            agent.speed = movementSpeed;
            agent.acceleration = 24;
            agent.angularSpeed = 0;
            agent.updateRotation = false;
            agent.stoppingDistance = .4f;
            agent.autoBraking = true;
            agent.avoidancePriority = 30;
        }

        private void Awake() { ConfigureAgent(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHearing()
        {
            NoiseSystem.Heard -= DispatchNoise;
            listeners.Clear();
            resolvedEvents.Clear();
            recentEvents.Clear();
        }

        private void OnEnable()
        {
            if (listeners.Count == 0) NoiseSystem.Heard += DispatchNoise;
            listeners.Add(this);
        }

        private void OnDisable()
        {
            listeners.Remove(this);
            ClaimedNoiseEventId = 0;
            if (listeners.Count == 0) ResetHearing();
        }

        public void ConfigurePatrol(Vector3[] waypoints)
        {
            if (waypoints == null || waypoints.Length < 2)
                throw new ArgumentException("A patrol requires at least two waypoints.", nameof(waypoints));
            patrol = (Vector3[])waypoints.Clone();
            PatrolWaypointIndex = -1;
            Role = ListenerRole.Roamer;
            if (navigationStarted && agent.isOnNavMesh) ReturnToRole();
        }

        public void ConfigureGuard(Vector3 anchor)
        {
            Role = ListenerRole.ExitGuard;
            explicitGuardAnchor = anchor;
            patrol = Array.Empty<Vector3>();
            PatrolWaypointIndex = -1;
            if (navigationStarted && agent.isOnNavMesh)
            {
                UpdateGuardAnchor();
                ReturnToRole();
            }
        }

        private void Start()
        {
            SetNavigationActive(true);
            TargetPosition = transform.position;
            UpdateGuardAnchor();
            navigationStarted = true;
            mode = Behavior.Guarding;
            idleClock = 4f;
            if (Role == ListenerRole.Roamer && agent.isOnNavMesh) ReturnToRole();
        }

        public void SetNavigationActive(bool active)
        {
            if (!active)
            {
                if (agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                }
                agent.enabled = false;
                return;
            }
            if (!NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas))
            {
                agent.enabled = false;
                return;
            }
            transform.position = hit.position;
            agent.enabled = true;
            if (!agent.Warp(hit.position)) return;
            agent.isStopped = true;
            if (navigationStarted)
            {
                UpdateGuardAnchor();
                ReturnToRole();
            }
        }

        void UpdateGuardAnchor()
        {
            var exit = GameSession.Instance != null ? GameSession.Instance.Exit : null;
            Vector3 desiredGuard = explicitGuardAnchor ?? (exit != null ? exit.position + guardOffset : transform.position);
            GuardPosition = NavMesh.SamplePosition(desiredGuard, out var hit, 2f, NavMesh.AllAreas)
                ? hit.position : transform.position;
        }

        public float HearingRange(NoiseKind kind, float loudness)
        {
            loudness = Mathf.Max(0, loudness);
            if (kind == NoiseKind.Footstep) return Mathf.Clamp(loudness * loudness * movementHearingScale, 2f, 28f);
            if (kind == NoiseKind.Cane) return Mathf.Min(16f, loudness * caneHearingMultiplier);
            if (kind == NoiseKind.Impact) return Mathf.Min(8f, loudness * impactHearingMultiplier);
            return 0f;
        }

        public void CueTutorialPaw()
        {
            Pawfall(pawOrder[nextPaw++ % 4], .62f);
        }

        public bool BeginTutorialPursuit(Vector3 target)
        {
            if (agent == null || !agent.isOnNavMesh || !TrySoundPath(target, out var path)) return false;
            if (!agent.SetPath(path)) return false;
            TargetPosition = target;
            mode = Behavior.Investigating;
            RemainingSteps = 100;
            listenRemaining = 0;
            approachListening = 0;
            ClaimedNoiseEventId = 0;
            pawClock = 0;
            stalledTime = 0;
            idleClock = 0;
            agent.speed = movementSpeed;
            agent.isStopped = false;
            return true;
        }

        public bool BeginScriptedInvestigation(Vector3 target, bool charge = true)
        {
            if (agent == null || !agent.isOnNavMesh || !TrySoundPath(target, out var path)) return false;
            var evidence = new NoiseEvent { Position = target, OriginalPosition = target, Confidence = 1f, Kind = NoiseKind.Impact };
            return AcceptNoise(evidence, path, charge ? movementSpeed : PatrolSpeed, scripted: true);
        }

        public void ReleaseTutorialControl(bool returnToExit)
        {
            TutorialHold = false;
            TutorialHearingSuppressed = false;
            TutorialImpactSource = null;
            if (returnToExit && agent != null && agent.isOnNavMesh) ReturnToRole();
        }

        static void DispatchNoise(NoiseEvent noise)
        {
            if (GameSession.Instance == null || GameSession.Instance.State != SessionState.Playing) return;
            if (noise.Kind != NoiseKind.Footstep && noise.Kind != NoiseKind.Cane && noise.Kind != NoiseKind.Impact) return;
            if (noise.EventId > 0 && resolvedEvents.Contains(noise.EventId)) return;

            ListenerCreature best = null;
            NavMeshPath bestPath = null;
            float bestCost = float.PositiveInfinity;
            foreach (var listener in listeners)
            {
                if (noise.EventId > 0 && listener.ClaimedNoiseEventId == noise.EventId) return;
                if (!listener.CanHear(noise) || !listener.TrySoundPath(noise.Position, out var path)) continue;
                // Favor a reachable approach. A roaming creature wins close calls, while
                // the exit guard still leaves its post for nearby lures or when alone.
                float cost = PathDistance(path, listener.transform.position)
                    + (listener.Role == ListenerRole.ExitGuard ? .8f : 0f);
                if (cost >= bestCost) continue;
                best = listener;
                bestPath = path;
                bestCost = cost;
            }
            if (best == null || !best.AcceptNoise(noise, bestPath, best.movementSpeed) || noise.EventId <= 0) return;

            // Completion releases the live claim but echoes and bounces from that same
            // event cannot open another claim. Keep the bounded history for delayed arrivals.
            if (recentEvents.Count == RememberedEvents) resolvedEvents.Remove(recentEvents.Dequeue());
            recentEvents.Enqueue(noise.EventId);
            resolvedEvents.Add(noise.EventId);
        }

        bool CanHear(NoiseEvent noise)
        {
            if (TutorialImpactSource != null && (noise.Kind != NoiseKind.Impact || noise.Source != TutorialImpactSource)) return false;
            if (TutorialHold || TutorialHearingSuppressed || IsCommittedToDistraction || !agent.isOnNavMesh) return false;
            if (noise.Source == gameObject || (noise.Source != null && noise.Source.transform.IsChildOf(transform))) return false;
            if (noise.Kind != NoiseKind.Impact && NoiseConfidence(noise) < .55f && Time.time - heardAt < .75f && HasHeardSound)
                return false;
            return Vector3.Distance(transform.position, noise.Position) <= HearingRange(noise.Kind, noise.Loudness);
        }

        static float NoiseConfidence(NoiseEvent noise) => noise.EventId > 0 ? Mathf.Clamp01(noise.Confidence) : 1f;

        bool AcceptNoise(NoiseEvent noise, NavMeshPath path, float speed, bool scripted = false)
        {
            if (!agent.SetPath(path)) return false;
            scriptedInspection = scripted;
            movedAroundDebris = false;
            TargetPosition = noise.Position;
            HasHeardSound = true;
            LastHeardPosition = noise.Position;
            LastHeardOrigin = noise.EventId > 0 ? noise.OriginalPosition : noise.Position;
            LastHeardReflection = noise.IsReflection;
            heardAt = Time.time;
            heardConfidence = NoiseConfidence(noise);
            approachListening = heardConfidence < .65f ? Mathf.Lerp(.85f, .2f, heardConfidence) : 0f;
            ClaimedNoiseEventId = noise.EventId;
            if (noise.Kind == NoiseKind.Impact)
            {
                // A real lure can interrupt a fresh player pursuit. Its travel is path-based,
                // not a few pawsteps that might stop short of the landing location.
                mode = Behavior.DistractionApproach;
                RemainingSteps = 0;
                var corners = path.corners;
                distractionDestination = corners.Length > 0 ? corners[corners.Length - 1] : transform.position;
                distractionTravelDeadline = Time.time + Mathf.Max(12f, PathDistance(path, transform.position) / speed + 3f);
            }
            else
            {
                mode = Behavior.Investigating;
                RemainingSteps = noise.Kind == NoiseKind.Footstep
                    ? Mathf.Clamp(Mathf.RoundToInt(18 + noise.Loudness * 12), 18, 28)
                    : Mathf.Clamp(Mathf.RoundToInt(12 + noise.Loudness * 6), 12, 20);
            }
            listenRemaining = 0;
            pawClock = 0;
            stalledTime = 0;
            agent.speed = speed;
            agent.isStopped = approachListening > 0f;
            idleClock = 0;
            return true;
        }

        static float PathDistance(NavMeshPath path, Vector3 start)
        {
            float length = 0f;
            foreach (Vector3 corner in path.corners)
            {
                length += Vector3.Distance(start, corner);
                start = corner;
            }
            return length;
        }

        private bool TrySoundPath(Vector3 sound, out NavMeshPath path)
        {
            path = null;
            // An impact on a table or shelf is heard from the floor next to it. Sampling
            // only the impact itself can select an isolated furniture-top NavMesh island.
            Vector3 groundPoint = new Vector3(sound.x, transform.position.y, sound.z);
            float closestDistance = float.PositiveInfinity;
            foreach (Vector3 offset in SoundSearchOffsets)
            {
                if (!NavMesh.SamplePosition(groundPoint + offset, out var hit, 1.1f, NavMesh.AllAreas)) continue;
                var candidatePath = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, candidatePath) || candidatePath.status != NavMeshPathStatus.PathComplete) continue;
                float distance = (hit.position - groundPoint).sqrMagnitude;
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                path = candidatePath;
                if (distance < .01f) break;
            }
            return path != null;
        }

        private void Update()
        {
            if (agent == null || !agent.isOnNavMesh) return;
            if (TutorialHold || Time.timeScale <= 0f || GameSession.Instance == null
                || GameSession.Instance.State != SessionState.Playing)
            {
                agent.isStopped = true;
                return;
            }

            if (approachListening > 0f)
            {
                approachListening = Mathf.Max(0f, approachListening - Time.deltaTime);
                agent.isStopped = true;
                Face(TargetPosition - transform.position, 4f);
            }
            else if (mode == Behavior.DistractionApproach || (mode == Behavior.Investigating && RemainingSteps > 0))
                UpdateApproach();
            else if (mode == Behavior.DistractionLinger || mode == Behavior.Investigating)
                UpdateInspection();
            else if (mode == Behavior.Returning || mode == Behavior.Patrolling)
                UpdateRoute();
            else UpdateGuard();

            if (!agent.isStopped) Face(agent.desiredVelocity, 10f);
            if (agent.velocity.sqrMagnitude > .025f) gaitClock += Time.deltaTime;
            AnimateLegs(agent.velocity.magnitude / movementSpeed);
            CheckCatch();
        }

        void UpdateApproach()
        {
            bool distraction = mode == Behavior.DistractionApproach;
            if (distraction && Time.time >= distractionTravelDeadline)
            {
                ReturnToRole();
                return;
            }
            agent.isStopped = false;
            if (agent.pathPending) return;
            if (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + .08f)
            {
                if (distraction && Vector3.Distance(transform.position, distractionDestination) > .8f) ReturnToRole();
                else FinishSoundInvestigation();
                return;
            }
            if (TrackPawsteps(.62f) && !distraction && --RemainingSteps <= 0) FinishSoundInvestigation();
            if (stalledTime <= 1.5f) return;
            if (distraction) ReturnToRole();
            else FinishSoundInvestigation();
        }

        void UpdateInspection()
        {
            listenRemaining -= Time.deltaTime;
            if (listenRemaining <= 0f)
            {
                ReturnToRole();
                return;
            }
            if (mode != Behavior.DistractionLinger)
            {
                agent.isStopped = true;
                return;
            }

            // The courtyard's scripted inspection takes one short step round the debris.
            // The inspection clock starts at its first arrival and keeps running throughout.
            if (scriptedInspection && !movedAroundDebris && listenRemaining < objectListenSeconds - 1.5f)
            {
                movedAroundDebris = true;
                var side = transform.position + (inspectionFacing * Vector3.right) * 1.15f;
                if (TryWaypointPath(side, out var path) && PathDistance(path, transform.position) <= 1.65f)
                {
                    agent.speed = PatrolSpeed * .65f;
                    agent.SetPath(path);
                    agent.isStopped = false;
                }
            }
            if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + .08f)
            {
                agent.isStopped = false;
                TrackPawsteps(.42f);
                return;
            }
            agent.isStopped = true;
            idleClock += Time.deltaTime;
            if (idleClock >= 1.4f)
            {
                idleClock = 0;
                Pawfall(pawOrder[nextPaw++ % 4], .34f);
            }
            if (scriptedInspection)
            {
                var glance = Quaternion.Euler(0, Mathf.Sin(listenRemaining * 3f) * 29f, 0) * inspectionFacing;
                transform.rotation = Quaternion.Slerp(transform.rotation, glance, Time.deltaTime * 4f);
            }
        }

        void UpdateRoute()
        {
            if (agent.pathPending) return;
            if (agent.hasPath && agent.remainingDistance > .55f)
            {
                agent.isStopped = false;
                TrackPawsteps(Role == ListenerRole.Roamer && mode == Behavior.Patrolling ? .5f : .62f);
                if (stalledTime > 2f)
                {
                    if (mode == Behavior.Patrolling) BeginNextPatrolWaypoint();
                    else ReturnToRole();
                }
                return;
            }
            if (Time.time < patrolRetryAt) return;
            if (Role == ListenerRole.Roamer)
            {
                if (mode == Behavior.Patrolling || Vector3.Distance(transform.position, returnDestination) <= .9f)
                    BeginNextPatrolWaypoint();
                else ReturnToRole();
            }
            else if (Vector3.Distance(transform.position, GuardPosition) <= .9f)
            {
                mode = Behavior.Guarding;
                agent.isStopped = true;
                agent.ResetPath();
                idleClock = 0;
            }
            else ReturnToRole();
        }

        void UpdateGuard()
        {
            agent.isStopped = true;
            if (Vector3.Distance(transform.position, GuardPosition) > .9f) ReturnToRole();
            idleClock += Time.deltaTime;
            if (idleClock > 7.5f)
            {
                idleClock = 0;
                Pawfall(pawOrder[nextPaw++ % 4], .34f);
            }
            Face(Vector3.back, 2f);
        }

        bool TrackPawsteps(float loudness)
        {
            if (agent.velocity.sqrMagnitude <= .025f)
            {
                stalledTime += Time.deltaTime;
                return false;
            }
            stalledTime = 0;
            pawClock += Time.deltaTime;
            if (pawClock < PawPeriod) return false;
            pawClock %= PawPeriod;
            Pawfall(pawOrder[nextPaw++ % 4], loudness);
            return true;
        }

        void Face(Vector3 direction, float response)
        {
            direction.y = 0;
            if (direction.sqrMagnitude > .01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * response);
        }

        private void FinishSoundInvestigation()
        {
            RemainingSteps = 0;
            pawClock = 0;
            agent.isStopped = true;
            agent.ResetPath();
            // Clear residual navigation velocity before starting the inspection clock.
            agent.velocity = Vector3.zero;
            if (mode == Behavior.DistractionApproach)
            {
                mode = Behavior.DistractionLinger;
                listenRemaining = objectListenSeconds;
                idleClock = 0;
                inspectionFacing = transform.rotation;
            }
            else listenRemaining = .6f;
        }

        private void ReturnToRole()
        {
            RemainingSteps = 0;
            listenRemaining = 0;
            approachListening = 0;
            ClaimedNoiseEventId = 0;
            pawClock = 0;
            stalledTime = 0;
            mode = Behavior.Returning;
            agent.speed = returnSpeed;
            if (Role == ListenerRole.ExitGuard && TrySoundPath(GuardPosition, out var guardPath))
            {
                if (SetRoute(guardPath)) return;
            }
            else if (Role == ListenerRole.Roamer && TryFindReturnWaypoint(out var path))
            {
                if (SetRoute(path)) return;
            }
            agent.ResetPath();
            agent.isStopped = true;
            patrolRetryAt = Time.time + RouteRetryDelay;
        }

        bool TryFindReturnWaypoint(out NavMeshPath nearestPath)
        {
            nearestPath = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < patrol.Length; ++i)
            {
                if (!TryWaypointPath(patrol[i], out var path)) continue;
                float distance = PathDistance(path, transform.position);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearestPath = path;
                PatrolWaypointIndex = i;
            }
            return nearestPath != null;
        }

        void BeginNextPatrolWaypoint()
        {
            mode = Behavior.Patrolling;
            agent.speed = PatrolSpeed;
            for (int offset = 1; offset <= patrol.Length; ++offset)
            {
                int next = (PatrolWaypointIndex + offset) % patrol.Length;
                if (!TryWaypointPath(patrol[next], out var path) || !SetRoute(path)) continue;
                PatrolWaypointIndex = next;
                return;
            }
            agent.ResetPath();
            agent.isStopped = true;
            patrolRetryAt = Time.time + RouteRetryDelay;
        }

        bool TryWaypointPath(Vector3 waypoint, out NavMeshPath path)
        {
            path = null;
            if (!NavMesh.SamplePosition(waypoint, out var hit, 1.45f, NavMesh.AllAreas)) return false;
            path = new NavMeshPath();
            return agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        bool SetRoute(NavMeshPath path)
        {
            var corners = path.corners;
            if (!agent.SetPath(path)) return false;
            returnDestination = corners.Length > 0 ? corners[corners.Length - 1] : transform.position;
            agent.isStopped = false;
            stalledTime = 0;
            patrolRetryAt = 0;
            return true;
        }

        private void Pawfall(int index, float loudness)
        {
            TotalPawsteps++;
            Vector3 point = paws[index] != null ? paws[index].position : transform.position;
            point.y = transform.position.y + .05f;
            NoiseSystem.Emit(point, loudness * (Role == ListenerRole.Roamer ? .86f : 1f), NoiseKind.Creature, gameObject);
        }

        private void CheckCatch()
        {
            var session = GameSession.Instance;
            if (player == null || session == null || CatchSuppressed) return;
            // The thrown prop still takes real flight time to distract the creature, while
            // this lesson locks player movement. Do not punish that forced observation;
            // ordinary proximity capture resumes as soon as the crouch lesson begins.
            if (session.Tutorial != null && session.Tutorial.Stage == TutorialStage.WatchDistraction) return;
            Vector3 offset = player.position - transform.position;
            if (Mathf.Abs(offset.y) > 1.25f) return;
            offset.y = 0;
            if (offset.sqrMagnitude > 1.1f * 1.1f) return;
            Vector3 eye = transform.position + Vector3.up * .9f;
            Vector3 destination = player.position + Vector3.up * .9f;
            if (!Physics.Linecast(eye, destination, 1, QueryTriggerInteraction.Ignore)) session.Catch();
        }

        private void AnimateLegs(float speed)
        {
            if (torso == null) return;
            float amplitude = Mathf.Clamp01(speed);
            float phase = gaitClock / (PawPeriod * 4f) * Mathf.PI * 2f + Mathf.PI * .5f;
            for (int i = 0; i < 4; i++)
            {
                if (paws[i] == null || upperLegs[i] == null || lowerLegs[i] == null) continue;
                float side = i % 2 == 0 ? -1 : 1;
                float front = i < 2 ? 1 : -1;
                int order = i == 0 ? 0 : i == 3 ? 1 : i == 1 ? 2 : 3;
                float legPhase = phase - order * Mathf.PI * .5f;
                float lift = Mathf.Max(0, Mathf.Sin(legPhase)) * .22f * amplitude;
                Vector3 foot = new Vector3(side * .67f, .10f + lift, front * .83f + Mathf.Cos(legPhase) * .25f * amplitude);
                Vector3 knee = new Vector3(side * .71f, .58f + lift * .3f, front * .83f - .11f);
                paws[i].localPosition = foot;
                Limb(upperLegs[i], HipPoints[i], knee, .135f);
                Limb(lowerLegs[i], knee, foot + Vector3.up * .04f, .085f);
            }
            torso.localPosition = new Vector3(0, 1.09f + Mathf.Sin(phase * 2f) * .027f * amplitude, 0);
            if (head != null)
            {
                float pitch = IsLingeringAtDistraction ? 12f + Mathf.Sin(Time.time * 2f) * 8f : 0f;
                float yaw = Mathf.Sin(Time.time * .8f) * (RemainingSteps > 0 ? 3f : 13f);
                head.localRotation = Quaternion.Euler(pitch, yaw, 0);
            }
        }

        private static void Limb(Transform limb, Vector3 start, Vector3 end, float radius)
        {
            limb.localPosition = (start + end) * .5f;
            limb.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
            limb.localScale = new Vector3(radius * 2, Vector3.Distance(start, end) * .5f, radius * 2);
        }
    }
}
