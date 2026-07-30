using System;
using System.Collections.Generic;
using NUnit.Framework;
using Rutin.GameFramework.Core;
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
        public void CommandFeature_ClampsInputAndConsumesEdgesOnce()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out ProbeCommandSource source,
                out PlayerCommandFeature commands);
            source.Command = new PlayerCommand(
                new Vector2(2f, 0f),
                new Vector2(12f, -4f),
                true,
                7);

            scheduler.Tick(0.016f, 0d);

            Assert.That(commands.CurrentCommand.Move, Is.EqualTo(Vector2.right));
            Assert.That(commands.CurrentCommand.Sequence, Is.EqualTo(7));
            Assert.That(commands.ConsumeJumpPressed(), Is.True);
            Assert.That(commands.ConsumeJumpPressed(), Is.False);
            Assert.That(commands.TryConsumeLookDelta(out Vector2 look), Is.True);
            Assert.That(look, Is.EqualTo(new Vector2(12f, -4f)));
            Assert.That(commands.TryConsumeLookDelta(out _), Is.False);
        }

        [Test]
        public void CommandFeature_OwnershipSwitchClearsLocalAndAcceptsRemoteCommand()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out ProbeCommandSource source,
                out PlayerCommandFeature commands);
            source.Command = new PlayerCommand(Vector2.up, Vector2.zero, false);
            scheduler.Tick(0.016f, 0d);
            Assert.That(commands.CurrentCommand.Move, Is.EqualTo(Vector2.up));

            commands.SetLocallyControlled(false);
            source.Command = new PlayerCommand(Vector2.left, Vector2.zero, false);
            scheduler.Tick(0.016f, 0d);
            Assert.That(commands.CurrentCommand.Move, Is.EqualTo(Vector2.zero));

            commands.SubmitCommand(
                new PlayerCommand(Vector2.right, Vector2.zero, false, 19));
            scheduler.Tick(0.016f, 0d);
            Assert.That(commands.CurrentCommand.Move, Is.EqualTo(Vector2.right));
            Assert.That(commands.CurrentCommand.Sequence, Is.EqualTo(19));
        }

        [Test]
        public void CommandFeature_UnavailableSourceClearsHeldInput()
        {
            BudgetedTickScheduler scheduler = new();
            CreateCommandPlayer(
                scheduler,
                out ProbeCommandSource source,
                out PlayerCommandFeature commands);
            source.Command = new PlayerCommand(Vector2.up, Vector2.zero, false);
            scheduler.Tick(0.016f, 0d);
            Assert.That(commands.CurrentCommand.Move, Is.EqualTo(Vector2.up));

            source.IsInputAvailable = false;
            scheduler.Tick(0.016f, 0d);

            Assert.That(commands.CurrentCommand.Move, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Motor_MovesAndTracksEntityActivationWithoutIndependentUpdate()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Motor Player");
            CharacterController controller = player.AddComponent<CharacterController>();
            player.AddComponent<GameplayEntity>();
            ProbeCommandSource source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            PlayerCharacterMotorFeature motor =
                player.AddComponent<PlayerCharacterMotorFeature>();
            commands.SetTickScheduler(scheduler);
            motor.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            source.Command = new PlayerCommand(Vector2.up, Vector2.zero, false);
            player.SetActive(true);

            Assert.That(scheduler.Count, Is.EqualTo(2));
            scheduler.Tick(0.1f, 0d);

            Assert.That(controller.transform.position.z, Is.GreaterThan(0f));
            player.SetActive(false);
            Assert.That(scheduler.Count, Is.Zero);
            player.SetActive(true);
            Assert.That(scheduler.Count, Is.EqualTo(2));
        }

        [Test]
        public void LookFeature_AppliesSubmittedDeltaOnlyOnce()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Look Player");
            player.AddComponent<GameplayEntity>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            PlayerLookFeature lookFeature = player.AddComponent<PlayerLookFeature>();
            GameObject pitchObject = CreateObject("Pitch Pivot");
            pitchObject.transform.SetParent(player.transform, false);

            commands.SetTickScheduler(scheduler);
            lookFeature.SetTickScheduler(scheduler);
            commands.SetLocallyControlled(false);
            lookFeature.SetViewTransforms(player.transform, pitchObject.transform);
            player.SetActive(true);
            commands.SubmitCommand(
                new PlayerCommand(Vector2.zero, new Vector2(30f, 10f), false));

            scheduler.Tick(0.016f, 0d);
            Assert.That(lookFeature.Yaw, Is.EqualTo(30f).Within(0.001f));
            Assert.That(lookFeature.Pitch, Is.EqualTo(-10f).Within(0.001f));

            scheduler.Tick(0.016f, 0d);
            Assert.That(lookFeature.Yaw, Is.EqualTo(30f).Within(0.001f));
            Assert.That(lookFeature.Pitch, Is.EqualTo(-10f).Within(0.001f));
        }

        [Test]
        public void CommandAndLookTick_DoesNotAllocateManagedMemory()
        {
            BudgetedTickScheduler scheduler = new();
            GameObject player = CreateInactiveObject("Allocation Player");
            player.AddComponent<GameplayEntity>();
            ProbeCommandSource source = player.AddComponent<ProbeCommandSource>();
            PlayerCommandFeature commands = player.AddComponent<PlayerCommandFeature>();
            PlayerLookFeature lookFeature = player.AddComponent<PlayerLookFeature>();
            commands.SetTickScheduler(scheduler);
            lookFeature.SetTickScheduler(scheduler);
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
            out PlayerCommandFeature commands)
        {
            GameObject player = CreateInactiveObject("Command Player");
            player.AddComponent<GameplayEntity>();
            source = player.AddComponent<ProbeCommandSource>();
            commands = player.AddComponent<PlayerCommandFeature>();
            commands.SetTickScheduler(scheduler);
            commands.SetCommandSource(source);
            player.SetActive(true);
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
