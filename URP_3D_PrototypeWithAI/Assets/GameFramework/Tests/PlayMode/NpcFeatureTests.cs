using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Rutin.GameFramework.Core;
using Rutin.GameFramework.Npc;
using Rutin.GameFramework.Player;
using Rutin.GameFramework.Ticking;
using UnityEngine;
using UnityEngine.TestTools;
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

            public bool SetsTarget { get; set; }

            public UnityEngine.Object Target { get; set; }

            public Vector3 TargetPosition { get; set; }

            public int SenseCount { get; private set; }

            public int ResetCount { get; private set; }

            public void Sense(
                ref NpcBlackboard blackboard,
                float deltaTime)
            {
                SenseCount++;
                if (SetsTarget)
                {
                    blackboard.SetTarget(Target, TargetPosition);
                }

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
        public void TargetSensor_ZeroDetectionRadiusDisablesAcquisition()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateNpc(
                scheduler,
                "Disabled Radius NPC",
                out NpcBrainFeature brain,
                out _,
                out _,
                activate: false);
            TransformTargetSensorFeature sensor =
                npc.AddComponent<TransformTargetSensorFeature>();
            npc.AddComponent<IdlePatrolChaseDecisionFeature>();
            GameObject target = CreateObject("Disabled Radius Target");
            target.transform.position = Vector3.right;
            sensor.SetTarget(target.transform);
            sensor.ConfigureRanges(0f, 0f);
            npc.SetActive(true);

            scheduler.Tick(0.016f, 0d);

            Assert.That(brain.Blackboard.HasTarget, Is.False);
            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Idle));

            sensor.ConfigureRanges(2f, 3f);
            scheduler.Tick(0.016f, 0.016d);

            Assert.That(brain.Blackboard.HasTarget, Is.True);
            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Chase));
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
        public void Brain_FailingSensorDoesNotClearEarlierSensorResult()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateNpc(
                scheduler,
                "Fault-Isolated Sensor NPC",
                out NpcBrainFeature brain,
                out _,
                out _,
                activate: false);
            GameObject target = CreateObject("Sensor Target");
            target.transform.position = Vector3.right * 4f;
            ProbeSensor targetSensor = npc.AddComponent<ProbeSensor>();
            targetSensor.SensorOrder = -100;
            targetSensor.SetsTarget = true;
            targetSensor.Target = target;
            targetSensor.TargetPosition = target.transform.position;
            ProbeSensor failingSensor = npc.AddComponent<ProbeSensor>();
            failingSensor.SensorOrder = 100;
            failingSensor.SenseAction = () =>
                throw new InvalidOperationException("Sensor failure");
            Assert.That(brain.RegisterSensor(targetSensor), Is.True);
            Assert.That(brain.RegisterSensor(failingSensor), Is.True);
            npc.SetActive(true);

            LogAssert.Expect(
                LogType.Exception,
                new System.Text.RegularExpressions.Regex(
                    "InvalidOperationException: Sensor failure"));
            scheduler.Tick(0.016f, 0d);

            Assert.That(targetSensor.SenseCount, Is.EqualTo(1));
            Assert.That(failingSensor.SenseCount, Is.EqualTo(1));
            Assert.That(brain.Blackboard.HasTarget, Is.True);
            Assert.That(brain.Blackboard.Target, Is.SameAs(target));

            scheduler.Tick(0.016f, 0.016d);

            Assert.That(targetSensor.SenseCount, Is.EqualTo(2));
            Assert.That(failingSensor.SenseCount, Is.EqualTo(1));
            Assert.That(brain.Blackboard.HasTarget, Is.True);
            Assert.That(brain.Blackboard.Target, Is.SameAs(target));
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
            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(2));

            BudgetedTickScheduler replacementScheduler = new();
            commands.SetTickScheduler(replacementScheduler);

            Assert.That(firstScheduler.Count, Is.Zero);
            Assert.That(replacementScheduler.Count, Is.EqualTo(1));
            Assert.That(brain.DecisionCount, Is.Zero);
            replacementScheduler.Tick(0.016f, 0d);
            Assert.That(brain.DecisionCount, Is.EqualTo(1));
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(3));
        }

        [Test]
        public void Brain_CommandSequenceRemainsMonotonicAcrossDecisionReset()
        {
            BudgetedTickScheduler scheduler = new();
            CreateNpc(
                scheduler,
                "Monotonic Sequence NPC",
                out NpcBrainFeature brain,
                out _,
                out ProbeCommandConsumer consumer);

            scheduler.Tick(0.016f, 0d);
            uint firstSequence = consumer.LastCommand.Sequence;
            brain.SetDecisionEnabled(false);
            brain.SetDecisionEnabled(true);
            scheduler.Tick(0.016f, 0.016d);
            uint secondSequence = consumer.LastCommand.Sequence;

            Assert.That(firstSequence, Is.EqualTo(1));
            Assert.That(secondSequence, Is.EqualTo(2));
            Assert.That(
                unchecked((int)(secondSequence - firstSequence)),
                Is.GreaterThan(0));
        }

        [Test]
        public void Brain_SanitizesDecisionVectorsAndCadenceConfiguration()
        {
            NpcDecision malformed = new(
                NpcBehaviourState.Chase,
                new Vector3(float.NaN, 20f, float.PositiveInfinity),
                true);

            Assert.That(malformed.WorldMove, Is.EqualTo(Vector3.zero));
            Assert.That(malformed.HasWorldFacing, Is.False);

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
        public void Brain_UsesAbsoluteWorldMovementAndOptionalFacingConsumer()
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
            NpcFacingFeature facing = npc.AddComponent<NpcFacingFeature>();
            TransformTargetSensorFeature sensor =
                npc.AddComponent<TransformTargetSensorFeature>();
            npc.AddComponent<IdlePatrolChaseDecisionFeature>();
            ProbeCommandConsumer consumer =
                npc.AddComponent<ProbeCommandConsumer>();
            GameObject yawObject = CreateObject("NPC Yaw Root");
            yawObject.transform.SetParent(npc.transform, false);
            facing.SetYawRoot(yawObject.transform);
            GameObject movementReference = CreateObject("NPC Movement Reference");
            movementReference.transform.SetParent(npc.transform, false);
            movementReference.transform.localRotation =
                Quaternion.Euler(0f, 90f, 0f);
            motor.SetMovementSpace(movementReference.transform);
            GameObject target = CreateObject("Oriented NPC Target");
            target.transform.position = Vector3.forward * 10f;
            sensor.SetTarget(target.transform);
            commands.SetTickScheduler(scheduler);
            commands.RegisterConsumer(consumer);
            brain.ConfigureDecisionCadence(0f, 0f);
            npc.SetActive(true);
            yawObject.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            scheduler.Tick(0.02f, 0d);

            Assert.That(
                consumer.LastCommand.MoveSpace,
                Is.EqualTo(PlayerCommandMoveSpace.World));
            Assert.That(consumer.LastCommand.Move.x, Is.Zero.Within(0.0001f));
            Assert.That(consumer.LastCommand.Move.y, Is.GreaterThan(0.99f));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(Vector2.zero));
            Assert.That(
                Vector3.Dot(yawObject.transform.forward, Vector3.forward),
                Is.GreaterThan(0.99f));
            Assert.That(Mathf.Abs(motor.Velocity.x), Is.LessThan(0.0001f));
            Assert.That(motor.Velocity.z, Is.GreaterThan(0f));
        }

        [Test]
        public void Brain_StationaryChasePublishesIndependentFacingIntent()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateObject("Stationary Chase NPC");
            npc.SetActive(false);
            npc.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands =
                npc.AddComponent<PlayerCommandFeature>();
            NpcBrainFeature brain = npc.AddComponent<NpcBrainFeature>();
            NpcFacingFeature facing = npc.AddComponent<NpcFacingFeature>();
            TransformTargetSensorFeature sensor =
                npc.AddComponent<TransformTargetSensorFeature>();
            npc.AddComponent<IdlePatrolChaseDecisionFeature>();
            ProbeCommandConsumer consumer =
                npc.AddComponent<ProbeCommandConsumer>();
            GameObject yawObject = CreateObject("Stationary Chase Yaw Root");
            yawObject.transform.SetParent(npc.transform, false);
            facing.SetYawRoot(yawObject.transform);
            GameObject target = CreateObject("Nearby Moving Target");
            target.transform.position = Vector3.right;
            sensor.SetTarget(target.transform);
            commands.SetTickScheduler(scheduler);
            commands.RegisterConsumer(consumer);
            brain.ConfigureDecisionCadence(0f, 0f);
            npc.SetActive(true);

            scheduler.Tick(0.016f, 0d);

            Assert.That(
                brain.CurrentDecision.State,
                Is.EqualTo(NpcBehaviourState.Chase));
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.HasWorldFacing, Is.True);
            Assert.That(consumer.LastCommand.WorldFacing, Is.EqualTo(Vector2.right));
            Assert.That(
                Vector3.Dot(yawObject.transform.forward, Vector3.right),
                Is.GreaterThan(0.99f));
        }

        [Test]
        public void Facing_NextAbsoluteSnapshotRepairsOrientationAfterSequenceGap()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateObject("Remote Facing NPC");
            npc.SetActive(false);
            npc.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands =
                npc.AddComponent<PlayerCommandFeature>();
            NpcFacingFeature facing = npc.AddComponent<NpcFacingFeature>();
            GameObject yawObject = CreateObject("Remote NPC Yaw Root");
            yawObject.transform.SetParent(npc.transform, false);
            facing.SetYawRoot(yawObject.transform);
            commands.SetTickScheduler(scheduler);
            commands.SetLocallyControlled(false);
            npc.SetActive(true);

            Assert.That(
                commands.SubmitCommand(
                    CreateWorldCommand(Vector2.right, sequence: 1)),
                Is.True);
            scheduler.Tick(0.016f, 0d);
            Assert.That(
                Vector3.Dot(yawObject.transform.forward, Vector3.right),
                Is.GreaterThan(0.99f));

            yawObject.transform.rotation = Quaternion.LookRotation(Vector3.back);
            Assert.That(
                commands.SubmitCommand(
                    CreateWorldCommand(Vector2.up, sequence: 3)),
                Is.True);
            scheduler.Tick(0.016f, 0.016d);

            Assert.That(
                Vector3.Dot(yawObject.transform.forward, Vector3.forward),
                Is.GreaterThan(0.99f));
        }

        [Test]
        public void Facing_PreservesAuthoredBaseAndRestoresItOnCommandReset()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateObject("Base Rotation NPC");
            npc.SetActive(false);
            npc.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands =
                npc.AddComponent<PlayerCommandFeature>();
            NpcFacingFeature facing = npc.AddComponent<NpcFacingFeature>();
            GameObject yawObject = CreateObject("Authored NPC Yaw Root");
            yawObject.transform.SetParent(npc.transform, false);
            Quaternion baseRotation = Quaternion.Euler(10f, 20f, 30f);
            yawObject.transform.localRotation = baseRotation;
            float baseVerticalForward =
                (baseRotation * Vector3.forward).y;
            facing.SetYawRoot(yawObject.transform);
            commands.SetTickScheduler(scheduler);
            commands.SetLocallyControlled(false);
            npc.SetActive(true);

            Assert.That(
                commands.SubmitCommand(
                    CreateWorldCommand(Vector2.right, sequence: 1)),
                Is.True);
            scheduler.Tick(0.016f, 0d);

            Vector3 facingForward = yawObject.transform.forward;
            Vector3 planarFacing = Vector3.ProjectOnPlane(
                facingForward,
                Vector3.up).normalized;
            Assert.That(
                Vector3.Dot(planarFacing, Vector3.right),
                Is.GreaterThan(0.99f));
            Assert.That(
                facingForward.y,
                Is.EqualTo(baseVerticalForward).Within(0.01f));

            commands.SetSimulationEnabled(false);

            Assert.That(
                Quaternion.Angle(
                    yawObject.transform.localRotation,
                    baseRotation),
                Is.LessThan(0.01f));
        }

        [Test]
        public void Facing_PreservesVerticalImportCorrectionWhenHeadingIsUndefined()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateObject("Vertically Corrected NPC");
            npc.SetActive(false);
            npc.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands =
                npc.AddComponent<PlayerCommandFeature>();
            NpcFacingFeature facing = npc.AddComponent<NpcFacingFeature>();
            GameObject yawObject = CreateObject("Vertical Import Correction Root");
            yawObject.transform.SetParent(npc.transform, false);
            Quaternion baseRotation = Quaternion.Euler(-90f, 0f, 0f);
            yawObject.transform.localRotation = baseRotation;
            facing.SetYawRoot(yawObject.transform);
            commands.SetTickScheduler(scheduler);
            commands.SetLocallyControlled(false);
            npc.SetActive(true);

            Assert.That(
                commands.SubmitCommand(
                    CreateWorldCommand(Vector2.right, sequence: 1)),
                Is.True);
            scheduler.Tick(0.016f, 0d);

            Quaternion expectedFacing =
                Quaternion.AngleAxis(90f, Vector3.up) * baseRotation;
            Assert.That(
                Quaternion.Angle(
                    yawObject.transform.localRotation,
                    expectedFacing),
                Is.LessThan(0.01f));

            commands.SetSimulationEnabled(false);

            Assert.That(
                Quaternion.Angle(
                    yawObject.transform.localRotation,
                    baseRotation),
                Is.LessThan(0.01f));
        }

        [Test]
        public void Facing_EntityRootPreservesSpawnRotationAndUsesAbsoluteYaw()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateObject("Entity Root Facing NPC");
            npc.SetActive(false);
            npc.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            npc.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands =
                npc.AddComponent<PlayerCommandFeature>();
            npc.AddComponent<NpcFacingFeature>();
            commands.SetTickScheduler(scheduler);
            commands.SetLocallyControlled(false);
            npc.SetActive(true);

            Assert.That(
                commands.SubmitCommand(
                    CreateWorldCommand(Vector2.up, sequence: 1)),
                Is.True);
            scheduler.Tick(0.016f, 0d);
            Assert.That(
                Vector3.Dot(npc.transform.forward, Vector3.forward),
                Is.GreaterThan(0.99f));

            npc.SetActive(false);
            Quaternion spawnRotation = Quaternion.Euler(0f, 135f, 0f);
            npc.transform.rotation = spawnRotation;
            npc.SetActive(true);

            Assert.That(
                Quaternion.Angle(npc.transform.rotation, spawnRotation),
                Is.LessThan(0.01f));
            Assert.That(
                commands.SubmitCommand(
                    CreateWorldCommand(Vector2.up, sequence: 2)),
                Is.True);
            scheduler.Tick(0.016f, 0.016d);
            Assert.That(
                Vector3.Dot(npc.transform.forward, Vector3.forward),
                Is.GreaterThan(0.99f));
        }

        [Test]
        public void Facing_DestroyedRigNeverRestoresItsBaseToEntityRoot()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject npc = CreateObject("Destroyed Rig NPC");
            npc.SetActive(false);
            npc.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands =
                npc.AddComponent<PlayerCommandFeature>();
            NpcFacingFeature facing = npc.AddComponent<NpcFacingFeature>();
            GameObject yawObject = CreateObject("Disposable NPC Yaw Root");
            yawObject.transform.SetParent(npc.transform, false);
            yawObject.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            facing.SetYawRoot(yawObject.transform);
            commands.SetTickScheduler(scheduler);
            commands.SetLocallyControlled(false);
            npc.SetActive(true);

            UnityEngine.Object.DestroyImmediate(yawObject);
            Quaternion gameplayRotation = Quaternion.Euler(0f, 135f, 0f);
            npc.transform.rotation = gameplayRotation;
            commands.SetSimulationEnabled(false);

            Assert.That(
                Quaternion.Angle(npc.transform.rotation, gameplayRotation),
                Is.LessThan(0.01f));
        }

        [Test]
        public void ThousandNpcBrainCores_AreFairAllocationFreeAndWithinBudget()
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

        private static PlayerCommand CreateWorldCommand(
            Vector2 move,
            uint sequence)
        {
            return new PlayerCommand(
                move,
                Vector2.zero,
                false,
                sequence,
                0f,
                false,
                PlayerCommandMoveSpace.World);
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
