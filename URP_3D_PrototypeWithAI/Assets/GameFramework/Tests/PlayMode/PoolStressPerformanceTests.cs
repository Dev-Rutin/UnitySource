using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Rutin.GameFramework.Factory;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rutin.GameFramework.Tests.PlayMode
{
    public sealed class PoolLifecycleProbe : MonoBehaviour, IPoolable
    {
        public static readonly List<string> Events = new();

        private void Awake()
        {
            Events.Add("Awake");
        }

        private void OnEnable()
        {
            Events.Add("OnEnable");
        }

        public void OnRentFromPool()
        {
            Events.Add("OnRentFromPool");
        }

        public void OnReturnToPool()
        {
        }
    }

    public sealed class PoolStressPerformanceTests
    {
        private const int DefaultObjectCount = 5000;
        private const double DefaultCycleBudgetMilliseconds = 1000d;
        private const long DefaultAllocationBudgetBytes = 2 * 1024 * 1024;

        [UnityTest]
        public IEnumerator ActivePrefab_RentCallbacksFollowDocumentedLifecycleOrder()
        {
            GameObject prefab = new("Active Pool Prefab");
            prefab.SetActive(false);
            prefab.AddComponent<PoolLifecycleProbe>();
            prefab.SetActive(true);
            PoolLifecycleProbe.Events.Clear();

            GameObject root = new("Active Pool Root");
            GameObjectPool pool = new(prefab, root.transform, 0, 1);
            try
            {
                PooledInstance first = pool.Rent(Vector3.zero, Quaternion.identity);
                Assert.That(
                    PoolLifecycleProbe.Events,
                    Is.EqualTo(new[] { "OnRentFromPool", "Awake", "OnEnable" }));

                pool.Release(first);
                PoolLifecycleProbe.Events.Clear();
                pool.Rent(Vector3.zero, Quaternion.identity);
                Assert.That(
                    PoolLifecycleProbe.Events,
                    Is.EqualTo(new[] { "OnRentFromPool", "OnEnable" }));
            }
            finally
            {
                pool.Dispose();
                UnityEngine.Object.Destroy(prefab);
                UnityEngine.Object.Destroy(root);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator WarmPool_ActivatesAndReturnsLargeBatchWithinBudget()
        {
            int objectCount = ReadPositiveInt(
                "RUTIN_POOL_STRESS_OBJECTS",
                DefaultObjectCount);
            double cycleBudgetMs = ReadPositiveDouble(
                "RUTIN_POOL_STRESS_BUDGET_MS",
                DefaultCycleBudgetMilliseconds);
            long allocationBudgetBytes = ReadPositiveLong(
                "RUTIN_POOL_STRESS_ALLOC_BYTES",
                DefaultAllocationBudgetBytes);

            GameObject prefab = new("Stress Prefab");
            prefab.SetActive(false);
            GameObject root = new("Stress Pool Root");
            GameObjectPool pool = new(prefab, root.transform, objectCount, objectCount);
            List<PooledInstance> rented = new(objectCount);

            yield return null;
            GC.Collect();
            GC.WaitForPendingFinalizers();

            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < objectCount; i++)
            {
                bool success = pool.TryRent(
                    out PooledInstance instance,
                    new Vector3(i % 100, 0f, i / 100),
                    Quaternion.identity);
                Assert.That(success, Is.True);
                rented.Add(instance);
            }

            for (int i = rented.Count - 1; i >= 0; i--)
            {
                Assert.That(pool.Release(rented[i]), Is.True);
            }

            stopwatch.Stop();
            long allocatedBytes = Math.Max(
                0L,
                GC.GetAllocatedBytesForCurrentThread() - allocationBefore);

            UnityEngine.Debug.Log(
                $"POOL_STRESS objects={objectCount} elapsed_ms=" +
                $"{stopwatch.Elapsed.TotalMilliseconds:F3} allocated_bytes={allocatedBytes}");

            Assert.That(
                stopwatch.Elapsed.TotalMilliseconds,
                Is.LessThanOrEqualTo(cycleBudgetMs),
                "Pooled activation cycle exceeded the configured time budget.");
            Assert.That(
                allocatedBytes,
                Is.LessThanOrEqualTo(allocationBudgetBytes),
                "Pooled activation cycle exceeded the configured allocation budget.");
            Assert.That(pool.CountRented, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(objectCount));

            pool.Dispose();
            UnityEngine.Object.Destroy(prefab);
            UnityEngine.Object.Destroy(root);
            yield return null;
        }

        private static int ReadPositiveInt(string variable, int fallback)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(variable), out int value) &&
                   value > 0
                ? value
                : fallback;
        }

        private static long ReadPositiveLong(string variable, long fallback)
        {
            return long.TryParse(Environment.GetEnvironmentVariable(variable), out long value) &&
                   value > 0
                ? value
                : fallback;
        }

        private static double ReadPositiveDouble(string variable, double fallback)
        {
            return double.TryParse(
                       Environment.GetEnvironmentVariable(variable),
                       out double value) &&
                   value > 0d
                ? value
                : fallback;
        }
    }
}
