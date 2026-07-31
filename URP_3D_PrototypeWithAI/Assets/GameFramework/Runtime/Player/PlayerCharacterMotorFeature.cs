using System;
using Rutin.GameFramework.Core;
using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// Fixed-step CharacterController locomotion driven by PlayerCommandFeature.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerCommandFeature))]
    public sealed class PlayerCharacterMotorFeature :
        EntityFeature,
        IPlayerCommandConsumer
    {
        [Min(0f)]
        [SerializeField] private float moveSpeed = 5f;

        [Min(0f)]
        [SerializeField] private float acceleration = 30f;

        [SerializeField] private float gravity = -25f;

        [Min(0f)]
        [SerializeField] private float jumpHeight = 1.25f;

        [Min(0f)]
        [SerializeField] private float jumpBufferSeconds = 0.1f;

        [Min(0f)]
        [SerializeField] private float maximumFallSpeed = 50f;

        [SerializeField] private float groundedVerticalSpeed = -2f;
        [SerializeField] private Transform movementSpace;

        [Min(0.001f)]
        [SerializeField] private float fixedStepSeconds = 1f / 60f;

        [Range(1, 32)]
        [SerializeField] private int maximumSubsteps = 16;

        private CharacterController _controller;
        private PlayerCommandFeature _commands;
        private Vector2 _desiredMove;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private double _accumulatedTime;
        private double _totalDiscardedSimulationTimeSeconds;
        private float _jumpBufferRemaining;
        private bool _usesLookMovementSpace;

        public int CommandOrder => 0;

        public Vector3 Velocity =>
            _horizontalVelocity + Vector3.up * _verticalVelocity;

        public Transform MovementSpace =>
            movementSpace != null ? movementSpace : transform;

        public double TotalDiscardedSimulationTimeSeconds =>
            _totalDiscardedSimulationTimeSeconds;

        public void SetMovementSpace(Transform value)
        {
            movementSpace = value;
            _usesLookMovementSpace = false;
        }

        internal void UseMovementSpaceIfUnset(Transform value)
        {
            if (movementSpace == null)
            {
                movementSpace = value;
                _usesLookMovementSpace = true;
            }
        }

        internal void UpdateLookMovementSpace(Transform value)
        {
            if (_usesLookMovementSpace)
            {
                movementSpace = value;
            }
        }

        public void ProcessPlayerCommand(PlayerCommand command, float deltaTime)
        {
            if (!IsFeatureActive ||
                _controller == null ||
                !_controller.enabled ||
                _commands == null ||
                !_commands.IsSimulationEnabled)
            {
                return;
            }

            _desiredMove = command.Move;
            if (command.JumpPressed)
            {
                _jumpBufferRemaining = Mathf.Max(
                    _jumpBufferRemaining,
                    Mathf.Max(jumpBufferSeconds, fixedStepSeconds));
            }

            double step = Math.Max(0.001d, fixedStepSeconds);
            int substepLimit = Mathf.Clamp(maximumSubsteps, 1, 32);
            double availableSimulationTime =
                _accumulatedTime + Math.Max(0d, deltaTime);
            double maximumSimulationTime = step * substepLimit;
            if (!command.HasSimulationDeltaTime &&
                availableSimulationTime > maximumSimulationTime)
            {
                _totalDiscardedSimulationTimeSeconds +=
                    availableSimulationTime - maximumSimulationTime;
                availableSimulationTime = maximumSimulationTime;
            }

            _accumulatedTime = availableSimulationTime;

            const double StepBoundaryTolerance = 0.000001d;
            int processedSteps = Math.Min(
                substepLimit,
                (int)Math.Floor(
                    (_accumulatedTime + StepBoundaryTolerance) / step));
            for (int i = 0; i < processedSteps; i++)
            {
                SimulateStep((float)step);
            }

            _accumulatedTime = Math.Max(
                0d,
                _accumulatedTime - processedSteps * step);
        }

        public void ResetPlayerCommandState()
        {
            ResetMotion();
        }

        protected override void OnFeatureInitialized()
        {
            _controller = GetComponent<CharacterController>();
            _commands = GetComponent<PlayerCommandFeature>();
            _commands.RegisterConsumer(this);

            if (movementSpace == null &&
                Owner.TryGetFeature(out PlayerLookFeature lookFeature))
            {
                movementSpace = lookFeature.MovementReference;
                _usesLookMovementSpace = true;
            }
        }

        protected override void OnFeatureDeactivated()
        {
            ResetMotion();
        }

        protected override void OnFeatureShutdown()
        {
            _commands?.UnregisterConsumer(this);
            ResetMotion();
            _controller = null;
            _commands = null;
            _usesLookMovementSpace = false;
        }

        private void SimulateStep(float step)
        {
            Vector3 desiredDirection = GetMovementDirection(_desiredMove);
            Vector3 desiredVelocity = desiredDirection * moveSpeed;
            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity,
                desiredVelocity,
                acceleration * step);

            bool grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = Mathf.Min(groundedVerticalSpeed, 0f);
            }

            float effectiveGravity = Mathf.Min(gravity, -0.001f);
            if (grounded && _jumpBufferRemaining > 0f)
            {
                _verticalVelocity = Mathf.Sqrt(
                    jumpHeight * -2f * effectiveGravity);
                _jumpBufferRemaining = 0f;
            }

            _verticalVelocity += effectiveGravity * step;
            _verticalVelocity = Mathf.Max(
                _verticalVelocity,
                -Mathf.Max(0f, maximumFallSpeed));

            Vector3 displacement =
                (_horizontalVelocity + Vector3.up * _verticalVelocity) * step;
            _controller.Move(displacement);
            _jumpBufferRemaining = Mathf.Max(
                0f,
                _jumpBufferRemaining - step);
        }

        private Vector3 GetMovementDirection(Vector2 input)
        {
            Transform space = MovementSpace;
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
            _desiredMove = Vector2.zero;
            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _accumulatedTime = 0f;
            _jumpBufferRemaining = 0f;
        }
    }
}
