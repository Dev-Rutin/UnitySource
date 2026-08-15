using UnityEngine;

namespace Rutin.GunShop
{
    [DisallowMultipleComponent]
    public sealed class HandArmVisualizer : MonoBehaviour
    {
        [SerializeField] private Transform shoulder;
        [SerializeField] private Transform hand;
        [SerializeField, Min(0.001f)] private float radius = 0.07f;

        private void LateUpdate()
        {
            Refresh();
        }

        public void Configure(Transform newShoulder, Transform newHand, float newRadius = 0.07f)
        {
            shoulder = newShoulder;
            hand = newHand;
            radius = Mathf.Max(0.001f, newRadius);
            Refresh();
        }

        public void Refresh()
        {
            if (shoulder == null || hand == null)
            {
                return;
            }

            var direction = hand.position - shoulder.position;
            var length = direction.magnitude;
            if (length <= Mathf.Epsilon)
            {
                return;
            }

            transform.position = shoulder.position + direction * 0.5f;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            transform.localScale = new Vector3(radius, length * 0.5f, radius);
        }
    }
}
