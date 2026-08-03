using Rutin.GameFramework.Core;
using Rutin.GameFramework.Player;
using UnityEngine;

namespace Rutin.GameFramework.Npc
{
    /// <summary>
    /// Optional visual-facing consumer for absolute NPC movement commands. Facing is derived
    /// from every world-space snapshot, so a later packet repairs orientation after packet loss.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCommandFeature))]
    public sealed class NpcFacingFeature :
        EntityFeature,
        IPlayerCommandConsumer
    {
        [SerializeField] private Transform yawRoot;

        [Min(0f)]
        [Tooltip("Zero snaps immediately; positive values limit yaw speed in degrees per second.")]
        [SerializeField] private float turnSpeedDegreesPerSecond;

        private PlayerCommandFeature _commands;
        private Transform _capturedYawRoot;
        private Quaternion _baseYawRotation;
        private bool _hasCapturedBaseRotation;

        public int CommandOrder => -100;

        public Transform YawRoot => yawRoot != null ? yawRoot : transform;

        public void SetYawRoot(Transform value)
        {
            if (ReferenceEquals(yawRoot, value))
            {
                return;
            }

            RestoreBaseRotation();
            yawRoot = value;
            CaptureBaseRotation();
        }

        public void SetTurnSpeed(float degreesPerSecond)
        {
            turnSpeedDegreesPerSecond = SanitizeNonNegative(degreesPerSecond);
        }

        public void ProcessPlayerCommand(PlayerCommand command, float deltaTime)
        {
            if (!IsFeatureActive)
            {
                return;
            }

            Transform activeYawRoot = YawRoot;
            Vector3 worldForward = command.HasWorldFacing
                ? command.GetWorldFacingDirection()
                : command.MoveSpace == PlayerCommandMoveSpace.World
                    ? command.GetWorldMoveDirection(null)
                    : Vector3.zero;
            if (worldForward.sqrMagnitude <= 0.0001f)
            {
                return;
            }
            Vector3 localForward = activeYawRoot.parent != null
                ? activeYawRoot.parent.InverseTransformDirection(worldForward)
                : worldForward;
            localForward.y = 0f;
            if (localForward.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            float yawDegrees = Mathf.Atan2(
                localForward.x,
                localForward.z) * Mathf.Rad2Deg;
            Quaternion absoluteYaw =
                Quaternion.AngleAxis(yawDegrees, Vector3.up);
            Quaternion targetRotation = UsesEntityRoot
                ? absoluteYaw
                : _baseYawRotation * absoluteYaw;
            float turnSpeed = SanitizeNonNegative(turnSpeedDegreesPerSecond);
            activeYawRoot.localRotation = turnSpeed <= 0f
                ? targetRotation
                : Quaternion.RotateTowards(
                    activeYawRoot.localRotation,
                    targetRotation,
                    turnSpeed * SanitizeNonNegative(deltaTime));
        }

        public void ResetPlayerCommandState()
        {
            RestoreBaseRotation();
        }

        protected override void OnFeatureInitialized()
        {
            _commands = GetComponent<PlayerCommandFeature>();
            CaptureBaseRotation();
            _commands.RegisterConsumer(this);
        }

        protected override void OnFeatureShutdown()
        {
            _commands?.UnregisterConsumer(this);
            RestoreBaseRotation();
            _capturedYawRoot = null;
            _hasCapturedBaseRotation = false;
            _commands = null;
        }

        private void CaptureBaseRotation()
        {
            _capturedYawRoot = null;
            _hasCapturedBaseRotation = false;
            if (!IsFeatureInitialized || UsesEntityRoot)
            {
                return;
            }

            _capturedYawRoot = YawRoot;
            _baseYawRotation = _capturedYawRoot.localRotation;
            _hasCapturedBaseRotation = true;
        }

        private void RestoreBaseRotation()
        {
            if (!_hasCapturedBaseRotation || _capturedYawRoot == null)
            {
                _capturedYawRoot = null;
                _hasCapturedBaseRotation = false;
                return;
            }

            _capturedYawRoot.localRotation = _baseYawRotation;
        }

        private bool UsesEntityRoot =>
            yawRoot == null || ReferenceEquals(yawRoot, transform);

        private static float SanitizeNonNegative(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Max(0f, value);
        }
    }
}
