using UnityEngine;
using UnityEngine.AI;

namespace Haunter
{
    /// <summary>Hunts movement by sound, then returns to protect the exit.</summary>
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
        public bool IsReturningToExit => mode == Behavior.Returning;
        public bool IsGuardingExit => mode == Behavior.Guarding;
        public int TotalPawsteps { get; private set; }
        public bool TutorialHold { get; set; }
        public bool TutorialHearingSuppressed { get; set; }
        public GameObject TutorialImpactSource { get; set; }

        enum Behavior { Guarding, Investigating, DistractionApproach, DistractionLinger, Returning }
        Behavior mode;
        Vector3 distractionDestination;
        float distractionTravelDeadline;
        float listenRemaining;
        float stalledTime;

        private NavMeshAgent agent;
        private float pawClock;
        private float gaitClock;
        private float idleClock;
        private int nextPaw;
        private readonly int[] pawOrder = { 0, 3, 1, 2 };
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
        private void OnEnable() { NoiseSystem.Heard += Hear; }
        private void OnDisable() { NoiseSystem.Heard -= Hear; }

        private void Start()
        {
            if (NavMesh.SamplePosition(transform.position, out var hit, 3f, NavMesh.AllAreas)) transform.position = hit.position;
            agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
            if (agent.isOnNavMesh) agent.isStopped = true;
            TargetPosition = transform.position;
            Vector3 desiredGuard = GameSession.Instance != null && GameSession.Instance.Exit != null
                ? GameSession.Instance.Exit.position + guardOffset : transform.position;
            GuardPosition = NavMesh.SamplePosition(desiredGuard, out hit, 2f, NavMesh.AllAreas)
                ? hit.position : transform.position;
            mode = Behavior.Guarding;
            idleClock = 4f;
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
            TargetPosition = target;
            mode = Behavior.Investigating;
            RemainingSteps = 100;
            listenRemaining = 0;
            pawClock = 0;
            stalledTime = 0;
            idleClock = 0;
            agent.speed = movementSpeed;
            agent.SetPath(path);
            agent.isStopped = false;
            return true;
        }

        public void ReleaseTutorialControl(bool returnToExit)
        {
            TutorialHold = false;
            TutorialHearingSuppressed = false;
            TutorialImpactSource = null;
            if (returnToExit && agent != null && agent.isOnNavMesh) ReturnToExit();
        }

