using System;
using UnityEngine;

namespace Rutin.GameFramework.Npc
{
    /// <summary>
    /// Basic policy example: chase perceived targets, otherwise patrol configured points,
    /// otherwise remain idle. Replace or precede it with another provider for richer AI.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NpcBrainFeature))]
    public sealed class IdlePatrolChaseDecisionFeature :
        NpcDecisionProviderFeature
    {
        [SerializeField] private int decisionOrder;
        [SerializeField] private Transform[] patrolPoints = Array.Empty<Transform>();

        [Min(0f)]
        [SerializeField] private float patrolAcceptanceRadius = 0.25f;

        [Min(0f)]
        [SerializeField] private float chaseStopDistance = 1.5f;

        [Range(0f, 1f)]
        [SerializeField] private float patrolMoveAmount = 0.6f;

        [Range(0f, 1f)]
        [SerializeField] private float chaseMoveAmount = 1f;

        private int _patrolIndex;

        public override int DecisionOrder => decisionOrder;

        public void SetPatrolPoints(Transform[] points)
        {
            patrolPoints = points ?? Array.Empty<Transform>();
            _patrolIndex = 0;
        }

        public override bool TryDecide(
            in NpcBlackboard blackboard,
            float deltaTime,
            out NpcDecision decision)
        {
            if (blackboard.HasTarget)
            {
                decision = CreateMovementDecision(
                    NpcBehaviourState.Chase,
                    blackboard.AgentPosition,
                    blackboard.TargetPosition,
                    chaseStopDistance,
                    chaseMoveAmount);
                return true;
            }

            int pointCount = patrolPoints?.Length ?? 0;
            for (int attempts = 0; attempts < pointCount; attempts++)
            {
                if (_patrolIndex < 0 || _patrolIndex >= pointCount)
                {
                    _patrolIndex = 0;
                }

                Transform patrolPoint = patrolPoints[_patrolIndex];
                if (patrolPoint == null)
                {
                    AdvancePatrolIndex(pointCount);
                    continue;
                }

                Vector3 planarOffset = patrolPoint.position -
                    blackboard.AgentPosition;
                planarOffset.y = 0f;
                float acceptance = SanitizeNonNegative(
                    patrolAcceptanceRadius);
                if (planarOffset.sqrMagnitude <= acceptance * acceptance)
                {
                    AdvancePatrolIndex(pointCount);
                    continue;
                }

                decision = new NpcDecision(
                    NpcBehaviourState.Patrol,
                    planarOffset.normalized * Mathf.Clamp01(patrolMoveAmount));
                return true;
            }

            decision = NpcDecision.Idle;
            return true;
        }

        public override void ResetNpcDecisionState()
        {
            _patrolIndex = 0;
        }

        private static NpcDecision CreateMovementDecision(
            NpcBehaviourState state,
            Vector3 origin,
            Vector3 destination,
            float stopDistance,
            float moveAmount)
        {
            Vector3 offset = destination - origin;
            offset.y = 0f;
            float sanitizedStopDistance = SanitizeNonNegative(stopDistance);
            if (offset.sqrMagnitude <=
                sanitizedStopDistance * sanitizedStopDistance)
            {
                return new NpcDecision(state, Vector3.zero);
            }

            return new NpcDecision(
                state,
                offset.normalized * Mathf.Clamp01(moveAmount));
        }

        private void AdvancePatrolIndex(int pointCount)
        {
            _patrolIndex = pointCount > 0
                ? (_patrolIndex + 1) % pointCount
                : 0;
        }

        private static float SanitizeNonNegative(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Max(0f, value);
        }
    }
}
