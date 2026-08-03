using UnityEngine;

namespace Rutin.GameFramework.Player
{
    /// <summary>
    /// Immutable command payload suitable for local input, replay, or network transport.
    /// Look is an angular delta in degrees for this command. A positive
    /// SimulationDeltaTimeSeconds makes replay simulation independent of scheduler timing.
    /// Commands constructed without a duration use the dispatch tick delta for live input;
    /// an explicitly supplied zero-duration command advances no simulation time. Network
    /// serializers must transport both HasSimulationDeltaTime and SimulationDeltaTimeSeconds.
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
            bool hasSimulationDeltaTime)
        {
            Move = SanitizeMove(move);
            Look = SanitizeLook(look);
            JumpPressed = jumpPressed;
            Sequence = sequence;
            HasSimulationDeltaTime = hasSimulationDeltaTime;
            SimulationDeltaTimeSeconds = hasSimulationDeltaTime
                ? SanitizeSimulationDeltaTime(simulationDeltaTimeSeconds)
                : 0f;
        }

        public Vector2 Move { get; }

        public Vector2 Look { get; }

        public bool JumpPressed { get; }

        public uint Sequence { get; }

        public bool HasSimulationDeltaTime { get; }

        public float SimulationDeltaTimeSeconds { get; }

        public static PlayerCommand Neutral => default;

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

        private static float SanitizeSimulationDeltaTime(float seconds)
        {
            return Mathf.Max(0f, SanitizeFinite(seconds));
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
    /// Lower order values run first.
    /// </summary>
    public interface IPlayerCommandConsumer
    {
        int CommandOrder { get; }

        void ProcessPlayerCommand(PlayerCommand command, float deltaTime);

        void ResetPlayerCommandState();
    }
}
