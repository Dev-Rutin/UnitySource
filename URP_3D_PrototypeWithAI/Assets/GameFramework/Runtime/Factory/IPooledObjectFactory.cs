using UnityEngine;

namespace Rutin.GameFramework.Factory
{
    public interface IPooledObjectFactory
    {
        int PoolCount { get; }

        bool TryRent(
            int typeId,
            out PooledInstance instance,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null);

        PooledInstance Rent(
            int typeId,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null);

        bool Release(PooledInstance instance);

        bool TryGetPool(int typeId, out GameObjectPool pool);
    }
}
