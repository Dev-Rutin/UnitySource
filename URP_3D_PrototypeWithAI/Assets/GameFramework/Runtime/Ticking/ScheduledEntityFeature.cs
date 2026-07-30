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
    public abstract class ScheduledEntityFeature : EntityFeature, IGameTickable
    {
        private ITickScheduler _scheduler;
        private bool _registered;
        private bool _missingSchedulerLogged;

        public abstract bool IsTickEnabled { get; }

        public abstract void Tick(float deltaTime);

        public void SetTickScheduler(ITickScheduler scheduler)
        {
            if (ReferenceEquals(_scheduler, scheduler))
            {
                return;
            }

            UnregisterFromScheduler();
            _scheduler = scheduler;
            _missingSchedulerLogged = false;
            if (IsFeatureActive)
            {
                RegisterWithScheduler();
            }
        }

        protected sealed override void OnFeatureInitialized()
        {
            OnScheduledFeatureInitialized();
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
                _scheduler = null;
                _missingSchedulerLogged = false;
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

        private void ResolveDefaultScheduler()
        {
            if (_scheduler != null)
            {
                return;
            }

            GameManagerHost host = GameManagerHost.Default;
            if (host != null && host.TryGetService(out ITickScheduler scheduler))
            {
                _scheduler = scheduler;
            }
        }

        private void RegisterWithScheduler()
        {
            if (_registered)
            {
                return;
            }

            ResolveDefaultScheduler();
            if (_scheduler == null)
            {
                if (!_missingSchedulerLogged)
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
            if (!_registered)
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
