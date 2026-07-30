using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rutin.GameFramework.Factory
{
    [Serializable]
    public sealed class PoolDefinition
    {
        [Min(1)] public int typeId = 1;
        public GameObject prefab;
        [Min(0)] public int prewarmCount;
        [Min(1)] public int maxSize = 1024;
    }

    /// <summary>
    /// Integer-keyed factory over bounded prefab pools. Integer IDs avoid string
    /// hashing and allocations on spawn hot paths.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledObjectFactory : MonoBehaviour
    {
        [SerializeField] private Transform inactiveRoot;
        [SerializeField] private List<PoolDefinition> definitions = new();

        private readonly Dictionary<int, GameObjectPool> _pools = new(16);
        private bool _initialized;

        public int PoolCount => _pools.Count;

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<int, GameObjectPool> pair in _pools)
            {
                pair.Value.Dispose();
            }

            _pools.Clear();
            _initialized = false;
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            for (int i = 0; i < definitions.Count; i++)
            {
                PoolDefinition definition = definitions[i];
                if (definition == null || definition.prefab == null)
                {
                    Debug.LogWarning($"Ignored invalid pool definition at index {i}.", this);
                    continue;
                }

                RegisterPool(
                    definition.typeId,
                    definition.prefab,
                    definition.prewarmCount,
                    definition.maxSize);
            }
        }

        public void RegisterPool(
            int typeId,
            GameObject prefab,
            int prewarmCount = 0,
            int maxSize = 1024)
        {
            if (_pools.ContainsKey(typeId))
            {
                throw new InvalidOperationException(
                    $"A pool is already registered for type ID {typeId}.");
            }

            GameObjectPool pool = new(
                prefab,
                inactiveRoot != null ? inactiveRoot : transform,
                prewarmCount,
                maxSize);

            _pools.Add(typeId, pool);
        }

        public bool TryRent(
            int typeId,
            out PooledInstance instance,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null)
        {
            if (!_initialized)
            {
                Initialize();
            }

            if (!_pools.TryGetValue(typeId, out GameObjectPool pool))
            {
                instance = null;
                return false;
            }

            return pool.TryRent(out instance, position, rotation, parent);
        }

        public PooledInstance Rent(
            int typeId,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null)
        {
            if (TryRent(typeId, out PooledInstance instance, position, rotation, parent))
            {
                return instance;
            }

            throw new InvalidOperationException(
                $"No available pooled instance for type ID {typeId}.");
        }

        public bool Release(PooledInstance instance)
        {
            return instance != null && instance.ReturnToPool();
        }

        public bool TryGetPool(int typeId, out GameObjectPool pool)
        {
            return _pools.TryGetValue(typeId, out pool);
        }
    }
}
