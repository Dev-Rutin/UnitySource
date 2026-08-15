using UnityEngine;

namespace Rutin.GunShop
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PhysicsHandMotor : MonoBehaviour
    {
        private const float MinimumPositiveValue = 0.0001f;

        [Header("References")]
        [SerializeField] private Rigidbody body;
        [SerializeField] private Transform workspaceOrigin;

        [Header("Position PD")]
        [SerializeField, Min(0f)] private float positionSpring = 180f;
        [SerializeField, Min(0f)] private float positionDamping = 24f;
        [SerializeField, Min(0f)] private float maximumForce = 120f;

        [Header("Rotation PD")]
        [SerializeField, Min(0f)] private float rotationSpring = 45f;
        [SerializeField, Min(0f)] private float rotationDamping = 8f;
        [SerializeField, Min(0f)] private float maximumTorque = 35f;

        [Header("Safety Limits")]
        [SerializeField, Min(MinimumPositiveValue)] private float maximumLinearSpeed = 4f;
        [SerializeField, Min(MinimumPositiveValue)] private float maximumAngularSpeed = 10f;
        [SerializeField, Min(MinimumPositiveValue)] private float maximumReach = 1.4f;
        [SerializeField, Min(1f)] private float abnormalDistanceMultiplier = 3f;

        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool initialized;

        public Rigidbody Body => body;

        public Vector3 TargetPosition { get; private set; }

        public Quaternion TargetRotation { get; private set; }

        public Vector3 LastAppliedForce { get; private set; }

        public Vector3 LastAppliedTorque { get; private set; }

        public float MaximumForce => maximumForce;

        public float MaximumTorque => maximumTorque;

        public float MaximumReach => maximumReach;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            ResetToHomePose();
        }

        private void OnDisable()
        {
            if (initialized && body != null)
            {
                ResetToHomePose();
            }
        }

        private void FixedUpdate()
        {
            StepPhysics(Time.fixedDeltaTime);
        }

        public void Configure(
            Transform newWorkspaceOrigin,
            float newMaximumReach,
            float newMaximumForce,
            float newMaximumTorque,
            float newMaximumLinearSpeed,
            float newMaximumAngularSpeed,
            float newPositionSpring = 180f,
            float newPositionDamping = 24f,
            float newRotationSpring = 45f,
            float newRotationDamping = 8f)
        {
            workspaceOrigin = newWorkspaceOrigin;
            maximumReach = Mathf.Max(MinimumPositiveValue, newMaximumReach);
            maximumForce = Mathf.Max(0f, newMaximumForce);
            maximumTorque = Mathf.Max(0f, newMaximumTorque);
            maximumLinearSpeed = Mathf.Max(MinimumPositiveValue, newMaximumLinearSpeed);
            maximumAngularSpeed = Mathf.Max(MinimumPositiveValue, newMaximumAngularSpeed);
            positionSpring = Mathf.Max(0f, newPositionSpring);
            positionDamping = Mathf.Max(0f, newPositionDamping);
            rotationSpring = Mathf.Max(0f, newRotationSpring);
            rotationDamping = Mathf.Max(0f, newRotationDamping);

            SetTargetPose(TargetPosition, TargetRotation);
        }

        public void SetTargetPose(Vector3 position, Quaternion rotation)
        {
            EnsureInitialized();

            if (!IsFinite(position) || !IsFinite(rotation))
            {
                ResetToHomePose();
                return;
            }

            var origin = workspaceOrigin != null ? workspaceOrigin.position : homePosition;
            var offset = Vector3.ClampMagnitude(position - origin, maximumReach);

            TargetPosition = origin + offset;
            TargetRotation = Quaternion.Normalize(rotation);
        }

        public void StepPhysics(float deltaTime)
        {
            EnsureInitialized();

            if (!IsBodyStateValid())
            {
                ResetToHomePose();
                return;
            }

            if (!IsFinite(deltaTime) || deltaTime <= 0f)
            {
                LastAppliedForce = Vector3.zero;
                LastAppliedTorque = Vector3.zero;
                return;
            }

            LimitVelocity();

            var positionError = TargetPosition - body.position;
            var force = positionSpring * positionError - positionDamping * body.linearVelocity;
            LastAppliedForce = Vector3.ClampMagnitude(force, maximumForce);
            body.AddForce(LastAppliedForce, ForceMode.Force);

            var angularError = CalculateAngularError(TargetRotation, body.rotation);
            var torque = rotationSpring * angularError - rotationDamping * body.angularVelocity;
            LastAppliedTorque = Vector3.ClampMagnitude(torque, maximumTorque);
            body.AddTorque(LastAppliedTorque, ForceMode.Force);
        }

        public void ResetToHomePose()
        {
            EnsureInitialized();

            body.position = homePosition;
            body.rotation = homeRotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            TargetPosition = homePosition;
            TargetRotation = homeRotation;
            LastAppliedForce = Vector3.zero;
            LastAppliedTorque = Vector3.zero;
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            homePosition = body.position;
            homeRotation = body.rotation;
            TargetPosition = homePosition;
            TargetRotation = homeRotation;
            initialized = true;
        }

        private bool IsBodyStateValid()
        {
            if (!IsFinite(body.position) ||
                !IsFinite(body.rotation) ||
                !IsFinite(body.linearVelocity) ||
                !IsFinite(body.angularVelocity))
            {
                return false;
            }

            var origin = workspaceOrigin != null ? workspaceOrigin.position : homePosition;
            var abnormalDistance = maximumReach * Mathf.Max(1f, abnormalDistanceMultiplier);
            return Vector3.Distance(body.position, origin) <= abnormalDistance;
        }

        private void LimitVelocity()
        {
            body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, maximumLinearSpeed);
            body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, maximumAngularSpeed);
        }

        private static Vector3 CalculateAngularError(Quaternion target, Quaternion current)
        {
            var delta = target * Quaternion.Inverse(current);
            if (delta.w < 0f)
            {
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            }

            delta.ToAngleAxis(out var angleDegrees, out var axis);
            if (!IsFinite(axis) || Mathf.Approximately(angleDegrees, 0f))
            {
                return Vector3.zero;
            }

            if (angleDegrees > 180f)
            {
                angleDegrees -= 360f;
            }

            return axis.normalized * (angleDegrees * Mathf.Deg2Rad);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w) &&
                   value.x * value.x +
                   value.y * value.y +
                   value.z * value.z +
                   value.w * value.w > MinimumPositiveValue;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
