using System;
using System.Collections.Generic;
using System.Diagnostics;
using Rutin.GameFramework.Utilities;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Rutin.GameFramework.Ticking
{
    public readonly struct TickBatchStats
    {
        public TickBatchStats(
            int registeredCount,
            int visitedCount,
            int processedCount,
            int quarantinedCount,
            bool roundCompleted,
            double elapsedMilliseconds)
        {
            RegisteredCount = registeredCount;
            VisitedCount = visitedCount;
            ProcessedCount = processedCount;
            QuarantinedCount = quarantinedCount;
            RoundCompleted = roundCompleted;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public int RegisteredCount { get; }

        public int VisitedCount { get; }

        public int ProcessedCount { get; }

        public int QuarantinedCount { get; }

        public bool RoundCompleted { get; }

        public double ElapsedMilliseconds { get; }
    }

    /// <summary>
    /// Dense, round-robin scheduler with O(1) registration removal and a time budget.
    /// </summary>
    public sealed class BudgetedTickScheduler : ITickScheduler
    {
        private readonly List<IGameTickable> _tickables;
        private readonly List<uint> _lastVisitedRounds;
        private readonly List<double> _lastTickTimes;
        private readonly List<int> _consecutiveFailures;
        private readonly List<bool> _hasLoggedFailure;
        private readonly Dictionary<IGameTickable, int> _indices;
        private readonly int _failureQuarantineThreshold;
        private int _cursor;
        private uint _currentRound;
        private int _remainingInRound;
        private bool _isTicking;
        private double _elapsedTime;

        public BudgetedTickScheduler(
            int initialCapacity = 256,
            int failureQuarantineThreshold = 3)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            if (failureQuarantineThreshold < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(failureQuarantineThreshold));
            }

            _tickables = new List<IGameTickable>(initialCapacity);
            _lastVisitedRounds = new List<uint>(initialCapacity);
            _lastTickTimes = new List<double>(initialCapacity);
            _consecutiveFailures = new List<int>(initialCapacity);
            _hasLoggedFailure = new List<bool>(initialCapacity);
            _indices = new Dictionary<IGameTickable, int>(
                initialCapacity,
                ReferenceEqualityComparer<IGameTickable>.Instance);
            _failureQuarantineThreshold = failureQuarantineThreshold;
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
            _lastVisitedRounds.Add(_isTicking ? _currentRound : 0);
            _lastTickTimes.Add(_elapsedTime);
            _consecutiveFailures.Add(0);
            _hasLoggedFailure.Add(false);
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
            uint removedLastVisitedRound = _lastVisitedRounds[index];
            uint lastVisitedRound = _lastVisitedRounds[lastIndex];
            double lastTickTime = _lastTickTimes[lastIndex];
            int consecutiveFailures = _consecutiveFailures[lastIndex];
            bool hasLoggedFailure = _hasLoggedFailure[lastIndex];

            if (_isTicking && removedLastVisitedRound != _currentRound)
            {
                _remainingInRound--;
            }

            _tickables.RemoveAt(lastIndex);
            _lastVisitedRounds.RemoveAt(lastIndex);
            _lastTickTimes.RemoveAt(lastIndex);
            _consecutiveFailures.RemoveAt(lastIndex);
            _hasLoggedFailure.RemoveAt(lastIndex);
            _indices.Remove(tickable);

            if (index != lastIndex)
            {
                _tickables[index] = last;
                _lastVisitedRounds[index] = lastVisitedRound;
                _lastTickTimes[index] = lastTickTime;
                _consecutiveFailures[index] = consecutiveFailures;
                _hasLoggedFailure[index] = hasLoggedFailure;
                _indices[last] = index;
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
            int maxProcessedItems = int.MaxValue,
            float maxAccumulatedDeltaTime = float.PositiveInfinity)
        {
            if (maxAccumulatedDeltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAccumulatedDeltaTime));
            }

            _elapsedTime += Math.Max(0f, deltaTime);
            int registeredCount = _tickables.Count;
            if (registeredCount == 0 || maxProcessedItems <= 0)
            {
                return new TickBatchStats(
                    registeredCount,
                    0,
                    0,
                    0,
                    registeredCount == 0,
                    0d);
            }

            long startTimestamp = Stopwatch.GetTimestamp();
            int visited = 0;
            int processed = 0;
            int quarantined = 0;
            bool roundCompleted = false;
            bool hasTimeBudget = timeBudgetMilliseconds > 0d;
            BeginRound(registeredCount);

            try
            {
                while (_remainingInRound > 0 && visited < maxProcessedItems)
                {
                    if (_cursor >= _tickables.Count)
                    {
                        _cursor = 0;
                    }

                    int currentIndex = _cursor;
                    _cursor++;

                    if (_lastVisitedRounds[currentIndex] == _currentRound)
                    {
                        continue;
                    }

                    IGameTickable tickable = _tickables[currentIndex];
                    int failuresBeforeVisit = _consecutiveFailures[currentIndex];
                    _lastVisitedRounds[currentIndex] = _currentRound;
                    _remainingInRound--;
                    visited++;

                    double accumulatedDeltaTime =
                        Math.Max(0d, _elapsedTime - _lastTickTimes[currentIndex]);
                    _lastTickTimes[currentIndex] = _elapsedTime;
                    bool isTickEnabled;
                    try
                    {
                        isTickEnabled = tickable.IsTickEnabled;
                    }
                    catch (Exception exception)
                    {
                        if (HandleTickFailure(tickable, exception))
                        {
                            quarantined++;
                        }

                        if (hasTimeBudget &&
                            GetElapsedMilliseconds(startTimestamp) >= timeBudgetMilliseconds)
                        {
                            break;
                        }

                        continue;
                    }

                    if (!isTickEnabled)
                    {
                        if (failuresBeforeVisit > 0)
                        {
                            ResetFailureCount(tickable);
                        }

                        if (hasTimeBudget &&
                            GetElapsedMilliseconds(startTimestamp) >= timeBudgetMilliseconds)
                        {
                            break;
                        }

                        continue;
                    }

                    processed++;
                    try
                    {
                        tickable.Tick((float)Math.Min(
                            accumulatedDeltaTime,
                            maxAccumulatedDeltaTime));
                        if (failuresBeforeVisit > 0)
                        {
                            ResetFailureCount(tickable);
                        }
                    }
                    catch (Exception exception)
                    {
                        if (HandleTickFailure(tickable, exception))
                        {
                            quarantined++;
                        }
                    }

                    if (hasTimeBudget &&
                        GetElapsedMilliseconds(startTimestamp) >= timeBudgetMilliseconds)
                    {
                        break;
                    }
                }

                roundCompleted = _remainingInRound == 0;
            }
            finally
            {
                _isTicking = false;
                _remainingInRound = 0;
            }

            return new TickBatchStats(
                registeredCount,
                visited,
                processed,
                quarantined,
                roundCompleted,
                GetElapsedMilliseconds(startTimestamp));
        }

        public void Clear()
        {
            _tickables.Clear();
            _lastVisitedRounds.Clear();
            _lastTickTimes.Clear();
            _consecutiveFailures.Clear();
            _hasLoggedFailure.Clear();
            _indices.Clear();
            _cursor = 0;
            _remainingInRound = 0;
            _elapsedTime = 0d;
        }

        private void BeginRound(int registeredCount)
        {
            _currentRound++;
            if (_currentRound == 0)
            {
                for (int i = 0; i < _lastVisitedRounds.Count; i++)
                {
                    _lastVisitedRounds[i] = 0;
                }

                _currentRound = 1;
            }

            _remainingInRound = registeredCount;
            _isTicking = true;
        }

        private static double GetElapsedMilliseconds(long startTimestamp)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            return elapsedTicks * 1000d / Stopwatch.Frequency;
        }

        private bool HandleTickFailure(
            IGameTickable tickable,
            Exception exception)
        {
            if (!_indices.TryGetValue(tickable, out int index))
            {
                Debug.LogException(exception, tickable as Object);
                return false;
            }

            if (!_hasLoggedFailure[index])
            {
                Debug.LogException(exception, tickable as Object);
                _hasLoggedFailure[index] = true;
            }

            int failureCount = _consecutiveFailures[index] + 1;
            _consecutiveFailures[index] = failureCount;
            if (failureCount < _failureQuarantineThreshold)
            {
                return false;
            }

            Debug.LogException(exception, tickable as Object);
            Unregister(tickable);
            return true;
        }

        private void ResetFailureCount(IGameTickable tickable)
        {
            if (_indices.TryGetValue(tickable, out int index))
            {
                _consecutiveFailures[index] = 0;
            }
        }
    }
}
