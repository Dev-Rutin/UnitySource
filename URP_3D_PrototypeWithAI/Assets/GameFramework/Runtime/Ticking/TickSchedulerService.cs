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

        private BudgetedTickScheduler _scheduler;
        private bool _hasShutDown;

        public int Count => EnsureScheduler().Count;

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
            LastFrameStats = default;
        }

        private void Update()
        {
            if (_scheduler == null)
            {
                return;
            }

            LastFrameStats = _scheduler.Tick(
                Time.deltaTime,
                frameBudgetMilliseconds,
                maxProcessedItemsPerFrame);
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
    }
}
