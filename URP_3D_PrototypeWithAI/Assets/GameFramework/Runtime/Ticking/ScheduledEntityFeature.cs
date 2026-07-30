using Rutin.GameFramework.Core;
using Rutin.GameFramework.Management;
using UnityEngine;

namespace Rutin.GameFramework.Ticking
{
    /// <summary>
    /// Entity feature base that owns allocation-free registration with the central scheduler.
    /// A scheduler can be injected for tests, server worlds, or multi-world clients; otherwise
    /// the feature resolves the default host once during initialization.
    /// </summary>
    public abstract class ScheduledEntityFeature :
        EntityFeature,
        IGameTickable,
        ITickSchedulerRegistrationObserver
    {
        private ITickScheduler _scheduler;
        private bool _registered;
        private bool _missingSchedulerLogged;
        private bool _hasExplicitScheduler;

        public abstract bool IsTickEnabled { get; }

        public abstract void Tick(float deltaTime);

        public void OnTickSchedulerUnregistered(
            ITickScheduler scheduler,
            TickUnregistrationReason reason)
        {
            _registered = false;
            if (reason == TickUnregistrationReason.SchedulerCleared)
            {
                _scheduler = null;
            }

            if (reason == TickUnregistrationReason.Quarantined)
            {
                Debug.LogWarning(
                    $"{GetType().Name} lost scheduler registration ({reason}). " +
                    "It will retry when the default service registry changes, when the " +
                    "feature is reactivated, or when SetTickScheduler is called.",
                    this);
            }

            if (reason != TickUnregistrationReason.Explicit)
            {
                OnSchedulerRegistrationLost(reason);
            }
        }

        public void SetTickScheduler(ITickScheduler scheduler)
        {
            bool schedulerChanged = !ReferenceEquals(_scheduler, scheduler);
            if (!schedulerChanged && _hasExplicitScheduler)
            {
                return;
            }

            if (schedulerChanged)
            {
                UnregisterFromScheduler();
            }

            _hasExplicitScheduler = true;
            _scheduler = scheduler;
            _missingSchedulerLogged = false;
            if (IsFeatureActive && _scheduler != null)
            {
                RegisterWithScheduler();
            }
        }

        public void UseDefaultTickScheduler()
        {
            UnregisterFromScheduler();
            _hasExplicitScheduler = false;
            _scheduler = null;
            _missingSchedulerLogged = false;
            if (IsFeatureActive)
            {
                RegisterWithScheduler();
            }
        }

        protected sealed override void OnFeatureInitialized()
        {
            OnScheduledFeatureInitialized();
            GameManagerHost.DefaultServicesChanged += HandleDefaultServicesChanged;
            ResolveDefaultScheduler();
        }

        protected sealed override void OnFeatureActivated()
        {
            OnScheduledFeatureActivated();
            RegisterWithScheduler();
        }

        protected sealed override void OnFeatureDeactivated()
        {
            UnregisterFromScheduler();
            OnScheduledFeatureDeactivated();
        }

        protected sealed override void OnFeatureShutdown()
        {
            UnregisterFromScheduler();
            try
            {
                OnScheduledFeatureShutdown();
            }
            finally
            {
                GameManagerHost.DefaultServicesChanged -= HandleDefaultServicesChanged;
                _scheduler = null;
                _missingSchedulerLogged = false;
                _hasExplicitScheduler = false;
            }
        }

        protected virtual void OnScheduledFeatureInitialized()
        {
        }

        protected virtual void OnScheduledFeatureActivated()
        {
        }

        protected virtual void OnScheduledFeatureDeactivated()
        {
        }

        protected virtual void OnScheduledFeatureShutdown()
        {
        }

        protected virtual void OnSchedulerRegistrationLost(
            TickUnregistrationReason reason)
        {
        }

        protected virtual void OnSchedulerRegistered()
        {
        }

        private void ResolveDefaultScheduler()
        {
            if (_scheduler != null || _hasExplicitScheduler)
            {
                return;
            }

            GameManagerHost host = GameManagerHost.Default;
            if (host != null && host.TryGetService(out ITickScheduler scheduler))
            {
                _scheduler = scheduler;
            }
        }

        private void HandleDefaultServicesChanged()
        {
            if (!IsFeatureActive || _registered || _hasExplicitScheduler)
            {
                return;
            }

            ResolveDefaultScheduler();
            RegisterWithScheduler(false);
        }

        private void RegisterWithScheduler(bool logFailure = true)
        {
            if (_registered)
            {
                return;
            }

            ResolveDefaultScheduler();
            if (_scheduler == null)
            {
                if (logFailure && !_hasExplicitScheduler && !_missingSchedulerLogged)
                {
                    Debug.LogWarning(
                        $"{GetType().Name} could not resolve {nameof(ITickScheduler)}. " +
                        "Inject one with SetTickScheduler or add TickSchedulerService to " +
                        "the default GameManagerHost.",
                        this);
                    _missingSchedulerLogged = true;
                }

                return;
            }

            _registered = _scheduler.Register(this);
            if (_registered)
            {
                _missingSchedulerLogged = false;
                OnSchedulerRegistered();
            }
            else if (logFailure)
            {
                Debug.LogWarning(
                    $"{GetType().Name} was already registered or its scheduler rejected it.",
                    this);
            }
        }

        private void UnregisterFromScheduler()
        {
            if (!_registered)
            {
                return;
            }

            _scheduler?.Unregister(this);
            _registered = false;
        }
    }
}
