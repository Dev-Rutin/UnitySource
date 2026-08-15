using NUnit.Framework;
using UnityEngine;

namespace Rutin.GunShop.Tests.EditMode
{
    public sealed class DualHandControlTests
    {
        [Test]
        public void CommandFrame_PreservesIndependentSimultaneousCommands()
        {
            var left = new HandCommand(Vector3.right, Vector3.zero, true);
            var right = new HandCommand(Vector3.forward, Vector3.up, false);
            var frame = new DualHandCommandFrame(left, right);

            Assert.That(frame.GetCommand(HandSide.Left).Translation, Is.EqualTo(Vector3.right));
            Assert.That(frame.GetCommand(HandSide.Left).GripHeld, Is.True);
            Assert.That(frame.GetCommand(HandSide.Right).Translation, Is.EqualTo(Vector3.forward));
            Assert.That(frame.GetCommand(HandSide.Right).Rotation, Is.EqualTo(Vector3.up));
        }

        [Test]
        public void CommandMapper_UsesSameAxesForTranslationOrPitchYawRoll()
        {
            var axes = new Vector3(0.25f, 0.5f, 0.75f);

            var translation = AssemblyHandCommandMapper.Map(axes, false, true);
            var rotation = AssemblyHandCommandMapper.Map(axes, true, false);

            Assert.That(translation.Translation, Is.EqualTo(axes));
            Assert.That(translation.Rotation, Is.EqualTo(Vector3.zero));
            Assert.That(translation.GripHeld, Is.True);
            Assert.That(rotation.Translation, Is.EqualTo(Vector3.zero));
            Assert.That(rotation.Rotation, Is.EqualTo(new Vector3(0.75f, 0.25f, 0.5f)));
            Assert.That(rotation.GripHeld, Is.False);
        }

        [Test]
        public void Motor_ClampsTargetToMaximumReach()
        {
            var workspace = new GameObject("Workspace");
            var hand = CreateHand("Hand", Vector3.zero, workspace.transform, 1.25f);
            var motor = hand.GetComponent<PhysicsHandMotor>();

            motor.SetTargetPose(new Vector3(10f, 0f, 0f), Quaternion.identity);

            Assert.That(Vector3.Distance(workspace.transform.position, motor.TargetPosition),
                Is.EqualTo(1.25f).Within(0.0001f));

            Object.DestroyImmediate(hand);
            Object.DestroyImmediate(workspace);
        }

        [Test]
        public void RigController_UpdatesBothHandsWithoutCrossTalk()
        {
            var workspace = new GameObject("Workspace");
            var left = CreateHand("Left", new Vector3(-0.2f, 0f, 0f), workspace.transform, 2f);
            var right = CreateHand("Right", new Vector3(0.2f, 0f, 0f), workspace.transform, 2f);
            var controllerObject = new GameObject("Controller");
            var controller = controllerObject.AddComponent<DualHandRigController>();
            controller.Configure(null, left.GetComponent<PhysicsHandMotor>(), right.GetComponent<PhysicsHandMotor>(), workspace.transform, 1f, 90f);

            var leftStart = left.transform.position;
            var rightStart = right.transform.position;
            controller.ApplyCommandFrame(
                new DualHandCommandFrame(
                    new HandCommand(Vector3.right, Vector3.zero, true),
                    new HandCommand(Vector3.forward, Vector3.up, false)),
                0.5f);

            Assert.That(left.GetComponent<PhysicsHandMotor>().TargetPosition,
                Is.EqualTo(leftStart + Vector3.right * 0.5f));
            Assert.That(right.GetComponent<PhysicsHandMotor>().TargetPosition,
                Is.EqualTo(rightStart + Vector3.forward * 0.5f));
            Assert.That(controller.LeftGripHeld, Is.True);
            Assert.That(controller.RightGripHeld, Is.False);

            Object.DestroyImmediate(controllerObject);
            Object.DestroyImmediate(left);
            Object.DestroyImmediate(right);
            Object.DestroyImmediate(workspace);
        }

        [Test]
        public void WorkbenchCamera_AppliesYawAndPitchFromMouseDelta()
        {
            var yaw = new GameObject("Yaw").transform;
            var pitch = new GameObject("Pitch").transform;
            pitch.SetParent(yaw, false);
            var controller = yaw.gameObject.AddComponent<WorkbenchCameraController>();
            controller.Configure(yaw, pitch, 0.1f);

            controller.ApplyLookDelta(new Vector2(10f, -20f));

            Assert.That(Quaternion.Angle(yaw.localRotation, Quaternion.Euler(0f, 1f, 0f)),
                Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(pitch.localRotation, Quaternion.Euler(2f, 0f, 0f)),
                Is.LessThan(0.001f));

            Object.DestroyImmediate(yaw.gameObject);
        }

        private static GameObject CreateHand(
            string name,
            Vector3 position,
            Transform workspace,
            float maximumReach)
        {
            var hand = new GameObject(name);
            hand.transform.position = position;
            var body = hand.AddComponent<Rigidbody>();
            body.useGravity = false;
            var motor = hand.AddComponent<PhysicsHandMotor>();
            motor.Configure(workspace, maximumReach, 100f, 20f, 4f, 10f);
            return hand;
        }
    }
}