        private void Hear(NoiseEvent noise)
        {
            if (TutorialImpactSource != null && (noise.Kind != NoiseKind.Impact || noise.Source != TutorialImpactSource)) return;
            if (TutorialHold || TutorialHearingSuppressed) return;
            if (GameSession.Instance == null || GameSession.Instance.State != SessionState.Playing) return;
            if (noise.Kind == NoiseKind.Creature || noise.Kind == NoiseKind.Intro) return;
            if (noise.Source == gameObject || (noise.Source != null && noise.Source.transform.IsChildOf(transform))) return;
            // An accepted distraction owns attention until arrival and a full inspection.
            // Drop intervening noises: neither footsteps nor bounces can redirect or extend it.
            if (IsCommittedToDistraction) return;
            if (!agent.isOnNavMesh || Vector3.Distance(transform.position, noise.Position) > HearingRange(noise.Kind, noise.Loudness)) return;
            if (!TrySoundPath(noise.Position, out var path)) return;
            TargetPosition = noise.Position;
            if (noise.Kind == NoiseKind.Impact)
            {
                // A real lure can interrupt a fresh player pursuit. Its travel is path-based,
                // not a few pawsteps that might stop short of the landing location.
                mode = Behavior.DistractionApproach;
                RemainingSteps = 0;
                var corners = path.corners;
                distractionDestination = corners.Length > 0 ? corners[corners.Length - 1] : transform.position;
                distractionTravelDeadline = Time.time + 12f;
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
            agent.speed = movementSpeed;
            agent.SetPath(path);
            agent.isStopped = false;
            idleClock = 0;
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
            if (TutorialHold || Time.timeScale <= 0f)
            {
                agent.isStopped = true;
                return;
            }
            if (GameSession.Instance == null || GameSession.Instance.State != SessionState.Playing)
            {
                agent.isStopped = true;
                return;
            }

            // A changed/blocked navigation route must not leave an unbreakable distraction.
            if (mode == Behavior.DistractionApproach && Time.time >= distractionTravelDeadline) ReturnToExit();

            if ((mode == Behavior.Investigating && RemainingSteps > 0) || mode == Behavior.DistractionApproach)
            {
                agent.isStopped = false;
                if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + .08f))
                {
                    if (mode == Behavior.DistractionApproach
                        && PlayerController.HorizontalDistance(transform.position, distractionDestination) > .8f)
                        ReturnToExit();
                    else FinishSoundInvestigation();
                }
                else
                {
                    if (agent.velocity.sqrMagnitude > .025f)
                    {
                        stalledTime = 0;
                        pawClock += Time.deltaTime;
                        if (pawClock >= pawInterval)
                        {
                            pawClock -= pawInterval;
                            int paw = pawOrder[nextPaw++ % 4];
                            Pawfall(paw, .62f);
                            if (mode == Behavior.Investigating)
                            {
                                RemainingSteps--;
                                if (RemainingSteps <= 0) FinishSoundInvestigation();
                            }
                        }
                    }
                    else
                    {
                        // A blocked path must not leave an indefinitely armed movement budget.
                        stalledTime += Time.deltaTime;
                        if (stalledTime > 1.5f)
                        {
                            if (mode == Behavior.DistractionApproach) ReturnToExit();
                            else FinishSoundInvestigation();
                        }
                    }
                }
            }
            else if (mode == Behavior.Investigating || mode == Behavior.DistractionLinger)
            {
                agent.isStopped = true;
                listenRemaining -= Time.deltaTime;
                if (mode == Behavior.DistractionLinger)
                {
                    idleClock += Time.deltaTime;
                    if (idleClock >= 1.4f)
                    {
                        idleClock = 0;
                        Pawfall(pawOrder[nextPaw++ % 4], .34f);
                    }
                }
                if (listenRemaining <= 0) ReturnToExit();
            }
            else if (mode == Behavior.Returning)
            {
                agent.isStopped = false;
                if (!agent.pathPending && agent.hasPath && agent.remainingDistance > .55f)
                {
                    pawClock += Time.deltaTime;
                    if (agent.velocity.sqrMagnitude > .025f)
                    {
                        stalledTime = 0;
                        if (pawClock >= pawInterval)
                        {
                            pawClock %= pawInterval;
                            Pawfall(pawOrder[nextPaw++ % 4], .62f);
                        }
                    }
                    else stalledTime += Time.deltaTime;
                    if (stalledTime > 2f) ReturnToExit();
                }
                else if (!agent.pathPending)
                {
                    if (PlayerController.HorizontalDistance(transform.position, GuardPosition) <= .9f)
                    {
                        mode = Behavior.Guarding;
                        agent.isStopped = true;
                        agent.ResetPath();
                        idleClock = 0;
                    }
                    else ReturnToExit();
                }
            }
            else
            {
                agent.isStopped = true;
                if (PlayerController.HorizontalDistance(transform.position, GuardPosition) > .9f) ReturnToExit();
                idleClock += Time.deltaTime;
                if (idleClock > 7.5f)
                {
                    idleClock = 0;
                    Pawfall(pawOrder[nextPaw++ % 4], .34f);
                }
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(Vector3.back), Time.deltaTime * 2f);
            }
            Vector3 velocity = agent.desiredVelocity;
            velocity.y = 0;
            if (!agent.isStopped && velocity.sqrMagnitude > .01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(velocity), Time.deltaTime * 10);
            if (agent.velocity.sqrMagnitude > .025f) gaitClock += Time.deltaTime;
            AnimateLegs(agent.velocity.magnitude / movementSpeed);
            CheckCatch();
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
            }
            else listenRemaining = .6f;
        }

        private void ReturnToExit()
        {
            RemainingSteps = 0;
            listenRemaining = 0;
            pawClock = 0;
            stalledTime = 0;
            mode = Behavior.Returning;
            agent.speed = returnSpeed;
            if (TrySoundPath(GuardPosition, out var path))
            {
                agent.SetPath(path);
                agent.isStopped = false;
            }
            else agent.isStopped = true;
        }

        private void Pawfall(int index, float loudness)
        {
            TotalPawsteps++;
            Vector3 point = paws[index] != null ? paws[index].position : transform.position;
            point.y = .05f;
            NoiseSystem.Emit(point, loudness, NoiseKind.Creature, gameObject);
        }

        private void CheckCatch()
        {
            if (player == null) return;
            Vector3 offset = player.position - transform.position;
            offset.y = 0;
            if (offset.sqrMagnitude > 1.1f * 1.1f) return;
            Vector3 eye = transform.position + Vector3.up * .9f;
            Vector3 destination = player.position + Vector3.up * .9f;
            if (!Physics.Linecast(eye, destination, 1, QueryTriggerInteraction.Ignore)) GameSession.Instance.Catch();
        }

        private void AnimateLegs(float speed)
        {
            if (torso == null) return;
            float amplitude = Mathf.Clamp01(speed);
            float phase = gaitClock / (pawInterval * 4f) * Mathf.PI * 2f + Mathf.PI * .5f;
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
            if (head != null) head.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * .8f) * (RemainingSteps > 0 ? 3f : 13f), 0);
        }

        private static void Limb(Transform limb, Vector3 start, Vector3 end, float radius)
        {
            limb.localPosition = (start + end) * .5f;
            limb.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
            limb.localScale = new Vector3(radius * 2, Vector3.Distance(start, end) * .5f, radius * 2);
        }
    }
}
