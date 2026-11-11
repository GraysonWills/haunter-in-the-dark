using UnityEngine;

namespace Haunter
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ThrowableObject : MonoBehaviour
    {
        public string DisplayName = "FOUND OBJECT";
        public bool IsHeld { get; private set; }
        public Rigidbody Body { get { EnsureComponents(); return body; } }
        public float HoldRadius { get { EnsureComponents(); return holdRadius; } }

        Rigidbody body;
        Collider[] shapeColliders;
        bool[] colliderStates;
        Collider carrierCollider;
        Camera carrierCamera;
        bool ignoringCarrier;
        float lastImpact = -10f;
        float releaseTime;
        float safeRadius = .18f;
        float holdRadius = .22f;
        Vector3 originalWorldScale = Vector3.one;

        void Awake() { EnsureComponents(); }

        void EnsureComponents()
        {
            if (body != null) return;
            body = GetComponent<Rigidbody>();
            shapeColliders = GetComponentsInChildren<Collider>();
            colliderStates = new bool[shapeColliders.Length];
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 16f;
            foreach (Transform child in GetComponentsInChildren<Transform>()) child.gameObject.layer = 8;
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            foreach (Collider shape in shapeColliders) bounds.Encapsulate(shape.bounds);
            safeRadius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z), .08f, .3f);
            holdRadius = Mathf.Max(.08f, bounds.extents.magnitude + Vector3.Distance(bounds.center, transform.position));
        }

        public void Pickup(Transform anchor, Collider playerCollider)
        {
            EnsureComponents();
            if (IsHeld || anchor == null) return;
            RestoreCarrierCollision();
            originalWorldScale = transform.lossyScale;
            carrierCollider = playerCollider;
            carrierCamera = anchor.GetComponentInParent<Camera>();
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            // Physics interpolation otherwise reapplies a stale world pose after the camera
            // parent moves, defeating viewmodel pullback and potentially hiding it in a wall.
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
            for (int i = 0; i < shapeColliders.Length; i++)
            {
                colliderStates[i] = shapeColliders[i].enabled;
                shapeColliders[i].enabled = false;
            }
            IsHeld = true;
            transform.SetParent(anchor, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.Euler(-12f, -15f, -14f);
            body.position = transform.position;
            body.rotation = transform.rotation;
            EchoMaterial.SetPersistent(gameObject, true);
        }

        public void Throw(Vector3 velocity) { Release(velocity); }
        public void Drop() { Release(Vector3.down * .35f); }

        void Release(Vector3 velocity)
        {
            EnsureComponents();
            if (!IsHeld) return;
            Physics.SyncTransforms();
            Vector3 position = transform.position;
            if (carrierCamera != null)
            {
                Vector3 origin = carrierCamera.transform.position;
                Vector3 forward = carrierCamera.transform.forward;
                float distance = .7f;
                // The carried collider is disabled. Exclude only the player; walls and other props
                // still shorten the release distance, so an object cannot be thrown through a wall.
                if (Physics.SphereCast(origin, safeRadius, forward, out RaycastHit hit, distance,
                    ~(1 << 9), QueryTriggerInteraction.Ignore)) distance = Mathf.Max(.02f, hit.distance - .04f);
                // A shape cast can miss when its starting sphere overlaps a surface.
                // A center ray still prevents release on the far side of that surface.
                if (Physics.Raycast(origin, forward, out hit, distance, ~(1 << 9), QueryTriggerInteraction.Ignore))
                    distance = Mathf.Max(0f, hit.distance - safeRadius - .04f);
                position = origin + forward * distance;
                if (Physics.CheckSphere(position, safeRadius, ~(1 << 9), QueryTriggerInteraction.Ignore))
                {
                    Vector3[] offsets = { Vector3.zero, -forward * .35f, carrierCamera.transform.right * .35f,
                        -carrierCamera.transform.right * .35f, Vector3.down * .4f };
                    bool foundSpace = false;
                    foreach (Vector3 offset in offsets)
                    {
                        Vector3 candidate = origin + offset;
                        if (Physics.CheckSphere(candidate, safeRadius, ~(1 << 9), QueryTriggerInteraction.Ignore)) continue;
                        if (offset.sqrMagnitude > .001f && Physics.Raycast(origin, offset.normalized, offset.magnitude,
                            ~(1 << 9), QueryTriggerInteraction.Ignore)) continue;
                        position = candidate;
                        foundSpace = true;
                        break;
                    }
                    // Keep holding if no safe release position exists; retry after stepping back.
                    if (!foundSpace) return;
                }
            }
            Quaternion releaseRotation = transform.rotation;
            transform.SetParent(null, true);
            // Viewmodel pullback scales only its presentation; physics retains its original size.
            transform.localScale = originalWorldScale;
            transform.SetPositionAndRotation(position, releaseRotation);
            IsHeld = false;
            for (int i = 0; i < shapeColliders.Length; i++) shapeColliders[i].enabled = colliderStates[i];
            body.isKinematic = false;
            body.position = position;
            body.rotation = releaseRotation;
            Physics.SyncTransforms();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.velocity = velocity;
            body.angularVelocity = Random.onUnitSphere * Random.Range(3f, 9f);
            body.WakeUp();
            releaseTime = Time.time;
            lastImpact = Time.time - .2f;
            EchoMaterial.SetPersistent(gameObject, false);
            if (carrierCollider != null)
            {
                foreach (Collider shape in shapeColliders)
                    if (shape.enabled) Physics.IgnoreCollision(shape, carrierCollider, true);
                ignoringCarrier = true;
            }
        }

        void Update()
        {
            if (!ignoringCarrier || IsHeld || Time.time - releaseTime < .2f) return;
            bool separated = true;
            if (carrierCollider != null)
                foreach (Collider shape in shapeColliders)
                    if (shape.enabled && shape.bounds.Intersects(carrierCollider.bounds)) separated = false;
            if (separated) RestoreCarrierCollision();
        }

        void RestoreCarrierCollision()
        {
            if (ignoringCarrier && carrierCollider != null && shapeColliders != null)
                foreach (Collider shape in shapeColliders)
                    if (shape != null) Physics.IgnoreCollision(shape, carrierCollider, false);
            ignoringCarrier = false;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (IsHeld || Time.time - lastImpact < .18f) return;
            if (GameSession.Instance != null && (GameSession.Instance.State != SessionState.Playing
                || GameSession.Instance.ElapsedSeconds < 1f)) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < .65f) return;
            lastImpact = Time.time;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            NoiseSystem.Emit(point, Mathf.Clamp(.18f + speed * .1f, .28f, 1.5f), NoiseKind.Impact, gameObject);
        }

        void OnDestroy() { RestoreCarrierCollision(); }
    }
}
