using Rutin.GameFramework.Core;
using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// Applies command look deltas after simulation while preserving rig base rotations.
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

        public int CommandOrder => 100;

        public float Yaw => _yaw;

        public float Pitch => _pitch;

        public Transform MovementReference =>
            yawRoot != null ? yawRoot : transform;

        public void SetViewTransforms(Transform newYawRoot, Transform newPitchPivot)
        {
            yawRoot = newYawRoot;
            pitchPivot = newPitchPivot;
            CaptureBaseRotations();
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

            Transform activeYawRoot = MovementReference;
            activeYawRoot.localRotation =
                _baseYawRotation * Quaternion.AngleAxis(_yaw, Vector3.up);
            if (pitchPivot != null)
            {
                pitchPivot.localRotation =
                    _basePitchRotation * Quaternion.AngleAxis(_pitch, Vector3.right);
            }
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
    }
}
