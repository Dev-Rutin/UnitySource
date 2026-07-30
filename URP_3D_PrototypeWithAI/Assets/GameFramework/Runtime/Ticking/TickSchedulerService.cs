using Rutin.GameFramework.Management;
using UnityEngine;

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
        [SerializeField] private int maxProcessedItemsPerFrame = 4096;

        [Min(1)]
        [SerializeField] private int initialCapacity = 1024;

        [Min(0f)]
        [SerializeField] private float maxAccumulatedDeltaTime = 0.25f;

        [Min(1)]
        [SerializeField] private int saturationWarningFrameThreshold = 120;

        private BudgetedTickScheduler _scheduler;
        private bool _hasShutDown;
        private int _consecutiveSaturatedFrames;

        public int Count => _scheduler?.Count ?? 0;

        public TickBatchStats LastFrameStats { get; private set; }

        protected override void RegisterServiceContracts()
        {
            RegisterContract<ITickScheduler>();
        }

        protected override void OnServiceInitialized()
        {
            _hasShutDown = false;
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
                maxProcessedItemsPerFrame,
                maxAccumulatedDeltaTime);
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
            _scheduler ??= new BudgetedTickScheduler(initialCapacity);
            return _scheduler;
        }

        private void UpdateSaturationDiagnostics()
        {
            if (LastFrameStats.VisitedCount >= LastFrameStats.RegisteredCount)
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
                    $"{LastFrameStats.RegisteredCount}, visited={LastFrameStats.VisitedCount}.",
                    this);
            }
        }
    }
}
