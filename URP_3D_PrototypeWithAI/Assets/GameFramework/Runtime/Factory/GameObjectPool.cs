using System;
using System.Collections.Generic;
using Rutin.GameFramework.Utilities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rutin.GameFramework.Factory
{
    /// <summary>
    /// Bounded prefab pool. Creation is explicit, duplicate release is rejected,
    /// and lifecycle callbacks are cached per instance.
    /// </summary>
    public sealed class GameObjectPool : IDisposable
    {
        private readonly GameObject _prefab;
        private readonly Transform _inactiveRoot;
        private readonly int _maxSize;
        private readonly Stack<PooledInstance> _inactive;
        private readonly HashSet<PooledInstance> _inactiveInstances;
        private readonly HashSet<PooledInstance> _allInstances;
        private bool _disposed;
        private int _rentedCount;

        public GameObjectPool(
            GameObject prefab,
            Transform inactiveRoot,
            int initialCapacity = 0,
            int maxSize = 1024)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            if (maxSize <= 0 || initialCapacity > maxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSize));
            }

            _prefab = prefab;
            _inactiveRoot = inactiveRoot;
            _maxSize = maxSize;
            _inactive = new Stack<PooledInstance>(Math.Max(initialCapacity, 4));
            _inactiveInstances = new HashSet<PooledInstance>(
                ReferenceEqualityComparer<PooledInstance>.Instance);
            _allInstances = new HashSet<PooledInstance>(
                ReferenceEqualityComparer<PooledInstance>.Instance);

            Warmup(initialCapacity);
        }

        public int CountAll => _allInstances.Count;

        public int CountInactive => _inactiveInstances.Count;

        public int CountRented => _rentedCount;

        public int MaxSize => _maxSize;

        public void Warmup(int targetCount)
        {
            ThrowIfDisposed();

            if (targetCount < 0 || targetCount > _maxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(targetCount));
            }

            while (_allInstances.Count < targetCount)
            {
                PooledInstance instance = CreateInstance();
                instance.gameObject.SetActive(false);
                instance.transform.SetParent(_inactiveRoot, false);
                _inactive.Push(instance);
                _inactiveInstances.Add(instance);
            }
        }

        public bool TryRent(
            out PooledInstance instance,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null)
        {
            ThrowIfDisposed();

            instance = TakeInactiveInstance();
            if (instance == null)
            {
                if (_allInstances.Count >= _maxSize)
                {
                    return false;
                }

                instance = CreateInstance();
            }

            _rentedCount++;
            instance.Rent(position, rotation, parent);
            return true;
        }

        public PooledInstance Rent(
            Vector3 position,
            Quaternion rotation,
            Transform parent = null)
        {
            if (TryRent(out PooledInstance instance, position, rotation, parent))
            {
                return instance;
            }

            throw new InvalidOperationException(
                $"Pool for {_prefab.name} reached its maximum size of {_maxSize}.");
        }

        public bool Release(GameObject instance)
        {
            return instance != null &&
                   instance.TryGetComponent(out PooledInstance pooledInstance) &&
                   Release(pooledInstance);
        }

        public bool Release(PooledInstance instance)
        {
            if (_disposed ||
                instance == null ||
                !ReferenceEquals(instance.Owner, this) ||
                !instance.IsRented)
            {
                return false;
            }

            instance.Return(_inactiveRoot);
            _inactive.Push(instance);
            _inactiveInstances.Add(instance);
            _rentedCount--;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (PooledInstance instance in _allInstances)
            {
                if (instance == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(instance.gameObject);
                }
                else
                {
                    Object.DestroyImmediate(instance.gameObject);
                }
            }

            _inactive.Clear();
            _inactiveInstances.Clear();
            _allInstances.Clear();
            _rentedCount = 0;
        }

        internal void NotifyInstanceDestroyed(PooledInstance instance, bool wasRented)
        {
            if (_disposed || !_allInstances.Remove(instance))
            {
                return;
            }

            _inactiveInstances.Remove(instance);
            if (wasRented && _rentedCount > 0)
            {
                _rentedCount--;
            }
        }

        private PooledInstance CreateInstance()
        {
            GameObject clone = Object.Instantiate(_prefab, _inactiveRoot, false);
            clone.name = $"{_prefab.name} (Pooled)";

            if (!clone.TryGetComponent(out PooledInstance instance))
            {
                instance = clone.AddComponent<PooledInstance>();
            }

            instance.Initialize(this);
            _allInstances.Add(instance);
            return instance;
        }

        private PooledInstance TakeInactiveInstance()
        {
            while (_inactive.Count > 0)
            {
                PooledInstance candidate = _inactive.Pop();
                if (!_inactiveInstances.Remove(candidate))
                {
                    continue;
                }

                if (candidate != null)
                {
                    return candidate;
                }

                _allInstances.Remove(candidate);
            }

            return null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(GameObjectPool));
            }
        }
    }
}
