using UnityEngine;

namespace Haunter
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public const string CrouchKeys = "SHIFT / C";
        public const string SprintKeys = "R";
#else
        public const string CrouchKeys = "CTRL / C";
        public const string SprintKeys = "SHIFT";
#endif
        [Header("Movement")]
        public float WalkSpeed = 2.8f;
        public float CrouchSpeed = 1.35f;
        public float SprintSpeed = 4.4f;
        public float MouseSensitivity = 1.8f;
        [Header("Interaction")]
        public float PickupReach = 2.6f;
        public float CaneCooldown = .85f;

        public Camera ViewCamera;
        // Runtime test/input handoff; deliberately not serialized into the playable scene.
        public bool AcceptInput { get; set; } = true;
        ThrowableObject heldObject;
        public ThrowableObject HeldObject
        {
            get
            {
                if (heldObject != null && !heldObject.IsHeld) heldObject = null;
                return heldObject;
            }
            private set { heldObject = value; }
        }
        public ThrowableObject NearbyObject { get; private set; }
        public float ThrowCharge { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool ChargingThrow { get; private set; }
        public float CaneReady { get { return Mathf.Clamp01((Time.time - lastTap) / CaneCooldown); } }

        CharacterController controller;
        [SerializeField] Transform handAnchor;
        [SerializeField] Transform cane;
        [SerializeField] Vector3 caneRestPosition;
        [SerializeField] Quaternion caneRestRotation = Quaternion.identity;
        float pitch;
        float gravityVelocity;
        float walkedDistance;
        float gait;
        float lastTap = -10f;
        float tapAnimation;
        float chargeStart;
        float currentEyeHeight = 1.62f;
        float handScale = 1f;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (ViewCamera == null) ViewCamera = GetComponentInChildren<Camera>();
            if (ViewCamera != null) Initialize(ViewCamera);
            if (cane != null) EchoMaterial.SetPersistent(cane.gameObject, true);
        }

        public void Initialize(Camera camera)
        {
            ViewCamera = camera;
            controller = GetComponent<CharacterController>();
            controller.height = 1.8f;
            controller.center = new Vector3(0f, .9f, 0f);
            controller.radius = .3f;
            controller.skinWidth = .025f;
            controller.stepOffset = .22f;
            controller.slopeLimit = 45f;
            gameObject.layer = 9;
            if (handAnchor == null && ViewCamera != null) handAnchor = ViewCamera.transform.Find("Held object anchor");
            if (handAnchor == null && ViewCamera != null)
            {
                GameObject anchor = new GameObject("Held object anchor");
                handAnchor = anchor.transform;
                handAnchor.SetParent(ViewCamera.transform, false);
                handAnchor.localPosition = new Vector3(.36f, -.3f, .65f);
            }
        }

        public void SetCane(Transform caneTransform)
        {
            cane = caneTransform;
            if (cane == null) return;
            caneRestPosition = cane.localPosition;
            caneRestRotation = cane.localRotation;
            EchoMaterial.SetPersistent(cane.gameObject, true);
        }

        void Update()
        {
            if (ViewCamera == null || GameSession.Instance == null || GameSession.Instance.State != SessionState.Playing)
            {
                ChargingThrow = false;
                ThrowCharge = 0f;
                return;
            }
            if (AcceptInput)
            {
                Look();
                Move();
                Interact();
            }
            AnimateHands();
        }

        void Look()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            transform.Rotate(0f, Input.GetAxisRaw("Mouse X") * MouseSensitivity, 0f);
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * MouseSensitivity, -82f, 82f);
            ViewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Ctrl + movement keys can trigger browser shortcuts such as closing the tab.
            bool crouchRequested = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.C);
            bool sprintRequested = Input.GetKey(KeyCode.R);
#else
            bool crouchRequested = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);
            bool sprintRequested = Input.GetKey(KeyCode.LeftShift);
