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
        public void CommandFeature_PreservesWorldMoveSpaceThroughDispatch()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out ProbeCommandSource source,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            source.Command = new PlayerCommand(
                Vector2.up,
                Vector2.zero,
                false,
                11,
                0f,
                false,
                PlayerCommandMoveSpace.World);

            scheduler.Tick(0.016f, 0d);

            Assert.That(
                consumer.LastCommand.MoveSpace,
                Is.EqualTo(PlayerCommandMoveSpace.World));
            Assert.That(
                commands.CurrentCommand.MoveSpace,
                Is.EqualTo(PlayerCommandMoveSpace.World));
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
        public void Motor_WorldMoveIgnoresRotatedRelativeMovementSpace()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateMotorPlayer(
                scheduler,
                "World Move Motor Player",
                out ProbeCommandSource source,
                out _);
            PlayerCharacterMotorFeature motor =
                player.GetComponent<PlayerCharacterMotorFeature>();
            GameObject movementReference = CreateObject("Rotated Movement Reference");
            movementReference.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            motor.SetMovementSpace(movementReference.transform);
            source.Command = new PlayerCommand(
                Vector2.up,
                Vector2.zero,
                false,
                1,
                0f,
                false,
                PlayerCommandMoveSpace.World);

            scheduler.Tick(0.02f, 0d);

            Assert.That(Mathf.Abs(motor.Velocity.x), Is.LessThan(0.0001f));
            Assert.That(motor.Velocity.z, Is.GreaterThan(0f));
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
        public void CommandFeature_LocalSourceReturnsToLiveTiming()
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
                sequence: 1,
                simulationDeltaTimeSeconds: 0.05f);
            scheduler.Tick(0.1f, 0d);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.05f));

            source.Command = new PlayerCommand(
                Vector2.up,
                Vector2.zero,
                false,
                sequence: 2);
            scheduler.Tick(0.2f, 0d);

            Assert.That(consumer.LastCommand.HasSimulationDeltaTime, Is.False);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void CommandFeature_AccumulatesRemoteCommandSimulationTime()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            commands.SetLocallyControlled(false);
            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.left,
                        Vector2.one,
                        false,
                        sequence: 1,
                        simulationDeltaTimeSeconds: 0.02f)),
                Is.True);
            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.right,
                        Vector2.one,
                        true,
                        sequence: 2,
                        simulationDeltaTimeSeconds: 0.03f)),
                Is.True);

            scheduler.Tick(0.1f, 0d);

            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.right));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(Vector2.one * 2f));
            Assert.That(consumer.LastCommand.JumpPressed, Is.True);
            Assert.That(consumer.LastCommand.HasSimulationDeltaTime, Is.True);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.05f).Within(0.0001f));

            scheduler.Tick(0.1f, 0d);

            Assert.That(consumer.LastCommand.HasSimulationDeltaTime, Is.True);
            Assert.That(consumer.LastDeltaTime, Is.Zero);
        }

        [Test]
        public void CommandFeature_TimedRemoteTimeoutResumesNeutralSimulation()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            commands.SetLocallyControlled(false);
            commands.SubmitCommand(
                new PlayerCommand(
                    Vector2.up,
                    Vector2.zero,
                    false,
                    sequence: 1,
                    simulationDeltaTimeSeconds: 0.05f));

            scheduler.Tick(0.1f, 0d);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.05f));

            scheduler.Tick(0.2f, 0d);

            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.HasSimulationDeltaTime, Is.False);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void CommandFeature_ZeroRemoteTimeoutPreservesCommandOwnedTime()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            commands.SetLocallyControlled(false);
            commands.SetRemoteCommandTimeout(0f);
            commands.SubmitCommand(
                new PlayerCommand(
                    Vector2.up,
                    Vector2.zero,
                    false,
                    sequence: 1,
                    simulationDeltaTimeSeconds: 0.05f));
            scheduler.Tick(0.1f, 0d);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.05f));

            scheduler.Tick(1f, 0d);

            Assert.That(consumer.LastCommand.HasSimulationDeltaTime, Is.True);
            Assert.That(consumer.LastDeltaTime, Is.Zero);
            Assert.That(commands.RemoteCommandTimeout, Is.Zero);
        }

        [Test]
        public void CommandFeature_RemoteTimingModeChangesAtDispatchBoundary()
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
                        Vector2.up,
                        Vector2.zero,
                        false,
                        sequence: 1,
                        simulationDeltaTimeSeconds: 0.05f)),
                Is.True);
            scheduler.Tick(0.1f, 0d);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.05f));

            Assert.That(
                commands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.up,
                        Vector2.zero,
                        false,
                        sequence: 2)),
                Is.True);
            scheduler.Tick(0.2f, 0d);

            Assert.That(consumer.LastCommand.HasSimulationDeltaTime, Is.False);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void CommandFeature_MixedTimingModeCanRetryWithoutLosingEdges()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out _,
                out PlayerCommandFeature commands,
                out ProbeCommandConsumer consumer);
            commands.SetLocallyControlled(false);

            Assert.That(
                commands.SubmitCommandDetailed(
                    new PlayerCommand(
                        Vector2.up,
                        Vector2.zero,
                        false,
                        sequence: 1,
                        simulationDeltaTimeSeconds: 0.05f)),
                Is.EqualTo(PlayerCommandSubmissionResult.Accepted));
            PlayerCommand liveCommand = new(
                Vector2.right,
                new Vector2(3f, -2f),
                true,
                sequence: 2);
            Assert.That(
                commands.SubmitCommandDetailed(liveCommand),
                Is.EqualTo(PlayerCommandSubmissionResult.RetryAfterDispatch));

            scheduler.Tick(0.1f, 0d);
            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(1));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(Vector2.zero));
            Assert.That(consumer.LastCommand.JumpPressed, Is.False);

            Assert.That(
                commands.SubmitCommandDetailed(liveCommand),
                Is.EqualTo(PlayerCommandSubmissionResult.Accepted));
            scheduler.Tick(0.2f, 0d);

            Assert.That(consumer.LastCommand.Sequence, Is.EqualTo(2));
            Assert.That(consumer.LastCommand.Look, Is.EqualTo(new Vector2(3f, -2f)));
            Assert.That(consumer.LastCommand.JumpPressed, Is.True);
            Assert.That(consumer.LastCommand.HasSimulationDeltaTime, Is.False);
            Assert.That(consumer.LastDeltaTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void PlayerCommand_TransportRoundTripPreservesTimingMode()
        {
            PlayerCommand live = new(
                Vector2.up,
                Vector2.one,
                true,
                sequence: 7);
            PlayerCommand restored = new(
                live.Move,
                live.Look,
                live.JumpPressed,
                live.Sequence,
                live.SimulationDeltaTimeSeconds,
                live.HasSimulationDeltaTime,
                live.MoveSpace);

            Assert.That(restored.HasSimulationDeltaTime, Is.False);
            Assert.That(restored.SimulationDeltaTimeSeconds, Is.Zero);
            Assert.That(
                restored.MoveSpace,
                Is.EqualTo(PlayerCommandMoveSpace.Relative));

            PlayerCommand timed = new(
                Vector2.down,
                Vector2.zero,
                false,
                8,
                0.125f,
                true,
                PlayerCommandMoveSpace.World);
            PlayerCommand restoredTimed = new(
                timed.Move,
                timed.Look,
                timed.JumpPressed,
                timed.Sequence,
                timed.SimulationDeltaTimeSeconds,
                timed.HasSimulationDeltaTime,
                timed.MoveSpace);

            Assert.That(restoredTimed.HasSimulationDeltaTime, Is.True);
            Assert.That(
                restoredTimed.SimulationDeltaTimeSeconds,
                Is.EqualTo(0.125f));
            Assert.That(
                restoredTimed.MoveSpace,
                Is.EqualTo(PlayerCommandMoveSpace.World));
        }

        [Test]
        public void PlayerCommand_WorldMoveHelperHonorsMoveSpace()
        {
            GameObject reference = CreateObject("Move Space Reference");
            reference.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            PlayerCommand relative = new(
                Vector2.up,
                Vector2.zero,
                false);
            PlayerCommand world = new(
                Vector2.up,
                Vector2.zero,
                false,
                1,
                0f,
                false,
                PlayerCommandMoveSpace.World);

            Assert.That(
                Vector3.Dot(
                    relative.GetWorldMoveDirection(reference.transform),
                    Vector3.right),
                Is.GreaterThan(0.99f));
            Assert.That(
                Vector3.Dot(
                    world.GetWorldMoveDirection(reference.transform),
                    Vector3.forward),
                Is.GreaterThan(0.99f));
        }

        [Test]
        public void PlayerCommand_SanitizesUntrustedSimulationDurations()
        {
            PlayerCommand notANumber = new(
                Vector2.zero,
                Vector2.zero,
                false,
                sequence: 1,
                simulationDeltaTimeSeconds: float.NaN);
            PlayerCommand positiveInfinity = new(
                Vector2.zero,
                Vector2.zero,
                false,
                sequence: 2,
                simulationDeltaTimeSeconds: float.PositiveInfinity);
            PlayerCommand largeFinite = new(
                Vector2.zero,
                Vector2.zero,
                false,
                sequence: 3,
                simulationDeltaTimeSeconds: 1000f);

            Assert.That(notANumber.SimulationDeltaTimeSeconds, Is.Zero);
            Assert.That(positiveInfinity.SimulationDeltaTimeSeconds, Is.Zero);
            Assert.That(largeFinite.SimulationDeltaTimeSeconds, Is.EqualTo(1000f));
        }

        [Test]
        public void PlayerCommand_SanitizesUntrustedMoveAndLookComponents()
        {
            PlayerCommand invalid = new(
                new Vector2(float.NaN, float.PositiveInfinity),
                new Vector2(float.NegativeInfinity, float.NaN),
                false);
            PlayerCommand bounded = new(
                new Vector2(2f, 0f),
                new Vector2(1000f, -1000f),
                false);

            Assert.That(invalid.Move, Is.EqualTo(Vector2.zero));
            Assert.That(invalid.Look, Is.EqualTo(Vector2.zero));
            Assert.That(bounded.Move, Is.EqualTo(Vector2.right));
            Assert.That(bounded.Look.x, Is.EqualTo(280f).Within(0.0001f));
            Assert.That(bounded.Look.y, Is.EqualTo(-180f));
        }

        [Test]
        public void LookFeature_ResetRepairsNonFiniteViewState()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Recovering Look Player");
            player.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            PlayerLookFeature look = player.AddComponent<PlayerLookFeature>();
            commands.SetTickScheduler(scheduler);
            player.SetActive(true);

            const System.Reflection.BindingFlags PrivateInstance =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            System.Reflection.FieldInfo yawField =
                typeof(PlayerLookFeature).GetField("_yaw", PrivateInstance);
            System.Reflection.FieldInfo pitchField =
                typeof(PlayerLookFeature).GetField("_pitch", PrivateInstance);
            Assert.That(yawField, Is.Not.Null);
            Assert.That(pitchField, Is.Not.Null);
            yawField.SetValue(look, float.NaN);
            pitchField.SetValue(look, float.PositiveInfinity);

            look.ResetPlayerCommandState();

            Assert.That(look.Yaw, Is.Zero);
            Assert.That(look.Pitch, Is.Zero);
            Assert.That(
                Quaternion.Angle(
                    player.transform.localRotation,
                    Quaternion.identity),
                Is.LessThan(0.001f));
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
        public void Motor_TimedBacklogProducesSameResultAcrossCommandPartitions()
        {
            BudgetedTickScheduler singleBatchScheduler = new();
            GameObject singleBatchPlayer = CreateMotorPlayer(
                singleBatchScheduler,
                "Timed Single Batch Player",
                out _,
                out Transform singleBatchTransform);
            PlayerCharacterMotorFeature singleBatchMotor =
                singleBatchPlayer.GetComponent<PlayerCharacterMotorFeature>();

            PlayerCommand singleBatchCommand = new(
                Vector2.up,
                Vector2.zero,
                false,
                sequence: 1,
                simulationDeltaTimeSeconds: 0.4f);
            singleBatchMotor.ProcessPlayerCommand(singleBatchCommand, 0.4f);
            singleBatchMotor.ProcessPlayerCommand(
                new PlayerCommand(
                    Vector2.up,
                    Vector2.zero,
                    false,
                    sequence: 2,
                    simulationDeltaTimeSeconds: 0f),
                0f);

            Vector3 singleBatchPosition = singleBatchTransform.position;
            double singleBatchDiscardedTime =
                singleBatchMotor.TotalDiscardedSimulationTimeSeconds;
            UnityEngine.Object.DestroyImmediate(singleBatchPlayer);

            BudgetedTickScheduler splitBatchScheduler = new();
            GameObject splitBatchPlayer = CreateMotorPlayer(
                splitBatchScheduler,
                "Timed Split Batch Player",
                out _,
                out Transform splitBatchTransform);
            PlayerCharacterMotorFeature splitBatchMotor =
                splitBatchPlayer.GetComponent<PlayerCharacterMotorFeature>();

            for (uint sequence = 1; sequence <= 2; sequence++)
            {
                splitBatchMotor.ProcessPlayerCommand(
                    new PlayerCommand(
                        Vector2.up,
                        Vector2.zero,
                        false,
                        sequence,
                        simulationDeltaTimeSeconds: 0.2f),
                    0.2f);
            }

            Assert.That(
                singleBatchDiscardedTime,
                Is.Zero.Within(0.000001d));
            Assert.That(
                splitBatchMotor.TotalDiscardedSimulationTimeSeconds,
                Is.Zero.Within(0.000001d));
            Assert.That(
                splitBatchTransform.position.z,
                Is.EqualTo(singleBatchPosition.z).Within(0.0001f));
            Assert.That(
                splitBatchTransform.position.y,
                Is.EqualTo(singleBatchPosition.y).Within(0.0001f));
        }

        [Test]
        public void Motor_TimedBacklogIsFiniteAndObservable()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateMotorPlayer(
                scheduler,
                "Bounded Timed Backlog Player",
                out _,
                out _);
            PlayerCharacterMotorFeature motor =
                player.GetComponent<PlayerCharacterMotorFeature>();
            PlayerCommand maximumDurationCommand = new(
                Vector2.up,
                Vector2.zero,
                false,
                sequence: 1,
                simulationDeltaTimeSeconds: 5f);

            for (int i = 0; i < 20; i++)
            {
                motor.ProcessPlayerCommand(
                    maximumDurationCommand,
                    maximumDurationCommand.SimulationDeltaTimeSeconds);
            }

            Assert.That(
                motor.PendingSimulationTimeSeconds,
                Is.LessThanOrEqualTo(motor.MaximumCommandBacklogSeconds));
            Assert.That(
                motor.PendingSimulationTimeSeconds,
                Is.GreaterThan(0d));
            Assert.That(
                motor.TotalDiscardedSimulationTimeSeconds,
                Is.GreaterThan(0d));
        }

        [Test]
        public void RemoteMotor_TimedReplayIsInvariantAcrossSubmissionPartitions()
        {
            BudgetedTickScheduler singleDispatchScheduler = new();
            GameObject singleDispatchPlayer = CreateMotorPlayer(
                singleDispatchScheduler,
                "Remote Single Dispatch Player",
                out _,
                out Transform singleDispatchTransform);
            PlayerCommandFeature singleDispatchCommands =
                singleDispatchPlayer.GetComponent<PlayerCommandFeature>();
            PlayerCharacterMotorFeature singleDispatchMotor =
                singleDispatchPlayer.GetComponent<PlayerCharacterMotorFeature>();
            singleDispatchCommands.SetLocallyControlled(false);
            singleDispatchCommands.SetRemoteCommandTimeout(0f);

            Assert.That(
                singleDispatchCommands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.up,
                        Vector2.zero,
                        false,
                        sequence: 1,
                        simulationDeltaTimeSeconds: 3f)),
                Is.True);
            Assert.That(
                singleDispatchCommands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.up,
                        Vector2.zero,
                        false,
                        sequence: 2,
                        simulationDeltaTimeSeconds: 3f)),
                Is.True);
            singleDispatchScheduler.Tick(0f, 0d);
            for (int i = 0; i < 22; i++)
            {
                singleDispatchScheduler.Tick(0f, 0d);
            }

            Vector3 singleDispatchPosition = singleDispatchTransform.position;
            double singleDispatchDiscardedTime =
                singleDispatchMotor.TotalDiscardedSimulationTimeSeconds;
            UnityEngine.Object.DestroyImmediate(singleDispatchPlayer);

            BudgetedTickScheduler splitDispatchScheduler = new();
            GameObject splitDispatchPlayer = CreateMotorPlayer(
                splitDispatchScheduler,
                "Remote Split Dispatch Player",
                out _,
                out Transform splitDispatchTransform);
            PlayerCommandFeature splitDispatchCommands =
                splitDispatchPlayer.GetComponent<PlayerCommandFeature>();
            PlayerCharacterMotorFeature splitDispatchMotor =
                splitDispatchPlayer.GetComponent<PlayerCharacterMotorFeature>();
            splitDispatchCommands.SetLocallyControlled(false);
            splitDispatchCommands.SetRemoteCommandTimeout(0f);

            Assert.That(
                splitDispatchCommands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.up,
                        Vector2.zero,
                        false,
                        sequence: 1,
                        simulationDeltaTimeSeconds: 3f)),
                Is.True);
            splitDispatchScheduler.Tick(0f, 0d);
            Assert.That(
                splitDispatchCommands.SubmitCommand(
                    new PlayerCommand(
                        Vector2.up,
                        Vector2.zero,
                        false,
                        sequence: 2,
                        simulationDeltaTimeSeconds: 3f)),
                Is.True);
            splitDispatchScheduler.Tick(0f, 0d);
            for (int i = 0; i < 21; i++)
            {
                splitDispatchScheduler.Tick(0f, 0d);
            }

            Assert.That(
                singleDispatchDiscardedTime,
                Is.Zero.Within(0.000001d));
            Assert.That(
                splitDispatchMotor.TotalDiscardedSimulationTimeSeconds,
                Is.Zero.Within(0.000001d));
            Assert.That(
                splitDispatchTransform.position.z,
                Is.EqualTo(singleDispatchPosition.z).Within(0.0001f));
            Assert.That(
                splitDispatchTransform.position.y,
                Is.EqualTo(singleDispatchPosition.y).Within(0.0001f));
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
