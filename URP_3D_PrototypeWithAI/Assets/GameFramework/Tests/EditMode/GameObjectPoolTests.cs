using NUnit.Framework;
using Rutin.GameFramework.Factory;
using UnityEngine;

namespace Rutin.GameFramework.Tests.EditMode
{
    public sealed class GameObjectPoolTests
    {
        private GameObject _prefab;
        private GameObject _rootObject;
        private GameObjectPool _pool;

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("Pool Test Prefab");
            _prefab.SetActive(false);
            _rootObject = new GameObject("Pool Root");
            _pool = new GameObjectPool(_prefab, _rootObject.transform, 1, 4);
        }

        [TearDown]
        public void TearDown()
        {
            _pool?.Dispose();
            Object.DestroyImmediate(_prefab);
            Object.DestroyImmediate(_rootObject);
        }

        [Test]
        public void ReleaseThenRent_ReusesInstance()
        {
            PooledInstance first = _pool.Rent(Vector3.zero, Quaternion.identity);
            Assert.That(_pool.Release(first), Is.True);

            PooledInstance second = _pool.Rent(Vector3.one, Quaternion.identity);

            Assert.That(second, Is.SameAs(first));
            Assert.That(second.transform.position, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void Release_DuplicateReturnIsRejected()
        {
            PooledInstance instance = _pool.Rent(Vector3.zero, Quaternion.identity);

            Assert.That(_pool.Release(instance), Is.True);
            Assert.That(_pool.Release(instance), Is.False);
            Assert.That(_pool.CountInactive, Is.EqualTo(1));
            Assert.That(_pool.CountRented, Is.Zero);
        }

        [Test]
        public void TryRent_RespectsMaximumSize()
        {
            PooledInstance[] rented = new PooledInstance[4];
            for (int i = 0; i < rented.Length; i++)
            {
                Assert.That(
                    _pool.TryRent(out rented[i], Vector3.zero, Quaternion.identity),
                    Is.True);
            }

            Assert.That(
                _pool.TryRent(out _, Vector3.zero, Quaternion.identity),
                Is.False);
        }

        [Test]
        public void DestroyRentedInstance_ReleasesCapacityAndRentedCount()
        {
            PooledInstance instance = _pool.Rent(Vector3.zero, Quaternion.identity);

            Object.DestroyImmediate(instance.gameObject);

            Assert.That(_pool.CountAll, Is.Zero);
            Assert.That(_pool.CountRented, Is.Zero);
            Assert.That(
                _pool.TryRent(out PooledInstance replacement, Vector3.zero, Quaternion.identity),
                Is.True);
            Assert.That(replacement, Is.Not.Null);
        }

        [Test]
        public void DestroyInactiveInstance_RemovesStaleStackEntry()
        {
            PooledInstance instance = _pool.Rent(Vector3.zero, Quaternion.identity);
            _pool.Release(instance);

            Object.DestroyImmediate(instance.gameObject);

            Assert.That(_pool.CountAll, Is.Zero);
            Assert.That(_pool.CountInactive, Is.Zero);
            Assert.That(
                _pool.TryRent(out PooledInstance replacement, Vector3.zero, Quaternion.identity),
                Is.True);
            Assert.That(replacement, Is.Not.Null);
        }
    }
}
