using UnityEngine;

namespace Rutin.GameFramework.Npc
{
    public enum NpcBehaviourState
    {
        Idle = 0,
        Patrol = 1,
        Chase = 2
    }

    /// <summary>
    /// Immutable result produced by an NPC decision provider. WorldMove is a planar,
    /// normalized intent that the brain transports as an absolute world-space command.
    /// WorldFacing optionally remains meaningful when movement is zero.
    /// </summary>
    public readonly struct NpcDecision
    {
        public NpcDecision(
            NpcBehaviourState state,
            Vector3 worldMove,
            bool jumpPressed = false)
        {
            State = state;
            WorldMove = SanitizeDirection(worldMove);
            WorldFacing = NormalizeDirection(WorldMove);
            HasWorldFacing = WorldFacing.sqrMagnitude > 0f;
            JumpPressed = jumpPressed;
        }

        public NpcDecision(
            NpcBehaviourState state,
            Vector3 worldMove,
            Vector3 worldFacing,
            bool jumpPressed = false)
        {
            State = state;
            WorldMove = SanitizeDirection(worldMove);
            WorldFacing = NormalizeDirection(
                SanitizeDirection(worldFacing));
            HasWorldFacing = WorldFacing.sqrMagnitude > 0f;
            JumpPressed = jumpPressed;
        }

        public NpcBehaviourState State { get; }

        public Vector3 WorldMove { get; }

        public Vector3 WorldFacing { get; }

        public bool HasWorldFacing { get; }

        public bool JumpPressed { get; }

        public static NpcDecision Idle => default;

        private static Vector3 SanitizeDirection(Vector3 value)
        {
            value.x = SanitizeFinite(value.x);
            value.y = 0f;
            value.z = SanitizeFinite(value.z);
            return Vector3.ClampMagnitude(value, 1f);
        }

        private static Vector3 NormalizeDirection(Vector3 value)
        {
            return value.sqrMagnitude > 0.0001f
                ? value.normalized
                : Vector3.zero;
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }
    }

    /// <summary>
    /// Per-agent, value-type working memory. Sensors repopulate perception on each decision;
    /// stale targets therefore cannot survive the next sensing pass after sensor removal,
    /// pooling, or authority changes.
    /// </summary>
    public struct NpcBlackboard
    {
        private Vector3 _agentPosition;
        private Vector3 _homePosition;
        private bool _hasTarget;
        private Object _target;
        private Vector3 _targetPosition;
        private float _targetDistanceSquared;

        public readonly Vector3 AgentPosition => _agentPosition;

        public readonly Vector3 HomePosition => _homePosition;

        public readonly bool HasTarget => _hasTarget;

        public readonly Object Target => _target;

        public readonly Vector3 TargetPosition => _targetPosition;

        public readonly float TargetDistanceSquared =>
            _targetDistanceSquared;

        public bool SetTarget(Object target, Vector3 position)
        {
            if (!IsFinite(position))
            {
                ClearTarget();
                return false;
            }

            Vector3 offset = position - _agentPosition;
            float distanceSquared = offset.sqrMagnitude;
            _hasTarget = true;
            _target = target;
            _targetPosition = position;
            _targetDistanceSquared = float.IsNaN(distanceSquared) ||
                float.IsInfinity(distanceSquared)
                    ? float.MaxValue
                    : distanceSquared;
            return true;
        }

        public void ClearTarget()
        {
            _hasTarget = false;
            _target = null;
            _targetPosition = Vector3.zero;
            _targetDistanceSquared = float.PositiveInfinity;
        }

        internal void Reset(Vector3 agentPosition)
        {
            _agentPosition = SanitizePosition(agentPosition);
            _homePosition = _agentPosition;
            ClearTarget();
        }

        internal void BeginSensing(Vector3 agentPosition)
        {
            _agentPosition = SanitizePosition(agentPosition);
            ClearTarget();
        }

        private static Vector3 SanitizePosition(Vector3 value)
        {
            return IsFinite(value) ? value : Vector3.zero;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public interface INpcSensor
    {
        int SensorOrder { get; }

        void Sense(ref NpcBlackboard blackboard, float deltaTime);

        void ResetNpcSensorState();
    }

    public interface INpcDecisionProvider
    {
        int DecisionOrder { get; }

        bool TryDecide(
            in NpcBlackboard blackboard,
            float deltaTime,
            out NpcDecision decision);

        void ResetNpcDecisionState();
    }
}
