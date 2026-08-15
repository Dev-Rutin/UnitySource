using UnityEngine;

namespace Rutin.GunShop
{
    [DisallowMultipleComponent]
    public sealed class DualHandRigController : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour commandSourceComponent;
        [SerializeField] private PhysicsHandMotor leftHand;
        [SerializeField] private PhysicsHandMotor rightHand;
        [SerializeField] private Transform movementSpace;
        [SerializeField, Min(0f)] private float translationSpeed = 0.8f;
        [SerializeField, Min(0f)] private float rotationSpeed = 110f;

        private IDualHandCommandSource commandSource;

        public bool LeftGripHeld { get; private set; }

        public bool RightGripHeld { get; private set; }

        private void Awake()
        {
            ResolveCommandSource();
        }

        private void Update()
        {
            if (commandSource == null)
            {
                ResolveCommandSource();
            }

            if (commandSource != null)
            {
                ApplyCommandFrame(commandSource.CurrentFrame, Time.deltaTime);
            }
        }

        private void OnDisable()
        {
            LeftGripHeld = false;
            RightGripHeld = false;
        }

        public void Configure(
            MonoBehaviour newCommandSource,
            PhysicsHandMotor newLeftHand,
            PhysicsHandMotor newRightHand,
            Transform newMovementSpace,
            float newTranslationSpeed = 0.8f,
            float newRotationSpeed = 110f)
        {
            commandSourceComponent = newCommandSource;
            leftHand = newLeftHand;
            rightHand = newRightHand;
            movementSpace = newMovementSpace;
            translationSpeed = Mathf.Max(0f, newTranslationSpeed);
            rotationSpeed = Mathf.Max(0f, newRotationSpeed);
            ResolveCommandSource();
        }

        public bool IsGripHeld(HandSide side)
        {
            return side == HandSide.Left ? LeftGripHeld : RightGripHeld;
        }

        public void ApplyCommandFrame(DualHandCommandFrame frame, float deltaTime)
        {
            var safeDeltaTime = Mathf.Max(0f, deltaTime);
            ApplyHandCommand(leftHand, frame.Left, safeDeltaTime);
            ApplyHandCommand(rightHand, frame.Right, safeDeltaTime);
            LeftGripHeld = frame.Left.GripHeld;
            RightGripHeld = frame.Right.GripHeld;
        }

        private void ApplyHandCommand(PhysicsHandMotor motor, HandCommand command, float deltaTime)
        {
            if (motor == null)
            {
                return;
            }

            var translation = command.Translation * (translationSpeed * deltaTime);
            if (movementSpace != null)
            {
                translation = movementSpace.TransformDirection(translation);
            }

            var rotationDelta = Quaternion.Euler(command.Rotation * (rotationSpeed * deltaTime));
            motor.SetTargetPose(
                motor.TargetPosition + translation,
                motor.TargetRotation * rotationDelta);
        }

        private void ResolveCommandSource()
        {
            commandSource = commandSourceComponent as IDualHandCommandSource;
        }
    }
}
