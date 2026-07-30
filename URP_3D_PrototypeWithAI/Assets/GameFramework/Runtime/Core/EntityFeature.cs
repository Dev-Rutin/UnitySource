using UnityEngine;

namespace Rutin.GameFramework.Core
{
    /// <summary>
    /// Base class for attachable gameplay capabilities. Features are initialized once,
    /// activated with their entity, and shut down in reverse order.
    /// </summary>
    [RequireComponent(typeof(GameplayEntity))]
    public abstract class EntityFeature : MonoBehaviour
    {
        private GameplayEntity _owner;
        private bool _initialized;
        private bool _active;

        public GameplayEntity Owner => _owner;

        public bool IsFeatureInitialized => _initialized;

        public bool IsFeatureActive => _active;

        /// <summary>
        /// Lower values initialize first and shut down last.
        /// </summary>
        public virtual int InitializationOrder => 0;

        protected virtual void Awake()
        {
            GameplayEntity entity = GetComponent<GameplayEntity>();
            entity.RegisterFeature(this);
        }

        protected virtual void OnEnable()
        {
            _owner?.NotifyFeatureEnabled(this);
        }

        protected virtual void OnDisable()
        {
            _owner?.NotifyFeatureDisabled(this);
        }

        protected virtual void OnDestroy()
        {
            _owner?.UnregisterFeature(this);
        }

        internal void Bind(GameplayEntity owner)
        {
            if (_owner != null && !ReferenceEquals(_owner, owner))
            {
                throw new System.InvalidOperationException(
                    $"{GetType().Name} is already attached to {_owner.name}.");
            }

            _owner = owner;
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            OnFeatureInitialized();
        }

        internal void SetFeatureActive(bool active)
        {
            if (!_initialized || _active == active)
            {
                return;
            }

            _active = active;
            if (active)
            {
                OnFeatureActivated();
            }
            else
            {
                OnFeatureDeactivated();
            }
        }

        internal void Unbind()
        {
            if (!_initialized)
            {
                _owner = null;
                return;
            }

            SetFeatureActive(false);
            OnFeatureShutdown();
            _initialized = false;
            _owner = null;
        }

        protected virtual void OnFeatureInitialized()
        {
        }

        protected virtual void OnFeatureActivated()
        {
        }

        protected virtual void OnFeatureDeactivated()
        {
        }

        protected virtual void OnFeatureShutdown()
        {
        }
    }
}
