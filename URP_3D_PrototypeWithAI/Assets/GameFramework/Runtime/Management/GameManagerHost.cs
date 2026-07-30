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
        private bool _isRejectedDuplicate;

        public static GameManagerHost Default { get; private set; }

        internal static event System.Action DefaultServicesChanged;

        public ServiceRegistry Services { get; } = new();

        public int ServiceCount => _services.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefaultHost()
        {
            Default = null;
            DefaultServicesChanged = null;
        }

        private void Awake()
        {
            if (makeDefaultHost)
            {
                if (Default != null && !ReferenceEquals(Default, this))
                {
                    RejectDuplicateDefaultHost();
                    return;
                }

                Default = this;
            }

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            _discoveryBuffer.Clear();
            GetComponents(_discoveryBuffer);
            for (int i = 0; i < _discoveryBuffer.Count; i++)
            {
                GameServiceBehaviour service = _discoveryBuffer[i];
                if (!service.HasInitializationFailed && IndexOfReference(service) < 0)
                {
                    InsertServiceSorted(service);
                }
            }

            for (int i = 0; i < _services.Count;)
            {
                if (TryInitializeService(_services[i]))
                {
                    i++;
                    continue;
                }

                _services.RemoveAt(i);
            }

            _discoveryBuffer.Clear();
            NotifyDefaultServicesChanged();
        }

        private void OnEnable()
        {
            if (_isRejectedDuplicate)
            {
                return;
            }

            _hostActive = true;
            for (int i = 0; i < _services.Count; i++)
            {
                GameServiceBehaviour service = _services[i];
                if (service != null && service.isActiveAndEnabled)
                {
                    TrySetServiceActive(service, true);
                }
            }
        }

        private void OnDisable()
        {
            _hostActive = false;
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                GameServiceBehaviour service = _services[i];
                if (service != null)
                {
                    TrySetServiceActive(service, false);
                }
            }
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                GameServiceBehaviour service = _services[i];
                if (service == null)
                {
                    continue;
                }

                try
                {
                    service.Shutdown();
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception, service);
                }
            }

            _services.Clear();
            Services.Clear();

            if (ReferenceEquals(Default, this))
            {
                Default = null;
                NotifyDefaultServicesChanged();
            }
        }

        public bool TryGetService<TContract>(out TContract service)
            where TContract : class
        {
            return Services.TryGet(out service);
        }

        internal void RegisterService(GameServiceBehaviour service)
        {
            if (service == null ||
                service.HasInitializationFailed ||
                _isShuttingDown ||
                IndexOfReference(service) >= 0)
            {
                return;
            }

            InsertServiceSorted(service);
            if (!TryInitializeService(service))
            {
                _services.Remove(service);
                return;
            }

            if (_hostActive && service.isActiveAndEnabled)
            {
                TrySetServiceActive(service, true);
            }

            NotifyDefaultServicesChanged();
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

            try
            {
                TryShutdownService(service);
            }
            finally
            {
                _services.RemoveAt(index);
                NotifyDefaultServicesChanged();
            }
        }

        internal void NotifyServiceEnabled(GameServiceBehaviour service)
        {
            if (!_isShuttingDown && _hostActive && IndexOfReference(service) >= 0)
            {
                TrySetServiceActive(service, true);
            }
        }

        internal void NotifyServiceDisabled(GameServiceBehaviour service)
        {
            if (!_isShuttingDown && IndexOfReference(service) >= 0)
            {
                TrySetServiceActive(service, false);
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

        private void InsertServiceSorted(GameServiceBehaviour service)
        {
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
        }

        private bool TryInitializeService(GameServiceBehaviour service)
        {
            try
            {
                service.Initialize(this);
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, service);
                return false;
            }
        }

        private void RejectDuplicateDefaultHost()
        {
            _isRejectedDuplicate = true;
            _isShuttingDown = true;
            Debug.LogError(
                $"Duplicate default GameManagerHost '{name}' was rejected. " +
                $"The existing host '{Default.name}' remains authoritative. " +
                "Only framework host and service components on the duplicate object were disabled.",
                this);

            for (int i = _services.Count - 1; i >= 0; i--)
            {
                GameServiceBehaviour service = _services[i];
                if (service == null)
                {
                    continue;
                }

                try
                {
                    service.Shutdown();
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception, service);
                }
            }

            _services.Clear();
            Services.Clear();
            enabled = false;
            _discoveryBuffer.Clear();
            GetComponents(_discoveryBuffer);
            for (int i = 0; i < _discoveryBuffer.Count; i++)
            {
                _discoveryBuffer[i].enabled = false;
            }

            _discoveryBuffer.Clear();
        }

        private static void TrySetServiceActive(GameServiceBehaviour service, bool active)
        {
            try
            {
                service.SetServiceActive(active);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, service);
            }
        }

        private static void TryShutdownService(GameServiceBehaviour service)
        {
            try
            {
                service.Shutdown();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, service);
            }
        }

        private void NotifyDefaultServicesChanged()
        {
            if (ReferenceEquals(Default, this) || Default == null)
            {
                DefaultServicesChanged?.Invoke();
            }
        }
    }
}
