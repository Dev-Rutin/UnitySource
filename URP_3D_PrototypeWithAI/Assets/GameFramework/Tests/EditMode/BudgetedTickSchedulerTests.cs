using System;
using NUnit.Framework;
using Rutin.GameFramework.Ticking;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rutin.GameFramework.Tests.EditMode
{
    public sealed class BudgetedTickSchedulerTests
    {
        private sealed class ProbeTickable : IGameTickable
        {
            public bool IsTickEnabled { get; set; } = true;

            public int TickCount { get; private set; }

            public float LastDeltaTime { get; private set; }

            public Action OnTick { get; set; }

            public void Tick(float deltaTime)
            {
                TickCount++;
                LastDeltaTime = deltaTime;
                OnTick?.Invoke();
            }
        }

        private sealed class ThrowingTickable : IGameTickable
        {
            private readonly bool _throwFromEnabledGetter;

            public ThrowingTickable(bool throwFromEnabledGetter)
            {
                _throwFromEnabledGetter = throwFromEnabledGetter;
            }

            public bool IsTickEnabled
            {
                get
                {
                    if (_throwFromEnabledGetter)
                    {
                        throw new InvalidOperationException("Tick enabled failure");
                    }

                    return true;
                }
            }

            public void Tick(float deltaTime)
            {
                throw new InvalidOperationException("Tick callback failure");
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

        [Test]
        public void Tick_WhenTickablesUnregisterDuringCallback_DoesNotVisitTwice()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable first = new();
            ProbeTickable second = new();
            ProbeTickable third = new();
            ProbeTickable fourth = new();
            ProbeTickable fifth = new();
            scheduler.Register(first);
            scheduler.Register(second);
            scheduler.Register(third);
            scheduler.Register(fourth);
            scheduler.Register(fifth);

            first.OnTick = () =>
            {
                scheduler.Unregister(second);
                scheduler.Unregister(third);
            };

            TickBatchStats stats = scheduler.Tick(0.016f, 0d);

            Assert.That(stats.RegisteredCount, Is.EqualTo(5));
            Assert.That(stats.VisitedCount, Is.EqualTo(3));
            Assert.That(first.TickCount, Is.EqualTo(1));
            Assert.That(second.TickCount, Is.Zero);
            Assert.That(third.TickCount, Is.Zero);
            Assert.That(fourth.TickCount, Is.EqualTo(1));
            Assert.That(fifth.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Unregister_AfterPartialRound_DoesNotSkipSwappedTickable()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable first = new();
            ProbeTickable second = new();
            ProbeTickable third = new();
            ProbeTickable fourth = new();
            ProbeTickable fifth = new();
            scheduler.Register(first);
            scheduler.Register(second);
            scheduler.Register(third);
            scheduler.Register(fourth);
            scheduler.Register(fifth);

            scheduler.Tick(0.016f, 0d, 3);
            scheduler.Unregister(first);
            scheduler.Tick(0.016f, 0d);

            Assert.That(second.TickCount, Is.EqualTo(2));
            Assert.That(third.TickCount, Is.EqualTo(2));
            Assert.That(fourth.TickCount, Is.EqualTo(1));
            Assert.That(fifth.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Register_DuringTick_DefersNewTickableUntilNextBatch()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable first = new();
            ProbeTickable added = new();
            first.OnTick = () => scheduler.Register(added);
            scheduler.Register(first);

            scheduler.Tick(0.016f, 0d);

            Assert.That(first.TickCount, Is.EqualTo(1));
            Assert.That(added.TickCount, Is.Zero);

            scheduler.Tick(0.016f, 0d);

            Assert.That(added.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_BudgetDelayedItemReceivesAccumulatedDeltaTime()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable first = new();
            ProbeTickable delayed = new();
            scheduler.Register(first);
            scheduler.Register(delayed);

            scheduler.Tick(0.1f, 0d, 1);
            scheduler.Tick(0.1f, 0d, 1);

            Assert.That(first.LastDeltaTime, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(delayed.LastDeltaTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void Tick_ClampsAccumulatedDeltaTime()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable first = new();
            ProbeTickable delayed = new();
            scheduler.Register(first);
            scheduler.Register(delayed);

            scheduler.Tick(0.2f, 0d, 1, 0.25f);
            scheduler.Tick(0.2f, 0d, 1, 0.25f);

            Assert.That(delayed.LastDeltaTime, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void Tick_CallbackFailuresAreLoggedAndQuarantined()
        {
            BudgetedTickScheduler scheduler = new();
            ProbeTickable healthy = new();
            scheduler.Register(new ThrowingTickable(true));
            scheduler.Register(new ThrowingTickable(false));
            scheduler.Register(healthy);

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Tick enabled failure"));
            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex("Tick callback failure"));

            scheduler.Tick(0.016f, 0d);

            Assert.That(healthy.TickCount, Is.EqualTo(1));
            Assert.That(scheduler.Count, Is.EqualTo(1));
        }
    }
}
