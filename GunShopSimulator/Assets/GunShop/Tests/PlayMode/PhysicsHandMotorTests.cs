using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rutin.GunShop.Tests.PlayMode
{
    public sealed class PhysicsHandMotorTests
    {
        [UnityTest]
        public IEnumerator Motor_TracksTargetAndRespectsSpeedLimits()
        {
            var workspace = new GameObject("Workspace");
            var hand = CreateHand(workspace.transform, 2f, 80f, 20f, 2f, 5f);
            var motor = hand.GetComponent<PhysicsHandMotor>();
            var target = new Vector3(0.75f, 0.2f, 0.1f);
            var initialDistance = Vector3.Distance(hand.transform.position, target);

            motor.SetTargetPose(target, Quaternion.Euler(0f, 45f, 0f));
            for (var index = 0; index < 30; index++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(Vector3.Distance(hand.transform.position, target), Is.LessThan(initialDistance));
            Assert.That(motor.Body.linearVelocity.magnitude, Is.LessThanOrEqualTo(2.001f));
            Assert.That(motor.Body.angularVelocity.magnitude, Is.LessThanOrEqualTo(5.001f));

            Object.Destroy(hand);
            Object.Destroy(workspace);
        }

        [UnityTest]
        public IEnumerator Motor_ClampsAppliedForceAndTorque()
        {
            var workspace = new GameObject("Workspace");
            var hand = CreateHand(workspace.transform, 2f, 5f, 2f, 4f, 10f);
            var motor = hand.GetComponent<PhysicsHandMotor>();

            motor.SetTargetPose(new Vector3(2f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
            yield return new WaitForFixedUpdate();

            Assert.That(motor.LastAppliedForce.magnitude, Is.LessThanOrEqualTo(5.001f));
            Assert.That(motor.LastAppliedTorque.magnitude, Is.LessThanOrEqualTo(2.001f));

            Object.Destroy(hand);
            Object.Destroy(workspace);
        }

        [UnityTest]
        public IEnumerator Motor_DisableAndEnableRestoresSafeHomeState()
        {
            var workspace = new GameObject("Workspace");
            var hand = CreateHand(workspace.transform, 2f, 80f, 20f, 4f, 10f);
            var motor = hand.GetComponent<PhysicsHandMotor>();
            var homePosition = hand.transform.position;

            motor.Body.position = new Vector3(0.8f, 0.4f, 0.2f);
            motor.Body.linearVelocity = Vector3.one;
            motor.Body.angularVelocity = Vector3.one;
            motor.enabled = false;

            Assert.That(motor.Body.position, Is.EqualTo(homePosition));
            Assert.That(motor.Body.linearVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(motor.Body.angularVelocity, Is.EqualTo(Vector3.zero));

            motor.enabled = true;
            yield return null;

            Assert.That(motor.TargetPosition, Is.EqualTo(homePosition));
            Assert.That(motor.TargetRotation, Is.EqualTo(Quaternion.identity));

            Object.Destroy(hand);
            Object.Destroy(workspace);
        }

        [UnityTest]
        public IEnumerator Motor_InvalidTargetResetsWithoutLeavingNonFiniteState()
        {
            var workspace = new GameObject("Workspace");
            var hand = CreateHand(workspace.transform, 2f, 80f, 20f, 4f, 10f);
            var motor = hand.GetComponent<PhysicsHandMotor>();
            var homePosition = hand.transform.position;

            motor.SetTargetPose(
                new Vector3(float.NaN, 0f, 0f),
                Quaternion.identity);
            yield return null;

            Assert.That(motor.TargetPosition, Is.EqualTo(homePosition));
            Assert.That(float.IsNaN(motor.Body.position.x), Is.False);

            Object.Destroy(hand);
            Object.Destroy(workspace);
        }

        [UnityTest]
        public IEnumerator Motor_AbnormalBodyPositionRestoresHomePose()
        {
            var workspace = new GameObject("Workspace");
            var hand = CreateHand(workspace.transform, 1f, 80f, 20f, 4f, 10f);
            var motor = hand.GetComponent<PhysicsHandMotor>();
            var homePosition = hand.transform.position;

            motor.Body.position = new Vector3(10f, 0f, 0f);
            yield return new WaitForFixedUpdate();

            Assert.That(motor.Body.position, Is.EqualTo(homePosition));
            Assert.That(motor.TargetPosition, Is.EqualTo(homePosition));
            Assert.That(motor.Body.linearVelocity, Is.EqualTo(Vector3.zero));

            Object.Destroy(hand);
            Object.Destroy(workspace);
        }

        private static GameObject CreateHand(
            Transform workspace,
            float maximumReach,
            float maximumForce,
            float maximumTorque,
            float maximumLinearSpeed,
            float maximumAngularSpeed)
        {
            var hand = new GameObject("Hand");
            var body = hand.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.mass = 1f;
            var motor = hand.AddComponent<PhysicsHandMotor>();
            motor.Configure(
                workspace,
                maximumReach,
                maximumForce,
                maximumTorque,
                maximumLinearSpeed,
                maximumAngularSpeed);
            return hand;
        }
    }
}
