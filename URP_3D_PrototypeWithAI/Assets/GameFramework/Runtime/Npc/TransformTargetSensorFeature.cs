using UnityEngine;

namespace Rutin.GameFramework.Npc
{
    /// <summary>
    /// Allocation-free sensor for a target assigned by gameplay, interest management,
    /// or a server authority. It deliberately performs no global physics or scene scan.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcBrainFeature))]
    public sealed class TransformTargetSensorFeature : NpcSensorFeature
    {
        [SerializeField] private int sensorOrder;
        [SerializeField] private Transform target;

        [Min(0f)]
        [Tooltip("Zero disables target acquisition.")]
        [SerializeField] private float detectionRadius = 20f;

        [Min(0f)]
        [SerializeField] private float lossRadius = 25f;

        private bool _wasDetected;

        public override int SensorOrder => sensorOrder;

        public Transform Target => target;

        public void SetTarget(Transform value)
        {
            target = value;
            _wasDetected = false;
        }

        public void ConfigureRanges(float acquireRadius, float retainRadius)
        {
            detectionRadius = SanitizeNonNegative(acquireRadius);
            lossRadius = Mathf.Max(
                detectionRadius,
                SanitizeNonNegative(retainRadius));
            _wasDetected = false;
        }

        public override void Sense(
            ref NpcBlackboard blackboard,
            float deltaTime)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                _wasDetected = false;
                return;
            }

            Vector3 targetPosition = target.position;
            Vector3 offset = targetPosition - blackboard.AgentPosition;
            float acquireRadius = SanitizeNonNegative(detectionRadius);
            if (acquireRadius <= 0f)
            {
                _wasDetected = false;
                return;
            }

            float radius = _wasDetected
                ? Mathf.Max(
                    acquireRadius,
                    SanitizeNonNegative(lossRadius))
                : acquireRadius;
            if (offset.sqrMagnitude > radius * radius)
            {
                _wasDetected = false;
                return;
            }

            _wasDetected = blackboard.SetTarget(target, targetPosition);
        }

        public override void ResetNpcSensorState()
        {
            _wasDetected = false;
        }

        private static float SanitizeNonNegative(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Max(0f, value);
        }
    }
}
