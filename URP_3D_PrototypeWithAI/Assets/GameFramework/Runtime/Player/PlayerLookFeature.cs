using Rutin.GameFramework.Ticking;
using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// Applies command look deltas to separate yaw and pitch transforms.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCommandFeature))]
    public sealed class PlayerLookFeature : ScheduledEntityFeature
    {
        [SerializeField] private Transform yawRoot;
        [SerializeField] private Transform pitchPivot;

        [Range(-89f, 0f)]
        [SerializeField] private float minimumPitch = -80f;

        [Range(0f, 89f)]
        [SerializeField] private float maximumPitch = 80f;

        [Min(0f)]
        [SerializeField] private float maximumLookDelta = 90f;

        private PlayerCommandFeature _commands;
        private float _yaw;
        private float _pitch;

        public override int InitializationOrder => 100;

        public override bool IsTickEnabled =>
            IsFeatureActive &&
            _commands != null &&
            _commands.IsSimulationEnabled;

        public float Yaw => _yaw;

        public float Pitch => _pitch;

        public void SetViewTransforms(Transform newYawRoot, Transform newPitchPivot)
        {
            yawRoot = newYawRoot;
            pitchPivot = newPitchPivot;
            CaptureCurrentAngles();
        }

        public override void Tick(float deltaTime)
        {
            if (!IsTickEnabled)
            {
                return;
            }

            if (!_commands.TryConsumeLookDelta(out Vector2 look))
            {
                return;
            }

            look.x = Mathf.Clamp(look.x, -maximumLookDelta, maximumLookDelta);
            look.y = Mathf.Clamp(look.y, -maximumLookDelta, maximumLookDelta);

            _yaw = Mathf.Repeat(_yaw + look.x, 360f);
            _pitch = Mathf.Clamp(_pitch - look.y, minimumPitch, maximumPitch);

            Transform activeYawRoot = yawRoot != null ? yawRoot : transform;
            activeYawRoot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            if (pitchPivot != null)
            {
                pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }

        protected override void OnScheduledFeatureInitialized()
        {
            _commands = GetComponent<PlayerCommandFeature>();
            CaptureCurrentAngles();
        }

        protected override void OnScheduledFeatureShutdown()
        {
            _commands = null;
        }

        private void CaptureCurrentAngles()
        {
            Transform activeYawRoot = yawRoot != null ? yawRoot : transform;
            _yaw = activeYawRoot.localEulerAngles.y;
            _pitch = pitchPivot != null
                ? NormalizeSignedAngle(pitchPivot.localEulerAngles.x)
                : 0f;
            _pitch = Mathf.Clamp(_pitch, minimumPitch, maximumPitch);
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
