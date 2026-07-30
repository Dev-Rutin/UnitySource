using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rutin.GameFramework.Core;
using Rutin.GameFramework.InputSystem;
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

            public void ProcessPlayerCommand(
                PlayerCommand command,
                float deltaTime)
            {
                CallCount++;
                LastCommand = command;
                OrderLog?.Add(Marker);
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
        public void CommandFeature_RemoteTimeoutDispatchesNeutralOnce()
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
            Assert.That(consumer.CallCount, Is.EqualTo(2));
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
            Assert.That(consumer.LastCommand.Move, Is.EqualTo(Vector2.up));

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

        private void CreateCommandPlayer(
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
