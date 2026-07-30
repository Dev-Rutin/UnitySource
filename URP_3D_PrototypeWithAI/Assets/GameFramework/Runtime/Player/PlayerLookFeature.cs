using Rutin.GameFramework.Core;
using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// Applies command look deltas before movement while preserving rig base rotations.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCommandFeature))]
    public sealed class PlayerLookFeature :
        EntityFeature,
        IPlayerCommandConsumer
    {
        [SerializeField] private Transform yawRoot;
        [SerializeField] private Transform pitchPivot;

        [Range(-89f, 0f)]
        [SerializeField] private float minimumPitch = -80f;

        [Range(0f, 89f)]
        [SerializeField] private float maximumPitch = 80f;

        private PlayerCommandFeature _commands;
        private Quaternion _baseYawRotation;
        private Quaternion _basePitchRotation;
        private float _yaw;
        private float _pitch;

        public override int InitializationOrder => 100;

        public int CommandOrder => -100;

        public float Yaw => _yaw;

        public float Pitch => _pitch;

        public Transform MovementReference =>
            yawRoot != null ? yawRoot : transform;

        public void SetViewTransforms(
            Transform newYawRoot,
            Transform newPitchPivot,
            bool preserveViewAngles = true)
        {
            if (ReferenceEquals(yawRoot, newYawRoot) &&
                ReferenceEquals(pitchPivot, newPitchPivot))
            {
                UpdateMotorMovementReference();
                return;
            }

            float previousYaw = preserveViewAngles ? _yaw : 0f;
            float previousPitch = preserveViewAngles ? _pitch : 0f;
            if (IsFeatureInitialized)
            {
                RestoreBaseRotations();
            }
            yawRoot = newYawRoot;
            pitchPivot = newPitchPivot;
            CaptureBaseRotations();
            _yaw = previousYaw;
            _pitch = Mathf.Clamp(previousPitch, minimumPitch, maximumPitch);
            ApplyViewRotation();
            UpdateMotorMovementReference();
        }

        public void ProcessPlayerCommand(PlayerCommand command, float deltaTime)
        {
            if (!IsFeatureActive || !_commands.IsSimulationEnabled)
            {
                return;
            }

            Vector2 look = command.Look;
            _yaw = Mathf.Repeat(_yaw + look.x, 360f);
            _pitch = Mathf.Clamp(_pitch - look.y, minimumPitch, maximumPitch);

            ApplyViewRotation();
        }

        public void ResetPlayerCommandState()
        {
        }

        protected override void OnFeatureInitialized()
        {
            _commands = GetComponent<PlayerCommandFeature>();
            _commands.RegisterConsumer(this);
            CaptureBaseRotations();
            if (Owner.TryGetFeature(out PlayerCharacterMotorFeature motor))
            {
                motor.UseMovementSpaceIfUnset(MovementReference);
            }
        }

        protected override void OnFeatureShutdown()
        {
            _commands?.UnregisterConsumer(this);
            _commands = null;
        }

        private void ApplyViewRotation()
        {
            Transform activeYawRoot = MovementReference;
            activeYawRoot.localRotation =
                _baseYawRotation * Quaternion.AngleAxis(_yaw, Vector3.up);
            if (pitchPivot != null)
            {
                pitchPivot.localRotation =
                    _basePitchRotation * Quaternion.AngleAxis(_pitch, Vector3.right);
            }
        }

        private void CaptureBaseRotations()
        {
            Transform activeYawRoot = MovementReference;
            _baseYawRotation = activeYawRoot.localRotation;
            _basePitchRotation = pitchPivot != null
                ? pitchPivot.localRotation
                : Quaternion.identity;
            _yaw = 0f;
            _pitch = 0f;
        }

        private void RestoreBaseRotations()
        {
            Transform activeYawRoot = MovementReference;
            activeYawRoot.localRotation = _baseYawRotation;
            if (pitchPivot != null)
            {
                pitchPivot.localRotation = _basePitchRotation;
            }
        }

        private void UpdateMotorMovementReference()
        {
            if (Owner != null &&
                Owner.TryGetFeature(out PlayerCharacterMotorFeature motor))
            {
                motor.UpdateLookMovementSpace(MovementReference);
            }
        }
    }
}
