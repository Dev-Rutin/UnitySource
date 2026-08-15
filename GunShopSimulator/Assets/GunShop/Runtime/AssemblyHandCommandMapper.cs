using UnityEngine;

namespace Rutin.GunShop
{
    public static class AssemblyHandCommandMapper
    {
        public static HandCommand Map(Vector3 axes, bool rotationMode, bool gripHeld)
        {
            var normalizedAxes = Vector3.ClampMagnitude(axes, 1f);

            if (rotationMode)
            {
                var pitchYawRoll = new Vector3(
                    normalizedAxes.z,
                    normalizedAxes.x,
                    normalizedAxes.y);

                return new HandCommand(Vector3.zero, pitchYawRoll, gripHeld);
            }

            return new HandCommand(normalizedAxes, Vector3.zero, gripHeld);
        }
    }
}
