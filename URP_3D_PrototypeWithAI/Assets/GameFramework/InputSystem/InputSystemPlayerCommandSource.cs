using Rutin.GameFramework.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rutin.GameFramework.InputSystem
{
    /// <summary>
    /// Optional local-device adapter. The runtime player assembly depends only on
    /// IPlayerCommandSource, so network or replay sources can replace this component.
    /// </summary>
    [DefaultExecutionOrder(-8995)]
    [DisallowMultipleComponent]
    public sealed class InputSystemPlayerCommandSource :
        MonoBehaviour,
        IPlayerCommandSource,
        IBufferedPlayerCommandSource
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
        private Vector2 _latestMove;
        private Vector2 _pendingLook;
        private bool _pendingJump;
        private uint _sequence;

        public bool IsInputAvailable => isActiveAndEnabled;

        private void OnEnable()
        {
            ClearBufferedInput();
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
            ClearBufferedInput();
        }

        private void Update()
        {
            BufferInputSample(
                ReadVector2(moveAction),
                ReadVector2(lookAction),
                jumpAction != null &&
                jumpAction.action != null &&
                jumpAction.action.WasPressedThisFrame(),
                Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Latches one render-frame sample. Exposed for custom Input System bridges and tests;
        /// ReadCommand returns all accumulated look/jump data exactly once.
        /// </summary>
        public void BufferInputSample(
            Vector2 move,
            Vector2 look,
            bool jumpPressed,
            float sampleDeltaTime)
        {
            _latestMove = move;
            look *= lookSensitivity;
            if (lookValueIsAngularRate)
            {
                look *= Mathf.Max(0f, sampleDeltaTime);
            }

            if (invertLookY)
            {
                look.y = -look.y;
            }

            _pendingLook += look;
            _pendingJump |= jumpPressed;
        }

        public PlayerCommand ReadCommand(float deltaTime)
        {
            if (!IsInputAvailable)
            {
                return PlayerCommand.Neutral;
            }

            _sequence++;
            if (_sequence == 0)
            {
                _sequence = 1;
            }

            PlayerCommand command = new(
                _latestMove,
                _pendingLook,
                _pendingJump,
                _sequence);
            _pendingLook = Vector2.zero;
            _pendingJump = false;
            return command;
        }

        public void DiscardBufferedInput()
        {
            ClearBufferedInput();
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

        private void ClearBufferedInput()
        {
            _latestMove = Vector2.zero;
            _pendingLook = Vector2.zero;
            _pendingJump = false;
        }
    }
}
