using System.Collections.Generic;
using UnityEngine;

namespace Rutin.GameFramework.Management
{
    /// <summary>
    /// Scene-level composition root for manager services.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class GameManagerHost : MonoBehaviour
    {
        [SerializeField] private bool makeDefaultHost = true;
        [SerializeField] private bool persistAcrossScenes;

        private readonly List<GameServiceBehaviour> _services = new(8);
        private readonly List<GameServiceBehaviour> _discoveryBuffer = new(8);
        private bool _hostActive;
        private bool _isShuttingDown;

        public static GameManagerHost Default { get; private set; }

        public ServiceRegistry Services { get; } = new();

        public int ServiceCount => _services.Count;

        private void Awake()
        {
            if (makeDefaultHost)
            {
                if (Default != null && !ReferenceEquals(Default, this))
                {
                    Debug.LogError("A default GameManagerHost already exists.", this);
                }
                else
                {
                    Default = this;
                }
            }

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            _discoveryBuffer.Clear();
            GetComponents(_discoveryBuffer);
            for (int i = 0; i < _discoveryBuffer.Count; i++)
            {
                RegisterService(_discoveryBuffer[i]);
            }

            _discoveryBuffer.Clear();
        }

        private void OnEnable()
        {
            _hostActive = true;
            for (int i = 0; i < _services.Count; i++)
            {
                GameServiceBehaviour service = _services[i];
                if (service != null && service.isActiveAndEnabled)
                {
                    service.SetServiceActive(true);
                }
            }
        }

        private void OnDisable()
        {
            _hostActive = false;
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                _services[i]?.SetServiceActive(false);
            }
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                _services[i]?.Shutdown();
            }

            _services.Clear();
            Services.Clear();

            if (ReferenceEquals(Default, this))
            {
                Default = null;
            }
        }

        public bool TryGetService<TContract>(out TContract service)
            where TContract : class
        {
            return Services.TryGet(out service);
        }

        internal void RegisterService(GameServiceBehaviour service)
        {
            if (service == null || _isShuttingDown || IndexOfReference(service) >= 0)
            {
                return;
            }

            service.Initialize(this);

            int insertIndex = _services.Count;
            int order = service.InitializationOrder;
            for (int i = 0; i < _services.Count; i++)
            {
                if (_services[i].InitializationOrder > order)
                {
                    insertIndex = i;
                    break;
                }
            }

            _services.Insert(insertIndex, service);

            if (_hostActive && service.isActiveAndEnabled)
            {
                service.SetServiceActive(true);
            }
        }

        internal void UnregisterService(GameServiceBehaviour service)
        {
            if (service == null || _isShuttingDown)
            {
                return;
            }

            int index = IndexOfReference(service);
            if (index < 0)
            {
                return;
            }

            service.Shutdown();
            _services.RemoveAt(index);
        }

        internal void NotifyServiceEnabled(GameServiceBehaviour service)
        {
            if (!_isShuttingDown && _hostActive && IndexOfReference(service) >= 0)
            {
                service.SetServiceActive(true);
            }
        }

        internal void NotifyServiceDisabled(GameServiceBehaviour service)
        {
            if (!_isShuttingDown && IndexOfReference(service) >= 0)
            {
                service.SetServiceActive(false);
            }
        }

        private int IndexOfReference(GameServiceBehaviour service)
        {
            for (int i = 0; i < _services.Count; i++)
            {
                if (ReferenceEquals(_services[i], service))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
