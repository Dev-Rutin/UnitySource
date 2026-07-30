using Rutin.GameFramework.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rutin.GameFramework.InputSystem
{
    /// <summary>
    /// Optional local-device adapter. The runtime player assembly depends only on
    /// IPlayerCommandSource, so network or replay sources can replace this component.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputSystemPlayerCommandSource :
        MonoBehaviour,
        IPlayerCommandSource
    {
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private bool enableActionsWithComponent = true;
        [SerializeField] private float lookSensitivity = 1f;
        [SerializeField] private bool lookValueIsAngularRate;
        [SerializeField] private bool invertLookY;

        private bool _ownsMoveEnable;
        private bool _ownsLookEnable;
        private bool _ownsJumpEnable;
        private uint _sequence;

        public bool IsInputAvailable => isActiveAndEnabled;

        private void OnEnable()
        {
            if (!enableActionsWithComponent)
            {
                return;
            }

            _ownsMoveEnable = EnableIfNeeded(moveAction);
            _ownsLookEnable = EnableIfNeeded(lookAction);
            _ownsJumpEnable = EnableIfNeeded(jumpAction);
        }

        private void OnDisable()
        {
            DisableIfOwned(moveAction, _ownsMoveEnable);
            DisableIfOwned(lookAction, _ownsLookEnable);
            DisableIfOwned(jumpAction, _ownsJumpEnable);
            _ownsMoveEnable = false;
            _ownsLookEnable = false;
            _ownsJumpEnable = false;
        }

        public PlayerCommand ReadCommand(float deltaTime)
        {
            if (!IsInputAvailable)
            {
                return PlayerCommand.Neutral;
            }

            Vector2 move = ReadVector2(moveAction);
            Vector2 look = ReadVector2(lookAction) * lookSensitivity;
            if (lookValueIsAngularRate)
            {
                look *= Mathf.Max(0f, deltaTime);
            }

            if (invertLookY)
            {
                look.y = -look.y;
            }

            bool jumpPressed =
                jumpAction != null &&
                jumpAction.action != null &&
                jumpAction.action.WasPressedThisFrame();

            _sequence++;
            if (_sequence == 0)
            {
                _sequence = 1;
            }

            return new PlayerCommand(move, look, jumpPressed, _sequence);
        }

        private static Vector2 ReadVector2(InputActionReference reference)
        {
            return reference != null && reference.action != null
                ? reference.action.ReadValue<Vector2>()
                : Vector2.zero;
        }

        private static bool EnableIfNeeded(InputActionReference reference)
        {
            InputAction action = reference != null ? reference.action : null;
            if (action == null || action.enabled)
            {
                return false;
            }

            action.Enable();
            return true;
        }

        private static void DisableIfOwned(
            InputActionReference reference,
            bool owned)
        {
            if (owned && reference != null && reference.action != null)
            {
                reference.action.Disable();
            }
        }
    }
}
