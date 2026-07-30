using NUnit.Framework;
using Rutin.GameFramework.Ticking;

namespace Rutin.GameFramework.Tests.EditMode
{
    public sealed class BudgetedTickSchedulerTests
    {
        private sealed class ProbeTickable : IGameTickable
        {
            public bool IsTickEnabled { get; set; } = true;

            public int TickCount { get; private set; }

            public void Tick(float deltaTime)
            {
                TickCount++;
            }
        }

        [Test]
        public void Register_DeduplicatesByReference()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable tickable = new();

            Assert.That(scheduler.Register(tickable), Is.True);
            Assert.That(scheduler.Register(tickable), Is.False);
            Assert.That(scheduler.Count, Is.EqualTo(1));
        }

        [Test]
        public void Tick_RespectsItemBudgetAndRoundRobins()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable first = new();
            ProbeTickable second = new();
            ProbeTickable third = new();
            scheduler.Register(first);
            scheduler.Register(second);
            scheduler.Register(third);

            TickBatchStats firstBatch = scheduler.Tick(0.016f, 0d, 2);
            TickBatchStats secondBatch = scheduler.Tick(0.016f, 0d, 2);

            Assert.That(firstBatch.ProcessedCount, Is.EqualTo(2));
            Assert.That(secondBatch.ProcessedCount, Is.EqualTo(2));
            Assert.That(first.TickCount + second.TickCount + third.TickCount, Is.EqualTo(4));
            Assert.That(third.TickCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Unregister_SwapRemovalKeepsRemainingTickables()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable first = new();
            ProbeTickable removed = new();
            ProbeTickable last = new();
            scheduler.Register(first);
            scheduler.Register(removed);
            scheduler.Register(last);

            Assert.That(scheduler.Unregister(removed), Is.True);
            scheduler.Tick(0.016f, 0d);

            Assert.That(first.TickCount, Is.EqualTo(1));
            Assert.That(removed.TickCount, Is.Zero);
            Assert.That(last.TickCount, Is.EqualTo(1));
        }
    }
}
