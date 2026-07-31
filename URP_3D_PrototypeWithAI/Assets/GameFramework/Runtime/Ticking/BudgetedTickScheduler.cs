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
            : this(
                registeredCount,
                visitedCount,
                processedCount,
                quarantinedCount,
                0,
                0d,
                roundCompleted,
                elapsedMilliseconds)
        {
        }

        public TickBatchStats(
            int registeredCount,
            int visitedCount,
            int processedCount,
            int quarantinedCount,
            int clampedTickCount,
            double discardedDeltaTimeSeconds,
            bool roundCompleted,
            double elapsedMilliseconds)
        {
            RegisteredCount = registeredCount;
            VisitedCount = visitedCount;
            ProcessedCount = processedCount;
            QuarantinedCount = quarantinedCount;
            ClampedTickCount = clampedTickCount;
            DiscardedDeltaTimeSeconds = discardedDeltaTimeSeconds;
            RoundCompleted = roundCompleted;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        public int RegisteredCount { get; }

        public int VisitedCount { get; }

        public int ProcessedCount { get; }

        public int QuarantinedCount { get; }

        public int ClampedTickCount { get; }

        public double DiscardedDeltaTimeSeconds { get; }

        public bool RoundCompleted { get; }

        public double ElapsedMilliseconds { get; }
    }

    /// <summary>
    /// Dense, round-robin scheduler with O(1) registration removal and a time budget.
    /// </summary>
    public sealed class BudgetedTickScheduler : ITickScheduler
    {
        private const int DisabledTimeBudgetCheckInterval = 16;

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
            return Unregister(tickable, TickUnregistrationReason.Explicit);
        }

        private bool Unregister(
            IGameTickable tickable,
            TickUnregistrationReason reason)
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

            if (tickable is ITickSchedulerRegistrationObserver observer)
            {
                NotifyUnregistered(observer, reason);
            }

            return true;
        }

        /// <summary>
        /// Visits registered tickables within both elapsed-time and item-count budgets.
        /// Disabled registrations consume the visit budget even though they are not processed.
        /// </summary>
        public TickBatchStats Tick(
            float deltaTime,
            double timeBudgetMilliseconds,
            int maxVisitedItems = int.MaxValue,
            float maxAccumulatedDeltaTime = float.PositiveInfinity)
        {
            if (maxAccumulatedDeltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAccumulatedDeltaTime));
            }

            _elapsedTime += Math.Max(0f, deltaTime);
            int registeredCount = _tickables.Count;
            if (registeredCount == 0 || maxVisitedItems <= 0)
            {
                return new TickBatchStats(
                    registeredCount,
                    0,
                    0,
                    0,
                    0,
                    0d,
                    registeredCount == 0,
                    0d);
            }

            long startTimestamp = Stopwatch.GetTimestamp();
            int visited = 0;
            int processed = 0;
            int quarantined = 0;
            int clampedTickCount = 0;
            double discardedDeltaTimeSeconds = 0d;
            bool roundCompleted = false;
            bool hasTimeBudget = timeBudgetMilliseconds > 0d;
            BeginRound(registeredCount);

            try
            {
                while (_remainingInRound > 0 && visited < maxVisitedItems)
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

                        // Disabled getters are expected to be cheap and can dominate large
                        // sleeping populations. Sample the clock periodically instead of
                        // paying for Stopwatch.GetTimestamp on every disabled registration.
                        if (hasTimeBudget &&
                            (visited == 1 ||
                             (visited & (DisabledTimeBudgetCheckInterval - 1)) == 0) &&
                            GetElapsedMilliseconds(startTimestamp) >= timeBudgetMilliseconds)
                        {
                            break;
                        }

                        continue;
                    }

                    processed++;
                    double deliveredDeltaTime = Math.Min(
                        accumulatedDeltaTime,
                        maxAccumulatedDeltaTime);
                    double discardedDeltaTime =
                        accumulatedDeltaTime - deliveredDeltaTime;
                    if (discardedDeltaTime > 0d)
                    {
                        clampedTickCount++;
                        discardedDeltaTimeSeconds += discardedDeltaTime;
                    }

                    try
                    {
                        tickable.Tick((float)deliveredDeltaTime);
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
                clampedTickCount,
                discardedDeltaTimeSeconds,
                roundCompleted,
                GetElapsedMilliseconds(startTimestamp));
        }

        public void Clear()
        {
            ITickSchedulerRegistrationObserver[] observers =
                new ITickSchedulerRegistrationObserver[_tickables.Count];
            int observerCount = 0;
            for (int i = 0; i < _tickables.Count; i++)
            {
                if (_tickables[i] is ITickSchedulerRegistrationObserver observer)
                {
                    observers[observerCount++] = observer;
                }
            }

            _tickables.Clear();
            _lastVisitedRounds.Clear();
            _lastTickTimes.Clear();
            _consecutiveFailures.Clear();
            _hasLoggedFailure.Clear();
            _indices.Clear();
            _cursor = 0;
            _currentRound = 0;
            _remainingInRound = 0;
            _isTicking = false;
            _elapsedTime = 0d;

            for (int i = 0; i < observerCount; i++)
            {
                NotifyUnregistered(
                    observers[i],
                    TickUnregistrationReason.SchedulerCleared);
            }
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

        private void NotifyUnregistered(
            ITickSchedulerRegistrationObserver observer,
            TickUnregistrationReason reason)
        {
            try
            {
                observer.OnTickSchedulerUnregistered(this, reason);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, observer as Object);
            }
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
            Unregister(tickable, TickUnregistrationReason.Quarantined);
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
