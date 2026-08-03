using UnityEngine;

namespace Rutin.GameFramework.Player
{
    public enum PlayerCommandMoveSpace
    {
        Relative = 0,
        World = 1
    }

    /// <summary>
    /// Immutable command payload suitable for local input, replay, or network transport.
    /// Look is an angular delta in degrees for this command. A positive
    /// SimulationDeltaTimeSeconds makes replay simulation independent of scheduler timing.
    /// Commands constructed without a duration use the dispatch tick delta for live input;
    /// an explicitly supplied zero-duration command advances no simulation time. Network
    /// serializers must transport MoveSpace, WorldFacing, HasWorldFacing,
    /// HasSimulationDeltaTime, and SimulationDeltaTimeSeconds.
    /// </summary>
    public readonly struct PlayerCommand
    {
        public PlayerCommand(
            Vector2 move,
            Vector2 look,
            bool jumpPressed,
            uint sequence = 0)
            : this(
                move,
                look,
                jumpPressed,
                sequence,
                0f,
                false)
        {
        }

        public PlayerCommand(
            Vector2 move,
            Vector2 look,
            bool jumpPressed,
            uint sequence,
            float simulationDeltaTimeSeconds)
            : this(
                move,
                look,
                jumpPressed,
                sequence,
                simulationDeltaTimeSeconds,
                true)
        {
        }

        public PlayerCommand(
            Vector2 move,
            Vector2 look,
            bool jumpPressed,
            uint sequence,
            float simulationDeltaTimeSeconds,
            bool hasSimulationDeltaTime,
            PlayerCommandMoveSpace moveSpace = PlayerCommandMoveSpace.Relative,
            Vector2 worldFacing = default,
            bool hasWorldFacing = false)
        {
            Move = SanitizeMove(move);
            Look = SanitizeLook(look);
            JumpPressed = jumpPressed;
            Sequence = sequence;
            MoveSpace = SanitizeMoveSpace(moveSpace);
            Vector2 sanitizedWorldFacing =
                SanitizeWorldFacing(worldFacing);
            HasWorldFacing = hasWorldFacing &&
                sanitizedWorldFacing.sqrMagnitude > 0f;
            WorldFacing = HasWorldFacing
                ? sanitizedWorldFacing
                : Vector2.zero;
            HasSimulationDeltaTime = hasSimulationDeltaTime;
            SimulationDeltaTimeSeconds = hasSimulationDeltaTime
                ? SanitizeSimulationDeltaTime(simulationDeltaTimeSeconds)
                : 0f;
        }

        /// <summary>
        /// Planar movement intent with magnitude at most one. Relative commands use x as
        /// right/strafe and y as forward in the consumer stack's movement reference. World
        /// commands use x as world X and y as world Z. Consumers must branch on MoveSpace or
        /// use GetWorldMoveDirection when they need a world-space vector.
        /// </summary>
        public Vector2 Move { get; }

        public Vector2 Look { get; }

        public bool JumpPressed { get; }

        public uint Sequence { get; }

        public PlayerCommandMoveSpace MoveSpace { get; }

        /// <summary>
        /// Optional absolute world XZ facing direction, independent of movement. Network
        /// serializers must transport this value together with HasWorldFacing.
        /// </summary>
        public Vector2 WorldFacing { get; }

        public bool HasWorldFacing { get; }

        public bool HasSimulationDeltaTime { get; }

        public float SimulationDeltaTimeSeconds { get; }

        public static PlayerCommand Neutral => default;

        /// <summary>
        /// Resolves Move to a planar world-space vector while preserving its input magnitude.
        /// A null relativeSpace uses the world X/Z axes.
        /// </summary>
        public Vector3 GetWorldMoveDirection(Transform relativeSpace)
        {
            if (MoveSpace == PlayerCommandMoveSpace.World ||
                relativeSpace == null)
            {
                return new Vector3(Move.x, 0f, Move.y);
            }

            Vector3 forward = relativeSpace.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            Vector3 right = relativeSpace.right;
            right.y = 0f;
            right = right.sqrMagnitude > 0.0001f
                ? right.normalized
                : Vector3.right;
            return Vector3.ClampMagnitude(
                right * Move.x + forward * Move.y,
                1f);
        }

        public Vector3 GetWorldFacingDirection()
        {
            return HasWorldFacing
                ? new Vector3(WorldFacing.x, 0f, WorldFacing.y)
                : Vector3.zero;
        }

        private static Vector2 SanitizeMove(Vector2 value)
        {
            return Vector2.ClampMagnitude(
                new Vector2(
                    SanitizeFinite(value.x),
                    SanitizeFinite(value.y)),
                1f);
        }

        private static Vector2 SanitizeLook(Vector2 value)
        {
            float yawDelta = SanitizeFinite(value.x) % 360f;
            float pitchDelta = Mathf.Clamp(
                SanitizeFinite(value.y),
                -180f,
                180f);
            return new Vector2(yawDelta, pitchDelta);
        }

        private static Vector2 SanitizeWorldFacing(Vector2 value)
        {
            Vector2 sanitized = new(
                SanitizeFinite(value.x),
                SanitizeFinite(value.y));
            return sanitized.sqrMagnitude > 0.0001f
                ? sanitized.normalized
                : Vector2.zero;
        }

        private static float SanitizeSimulationDeltaTime(float seconds)
        {
            return Mathf.Max(0f, SanitizeFinite(seconds));
        }

        private static PlayerCommandMoveSpace SanitizeMoveSpace(
            PlayerCommandMoveSpace value)
        {
            return value == PlayerCommandMoveSpace.World
                ? PlayerCommandMoveSpace.World
                : PlayerCommandMoveSpace.Relative;
        }

        private static float SanitizeFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : value;
        }
    }

    /// <summary>
    /// Replaceable producer for local device, AI possession, replay, or network commands.
    /// Implementations must not allocate from ReadCommand. MonoBehaviour sources that sample
    /// frame input in Update must execute before TickSchedulerService so the same frame consumes
    /// the sample; frame-latched implementations should also implement IBufferedPlayerCommandSource.
    /// </summary>
    public interface IPlayerCommandSource
    {
        bool IsInputAvailable { get; }

        PlayerCommand ReadCommand(float deltaTime);
    }

    /// <summary>
    /// Optional contract for frame-latched sources that must discard stale edges when
    /// ownership, simulation, or scheduler registration changes.
    /// </summary>
    public interface IBufferedPlayerCommandSource
    {
        void DiscardBufferedInput();
    }

    /// <summary>
    /// Deterministic consumer invoked by PlayerCommandFeature after input production.
    /// Lower order values run first. Move changes meaning with PlayerCommand.MoveSpace;
    /// consumers that interpret movement must branch on it or use GetWorldMoveDirection.
    /// </summary>
    public interface IPlayerCommandConsumer
    {
        int CommandOrder { get; }

        void ProcessPlayerCommand(PlayerCommand command, float deltaTime);

        void ResetPlayerCommandState();
    }
}
