using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rutin.GameFramework.Core;
using Rutin.GameFramework.InputSystem;
using Rutin.GameFramework.Management;
using Rutin.GameFramework.Player;
using Rutin.GameFramework.Ticking;
using UnityEngine;

namespace Rutin.GameFramework.Tests.PlayMode
{
    public sealed class PlayerFeatureTests
    {
        private sealed class ProbeCommandSource :
            MonoBehaviour,
            IPlayerCommandSource
        {
            public bool IsInputAvailable { get; set; } = true;

            public PlayerCommand Command { get; set; }

            public PlayerCommand ReadCommand(float deltaTime)
            {
                return Command;
            }
        }

        private sealed class ProbeCommandConsumer :
            MonoBehaviour,
            IPlayerCommandConsumer
        {
            public int CommandOrder { get; set; }

            public int Marker { get; set; }

            public List<int> OrderLog { get; set; }

            public int CallCount { get; private set; }

            public int ResetCount { get; private set; }

            public PlayerCommand LastCommand { get; private set; }

            public float LastDeltaTime { get; private set; }

            public Action ProcessAction { get; set; }

            public void ProcessPlayerCommand(
                PlayerCommand command,
                float deltaTime)
            {
                CallCount++;
                LastCommand = command;
                LastDeltaTime = deltaTime;
                OrderLog?.Add(Marker);
                ProcessAction?.Invoke();
            }

            public void ResetPlayerCommandState()
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
        public void CommandFeature_ClampsInputAndDispatchesEdges()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out ProbeCommandSource source,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            source.Command = new PlayerCommand(
                new Vector2(2f, 0f),
                new Vector2(12f, -4f),
                true,
                7);

            scheduler.Tick(0.016f, 0d);

            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.right));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(new Vector2(12f, -4f)));
            Assert.That(consumer.LastCommand.JumpPressed, Is.True);
            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(7));
            Assert.That(commands.CurrentCommand.Look, Is.EqualTo(Vector2.zero));
            Assert.That(commands.CurrentCommand.JumpPressed, Is.False);
        }

        [Test]
        public void CommandFeature_DispatchesConsumersInDeclaredOrder()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Ordered Command Player");
            player.AddComponent<GameplayEntity>();
            ProbeCommandSource source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            ProbeCommandConsumer later = player.AddComponent<ProbeCommandConsumer>();
            ProbeCommandConsumer earlier = player.AddComponent<ProbeCommandConsumer>();
            List<int> order = new();
            later.CommandOrder = 100;
            later.Marker = 100;
            later.OrderLog = order;
            earlier.CommandOrder = -100;
            earlier.Marker = -100;
            earlier.OrderLog = order;
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            commands.RegisterConsumer(later);
            commands.RegisterConsumer(earlier);
            player.SetActive(true);

            scheduler.Tick(0.016f, 0d);

            Assert.That(order, Is.EqualTo(new[] { -100, 100 }));
        }

        [Test]
        public void CommandFeature_RemoteCommandsLatchEdgesAndRejectOldSequences()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            commands.SetLocallyControlled(false);

            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.left,
                        new Vector2(2f, 3f),
                        true,
                        10)),
                Is.True);
            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.right,
                        new Vector2(5f, -1f),
                        false,
                        11)),
                Is.True);
            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(Vector2.down, Vector2.one, true, 11)),
                Is.False);
            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(Vector2.down, Vector2.one, true, 9)),
                Is.False);

            scheduler.Tick(0.016f, 0d);

            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.right));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(new Vector2(7f, 2f)));
            Assert.That(consumer.LastCommand.JumpPressed, Is.True);
            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(11));
        }

        [Test]
        public void CommandFeature_RejectsRemoteCommandsWhileInactive()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            commands.SetLocallyControlled(false);
            player.SetActive(false);

            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.up,
                        new Vector2(45f, 10f),
                        true,
                        1)),
                Is.False);

            player.SetActive(true);
            scheduler.Tick(0.016f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.JumpPressed, Is.False);

            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(Vector2.right, Vector2.zero, false, 1)),
                Is.True);
        }

        [Test]
        public void CommandFeature_RejectsRemoteCommandsWhileLocallyControlled()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);

            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.up,
                        new Vector2(45f, 10f),
                        true,
                        1)),
                Is.False);

            scheduler.Tick(0.016f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.JumpPressed, Is.False);
        }

        [Test]
        public void CommandFeature_RemoteTimeoutContinuesNeutralDispatch()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            commands.SetLocallyControlled(false);
            commands.SubmitCommand(
                new PlayerCommand(Vector2.up, Vector2.zero, false, 1));

            scheduler.Tick(0.1f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.up));

            scheduler.Tick(0.2f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.CallCount, Is.EqualTo(2));

            scheduler.Tick(0.2f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.CallCount, Is.EqualTo(3));
        }

        [Test]
        public void CommandFeature_ReentrantRegistrationDoesNotSkipOrDuplicateConsumers()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Reentrant Command Player");
            player.AddComponent<GameplayEntity>();
            ProbeCommandSource source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            ProbeCommandConsumer first = player.AddComponent<ProbeCommandConsumer>();
            ProbeCommandConsumer second = player.AddComponent<ProbeCommandConsumer>();
            ProbeCommandConsumer newcomer = player.AddComponent<ProbeCommandConsumer>();
            first.CommandOrder = 0;
            newcomer.CommandOrder = 5;
            second.CommandOrder = 10;
            first.ProcessAction = () =>
            {
                commands.UnregisterConsumer(first);
                commands.RegisterConsumer(newcomer);
            };
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            commands.RegisterConsumer(first);
            commands.RegisterConsumer(second);
            player.SetActive(true);

            scheduler.Tick(0.016f, 0d);

            Assert.That(first.CallCount, Is.EqualTo(1));
            Assert.That(second.CallCount, Is.EqualTo(1));
            Assert.That(newcomer.CallCount, Is.Zero);

            scheduler.Tick(0.016f, 0d);

            Assert.That(first.CallCount, Is.EqualTo(1));
            Assert.That(second.CallCount, Is.EqualTo(2));
            Assert.That(newcomer.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void CommandFeature_ReentrantResetAbortsRemainingDispatch()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Resetting Command Player");
            player.AddComponent<GameplayEntity>();
            ProbeCommandSource source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            ProbeCommandConsumer first = player.AddComponent<ProbeCommandConsumer>();
            ProbeCommandConsumer second = player.AddComponent<ProbeCommandConsumer>();
            first.CommandOrder = 0;
            second.CommandOrder = 10;
            first.ProcessAction = () => commands.SetSimulationEnabled(false);
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            commands.RegisterConsumer(first);
            commands.RegisterConsumer(second);
            player.SetActive(true);
            int firstResetCount = first.ResetCount;
            int secondResetCount = second.ResetCount;

            scheduler.Tick(0.016f, 0d);

            Assert.That(first.CallCount, Is.EqualTo(1));
            Assert.That(second.CallCount, Is.Zero);
            Assert.That(first.ResetCount, Is.EqualTo(firstResetCount + 1));
            Assert.That(second.ResetCount, Is.EqualTo(secondResetCount + 1));
        }

        [Test]
        public void CommandFeature_OwnershipSwitchClearsLocalAndAcceptsRemoteCommand()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out ProbeCommandSource source,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            source.Command = new PlayerCommand(Vector2.up, Vector2.zero, false);
            scheduler.Tick(0.016f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.up));

            commands.SetLocallyControlled(false);
            source.Command = new PlayerCommand(Vector2.left, Vector2.zero, false);
            scheduler.Tick(0.016f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));

            commands.SubmitCommand(
                new PlayerCommand(Vector2.right, Vector2.zero, false, 19));
            scheduler.Tick(0.016f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.right));
            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(19));
        }

        [Test]
        public void CommandFeature_UnavailableSourceClearsHeldInput()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out ProbeCommandSource source,
                out _,
                out ProbeCommandConsumer consumer);
            source.Command = new PlayerCommand(Vector2.up, Vector2.zero, false);
            scheduler.Tick(0.016f, 0d);
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.up));

            source.IsInputAvailable = false;
            scheduler.Tick(0.016f, 0d);

            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ScheduledFeature_ReRegistersAfterSchedulerClear()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out _);
            Assert.That(scheduler.Count, Is.EqualTo(1));

            scheduler.Clear();
            Assert.That(scheduler.Count, Is.Zero);

            commands.SetTickScheduler(scheduler);
            Assert.That(scheduler.Count, Is.EqualTo(1));
        }

        [Test]
        public void ScheduledFeature_ReRegistersWhenDefaultSchedulerIsReplaced()
        {
            GameObject hostObject = CreateObject("Scheduler Host");
            TickSchedulerService original =
                hostObject.AddComponent<TickSchedulerService>();
            GameObject player = CreateInactiveObject("Default Scheduled Player");
            player.AddComponent<GameplayEntity>();
            ProbeCommandSource source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            commands.SetCommandSource(source);
            player.SetActive(true);
            Assert.That(original.Count, Is.EqualTo(1));

            int missingSchedulerWarnings = 0;
            void CountMissingSchedulerWarning(
                string condition,
                string stackTrace,
                LogType type)
            {
                if (type == LogType.Warning &&
                    condition.Contains("could not resolve ITickScheduler"))
                {
                    missingSchedulerWarnings++;
                }
            }

            Application.logMessageReceived += CountMissingSchedulerWarning;
            TickSchedulerService replacement;
            try
            {
                UnityEngine.Object.DestroyImmediate(original);
                replacement = hostObject.AddComponent<TickSchedulerService>();
            }
            finally
            {
                Application.logMessageReceived -= CountMissingSchedulerWarning;
            }

            Assert.That(replacement.Count, Is.EqualTo(1));
            Assert.That(missingSchedulerWarnings, Is.Zero);
        }

        [Test]
        public void ScheduledFeature_ExplicitSchedulerNeverFallsBackToDefault()
        {
            GameObject hostObject = CreateObject("Default Scheduler Host");
            TickSchedulerService defaultScheduler =
                hostObject.AddComponent<TickSchedulerService>();
            BudgetedTickScheduler explicitScheduler = new();
            CreateCommandPlayer(
                explicitScheduler,
                out _,
                out PlayerCommandFeature commands,
                out _);
            Assert.That(explicitScheduler.Count, Is.EqualTo(1));
            Assert.That(defaultScheduler.Count, Is.Zero);

            explicitScheduler.Clear();

            Assert.That(explicitScheduler.Count, Is.Zero);
            Assert.That(defaultScheduler.Count, Is.Zero);

            commands.SetTickScheduler(explicitScheduler);
            Assert.That(explicitScheduler.Count, Is.EqualTo(1));
            Assert.That(defaultScheduler.Count, Is.Zero);

            commands.SetTickScheduler(null);
            Assert.That(explicitScheduler.Count, Is.Zero);
            Assert.That(defaultScheduler.Count, Is.Zero);

            commands.UseDefaultTickScheduler();
            Assert.That(defaultScheduler.Count, Is.EqualTo(1));
        }

        [Test]
        public void ScheduledFeature_InactiveDuringDefaultReplacementUsesNewScheduler()
        {
            GameObject hostObject = CreateObject("Replacement Scheduler Host");
            TickSchedulerService original =
                hostObject.AddComponent<TickSchedulerService>();
            GameObject player = CreateInactiveObject("Inactive Scheduled Player");
            player.AddComponent<GameplayEntity>();
            ProbeCommandSource source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            commands.SetCommandSource(source);
            player.SetActive(true);
            Assert.That(original.Count, Is.EqualTo(1));

            player.SetActive(false);
            Assert.That(original.Count, Is.Zero);
            UnityEngine.Object.DestroyImmediate(original);
            TickSchedulerService replacement =
                hostObject.AddComponent<TickSchedulerService>();
            Assert.That(replacement.Count, Is.Zero);

            player.SetActive(true);

            Assert.That(replacement.Count, Is.EqualTo(1));
        }

        [Test]
        public void BudgetedScheduler_ProcessesEachPlayerStackAtomically()
        {
            BudgetedTickScheduler scheduler = new();
            CreateMotorPlayer(
                scheduler,
                "First Player",
                out ProbeCommandSource firstSource,
                out Transform firstTransform);
            CreateMotorPlayer(
                scheduler,
                "Second Player",
                out ProbeCommandSource secondSource,
                out Transform secondTransform);
            firstSource.Command = new PlayerCommand(Vector2.up, Vector2.zero, false);
            secondSource.Command = new PlayerCommand(Vector2.up, Vector2.zero, false);

            scheduler.Tick(0.1f, 0d, 1);
            Assert.That(firstTransform.position.z, Is.GreaterThan(0f));
            Assert.That(secondTransform.position.z, Is.EqualTo(0f).Within(0.0001f));

            scheduler.Tick(0.1f, 0d, 1);
            Assert.That(secondTransform.position.z, Is.GreaterThan(0f));
        }

        [Test]
        public void Motor_UsesFixedStepsAndTracksEntityActivation()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateMotorPlayer(
                scheduler,
                "Motor Player",
                out ProbeCommandSource source,
                out Transform playerTransform);
            source.Command = new PlayerCommand(Vector2.up, Vector2.zero, false);

            Assert.That(scheduler.Count, Is.EqualTo(1));
            scheduler.Tick(0.1f, 0d);

            Assert.That(playerTransform.position.z, Is.GreaterThan(0f));
            player.SetActive(false);
            Assert.That(scheduler.Count, Is.Zero);
            player.SetActive(true);
            Assert.That(scheduler.Count, Is.EqualTo(1));
        }

        [Test]
        public void CommandFeature_UsesCommandSimulationDeltaForReplay()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out ProbeCommandSource source,
                out _,
                out ProbeCommandConsumer consumer);
            source.Command = new PlayerCommand(
                Vector2.up,
                Vector2.zero,
                false,
                sequence: 42,
                simulationDeltaTimeSeconds: 0.05f);

            scheduler.Tick(0.2f, 0d);

            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(42));
            Assert.That(
                consumer.LastCommand.SimulationDeltaTimeSeconds,
                Is.EqualTo(0.05f));
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.05f));
        }

        [Test]
        public void Motor_ReportsInternallyDiscardedSimulationTime()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateMotorPlayer(
                scheduler,
                "Clamped Motor Player",
                out _,
                out _);
            PlayerCharacterMotorFeature motor =
                player.GetComponent<PlayerCharacterMotorFeature>();

            motor.ProcessPlayerCommand(PlayerCommand.Neutral, 1f);

            Assert.That(
                motor.TotalDiscardedSimulationTimeSeconds,
                Is.GreaterThan(0.7d));
        }

        [Test]
        public void Motor_FixedStepProducesSameResultAcrossTickPartitions()
        {
            BudgetedTickScheduler singleBatchScheduler = new();
            CreateMotorPlayer(
                singleBatchScheduler,
                "Single Batch Player",
                out ProbeCommandSource singleBatchSource,
                out Transform singleBatchTransform);
            singleBatchSource.Command =
                new PlayerCommand(Vector2.up, Vector2.zero, false);

            BudgetedTickScheduler splitBatchScheduler = new();
            GameObject splitPlayer = CreateMotorPlayer(
                splitBatchScheduler,
                "Split Batch Player",
                out ProbeCommandSource splitBatchSource,
                out Transform splitBatchTransform);
            splitPlayer.transform.position = Vector3.right * 10f;
            splitBatchSource.Command =
                new PlayerCommand(Vector2.up, Vector2.zero, false);

            singleBatchScheduler.Tick(0.1f, 0d);
            splitBatchScheduler.Tick(0.05f, 0d);
            splitBatchScheduler.Tick(0.05f, 0d);

            Assert.That(
                splitBatchTransform.position.z,
                Is.EqualTo(singleBatchTransform.position.z).Within(0.0001f));
            Assert.That(
                splitBatchTransform.position.y,
                Is.EqualTo(singleBatchTransform.position.y).Within(0.0001f));
        }

        [Test]
        public void RemoteMotorWithoutPackets_ContinuesNeutralGravitySimulation()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateMotorPlayer(
                scheduler,
                "Remote Falling Player",
                out _,
                out Transform playerTransform);
            playerTransform.position = Vector3.up * 2f;
            player.GetComponent<PlayerCommandFeature>().SetLocallyControlled(false);

            scheduler.Tick(0.1f, 0d);

            Assert.That(playerTransform.position.y, Is.LessThan(2f));
        }

        [Test]
        public void Motor_BuffersJumpUntilGroundedWithinFixedStepBatch()
        {
            GameObject floor = CreateObject("Jump Buffer Floor");
            floor.transform.position = Vector3.down * 0.5f;
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.AddComponent<BoxCollider>();

            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateMotorPlayer(
                scheduler,
                "Buffered Jump Player",
                out ProbeCommandSource source,
                out Transform playerTransform);
            playerTransform.position = Vector3.up * 1.01f;
            source.Command =
                new PlayerCommand(Vector2.zero, Vector2.zero, true);

            scheduler.Tick(0.1f, 0d);

            Assert.That(playerTransform.position.y, Is.GreaterThan(1.05f));
        }

        [Test]
        public void LookFeature_PreservesRigBaseRotationsAndSharesMovementReference()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Look Player");
            player.AddComponent<CharacterController>();
            player.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            PlayerCharacterMotorFeature motor =
                player.AddComponent<PlayerCharacterMotorFeature>();
            PlayerLookFeature lookFeature = player.AddComponent<PlayerLookFeature>();
            GameObject yawObject = CreateObject("Yaw Root");
            yawObject.transform.SetParent(player.transform, false);
            GameObject pitchObject = CreateObject("Pitch Pivot");
            pitchObject.transform.SetParent(yawObject.transform, false);
            Quaternion baseYaw = Quaternion.Euler(10f, 20f, 5f);
            Quaternion basePitch = Quaternion.Euler(3f, 0f, 7f);
            yawObject.transform.localRotation = baseYaw;
            pitchObject.transform.localRotation = basePitch;

            commands.SetTickScheduler(scheduler);
            commands.SetLocallyControlled(false);
            lookFeature.SetViewTransforms(yawObject.transform, pitchObject.transform);
            player.SetActive(true);
            commands.SubmitCommand(
                new PlayerCommand(Vector2.zero, new Vector2(30f, 10f), false));

            scheduler.Tick(0.016f, 0d);

            Quaternion expectedYaw =
                baseYaw * Quaternion.AngleAxis(30f, Vector3.up);
            Quaternion expectedPitch =
                basePitch * Quaternion.AngleAxis(-10f, Vector3.right);
            Assert.That(
                Quaternion.Angle(yawObject.transform.localRotation, expectedYaw),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(pitchObject.transform.localRotation, expectedPitch),
                Is.LessThan(0.001f));
            Assert.That(motor.MovementSpace, Is.SameAs(yawObject.transform));

            GameObject replacementYaw = CreateObject("Replacement Yaw Root");
            replacementYaw.transform.SetParent(player.transform, false);
            Quaternion replacementBase = Quaternion.Euler(4f, 15f, 2f);
            replacementYaw.transform.localRotation = replacementBase;
            lookFeature.SetViewTransforms(replacementYaw.transform, null);

            Assert.That(motor.MovementSpace, Is.SameAs(replacementYaw.transform));
            Assert.That(lookFeature.Yaw, Is.EqualTo(30f).Within(0.001f));
            Assert.That(
                Quaternion.Angle(
                    replacementYaw.transform.localRotation,
                    replacementBase * Quaternion.AngleAxis(30f, Vector3.up)),
                Is.LessThan(0.001f));

            lookFeature.SetViewTransforms(
                yawObject.transform,
                pitchObject.transform);
            Assert.That(motor.MovementSpace, Is.SameAs(yawObject.transform));
            Assert.That(
                Quaternion.Angle(
                    yawObject.transform.localRotation,
                    expectedYaw),
                Is.LessThan(0.001f));

            GameObject explicitMovementSpace =
                CreateObject("Explicit Movement Space");
            motor.SetMovementSpace(explicitMovementSpace.transform);
            GameObject anotherYaw = CreateObject("Another Yaw Root");
            anotherYaw.transform.SetParent(player.transform, false);
            lookFeature.SetViewTransforms(anotherYaw.transform, null);

            Assert.That(
                motor.MovementSpace,
                Is.SameAs(explicitMovementSpace.transform));
        }

        [Test]
        public void LookFeature_AppliesCurrentYawBeforeMotorSimulation()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Look Driven Motor Player");
            player.AddComponent<CharacterController>();
            player.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            player.AddComponent<PlayerCharacterMotorFeature>();
            PlayerLookFeature look = player.AddComponent<PlayerLookFeature>();
            GameObject yawObject = CreateObject("Movement Yaw Root");
            yawObject.transform.SetParent(player.transform, false);
            look.SetViewTransforms(yawObject.transform, null);
            commands.SetTickScheduler(scheduler);
            commands.SetLocallyControlled(false);
            player.SetActive(true);
            commands.SubmitCommand(
                new PlayerCommand(Vector2.up, new Vector2(90f, 0f), false));

            scheduler.Tick(0.1f, 0d);

            Assert.That(player.transform.position.x, Is.GreaterThan(0.01f));
            Assert.That(
                Mathf.Abs(player.transform.position.z),
                Is.LessThan(0.001f));
        }

        [Test]
        public void InputSystemSource_UpdatesBeforeTickScheduler()
        {
            DefaultExecutionOrder sourceOrder =
                (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                    typeof(InputSystemPlayerCommandSource),
                    typeof(DefaultExecutionOrder));
            DefaultExecutionOrder schedulerOrder =
                (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                    typeof(TickSchedulerService),
                    typeof(DefaultExecutionOrder));

            Assert.That(sourceOrder, Is.Not.Null);
            Assert.That(schedulerOrder, Is.Not.Null);
            Assert.That(sourceOrder.order, Is.LessThan(schedulerOrder.order));
        }

        [Test]
        public void InputSystemSource_LatchesFrameEdgesUntilSchedulerRead()
        {
            GameObject inputObject = CreateObject("Input Source");
            InputSystemPlayerCommandSource source =
                inputObject.AddComponent<InputSystemPlayerCommandSource>();
            source.BufferInputSample(
                Vector2.left,
                new Vector2(2f, 3f),
                true,
                0.016f);
            source.BufferInputSample(
                Vector2.right,
                new Vector2(5f, -1f),
                false,
                0.016f);

            PlayerCommand first = source.ReadCommand(0.032f);
            PlayerCommand second = source.ReadCommand(0.016f);

            Assert.That(first.Move, Is.EqualTo(Vector2.right));
            Assert.That(first.Look, Is.EqualTo(new Vector2(7f, 2f)));
            Assert.That(first.JumpPressed, Is.True);
            Assert.That(second.Move, Is.EqualTo(Vector2.right));
            Assert.That(second.Look, Is.EqualTo(Vector2.zero));
            Assert.That(second.JumpPressed, Is.False);
        }

        [Test]
        public void InputSystemSource_DiscardsLatchedInputAcrossOwnershipChange()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Buffered Input Player");
            player.AddComponent<GameplayEntity>();
            InputSystemPlayerCommandSource source =
                player.AddComponent<InputSystemPlayerCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            ProbeCommandConsumer consumer = player.AddComponent<ProbeCommandConsumer>();
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            commands.RegisterConsumer(consumer);
            player.SetActive(true);
            source.BufferInputSample(
                Vector2.up,
                new Vector2(45f, 10f),
                true,
                0.016f);

            commands.SetLocallyControlled(false);
            commands.SetLocallyControlled(true);
            scheduler.Tick(0.016f, 0d);

            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.JumpPressed, Is.False);
        }

        [Test]
        public void InputSystemSource_DiscardsInputBufferedWhileCommandFeatureDisabled()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Reactivated Input Player");
            player.AddComponent<GameplayEntity>();
            InputSystemPlayerCommandSource source =
                player.AddComponent<InputSystemPlayerCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            ProbeCommandConsumer consumer = player.AddComponent<ProbeCommandConsumer>();
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            commands.RegisterConsumer(consumer);
            player.SetActive(true);

            commands.enabled = false;
            source.BufferInputSample(
                Vector2.up,
                new Vector2(45f, 10f),
                true,
                0.016f);
            commands.enabled = true;
            scheduler.Tick(0.016f, 0d);

            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.JumpPressed, Is.False);
        }

        [Test]
        public void CommandAndConsumers_DoesNotAllocateManagedMemory()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Allocation Player");
            player.AddComponent<GameplayEntity>();
            ProbeCommandSource source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            player.AddComponent<PlayerLookFeature>();
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            source.Command = new PlayerCommand(
                Vector2.up,
                new Vector2(0.1f, 0.1f),
                false);
            player.SetActive(true);

            for (int i = 0; i < 32; i++)
            {
                scheduler.Tick(0.016f, 0d);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                scheduler.Tick(0.016f, 0d);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        private GameObject CreateCommandPlayer(
            BudgetedTickScheduler scheduler,
            out ProbeCommandSource source,
            out PlayerCommandFeature commands,
            out ProbeCommandConsumer consumer)
        {
            GameObject player = CreateInactiveObject("Command Player");
            player.AddComponent<GameplayEntity>();
            source = player.AddComponent<ProbeCommandSource>();
            commands = player.AddComponent<PlayerCommandFeature>();
            consumer = player.AddComponent<ProbeCommandConsumer>();
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            commands.RegisterConsumer(consumer);
            player.SetActive(true);
            return player;
        }

        private GameObject CreateMotorPlayer(
            BudgetedTickScheduler scheduler,
            string name,
            out ProbeCommandSource source,
            out Transform playerTransform)
        {
            GameObject player = CreateInactiveObject(name);
            player.AddComponent<CharacterController>();
            player.AddComponent<GameplayEntity>();
            source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            player.AddComponent<PlayerCharacterMotorFeature>();
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            player.SetActive(true);
            playerTransform = player.transform;
            return player;
        }

        private GameObject CreateInactiveObject(string name)
        {
            GameObject instance = CreateObject(name);
            instance.SetActive(false);
            return instance;
        }

        private GameObject CreateObject(string name)
        {
            GameObject instance = new(name);
            _createdObjects.Add(instance);
            return instance;
        }
    }
}
