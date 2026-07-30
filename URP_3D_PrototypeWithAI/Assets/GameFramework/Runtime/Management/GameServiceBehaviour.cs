using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rutin.GameFramework.Management
{
    /// <summary>
    /// Base for attachable manager services hosted by <see cref="GameManagerHost"/>.
    /// </summary>
    [RequireComponent(typeof(GameManagerHost))]
    public abstract class GameServiceBehaviour : MonoBehaviour
    {
        private readonly List<Type> _registeredContracts = new(4);
        private GameManagerHost _host;
        private bool _initialized;
        private bool _active;
        private bool _initializationFailed;

        public GameManagerHost Host => _host;

        public bool IsServiceInitialized => _initialized;

        public bool IsServiceActive => _active;

        internal bool HasInitializationFailed => _initializationFailed;

        public virtual int InitializationOrder => 0;

        protected virtual void Awake()
        {
            GetComponent<GameManagerHost>().RegisterService(this);
        }

        protected virtual void OnEnable()
        {
            _host?.NotifyServiceEnabled(this);
        }

        protected virtual void OnDisable()
        {
            _host?.NotifyServiceDisabled(this);
        }

        protected virtual void OnDestroy()
        {
            _host?.UnregisterService(this);
        }

        protected void RegisterContract<TContract>()
            where TContract : class
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "Contracts can only be registered during service initialization.");
            }

            Type contractType = typeof(TContract);
            _host.Services.Register(contractType, this);
            if (!_registeredContracts.Contains(contractType))
            {
                _registeredContracts.Add(contractType);
            }
        }

        internal void Initialize(GameManagerHost host)
        {
            if (_initialized)
            {
                return;
            }

            _host = host;
            _initialized = true;
            _initializationFailed = false;

            try
            {
                Type concreteType = GetType();
                host.Services.Register(concreteType, this);
                _registeredContracts.Add(concreteType);

                RegisterServiceContracts();
                OnServiceInitialized();
            }
            catch
            {
                RollbackFailedInitialization();
                _initializationFailed = true;
                throw;
            }
        }

        internal void SetServiceActive(bool active)
        {
            if (!_initialized || _active == active)
            {
                return;
            }

            _active = active;
            if (active)
            {
                try
                {
                    OnServiceActivated();
                }
                catch
                {
                    _active = false;
                    throw;
                }
            }
            else
            {
                OnServiceDeactivated();
            }
        }

        internal void Shutdown()
        {
            if (!_initialized)
            {
                _host = null;
                return;
            }

            try
            {
                SetServiceActive(false);
                OnServiceShutdown();
            }
            finally
            {
                UnregisterContracts();
                _initialized = false;
                _host = null;
            }
        }

        private void RollbackFailedInitialization()
        {
            try
            {
                SetServiceActive(false);
                OnServiceShutdown();
            }
            catch (Exception rollbackException)
            {
                Debug.LogException(rollbackException, this);
            }

            UnregisterContracts();
            _initialized = false;
            _host = null;
        }

        private void UnregisterContracts()
        {
            if (_host != null)
            {
                for (int i = _registeredContracts.Count - 1; i >= 0; i--)
                {
                    _host.Services.Unregister(_registeredContracts[i], this);
                }
            }

            _registeredContracts.Clear();
        }

        protected virtual void RegisterServiceContracts()
        {
        }

        protected virtual void OnServiceInitialized()
        {
        }

        protected virtual void OnServiceActivated()
        {
        }

        protected virtual void OnServiceDeactivated()
        {
        }

        protected virtual void OnServiceShutdown()
        {
        }
    }
}
