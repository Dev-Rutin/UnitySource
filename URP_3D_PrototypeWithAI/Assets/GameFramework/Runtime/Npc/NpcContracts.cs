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
    /// normalized intent so the brain can adapt it to the command stack's movement space.
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
            JumpPressed = jumpPressed;
        }

        public NpcBehaviourState State { get; }

        public Vector3 WorldMove { get; }

        public bool JumpPressed { get; }

        public static NpcDecision Idle => default;

        private static Vector3 SanitizeDirection(Vector3 value)
        {
            value.x = SanitizeFinite(value.x);
            value.y = 0f;
            value.z = SanitizeFinite(value.z);
            return Vector3.ClampMagnitude(value, 1f);
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
    /// stale targets therefore cannot survive sensor removal, pooling, or authority changes.
    /// </summary>
    public struct NpcBlackboard
    {
        public Vector3 AgentPosition { get; private set; }

        public Vector3 HomePosition { get; private set; }

        public bool HasTarget { get; private set; }

        public Object Target { get; private set; }

        public Vector3 TargetPosition { get; private set; }

        public float TargetDistanceSquared { get; private set; }

        public bool SetTarget(Object target, Vector3 position)
        {
            if (!IsFinite(position))
            {
                ClearTarget();
                return false;
            }

            Vector3 offset = position - AgentPosition;
            float distanceSquared = offset.sqrMagnitude;
            HasTarget = true;
            Target = target;
            TargetPosition = position;
            TargetDistanceSquared = float.IsNaN(distanceSquared) ||
                float.IsInfinity(distanceSquared)
                    ? float.MaxValue
                    : distanceSquared;
            return true;
        }

        public void ClearTarget()
        {
            HasTarget = false;
            Target = null;
            TargetPosition = Vector3.zero;
            TargetDistanceSquared = float.PositiveInfinity;
        }

        internal void Reset(Vector3 agentPosition)
        {
            AgentPosition = SanitizePosition(agentPosition);
            HomePosition = AgentPosition;
            ClearTarget();
        }

        internal void BeginSensing(Vector3 agentPosition)
        {
            AgentPosition = SanitizePosition(agentPosition);
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
