using UnityEngine;
using UnityEngine.AI;

namespace Haunter
{
    [System.Flags]
    public enum PlayerActions
    {
        None = 0, Look = 1, Move = 2, Crouch = 4, Sprint = 8, Cane = 16,
        Pickup = 32, Throw = 64, Drop = 128, Exit = 256, All = 511
    }

    public enum TutorialStage
    {
        Inactive, TapCane, RevealCreature, Move, Charge, FocusPickup, Pickup,
        FocusThrow, Throw, WatchDistraction, Crouch, Complete, Skipped
    }

    /// <summary>Guides one opening encounter; completion belongs to this run, never to a saved profile.</summary>
    [DisallowMultipleComponent]
    public sealed class TutorialDirector : MonoBehaviour
    {
        public TutorialStage Stage { get; private set; }
        public bool Active => Stage != TutorialStage.Inactive && Stage != TutorialStage.Complete && Stage != TutorialStage.Skipped;
        public bool WorldFrozen => Active && (Stage == TutorialStage.FocusPickup || Stage == TutorialStage.Pickup
            || Stage == TutorialStage.FocusThrow || Stage == TutorialStage.Throw);
        public ThrowableObject TargetObject { get; private set; }
        public Vector3 ThrowTarget { get; private set; }
        public int FootstepsTaken { get; private set; }
        public bool HasCrouched { get; private set; }
        public bool RequiresCrouch => Stage == TutorialStage.Crouch;

        public PlayerActions Actions
        {
            get
            {
                switch (Stage)
                {
                    case TutorialStage.TapCane: return PlayerActions.Look | PlayerActions.Cane;
                    case TutorialStage.Move: return PlayerActions.Look | PlayerActions.Move;
                    case TutorialStage.Pickup: return PlayerActions.Pickup;
                    case TutorialStage.Throw: return PlayerActions.Throw;
                    case TutorialStage.Crouch: return PlayerActions.Look | PlayerActions.Move | PlayerActions.Crouch | PlayerActions.Exit;
                    case TutorialStage.Inactive:
                    case TutorialStage.Complete:
                    case TutorialStage.Skipped: return PlayerActions.All;
                    default: return PlayerActions.None;
                }
            }
        }

        public string Heading
        {
            get
            {
                switch (Stage)
                {
                    case TutorialStage.TapCane: return "SOUND IS YOUR SIGHT";
                    case TutorialStage.RevealCreature: return "SOMETHING IS GUARDING THE EXIT";
                    case TutorialStage.Move: return "TAKE A FEW STEPS";
                    case TutorialStage.Charge: return "IT HEARD YOU";
                    case TutorialStage.FocusPickup:
                    case TutorialStage.Pickup: return "YOU NEED TO DISTRACT IT NOW";
                    case TutorialStage.FocusThrow:
                    case TutorialStage.Throw: return "GIVE IT SOMETHING ELSE TO FOLLOW";
                    case TutorialStage.WatchDistraction: return "WATCH WHERE IT GOES";
                    case TutorialStage.Crouch: return "STAY LOW. STAY QUIET.";
                    default: return string.Empty;
                }
            }
        }

        public string Instruction
        {
            get
            {
                switch (Stage)
                {
                    case TutorialStage.TapCane: return "SPACE / RIGHT MOUSE  —  Tap your cane to see the room.";
                    case TutorialStage.RevealCreature: return "Oh no... I've got to sneak past it. It's blocking the only exit.";
                    case TutorialStage.Move: return "WASD  —  Move around. Your footsteps make sound, too.";
                    case TutorialStage.Charge: return "Your footsteps gave you away.";
                    case TutorialStage.FocusPickup: return "Time is frozen. Look at the object beside you.";
                    case TutorialStage.Pickup: return "E  —  Pick up the " + (TargetObject ? TargetObject.DisplayName.ToLowerInvariant() : "object") + ".";
                    case TutorialStage.FocusThrow: return "Throw it over there, close enough for the creature to hear.";
                    case TutorialStage.Throw: return "HOLD LEFT MOUSE, THEN RELEASE  —  Throw the object. Time resumes when you throw.";
                    case TutorialStage.WatchDistraction: return "The impact draws it away. Wait for it to start inspecting.";
                    case TutorialStage.Crouch: return HasCrouched
                        ? "Keep holding " + PlayerController.CrouchKeys + " to move quietly. It will return to the exit."
                        : "HOLD " + PlayerController.CrouchKeys + " + WASD  —  Crouch to move while it investigates.";
                    default: return string.Empty;
                }
            }
        }

        GameSession session;
        float age;
        bool secondPaw;
        Vector3 crouchStart;
        PlayerController Player => session.Player;
        ListenerCreature Creature => session.Creature;

        void Awake() { session = GetComponent<GameSession>(); }
        void OnEnable() { NoiseSystem.Heard += OnNoise; }
        void OnDisable()
        {
            NoiseSystem.Heard -= OnNoise;
            ClearHighlight();
            if (session != null && Creature != null) Creature.ReleaseTutorialControl(false);
        }

