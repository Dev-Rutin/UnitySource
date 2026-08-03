using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Rutin.GameFramework.Core;
using Rutin.GameFramework.Npc;
using Rutin.GameFramework.Player;
using Rutin.GameFramework.Ticking;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Rutin.GameFramework.Tests.PlayMode
{
    public sealed class NpcFeatureTests
    {
        private sealed class ProbeCommandConsumer :
            MonoBehaviour,
            IPlayerCommandConsumer
        {
            public int CommandOrder => 0;

            public int CallCount { get; private set; }

            public int ResetCount { get; private set; }

            public PlayerCommand LastCommand { get; private set; }

            public void ProcessPlayerCommand(
                PlayerCommand command,
                float deltaTime)
            {
                CallCount++;
                LastCommand = command;
            }

            public void ResetPlayerCommandState()
            {
                ResetCount++;
                LastCommand = PlayerCommand.Neutral;
            }
        }

        private sealed class ProbeDecisionProvider :
            MonoBehaviour,
            INpcDecisionProvider
        {
            public int DecisionOrder { get; set; }

            public bool HandlesDecision { get; set; } = true;

            public NpcDecision Decision { get; set; }

            public int ResetCount { get; private set; }

            public bool TryDecide(
                in NpcBlackboard blackboard,
                float deltaTime,
                out NpcDecision decision)
            {
                decision = Decision;
                return HandlesDecision;
            }

            public void ResetNpcDecisionState()
            {
                ResetCount++;
            }
        }

        private readonly List<GameObject> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
                }
            }

            _createdObjects.Clear();
        }

        [Test]
        public void Brain_TransitionsBetweenChasePatrolAndIdle()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateNpc(
                scheduler,
                "State NPC",
                out NpcBrainFeature brain,
                out _,
                out ProbeCommandConsumer consumer,
                activate: false);
            TransformTargetSensorFeature sensor =
                npc.AddComponent<TransformTargetSensorFeature>();
            IdlePatrolChaseDecisionFeature decision =
                npc.AddComponent<IdlePatrolChaseDecisionFeature>();
            GameObject patrolPoint = CreateObject("Patrol Point");
            patrolPoint.transform.position = Vector3.forward * 10f;
            decision.SetPatrolPoints(new[] { patrolPoint.transform });
            GameObject target = CreateObject("Target");
            target.transform.position = Vector3.right * 10f;
            sensor.SetTarget(target.transform);
            npc.SetActive(true);

            scheduler.Tick(0.016f, 0d);

            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Chase));
            Assert.That(consumer.LastCommand.Move.x, Is.GreaterThan(0.99f));
            Assert.That(scheduler.Count, Is.EqualTo(1),
                "NpcBrainFeature must reuse PlayerCommandFeature scheduling.");

            target.SetActive(false);
            scheduler.Tick(0.016f, 0d);

            Assert.That(brain.Blackboard.HasTarget, Is.False);
            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Patrol));
            Assert.That(consumer.LastCommand.Move.y, Is.GreaterThan(0f));

            decision.SetPatrolPoints(Array.Empty<Transform>());
            scheduler.Tick(0.016f, 0d);

            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Idle));
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Brain_RuntimeProvidersUsePriorityAndCanBeReplaced()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateNpc(
                scheduler,
                "Replaceable Policy NPC",
                out NpcBrainFeature brain,
                out _,
                out ProbeCommandConsumer consumer);
            ProbeDecisionProvider fallback =
                npc.AddComponent<ProbeDecisionProvider>();
            fallback.DecisionOrder = 100;
            fallback.Decision = new NpcDecision(
                NpcBehaviourState.Patrol,
                Vector3.forward);
            ProbeDecisionProvider overrideProvider =
                npc.AddComponent<ProbeDecisionProvider>();
            overrideProvider.DecisionOrder = -100;
            overrideProvider.Decision = new NpcDecision(
                NpcBehaviourState.Chase,
                Vector3.left);
            Assert.That(brain.RegisterDecisionProvider(fallback), Is.True);
            Assert.That(brain.RegisterDecisionProvider(overrideProvider), Is.True);

            scheduler.Tick(0.016f, 0d);

            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Chase));
            Assert.That(consumer.LastCommand.Move.x, Is.LessThan(-0.99f));

            Assert.That(
                brain.UnregisterDecisionProvider(overrideProvider),
                Is.True);
            scheduler.Tick(0.016f, 0d);

            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Patrol));
            Assert.That(consumer.LastCommand.Move.y, Is.GreaterThan(0.99f));

            fallback.enabled = false;
            scheduler.Tick(0.016f, 0d);
            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Idle));
            fallback.enabled = true;
            scheduler.Tick(0.016f, 0d);
            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Patrol));

            brain.SetDecisionEnabled(false);
            Assert.That(fallback.ResetCount, Is.GreaterThan(0));
            scheduler.Tick(0.016f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));

            brain.SetDecisionEnabled(true);
            UnityEngine.Object.DestroyImmediate(fallback);
            scheduler.Tick(0.016f, 0d);
            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Idle));
        }

        [Test]
        public void Brain_PoolingAndSchedulerReplacementClearStaleState()
        {
            BudgetedTickScheduler firstScheduler = new();
            GameObject npc = CreateNpc(
                firstScheduler,
                "Lifecycle NPC",
                out NpcBrainFeature brain,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer,
                activate: false);
            TransformTargetSensorFeature sensor =
                npc.AddComponent<TransformTargetSensorFeature>();
            npc.AddComponent<IdlePatrolChaseDecisionFeature>();
            GameObject target = CreateObject("Lifecycle Target");
            target.transform.position = Vector3.right * 5f;
            sensor.SetTarget(target.transform);
            npc.SetActive(true);
            firstScheduler.Tick(0.016f, 0d);
            Assert.That(brain.Blackboard.HasTarget, Is.True);
            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(1));

            npc.SetActive(false);
            sensor.SetTarget(null);
            npc.SetActive(true);

            Assert.That(brain.Blackboard.HasTarget, Is.False);
            Assert.That(brain.DecisionCount, Is.Zero);
            firstScheduler.Tick(0.016f, 0d);
            Assert.That(brain.CurrentDecision.State, Is.EqualTo(NpcBehaviourState.Idle));
            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(1));

            BudgetedTickScheduler replacementScheduler = new();
            commands.SetTickScheduler(replacementScheduler);

            Assert.That(firstScheduler.Count, Is.Zero);
            Assert.That(replacementScheduler.Count, Is.EqualTo(1));
            Assert.That(brain.DecisionCount, Is.Zero);
            replacementScheduler.Tick(0.016f, 0d);
            Assert.That(brain.DecisionCount, Is.EqualTo(1));
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Brain_SanitizesDecisionVectorsAndCadenceConfiguration()
        {
            NpcDecision malformed = new(
                NpcBehaviourState.Chase,
                new Vector3(float.NaN, 20f, float.PositiveInfinity),
                true);

            Assert.That(malformed.WorldMove, Is.EqualTo(Vector3.zero));

            BudgetedTickScheduler scheduler = new();
            CreateNpc(
                scheduler,
                "Cadence NPC",
                out NpcBrainFeature brain,
                out _,
                out _,
                activate: true,
                configureImmediateCadence: false);
            brain.ConfigureDecisionCadence(float.NaN, float.PositiveInfinity);

            Assert.That(brain.DecisionIntervalSeconds, Is.Zero);
            Assert.That(brain.TimeUntilNextDecisionSeconds, Is.Zero);
        }

        [Test]
        public void ThousandNpcBrains_AreFairAllocationFreeAndWithinBudget()
        {
            const int Population = 1000;
            const int WarmupTicks = 16;
            const int MeasuredTicks = 10;
            BudgetedTickScheduler scheduler = new(Population);
            List<NpcBrainFeature> brains = new(Population);

            for (int i = 0; i < Population; i++)
            {
                GameObject npc = CreateNpc(
                    scheduler,
                    $"Stress NPC {i}",
                    out NpcBrainFeature brain,
                    out _,
                    out _,
                    activate: false);
                npc.AddComponent<IdlePatrolChaseDecisionFeature>();
                npc.SetActive(true);
                brains.Add(brain);
            }

            for (int i = 0; i < 10; i++)
            {
                scheduler.Tick(
                    0.016f,
                    0d,
                    maxVisitedItems: Population / 10);
            }

            for (int i = 0; i < Population; i++)
            {
                Assert.That(
                    brains[i].DecisionCount,
                    Is.EqualTo(1),
                    $"NPC {i} was starved by the item budget.");
            }

            for (int i = 0; i < WarmupTicks; i++)
            {
                scheduler.Tick(0.016f, 0d);
            }

            double budgetMilliseconds = ReadPositiveEnvironmentDouble(
                "RUTIN_NPC_STRESS_BUDGET_MS",
                250d);
            long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
            long startTimestamp = Stopwatch.GetTimestamp();
            for (int i = 0; i < MeasuredTicks; i++)
            {
                scheduler.Tick(0.016f, 0d);
            }

            double elapsedMilliseconds =
                (Stopwatch.GetTimestamp() - startTimestamp) *
                1000d / Stopwatch.Frequency;
            long allocatedBytes =
                GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;

            Debug.Log(
                $"NPC_STRESS population={Population}, ticks={MeasuredTicks}, " +
                $"elapsedMs={elapsedMilliseconds:F3}, allocatedBytes={allocatedBytes}");
            Assert.That(allocatedBytes, Is.Zero);
            Assert.That(
                elapsedMilliseconds,
                Is.LessThanOrEqualTo(budgetMilliseconds));
        }

        private GameObject CreateNpc(
            BudgetedTickScheduler scheduler,
            string name,
            out NpcBrainFeature brain,
            out PlayerCommandFeature commands,
            out ProbeCommandConsumer consumer,
            bool activate = true,
            bool configureImmediateCadence = true)
        {
            GameObject npc = CreateObject(name);
            npc.SetActive(false);
            npc.AddComponent<GameplayEntity>();
            commands = npc.AddComponent<PlayerCommandFeature>();
            brain = npc.AddComponent<NpcBrainFeature>();
            consumer = npc.AddComponent<ProbeCommandConsumer>();
            commands.SetTickScheduler(scheduler);
            commands.RegisterConsumer(consumer);
            if (configureImmediateCadence)
            {
                brain.ConfigureDecisionCadence(0f, 0f);
            }

            if (activate)
            {
                npc.SetActive(true);
            }

            return npc;
        }

        private GameObject CreateObject(string name)
        {
            GameObject instance = new(name);
            _createdObjects.Add(instance);
            return instance;
        }

        private static double ReadPositiveEnvironmentDouble(
            string name,
            double fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return double.TryParse(value, out double parsed) && parsed > 0d
                ? parsed
                : fallback;
        }
    }
}
