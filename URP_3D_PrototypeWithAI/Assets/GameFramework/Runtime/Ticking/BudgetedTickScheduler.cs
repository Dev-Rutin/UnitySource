using System;
using System.Collections.Generic;
using System.Diagnostics;
using Rutin.GameFramework.Utilities;

namespace Rutin.GameFramework.Ticking
{
    public readonly struct TickBatchStats
    {
        public TickBatchStats(
            int registeredCount,
            int visitedCount,
            int processedCount,
            double elapsedMilliseconds)
        {
            RegisteredCount = registeredCount;
            VisitedCount = visitedCount;
            ProcessedCount = processedCount;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public int RegisteredCount { get; }

        public int VisitedCount { get; }

        public int ProcessedCount { get; }

        public double ElapsedMilliseconds { get; }
    }

    /// <summary>
    /// Dense, round-robin scheduler with O(1) registration removal and a time budget.
    /// </summary>
    public sealed class BudgetedTickScheduler : ITickScheduler
    {
        private readonly List<IGameTickable> _tickables;
        private readonly Dictionary<IGameTickable, int> _indices;
        private int _cursor;

        public BudgetedTickScheduler(int initialCapacity = 256)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _tickables = new List<IGameTickable>(initialCapacity);
            _indices = new Dictionary<IGameTickable, int>(
                initialCapacity,
                ReferenceEqualityComparer<IGameTickable>.Instance);
        }

        public int Count => _tickables.Count;

        public bool Register(IGameTickable tickable)
        {
            if (tickable == null)
            {
                throw new ArgumentNullException(nameof(tickable));
            }

            if (_indices.ContainsKey(tickable))
            {
                return false;
            }

            int index = _tickables.Count;
            _tickables.Add(tickable);
            _indices.Add(tickable, index);
            return true;
        }

        public bool Unregister(IGameTickable tickable)
        {
            if (tickable == null || !_indices.TryGetValue(tickable, out int index))
            {
                return false;
            }

            int lastIndex = _tickables.Count - 1;
            IGameTickable last = _tickables[lastIndex];
            _tickables.RemoveAt(lastIndex);
            _indices.Remove(tickable);

            if (index != lastIndex)
            {
                _tickables[index] = last;
                _indices[last] = index;
            }

            if (index < _cursor)
            {
                _cursor--;
            }

            if (_cursor < 0 || _cursor >= _tickables.Count)
            {
                _cursor = 0;
            }

            return true;
        }

        public TickBatchStats Tick(
            float deltaTime,
            double timeBudgetMilliseconds,
            int maxProcessedItems = int.MaxValue)
        {
            int registeredCount = _tickables.Count;
            if (registeredCount == 0 || maxProcessedItems <= 0)
            {
                return new TickBatchStats(registeredCount, 0, 0, 0d);
            }

            long startTimestamp = Stopwatch.GetTimestamp();
            int visited = 0;
            int processed = 0;
            bool hasTimeBudget = timeBudgetMilliseconds > 0d;

            while (visited < registeredCount && processed < maxProcessedItems)
            {
                if (_cursor >= _tickables.Count)
                {
                    _cursor = 0;
                }

                IGameTickable tickable = _tickables[_cursor];
                _cursor++;
                visited++;

                if (!tickable.IsTickEnabled)
                {
                    continue;
                }

                tickable.Tick(deltaTime);
                processed++;

                if (hasTimeBudget &&
                    GetElapsedMilliseconds(startTimestamp) >= timeBudgetMilliseconds)
                {
                    break;
                }
            }

            return new TickBatchStats(
                registeredCount,
                visited,
                processed,
                GetElapsedMilliseconds(startTimestamp));
        }

        private static double GetElapsedMilliseconds(long startTimestamp)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            return elapsedTicks * 1000d / Stopwatch.Frequency;
        }
    }
}
