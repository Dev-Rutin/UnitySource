using System;
using System.Collections.Generic;

namespace Rutin.GameFramework.Management
{
    /// <summary>
    /// Explicit service map. Lookups are O(1), allocation-free after registration,
    /// and do not rely on scene-wide object searches.
    /// </summary>
    public sealed class ServiceRegistry
    {
        private readonly Dictionary<Type, object> _services = new(16);

        public int Count => _services.Count;

        public void Register<TContract>(TContract service)
            where TContract : class
        {
            Register(typeof(TContract), service);
        }

        public void Register(Type contractType, object service)
        {
            if (contractType == null)
            {
                throw new ArgumentNullException(nameof(contractType));
            }

            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (!contractType.IsInstanceOfType(service))
            {
                throw new ArgumentException(
                    $"{service.GetType().Name} does not implement {contractType.Name}.",
                    nameof(service));
            }

            if (_services.TryGetValue(contractType, out object existing))
            {
                if (ReferenceEquals(existing, service))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"A service is already registered for {contractType.FullName}.");
            }

            _services.Add(contractType, service);
        }

        public bool TryGet<TContract>(out TContract service)
            where TContract : class
        {
            if (_services.TryGetValue(typeof(TContract), out object value))
            {
                service = (TContract)value;
                return true;
            }

            service = null;
            return false;
        }

        public TContract GetRequired<TContract>()
            where TContract : class
        {
            if (TryGet(out TContract service))
            {
                return service;
            }

            throw new KeyNotFoundException(
                $"No service is registered for {typeof(TContract).FullName}.");
        }

        public bool Unregister<TContract>(TContract service)
            where TContract : class
        {
            return Unregister(typeof(TContract), service);
        }

        public bool Unregister(Type contractType, object service)
        {
            if (contractType == null || service == null)
            {
                return false;
            }

            if (!_services.TryGetValue(contractType, out object existing) ||
                !ReferenceEquals(existing, service))
            {
                return false;
            }

            return _services.Remove(contractType);
        }

        public void Clear()
        {
            _services.Clear();
        }
    }
}
