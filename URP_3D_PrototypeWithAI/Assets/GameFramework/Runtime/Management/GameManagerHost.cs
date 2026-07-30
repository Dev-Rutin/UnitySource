using System.Collections.Generic;
using Rutin.GameFramework.Utilities;
using UnityEngine;

namespace Rutin.GameFramework.Management
{
    public interface IDefaultServicesObserver
    {
        void OnDefaultServicesChanged();
    }

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

        private static readonly List<IDefaultServicesObserver>
            DefaultServiceObservers = new(256);
        private static readonly Dictionary<IDefaultServicesObserver, int>
            DefaultServiceObserverIndices = new(
                256,
                ReferenceEqualityComparer<IDefaultServicesObserver>.Instance);
        private static bool _isNotifyingDefaultServiceObservers;

        public static GameManagerHost Default { get; private set; }

        public ServiceRegistry Services { get; } = new();

        public int ServiceCount => _services.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefaultHost()
        {
            Default = null;
            DefaultServiceObservers.Clear();
            DefaultServiceObserverIndices.Clear();
            _isNotifyingDefaultServiceObservers = false;
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
                NotifyDefaultServicesChanged();
                Default = null;
            }
        }

        public bool TryGetService<TContract>(out TContract service)
            where TContract : class
        {
            return Services.TryGet(out service);
        }

        internal static void RegisterDefaultServicesObserver(
            IDefaultServicesObserver observer)
        {
            if (observer == null ||
                DefaultServiceObserverIndices.ContainsKey(observer))
            {
                return;
            }

            int index = DefaultServiceObservers.Count;
            DefaultServiceObservers.Add(observer);
            DefaultServiceObserverIndices.Add(observer, index);
        }

        internal static void UnregisterDefaultServicesObserver(
            IDefaultServicesObserver observer)
        {
            if (observer == null ||
                !DefaultServiceObserverIndices.TryGetValue(observer, out int index))
            {
                return;
            }

            DefaultServiceObserverIndices.Remove(observer);
            if (_isNotifyingDefaultServiceObservers)
            {
                DefaultServiceObservers[index] = null;
                return;
            }

            RemoveDefaultServicesObserverAt(index);
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
            if (!ReferenceEquals(Default, this))
            {
                return;
            }

            int observerCount = DefaultServiceObservers.Count;
            _isNotifyingDefaultServiceObservers = true;
            try
            {
                for (int i = 0; i < observerCount; i++)
                {
                    IDefaultServicesObserver observer =
                        DefaultServiceObservers[i];
                    if (observer == null ||
                        observer is Object unityObject && unityObject == null)
                    {
                        if (observer != null)
                        {
                            DefaultServiceObserverIndices.Remove(observer);
                        }

                        DefaultServiceObservers[i] = null;
                        continue;
                    }

                    try
                    {
                        observer.OnDefaultServicesChanged();
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogException(exception, observer as Object);
                    }
                }
            }
            finally
            {
                _isNotifyingDefaultServiceObservers = false;
                CompactDefaultServicesObservers();
            }
        }

        private static void RemoveDefaultServicesObserverAt(int index)
        {
            int lastIndex = DefaultServiceObservers.Count - 1;
            IDefaultServicesObserver last = DefaultServiceObservers[lastIndex];
            DefaultServiceObservers.RemoveAt(lastIndex);
            if (index == lastIndex)
            {
                return;
            }

            DefaultServiceObservers[index] = last;
            if (last != null)
            {
                DefaultServiceObserverIndices[last] = index;
            }
        }

        private static void CompactDefaultServicesObservers()
        {
            int writeIndex = 0;
            for (int readIndex = 0;
                 readIndex < DefaultServiceObservers.Count;
                 readIndex++)
            {
                IDefaultServicesObserver observer =
                    DefaultServiceObservers[readIndex];
                if (observer == null)
                {
                    continue;
                }

                DefaultServiceObservers[writeIndex] = observer;
                DefaultServiceObserverIndices[observer] = writeIndex;
                writeIndex++;
            }

            if (writeIndex < DefaultServiceObservers.Count)
            {
                DefaultServiceObservers.RemoveRange(
                    writeIndex,
                    DefaultServiceObservers.Count - writeIndex);
            }
        }
    }
}
