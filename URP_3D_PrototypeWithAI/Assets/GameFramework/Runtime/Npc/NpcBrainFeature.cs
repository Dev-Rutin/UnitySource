using System;
using System.Collections.Generic;
using Rutin.GameFramework.Core;
using Rutin.GameFramework.Player;
using UnityEngine;

namespace Rutin.GameFramework.Npc
{
    /// <summary>
    /// Allocation-conscious NPC composition root. It is sampled by PlayerCommandFeature,
    /// keeping an NPC stack on one central scheduler registration instead of adding Update loops.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCommandFeature))]
    public sealed class NpcBrainFeature :
        EntityFeature,
        IPlayerCommandSource,
        IBufferedPlayerCommandSource
    {
        [SerializeField] private bool decisionEnabled = true;

        [Min(0f)]
        [SerializeField] private float decisionIntervalSeconds = 0.1f;

        [Tooltip("Negative values deterministically stagger the first decision across the interval.")]
        [SerializeField] private float initialDecisionDelaySeconds = -1f;

        [SerializeField] private Transform movementSpace;

        private readonly List<INpcSensor> _sensors = new(4);
        private readonly List<INpcSensor> _sensorSnapshot = new(4);
        private readonly List<INpcSensor> _sensorResetSnapshot = new(4);
        private readonly List<INpcDecisionProvider> _decisionProviders = new(4);
        private readonly List<INpcDecisionProvider> _decisionSnapshot = new(4);
        private readonly List<INpcDecisionProvider> _decisionResetSnapshot = new(4);
        private PlayerCommandFeature _commands;
        private PlayerCharacterMotorFeature _motor;
        private PlayerLookFeature _look;
        private NpcBlackboard _blackboard;
        private NpcDecision _currentDecision;
        private float _decisionElapsedSeconds;
        private float _timeUntilNextDecisionSeconds;
        private bool _pendingJump;
        private bool _hasExplicitStaggerSeed;
        private uint _staggerSeed;
        private uint _commandSequence;
        private uint _decisionCount;
        private uint _evaluationGeneration;
        private bool _runtimeStateClean;
        private bool _isResettingRuntimeState;
        private bool _isResettingParticipants;

        public override int InitializationOrder => -150;

        public bool IsInputAvailable => IsFeatureActive && decisionEnabled;

        public bool IsDecisionEnabled => decisionEnabled;

        public float DecisionIntervalSeconds => decisionIntervalSeconds;

        public float TimeUntilNextDecisionSeconds =>
            Mathf.Max(0f, _timeUntilNextDecisionSeconds);

        public Transform MovementSpace =>
            _motor != null
                ? _motor.MovementSpace
                : movementSpace != null
                    ? movementSpace
                    : transform;

        public bool HasExplicitStaggerSeed => _hasExplicitStaggerSeed;

        public NpcBlackboard Blackboard => _blackboard;

        public NpcDecision CurrentDecision => _currentDecision;

        public uint DecisionCount => _decisionCount;

        public void SetDecisionEnabled(bool value)
        {
            if (decisionEnabled == value)
            {
                return;
            }

            decisionEnabled = value;
            ResetRuntimeState(true);
        }

        /// <summary>
        /// Configures thinking cadence. A negative initial delay enables deterministic
        /// per-instance staggering; zero requests an immediate first decision.
        /// </summary>
        public void ConfigureDecisionCadence(
            float intervalSeconds,
            float initialDelaySeconds = -1f)
        {
            decisionIntervalSeconds = SanitizeNonNegative(intervalSeconds);
            initialDecisionDelaySeconds = IsFinite(initialDelaySeconds)
                ? initialDelaySeconds
                : -1f;
            _decisionElapsedSeconds = 0f;
            _timeUntilNextDecisionSeconds = ResolveInitialDelay();
        }

        /// <summary>
        /// Uses a stable spawn/network identifier to make automatic staggering repeatable
        /// across processes and replay sessions. Explicit initial delays still take priority.
        /// </summary>
        public void SetStaggerSeed(uint seed)
        {
            _hasExplicitStaggerSeed = true;
            _staggerSeed = seed;
            _decisionElapsedSeconds = 0f;
            _timeUntilNextDecisionSeconds = ResolveInitialDelay();
        }

        public void ClearStaggerSeed()
        {
            _hasExplicitStaggerSeed = false;
            _staggerSeed = 0;
            _decisionElapsedSeconds = 0f;
            _timeUntilNextDecisionSeconds = ResolveInitialDelay();
        }

        public void SetMovementSpace(Transform value)
        {
            movementSpace = value;
            _motor?.SetMovementSpace(value);
        }

        public bool RegisterSensor(INpcSensor sensor)
        {
            if (sensor == null ||
                IsDestroyedUnityObject(sensor) ||
                IndexOfSensor(sensor) >= 0)
            {
                return false;
            }

            int insertIndex = _sensors.Count;
            for (int i = 0; i < _sensors.Count; i++)
            {
                if (_sensors[i].SensorOrder > sensor.SensorOrder)
                {
                    insertIndex = i;
                    break;
                }
            }

            _sensors.Insert(insertIndex, sensor);
            _runtimeStateClean = false;
            return true;
        }

        public bool UnregisterSensor(INpcSensor sensor)
        {
            int index = IndexOfSensor(sensor);
            if (index < 0)
            {
                return false;
            }

            _sensors.RemoveAt(index);
            _blackboard.ClearTarget();
            _runtimeStateClean = false;
            return true;
        }

        public bool RegisterDecisionProvider(INpcDecisionProvider provider)
        {
            if (provider == null ||
                IsDestroyedUnityObject(provider) ||
                IndexOfDecisionProvider(provider) >= 0)
            {
                return false;
            }

            int insertIndex = _decisionProviders.Count;
            for (int i = 0; i < _decisionProviders.Count; i++)
            {
                if (_decisionProviders[i].DecisionOrder > provider.DecisionOrder)
                {
                    insertIndex = i;
                    break;
                }
            }

            _decisionProviders.Insert(insertIndex, provider);
            _runtimeStateClean = false;
            return true;
        }

        public bool UnregisterDecisionProvider(INpcDecisionProvider provider)
        {
            int index = IndexOfDecisionProvider(provider);
            if (index < 0)
            {
                return false;
            }

            _decisionProviders.RemoveAt(index);
            _currentDecision = NpcDecision.Idle;
            _pendingJump = false;
            _runtimeStateClean = false;
            return true;
        }

        public PlayerCommand ReadCommand(float deltaTime)
        {
            if (!IsInputAvailable)
            {
                return PlayerCommand.Neutral;
            }

            _runtimeStateClean = false;
            float elapsed = SanitizeNonNegative(deltaTime);
            _decisionElapsedSeconds = SaturatingAdd(
                _decisionElapsedSeconds,
                elapsed);
            _timeUntilNextDecisionSeconds -= elapsed;
            if (decisionIntervalSeconds <= 0f ||
                _timeUntilNextDecisionSeconds <= 0f)
            {
                uint generation = _evaluationGeneration;
                EvaluateDecision(_decisionElapsedSeconds);
                if (generation != _evaluationGeneration ||
                    !IsInputAvailable)
                {
                    return PlayerCommand.Neutral;
                }

                _decisionElapsedSeconds = 0f;
                _timeUntilNextDecisionSeconds =
                    SanitizeNonNegative(decisionIntervalSeconds);
            }

            BuildMovementAndLookCommand(
                _currentDecision.WorldMove,
                out Vector2 move,
                out Vector2 look);
            bool jumpPressed = _pendingJump;
            _pendingJump = false;
            return new PlayerCommand(
                move,
                look,
                jumpPressed,
                NextCommandSequence());
        }

        public void DiscardBufferedInput()
        {
            ResetRuntimeState(false);
        }

        protected override void OnFeatureInitialized()
        {
            _commands = GetComponent<PlayerCommandFeature>();
            _motor = GetComponent<PlayerCharacterMotorFeature>();
            _look = GetComponent<PlayerLookFeature>();
            if (_motor != null && movementSpace != null)
            {
                _motor.SetMovementSpace(movementSpace);
            }

            _commands.SetCommandSource(this);
            ResetRuntimeState(true);
        }

        protected override void OnFeatureActivated()
        {
            ResetRuntimeState(true);
        }

        protected override void OnFeatureDeactivated()
        {
            ResetRuntimeState(true);
        }

        protected override void OnFeatureShutdown()
        {
            ResetRuntimeState(true);
            if (_commands != null &&
                ReferenceEquals(_commands.CommandSource, this))
            {
                _commands.SetCommandSource(null);
            }

            _commands = null;
            _motor = null;
            _look = null;
            _sensors.Clear();
            _sensorSnapshot.Clear();
            _sensorResetSnapshot.Clear();
            _decisionProviders.Clear();
            _decisionSnapshot.Clear();
            _decisionResetSnapshot.Clear();
        }

        private void EvaluateDecision(float deltaTime)
        {
            uint generation = _evaluationGeneration;
            _blackboard.BeginSensing(transform.position);
            RunSensors(deltaTime, generation);
            if (generation != _evaluationGeneration)
            {
                return;
            }

            NpcDecision nextDecision = NpcDecision.Idle;
            _decisionSnapshot.Clear();
            _decisionSnapshot.AddRange(_decisionProviders);
            try
            {
                for (int i = 0; i < _decisionSnapshot.Count; i++)
                {
                    if (generation != _evaluationGeneration)
                    {
                        break;
                    }

                    INpcDecisionProvider provider = _decisionSnapshot[i];
                    if (IndexOfDecisionProvider(provider) < 0)
                    {
                        continue;
                    }

                    if (IsDestroyedUnityObject(provider))
                    {
                        UnregisterDecisionProvider(provider);
                        continue;
                    }

                    if (!IsParticipantEnabled(provider))
                    {
                        continue;
                    }

                    try
                    {
                        if (provider.TryDecide(
                            in _blackboard,
                            deltaTime,
                            out NpcDecision candidate))
                        {
                            nextDecision = candidate;
                            break;
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(
                            exception,
                            provider as UnityEngine.Object);
                        UnregisterDecisionProvider(provider);
                    }
                }
            }
            finally
            {
                _decisionSnapshot.Clear();
            }

            if (generation != _evaluationGeneration)
            {
                return;
            }

            _currentDecision = nextDecision;
            _pendingJump |= nextDecision.JumpPressed;
            _decisionCount++;
        }

        private void RunSensors(float deltaTime, uint generation)
        {
            _sensorSnapshot.Clear();
            _sensorSnapshot.AddRange(_sensors);
            try
            {
                for (int i = 0; i < _sensorSnapshot.Count; i++)
                {
                    if (generation != _evaluationGeneration)
                    {
                        break;
                    }

                    INpcSensor sensor = _sensorSnapshot[i];
                    if (IndexOfSensor(sensor) < 0)
                    {
                        continue;
                    }

                    if (IsDestroyedUnityObject(sensor))
                    {
                        UnregisterSensor(sensor);
                        continue;
                    }

                    if (!IsParticipantEnabled(sensor))
                    {
                        continue;
                    }

                    try
                    {
                        sensor.Sense(ref _blackboard, deltaTime);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(
                            exception,
                            sensor as UnityEngine.Object);
                        UnregisterSensor(sensor);
                    }
                }
            }
            finally
            {
                _sensorSnapshot.Clear();
            }
        }

        private void ResetRuntimeState(bool force)
        {
            if (!force && _runtimeStateClean)
            {
                return;
            }

            _evaluationGeneration++;
            if (_isResettingRuntimeState)
            {
                return;
            }

            _isResettingRuntimeState = true;
            try
            {
                ResetParticipants();
                _blackboard.Reset(transform.position);
                _currentDecision = NpcDecision.Idle;
                _decisionElapsedSeconds = 0f;
                _timeUntilNextDecisionSeconds = ResolveInitialDelay();
                _pendingJump = false;
                _commandSequence = 0;
                _decisionCount = 0;
                _runtimeStateClean = true;
            }
            finally
            {
                _isResettingRuntimeState = false;
            }
        }

        private void ResetParticipants()
        {
            if (_isResettingParticipants)
            {
                return;
            }

            _isResettingParticipants = true;
            _sensorResetSnapshot.Clear();
            _sensorResetSnapshot.AddRange(_sensors);
            try
            {
                for (int i = 0; i < _sensorResetSnapshot.Count; i++)
                {
                    INpcSensor sensor = _sensorResetSnapshot[i];
                    if (IndexOfSensor(sensor) < 0)
                    {
                        continue;
                    }

                    if (IsDestroyedUnityObject(sensor))
                    {
                        UnregisterSensor(sensor);
                        continue;
                    }

                    try
                    {
                        sensor.ResetNpcSensorState();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(
                            exception,
                            sensor as UnityEngine.Object);
                        UnregisterSensor(sensor);
                    }
                }
            }
            finally
            {
                _sensorResetSnapshot.Clear();
            }

            _decisionResetSnapshot.Clear();
            _decisionResetSnapshot.AddRange(_decisionProviders);
            try
            {
                for (int i = 0; i < _decisionResetSnapshot.Count; i++)
                {
                    INpcDecisionProvider provider = _decisionResetSnapshot[i];
                    if (IndexOfDecisionProvider(provider) < 0)
                    {
                        continue;
                    }

                    if (IsDestroyedUnityObject(provider))
                    {
                        UnregisterDecisionProvider(provider);
                        continue;
                    }

                    try
                    {
                        provider.ResetNpcDecisionState();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(
                            exception,
                            provider as UnityEngine.Object);
                        UnregisterDecisionProvider(provider);
                    }
                }
            }
            finally
            {
                _decisionResetSnapshot.Clear();
                _isResettingParticipants = false;
            }
        }

        private void BuildMovementAndLookCommand(
            Vector3 worldMove,
            out Vector2 move,
            out Vector2 look)
        {
            move = ConvertWorldMoveToLocal(worldMove);
            look = Vector2.zero;
            if (_look == null ||
                !_look.IsFeatureActive ||
                worldMove.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Transform lookReference = _look.MovementReference;
            Vector3 lookForward = lookReference.forward;
            lookForward.y = 0f;
            if (lookForward.sqrMagnitude <= 0.0001f)
            {
                lookForward = Vector3.forward;
            }
            else
            {
                lookForward.Normalize();
            }

            Vector3 desiredForward = worldMove.normalized;
            float yawDelta = Vector3.SignedAngle(
                lookForward,
                desiredForward,
                Vector3.up);
            look = new Vector2(yawDelta, 0f);
            if (ReferenceEquals(MovementSpace, lookReference))
            {
                move = Vector2.up * worldMove.magnitude;
            }
        }

        private Vector2 ConvertWorldMoveToLocal(Vector3 worldMove)
        {
            Transform space = MovementSpace;
            Vector3 forward = space.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            Vector3 right = space.right;
            right.y = 0f;
            right = right.sqrMagnitude > 0.0001f
                ? right.normalized
                : Vector3.right;
            return Vector2.ClampMagnitude(
                new Vector2(
                    Vector3.Dot(worldMove, right),
                    Vector3.Dot(worldMove, forward)),
                1f);
        }

        private float ResolveInitialDelay()
        {
            float interval = SanitizeNonNegative(decisionIntervalSeconds);
            if (interval <= 0f)
            {
                return 0f;
            }

            if (initialDecisionDelaySeconds >= 0f)
            {
                return Mathf.Min(
                    SanitizeNonNegative(initialDecisionDelaySeconds),
                    interval);
            }

            uint seed = _hasExplicitStaggerSeed
                ? _staggerSeed
                : unchecked((uint)GetInstanceID());
            uint hash = seed * 2654435761u;
            return (hash & 0x00FFFFFFu) /
                16777216f * interval;
        }

        private uint NextCommandSequence()
        {
            _commandSequence++;
            if (_commandSequence == 0)
            {
                _commandSequence = 1;
            }

            return _commandSequence;
        }

        private int IndexOfSensor(INpcSensor sensor)
        {
            for (int i = 0; i < _sensors.Count; i++)
            {
                if (ReferenceEquals(_sensors[i], sensor))
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfDecisionProvider(INpcDecisionProvider provider)
        {
            for (int i = 0; i < _decisionProviders.Count; i++)
            {
                if (ReferenceEquals(_decisionProviders[i], provider))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsDestroyedUnityObject(object participant)
        {
            return participant is UnityEngine.Object unityObject &&
                unityObject == null;
        }

        private static bool IsParticipantEnabled(object participant)
        {
            return !(participant is Behaviour behaviour) ||
                behaviour.isActiveAndEnabled;
        }

        private static float SaturatingAdd(float left, float right)
        {
            double sum = (double)left + right;
            return sum >= float.MaxValue ? float.MaxValue : (float)sum;
        }

        private static float SanitizeNonNegative(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
