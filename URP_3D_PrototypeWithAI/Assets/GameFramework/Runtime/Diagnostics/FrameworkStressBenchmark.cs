using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Rutin.GameFramework.Factory;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Rutin.GameFramework.Diagnostics
{
    [Serializable]
    public struct StressBenchmarkResult
    {
        public int objectCount;
        public int measuredCycles;
        public double initialActivationMilliseconds;
        public double averageCycleMilliseconds;
        public long measuredAllocatedBytes;
        public bool passed;
    }

    /// <summary>
    /// Add this component to a benchmark scene and reference a configured factory.
    /// It measures bulk activation plus steady-state rent/return cycles.
    /// </summary>
    public sealed class FrameworkStressBenchmark : MonoBehaviour
    {
        [SerializeField] private PooledObjectFactory factory;
        [SerializeField] private int typeId = 1;
        [Min(1)]
        [SerializeField] private int objectCount = 5000;
        [Min(1)]
        [SerializeField] private int measuredCycles = 10;
        [Min(0.01f)]
        [SerializeField] private float initialActivationBudgetMilliseconds = 1000f;
        [Min(0.01f)]
        [SerializeField] private float averageCycleBudgetMilliseconds = 250f;
        [Min(0)]
        [SerializeField] private long allocationBudgetBytes = 1048576;
        [SerializeField] private bool runOnStart;

        private readonly List<PooledInstance> _activeInstances = new();
        private bool _running;

        public StressBenchmarkResult LastResult { get; private set; }

        private IEnumerator Start()
        {
            if (runOnStart)
            {
                yield return Run();
            }
        }

        [ContextMenu("Run Stress Benchmark")]
        public void RunFromContextMenu()
        {
            if (Application.isPlaying && !_running)
            {
                StartCoroutine(Run());
            }
        }

        public IEnumerator Run()
        {
            if (_running)
            {
                yield break;
            }

            if (factory == null)
            {
                throw new InvalidOperationException("A PooledObjectFactory is required.");
            }

            if (!factory.TryGetPool(typeId, out GameObjectPool pool))
            {
                throw new InvalidOperationException(
                    $"No pool is registered for type ID {typeId}.");
            }

            _running = true;
            try
            {
                _activeInstances.Clear();
                if (_activeInstances.Capacity < objectCount)
                {
                    _activeInstances.Capacity = objectCount;
                }

                pool.Warmup(objectCount);
                yield return null;

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch stopwatch = Stopwatch.StartNew();
                RentAll(pool);
                stopwatch.Stop();
                double initialActivationMs = stopwatch.Elapsed.TotalMilliseconds;

                ReturnAll(pool);
                yield return null;

                double totalCycleMilliseconds = 0d;
                for (int cycle = 0; cycle < measuredCycles; cycle++)
                {
                    stopwatch.Restart();
                    RentAll(pool);
                    ReturnAll(pool);
                    stopwatch.Stop();
                    totalCycleMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
                    yield return null;
                }

                long allocatedBytes = Math.Max(
                    0L,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
                double averageCycleMs = totalCycleMilliseconds / measuredCycles;
                bool passed =
                    initialActivationMs <= initialActivationBudgetMilliseconds &&
                    averageCycleMs <= averageCycleBudgetMilliseconds &&
                    allocatedBytes <= allocationBudgetBytes;

                LastResult = new StressBenchmarkResult
                {
                    objectCount = objectCount,
                    measuredCycles = measuredCycles,
                    initialActivationMilliseconds = initialActivationMs,
                    averageCycleMilliseconds = averageCycleMs,
                    measuredAllocatedBytes = allocatedBytes,
                    passed = passed
                };

                string summary =
                    $"Framework stress benchmark: objects={objectCount}, " +
                    $"activate={initialActivationMs:F2} ms, " +
                    $"average cycle={averageCycleMs:F2} ms, " +
                    $"allocated={allocatedBytes} bytes, passed={passed}.";

                if (passed)
                {
                    Debug.Log(summary, this);
                }
                else
                {
                    Debug.LogError(summary, this);
                }
            }
            finally
            {
                try
                {
                    ReturnAll(pool);
                }
                finally
                {
                    _running = false;
                }
            }
        }

        private void RentAll(GameObjectPool pool)
        {
            for (int i = 0; i < objectCount; i++)
            {
                if (!pool.TryRent(
                        out PooledInstance instance,
                        new Vector3(i % 100, 0f, i / 100),
                        Quaternion.identity,
                        null))
                {
                    throw new InvalidOperationException(
                        $"Pool exhausted at {i} of {objectCount} instances.");
                }

                _activeInstances.Add(instance);
            }
        }

        private void ReturnAll(GameObjectPool pool)
        {
            for (int i = _activeInstances.Count - 1; i >= 0; i--)
            {
                pool.Release(_activeInstances[i]);
            }

            _activeInstances.Clear();
        }
    }
}
