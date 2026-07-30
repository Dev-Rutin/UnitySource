using Rutin.GameFramework.Management;
using UnityEngine;

namespace Rutin.GameFramework.Ticking
{
    /// <summary>
    /// One Update loop for many registered gameplay features.
    /// </summary>
    public sealed class TickSchedulerService : GameServiceBehaviour, ITickScheduler
    {
        [Min(0f)]
        [SerializeField] private float frameBudgetMilliseconds = 2f;

        [Min(1)]
        [SerializeField] private int maxProcessedItemsPerFrame = 4096;

        [Min(1)]
        [SerializeField] private int initialCapacity = 1024;

        private BudgetedTickScheduler _scheduler;

        public int Count => _scheduler?.Count ?? 0;

        public TickBatchStats LastFrameStats { get; private set; }

        protected override void RegisterServiceContracts()
        {
            RegisterContract<ITickScheduler>();
        }

        protected override void OnServiceInitialized()
        {
            _scheduler = new BudgetedTickScheduler(initialCapacity);
        }

        protected override void OnServiceShutdown()
        {
            _scheduler = null;
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
            return _scheduler != null && _scheduler.Register(tickable);
        }

        public bool Unregister(IGameTickable tickable)
        {
            return _scheduler != null && _scheduler.Unregister(tickable);
        }
    }
}
