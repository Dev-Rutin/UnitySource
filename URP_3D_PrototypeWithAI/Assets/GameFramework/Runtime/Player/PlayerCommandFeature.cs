using System;
using System.Collections.Generic;
using Rutin.GameFramework.Core;
using Rutin.GameFramework.Ticking;
using UnityEngine;

namespace Rutin.GameFramework.Player
{
    public enum PlayerCommandSubmissionResult
    {
        Accepted,
        RejectedInactive,
        RejectedOwnership,
        RejectedStaleSequence,
        RetryAfterDispatch
    }

    /// <summary>
    /// Ownership-aware command producer and dispatcher. This is the only scheduled component
    /// in a player stack; motor, view, and custom consumers are invoked in deterministic order.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCommandFeature : ScheduledEntityFeature
    {
        [SerializeField] private bool locallyControlled = true;
        [SerializeField] private bool simulationEnabled = true;

        [Min(0f)]
        [SerializeField] private float remoteCommandTimeout = 0.25f;

        private readonly List<MonoBehaviour> _sourceDiscoveryBuffer = new(4);
        private readonly List<IPlayerCommandConsumer> _consumers = new(4);
        private readonly List<IPlayerCommandConsumer> _dispatchSnapshot = new(4);
        private readonly List<IPlayerCommandConsumer> _resetSnapshot = new(4);
        private IPlayerCommandSource _source;
        private Vector2 _moveState;
        private PlayerCommandMoveSpace _moveSpaceState;
        private Vector2 _pendingLook;
        private bool _pendingJump;
        private bool _hasRemoteCommand;
        private bool _hasAcceptedSequence;
        private uint _lastAcceptedSequence;
        private uint _currentSequence;
        private bool _usesCommandSimulationTime;
        private bool _hasPendingCommandForDispatch;
        private float _pendingSimulationDeltaTimeSeconds;
        private float _remoteCommandAge;
        private uint _dispatchGeneration;
        private bool _isResettingConsumers;

        public override int InitializationOrder => -200;

        public override bool IsTickEnabled =>
            IsFeatureActive &&
            simulationEnabled;

        public bool IsLocallyControlled => locallyControlled;

        public bool IsSimulationEnabled => simulationEnabled;

        public float RemoteCommandTimeout => remoteCommandTimeout;

        public IPlayerCommandSource CommandSource => _source;

        public PlayerCommand CurrentCommand =>
            new(
                _moveState,
                _pendingLook,
                _pendingJump,
                _currentSequence,
                _pendingSimulationDeltaTimeSeconds,
                _usesCommandSimulationTime,
                _moveSpaceState);

        public void SetCommandSource(IPlayerCommandSource source)
        {
            if (ReferenceEquals(_source, source))
            {
                return;
            }

            DiscardSourceBuffer();
            _source = source;
            ClearCommandState(true);
            ResetConsumers();
        }

        public void SetLocallyControlled(bool value)
        {
            if (locallyControlled == value)
            {
                return;
            }

            locallyControlled = value;
            ClearCommandState(true);
            ResetConsumers();
        }

        public void SetSimulationEnabled(bool value)
        {
            if (simulationEnabled == value)
            {
                return;
            }

            simulationEnabled = value;
            ClearCommandState(true);
            ResetConsumers();
        }

        /// <summary>
        /// Sets the wall-clock timeout for remote input. Zero disables timeout fallback for
        /// deterministic command-owned streams.
        /// </summary>
        public void SetRemoteCommandTimeout(float seconds)
        {
            remoteCommandTimeout = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// Supplies a replay, server-authoritative, or remote-owned command while this feature
        /// is not locally controlled. Non-zero sequence values must be newer than the last
        /// accepted sequence; zero opts out of ordering.
        /// </summary>
        public bool SubmitCommand(PlayerCommand command)
        {
            return SubmitCommandDetailed(command) ==
                PlayerCommandSubmissionResult.Accepted;
        }

        /// <summary>
        /// Supplies a remote command and reports whether rejection is permanent or the producer
        /// should retry after the current pending timing mode has been dispatched.
        /// </summary>
        public PlayerCommandSubmissionResult SubmitCommandDetailed(
            PlayerCommand command)
        {
            if (!IsFeatureActive ||
                !simulationEnabled)
            {
                return PlayerCommandSubmissionResult.RejectedInactive;
            }

            if (locallyControlled)
            {
                return PlayerCommandSubmissionResult.RejectedOwnership;
            }

            if (_hasPendingCommandForDispatch &&
                command.HasSimulationDeltaTime != _usesCommandSimulationTime)
            {
                return PlayerCommandSubmissionResult.RetryAfterDispatch;
            }

            if (!AcceptRemoteSequence(command.Sequence))
            {
                return PlayerCommandSubmissionResult.RejectedStaleSequence;
            }

            if (!_hasPendingCommandForDispatch)
            {
                _usesCommandSimulationTime =
                    command.HasSimulationDeltaTime;
            }

            AcceptCommand(command);
            _hasPendingCommandForDispatch = true;
            _hasRemoteCommand = true;
            _remoteCommandAge = 0f;
            return PlayerCommandSubmissionResult.Accepted;
        }

        public bool RegisterConsumer(IPlayerCommandConsumer consumer)
        {
            if (consumer == null || IndexOfConsumer(consumer) >= 0)
            {
                return false;
            }

            int insertIndex = _consumers.Count;
            for (int i = 0; i < _consumers.Count; i++)
            {
                if (_consumers[i].CommandOrder > consumer.CommandOrder)
                {
                    insertIndex = i;
                    break;
                }
            }

            _consumers.Insert(insertIndex, consumer);
            return true;
        }

        public bool UnregisterConsumer(IPlayerCommandConsumer consumer)
        {
            int index = IndexOfConsumer(consumer);
            if (index < 0)
            {
                return false;
            }

            _consumers.RemoveAt(index);
            return true;
        }

        public override void Tick(float deltaTime)
        {
            if (!IsTickEnabled)
            {
                return;
            }

            float elapsed = Mathf.Max(0f, deltaTime);
            if (locallyControlled)
            {
                if (_source != null && _source.IsInputAvailable)
                {
                    _usesCommandSimulationTime = false;
                    AcceptCommand(_source.ReadCommand(elapsed));
                }
                else
                {
                    ClearCommandState(false);
                }
            }
            else
            {
                if (_hasRemoteCommand)
                {
                    _remoteCommandAge += elapsed;
                    if (remoteCommandTimeout > 0f &&
                        _remoteCommandAge >= remoteCommandTimeout)
                    {
                        ClearPendingInput();
                        _usesCommandSimulationTime = false;
                        _hasRemoteCommand = false;
                    }
                }
            }

            DispatchCommand(elapsed);
        }

        protected override void OnScheduledFeatureInitialized()
        {
            if (_source == null)
            {
                DiscoverCommandSource();
            }
        }

        protected override void OnScheduledFeatureActivated()
        {
            ClearCommandState(true);
            ResetConsumers();
        }

        protected override void OnScheduledFeatureDeactivated()
        {
            ClearCommandState(true);
            ResetConsumers();
        }

        protected override void OnSchedulerRegistrationLost(
            TickUnregistrationReason reason)
        {
            ClearCommandState(true);
            ResetConsumers();
        }

        protected override void OnSchedulerRegistered()
        {
            DiscardSourceBuffer();
        }

        protected override void OnScheduledFeatureShutdown()
        {
            ClearCommandState(true);
            ResetConsumers();
            _source = null;
            _sourceDiscoveryBuffer.Clear();
            _consumers.Clear();
            _dispatchSnapshot.Clear();
            _resetSnapshot.Clear();
        }

        private void DiscoverCommandSource()
        {
            _sourceDiscoveryBuffer.Clear();
            GetComponents(_sourceDiscoveryBuffer);
            for (int i = 0; i < _sourceDiscoveryBuffer.Count; i++)
            {
                MonoBehaviour behaviour = _sourceDiscoveryBuffer[i];
                if (behaviour is IPlayerCommandSource source)
                {
                    _source = source;
                    break;
                }
            }

            _sourceDiscoveryBuffer.Clear();
        }

        private bool AcceptRemoteSequence(uint sequence)
        {
            if (sequence == 0)
            {
                return true;
            }

            if (_hasAcceptedSequence &&
                unchecked((int)(sequence - _lastAcceptedSequence)) <= 0)
            {
                return false;
            }

            _hasAcceptedSequence = true;
            _lastAcceptedSequence = sequence;
            return true;
        }

        private void AcceptCommand(PlayerCommand command)
        {
            _moveState = command.Move;
            _moveSpaceState = command.MoveSpace;
            _pendingLook += command.Look;
            _pendingJump |= command.JumpPressed;
            _currentSequence = command.Sequence;
            if (command.HasSimulationDeltaTime)
            {
                _usesCommandSimulationTime = true;
                double accumulatedSimulationTime =
                    (double)_pendingSimulationDeltaTimeSeconds +
                    command.SimulationDeltaTimeSeconds;
                _pendingSimulationDeltaTimeSeconds =
                    accumulatedSimulationTime >= float.MaxValue
                        ? float.MaxValue
                        : (float)accumulatedSimulationTime;
            }
        }

        private void DispatchCommand(float deltaTime)
        {
            PlayerCommand command = new(
                _moveState,
                _pendingLook,
                _pendingJump,
                _currentSequence,
                _pendingSimulationDeltaTimeSeconds,
                _usesCommandSimulationTime,
                _moveSpaceState);
            float simulationDeltaTime =
                command.HasSimulationDeltaTime
                    ? command.SimulationDeltaTimeSeconds
                    : deltaTime;
            _pendingLook = Vector2.zero;
            _pendingJump = false;
            _hasPendingCommandForDispatch = false;
            _pendingSimulationDeltaTimeSeconds = 0f;

            uint generation = _dispatchGeneration;
            _dispatchSnapshot.Clear();
            _dispatchSnapshot.AddRange(_consumers);
            try
            {
                for (int i = 0; i < _dispatchSnapshot.Count; i++)
                {
                    if (generation != _dispatchGeneration)
                    {
                        break;
                    }

                    IPlayerCommandConsumer consumer = _dispatchSnapshot[i];
                    if (IndexOfConsumer(consumer) < 0)
                    {
                        continue;
                    }

                    if (consumer is UnityEngine.Object unityObject && unityObject == null)
                    {
                        UnregisterConsumer(consumer);
                        continue;
                    }

                    if (consumer is EntityFeature feature && !feature.IsFeatureActive)
                    {
                        continue;
                    }

                    try
                    {
                        consumer.ProcessPlayerCommand(
                            command,
                            simulationDeltaTime);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, consumer as UnityEngine.Object);
                        UnregisterConsumer(consumer);
                    }
                }
            }
            finally
            {
                _dispatchSnapshot.Clear();
            }
        }

        private void ResetConsumers()
        {
            _dispatchGeneration++;
            if (_isResettingConsumers)
            {
                return;
            }

            _isResettingConsumers = true;
            _resetSnapshot.Clear();
            _resetSnapshot.AddRange(_consumers);
            try
            {
                for (int i = 0; i < _resetSnapshot.Count; i++)
                {
                    IPlayerCommandConsumer consumer = _resetSnapshot[i];
                    if (IndexOfConsumer(consumer) < 0)
                    {
                        continue;
                    }

                    if (consumer is UnityEngine.Object unityObject && unityObject == null)
                    {
                        UnregisterConsumer(consumer);
                        continue;
                    }

                    try
                    {
                        consumer.ResetPlayerCommandState();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, consumer as UnityEngine.Object);
                        UnregisterConsumer(consumer);
                    }
                }
            }
            finally
            {
                _resetSnapshot.Clear();
                _isResettingConsumers = false;
            }
        }

