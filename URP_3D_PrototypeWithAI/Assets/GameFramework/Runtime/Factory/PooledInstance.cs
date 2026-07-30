using System;
using UnityEngine;

namespace Rutin.GameFramework.Factory
{
    /// <summary>
    /// Lease state and cached callbacks for one pooled hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PooledInstance : MonoBehaviour
    {
        private GameObjectPool _owner;
        private IPoolable[] _callbacks = Array.Empty<IPoolable>();
        private bool _isRented;
        private uint _leaseVersion;

        public bool IsRented => _isRented;

        public uint LeaseVersion => _leaseVersion;

        internal GameObjectPool Owner => _owner;

        internal void Initialize(GameObjectPool owner)
        {
            if (_owner != null && !ReferenceEquals(_owner, owner))
            {
                throw new InvalidOperationException(
                    $"{name} already belongs to another pool.");
            }

            _owner = owner;
            RefreshCallbacks();
            _isRented = false;
        }

        public void RefreshCallbacks()
        {
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            int callbackCount = 0;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IPoolable)
                {
                    callbackCount++;
                }
            }

            if (callbackCount == 0)
            {
                _callbacks = Array.Empty<IPoolable>();
                return;
            }

            IPoolable[] callbacks = new IPoolable[callbackCount];
            int writeIndex = 0;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IPoolable callback)
                {
                    callbacks[writeIndex++] = callback;
                }
            }

            _callbacks = callbacks;
        }

        public bool ReturnToPool()
        {
            return _owner != null && _owner.Release(this);
        }

        internal void Rent(Vector3 position, Quaternion rotation, Transform parent)
        {
            if (_isRented)
            {
                throw new InvalidOperationException($"{name} is already rented.");
            }

            _isRented = true;
            _leaseVersion++;

            Transform cachedTransform = transform;
            cachedTransform.SetParent(parent, true);
            cachedTransform.SetPositionAndRotation(position, rotation);

            for (int i = 0; i < _callbacks.Length; i++)
            {
                _callbacks[i].OnRentFromPool();
            }

            gameObject.SetActive(true);
        }

        internal void Return(Transform inactiveRoot)
        {
            if (!_isRented)
            {
                return;
            }

            for (int i = _callbacks.Length - 1; i >= 0; i--)
            {
                _callbacks[i].OnReturnToPool();
            }

            gameObject.SetActive(false);
            transform.SetParent(inactiveRoot, false);
            _isRented = false;
        }

        private void OnDestroy()
        {
            GameObjectPool owner = _owner;
            bool wasRented = _isRented;
            _owner = null;
            _isRented = false;
            owner?.NotifyInstanceDestroyed(this, wasRented);
        }
    }
}