        public void StartTutorial()
        {
            ClearHighlight();
            FootstepsTaken = 0;
            HasCrouched = false;
            secondPaw = false;
            TargetObject = null;
            if (session == null || Player == null || Creature == null) { Skip(); return; }
            Player.CancelThrow();
            SetStage(TutorialStage.TapCane);
        }

        public void Skip()
        {
            ClearHighlight();
            if (session != null && Player != null) Player.CancelThrow();
            Stage = TutorialStage.Skipped;
            if (session != null && Creature != null) Creature.ReleaseTutorialControl(true);
            if (session != null && session.State == SessionState.Playing) Time.timeScale = 1f;
        }

        void ClearHighlight()
        {
            if (TargetObject != null && !TargetObject.IsHeld) EchoMaterial.SetPersistent(TargetObject.gameObject, false);
        }

        void SetStage(TutorialStage next)
        {
            Stage = next;
            age = 0;
            Creature.TutorialHold = next == TutorialStage.TapCane || next == TutorialStage.RevealCreature
                || next == TutorialStage.Move || WorldFrozen;
            // Only the actual thrown prop is allowed to interrupt this authored opening.
            Creature.TutorialHearingSuppressed = Active && next != TutorialStage.WatchDistraction;
            Creature.TutorialImpactSource = next == TutorialStage.WatchDistraction && TargetObject != null ? TargetObject.gameObject : null;
            if (session.State == SessionState.Playing) Time.timeScale = WorldFrozen ? 0f : 1f;
        }

        void OnNoise(NoiseEvent noise)
        {
            if (!Active || session.State != SessionState.Playing || noise.Source != Player.gameObject) return;
            if (Stage == TutorialStage.TapCane && noise.Kind == NoiseKind.Cane)
            {
                SetStage(TutorialStage.RevealCreature);
                Creature.CueTutorialPaw();
            }
            else if (Stage == TutorialStage.Move && noise.Kind == NoiseKind.Footstep)
            {
                FootstepsTaken++;
                if (FootstepsTaken < 2) return;
                SetStage(TutorialStage.Charge);
                if (!Creature.BeginTutorialPursuit(Player.transform.position)) Skip();
            }
        }

        void Update()
        {
            if (!Active || session == null || session.State != SessionState.Playing) return;
            if (Player.AcceptInput && Input.GetKeyDown(KeyCode.T)) { Skip(); return; }
            age += Time.unscaledDeltaTime;
            switch (Stage)
            {
                case TutorialStage.RevealCreature:
                    Pan(Creature.transform.position + Vector3.up, false);
                    if (!secondPaw && age >= 1.5f) { secondPaw = true; Creature.CueTutorialPaw(); }
                    if (age >= 4f) SetStage(TutorialStage.Move);
                    break;
                case TutorialStage.Charge:
                    Pan(Creature.transform.position + Vector3.up, false);
                    if (PlayerController.HorizontalDistance(Player.transform.position, Creature.transform.position)
                        <= 7f + Creature.ChaseSpeed * Time.deltaTime)
                        PreparePickup();
                    else if (age > 10f) Skip(); // A modified/blocked room must never trap the player.
                    break;
                case TutorialStage.FocusPickup:
                case TutorialStage.Pickup:
                    if (TargetObject == null) { PreparePickup(); break; }
                    if (Player.HeldObject == TargetObject)
                    {
                        if (!FindThrowTarget()) { Skip(); break; }
                        SetStage(TutorialStage.FocusThrow);
                        break;
                    }
                    Pan(TargetObject.transform.position, Stage == TutorialStage.Pickup);
                    if (Stage == TutorialStage.FocusPickup && age >= 1.2f)
                    {
                        Pan(TargetObject.transform.position, true);
                        SetStage(TutorialStage.Pickup);
                    }
                    break;
                case TutorialStage.FocusThrow:
                case TutorialStage.Throw:
                    Pan(ThrowTarget, Stage == TutorialStage.Throw);
                    if (Stage == TutorialStage.FocusThrow && age >= 1.2f)
                    {
                        Pan(ThrowTarget, true);
                        SetStage(TutorialStage.Throw);
                    }
                    break;
                case TutorialStage.WatchDistraction:
                    Pan(Creature.transform.position + Vector3.up, false);
                    if (Creature.IsLingeringAtDistraction)
                    {
                        crouchStart = Player.transform.position;
                        SetStage(TutorialStage.Crouch);
                    }
                    else if (age > 14f)
                    {
                        // If a modified room obstructs the throw, offer another real pickup/throw.
                        PreparePickup();
                    }
                    break;
                case TutorialStage.Crouch:
                    if (Player.IsCrouching && PlayerController.HorizontalDistance(crouchStart, Player.transform.position) > .15f)
                        HasCrouched = true;
                    if (HasCrouched && Creature.IsGuardingExit)
                    {
                        SetStage(TutorialStage.Complete);
                        Creature.ReleaseTutorialControl(false);
                        ClearHighlight();
                    }
                    break;
            }
        }

