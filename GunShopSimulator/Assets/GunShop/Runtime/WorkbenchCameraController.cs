using UnityEngine;
using UnityEngine.InputSystem;

namespace Rutin.GunShop
{
    [DisallowMultipleComponent]
    public sealed class WorkbenchCameraController : MonoBehaviour
    {
        [SerializeField] private Transform yawPivot;
        [SerializeField] private Transform pitchPivot;
        [SerializeField, Min(0f)] private float sensitivity = 0.12f;
        [SerializeField] private Vector2 pitchLimits = new(-55f, 65f);
        [SerializeField] private bool lockCursorOnEnable = true;

        private float yaw;
        private float pitch;

        private void OnEnable()
        {
            if (yawPivot != null)
            {
                yaw = yawPivot.localEulerAngles.y;
            }

            if (pitchPivot != null)
            {
                pitch = NormalizeAngle(pitchPivot.localEulerAngles.x);
            }

            if (lockCursorOnEnable)
            {
                SetCursorLocked(true);
            }
        }

        private void OnDisable()
        {
            SetCursorLocked(false);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCursorLocked(false);
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                SetCursorLocked(true);
            }

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                ApplyLookDelta(mouse.delta.ReadValue());
            }
        }

        public void Configure(
            Transform newYawPivot,
            Transform newPitchPivot,
            float newSensitivity = 0.12f)
        {
            yawPivot = newYawPivot;
            pitchPivot = newPitchPivot;
            sensitivity = Mathf.Max(0f, newSensitivity);
        }

        public void ApplyLookDelta(Vector2 delta)
        {
            yaw += delta.x * sensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * sensitivity, pitchLimits.x, pitchLimits.y);

            if (yawPivot != null)
            {
                yawPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            if (pitchPivot != null)
            {
                pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
