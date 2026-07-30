using System;
using System.Collections.Generic;
using Rutin.GameFramework.Core;
using Rutin.GameFramework.Ticking;
using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// Ownership-aware command producer and dispatcher. This is the only scheduled component
    /// in a player stack; motor, view, and custom consumers are invoked in deterministic order.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCommandFeature : ScheduledEntityFeature
    {
        [SerializeField] private bool locallyControlled = true;
        [SerializeField] private bool simulationEnabled = true;

        [Min(0.01f)]
        [SerializeField] private float remoteCommandTimeout = 0.25f;

        private readonly List<MonoBehaviour> _sourceDiscoveryBuffer = new(4);
        private readonly List<IPlayerCommandConsumer> _consumers = new(4);
        private IPlayerCommandSource _source;
        private Vector2 _moveState;
        private Vector2 _pendingLook;
        private bool _pendingJump;
        private bool _hasRemoteCommand;
        private bool _hasAcceptedSequence;
        private uint _lastAcceptedSequence;
        private uint _currentSequence;
        private float _remoteCommandAge;

        public override int InitializationOrder => -200;

        public override bool IsTickEnabled =>
            IsFeatureActive &&
            simulationEnabled &&
            (locallyControlled ? _source != null : _hasRemoteCommand);

        public bool IsLocallyControlled => locallyControlled;

        public bool IsSimulationEnabled => simulationEnabled;

        public PlayerCommand CurrentCommand =>
            new(_moveState, _pendingLook, _pendingJump, _currentSequence);

        public void SetCommandSource(IPlayerCommandSource source)
        {
            if (ReferenceEquals(_source, source))
            {
                return;
            }

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
        /// Supplies a replay, server-authoritative, or remote-owned command. Non-zero sequence
        /// values must be newer than the last accepted sequence; zero opts out of ordering.
        /// </summary>
        public bool SubmitCommand(PlayerCommand command)
        {
            if (!simulationEnabled || !AcceptRemoteSequence(command.Sequence))
            {
                return false;
            }

            AcceptCommand(command);
            _hasRemoteCommand = true;
            _remoteCommandAge = 0f;
            return true;
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
                if (_source.IsInputAvailable)
                {
                    AcceptCommand(_source.ReadCommand(elapsed));
                }
                else
                {
                    ClearCommandState(false);
                }
            }
            else
            {
                _remoteCommandAge += elapsed;
                if (_remoteCommandAge >= Mathf.Max(0.01f, remoteCommandTimeout))
                {
                    _moveState = Vector2.zero;
                    _hasRemoteCommand = false;
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

        protected override void OnScheduledFeatureDeactivated()
        {
            ClearCommandState(true);
            ResetConsumers();
        }

        protected override void OnScheduledFeatureShutdown()
        {
            ClearCommandState(true);
            ResetConsumers();
            _source = null;
            _sourceDiscoveryBuffer.Clear();
            _consumers.Clear();
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
            _pendingLook += command.Look;
            _pendingJump |= command.JumpPressed;
            _currentSequence = command.Sequence;
        }

        private void DispatchCommand(float deltaTime)
        {
            PlayerCommand command = new(
                _moveState,
                _pendingLook,
                _pendingJump,
                _currentSequence);
            _pendingLook = Vector2.zero;
            _pendingJump = false;

            for (int i = 0; i < _consumers.Count; i++)
            {
                IPlayerCommandConsumer consumer = _consumers[i];
                if (consumer is UnityEngine.Object unityObject && unityObject == null)
                {
                    _consumers.RemoveAt(i--);
                    continue;
                }

                if (consumer is EntityFeature feature && !feature.IsFeatureActive)
                {
                    continue;
                }

                try
                {
                    consumer.ProcessPlayerCommand(command, deltaTime);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, consumer as UnityEngine.Object);
                    _consumers.RemoveAt(i--);
                }
            }
        }

        private void ResetConsumers()
        {
            for (int i = 0; i < _consumers.Count; i++)
            {
                IPlayerCommandConsumer consumer = _consumers[i];
                if (consumer is UnityEngine.Object unityObject && unityObject == null)
                {
                    _consumers.RemoveAt(i--);
                    continue;
                }

                try
                {
                    consumer.ResetPlayerCommandState();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, consumer as UnityEngine.Object);
                    _consumers.RemoveAt(i--);
                }
            }
        }

        private void ClearCommandState(bool resetSequence)
        {
            _moveState = Vector2.zero;
            _pendingLook = Vector2.zero;
            _pendingJump = false;
            _hasRemoteCommand = false;
            _remoteCommandAge = 0f;
            _currentSequence = 0;
            if (resetSequence)
            {
                _hasAcceptedSequence = false;
                _lastAcceptedSequence = 0;
            }
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