        void Pan(Vector3 point, bool snap)
        {
            Player.GuideLookAt(point, snap ? 1f : 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
        }

        void PreparePickup()
        {
            SetStage(TutorialStage.FocusPickup);
            ClearHighlight();
            TargetObject = null;
            var items = FindObjectsByType<ThrowableObject>(FindObjectsSortMode.None);
            Vector3 eye = Player.ViewCamera.transform.position;
            float best = float.PositiveInfinity;
            ThrowableObject nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (var item in items)
            {
                if (item.IsHeld) continue;
                float distance = Vector3.Distance(eye, item.transform.position);
                if (distance < nearestDistance) { nearest = item; nearestDistance = distance; }
                if (distance > Player.PickupReach - .1f || distance >= best) continue;
                if (Physics.Linecast(eye, item.transform.position, out var hit, ~(1 << 9), QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<ThrowableObject>() != item) continue;
                TargetObject = item;
                best = distance;
            }
            if (TargetObject == null && nearest != null)
            {
                // Keep the lesson playable even when the opening steps go away from the scattered props.
                for (int i = 0; i < 12; i++)
                {
                    Vector3 offset = Quaternion.Euler(0, i * 30, 0) * Player.transform.forward;
                    Vector3 candidate = Player.transform.position + offset * 1.1f;
                    if (!Physics.Raycast(candidate + Vector3.up, Vector3.down, out var floor, 2f, 1)) continue;
                    candidate = floor.point + Vector3.up * .25f;
                    if (Physics.CheckSphere(candidate, .2f, 1, QueryTriggerInteraction.Ignore)
                        || Physics.Linecast(eye, candidate, 1, QueryTriggerInteraction.Ignore)) continue;
                    nearest.Body.velocity = Vector3.zero;
                    nearest.Body.angularVelocity = Vector3.zero;
                    nearest.Body.position = candidate;
                    nearest.transform.position = candidate;
                    Physics.SyncTransforms();
                    TargetObject = nearest;
                    break;
                }
            }
            if (TargetObject == null) { Skip(); return; }
            EchoMaterial.SetPersistent(TargetObject.gameObject, true);
        }

        bool FindThrowTarget()
        {
            var nav = Creature.GetComponent<NavMeshAgent>();
            Vector3 towardPlayer = (Player.transform.position - Creature.transform.position).normalized;
            // Begin to the side: a distraction should pull the creature off the player's route.
            for (int radius = 3; radius >= 1; radius--)
            for (int i = 0; i < 16; i++)
            {
                float angle = i % 2 == 0 ? 75f + i * 12f : -75f - i * 12f;
                Vector3 offset = Quaternion.Euler(0, angle, 0) * towardPlayer * radius;
                if (!NavMesh.SamplePosition(Creature.transform.position + offset, out var floor, .6f, NavMesh.AllAreas)) continue;
                if (PlayerController.HorizontalDistance(floor.position, Creature.transform.position) > 3.5f
                    || PlayerController.HorizontalDistance(floor.position, Player.transform.position) < 3f) continue;
                var path = new NavMeshPath();
                if (!nav.CalculatePath(floor.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                Vector3 candidate = floor.position + Vector3.up * .3f;
                if (Physics.CheckSphere(candidate, .23f, 1, QueryTriggerInteraction.Ignore)) continue;
                Vector3 eye = Player.ViewCamera.transform.position;
                Vector3 release = eye + (candidate - eye).normalized * .7f;
                float flight = FlightTime(release, candidate);
                // The player cannot dodge during the demonstration: land before the charging guard can reach them.
                float timeUntilCatch = (PlayerController.HorizontalDistance(Player.transform.position, Creature.transform.position) - 1.1f) / Creature.ChaseSpeed;
                if (flight > .8f || flight + .18f >= timeUntilCatch) continue;
                Vector3 velocity = BallisticVelocity(release, candidate, flight);
                Vector3 previous = eye;
                bool clear = true;
                for (int step = 0; step <= 20; step++)
                {
                    float t = flight * step / 20f;
                    Vector3 point = release + velocity * t + Physics.gravity * (.5f * t * t);
                    if (Physics.Linecast(previous, point, 1, QueryTriggerInteraction.Ignore)
                        || Physics.CheckSphere(point, .22f, 1, QueryTriggerInteraction.Ignore)) { clear = false; break; }
                    previous = point;
                }
                if (!clear) continue;
                ThrowTarget = candidate;
                return true;
            }
            return false;
        }

        static float FlightTime(Vector3 start, Vector3 end) => Mathf.Clamp(PlayerController.HorizontalDistance(start, end) / 9f, .65f, 1.25f);
        static Vector3 BallisticVelocity(Vector3 start, Vector3 end, float time) => (end - start - Physics.gravity * (.5f * time * time)) / time;

        public Vector3 GetThrowVelocity()
        {
            Vector3 start = Player.ViewCamera.transform.position + Player.ViewCamera.transform.forward * .7f;
            return BallisticVelocity(start, ThrowTarget, FlightTime(start, ThrowTarget));
        }

        public void NotifyThrown(ThrowableObject item)
        {
            if (Stage == TutorialStage.Throw && item == TargetObject && !item.IsHeld)
                SetStage(TutorialStage.WatchDistraction);
        }
    }
}
