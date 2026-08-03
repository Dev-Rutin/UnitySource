using Rutin.GameFramework.Core;
using Rutin.GameFramework.Player;
using UnityEngine;

namespace Rutin.GameFramework.Npc
{
    /// <summary>
    /// Optional visual-facing consumer for absolute NPC movement commands. Facing is derived
    /// from every world-space snapshot, so a later packet repairs orientation after packet loss.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCommandFeature))]
    public sealed class NpcFacingFeature :
        EntityFeature,
        IPlayerCommandConsumer
    {
        [SerializeField] private Transform yawRoot;

        private PlayerCommandFeature _commands;

        public int CommandOrder => -100;

        public Transform YawRoot => yawRoot != null ? yawRoot : transform;

        public void SetYawRoot(Transform value)
        {
            yawRoot = value;
        }

        public void ProcessPlayerCommand(PlayerCommand command, float deltaTime)
        {
            if (!IsFeatureActive ||
                command.MoveSpace != PlayerCommandMoveSpace.World ||
                command.Move.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 worldForward = new(command.Move.x, 0f, command.Move.y);
            YawRoot.rotation = Quaternion.LookRotation(worldForward, Vector3.up);
        }

        public void ResetPlayerCommandState()
        {
        }

        protected override void OnFeatureInitialized()
        {
            _commands = GetComponent<PlayerCommandFeature>();
            _commands.RegisterConsumer(this);
        }

        protected override void OnFeatureShutdown()
        {
            _commands?.UnregisterConsumer(this);
            _commands = null;
        }
    }
}