#endif
            bool blockedAbove = IsCrouching && Physics.SphereCast(transform.position + Vector3.up * .9f,
                .29f, Vector3.up, out _, .6f, ~(1 << 9), QueryTriggerInteraction.Ignore);
            IsCrouching = crouchRequested || blockedAbove;
            IsSprinting = !IsCrouching && sprintRequested;
            float height = IsCrouching ? 1.15f : 1.8f;
            controller.height = height;
            controller.center = Vector3.up * height * .5f;
            Vector2 moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            moveInput = Vector2.ClampMagnitude(moveInput, 1f);
            float speed = IsCrouching ? CrouchSpeed : IsSprinting ? SprintSpeed : WalkSpeed;
            Vector3 movement = (transform.right * moveInput.x + transform.forward * moveInput.y) * speed;
            bool groundedBefore = controller.isGrounded;
            if (groundedBefore && gravityVelocity < 0f) gravityVelocity = -2f;
            gravityVelocity = Mathf.Max(gravityVelocity - 24f * Time.deltaTime, -25f);
            Vector3 oldPosition = transform.position;
            controller.Move((movement + Vector3.up * gravityVelocity) * Time.deltaTime);
            Vector3 displacement = transform.position - oldPosition;
            displacement.y = 0f;
            float travelled = displacement.magnitude;
            if (controller.isGrounded && travelled > .001f)
            {
                walkedDistance += travelled;
                gait += travelled * (IsCrouching ? 5f : 6.5f);
                float stride = IsCrouching ? 1f : IsSprinting ? 1.1f : .85f;
                if (walkedDistance >= stride)
                {
                    walkedDistance %= stride;
                    NoiseSystem.Emit(transform.position + Vector3.up * .04f,
                        IsCrouching ? .25f : IsSprinting ? .9f : .48f, NoiseKind.Footstep, gameObject);
                }
            }
            // Preserve partial strides across stops: short bursts of walking still make footsteps.
            currentEyeHeight = Mathf.Lerp(currentEyeHeight, IsCrouching ? 1.02f : 1.62f, Time.deltaTime * 12f);
            float bob = travelled > .001f ? Mathf.Sin(gait) * (IsCrouching ? .009f : .018f) : 0f;
            ViewCamera.transform.localPosition = new Vector3(0f, currentEyeHeight + bob, 0f);
        }

        void Interact()
        {
            NearbyObject = null;
            if (HeldObject == null && Physics.Raycast(ViewCamera.transform.position, ViewCamera.transform.forward,
                out RaycastHit hit, PickupReach, ~(1 << 9), QueryTriggerInteraction.Ignore))
            {
                NearbyObject = hit.collider.GetComponentInParent<ThrowableObject>();
                if (NearbyObject != null && NearbyObject.IsHeld) NearbyObject = null;
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (HeldObject == null && NearbyObject != null) PickUp(NearbyObject);
                else if (GameSession.Instance.Exit != null && HorizontalDistance(transform.position, GameSession.Instance.Exit.position) < 1.9f)
                    GameSession.Instance.Escape();
            }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(1)) TapCane();
            if (HeldObject == null)
            {
                ChargingThrow = false;
                ThrowCharge = 0f;
                return;
            }
            if (Input.GetKeyDown(KeyCode.Q))
            {
                DropHeld();
                return;
            }
            if (Input.GetMouseButtonDown(0)) { ChargingThrow = true; chargeStart = Time.time; }
            if (ChargingThrow) ThrowCharge = Mathf.Clamp01((Time.time - chargeStart) / .8f);
            if (ChargingThrow && Input.GetMouseButtonUp(0))
            {
                Vector3 direction = (ViewCamera.transform.forward + Vector3.up * .14f).normalized;
                ThrowHeld(direction * Mathf.Lerp(7.2f, 15f, ThrowCharge));
            }
        }

        public void PickUp(ThrowableObject item)
        {
            if (item == null || HeldObject != null || item.IsHeld || handAnchor == null) return;
            handScale = 1f;
            handAnchor.localScale = Vector3.one;
            item.Pickup(handAnchor, controller);
            if (item.IsHeld)
            {
                HeldObject = item;
                AnimateHands();
            }
        }

        public void ThrowHeld(Vector3 velocity)
        {
            ThrowableObject item = HeldObject;
            if (item == null) return;
            item.Throw(velocity);
            if (!item.IsHeld) HeldObject = null;
            ChargingThrow = false;
            ThrowCharge = 0f;
        }

        public void DropHeld()
        {
            ThrowableObject item = HeldObject;
            if (item == null) return;
            item.Drop();
            if (!item.IsHeld) HeldObject = null;
            ChargingThrow = false;
            ThrowCharge = 0f;
        }

        public void TapCane()
        {
            if (Time.time - lastTap < CaneCooldown) return;
            lastTap = Time.time;
            tapAnimation = 1f;
            Vector3 tapPoint = transform.position + transform.forward * .7f + Vector3.up * .035f;
            // A cane cannot tap through a nearby wall.
            if (Physics.Raycast(transform.position + Vector3.up * .2f, transform.forward,
                out RaycastHit hit, .7f, ~(1 << 9), QueryTriggerInteraction.Ignore)) tapPoint = hit.point;
            NoiseSystem.Emit(tapPoint, .9f, NoiseKind.Cane, gameObject);
        }

        void AnimateHands()
        {
            tapAnimation = Mathf.Max(0f, tapAnimation - Time.deltaTime * 3.8f);
            if (cane != null)
            {
                float sweep = Mathf.Sin(tapAnimation * Mathf.PI);
                cane.localPosition = caneRestPosition + Vector3.down * sweep * .07f;
                cane.localRotation = caneRestRotation * Quaternion.Euler(sweep * -15f, sweep * 9f, 0f);
            }
            if (handAnchor != null)
            {
                float sway = Mathf.Sin(gait * .5f) * .012f;
                Vector3 offset = new Vector3(.36f + sway, -.3f - ThrowCharge * .08f, .65f - ThrowCharge * .16f);
                if (HeldObject != null)
                {
                    float safeScale = GetSafeHandScale(offset, HeldObject.HoldRadius);
                    // Snap away from approaching surfaces, then ease back to the normal hand pose.
                    handScale = safeScale < handScale ? safeScale : Mathf.MoveTowards(handScale, safeScale, Time.deltaTime * 3f);
                }
                else handScale = 1f;
                // Scaling both distance and geometry preserves its apparent size in perspective.
                handAnchor.localPosition = offset * handScale;
                handAnchor.localScale = Vector3.one * handScale;
                handAnchor.localRotation = Quaternion.Euler(ThrowCharge * -18f, 0f, sway * 40f);
            }
        }

        float GetSafeHandScale(Vector3 offset, float radius)
        {
            Vector3 origin = ViewCamera.transform.position;
            Vector3 worldOffset = ViewCamera.transform.TransformDirection(offset);
            float distance = worldOffset.magnitude;
            Vector3 direction = worldOffset / distance;
            int mask = ~(1 << 9);
            float scale = 1f;
            float extent = distance + radius;
            const float margin = .035f;
            if (Physics.SphereCast(origin, radius, direction, out RaycastHit hit, distance,
                mask, QueryTriggerInteraction.Ignore))
                scale = Mathf.Min(scale, (hit.distance + radius - margin) / extent);
            // Shape casts miss initial overlaps; center rays still catch nearby thin walls.
            if (Physics.Raycast(origin, direction, out hit, extent, mask, QueryTriggerInteraction.Ignore))
                scale = Mathf.Min(scale, (hit.distance - margin) / extent);
            float forwardExtent = offset.z + radius;
            if (Physics.Raycast(origin, ViewCamera.transform.forward, out hit, forwardExtent,
                mask, QueryTriggerInteraction.Ignore))
                scale = Mathf.Min(scale, (hit.distance - margin) / forwardExtent);
            scale = Mathf.Clamp(scale, .12f, 1f);
            // A glancing wall can touch the prop's side even when its center ray is clear.
            // Find the largest collision-free scaled sphere entirely on the camera side.
            if (Physics.CheckSphere(origin + worldOffset * scale, radius * scale + .01f,
                mask, QueryTriggerInteraction.Ignore))
            {
                float lower = .12f;
                float upper = scale;
                for (int i = 0; i < 7; i++)
                {
                    float middle = (lower + upper) * .5f;
                    if (Physics.CheckSphere(origin + worldOffset * middle, radius * middle + .01f,
                        mask, QueryTriggerInteraction.Ignore)) upper = middle;
                    else lower = middle;
                }
                scale = lower;
            }
            return scale;
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
