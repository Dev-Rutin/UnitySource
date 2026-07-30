using System.Collections.Generic;
using Rutin.GameFramework.Ticking;
using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// Ownership-aware command buffer. Local sources are polled by the central scheduler;
    /// remote/server commands can be submitted directly without enabling local control.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCommandFeature : ScheduledEntityFeature
    {
        [SerializeField] private bool locallyControlled = true;
        [SerializeField] private bool simulationEnabled = true;

        private readonly List<MonoBehaviour> _sourceDiscoveryBuffer = new(4);
        private IPlayerCommandSource _source;
        private PlayerCommand _currentCommand;
        private uint _commandVersion;
        private uint _consumedJumpVersion;
        private uint _consumedLookVersion;
        private uint _controlRevision;

        public override int InitializationOrder => -200;

        public override bool IsTickEnabled =>
            IsFeatureActive &&
            simulationEnabled &&
            locallyControlled &&
            _source != null;

        public bool IsLocallyControlled => locallyControlled;

        public bool IsSimulationEnabled => simulationEnabled;

        public PlayerCommand CurrentCommand => _currentCommand;

        public uint ControlRevision => _controlRevision;

        public void SetCommandSource(IPlayerCommandSource source)
        {
            _source = source;
            AdvanceControlRevision();
            ClearCommand();
        }

        public void SetLocallyControlled(bool value)
        {
            if (locallyControlled == value)
            {
                return;
            }

            locallyControlled = value;
            AdvanceControlRevision();
            ClearCommand();
        }

        public void SetSimulationEnabled(bool value)
        {
            if (simulationEnabled == value)
            {
                return;
            }

            simulationEnabled = value;
            AdvanceControlRevision();
            if (!simulationEnabled)
            {
                ClearCommand();
            }
        }

        /// <summary>
        /// Supplies a replay, server-authoritative, or remote-owned command.
        /// </summary>
        public void SubmitCommand(PlayerCommand command)
        {
            AcceptCommand(command);
        }

        public bool ConsumeJumpPressed()
        {
            if (!_currentCommand.JumpPressed ||
                _consumedJumpVersion == _commandVersion)
            {
                return false;
            }

            _consumedJumpVersion = _commandVersion;
            return true;
        }

        public bool TryConsumeLookDelta(out Vector2 look)
        {
            if (_consumedLookVersion == _commandVersion)
            {
                look = Vector2.zero;
                return false;
            }

            _consumedLookVersion = _commandVersion;
            look = _currentCommand.Look;
            return look.sqrMagnitude > 0f;
        }

        public override void Tick(float deltaTime)
        {
            if (!IsTickEnabled)
            {
                return;
            }

            if (_source.IsInputAvailable)
            {
                AcceptCommand(_source.ReadCommand(deltaTime));
            }
            else
            {
                ClearCommand();
            }
        }

        protected override void OnScheduledFeatureInitialized()
        {
            if (_source != null)
            {
                return;
            }

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

        protected override void OnScheduledFeatureDeactivated()
        {
            ClearCommand();
        }

        protected override void OnScheduledFeatureShutdown()
        {
            ClearCommand();
            _source = null;
            _sourceDiscoveryBuffer.Clear();
        }

        private void AcceptCommand(PlayerCommand command)
        {
            if (!simulationEnabled)
            {
                return;
            }

            _currentCommand = command;
            _commandVersion++;
            if (_commandVersion == 0)
            {
                _commandVersion = 1;
                _consumedJumpVersion = 0;
            }
        }

        private void ClearCommand()
        {
            _currentCommand = PlayerCommand.Neutral;
            _commandVersion++;
            _consumedJumpVersion = _commandVersion;
            _consumedLookVersion = _commandVersion;
        }

        private void AdvanceControlRevision()
        {
            _controlRevision++;
            if (_controlRevision == 0)
            {
                _controlRevision = 1;
            }
        }
    }
}
