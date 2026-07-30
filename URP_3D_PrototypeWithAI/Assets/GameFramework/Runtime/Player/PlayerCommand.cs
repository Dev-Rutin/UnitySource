using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// Immutable command payload suitable for local input, replay, or network transport.
    /// Look is an angular delta in degrees for this command.
    /// </summary>
    public readonly struct PlayerCommand
    {
        public PlayerCommand(
            Vector2 move,
            Vector2 look,
            bool jumpPressed,
            uint sequence = 0)
        {
            Move = Vector2.ClampMagnitude(move, 1f);
            Look = look;
            JumpPressed = jumpPressed;
            Sequence = sequence;
        }

        public Vector2 Move { get; }

        public Vector2 Look { get; }

        public bool JumpPressed { get; }

        public uint Sequence { get; }

        public static PlayerCommand Neutral => default;
    }

    /// <summary>
    /// Replaceable producer for local device, AI possession, replay, or network commands.
    /// Implementations must not allocate from ReadCommand.
    /// </summary>
    public interface IPlayerCommandSource
    {
        bool IsInputAvailable { get; }

        PlayerCommand ReadCommand(float deltaTime);
    }
}
