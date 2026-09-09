using UnityEngine;

namespace GiantCity
{
    /// <summary>
    /// Reliable stomp hit detection. It tests, per foot, both halves of the two-part rule required by the
    /// spec: (1) the foot is in a downward-stomp motion (its world Y is decreasing for a sustained window),
    /// and (2) the tiny player's horizontal position is within the foot footprint. Confirmed hits are
    /// dispatched through a callback so the controller decides what invincibility / respawn to apply.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StompDetector : MonoBehaviour
    {
        [Header("Feet")]
        [Tooltip("Left foot bone; null to skip left detection.")]
        public Transform leftFoot;
        [Tooltip("Right foot bone; null to skip right detection.")]
        public Transform rightFoot;

        [Header("Rules")]
        [Tooltip("Horizontal radius (giantess world space) around a foot that counts as 'under the foot'.")]
        public float hitRadius = 3.5f;
        [Tooltip("Foot Y must be falling by at least this (m/s) to count as stomping down.")]
        public float descendVelocityThreshold = 1.5f;
        [Tooltip("Seconds the hit must persist before it fires, so the player can see it coming.")]
        public float confirmTime = 0.15f;

        private Transform _player;
        private System.Action _onHit;

        private float _lastLeftY;
        private float _lastRightY;
        private float _leftContactTimer;
        private float _rightContactTimer;
        private bool _initialized;

        /// <summary>Sets the feet and the player to test against, plus the hit callback.</summary>
        public void Configure(Transform leftFoot, Transform rightFoot, Transform player, System.Action onHit)
        {
            this.leftFoot = leftFoot;
            this.rightFoot = rightFoot;
            _player = player;
            _onHit = onHit;
            _lastLeftY = leftFoot != null ? leftFoot.position.y : 0f;
            _lastRightY = rightFoot != null ? rightFoot.position.y : 0f;
            _leftContactTimer = 0f;
            _rightContactTimer = 0f;
            _initialized = true;
        }

        private void LateUpdate()
        {
            if (!_initialized || _player == null)
            {
                return;
            }

            if (leftFoot != null)
            {
                TickFoot(leftFoot, ref _lastLeftY, ref _leftContactTimer, OnConfirmedHit);
            }
            if (rightFoot != null)
            {
                TickFoot(rightFoot, ref _lastRightY, ref _rightContactTimer, OnConfirmedHit);
            }
        }

        private void TickFoot(Transform foot, ref float lastY, ref float contactTimer, System.Action fire)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float currentY = foot.position.y;
            float velocityY = (currentY - lastY) / dt;
            lastY = currentY;

            bool descending = velocityY < -descendVelocityThreshold;
            bool underFoot = IsPlayerUnderFoot(foot);

            if (descending && underFoot)
            {
                contactTimer += dt;
                if (contactTimer >= confirmTime)
                {
                    contactTimer = 0f;
                    fire();
                }
            }
            else
            {
                contactTimer = 0f;
            }
        }

        private bool IsPlayerUnderFoot(Transform foot)
        {
            Vector3 footPos = foot.position;
            Vector3 playerPos = _player.position;

            // Only the horizontal footprint matters; the giantess is far taller than the player.
            float dx = playerPos.x - footPos.x;
            float dz = playerPos.z - footPos.z;
            return (dx * dx + dz * dz) <= hitRadius * hitRadius;
        }

        private void OnConfirmedHit()
        {
            _onHit?.Invoke();
        }
    }
}
