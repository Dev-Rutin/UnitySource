using UnityEngine;
using UnityEngine.InputSystem;

namespace Rutin.GunShop
{
    [DefaultExecutionOrder(-100)]
    public sealed class AssemblyDualHandInputSource : MonoBehaviour, IDualHandCommandSource
    {
        public DualHandCommandFrame CurrentFrame { get; private set; } = DualHandCommandFrame.Neutral;

        private void Update()
        {
            SampleInput();
        }

        private void OnDisable()
        {
            CurrentFrame = DualHandCommandFrame.Neutral;
        }

        public void SampleInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                CurrentFrame = DualHandCommandFrame.Neutral;
                return;
            }

            var leftAxes = new Vector3(
                ReadAxis(keyboard.dKey.isPressed, keyboard.aKey.isPressed),
                ReadAxis(keyboard.eKey.isPressed, keyboard.qKey.isPressed),
                ReadAxis(keyboard.wKey.isPressed, keyboard.sKey.isPressed));

            var rightAxes = new Vector3(
                ReadAxis(keyboard.lKey.isPressed, keyboard.jKey.isPressed),
                ReadAxis(keyboard.oKey.isPressed, keyboard.uKey.isPressed),
                ReadAxis(keyboard.iKey.isPressed, keyboard.kKey.isPressed));

            CurrentFrame = new DualHandCommandFrame(
                AssemblyHandCommandMapper.Map(
                    leftAxes,
                    keyboard.leftAltKey.isPressed,
                    keyboard.leftShiftKey.isPressed),
                AssemblyHandCommandMapper.Map(
                    rightAxes,
                    keyboard.rightAltKey.isPressed,
                    keyboard.rightShiftKey.isPressed));
        }

        private static float ReadAxis(bool positive, bool negative)
        {
            return (positive ? 1f : 0f) - (negative ? 1f : 0f);
        }
    }
}
