using Rutin.GameFramework.Management;
using UnityEngine;
using UnityEngine.Serialization;

namespace Rutin.GameFramework.Ticking
{
    /// <summary>
    /// One Update loop for many registered gameplay features.
    /// </summary>
    [DefaultExecutionOrder(-8990)]
    public sealed class TickSchedulerService : GameServiceBehaviour, ITickScheduler
    {
        [Min(0f)]
        [SerializeField] private float frameBudgetMilliseconds = 2f;

        [Min(1)]
        [FormerlySerializedAs("maxProcessedItemsPerFrame")]
        [SerializeField] private int maxVisitedItemsPerFrame = 4096;

        [Min(1)]
        [SerializeField] private int initialCapacity = 1024;

        [Min(0f)]
        [SerializeField] private float maxAccumulatedDeltaTime = 0.25f;

        [Min(1)]
        [SerializeField] private int saturationWarningFrameThreshold = 120;

        [Min(1)]
        [SerializeField] private int failureQuarantineThreshold = 3;

        private BudgetedTickScheduler _scheduler;
        private bool _hasShutDown;
        private int _consecutiveSaturatedFrames;

        public int Count => _scheduler?.Count ?? 0;

        public TickBatchStats LastFrameStats { get; private set; }

        public long TotalQuarantinedCount { get; private set; }

        public double TotalDiscardedDeltaTimeSeconds { get; private set; }

        protected override void RegisterServiceContracts()
        {
            RegisterContract<ITickScheduler>();
        }

        protected override void OnServiceInitialized()
        {
            _hasShutDown = false;
            TotalQuarantinedCount = 0;
            TotalDiscardedDeltaTimeSeconds = 0d;
            EnsureScheduler();
        }

        protected override void OnServiceShutdown()
        {
            _scheduler?.Clear();
            _scheduler = null;
            _hasShutDown = true;
            _consecutiveSaturatedFrames = 0;
            LastFrameStats = default;
        }

        private void Update()
        {
            if (_scheduler == null || !IsServiceActive)
            {
                return;
            }

            LastFrameStats = _scheduler.Tick(
                Time.deltaTime,
                frameBudgetMilliseconds,
                maxVisitedItemsPerFrame,
                maxAccumulatedDeltaTime);
            TotalDiscardedDeltaTimeSeconds +=
                LastFrameStats.DiscardedDeltaTimeSeconds;
            if (LastFrameStats.QuarantinedCount > 0)
            {
                TotalQuarantinedCount += LastFrameStats.QuarantinedCount;
                Debug.LogWarning(
                    $"{nameof(TickSchedulerService)} quarantined " +
                    $"{LastFrameStats.QuarantinedCount} tickable(s) after repeated failures. " +
                    $"Session total={TotalQuarantinedCount}.",
                    this);
            }

            UpdateSaturationDiagnostics();
        }

        public bool Register(IGameTickable tickable)
        {
            if (_hasShutDown)
            {
                Debug.LogWarning(
                    $"{nameof(TickSchedulerService)} rejected registration after shutdown.",
                    this);
                return false;
            }

            return EnsureScheduler().Register(tickable);
        }

        public bool Unregister(IGameTickable tickable)
        {
            return _scheduler != null && _scheduler.Unregister(tickable);
        }

        private BudgetedTickScheduler EnsureScheduler()
        {
            _scheduler ??= new BudgetedTickScheduler(
                initialCapacity,
                Mathf.Max(1, failureQuarantineThreshold));
            return _scheduler;
        }

        private void UpdateSaturationDiagnostics()
        {
            if (LastFrameStats.RoundCompleted)
            {
                _consecutiveSaturatedFrames = 0;
                return;
            }

            _consecutiveSaturatedFrames++;
            if (_consecutiveSaturatedFrames == saturationWarningFrameThreshold)
            {
                Debug.LogWarning(
                    $"{nameof(TickSchedulerService)} has exceeded its processing budget for " +
                    $"{_consecutiveSaturatedFrames} consecutive frames. Registered=" +
                    $"{LastFrameStats.RegisteredCount}, visited={LastFrameStats.VisitedCount}, " +
                    $"clamped={LastFrameStats.ClampedTickCount}, discardedSeconds=" +
                    $"{TotalDiscardedDeltaTimeSeconds:F3}.",
                    this);
            }
        }
    }
}
