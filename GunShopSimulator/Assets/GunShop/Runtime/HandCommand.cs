using UnityEngine;

namespace Rutin.GunShop
{
    public readonly struct HandCommand
    {
        public static HandCommand Neutral => new(Vector3.zero, Vector3.zero, false);

        public HandCommand(Vector3 translation, Vector3 rotation, bool gripHeld)
        {
            Translation = translation;
            Rotation = rotation;
            GripHeld = gripHeld;
        }

        public Vector3 Translation { get; }

        public Vector3 Rotation { get; }

        public bool GripHeld { get; }
    }
}
