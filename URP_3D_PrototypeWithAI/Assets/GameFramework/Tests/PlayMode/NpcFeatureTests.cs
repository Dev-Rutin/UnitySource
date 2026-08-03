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

            public Action DecideAction { get; set; }

            public int CallCount { get; private set; }

            public int ResetCount { get; private set; }

            public bool TryDecide(
                in NpcBlackboard blackboard,
                float deltaTime,
                out NpcDecision decision)
            {
                CallCount++;
                DecideAction?.Invoke();
                decision = Decision;
                return HandlesDecision;
            }

            public void ResetNpcDecisionState()
            {
                ResetCount++;
            }
        }

        private sealed class ProbeSensor : MonoBehaviour, INpcSensor
        {
            public int SensorOrder { get; set; }

            public Action SenseAction { get; set; }

            public int SenseCount { get; private set; }

            public int ResetCount { get; private set; }

            public void Sense(
                ref NpcBlackboard blackboard,
                float deltaTime)
            {
                SenseCount++;
                SenseAction?.Invoke();
            }

            public void ResetNpcSensorState()
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
            ProbeDecisionProvider rejecting =
                npc.AddComponent<ProbeDecisionProvider>();
            rejecting.DecisionOrder = -200;
            rejecting.HandlesDecision = false;
            rejecting.Decision = new NpcDecision(
                NpcBehaviourState.Chase,
                Vector3.right);
            ProbeDecisionProvider overrideProvider =
                npc.AddComponent<ProbeDecisionProvider>();
            overrideProvider.DecisionOrder = -100;
            overrideProvider.Decision = new NpcDecision(
                NpcBehaviourState.Chase,
                Vector3.left);
            Assert.That(brain.RegisterDecisionProvider(rejecting), Is.True);
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
            int resetCountAfterDisable = fallback.ResetCount;
            for (int i = 0; i < 10; i++)
            {
                scheduler.Tick(0.016f, 0d);
            }

            Assert.That(fallback.ResetCount, Is.EqualTo(resetCountAfterDisable));
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));

            brain.SetDecisionEnabled(true);
            UnityEngine.Object.DestroyImmediate(fallback);
            scheduler.Tick(0.016f, 0d);
            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Idle));
        }

        [Test]
        public void Brain_ReentrantSensorResetCancelsTheEvaluation()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateNpc(
                scheduler,
                "Reentrant Sensor NPC",
                out NpcBrainFeature brain,
                out _,
                out ProbeCommandConsumer consumer);
            ProbeSensor resetSensor = npc.AddComponent<ProbeSensor>();
            resetSensor.SensorOrder = -100;
            ProbeSensor laterSensor = npc.AddComponent<ProbeSensor>();
            laterSensor.SensorOrder = 100;
            resetSensor.SenseAction = () => brain.SetDecisionEnabled(false);
            Assert.That(brain.RegisterSensor(resetSensor), Is.True);
            Assert.That(brain.RegisterSensor(laterSensor), Is.True);

            scheduler.Tick(0.016f, 0d);

            Assert.That(resetSensor.SenseCount, Is.EqualTo(1));
            Assert.That(laterSensor.SenseCount, Is.Zero);
            Assert.That(brain.DecisionCount, Is.Zero);
            Assert.That(brain.CurrentDecision.State, Is.EqualTo(NpcBehaviourState.Idle));
            Assert.That(consumer.LastCommand, Is.EqualTo(PlayerCommand.Neutral));
        }

        [Test]
        public void Brain_ReentrantProviderResetCancelsCommitAndLaterProviders()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateNpc(
                scheduler,
                "Reentrant Provider NPC",
                out NpcBrainFeature brain,
                out _,
                out ProbeCommandConsumer consumer);
            ProbeDecisionProvider resetProvider =
                npc.AddComponent<ProbeDecisionProvider>();
            resetProvider.DecisionOrder = -100;
            resetProvider.Decision = new NpcDecision(
                NpcBehaviourState.Chase,
                Vector3.right);
            resetProvider.DecideAction = () => brain.SetDecisionEnabled(false);
            ProbeDecisionProvider laterProvider =
                npc.AddComponent<ProbeDecisionProvider>();
            laterProvider.DecisionOrder = 100;
            laterProvider.Decision = new NpcDecision(
                NpcBehaviourState.Patrol,
                Vector3.forward);
            Assert.That(brain.RegisterDecisionProvider(resetProvider), Is.True);
            Assert.That(brain.RegisterDecisionProvider(laterProvider), Is.True);

            scheduler.Tick(0.016f, 0d);

            Assert.That(resetProvider.CallCount, Is.EqualTo(1));
            Assert.That(laterProvider.CallCount, Is.Zero);
            Assert.That(brain.DecisionCount, Is.Zero);
            Assert.That(brain.CurrentDecision.State, Is.EqualTo(NpcBehaviourState.Idle));
            Assert.That(consumer.LastCommand, Is.EqualTo(PlayerCommand.Neutral));
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

            CreateNpc(
                scheduler,
                "Second Cadence NPC",
                out NpcBrainFeature secondBrain,
                out _,
                out _,
                activate: true,
                configureImmediateCadence: false);
            brain.ConfigureDecisionCadence(1f);
            secondBrain.ConfigureDecisionCadence(1f);
            brain.SetStaggerSeed(42u);
            secondBrain.SetStaggerSeed(42u);

            Assert.That(brain.HasExplicitStaggerSeed, Is.True);
            Assert.That(
                secondBrain.TimeUntilNextDecisionSeconds,
                Is.EqualTo(brain.TimeUntilNextDecisionSeconds));

            secondBrain.ClearStaggerSeed();
            Assert.That(secondBrain.HasExplicitStaggerSeed, Is.False);
        }

        [Test]
        public void Brain_UsesMotorMovementSpaceAndTurnsLookTowardIntent()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateObject("Oriented NPC");
            npc.SetActive(false);
            npc.AddComponent<CharacterController>();
            npc.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands =
                npc.AddComponent<PlayerCommandFeature>();
            NpcBrainFeature brain = npc.AddComponent<NpcBrainFeature>();
            PlayerCharacterMotorFeature motor =
                npc.AddComponent<PlayerCharacterMotorFeature>();
            PlayerLookFeature look = npc.AddComponent<PlayerLookFeature>();
            TransformTargetSensorFeature sensor =
                npc.AddComponent<TransformTargetSensorFeature>();
            npc.AddComponent<IdlePatrolChaseDecisionFeature>();
            ProbeCommandConsumer consumer =
                npc.AddComponent<ProbeCommandConsumer>();
            GameObject yawObject = CreateObject("NPC Yaw Root");
            yawObject.transform.SetParent(npc.transform, false);
            yawObject.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            look.SetViewTransforms(yawObject.transform, null);
            GameObject target = CreateObject("Oriented NPC Target");
            target.transform.position = Vector3.forward * 10f;
            sensor.SetTarget(target.transform);
            commands.SetTickScheduler(scheduler);
            commands.RegisterConsumer(consumer);
            brain.ConfigureDecisionCadence(0f, 0f);
            npc.SetActive(true);

            Assert.That(brain.MovementSpace, Is.SameAs(motor.MovementSpace));
            Assert.That(brain.MovementSpace, Is.SameAs(yawObject.transform));

            scheduler.Tick(0.016f, 0d);

            Assert.That(consumer.LastCommand.Move.y, Is.GreaterThan(0.99f));
            Assert.That(
                Mathf.Abs(consumer.LastCommand.Look.x),
                Is.EqualTo(90f).Within(0.01f));
            Assert.That(
                Vector3.Dot(yawObject.transform.forward, Vector3.forward),
                Is.GreaterThan(0.99f));
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