        private void ClearCommandState(bool resetSequence)
        {
            DiscardSourceBuffer();

            _moveState = Vector2.zero;
            _moveSpaceState = PlayerCommandMoveSpace.Relative;
            _pendingLook = Vector2.zero;
            _pendingJump = false;
            _hasRemoteCommand = false;
            _remoteCommandAge = 0f;
            _currentSequence = 0;
            _usesCommandSimulationTime = false;
            _hasPendingCommandForDispatch = false;
            _pendingSimulationDeltaTimeSeconds = 0f;
            if (resetSequence)
            {
                _hasAcceptedSequence = false;
                _lastAcceptedSequence = 0;
            }
        }

        private void DiscardSourceBuffer()
        {
            if (_source is IBufferedPlayerCommandSource bufferedSource)
            {
                bufferedSource.DiscardBufferedInput();
            }
        }

        private void ClearPendingInput()
        {
            _moveState = Vector2.zero;
            _moveSpaceState = PlayerCommandMoveSpace.Relative;
            _pendingLook = Vector2.zero;
            _pendingJump = false;
            _hasPendingCommandForDispatch = false;
            _pendingSimulationDeltaTimeSeconds = 0f;
        }

        private int IndexOfConsumer(IPlayerCommandConsumer consumer)
        {
            for (int i = 0; i < _consumers.Count; i++)
            {
                if (ReferenceEquals(_consumers[i], consumer))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
