using Rutin.GameFramework.Ticking;
using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// CharacterController locomotion driven only by PlayerCommandFeature snapshots.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerCommandFeature))]
    public sealed class PlayerCharacterMotorFeature : ScheduledEntityFeature
    {
        [Min(0f)]
        [SerializeField] private float moveSpeed = 5f;

        [Min(0f)]
        [SerializeField] private float acceleration = 30f;

        [SerializeField] private float gravity = -25f;

        [Min(0f)]
        [SerializeField] private float jumpHeight = 1.25f;

        [Min(0f)]
        [SerializeField] private float maximumFallSpeed = 50f;

        [SerializeField] private float groundedVerticalSpeed = -2f;
        [SerializeField] private Transform movementSpace;

        private CharacterController _controller;
        private PlayerCommandFeature _commands;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private uint _observedControlRevision;

        public override bool IsTickEnabled =>
            IsFeatureActive &&
            _controller != null &&
            _controller.enabled &&
            _commands != null &&
            _commands.IsSimulationEnabled;

        public Vector3 Velocity =>
            _horizontalVelocity + Vector3.up * _verticalVelocity;

        public void SetMovementSpace(Transform value)
        {
            movementSpace = value;
        }

        public override void Tick(float deltaTime)
        {
            if (!IsTickEnabled || deltaTime <= 0f)
            {
                return;
            }

            if (_observedControlRevision != _commands.ControlRevision)
            {
                ResetMotion();
                _observedControlRevision = _commands.ControlRevision;
            }

            PlayerCommand command = _commands.CurrentCommand;
            Vector3 desiredDirection = GetMovementDirection(command.Move);
            Vector3 desiredVelocity = desiredDirection * moveSpeed;
            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity,
                desiredVelocity,
                acceleration * deltaTime);

            bool grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = Mathf.Min(groundedVerticalSpeed, 0f);
            }

            float effectiveGravity = Mathf.Min(gravity, -0.001f);
            bool jumpRequested = _commands.ConsumeJumpPressed();
            if (grounded && jumpRequested)
            {
                _verticalVelocity = Mathf.Sqrt(
                    jumpHeight * -2f * effectiveGravity);
            }

            _verticalVelocity += effectiveGravity * deltaTime;
            _verticalVelocity = Mathf.Max(_verticalVelocity, -maximumFallSpeed);

            Vector3 displacement =
                (_horizontalVelocity + Vector3.up * _verticalVelocity) * deltaTime;
            _controller.Move(displacement);
        }

        protected override void OnScheduledFeatureInitialized()
        {
            _controller = GetComponent<CharacterController>();
            _commands = GetComponent<PlayerCommandFeature>();
            _observedControlRevision = _commands.ControlRevision;
        }

        protected override void OnScheduledFeatureDeactivated()
        {
            ResetMotion();
        }

        protected override void OnScheduledFeatureShutdown()
        {
            ResetMotion();
            _controller = null;
            _commands = null;
        }

        private Vector3 GetMovementDirection(Vector2 input)
        {
            Transform space = movementSpace != null ? movementSpace : transform;
            Vector3 forward = space.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                forward.Normalize();
            }
            else
            {
                forward = Vector3.forward;
            }

            Vector3 right = space.right;
            right.y = 0f;
            if (right.sqrMagnitude > 0.0001f)
            {
                right.Normalize();
            }
            else
            {
                right = Vector3.right;
            }

            return Vector3.ClampMagnitude(
                right * input.x + forward * input.y,
                1f);
        }

        private void ResetMotion()
        {
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
