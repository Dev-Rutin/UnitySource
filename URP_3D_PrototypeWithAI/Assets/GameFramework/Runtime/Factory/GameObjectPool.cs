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
        private readonly GameObject _creationRootObject;
        private readonly Transform _creationRoot;
        private readonly int _maxSize;
        private readonly Stack<PooledInstance> _inactive;
        private readonly HashSet<PooledInstance> _inactiveInstances;
        private readonly HashSet<PooledInstance> _rentedInstances;
        private readonly HashSet<PooledInstance> _allInstances;
        private readonly List<PooledInstance> _destroyedBuffer;
        private bool _disposed;
        private bool _compactionAttemptedAtCapacity;

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
            _creationRootObject = new GameObject($"{prefab.name} Pool Creation Root");
            _creationRootObject.SetActive(false);
            _creationRoot = _creationRootObject.transform;
            _creationRoot.SetParent(inactiveRoot, false);
            _inactive = new Stack<PooledInstance>(Math.Max(initialCapacity, 4));
            _inactiveInstances = new HashSet<PooledInstance>(
                initialCapacity,
                ReferenceEqualityComparer<PooledInstance>.Instance);
            _rentedInstances = new HashSet<PooledInstance>(
                initialCapacity,
                ReferenceEqualityComparer<PooledInstance>.Instance);
            _allInstances = new HashSet<PooledInstance>(
                initialCapacity,
                ReferenceEqualityComparer<PooledInstance>.Instance);
            _destroyedBuffer = new List<PooledInstance>(
                Math.Max(initialCapacity, 4));

            Warmup(initialCapacity);
        }

        public int CountAll => _allInstances.Count;

        public int CountInactive => _inactiveInstances.Count;

        public int CountRented => _rentedInstances.Count;

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
                    if (!_compactionAttemptedAtCapacity)
                    {
                        CompactDestroyedInstances();
                        _compactionAttemptedAtCapacity = true;
                    }

                    if (_allInstances.Count >= _maxSize)
                    {
                        return false;
                    }
                }

                instance = CreateInstance();
            }

            _rentedInstances.Add(instance);
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
            _rentedInstances.Remove(instance);
            _compactionAttemptedAtCapacity = false;
            return true;
        }

        /// <summary>
        /// Performs an explicit O(n) maintenance pass for Unity objects destroyed without
        /// reaching their lifecycle callback. Normal rent, release, and count paths stay O(1).
        /// </summary>
        public int CompactDestroyedInstances()
        {
            ThrowIfDisposed();

            if (_allInstances.Count == 0)
            {
                return 0;
            }

            _destroyedBuffer.Clear();
            foreach (PooledInstance instance in _allInstances)
            {
                if (instance == null)
                {
                    _destroyedBuffer.Add(instance);
                }
            }

            int removedCount = _destroyedBuffer.Count;
            for (int i = 0; i < removedCount; i++)
            {
                PooledInstance destroyed = _destroyedBuffer[i];
                _allInstances.Remove(destroyed);
                _inactiveInstances.Remove(destroyed);
                _rentedInstances.Remove(destroyed);
            }

            _destroyedBuffer.Clear();
            return removedCount;
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
            _rentedInstances.Clear();
            _allInstances.Clear();
            _destroyedBuffer.Clear();
            if (_creationRootObject != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_creationRootObject);
                }
                else
                {
                    Object.DestroyImmediate(_creationRootObject);
                }
            }
        }

        internal void NotifyInstanceDestroyed(PooledInstance instance, bool wasRented)
        {
            if (_disposed || !_allInstances.Remove(instance))
            {
                return;
            }

            _inactiveInstances.Remove(instance);
            if (wasRented)
            {
                _rentedInstances.Remove(instance);
            }

            _compactionAttemptedAtCapacity = false;
        }

        private PooledInstance CreateInstance()
        {
            GameObject clone = Object.Instantiate(_prefab, _creationRoot, false);
            clone.name = $"{_prefab.name} (Pooled)";
            clone.SetActive(false);
            clone.transform.SetParent(_inactiveRoot, false);

            if (!clone.TryGetComponent(out PooledInstance instance))
            {
                instance = clone.AddComponent<PooledInstance>();
            }

            instance.Initialize(this);
            _allInstances.Add(instance);
            _compactionAttemptedAtCapacity = false;
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
                _rentedInstances.Remove(candidate);
                _compactionAttemptedAtCapacity = false;
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
